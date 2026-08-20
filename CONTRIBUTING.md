# 🤝 Contributing to Moonshine

We welcome contributions to Moonshine! Because Moonshine is an ultra-performance project, all contributions must adhere to rigorous performance, testing, and styling standards.

---

## 1. Development Workflow

1. **Fork the Repository** and create a feature branch (`feature/my-optimized-feature` or `fix/my-bugfix`).
2. **Follow Coding Standards**:
   - Adhere to the [.editorconfig](./.editorconfig).
   - Ensure all C# code uses C# 13 features with strict nullability and zero-allocation idioms.
   - Ensure all C++ code follows modern C++23 standards, RAII, and cache-friendly alignment.
3. **Write Unit Tests & Benchmarks**:
   - Every protocol parser, FEC calculation, or native bridge must have corresponding unit tests in `tests/`.
   - Any modification touching the hot path must include BenchmarkDotNet benchmarks in `src/Moonshine.Benchmarks`.
4. **Run Verification Scripts**:
   ```powershell
   ./scripts/verify_codebase.ps1
   ```
5. **Submit a Pull Request** using the standard PR template.

---

## 2. Commit Message Conventions

We follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

- `feat(fec)`: Add AVX-512 GFNI Galois Field matrix multiplier
- `perf(protocol)`: Reduce RTP packet header parsing latency to 12ns with Span
- `fix(interop)`: Fix 64-bit struct padding in D3D11 frame descriptor
- `docs(readme)`: Update architecture sequence diagram
- `test(jitter)`: Add test for out-of-order packet reassembly

---

## 3. PR Review Criteria

- [ ] Zero managed allocations in streaming loops (`0 B` reported by BenchmarkDotNet).
- [ ] No mutex locks or blocking synchronization in hot packet processing paths.
- [ ] Multi-platform compatibility (Windows, Linux, macOS) accounted for.
- [ ] All unit tests passing in CI.
