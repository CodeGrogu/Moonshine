# Implementation Integrity and Platform Scope Rules

All implementation decisions and platform assumptions must strictly follow the engineering standards in [`STANDARDS.md`](file:///c:/Users/Jaden/Documents/antigravity/Moonshine%20Pro/STANDARDS.md).

These rules govern all architectural, coding, and reporting decisions in Moonshine.

## 1. No Simulations and Scaffolding Transparency
- Never simulate functionality, protocols, or hardware interaction with fake or simulated responses.
- If a component, class, or method is scaffolding, placeholder, or stub logic:
  - State explicitly that it is scaffolding in documentation, code comments, and status reports.
  - Formulate and record a concrete plan for the actual production implementation.

## 2. Accurate Status Representation
- Never overstate what exists, works, or has been implemented in the codebase.
- Provide objective, precise descriptions of completed vs pending work.

## 3. Prohibition of Hardcoding
- Do not hardcode values, constants, mock payloads, or logic where dynamic, configurable, or production-grade implementations are required.
- Configuration, network parameters, hardware capabilities, and protocol responses must be dynamically resolved or formally configured.

## 4. Target Platform Scope: Windows 11
- Focus exclusively on Windows 11 for all active development, hardware acceleration pathways (D3D11/D3D12/WASAPI), and build verification.
- Do not add compatibility layers or concessions for other operating systems unless explicitly directed.
