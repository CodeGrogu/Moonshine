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

**Moonshine is one custom Windows application, written in C# and C++, that provides a high-performance platform for remotely streaming and interacting with PCs.**

A user installs the same Moonshine application on a PC and chooses how that installation should operate:

- **Host mode**: the application exposes the PC as a streaming host.
- **Client mode**: the application connects to another Moonshine host and acts as the remote client.
- **Host + Client mode**: the same installation can perform both roles when the user explicitly enables both.

These are **runtime roles of one application**, not separate host and client products.

When only one role is enabled, the other role is intended to be completely inactive. A Host-only installation must not start client media pipelines, client capture/rendering components, or client-specific network listeners. A Client-only installation must not initialise host capture/encoding/device services, open host-side listening ports, or consume host-specific resources. The goal is that an unused role has effectively no runtime footprint beyond the small amount of configuration needed to select or change the role.

Moonshine is being designed as its **own platform, protocol, architecture, and implementation**. It is not a reimplementation of Sunshine or Moonlight, and it does not use their architecture or protocol as a foundation. Compatibility with other streaming systems is not a prerequisite for correctness. Moonshine's protocols, data structures, media pipeline, device integration, security model, and performance characteristics are designed specifically for Moonshine.

The core model is:

```text
                         ONE MOONSHINE APPLICATION
                                   │
                    ┌──────────────┼──────────────┐
                    │              │              │
                    ▼              ▼              ▼
                 HOST ONLY     CLIENT ONLY    HOST + CLIENT
                    │              │              │
                    │              │              │
             Host subsystems   Client subsystems  Both role sets
             active only       active only         explicitly active
                    │              │              │
                    └──────────────┴──────────────┘
                                   │
                            Moonshine Core
```

When a session exists, the application can carry traffic in both directions:

```text
                         MOONSHINE SESSION

                 HOST ROLE                    CLIENT ROLE
              ┌─────────────┐              ┌─────────────┐
              │ PC Capture  │─────────────►│ Video       │
              │ Video       │   Video      │ Rendering   │
              └─────────────┘              └─────────────┘

              ┌─────────────┐              ┌─────────────┐
              │ Host Audio  │─────────────►│ Audio       │
              │ Capture     │   Audio      │ Playback    │
              └─────────────┘              └─────────────┘

              ┌─────────────┐              ┌─────────────┐
              │ Virtual     │◄─────────────│ Client      │
              │ Microphone  │  Microphone  │ Microphone  │
              └─────────────┘              └─────────────┘

                       ◄──── Control ────►
                    Host + Client Settings
                    Session Management
                    Device Configuration
```

The client is not merely a passive receiver. A connected client is intended to **interact with the host PC and manage authorised host-side settings**, while also managing its own local configuration.

---

## Application Roles

### Host mode

When the user selects **Host only**, Moonshine activates the services required to make the local PC available for remote streaming and control.

Host responsibilities include:

- Desktop and/or application capture.
- Game or desktop video capture.
- Hardware-accelerated video encoding.
- Host audio capture.
- Client input reception.
- Client microphone reception and decoding.
- Moonshine virtual microphone integration.
- Host-side device management.
- Authenticated remote configuration.
- Host-side session and transport services.

The Client role is not initialised in Host-only mode.

### Client mode

When the user selects **Client only**, Moonshine activates the services required to connect to and use another Moonshine host.

Client responsibilities include:

- Receiving and decoding streamed video.
- Rendering the remote PC.
- Receiving and playing host audio.
- Capturing the client's local microphone.
- Sending microphone audio to the host.
- Sending input to the host.
- Managing authorised host settings.
- Managing local client configuration.

The Host role is not initialised in Client-only mode. Host capture, encoder, virtual microphone, host listener, and host-specific device services remain inactive.

### Host + Client mode

Users who want the local PC to both provide and consume Moonshine sessions can explicitly enable **Host + Client mode**. The application then activates both role sets while keeping their resources and lifecycle boundaries separate.

Role selection is therefore an architectural concern, not merely a UI preference. Disabled subsystems must not create listeners, initialise drivers/devices, allocate large media buffers, or start background workers unnecessarily.

---

## Core Capabilities

### PC Streaming

Moonshine allows a host PC to stream its PC environment to a client with an emphasis on low latency, high throughput, deterministic behaviour, and efficient resource utilisation.

The streaming pipeline is being designed to support:

- Desktop and application capture.
- Game streaming.
- Hardware-accelerated video encoding and decoding.
- High-quality host audio streaming.
- Client input sent back to the host.
- Bidirectional audio communication.
- Session and connection management.

### Bidirectional Audio

Audio is a first-class part of Moonshine rather than an afterthought.

The host can send its audio to the client:

```text
Host applications / games
        │
        ▼
Moonshine host audio pipeline
        │
        ▼
Audio encoding
        │
        ▼
Moonshine transport
        │
        ▼
Client audio decoding
        │
        ▼
Client audio output
```

The client can also send microphone audio back to the host:

```text
Client microphone
        │
        ▼
Microphone capture
        │
        ▼
Audio encoding
        │
        ▼
Moonshine transport
        │
        ▼
Host audio decoding
        │
        ▼
Moonshine virtual microphone
        │
        ▼
Windows applications
```

This allows applications running on the host PC to use the remote client's microphone as a normal Windows microphone source.

### Windows-Native Audio Devices

Moonshine is intended to provide dedicated Windows audio-device integration so that Moonshine's streaming audio is separated from unrelated applications and audio devices.

The host-side device architecture is intended to provide dedicated Moonshine audio endpoints rather than treating existing application audio devices as Moonshine-owned resources.

The planned device layer includes:

- A Moonshine audio endpoint for host-side streaming audio.
- A Moonshine virtual microphone for client microphone input.
- Efficient audio input/output paths designed specifically for streaming.
- Low-latency buffering and transport between the Windows audio stack and the Moonshine media pipeline.

The exact driver architecture will be determined during implementation. Moonshine will not introduce a driver merely for the sake of having one; kernel/device components will exist where they provide a concrete architectural or performance benefit.

### Remote Host Control

A Moonshine client is intended to be able to manage authorised host settings remotely.

Examples include:

- Video configuration.
- Audio configuration.
- Microphone configuration.
- Capture configuration.
- Streaming configuration.
- Host device selection.
- Session configuration.
- Host status and capabilities.

The control system will use an explicit authenticated and authorised protocol rather than exposing unrestricted access to host internals.

The architecture therefore treats Moonshine as a **bidirectional platform**, not simply a one-way streaming client.

---

## Custom Architecture

Moonshine is being designed around a clear separation of responsibilities inside the single application.

```text
                         Moonshine Application
                                  │
                    ┌─────────────┼─────────────┐
                    │             │             │
                    ▼             ▼             ▼
                Host Role     Client Role    Shared Core
                    │             │             │
                    │             │             │
                    └──────┬──────┴──────┬──────┘
                           │             │
                           ▼             ▼
                     Media Plane     Control Plane
                           │             │
                           └──────┬──────┘
                                  ▼
                            Device Plane
```

### Media Plane

The media plane carries time-sensitive data:

- Video.
- Host-to-client audio.
- Client-to-host microphone audio.

The media plane is designed around low latency and predictable processing rather than general-purpose request/response semantics.

### Control Plane

The control plane handles state and configuration:

- Host settings.
- Client settings.
- Session lifecycle.
- Capability negotiation.
- Device configuration.
- Runtime status.
- Telemetry.

Media transport and host management are intentionally separated so that configuration operations cannot interfere with the real-time media path.

### Device Plane

The device plane integrates Moonshine with Windows hardware and software interfaces, including audio devices and future device components where required.

---

## Performance Philosophy

**Performance is a core architectural requirement of Moonshine.**

The project is being engineered with the expectation that latency, throughput, CPU utilisation, memory behaviour, and scheduling overhead must be measurable and continuously verified.

The hot path should favour:

- C++ for performance-critical native components.
- C# for high-level orchestration and application logic where appropriate.
- Zero or near-zero allocations in latency-sensitive paths.
- Lock-free or low-contention data structures where they provide a measurable benefit.
- Cache-aware data structures.
- SIMD acceleration where appropriate.
- Preallocated buffers and memory pools.
- Explicit ownership and lifetime rules.
- Hardware acceleration wherever available.
- Asynchronous I/O without unnecessary scheduling overhead.
- Deterministic error and shutdown behaviour.

Performance claims are expected to be backed by benchmarks and profiling rather than assumptions.

Moonshine is not being optimised around a single synthetic benchmark. The objective is **high end-to-end performance**, from capture and encoding through transport, buffering, decoding, rendering, audio processing, microphone uplink, and control operations.

---

## GPU Support

Moonshine is intended to support **a broad range of GPU hardware and vendor acceleration paths** rather than being designed around a single GPU manufacturer.

The native backend architecture is therefore being designed to accommodate vendor-specific implementations where necessary while presenting a consistent Moonshine interface to the rest of the system.

The intended hardware acceleration scope includes support for the major Windows GPU ecosystems, including:

- NVIDIA.
- AMD.
- Intel.

Hardware capabilities must be discovered from the actual system. A backend must never claim operational hardware support when the required device, driver, or vendor API is unavailable.

Where a hardware backend cannot operate, Moonshine must report that state explicitly rather than silently pretending that a simulated or unavailable implementation is operational.

---

## Technology Stack

Moonshine uses two primary implementation languages:

### C#

C# is used for managed application and orchestration responsibilities, including:

- Session management.
- Protocol orchestration.
- Configuration.
- Authentication and trust management.
- High-level networking.
- Host/client coordination.
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

The boundary between C# and C++ is intended to remain explicit and measurable. Native code is not used simply because it is faster in theory, and managed code is not avoided simply because it is managed. Components are implemented in the layer that provides the best combination of correctness, maintainability, and measured performance.

---

## Current Development Focus

Moonshine is currently focused heavily on the **backend foundation**.

The immediate priority is not the visual UI. The priority is making the underlying system correct, fast, testable, and reliable.

Current architectural focus areas include:

1. Network transport and packet processing.
2. Buffer ownership and memory management.
3. Low-latency concurrency.
4. Video capture, encoding, decoding, and presentation.
5. Host audio capture and client playback.
6. Client microphone capture and host microphone injection.
7. FEC and packet-loss recovery.
8. Jitter buffering and frame assembly.
9. Hardware capability detection and acceleration.
10. Windows audio/device integration.
11. Authentication and session security.
12. Remote host configuration and control.
13. End-to-end performance measurement.
14. Reliability and fault propagation.

The UI and broader user experience will be built on top of these foundations once the backend architecture is sufficiently mature.

---

## Project Structure

Moonshine is organised as a modular solution across managed and native components:

- [`src/Moonshine.Native`](./src/Moonshine.Native): C++ native engine containing performance-critical networking, memory, concurrency, SIMD, media, and Windows integration components.
- [`src/Moonshine.Protocol`](./src/Moonshine.Protocol): Moonshine-native protocol definitions, packet formats, stream metadata, capability negotiation, and control messages.
- [`src/Moonshine.Interop`](./src/Moonshine.Interop): C#/.NET to native C++ interoperability layer.
- [`src/Moonshine.Core`](./src/Moonshine.Core): Shared managed application and session logic, configuration, security, orchestration, and networking coordination.
- [`src/Moonshine.Host`](./src/Moonshine.Host): Host-role capture, encoding, audio, microphone, streaming, and remote-control components.
- [`src/Moonshine.Client`](./src/Moonshine.Client): Client-role receiving, decoding, rendering, microphone capture, input, and host-control components.
- [`src/Moonshine.Benchmarks`](./src/Moonshine.Benchmarks): Performance and micro-benchmark suite.

The exact boundaries between these projects may evolve as the native and managed architectures mature, but the separation between role, media, control, device, and application concerns is intentional.

---

## Platform and Scope

Moonshine is initially focused on **Windows 11 x64 PCs** because Windows provides the native graphics, audio, input, and driver interfaces required for the project's performance goals.

The initial platform scope allows Moonshine to optimise deeply for:

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

Moonshine follows several principles throughout development:

### One application, explicit roles

Host and Client are runtime roles of the same application. Disabled roles must not initialise their pipelines, listeners, devices, drivers, large resource pools, or background workers.

### Custom by design

Moonshine defines its own architecture and protocol. Compatibility with other streaming platforms is not a prerequisite for correctness.

### No fake capabilities

A component must not report functionality that it cannot actually provide.

### Performance must be measurable

Claims about latency, throughput, allocations, or hardware acceleration must be supported by reproducible measurements.

### Hot paths stay simple

Real-time media processing should avoid unnecessary abstraction, allocation, locking, copying, and scheduling.

### Explicit ownership

Buffers, packets, frames, audio samples, and native resources must have clearly defined ownership and lifetime rules.

### Failure must be visible

Network, hardware, device, and pipeline failures must propagate to the appropriate subsystem instead of being silently swallowed or represented as successful operation.

### Security is part of the architecture

Authentication, trust, authorisation, and secure configuration are designed into the system rather than added after the media pipeline is complete.

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
