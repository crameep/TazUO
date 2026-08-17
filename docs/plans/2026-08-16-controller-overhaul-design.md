# Controller Overhaul Design

## Goal

Make TazUO fully playable from a controller with no mouse or keyboard — log in,
move, fight, target, loot, manage inventory, and use gumps. Delivered as four
phases that each ship something usable on their own, so the work can stop at any
phase boundary without leaving the client in a worse state than it started.

## Current State

Controller support already exists and is lopsided: the **binding** layer is in
good shape, the **game feel** layer is not.

**Working today:**
- `HotkeyBinding` (`Game/Managers/Hotkeys/HotkeyBinding.cs`) treats controller
  chords as first-class alongside key/mouse/wheel, with conflict detection and
  a precedence order (controller > mouse button > wheel > keyboard).
- `SpellBarManager` supports per-slot controller chords.
- `BaseOptionsGump` has capture UI for controller bindings.
- `Control` exposes `OnControllerButtonDown/Up` virtual hooks (`Control.cs:857`).
- `Controller` (`Input/Controller.cs`) tracks button state and exposes
  `AreButtonsPressed` with exact/inexact chord matching.

**Defects and gaps:**

1. **Movement** (`Game/Scenes/GameSceneInputHandler.cs:98`) — `MoveCharByController`
   is an eight-branch threshold cascade with no radial deadzone; the only gate is
   `ThumbSticks.Left != Vector2.Zero`. Run/walk is decided by
   `dir.X > 0.5 || dir.Y > 0.5` — a per-axis test rather than magnitude — so a
   diagonal push of (0.45, 0.45) walks while a cardinal 0.55 runs. Stick magnitude
   is otherwise discarded entirely, so there is no analog speed.

2. **Cursor** (`Input/Mouse.cs:212`) — the right stick moves the **real OS cursor**
   via `SDL_WarpMouseInWindow`, applied once per `Update()` with **no delta-time**,
   so pointer speed scales with framerate. Deadzone is `!= Vector2.Zero`, so any
   stick drift creeps the pointer indefinitely. Response is linear and controlled
   by a single integer (`ControllerSensitivity`, default 10).

3. **Triggers** (`GameController.cs:1148`) — SDL reports triggers as axes, and the
   current workaround forwards them as the `BACK` and `GUIDE` **button** enums with
   hardcoded thresholds (32000 down / 5000 up). This burns two real buttons and
   discards analog travel.

4. **No targeting assistance.** Selecting a specific mobile in an isometric world
   with a drifting pointer is the single biggest pain point, and nothing addresses it.

5. **No UI focus traversal.** The per-control hooks exist but nothing consumes them;
   every gump falls back to the pointer.

6. **Device plumbing.** `PlayerIndex.One` is hardcoded in both
   `Mouse.Update` and `MoveCharByController`; no hot-plug handling, no rumble,
   no per-vendor button glyphs.

---

## Implementation Status (2026-08-17)

Work is on `feature/controller-overhaul`. Tests: 678 passing, up from a 604 baseline.

**Phase 0 — complete.** Sections 1-5 all landed.

**Phase 1 — largely complete.** Sections 6-8 landed. Section 9 (magnetic snap) was
made redundant in practice: cycling parks the cursor directly on the selected
entity, which is the effect snap was meant to approximate, so a separate
attraction force has not been added and may not be needed.

**Phase 2 — partial.** Section 10's scoring is implemented and tested as a pure
function, but is *not yet wired to the d-pad*, so gump traversal does not work
in game yet. Sections 11-12 not started.

**Phase 3 — partial.** Device plumbing for multi-pad and hot-plug state landed.
Button glyphs and rumble not started. Section 13 text entry still blocked on the
open decision below.

### Deviations from the original design

**Warping could not simply be removed (section 6).** When
`RunMouseInASeparateThread` is on — the default — the client hands the pointer
graphic to SDL via `SDL_SetCursor`, so the OS cursor *is* the visible pointer.
Removing the warp there would leave the drawn cursor frozen while an invisible
position moved. The virtual cursor is now authoritative and warping mirrors it,
which still buys clamping, arbitration and snapping. Fully eliminating the warp
means drawing the cursor client-side in both modes; that is a larger change and
is not done.

**Sensitivity needed no migration.** Rather than rewriting the persisted value,
`ControllerMouseSensativity` is scaled by a 60 FPS reference at the point of use,
so existing profiles feel unchanged at the default framerate and become correct
at every other one.

**Trigger migration was wider than expected.** Rewriting BACK/GUIDE bindings had
to cover the spell bar's own persisted `int[][]` as well as `HotkeyBinding`.
Counter bar and script hotkeys needed nothing, as both read `HotkeyBinding`.

**Analog movement speed is not achievable.** UO's movement protocol has only walk
and run, so stick magnitude can drive the walk/run threshold and nothing finer.

## Design Principles

**The pointer is the floor; selection is the accelerator.**

UO gumps are server-authored. Container contents sit at arbitrary x/y and may
overlap, shop and vendor layouts come from the shard, and custom gumps arrive as
packets the client has never seen. There is no logical widget order to traverse.
If discrete selection were the *only* input model, every such surface would need
bespoke focus handling, and an unfamiliar gump would make the client unusable.

Keeping a pointer available everywhere guarantees parity from day one. Selection
layers on top to make the common cases fast, and can never regress a surface into
"impossible" — only "slower".

**Every phase ships.** Phase 0 improves the current experience with no new
concepts. Each later phase is independently valuable.

---

## Part A: Phase 0 — Input Plumbing Foundation

No new features. Correct the maths that everything else will sit on. This is the
prerequisite for judging whether later phases are even needed as badly as assumed.

### 1. Shared Stick Processing

New static class `Input/ControllerAxis.cs` holding the pure functions, so they are
unit-testable without a game loop:

```
Vector2 ApplyRadialDeadzone(Vector2 raw, float inner, float outer)
float    ApplyResponseCurve(float magnitude, float exponent)
Direction ToOctant(Vector2 dir)
```

- **Radial deadzone** uses `raw.Length()`, not per-axis tests. Below `inner`,
  returns `Vector2.Zero`. Above, rescales magnitude to ramp 0..1 across
  `inner..outer` so there is no jump at the deadzone edge.
- **Response curve** applies `pow(magnitude, exponent)` with a configurable
  exponent (1.0 = linear, higher = finer control near centre).
- **Octant mapping** uses `Math.Atan2` and divides the circle into eight 45°
  sectors, replacing the eight-branch cascade. Deterministic and symmetric.

### 2. Movement Fix

Rewrite `MoveCharByController` to use the shared helpers:

- Gate on deadzone rather than `!= Vector2.Zero`.
- Derive direction from `ToOctant`.
- Derive run/walk from **magnitude** against a configurable threshold, so the
  walk/run boundary is a circle rather than a square.
- Keep the existing `Player.Walk(Direction, run)` call, so nothing downstream changes.

### 3. Frame-Rate-Independent Cursor

`Time.Delta` is already assigned immediately before `Mouse.Update()`
(`GameController.cs:484-487`), so it is available with no plumbing changes.

Change cursor motion from per-frame pixels to **pixels per second**:

```
Position += dir * CursorSpeed * Time.Delta
```

`ControllerMouseSensativity` is currently persisted as an int in `Profile`
(`Configuration/Profile.cs:855`). Keep the property name and JSON key for
compatibility, but reinterpret the stored value as px/sec via a migration
multiplier so existing profiles land at roughly their current feel rather than
becoming ~60x slower or faster.

### 4. Real Analog Triggers

Handle `SDL_GAMEPAD_AXIS_LEFT_TRIGGER` / `SDL_GAMEPAD_AXIS_RIGHT_TRIGGER` as axes
and expose `Controller.LeftTrigger` / `RightTrigger` as floats, plus derived
booleans with hysteresis (separate press and release thresholds) so a trigger
resting near the threshold does not chatter.

**Migration matters here.** Existing user profiles contain bindings that reference
`SDL_GAMEPAD_BUTTON_BACK` and `SDL_GAMEPAD_BUTTON_GUIDE` as trigger stand-ins. On
load, rewrite those to the new dedicated trigger identifiers, otherwise every
existing trigger binding silently breaks. This applies to `HotkeyBinding`,
`SpellBarManager.ControllerButtons`, `CounterBarHotkeysManager`, and
`ScriptHotkeysManager`.

Once migrated, `BACK` and `GUIDE` become bindable buttons in their own right.

### 5. Options Exposure

Add to the controller section of the options gump: inner/outer deadzone,
response curve exponent, cursor speed, run threshold. All per-profile.

**Phase 0 acceptance:** cursor speed is identical at 30 and 144 FPS; a released
stick produces zero cursor drift; diagonal and cardinal run thresholds match;
triggers report analog values and existing trigger bindings still fire.

---

## Part B: Phase 1 — Virtual Cursor and World Selection

### 6. Client-Owned Virtual Cursor

Stop calling `SDL_WarpMouseInWindow`. Instead, when input is coming from the pad,
the client owns `Mouse.Position` directly.

This is cheaper than it sounds: `GameCursor.Draw` already renders at
`Mouse.Position` (`Game/GameCursor.cs:242+`), and `UIManager` already consumes
`Mouse.Position`, so no new rendering or event path is required. What changes is
only *who writes the value*.

Benefits over warping: the pointer can be clamped to the viewport, it stops
fighting the desktop and multi-monitor setups, and it can be nudged by snapping
without the OS cursor visibly teleporting.

**Input source arbitration:** track which device moved last. Physical mouse motion
takes ownership and hides pad-specific affordances; stick motion takes it back.
This prevents the two fighting each other, which is the main failure mode of
virtual-cursor implementations.

### 7. World Candidate Selection

New `Game/Managers/ControllerTargetManager.cs`.

- **Enumerate** candidates from `World.Mobiles` and `World.Items` that are on
  screen and within a configurable range.
- **Filter** by category — hostile, all mobiles, items/corpses, or context-appropriate.
- **Sort** deterministically by screen position so cycling order is stable frame to
  frame (unstable ordering is what makes this feature feel broken).
- **Cycle** with the bumpers; the selection is drawn with a highlight and, for
  mobiles, a health bar.
- **Act** with face buttons, mapping to the existing single-click / double-click
  actions rather than inventing new verbs.

### 8. Server Target Cursor Integration

This is the highest-payoff surface. `TargetManager` already exposes
`IsTargeting`, `TargetingState`, `Target(uint serial)`, and `CancelTarget()`
(`Game/Managers/TargetManager.cs:216-337`).

When `IsTargeting` becomes true, automatically enter selection mode with the
candidate list filtered to valid target types, pre-select the most likely target
(last target if still valid, else nearest hostile), confirm with `A` →
`TargetManager.Target(serial)`, cancel with `B` → `CancelTarget()`.

`MultiTargetInfo` (house/multi placement) keeps the free pointer, since it targets
ground tiles rather than entities.

### 9. Magnetic Snap

Optional pull of the virtual cursor toward the nearest candidate, strength
configurable and defaulting to off until it can be tuned in real play. Snap must
never fully lock the cursor — it biases motion, it does not capture it.

---

## Part C: Phase 2 — Gump Traversal and Item Handling

### 10. Spatial Focus Navigation

D-pad moves focus between controls within the focused gump using
**nearest-neighbour in the pressed direction** rather than a fixed tab order,
because server-authored layouts have no meaningful declaration order.

- Candidate set: controls with `AcceptMouseInput` in the active gump.
- Scoring: primarily projection along the pressed axis, penalised by perpendicular
  offset, so "down" prefers the control most directly below.
- Render a focus ring on the focused control.
- Shoulder buttons cycle between open gumps; the focused gump is visibly distinguished.

### 11. Pick Up / Place Verbs

Dragging by holding a button while steering a stick is miserable. Replace it with
two discrete verbs backed by the existing hold system: `A` on an item picks it up
into `ItemHold`, `A` on a destination drops it. `B` cancels the hold. The
already-held item follows the cursor exactly as it does with a mouse, so the
existing rendering and drop-target logic is reused unchanged.

### 12. Context Actions

A single button opens a small radial or list menu of the valid actions for the
current selection (use, look, loot all, attack, add to friends), replacing
combinations that would otherwise need a keyboard.

---

## Part D: Phase 3 — Text Entry and Device Polish

### 13. Text Entry

**Open decision — see below.** If an on-screen keyboard is required, it is a
gump-based grid driven by the d-pad and face buttons, feeding the existing
`StbTextBox` input path, with a recent-phrases list for common speech.

### 14. Device Plumbing

- Handle `SDL_EVENT_GAMEPAD_ADDED` / `SDL_EVENT_GAMEPAD_REMOVED` for hot-plug.
- Replace hardcoded `PlayerIndex.One` with the active device.
- Per-vendor button glyphs (Xbox / PlayStation / Nintendo / generic) sourced from
  the SDL gamepad type, used everywhere a binding is displayed.
- Optional rumble on damage taken and on target acquisition.

---

## Open Decisions

1. **Does Phase 3 text entry block "no keyboard"?** If the target is Steam Deck or
   Steam Input, the Steam overlay keyboard may already cover chat and speech, which
   would remove the largest item in Phase 3. Needs a decision before Phase 3 is
   scoped; does not block Phases 0–2.
2. **Snap default.** Section 9 defaults to off. Whether it ships on is a tuning
   question that can only be answered by playing Phase 1.
3. **Context menu form.** Radial vs list (section 12) is deferred to Phase 2.

---

## Testing

**Unit tests** in the existing `tests/ClassicUO.UnitTests` project, covering the
pure functions — deadzone rescaling (including the no-jump property at the
threshold), response curve monotonicity, octant mapping symmetry across all eight
sectors, candidate sort stability, and spatial navigation scoring. These are the
parts where defects are invisible in play but obvious in a test.

**Manual testing** covers everything involving feel: framerate independence,
drift, snap strength, and traversal in real gumps.

---

## Branch and Try-It-Out

Work happens on `feature/controller-overhaul`.

Pushing that branch to `origin` triggers `.github/workflows/branch-build.yml`,
which builds win-x64, linux-x64, osx-arm64 and osx-x64 and publishes a prerelease
tagged `branch-feature-controller-overhaul`, named
`Feature Branch: feature/controller-overhaul`. The TazUO Launcher's **Feature
Branch** channel discovers `branch-*` releases, so it can be selected and played
directly from the launcher.

Two operational notes:
- The workflow has `paths-ignore: docs/**`, so a docs-only commit (such as this
  spec) does **not** trigger a build. The first build happens on the first code push.
- The workflow's `delete:` trigger removes the release and tag when the branch is
  deleted, so cleanup after merge is automatic.

---

## Files Summary

**New:**
- `src/ClassicUO.Client/Input/ControllerAxis.cs` — pure stick maths (Phase 0)
- `src/ClassicUO.Client/Game/Managers/ControllerTargetManager.cs` — candidates, cycling, selection (Phase 1)
- `src/ClassicUO.Client/Game/UI/Gumps/ControllerKeyboardGump.cs` — on-screen keyboard (Phase 3, conditional)
- `tests/ClassicUO.UnitTests/ControllerAxisTests.cs` — unit tests

**Modified:**
- `Input/Mouse.cs` — delta-time motion, deadzone, virtual cursor ownership, source arbitration
- `Input/Controller.cs` — analog triggers, hot-plug, active device
- `GameController.cs` — trigger axis handling, remove BACK/GUIDE workaround, device events
- `Game/Scenes/GameSceneInputHandler.cs` — `MoveCharByController` rewrite, selection input
- `Game/Managers/TargetManager.cs` — hooks for controller target integration
- `Game/UI/Controls/Control.cs` — focus ring, spatial navigation support
- `Game/UI/Gumps/BaseOptionsGump.cs` — new controller options
- `Configuration/Profile.cs` — deadzone, curve, speed, run threshold; sensitivity migration
- `Game/Managers/Hotkeys/*`, `SpellBarManager.cs`, `CounterBarHotkeysManager.cs`,
  `LegionScripting/ScriptHotkeysManager.cs` — trigger binding migration
