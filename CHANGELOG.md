# Changelog

All notable changes to **Moonshine** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

_No unreleased changes yet._

## [0.5.6-alpha] - 2026-08-25

> **Development Milestone**: This is the first tagged version of Moonshine, reflecting the project's progress across 56 closed issues. The project is in active pre-release development. No end-to-end streaming pipeline is operational yet.

### Added
- **Moonshine Architecture**: Initial repository initialisation and multi-tiered high-performance architecture with selectable Host, Client, and Host + Client runtime roles.
- **Moonshine Native Binary Protocol (MNBP v1)**: First-party control and media wire protocol specification. Defined in `docs/PROTOCOL_SPEC_V1.md`. Legacy RTSP, RTP, RTCP, and GameStream protocol code remains in the repository only for compatibility reference and is excluded from the production composition root.
- **Moonshine.Native Engine (C++23)**:
  - SIMD-accelerated Reed-Solomon Galois Field GF(2^8) FEC matrix multiplier supporting AVX2 and AVX-512 GFNI.
  - Lock-free, cache-aligned SPSC (Single-Producer Single-Consumer) ring buffer with sub-microsecond latency.
  - Hardware video decoder abstraction with Direct3D 11/12 and Vulkan Video pipelines (capability discovery implemented, physical bitstream decoding in progress).
  - Sub-millisecond predictive jitter buffer and packet reassembly engine.
  - Low-latency WASAPI Exclusive and ASIO audio rendering subsystem.
  - Blittable C-ABI export surface with direct pointer interop.
- **Moonshine.Protocol (C# 13 / .NET 9)**:
  - MNBP v1 packet envelope codec with zero-allocation serialisation and deserialisation.
  - Zero-allocation RTSP and SDP parser (legacy compatibility, not used by production roles).
  - High-throughput RTP video/audio header parsers using `ReadOnlySpan<byte>` (legacy compatibility).
  - FEC packet layout and header validation structs.
  - Encrypted control packet serialiser and loss feedback generator.
  - High-polling input packet definitions (Gamepad, Mouse, Keyboard, Touch).
- **Moonshine.Interop**:
  - `[LibraryImport]` source-generated P/Invoke bindings.
  - Blittable memory layouts and `NativeMemoryOwner` unmanaged memory pooling.
- **Moonshine.Core**:
  - Host discovery via mDNS and HTTP (legacy compatibility module).
  - Cryptographic X.509 certificate exchange and AES-128/256-GCM pairing handshake.
  - RTSP state machine and streaming session coordinator (legacy compatibility).
  - Socket ingestion pipelines with `System.IO.Pipelines`.
- **Hardware Video Encoding (NVENC, AMF, QuickSync)**:
  - Multi-vendor hardware encoder abstraction with dynamic runtime capability discovery.
  - Dynamic rate-control reconfiguration, keyframe requests, and latency measurement.
  - Physical bitstream encoding in fail-closed state pending downstream pipeline wiring.
- **Desktop Capture Engine**:
  - DXGI OutputDuplication and Windows.Graphics.Capture frame ingestion (prototype).
- **Virtual Audio Driver (WaveRT)**:
  - PortCls WaveRT miniport driver source, user-mode C-ABI bridge, and Shared Memory IPC pipeline (prototype, requires WDK and driver signing for deployment).
- **Moonshine.Benchmarks**:
  - Microbenchmarks for SIMD vs Scalar FEC recovery, Span RTP parsing, and lock-free ring buffer throughput.
- **Developer and Antigravity Tooling**:
  - Custom rules, hooks, and specialised skills for Antigravity:
    - `moonshine-perf-audit`
    - `moonshine-build-and-verify`
    - `moonshine-fec-simd-optimizer`
    - `moonshine-protocol-diagnostics`
    - `moonshine-hardware-pipeline`
    - `moonshine-adversarial-audit`
- **Engineering Standards**: Ten-rule engineering methodology (`STANDARDS.md`) for solo + AI development with mandatory proof-of-work, adversarial self-audit, and maturity taxonomy.

### Status
- Application composition root (`MoonshineApplication`) is **fail-closed** by design: all roles report unsupported until Moonshine-native session control and media transport are fully implemented.
- Legacy GameStream/Sunshine compatibility modules exist in the codebase but are **classified as Incompatible** and unreachable from the production composition root.
- No end-to-end streaming pipeline is operational.

[Unreleased]: https://github.com/CodeGrogu/Moonshine/compare/v0.5.6-alpha...HEAD
[0.5.6-alpha]: https://github.com/CodeGrogu/Moonshine/releases/tag/v0.5.6-alpha
