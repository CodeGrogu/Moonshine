# Moonshine Active TODO

> `TODO.md` is the active execution queue. The long-form implementation backlog has moved to [`BACKLOG.md`](./BACKLOG.md).
>
> A task may only be marked complete when implementation, tests, independent review, reproducible evidence, Definition of Done, and blocker checks are all satisfied.

## Current Task - Issue #81

**Title:** `[PERF] Build Real End-to-End Streaming Benchmark and Latency Instrumentation`

**Status:** `In Progress`

**Priority:** `High`

**GitHub issue:** [#81](https://github.com/CodeGrogu/Moonshine/issues/81)

**Audit date:** 2026-08-26

### Objective

Create the measurement system that proves Moonshine is highly performant using real execution rather than claimed targets.

### Issue #81 acceptance criteria

- [ ] End-to-end latency can be measured from real capture to real presentation.
- [ ] Network and audio paths have independent measurements.
- [ ] Allocation counts are measured rather than inferred.
- [ ] Benchmark results are reproducible and tied to an exact commit/environment.
- [ ] Performance regressions can fail CI or the canonical verification gate.

## Issue #81 audit status

| Requirement | Status | Assessment |
|---|:---:|---|
| Real capture → real presentation latency | ❌ | **Missing** |
| Independent network measurements | 🟡 | Loopback/transport evidence exists, but not real E2E |
| Independent audio measurements | 🟡 | Strong subsystem benchmarks, no E2E telemetry |
| p50 | ❌ | No E2E percentile collector |
| p95 | ❌ | No E2E percentile collector |
| p99 | ❌ | No E2E percentile collector |
| Throughput | 🟡 | Existing micro/loopback measurements |
| Queue depth | 🟡 | Queue APIs exist, no telemetry system |
| Packet loss | 🟡 | Existing loopback tests |
| Jitter | 🟡 | Existing loopback tests |
| CPU usage | ❌ | No unified E2E collector |
| GPU usage | ❌ | No unified GPU telemetry |
| Allocations | 🟡 | Microbenchmarks only |
| Frame/packet correlation | 🟡 | Frame IDs/timestamps partially exist |
| Native high-resolution timing | ✅ | QPC foundation exists |
| Managed integration | 🟡 | Timing exists, telemetry architecture doesn't |
| No hot-path allocation | 🟡 | Individual paths demonstrated |
| Real hardware | 🟡 | Hardware encoder tests exist |
| Real network | ❌ | No canonical real-network E2E harness |
| Separate benchmark fixtures | 🟡 | Benchmark project exists, but E2E fixture architecture doesn't |
| Environment metadata | 🟡 | Partial |
| GPU/driver metadata | ❌ | Missing |
| Reproducible results | 🟡 | Provenance foundation exists |
| Regression CI gate | ❌ | Benchmark CI uploads results but doesn't gate regressions |

## Active implementation sequence

### Phase A - Telemetry primitives

- [ ] Define a canonical telemetry event structure with no hot-path managed allocations.
- [ ] Define one monotonic timestamp representation shared across native and managed boundaries.
- [ ] Define canonical frame and packet correlation identifiers.
- [ ] Add a bounded telemetry buffer suitable for high-frequency stage events.

### Phase B - Pipeline instrumentation

- [ ] Instrument capture.
- [ ] Instrument encode submission and completion.
- [ ] Instrument packetisation.
- [ ] Instrument network send and receive.
- [ ] Instrument frame reassembly.
- [ ] Instrument decode submission and completion.
- [ ] Instrument video presentation.
- [ ] Instrument host audio capture/encode/send.
- [ ] Instrument client audio receive/decode/presentation.

### Phase C - Runtime resource metrics

- [ ] Record queue depth at relevant pipeline boundaries.
- [ ] Record bytes, packets, loss, reorder, jitter, and throughput.
- [ ] Record CPU utilisation.
- [ ] Record GPU utilisation and relevant encode/decode/presentation activity.
- [ ] Measure managed allocations in the benchmark session.
- [ ] Measure native allocations in the benchmark session.
- [ ] Record copy counts and synchronisation stalls where observable.

### Phase D - Real E2E benchmark fixture

- [ ] Create a benchmark-only real host/client fixture.
- [ ] Use real capture, real hardware encoding, real packetisation, real UDP sockets, real reassembly, real hardware decode, and real presentation.
- [ ] Keep the fixture separate from production simulation paths.
- [ ] Run the benchmark on real Windows 11 hardware and real network traffic.

### Phase E - Statistical reporting and provenance

- [ ] Calculate p50, p95, and p99 for end-to-end latency.
- [ ] Calculate stage-level latency distributions.
- [ ] Report network and audio metrics independently.
- [ ] Record CPU/GPU/allocation statistics.
- [ ] Record commit, OS/build, CPU, GPU, GPU driver, compiler, runtime, codec, resolution, frame rate, bitrate, network configuration, and benchmark timestamp.
- [ ] Store reproducible result artefacts tied to the exact commit.

### Phase F - Regression gate

- [ ] Define a canonical baseline format.
- [ ] Compare benchmark results with the baseline.
- [ ] Define explicit regression thresholds for latency, throughput, loss, jitter, and allocations.
- [ ] Fail the canonical verification gate when a protected metric regresses beyond its threshold.
- [ ] Publish the benchmark comparison as CI evidence.

## Current blockers

- [ ] Real host-to-client streaming path is not yet an operational end-to-end production path.
- [ ] No unified E2E telemetry/correlation model spans capture through presentation.
- [ ] No real-network E2E benchmark harness exists.
- [ ] No resource telemetry collector covers CPU, GPU, queue depth, and allocations for the real session.
- [ ] No canonical percentile report or regression gate exists for Issue #81.

## Completion gate

Do **not** mark Issue #81 complete until all of the following are true:

- [ ] Real capture-to-presentation latency is measured on a real host/client pair.
- [ ] Video and audio have independent end-to-end measurements.
- [ ] p50/p95/p99 are calculated from real session observations.
- [ ] Network loss, jitter, throughput, and queue depth are measured from real traffic.
- [ ] CPU, GPU, managed allocation, and native allocation measurements are evidence-based.
- [ ] Frames and packets are correlated across the pipeline using monotonic timestamps and sequence identifiers.
- [ ] Results include exact platform and environment provenance.
- [ ] Benchmark fixtures remain separate from production simulation paths.
- [ ] A regression comparison can fail the canonical verification gate.
- [ ] All tests and required review/evidence checks pass.

## Superseded completion claim

The previous long-form `TODO.md` contained a `TODO-023` entry marking Issue #81 as `Completed`. The 26 August 2026 audit determined that claim was premature because the required real end-to-end telemetry and benchmark gate do not yet exist. That backlog history is retained in `BACKLOG.md` as superseded context; this active file is authoritative for the current state.
