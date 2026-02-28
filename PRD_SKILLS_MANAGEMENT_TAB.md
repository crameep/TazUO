# PRD: Skills Management Tab for Legion Assistant

## Overview

Add a Razor-style skills management tab to the Legion Assistant window in TazUO. The existing skills gumps (`StandardSkillsGump` and `SkillGumpAdvanced`) are basic UO-style windows. This new ImGui-based "Skills" tab provides sortable columns, bulk lock management, change tracking, clipboard export, skill use buttons, and optional group view — matching Razor/Razor Enhanced feature parity.

## Goals

- Provide a modern, full-featured skills management UI inside the Legion Assistant
- Support sorting by any column (name, value, base, cap, change delta, lock state)
- Enable bulk skill lock management (Set All Up/Down/Locked)
- Track cumulative skill changes with reset capability
- Allow clipboard export of all skill data
- Support optional grouped view using existing `SkillsGroupManager`

## Non-Goals

- Replacing the existing `StandardSkillsGump` or `SkillGumpAdvanced` (they remain as-is)
- Adding skill training/macro functionality
- Modifying any existing skill data structures or network packets

## Architecture

### New File: `src/ClassicUO.Client/Game/UI/ImGuiControls/Skills/SkillsTabContent.cs`

A `TabContent` subclass (same base class as all other Legion Assistant tabs like `GeneralTabContent`, `AgentsTabContent`, etc.).

### Modified File: `src/ClassicUO.Client/Game/UI/ImGuiControls/AssistantWindow.cs`

Register the new tab alongside existing tabs (General, Agents, Organizer, Filters, Item Database, Macros).

### Key Dependencies (read-only, no modifications)

| File | What We Use |
|------|-------------|
| `src/ClassicUO.Client/Game/Data/Skill.cs` | `Skill` class — `Name`, `Index`, `IsClickable`, `Value`, `Base`, `Cap`, `Lock`, `BaseFixed`. Static events: `SkillBaseChangedEvent`, `SkillValueChangedEvent`, `SkillCapChangedEvent`. `Lock` enum: `Up=0`, `Down=1`, `Locked=2` |
| `src/ClassicUO.Client/Game/Managers/SkillsGroupManager.cs` | `SkillsGroupManager.Groups` (List of `SkillsGroup`). Each group has `Name`, `Count`, `GetSkill(i)` returning byte skill index, `IsMaximized` |
| `src/ClassicUO.Client/Game/GameActions.cs` | `GameActions.UseSkill(int index)` — sends skill use packet. `GameActions.ChangeSkillLockStatus(ushort skillindex, byte lockstate)` — sends lock change packet |
| `src/ClassicUO.Client/Game/GameObjects/PlayerMobile.cs` | `World.Instance.Player.Skills` — `Skill[]` array indexed by skill index |
| `src/ClassicUO.Client/Game/UI/ImGuiControls/TabContent.cs` | Base class providing `DrawContent()`, `Update()`, `Dispose()`, `SetTooltip()`, `ClipboardOnClick()` |

### Access Patterns

- Skills array: `World.Instance.Player.Skills` (null-check `World.Instance?.Player`)
- Skill count: `World.Instance.Player.Skills.Length`
- Skills group manager: `World.Instance.SkillsGroupManager` (need to verify accessor)
- Clipboard: `SDL3.SDL.SDL_SetClipboardText(string)` (project uses SDL3 namespace)
- Game messages: `GameActions.Print(message, hue)` with `Constants.HUE_SUCCESS`

## UI Layout

### Toolbar Row

```
[Set All ▼ Up|Down|Locked]  [Reset +/-]  [Copy All]  [☐ Show Groups]  Total: 720.0 / 720.0
```

- **Set All dropdown**: Combo with 3 options (Up, Down, Locked). On selection, iterates all skills and calls `GameActions.ChangeSkillLockStatus(skillIndex, lockState)` for each, also updates `skill.Lock`
- **Reset +/- button**: Snapshots current `Skill.Base` values into `_baselineBase[]` array, clearing all deltas
- **Copy All button**: Builds tab-separated text of all visible skills (respecting current sort order), copies via `SDL3.SDL.SDL_SetClipboardText()`
- **Show Groups checkbox**: Toggles between flat table view and grouped tree-node view
- **Total display**: Sum of all `Skill.Base` values, formatted as `Total: {sum:F1} / {capSum:F1}`

### Skills Table

ImGui table with 7 columns using `ImGuiTableFlags.Sortable | ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable`:

| Column | Source | Width | Sortable | Notes |
|--------|--------|-------|----------|-------|
| Use | `Skill.IsClickable` | Fixed ~30px | No | Small "Use" button, only shown if `IsClickable`. Calls `GameActions.UseSkill(skill.Index)` |
| Name | `Skill.Name` | Stretch | Yes | Skill name string |
| Value | `Skill.Value` | Fixed ~60px | Yes | Float with stat modifiers, formatted `{value:F1}` |
| Base | `Skill.Base` | Fixed ~60px | Yes | Raw base value, formatted `{base:F1}` |
| Cap | `Skill.Cap` | Fixed ~60px | Yes | Max allowed value, formatted `{cap:F1}` |
| +/- | Computed delta | Fixed ~60px | Yes | `currentBase - baselineBase`. Green if positive, red if negative. Formatted `+{delta:F1}` or `{delta:F1}` |
| Lock | `Skill.Lock` | Fixed ~50px | Yes | Clickable button cycling Up→Down→Locked. Display: "U"/"D"/"L" or arrow icons. Calls `GameActions.ChangeSkillLockStatus(index, newLock)` |

### Sorting

- Track `_sortColumnIndex` (int, maps to column) and `_sortAscending` (bool)
- Use `ImGui.TableGetSortSpecs()` to detect header clicks
- Sort a temporary index array each frame (don't mutate skill data)
- Default sort: by Name ascending

### Grouped View

When "Show Groups" is checked:
- Iterate `SkillsGroupManager.Groups`
- Each group rendered as `ImGui.TreeNodeEx(group.Name)` collapsible header
- Inside each tree node, render the same table rows for skills in that group
- Skills within each group respect the current sort order
- Group headers show group total: `{groupName} ({groupBaseTotal:F1})`

### Change Tracking (+/- Column)

- `float[] _baselineBase` — initialized to current `Skill.Base` values on first draw or player connect
- Delta = `skill.Base - _baselineBase[skill.Index]`
- Subscribe to `Skill.SkillBaseChangedEvent` — no explicit action needed since ImGui redraws each frame, but can use to trigger refresh if needed
- "Reset +/-" button: copies all current `Skill.Base` into `_baselineBase`
- Color coding: `ImGui.TextColored()` — green `(0,1,0,1)` for positive, red `(1,0,0,1)` for negative, default for zero

### Copy All Format

Tab-separated, one line per skill (respecting current sort):
```
Name\tValue\tBase\tCap\t+/-\tLock
Swordsmanship\t100.0\t100.0\t120.0\t+2.3\tUp
Magery\t95.5\t95.5\t120.0\t-1.2\tDown
...
```

## Implementation Notes

### Class Structure

```csharp
public class SkillsTabContent : TabContent
{
    // Sorting state
    private int _sortColumnIndex = 1; // Default: Name column
    private bool _sortAscending = true;

    // Change tracking
    private float[] _baselineBase;
    private bool _baselineInitialized;

    // View state
    private bool _showGroups;

    // Cached sorted indices (rebuilt each frame if sort changes)
    private int[] _sortedIndices;
}
```

### Registration in AssistantWindow.cs

Add alongside existing tabs:
1. Field: `private SkillsTabContent _skillsTab;`
2. Constructor: `_skillsTab = new SkillsTabContent();`
3. `DrawContent()`: New `ImGui.BeginTabItem("Skills")` block (after Macros tab)
4. `Dispose()`: `_skillsTab?.Dispose();`

### Edge Cases

- **Null player**: Guard with `if (World.Instance?.Player == null)` early return showing "Not connected"
- **Empty skills**: Handle zero-length skills array gracefully
- **Lock state sync**: After calling `ChangeSkillLockStatus`, the server sends back confirmation — don't locally mutate until confirmed (skill.Lock is set by packet handler)
- **Baseline on reconnect**: Re-initialize baseline when player changes (track player serial or null check)
- **Group view with sorting**: Within each group, apply same sort; groups themselves always display in their original order

## Verification Criteria

- `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj` compiles without errors
- Skills tab appears in Legion Assistant window
- Skills populate with correct values from `World.Instance.Player.Skills`
- Clicking column headers sorts correctly in both directions
- Lock toggle buttons cycle Up→Down→Locked and send packets via `GameActions.ChangeSkillLockStatus`
- "Use" buttons call `GameActions.UseSkill` for clickable skills
- Set All bulk lock updates all skills
- +/- column shows deltas from baseline, Reset clears them
- Copy All puts tab-separated data on clipboard
- Show Groups toggle switches between flat and grouped view
- Total skill points display is accurate
