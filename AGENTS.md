# Moonshine Agent Instructions

- Prioritize performance optimization across all decisions.
- Maintain zero-allocation discipline in C# streaming hot paths (`Span<T>`, `ValueTask`, `NativeMemoryOwner`).
- Maintain cache-aligned lock-free concurrency in C++23.
- Use CMake and MSVC/Ninja for C++ native builds, and `dotnet` / `MSBuild` for .NET.
- Keep the test suites and micro-benchmarks updated when modifying protocols, algorithms, or native bridges.
