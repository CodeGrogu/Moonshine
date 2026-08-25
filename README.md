<div align="center">

# Moonshine

**A Custom, High-Performance Windows PC Streaming Platform**

One application. Host, Client, or both. Built from the ground up with C# and C++ for low-latency streaming, bidirectional audio, remote host control, and Windows-native device integration.

[![Version](https://img.shields.io/badge/version-0.5.6--alpha-orange)](https://github.com/CodeGrogu/Moonshine/releases)
[![Status](https://img.shields.io/badge/status-Pre--Release%20Development-yellow)](#project-status)
[![CI Build](https://github.com/CodeGrogu/Moonshine/actions/workflows/ci.yml/badge.svg)](https://github.com/CodeGrogu/Moonshine/actions)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011%20x64-0078D4)](#platform-and-scope)

</div>

---

## Table of Contents

- [Project Status](#project-status)
- [What is Moonshine?](#what-is-moonshine)
- [What Moonshine Is Not](#what-moonshine-is-not)
- [Features and Architecture](#features-and-architecture)
- [Runtime Role Model](#runtime-role-model)
- [Technology Stack](#technology-stack)
- [Project Structure](#project-structure)
- [Platform and Scope](#platform-and-scope)
- [Getting Started](#getting-started)
- [Roadmap](#roadmap)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [Licence](#licence)

---

## Project Status

> **Moonshine is in active pre-release development (v0.5.6-alpha).** The backend infrastructure is being built. No end-to-end streaming pipeline is operational yet.

| Area | Status | Detail |
| :--- | :---: | :--- |
| Application shell and role selector | Implemented | Fail-closed by design until native streaming is complete |
| Moonshine Native Binary Protocol (MNBP v1) | Specified | Wire contract defined and codec-tested, not yet wired to transport |
| Native C++23 engine (FEC, SPSC, jitter buffer) | Verified | 25 CTests passed (verified 2026-08-25) |
| Managed .NET 9 test suite | Verified | 706 xUnit tests passed (712 total, 6 skipped for absent hardware, verified 2026-08-25) |
| Hardware encoder discovery (NVENC, AMF, QuickSync) | Implemented | Live capability probing, fail-closed on missing hardware |
| Hardware encoder bitstream output | In Progress | Fail-closed pending downstream pipeline |
| Desktop capture (DXGI/WGC) | Prototype | Can expose real device failures, no end-to-end path |
| Hardware video decoding (D3D11VA/D3D12) | Incomplete | Capability discovery and profile negotiation implemented |
| Audio engine (WASAPI Exclusive) | Prototype | Subsystem tested in isolation |
| Virtual audio driver (WaveRT) | Prototype | Requires WDK and driver signing for deployment |
| End-to-end host-to-client streaming | Not Started | Requires MNBP transport, encoder, and packetiser integration |

---

## What is Moonshine?

**Moonshine is one custom Windows application, written in C# and C++, for high-performance remote PC streaming and interaction.**

A user installs the same Moonshine application on a PC and selects how that installation operates:

- **Host only**: the application exposes the local PC as a Moonshine streaming host.
- **Client only**: the application connects to another Moonshine host as a client.
- **Host + Client**: the same application enables both roles simultaneously.

These are **runtime roles of one application**, not separate Host and Client products.

Moonshine is designed as its **own platform, protocol, architecture, and implementation**. Its wire formats, media pipeline, device integration, security model, and performance architecture are designed specifically for Moonshine.

---

## What Moonshine Is Not

- **Not a Moonlight fork or replacement.** Moonshine does not share Moonlight's codebase, protocol, or architecture.
- **Not a GameStream or Sunshine client.** Legacy compatibility code exists in the repository for reference and audit, but it is classified as Incompatible and unreachable from the production composition root.
- **Not a reimplementation of any existing streaming platform.** Moonshine may independently use techniques that are technically useful, but it defines its own protocol (MNBP v1), transport, and media pipeline.

---

## Features and Architecture

### PC Streaming (Design Target)

```text
Host PC                                        Client PC
  |                                               |
  +-- Desktop / application capture                |
  +-- Video encoding (NVENC / AMF / QuickSync)     |
  +-- Host audio capture (WASAPI)                  |
  |                                               |
  +=========== Moonshine Transport ==============+
  |           (MNBP v1 over UDP/QUIC)             |
  |                                               |
  |                +-- Video decode + rendering ---+
  |                +-- Audio decode + playback ----+
  |                +-- Microphone backchannel -----+
  +-- Virtual microphone injection <--------------+
```

### Application Architecture

Moonshine has three major backend planes:

```text
                         Moonshine Application
                                  |
              +-------------------+-------------------+
              |                   |                   |
              v                   v                   v
         Host Role           Client Role          Shared Core
              |                   |                   |
              +----------+-------+----------+--------+
                         |                  |
                         v                  v
                    Media Plane        Control Plane
                         |                  |
                         +---------+--------+
                                   v
                              Device Plane
```

### Core Capabilities (Implemented or In Progress)

- **Moonshine Native Binary Protocol (MNBP v1)**: First-party control and media wire protocol with zero-allocation serialisation. Legacy RTP, RTSP, RTCP, and GameStream code remains in the repository only for compatibility reference and is deliberately excluded from the production composition root.
- **SIMD Reed-Solomon FEC**: Vectorised Galois Field GF(2^8) arithmetic via AVX2 and AVX-512 GFNI.
- **Lock-free SPSC ring buffers**: Cacheline-padded (64-byte aligned) atomic queues for cross-thread media flow.
- **Predictive jitter buffer**: Sub-millisecond frame reassembly without dynamic allocations.
- **Multi-vendor hardware encoding**: NVENC, AMF, and QuickSync capability discovery and session lifecycle.
- **Desktop capture**: DXGI OutputDuplication and Windows.Graphics.Capture.
- **Audio engine**: WASAPI Exclusive mode, Opus compression, bidirectional microphone passthrough.
- **Virtual audio driver**: Custom WaveRT miniport driver for dedicated Moonshine audio endpoints.
- **HDR10 colour pipeline**: Display colorimetry extraction and SMPTE ST 2084 PQ curve support.

### Verified Performance Baselines

These are **isolated microbenchmark** results, not end-to-end streaming measurements:

| Subsystem | Mean Latency | Allocation |
| :--- | ---: | ---: |
| UDP pinned-buffer rent/return | 39.73 ns | 0 B |
| UDP datagram processing | 57.19 ns | 0 B |
| RTP span parsing | 1.133 ns | 0 B |
| SPSC enqueue/dequeue | 10.45 ns | 0 B |
| Jitter assembly/pop | 53.35 ns | 0 B |
| SIMD Reed-Solomon FEC recovery | 287.54 ns | 0 B |

### Hardware Verification Matrix (2026-08-25 Local Test Run)

> **Verification Provenance**: Verified locally on Windows 11 Pro build 26200 via `scripts/verify_codebase.ps1` on 2026-08-25. Total: **25 CTests** passed (100%), **706 managed xUnit tests** passed (712 total, 6 skipped). GitHub Actions CI workflows execute independently.
>
> **System GPU Inventory**: NVIDIA GeForce RTX 2060 (`0x10DE`, primary display adapter) and Intel Iris Xe Graphics (`0x8086`, secondary headless adapter) physically installed.

| Hardware Backend | Physical GPU on Host | Software / Capability-Independent Tests | Physical Bitstream & Loopback Tests | Codec Coverage Detail | Disposition |
| :--- | :---: | :---: | :---: | :--- | :--- |
| **NVIDIA NVENC** | Present (RTX 2060) | Passed | Passed | H.264 & HEVC Main10 passed; AV1 capability-gated on Turing | Physically exercised and verified |
| **AMD AMF** | Absent | Passed | Skipped (3 tests) | Capability-gated on missing AMD hardware | Capability-gated, skipped cleanly |
| **Intel QuickSync (QSV)** | Present (Iris Xe) | Passed | Skipped (3 tests) | Software/ABI tests passed; QSV hardware session uninitialised on test context | Capability-gated, skipped cleanly |
| **Direct3D 11 Hardware** | Present | Passed | Passed | Universal hardware transform fallback | Physically exercised and verified |

---

## Runtime Role Model

Role selection is an architectural resource boundary, not merely a UI setting.

| Mode | Host Role | Client Role | Host Listeners | Client Connections |
| :--- | :---: | :---: | :---: | :---: |
| **Host only** | On | Off | On | Off |
| **Client only** | Off | On | Off | On |
| **Host + Client** | On | On | On | On |

A disabled role must not initialise its listeners, sockets, capture sessions, decoders/encoders, audio endpoints, device interfaces, large media buffers, background workers, or other persistent role-specific resources.

---

## Technology Stack

| Layer | Language | Responsibilities |
| :--- | :--- | :--- |
| **Application and Orchestration** | C# 13 / .NET 9 | Session management, protocol orchestration, configuration, authentication, networking coordination |
| **Native Acceleration** | C++23 / MSVC | SIMD processing, lock-free queues, buffer management, hardware media backends, Direct3D integration |
| **Interop Boundary** | C-ABI | `[LibraryImport]` source-generated P/Invoke with strict blittable layouts |
| **Build System** | CMake + Ninja (native), MSBuild (.NET) | Cross-configuration builds with AVX2/AVX-512 dispatch |

---

## Project Structure

```
Moonshine/
+-- src/
|   +-- Moonshine.Native/       C++23 native engine (FEC, SPSC, media, devices)
|   +-- Moonshine.Protocol/     Wire protocol definitions and codecs
|   +-- Moonshine.Interop/      C#-to-C++ interoperability layer
|   +-- Moonshine.Core/         Shared session logic, security, orchestration
|   +-- Moonshine.Host/         Host-role capture, encoding, audio, streaming
|   +-- Moonshine.Client/       Client-role receiving, decoding, rendering
|   +-- Moonshine.Benchmarks/   BenchmarkDotNet performance suite
+-- tests/                      xUnit and CTest suites
+-- docs/                       Protocol spec, benchmarks, driver docs
+-- wiki/                       GitHub wiki source files
+-- drivers/                    Virtual audio driver source
+-- scripts/                    Build, verification, and preflight scripts
+-- .github/                    CI workflows, issue templates, PR template
```

---

## Platform and Scope

Moonshine is focused exclusively on **Windows 11 x64 PCs** (build 22000 and later).

The platform focus enables deep optimisation for:

- Direct3D 11/12 and DXGI
- Windows Audio Session API (WASAPI)
- Win32 input and device integration
- Modern x64 CPUs (AVX2/AVX-512)
- NVIDIA, AMD, and Intel GPUs

Cross-platform support is not a current goal.

---

## Getting Started

### Prerequisites

- Windows 11 (build 22000 or later), x64
- Visual Studio 2022 with C++23 (MSVC v143+)
- .NET 9 SDK
- CMake 3.28+ and Ninja

### Build

```powershell
# Verify toolchain
powershell -ExecutionPolicy Bypass -File .\scripts\verify_environment.ps1

# Build native engine
cmake --preset release-avx2
cmake --build build/release-avx2 --config Release

# Build managed solution
dotnet build Moonshine.sln -c Release

# Run all tests
powershell -ExecutionPolicy Bypass -File .\scripts\verify_codebase.ps1
```

> **Note**: Moonshine does not produce a runnable streaming application yet. The application shell starts in fail-closed mode. See [Project Status](#project-status).

---

## Roadmap

Development progress is tracked via [GitHub Issues](https://github.com/CodeGrogu/Moonshine/issues).

| Milestone | Target |
| :--- | :--- |
| v0.5.6-alpha | Current: backend infrastructure, protocol spec, hardware encoder discovery |
| v1.0.0-alpha | First functional end-to-end streaming (host capture to client render) |
| v1.0.0-beta | Multi-vendor GPU support, audio pipeline, remote host control |
| v1.0.0 | Stable release with documented performance characteristics |

---

## Documentation

| Document | Description |
| :--- | :--- |
| [Wiki](https://github.com/CodeGrogu/Moonshine/wiki) | Comprehensive technical documentation |
| [Architecture](./ARCHITECTURE.md) | Technical architecture and protocol pipelines |
| [Protocol Spec (MNBP v1)](./docs/PROTOCOL_SPEC_V1.md) | Moonshine Native Binary Protocol wire format |
| [Engineering Standards](./STANDARDS.md) | Solo + AI development methodology |
| [Performance Guidelines](./PERFORMANCE.md) | Latency budgets and allocation targets |
| [Benchmarks](./docs/BENCHMARKS.md) | Verified BenchmarkDotNet results |
| [Known Issues](./KNOWN_ISSUES.md) | Component maturity status and limitations |
| [Baseline Audit](./BASELINE_AUDIT.md) | Current evidence boundaries and runtime inventory |
| [Changelog](./CHANGELOG.md) | Version history |
| [Contributing](./CONTRIBUTING.md) | Contribution guidelines |
| [Security](./SECURITY.md) | Vulnerability reporting |
| [Code of Conduct](./CODE_OF_CONDUCT.md) | Community standards |

---

## Contributing

Contributions are welcome. Moonshine enforces strict performance, testing, and formatting standards. Please read [CONTRIBUTING.md](./CONTRIBUTING.md) before submitting a pull request.

Key requirements:

- Zero managed allocations in streaming hot paths
- British English in all text
- All tests passing (`scripts/verify_codebase.ps1`)
- BenchmarkDotNet results for any hot path changes

---

## Licence

Moonshine is licensed under the [GNU General Public License v3.0 (GPLv3)](./LICENSE).
