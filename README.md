<p align="center"><a href="https://discord.gg/QvqzkB95G4"><img src="https://discord.com/api/guilds/1344851225538986064/widget.png?style=banner3" alt="Discord Banner 3"/></a></p>

***

| Channel | Status |
| --- | --- |
| Stable | [![Stable](https://github.com/crameep/TazUO/actions/workflows/build-test.yml/badge.svg?branch=main)](https://github.com/crameep/TazUO/actions/workflows/build-test.yml) |
| Bleeding Edge | [![Bleeding Edge](https://github.com/crameep/TazUO/actions/workflows/bleeding-edge.yml/badge.svg?branch=dev)](https://github.com/crameep/TazUO/actions/workflows/bleeding-edge.yml) |

# crameep's TazUO

A fork of [TazUO](https://github.com/PlayTazUO/TazUO) focused on performance, auto-loot power features, scripting reliability, and quality-of-life improvements. If you've hit the limits of upstream TazUO — laggy loot lists, stalled scavengers, choppy movement — this fork addresses those problems directly.

## Getting Started

Download the [launcher](https://github.com/crameep/TUO-Launcher/releases/latest) and pick an update channel:

- **Stable** — Tagged releases that have been tested on the bleeding edge
- **Bleeding Edge** — Latest features and fixes, auto-built from the `dev` branch
- **Feature Branch** — Individual feature builds for testing specific changes

## What's Different from Upstream TazUO

### Auto-Loot System Overhaul

- **Performance Optimizations** — Graphic Index (O(1) lookup by graphic ID), Spatial Tracking (only scans nearby ground items), and Match Cache (per-serial results with OPL-aware staleness detection). Loot lists with 500+ entries no longer cause stuttering.
- **Priority Tiers** — Assign High, Normal, or Low priority per loot entry. Valuable items are always looted first.
- **Profiles** — Multiple named loot profiles per character. Enable/disable individual profiles with checkboxes, export/import via clipboard, and import from other character configurations. Sidebar UI with drag-to-reorder.
- **Decoupled Corpse Opening** — Corpse double-click opens run on a separate queue from item moves. Looting near multiple corpses is dramatically faster.
- **Scavenger Fix** — Ground items no longer starved behind corpse items in the action queue. Ground and corpse items share the same priority tier with an internal bias toward corpse items.

### Movement & Performance

- **Removed debug render target** — An extra full-screen compositing pass was running every frame (`_useScreenRenderTarget` flag left enabled). Removing it is the single biggest performance win.
- **Spatial door lookup** — `TryOpenDoors()` was scanning all world items via LINQ on every direction change. Now uses tile-based spatial lookup via `Map.GetTile()`.
- **Cached corpse snapshots** — `GetCorpseSnapshot()` was allocating a new array every movement step. Now caches and only rebuilds when corpses are added or removed.
- **Gated pathfinding** — `WalkableManager` and `LongDistancePathfinder` no longer update every frame when long-distance pathfinding is disabled.

### UI Scaling

- Replaced `RenderScale` with a `UIScale` matrix applied to `UIManager.Draw()` only
- ImGui, game world, cursor, and plugins are unaffected by the UI scale
- Fixed positioning for `ContextMenuControl`, `GameCursor`, and `LoginScene`
- Added `ScreenToUI` helpers for correct hit-testing and gump dragging at any scale

### Skills Management

New ImGui-based Skills tab in the Legion Assistant window:

- Sortable columns: Name, Value, Base, Cap, +/-, Lock
- Bulk lock buttons: Set All Up / All Down / All Locked
- Change tracking with color-coded +/- deltas from login baseline
- Copy All to clipboard in tab-delimited format
- Optional grouped view by skill category with totals

### Scripting Improvements

- **PreTarget** accepts an optional `TargetType` parameter — omit it to match any server target type (fixes rogue targeting cursors when the server sends an unexpected type)
- **Sound API** — `ClearSoundLog()`, `CheckSoundLog(soundId)`, and `GetSoundLog(seconds)` for reacting to in-game sounds from scripts
- **ScriptName / ScriptPath** properties on `API` — get the current script's filename and directory path

### Other Improvements

- **Mobile outline by notoriety** — Automatically outlines mobiles with their notoriety color (General tab checkbox)
- **Organizer graphic hover preview** — Hovering over an item graphic in the Organizer tab shows the graphic ID in hex
- **Skills lock cycling fix** — Lock button in the Skills tab now updates locally immediately instead of waiting for a server round-trip

## Branching Strategy

| Branch | Purpose |
| --- | --- |
| `main` | Stable releases only. Merges from `dev` when ready for release. |
| `dev` | Bleeding edge. All features get merged here for testing before stable. |
| Feature branches | Individual features/fixes. Merged into `dev` when ready. |

## Building from Source

```bash
# Debug build
dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj

# Release build
dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Release

# Run tests
dotnet test tests/ClassicUO.UnitTests/
```

Requires .NET 10 SDK. Target platform is x64.

## Links

- [Launcher](https://github.com/crameep/TUO-Launcher/releases/latest)
- [Discord](https://discord.gg/QvqzkB95G4)
- [GitHub](https://github.com/crameep/TazUO)
