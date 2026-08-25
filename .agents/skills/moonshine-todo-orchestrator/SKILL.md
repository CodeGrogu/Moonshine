---
name: moonshine-todo-orchestrator
description: Autonomous execution engine for the Moonshine repository TODO backlog and continuous execution loop. Use whenever executing /TODO or /CONTINUE to drive tasks from backlog to genuine Definition of Done completion.
---

# Moonshine TODO Backlog Autonomous Orchestrator

## Overview

This skill guides the Orchestrator through the deterministic 18-step TODO program loop. It prevents superficial "looks good" closures and enforces the strict mathematical Definition of Done.

## Strict Definition of Done (DoD)

$$\text{Implementation} + \text{Tests} + \text{Independent Review} + \text{Evidence} + \text{Definition of Done} + \text{No Unresolved Blockers} = \mathbf{DONE}$$

## The 18-Step Execution Procedure

1. **Read Backlog**: Read `TODO.md` to load active items and dependency topology.
2. **Parse Dependencies**: Filter for unblocked, actionable items.
3. **Select Priority**: Choose the highest priority actionable task (P1 over P2).
4. **Probe Environment**: Run `scripts/verify_environment.ps1` to ensure MSVC, CMake, Ninja, and .NET toolchains are healthy.
5. **Analyze Requirements**: Extract fail-closed requirements and boundary conditions.
6. **Research Docs**: Query `microsoftdocs/mcp` and official documentation.
7. **Inspect Codebase**: View existing C++23 native, C-ABI export, and C# managed code.
8. **Implement**: Apply modifications strictly adhering to zero-allocation hot paths and blittable binary parity.
9. **Compile**: Run `scripts/build.ps1 -SkipTests` and verify 0 warnings and 0 errors.
10. **Test**: Run `ctest` and `dotnet test`.
11. **Adversarial Audit**: Perform Rule 2 self-critique arguing against the implementation.
12. **Correct**: Remediate any discovered defects.
13. **Re-Test**: Verify zero regressions across the test matrix.
14. **Re-Review**: Confirm all adversarial claims are satisfied.
15. **Verify Evidence**: Run `scripts/verify_codebase.ps1` and generate Rule 9 provenance records.
16. **Mark Complete**: Update task status and proof in `TODO.md`.
17. **Commit State**: Commit with `feat(subsystem): description (Issue #<number>)`.
18. **Next Item**: Advance to the next task in the backlog and repeat until complete.

## Recovery via `/CONTINUE`

When invoked via `/CONTINUE`, inspect `task.md` and `TODO.md` to identify the in-progress step of the active task, resuming execution directly without restarting completed tasks.
