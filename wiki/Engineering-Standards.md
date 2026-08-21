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
   - Auto-initialises MSVC environment variables if running in plain PowerShell.
   - Compiles and executes a scratch C++ probe verifying standard library header resolution (`<cstdint>`, `<iostream>`).
2. **Preflight Sweep (`scripts/preflight.ps1`)**:
   - Mechanically scans for unannotated stubs (`// STUB:` with >= 15 char justification).
   - Prohibits hardcoded private keys or tokens.
   - Forbids swallowed exceptions (`catch (Exception)` without `// ALLOWED_EXCEPTION:`).
   - Prevents inline unapproved TLS validation callbacks.
   - Rejects unprovenanced metric claims lacking `<!-- VERIFIED: -->` tags.
3. **Physical Artifact Verification**:
   - Tests physical presence of output binaries (`build\bin\Moonshine.Native.dll` and `src\Moonshine.Host\bin\Release\net9.0\Moonshine.Host.dll`).
4. **Native & Managed Test Suites**:
   - Runs 16 native C++23 CTest test suites.
   - Runs 239 managed xUnit tests across all 4 test projects.

---

## 3. Four-Tier Maturity Taxonomy

Features in Moonshine transition through four distinct maturity phases:
1. **Prototype**: Scaffolding or early implementation. Compiles cleanly, but lacks end-to-end hardware testing.
2. **Verified**: Passed local proof-of-work validation (Rule 1) and value-based regression tests (Rule 3).
3. **Interop-verified**: Validated against external software (Sunshine, Moonlight, `ffprobe`).
4. **Trusted**: Interop-verified and proven stable under sustained real-world streaming workloads.
