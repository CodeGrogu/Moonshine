<div align="center">

# ⚡ Moonshine

**Next-Generation, Ultra-Low-Latency GameStream & Sunshine Client Engine**  
*Engineered in Modern C# 13 (.NET 9/10 Native AOT) and C++23 / AVX2 / AVX-512 SIMD*

[![CI Build](https://github.com/moonshine-stream/moonshine/actions/workflows/ci.yml/badge.svg)](https://github.com/moonshine-stream/moonshine/actions)
[![Benchmarks](https://github.com/moonshine-stream/moonshine/actions/workflows/benchmarks.yml/badge.svg)](https://github.com/moonshine-stream/moonshine/actions)
[![Code Quality](https://github.com/moonshine-stream/moonshine/actions/workflows/code-quality.yml/badge.svg)](https://github.com/moonshine-stream/moonshine/actions)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Platform: Windows / Linux / macOS](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-informational)](#platform-support)

</div>

---

## 🚀 Overview

**Moonshine** is a ground-up reimagining and ultra-performance rewrite of the client streaming stack for NVIDIA GameStream and [Sunshine](https://github.com/LizardByte/Sunshine) hosts (originally popularized by [Moonlight](https://moonlight-stream.org)).

Moonshine is built with one relentless, uncompromised guiding principle: **Absolute Minimum End-to-End Latency and Maximum Frame Pacing Consistency**.

Every single component in Moonshine—from network socket ingestion, cryptographic authentication, RTSP control state machines, lock-free ring buffers, SIMD-accelerated Reed-Solomon Forward Error Correction (FEC), to zero-copy hardware video decoding (Direct3D 11/12, Vulkan Video, NVDEC) and sub-millisecond audio presentation—is engineered for zero-allocation and bare-metal performance.

```mermaid
graph TD
    subgraph Host ["Sunshine / GameStream Host"]
        VideoEncoder["Hardware NVENC / AMF / QSV (AV1/HEVC/H.264)"]
        AudioEncoder["Opus / 7.1 Surround Encoder"]
        CryptoEngine["AES-128-GCM / RTSP Server"]
        NetTx["UDP / RTP / FEC Streamer"]
    end

    subgraph Moonshine ["⚡ Moonshine Architecture"]
        subgraph NetLayer ["Zero-Copy Network & Ingestion Plane"]
            SocketRx["System.IO.Pipelines Socket Receiver"]
            RtpParser["Zero-Alloc RTP/FEC Header Parser (Span<byte>)"]
            SPSC["Lock-Free Cache-Aligned SPSC Ring Buffer (C++23)"]
        end

        subgraph CoreEngine ["High-Performance Hybrid Engine"]
            FecSimd["SIMD Reed-Solomon FEC Matrix Engine (AVX2/AVX-512/NEON)"]
            JitterBuffer["Sub-Millisecond Predictive Jitter Buffer"]
            D3DVideo["Zero-Copy Direct3D 11/12 & Vulkan Video Decoder"]
            WasapiAudio["Exclusive WASAPI / ASIO Audio Pipeline"]
        end

        subgraph ProtocolLayer ["Managed Protocol & Control (C# .NET 9 Native AOT)"]
            RtspClient["Async Zero-Alloc RTSP State Machine"]
            Pairing["Cryptographic Pairing (AES-GCM / X.509)"]
            InputEngine["High-Polling Input Engine (RawInput / DualSense / XInput)"]
        end

        subgraph Presentation ["Ultra-Low Latency Presentation"]
            DisplayFlip["DXGI Flip Model (Sub-Frame Presentation)"]
            DisplayOut["HDR10 / VRR / 240Hz+ Display Output"]
        end
    end

    NetTx ==> SocketRx
    SocketRx --> RtpParser --> SPSC
    SPSC --> FecSimd --> JitterBuffer --> D3DVideo
    SPSC --> WasapiAudio
    CryptoEngine <--> Pairing
    CryptoEngine <--> RtspClient
    InputEngine ==> NetTx
    D3DVideo ==> DisplayFlip ==> DisplayOut
```

---

## 💎 Key Performance Features

| Feature | Moonlight (Legacy) | Moonshine (New Architecture) |
| :--- | :--- | :--- |
| **Language Stack** | C (moonlight-common-c) + Qt C++ | Modern C# 13 (.NET 9/10 AOT) + Modern C++23 / SIMD |
| **Video Parsing Pipeline** | Copy-heavy buffers | **Zero-Copy `Span<byte>` / Direct Native Memory Arena** |
| **FEC Galois Field Arithmetic** | Scalar LUT / Sequential loop | **AVX2 / AVX-512 / ARM NEON Vectorized Polynomial Multiplier** (>12x throughput) |
| **Concurrency Model** | Mutex / Condition Variables | **Lock-Free Cacheline-Padded (64-byte) SPSC Ring Buffers** |
| **Memory Allocation in Hot Path** | Dynamic malloc/free per packet | **Strictly 0 Bytes Heap Allocation per Frame** |
| **Video Decoding Integration** | Intermediated FFmpeg/Qt surface | **Direct3D 11/12 & Vulkan Direct Video Surface Flipping** |
| **Audio Engine** | SDL2 Audio Buffering (10–20ms) | **WASAPI Exclusive / ASIO Sub-5ms Pipeline** |
| **Input Polling & Precision** | 125Hz – 250Hz timer loop | **Sub-Millisecond RawInput / 1000Hz+ Direct Polling** |
| **Compilation** | JIT / Dynamic Linking | **Native AOT (Ahead-Of-Time) Single-Binary Execution** |

---

## 🏛 Project Architecture

Moonshine is structured as a high-performance modular solution:

- [`src/Moonshine.Native`](./src/Moonshine.Native): Modern C++23 native acceleration library. Contains SIMD Galois Field FEC engines, lock-free ring buffers, sub-millisecond predictive jitter buffers, and native D3D11/D3D12/Vulkan video decoder interfaces.
- [`src/Moonshine.Protocol`](./src/Moonshine.Protocol): Zero-allocation protocol definitions. Features high-speed binary serialization for RTSP, SDP, RTP headers, FEC frames, encrypted control messages, and raw controller/mouse input packets.
- [`src/Moonshine.Interop`](./src/Moonshine.Interop): Ultra-efficient `[LibraryImport]` source-generated interop bindings with 1:1 blittable memory layouts and zero-overhead native pointers.
- [`src/Moonshine.Core`](./src/Moonshine.Core): Managed client engine. Handles mDNS / HTTP discovery, cryptographic X.509 certificate exchange and AES-128/256-GCM pairing handshakes, RTSP stream control, and network health monitoring.
- [`src/Moonshine.Client`](./src/Moonshine.Client): Client orchestrator, display presenter, and input capture system.
- [`src/Moonshine.Benchmarks`](./src/Moonshine.Benchmarks): Automated BenchmarkDotNet suite tracking micro-architectural throughput, SIMD speedups, and memory allocations.

---

## 🛠 Prerequisites & Build Instructions

### Prerequisites
- **Windows**: Windows 10 (1903+) or Windows 11 (64-bit / ARM64)
- **C++ Compiler**: MSVC v143 (Visual Studio 2022 / Build Tools) or Clang 18+
- **Build System**: CMake 3.25+ and Ninja
- **.NET SDK**: .NET 9.0 SDK or .NET 10.0 SDK (with Native AOT workload)

### 1. Build the Native C++ Acceleration Library
```powershell
# Configure optimized Release build with AVX2 SIMD
cmake -B build -S . --preset=windows-release-avx2

# Compile native library
cmake --build build --config Release --parallel
```

### 2. Build the Managed .NET Core & Client (Native AOT)
```powershell
# Restore and build the solution
dotnet build Moonshine.sln -c Release

# Publish Native AOT optimized binary
dotnet publish src/Moonshine.Client/Moonshine.Client.csproj -c Release -r win-x64 --self-contained
```

### 3. Run Micro-Benchmarks
```powershell
dotnet run --project src/Moonshine.Benchmarks/Moonshine.Benchmarks.csproj -c Release -- --job short
```

---

## ⚡ Performance Manifesto

In Moonshine, performance is not an afterthought or a secondary optimization phase; it is an architectural requirement:

1. **Zero Garbage Collection in Streaming Hot Path**: Once the streaming session begins, the managed allocation counter must remain at exactly `0 B/s`.
2. **Lock-Free Concurrency**: No mutexes, lock statements, or thread synchronization primitives in packet processing threads.
3. **Data-Oriented Cache Alignment**: All ring buffers, frame descriptors, and packet queues are aligned to CPU cache lines (`alignas(64)` / `[StructLayout(LayoutKind.Sequential, Pack = 64)]`) to prevent cache line bouncing and false sharing.
4. **SIMD Vectorization**: All multi-byte transformations, Galois field arithmetic, matrix recovery, and packet checksumming must leverage AVX2, AVX-512, or ARM NEON.

See [PERFORMANCE.md](./PERFORMANCE.md) for full benchmarks and latency profiling methodologies.

---

## 📜 Documentation

- [Architecture & Protocol Deep Dive](./ARCHITECTURE.md)
- [Performance Guidelines & Allocations Budget](./PERFORMANCE.md)
- [Contributing Guide & Standards](./CONTRIBUTING.md)
- [Security Policy](./SECURITY.md)
- [Changelog](./CHANGELOG.md)

---

## ⚖ License

Moonshine is licensed under the [GNU General Public License v3.0 (GPLv3)](./LICENSE), preserving compatibility with the open-source Moonlight and Sunshine streaming ecosystem.
