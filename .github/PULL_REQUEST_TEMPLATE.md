## Description

Provide a clear and concise summary of the changes introduced in this Pull Request.

Fixes #(issue number)

---

## Architectural and Performance Rationale

- **Performance Impact**: Explain how this change preserves or improves latency and throughput.
- **Memory Discipline**: Confirm whether managed GC allocations in streaming hot paths remain 0 bytes (`GC.GetAllocatedBytesForCurrentThread() == 0`).
- **SIMD / Concurrency**: Detail any AVX2/AVX-512 vectorization or lock-free memory ordering used.

---

## Quality & Compliance Checklist

- [ ] All native C++23 CTest suites pass without errors or warnings.
- [ ] All managed .NET 9 xUnit test suites pass without errors or warnings.
- [ ] Code adheres strictly to British English spelling conventions across code comments and commit messages.
- [ ] No em dashes are used (replaced with colons, hyphens, parentheses, or commas).
- [ ] No emojis are used.
- [ ] All custom implementations, mathematical models, and protocol extensions are documented in `wiki/`.
- [ ] BenchmarkDotNet micro-benchmarks have been executed or updated if touching hot paths.
