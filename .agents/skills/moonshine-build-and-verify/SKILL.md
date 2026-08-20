---
name: moonshine-build-and-verify
description: >-
  Builds, compiles, and verifies the entire Moonshine repository, including the C++23
  native acceleration library (CMake + MSVC / Ninja) and managed .NET 9 Native AOT solution.
  Use whenever compiling the project, running test suites, or verifying build health.
---

# Moonshine Build & Verification Skill

This runbook guides building and testing both native C++23 libraries and managed .NET solutions across Debug and Release configurations.

## Build Steps

### 1. Build Native C++ Engine
```powershell
cmd.exe /c "call ""C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"" && ""C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"" -B build -S . -G ""Ninja"" -DCMAKE_MAKE_PROGRAM=""C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"" -DCMAKE_BUILD_TYPE=Release -DMOONSHINE_ENABLE_AVX2=ON -DMOONSHINE_BUILD_TESTS=ON && ""C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"" --build build --config Release"
```

### 2. Run Native CTest Suite
```powershell
cmd.exe /c "call ""C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"" && set PATH=%CD%\build\src\Moonshine.Native;%PATH% && ""C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"" --test-dir build --output-on-failure -C Release"
```

### 3. Run Verification Script
```powershell
./scripts/verify_codebase.ps1
```
