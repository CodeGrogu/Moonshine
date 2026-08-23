# Moonshine Known Issues and Operational Trackers

This document tracks known runtime edge cases, concurrency hazards, and diagnostic logs across the Moonshine Windows 11 streaming platform, along with their reproduction conditions, triggering tests, root-cause analyses, and mitigation strategies.

---

## KI-001: Native `0xC0000005` Access Violation in Host Opus Audio Encoder Background Worker

- **Subsystem**: `Moonshine.Host` / `Moonshine.Native` (Audio Capture & Opus Compression)
- **Status**: Identified & Fortified (Tracking under Issue #27 / Issue #80)
- **Affected OS**: Windows 11 Pro x64 (Build 22000+)

### Triggering Test Cases
- `MoonshineHostCoordinator_LifecycleAsync_TransitionsAndCleansUp` in [`tests/Moonshine.Host.Tests/HostCoordinatorTests.cs`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/tests/Moonshine.Host.Tests/HostCoordinatorTests.cs)
- `HostAudioPipeline_ConcurrentProcessAndDispose_IsThreadSafeAndClean` and `HostAudioPipeline_StartAndStop_BackgroundWorkerLifecycle` in [`tests/Moonshine.Host.Tests/HostAudioPipelineTests.cs`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/tests/Moonshine.Host.Tests/HostAudioPipelineTests.cs)
- **Execution Condition**: Occurs non-deterministically during full assembly test sweeps (e.g. `dotnet test tests/Moonshine.Host.Tests -c Release` or `scripts/verify_codebase.ps1`) when tests run in batch sequence without inter-process isolation.

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
1. **Background Worker vs. Unmanaged Handle Lifetime**:
   `MoonshineHostAudioPipeline` runs a background thread `AudioProcessingLoop` at 5 ms intervals. When a host session or coordinator drops an audio pipeline instance without explicit synchronous disposal, the .NET Garbage Collector collects the managed pipeline wrapper.
2. **Asymmetric Finalization**:
   The child `OpusAudioEncoderPipeline` defines a finalizer `~OpusAudioEncoderPipeline()` that immediately invokes `MoonshineNativeMethods.OpusEncoderDestroy(_handle)`. If the parent `MoonshineHostAudioPipeline` worker thread has not fully joined, it attempts to pass the destroyed native pointer to `OpusEncoderEncodeFloat`, causing an unmanaged access violation when C++ attempts to acquire `std::recursive_mutex` on freed heap memory.
3. **Premature Synchronization Event Disposal**:
   Disposing `ManualResetEventSlim` primitives (`_workerExitedEvent`, `_drainCompletedEvent`) inside `TeardownResourcesLocked()` while worker loops or concurrent caller threads are in `finally` blocks calling `.Set()` leads to `ObjectDisposedException` on background threads.

### Resolution and Defensive Architecture
1. **Defensive Worker Teardown & Event Safety**:
   - `MoonshineHostAudioPipeline.Stop()` guarantees worker thread exit via bounded event wait and thread join (`worker.Join()`).
   - Synchronization primitives are kept alive across teardown sweeps to eliminate `ObjectDisposedException`.
   - `AudioProcessingLoop` catches `OperationCanceledException` and `ObjectDisposedException` during teardown unwinding.
2. **Native Handle Registry Hardening**:
   - Implement thread-safe global handle tables in `Moonshine.Native` (`SafeHandleRegistry<T>`) using `std::shared_mutex` so that native C-ABI exports safely reject stale or destroyed pointers with a zero return code rather than dereferencing invalid pointers.

---
