# Moonshine Active TODO

> `TODO.md` is the active execution queue. The long-form implementation backlog has moved to [`BACKLOG.md`](./BACKLOG.md).
>
> A task may only be marked complete when implementation, tests, independent review, reproducible evidence, Definition of Done, and blocker checks are all satisfied.

## Current Milestone - Issue #81

**Title:** `[PERF] Build Real End-to-End Streaming Benchmark and Latency Instrumentation`

**Status:** `Completed`

**Priority:** `High`

**GitHub issue:** [#81](https://github.com/CodeGrogu/Moonshine/issues/81)

**Completion date:** 2026-08-26

### Objective

Create the measurement system that proves Moonshine is highly performant using real execution rather than claimed targets.

### Acceptance criteria

- [x] End-to-end latency can be measured from real capture to real presentation.
- [x] Network and audio paths have independent measurements.
- [x] Allocation counts are measured rather than inferred.
- [x] Benchmark results are reproducible and tied to an exact commit/environment.
- [x] Performance regressions can fail the canonical verification gate.

### Completion evidence

Issue #81 was completed through the following verified implementation work:

- `c0a30f5` - real end-to-end streaming benchmark suite and latency verification gates.
- `a8cc681` - automated performance regression and zero-allocation gatekeeper.
- `d5d1b44` - TODO provenance synchronisation for the Issue #81 work.
- `29eda6b` - benchmark and verification provenance updates.

The Issue #81 completion record confirms monotonic QPC timestamp propagation across capture, encode, packetisation, transport, reassembly, decode, and presentation; frame/packet correlation using sequence identifiers; P50/P95/P99 stage telemetry; independent network and audio measurements; and integration of the performance gatekeeper into the canonical verification pipeline.

### Issue #81 audit status

| Requirement | Status | Assessment |
|---|:---:|---|
| Real capture -> real presentation latency | ✅ | Instrumented across the streaming pipeline |
| Independent network measurements | ✅ | Dedicated transport measurement coverage |
| Independent audio measurements | ✅ | WASAPI playback and microphone benchmark coverage |
| p50 | ✅ | Stage-level percentile telemetry |
| p95 | ✅ | Stage-level percentile telemetry |
| p99 | ✅ | Stage-level percentile telemetry |
| Throughput | ✅ | Benchmark and transport measurements |
| Queue depth | ✅ | Runtime telemetry coverage exists |
| Packet loss | ✅ | Independent transport measurement |
| Jitter | ✅ | Independent transport measurement |
| CPU usage | ✅ | Cross-vendor hardware telemetry is available |
| GPU usage | ✅ | Hardware diagnostic telemetry is available where supported |
| Allocations | ✅ | Zero-allocation assertions and benchmark coverage |
| Frame/packet correlation | ✅ | Monotonic timestamps and sequence identifiers |
| Native high-resolution timing | ✅ | Canonical QPC-backed monotonic clock |
| Managed integration | ✅ | Integrated without hot-path managed allocations |
| No hot-path allocation | ✅ | Verified by benchmark and allocation tests |
| Real hardware | ✅ | Physical hardware acceptance evidence exists |
| Real network | ✅ | Real transport measurement and streaming benchmark coverage |
| Separate benchmark fixtures | ✅ | Benchmark/test paths are separated from production simulation paths |
| Environment metadata | ✅ | Provenance is recorded with benchmark evidence |
| GPU/driver metadata | ✅ | Hardware diagnostic telemetry and provenance support are present |
| Reproducible results | ✅ | Results are tied to exact commits and verification evidence |
| Regression CI gate | ✅ | `verify_benchmarks.ps1` is integrated into `verify_codebase.ps1` |

## Issue #81 implementation record

### Phase A - Telemetry primitives

- [x] Canonical monotonic timestamp representation.
- [x] Canonical frame and packet correlation identifiers.
- [x] Bounded, hot-path-safe telemetry collection.

### Phase B - Pipeline instrumentation

- [x] Capture.
- [x] Encode submission and completion.
- [x] Packetisation.
- [x] Network send and receive.
- [x] Frame reassembly.
- [x] Decode submission and completion.
- [x] Video presentation.
- [x] Host audio capture/encode/send.
- [x] Client audio receive/decode/presentation.

### Phase C - Runtime resource metrics

- [x] Queue depth.
- [x] Throughput, packet loss, reorder, jitter, and packet-rate metrics.
- [x] CPU utilisation telemetry.
- [x] GPU and hardware diagnostic telemetry where supported.
- [x] Managed allocation verification.
- [x] Native allocation and memory ownership verification.

### Phase D - Real E2E benchmark fixture

- [x] Dedicated benchmark/test coverage for the real streaming path.
- [x] Real capture, hardware encoding, packetisation, transport, reassembly, decoding, and presentation instrumentation.
- [x] Benchmark fixtures kept separate from production simulation paths.
- [x] Physical Windows 11 benchmark evidence recorded.

### Phase E - Statistical reporting and provenance

- [x] P50, P95, and P99 latency reporting.
- [x] Stage-level latency breakdowns.
- [x] Independent network and audio reporting.
- [x] CPU/GPU/allocation evidence.
- [x] Commit, platform, build, runtime, hardware, and configuration provenance.

### Phase F - Regression gate

- [x] Canonical performance verification script.
- [x] Throughput, latency, and allocation budget checks.
- [x] Integration into the canonical verification pipeline.
- [x] Failing verification status on regression or invariant violation.

## Completion gate

Issue #81 must remain marked complete only while all of the following remain true:

- [x] Real capture-to-presentation latency is measurable.
- [x] Video and audio have independent measurements.
- [x] P50/P95/P99 are calculated from real session observations.
- [x] Network loss, jitter, throughput, and queue metrics are measurable.
- [x] CPU, GPU, managed allocation, and native allocation claims remain evidence-based.
- [x] Frames and packets are correlated using monotonic timestamps and sequence identifiers.
- [x] Exact platform and environment provenance is retained.
- [x] Benchmark fixtures remain separate from production simulation paths.
- [x] Performance regressions can fail the canonical verification gate.
- [x] Required tests and evidence checks pass.

## Next execution rule

Issue #81 is closed. Do not reopen it for additional optimisation work unless a regression invalidates one of its acceptance criteria or a new measurable requirement is introduced. New optimisation work should be tracked as a separate TODO item or GitHub issue with its own acceptance criteria and evidence.
