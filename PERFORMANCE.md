# ⚡ Moonshine Performance Manifesto & Guidelines

Performance in Moonshine is the primary design constraint. Every line of code, data structure, and system call must be justified by its latency, throughput, and CPU cache impact.

---

## 1. Golden Rules of Moonshine Engineering

### Rule 1: Zero Managed Allocation in the Hot Path
- **NEVER** use `new`, `class` object instantiation, string concatenation, lambda captures (`Func<T>`, `Action<T>`), or boxing in packet processing, frame assembly, or rendering paths.
- Prefer `readonly ref struct`, `ValueTask<T>`, `Span<T>`, `ReadOnlySpan<T>`, and `Memory<T>`.
- Use unmanaged native memory arenas, object pools (`ArrayPool<T>.Shared`), or `stackalloc` for temporary buffers.

### Rule 2: Zero Unnecessary Copies
- Video and audio buffers must never be cloned across managed/unmanaged boundaries.
- Data from the network socket must be parsed in-place using `ReadOnlySpan<byte>` and passed as raw pointers (`byte*`) to native C++ APIs.

### Rule 3: Lock-Free Concurrency
- **NEVER** use `lock (syncRoot)`, `Monitor.Enter`, `std::mutex`, or `std::shared_mutex` in latency-sensitive threads.
- Use `SpscRingBuffer` with atomic head/tail indices and memory order acquire/release semantics (`std::memory_order_acquire`, `std::memory_order_release`).
- Ensure all atomic indices are padded to 64 bytes (`alignas(64)`) to eliminate false sharing between CPU cores.

### Rule 4: Data-Oriented & Cache-Friendly Design
- Structure data in Arrays of Structures (AoS) or Structures of Arrays (SoA) that fit neatly within L1/L2 cache lines (64 bytes).
- Avoid pointer chasing; keep related data contiguous in memory.

### Rule 5: SIMD Vectorization
- Any loop processing binary arrays, matrix math, Galois Field arithmetic, or checksum verification must use x64 AVX2 or AVX-512 intrinsics.
- Fallback paths for legacy CPUs must be clearly isolated and dynamically dispatched at runtime using CPUID detection.

---

## 2. Memory & Latency Budgets

```
Total End-to-End Client Latency Budget: < 3.0 ms (Target: 1.5 - 2.0 ms at 120 FPS / 4K)
┌───────────────────────┬───────────────┬────────────────────────┐
│ Stage                 │ Latency Limit │ Allocation Limit       │
├───────────────────────┼───────────────┼────────────────────────┤
│ Socket Ingestion (UDP)│ < 50 µs       │ 0 Bytes                │
│ RTP / Header Parsing  │ < 20 µs       │ 0 Bytes                │
│ FEC Recovery (AVX2)   │ < 100 µs      │ 0 Bytes (Arena Pool)   │
│ Jitter Reassembly     │ < 150 µs      │ 0 Bytes (Slot Ring)    │
│ Hardware Video Decode │ < 1.50 ms     │ 0 Bytes (GPU Surface)  │
│ Presentation Flip     │ < 0.20 ms     │ 0 Bytes (DXGI Flip)    │
└───────────────────────┴───────────────┴────────────────────────┘
```

---

## 3. C# .NET 9/10 Optimization Patterns

### Blittable Interop
```csharp
// CORRECT: Zero-marshaling blittable struct with fixed size
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct RtpHeader(
    byte Flags,
    byte PayloadType,
    ushort SequenceNumber,
    uint Timestamp,
    uint Ssrc
);

// P/Invoke with LibraryImport
[LibraryImport("Moonshine.Native.dll", EntryPoint = "moonshine_fec_decode_avx2")]
[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
internal static unsafe partial int MoonshineFecDecodeAvx2(
    byte** packets,
    int packetCount,
    int packetSize,
    byte* outputBuffer
);
```

### In-Place Span Parsing
```csharp
public static bool TryParseRtp(ReadOnlySpan<byte> buffer, out RtpHeader header, out ReadOnlySpan<byte> payload)
{
    if (buffer.Length < sizeof(RtpHeader))
    {
        header = default;
        payload = default;
        return false;
    }

    header = MemoryMarshal.Read<RtpHeader>(buffer);
    payload = buffer[sizeof(RtpHeader)..];
    return true;
}
```

---

## 4. Native C++23 Optimization Patterns

### Cacheline Alignment
```cpp
struct alignas(64) FrameSlot {
    uint32_t frame_index;
    uint32_t packet_count;
    uint32_t received_count;
    uint8_t* buffer;
    std::atomic<bool> ready;
    char padding[64 - sizeof(uint32_t)*3 - sizeof(uint8_t*) - sizeof(std::atomic<bool>)];
};
```

### Vectorized XOR & Galois Multiply
```cpp
// AVX2 256-bit Vectorized XOR for Fast Parity
inline void VectorXor256(uint8_t* dest, const uint8_t* src, size_t length) {
    size_t i = 0;
    for (; i + 32 <= length; i += 32) {
        __m256i d = _mm256_loadu_si256(reinterpret_cast<const __m256i*>(dest + i));
        __m256i s = _mm256_loadu_si256(reinterpret_cast<const __m256i*>(src + i));
        _mm256_storeu_si256(reinterpret_cast<__m256i*>(dest + i), _mm256_xor_si256(d, s));
    }
    for (; i < length; ++i) {
        dest[i] ^= src[i];
    }
}
```

---

## 5. Benchmarking & Verification

Every critical PR must include BenchmarkDotNet numbers demonstrating:
1. Mean execution time $\le$ baseline.
2. Allocations = `0 B` per operation.
3. No GC collections in Gen 0, Gen 1, or Gen 2.
