# Organizer Improvements Implementation Plan

## Objective

Deliver the organizer/tome design in production-safe phases with backward compatibility,
clear checkpoints, and minimal regression risk.

## Delivery Strategy

- Build backend and migration first.
- Add tome execution engine second.
- Layer UI and command/API changes after backend contracts stabilize.
- Validate each phase with unit tests + manual in-client checks.

## Phase 1: Data Contracts and Compatibility

- [ ] Add `DestType` enum (`ConfigDefault`, `Container`, `Tome`).
- [ ] Add `TomeMode` enum (`FillAll`, `TargetEach`, `TargetContainer`, `TargetRepeat`).
- [ ] Update `OrganizerConfig` with `Group`, `Recursive`, `DestinationType`, `TomeDefinitionName`.
- [ ] Update `OrganizerItemConfig` with `Name`, `RegexSearch`, `DestinationType`, `TomeDefinitionName`.
- [ ] Change `OrganizerItemConfig.Graphic` to `int` for `-1` wildcard support.
- [ ] Add JSON context support for all new/updated types.
- [ ] Keep defaults backward compatible for existing `OrganizerConfig.json` files.

### Exit Criteria

- Existing profiles deserialize without errors.
- New fields persist and round-trip in JSON.

## Phase 2: Organizer Matching and Traversal

- [ ] Update organizer match logic to: Graphic check -> Hue check -> Regex check.
- [ ] Reuse `RegexHelper` for cached compiled regex evaluation.
- [ ] Use OPL text (`World.OPL.TryGetNameAndData`) with `ItemData.Name` fallback.
- [ ] Add recursive source traversal helper with destination-container loop guard.
- [ ] Add backend helper to scan a targeted container and produce unique item entries.

### Exit Criteria

- Wildcard graphic/hue matching works.
- Regex-only entries work (`Graphic=-1`).
- Recursive mode includes subcontainers and excludes destination container loops.

## Phase 3: Run State and Progress

- [ ] Add `OrganizerRunState` model on `OrganizerAgent`.
- [ ] Track queued, completed, and skipped actions.
- [ ] Integrate move action callbacks for progress updates.
- [ ] Emit start/completion journal messages with moved/skipped summary.

### Exit Criteria

- Progress metrics update during runs and reset after completion.
- Completion summary is always emitted for non-empty runs.

## Phase 4: Tome Definitions + Migration

- [ ] Add `TomeDefinition` model and `TomeDefinitionContext` JSON context.
- [ ] Implement `TomeManager` for load/save CRUD using `TomeDefinitions.json`.
- [ ] Add migration in `OrganizerAgent.Load()` from deprecated per-config tome fields.
- [ ] Uniquify generated migrated tome names deterministically.
- [ ] Clear deprecated fields from organizer configs after successful migration save.

### Exit Criteria

- Old configs migrate exactly once and preserve data.
- Re-launch does not duplicate migrated tomes.

## Phase 5: TomeActionRunner Engine

- [ ] Implement `TomeActionRunner` state machine in `Game/Managers/`.
- [ ] Support all four modes (`FillAll`, `TargetEach`, `TargetContainer`, `TargetRepeat`).
- [ ] Add per-state timeout handling and journal error messages.
- [ ] Add optional `RequiresWalk` behavior before first tome interaction.
- [ ] Add delay handling between operations.

### Exit Criteria

- Runner completes/aborts deterministically with no stuck state.
- Timeout failures do not crash organizer processing.

## Phase 6: Organizer Execution Integration

- [ ] Resolve per-item or config-level destination as container/tome/default.
- [ ] Queue container moves via `ObjectActionQueue` (existing behavior path).
- [ ] Group tome-destination items by tome definition and enqueue into runner.
- [ ] Run both operation classes in one organizer invocation with shared progress reporting.

### Exit Criteria

- Mixed destination organizers execute both move and tome paths correctly.
- Progress reflects both types of operations.

## Phase 7: Organizer UI Refactor

- [ ] Remove legacy tome capture section from organizer tab.
- [ ] Add group-sorted organizer list with collapsible headers and ungrouped `General` bucket.
- [ ] Add group context actions (`Enable All`, `Disable All`, `Run Group`).
- [ ] Add run progress UI at top of details panel.
- [ ] Add item fields for name + regex.
- [ ] Add destination dropdown behavior for config-level and per-item destination.
- [ ] Add `Scan Container` flow and summary messaging.

### Exit Criteria

- Organizer tab exposes all new non-tome config controls cleanly.
- Group actions and scan workflow function without runtime errors.

## Phase 8: New Tomes Tab

- [ ] Add `TomeTabContent` with list/details layout.
- [ ] Add `Set Tome` targeting workflow.
- [ ] Reuse `GumpButtonCapture` for `Capture Gump Button` flow.
- [ ] Add mode selector + conditional target-container fields.
- [ ] Add delay and walk toggles.
- [ ] Wire new tab into `AssistantWindow` lifecycle.

### Exit Criteria

- Tome CRUD and capture flows are fully accessible from UI.
- No references remain to removed organizer-tab tome inputs.

## Phase 9: Commands and Scripting API

- [ ] Extend organizer command parser with `-organize group <name>`.
- [ ] Add group info to `-organizerlist` output.
- [ ] Add `API.OrganizerGroup("GroupName")` method.

### Exit Criteria

- Command and script callers can trigger group runs reliably.

## Phase 10: Test and Validation Matrix

- [ ] Add unit tests for model defaults and JSON round-trip.
- [ ] Add unit tests for migration behavior.
- [ ] Add unit tests for matching behavior (wildcards + regex).
- [ ] Add command/API tests for group execution entry points.
- [ ] Run `dotnet test tests/ClassicUO.UnitTests/`.
- [ ] Perform in-client verification for each tome mode and organizer progress UX.

## Risk Controls

- Keep migration idempotent and guarded by source-field presence.
- Ensure all timeouts clear pending auto-target/gump state before advancing.
- Prevent recursive traversal from touching destination container branch.
- Define single active organizer run policy to avoid conflicting state updates.

## Recommended PR Slices

1. Models/serialization + matching + recursion + tests.
2. Tome manager + migration + tests.
3. Tome action runner + organizer integration + tests.
4. Organizer UI refactor + Tomes tab.
5. Command/API updates + final validation pass.
