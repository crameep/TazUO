---
title: "Auto-Loot Performance Optimization"
created: 2026-02-08
poured:
  - TazUo-bea
  - TazUo-tp4
  - TazUo-dud
  - TazUo-zag
  - TazUo-znu
  - TazUo-kqx
  - TazUo-114
  - TazUo-twj
  - TazUo-4jv
  - TazUo-a2i
  - TazUo-b1q
  - TazUo-53g
  - TazUo-fbg
iteration: 1
auto_discovery: false
auto_learnings: false
---
<project_specification>
<project_name>Auto-Loot Performance Optimization</project_name>

  <overview>
    The auto-loot system in TazUO stutters and lags when players have large loot lists (200+ entries).
    The root cause is a linear scan of the entire loot list for every item, combined with redundant
    re-evaluation across multiple event triggers and a full world-item scan on every player step
    (scavenger mode). Three independent fixes address these bottlenecks: a graphic-based index for
    fast lookup, spatial tracking to replace the world scan, and an OPL-aware match cache to
    eliminate redundant evaluations. All fixes are in-memory only -- no save file format changes,
    no backwards compatibility risk.
  </overview>

  <context>
    <existing_patterns>
      - Singleton manager pattern: AutoLootManager uses lazy `Instance` property with `field` keyword
      - Event-driven via static EventSink class: subscribe in OnSceneLoad, unsubscribe in OnSceneUnload
      - JSON serialization requires generated JsonSerializerContext (AutoLootJsonContext)
      - GridHighLightData uses multi-level caching: _normalizeCache (Dictionary), _cacheValid flag, EnsureCache() pattern
      - GridHighLightData uses static reusable collections (_reusableItemData, _reusableRequeueItems) to reduce GC pressure
      - GridHighLightData processes items in batches (3 per frame) to avoid stalls
      - Time-based cache clearing pattern: _nextClearRecents with Time.Ticks + 5000 (5-second window)
      - HashSet-based deduplication: _quickContainsLookup and _recentlyLooted prevent duplicate processing
      - Dictionary-based O(1) lookup used elsewhere: ObjectPropertiesListManager._itemsProperties, World.Items, World.Mobiles
    </existing_patterns>
    <integration_points>
      - AutoLootManager.cs (src/ClassicUO.Client/Game/Managers/AutoLootManager.cs) -- primary file, all 3 fixes
      - AutoLootTabContent.cs (src/ClassicUO.Client/Game/UI/ImGuiControls/Agents/AutoLootTabContent.cs) -- UI mutations need to notify manager
      - ObjectPropertiesListManager.cs -- provides OPL data via TryGetNameAndData(); timing critical for match cache
      - GridHighLightData.cs -- LootOnMatch sets item.ShouldAutoLoot bypassing IsOnLootList; must not break
      - EventSink.cs -- event definitions (OPLOnReceive, OnItemCreated, OnItemUpdated, OnOpenContainer, OnPositionChanged)
      - NearbyLootGump.cs -- calls AddAutoLootEntry() on Shift+Click
      - GridContainer.cs -- calls AddAutoLootEntry() on Shift+Click
      - AutoLootManagerTest.cs (tests/ClassicUO.UnitTests/Game/Managers/AutoLootManagerTest.cs) -- existing xUnit tests (740 lines)
    </integration_points>
    <new_technologies>
      - No new technologies. All fixes use standard C# Dictionary, HashSet, and List.
    </new_technologies>
    <conventions>
      - No license headers on new files (CLAUDE.md rule)
      - All JSON serialize/deserialize need generated context (CLAUDE.md rule)
      - Build command: dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj
      - Test framework: xUnit with FluentAssertions
      - Tests at: tests/ClassicUO.UnitTests/
      - Existing tests use AAA pattern, boundary value testing, serialization round-trips
      - Fields use underscore prefix (_autoLootItems), properties use PascalCase
      - Private methods use camelCase-like naming (CheckAndLoot, IsOnLootList)
    </conventions>
  </context>

  <tasks>

    <!-- ==================== FIX 1: GRAPHIC INDEX ==================== -->

    <task id="graphic-index-data-structures" priority="1" category="functional">
      <title>Add graphic index data structures and rebuild method</title>
      <description>
        Add a Dictionary&lt;int, List&lt;AutoLootConfigEntry&gt;&gt; (_graphicIndex) and a
        List&lt;AutoLootConfigEntry&gt; (_wildcardEntries) to AutoLootManager. Add a
        RebuildGraphicIndex() method that clears both, then iterates _autoLootItems and
        buckets each entry: entries with Graphic == -1 go into _wildcardEntries, all others
        go into _graphicIndex keyed by their Graphic value.

        PITFALL: Graphic = 0 is a valid graphic ID (not a wildcard). Only Graphic = -1 is wildcard.
        PITFALL: Thread safety -- Load() runs on Task.Factory.StartNew(). Call RebuildGraphicIndex()
        BEFORE setting _loaded = true so the index is fully built before any event handler reads it.
      </description>
      <steps>
        - Add field: private Dictionary&lt;int, List&lt;AutoLootConfigEntry&gt;&gt; _graphicIndex = new()
        - Add field: private List&lt;AutoLootConfigEntry&gt; _wildcardEntries = new()
        - Add method RebuildGraphicIndex() that clears both and re-buckets from _autoLootItems
        - Wildcard check: entry.Graphic == -1 goes to _wildcardEntries, else _graphicIndex
        - Call RebuildGraphicIndex() in Load() BEFORE setting _loaded = true (line 387)
        - Call RebuildGraphicIndex() in AutoLootList setter (line 36)
      </steps>
      <test_steps>
        1. Build the project: dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj
        2. Verify RebuildGraphicIndex correctly separates wildcard (Graphic == -1) from specific entries
        3. Verify Graphic = 0 entries go into _graphicIndex[0], NOT _wildcardEntries
        4. Verify empty list produces empty index and empty wildcard list
      </test_steps>
      <review></review>
    </task>

    <task id="graphic-index-lookup" priority="1" category="functional">
      <title>Replace IsOnLootList linear scan with indexed lookup</title>
      <description>
        Modify IsOnLootList(Item i) to use the graphic index instead of iterating all entries.
        Look up _graphicIndex[item.Graphic] for graphic-specific entries, then also check
        _wildcardEntries. Check both sets with entry.Match(i) and return the first match.

        PITFALL: Must check BOTH the graphic bucket AND the wildcard list. If you only check
        the graphic bucket, wildcard entries (Graphic == -1) will never match.
        PITFALL: If a user has mostly wildcard entries, the index provides less benefit -- this is
        acceptable, it's still a net win for the common case.
      </description>
      <steps>
        - Modify IsOnLootList to use _graphicIndex.TryGetValue(i.Graphic, out var entries)
        - If entries found, iterate them and call entry.Match(i), return first match
        - Always also iterate _wildcardEntries and call entry.Match(i)
        - Remove the old foreach loop over _autoLootItems
        - Preserve the !_loaded early return
      </steps>
      <test_steps>
        1. Build the project successfully
        2. Verify items matching graphic-specific entries are still found
        3. Verify items matching wildcard entries (Graphic == -1) are still found
        4. Verify items that don't match any entry return null
        5. Verify hue and regex checks still apply within the narrowed bucket
      </test_steps>
      <review></review>
    </task>

    <task id="graphic-index-mutation-hooks" priority="1" category="functional">
      <title>Wire up index rebuild at all loot list mutation points</title>
      <description>
        Add a public NotifyEntryChanged() method to AutoLootManager that calls
        RebuildGraphicIndex(). Call RebuildGraphicIndex() directly after list mutations
        inside the manager, and call NotifyEntryChanged() from AutoLootTabContent.cs
        after UI property edits.

        6 mutation points that need rebuild:
        1. AddAutoLootEntry() -- after _autoLootItems.Add(item) at line 139
        2. TryRemoveAutoLootEntry() -- after _autoLootItems.RemoveAt(removeAt) at line 176
        3. ImportEntries() -- after _autoLootItems.AddRange(newItems) at line 480
        4. AutoLootTabContent.cs line 260 -- entry.Graphic changed
        5. AutoLootTabContent.cs lines 277, 281 -- entry.Hue changed
        6. AutoLootTabContent.cs lines 200, 307 -- entry.RegexSearch changed

        PITFALL: The UI code directly modifies entry properties without going through the manager.
        Using NotifyEntryChanged() (Option A from PRD) is simplest -- no serialization impact.
        PITFALL: Public AutoLootList setter must also rebuild. Change the setter to call
        RebuildGraphicIndex() after assignment.
      </description>
      <steps>
        - Add public void NotifyEntryChanged() method that calls RebuildGraphicIndex()
        - Call RebuildGraphicIndex() after _autoLootItems.Add() in AddAutoLootEntry()
        - Call RebuildGraphicIndex() after _autoLootItems.RemoveAt() in TryRemoveAutoLootEntry()
        - Call RebuildGraphicIndex() after _autoLootItems.AddRange() in ImportEntries()
        - Modify AutoLootList setter to call RebuildGraphicIndex() after setting _autoLootItems
        - In AutoLootTabContent.cs: call AutoLootManager.Instance.NotifyEntryChanged() after entry.Graphic = newGraphic (line 260)
        - In AutoLootTabContent.cs: call NotifyEntryChanged() after entry.Hue changes (lines 277, 281)
        - In AutoLootTabContent.cs: call NotifyEntryChanged() after entry.RegexSearch changes (lines 200, 307)
      </steps>
      <test_steps>
        1. Build the project successfully
        2. Add a new entry via UI -> verify it appears in the graphic index immediately
        3. Delete an entry via UI -> verify it's removed from the graphic index
        4. Edit an entry's graphic via UI -> verify the index reflects the new graphic
        5. Import entries from clipboard -> verify all imported entries are indexed
        6. Change an entry's regex -> verify matching behavior updates
      </test_steps>
      <review></review>
    </task>

    <task id="graphic-index-tests" priority="2" category="functional">
      <title>Add unit tests for graphic index</title>
      <description>
        Add tests to the existing AutoLootManagerTest.cs file to verify the graphic index
        behaves correctly. Cover: index building, wildcard separation, mutation-triggered
        rebuilds, edge cases (Graphic=0, empty list, all wildcards).
      </description>
      <steps>
        - Add test: index correctly buckets entries by graphic
        - Add test: wildcard entries (Graphic == -1) are separated
        - Add test: Graphic = 0 is treated as a normal key, not wildcard
        - Add test: adding an entry rebuilds the index
        - Add test: removing an entry rebuilds the index
        - Add test: importing entries rebuilds the index
        - Add test: empty list produces empty index
        - Add test: list of all wildcards produces empty graphic index, full wildcard list
      </steps>
      <test_steps>
        1. Run: dotnet test tests/ClassicUO.UnitTests/ --filter "AutoLoot"
        2. All new tests pass
        3. All existing tests still pass
      </test_steps>
      <review></review>
    </task>

    <!-- ==================== FIX 2: SPATIAL TRACKING ==================== -->

    <task id="spatial-tracking-data-structures" priority="1" category="functional">
      <title>Add nearby ground items tracking set</title>
      <description>
        Add a HashSet&lt;uint&gt; (_nearbyGroundItems) to AutoLootManager that tracks serials
        of ground items within a broad tracking radius (~20 tiles). This set will be used by
        OnPositionChanged instead of scanning all world items.

        Add helper method IsTrackableGroundItem(Item item) that checks:
        item != null, item.OnGround, !item.IsCorpse, !item.IsLocked.

        Add constant SCAVENGER_TRACKING_RADIUS = 20 for the broad tracking radius.

        PITFALL: Memory is negligible -- 10-100 entries of 4 bytes each.
        PITFALL: Must clear the set in OnSceneUnload() to prevent stale data across world changes.
      </description>
      <steps>
        - Add field: private readonly HashSet&lt;uint&gt; _nearbyGroundItems = new()
        - Add constant: private const int SCAVENGER_TRACKING_RADIUS = 20
        - Add helper: private bool IsTrackableGroundItem(Item item) checking OnGround, !IsCorpse, !IsLocked
        - Clear _nearbyGroundItems in OnSceneUnload()
      </steps>
      <test_steps>
        1. Build the project successfully
        2. Verify the set is cleared on scene unload
      </test_steps>
      <review></review>
    </task>

    <task id="spatial-tracking-population" priority="1" category="functional">
      <title>Populate tracking set from item events</title>
      <description>
        Modify OnItemCreatedOrUpdated to add ground items within SCAVENGER_TRACKING_RADIUS
        to _nearbyGroundItems. Also remove items from the set if they're no longer on the ground
        (picked up, moved to container). Add a bootstrap method BootstrapNearbyGroundItems()
        that does a one-time full world scan to populate the set when scavenger mode is first
        enabled.

        PITFALL: Items already on the ground when scavenger enables won't fire OnItemCreated.
        The bootstrap scan handles this. It's a one-time cost.
        PITFALL: Items entering client visibility (18 tiles) fire OnItemCreated as the server
        sends them -- these WILL be caught automatically, no gap.
        PITFALL: Items moved by server-side scripts without triggering OnItemUpdated will be
        pruned during iteration when their distance exceeds the tracking radius.
      </description>
      <steps>
        - In OnItemCreatedOrUpdated: if scavenger enabled and IsTrackableGroundItem(i) and i.Distance &lt;= SCAVENGER_TRACKING_RADIUS, add i.Serial to _nearbyGroundItems
        - In OnItemCreatedOrUpdated: if item is NOT a trackable ground item, remove i.Serial from _nearbyGroundItems (handles pickup)
        - Add method BootstrapNearbyGroundItems() that iterates _world.Items.Values once, adding all trackable ground items within SCAVENGER_TRACKING_RADIUS
        - Call BootstrapNearbyGroundItems() when scavenger is enabled (first OnPositionChanged with scavenger on and empty set)
      </steps>
      <test_steps>
        1. Build the project successfully
        2. Enable scavenger mode -> verify bootstrap populates nearby items
        3. New item appears on ground within range -> verify it's added to tracking set
        4. Item is picked up (leaves ground) -> verify it's removed from tracking set
      </test_steps>
      <review></review>
    </task>

    <task id="spatial-tracking-position-handler" priority="1" category="functional">
      <title>Replace OnPositionChanged world scan with tracked set iteration</title>
      <description>
        Replace the _world.Items.Values iteration in OnPositionChanged with iteration over
        _nearbyGroundItems. For each serial in the set, look up the item, prune invalid/out-of-range
        entries, and CheckAndLoot items within loot range (Distance &lt; 3).

        PITFALL: Iterating a HashSet while modifying it throws. Collect serials to remove in a
        temporary list, then remove after iteration. OR use a snapshot/copy approach.
        PITFALL: item.Distance is relative to player's current position and recalculates
        automatically -- no manual recalculation needed.
        PITFALL: OnPositionChanged fires ~4 times/second while running. The iteration must be
        cheap. Iterating 50-100 entries with dictionary lookups is ~0.01ms.
        PITFALL: The HashSet approach avoids the _world.Items.Values collection-modified-during-
        enumeration risk entirely since we iterate our own set.
      </description>
      <steps>
        - Replace the foreach (Item item in _world.Items.Values) block in OnPositionChanged
        - New logic: iterate _nearbyGroundItems, look up each serial via _world.Items.Get()
        - If item is null (destroyed), not on ground, or locked -> mark for removal
        - If item.Distance >= SCAVENGER_TRACKING_RADIUS -> mark for removal (left tracking area)
        - If item.Distance &lt; 3 (within loot range) -> CheckAndLoot(item)
        - After iteration, remove all marked serials from the set
        - If _nearbyGroundItems is empty and scavenger is enabled, call BootstrapNearbyGroundItems()
      </steps>
      <test_steps>
        1. Build the project successfully
        2. Walk toward items on the ground -> verify they're picked up within 3 tiles
        3. Walk away from items -> verify they are NOT picked up
        4. Verify no more iteration over _world.Items.Values in OnPositionChanged
        5. Verify destroyed items are pruned from the set
        6. Verify items that moved out of range are pruned
      </test_steps>
      <review></review>
    </task>

    <task id="spatial-tracking-tests" priority="2" category="functional">
      <title>Add unit tests for spatial tracking</title>
      <description>
        Add tests to verify spatial tracking behavior: bootstrap population, event-driven
        updates, pruning of invalid items, and correct loot range checking.
      </description>
      <steps>
        - Add test: bootstrap scan populates set with nearby ground items
        - Add test: items beyond tracking radius are not added
        - Add test: non-ground items (in containers) are not tracked
        - Add test: corpses and locked items are excluded
        - Add test: items removed from world are pruned during iteration
        - Add test: items moving out of range are pruned
        - Add test: set is cleared on scene unload
      </steps>
      <test_steps>
        1. Run: dotnet test tests/ClassicUO.UnitTests/ --filter "AutoLoot"
        2. All new tests pass
        3. All existing tests still pass
      </test_steps>
      <review></review>
    </task>

    <!-- ==================== FIX 3: MATCH CACHE ==================== -->

    <task id="match-cache-data-structures" priority="2" category="functional">
      <title>Add OPL-aware match cache data structures</title>
      <description>
        Add a Dictionary&lt;uint, AutoLootConfigEntry?&gt; (_matchCache) that stores match results
        per item serial. Add a HashSet&lt;uint&gt; (_matchCacheHasOpl) that tracks whether each
        cached result was evaluated WITH OPL data available. This enables safe invalidation
        when OPL arrives for an item that was previously evaluated without it.

        PITFALL (CRITICAL): The OPL timing problem. Items are created BEFORE their tooltips arrive.
        If we cache "no match" before OPL loads, and OPL later arrives with properties that WOULD
        match a regex entry, the cached "no match" causes the item to be silently skipped forever.
        The _matchCacheHasOpl set solves this by tracking whether OPL was present at cache time.

        PITFALL: Thread safety -- _matchCache is a regular Dictionary (not concurrent). All access
        is on the game thread after _loaded = true. Load() thread doesn't touch the cache.
      </description>
      <steps>
        - Add field: private readonly Dictionary&lt;uint, AutoLootConfigEntry?&gt; _matchCache = new()
        - Add field: private readonly HashSet&lt;uint&gt; _matchCacheHasOpl = new()
        - Add method ClearMatchCache() that clears both
        - Call ClearMatchCache() in OnSceneUnload()
      </steps>
      <test_steps>
        1. Build the project successfully
        2. Verify cache structures are initialized empty
        3. Verify ClearMatchCache() clears both the cache and the OPL tracking set
      </test_steps>
      <review></review>
    </task>

    <task id="match-cache-lookup-logic" priority="2" category="functional">
      <title>Add OPL-aware cache logic to IsOnLootList</title>
      <description>
        Modify IsOnLootList to check the cache before running the matching logic. On cache hit,
        return the cached result. On cache miss, run the matching logic, store the result, and
        record whether OPL was available during evaluation.

        For negative cache hits (cached null / no match): check if OPL is NOW available but
        WASN'T when we cached (serial NOT in _matchCacheHasOpl). If so, the cache is stale --
        clear that entry and re-evaluate. This is Option B from the PRD.

        PITFALL (CRITICAL): The exact sequence to guard against:
        1. OnItemCreated fires, OPL not loaded yet
        2. RegexCheck falls back to ItemData.Name ("a longsword")
        3. Regex for "Damage Increase" fails -> cached as null without OPL
        4. OPL arrives: "a longsword\nDamage Increase 50%"
        5. OnOPLReceived fires -> cache hit -> must detect OPL was missing before -> re-evaluate

        PITFALL: ShouldAutoLoot items bypass IsOnLootList entirely (checked at line 98-101
        in CheckAndLoot). These never enter the cache. This is correct behavior.
      </description>
      <steps>
        - At start of IsOnLootList, check _matchCache.TryGetValue(i.Serial, out var cached)
        - If cache hit with non-null value (positive match) -> return cached
        - If cache hit with null value (negative match):
          - Check if OPL is now available via _world.OPL.TryGetNameAndData(i.Serial, ...)
          - If OPL available AND serial NOT in _matchCacheHasOpl -> stale cache, fall through to re-evaluate
          - Otherwise -> return null (valid negative cache)
        - On cache miss or stale cache: run matching logic (graphic index + wildcard scan)
        - Store result in _matchCache[i.Serial]
        - If OPL was available during evaluation: add i.Serial to _matchCacheHasOpl
        - If OPL was NOT available: ensure serial is NOT in _matchCacheHasOpl
        - Return result
      </steps>
      <test_steps>
        1. Build the project successfully
        2. Verify cache hit returns stored result without re-running matching
        3. Verify cache miss runs matching and stores result
        4. Verify negative cache is invalidated when OPL arrives for that item
        5. Verify positive cache is NOT invalidated when OPL arrives (already correct)
        6. CRITICAL: Verify item with regex entry is NOT silently skipped when OPL arrives after initial check
      </test_steps>
      <review></review>
    </task>

    <task id="match-cache-invalidation" priority="2" category="functional">
      <title>Wire up cache invalidation at all mutation and timer points</title>
      <description>
        Clear the entire match cache when the loot list changes (all 6 mutation points).
        Invalidate individual cache entries when OPL is received. Clear the cache periodically
        on the existing 5-second timer to prevent memory growth.

        Cache must be cleared at:
        1. AddAutoLootEntry() -- new entry might match previously-rejected items
        2. TryRemoveAutoLootEntry() -- removed entry might have been the cached match
        3. ImportEntries() -- same as add
        4-6. UI property edits (graphic, hue, regex) via NotifyEntryChanged()
        7. OnOPLReceived -- invalidate single entry for that serial
        8. Every 5 seconds (piggyback on _recentlyLooted clear timer)
        9. OnSceneUnload -- world changing, all serials invalid

        PITFALL: NotifyEntryChanged() from Fix 1 already exists. Extend it to also call
        ClearMatchCache(), so both the graphic index and match cache are rebuilt/cleared together.
        PITFALL: For OnOPLReceived invalidation, only remove the specific serial from _matchCache
        and _matchCacheHasOpl. Don't clear the whole cache -- OPL arrives frequently.
        PITFALL: Item property changes (imbuing, enhancing) fire OnItemUpdated. Always invalidate
        that serial's cache entry on OnItemUpdated. Item updates are rare enough.
      </description>
      <steps>
        - Extend NotifyEntryChanged() to also call ClearMatchCache() (covers mutations 1-6)
        - In OnOPLReceived: remove e.Serial from _matchCache and _matchCacheHasOpl before calling CheckCorpse
        - In OnItemCreatedOrUpdated: remove i.Serial from _matchCache and _matchCacheHasOpl
        - In Update() where _recentlyLooted.Clear() happens (line 280): also call ClearMatchCache()
        - ClearMatchCache() already called in OnSceneUnload from data structures task
      </steps>
      <test_steps>
        1. Build the project successfully
        2. Add a new loot entry -> verify entire cache is cleared
        3. Delete a loot entry -> verify entire cache is cleared
        4. Import entries -> verify entire cache is cleared
        5. Edit entry property in UI -> verify entire cache is cleared
        6. OPL received for specific item -> verify only that item's cache entry is invalidated
        7. Wait 5+ seconds with no looting -> verify cache is cleared
        8. Leave game world -> verify cache is cleared
      </test_steps>
      <review></review>
    </task>

    <task id="match-cache-tests" priority="2" category="functional">
      <title>Add unit tests for match cache with OPL timing</title>
      <description>
        Add comprehensive tests for the match cache, with special focus on the OPL timing
        edge case. This is the highest-risk component and needs thorough coverage.
      </description>
      <steps>
        - Add test: positive cache hit returns stored entry without re-matching
        - Add test: negative cache hit returns null without re-matching
        - Add test: cache miss runs matching and stores result
        - Add test: CRITICAL -- item cached as "no match" WITHOUT OPL is re-evaluated when OPL arrives
        - Add test: CRITICAL -- item cached as "no match" WITH OPL is NOT re-evaluated on subsequent checks
        - Add test: CRITICAL -- regex-only entry matches item after OPL arrives despite initial cache miss
        - Add test: full cache clear on loot list mutation
        - Add test: single entry invalidation on OPL receive
        - Add test: periodic cache clear on 5-second timer
        - Add test: cache clear on scene unload
        - Add test: ShouldAutoLoot items bypass cache entirely
        - Add test: memory growth -- cache clears prevent unbounded growth
      </steps>
      <test_steps>
        1. Run: dotnet test tests/ClassicUO.UnitTests/ --filter "AutoLoot"
        2. All new tests pass, especially the OPL timing tests
        3. All existing tests still pass
        4. No test relies on timing -- use deterministic OPL state setup
      </test_steps>
      <review></review>
    </task>

    <!-- ==================== INTEGRATION ==================== -->

    <task id="integration-verification" priority="2" category="functional">
      <title>Integration verification and regression testing</title>
      <description>
        After all three fixes are implemented, verify the complete system works together.
        Run all existing tests, build the project, and document any manual testing needed.

        Key integration checks:
        - Graphic index + match cache work together (cache stores indexed results)
        - Spatial tracking + match cache work together (scavenger items are cached)
        - NotifyEntryChanged() rebuilds index AND clears cache in one call
        - GridHighlight LootOnMatch still works (bypasses both index and cache)
        - AutoLoot.json round-trip is unchanged
        - Cross-character import works
      </description>
      <steps>
        - Run full test suite: dotnet test tests/ClassicUO.UnitTests/
        - Build the project: dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj
        - Verify AutoLoot.json can be loaded from existing save files
        - Verify AutoLoot.json saved after changes can be reloaded
        - Verify cross-character import works
        - Verify GridHighlight LootOnMatch still triggers auto-loot correctly
        - Verify scavenger mode picks up ground items
        - Verify corpse looting works with graphic-specific and wildcard entries
        - Verify regex entries match after OPL arrives
      </steps>
      <test_steps>
        1. dotnet test tests/ClassicUO.UnitTests/ -- all tests pass
        2. dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -- builds clean
        3. Load an existing AutoLoot.json with 100+ entries -- no errors
        4. Save and reload -- identical behavior
        5. All manual testing scenarios from PRD section 6 pass
      </test_steps>
      <review></review>
    </task>

  </tasks>

  <success_criteria>
    - Auto-loot with 500+ entries has no visible stuttering when opening corpses
    - Scavenger mode walking with 500+ entries has no per-step lag
    - Same items are looted as before -- zero behavior changes
    - No silent item skipping due to OPL timing
    - AutoLoot.json format unchanged -- existing configs work without migration
    - Grid Highlight "Loot on Match" continues working
    - All existing unit tests pass
    - New unit tests cover graphic index, spatial tracking, and OPL-aware cache
  </success_criteria>

</project_specification>
