# Moonshine Agent Instructions

## Operational Standard
All development, code modifications, testing, and agent interactions in this repository MUST strictly follow the **Moonshine Engineering Standards: Solo + AI Edition** in [`STANDARDS.md`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/STANDARDS.md).

## Core Directives
- **British English**: Use British English exclusively across all documentation, code comments, commit messages, and agent communication (`optimise`, `prioritise`, `behaviour`, `synchronisation`, etc.).
- **Formatting Rules**: Never use em dashes (use colons, hyphens, parentheses, or commas). Never use emojis.
- **Proof-of-Work**: Every feature and fix requires personally running commands that prove real value transformation (Rule 1).
- **Adversarial Audit**: Every completion report must include an explicit Adversarial Self-Audit arguing against the implementation (Rule 2).
- **Platform Scope**: Focus exclusively on Windows 11 for all current development and architecture.
- **Toolchain Probe & Preflight**: Always execute `scripts/verify_environment.ps1` and `scripts/preflight.ps1` before committing code.
- **Commit-to-Issue Association**: Every git commit message must explicitly include the associated GitHub issue reference in its subject line (e.g. `feat(subsystem): brief summary (Issue #<number>)` or `(#<number>)`) to ensure GitHub automatically establishes the two-way commit-to-issue association upon push.
- **Benchmark Documentation Workflow**: Whenever implementing or modifying performance-critical streaming pipelines, hot paths, or claiming performance characteristics, execute BenchmarkDotNet microbenchmarks and log physical results in `docs/BENCHMARKS.md` with timestamped Rule 9 provenance tags before task completion.
- **MCP Research Directives**: Always make proactive use of `microsoftdocs/mcp` (for official Microsoft, Windows, DirectX, and .NET documentation and code samples), `com.microsoft/nuget` (for NuGet package queries, versions, and security audits), and `io.github.upstash/context7` (for library and API documentation resolution).
