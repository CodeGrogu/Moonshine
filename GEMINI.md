# Moonshine Project Rules and Guidelines

## Core Principles
1. Performance is Priority Number 1: Absolute minimum latency, zero-copy architecture, zero GC allocations in streaming hot paths.
2. Custom High-Performance Implementations: Whenever a faster, custom-made approach can be designed, prioritise and implement that custom approach. Heavily document the design, mathematical model, and assembly/SIMD execution paths in the GitHub wiki (`wiki/`).
3. Always Document in GitHub Wiki: Every feature, protocol detail, architectural decision, and benchmark must be documented in `wiki/`.
4. British English Standard: Use British English exclusively across all code comments, documentation, commit messages, and agent communication (optimise, behaviour, synchronisation, etc.).
5. Strict Formatting Restrictions: Never use em dashes. Never use emojis.
6. Hybrid Architecture:
   - C# 13 (.NET 9 Native AOT) for managed protocol orchestration, networking pipelines, pairing crypto, and input polling.
   - C++23 for SIMD AVX2/AVX-512 Galois Field FEC decoding, lock-free SPSC queues, D3D11/D3D12/Vulkan hardware video decoding, and sub-5ms WASAPI audio.
7. Strict Interop Alignment: All structs across the C#/C++ boundary must be 1:1 blittable with identical field packing and sizes.
8. Testing and Verification: Every change must maintain passing native CTest suites and .NET xUnit suites, with 0 warnings in compilation.
