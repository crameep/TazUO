# PRD: Auto-Loot Profiles

## Overview
Replace the single flat auto-loot list with a named profile system. Each profile is a self-contained set of loot entries (graphic, hue, regex, priority, destination container). Profiles are displayed in a sidebar with checkboxes to toggle them active/inactive. The auto-loot engine sees the union of all active profiles' entries.

## Dependencies
- **Auto-Loot Priority Tiers** (`autoloot-priority-tiers` branch / PR #363) — profiles store and use the priority system for conflict resolution.

## Use Cases
1. **Shard/area switching** — different loot rules for dungeons vs overworld vs PvP
2. **Character role switching** — crafter needs different loot rules than grinding
3. **Sharing with others** — export a "Treasure Hunter" profile and share with guildmates

## Core Behavior

### Profile Model
Each profile contains:
- **Name** — user-defined string, also used as the filename
- **IsActive** — boolean, whether this profile's entries are included in the merged loot list
- **Entries** — list of `AutoLootConfigEntry` (graphic, hue, regex, priority, destination container)

### Merged Loot List
- The auto-loot engine builds a merged list from all active profiles at runtime.
- When multiple entries from different profiles match the same item, the entry with the **highest priority wins** (High > Normal > Low). Ties don't need a tiebreaker.
- The winning entry's destination container and priority are used for the `ObjectActionQueue`.
- The merged list rebuilds whenever a profile is toggled on/off or entries in an active profile change.

### Storage
```
ProfilePath/
  AutoLootProfiles/
    Default.json
    DungeonLoot.json
    TreasureHunter.json
```

Each file:
```json
{
  "Name": "DungeonLoot",
  "IsActive": true,
  "Entries": [
    {
      "Name": "",
      "Graphic": 3821,
      "Hue": 65535,
      "RegexSearch": "",
      "DestinationContainer": 0,
      "Priority": 1
    }
  ]
}
```

- `IsActive` is stored in each profile file directly — no separate state file.
- Profile files use the JSON source generation context pattern required by the project.

### Migration
On first load, if `AutoLoot.json` exists and the `AutoLootProfiles/` directory does not:
1. Create `AutoLootProfiles/` directory
2. Read all entries from `AutoLoot.json`
3. Write them into `AutoLootProfiles/Default.json` with `IsActive: true`
4. Leave the old `AutoLoot.json` in place (no deletion, no data loss risk)

Subsequent loads read from `AutoLootProfiles/` only.

## UI Design

### Layout
The auto-loot tab gets a **left sidebar** (~200px) and the existing loot table stays in the **main content area** on the right.

### Sidebar
- List of profiles, each with a **checkbox** (active/inactive) and the **profile name**
- Clicking a profile name **selects it for editing** — its entries appear in the main table
- The currently-selected profile is visually highlighted
- **Drag-to-reorder** profiles in the sidebar (display order only, no impact on loot behavior)
- **"New Profile"** button at the bottom
- **Right-click context menu** on a profile: Rename, Delete, Export to Clipboard, Import from Clipboard

### Main Content Area
- Top settings row stays the same (Enable Auto Loot, Enable Scavenger, etc.) — **these are global, not per-profile**
- Loot entry table shows entries **for the selected profile only**
- Adding/removing/editing entries operates on the selected profile
- Changes auto-save immediately (same as current behavior)

### Import/Export
- **Export** button exports the currently selected profile to clipboard as JSON
- **Import** button (clipboard) creates a **new profile** from pasted JSON
- **Import from Character** creates a new profile from the other character's loot config
- Clipboard format is the same profile JSON structure

## Scope

### In Scope
- Profile data model (name, active flag, entries)
- `AutoLootProfiles/` directory with per-profile JSON files
- Migration from old `AutoLoot.json` to `Default.json`
- Sidebar UI with checkboxes, selection, new/rename/delete
- Drag-to-reorder profiles in sidebar
- Merged entry list from active profiles
- Highest-priority-wins conflict resolution
- Clipboard export/import per profile
- Import from Character creates a new profile
- Auto-save on edit
- JSON source generation context for new types

### Out of Scope
- File-based export/import (clipboard only)
- Per-profile settings (enable/scavenger/etc stay global)
- Cross-character shared profile directory

## Key Files
- `src/ClassicUO.Client/Game/Managers/AutoLootManager.cs` — core loot logic, profile loading/saving, merged list
- `src/ClassicUO.Client/Game/UI/ImGuiControls/Agents/AutoLootTabContent.cs` — UI for sidebar and entry table
- `src/ClassicUO.Client/Game/Managers/ObjectActionQueue.cs` — priority enum (from dependency)

## Implementation Notes
- All JSON serialize/deserialize must use source-generated context (project convention).
- Don't put license headers on new files (project convention).
- The merged list should use the same data structures the current flat list uses so the loot engine (`CheckAndLoot`, `MatchItem`, etc.) needs minimal changes.
- Profile filenames should be sanitized (strip invalid path characters from user-provided names).
