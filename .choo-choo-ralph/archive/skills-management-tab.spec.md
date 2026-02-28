---
title: "Skills Management Tab"
created: 2026-02-08
poured:
  - TazUo-lum
  - TazUo-0bn
  - TazUo-1lx
  - TazUo-6fk
  - TazUo-a16
  - TazUo-0x5
iteration: 1
auto_discovery: false
auto_learnings: false
---
<project_specification>
<project_name>Skills Management Tab</project_name>

  <overview>
    Add a Razor-style skills management tab to the Legion Assistant window in TazUO.
    The existing skills gumps (StandardSkillsGump and SkillGumpAdvanced) are basic UO-style
    windows. This new ImGui-based "Skills" tab provides sortable columns, bulk lock management,
    change tracking, clipboard export, skill use buttons, and optional group view — matching
    Razor/Razor Enhanced feature parity.
  </overview>

  <context>
    <existing_patterns>
      - All Legion Assistant tabs extend `TabContent` base class (abstract, provides DrawContent/Update/Dispose/SetTooltip/ClipboardOnClick/DrawArt)
      - TabContent implementations follow pattern: constructor initializes state, DrawContent renders ImGui, Dispose cleans up
      - AssistantWindow registers tabs as fields, instantiates in constructor, calls DrawContent inside BeginTabItem blocks, disposes in Dispose
      - ImGui tables use pattern: BeginTable with flags (Borders|RowBg|ScrollY), TableSetupColumn for each col, TableHeadersRow, then TableNextRow/TableNextColumn loop
      - Existing table scroll height: `ImGuiTheme.Dimensions.STANDARD_TABLE_SCROLL_HEIGHT` (200f)
      - Color text uses `ImGui.TextColored()` with `ImGuiTheme.Current.Success`, `.Error`, `.Warning`, `.Info`
      - Combo dropdowns: `ImGui.Combo("Label", ref index, items, items.Length)`
      - Buttons use `ImGui.PushStyleColor`/`PopStyleColor` for coloring
    </existing_patterns>
    <integration_points>
      - `src/ClassicUO.Client/Game/UI/ImGuiControls/AssistantWindow.cs` — register new tab (add field, constructor init, DrawContent entry, Dispose call)
      - `src/ClassicUO.Client/Game/Data/Skill.cs` — Skill class with Name, Index, IsClickable, Value, Base, Cap, Lock, BaseFixed; Lock enum (Up=0, Down=1, Locked=2); static events SkillBaseChangedEvent, SkillValueChangedEvent, SkillCapChangedEvent
      - `src/ClassicUO.Client/Game/GameObjects/PlayerMobile.cs` — `World.Instance.Player.Skills` (Skill[] array)
      - `src/ClassicUO.Client/Game/GameActions.cs` — `UseSkill(int index)`, `ChangeSkillLockStatus(ushort skillindex, byte lockstate)`
      - `src/ClassicUO.Client/Game/Managers/SkillsGroupManager.cs` — `World.Instance.SkillsGroupManager.Groups` (List of SkillsGroup, each with Name, Count, GetSkill(i), IsMaximized)
      - `SDL3.SDL.SDL_SetClipboardText(string)` for clipboard operations
      - `GameActions.Print(message, Constants.HUE_SUCCESS)` for user feedback messages
    </integration_points>
    <new_technologies>
      - ImGui sortable tables: Use `ImGuiTableFlags.Sortable` flag + `ImGui.TableGetSortSpecs()` to detect header clicks — not yet used elsewhere in codebase, first usage
      - `ImGuiTableColumnFlags.DefaultSort` to set initial sort column
      - Sort specs have `SpecsDirty` flag and `Specs.ColumnIndex` / `Specs.SortDirection` for reading user sort intent
    </new_technologies>
    <conventions>
      - Tab content files live in subdirectories under ImGuiControls (e.g., General/, Agents/, Filters/)
      - New file goes in `Skills/` subdirectory: `src/ClassicUO.Client/Game/UI/ImGuiControls/Skills/SkillsTabContent.cs`
      - Namespace: `ClassicUO.Game.UI.ImGuiControls`
      - No license headers on new files
      - All JSON serialize/deserialize need context generated (not applicable here — no JSON)
      - Null-guard World.Instance?.Player before accessing skills
      - Build command: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
    </conventions>
  </context>

  <tasks>
    <task id="scaffold-class" priority="1" category="infrastructure">
      <title>Scaffold SkillsTabContent class and register in AssistantWindow</title>
      <description>
        Create the new file `src/ClassicUO.Client/Game/UI/ImGuiControls/Skills/SkillsTabContent.cs`
        with a minimal TabContent subclass that renders a placeholder. Register it as a new
        "Skills" tab in AssistantWindow.cs (add field, constructor init, DrawContent tab item
        after Macros, Dispose call). Verify it compiles and the tab appears.

        Files to create:
        - `src/ClassicUO.Client/Game/UI/ImGuiControls/Skills/SkillsTabContent.cs`

        Files to modify:
        - `src/ClassicUO.Client/Game/UI/ImGuiControls/AssistantWindow.cs`
          - Add `using` if needed for Skills namespace (same namespace, so likely not needed)
          - Add field: `private SkillsTabContent _skillsTab;`
          - Constructor: `_skillsTab = new SkillsTabContent();`
          - DrawContent: add `if (ImGui.BeginTabItem("Skills")) { _skillsTab.DrawContent(); ImGui.EndTabItem(); }` after Macros tab
          - Dispose: `_skillsTab?.Dispose();`

        The SkillsTabContent constructor should:
        - Initialize `_baselineBase` and `_sortedIndices` arrays
        - Set `_baselineInitialized = false`

        DrawContent should initially just show "Skills tab" text as placeholder.
      </description>
      <steps>
        - Create Skills/ directory under ImGuiControls
        - Create SkillsTabContent.cs extending TabContent with constructor, DrawContent (placeholder), Dispose
        - Add field, init, tab item, and dispose for SkillsTabContent in AssistantWindow.cs
      </steps>
      <test_steps>
        1. Run `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj` — verify clean build
        2. In-game: open Legion Assistant window, verify "Skills" tab appears
        3. Click Skills tab — verify placeholder text renders without crash
      </test_steps>
      <review></review>
    </task>

    <task id="skills-table" priority="1" category="functional">
      <title>Implement sortable skills table with all columns</title>
      <description>
        Replace the placeholder in DrawContent with a full ImGui table rendering all skills.

        Table setup:
        - 7 columns: Use, Name, Value, Base, Cap, +/-, Lock
        - Flags: `ImGuiTableFlags.Sortable | ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable`
        - Use `ImGui.TableSetupScrollFreeze(0, 1)` to freeze header row
        - Column widths: Use=Fixed 30, Name=Stretch, Value=Fixed 60, Base=Fixed 60, Cap=Fixed 60, +/-=Fixed 60, Lock=Fixed 50
        - Name column gets `ImGuiTableColumnFlags.DefaultSort` for initial sort

        Data source: `World.Instance.Player.Skills` (guard with null check, show "Not connected" if null)

        Sorting:
        - Maintain `int[] _sortedIndices` array of skill indices
        - After `ImGui.TableHeadersRow()`, call `ImGui.TableGetSortSpecs()` — if `SpecsDirty`, rebuild sorted indices
        - Sort comparisons: Name=string, Value/Base/Cap/Delta=float, Lock=byte enum cast
        - Use column has `ImGuiTableColumnFlags.NoSort`

        Row rendering (iterate `_sortedIndices`):
        - **Use**: `ImGui.SmallButton("Use##" + index)` only if `skill.IsClickable`, calls `GameActions.UseSkill(skill.Index)`
        - **Name**: `ImGui.Text(skill.Name)`
        - **Value**: `ImGui.Text(skill.Value.ToString("F1"))`
        - **Base**: `ImGui.Text(skill.Base.ToString("F1"))`
        - **Cap**: `ImGui.Text(skill.Cap.ToString("F1"))`
        - **+/-**: Computed from `_baselineBase`, colored green/red (detail in change-tracking task)
        - **Lock**: Placeholder text for now ("U"/"D"/"L"), will be interactive in lock-toggle task

        Initialize `_baselineBase` on first draw when player is available (`_baselineInitialized` flag).
      </description>
      <steps>
        - Add null guard for World.Instance?.Player at top of DrawContent
        - Initialize _baselineBase array from current skill bases on first draw
        - Create ImGui table with 7 columns and proper flags/widths
        - Implement sort specs handling with _sortedIndices rebuild
        - Render each skill row with all column data
        - Show +/- delta as plain text (coloring in next task)
        - Show lock state as plain text (interactivity in next task)
      </steps>
      <test_steps>
        1. Build succeeds
        2. Skills tab shows all character skills in a scrollable table
        3. Click Name header — skills sort alphabetically ascending
        4. Click Name header again — sorts descending
        5. Click Value header — sorts by value
        6. Click Base, Cap headers — sorts by those columns
        7. +/- column shows 0.0 for all skills (no changes yet)
        8. Lock column shows current lock state text
      </test_steps>
      <review></review>
    </task>

    <task id="lock-toggle" priority="1" category="functional">
      <title>Implement interactive lock toggle buttons</title>
      <description>
        Replace the lock column's plain text with clickable buttons that cycle through
        Up → Down → Locked states.

        For each skill row's Lock column:
        - Render a small button with text based on current `skill.Lock`:
          - Lock.Up → "U" (or "▲")
          - Lock.Down → "D" (or "▼")
          - Lock.Locked → "L" (or "🔒")
        - Use unique ID: `"##lock" + skill.Index`
        - On click, compute next state: Up→Down→Locked→Up (cycle with `(byte)((byte)skill.Lock + 1) % 3`)
        - Call `GameActions.ChangeSkillLockStatus((ushort)skill.Index, nextLockByte)`
        - Do NOT locally mutate `skill.Lock` — the server confirms via packet handler

        Optional: Color the button based on state using PushStyleColor/PopStyleColor
        - Up = green-ish
        - Down = red-ish
        - Locked = gray/default
      </description>
      <steps>
        - Replace lock column text with ImGui.SmallButton per skill
        - Compute next lock state on click (cycle Up→Down→Locked→Up)
        - Call GameActions.ChangeSkillLockStatus with skill index and new lock byte
        - Optionally color buttons by lock state
      </steps>
      <test_steps>
        1. Build succeeds
        2. Each skill row shows a clickable lock button
        3. Click a lock button showing "U" — sends packet, server updates to "D"
        4. Click again — cycles to "L"
        5. Click again — cycles back to "U"
        6. Verify no client crash if server is slow to respond
      </test_steps>
      <review></review>
    </task>

    <task id="change-tracking" priority="2" category="functional">
      <title>Implement +/- change tracking with colored deltas</title>
      <description>
        Complete the change tracking system for the +/- column.

        State:
        - `float[] _baselineBase` — snapshot of Skill.Base values at reset time
        - `bool _baselineInitialized` — set true after first initialization
        - Track player serial to detect reconnect/character change

        Initialization:
        - On first DrawContent where player is non-null, snapshot all Skill.Base into _baselineBase
        - If player serial changes, re-initialize baseline

        Delta display:
        - Compute: `float delta = skill.Base - _baselineBase[skill.Index]`
        - Format: positive = "+{delta:F1}", negative = "{delta:F1}" (minus auto), zero = "0.0"
        - Color: positive = `ImGuiTheme.Current.Success` (green), negative = `ImGuiTheme.Current.Error` (red), zero = default text color

        Sorting on +/- column uses the computed delta float value.
      </description>
      <steps>
        - Track player serial to detect character changes
        - Initialize baseline on first draw or character change
        - Compute delta per skill in render loop
        - Use ImGui.TextColored for green/red/default based on delta sign
        - Ensure sort by +/- column compares delta floats
      </steps>
      <test_steps>
        1. Build succeeds
        2. Open Skills tab — all +/- values show 0.0 in default color
        3. Gain a skill point in-game — that skill's +/- shows green positive delta
        4. Lose a skill point — shows red negative delta
        5. Sort by +/- column — skills with changes sort correctly
      </test_steps>
      <review></review>
    </task>

    <task id="toolbar" priority="2" category="functional">
      <title>Implement toolbar: Set All, Reset, Copy All, Show Groups toggle, Total display</title>
      <description>
        Add a toolbar row above the skills table with all controls.

        Layout (all on one line using ImGui.SameLine):
        ```
        [Set All ▼]  [Reset +/-]  [Copy All]  [☐ Show Groups]  Total: 720.0 / 720.0
        ```

        **Set All dropdown:**
        - `ImGui.SetNextItemWidth(100)` then `ImGui.Combo("##SetAll", ref _setAllIndex, ["Up", "Down", "Locked"], 3)`
        - Use a sentinel value (-1 or track previous) so selection triggers action
        - On change: iterate all skills, call `GameActions.ChangeSkillLockStatus((ushort)i, (byte)lockState)` for each
        - Reset combo to -1 after action (or use Button+Popup pattern instead)
        - Alternative: Use 3 separate small buttons "All Up" / "All Down" / "All Lock" if combo UX is awkward

        **Reset +/- button:**
        - `ImGui.Button("Reset +/-")`
        - On click: copy all current `skill.Base` values into `_baselineBase[]`

        **Copy All button:**
        - `ImGui.Button("Copy All")`
        - Build tab-separated string with header row: `Name\tValue\tBase\tCap\t+/-\tLock`
        - Iterate skills in current sort order, append each skill's data
        - Call `SDL3.SDL.SDL_SetClipboardText(result)`
        - Show feedback: `GameActions.Print("Skills copied to clipboard.", Constants.HUE_SUCCESS)`

        **Show Groups checkbox:**
        - `ImGui.Checkbox("Show Groups", ref _showGroups)`
        - When checked, grouped view is used (implemented in next task)
        - When unchecked, flat sorted table

        **Total display:**
        - Compute sum of all Skill.Base and all Skill.Cap
        - `ImGui.Text($"Total: {baseSum:F1} / {capSum:F1}")`
        - Right-align using `ImGui.SameLine(ImGui.GetWindowWidth() - textWidth)` or similar
      </description>
      <steps>
        - Add toolbar row before the table in DrawContent
        - Implement Set All as combo or 3 buttons — iterate all skills on change
        - Implement Reset button — snapshot current bases to _baselineBase
        - Implement Copy All — build tab-separated string, copy to clipboard, show message
        - Add Show Groups checkbox (just toggle bool, grouped rendering in next task)
        - Add Total display showing sum of bases and caps
        - Use ImGui.SameLine between toolbar items
      </steps>
      <test_steps>
        1. Build succeeds
        2. Toolbar renders above the skills table on one line
        3. Set All → select "Locked" → all skills send lock packets
        4. Reset +/- → all delta values reset to 0.0
        5. Copy All → paste into text editor, verify tab-separated skill data with header
        6. Show Groups checkbox toggles (grouped view in next task)
        7. Total shows correct sum of base values / cap values
      </test_steps>
      <review></review>
    </task>

    <task id="grouped-view" priority="3" category="functional">
      <title>Implement grouped view using SkillsGroupManager</title>
      <description>
        When "Show Groups" checkbox is checked, render skills organized by
        SkillsGroupManager groups instead of a flat table.

        Access: `World.Instance.SkillsGroupManager.Groups` (List of SkillsGroup)

        Grouped rendering approach:
        - Keep the same table structure (same 7 columns)
        - Iterate each SkillsGroup in Groups list
        - For each group, render a `ImGui.TreeNodeEx` collapsible header with group name and total
          - Format: `"{group.Name} ({groupBaseTotal:F1})"` where groupBaseTotal = sum of bases for skills in group
          - Use `ImGuiTreeNodeFlags.DefaultOpen` for initially expanded
        - Inside each tree node, render table rows for the group's skills
          - Get skill indices via `group.GetSkill(i)` for i in 0..group.Count
          - Apply current sort order within each group
          - Groups themselves stay in their original order

        Edge cases:
        - SkillsGroupManager may not be loaded — guard with null check
        - Skill index from group.GetSkill may be 0xFF (invalid) — skip those
        - Skill index may exceed player skills array length — skip those

        The flat view (Show Groups unchecked) remains the default and uses _sortedIndices as before.
      </description>
      <steps>
        - In DrawContent, branch on _showGroups flag
        - When grouped: iterate SkillsGroupManager.Groups
        - For each group, render TreeNodeEx with name and base total
        - Inside tree node, collect group skill indices, sort them by current sort, render table rows
        - Guard against null SkillsGroupManager, invalid indices (0xFF), out-of-range indices
        - Keep flat view as the else branch (existing implementation)
      </steps>
      <test_steps>
        1. Build succeeds
        2. Check "Show Groups" — skills reorganize into collapsible groups (Combat, Magic, etc.)
        3. Each group header shows group name and total base points
        4. Expand/collapse groups by clicking headers
        5. Sort by column — skills within each group re-sort, group order unchanged
        6. Uncheck "Show Groups" — returns to flat sorted view
        7. Lock toggles and Use buttons still work in grouped view
      </test_steps>
      <review></review>
    </task>
  </tasks>

  <success_criteria>
    - `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj` compiles without errors
    - Skills tab appears in Legion Assistant window and populates with character skills
    - Table sorts by any column in both directions
    - Lock toggles cycle correctly and send server packets
    - Use buttons trigger skill use for clickable skills
    - Set All bulk lock updates all skills
    - +/- tracks changes with green/red coloring, Reset clears deltas
    - Copy All exports tab-separated data to clipboard
    - Show Groups toggles between flat and grouped view
    - Total skill points display is accurate
    - No crashes on null player, empty skills, or missing group manager
  </success_criteria>
</project_specification>
