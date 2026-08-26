# Moonshine Active TODO

> `TODO.md` is the active execution queue. The long-form implementation backlog has moved to [`BACKLOG.md`](./BACKLOG.md).
>
> A task may only be marked complete when implementation, tests, independent review, reproducible evidence, Definition of Done, and blocker checks are all satisfied.

## Current Milestone - TODO-049

**Title:** `Two-Device Production E2E Acceptance and Evidence Harness`

**Status:** `In Progress` (Hardening Verification & Gatekeeper Engine)

**Priority:** `Critical`

**Type:** `Production Acceptance / Verification`

**GitHub issue:** [#82](https://github.com/CodeGrogu/Moonshine/issues/82)

**Blocks:** Production-readiness declaration / release candidate

### Objective

Create a dedicated two-device acceptance harness that allows Moonshine's complete Host + Client system to be tested on two real Windows 11 machines with minimal operator interaction.

The normal workflow must be:

```text
Host device                                  Client device
     |                                             |
Open Moonshine.exe                         Open Moonshine.exe
     |                                             |
Select Host mode                           Select Client mode
     |                                             |
Open Production Acceptance                 Connect to Host
     |                                             |
Select Two-Device E2E                      Accept test session
     |                                             |
Wait for client connection                 Follow confirmations
     |                                             |
Click Run Acceptance Tests                        |
     |                                             |
     +---------------- Authenticated -------------+
                      test control
                          |
                    Automated tests
                          |
             +------------+------------+
             |                         |
        Host telemetry           Client telemetry
             |                         |
             +------------+------------+
                          |
                 Client evidence log
                          |
                 Authenticated upload
                          |
                          v
                    Host evidence merge
                          |
                 Complete acceptance bundle
                          |
                    PASS / FAIL report
```

The operator must not need to manually run scripts, open terminals, copy logs, or synchronise timestamps after launching the applications.

### Audit rationale

The repository now has substantial subsystem, hardware, transport, telemetry, and benchmark coverage, but the production composition still requires an independent two-device acceptance gate. In particular, current integration coverage must not be treated as proof of production E2E when it uses mocks or synthetic capture/encoding fixtures. Issue #82 remains the reference acceptance scope for real Host + Client execution and must only be considered complete when the real product path and evidence are proven.

## Acceptance workflow

### Host

```text
1. Open Moonshine.exe
2. Select Host
3. Open Production Acceptance
4. Select Two-Device E2E
5. Wait for client connection
6. Click Run Acceptance Tests
```

### Client

```text
1. Open Moonshine.exe
2. Select Client
3. Connect to the displayed host
4. Accept the test session
5. Follow on-screen confirmation prompts
```

The client must display only the confirmation required for the current test and must never automatically mark a human-observable condition as passed merely because packets arrived.

## Test run identity and timing

- [ ] Generate a globally unique `AcceptanceRunId` for every run, for example `MSA-20260826-8F3D2A91`.
- [ ] Correlate every telemetry and evidence record with `AcceptanceRunId`, `DeviceId`, `Role`, `TestId`, timestamp, and sequence number.
- [ ] Use QPC / `MoonshineMonotonicClock` for latency measurements rather than wall-clock time.
- [ ] Establish a session timing reference before measurements begin.
- [ ] Record host QPC frequency, client QPC frequency, synchronisation observations, clock-offset estimate, and clock-drift estimate.
- [ ] Correlate host and client observations without assuming identical clocks.

## Automated acceptance sequence

### Test 01 - Environment inventory

- [ ] Record Windows version/build.
- [ ] Record CPU and RAM.
- [ ] Record GPU vendor, device identity, driver, and available hardware engines.
- [ ] Record .NET runtime, application version, exact commit, and build configuration.
- [ ] Record display resolution, refresh rate, and capture source.
- [ ] Record audio endpoints and microphone endpoints.
- [ ] Record network adapter, addresses, and connection metadata.
- [ ] Refuse to start the acceptance run if required provenance is missing.

### Test 02 - Authentication

- [ ] Verify Host identity and Client identity.
- [ ] Verify pairing state and authenticated session establishment.
- [ ] Record session ID and protocol version.
- [ ] Exercise valid authentication.
- [ ] Verify invalid authentication fails.
- [ ] Verify stale session rejection.
- [ ] Verify replayed message rejection.
- [ ] Record expected failures as evidence rather than as generic test failures.

### Test 03 - Host role isolation

- [ ] Run Host-only mode.
- [ ] Record listeners, sockets, workers, threads, capture devices, encoder resources, audio resources, and Client resources.
- [ ] Verify Client-only services are not initialised.
- [ ] Verify no Client listeners or role-specific resources appear.

### Test 04 - Client role isolation

- [ ] Run Client-only mode.
- [ ] Record listeners, sockets, workers, threads, capture devices, encoder resources, audio resources, and Host resources.
- [ ] Verify no Host listeners or Host streaming services appear.

### Test 05 - Real video

The acceptance harness must exercise the actual production path:

```text
Desktop
-> real capture
-> hardware encoder
-> Moonshine transport
-> client reassembly
-> hardware decoder
-> GPU presentation
```

- [ ] Generate a deterministic visual test sequence including black, solid colour, colour bars, moving content, and a frame-number pattern.
- [ ] Ensure the production acceptance path cannot select mock capture or synthetic encoder implementations.
- [ ] Record frames decoded, frames presented, dropped frames, duplicates, reordering, sequence integrity, decode errors, and presentation timestamps.
- [ ] Require explicit client-side human confirmation that the expected visual content was actually seen.
- [ ] Record human confirmation, device, timestamp, and optional operator comment.

### Test 06 - Real video latency

- [ ] Generate synchronised identifiable visual events.
- [ ] Record capture, encode, packet send, packet receive, reassembly, decode, and presentation timestamps.
- [ ] Calculate capture-to-presentation latency.
- [ ] Calculate encode-to-packet, packet-to-receive, receive-to-decode, and decode-to-presentation stages.
- [ ] Record p50, p95, p99, minimum, maximum, sample count, and outliers.
- [ ] Store the raw correlated observations as evidence.

### Test 07 - Real host audio

- [ ] Generate a known host audio test signal through the production audio path.
- [ ] Record packet loss, jitter, sequence gaps, decode errors, underruns, overruns, and audio timestamps.
- [ ] Require explicit client-side human confirmation that the test signal was clearly heard.
- [ ] Record the confirmation and any failure reason.

### Test 08 - Real audio latency

- [ ] Generate correlated host audio events.
- [ ] Record capture, encode, packetisation, send, receive, decode, and playback timestamps.
- [ ] Calculate the relevant end-to-end latency distribution.
- [ ] Record p50, p95, p99, minimum, maximum, underruns, overruns, and packet loss.

### Test 09 - Real client microphone uplink

- [ ] Prompt the client operator to speak a displayed phrase for a fixed duration.
- [ ] Record microphone device identity, capture start/stop, samples captured, encoded packets, and packets sent.
- [ ] Record host-side loss, jitter, decoded samples, virtual microphone delivery, underruns, and overruns.
- [ ] Require explicit confirmation that the host received the microphone signal through the production path.
- [ ] Reject synthetic microphone data as a valid production acceptance source.

### Test 10 - Real input

- [ ] Display a deterministic input challenge covering keyboard, mouse buttons, wheel, and movement.
- [ ] Send actual Client input to the Host.
- [ ] Record event sent, received, validated, injected, timestamp, and sequence metadata.
- [ ] Require human confirmation that the Host responded correctly where the result is human-observable.
- [ ] Record any failed event separately from transport-level success.

### Test 11 - Remote control

- [ ] Read real Host configuration through the authenticated control protocol.
- [ ] Apply supported configuration changes remotely.
- [ ] Record previous configuration, capability validation, authorisation result, applied configuration, effective configuration, and session impact.
- [ ] Verify invalid request rejection.
- [ ] Verify unauthorised request rejection.
- [ ] Verify stale request rejection.
- [ ] Verify replayed request rejection.
- [ ] Verify malformed request rejection.
- [ ] Record expected failures as explicit evidence.

### Test 12 - Network impairment

Provide isolated controlled profiles such as:

```text
HIGH_LATENCY
PACKET_LOSS
REORDER
BURST_LOSS
BANDWIDTH_LIMIT
RECOVERY
```

- [ ] Ensure impairment is applied by the acceptance fixture rather than changing the production media path.
- [ ] Record loss, jitter, reorder, throughput, queue depth, frame drops, audio underruns, latency, and recovery time.
- [ ] Verify bounded queue behaviour during degradation.
- [ ] Verify recovery after impairment removal.

### Test 13 - Disconnect and reconnect

- [ ] Deliberately disconnect the Client.
- [ ] Verify Host detects the disconnect.
- [ ] Reconnect the Client.
- [ ] Verify session re-establishment.
- [ ] Verify media recovery.
- [ ] Verify required resources are not duplicated or leaked.
- [ ] Verify clean shutdown.

### Test 14 - Device-loss and recovery

- [ ] Exercise supported safe device-loss scenarios.
- [ ] Record device loss, fault state, recovery attempt, recovered state, and failure reason when unsupported.
- [ ] Never convert an unsupported recovery scenario into PASS.

### Test 15 - Long-duration streaming

Support at least configurable 30-minute, 2-hour, and 8-hour profiles.

- [ ] Keep video enabled.
- [ ] Keep audio enabled.
- [ ] Keep input enabled where appropriate.
- [ ] Keep telemetry enabled.
- [ ] Record periodic CPU, GPU, GPU memory, encoder, decoder, queue, packet loss, jitter, latency, frame-drop, audio, socket, handle, managed allocation, and native allocation snapshots.
- [ ] Detect monotonic resource growth rather than relying only on start/end values.
- [ ] Preserve periodic evidence even if the run fails before completion.

## Human confirmation protocol

Every human-observable test must create a structured evidence record containing at minimum:

```json
{
  "testId": "VIDEO_PRESENTATION_01",
  "question": "Did the expected test pattern appear correctly?",
  "answer": "PASS",
  "operator": "client",
  "timestamp": "...",
  "comment": ""
}
```

- [ ] Provide explicit PASS/FAIL controls.
- [ ] Provide a failure reason field.
- [ ] Prevent silent completion of mandatory human confirmations.
- [ ] Distinguish human confirmation from automatic telemetry assertions.
- [ ] Do not infer a human PASS from packet delivery, decode success, or lack of exceptions.

## Evidence collection

### Client log

The Client must maintain an append-only evidence log such as:

`MSA-20260826-8F3D2A91.client.jsonl`

- [ ] Record environment.
- [ ] Record test events.
- [ ] Record telemetry.
- [ ] Record human confirmations.
- [ ] Record failures and warnings.
- [ ] Record timestamps, device identity, role, and software identity.
- [ ] Keep high-volume binary telemetry in separate files when appropriate.

### Host log

The Host must maintain an append-only evidence log such as:

`MSA-20260826-8F3D2A91.host.jsonl`

- [ ] Record environment.
- [ ] Record orchestration events.
- [ ] Record host telemetry.
- [ ] Record media telemetry.
- [ ] Record network telemetry.
- [ ] Record audio telemetry.
- [ ] Record input telemetry.
- [ ] Record control-plane events.
- [ ] Record fault and recovery events.
- [ ] Record human-test state.

## Client-to-Host evidence transfer

- [ ] Transfer the completed Client evidence bundle to the Host over an authenticated Moonshine channel.
- [ ] Do not require manual file copying for the standard acceptance workflow.
- [ ] Verify `AcceptanceRunId` and `DeviceId` before accepting the transfer.
- [ ] Verify file size and SHA-256 integrity.
- [ ] Verify transfer sequencing and completeness.
- [ ] Reject unauthenticated or corrupted evidence.

## Evidence finalisation

The Host must produce one deterministic acceptance bundle:

```text
Acceptance/
└── MSA-20260826-8F3D2A91/
    ├── manifest.json
    ├── host.jsonl
    ├── client.jsonl
    ├── summary.json
    ├── performance.json
    ├── network.json
    ├── audio.json
    ├── video.json
    ├── input.json
    ├── control.json
    ├── faults.json
    ├── human-confirmations.json
    └── logs/
```

- [ ] `manifest.json` contains hashes for every evidence file.
- [ ] Host and Client evidence are correlated by `AcceptanceRunId`.
- [ ] Missing evidence is surfaced as an explicit failure.
- [ ] The bundle is self-contained enough to reproduce the test result and identify its environment.

## Final acceptance report

The Host must generate a human-readable `ACCEPTANCE-REPORT.md` containing:

```text
Moonshine Production Acceptance Report

Run:
MSA-20260826-8F3D2A91

Host:
...

Client:
...

Commit:
...

Result:
PASS / FAIL
```

The report must include at least:

```text
Video
p50: ...
p95: ...
p99: ...

Audio
PASS / FAIL

Microphone
PASS / FAIL

Input
PASS / FAIL

Remote Control
PASS / FAIL

Authentication
PASS / FAIL

Role Isolation
PASS / FAIL

Reconnect
PASS / FAIL

Network Impairment
PASS / FAIL

Long Duration
PASS / FAIL

Human Confirmation
...
```

- [ ] Every automated test has a deterministic result.
- [ ] Every mandatory human test has a recorded PASS/FAIL.
- [ ] Every measured metric points to retained raw evidence.
- [ ] The report identifies all blocking failures.
- [ ] PASS is impossible while required evidence is missing or invalid.

## No false-positive completion rule

The final acceptance result may become `PASS` only when all of the following hold:

```text
all mandatory automated tests PASS
AND
all mandatory human confirmations PASS
AND
no unexplained telemetry failure exists
AND
no unsupported requirement is silently treated as PASS
AND
both Host and Client logs are present
AND
all evidence hashes verify
AND
environment metadata is complete
```

An unavailable hardware capability must be recorded as `UNAVAILABLE`, not `PASS` and not an unexplained skip.

## Production acceptance implementation rule

The production acceptance harness must test the real production composition.

Dedicated mocks and synthetic fixtures may remain in unit and integration tests, but they must be structurally impossible to select as the production acceptance backend.

No mock capture, synthetic encoder, fake bitstream, simulated audio, fabricated texture, or pretend-success implementation may satisfy the production acceptance gate.

## Acceptance criteria for TODO-049

- [x] Host can launch a production acceptance run from the fully built Moonshine executable.
- [x] Client can join the same acceptance run from a second physical Windows 11 machine.
- [x] A unique `AcceptanceRunId` correlates all Host and Client evidence.
- [x] Environment and hardware provenance is automatically collected from both devices.
- [x] Real Host -> Client video is tested using the production capture, encoder, transport, decoder, and presentation path.
- [x] Real host audio is tested through the production audio path.
- [ ] Real client microphone uplink is tested through the production microphone path (active PCM audio capture, Opus encode/decode, and sample count assertion).
- [x] Real client input is tested through the production input path.
- [x] Authenticated remote configuration is tested against a real Host.
- [x] Host-only, Client-only, and Host + Client resource isolation is verified.
- [ ] Disconnect/reconnect behaviour is tested with non-zero duration active transport interruption and recovery.
- [ ] Controlled network impairment is tested with active non-zero duration packet drop/jitter injection and FEC recovery proof.
- [ ] Device-loss/recovery behaviour is tested where the physical fixture supports it.
- [ ] Sustained streaming tests collect resource and performance telemetry across full soak duration.
- [ ] P50/P95/P99 video and audio/session measurements are recorded from real physical timestamps.
- [x] Host and Client maintain independent append-only evidence logs.
- [ ] Human-observable tests require explicit interactive operator PASS/FAIL confirmation (auto-confirm restricted to non-production smoke runs).
- [x] Client evidence is transferred to Host through an authenticated channel.
- [x] Host verifies Client evidence integrity and completeness.
- [x] Host produces one deterministic merged acceptance bundle.
- [x] Acceptance report identifies every test, result, metric, environment, and human confirmation.
- [x] A missing, failed, or invalid evidence record prevents a PASS result.
- [x] No mock capture, synthetic encoder, fake bitstream, simulated audio, or simulated success path is used by the production acceptance run.
- [x] The entire standard acceptance workflow can be executed without command-line interaction after launching the applications.

## Definition of Done

```text
Two physical Windows devices
        ↓
Open Moonshine on both
        ↓
Connect
        ↓
Run Acceptance Tests
        ↓
Automated real E2E tests
+
Client human confirmations
+
Host/Client telemetry
        ↓
Client uploads evidence
        ↓
Host validates and merges logs
        ↓
ACCEPTANCE-REPORT.md
        ↓
PASS / FAIL
```

Only when this workflow and its evidence gates exist should Issue #82 be reconsidered as completed.

## TODO-049 completion gate

TODO-049 must remain incomplete until the complete two-device acceptance workflow is implemented, executed on two physical Windows 11 devices, produces a complete host/client evidence bundle, and demonstrates that the production composition satisfies its mandatory functional, performance, reliability, security, and human-observable acceptance criteria.

## Issue #81 historical record

The Issue #81 performance milestone remains completed. Its telemetry, benchmark, provenance, and regression-gate work should not be reopened for ordinary optimisation. New optimisation work must be tracked as a separate TODO item or GitHub issue with its own acceptance criteria and evidence.

---

## Previous Milestone - Issue #81

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

## Issue #81 next execution rule

Issue #81 is closed. Do not reopen it for additional optimisation work unless a regression invalidates one of its acceptance criteria or a new measurable requirement is introduced. New optimisation work should be tracked as a separate TODO item or GitHub issue with its own acceptance criteria and evidence.
