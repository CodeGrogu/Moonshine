# Moonshine Known Issues and Operational Trackers

This document tracks known runtime edge cases, concurrency hazards, and diagnostic logs across the Moonshine Windows 11 streaming platform, along with their reproduction conditions, triggering tests, root-cause analyses, and mitigation strategies.

---

## KI-001: Native `0xC0000005` Access Violation in Host Opus Audio Encoder Background Worker

- **Subsystem**: `Moonshine.Host` / `Moonshine.Native` (Audio Capture & Opus Compression)
- **Status**: **Resolved** (Issue #80)
- **Affected OS**: Windows 11 Pro x64 (Build 22000+)

### Triggering Test Cases
- `MoonshineHostCoordinator_LifecycleAsync_TransitionsAndCleansUp` in [`tests/Moonshine.Host.Tests/HostCoordinatorTests.cs`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/tests/Moonshine.Host.Tests/HostCoordinatorTests.cs)
- `HostAudioPipeline_ConcurrentProcessAndDispose_IsThreadSafeAndClean` and `HostAudioPipeline_StartAndStop_BackgroundWorkerLifecycle` in [`tests/Moonshine.Host.Tests/HostAudioPipelineTests.cs`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/tests/Moonshine.Host.Tests/HostAudioPipelineTests.cs)
- **Execution Condition**: Previously occurred non-deterministically during full assembly test sweeps when tests ran in batch sequence without inter-process isolation. No longer reproducible after fix.

### Symptoms and Stack Trace
```text
Fatal error. 0xC0000005
   at Moonshine.Interop.MoonshineNativeMethods.<OpusEncoderEncodeFloat>g____PInvoke|50_0(IntPtr, Single*, UInt32, Byte*, UInt32, UInt32*)
   at Moonshine.Interop.MoonshineNativeMethods.OpusEncoderEncodeFloat(IntPtr, Single*, UInt32, Byte*, UInt32, UInt32 ByRef)
   at Moonshine.Host.Audio.OpusAudioEncoderPipeline.TryEncode(System.ReadOnlySpan`1<Single>, UInt32, System.Span`1<Byte>, Int32 ByRef)
   at Moonshine.Host.Audio.MoonshineHostAudioPipeline.ExecutePcmFrameStepLocked(System.ReadOnlySpan`1<Single>, Moonshine.Core.Media.AudioPacketSink, Boolean)
   at Moonshine.Host.Audio.MoonshineHostAudioPipeline.ExecuteAudioFrameStep(Moonshine.Core.Media.AudioPacketSink, Boolean)
   at Moonshine.Host.Audio.MoonshineHostAudioPipeline.AudioProcessingLoop()
```

### Technical Root Cause
1. **TOCTOU Race in Native Handle Registry (Primary)**:
   The original `SafeHandleRegistry<T>` validated a pointer via `is_valid()` under a shared lock, then released the lock before the operation used the pointer. A concurrent `destroy` call could unregister and `delete` the handle between the validity check and the actual encode call, leaving the encoder with a dangling pointer.
2. **Asymmetric Finalisation (Secondary)**:
   The child `OpusAudioEncoderPipeline` defined a finaliser `~OpusAudioEncoderPipeline()` that invoked `MoonshineNativeMethods.OpusEncoderDestroy(_handle)` from the GC thread. If the parent `MoonshineHostAudioPipeline` worker thread had not fully joined, it attempted to pass the destroyed native pointer to `OpusEncoderEncodeFloat`.
3. **GC-Thread Thread.Join() Deadlock Risk**:
   `MoonshineHostAudioPipeline` defined a finaliser that called `Dispose()` which called `Stop()` which called `worker.Join()`. Calling `Thread.Join()` from the GC finaliser thread risks deadlock when the worker thread is waiting on a managed lock.

### Resolution
1. **`SafeHandleStore<T>` with `shared_ptr` Reference Counting** (Native C++):
   - Replaced `SafeHandleRegistry<T>` (TOCTOU-vulnerable `is_valid()` + `unordered_set`) with `SafeHandleStore<T>` (`acquire()` + `unordered_map<T*, shared_ptr<T>>`).
   - `acquire()` returns a `shared_ptr` copy that keeps the handle alive for the duration of the caller's operation. `release()` removes the entry from the map, but the actual `delete` is deferred until the last `shared_ptr` guard goes out of scope.
   - Applied to all encoder, decoder, and capture API exports.
2. **Finaliser Removal** (Managed C#):
   - Removed `~OpusAudioEncoderPipeline()` and `~MoonshineHostAudioPipeline()` finalisers. All production paths call `Dispose()` explicitly. The native `SafeHandleStore` now safely defers deallocation, eliminating the need for a GC-thread backstop.
3. **Verification**: 5 consecutive full test sweeps (100 Host + 81 Interop per sweep) with zero crashes, zero aborts.

---
