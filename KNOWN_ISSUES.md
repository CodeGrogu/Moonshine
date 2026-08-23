# Moonshine Known Issues & Operational Status

This document tracks known platform limitations, environment requirements, and current scaffolding states in accordance with Rule 5 and Rule 8 of `STANDARDS.md`.

---

## 1. Environment & Build Requirements

- **Plain PowerShell Toolchain Resolution**:
  - When running from a standard (non-developer) Windows PowerShell terminal, `cl.exe` will fail to resolve standard C++ headers (`<iostream>`, `<cstdint>`, `<stdint.h>`) because `INCLUDE` and `LIB` environment variables are unset.
  - **Workaround & Solution**: Execute `scripts/verify_environment.ps1` or run inside the **Developer PowerShell for VS 2022** environment. `scripts/verify_codebase.ps1` automatically probes and self-initialises the MSVC developer shell.

---

## 2. Component Maturity Status

<!-- VERIFIED: 2026-08-21, via `ctest --test-dir build/release-avx2 --build-config Release --output-on-failure --no-tests=error` on Windows 11 with MSVC C++23 -->
### Native C++23 Engine (Moonshine.Native)
- **Status**: Verified in Developer Environment / CI.
- **Test Targets**: 18 native CTest targets passed in the MSVC developer environment.

<!-- VERIFIED: 2026-08-21, via `scripts/verify_codebase.ps1` on Windows 11 -->
### Managed .NET 9 Solution
- **Status**: Verified (289 unit and integration tests passed across `Moonshine.Protocol.Tests` (71), `Moonshine.Core.Tests` (85), `Moonshine.Interop.Tests` (76), `Moonshine.Host.Tests` (57)).

### Hardware Video Encoders (NVENC, AMF, QuickSync, D3D11)
- **Status**: Substantially Implemented / Live Capability Discovery with Fail-Closed Semantics.
- **Hardware Capability Discovery**: Implemented. Dynamic runtime loaders (`nvEncodeAPI64.dll`, `amfrt64.dll`, `vpl.dll`/`mfx64.dll`, and Windows Hardware MFTs) query live GPU driver capabilities on physical Direct3D 11 adapters and fail closed on missing/unsupported hardware without synthetic fallbacks.
- **Hardware Encoder Abstraction**: Implemented. Multi-vendor unification, dynamic rate-control reconfiguration, keyframe requests, latency measurement, and zero-allocation managed wrappers are operational.
- **Physical Bitstream Encoding**: In Progress / Fail-Closed. Physical GPU encode session lifecycle and bitstream extraction per vendor ASIC operate in fail-closed states pending complete downstream hardware pipeline wiring.
- **End-to-End Pipeline**: Not implemented yet (staged for downstream media packetisation and session orchestrator in Issues #70, #79, #82).

### Direct3D 11 / 12 Hardware Video Decoder
- **Status**: Substantially Implemented / Live Capability Discovery & DXVA Profile Negotiation with Fail-Closed Semantics.
- **Hardware Capability Discovery**: Implemented. Live queries probe `ID3D11VideoDevice` profile GUIDs (H.264, HEVC Main, HEVC Main10 10-bit HDR, AV1) and D3D12 video decode feature support on physical GPU adapters, rejecting software WARP rasterization.
- **Decoder Abstraction & Surface Retention**: Implemented. `MoonshineVideoPipeline` and `HardwareVideoDecoderPipeline` provide microsecond latency tracking, GPU-resident texture extraction for swapchain presentation, dynamic resolution reset, and zero GC allocations on the decode hot path.
- **Physical Bitstream Decoding**: In Progress / Fail-Closed. Real bitstream buffer submission and device loss recovery are operational on supported physical GPU hardware and fail closed gracefully without synthetic success paths.

### Dedicated Virtual Audio Driver (WaveRT Miniport)
- **Status**: Prototype (Rule 8). The PortCls WaveRT driver package source (`drivers/audio/`), user-mode C-ABI bridge (`VirtualAudioDriverController`), and Shared Memory IPC pipeline (`VirtualAudioIpcBridge`) with strict DACL kernel security are fully implemented and verified in software test suites. Compilation of the binary `MoonshineAudio.sys` driver package requires the Windows Driver Kit (WDK), and live Windows PnP installation requires Windows Test-Signing mode (`bcdedit /set TESTSIGNING ON`) or WHQL production signing. Full architectural documentation and deployment procedures are registered in `docs/AUDIO_DRIVER.md`.

### GameStream/Sunshine Video Packet Interoperability
- **Status**: Prototype. The RTP ingestion path now parses the documented raw packet layout: RTP, four reserved bytes, and the 16-byte `NV_VIDEO_PACKET` header. It preserves the actual stream packet index and keeps packets out of `JitterBuffer` until a protocol-aware frame/FEC assembly stage derives packet counts.
- **Outstanding Proof**: Capture and replay a real unencrypted Sunshine or GameStream video datagram, then validate frame/FEC assembly against Moonlight before classifying this path as Interop-verified.
