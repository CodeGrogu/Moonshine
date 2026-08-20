# Moonshine Project Rules & Guidelines

## Core Principles
1. **Performance is Priority #1**: Absolute minimum latency, zero-copy architecture, zero GC allocations in streaming hot paths.
2. **Hybrid Architecture**:
   - C# 13 (.NET 9/10 Native AOT) for managed protocol orchestration, networking pipelines, pairing crypto, and input polling.
   - C++23 for SIMD AVX2/AVX-512 Galois Field FEC decoding, lock-free SPSC queues, D3D11/D3D12/Vulkan hardware video decoding, and sub-5ms WASAPI audio.
3. **Strict Interop Alignment**: All structs across the C#/C++ boundary must be 1:1 blittable with identical field packing and sizes.
4. **Testing & Verification**: Every change must maintain passing native CTest suites and .NET xUnit suites, with 0 warnings in compilation.
