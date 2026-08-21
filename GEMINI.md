# Moonshine Project Rules and Guidelines

## Operational Standard
All architectural decisions, native SIMD pipelines, managed protocols, and engineering practices MUST adhere to the **Moonshine Engineering Standards: Solo + AI Edition** in [`STANDARDS.md`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/STANDARDS.md).

## Architectural Foundations
- **Zero-Allocation Discipline**: Zero GC allocations in C# streaming hot paths (`Span<T>`, `ValueTask`, `NativeMemoryOwner`).
- **Cache-Aligned Concurrency**: Lock-free SPSC queues with explicit 64-byte cacheline padding in C++23.
- **Strict Blittable Interop**: 1:1 binary layout parity across C# P/Invoke and C-ABI export boundaries.
- **Formatting Standards**: British English standard, no em dashes, no emojis.
- **Verification Pipeline**: All changes must pass `scripts/verify_codebase.ps1` (environment probe, preflight sweep, physical artifact checks, 16 CTests, and 239 xUnit tests).
