# Moonshine Active TODO

> `TODO.md` is the active execution queue. The long-form implementation backlog has moved to [`BACKLOG.md`](./BACKLOG.md).
>
> A task may only be marked complete when implementation, tests, independent review, reproducible evidence, Definition of Done, and blocker checks are all satisfied.

## Current Milestone - TODO-050

**Title:** `Migrate Moonshine Application to WinUI 3 and Integrate Production Acceptance Centre`

**Status:** `Not Started`

**Priority:** `Critical`

**Type:** `Application / UI Architecture / Production Acceptance`

**Blocks:** Final completion of TODO-049 and production-readiness declaration

### Objective

Move Moonshine's primary user-facing application experience from the current CLI/console-driven runner to a proper C# WinUI 3 desktop application while preserving the existing C#/.NET and C++ backend architecture.

The WinUI application becomes the authoritative production UI for Host, Client, Host + Client, streaming, connection management, and the two-device production acceptance workflow.

The CLI remains available as a developer/debug interface, but it must not be the production acceptance interface or the mechanism used to satisfy human-observable acceptance criteria.

### Architecture rule

Do not rewrite the streaming backend to fit the UI.

```text
WinUI 3 application
        |
        v
Application orchestration
        |
        +---------------------------+
        |                           |
   Moonshine Core            Native C++ backend
        |                           |
        +-----------+---------------+
                    |
              Host / Client
              media pipeline
```

The WinUI layer must consume existing backend services through explicit application/orchestration interfaces rather than duplicating capture, encoding, decoding, networking, telemetry, or protocol logic.

---

## TODO-050 Acceptance Criteria

### Application shell

- [ ] Create the production C# WinUI 3 application shell.
- [ ] Establish the root navigation model for Host, Client, Host + Client, settings, diagnostics, and acceptance.
- [ ] Preserve the single-application runtime role model.
- [ ] Host-only, Client-only, and Host + Client remain real runtime states.
- [ ] Disabled roles do not initialise their backend resources merely because a UI page exists.
- [ ] Application startup and shutdown are deterministic and fail closed.

### Host experience

- [ ] Provide Host setup/status UI.
- [ ] Show active Host state, client connection state, session state, and relevant backend faults.
- [ ] Show real streaming telemetry without fabricating unavailable values.
- [ ] Provide clear Host lifecycle controls.

### Client experience

- [ ] Provide Client discovery/connection UI.
- [ ] Show connection, authentication, session, decoder, audio, and input state.
- [ ] Provide the real decoded video presentation surface in the WinUI application.
- [ ] Provide clear connection-loss and reconnect state.
- [ ] Keep the video presentation surface separate from diagnostic/acceptance overlays so it can be evaluated by a human operator.

### Production Acceptance Centre

Add a first-class WinUI workflow:

```text
Host
  -> Open Production Acceptance
  -> Select Two-Device E2E
  -> Wait for Client
  -> Start Acceptance Run

Client
  -> Connect to Host
  -> Accept Acceptance Session
  -> Follow on-screen confirmation prompts

Both
  -> Execute automated tests
  -> Collect telemetry
  -> Record evidence
  -> Complete acceptance bundle
```

- [ ] Host can start a production acceptance run from the WinUI application.
- [ ] Client can join the acceptance session from the WinUI application.
- [ ] A unique `AcceptanceRunId` is displayed on both devices.
- [ ] Test progress is visible on both devices.
- [ ] Current test name, status, and failure state are visible.
- [ ] The UI prevents a user from confirming the wrong test.
- [ ] The UI distinguishes automated assertions from human observations.
- [ ] Acceptance cannot silently continue past a required human confirmation.
- [ ] Smoke/`--auto-confirm` execution remains explicitly non-production.

### Human-observable video acceptance

The Client acceptance surface must be capable of showing the actual streamed video while asking the operator to confirm what was observed.

- [ ] Display the actual production video stream during video acceptance tests.
- [ ] Display deterministic test content such as colour bars, moving patterns, and frame-number patterns where appropriate.
- [ ] Ask explicit PASS/FAIL questions after the correct test content is displayed.
- [ ] Record operator identity/role, answer, timestamp, and optional comment.
- [ ] A packet/decode success must never automatically become a human PASS.

### Human-observable audio acceptance

- [ ] Display instructions for the current host-audio test.
- [ ] Tell the Client operator when the test signal is being generated.
- [ ] Ask explicit PASS/FAIL confirmation for audibility/quality.
- [ ] Record the answer and failure reason.
- [ ] Do not infer audibility from packet or decoder success.

### Human-observable microphone acceptance

- [ ] Prompt the Client operator to speak a displayed phrase for a controlled duration.
- [ ] Show microphone capture state and selected device.
- [ ] Show whether samples are actually being captured.
- [ ] Host acceptance UI shows whether real decoded microphone PCM reached the production Moonshine Microphone path.
- [ ] Require explicit human confirmation that the host received the expected microphone signal.
- [ ] No simulated microphone signal may satisfy the acceptance test.

### Human-observable input acceptance

- [ ] Display a deterministic keyboard/mouse/controller challenge.
- [ ] Show which event is currently being tested.
- [ ] Record real event sent/received/validated/injected telemetry.
- [ ] Ask for human confirmation where the expected result is visually or physically observable.
- [ ] Record failures separately from network delivery success.

### Acceptance telemetry and evidence UI

- [ ] Display real Host and Client telemetry during the acceptance run.
- [ ] Display measured latency, throughput, packet loss, jitter, queue depth, CPU, GPU, frame statistics, and audio health where available.
- [ ] Clearly distinguish `UNAVAILABLE` from zero.
- [ ] Show the current test's raw evidence status without allowing manual alteration of measured telemetry.
- [ ] Allow the operator to inspect blocking failures before finalising the run.

### Evidence workflow

- [ ] Maintain separate append-only Host and Client evidence records.
- [ ] Correlate records with `AcceptanceRunId`, `DeviceId`, `Role`, `TestId`, monotonic timestamps, and sequence information.
- [ ] Transfer Client evidence to Host through the authenticated acceptance channel.
- [ ] Display evidence transfer and integrity verification status in the UI.
- [ ] Generate the merged acceptance bundle on the Host.
- [ ] Generate `ACCEPTANCE-REPORT.md` and a machine-readable summary.
- [ ] Display the final PASS/FAIL result in both applications.
- [ ] Prevent final PASS when mandatory evidence or confirmations are missing.

### Build and packaging

- [ ] Produce a fully deployable Windows x64 application from the normal build process.
- [ ] Package all required managed/native runtime dependencies.
- [ ] Ensure the acceptance workflow works from the packaged application rather than requiring the repository or CLI tools.
- [ ] Verify the packaged application can be installed/launched independently on both physical Windows 11 devices.
- [ ] Record exact application version and commit provenance in the acceptance environment record.

### Definition of Done

```text
Build Moonshine
      |
      v
Open WinUI application on Host + Client
      |
      v
Connect real devices
      |
      v
Run Production Acceptance
      |
      +------------------------------+
      |                              |
Automated backend tests      Human confirmations
      |                              |
      +------------------------------+
                     |
                     v
              Evidence transfer
                     |
                     v
              Host evidence merge
                     |
                     v
            ACCEPTANCE-REPORT.md
                     |
                     v
                 PASS / FAIL
```

TODO-050 is complete only when this WinUI workflow is implemented, packaged, exercised on both physical devices, and ready to support the final TODO-049 acceptance run.

---

## Blocked Milestone - TODO-049

**Title:** `Two-Device Production E2E Acceptance and Evidence Harness`

**Status:** `Blocked by TODO-050` 

**Priority:** `Critical`

**GitHub issue:** [#82](https://github.com/CodeGrogu/Moonshine/issues/82)

### Remaining TODO-049 release gates

- [ ] Real client microphone uplink is proven through active PCM capture, Opus encode/decode, host reception, and production Moonshine Microphone delivery.
- [ ] Disconnect/reconnect is proven with an actual non-zero-duration transport interruption and recovery.
- [ ] Network impairment is applied for a non-zero duration and produces measurable loss/jitter/recovery evidence.
- [ ] Supported device-loss/recovery behaviour is physically verified, or explicitly recorded as unavailable where the physical fixture cannot safely exercise it.
- [ ] Sustained streaming passes the minimum 30-minute production soak, with 2-hour and 8-hour profiles available.
- [ ] P50/P95/P99 video and audio/session measurements are recorded from real physical timestamps.
- [ ] Human-observable tests are confirmed interactively by the Client operator.
- [ ] Final acceptance report contains complete Host and Client evidence with all hashes verified.
- [ ] Production PASS is impossible when a mandatory automated or human-observable requirement is missing, failed, or unverified.

### Evidence rule

The CLI acceptance runner may remain useful for developer smoke testing, but it cannot close TODO-049. The production acceptance run must use the WinUI application so the Client operator can actually see the streamed video, hear the test audio, interact with the microphone/input tests, and explicitly confirm human-observable results.

TODO-049 must only return to active completion work after TODO-050 has provided the required production UI surface.

---

## Previous Milestone - Issue #81

**Title:** `[PERF] Build Real End-to-End Streaming Benchmark and Latency Instrumentation`

**Status:** `Completed`

**Priority:** `High`

**GitHub issue:** [#81](https://github.com/CodeGrogu/Moonshine/issues/81)

**Completion date:** 2026-08-26

### Completion evidence

- `c0a30f5` - real end-to-end streaming benchmark suite and latency verification gates.
- `a8cc681` - automated performance regression and zero-allocation gatekeeper.
- `d5d1b44` - TODO provenance synchronisation for the Issue #81 work.
- `29eda6b` - benchmark and verification provenance updates.

Issue #81 remains closed. Its telemetry, benchmark, provenance, and regression-gate work must not be reopened for ordinary optimisation. New optimisation work requires a separate TODO item or GitHub issue with its own acceptance criteria and evidence.
