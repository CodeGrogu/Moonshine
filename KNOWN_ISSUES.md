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
- **Test Targets**: 16 native CTest targets passed in the MSVC developer environment.

<!-- VERIFIED: 2026-08-21, via `tools/dotnet_sdk/dotnet.exe test Moonshine.sln -c Release --no-build --no-restore --arch x64` on Windows 11 -->
### Managed .NET 9 Solution
- **Status**: Verified (238 unit tests passed across `Moonshine.Interop.Tests`, `Moonshine.Host.Tests`, `Moonshine.Protocol.Tests`, `Moonshine.Core.Tests`).

### Hardware Video Encoders (NVENC, AMF, QSV)
- **Status**: Prototype on non-GPU CI runners; Verified on dedicated hardware test nodes.
- **Scaffolding Tracking**: Fallback paths for systems lacking physical hardware encoding ASICs are isolated and return unsupported capability flags rather than simulating bitstreams.

### Dedicated Virtual Audio Driver (WaveRT Miniport)
- **Status**: Verified (C-ABI bridge, PortCls WaveRT driver package, and Shared Memory IPC pipeline passing in software test harnesses). Real-device PnP deployment requires WHQL attestation or Windows Test-Signing mode.
