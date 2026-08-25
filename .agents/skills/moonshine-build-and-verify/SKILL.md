---
name: moonshine-build-and-verify
description: >-
  Builds, compiles, and verifies the entire Moonshine repository, including toolchain probe,
  preflight scanner, C++23 native acceleration library, and managed .NET 9 solution.
  Use whenever compiling the project, running test suites, or verifying build health.
---

# Moonshine Build & Verification Skill

This runbook guides building and testing the Moonshine stack in compliance with [`STANDARDS.md`](../../../STANDARDS.md).

## Canonical Verification Pipeline

Always execute the canonical verification pipeline before committing or accepting changes:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify_codebase.ps1
```

This automated pipeline executes:
- **Step -1**: `scripts/verify_environment.ps1` (Probes MSVC toolchain, C++ standard headers, and auto-initialises developer environment).
- **Step 0**: `scripts/preflight.ps1` (Pre-commit scan for stubs, secrets, swallowed catches, and unprovenanced metrics).
- **Step 1**: Native C++23 build with physical artifact existence verification (`Moonshine.Native.dll`).
- **Step 2**: 16 registered native CTest test suites.
- **Step 3**: Managed .NET 9 solution build with physical artifact verification (`Moonshine.Host.dll`).
- **Step 4**: 415 managed xUnit tests across all test projects (89 Protocol + 145 Core + 100 Host + 81 Interop).

## Standalone Native Compilation (MSVC Environment)

When running CMake or Ninja directly, always ensure the Visual Studio Developer environment is active:

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\Launch-VsDevShell.ps1' -SkipAutomaticLocation
cmake --build build --config Release
```
