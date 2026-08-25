> [!WARNING]
> **Status Disclaimer:** Moonshine is in active development (v0.5.6-alpha). It is its own platform with its own protocol (MNBP v1), not a GameStream client or Moonlight replacement. No end-to-end streaming works yet. The application is fail-closed.

# Moonshine Engineering Standards: Solo + AI Edition

This document summarises the operational principles, verification gates, and mechanical code standards governing the Moonshine repository. The authoritative canonical specification is located in [`STANDARDS.md`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/STANDARDS.md).

---

## 1. The Core Mandate

> **Never merge or trust code because it looks correct. Merge and trust code only after personally running a command or test that proves it performs the required task.**

In a solo developer plus AI environment, process replaces the missing human reviewer. Plausible, polished code must always be backed by concrete proof-of-work.

---

## 2. The Verification Pipeline

All changes must pass the unified verification pipeline:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify_codebase.ps1
```

### Verification Layers
1. **Toolchain & Environment Verification (`scripts/verify_environment.ps1`)**:
   - Requires Windows 11 version 21H2 (build 22000) or later.
   - Auto-initialises the MSVC environment if running in plain PowerShell.
   - Compiles and executes a C++23 probe using MSVC, then verifies CMake, Ninja, CTest, and .NET 9.
2. **Preflight Sweep (`scripts/preflight.ps1`)**:
   - Mechanically scans for unannotated stubs (`// STUB:` with >= 15 char justification).
   - Prohibits hardcoded private keys or tokens.
   - Forbids swallowed exceptions (`catch (Exception)` without `// ALLOWED_EXCEPTION:`).
   - Prevents inline unapproved TLS validation callbacks.
   - Rejects unprovenanced test-count claims and non-Windows platform references.
3. **Physical Artifact Verification**:
   - Tests physical presence of Windows binaries (`build\release-avx2\bin\Moonshine.Native.dll` and the Windows-targeted `Moonshine.Host.dll`).
4. **Native & Managed Test Suites**:
   - Runs 25 native C++23 CTest test suites.
   - Runs 706 passed managed .NET 9 xUnit tests (712 total) across all four test projects.

---

## 3. Four-Tier Maturity Taxonomy

Features in Moonshine transition through four distinct maturity phases:
1. **Prototype**: Scaffolding or early implementation. Compiles cleanly, but lacks end-to-end hardware testing.
2. **Verified**: Passed local proof-of-work validation (Rule 1) and value-based regression tests (Rule 3).
3. **Interop-verified**: Validated against external software counterparts (e.g., Sunshine, Moonlight, `ffprobe`).
4. **Trusted**: Interop-verified and proven stable under sustained real-world streaming workloads using Moonshine's native protocol (MNBP v1).
