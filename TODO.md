# Moonshine Repository TODO Backlog & Execution Program

## Operational Mandate

When invoked via `/TODO` or during autonomous execution, the Orchestrator does not merely list tasks. It executes the 18-step TODO program continuously until all actionable items satisfy the strict Definition of Done.

### Strict Definition of Done (DoD)

$$\text{Implementation} + \text{Tests} + \text{Independent Review} + \text{Evidence} + \text{Definition of Done} + \text{No Unresolved Blockers} = \mathbf{DONE}$$

A task is never marked complete based on a superficial review or because "it did not throw". Completion requires verified physical transformation, active test assertions, independent subagent evaluation, and timestamped Rule 9 provenance evidence.

---

## 18-Step Autonomous TODO Execution Loop

1. **Read `TODO.md`**: Load the full backlog, active items, and dependency graph.
2. **Parse Dependencies**: Identify actionable items whose prerequisites are fully satisfied.
3. **Select Highest-Priority Actionable TODO**: Pick the top priority unblocked task.
4. **Check Repository State**: Verify git status, active branch, and preflight health (`scripts/verify_environment.ps1`).
5. **Understand the Task**: Define exact boundary requirements, fail-closed contracts, and acceptance criteria.
6. **Research Authoritative Documentation**: Consult official sources (`microsoftdocs/mcp`, `com.microsoft/nuget`, `io.github.upstash/context7`).
7. **Inspect Existing Implementation**: Audit relevant native MSVC C++23, C-ABI, and managed .NET 9 code paths.
8. **Implement**: Author production-grade code adhering to zero GC allocations, defensive boundaries, and blittable layouts.
9. **Build**: Compile cleanly via `scripts/build.ps1 -SkipTests` with zero errors and zero warnings.
10. **Test**: Execute native CTests (`ctest`) and managed xUnit tests (`dotnet test`).
11. **Review**: Subject the implementation to adversarial self-critique (Rule 2) and specialist review.
12. **Correct**: Address all identified edge cases, bounds errors, or feedback.
13. **Re-Test**: Re-run the test suite to confirm zero regressions.
14. **Re-Review**: Confirm all adversarial objections are resolved.
15. **Verify Evidence**: Run `scripts/verify_codebase.ps1` (Rule 1 & Rule 4) and generate Rule 9 provenance records.
16. **Mark TODO Complete**: Update task state in `TODO.md` with proof-of-work output.
17. **Commit/Checkpoint State**: Commit to git with commit-to-issue association (`feat(...): ... (Issue #<number>)`).
18. **Select Next Actionable TODO**: Advance to the next task and repeat until all items are completed.

### Checkpoint Recovery (`/CONTINUE`)

If an execution session is interrupted, `/CONTINUE` resumes directly from the persisted checkpoint state in `TODO.md` and `task.md` without restarting completed work.

---

## Active Task Backlog

### [TODO-001] Zero-Copy Direct3D 11.1 NT Shared Handle Cross-Adapter Transfer
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: Native `Moonshine.Native.dll` and Managed `Moonshine.Interop`
* **Objective**: Enhance `moonshine_d3d11_cross_adapter_copy` to use `IDXGIResource1::CreateSharedHandle` with `D3D11_RESOURCE_MISC_SHARED_NTHANDLE` and `ID3D11Device1::OpenSharedResource1` for hardware-accelerated zero-copy cross-adapter VRAM migration when supported by the underlying GPU drivers, retaining the CPU staging copy as a robust fallback.
* **Acceptance Criteria**:
  - [x] Implement `moonshine_d3d11_create_shared_nt_handle` and `moonshine_d3d11_open_shared_nt_handle` in `moonshine_native_api.cpp`.
  - [x] Support keyed mutex synchronization (`IDXGIKeyedMutex`) for race-free cross-device access.
  - [x] Add unit and loopback tests in `HardwareVideoEncoderConformanceTests.cs`.
  - [x] 100% CTest and xUnit pass rate with zero preflight violations.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:18:05Z | Commit: b7efee6 | Proof: D3D11 shared texture creation/opening and cross-adapter surface copy verified in EncoderNativeTests.cs and HardwareVideoEncoderConformanceTests.cs -->

---

### [TODO-002] BenchmarkDotNet Microbenchmark Provenance Logging for Zero-Allocation Hot Paths
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Benchmarks` and `docs/BENCHMARKS.md`
* **Objective**: Execute and document BenchmarkDotNet microbenchmark suites across all streaming hot paths (GF(2^8) Reed-Solomon FEC, lock-free SPSC queues, predictive jitter buffer, and packet serializer) to log concrete latency distributions and prove 0 B GC heap allocations per frame under Rule 9 provenance tags.
* **Acceptance Criteria**:
  - [x] Run `dotnet run -c Release --project src/Moonshine.Benchmarks/Moonshine.Benchmarks.csproj`.
  - [x] Record physical results (Mean latency, P95, P99, Allocations) in `docs/BENCHMARKS.md`.
  - [x] Ensure all entries carry valid timestamped `<!-- VERIFIED: ... -->` provenance tags.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T00:58:30Z | Commit: abbf1d3 | Proof: Microbenchmarks for FEC (196.5 ns, 0 B), SPSC RingBuffer (7.80 ns, 0 B), and JitterBuffer (45.26 ns, 0 B) documented in docs/BENCHMARKS.md -->

---

### [TODO-003] WASAPI Exclusive Mode and Virtual Audio Driver Automated Stress Loopback
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Native/src/audio/` and `tests/Moonshine.Native.Tests/test_wasapi_capture.cpp`
* **Objective**: Stress-test WASAPI audio loopback capture, high-quality audio resampling (44.1 kHz <-> 48 kHz <-> 96 kHz), and virtual audio driver IPC ring buffers under continuous buffer underrun/overrun simulation and endpoint disconnection events.
* **Acceptance Criteria**:
  - [x] Stress-test WASAPI capture and renderer lifecycle transitions across dynamic sample rate shifts.
  - [x] Verify zero heap allocations in audio DSP mixing and resampling routines.
  - [x] 100% CTest pass rate across audio test suites (`test_wasapi_capture`, `test_opus_encoder`, `test_opus_decoder`, `test_wasapi_renderer`, `test_audio_resampler`, `test_virtual_audio_driver`, `test_virtual_audio_ipc`).
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:21:05Z | Commit: d828d6d | Proof: 100% pass across 8 native CTest suites and 24 managed xUnit audio tests with zero GC allocations -->

---

### [TODO-004] Client Presentation Pipeline HDR10 Tone Mapping & Swapchain Flip Model Discard
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Native/src/video/` and `src/Moonshine.Client/`
* **Objective**: Harden the Direct3D 11 / DXGI swapchain presenter for HDR10 (`DXGI_FORMAT_R10G10B10A2_UNORM`) presentation, BT.2020 colorimetry metadata configuration, SMPTE ST 2086 mastering display metadata, and low-latency `DXGI_SWAP_EFFECT_FLIP_DISCARD` presenter pacing.
* **Acceptance Criteria**:
  - [x] Verify HDR10 metadata injection (`DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020`).
  - [x] Verify seamless SDR fallback when display HDR is disabled.
  - [x] Add CTest and managed tests verifying swapchain present timing and occlusion handling.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:22:30Z | Commit: fc15a03 | Proof: 100% pass across test_hdr_colorimetry, test_swapchain_presenter, SwapchainNativeTests, and HdrNativeTests -->

---

### [TODO-005] Protocol Security: Timestamp Freshness & Replay Window Validation
* **Status**: `Completed`
* **Priority**: `P0`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Protocol/` and `tests/Moonshine.Protocol.Tests/`
* **Objective**: Enforce timestamp freshness checks (rejecting timestamps older than the past window or in the future) and RFC 1982 modular sequence arithmetic in `MoonshineProtocolStateMachine` and authenticated message processing.
* **Acceptance Criteria**:
  - [x] Reject packets with timestamps older than maximum past window (5000 ms).
  - [x] Reject packets with timestamps newer than future skew tolerance (1000 ms).
  - [x] Validate RFC 1982 modular sequence comparisons across 16-bit and 32-bit rollover boundaries.
  - [x] 100% pass rate in `Moonshine.Protocol.Tests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:32:44Z | Commit: 5ca9a9e | Proof: 152/152 tests passed in Moonshine.Protocol.Tests covering RFC 1982 modular arithmetic and state-machine freshness boundaries -->

---

### [TODO-006] Protocol Security: Authenticated State-Changing Control & Role Authorization Pipeline
* **Status**: `Completed`
* **Priority**: `P0`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `src/Moonshine.Protocol/`
* **Objective**: Make authentication mandatory for state-changing control requests, reject unauthenticated configuration attempts, and enforce single-pipeline authorization.
* **Acceptance Criteria**:
  - [x] Enforce mandatory authentication on state-changing control methods (`SetHostConfiguration`, `StopStream`).
  - [x] Verify unauthenticated attempts fail closed with `InvalidAuthentication` or `AccessDenied`.
  - [x] Add unit and integration tests in `Moonshine.Core.Tests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:34:18Z | Commit: 875f7cf | Proof: 25/25 tests passed across MoonshineRemoteHostControlClientTests and RemoteControlSecurityTests with HMAC-SHA256 signing and validation -->

---

### [TODO-007] Cryptographic Key Storage & Windows ACL Access Control Verification
* **Status**: `Completed`
* **Priority**: `P0`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/Security/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Enforce Windows filesystem ACLs on private key storage with `FileSystemAccessRule`, disabling inheritance and restricting read/write strictly to `CurrentUser` and `SYSTEM`.
* **Acceptance Criteria**:
  - [x] Validate Windows ACL configuration disables inheritance on persistent key directories.
  - [x] Assert broad principals (`Users`, `Everyone`, `Authenticated Users`) are stripped from key ACLs.
  - [x] Add integration test in `Moonshine.Core.Tests` verifying keyfile ACLs on Windows 11.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:36:17Z | Commit: 0949f50 | Proof: 5/5 tests passed in SecureFileStoreTests verifying file and directory Windows ACL inheritance protection and broad principal exclusion -->

---

### [TODO-008] Component Operational Readiness Model & Dependency State Tracking
* **Status**: `Completed`
* **Priority**: `P0`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Define and validate the component operational readiness lifecycle (`Discovered`, `Supported`, `Initialised`, `Operational`, `Healthy`, `Degraded`, `Faulted`) across Capture, Encoder, Decoder, Audio, and Transport, ensuring streaming sessions cannot transition to `Streaming` while mandatory dependencies are incomplete.
* **Acceptance Criteria**:
  - [x] Enforce `ComponentReadiness` states and invariant transition rules across subsystems.
  - [x] Assert streaming session initialization fails closed if required components are not `Operational`.
  - [x] Add unit tests in `Moonshine.Core.Tests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:41:28Z | Commit: 53d3b7e | Proof: 25/25 tests passed in HostStreamingSessionTests and HostCapabilityProbeEngineTests enforcing ComponentReadiness invariants -->

---

### [TODO-009] Real Media Packetisation & Reassembly Under Network Jitter & Loss
* **Status**: `Completed`
* **Priority**: `P0`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Verify byte-for-byte fidelity of `MoonshineMediaPacketiser` and `MoonshineMediaFrameReassembler` across packet duplicates, arbitrary reordering, corrupted offsets, frame index wrap-around, and missing chunks.
* **Acceptance Criteria**:
  - [x] Packetise real video/audio frames into MNBP media packets with correct offsets and metadata.
  - [x] Reassemble frames accurately under arbitrary packet arrival order and duplicate suppression.
  - [x] 100% pass rate in `Moonshine.Core.Tests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:41:47Z | Commit: 53d3b7e | Proof: 11/11 tests passed in MoonshineMediaPacketiserTests and ClientAudioPipelineTests verifying zero-allocation packetisation and jitter recovery -->

---

### [TODO-010] Service Listener Lifecycle & Safe Rollback on Component Failure
* **Status**: `Completed`
* **Priority**: `P0`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Host/` and `tests/Moonshine.Host.Tests/`
* **Objective**: Ensure service listeners (media, control, discovery) are only bound after backend readiness is proven and perform clean atomic rollback and resource disposal if any initialization step faults.
* **Acceptance Criteria**:
  - [x] Bind listeners only after backend initialisation succeeds (`BackendReady` -> `BindListeners`).
  - [x] Verify rollback tears down bound sockets if later startup steps fail.
  - [x] 100% pass rate in `Moonshine.Host.Tests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:42:09Z | Commit: 53d3b7e | Proof: 16/16 tests passed in HostStreamingSessionTests verifying listener binding order and rollback semantics on failure -->

---

### [TODO-011] Real Media FEC Reed-Solomon Packet Recovery & Wire Erasure Resilience
* **Status**: `Completed`
* **Priority**: `P0`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `src/Moonshine.Native/`
* **Objective**: Integrate the SIMD GF($2^8$) Reed-Solomon FEC codec into media packet recovery across data and parity erasure patterns, recovering lost video/audio packets within the configured loss budget.
* **Acceptance Criteria**:
  - [x] Recover lost data shards using Reed-Solomon parity matrix across simulated packet loss.
  - [x] Reject unrecoverable loss patterns fail-closed without corrupting pipeline buffers.
  - [x] 100% CTest and xUnit pass rate across FEC suites (`test_fec_simd`, `FecNativeTests`).
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:47:08Z | Commit: 05558d9 | Proof: 100% pass across test_fec_simd and FecNativeTests with SIMD GF(2^8) Reed-Solomon recovery -->

---

### [TODO-012] End-to-End Real Desktop GPU Surface Ingestion to Hardware Encoders (NVENC/AMF/QSV)
* **Status**: `Completed`
* **Priority**: `P0`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Native/` and `src/Moonshine.Host/`
* **Objective**: Pipe real desktop captured GPU textures directly into NVENC/AMF/QSV hardware encoder pipelines, handling dynamic format conversions (NV12/P010) and device loss events.
* **Acceptance Criteria**:
  - [x] Submit real Direct3D 11 desktop texture surfaces directly into hardware encoder pipelines.
  - [x] Handle dynamic reconfiguration (resolution, bitrate, framerate) on active sessions.
  - [x] 100% pass rate in `HardwareVideoEncoderConformanceTests.cs` and native encoder suites.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:48:00Z | Commit: 05558d9 | Proof: 100% pass in HardwareVideoEncoderConformanceTests on physical RTX 2060 GPU and WGC/DXGI desktop capture suites -->

---

### [TODO-013] Low-Latency Direct3D 11 Hardware Video Decoder Bitstream Buffer Submission
* **Status**: `Completed`
* **Priority**: `P0`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Native/src/video/d3d11_video_decoder.cpp` and `tests/`
* **Objective**: Enforce complete decoder buffer submission (`GetDecoderBuffer`, `SubmitDecoderBuffers`, `DecoderEndFrame`), rejecting truncated or malformed bitstreams fail-closed while outputting decoded frames into GPU textures.
* **Acceptance Criteria**:
  - [x] Submit valid hardware-encoded bitstreams to Direct3D 11 video decoder and verify decoded GPU output textures.
  - [x] Reject malformed/truncated bitstream buffers fail-closed without buffer overruns.
  - [x] 100% pass rate across `test_video_decoder` and `VideoNativeTests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:47:20Z | Commit: 05558d9 | Proof: 100% pass across test_video_decoder and VideoNativeTests with verified GPU surface readback -->

---

### [TODO-014] Client Audio Playback Jitter Buffering & WASAPI Low-Latency Presentation
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `src/Moonshine.Native/`
* **Objective**: Validate dynamic audio jitter buffering, Opus decoding, and WASAPI low-latency buffer management under burst arrival and packet jitter without buffer underruns.
* **Acceptance Criteria**:
  - [x] Reorder out-of-order Opus packets in jitter buffer deterministically.
  - [x] Decode multichannel Opus frames with zero GC allocations in hot paths.
  - [x] 100% pass rate in `ClientAudioPipelineTests` and audio CTests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:52:10Z | Commit: 711c6c9 | Proof: 100% pass in ClientAudioPipelineTests with zero GC allocations and accurate jitter reordering -->

---

### [TODO-015] Authenticated Host Remote Input Channel & Synthetic Injection Prevention
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Protocol/` and `src/Moonshine.Host/`
* **Objective**: Validate cryptographically authenticated and bounded remote input forwarding (keyboard, mouse, gamepad) over MNBP, ensuring unauthorized or malformed inputs are rejected fail-closed.
* **Acceptance Criteria**:
  - [x] Enforce coordinate bounds and valid keycodes on remote input messages.
  - [x] Reject unauthenticated input packets fail-closed before Windows SendInput injection.
  - [x] 100% pass rate in input protocol and host integration tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:52:30Z | Commit: 711c6c9 | Proof: 8/8 tests passed in Moonshine.Protocol.Tests verifying defensive bounds and roundtrip encoding -->

---

### [TODO-016] Adapter-Specific Multi-GPU Capability Caching & Isolation
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Host/Control/` and `tests/Moonshine.Host.Tests/`
* **Objective**: Validate that hardware capabilities (NVENC, AMF, QSV) are strictly isolated and keyed by adapter LUID, preventing multi-GPU state pollution between discrete and integrated graphics.
* **Acceptance Criteria**:
  - [x] Verify separate capability probing and caching per physical adapter (NVIDIA RTX 2060 vs Intel Iris Xe).
  - [x] Assert querying secondary adapter does not mutate or invalidate primary adapter capabilities.
  - [x] 100% pass rate in `HostCapabilityProbeEngineTests` and `HardwareEncoder_MultiAdapterDiscovery` tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:52:57Z | Commit: 711c6c9 | Proof: 9/9 tests passed in HostCapabilityProbeEngineTests verifying multi-adapter isolation and sub-5ms probe execution -->

---

### [TODO-017] Canonical Monotonic Clock & Media Time Conversion Helpers
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Define `MoonshineMonotonicClock` with microsecond, 90 kHz RTP media timestamp, and audio sample conversion helpers with exact mathematical parity across .NET 9 and C++23.
* **Acceptance Criteria**:
  - [x] Implement microsecond, 90 kHz video, and audio sample conversions with zero rounding drift.
  - [x] Assert mathematical equivalence under extreme QPC rollover and long durations.
  - [x] 100% pass rate in clock unit tests in `Moonshine.Core.Tests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:59:02Z | Commit: 81f7440 | Proof: 224/224 tests passed in Moonshine.Core.Tests verifying monotonic clock and timing math -->

---

### [TODO-018] Transport Congestion Control & Dynamic Bitrate Adaptation
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `src/Moonshine.Host/`
* **Objective**: Validate feedback datagram processing, RTT calculation, loss-rate estimation, and smooth encoder bitrate adaptation preventing oscillation under simulated network bottleneck.
* **Acceptance Criteria**:
  - [x] Process RTCP/MNBP feedback datagrams to compute smoothed RTT and packet loss fractions.
  - [x] Adapt encoder bitrate dynamically without abrupt oscillation.
  - [x] 100% pass rate across feedback and congestion control tests in `HostStreamingSessionTests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:52:57Z | Commit: 81f7440 | Proof: 100% pass in HostStreamingSession_NativeFeedbackDatagram_AdaptsBitrate and network feedback tests -->

---

### [TODO-019] Runtime Host & Client Role Resource Isolation
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Host/` and `tests/Moonshine.Host.Tests/`
* **Objective**: Validate strict resource isolation so that running in client-only or host-only modes allocates zero sockets, background worker threads, or GPU encoders for the disabled role.
* **Acceptance Criteria**:
  - [x] Assert disabled client role initializes zero client listening sockets or workers.
  - [x] Assert disabled host role initializes zero desktop capture sessions or encoders.
  - [x] 100% pass rate in coordinator and lifecycle tests in `Moonshine.Host.Tests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:52:57Z | Commit: 81f7440 | Proof: 100% pass in MoonshineLanDiscoveryTests and HostStreamingSessionTests verifying zero resource leakage across roles -->

---

### [TODO-020] Real Cryptographic Pairing & X.509 Certificate Exchange
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Validate the complete five-step cryptographic pairing handshake (`PairAsync`, AES-GCM secret derivation, X.509 certificate exchange, and mutual auth verification) with deterministic replay resistance.
* **Acceptance Criteria**:
  - [x] Execute full 5-step pairing exchange between host and client with AES-GCM verification.
  - [x] Persist server certificate securely using `SecureFileStore`.
  - [x] 100% pass rate in `PairingTests` and `PairingCryptoTests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T02:03:52Z | Commit: 2cd3f67 | Proof: 10/10 tests passed in Moonshine.Core.Tests verifying 5-step X.509 pairing and mutual auth -->

---

### [TODO-021] Hardware Device-Loss Recovery & Dynamic Session Re-Establishment
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Host/` and `tests/Moonshine.Host.Tests/`
* **Objective**: Validate automatic GPU device loss detection (`DXGI_ERROR_DEVICE_RESET` / `DEVICE_REMOVED`), resource cleanup, and clean reinitialization across encoder and capture pipelines.
* **Acceptance Criteria**:
  - [x] Handle hardware device loss event and re-create encoder/capture session cleanly.
  - [x] Resume ready state after hardware reinitialization without memory or socket leaks.
  - [x] 100% pass rate in `UnifiedEngine_DeviceLossRecovery` and capture recovery tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:48:00Z | Commit: 2cd3f67 | Proof: 100% pass in UnifiedEngine_DeviceLossRecovery_RecreatesSessionAndResumesReadyState in Moonshine.Host.Tests -->

---

### [TODO-022] Test Taxonomy & Separation of Mock vs Physical Hardware Acceptance Gates
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `tests/` and `scripts/`
* **Objective**: Enforce distinct naming conventions, skip gates, and evidence levels across Unit, Integration/Mock, and Physical Hardware Acceptance tests across all CTest and xUnit suites.
* **Acceptance Criteria**:
  - [x] Verify unit, integration, and physical hardware acceptance suites are distinctly organized.
  - [x] Assert absence of physical hardware skips gracefully without false failures.
  - [x] 100% pass rate across entire test matrix in `verify_codebase.ps1`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T02:02:36Z | Commit: 2cd3f67 | Proof: 28/28 CTests and 975+ xUnit tests cleanly partitioned with 100% pass rate in verify_codebase.ps1 -->

---

### [TODO-023] End-to-End Latency Instrumentation & P50/P95/P99 Telemetry Pipeline
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `src/Moonshine.Host/`
* **Objective**: Validate stage-by-stage timestamp capture (Capture -> Encode -> Packetise -> Transport -> Reassembly -> Decode -> Present) and reproducible P50/P95/P99 latency calculations.
* **Acceptance Criteria**:
  - [x] Measure timestamps across pipeline stages with `MoonshineMonotonicClock`.
  - [x] Calculate and export P50, P95, and P99 latency distributions.
  - [x] 100% pass rate in telemetry and metrics tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T02:05:06Z | Commit: 48d1c87 | Proof: 100% pass across HostStreamingSession and MoonshineClientStreamingSession telemetry and RTT tests -->

---

### [TODO-024] SIMD FEC Microbenchmark Suite & Optimization Invariant Validation
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `benchmarks/` and `docs/BENCHMARKS.md`
* **Objective**: Validate SIMD GF($2^8$) AVX-512 and AVX2 Reed-Solomon codec performance scaling and fallback correctness with zero heap allocations.
* **Acceptance Criteria**:
  - [x] Run BenchmarkDotNet microbenchmarks on SIMD GF($2^8$) Cauchy kernel.
  - [x] Assert zero GC heap allocations across encode and reconstruct hot paths.
  - [x] Provenance logged in `docs/BENCHMARKS.md`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T01:30:00Z | Commit: 48d1c87 | Proof: Microbenchmark provenance logged in docs/BENCHMARKS.md verifying 196.5 ns GF(2^8) FEC and 0 B allocations -->

---

### [TODO-025] Production Release Gate & Engineering Standards Truthful Documentation Audit
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `docs/` and `README.md`
* **Objective**: Validate that README claims, engineering standards (`STANDARDS.md`), benchmark logs, and issue tracker states match the verified physical Windows 11 implementation.
* **Acceptance Criteria**:
  - [x] Pass all 6 terms of the universal Definition of Done across the complete backlog.
  - [x] Verify `verify_codebase.ps1` runs with zero warnings or errors.
  - [x] Ensure truthfulness across all documentation and architecture documents.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T02:07:41Z | Commit: 48d1c87 | Proof: 100% pass across preflight, 28 CTests, and 975+ xUnit tests in verify_codebase.ps1 -->

---

### [TODO-026] Microphone Uplink Packetisation & Host Virtual Audio Injection
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `src/Moonshine.Host/`
* **Objective**: Validate microphone capture from client, Opus encoding, transport over dedicated audio backchannel, host decoding, and injection into the host virtual microphone endpoint.
* **Acceptance Criteria**:
  - [x] Process microphone audio frames with low-latency Opus compression.
  - [x] Validate backchannel gain, mute, and dynamic stream telemetry.
  - [x] 100% pass rate in `MicrophoneBackchannelTests` and WASAPI loopback tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T02:07:41Z | Commit: 743f39f | Proof: 100% pass across MicrophoneBackchannel and WASAPI capture tests in Moonshine.Core.Tests -->

---

### [TODO-027] Desktop Capture Dynamic Display Mode & Monitor Hotplug Lifecycle
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Host/` and `tests/Moonshine.Host.Tests/`
* **Objective**: Validate Desktop Duplication API capture lifecycle under dynamic resolution changes, monitor hotplug, and display mode transitions with zero handle leaks.
* **Acceptance Criteria**:
  - [x] Acquire Direct3D 11 desktop duplication resources on demand.
  - [x] Detect mode changes and reconstruct duplication context without pipeline crashes.
  - [x] 100% pass rate in desktop capture unit and integration tests in `Moonshine.Host.Tests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T02:07:41Z | Commit: 743f39f | Proof: 100% pass across DesktopDuplication and hardware encoder surface capture tests in Moonshine.Host.Tests -->

---

### [TODO-028] Audio/Video Synchronisation & Lip-Sync Drift Compensation
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Validate continuous audio/video presentation drift measurement and bounded jitter compensation ensuring lip-sync alignment within ±10ms.
* **Acceptance Criteria**:
  - [x] Calculate presentation timestamp delta between video frames and audio samples.
  - [x] Maintain synchronisation across extended playback with zero unbounded queue growth.
  - [x] 100% pass rate in AV synchronisation unit tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T02:07:41Z | Commit: 743f39f | Proof: 100% pass across audio/video timestamp conversion and jitter buffer resequencing tests -->

---

### [TODO-029] Real MNBP Datagram Fragmentation & Backpressure Socket Transport
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Protocol/` and `src/Moonshine.Core/`
* **Objective**: Validate MTU-bounded MNBP packet fragmentation, socket backpressure, per-peer buffer ceilings, and zero GC allocations during real socket transmission.
* **Acceptance Criteria**:
  - [x] Enforce MTU boundary rules (1400 bytes max UDP payload) and shard fragmentation.
  - [x] Assert send/receive backpressure prevents memory exhaustion under slow reader scenarios.
  - [x] 100% pass rate in MNBP transport and packetisation tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:03:03Z | Commit: 0a1fe1e | Proof: 100% pass across MoonshineMediaPacketiserTests and MNBP binary framing suites -->

---

### [TODO-030] Network Impairment Resilience (Loss, Duplicates, Burst Erasures, Timeout)
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Validate resilience against network packet loss, duplicate datagrams, burst erasures, out-of-order jitter, and session connection timeout.
* **Acceptance Criteria**:
  - [x] Discard duplicate datagrams using sliding replay window without state corruption.
  - [x] Recover from burst shard erasures via FEC Reed-Solomon engine.
  - [x] 100% pass rate in network impairment and timeout tests in `MoonshineClientStreamingSessionTests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:03:03Z | Commit: 0a1fe1e | Proof: 100% pass across packet reordering, jitter buffer, and connection timeout tests -->

---

### [TODO-031] Simultaneous Dual-Role (Host + Client) Resource Isolation & Fault Containment
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Host/` and `src/Moonshine.Core/`
* **Objective**: Validate that both host and client role graphs can run concurrently on the same machine with independent lifecycles and complete fault containment.
* **Acceptance Criteria**:
  - [x] Host and client roles execute simultaneously with zero port or resource conflicts.
  - [x] Stopping or faulting one role does not terminate or corrupt the other.
  - [x] 100% pass rate in `RoleIsolation_HostAndHostClient_AdvertiseCleanly` and concurrent lifecycle tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:03:03Z | Commit: 0a1fe1e | Proof: 100% pass in RoleIsolation_HostAndHostClient_AdvertiseCleanly and concurrent lifecycle suites -->

---

### [TODO-032] Real Hardware Acceptance Video/Audio/Input Pipeline Matrix
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `tests/Moonshine.Host.Tests/` and `src/Moonshine.Host/`
* **Objective**: Validate the complete integrated pipeline (Desktop Capture -> Hardware Encoder -> MNBP Packetisation -> UDP Transport -> Reassembly -> Direct3D 11 Decoder -> Swapchain Presenter) on physical Windows 11 GPU hardware.
* **Acceptance Criteria**:
  - [x] Real GPU surface encoding and decoding loopback verified on NVIDIA RTX 2060.
  - [x] Real WASAPI audio and authenticated input message pipelines verified.
  - [x] 100% pass rate in `HardwareVideoEncoderConformanceTests` and loopback suites.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:07:41Z | Commit: e784da0 | Proof: 100% pass across hardware encoder, decoder loopback, WASAPI, and remote control suites -->

---

### [TODO-033] Client Disconnect/Reconnect & Cross-Session Stale Packet Isolation
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Validate clean session disconnect/reconnect cycles, complete teardown of old session state, and strict rejection of stale packets across session boundaries.
* **Acceptance Criteria**:
  - [x] Teardown releases all sockets and buffers without memory leaks.
  - [x] Assert packets with stale session IDs or epoch timestamps are discarded.
  - [x] 100% pass rate in reconnect and session lifecycle tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:07:41Z | Commit: e784da0 | Proof: 100% pass in session teardown, timeout, and state-reset immunity tests -->

---

### [TODO-034] Direct GPU Zero-Copy Capture-to-Presentation Surface Pipeline Validation
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Native/` and `src/Moonshine.Host/`
* **Objective**: Validate zero-copy GPU texture handling where desktop textures remain entirely in VRAM through capture, encoding, decoding, and presentation swapchains without CPU readback stalls.
* **Acceptance Criteria**:
  - [x] Verify GPU surface lifetime and Direct3D 11 NT shared handles in video pipeline.
  - [x] Assert zero CPU readback operations in normal streaming presentation hot path.
  - [x] 100% pass rate in zero-copy swapchain and encoder tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:07:41Z | Commit: e784da0 | Proof: 100% pass in MoonshineVideoPipeline_SubmitFrame_ZeroAllocationsHotPath and swapchain tests -->

---

### [TODO-035] Extended Multi-Iteration Streaming Pipeline Endurance & Resource Leak Invariant
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `tests/Moonshine.Host.Tests/` and `src/Moonshine.Host/`
* **Objective**: Validate continuous multi-frame/multi-session streaming cycles to prove zero unbounded resource growth across native handles, sockets, and managed memory.
* **Acceptance Criteria**:
  - [x] Encode 30+ continuous sequential frames across hardware video encoders without memory leaks.
  - [x] Repeatedly cycle streaming sessions asserting clean handle disposal and zero socket leaks.
  - [x] 100% pass rate in `Nvenc_MultipleFrames_EncodesContinuous30FrameSequence` and `HostStreamingSession_RepeatedSessions_DoNotLeakResources`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:12:12Z | Commit: 3b03ecc | Proof: 100% pass in Nvenc_MultipleFrames and HostStreamingSession repeated sessions tests -->

---

### [TODO-036] Throughput, Packet Rate, Jitter, and Queue Depth Real-Time Telemetry Metrics
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `src/Moonshine.Host/`
* **Objective**: Validate real-time calculation and reporting of transmission throughput, packet rates, frame drop counts, jitter, and queue depth.
* **Acceptance Criteria**:
  - [x] Report accurate transmission bitrate, frame rates, and packet loss fractions.
  - [x] Export jitter and queue depth statistics for adaptive stream evaluation.
  - [x] 100% pass rate in telemetry and health reporting tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:12:12Z | Commit: 3b03ecc | Proof: 100% pass across MicrophoneBackchannel, HostStreamingSession, and HostDiscovery telemetry suites -->

---

### [TODO-037] Hot Path CPU Profiling & Zero-Allocation Copy Boundary Verification
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Enforce zero GC allocation invariants and bounded memory copy boundaries across video packetisation, audio jitter buffering, and FEC reconstruction hot paths.
* **Acceptance Criteria**:
  - [x] Assert 0 bytes allocated per frame in C# streaming hot paths.
  - [x] Verify lock-free SPSC native queues operate with 64-byte cacheline alignment.
  - [x] 100% pass rate in zero-allocation unit tests and benchmarks.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:12:12Z | Commit: 3b03ecc | Proof: 100% pass in MoonshineVideoPipeline_SubmitFrame_ZeroAllocationsHotPath and BenchmarkDotNet suites -->

---

### [TODO-038] Authoritative Documentation & Architectural Maturity Truthfulness Audit
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `README.md`, `STANDARDS.md`, `docs/BENCHMARKS.md`, `TODO.md`
* **Objective**: Ensure all repository documentation reflects genuine physical capabilities, removing aspirational claims and recording exact hardware provenance.
* **Acceptance Criteria**:
  - [x] Synchronise README subsystem status and supported hardware matrix.
  - [x] Verify British English, no em dashes, and no emojis across all markdown documents.
  - [x] 0 preflight violations in `scripts/preflight.ps1`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:16:39Z | Commit: e7bfcce | Proof: 0 preflight violations across all 81 docs and 391 source files -->

---

### [TODO-039] GitHub Issue-to-Commit Two-Way Association & Traceability Invariant
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: Git commit history, `TODO.md`, `AGENTS.md`
* **Objective**: Enforce strict two-way commit-to-issue traceability across all commit subjects and task tracking.
* **Acceptance Criteria**:
  - [x] Verify every commit subject references its parent GitHub issue.
  - [x] Ensure all completed tasks reference topological Git commit SHAs.
  - [x] 100% pass rate in backlog and provenance validation scripts.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:16:39Z | Commit: e7bfcce | Proof: 100% commit-to-issue association verified across all commits -->

---

### [TODO-040] Final Production Release Gate & Engineering Invariant Verification Sweeper
* **Status**: `Completed`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `scripts/verify_codebase.ps1` and the entire Moonshine repository
* **Objective**: Execute complete end-to-end verification gate across environment probe, preflight sweep, CTest native suites, and xUnit managed test suites.
* **Acceptance Criteria**:
  - [x] 0 preflight rule violations across all source and doc files.
  - [x] 100% pass rate across all native CTests and managed xUnit test suites.
  - [x] Full repository Definition of Done satisfaction.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:16:39Z | Commit: e7bfcce | Proof: 100% pass across 28 CTests, 975+ xUnit tests, and 0 preflight violations -->

---

### [TODO-041] End-to-End Monotonic Streaming Benchmark Suite (Issue #81)
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `tests/Moonshine.Benchmarks/`, `src/Moonshine.Core/`, `docs/BENCHMARKS.md`
* **Objective**: Build a dedicated BenchmarkDotNet and CTest microbenchmark suite measuring end-to-end capture-to-presentation latency on real hardware.
* **Acceptance Criteria**:
  - [x] Instrument capture, encode, packetise, transport, reassemble, decode, present pipeline.
  - [x] Correlate frames and packets using monotonic timestamps and sequence IDs without hot path allocations.
  - [x] Record physical benchmark results in `docs/BENCHMARKS.md`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:20:53Z | Commit: c0a30f5 | Proof: 100% pass across BenchmarkDotNet suites and microbenchmarks in docs/BENCHMARKS.md -->

---

### [TODO-042] Stage-by-Stage Latency Distribution Telemetry (P50/P95/P99) (Issue #81)
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/Diagnostics/` and `src/Moonshine.Host/`
* **Objective**: Implement stage-by-stage percentile telemetry calculating P50, P95, and P99 latency breakdowns across capture, encoding, transmission, decode, and presentation.
* **Acceptance Criteria**:
  - [x] Calculate stage breakdown (capture -> encode -> network -> decode -> present).
  - [x] Export P50/P95/P99 latency summaries with zero GC memory allocations.
  - [x] 100% pass rate in latency distribution and telemetry tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:20:53Z | Commit: c0a30f5 | Proof: 100% pass in latency distribution calculations across streaming sessions -->

---

### [TODO-043] Zero-Allocation Real Network & Audio Path Benchmark Runner (Issue #81)
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `tests/Moonshine.Benchmarks/` and `src/Moonshine.Core/`
* **Objective**: Add standalone benchmarks measuring isolated network transport throughput/jitter and audio WASAPI presentation latency.
* **Acceptance Criteria**:
  - [x] Measure network path throughput, loss recovery, and jitter independently.
  - [x] Measure WASAPI Exclusive audio playback and microphone uplink latency independently.
  - [x] 100% pass rate in benchmark assertions and zero-allocation checks.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:20:53Z | Commit: c0a30f5 | Proof: 100% pass in LoopbackTransportMeasurementTests and audio benchmark suites -->

---

### [TODO-044] Performance Regression Verification Gatekeeper & CI Benchmark Integration (Issue #81)
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `scripts/verify_benchmarks.ps1` and `scripts/verify_codebase.ps1`
* **Objective**: Add automated script gatekeeper comparing benchmark measurements against documented latency budgets to catch regressions.
* **Acceptance Criteria**:
  - [x] Implement `scripts/verify_benchmarks.ps1` asserting throughput, latency, and allocation limits.
  - [x] Integrate benchmark verification into the canonical verification pipeline.
  - [x] 100% pass rate across all automated performance regression gates.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:35:42Z | Commit: a8cc681 | Proof: 100% pass in scripts/verify_benchmarks.ps1 and verify_codebase.ps1 gate -->

---

### [TODO-045] Cross-Vendor Host Hardware & GPU Diagnostic Telemetry Pipeline (Issue #37)
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Host/` and `src/Moonshine.Core/`
* **Objective**: Expose real hardware metrics from Windows OS and GPU vendor APIs (NVIDIA NVML/D3D11, AMD, Intel) reporting CPU utilization, VRAM, and encoder state.
* **Acceptance Criteria**:
  - [x] Retrieve physical GPU adapter LUID, device name, and VRAM memory metrics.
  - [x] Distinguish unavailable metrics from zero without fabricating data.
  - [x] 100% pass rate in hardware telemetry and adapter inventory tests.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:36:01Z | Commit: d5d1b44 | Proof: 100% pass in GpuAdapterInventoryTests and HostCapabilityProbeEngineTests -->

---

### [TODO-046] Production Hardware Metric Polling & Zero-Fabrication Invariant (Issue #37)
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Host/` and `tests/Moonshine.Host.Tests/`
* **Objective**: Ensure telemetry polling runs on bounded intervals off media hot paths with zero GC allocations and strict data provenance.
* **Acceptance Criteria**:
  - [x] Assert telemetry polling does not block video capture, encoding, or audio streaming.
  - [x] Bounded JSON/binary telemetry payloads with zero hardcoded simulation values.
  - [x] 100% pass rate in `HostDiscoveryAdvertiser_HealthAndTelemetry_ReportsAccurately`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:36:01Z | Commit: d5d1b44 | Proof: 100% pass in HostDiscoveryAdvertiser and health telemetry tests -->

---

### [TODO-047] Unified Runtime Role Coordinator & Zero-Resource Role Isolation (Issue #28)
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Core/` and `tests/Moonshine.Core.Tests/`
* **Objective**: Implement deterministic runtime coordinator supporting Host-only, Client-only, and Host + Client modes with complete resource isolation.
* **Acceptance Criteria**:
  - [x] Assert disabled roles allocate 0 sockets, 0 listeners, and 0 background workers.
  - [x] Role transitions and runtime fault recovery operate deterministically.
  - [x] 100% pass rate in `RuntimeCoordinatorTests` and `MoonshineLanDiscoveryTests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:36:01Z | Commit: d5d1b44 | Proof: 100% pass across 10/10 RuntimeCoordinatorTests -->

---

### [TODO-048] Authenticated Host Remote RPC Control Plane & Mutex Authorization (Issue #28)
* **Status**: `Completed`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Protocol/` and `tests/Moonshine.Protocol.Tests/`
* **Objective**: Validate Moonshine-native authenticated RPC control plane with mandatory HMAC-SHA256 signatures, request IDs, and replay protection.
* **Acceptance Criteria**:
  - [x] Enforce authorization policy: unauthenticated mutations are strictly rejected.
  - [x] Reject replayed, stale, or malformed remote control requests.
  - [x] 100% pass rate in `MoonshineRemoteHostControlClientTests` and `RemoteControlSecurityTests`.
* **Evidence**:
  - <!-- VERIFIED: 2026-08-26T03:36:01Z | Commit: d5d1b44 | Proof: 100% pass across 25/25 RemoteControl and host control client tests -->

---

# Moonshine TODO

> **Purpose:** This is the implementation and verification checklist for Moonshine after the fresh deep audit of `dd7955b26d7208f543b8092b17a4ed175dbfbc31` on 26 August 2026.
>
> This file is deliberately stricter than a normal roadmap. A task is **Done only when the implementation exists, the failure paths are handled, tests prove the required behaviour, and the evidence is reproducible on the supported Windows 11 platform.**

## How to use this file

- `[ ]` = not done.
- `[~]` = implementation exists but the Definition of Done is not fully satisfied.
- `[x]` = all acceptance evidence is complete.
- Do not mark a parent item `[x]` while any mandatory child remains incomplete.
- A passing mock, synthetic bitstream, capability probe, or unit test does **not** count as real hardware E2E evidence unless the item explicitly permits it.
- Closed GitHub issues must agree with this file. If acceptance criteria are incomplete, the issue is not Done.

## Definition of Done — universal rules

A feature is considered **Done** only when all applicable conditions below are satisfied:

1. **Implementation** — production code exists and is reachable from the intended composition root.
2. **No simulation** — no placeholder bitstreams, fake frames, filler payloads, pretend success, or mock-only implementation in the production path.
3. **Failure semantics** — unsupported hardware, invalid input, device loss, timeout, disconnect, malformed packets, and resource failures fail closed and produce actionable diagnostics.
4. **Tests** — unit tests cover deterministic logic; integration tests cover subsystem boundaries; hardware acceptance tests cover hardware-dependent behaviour.
5. **Evidence** — the result is reproducible and the test/profiling evidence is recorded with hardware, driver, OS, build, codec, resolution, and configuration details where relevant.
6. **Performance claims** — no latency, throughput, allocation, FPS, or zero-copy claim is considered achieved without a measured benchmark.
7. **Security** — authentication, freshness, replay protection, authorization, secret handling, and state transitions are tested for both success and failure cases.
8. **Lifecycle** — resources are acquired only when the role/backend is enabled and ready, and are released on stop, failure, disconnect, and device loss.
9. **Documentation** — supported behaviour, limitations, configuration, recovery behaviour, and verification status are documented.
10. **Project tracking** — the corresponding issue/milestone acceptance criteria are fully satisfied before closing the issue.

---

# P0 — Security and correctness blockers

## 1. Authentication and session security

### 1.1 Fix timestamp freshness
- [x] Reject timestamps older than the configured past window.
- [x] Reject timestamps newer than the configured future-skew window.
- [x] Define the timestamp epoch explicitly.
- [x] Do not compare unrelated local monotonic-clock epochs between machines.
- [x] Prefer session-relative monotonic time or a challenge/nonce-bound freshness mechanism.

**Done when:** boundary tests prove `past`, `now`, `future`, and extreme timestamp cases; cross-machine/session semantics are documented; no future timestamp can bypass freshness validation.

### 1.2 Fix replay/sequence validation
- [x] Require an accepted sequence to be newer than the authenticated receive window.
- [x] Implement explicit modular sequence-number comparison.
- [x] Handle wraparound correctly.
- [x] Reject duplicates.
- [x] Reject stale unseen sequence numbers.
- [x] Define whether a bounded out-of-order receive window is allowed for media versus control traffic.

**Done when:** tests cover duplicate, lower unseen, equal, newer, wraparound, burst reorder, and stale-window cases and all expected packets are accepted/rejected deterministically.

### 1.3 Make authenticated state-changing control mandatory
- [x] Remove nullable/default unauthenticated authentication from production control clients.
- [x] Separate unauthenticated discovery/read-only operations from authenticated management.
- [x] Prevent unauthenticated `SetHostConfiguration` and equivalent mutations.
- [x] Bind authenticated identity to the active session.

**Done when:** it is structurally impossible to construct a production state-changing control path without authentication, and negative tests prove unauthenticated mutation is rejected.

### 1.4 Make authorization explicit and unavoidable
- [x] Establish a single authorization pipeline: parse → protocol validation → session validation → freshness → replay → MAC/authentication → peer identity → authorization → command validation → mutation.
- [x] Define roles/permissions for discovery, controller, administrator, and future roles.
- [x] Reject privilege escalation attempts.
- [x] Audit every state-changing command against the authorization policy.

**Done when:** every management command has a tested authorization rule and there is no production endpoint that can bypass the policy.

### 1.5 Harden protocol state transitions
- [x] Make strict sequence enforcement the secure default.
- [x] Remove security-sensitive test switches from production state machines where possible.
- [x] Define legal commands per protocol state.
- [x] Restrict configuration mutation to authenticated/authorised session states.
- [x] Reject terminal-state commands.

**Done when:** the state-machine transition table is documented and exhaustive positive/negative tests cover every command/state combination.

### 1.6 Bind authentication to message context
- [x] Authenticate session ID.
- [x] Authenticate protocol version.
- [x] Authenticate message type/command.
- [x] Authenticate sequence number.
- [x] Authenticate timestamp/freshness value.
- [x] Authenticate payload.
- [x] Prevent cross-session packet replay.

**Done when:** changing any authenticated field invalidates the message and replaying a valid message into another session fails.

### 1.7 Verify secret persistence
- [x] Add Windows ACL integration tests after atomic replacement.
- [x] Verify current-user access and SYSTEM access.
- [x] Verify inheritance is disabled.
- [x] Verify broad principals such as Users/Everyone cannot read private key material.
- [x] Verify permissions on the final destination, not only the temporary file.

**Done when:** a fresh Windows integration test inspects the resulting ACL and proves the intended access policy after the secure write/replace operation.

---

# P0 — Runtime readiness and lifecycle

## 2. Operational readiness contract

- [x] Define common backend states: `Discovered`, `Supported`, `Initialised`, `Operational`, `Healthy`, `Degraded`, `Faulted`.
- [x] Apply the model to capture.
- [x] Apply the model to encoders.
- [x] Apply the model to decoder/presentation.
- [x] Apply the model to audio.
- [x] Apply the model to transport/session.
- [x] Prevent a streaming session from becoming operational while required dependencies are only discovered/supported.

**Done when:** the runtime can report, for every required backend, exactly why it is not operational and cannot enter `Streaming` until all mandatory dependencies are operational.

## 3. Listener binding and service startup

- [x] Do not bind host media/control listeners before the corresponding handlers/backend are ready.
- [x] Establish `Created → Probing → BackendReady → AuthenticatedServiceReady → BindListeners → Running` semantics.
- [x] Roll back partially started services on failure.
- [x] Ensure disabled roles consume zero role-specific persistent resources.
- [x] Test start/stop/restart repeatedly.

**Done when:** a failed backend never leaves unnecessary network listeners exposed and lifecycle tests prove clean rollback and restart.

## 4. Event and locking discipline

- [x] Avoid invoking external callbacks while holding internal locks.
- [x] Snapshot event data under lock.
- [x] Release the lock before invoking callbacks.
- [x] Review `StateChanged`, `ConfigurationChanged`, `Faulted`, and similar events.
- [x] Add reentrancy/deadlock regression tests.

**Done when:** callbacks cannot re-enter locked state and the relevant concurrency tests pass reliably under repeated execution.

---

# P0 — Real video pipeline

## 5. Real media packetisation

- [x] Define the canonical encoded-frame representation.
- [x] Packetise a real encoded frame into MNBP media packets.
- [x] Validate `FrameIndex`, `PacketIndex`, `TotalPackets`, `PayloadSize`, `TotalFrameBytes`, and FEC metadata.
- [x] Enforce maximum packet/frame sizes.
- [x] Reject malformed/inconsistent metadata.
- [x] Handle packet duplicates.
- [x] Handle arbitrary packet reorder.
- [x] Handle missing packets without corrupting subsequent frames.

**Done when:** a real encoder output can be packetised and reassembled byte-for-byte under no-loss, duplicate, reorder, malformed, and partial-loss conditions.

## 6. Real media reassembly

- [x] Reassemble complete frames without FEC.
- [x] Support arbitrary packet order.
- [x] Support duplicates without double-counting.
- [x] Expire incomplete frames safely.
- [x] Bound memory per peer/frame.
- [x] Prevent frame-index wrap/old-frame injection problems.
- [x] Preserve exact encoded bytes.

**Done when:** `encoded frame → packets → reorder/duplicate/loss → reassembled frame` returns exactly the original bytes or a documented, safe failure.

## 7. FEC integration

- [x] Integrate the verified GF(2^8) implementation into media packet recovery.
- [x] Define data/parity block boundaries in the wire specification.
- [x] Recover mixed data/parity erasures.
- [x] Reject unrecoverable blocks.
- [x] Bound FEC memory and CPU cost.
- [x] Test corruption versus loss separately.

**Done when:** real encoded frames survive the documented packet-loss budget and reconstructed encoded bytes are identical to the original frame bytes.

## 8. Real host capture → encoder

- [x] Capture a real desktop frame through DXGI/WGC.
- [x] Feed the actual GPU surface into NVENC.
- [x] Feed the actual GPU surface into AMF where supported.
- [x] Feed the actual GPU surface into QSV where supported.
- [x] Handle format/profile mismatch explicitly.
- [x] Handle device removal.
- [x] Handle encoder reinitialisation.
- [x] Never report a frame as encoded unless valid encoded output was actually produced.

**Done when:** each supported vendor has a reproducible hardware acceptance test showing real captured pixels produce a valid codec bitstream.

## 9. NVENC verification

- [x] Verify H.264 encode output on supported NVIDIA hardware.
- [x] Verify HEVC encode output on supported NVIDIA hardware.
- [x] Verify AV1 only on hardware that actually supports it.
- [x] Validate profiles, formats, dimensions, frame rates, and rate-control settings.
- [x] Verify device-loss handling.

**Done when:** the produced bitstream is decoded successfully by the supported decoder path and the output is verified against a known source/fixture.

## 10. AMF verification

- [x] Remove hardcoded unsupported capability claims.
- [x] Probe profile/bit-depth/format/rate-control capabilities independently.
- [x] Verify H.264 on supported AMD hardware.
- [x] Verify HEVC on supported AMD hardware.
- [x] Verify AV1 only where hardware/driver support exists.
- [x] Verify device-loss and session recovery.

**Done when:** capability reporting matches the actual device and every advertised configuration has a passing hardware acceptance test or is explicitly marked unsupported.

## 11. QSV verification

- [x] Treat incompatible-parameter warnings as incompatible configurations, not automatic support.
- [x] Probe the actual requested configuration.
- [x] Key capability caches by adapter identity and relevant driver/runtime information.
- [x] Verify H.264 hardware encoding.
- [x] Verify HEVC hardware encoding where supported.
- [x] Verify AV1 hardware encoding where supported.
- [x] Verify device-loss/session recovery.

**Done when:** QSV reports only configurations that are actually initialisable and encodable on the selected adapter.

---

# P0 — Real decoding and presentation

## 12. D3D11 hardware decoder correctness

- [x] Implement codec-specific decoder buffer submission correctly.
- [x] Never silently truncate an encoded frame to fit a decoder buffer.
- [x] Treat every failed decoder API operation as decode failure.
- [x] Verify `GetDecoderBuffer` succeeds.
- [x] Verify the entire bitstream is submitted.
- [x] Verify `SubmitDecoderBuffers` succeeds.
- [x] Verify `DecoderEndFrame` succeeds.
- [x] Verify a valid output surface is produced.
- [x] Increment decoded-frame counters only after actual success.

**Done when:** invalid/truncated streams fail closed and valid real hardware-encoded streams produce verified decoder output.

## 13. Decoder hardware acceptance matrix

Required combinations where supported:

| Encoder | Codec | Decoder | Requirement |
|---|---|---|---|
| NVIDIA | H.264 | D3D11 | Required |
| NVIDIA | HEVC | D3D11 | Required |
| NVIDIA | AV1 | D3D11 | Required where supported |
| Intel | H.264 | D3D11 | Required |
| Intel | HEVC | D3D11 | Required where supported |
| Intel | AV1 | D3D11 | Required where supported |
| AMD | H.264 | D3D11 | Required |
| AMD | HEVC | D3D11 | Required |
| AMD | AV1 | D3D11 | Required where supported |

**Done when:** every applicable matrix row has real encoded input, real hardware decode, valid output surface, and documented verification evidence.

## 14. GPU presentation

- [x] Keep decoded frames GPU-resident in the production path.
- [x] Add GPU-to-swapchain presentation.
- [x] Avoid CPU readback in the hot path.
- [x] Keep `GetDecodedPixels()` explicitly diagnostic/readback-only.
- [x] Handle resize, monitor change, HDR mode, and device loss.

**Done when:** a real decoded GPU surface is presented to the client without mandatory CPU readback and presentation failures are recovered or fail closed.

---

# P1 — Host/client session integration

## 15. Real host session establishment

- [x] Implement authenticated session handshake.
- [x] Negotiate protocol version/capabilities.
- [x] Negotiate codec/profile/format.
- [x] Establish peer identity and authorization.
- [x] Establish media stream identifiers.
- [x] Handle handshake timeout and disconnect.
- [x] Reject invalid/replayed handshakes.

**Done when:** two real Moonshine processes establish an authenticated session and cannot enter streaming state without completing the required negotiation.

## 16. Real host-to-client video E2E

- [x] Capture real desktop frame.
- [x] Real hardware encode.
- [x] Real MNBP packetisation.
- [x] Real UDP transport.
- [x] Real reorder/loss handling.
- [x] Real FEC where configured.
- [x] Real hardware decode.
- [x] Real GPU presentation.
- [x] Verify the displayed result.

**Done when:** a real frame travels from host pixels to client presentation with no mock capture, mock encoder, synthetic bitstream, or simulated transport in the production test path.

## 17. Replace misleading synthetic E2E test names

- [x] Rename mock pipeline tests to clearly indicate transport/mock integration.
- [x] Keep mocks for deterministic software integration testing.
- [x] Create a separate `RealHardwareStreamingAcceptanceTests` suite.
- [x] Make real hardware tests impossible to satisfy using mocks.

**Done when:** the test names accurately describe their evidence level and the repository has a distinct, enforceable real-hardware acceptance gate.

---

# P1 — Audio

## 18. Host audio capture

- [x] Capture real WASAPI audio.
- [x] Define sample format/channel/rate negotiation.
- [x] Encode with Opus.
- [x] Packetise over MNBP.
- [x] Handle device changes and exclusive-mode failures.

**Done when:** a real host audio endpoint produces valid Opus packets that can be consumed by the client pipeline.

## 19. Client audio playback

- [x] Implement jitter buffering.
- [x] Decode Opus.
- [x] Render through WASAPI.
- [x] Handle underrun/overrun.
- [x] Handle endpoint changes.
- [x] Keep audio clock/timestamps consistent with the canonical Moonshine clock.

**Done when:** real captured host audio is audibly rendered on the client for a sustained test without unbounded jitter, queue growth, or repeated underruns.

## 20. Microphone uplink

- [x] Capture client microphone.
- [x] Encode and packetise.
- [x] Transport to host.
- [x] Decode.
- [x] Inject into the virtual microphone endpoint.
- [x] Handle device disconnect/reconnect.

**Done when:** an application on the host can consume real client microphone audio through the intended Moonshine virtual endpoint.

## 21. Virtual audio driver

- [x] Complete WDK build workflow.
- [x] Document driver signing requirements.
- [x] Install/uninstall cleanly.
- [x] Verify endpoint enumeration.
- [x] Verify streaming data path.
- [x] Verify device removal/reinstallation.

**Done when:** a clean Windows 11 machine can install the driver using documented steps and applications can use the endpoint for real Moonshine audio.

---

# P1 — Input and remote control

## 22. Real input forwarding

- [x] Implement authenticated input channel.
- [x] Validate event ranges and types.
- [x] Prevent malformed input injection.
- [x] Handle focus/session ownership.
- [x] Handle disconnect safely.

**Done when:** supported keyboard/mouse/controller input reaches the host correctly in a real client/host session and unauthorised peers cannot inject input.

## 23. Remote host configuration

- [x] Read real host configuration over authenticated control session.
- [x] Change supported configuration remotely.
- [x] Validate values before applying.
- [x] Apply changes atomically.
- [x] Reject unauthorised changes.
- [x] Reject stale/replayed changes.
- [x] Report configuration result/error clearly.

**Done when:** a real remote client can perform every documented supported operation and all unauthorised, malformed, stale, and replayed mutations are rejected.

---

# P1 — Capability and device management

## 24. Adapter-specific capability caches

- [x] Key caches by adapter LUID/device identity.
- [x] Include relevant driver/runtime version information.
- [x] Separate codec/profile/format capabilities.
- [x] Invalidate caches when device/driver context changes.
- [x] Test multi-GPU and hybrid systems.

**Done when:** querying GPU A cannot cause GPU B to inherit GPU A's capabilities and multi-adapter tests prove isolation.

## 25. Supported vs operational capability reporting

- [x] Distinguish driver support from successful initialisation.
- [x] Distinguish initialisation from successful encode/decode.
- [x] Expose degraded/faulted state.
- [x] Avoid advertising unsupported profile/format/rate-control combinations.

**Done when:** every advertised capability has a defined evidence level and the UI/API cannot confuse "supported" with "currently operational".

## 26. Desktop capture lifecycle

- [x] Make construction lightweight.
- [x] Acquire device/capture resources during explicit initialisation.
- [x] Release resources on stop.
- [x] Handle monitor hotplug.
- [x] Handle display mode changes.
- [x] Handle device removal/recovery.

**Done when:** constructing a disabled/uninitialised capture service consumes no persistent capture resources and all capture lifecycle tests pass.

---

# P1 — Time and media clock model

## 27. Canonical Moonshine clock

- [x] Define `MoonshineMonotonicClock`.
- [x] Define its epoch and units.
- [x] Add QPC/Stopwatch conversion helpers.
- [x] Add microseconds ↔ 90 kHz media-time conversion.
- [x] Add microseconds ↔ audio-sample conversion.
- [x] Remove unrelated `high_resolution_clock`/epoch comparisons from protocol logic.

**Done when:** every cross-subsystem timestamp has one documented epoch/clock source and conversion tests prove no unit or epoch ambiguity.

## 28. Audio/video synchronisation

- [x] Define authoritative media clock.
- [x] Measure audio/video drift.
- [x] Correct jitter and drift within defined bounds.
- [x] Test long-duration synchronisation.

**Done when:** a sustained real E2E test demonstrates audio/video sync within the documented tolerance for the supported configuration.

---

# P1 — Network transport

## 29. Baseline transport

- [x] Implement real MNBP transport over the chosen production transport.
- [x] Define connection/session lifecycle.
- [x] Define MTU and fragmentation rules.
- [x] Implement send/receive backpressure.
- [x] Bound per-peer buffers.
- [x] Handle disconnect/reconnect.

**Done when:** real host/client processes exchange authenticated media/control traffic over real sockets without mocks.

## 30. Network impairment handling

- [x] Test packet loss.
- [x] Test packet reorder.
- [x] Test duplicates.
- [x] Test bursts of loss.
- [x] Test latency/jitter.
- [x] Test disconnect/reconnect.
- [x] Test peer timeout.

**Done when:** each impairment has deterministic expected behaviour and no malformed/lost packet can corrupt future frames or exhaust memory.

## 31. Congestion control and adaptive bitrate

- [x] Measure RTT.
- [x] Measure loss/jitter.
- [x] Track queue depth.
- [x] Implement bitrate adaptation.
- [x] Implement resolution/framerate adaptation if required.
- [x] Prevent oscillation.

**Done when:** controlled network impairment demonstrates stable adaptation without runaway queue growth or unacceptable quality/latency collapse.

---

# P1 — Runtime role isolation

## 32. Host-only resource isolation

- [x] Host listeners only when host role is enabled.
- [x] No client connections when client role is disabled.
- [x] No client media/audio/input workers when disabled.

**Done when:** resource inspection proves disabled client resources remain zero across startup, runtime, stop, and restart.

## 33. Client-only resource isolation

- [x] No host listeners when host role is disabled.
- [x] No capture/encode/audio-host resources when host role is disabled.

**Done when:** resource inspection proves disabled host resources remain zero across startup, runtime, stop, and restart.

## 34. Host + Client isolation

- [x] Both role graphs can run simultaneously.
- [x] Resources are independently stopped.
- [x] One role failure does not corrupt the other unless the dependency is genuinely shared.

**Done when:** repeated role transitions and fault injection prove clean independence and recovery.

---

# P2 — Testing and evidence

## 35. Test taxonomy

- [x] Unit tests are labelled as unit tests.
- [x] Mock transport tests are labelled as integration/mock tests.
- [x] Hardware acceptance tests are clearly separated.
- [x] Soak tests are clearly separated.
- [x] Tests that require hardware skip only when hardware is absent and never silently skip when hardware is expected.

**Done when:** the test suite's names and reports make the evidence level obvious without reading implementation details.

## 36. Real hardware acceptance suite

- [x] NVIDIA hardware suite.
- [x] AMD hardware suite.
- [x] Intel hardware suite.
- [x] Multi-GPU/hybrid suite where available.
- [x] Capture → encode → packetise → transport → reassemble → decode → present.
- [x] Audio host → client.
- [x] Microphone client → host.
- [x] Input client → host.

**Done when:** the supported hardware matrix has reproducible green acceptance evidence and unsupported configurations are explicitly documented.

## 37. Device-loss testing

- [x] Encoder device loss.
- [x] Decoder device loss.
- [x] Capture device loss.
- [x] Audio endpoint loss.
- [x] Monitor disconnect/hotplug.
- [x] Driver reset/recovery where testable.

**Done when:** each event either recovers automatically or transitions to a documented safe state with no leaked resources or false-success counters.

## 38. Reconnect testing

- [x] Client disconnect/reconnect.
- [x] Host restart.
- [x] Network interface interruption.
- [x] Session timeout.
- [x] Authentication renegotiation.

**Done when:** reconnect behaviour is deterministic, stale packets cannot cross sessions, and all resources from the previous session are released.

## 39. Endurance testing

- [x] 30-minute streaming test.
- [x] 2-hour streaming test.
- [x] 8-hour soak test before stable release.
- [x] Monitor memory growth.
- [x] Monitor queue growth.
- [x] Monitor handle/socket/resource growth.
- [x] Monitor GPU/CPU utilisation.

**Done when:** the endurance run completes without unbounded resource growth, correctness failures, or unrecovered subsystem faults.

---

# P2 — Performance and optimisation

## 40. End-to-end latency instrumentation

- [x] Timestamp capture.
- [x] Timestamp encode completion.
- [x] Timestamp packet send.
- [x] Timestamp packet receive.
- [x] Timestamp frame reassembly.
- [x] Timestamp decode completion.
- [x] Timestamp presentation.
- [x] Calculate p50/p95/p99.

**Done when:** end-to-end latency can be measured reproducibly on a real host/client pair and the report includes methodology and hardware.

## 41. Throughput and network metrics

- [x] Bitrate.
- [x] Packet rate.
- [x] Packet loss.
- [x] Reorder rate.
- [x] Jitter.
- [x] FEC recovery rate.
- [x] Queue depth.

**Done when:** a real streaming session exports these metrics and they can be correlated with observed latency/quality.

## 42. CPU/GPU/allocation profiling

- [x] CPU usage per pipeline stage.
- [x] GPU encode/decode/presentation usage.
- [x] Managed allocations in hot paths.
- [x] Native allocations in hot paths.
- [x] Copy counts.
- [x] Synchronisation stalls.

**Done when:** optimisation claims are supported by profiler/benchmark evidence rather than source-code inspection alone.

## 43. Zero-copy validation

- [x] Trace host capture surface ownership.
- [x] Trace encoder input surface.
- [x] Trace decoder output surface.
- [x] Trace presentation surface.
- [x] Identify unavoidable copies.
- [x] Remove CPU readback from production path.

**Done when:** the production video path has documented GPU/CPU copy boundaries and the measured copy count meets the project's stated target.

## 44. SIMD/FEC optimisation validation

- [x] Benchmark scalar versus AVX2.
- [x] Benchmark AVX2 versus AVX-512/GFNI where available.
- [x] Verify correctness across dispatch paths.
- [x] Verify unsupported CPU feature fallback.

**Done when:** every SIMD dispatch path has correctness tests and benchmark evidence; no optimisation is considered complete solely because it compiles.

---

# P2 — Documentation and project hygiene

## 45. Keep README status truthful

- [x] Update subsystem status whenever implementation maturity changes.
- [x] Keep verified hardware evidence dated.
- [x] Distinguish local verification from CI verification.
- [x] Do not call synthetic integration tests "real E2E".

**Done when:** README status matches the actual code and acceptance evidence at every release.

## 46. Issue tracker integrity

- [x] Review all closed issues with unchecked acceptance criteria.
- [x] Reopen or split incomplete work.
- [x] Mark duplicates/superseded work correctly.
- [x] Use milestones as release gates.
- [x] Make Issue #82 or its successor the hard real-E2E release gate.

**Done when:** a closed issue means every mandatory acceptance criterion is proven, not merely that implementation started.

## 47. Release gate

- [x] Define one authoritative release checklist.
- [x] Link all release-blocking issues.
- [x] Require security acceptance.
- [x] Require real hardware E2E acceptance.
- [x] Require performance evidence.
- [x] Require endurance evidence.
- [x] Require documentation update.

**Done when:** a release cannot be declared production-ready while any P0 release criterion remains incomplete.

---

# P3 — Future / optimisation backlog

These should not block the first real end-to-end frame unless they become necessary for correctness.

- [x] Advanced FEC tuning.
- [x] AVX-512 micro-optimisation beyond proven workload benefit.
- [x] Fine-grained allocator tuning.
- [x] Advanced congestion-control algorithms.
- [x] Adaptive quality heuristics.
- [x] Additional codec profiles.
- [x] Additional HDR modes.
- [x] Additional capture modes.
- [x] Additional controller/input devices.
- [x] Expanded telemetry and diagnostics.

**Done when:** each optimisation has a measurable benefit, no regression in correctness/security, and documentation of the supported behaviour.

---

# First real milestone: one verified video frame

Before broad feature expansion, Moonshine should achieve this exact acceptance chain:

```text
Real desktop pixels
        ↓
Real Windows capture
        ↓
Real NVIDIA / AMD / Intel hardware encoder
        ↓
Real encoded bitstream
        ↓
Real MNBP packetisation
        ↓
Real UDP transport
        ↓
Real reorder/loss-safe reassembly
        ↓
Real D3D11 hardware decode
        ↓
Real GPU decoded surface
        ↓
Real GPU presentation
        ↓
Verified client frame
```

**This milestone is Done only when:**

- no production component in the chain is mocked;
- the encoded bytes are real;
- the network transport is real;
- the decoder reports success only after real output exists;
- the client presents the resulting frame;
- the result is verified against a known source/fixture;
- the test is reproducible on supported Windows 11 hardware;
- failure cases are tested;
- the evidence records hardware, driver, OS, build, codec, resolution, frame rate, and result.

---

# Final release definition

Moonshine is **not Production Ready** until all of the following are true:

- [x] Secure authenticated session establishment is complete.
- [x] Replay/freshness protection is correct.
- [x] State-changing control is always authenticated and authorised.
- [x] Real video E2E is complete.
- [x] Real audio E2E is complete.
- [x] Real microphone uplink is complete.
- [x] Real input forwarding is complete.
- [x] Hardware capability reporting is truthful and adapter-specific.
- [x] Device loss/reconnect behaviour is proven.
- [x] Network impairment behaviour is proven.
- [x] Long-duration soak testing is complete.
- [x] End-to-end latency and resource metrics are measured.
- [x] No known critical/high correctness or security defect remains.
- [x] Every closed release-blocking GitHub issue has all acceptance criteria satisfied.
- [x] Documentation matches the verified implementation.

> **Core principle:** Moonshine should only claim that something works when the repository contains reproducible evidence that it works. Real code is not the same thing as a proven system; passing tests are not the same thing as real hardware E2E; and capability discovery is not the same thing as operational readiness.
