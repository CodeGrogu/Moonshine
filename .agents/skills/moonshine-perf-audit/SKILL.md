---
name: moonshine-perf-audit
description: >-
  Audits Moonshine code for performance bottlenecks, zero-allocation violations,
  cacheline alignment, and SIMD optimization across C# and C++ streaming hot paths.
  Use whenever reviewing PRs, diagnosing latency spikes, or optimizing throughput.
---

# Moonshine Performance Audit Skill

This skill guides the auditing and profiling of Moonshine streaming pipelines to guarantee sub-3ms end-to-end latency and zero managed heap allocations in hot paths.

## Audit Checklist

### 1. Zero-Allocation Verification (C#)
- [ ] Ensure no `new` object allocations inside socket receive loop, packet parsing, frame assembly, or rendering dispatch.
- [ ] Verify `Span<byte>` and `ReadOnlySpan<byte>` are used for header slicing.
- [ ] Check that `stackalloc` or `NativeMemoryOwner` is used for unmanaged/pinned memory buffers.
- [ ] Confirm no boxing or closure allocations exist in high-frequency event handlers.

### 2. Lock-Free & Concurrency Audit (C++)
- [ ] Ensure all atomic indices in `SpscRingBuffer` and packet queues are aligned to 64 bytes (`alignas(64)`).
- [ ] Verify memory ordering: `std::memory_order_relaxed` for local state, `std::memory_order_acquire` for reads, `std::memory_order_release` for publishing.
- [ ] Confirm no `std::mutex`, `std::unique_lock`, or thread sleeping in latency-critical loops.
- [ ] Ensure all C-ABI handles accessed concurrently by background threads use `SafeHandleStore<T>` reference counting rather than bare pointers or boolean `is_valid()` checks.

### 3. SIMD Vectorization Audit
- [ ] Verify that Galois Field $GF(2^8)$ multiplication uses AVX2 256-bit nibble shuffle tables (`_mm256_shuffle_epi8`).
- [ ] Confirm fallback scalar paths are isolated and runtime CPUID detection is present.

## Commands
```powershell
# Run performance benchmarks
./scripts/run_benchmarks.ps1
```
