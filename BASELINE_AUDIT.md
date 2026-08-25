# Moonshine Backend Baseline Audit

> **Historical Baseline Snapshot (Recorded: 2026-08-21)**: This document records the original structural audit of the Moonshine repository prior to the implementation of multi-vendor hardware encoder pipelines (NVENC/AMF/QSV), physical Direct3D 11 decoder loopback suites, and the MNBP v1 protocol specification.
>
> **Current Verification Status (2026-08-25)**: Since this baseline was recorded, the repository has expanded from 16 to 25 native CTests and from 254 to 706 passing managed xUnit tests (712 total, 6 skipped for absent hardware). The application composition root (`MoonshineApplication`) remains deliberately fail-closed pending native transport wiring. See [Current Verification Status](#current-verification-status-2026-08-25) below.

<!-- VERIFIED: 2026-08-21, via `scripts/verify_environment.ps1`, `scripts/preflight.ps1`, `ctest --test-dir build/release-avx2 --build-config Release --output-on-failure --no-tests=error`, and `tools/dotnet_sdk/dotnet.exe test Moonshine.sln -c Release --no-build --no-restore --arch x64` on Windows 11 Pro build 26200 -->

## Product Boundary

Moonshine has one Windows executable: `src/Moonshine.Client`, with assembly name `Moonshine`. It selects one role per invocation:

- `Moonshine --role host`
- `Moonshine --role client`
- `Moonshine --role host-client`

The composition root is `Moonshine.App.MoonshineApplication`. It is deliberately fail-closed: every role reports unsupported until the Moonshine-native session-control and media transport designs are implemented. It starts no listener, worker, compatibility handshake, or simulated device path.

## Dependency Graph

```text
Moonshine executable
  -> Moonshine.Host: Host role coordinator, capture, encode, audio
  -> Moonshine.Core: shared lifecycle, security, input, video and audio abstractions
  -> Moonshine.Interop: C# to C++ ABI only
  -> Moonshine.Protocol: shared serialisation and cryptography primitives
  -> Moonshine.Native.dll: Windows device, SIMD, capture, media and audio resources

Compatibility-only code, not reached by MoonshineApplication:
  Moonshine.ClientEngine -> GameStream discovery/pairing -> RTSP -> RTP/RTCP/UDP
```

The `MoonshineClientEngine` compatibility entry point is excluded from compilation by the executable project. The following compatibility modules remain in the repository for audit and migration reference only. They must not be composed by a Host, Client, or Host + Client role: `MoonshineDiscoveryService`, `LiveHostDiscoveryEngine`, `MoonshinePairingManager`, `MoonshineRtspClient`, `MoonshineStreamSession`, `UdpSocketPipeline`, and RTP, RTCP, RTSP, mDNS, SSDP, and GameStream packet codecs. They require extraction to a non-product compatibility assembly before any new transport is added.

## Runtime Inventory and Classification (2026-08-21 Snapshot)

| Area | Resources and boundaries | State | Product disposition |
| --- | --- | --- | --- |
| Single application | Console executable and role selector | Incomplete | Production composition root, fail-closed until native streaming exists. |
| Host coordinator | Host state only, no listener | Incomplete | Reports `Unsupported`; never reports `Running`. |
| Client engine | HTTP pairing, certificate handling, GameStream host query | Incompatible | Compatibility-only, unreachable from the application composition root. |
| Discovery | mDNS and SSDP UDP sockets, HTTP `serverinfo` probes, background task | Incompatible | Compatibility-only, no active product listener. |
| RTSP session | TCP client, request serialiser, RTP and RTCP packet dispatch | Incompatible | Compatibility-only, no active product listener. |
| UDP ingestion | UDP bind, long-running receive worker, pinned pool, native SPSC interop | Incompatible | Compatibility-only because it assumes RTP and GameStream framing. |
| Host audio | WASAPI loopback, Opus encoder, RTP packetiser, virtual-device IPC | Incomplete | Device portions have software tests; the RTP output contract is incompatible with the new transport. |
| Client audio | microphone capture, Opus, RTP backchannel | Incompatible | Not selected by application roles. |
| Desktop capture | DXGI duplication, Windows.Graphics.Capture, D3D texture handles | Prototype | Can expose real device failures. No end-to-end Host media path exists. |
| Hardware encoding | NVENC, AMF, QuickSync, D3D11 hardware encoder ABI | Incomplete | Explicit unsupported results. All synthetic bitstream success paths are blocked. |
| Hardware decoding | D3D11VA and D3D12 Video ABI | Incomplete | Explicit unsupported results and zero capabilities. |
| Presentation | DXGI swapchain ABI | Incomplete | Explicit unsupported result because no retained real `IDXGISwapChain` exists. |
| Native data plane | SPSC ring, Reed-Solomon FEC, jitter buffer | Prototype | Value-tested in isolation, but the existing framing assumes compatibility wire formats. |
| Interop boundary | `LibraryImport` C ABI structs and `Moonshine.Native.dll` | Prototype | ABI tests cover layouts and explicit unsupported media results. |
| Virtual audio driver | WaveRT driver, shared-memory IPC and PnP integration | Prototype | Test-harness verified only; deployment needs Windows driver signing or test-signing. |

## Listeners, Workers, and Device Resources

There are no active product listeners or streaming workers in `MoonshineApplication`. Legacy-only resources are: discovery mDNS and SSDP socket receive tasks, HTTP probe tasks, RTSP TCP client, UDP receiver thread, client microphone audio capture, WASAPI loopback capture, DXGI/WGC capture handles, encoder handles, decoder handles, swapchain handles, virtual audio IPC mappings, and the WaveRT driver. These resources remain disabled by the role selector.

## Baseline Evidence (2026-08-21 Snapshot)

<!-- VERIFIED: 2026-08-21, via `scripts/verify_environment.ps1` on Windows 11 Pro build 26200 -->

- Toolchain: MSVC C++23, CMake 3.31.5, Ninja, CTest, and repository .NET SDK 9.0.317 resolved successfully.

<!-- VERIFIED: 2026-08-21, via `scripts/preflight.ps1` and `tests/test_preflight_fixtures.ps1` on Windows 11 Pro build 26200 -->

- Preflight: 204 source files and 64 documents scanned with zero violations. Fixture regression: 10 passed.

<!-- VERIFIED: 2026-08-21, via `ctest --test-dir build/release-avx2 --build-config Release --output-on-failure --no-tests=error` on Windows 11 Pro build 26200 -->

- Native baseline before remediation: 16 of 16 CTest targets passed.

<!-- VERIFIED: 2026-08-21, via `tools/dotnet_sdk/dotnet.exe test Moonshine.sln -c Release --no-build --no-restore --arch x64 --logger "console;verbosity=minimal"` on Windows 11 Pro build 26200 -->

- Managed baseline before remediation: 254 tests passed: Protocol 61, Core 51, Interop 71, Host 71.

<!-- VERIFIED: 2026-08-21, via existing `BenchmarkDotNet.Artifacts/results/*-report-github.md` results generated with the repository benchmark configuration on Windows 11 build 26200 -->

| Benchmark | Mean | Allocation |
| --- | ---: | ---: |
| UDP pinned-buffer rent and return | 39.73 ns | 0 B |
| UDP datagram processing | 57.19 ns | 0 B |
| RTP span parsing | 1.133 ns | 0 B |
| SPSC enqueue and dequeue | 10.45 ns | 0 B |
| Jitter assembly and pop | 53.35 ns | 0 B |
| SIMD Reed-Solomon FEC recovery | 287.54 ns | 0 B |

These are microbenchmark baselines for isolated algorithms, not streaming throughput or end-to-end latency claims. No real frame, hardware encode, decode, presentation, or host-to-client latency measurement exists.

---

## Current Verification Status (2026-08-25)

<!-- VERIFIED: 2026-08-25, via `scripts/verify_codebase.ps1` on Windows 11 Pro build 26200 -->

> **Verification Provenance**: Verified locally on Windows 11 Pro build 26200 via `scripts/verify_codebase.ps1` on 2026-08-25. Total: **25 CTests** passed (100%), **706 managed xUnit tests** passed (712 total, 6 skipped). GitHub Actions CI status executes independently.

### Progression from 2026-08-21 Baseline to 2026-08-25

| Metric / Area | 2026-08-21 Baseline | 2026-08-25 Current Verification | Progression Detail |
| :--- | :--- | :--- | :--- |
| **Native CTests** | 16 passed (100%) | **25 passed (100%)** | Added 9 test suites: NVENC pipeline & conformance, AMF pipeline & conformance, QSV pipeline & conformance, WGC capture, HDR colorimetry, WASAPI capture & renderer, Opus codec, and Swapchain presenter. |
| **Managed xUnit Tests** | 254 passed | **706 passed (712 total, 6 skipped)** | Expanded across Protocol (102), Core (214), Host (308), Interop (88). 6 tests skipped on test machines lacking physical AMD or Intel GPUs. |
| **Hardware Video Encoding** | Incomplete (Blocked) | **Subsystem Implemented & Tested** | Implemented and hardware-conformance-tested for capability-supported configurations (e.g. NVENC on test host; AMF/QSV capability-gated); not yet integrated into the end-to-end product streaming path. |
| **Protocol Specification** | Legacy RTP/RTSP | **MNBP v1 Specified & Tested** | First-party Moonshine Native Binary Protocol (`docs/PROTOCOL_SPEC_V1.md`) with zero-allocation packet codecs. |
| **Application Boundary** | Fail-Closed | **Fail-Closed (Preserved Invariant)** | `MoonshineApplication` starts no background listeners or workers; roles return unsupported until native transport wiring is complete. |

### Hardware Verification Matrix (2026-08-25 Local Test Run)

> **System GPU Inventory**: NVIDIA GeForce RTX 2060 (`0x10DE`, primary display adapter) and Intel Iris Xe Graphics (`0x8086`, secondary headless adapter) physically installed on test runner.

| Hardware Backend | Physical GPU on Host | Software / Capability-Independent Tests | Physical Bitstream & Loopback Tests | Codec Coverage Detail | Disposition |
| :--- | :---: | :---: | :---: | :--- | :--- |
| **NVIDIA NVENC** | Present (RTX 2060) | Passed | Passed | H.264 & HEVC Main10 passed; AV1 capability-gated on Turing | Physically exercised and verified |
| **AMD AMF** | Absent | Passed | Skipped (3 tests) | Capability-gated on missing AMD hardware | Capability-gated, skipped cleanly |
| **Intel QuickSync (QSV)** | Present (Iris Xe) | Passed | Skipped (3 tests) | Software/ABI tests passed; QSV hardware session uninitialised on test context | Capability-gated, skipped cleanly |
| **Direct3D 11 Hardware** | Present | Passed | Passed | Universal hardware transform fallback | Physically exercised and verified |

## Direct Follow-up Work

1. Extract the listed compatibility-only modules into a separate assembly, then remove it from the product executable dependency graph.
2. Specify and implement a Moonshine-native authenticated control protocol and media framing before enabling listeners or UDP workers.
3. Integrate each hardware SDK or Windows media API with physical-device capability probes and frame-value validation before changing any unsupported result.
4. Record real host-to-client throughput, allocation, and latency measurements using physical capture, encode, transport, decode, and presentation.
