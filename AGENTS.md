# Moonshine Agent Instructions

- Use British English exclusively across all documentation, commit messages, and agent outputs.
- Never use em dashes. Use colons, hyphens, parentheses, or commas instead.
- Never use emojis.
- Prioritise performance optimisation across all decisions.
- If a faster, custom-built implementation can be designed for any component or algorithm, implement the custom solution.
- Heavily document all custom implementations, mathematical algorithms, and architecture in the GitHub wiki (`wiki/`).
- Maintain zero-allocation discipline in C# streaming hot paths (Span, ValueTask, NativeMemoryOwner).
- Maintain cache-aligned lock-free concurrency in C++23.
- Keep the test suites and micro-benchmarks updated when modifying protocols, algorithms, or native bridges.
