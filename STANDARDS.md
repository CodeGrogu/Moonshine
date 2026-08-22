# Moonshine Engineering Standards: Solo + AI Edition

## Context and Foundation

Moonshine is developed in a solo engineer plus AI pair-programming environment. There is no secondary human reviewer, no QA team, and no separate pull request review gate.

In this operational model, rules that rely on external human oversight are non-functional wishes. The failure mode of solo plus AI development: plausible, polished, structurally flawed code (such as simulated hardware encoders, broken Galois Field multi-shard recovery, or world-readable cryptographic keys) occurs faster without systematic process enforcement.

These engineering standards replace the missing reviewer with deterministic mechanical process, strict proof-of-work validation, and active adversarial auditing.

---

## The Core Mandate

> **Never merge or trust code because it looks correct. Merge and trust code only after personally running a command or test that proves it performs the required task.**

Compilation is not proof of correctness. Test suite execution is proof only if the assertions validate real transformed data values rather than simply verifying that no exception was thrown.

---

## The Ten Core Rules

### Rule 1: Mandatory Proof-of-Work Before Task Acceptance
Every AI-implemented feature, bug fix, or protocol handler must be verified with an explicit proof-of-work validation step before acceptance:
- Execute the component against real data input and inspect the concrete output (for example: inspect recovered payload bytes, probe bitstreams via external tools, or inspect network packet byte layouts).
- Run a test with hand-verified assertions rather than unexamined machine-generated assertions.
- Diff runtime behaviour against an independent, known-good reference specification or external implementation.

If proof-of-work cannot be established within ten minutes, the task scope is too broad: decompose it into smaller, verifiable units.

### Rule 2: Adversarial Self-Critique on Every Feature
Before declaring any task or feature complete, the AI agent must explicitly argue against its own implementation by answering:
> "List every way this implementation could be incorrect, incomplete, unoptimised, fragile, or simulated rather than genuine. Highlight all edge cases, missing bounds checks, and platform assumptions."

This adversarial self-audit must be included in the final completion summary.

### Rule 3: Never Let "It Did Not Throw" Count as "It Works"
Every test accepted into the Moonshine repository must assert on real, concrete output values:
- Verify exact recovered byte arrays, decoded frame dimensions, calculated FEC syndromes, and parsed packet headers.
- Tests that only assert `Assert.True(true)`, `Assert.NotNull(result)`, or `Assert.DoesNotThrow` are rejected.
- For data reconstruction subsystems (FEC, sequence unwrappers, jitter buffers, ring buffers): tests must deliberately corrupt or drop known inputs, reconstruct them, and assert byte-for-byte identity against the ground truth.

### Rule 4: Standing Pre-Commit Preflight Sweep
Before every commit, execute the canonical repository preflight scanner (`scripts/preflight.ps1`). The script enforces zero-tolerance checks for:
- Unannotated stubs, placeholders, or simulations.
- Hardcoded credentials, private keys, or tokens.
- Swallowed exceptions (`catch (Exception)` or empty `catch {}` blocks).
- Inline unapproved TLS validation callbacks.
- Metric and count claims lacking timestamped provenance tags.

### Rule 5: Transparent Stub Disclosure and Debt Tracking
Scaffolding and stubs are permissible during early development only when explicitly declared:
- Every stub must be tagged with `// STUB: <detailed explanation>` or `// SIMULATED: <detailed explanation>`.
- Explanations must provide at least 15 characters of specific rationale describing what is missing and why.
- Stubs are prohibited from Release build configurations and must not be described as complete in user-facing documentation.

### Rule 6: Centralised Secret Storage and TLS Discipline
To eliminate credential leak risks structurally:
- All credential and private key writes must route through `Moonshine.Core.Security.SecureFileStore`, which sets owner-only Windows ACLs. Never use raw `File.WriteAllText` on sensitive paths.
- All GameStream self-signed certificate validation must route through the central `CertificateValidation.AcceptSelfSignedGameStreamCert` handler rather than inline lambdas.

### Rule 7: External Counterpart Interoperability
To prevent building internally consistent but externally incompatible protocol implementations:
- Before declaring any streaming or protocol feature complete, test it against an independent external counterpart: Sunshine host, Moonlight client, `ffprobe`, `ffplay`, or a reference Opus decoder.
- If physical hardware or external software is unavailable for immediate testing, document this status explicitly in `KNOWN_ISSUES.md` rather than assuming compatibility.

### Rule 8: Maturity Taxonomy Instead of Binary "Done"
Every component, pipeline, and feature in Moonshine is classified under a four-tier maturity model:
1. **Prototype**: Scaffolding or early implementation. Compiles cleanly, but lacks end-to-end validation on real hardware.
2. **Verified**: Passed local proof-of-work validation (Rule 1) and destructive value-based regression tests (Rule 3).
3. **Interop-verified**: Validated against real external software or hardware counterparts (Rule 7).
4. **Trusted**: Interop-verified and proven stable under sustained real-world streaming workloads.

Only features classified as **Trusted** may be described in `README.md` without qualification.

### Rule 9: Documentation as Auditable Claims (The Provenance Requirement)
Documentation, performance numbers, and test counts are engineering claims, not marketing decoration:
- Every documented metric, benchmark latency, and test count must include an explicit timestamped provenance tag:
  `<!-- VERIFIED: <YYYY-MM-DD>, via `<command>` in <environment> -->`
  or
  `<!-- REGISTERED: <YYYY-MM-DD>, via `<command>` in <environment> -->`
- **Case Study (Syntactic Provenance Illusion)**: During early development, a claim of "247 managed tests" was repeatedly carried forward in documentation with a syntactic `<!-- VERIFIED: ... -->` tag attached, despite the actual passed test runner execution being 239 tests (68 Interop + 59 Protocol + 71 Host + 41 Core = 239). The tag created the illusion of truth without the underlying command being run. A provenance tag is not truth: it is an auditable instruction defining what command must be executed to verify reality.
- Whenever updating documentation, verify that all claims match actual system state. Remove or correct outdated assertions immediately.

### Rule 10: AI Pair-Programming Protocol
- The default human response to any completion claim is "show me the proof."
- The AI agent must provide concrete command outputs, test execution evidence, and adversarial critiques before claiming completion.
- Always make proactive use of `microsoftdocs/mcp`, `com.microsoft/nuget`, and `io.github.upstash/context7` for researching official platform documentation, package versions, security advisories, and library references.
- Periodically execute full repository audits using `.agents/skills/moonshine-adversarial-audit` to detect accumulated stubs, simulated code paths, or stale documentation claims.
- Moonshine supports Windows 11 version 21H2 (build 22000) or later on x64 only. The required native test stack is MSVC C++23, CMake, Ninja, and CTest. The required managed test stack is .NET 9 and xUnit through `dotnet test`.

---

## Standing Pre-Commit Checklist

Before staging or committing any code change, verify every item on this checklist:

- [ ] Executed `scripts/verify_environment.ps1` to confirm MSVC compiler and header resolution.
- [ ] Executed `scripts/preflight.ps1` and resolved or justified every detected item.
- [ ] Personally inspected proof-of-work output on real inputs (Rule 1).
- [ ] Included an Adversarial Self-Audit arguing against the implementation (Rule 2).
- [ ] Verified that all unit tests assert concrete transformed values (Rule 3).
- [ ] Ensured all new stubs carry `// STUB:` with >= 15 characters of explanation (Rule 5).
- [ ] Ensured credential file writes use `SecureFileStore` (Rule 6).
- [ ] Verified physical build artifacts exist on disk (Rule 3).
- [ ] Attached timestamped provenance tags to all documented metrics (Rule 9).
- [ ] Included explicit GitHub issue reference (`(Issue #<number>)` or `(#<number>)`) in the commit message subject and body.
- [ ] Updated `KNOWN_ISSUES.md` if any component remains incomplete or scaffolding.
