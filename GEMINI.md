# Moonshine Project Rules and Guidelines

## Operational Standard
All architectural decisions, native SIMD pipelines, managed protocols, and engineering practices MUST adhere to the **Moonshine Engineering Standards: Solo + AI Edition** in [`STANDARDS.md`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/STANDARDS.md).

## Architectural Foundations
- **Zero-Allocation Discipline**: Zero GC allocations in C# streaming hot paths (`Span<T>`, `ValueTask`, `NativeMemoryOwner`).
- **Cache-Aligned Concurrency & Atomic References**: Lock-free SPSC queues with explicit 64-byte cacheline padding in C++23. Shared memory ring headers and cross-boundary synchronization must use standard C++23 `std::atomic_ref<T>` with explicit acquire/release memory ordering (`std::memory_order_acquire` / `std::memory_order_release`) rather than relying on `volatile` qualifiers alone.
- **Strict Blittable Interop & Defensive C-ABI Boundaries**: 1:1 binary layout parity across C# P/Invoke and C-ABI export boundaries. All C-ABI export functions must validate pointer arguments, assert non-zero buffer capacities, and wrap native invocations in `try / catch (...)` blocks. All codec abstractions (Opus, hardware encoders/decoders) must validate input frame dimensions and durations against codec specifications.
- **Safe Native Handle Lifetime**: C-ABI exports must use `std::shared_ptr` ownership guards (`SafeHandleStore<T>`) to prevent TOCTOU use-after-free races during concurrent disposal. Avoid managed finalisers on streaming pipeline wrappers with background worker threads.
- **Active Native Test Assertions**: Native C++ test sources must not use standard `<cassert>` `assert(...)` macros that are compiled out under `NDEBUG` in Release builds. All test conditions must use active assertion macros (`REQUIRE` or `TEST_ASSERT`) that evaluate and fail deterministically across both Debug and Release configurations.
- **Formatting Standards**: British English standard, no em dashes, no emojis.
- **MCP Research Protocols**: Always make use of `microsoftdocs/mcp` (for Windows, DirectX, Win32, and .NET documentation/samples), `com.microsoft/nuget` (for package searches, version verification, and security reviews), and `io.github.upstash/context7` (for library and documentation lookups).
- **Overseer Subagent Governance**: Overseer governs up to six specialized subagents (`researcher`, `implementer`, `adversary`, `test-writer`, `script-runner`, `specialist`). Work is mandatorily delegated to subagents.
- **Script Timing & Polling Discipline**: Schedule timer checks between $\frac{1}{2} T$ and $3 \times T$ for scripts with estimated duration $T$, checking at most once, twice, or thrice.
- **Verification Pipeline**: All changes must pass `scripts/verify_codebase.ps1` (environment probe, preflight sweep, physical artifact checks, 22 CTests, and 493 xUnit tests).
- **Official Script Priority**: If an official script exists for any task or workflow (e.g. `scripts/verify_codebase.ps1`, `scripts/preflight.ps1`, `scripts/verify_environment.ps1`), always execute the official script directly and modify or extend it afterwards if additional behaviour or adjustments are required.

