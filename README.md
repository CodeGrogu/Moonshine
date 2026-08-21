<div align="center">

# Moonshine

**A Custom, High-Performance Windows PC Streaming Platform**  
*Built from the ground up with C# and C++ for low-latency streaming, bidirectional audio, remote host control, and Windows-native device integration.*

[![CI Build](https://github.com/moonshine-stream/moonshine/actions/workflows/ci.yml/badge.svg)](https://github.com/moonshine-stream/moonshine/actions)
[![Benchmarks](https://github.com/moonshine-stream/moonshine/actions/workflows/benchmarks.yml/badge.svg)](https://github.com/moonshine-stream/moonshine/actions)
[![Code Quality](https://github.com/moonshine-stream/moonshine/actions/workflows/code-quality.yml/badge.svg)](https://github.com/moonshine-stream/moonshine/actions)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011-informational)](#platform-and-scope)

</div>

---

## What is Moonshine?

**Moonshine is one custom Windows application, written in C# and C++, for high-performance remote PC streaming and interaction.**

A user installs the same application on a PC and selects how that installation operates:

- **Host only** - the application exposes the local PC as a Moonshine streaming host.
- **Client only** - the application connects to another Moonshine host as a client.
- **Host + Client** - the same application enables both roles simultaneously.

These are **runtime roles of one application**, not separate Host and Client products.

Moonshine is being designed as its **own platform, protocol, architecture, and implementation**. It is not a reimplementation of Sunshine or Moonlight, and their architecture and protocols are not the foundation of the project. Moonshine may independently use techniques that are technically useful, but its wire formats, media pipeline, device integration, security model, and performance architecture are designed specifically for Moonshine.

---

## Runtime Role Model

Role selection is an architectural resource boundary, not merely a UI setting.

| Mode | Host role | Client role | Host listeners | Client connections | Host devices | Client devices |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **Host only** | On | Off | On | Off | On | Off |
| **Client only** | Off | On | Off | On | Off | On |
| **Host + Client** | On | On | On | On | On | On |

### Host only

When Host-only mode is selected, the Client role is not initialised.

Client-specific media pipelines, rendering, microphone capture, client-side workers, client-only connections, and other Client resources must not be started.

Host-only mode should expose only the listeners, devices, memory pools, worker threads, and media pipelines required by the Host role.

### Client only

When Client-only mode is selected, the Host role is not initialised.

Host capture, hardware encoders, host-side listeners, host virtual devices, host-only workers, large host media buffers, and other Host resources must not be started.

Client-only mode should therefore behave like a client installation without consuming resources or opening ports that belong to the Host role.

### Host + Client

Host + Client mode enables both role sets in the same process.

The roles remain independently owned and managed so that each can be started, stopped, faulted, and reconfigured without accidentally starting resources belonging to the other role.

> **A disabled role should have no meaningful runtime footprint beyond the configuration required to select or re-enable that role.**

That includes avoiding unnecessary CPU work, memory allocation, GPU contexts, audio devices, network listeners, sockets, timers, background threads, and driver/device initialisation.

---

## What Moonshine Does

### PC Streaming

A Moonshine Host streams its PC environment to a Moonshine Client.

The target media path is:

```text
Host PC
  |
  +-- Desktop / application capture
  +-- Video encoding
  +-- Host audio capture
  |
  v
Moonshine transport
  |
  v
Client
  +-- Video decode + rendering
  +-- Audio decode + playback
```

The system is designed for low latency, high throughput, predictable timing, and efficient resource use rather than general-purpose media processing.

### Bidirectional Audio

Host-to-client audio is only one direction of the audio architecture.

Moonshine also supports the intended reverse path:

```text
Client microphone
      |
      v
Client capture
      |
      v
Moonshine microphone stream
      |
      v
Host transport / decode
      |
      v
Moonshine virtual microphone
      |
      v
Windows applications
```

This allows an application running on the Host PC to use the Client PC's physical microphone as a normal Windows microphone source.

### Windows-Native Audio Integration

Moonshine is intended to provide dedicated Windows audio endpoints so that Moonshine's streaming audio can be separated from unrelated application audio and devices.

The planned Host-side device layer includes:

- A Moonshine audio endpoint for host-side streaming audio.
- A Moonshine virtual microphone for Client microphone input.
- Efficient audio input/output paths designed specifically for streaming.
- Low-latency buffering between the Windows audio stack and the Moonshine media pipeline.

A driver is not being introduced merely because it is called a driver. Kernel or device components will exist where a real Windows integration or performance requirement justifies them.

### Remote Host Control

A connected Client is not a passive receiver. It is intended to manage authorised Host settings through a dedicated authenticated control plane.

Examples include:

- Video configuration.
- Audio configuration.
- Microphone configuration.
- Capture configuration.
- Streaming configuration.
- Host device selection.
- Session configuration.
- Host status and capabilities.

The Client also has its own local configuration. Host settings and Client settings are separate domains and must not be conflated.

---

## Application Architecture

Moonshine is one application with three major backend planes.

```text
                         Moonshine Application
                                  |
              +-------------------+-------------------+
              |                   |                   |
              v                   v                   v
         Host Role           Client Role          Shared Core
              |                   |                   |
              +----------+--------+--------+----------+
                         |                 |
                         v                 v
                    Media Plane       Control Plane
                         |                 |
                         +--------+--------+
                                  |
                                  v
                             Device Plane
```

### Media Plane

The Media Plane carries time-sensitive traffic:

- Video.
- Host-to-client audio.
- Client-to-host microphone audio.
- Other session media required by Moonshine.

It is designed around low latency, bounded processing, explicit ownership, and predictable timing.

### Control Plane

The Control Plane handles state and configuration:

- Host settings.
- Client settings.
- Session lifecycle.
- Capability negotiation.
- Device configuration.
- Runtime status.
- Telemetry.

Control operations are separated from real-time media transport so configuration traffic cannot interfere with media timing.

### Device Plane

The Device Plane integrates Moonshine with Windows APIs, audio devices, GPU interfaces, and other OS-level resources required by the product.

---

## Performance Philosophy

**Performance is a core architectural requirement of Moonshine.**

The goal is high end-to-end performance across the complete path:

```text
capture
  -> encode
  -> packetise
  -> transport
  -> buffering / FEC
  -> decode
  -> render / playback
```

and for the reverse microphone path:

```text
microphone capture
  -> encode
  -> transport
  -> decode
  -> virtual microphone
```

The backend should favour:

- C++ for genuinely performance-critical native components.
- C# for orchestration and application logic where appropriate.
- Zero or near-zero allocations in latency-sensitive paths.
- Lock-free or low-contention structures where they provide measurable benefit.
- Cache-aware data structures.
- SIMD acceleration where appropriate.
- Preallocated buffers and memory pools.
- Explicit ownership and lifetime rules.
- Hardware acceleration where available.
- Efficient asynchronous I/O.
- Deterministic failure and shutdown behaviour.

Performance claims must be measured and reproducible rather than inferred from implementation style.

---

## GPU Support

Moonshine is intended to support a broad range of modern Windows GPUs rather than being tied to one vendor.

The target hardware ecosystems include:

- NVIDIA.
- AMD.
- Intel.

Vendor-specific backends may be implemented where the underlying hardware and API require it, while Moonshine exposes a consistent internal media interface.

Hardware capability discovery must reflect the actual machine. A backend must never report operational support when the required GPU, driver, SDK, or device resources do not exist.

Synthetic or simulated backends are for controlled development/testing environments and must never masquerade as production hardware.

---

## Technology Stack

### C#

C# is used for managed application and orchestration responsibilities, including:

- Session management.
- Protocol orchestration.
- Configuration.
- Authentication and trust management.
- High-level networking.
- Host/Client coordination.
- Application-level control logic.

### C++

C++ is used where native execution and predictable performance are important, including:

- High-throughput networking primitives.
- Packet processing.
- Buffer management.
- Lock-free queues.
- FEC and SIMD processing.
- Hardware media backends.
- Direct3D integration.
- Performance-critical audio components.
- Native Windows integration.

The C# and C++ boundary should remain explicit and measurable. Neither language is used merely because it is traditionally associated with a particular task. Components belong in the layer that provides the best combination of correctness, maintainability, and measured performance.

---

## Current Backend Focus

The current priority is the **backend**, not the UI.

The immediate engineering focus includes:

1. Runtime Host/Client role selection and strict resource isolation.
2. Network transport and packet processing.
3. Buffer ownership and memory management.
4. Low-latency concurrency.
5. Video capture, encoding, decoding, and presentation.
6. Host audio capture and Client playback.
7. Client microphone capture and Host microphone injection.
8. FEC and packet-loss recovery.
9. Jitter buffering and frame assembly.
10. Hardware capability detection and acceleration.
11. Windows audio/device integration.
12. Authentication and session security.
13. Remote Host configuration and control.
14. End-to-end performance measurement.
15. Reliability and fault propagation.

UI work will build on these foundations rather than defining the backend architecture.

---

## Project Structure

Moonshine is organised as a modular solution across managed and native components:

- [`src/Moonshine.Native`](./src/Moonshine.Native): C++ native engine containing performance-critical networking, memory, concurrency, SIMD, media, and Windows integration components.
- [`src/Moonshine.Protocol`](./src/Moonshine.Protocol): Moonshine-native protocol definitions, packet formats, stream metadata, capability negotiation, and control messages.
- [`src/Moonshine.Interop`](./src/Moonshine.Interop): C#/.NET to native C++ interoperability layer.
- [`src/Moonshine.Core`](./src/Moonshine.Core): Shared managed application and session logic, configuration, security, orchestration, and networking coordination.
- [`src/Moonshine.Host`](./src/Moonshine.Host): Host-role capture, encoding, audio, microphone, streaming, device, and remote-control components.
- [`src/Moonshine.Client`](./src/Moonshine.Client): Client-role receiving, decoding, rendering, microphone capture, input, and host-control components.
- [`src/Moonshine.Benchmarks`](./src/Moonshine.Benchmarks): Performance and micro-benchmark suite.

The exact project boundaries may evolve as the backend matures, but role, media, control, device, and application concerns remain deliberate architectural boundaries.

---

## Platform and Scope

Moonshine is initially focused on **Windows 11 x64 PCs** because Windows provides the native graphics, audio, input, device, and driver interfaces required for the project's performance goals.

The platform focus allows Moonshine to optimise deeply for:

- Direct3D.
- DXGI.
- Windows audio APIs.
- Windows device integration.
- Win32 input.
- Modern x64 CPUs.
- Modern NVIDIA, AMD, and Intel GPUs.

Cross-platform support is not currently a primary goal. The project will prioritise a highly capable Windows implementation before considering other platforms.

---

## Engineering Principles

### One application, explicit roles

Host and Client are runtime roles of the same application. Disabled roles remain inactive and resource-isolated.

### Custom by design

Moonshine defines its own architecture and protocol. Compatibility with other streaming platforms is not a prerequisite for correctness.

### No fake capabilities

A component must not report functionality that it cannot actually provide.

### No pretend implementations

Production functionality must be implemented with real OS, hardware, networking, or algorithmic behaviour. Simulated/stub implementations are explicitly isolated to development and test contexts and must never be exposed as operational production capabilities.

### Performance must be measurable

Claims about latency, throughput, allocations, or hardware acceleration must be supported by reproducible measurements.

### Hot paths stay simple

Real-time media processing should avoid unnecessary abstraction, allocation, locking, copying, and scheduling.

### Explicit ownership

Buffers, packets, frames, audio samples, and native resources must have clearly defined ownership and lifetime rules.

### Failure must be visible

Network, hardware, device, and pipeline failures must propagate to the appropriate subsystem instead of being silently swallowed or represented as successful operation.

### Security is part of the architecture

Authentication, trust, authorisation, secure configuration, and control-plane permissions are designed into the system rather than added after the media pipeline is complete.

---

## Documentation

- [Architecture & Protocol Deep Dive](./ARCHITECTURE.md)
- [Engineering Standards: Solo + AI Edition](./STANDARDS.md)
- [Performance Guidelines & Allocations Budget](./PERFORMANCE.md)
- [Known Issues & Scaffolding Tracking](./KNOWN_ISSUES.md)
- [Contributing Guide](./CONTRIBUTING.md)
- [Security Policy](./SECURITY.md)
- [Changelog](./CHANGELOG.md)

---

## Licence

Moonshine is licensed under the [GNU General Public License v3.0 (GPLv3)](./LICENSE).
