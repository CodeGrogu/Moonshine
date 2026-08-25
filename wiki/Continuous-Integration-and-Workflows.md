> [!WARNING]
> **Status Disclaimer:** Moonshine is in active development (v0.5.6-alpha). It is its own platform with its own protocol (MNBP v1), not a GameStream client or Moonlight replacement. No end-to-end streaming works yet. The application is fail-closed.

# Continuous Integration and GitHub Workflows Architecture

Moonshine employs an extensive, multi-tier GitHub Actions continuous integration and verification pipeline. Each workflow is engineered to enforce strict performance boundaries, eliminate memory safety vulnerabilities, and guarantee build health on Windows 11.

---

## 1. Workflow Catalog

| Workflow File | Trigger | Purpose & Quality Gates |
| :--- | :--- | :--- |
| **`ci.yml`** | Push and PR (`main`, `develop`) | Windows 11 MSVC C++23 build with CTest and .NET 9 xUnit test suites. |
| **`native-sanitizers.yml`** | Push (`main`), PR, Nightly | AddressSanitizer (ASan), UndefinedBehaviorSanitizer (UBSan), and ThreadSanitizer (TSan) concurrency checks. |
| **`aot-publish-verification.yml`** | Push (`main`), PR | Standalone .NET 9 Native AOT publish verification, trimming audit, and binary size budget enforcement ($< 40\,\text{MB}$). |
| **`benchmarks.yml`** | Push (`main`), PR | Automated BenchmarkDotNet runs validating the 0B managed allocation rule and tracking latency regressions. |
| **`code-quality.yml`** | Push & PR (`main`, `develop`) | Strict code formatting, Roslyn code analysis, British English compliance, and emoji/em dash exclusion checks. |
| **`security-audit.yml`** | Push (`main`), Weekly | CodeQL static analysis and automated NuGet dependency vulnerability auditing. |

---

## 2. CI Build Matrix (`ci.yml`)

The primary CI workflow validates both the native C++23 acceleration library and the managed .NET 9 Native AOT solution:
1. Native Configuration: Generates build files using Ninja and compiles with MSVC C++23 on Windows 11.
2. Native Unit Tests: Executes CTest suites (`test_fec_simd`, `test_spsc_ring_buffer`, `test_jitter_buffer`, `test_c_abi_export`).
3. Managed Compilation: Compiles all solution projects with `TreatWarningsAsErrors=true`.
4. Managed Unit Tests: Runs xUnit suites across Protocol, Interop, and Core layers with code coverage collection.

---

## 3. Sanitizers and Concurrency Verification (`native-sanitizers.yml`)

To guarantee memory safety in C++23 without runtime garbage collection:
- **ASan (AddressSanitizer)**: Detects out-of-bounds memory accesses, buffer overflows, and memory leaks.
- **UBSan (UndefinedBehaviorSanitizer)**: Flags undefined bitwise shifts, integer overflows, and unaligned pointer accesses.
- **TSan (ThreadSanitizer)**: Stress-tests the lock-free SPSC circular queue across multiple concurrent producer/consumer threads to ensure zero data races under acquire-release memory ordering.

---

## 4. Native AOT Trimming Verification (`aot-publish-verification.yml`)

Moonshine is compiled ahead-of-time (AOT) to native machine code without the .NET runtime JIT compiler. The AOT verification workflow guarantees:
1. Zero reflection-related trim warnings (`IL2026`, `IL3050`).
2. Self-contained single-file executable generation.
3. Binary size budget enforcement ($< 40\,\text{MB}$ for full client runtime).
