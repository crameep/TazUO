# Closed Beta Readiness and Triage

This document defines how to run closed beta checks for the runtime.

## Required signals

- Runtime telemetry metrics emitted every tick.
- Runtime fault events recorded and bucketed.
- Bounded queue pressure (`runtime.tick.dropped_actions`) monitored.

## Triage artifacts

- `runtime-beta-report-*.json` written to Legion log path on unload.
- Fault buckets grouped by reason.
- Release gate recommendation:
  - `GREEN` when fault count is 0.
  - `REVIEW_REQUIRED` otherwise.

## Blocker policy

- P0: crash, deadlock, unrecoverable runtime corruption.
- P1: repeated watchdog faults under normal load.
- Ship gate requires no open P0/P1 blockers.

## Suggested beta flow

1. Run templates and representative user scripts together.
2. Trigger lifecycle transitions and reconnect paths.
3. Review generated beta report and fault buckets.
4. Resolve P0/P1 before go-live decision.
