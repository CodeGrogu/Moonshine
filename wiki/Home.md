# Welcome to the Moonshine Wiki

Moonshine is a next-generation, ultra-low-latency game streaming client engineered from the ground up to replace Moonlight. Designed for maximum throughput and minimum frame latency, Moonshine combines C# 13 (.NET 9 Native AOT) for high-level protocol orchestration with a modern C++23 AVX2 and AVX-512 SIMD engine for the low-level data plane.

---

## Wiki Navigation

### Architecture and Design
- [[Architecture Overview|Architecture-Overview]]: High-level hybrid C# and C++ architecture, data flow, and separation of concerns.
- [[Zero-Allocation Data Plane|Zero-Allocation-Data-Plane]]: Zero-copy ingestion, Span, ReadOnlySequence, and unmanaged memory slabs.
- [[Custom SIMD Galois Field FEC|Custom-SIMD-Galois-Field-FEC]]: Vectorised Reed-Solomon GF(2^8) arithmetic via AVX2, AVX-512, and ARM NEON.
- [[Custom Lock-Free SPSC Concurrency|Custom-Lock-Free-SPSC-Concurrency]]: Cacheline-padded atomic ring buffers with acquire-release memory ordering.
- [[Predictive Jitter Buffer|Predictive-Jitter-Buffer]]: Custom predictive frame reassembly algorithms and zero-allocation indexing.
- [[Hardware Video Pipeline|Hardware-Video-Pipeline]]: Direct3D 11/12 and Vulkan Video decoders, DXGI Flip Model, and HDR10 tone mapping.
- [[Audio WASAPI Exclusive|Audio-WASAPI-Exclusive]]: Sub-5ms low-latency audio rendering and Opus packet processing.

### Protocols and Networking
- **[GameStream and Sunshine Protocol](GameStream-Sunshine-Protocol)**: Network port matrix, cryptographic pairing, and RTSP stream orchestration.
- **[Real-Time LAN Host Discovery](Real-Time-Host-Discovery)**: Zero-allocation Multicast DNS (mDNS) and SSDP UPnP host discovery engine.
- **[Cryptographic Pairing Pipeline](Cryptographic-Pairing-Pipeline)**: RSA 2048-bit X.509 certificate generation, PBKDF2/SHA-256 key derivation, and AES-128 challenge-response authentication.
- **[RTSP Stream Control and Dynamic SDP](RTSP-Stream-Control-and-SDP)**: Stateful RTSP client state machine, RFC 4566 SDP offer/answer negotiation, HDR10 static metadata, and dynamic bitrate adaptation announcements.
- **[Zero-Copy UDP Ingestion Pipeline](Zero-Copy-UDP-Ingestion)**: High-throughput UDP datagram receiver, cacheline-aligned PinnedBufferPool, and lock-free C++23 SPSC queue dispatching.
- **[1000Hz Input Subsystem](Input-Subsystem-1000Hz)**: Sub-millisecond raw input polling, atomic delta staging, and binary serialization.
- **[Dynamic RTCP Congestion Control](Dynamic-RTCP-Congestion-Control)**: Real-time RTCP receiver feedback, EMA loss smoothing, and predictive AIMD bandwidth adaptation.

### Moonshine Host Subsystem
- **[Direct3D Desktop Capture Engine](Direct3D-Desktop-Capture)**: IDXGIOutputDuplication & Windows.Graphics.Capture VRAM frame ingestion.
- **[HDR10 & Dynamic Color Space Engine](HDR10-Color-Pipeline)**: Display colorimetry extraction, SMPTE ST 2084 PQ curve, and Direct3D GPU color conversion.
- **[GPU Hardware Video Encoding](GPU-Hardware-Video-Encoding)**: Native NVENC, AMF, and QuickSync low-latency GPU encoders for HEVC/AV1.
- **[NVIDIA NVENC Hardware Pipeline](NVENC-Hardware-Pipeline)**: Dedicated NVENC SDK encoder, P1/P2 ultra-low latency presets, and progressive intra-refresh.
- **[AMD AMF & Intel QuickSync Pipelines](AMF-and-QuickSync-Hardware-Pipelines)**: Dedicated AMD VCN and Intel oneVPL/QSV hardware encoder pipelines.
- **[WASAPI Loopback Audio Engine](WASAPI-Loopback-Audio)**: Low-latency master audio mix capture and multi-channel audio streaming.
- **[Opus Audio Compression Engine](Opus-Audio-Compression)**: Ultra-low latency multi-channel Opus audio compression and multi-stream encoding.
- **[Microphone Passthrough Engine](Microphone-Passthrough-and-Virtual-Sink)**: Client-to-host low-latency microphone audio streaming, jitter buffering, and virtual audio device injection.
- **[Virtual Input & Driver Injection](Virtual-Input-Driver-Injection)**: ViGEmBus controller emulation and kernel-level mouse/keyboard injection.
- **[GameStream HTTPS & RTSP Host Server](GameStream-HTTPS-RTSP-Host-Server)**: Embedded pairing, discovery, and RTSP stream orchestration.
- **[Authenticated Remote Control Plane](Authenticated-Remote-Control-Plane)**: Secure client-to-host RPC, dynamic display mode adaptation, and zero-overhead coordinator.

### Workflows, Testing and Performance
- [[CI and GitHub Workflows|Continuous-Integration-and-Workflows]]: Multi-OS CI matrix, ASan/TSan sanitizers, AOT trimming verification, and security audits.
- [[Exhaustive Testing Strategy|Exhaustive-Testing-Strategy]]: Unit testing matrix across native SIMD, concurrency, and managed protocols.
- [[Benchmarking and Performance Audit|Benchmarking-and-Performance-Audit]]: Micro-benchmarks, BenchmarkDotNet methodology, allocation verification, and profiling workflows.
- [[Developer Setup and Build|Developer-Setup-and-Build]]: Toolchain prerequisites (MSVC, CMake, Ninja, .NET 9 SDK) and automated verification commands.

---

## Performance Manifesto

Moonshine adheres to strict latency and memory rules:
1. Zero Bytes Managed GC Allocations in Streaming Hot Paths: Frame ingestion, packet parsing, and FEC processing must never trigger garbage collection.
2. Lock-Free Cross-Thread Data Flow: Video and audio frames are passed across threads using lock-free single-producer single-consumer (SPSC) ring buffers padded to 64-byte cache lines (alignas(64)).
3. Hardware-Direct Presentation: Frame surfaces are presented directly via DXGI Flip Model (DXGI_SWAP_EFFECT_FLIP_DISCARD) with sub-frame presentation waitables.
4. SIMD-Accelerated Parity Recovery: Reed-Solomon FEC matrix multiplications execute in vectorised 256-bit registers, yielding over twelve times speedup over scalar lookups.
5. Custom High-Performance Implementations: Generic third-party libraries are superseded by custom implementations whenever measurable performance gains can be achieved.
