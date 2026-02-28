---
title: "Auto-Loot Priority Tiers"
created: 2026-02-08
poured:
  - TazUo-s6t
  - TazUo-11n
  - TazUo-gae
  - TazUo-115
  - TazUo-116
iteration: 1
auto_discovery: false
auto_learnings: false
---
<project_specification>
<project_name>Auto-Loot Priority Tiers</project_name>

  <overview>
    Add tier-based priority (High / Normal / Low) to auto-loot entries so high-value items
    are always grabbed first. Currently the system uses a FIFO queue — whatever the server
    sends first gets looted first. With large loot lists (500+ entries), valuable items can
    get stuck behind junk and be lost if a corpse decays or the player walks away.

    When multiple entries match the same item, the highest-priority entry wins (determining
    destination container). All existing configs remain backward-compatible — the new Priority
    field defaults to Normal.
  </overview>

  <context>
    <existing_patterns>
      - Enums in Managers/ follow simple PascalCase pattern with explicit int values when serialization matters (e.g., GraphicObjectType : byte in GraphicsReplacement.cs, AgentType in BuySellAgent.cs)
      - ImGui combo boxes use `ImGui.Combo(label, ref int index, string[] names, int count)` with unique `$"##label{i}"` IDs and a parallel values array. `ImGui.SetNextItemWidth()` always precedes the combo. See GraphicReplacementTabContent.cs:304-311 and MacrosTabContent.cs:448-452
      - AutoLootTabContent entry table: 6 columns, entries iterate in reverse order (newest first), per-entry state cached in `Dictionary<string, string>` keyed by `entry.Uid`
      - Unit tests use xUnit + FluentAssertions, AAA pattern, naming convention `MethodOrComponent_Scenario_ExpectedOutcome`, organized with `#region` blocks
      - Test helpers: `CreateTestManager()`, `CreateWorldWithPlayer()`, `CreateGroundItem()`, `CreateCacheTestManager()`, `SetProfileForScavenger()`
    </existing_patterns>
    <integration_points>
      - AutoLootManager.cs line 43: Queue declaration — replace with PriorityQueue
      - AutoLootManager.cs lines 96-103: LootItem() — enqueue with priority key
      - AutoLootManager.cs line 440: Update() — dequeue from PriorityQueue
      - AutoLootManager.cs lines 148-202: IsOnLootList() — change first-match to best-match
      - AutoLootManager.cs lines 761-807: AutoLootConfigEntry class — add enum and property
      - AutoLootManager.cs lines 17-22: AutoLootJsonContext — source generator picks up new property automatically
      - AutoLootManager.cs line 80: NotifyEntryChanged() for structural changes, line 86: NotifyMatchCriteriaChanged() for match-only changes
      - AutoLootTabContent.cs line 229: Table setup — add 7th column for Priority
      - AutoLootTabContent.cs lines 239-371: Entry rendering loop — add combo box
      - AutoLootConfigEntry.Equals() (line 806) — add Priority to equality check for deduplication
    </integration_points>
    <new_technologies>
      - PriorityQueue<TElement, TPriority> available since .NET 6, fully supported on .NET 10. Min-heap by default — negate priority int so High (2) dequeues before Low (0). Note: PriorityQueue is NOT stable — equal-priority items may not preserve insertion order, which is acceptable.
    </new_technologies>
    <conventions>
      - JSON serialization requires source-generated context (CLAUDE.md rule). AutoLootJsonContext already registers AutoLootConfigEntry and List of it — new enum property is picked up automatically
      - No license headers on new files (CLAUDE.md rule)
      - Notification pattern: NotifyEntryChanged() rebuilds graphic index + clears cache. NotifyMatchCriteriaChanged() clears cache only. Priority changes need cache clear since they affect best-match result
      - Tests mirror source structure: tests/ClassicUO.UnitTests/Game/Managers/AutoLootManagerTest.cs
    </conventions>
  </context>

  <tasks>
    <task id="enum-and-property" priority="0" category="functional">
      <title>Add AutoLootPriority enum and property to AutoLootConfigEntry</title>
      <description>
        Add the priority enum and property to the data model. This is the foundation
        all other tasks depend on.

        File: src/ClassicUO.Client/Game/Managers/AutoLootManager.cs

        1. Add enum before AutoLootConfigEntry class (~line 760):
           public enum AutoLootPriority { Low = 0, Normal = 1, High = 2 }

        2. Add property to AutoLootConfigEntry (~line 767):
           public AutoLootPriority Priority { get; set; } = AutoLootPriority.Normal;

        3. Update Equals() (line 806) to include Priority in comparison so entries
           differing only in priority are treated as distinct for deduplication.

        The AutoLootJsonContext source generator will automatically pick up the new
        property. Old JSON configs without the field will deserialize to Normal (default).
      </description>
      <steps>
        - Add AutoLootPriority enum with Low=0, Normal=1, High=2 before AutoLootConfigEntry class
        - Add Priority property with default AutoLootPriority.Normal to AutoLootConfigEntry
        - Update Equals() to include Priority in the comparison
      </steps>
      <test_steps>
        1. dotnet build -c Debug — compiles cleanly
        2. Existing unit tests pass (dotnet test tests/ClassicUO.UnitTests/)
        3. Verify JSON round-trip: serialize an entry with Priority=High, deserialize, confirm value preserved
        4. Verify backward compat: deserialize JSON without Priority field, confirm defaults to Normal
      </test_steps>
      <review></review>
    </task>

    <task id="priority-queue" priority="0" category="functional">
      <title>Replace FIFO Queue with PriorityQueue</title>
      <description>
        Swap the loot queue from Queue to PriorityQueue so items are dequeued
        in priority order (High first, then Normal, then Low).

        File: src/ClassicUO.Client/Game/Managers/AutoLootManager.cs

        1. Line 43 — change declaration:
           Before: Queue&lt;(uint item, AutoLootConfigEntry entry)&gt;
           After:  PriorityQueue&lt;(uint item, AutoLootConfigEntry entry), int&gt;

        2. Line 100 in LootItem() — change enqueue:
           int pri = entry != null ? -(int)entry.Priority : -(int)AutoLootPriority.Normal;
           _lootItems.Enqueue((item, entry), pri);

        3. Line 440 in Update() — PriorityQueue.Dequeue() returns the element directly,
           same destructuring syntax works. No change needed to the dequeue line itself.

        4. Queue.Count and PriorityQueue.Count have the same API — no changes needed at
           lines 428, 443, 455.
      </description>
      <steps>
        - Replace Queue declaration with PriorityQueue on line 43
        - Update LootItem() enqueue call to pass negated priority as sort key
        - Verify Update() dequeue and Count references still compile
      </steps>
      <test_steps>
        1. dotnet build -c Debug — compiles cleanly
        2. Existing unit tests pass
        3. New unit test: enqueue items with Low, Normal, High priorities in random order, dequeue all, verify High comes first then Normal then Low
      </test_steps>
      <review></review>
    </task>

    <task id="best-match" priority="1" category="functional">
      <title>Change IsOnLootList() to return highest-priority match</title>
      <description>
        Currently IsOnLootList() returns the first matching entry (breaks on first match).
        Change it to iterate all matching entries and return the one with the highest Priority.

        File: src/ClassicUO.Client/Game/Managers/AutoLootManager.cs, lines 148-202

        1. Lines 177-183 (graphic index loop): Instead of breaking on first match,
           track the best match. If an entry matches and has higher Priority than current
           best, update best.

        2. Lines 185-191 (wildcard loop): Same change — iterate all wildcards,
           track best match across both loops.

        3. The cache at lines 194-199 stores the result — now it stores the best match
           instead of first match. Cache invalidation is unchanged.

        Performance note: graphic buckets are typically 1-5 entries per graphic ID.
        Iterating the full bucket instead of short-circuiting is negligible cost.
      </description>
      <steps>
        - Refactor graphic index loop (lines 177-183) to track best-priority match instead of breaking
        - Refactor wildcard loop (lines 185-191) similarly, continuing the same best-match tracking
        - Ensure cache stores the best match result
      </steps>
      <test_steps>
        1. dotnet build -c Debug — compiles cleanly
        2. Existing unit tests pass
        3. New unit test: two entries match same item — one Normal with container A, one High with container B. IsOnLootList returns the High entry
        4. New unit test: wildcard High entry beats graphic-specific Normal entry
        5. New unit test: single matching entry still returned correctly (regression)
      </test_steps>
      <review></review>
    </task>

    <task id="ui-column" priority="2" category="functional">
      <title>Add Priority dropdown column to AutoLootTabContent</title>
      <description>
        Add a combo box column to the auto-loot entry table so users can set
        priority per entry.

        File: src/ClassicUO.Client/Game/UI/ImGuiControls/Agents/AutoLootTabContent.cs

        1. Line 229: Change column count from 6 to 7

        2. Add column setup after existing columns (near line 235):
           ImGui.TableSetupColumn("Priority", ImGuiTableColumnFlags.WidthFixed, 80)

        3. In the entry rendering loop (lines 239-371), add a new TableNextColumn block
           with an ImGui.Combo for Priority. Follow the existing pattern:
           - string[] priorityNames = { "High", "Normal", "Low" };
           - AutoLootPriority[] priorityValues = { AutoLootPriority.High, AutoLootPriority.Normal, AutoLootPriority.Low };
           - Map current entry.Priority to index, render combo
           - On change: set entry.Priority, call NotifyMatchCriteriaChanged()

        Place the Priority column after the Hue column and before the Regex column
        for logical grouping (match criteria together, then actions).
      </description>
      <steps>
        - Increase table column count from 6 to 7
        - Add "Priority" column setup with 80px fixed width
        - Add ImGui.Combo in the entry loop with High/Normal/Low options
        - On change, update entry.Priority and call NotifyMatchCriteriaChanged()
      </steps>
      <test_steps>
        1. dotnet build -c Debug — compiles cleanly
        2. Launch client, open TazUO Options > Agents > Auto Loot
        3. Verify Priority column appears in the table
        4. Add a new entry — verify it defaults to "Normal"
        5. Change priority to "High" — verify it persists after closing and reopening options
        6. Import an old config (no Priority field) — verify all entries show "Normal"
      </test_steps>
      <review></review>
    </task>

    <task id="unit-tests" priority="2" category="functional">
      <title>Add unit tests for priority features</title>
      <description>
        Add comprehensive unit tests covering all priority-related behavior.

        File: tests/ClassicUO.UnitTests/Game/Managers/AutoLootManagerTest.cs

        Follow existing test patterns: xUnit, FluentAssertions, AAA, region blocks,
        naming convention MethodOrComponent_Scenario_ExpectedOutcome.

        Tests to add:
        1. AutoLootConfigEntry_DefaultPriority_ShouldBeNormal
        2. AutoLootConfigEntry_Equals_DifferentPriority_ShouldNotBeEqual
        3. PriorityQueue_MixedPriorities_DequeuesHighFirst
        4. PriorityQueue_SamePriority_AllDequeued (order doesn't matter)
        5. IsOnLootList_MultiplMatchesDifferentPriority_ReturnsHighest
        6. IsOnLootList_WildcardHighBeatsGraphicNormal
        7. IsOnLootList_SingleMatch_StillWorks (regression)
        8. MatchCache_StoresBestMatch_NotFirstMatch
        9. JsonRoundTrip_PriorityPreserved
        10. JsonBackwardCompat_MissingPriority_DefaultsToNormal
      </description>
      <steps>
        - Add #region Priority Tests block
        - Implement all 10 test cases listed above
        - Use existing test helpers (CreateTestManager, CreateCacheTestManager, etc.)
      </steps>
      <test_steps>
        1. dotnet test tests/ClassicUO.UnitTests/ — all tests pass including new ones
        2. Verify no existing tests were broken
      </test_steps>
      <review></review>
    </task>
  </tasks>

  <success_criteria>
    - High-priority items are looted before Normal and Low-priority items
    - When multiple entries match the same item, the highest-priority entry determines destination container
    - Existing auto-loot configs load without changes (Priority defaults to Normal)
    - All existing unit tests pass unchanged
    - New unit tests cover priority ordering, best-match, and backward compatibility
    - No performance regression — graphic index and spatial tracking are untouched
    - UI dropdown is intuitive and consistent with existing ImGui patterns
  </success_criteria>
</project_specification>
