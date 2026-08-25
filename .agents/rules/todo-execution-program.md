# Rule: Autonomous TODO Execution Program & Definition of Done

## Scope & Applicability
This rule governs all agent operations triggered by /TODO, /CONTINUE, or general autonomous task resolution across the Moonshine repository.

---

## 1. The Operational Mandate of /TODO

Invoking /TODO mandates executing the repository's TODO program continuously. The Orchestrator MUST NOT merely list tasks or produce conversational commentary. It executes tasks to completion following the deterministic 18-step loop.

### The 18-Step Autonomous Loop
1. **Read `TODO.md`**: Load the entire backlog, active items, and dependency graph.
2. **Parse Dependencies**: Identify actionable items whose prerequisites are fully satisfied.
3. **Select Highest-Priority Actionable TODO**: Pick the top priority unblocked task.
4. **Check Repository State**: Verify git status, active branch, and preflight health (`scripts/verify_environment.ps1`).
5. **Understand the Task**: Define exact boundary requirements, fail-closed contracts, and acceptance criteria.
6. **Research Authoritative Documentation**: Consult official sources (`microsoftdocs/mcp`, `com.microsoft/nuget`, `io.github.upstash/context7`).
7. **Inspect Existing Implementation**: Audit relevant native MSVC C++23, C-ABI, and managed .NET 9 code paths.
8. **Implement**: Author production-grade code adhering to zero GC allocations, defensive boundaries, and blittable layouts.
9. **Build**: Compile cleanly via `scripts/build.ps1 -SkipTests` with zero errors and zero warnings.
10. **Test**: Execute native CTests (`ctest`) and managed xUnit tests (`dotnet test`).
11. **Review**: Subject the implementation to adversarial self-critique (Rule 2) and specialist review.
12. **Correct**: Address all identified edge cases, bounds errors, or feedback.
13. **Re-Test**: Re-run the test suite to confirm zero regressions.
14. **Re-Review**: Confirm all adversarial objections are resolved.
15. **Verify Evidence**: Run `scripts/verify_codebase.ps1` (Rule 1 & Rule 4) and generate Rule 9 provenance records.
16. **Mark TODO Complete**: Update task state in `TODO.md` with proof-of-work output.
17. **Commit/Checkpoint State**: Commit to git with commit-to-issue association (`feat(...): ... (Issue #<number>)`).
18. **Select Next Actionable TODO**: Advance to the next task and repeat until all items are completed.

---

## 2. Universal Definition of Done (DoD)

A task is NEVER marked complete because a reviewer says "looks good" or because code "did not throw". The universal completion condition is:

\text{Implementation} + \text{Tests} + \text{Independent Review} + \text{Evidence} + \text{Definition of Done} + \text{No Unresolved Blockers} = \mathbf{DONE}

---

## 3. Checkpoint Recovery (`/CONTINUE`)

If an execution turn or session is interrupted:
- `/CONTINUE` recovers execution from the persisted checkpoint state in `TODO.md` and `task.md`.
- It resumes directly with the in-progress step rather than restarting previously completed and verified tasks.
