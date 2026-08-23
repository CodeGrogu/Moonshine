---
name: moonshine-adversarial-audit
description: >-
  Audits the Moonshine repository against Rule 2, Rule 4, Rule 5, and Rule 9 of STANDARDS.md.
  Detects unannotated stubs, lazy justifications, unhandled catches, hardcoded secrets,
  and unprovenanced metric claims.
---

# Moonshine Adversarial Audit Skill

This skill enforces Rule 2 ("Adversarial Self-Critique") and Rule 10 ("AI Pair-Programming Protocol") across the Moonshine codebase.

## Audit Workflow

### 1. Execute Canonical Preflight Sweep
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\preflight.ps1
```

### 2. Verify Preflight Fixture Regressions
```powershell
powershell -ExecutionPolicy Bypass -File .\tests\test_preflight_fixtures.ps1
```

### 3. Perform Adversarial Self-Audit
Before completing any task, answer the core adversarial questions:
1. What assumptions about hardware or platform environment does this code make?
2. Are there any edge cases, integer overflows, or buffer bounds not covered by value-based tests?
3. Are all metric claims tagged with valid `<!-- VERIFIED: -->` or `<!-- REGISTERED: -->` provenance comments?
4. Are all stubs tagged with `// STUB:` with >= 15 characters of rationale?
5. Are all native C++ test assertions active (`REQUIRE` / `TEST_ASSERT`) rather than `<cassert>` `assert(...)` that gets compiled out under `NDEBUG` in Release builds?
6. Are cross-boundary shared memory atomic operations using standard C++23 `std::atomic_ref` with explicit memory order rather than `volatile` qualifiers?
7. Are all C-ABI export boundaries protected with defensive pointer non-nullness checks, buffer capacity assertions, and `try / catch (...)` exception wrappers?
