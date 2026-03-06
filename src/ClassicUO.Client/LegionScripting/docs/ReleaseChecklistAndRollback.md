# Release Checklist and Rollback Playbook

## Pre-release checklist

- LegionScript/runtime unit suites green.
- No open P0/P1 runtime blockers.
- Closed beta report reviewed (`runtime-beta-report-*.json`).
- Telemetry confirms bounded tick/action behavior.
- Starter templates verified (`healer`, `potion`, `combo`).

## Go/No-Go gates

- Go if: all quality gates pass and blocker policy is clear.
- No-Go if: watchdog storms, repeated fault buckets, or queue saturation regressions.

## Rollback plan

1. Disable new runtime entrypoints (`runstarter`/runtime template usage) operationally.
2. Revert to prior stable branch/commit.
3. Preserve beta report artifacts for postmortem.
4. Re-run LegionScript suite to confirm stable fallback.

## Post-rollback validation

- Legacy script start/stop still functional.
- Main update loop remains non-blocking.
- No data/config path regressions in storage mapping.
