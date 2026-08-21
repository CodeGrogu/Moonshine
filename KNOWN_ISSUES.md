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
- **Test Targets**: 17 native CTest targets passed in the MSVC developer environment.

<!-- VERIFIED: 2026-08-21, via `scripts/verify_codebase.ps1` on Windows 11 -->
### Managed .NET 9 Solution
- **Status**: Verified (234 unit and integration tests passed across `Moonshine.Protocol.Tests` (71), `Moonshine.Core.Tests` (64), `Moonshine.Interop.Tests` (55), `Moonshine.Host.Tests` (44)).

### NVIDIA NVENC Hardware Video Encoder
- **Status**: Prototype / Fail-Closed.
- **Scaffolding Tracking**: Operates in an explicit fail-closed unsupported state; physical NVIDIA Video Codec SDK link libraries and driver integrations are planned for dedicated hardware driver integration.

### AMD AMF Hardware Video Encoder
- **Status**: Prototype / Fail-Closed.
- **Scaffolding Tracking**: Operates in an explicit fail-closed unsupported state; physical AMD Advanced Media Framework SDK link libraries and driver integrations are planned for dedicated hardware driver integration.

### Intel QuickSync Hardware Video Encoder
- **Status**: Prototype / Fail-Closed.
- **Scaffolding Tracking**: Operates in an explicit fail-closed unsupported state; physical Intel oneVPL / Media SDK link libraries and driver integrations are planned for dedicated hardware driver integration.

### Direct3D 11 / 12 Hardware Video Decoder
- **Status**: Prototype / Fail-Closed.
- **Scaffolding Tracking**: Decoder frame submission and capability queries operate in an explicit fail-closed unsupported state; physical Direct3D 11 Video Accelerator (`ID3D11VideoDecoder`) and D3D12 bitstream decode buffer submission are staged for downstream driver integration.

### Dedicated Virtual Audio Driver (WaveRT Miniport)
- **Status**: Verified (C-ABI bridge, PortCls WaveRT driver package, and Shared Memory IPC pipeline passing in software test harnesses). Real-device PnP deployment requires WHQL attestation or Windows Test-Signing mode.

### GameStream/Sunshine Video Packet Interoperability
- **Status**: Prototype. The RTP ingestion path now parses the documented raw packet layout: RTP, four reserved bytes, and the 16-byte `NV_VIDEO_PACKET` header. It preserves the actual stream packet index and keeps packets out of `JitterBuffer` until a protocol-aware frame/FEC assembly stage derives packet counts.
- **Outstanding Proof**: Capture and replay a real unencrypted Sunshine or GameStream video datagram, then validate frame/FEC assembly against Moonlight before classifying this path as Interop-verified.
