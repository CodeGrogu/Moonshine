# Performance-First Development Rules

These rules govern all code modifications and additions in Moonshine.

## 1. Zero-Allocation Mandate
- **Hot Paths**: Network receive loop, packet parsing, FEC Galois Field reconstruction, jitter buffer reassembly, video frame queueing, audio mixing, and input polling.
- **Rule**: `0 B` heap allocations per packet/frame.
- **Prohibited**:
  - `new` class instantiations inside streaming loops.
  - Linq expressions (`.Select()`, `.Where()`, `.ToList()`, etc.).
  - Boxing value types or struct-to-interface casts.
  - String formatting or concatenation (`$"{foo}"`, `+`).
  - Closures and lambda allocations (`() => ...`).
- **Required**:
  - `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `ReadOnlySequence<T>`.
  - `stackalloc` for small fixed buffers (< 1KB).
  - Unmanaged native memory buffers via `NativeMemoryOwner`.
  - `ValueTask` or synchronous returns for high-frequency operations.

## 2. Cacheline Alignment & False Sharing
- Multi-threaded shared structures (like SPSC ring buffers, packet queues, frame descriptors) must be aligned to 64 bytes (`alignas(64)` in C++, `[StructLayout(LayoutKind.Sequential, Pack = 64)]` in C#).
- Atomic read and write indices must reside on separate cachelines to prevent CPU cache bouncing across cores.

## 3. SIMD Vectorization
- All multi-byte operations (parity XOR, Galois Field matrix operations, checksum calculation) must have optimized AVX2 and AVX-512 SIMD implementations with fallback paths.
