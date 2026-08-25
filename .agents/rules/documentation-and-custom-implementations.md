# Documentation and Custom Implementations Standards

All documentation and custom components must adhere to the overarching engineering standards in [`STANDARDS.md`](../../STANDARDS.md).

## 1. Tone, Style, and Typography
- Use British English exclusively across all documentation, code comments, commit messages, and agent outputs (for example: optimise, prioritise, behaviour, serialisation, synchronise, analyse).
- Do not use em dashes under any circumstance. Use colons, hyphens, parentheses, or commas instead.
- Do not use emojis under any circumstance.

## 2. GitHub Wiki Documentation Mandate
- Always document new architecture, protocols, components, and workflows in the GitHub wiki (`wiki/`).
- Keep the wiki updated whenever algorithms, interfaces, structs, or pipelines are modified.

## 3. Custom High-Performance Implementations
- Whenever a faster, custom-made implementation can be designed for any protocol, math kernel, queue, decoder, or buffer mechanism, always choose the custom high-performance solution over generic libraries.
- When creating custom implementations:
  - Document the design thoroughly in the GitHub wiki.
  - Detail the algorithmic complexity, mathematical foundation, SIMD vectorisation model, cacheline alignment, and zero-allocation memory guarantees.
  - Provide comparative micro-benchmarks demonstrating the performance advantage over standard approaches.
