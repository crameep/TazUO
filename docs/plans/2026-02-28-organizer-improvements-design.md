# Organizer Improvements Design

## Goal

Make the organizer agent more useful for sorting/cleanup workflows, improve
the UI/UX with progress feedback and config grouping, and add first-class
tome support with a dedicated Tomes tab and multi-mode automation engine.

## Current State

The organizer matches items by Graphic + Hue only, has no progress feedback,
and configs are a flat list with no grouping. Adding items requires targeting
them one at a time or entering hex codes manually. Tome fields were recently
added to OrganizerConfig but have no execution logic — they only capture and
store gump ID / button ID.

---

## Part A: Organizer Improvements

### 1. Matching Overhaul

Add richer matching to `OrganizerItemConfig`, aligning with auto loot's proven
matching model.

**New fields on `OrganizerItemConfig`:**
- `Name` (string) — human-readable display name for the entry.
- `RegexSearch` (string) — regex pattern matched against OPL name+data text.
  Empty string means no regex check (current behavior preserved).

**Wildcard graphic:** `Graphic == -1` means "match any graphic". Useful for
regex-only entries like "match anything with 'vanq' in tooltip".

**Match order (same as auto loot):**
1. Graphic check — O(1), skip if wildcard (-1)
2. Hue check — O(1), skip if wildcard (ushort.MaxValue)
3. Regex check — only runs when `RegexSearch` is non-empty

**Implementation:**
- Reuse `RegexHelper` for cached compiled regexes.
- Use `World.OPL.TryGetNameAndData()` for tooltip text, fall back to
  `ItemData.Name` when OPL unavailable.
- Update `IsMatch()` to accept a `World` parameter for OPL access.

### 2. Bulk Add from Container

Add a "Scan Container" button to the items section.

**Flow:**
1. User clicks "Scan Container".
2. Target cursor activates — user clicks a container.
3. Iterate all items in the container (top-level only).
4. For each unique (Graphic, Hue) pair not already in the config:
   - Create an `OrganizerItemConfig` entry.
   - Populate `Name` from OPL or `ItemData.Name`.
   - Set `Enabled = true`, `Amount = 0` (move all).
5. Print summary: "Added N item types from container".

Deduplicates against existing entries to avoid duplicates.

### 3. Recursive Containers

Add a `Recursive` bool to `OrganizerConfig` (default: false).

When enabled, `OrganizeItems` walks into sub-containers within the source
container instead of only processing top-level items. The destination container
serial is skipped during traversal to prevent loops.

**Implementation:**
- Add a helper method that yields items from a container, optionally recursing
  into sub-containers.
- Pass `destContSerial` to skip during recursion.

### 4. Progress & Status Feedback

**OrganizerRunState** — lightweight tracking class:
- `string ConfigName` — which organizer is running.
- `int TotalItems` — items queued to move.
- `int ItemsMoved` — moves completed so far.
- `int ItemsSkipped` — moves that failed or were skipped.
- `DateTime StartTime` — when the run started.
- `bool IsRunning` — whether a run is active.

**UI changes:**
- Progress bar at the top of the details panel when `IsRunning` is true.
  Format: "Organizing 'Reagents': 5/12 items moved".
- Running indicator in the left panel list — color or icon change on the
  active organizer's name.
- Completion summary printed to game journal when finished:
  "Organizer 'Reagents' complete: moved 12 items (2 skipped)."

**Tracking hooks:**
- `OrganizeItems` sets up the run state before queuing moves.
- Hook into `ObjectActionQueue` completion/failure callbacks to increment
  counters.
- Run state resets when all queued moves complete or fail.

### 5. Groups

Add a `Group` string field to `OrganizerConfig` (default: empty string).

**UI changes:**
- Left panel sorts configs by group, then by name within each group.
- Collapsible group headers — click to expand/collapse.
- Ungrouped configs appear under a "General" header.
- Right-click group header context menu:
  - "Enable All" / "Disable All" — toggle all configs in the group.
  - "Run Group" — run all enabled organizers in the group.

**Commands:**
- `-organize group <name>` — run all enabled organizers in a group.

**Storage:** The `Group` field is stored in the existing `OrganizerConfig.json`.
No separate files, no profile merging. Groups are just a tag.

---

## Part B: Tome System

### 6. Tome Definitions (New Tomes Tab)

A new "Tomes" tab in the Assistant window for defining reusable tome
configurations. Tomes are first-class entities, defined once and referenced
by name from organizer item destinations.

**TomeDefinition data model:**
```
Name: string               — human-readable name (e.g. "Reagent Tome")
TomeSerial: uint           — the tome item serial (targeted in-game)
GumpId: uint               — the gump server serial
AddButtonId: int           — the button ID that triggers add/fill
Mode: TomeMode             — FillAll / TargetEach / TargetContainer / TargetRepeat
TargetSerial: uint         — for TargetContainer mode: container serial, or 0 for self-target
Delay: int                 — ms between operations (default 1000), configurable
RequiresWalk: bool         — walk to tome before using (default false)
```

**Four automation modes:**

| Mode | Flow | Per-item? |
|---|---|---|
| **FillAll** | Use tome → gump → press button → done | No — button handles everything |
| **TargetEach** | Use tome → gump → press button → target item → (repeat full cycle per item) | Yes — one full gump cycle per item |
| **TargetContainer** | Use tome → gump → press button → target container or self → done | No — one cycle handles all |
| **TargetRepeat** | Use tome → gump → press button → target item 1 → cursor reappears → target item 2 → ... → cancel | Yes — one gump cycle, target repeatedly |

**Mode details:**

- **FillAll:** For tomes with a "fill from backpack" button. No targeting needed.
  The organizer just opens the tome and presses the button.

- **TargetEach:** For tomes where you press "Add", get a target cursor, target
  ONE item, and the tome closes. The entire use → gump → button → target cycle
  repeats for each item.

- **TargetContainer:** For tomes where pressing "Add" then targeting a container
  (or self-targeting) makes the tome pull all matching items from that container.
  `TargetSerial = 0` means self-target (player serial). One cycle handles all
  items.

- **TargetRepeat:** For tomes where pressing "Add" once puts you in a repeating
  target mode — the target cursor keeps reappearing after each item until you
  cancel. The automation targets items one by one from its queue, then sends a
  target cancel when done.

**Tomes tab UI:**
- Same left-list / right-details pattern as the organizer.
- "Set Tome" button — target the tome item in-game.
- "Capture Gump Button" — reuses `GumpButtonCapture` from the existing code.
- Mode dropdown — select FillAll / TargetEach / TargetContainer / TargetRepeat.
- Conditional fields: TargetContainer shows a "Set Target Container" button
  and a "Self-Target" checkbox.
- Delay slider with ms input.
- RequiresWalk checkbox.
- Manual entry fields for GumpId and AddButtonId (hex/decimal).

**Storage:** `TomeDefinitions.json` in the profile path, with its own
`TomeDefinitionContext` JSON serialization context.

### 7. Organizer Destination Dropdown

The existing Destination column in the organizer items table becomes a unified
dropdown that can point to either a container or a tome.

**New fields on `OrganizerItemConfig`:**
- `DestinationType` enum: `ConfigDefault`, `Container`, `Tome`
- `DestContSerial` (uint) — used when type is `Container` (existing field)
- `TomeDefinitionName` (string) — used when type is `Tome`

**UI in the items table Destination column:**
- Combo dropdown with options:
  - "Config Default" — uses the organizer config's destination
  - Each named tome (e.g. "Reagent Tome", "Scroll Tome")
  - "Container: 0xABCD" — existing per-item container override
- "Set Container" button appears when Container is selected.

**Config-level destination also gets the dropdown** — the organizer config's
main destination can be a tome too, serving as the default for all items.

**Execution logic in `OrganizeItems`:**
- Items with `Container` destination → `MoveRequest` into OAQ (current behavior).
- Items with `Tome` destination → grouped by tome, queued into `TomeActionRunner`.
- Container moves and tome operations run concurrently (different mechanisms).

### 8. Tome Execution Engine (TomeActionRunner)

A state machine that manages async multi-step tome operations.

**Location:** `TomeActionRunner.cs` in `Game/Managers/`.

**Architecture:**
- Lives on `OrganizerAgent`, ticked from the game loop via `Update()`.
- Maintains a queue of `TomeOperation` items (tome definition + list of item
  serials to add).
- Processes one operation at a time using a state machine.
- Uses `NextGumpConfig` and `NextAutoTarget` to set up auto-responses.

**State machine (TargetEach mode):**
```
Idle
  → Set NextGumpConfig(gumpId, addButtonId)
  → Set NextAutoTarget(itemSerial)
  → DoubleClick(tomeSerial)
  → state = WaitingForGump

WaitingForGump
  → NextGumpConfig auto-responds when gump arrives
  → state = WaitingForTarget

WaitingForTarget
  → NextAutoTarget auto-fires when target cursor arrives
  → state = WaitingForDelay

WaitingForDelay
  → Wait configured delay ms
  → If more items: back to Idle for next item
  → If done: state = Complete
```

**State machine (TargetRepeat mode):**
```
Idle
  → Set NextGumpConfig(gumpId, addButtonId)
  → DoubleClick(tomeSerial)
  → state = WaitingForGump

WaitingForGump
  → NextGumpConfig auto-responds when gump arrives
  → state = WaitingForFirstTarget

WaitingForFirstTarget
  → Target cursor arrives → auto-target item 1
  → state = TargetingRepeatedly

TargetingRepeatedly
  → Target cursor reappears → auto-target next item
  → Wait configured delay between targets
  → When no items remain: send target cancel
  → state = Complete
```

**State machine (FillAll mode):**
```
Idle
  → Set NextGumpConfig(gumpId, addButtonId)
  → DoubleClick(tomeSerial)
  → state = WaitingForGump

WaitingForGump
  → NextGumpConfig auto-responds (button press handles everything)
  → state = Complete
```

**State machine (TargetContainer mode):**
```
Idle
  → Set NextGumpConfig(gumpId, addButtonId)
  → Set NextAutoTarget(targetSerial or playerSerial if self-target)
  → DoubleClick(tomeSerial)
  → state = WaitingForGump

WaitingForGump
  → NextGumpConfig auto-responds
  → state = WaitingForTarget

WaitingForTarget
  → NextAutoTarget auto-fires
  → state = Complete
```

**Timeouts:** Each state has a 5-second timeout. If the expected response
(gump or target cursor) doesn't arrive, log an error to the journal and
advance to the next operation or abort.

**Walking support:** If `RequiresWalk` is true, before the first step the
runner checks distance to the tome. If out of range (> 2 tiles), it uses
pathfinding to walk within range. The state machine blocks at a
`WalkingToTome` state until the player is within range or a timeout expires.

**Progress integration:** The runner reports progress to `OrganizerRunState`
so the progress bar reflects tome operations alongside container moves.

### 9. Migration from Per-Config Tome Fields

The Codex-added fields (`TomeSerial`, `TomeGumpId`, `TomeAddButtonId`) on
`OrganizerConfig` are migrated to the new system and removed.

**Migration logic in `OrganizerAgent.Load()`:**
1. After loading configs, scan for any with `TomeSerial != 0`.
2. For each, create a `TomeDefinition` with:
   - `Name` = "Migrated Tome" (uniquified)
   - `TomeSerial` = config.TomeSerial
   - `GumpId` = config.TomeGumpId
   - `AddButtonId` = config.TomeAddButtonId
   - `Mode` = TargetEach (safe default)
   - `Delay` = 1000
3. Save the new tome definitions.
4. Clear the deprecated fields on the config.
5. Save configs.

**Fields removed from `OrganizerConfig`:**
- `TomeSerial`
- `TomeGumpId`
- `TomeAddButtonId`

**UI cleanup from `OrganizerTabContent`:**
- Remove `DrawTomeSettings()` method
- Remove capture state fields (`_captureNextTomeButton`, `_tomeInputBoundConfig`,
  `_tomeGumpIdInput`, `_tomeButtonIdInput`)
- Capture UI moves to the Tomes tab

---

## Data Model Summary

```
OrganizerConfig (modified)
  + Group: string = ""
  + Recursive: bool = false
  + DestinationType: DestType = ConfigDefault    (config-level default)
  + TomeDefinitionName: string = ""              (when DestType is Tome)
  - TomeSerial (removed, migrated)
  - TomeGumpId (removed, migrated)
  - TomeAddButtonId (removed, migrated)

OrganizerItemConfig (modified)
  + Name: string = ""
  + RegexSearch: string = ""
  + DestinationType: DestType = ConfigDefault
  + TomeDefinitionName: string = ""
  Graphic: ushort → int (to support -1 wildcard)

DestType (new enum)
  ConfigDefault = 0
  Container = 1
  Tome = 2

TomeDefinition (new)
  Name: string
  TomeSerial: uint
  GumpId: uint
  AddButtonId: int
  Mode: TomeMode
  TargetSerial: uint         (for TargetContainer; 0 = self-target)
  Delay: int = 1000
  RequiresWalk: bool = false

TomeMode (new enum)
  FillAll = 0
  TargetEach = 1
  TargetContainer = 2
  TargetRepeat = 3

OrganizerRunState (new)
  ConfigName: string
  TotalItems: int
  ItemsMoved: int
  ItemsSkipped: int
  StartTime: DateTime
  IsRunning: bool
```

## JSON Serialization

- Update `OrganizerAgentContext` for modified OrganizerConfig/ItemConfig fields
  and new enums.
- New `TomeDefinitionContext` for `TomeDefinition` and `List<TomeDefinition>`.
- All new fields have defaults that preserve backward compatibility.

## Scripting API

- `API.Organizer("MyOrganizer")` — unchanged, run by name.
- `API.OrganizerGroup("MyGroup")` — new, run all enabled in group.

## Commands

- `-organize` — run all enabled organizers (unchanged).
- `-organize <index>` — run by index (unchanged).
- `-organize <name>` — run by name (unchanged).
- `-organize group <name>` — run all enabled organizers in a group (new).
- `-organizerlist` — list organizers (unchanged, add group info to output).

## Files Summary

**Modified:**
- `OrganizerAgent.cs` — matching, recursion, groups, progress, migration,
  tome integration.
- `OrganizerTabContent.cs` — remove tome UI, add destination dropdown, groups,
  progress bar, bulk add, regex fields.
- `AssistantWindow.cs` — add Tomes tab.
- `CommandManager.cs` — group command.
- `API.cs` — `OrganizerGroup()` method.
- `GumpButtonCapture.cs` — stays as-is (reused by Tomes tab).
- `GameActions.cs` — `GumpButtonCapture.Record` call stays as-is.

**New:**
- `TomeDefinition.cs` — data model + JSON context.
- `TomeManager.cs` — load/save/manage tome definitions.
- `TomeActionRunner.cs` — state machine execution engine.
- `TomeTabContent.cs` — Tomes tab UI in Assistant window.
