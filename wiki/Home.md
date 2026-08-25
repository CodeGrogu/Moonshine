# Welcome to the Moonshine Wiki

Moonshine is a custom, high-performance Windows PC streaming platform built from the ground up with C# 13 (.NET 9) and C++23. It provides a single application that can operate as a streaming Host, Client, or both simultaneously.

> **Development Status (v0.5.6-alpha)**: Moonshine is in active pre-release development. The backend infrastructure is being built. No end-to-end streaming pipeline is operational yet. See the [Project Status](https://github.com/CodeGrogu/Moonshine#project-status) section of the README for current component maturity.

> **Important**: Moonshine defines its own platform, protocol (MNBP v1), architecture, and implementation. It is not a Moonlight fork, GameStream client, or Sunshine reimplementation. Legacy compatibility code exists in the repository for audit and migration reference only.

---

## Wiki Navigation

### Architecture and Design
- [[Architecture Overview|Architecture-Overview]]: High-level hybrid C# and C++ architecture, data flow, and separation of concerns.
- [[Zero-Allocation Data Plane|Zero-Allocation-Data-Plane]]: Zero-copy ingestion, Span, ReadOnlySequence, and unmanaged memory slabs.
- [[Custom SIMD Galois Field FEC|Custom-SIMD-Galois-Field-FEC]]: Vectorised Reed-Solomon GF(2^8) arithmetic via AVX2 and AVX-512.
- [[Custom Lock-Free SPSC Concurrency|Custom-Lock-Free-SPSC-Concurrency]]: Cacheline-padded atomic ring buffers with acquire-release memory ordering.
- [[Predictive Jitter Buffer|Predictive-Jitter-Buffer]]: Custom predictive frame reassembly algorithms and zero-allocation indexing.
- [[Hardware Video Pipeline|Hardware-Video-Pipeline]]: Direct3D 11/12 and Vulkan Video decoders, DXGI Flip Model, and HDR10 tone mapping.
- [[Audio Engine (WASAPI Exclusive)|Audio-Engine-WASAPI]]: Sub-5ms low-latency audio rendering and Opus packet processing.

### Protocols and Networking
- [[GameStream and Sunshine Protocol|GameStream-Sunshine-Protocol]]: Legacy compatibility protocol documentation (network ports, cryptographic pairing, RTSP orchestration).
- [[Real-Time LAN Host Discovery|Real-Time-Host-Discovery]]: Zero-allocation Multicast DNS (mDNS) and SSDP UPnP host discovery engine.
- [[Cryptographic Pairing Pipeline|Cryptographic-Pairing-Pipeline]]: RSA 2048-bit X.509 certificate generation, PBKDF2/SHA-256 key derivation, and AES-128 challenge-response authentication.
- [[RTSP Stream Control and Dynamic SDP|RTSP-Stream-Control-and-SDP]]: Stateful RTSP client state machine, RFC 4566 SDP offer/answer negotiation, HDR10 static metadata, and dynamic bitrate adaptation announcements.
- [[Zero-Copy UDP Ingestion Pipeline|Zero-Copy-UDP-Ingestion]]: High-throughput UDP datagram receiver, cacheline-aligned PinnedBufferPool, and lock-free C++23 SPSC queue dispatching.
- [[1000Hz Input Subsystem|Input-Subsystem-1000Hz]]: Sub-millisecond raw input polling, atomic delta staging, and binary serialisation.
- [[Dynamic RTCP Congestion Control|Dynamic-RTCP-Congestion-Control]]: Real-time RTCP receiver feedback, EMA loss smoothing, and predictive AIMD bandwidth adaptation.

### Moonshine Host Subsystem
- [[Direct3D Desktop Capture Engine|Direct3D-Desktop-Capture]]: IDXGIOutputDuplication and Windows.Graphics.Capture VRAM frame ingestion.
- [[HDR10 and Dynamic Colour Space Engine|HDR10-Color-Pipeline]]: Display colorimetry extraction, SMPTE ST 2084 PQ curve, and Direct3D GPU colour conversion.
- [[GPU Hardware Video Encoding|GPU-Hardware-Video-Encoding]]: Native NVENC, AMF, and QuickSync low-latency GPU encoders for HEVC/AV1.
- [[NVIDIA NVENC Hardware Pipeline|NVENC-Hardware-Pipeline]]: Dedicated NVENC SDK encoder, P1/P2 ultra-low latency presets, and progressive intra-refresh.
- [[AMD AMF and Intel QuickSync Pipelines|AMF-and-QuickSync-Hardware-Pipelines]]: Dedicated AMD VCN and Intel oneVPL/QSV hardware encoder pipelines.
- [[WASAPI Loopback Audio Engine|WASAPI-Loopback-Audio]]: Low-latency master audio mix capture and multi-channel audio streaming.
- [[Opus Audio Compression Engine|Opus-Audio-Compression]]: Ultra-low latency multi-channel Opus audio compression and multi-stream encoding.
- [[Microphone Passthrough Engine|Microphone-Passthrough-and-Virtual-Sink]]: Client-to-host low-latency microphone audio streaming, jitter buffering, and virtual audio device injection.
- [[Dedicated Virtual Audio Driver|Virtual-Audio-Driver]]: Custom Windows WDK PortCls WaveRT miniport driver exposing Moonshine Audio and Moonshine Microphone endpoints.
- [[Virtual Audio Shared Memory IPC|Virtual-Audio-Shared-Memory-IPC]]: Zero-copy lock-free ring buffer IPC bridge with MMCSS Pro Audio event signalling and DACL security permissions.

### Workflows, Testing, and Performance
- [[Engineering Standards|Engineering-Standards]]: Solo + AI development methodology, proof-of-work gates, and toolchain verification.
- [[CI and GitHub Workflows|Continuous-Integration-and-Workflows]]: CI workflows, sanitisers, AOT trimming verification, and security audits.
- [[Exhaustive Testing Strategy|Exhaustive-Testing-Strategy]]: Unit testing matrix across native SIMD, concurrency, and managed protocols.
- [[Benchmarking and Performance Audit|Benchmarking-and-Performance-Audit]]: Microbenchmarks, BenchmarkDotNet methodology, allocation verification, and profiling workflows.
- [[Developer Setup and Build|Developer-Setup-and-Build]]: Toolchain prerequisites (MSVC, CMake, Ninja, .NET 9 SDK) and automated verification commands.

---

## Performance Design Targets

Moonshine adheres to strict latency and memory design targets (not yet measured end-to-end):

1. **Zero Bytes Managed GC Allocations in Streaming Hot Paths**: Frame ingestion, packet parsing, and FEC processing must never trigger garbage collection.
2. **Lock-Free Cross-Thread Data Flow**: Video and audio frames are passed across threads using lock-free single-producer single-consumer (SPSC) ring buffers padded to 64-byte cache lines.
3. **Hardware-Direct Presentation**: Frame surfaces are presented directly via DXGI Flip Model with sub-frame presentation waitables.
4. **SIMD-Accelerated Parity Recovery**: Reed-Solomon FEC matrix multiplications execute in vectorised 256-bit registers, yielding over twelve times speedup over scalar lookups.
5. **Custom High-Performance Implementations**: Generic third-party libraries are superseded by custom implementations whenever measurable performance gains can be achieved.
