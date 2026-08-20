# 📝 Changelog

All notable changes to **Moonshine** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Moonshine Architecture**: Initial repository initialization and multi-tiered high-performance architecture.
- **Moonshine.Native Engine (C++23)**:
  - SIMD-accelerated Reed-Solomon Galois Field $GF(2^8)$ FEC matrix multiplier supporting AVX2 and AVX-512 GFNI.
  - Lock-free, cache-aligned SPSC (Single-Producer Single-Consumer) ring buffer with sub-microsecond latency.
  - Hardware video decoder abstraction with Direct3D 11/12 and Vulkan Video pipelines.
  - Sub-millisecond predictive jitter buffer and packet reassembly engine.
  - Low-latency WASAPI Exclusive and ASIO audio rendering subsystem.
  - Blittable C-ABI export surface with direct pointer interop.
- **Moonshine.Protocol (C# 13 / .NET 9 Native AOT)**:
  - Zero-allocation RTSP and SDP parser.
  - High-throughput RTP video/audio header parsers using `ReadOnlySpan<byte>`.
  - FEC packet layout and header validation structs.
  - Encrypted control packet serializer and loss feedback generator.
  - High-polling input packet definitions (Gamepad, Mouse, Keyboard, Touch).
- **Moonshine.Interop**:
  - `[LibraryImport]` source-generated P/Invoke bindings.
  - Blittable memory layouts and `NativeMemoryOwner` unmanaged memory pooling.
- **Moonshine.Core**:
  - Host discovery via mDNS and HTTP.
  - Cryptographic X.509 certificate exchange and AES-128/256-GCM pairing handshake.
  - RTSP state machine and streaming session coordinator.
  - Socket ingestion pipelines with `System.IO.Pipelines`.
- **Moonshine.Benchmarks**:
  - Micro-benchmarks for SIMD vs Scalar FEC recovery, Span RTP parsing, and lock-free ring buffer throughput.
- **Developer & Antigravity Tooling**:
  - Custom rules, hooks, and specialized skills for Antigravity:
    - `moonshine-perf-audit`
    - `moonshine-build-and-verify`
    - `moonshine-fec-simd-optimizer`
    - `moonshine-protocol-diagnostics`
    - `moonshine-hardware-pipeline`
