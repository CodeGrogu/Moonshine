> [!WARNING]
> **Status Disclaimer:** Moonshine is in active development (v0.5.6-alpha). It is its own platform with its own protocol (MNBP v1), not a GameStream client or Moonlight replacement. No end-to-end streaming works yet. The application is fail-closed.

# Developer Setup and Build Guide

## 1. Prerequisites

### Windows Development Environment
- Visual Studio 2022 (v17.8 or higher) or Visual Studio Build Tools with:
  - C++ CMake tools for Windows
  - MSVC v143 (x64) C++ build tools
  - Windows 11 SDK (10.0.22621.0 or newer)
- .NET 9.0 SDK (x64)
- Git for Windows

---

## 2. Fast 1-Click Verification

The repository includes automated PowerShell scripts that detect all installed build tools and run complete compilation and test passes:

```powershell
# 1. Run full verification (25 CTests and 706+ xUnit tests)
./scripts/verify_codebase.ps1

# 2. Build Release binaries without executing test suites
./scripts/build.ps1 -Configuration Release -SkipTests

# 3. Execute microbenchmarks
./scripts/run_benchmarks.ps1
```

---

## 3. Manual Build Instructions

### Building Native C++23 Library (`Moonshine.Native`)
```powershell
# Configure CMake with Ninja and MSVC toolchain
cmake -B build -S . -G "Ninja" -DCMAKE_BUILD_TYPE=Release -DMOONSHINE_ENABLE_AVX2=ON -DMOONSHINE_BUILD_TESTS=ON

# Compile native library
cmake --build build --config Release --parallel

# Execute native CTest suite
ctest --test-dir build --output-on-failure -C Release
```

### Building Managed .NET Solution (`Moonshine.sln`)
```powershell
# Restore and build managed projects
dotnet build Moonshine.sln -c Release

# Execute managed xUnit test suites
dotnet test Moonshine.sln -c Release --no-build
```

---

## 4. Continuous Integration Matrix

GitHub Actions (`.github/workflows/ci.yml`) automatically builds and tests every commit across:
- `windows-latest` (MSVC 2022 + Ninja + .NET 9 Native AOT)
- `windows-2022` (MSVC C++23 + Ninja + .NET 9 Native AOT)
