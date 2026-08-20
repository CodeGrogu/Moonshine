## ⚡ Moonshine Pull Request

### Summary of Changes
Provide a concise overview of what this PR introduces, fixes, or optimizes.

### Performance Verification
- [ ] **Zero Heap Allocation**: Verified using BenchmarkDotNet / dotMemory that no allocations occur in the hot path.
- [ ] **Lock-Free Discipline**: No mutexes, locks, or blocking waits introduced in real-time threads.
- [ ] **SIMD Optimization**: Vectorized hot loops (AVX2/AVX-512/NEON) where applicable.
- [ ] **Benchmark Results**:
  ```text
  // Paste BenchmarkDotNet summary table here if modifying hot paths
  ```

### Checklist
- [ ] Code builds cleanly across all configurations (`Release`, `Debug`).
- [ ] Unit tests pass in both managed (.NET) and native (C++) test suites.
- [ ] Conforms to [.editorconfig](../.editorconfig) and Clang-Format rules.
- [ ] Documentation updated in `docs/`, `README.md`, or `ARCHITECTURE.md` where appropriate.
