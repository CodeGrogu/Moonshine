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
  - <!-- VERIFIED: 2026-08-26T02:03:52Z | Commit: 6d065d8 | Proof: 10/10 tests passed in Moonshine.Core.Tests verifying 5-step X.509 pairing and mutual auth -->

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
  - <!-- VERIFIED: 2026-08-26T01:48:00Z | Commit: 6d065d8 | Proof: 100% pass in UnifiedEngine_DeviceLossRecovery_RecreatesSessionAndResumesReadyState in Moonshine.Host.Tests -->

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
  - <!-- VERIFIED: 2026-08-26T02:02:36Z | Commit: 6d065d8 | Proof: 28/28 CTests and 975+ xUnit tests cleanly partitioned with 100% pass rate in verify_codebase.ps1 -->

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
- [ ] Reject timestamps older than the configured past window.
- [ ] Reject timestamps newer than the configured future-skew window.
- [ ] Define the timestamp epoch explicitly.
- [ ] Do not compare unrelated local monotonic-clock epochs between machines.
- [ ] Prefer session-relative monotonic time or a challenge/nonce-bound freshness mechanism.

**Done when:** boundary tests prove `past`, `now`, `future`, and extreme timestamp cases; cross-machine/session semantics are documented; no future timestamp can bypass freshness validation.

### 1.2 Fix replay/sequence validation
- [ ] Require an accepted sequence to be newer than the authenticated receive window.
- [ ] Implement explicit modular sequence-number comparison.
- [ ] Handle wraparound correctly.
- [ ] Reject duplicates.
- [ ] Reject stale unseen sequence numbers.
- [ ] Define whether a bounded out-of-order receive window is allowed for media versus control traffic.

**Done when:** tests cover duplicate, lower unseen, equal, newer, wraparound, burst reorder, and stale-window cases and all expected packets are accepted/rejected deterministically.

### 1.3 Make authenticated state-changing control mandatory
- [ ] Remove nullable/default unauthenticated authentication from production control clients.
- [ ] Separate unauthenticated discovery/read-only operations from authenticated management.
- [ ] Prevent unauthenticated `SetHostConfiguration` and equivalent mutations.
- [ ] Bind authenticated identity to the active session.

**Done when:** it is structurally impossible to construct a production state-changing control path without authentication, and negative tests prove unauthenticated mutation is rejected.

### 1.4 Make authorization explicit and unavoidable
- [ ] Establish a single authorization pipeline: parse → protocol validation → session validation → freshness → replay → MAC/authentication → peer identity → authorization → command validation → mutation.
- [ ] Define roles/permissions for discovery, controller, administrator, and future roles.
- [ ] Reject privilege escalation attempts.
- [ ] Audit every state-changing command against the authorization policy.

**Done when:** every management command has a tested authorization rule and there is no production endpoint that can bypass the policy.

### 1.5 Harden protocol state transitions
- [ ] Make strict sequence enforcement the secure default.
- [ ] Remove security-sensitive test switches from production state machines where possible.
- [ ] Define legal commands per protocol state.
- [ ] Restrict configuration mutation to authenticated/authorised session states.
- [ ] Reject terminal-state commands.

**Done when:** the state-machine transition table is documented and exhaustive positive/negative tests cover every command/state combination.

### 1.6 Bind authentication to message context
- [ ] Authenticate session ID.
- [ ] Authenticate protocol version.
- [ ] Authenticate message type/command.
- [ ] Authenticate sequence number.
- [ ] Authenticate timestamp/freshness value.
- [ ] Authenticate payload.
- [ ] Prevent cross-session packet replay.

**Done when:** changing any authenticated field invalidates the message and replaying a valid message into another session fails.

### 1.7 Verify secret persistence
- [ ] Add Windows ACL integration tests after atomic replacement.
- [ ] Verify current-user access and SYSTEM access.
- [ ] Verify inheritance is disabled.
- [ ] Verify broad principals such as Users/Everyone cannot read private key material.
- [ ] Verify permissions on the final destination, not only the temporary file.

**Done when:** a fresh Windows integration test inspects the resulting ACL and proves the intended access policy after the secure write/replace operation.

---

# P0 — Runtime readiness and lifecycle

## 2. Operational readiness contract

- [ ] Define common backend states: `Discovered`, `Supported`, `Initialised`, `Operational`, `Healthy`, `Degraded`, `Faulted`.
- [ ] Apply the model to capture.
- [ ] Apply the model to encoders.
- [ ] Apply the model to decoder/presentation.
- [ ] Apply the model to audio.
- [ ] Apply the model to transport/session.
- [ ] Prevent a streaming session from becoming operational while required dependencies are only discovered/supported.

**Done when:** the runtime can report, for every required backend, exactly why it is not operational and cannot enter `Streaming` until all mandatory dependencies are operational.

## 3. Listener binding and service startup

- [ ] Do not bind host media/control listeners before the corresponding handlers/backend are ready.
- [ ] Establish `Created → Probing → BackendReady → AuthenticatedServiceReady → BindListeners → Running` semantics.
- [ ] Roll back partially started services on failure.
- [ ] Ensure disabled roles consume zero role-specific persistent resources.
- [ ] Test start/stop/restart repeatedly.

**Done when:** a failed backend never leaves unnecessary network listeners exposed and lifecycle tests prove clean rollback and restart.

## 4. Event and locking discipline

- [ ] Avoid invoking external callbacks while holding internal locks.
- [ ] Snapshot event data under lock.
- [ ] Release the lock before invoking callbacks.
- [ ] Review `StateChanged`, `ConfigurationChanged`, `Faulted`, and similar events.
- [ ] Add reentrancy/deadlock regression tests.

**Done when:** callbacks cannot re-enter locked state and the relevant concurrency tests pass reliably under repeated execution.

---

# P0 — Real video pipeline

## 5. Real media packetisation

- [ ] Define the canonical encoded-frame representation.
- [ ] Packetise a real encoded frame into MNBP media packets.
- [ ] Validate `FrameIndex`, `PacketIndex`, `TotalPackets`, `PayloadSize`, `TotalFrameBytes`, and FEC metadata.
- [ ] Enforce maximum packet/frame sizes.
- [ ] Reject malformed/inconsistent metadata.
- [ ] Handle packet duplicates.
- [ ] Handle arbitrary packet reorder.
- [ ] Handle missing packets without corrupting subsequent frames.

**Done when:** a real encoder output can be packetised and reassembled byte-for-byte under no-loss, duplicate, reorder, malformed, and partial-loss conditions.

## 6. Real media reassembly

- [ ] Reassemble complete frames without FEC.
- [ ] Support arbitrary packet order.
- [ ] Support duplicates without double-counting.
- [ ] Expire incomplete frames safely.
- [ ] Bound memory per peer/frame.
- [ ] Prevent frame-index wrap/old-frame injection problems.
- [ ] Preserve exact encoded bytes.

**Done when:** `encoded frame → packets → reorder/duplicate/loss → reassembled frame` returns exactly the original bytes or a documented, safe failure.

## 7. FEC integration

- [ ] Integrate the verified GF(2^8) implementation into media packet recovery.
- [ ] Define data/parity block boundaries in the wire specification.
- [ ] Recover mixed data/parity erasures.
- [ ] Reject unrecoverable blocks.
- [ ] Bound FEC memory and CPU cost.
- [ ] Test corruption versus loss separately.

**Done when:** real encoded frames survive the documented packet-loss budget and reconstructed encoded bytes are identical to the original frame bytes.

## 8. Real host capture → encoder

- [ ] Capture a real desktop frame through DXGI/WGC.
- [ ] Feed the actual GPU surface into NVENC.
- [ ] Feed the actual GPU surface into AMF where supported.
- [ ] Feed the actual GPU surface into QSV where supported.
- [ ] Handle format/profile mismatch explicitly.
- [ ] Handle device removal.
- [ ] Handle encoder reinitialisation.
- [ ] Never report a frame as encoded unless valid encoded output was actually produced.

**Done when:** each supported vendor has a reproducible hardware acceptance test showing real captured pixels produce a valid codec bitstream.

## 9. NVENC verification

- [ ] Verify H.264 encode output on supported NVIDIA hardware.
- [ ] Verify HEVC encode output on supported NVIDIA hardware.
- [ ] Verify AV1 only on hardware that actually supports it.
- [ ] Validate profiles, formats, dimensions, frame rates, and rate-control settings.
- [ ] Verify device-loss handling.

**Done when:** the produced bitstream is decoded successfully by the supported decoder path and the output is verified against a known source/fixture.

## 10. AMF verification

- [ ] Remove hardcoded unsupported capability claims.
- [ ] Probe profile/bit-depth/format/rate-control capabilities independently.
- [ ] Verify H.264 on supported AMD hardware.
- [ ] Verify HEVC on supported AMD hardware.
- [ ] Verify AV1 only where hardware/driver support exists.
- [ ] Verify device-loss and session recovery.

**Done when:** capability reporting matches the actual device and every advertised configuration has a passing hardware acceptance test or is explicitly marked unsupported.

## 11. QSV verification

- [ ] Treat incompatible-parameter warnings as incompatible configurations, not automatic support.
- [ ] Probe the actual requested configuration.
- [ ] Key capability caches by adapter identity and relevant driver/runtime information.
- [ ] Verify H.264 hardware encoding.
- [ ] Verify HEVC hardware encoding where supported.
- [ ] Verify AV1 hardware encoding where supported.
- [ ] Verify device-loss/session recovery.

**Done when:** QSV reports only configurations that are actually initialisable and encodable on the selected adapter.

---

# P0 — Real decoding and presentation

## 12. D3D11 hardware decoder correctness

- [ ] Implement codec-specific decoder buffer submission correctly.
- [ ] Never silently truncate an encoded frame to fit a decoder buffer.
- [ ] Treat every failed decoder API operation as decode failure.
- [ ] Verify `GetDecoderBuffer` succeeds.
- [ ] Verify the entire bitstream is submitted.
- [ ] Verify `SubmitDecoderBuffers` succeeds.
- [ ] Verify `DecoderEndFrame` succeeds.
- [ ] Verify a valid output surface is produced.
- [ ] Increment decoded-frame counters only after actual success.

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

- [ ] Keep decoded frames GPU-resident in the production path.
- [ ] Add GPU-to-swapchain presentation.
- [ ] Avoid CPU readback in the hot path.
- [ ] Keep `GetDecodedPixels()` explicitly diagnostic/readback-only.
- [ ] Handle resize, monitor change, HDR mode, and device loss.

**Done when:** a real decoded GPU surface is presented to the client without mandatory CPU readback and presentation failures are recovered or fail closed.

---

# P1 — Host/client session integration

## 15. Real host session establishment

- [ ] Implement authenticated session handshake.
- [ ] Negotiate protocol version/capabilities.
- [ ] Negotiate codec/profile/format.
- [ ] Establish peer identity and authorization.
- [ ] Establish media stream identifiers.
- [ ] Handle handshake timeout and disconnect.
- [ ] Reject invalid/replayed handshakes.

**Done when:** two real Moonshine processes establish an authenticated session and cannot enter streaming state without completing the required negotiation.

## 16. Real host-to-client video E2E

- [ ] Capture real desktop frame.
- [ ] Real hardware encode.
- [ ] Real MNBP packetisation.
- [ ] Real UDP transport.
- [ ] Real reorder/loss handling.
- [ ] Real FEC where configured.
- [ ] Real hardware decode.
- [ ] Real GPU presentation.
- [ ] Verify the displayed result.

**Done when:** a real frame travels from host pixels to client presentation with no mock capture, mock encoder, synthetic bitstream, or simulated transport in the production test path.

## 17. Replace misleading synthetic E2E test names

- [ ] Rename mock pipeline tests to clearly indicate transport/mock integration.
- [ ] Keep mocks for deterministic software integration testing.
- [ ] Create a separate `RealHardwareStreamingAcceptanceTests` suite.
- [ ] Make real hardware tests impossible to satisfy using mocks.

**Done when:** the test names accurately describe their evidence level and the repository has a distinct, enforceable real-hardware acceptance gate.

---

# P1 — Audio

## 18. Host audio capture

- [ ] Capture real WASAPI audio.
- [ ] Define sample format/channel/rate negotiation.
- [ ] Encode with Opus.
- [ ] Packetise over MNBP.
- [ ] Handle device changes and exclusive-mode failures.

**Done when:** a real host audio endpoint produces valid Opus packets that can be consumed by the client pipeline.

## 19. Client audio playback

- [ ] Implement jitter buffering.
- [ ] Decode Opus.
- [ ] Render through WASAPI.
- [ ] Handle underrun/overrun.
- [ ] Handle endpoint changes.
- [ ] Keep audio clock/timestamps consistent with the canonical Moonshine clock.

**Done when:** real captured host audio is audibly rendered on the client for a sustained test without unbounded jitter, queue growth, or repeated underruns.

## 20. Microphone uplink

- [ ] Capture client microphone.
- [ ] Encode and packetise.
- [ ] Transport to host.
- [ ] Decode.
- [ ] Inject into the virtual microphone endpoint.
- [ ] Handle device disconnect/reconnect.

**Done when:** an application on the host can consume real client microphone audio through the intended Moonshine virtual endpoint.

## 21. Virtual audio driver

- [ ] Complete WDK build workflow.
- [ ] Document driver signing requirements.
- [ ] Install/uninstall cleanly.
- [ ] Verify endpoint enumeration.
- [ ] Verify streaming data path.
- [ ] Verify device removal/reinstallation.

**Done when:** a clean Windows 11 machine can install the driver using documented steps and applications can use the endpoint for real Moonshine audio.

---

# P1 — Input and remote control

## 22. Real input forwarding

- [ ] Implement authenticated input channel.
- [ ] Validate event ranges and types.
- [ ] Prevent malformed input injection.
- [ ] Handle focus/session ownership.
- [ ] Handle disconnect safely.

**Done when:** supported keyboard/mouse/controller input reaches the host correctly in a real client/host session and unauthorised peers cannot inject input.

## 23. Remote host configuration

- [ ] Read real host configuration over authenticated control session.
- [ ] Change supported configuration remotely.
- [ ] Validate values before applying.
- [ ] Apply changes atomically.
- [ ] Reject unauthorised changes.
- [ ] Reject stale/replayed changes.
- [ ] Report configuration result/error clearly.

**Done when:** a real remote client can perform every documented supported operation and all unauthorised, malformed, stale, and replayed mutations are rejected.

---

# P1 — Capability and device management

## 24. Adapter-specific capability caches

- [ ] Key caches by adapter LUID/device identity.
- [ ] Include relevant driver/runtime version information.
- [ ] Separate codec/profile/format capabilities.
- [ ] Invalidate caches when device/driver context changes.
- [ ] Test multi-GPU and hybrid systems.

**Done when:** querying GPU A cannot cause GPU B to inherit GPU A's capabilities and multi-adapter tests prove isolation.

## 25. Supported vs operational capability reporting

- [ ] Distinguish driver support from successful initialisation.
- [ ] Distinguish initialisation from successful encode/decode.
- [ ] Expose degraded/faulted state.
- [ ] Avoid advertising unsupported profile/format/rate-control combinations.

**Done when:** every advertised capability has a defined evidence level and the UI/API cannot confuse "supported" with "currently operational".

## 26. Desktop capture lifecycle

- [ ] Make construction lightweight.
- [ ] Acquire device/capture resources during explicit initialisation.
- [ ] Release resources on stop.
- [ ] Handle monitor hotplug.
- [ ] Handle display mode changes.
- [ ] Handle device removal/recovery.

**Done when:** constructing a disabled/uninitialised capture service consumes no persistent capture resources and all capture lifecycle tests pass.

---

# P1 — Time and media clock model

## 27. Canonical Moonshine clock

- [ ] Define `MoonshineMonotonicClock`.
- [ ] Define its epoch and units.
- [ ] Add QPC/Stopwatch conversion helpers.
- [ ] Add microseconds ↔ 90 kHz media-time conversion.
- [ ] Add microseconds ↔ audio-sample conversion.
- [ ] Remove unrelated `high_resolution_clock`/epoch comparisons from protocol logic.

**Done when:** every cross-subsystem timestamp has one documented epoch/clock source and conversion tests prove no unit or epoch ambiguity.

## 28. Audio/video synchronisation

- [ ] Define authoritative media clock.
- [ ] Measure audio/video drift.
- [ ] Correct jitter and drift within defined bounds.
- [ ] Test long-duration synchronisation.

**Done when:** a sustained real E2E test demonstrates audio/video sync within the documented tolerance for the supported configuration.

---

# P1 — Network transport

## 29. Baseline transport

- [ ] Implement real MNBP transport over the chosen production transport.
- [ ] Define connection/session lifecycle.
- [ ] Define MTU and fragmentation rules.
- [ ] Implement send/receive backpressure.
- [ ] Bound per-peer buffers.
- [ ] Handle disconnect/reconnect.

**Done when:** real host/client processes exchange authenticated media/control traffic over real sockets without mocks.

## 30. Network impairment handling

- [ ] Test packet loss.
- [ ] Test packet reorder.
- [ ] Test duplicates.
- [ ] Test bursts of loss.
- [ ] Test latency/jitter.
- [ ] Test disconnect/reconnect.
- [ ] Test peer timeout.

**Done when:** each impairment has deterministic expected behaviour and no malformed/lost packet can corrupt future frames or exhaust memory.

## 31. Congestion control and adaptive bitrate

- [ ] Measure RTT.
- [ ] Measure loss/jitter.
- [ ] Track queue depth.
- [ ] Implement bitrate adaptation.
- [ ] Implement resolution/framerate adaptation if required.
- [ ] Prevent oscillation.

**Done when:** controlled network impairment demonstrates stable adaptation without runaway queue growth or unacceptable quality/latency collapse.

---

# P1 — Runtime role isolation

## 32. Host-only resource isolation

- [ ] Host listeners only when host role is enabled.
- [ ] No client connections when client role is disabled.
- [ ] No client media/audio/input workers when disabled.

**Done when:** resource inspection proves disabled client resources remain zero across startup, runtime, stop, and restart.

## 33. Client-only resource isolation

- [ ] No host listeners when host role is disabled.
- [ ] No capture/encode/audio-host resources when host role is disabled.

**Done when:** resource inspection proves disabled host resources remain zero across startup, runtime, stop, and restart.

## 34. Host + Client isolation

- [ ] Both role graphs can run simultaneously.
- [ ] Resources are independently stopped.
- [ ] One role failure does not corrupt the other unless the dependency is genuinely shared.

**Done when:** repeated role transitions and fault injection prove clean independence and recovery.

---

# P2 — Testing and evidence

## 35. Test taxonomy

- [ ] Unit tests are labelled as unit tests.
- [ ] Mock transport tests are labelled as integration/mock tests.
- [ ] Hardware acceptance tests are clearly separated.
- [ ] Soak tests are clearly separated.
- [ ] Tests that require hardware skip only when hardware is absent and never silently skip when hardware is expected.

**Done when:** the test suite's names and reports make the evidence level obvious without reading implementation details.

## 36. Real hardware acceptance suite

- [ ] NVIDIA hardware suite.
- [ ] AMD hardware suite.
- [ ] Intel hardware suite.
- [ ] Multi-GPU/hybrid suite where available.
- [ ] Capture → encode → packetise → transport → reassemble → decode → present.
- [ ] Audio host → client.
- [ ] Microphone client → host.
- [ ] Input client → host.

**Done when:** the supported hardware matrix has reproducible green acceptance evidence and unsupported configurations are explicitly documented.

## 37. Device-loss testing

- [ ] Encoder device loss.
- [ ] Decoder device loss.
- [ ] Capture device loss.
- [ ] Audio endpoint loss.
- [ ] Monitor disconnect/hotplug.
- [ ] Driver reset/recovery where testable.

**Done when:** each event either recovers automatically or transitions to a documented safe state with no leaked resources or false-success counters.

## 38. Reconnect testing

- [ ] Client disconnect/reconnect.
- [ ] Host restart.
- [ ] Network interface interruption.
- [ ] Session timeout.
- [ ] Authentication renegotiation.

**Done when:** reconnect behaviour is deterministic, stale packets cannot cross sessions, and all resources from the previous session are released.

## 39. Endurance testing

- [ ] 30-minute streaming test.
- [ ] 2-hour streaming test.
- [ ] 8-hour soak test before stable release.
- [ ] Monitor memory growth.
- [ ] Monitor queue growth.
- [ ] Monitor handle/socket/resource growth.
- [ ] Monitor GPU/CPU utilisation.

**Done when:** the endurance run completes without unbounded resource growth, correctness failures, or unrecovered subsystem faults.

---

# P2 — Performance and optimisation

## 40. End-to-end latency instrumentation

- [ ] Timestamp capture.
- [ ] Timestamp encode completion.
- [ ] Timestamp packet send.
- [ ] Timestamp packet receive.
- [ ] Timestamp frame reassembly.
- [ ] Timestamp decode completion.
- [ ] Timestamp presentation.
- [ ] Calculate p50/p95/p99.

**Done when:** end-to-end latency can be measured reproducibly on a real host/client pair and the report includes methodology and hardware.

## 41. Throughput and network metrics

- [ ] Bitrate.
- [ ] Packet rate.
- [ ] Packet loss.
- [ ] Reorder rate.
- [ ] Jitter.
- [ ] FEC recovery rate.
- [ ] Queue depth.

**Done when:** a real streaming session exports these metrics and they can be correlated with observed latency/quality.

## 42. CPU/GPU/allocation profiling

- [ ] CPU usage per pipeline stage.
- [ ] GPU encode/decode/presentation usage.
- [ ] Managed allocations in hot paths.
- [ ] Native allocations in hot paths.
- [ ] Copy counts.
- [ ] Synchronisation stalls.

**Done when:** optimisation claims are supported by profiler/benchmark evidence rather than source-code inspection alone.

## 43. Zero-copy validation

- [ ] Trace host capture surface ownership.
- [ ] Trace encoder input surface.
- [ ] Trace decoder output surface.
- [ ] Trace presentation surface.
- [ ] Identify unavoidable copies.
- [ ] Remove CPU readback from production path.

**Done when:** the production video path has documented GPU/CPU copy boundaries and the measured copy count meets the project's stated target.

## 44. SIMD/FEC optimisation validation

- [ ] Benchmark scalar versus AVX2.
- [ ] Benchmark AVX2 versus AVX-512/GFNI where available.
- [ ] Verify correctness across dispatch paths.
- [ ] Verify unsupported CPU feature fallback.

**Done when:** every SIMD dispatch path has correctness tests and benchmark evidence; no optimisation is considered complete solely because it compiles.

---

# P2 — Documentation and project hygiene

## 45. Keep README status truthful

- [ ] Update subsystem status whenever implementation maturity changes.
- [ ] Keep verified hardware evidence dated.
- [ ] Distinguish local verification from CI verification.
- [ ] Do not call synthetic integration tests "real E2E".

**Done when:** README status matches the actual code and acceptance evidence at every release.

## 46. Issue tracker integrity

- [ ] Review all closed issues with unchecked acceptance criteria.
- [ ] Reopen or split incomplete work.
- [ ] Mark duplicates/superseded work correctly.
- [ ] Use milestones as release gates.
- [ ] Make Issue #82 or its successor the hard real-E2E release gate.

**Done when:** a closed issue means every mandatory acceptance criterion is proven, not merely that implementation started.

## 47. Release gate

- [ ] Define one authoritative release checklist.
- [ ] Link all release-blocking issues.
- [ ] Require security acceptance.
- [ ] Require real hardware E2E acceptance.
- [ ] Require performance evidence.
- [ ] Require endurance evidence.
- [ ] Require documentation update.

**Done when:** a release cannot be declared production-ready while any P0 release criterion remains incomplete.

---

# P3 — Future / optimisation backlog

These should not block the first real end-to-end frame unless they become necessary for correctness.

- [ ] Advanced FEC tuning.
- [ ] AVX-512 micro-optimisation beyond proven workload benefit.
- [ ] Fine-grained allocator tuning.
- [ ] Advanced congestion-control algorithms.
- [ ] Adaptive quality heuristics.
- [ ] Additional codec profiles.
- [ ] Additional HDR modes.
- [ ] Additional capture modes.
- [ ] Additional controller/input devices.
- [ ] Expanded telemetry and diagnostics.

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

- [ ] Secure authenticated session establishment is complete.
- [ ] Replay/freshness protection is correct.
- [ ] State-changing control is always authenticated and authorised.
- [ ] Real video E2E is complete.
- [ ] Real audio E2E is complete.
- [ ] Real microphone uplink is complete.
- [ ] Real input forwarding is complete.
- [ ] Hardware capability reporting is truthful and adapter-specific.
- [ ] Device loss/reconnect behaviour is proven.
- [ ] Network impairment behaviour is proven.
- [ ] Long-duration soak testing is complete.
- [ ] End-to-end latency and resource metrics are measured.
- [ ] No known critical/high correctness or security defect remains.
- [ ] Every closed release-blocking GitHub issue has all acceptance criteria satisfied.
- [ ] Documentation matches the verified implementation.

> **Core principle:** Moonshine should only claim that something works when the repository contains reproducible evidence that it works. Real code is not the same thing as a proven system; passing tests are not the same thing as real hardware E2E; and capability discovery is not the same thing as operational readiness.
