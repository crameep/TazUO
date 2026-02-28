---
title: "Auto-Loot Profiles"
created: 2026-02-11
poured:
  - TazUo-mol-joq
  - TazUo-mol-vj7
  - TazUo-mol-bjx
  - TazUo-mol-4hs
  - TazUo-mol-wlu
  - TazUo-mol-0rz
  - TazUo-mol-87b
  - TazUo-mol-1kj
  - TazUo-mol-fed
  - TazUo-mol-604
  - TazUo-mol-1vj
  - TazUo-mol-8kx
  - TazUo-mol-hi9
  - TazUo-mol-1rs
  - TazUo-mol-vet
  - TazUo-mol-hhz
  - TazUo-mol-al0
  - TazUo-mol-wa3
  - TazUo-mol-i96
  - TazUo-mol-j4y
  - TazUo-mol-198
  - TazUo-mol-2vy
  - TazUo-mol-962
  - TazUo-mol-hnc
  - TazUo-mol-h8z
  - TazUo-mol-ctt
  - TazUo-mol-gn7
  - TazUo-mol-024
iteration: 3
auto_discovery: false
auto_learnings: false
---
<project_specification>
<project_name>Auto-Loot Profiles</project_name>

  <overview>
    Replace the single flat auto-loot list with a named profile system. Each profile is a
    self-contained set of loot entries (graphic, hue, regex, priority, destination container).
    Profiles are displayed in a sidebar with checkboxes to toggle them active/inactive.
    The auto-loot engine sees the union of all active profiles' entries, with highest-priority-wins
    conflict resolution when multiple entries match the same item.

    Depends on the auto-loot priority tiers feature (PR #363 / autoloot-priority-tiers branch).
  </overview>

  <context>
    <existing_patterns>
      - OrganizerAgent uses sidebar + detail view with selectable list pattern (OrganizerTabContent.cs)
      - OrganizerAgent stores multiple configs in a single JSON file as List of OrganizerConfig
      - OrganizerAgent.GetUniqueName() checks display names against existing configs list
      - DressAgentManager has cross-character config loading via GetAllCharacterPaths()
      - AutoLootManager uses singleton Instance pattern with lazy initialization
      - AutoLootManager.Load() runs async via Task.Factory.StartNew()
      - JsonHelper.SaveAndBackup() provides 3-level backup rotation (creates .backup1/.backup2/.backup3)
      - JsonHelper.Load() tries main file then backup1/2/3 on failure
      - TabContent base class provides DrawArt() and SetTooltip() helpers
      - ImGui tables use ##UniqueId suffix pattern for unique widget IDs
      - Input state tracked via Dictionary keyed by entry.Uid — cleared on import (AutoLootTabContent.cs:96-99)
      - ImGui.BeginPopupContextItem() must be called IMMEDIATELY after the widget it applies to
      - OrganizerTabContent uses ImGuiTableFlags.Resizable with WidthFixed for sidebar column (no explicit px)
      - ExportToFile()/ImportFromFile() exist at AutoLootManager.cs:435-468 (file-based, operate on flat list)
      - Existing Save() is only called on OnSceneUnload — field edits are NOT saved per-keystroke
      - Directory.GetFiles(*, "*.json") does NOT match .backup1/.backup2/.backup3 files (different extension)
    </existing_patterns>
    <integration_points>
      - AutoLootManager.cs — core loot logic, loading/saving, matching engine
      - AutoLootTabContent.cs — ImGui UI for loot entry table, import/export buttons
      - AutoLootJsonContext (AutoLootManager.cs:18-24) — source-generated JSON context, namespace-level
      - ProfileManager.ProfilePath — per-character storage directory
      - ObjectActionQueue — ActionPriority enum for priority-based looting
      - AutoLootConfigEntry — existing entry model (graphic, hue, regex, priority, destination, uid)
      - CheckAndLoot/IsOnLootList — matching loop that iterates the loot list
      - ImportFromJson/GetJsonExport — clipboard serialization methods
      - ImportFromOtherCharacter/GetOtherCharacterConfigs — cross-character import
      - NearbyLootGump.cs:464 — Shift+Click calls AddAutoLootEntry() (external caller)
      - GridContainer.cs:1383 — Shift+Click calls AddAutoLootEntry() (external caller)
      - Ancient migration path: CUOEnviroment.ExecutablePath/Data/Profiles/AutoLoot.json
    </integration_points>
    <new_technologies>
      - No new technologies — uses existing ImGui, System.Text.Json, and file I/O patterns
    </new_technologies>
    <conventions>
      - All JSON serialize/deserialize must use source-generated context (JsonSerializable attribute)
      - No license headers on new files
      - JsonHelper.SaveAndBackup for persistence with backup rotation
      - Profile-scoped files stored in ProfileManager.ProfilePath directory
      - OrganizerAgent.GetUniqueName() pattern for duplicate name prevention
      - Nested types inside AutoLootManager need full type path in JsonSerializable attributes
        (e.g., AutoLootManager.AutoLootProfile)
      - Filename sanitization via Path.GetInvalidFileNameChars() (see FileSystemHelper.cs)
      - Error messages via GameActions.Print() for consistency with existing patterns
    </conventions>
  </context>

  <tasks>
    <task id="profile-model" priority="0" category="infrastructure">
      <title>Create AutoLootProfile data model and JSON context</title>
      <description>
        Define the AutoLootProfile class with Name, IsActive, FileName, and Entries fields.
        Add it to the AutoLootJsonContext for source-generated serialization.
        This is the foundation all other tasks build on.
      </description>
      <steps>
        - Add public class AutoLootProfile inside AutoLootManager (must be public for unit test access)
          with properties:
          Name (string, default ""), IsActive (bool, default true),
          Entries (List of AutoLootConfigEntry, default new()),
          FileName (string, [JsonIgnore]) — stores the actual filename on disk, set during
          load and create, used by SaveProfile to avoid filename instability
        - Do NOT add DisplayOrder yet — defer to drag-reorder task to avoid dead weight
        - Add [JsonSerializable(typeof(AutoLootManager.AutoLootProfile))] to AutoLootJsonContext
          (note: full type path needed because AutoLootProfile is nested inside AutoLootManager).
          Do NOT add List of AutoLootProfile — profiles are stored individually, never as a list.
        - Write a unit test that creates an AutoLootProfile with 2 entries (one with non-default
          Priority=High and a RegexSearch), serializes via AutoLootJsonContext.Default.AutoLootProfile,
          deserializes, and asserts Name/IsActive/Entries/Priority/RegexSearch are preserved.
          Also verify FileName is NOT serialized (JsonIgnore).
      </steps>
      <test_steps>
        1. Build the project — verify no compilation errors
        2. Run unit test — verify round-trip serialization preserves all fields except FileName
      </test_steps>
      <review></review>
    </task>

    <task id="profile-storage" priority="0" category="functional">
      <title>Implement profile storage in AutoLootProfiles directory</title>
      <description>
        Replace single-file storage with per-profile files in an AutoLootProfiles/ subdirectory.
        Each profile is stored as a separate JSON file named after the profile.
        Implement load, save, create, rename, and delete operations. FileName property on each
        profile tracks the actual disk filename to prevent instability.
      </description>
      <steps>
        - Add _profilesDir field: Path.Combine(ProfileManager.ProfilePath, "AutoLootProfiles")
          Set in constructor alongside existing _savePath (line 57). Keep _savePath for migration use.
        - Add Profiles list (List of AutoLootProfile) to AutoLootManager
        - Add SelectedProfile property to AutoLootManager (stored in manager so external callers
          like NearbyLootGump and GridContainer can add entries to the selected profile)
        - Implement GetUniqueName(string baseName) for profiles — check display names against
          Profiles list, follow OrganizerAgent.GetUniqueName() pattern (OrganizerAgent.cs:119-126)
        - Implement SanitizeFileName(string name) — strip chars from Path.GetInvalidFileNameChars().
          Check for collisions against existing filenames on disk (excluding the profile's own file
          if it already exists). Append counter if collision detected. Return the unique filename.
        - Implement LoadProfiles() — keep inside Task.Factory.StartNew() (async, same as current):
          Use Directory.GetFiles(_profilesDir, "*.json") to scan files (this naturally excludes
          .backup1/.backup2/.backup3 files since their extension is not .json).
          Deserialize each as AutoLootProfile using AutoLootJsonContext.Default.AutoLootProfile.
          Set each profile's FileName property to the filename that was loaded (Path.GetFileName).
          On individual file failure: log error, skip that profile, continue loading others.
          Build the full Profiles list, then assign it atomically (new list, single reference swap).
          Set _loaded = true only AFTER all files are processed.
        - Implement SaveProfile(AutoLootProfile) — serialize to
          AutoLootProfiles/{profile.FileName} using JsonHelper.SaveAndBackup with
          AutoLootJsonContext.Default.AutoLootProfile. Uses the stored FileName, NOT derived
          from the current Name (avoids the instability bug where GetUniqueFileName sees its own
          file on disk and generates a different name).
        - Implement SaveAll() — iterate Profiles and call SaveProfile on each. Called from
          OnSceneUnload() as a safety net.
        - Save strategy: for add/remove entry operations, save the affected profile immediately.
          For in-place field edits (graphic, hue, regex, priority, destination), rely on SaveAll()
          on scene unload (matches current behavior — no per-keystroke saves).
        - Implement CreateProfile(string name) — use GetUniqueName for display name, SanitizeFileName
          for disk name, set FileName on the profile, add to Profiles, save to disk, return it
        - Implement DeleteProfile(AutoLootProfile) — delete the .json file AND its .backup1/.backup2/
          .backup3 files from disk, remove from Profiles list. Prevent deleting the last profile.
        - Implement RenameProfile(AutoLootProfile, string newName) — get unique display name, get
          sanitized filename, delete old .json AND old .backup files from disk, update Name and
          FileName properties, save with new filename
        - Update AddAutoLootEntry() — add to SelectedProfile.Entries instead of _autoLootItems.
          Update the duplicate check to search SelectedProfile.Entries (not the old flat list).
          External callers (NearbyLootGump, GridContainer) don't change — they call the same method.
          Then call RebuildMergedList() and SaveProfile(SelectedProfile).
          Null guard: if SelectedProfile is null AND _loaded is true, fall back to first profile
          in Profiles or create Default. If _loaded is false, log warning and return.
        - Update TryRemoveAutoLootEntry() — search SelectedProfile.Entries by Uid (not all profiles).
          Then call RebuildMergedList() and SaveProfile(SelectedProfile).
        - Remove ExportToFile() and ImportFromFile() methods (AutoLootManager.cs:435-468) — these
          operate on the flat list which no longer exists. File-based export/import is out of scope.
        - Remove _savePath field (no longer needed after migration logic is extracted)
        - Remove _autoLootItems field (replaced by per-profile Entries + _mergedEntries)
        - Replace the old Load() method to call LoadProfiles()
        - Replace the old Save() method with SaveAll()
      </steps>
      <test_steps>
        1. Build successfully
        2. Manually create a test AutoLootProfiles/Test.json file — verify it loads and FileName is set
        3. Verify creating a profile writes a .json file to disk with correct FileName
        4. Verify deleting a profile removes the .json and .backup files
        5. Verify renaming deletes old .json and .backup files, creates new file
        6. Verify AddAutoLootEntry checks duplicates against SelectedProfile.Entries
        7. Verify AddAutoLootEntry with null SelectedProfile and _loaded=true falls back to first profile
        8. Verify AddAutoLootEntry with _loaded=false logs warning and does not crash
        9. Verify SaveProfile uses stored FileName (rename then save doesn't create duplicate files)
      </test_steps>
      <review></review>
    </task>

    <task id="migration" priority="1" category="functional">
      <title>Migrate existing AutoLoot.json to Default profile</title>
      <description>
        On first load, if AutoLoot.json exists and AutoLootProfiles/ does not,
        migrate all entries into a Default.json profile. Leave the old file in place
        for safety. Handle three possible states: new install, current format, ancient format.
        NOTE: the current code at line 390-392 MOVES the ancient file (File.Move). The new code
        should READ it instead — do NOT copy the old File.Move pattern.
      </description>
      <steps>
        - In LoadProfiles(), before scanning the directory, check migration states in order:
          1. AutoLootProfiles/ dir exists with .json files → normal load, no migration
             (Note: dir with only .backup files counts as empty since *.json glob won't match them)
          2. AutoLootProfiles/ dir exists but is empty → create empty Default profile, save immediately
          3. AutoLoot.json exists at ProfileManager.ProfilePath → migrate to Default profile
          4. AutoLoot.json exists at ancient path (CUOEnviroment.ExecutablePath/Data/Profiles/AutoLoot.json)
             → READ from ancient path (do NOT File.Move like the old code), migrate to Default profile
          5. Nothing exists → create empty Default profile, save immediately
        - For migration (cases 3 and 4): read entries using JsonHelper.Load with
          AutoLootJsonContext.Default.ListAutoLootConfigEntry (the existing list format).
          JsonHelper.Load tries backup files too, which helps recover from corruption.
        - Create AutoLootProfile with Name="Default", IsActive=true, Entries=migrated entries,
          FileName="Default.json"
        - Create AutoLootProfiles/ directory, save via SaveProfile() (reuses profile-storage method)
        - Leave old AutoLoot.json untouched (no deletion, no data loss)
        - Save empty Default profile to disk immediately (cases 2 and 5) — establishes directory
          structure so migration check doesn't re-run
        - Log migration status: "Migrated N entries from AutoLoot.json to Default profile" or
          "Created empty Default profile"
      </steps>
      <test_steps>
        1. With existing AutoLoot.json and no AutoLootProfiles/ dir — verify Default.json created
        2. Verify AutoLoot.json still exists (not deleted)
        3. Verify entries in Default profile match the original
        4. On subsequent loads, verify migration does not run again
        5. With empty AutoLootProfiles/ dir — verify empty Default.json is created
        6. With nothing — verify empty Default.json is created
        7. With ancient path AutoLoot.json — verify migration reads (not moves) from old location
      </test_steps>
      <review></review>
    </task>

    <task id="merged-list" priority="1" category="functional">
      <title>Build merged loot list from active profiles</title>
      <description>
        The auto-loot engine needs a single merged list of entries from all active profiles.
        When multiple entries match the same item, highest priority wins.
        The merged list rebuilds when profiles are toggled or entries change.
        Thread-safe via atomic reference swap. Guarded by _loaded flag.
      </description>
      <steps>
        - Add _mergedEntries (List of AutoLootConfigEntry) to AutoLootManager
        - Implement RebuildMergedList():
          * Guard: if !_loaded, return immediately (prevents race with async Load)
          * Create a NEW list instance
          * Collect all entries from profiles where IsActive=true
          * Assign the new list to _mergedEntries in a single reference assignment (atomic swap)
          This prevents InvalidOperationException if IsOnLootList is iterating on the network
          thread while the UI triggers a rebuild.
        - Update AutoLootList property getter to return _mergedEntries
        - Call RebuildMergedList() when: a profile is toggled active/inactive, entries are
          added/removed in an active profile. In-place field edits (priority, hue, regex) are
          reflected immediately via shared object references — no rebuild needed, just SaveProfile
          on scene unload.
        - Update IsOnLootList() (AutoLootManager.cs:115-124):
          * Optimization: if only ONE profile is active, keep the current early-exit behavior
            (return first match). This is the common case and avoids a performance regression.
          * If MULTIPLE profiles are active: full-scan — iterate ALL _mergedEntries, collect
            matches, return the match with the highest Priority (High > Normal > Low).
            On ties, first match wins (determined by profile iteration order).
          * This optimization avoids the perf regression for the common single-profile case
            while correctly handling multi-profile conflict resolution.
        - Redirect all remaining _autoLootItems references to use profiles or _mergedEntries:
          * IsOnLootList (line 119) → iterate _mergedEntries
          * GetJsonExport (line 567) → moved to per-profile export in context-menu task
          * ImportFromJson (line 582) → moved to per-profile import in context-menu task
          * ImportEntries (line 493-506) → update to add to a specified profile's Entries
        - Remove the old _autoLootItems field entirely
      </steps>
      <test_steps>
        1. Single active profile — verify early-exit matching (first match wins, fast)
        2. Two active profiles with overlapping entries at different priorities — verify highest wins
        3. Enable both profiles — verify higher priority entry's destination is used
        4. Disable one — verify its entries are no longer matched
        5. Add an entry to an active profile — verify merged list updates immediately
        6. Edit an entry's priority in-place — verify it takes effect without explicit rebuild
        7. Verify RebuildMergedList is a no-op when _loaded is false
        8. Verify no crash when toggling profiles while items are being checked on another thread
      </test_steps>
      <review></review>
    </task>

    <task id="sidebar-ui" priority="1" category="functional">
      <title>Add profile sidebar to auto-loot tab</title>
      <description>
        Restructure the auto-loot tab with a left sidebar listing profiles
        (checkbox + name) and the loot entry table on the right. Follow the
        OrganizerTabContent sidebar + detail view pattern.
      </description>
      <steps>
        - Remove the old lootEntries field (AutoLootTabContent.cs:21) — replaced by
          _selectedProfile.Entries throughout
        - Draw global settings (Enable Auto Loot, Scavenger, Progress Bar, etc. — lines 51-86)
          BEFORE the profile layout table. Everything up to and including ImGui.SeparatorText("Entries:")
          stays outside the table.
        - Wrap the rest of DrawContent() in a 2-column ImGui table: use
          ImGui.BeginTable("AutoLootProfileTable", 2, ImGuiTableFlags.Resizable).
          Column 0: ImGuiTableColumnFlags.WidthFixed (match OrganizerTabContent pattern, no hardcoded px).
          Column 1: ImGuiTableColumnFlags.WidthStretch.
        - Left column — draw sidebar:
          * Handle loading state: if AutoLootManager profiles not loaded yet, show "Loading..."
          * Lazy-init: on first render where Profiles is non-empty and _selectedProfile is null,
            auto-select the first profile (set both _selectedProfile AND
            AutoLootManager.Instance.SelectedProfile)
          * For each profile in AutoLootManager.Instance.Profiles:
            - ImGui.Checkbox for IsActive toggle. On change: call SaveProfile, RebuildMergedList.
            - ImGui.SameLine()
            - ImGui.Selectable for profile name. If clicked: set BOTH _selectedProfile (UI field)
              AND AutoLootManager.Instance.SelectedProfile (manager field) to keep them in sync.
              Also set _selectedProfileIndex.
              Clear all input dictionaries (entryGraphicInputs, entryHueInputs, entryRegexInputs,
              entryDestinationInputs) to avoid stale state from previous profile.
          * "New Profile" button at bottom — calls CreateProfile("New Profile"), selects it
            (update both UI and manager SelectedProfile)
        - Right column — draw entry table:
          * "Import from Character" button stays in right column header area (near Add Manual Entry
            and Add from Target)
          * If _selectedProfile is null: show "Select a profile" prompt (match OrganizerTabContent
            null-state pattern at line 96-100)
          * Otherwise: draw the existing loot entry table using _selectedProfile.Entries
        - Track _selectedProfile (AutoLootProfile ref) and _selectedProfileIndex (int) as fields
        - Update these specific call sites to operate on _selectedProfile.Entries:
          1. Add Manual Entry (line 187) — add to _selectedProfile.Entries, call SaveProfile + RebuildMergedList
          2. Add from Target (line 139) — same
          3. Delete button (line 352) — remove from _selectedProfile.Entries, call SaveProfile + RebuildMergedList
          4. Clipboard Import button (line 93) — moved to context-menu task
      </steps>
      <test_steps>
        1. Open auto-loot tab — verify sidebar on left, entries on right
        2. Click a profile name — verify its entries appear, previous profile's entries disappear
        3. Toggle checkbox — verify loot behavior changes (active/inactive)
        4. Click "New Profile" — verify new profile appears and is selected
        5. Verify global settings remain above both columns
        6. Open tab before profiles finish loading — verify no crash, shows loading state
        7. Switch profiles — verify input fields reset (no stale values from previous profile)
        8. Shift+Click item in GridContainer — verify it goes to the profile selected in sidebar
           (confirms UI and manager SelectedProfile are in sync)
      </test_steps>
      <review></review>
    </task>

    <task id="context-menu" priority="2" category="functional">
      <title>Add right-click context menu and clipboard operations for profiles</title>
      <description>
        Right-clicking a profile in the sidebar opens a context menu with
        Rename, Delete, Export to Clipboard, and Import from Clipboard.
        Clipboard import supports both the new profile format and legacy entry-list format.
        Context menu operations target the right-clicked profile, which may differ from
        the selected profile.
      </description>
      <steps>
        - Add _contextMenuProfile field — stores which profile was right-clicked (may differ from
          _selectedProfile). Set when context menu opens.
        - After each ImGui.Selectable() in the sidebar loop, IMMEDIATELY call
          ImGui.BeginPopupContextItem($"##ProfileCtx{i}") (must be right after Selectable, before
          any other ImGui calls — ImGui ordering requirement).
          When the popup opens, set _contextMenuProfile = current profile.
        - "Rename" menu item:
          * Use modal popup approach (consistent with existing Import from Character modal at
            AutoLootTabContent.cs:372): set _showRenamePopup flag and _renameInput = _contextMenuProfile.Name
          * Draw rename popup outside the loop: ImGui.BeginPopupModal("Rename Profile") with
            InputText for new name, OK/Cancel buttons
          * On OK: call RenameProfile(_contextMenuProfile, _renameInput)
        - "Delete" menu item:
          * Use ImGui.BeginDisabled/EndDisabled when Profiles.Count <= 1 (grey out, don't silently skip)
          * On click: set _showDeletePopup flag
          * Draw confirmation popup outside loop: "Delete profile '{_contextMenuProfile.Name}'?"
          * On confirm: call DeleteProfile(_contextMenuProfile). If deleted profile was _selectedProfile,
            select another profile (previous or first). Update both UI and manager SelectedProfile.
        - "Export to Clipboard" menu item:
          * Add new method GetProfileJsonExport(AutoLootProfile profile) to AutoLootManager —
            serialize using AutoLootJsonContext.Default.AutoLootProfile (exports full profile
            object: Name + IsActive + Entries, NOT just the entry list)
          * Call .CopyToClipboard() on the result
          * GameActions.Print("Exported profile to clipboard!", Constants.HUE_SUCCESS)
        - "Import from Clipboard" menu item:
          * Read clipboard text
          * Try deserialize as AutoLootProfile first (new format)
          * If that fails, try deserialize as List of AutoLootConfigEntry (legacy/backward-compat) —
            wrap in new AutoLootProfile with Name="Imported"
          * If both fail: GameActions.Print("Clipboard does not contain valid profile data.", Constants.HUE_ERROR)
          * On success: use GetUniqueName on the profile name, set IsActive=false (safety — prevents
            stale merged list), add to Profiles, save to disk. If user wants it active they toggle.
        - Move the existing top-bar Import/Export buttons to the context menu (they now operate
          per-profile, not on the flat list)
      </steps>
      <test_steps>
        1. Right-click a NON-selected profile — verify context menu targets the right-clicked one
        2. Rename — verify name changes in sidebar and filename on disk
        3. Delete with 2+ profiles — verify confirmation, profile removed
        4. Delete the selected profile — verify another profile becomes selected
        5. Delete with only 1 profile — verify Delete is greyed out
        6. Export — verify JSON on clipboard contains profile Name, IsActive, and Entries
        7. Import new format — paste exported profile JSON, verify new inactive profile created
        8. Import legacy format — paste old List of AutoLootConfigEntry JSON, verify new profile
           created named "Imported" with all entries
        9. Import garbage — verify error message via GameActions.Print, no crash
      </test_steps>
      <review></review>
    </task>

    <task id="external-callers" priority="2" category="functional">
      <title>Update external callers of AddAutoLootEntry</title>
      <description>
        NearbyLootGump.cs and GridContainer.cs call AddAutoLootEntry() via Shift+Click.
        These callers have no concept of selected profile. Verify they work correctly
        with the updated AddAutoLootEntry() that now routes to SelectedProfile.
      </description>
      <steps>
        - Verify NearbyLootGump.cs:464 Shift+Click still compiles and works — it calls
          AutoLootManager.Instance.AddAutoLootEntry() which now adds to SelectedProfile.Entries
        - Verify GridContainer.cs:1383 Shift+Click still compiles and works — same path
        - The null guard in AddAutoLootEntry (profile-storage task) handles the edge cases:
          * SelectedProfile is null + _loaded is true → fall back to first profile
          * _loaded is false → log warning, return (do NOT create a profile during async load)
        - Duplicate check in AddAutoLootEntry searches SelectedProfile.Entries only (not across
          all profiles) — this is correct: same item can exist in different profiles intentionally
        - Test that Shift+Click from grid container adds the item to the currently selected profile
          and the entry appears when that profile is viewed in the auto-loot tab
      </steps>
      <test_steps>
        1. Open a grid container, Shift+Click an item — verify entry added to selected profile
        2. Open NearbyLootGump, Shift+Click an item — verify entry added to selected profile
        3. Switch to a different profile in sidebar, Shift+Click — verify entry goes to new profile
        4. Verify no crash if profiles haven't loaded yet (Shift+Click during startup)
      </test_steps>
      <review></review>
    </task>

    <task id="import-character" priority="2" category="functional">
      <title>Update Import from Character to create profiles</title>
      <description>
        Change the "Import from Character" flow to create a new profile
        from the imported character's loot config instead of merging into
        the current list. Keep the return type as flat entry list for simplicity.
      </description>
      <steps>
        - IN AutoLootManager.cs:
          * Update LoadOtherCharacterConfig(characterPath) to try AutoLootProfiles/ first:
            scan for .json files, deserialize each as AutoLootProfile, aggregate all entries into
            a flat list. Fall back to reading AutoLoot.json if AutoLootProfiles/ dir doesn't exist.
            Keep return type as List of AutoLootConfigEntry.
          * Update GetOtherCharacterConfigs() — no return type change needed (still
            Dictionary of string to List of AutoLootConfigEntry)
          * Update ImportFromOtherCharacter() — change return type from void to AutoLootProfile.
            Instead of calling ImportEntries() (which did duplicate checking), create a new profile:
            Name = GetUniqueName("Import - {characterName}")
            IsActive = false (safety — user must toggle on explicitly)
            Entries = all imported entries as-is (NO duplicate checking — fresh profile)
            FileName = SanitizeFileName(name)
            Add to Profiles, save to disk. Return the created profile.
        - IN AutoLootTabContent.cs:
          * Update the Import from Character popup UI:
            Keep text "Select a character to import autoloot configuration from:"
            Add subtext: "This will create a new inactive profile with the imported entries."
            Button text stays "{characterName} ({configs.Count} items)"
          * After import: receive the returned AutoLootProfile, set both _selectedProfile AND
            AutoLootManager.Instance.SelectedProfile to it. Clear input dictionaries.
      </steps>
      <test_steps>
        1. Click "Import from Character" — verify popup shows other characters
        2. Select a character — verify new profile created named "Import - {characterName}"
        3. Verify imported profile is inactive by default and selected in sidebar
        4. Toggle it active — verify its entries participate in looting
        5. Verify existing profiles are not modified
        6. Import from a character that has migrated to AutoLootProfiles/ — verify all their
           profiles' entries are aggregated into one imported profile
        7. Import from a character with old AutoLoot.json — verify fallback works
      </test_steps>
      <review></review>
    </task>

    <task id="drag-reorder" priority="3" category="style">
      <title>Add drag-to-reorder for profiles in sidebar (optional)</title>
      <description>
        Allow dragging profiles up and down in the sidebar to reorder them.
        Display order only — no impact on loot behavior. This task is optional
        and can be deferred without blocking core functionality.
      </description>
      <steps>
        - Add DisplayOrder (int) property to AutoLootProfile model, default to index in Profiles list.
          Already covered by AutoLootProfile's JsonSerializable registration (simple int property).
        - Sort Profiles by DisplayOrder when rendering the sidebar
        - Implement drag-and-drop using ImGui API (note: ImGui.NET uses unsafe pointers for
          payload marshaling — use unsafe { } blocks):
          * After each Selectable: call ImGui.BeginDragDropSource(), SetDragDropPayload("PROFILE", index),
            draw drag preview (profile name text), EndDragDropSource()
          * For each Selectable as target: call ImGui.BeginDragDropTarget(),
            AcceptDragDropPayload("PROFILE"), EndDragDropTarget()
        - On drop: remove dragged profile from its position, insert at the target position,
          then reassign DisplayOrder sequentially (0, 1, 2, ...) for all profiles.
          Do NOT use a simple swap — that breaks non-adjacent moves.
        - Save all affected profiles after reorder to persist DisplayOrder
        - New profiles get DisplayOrder = max(existing) + 1 (append to end)
        - Visual feedback: draw a colored separator line at the drop target position using
          ImGui.GetCursorScreenPos() and ImGui.GetWindowDrawList().AddLine()
      </steps>
      <test_steps>
        1. Drag a profile up in the sidebar — verify order changes correctly
        2. Drag a profile from top to bottom (non-adjacent) — verify all intermediate items shift
        3. Restart client — verify order persisted
        4. Create new profile — verify it appears at the end
        5. Verify loot behavior unaffected by display order
      </test_steps>
      <review></review>
    </task>
  </tasks>

  <success_criteria>
    - Existing AutoLoot.json migrates seamlessly to Default profile on first load
    - Ancient path migration (ExecutablePath/Data/Profiles/) still works (read, not move)
    - Multiple profiles can be created, named, and toggled independently
    - Loot engine correctly unions active profile entries with highest-priority-wins conflict resolution
    - Single-profile early-exit optimization preserves existing performance
    - Clipboard export/import works per-profile (full profile format)
    - Backward-compatible import of legacy clipboard format (List of AutoLootConfigEntry)
    - Import from Character creates a new inactive profile
    - Profile sidebar follows established OrganizerTabContent UI patterns
    - All profile data persists correctly across client restarts
    - No regression in loot matching behavior for single-profile usage
    - Shift+Click add-to-autoloot from NearbyLootGump and GridContainer still works
    - UI SelectedProfile and Manager SelectedProfile stay in sync
    - No thread-safety regressions in loot matching during profile toggle
    - SaveProfile uses stored FileName (no filename instability on repeated saves)
  </success_criteria>
</project_specification>
