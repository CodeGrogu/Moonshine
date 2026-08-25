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
* **Status**: `Pending`
* **Priority**: `P1`
* **Prerequisites**: None
* **Scope**: Native `Moonshine.Native.dll` and Managed `Moonshine.Interop`
* **Objective**: Enhance `moonshine_d3d11_cross_adapter_copy` to use `IDXGIResource1::CreateSharedHandle` with `D3D11_RESOURCE_MISC_SHARED_NTHANDLE` and `ID3D11Device1::OpenSharedResource1` for hardware-accelerated zero-copy cross-adapter VRAM migration when supported by the underlying GPU drivers, retaining the CPU staging copy as a robust fallback.
* **Acceptance Criteria**:
  - [ ] Implement `moonshine_d3d11_create_shared_nt_handle` and `moonshine_d3d11_open_shared_nt_handle` in `moonshine_native_api.cpp`.
  - [ ] Support keyed mutex synchronization (`IDXGIKeyedMutex`) for race-free cross-device access.
  - [ ] Add unit and loopback tests in `HardwareVideoEncoderConformanceTests.cs`.
  - [ ] 100% CTest and xUnit pass rate with zero preflight violations.
* **Evidence**:
  - <!-- PENDING: Provenance tag will be added upon completion -->

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
* **Status**: `Pending`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Native/src/audio/` and `tests/Moonshine.Native.Tests/test_wasapi_capture.cpp`
* **Objective**: Stress-test WASAPI audio loopback capture, high-quality audio resampling (44.1 kHz <-> 48 kHz <-> 96 kHz), and virtual audio driver IPC ring buffers under continuous buffer underrun/overrun simulation and endpoint disconnection events.
* **Acceptance Criteria**:
  - [ ] Stress-test WASAPI capture and renderer lifecycle transitions across dynamic sample rate shifts.
  - [ ] Verify zero heap allocations in audio DSP mixing and resampling routines.
  - [ ] 100% CTest pass rate across audio test suites (`test_wasapi_capture`, `test_opus_encoder`, `test_opus_decoder`, `test_wasapi_renderer`, `test_audio_resampler`, `test_virtual_audio_driver`, `test_virtual_audio_ipc`).
* **Evidence**:
  - <!-- PENDING: Provenance tag will be added upon completion -->

---

### [TODO-004] Client Presentation Pipeline HDR10 Tone Mapping & Swapchain Flip Model Discard
* **Status**: `Pending`
* **Priority**: `P2`
* **Prerequisites**: None
* **Scope**: `src/Moonshine.Native/src/video/` and `src/Moonshine.Client/`
* **Objective**: Harden the Direct3D 11 / DXGI swapchain presenter for HDR10 (`DXGI_FORMAT_R10G10B10A2_UNORM`) presentation, BT.2020 colorimetry metadata configuration, SMPTE ST 2086 mastering display metadata, and low-latency `DXGI_SWAP_EFFECT_FLIP_DISCARD` presenter pacing.
* **Acceptance Criteria**:
  - [ ] Verify HDR10 metadata injection (`DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020`).
  - [ ] Verify seamless SDR fallback when display HDR is disabled.
  - [ ] Add CTest and managed tests verifying swapchain present timing and occlusion handling.
* **Evidence**:
  - <!-- PENDING: Provenance tag will be added upon completion -->
