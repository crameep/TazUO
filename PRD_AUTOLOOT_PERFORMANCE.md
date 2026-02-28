# PRD: Auto-Loot Performance Optimization

## Document Info
- **Feature:** Auto-Loot & Scavenger Performance Improvements
- **Status:** Ready for Implementation
- **Priority:** High (user-reported lag with 200+ entry loot lists)
- **Risk:** Low to Medium-High depending on fix
- **Affected Systems:** Auto-Loot, Scavenger, Grid Highlight (Loot on Match)
- **Save Format Changes:** None (all fixes are in-memory only)

---

## 1. Problem Statement

### What users experience
When a player has a large auto-loot list (200+ entries), the game stutters. The more entries, the worse it gets:
- **~100 entries:** Barely noticeable
- **~300 entries:** Visible lag when opening corpses
- **~500 entries:** Stuttering when walking (scavenger mode) or looting
- **~1000+ entries:** Unusable, game freezes briefly every step

### Why it happens
The auto-loot system checks every item against every entry in the loot list, one by one. It does this every time:
- A corpse is opened
- An item appears in the world
- An item's tooltip (OPL) loads from the server
- The player takes a single step (scavenger mode scans ALL world items)

With 500 entries and 20 items in a corpse, that's 10,000 comparisons. With scavenger scanning 5,000 world items per step, that's 2,500,000 comparisons per step. Many of these are redundant — the same item gets checked 2-3 times from different event triggers.

### Who is affected
Power users who accumulate large loot lists over time. Common on servers with many item types (custom shards, high-loot-table servers). Scavenger mode users are hit hardest because the world scan runs on every single step.

---

## 2. Current Architecture

### Core flow
```
Game Event (corpse opened, item created, OPL received, player moved)
    |
    v
AutoLootManager event handler
    |
    v
CheckAndLoot(item)
    |
    +--> item.ShouldAutoLoot? --> LootItem() [skip loot list, GridHighlight already matched]
    |
    +--> IsOnLootList(item)
            |
            +--> foreach entry in _autoLootItems  [LINEAR SCAN - THE BOTTLENECK]
                    |
                    +--> entry.Match(item)
                            |
                            +--> Graphic check (fast, integer compare)
                            +--> Hue check (fast, integer compare)
                            +--> RegexCheck (SLOW - string concat + regex eval)
```

### Key data structures
| Structure | Type | Purpose |
|-----------|------|---------|
| `_autoLootItems` | `List<AutoLootConfigEntry>` | The loot list. Flat, unindexed. |
| `_quickContainsLookup` | `HashSet<uint>` | Prevents same item being queued twice |
| `_recentlyLooted` | `HashSet<uint>` | Prevents re-looting. Cleared every 5 seconds |
| `_lootItems` | `Queue<(uint, AutoLootConfigEntry)>` | Items waiting to be moved to bag |

### AutoLootConfigEntry fields
| Field | Type | Purpose | Matching behavior |
|-------|------|---------|-------------------|
| `Graphic` | `int` | Item appearance ID | `-1` = match any (wildcard), otherwise exact match |
| `Hue` | `ushort` | Item color | `ushort.MaxValue (65535)` = match any, otherwise exact match |
| `RegexSearch` | `string` | Text pattern to match against item tooltip | Empty = skip regex check. Non-empty = must match against name+tooltip text |
| `DestinationContainer` | `uint` | Where to put looted item | `0` = use default grab bag |
| `Name` | `string` | Display name (not used in matching) | Not used in Match() |
| `Uid` | `string` | Unique identifier (GUID) | Used for UI operations, deletion |

### Event triggers that cause item evaluation
| Event | Handler | When it fires | What it does |
|-------|---------|---------------|-------------|
| `OPLOnReceive` | `OnOPLReceived` | Item tooltip loaded from server | Checks item + its corpse container |
| `OnItemCreated` | `OnItemCreatedOrUpdated` | New item appears in world | Checks item's corpse + scavenger ground check |
| `OnItemUpdated` | `OnItemCreatedOrUpdated` | Item properties change | Same as created |
| `OnOpenContainer` | `OnOpenContainer` | Container is opened | Checks if container is a corpse, processes contents |
| `OnPositionChanged` | `OnPositionChanged` | Player moves one tile | Scavenger: iterates ALL world items |

---

## 3. The Three Fixes

Each fix is independent. They can be implemented in any order, but the recommended order is 1 -> 2 -> 3 (easiest/safest first).

---

### Fix 1: Graphic Index

**Goal:** Replace the linear scan in `IsOnLootList()` with an indexed lookup by graphic ID.

**Current code** (`AutoLootManager.cs:113-122`):
```csharp
private AutoLootConfigEntry IsOnLootList(Item i)
{
    if (!_loaded) return null;
    foreach (AutoLootConfigEntry entry in _autoLootItems)
        if (entry.Match(i))
            return entry;
    return null;
}
```

**Proposed change:** Add a `Dictionary<int, List<AutoLootConfigEntry>>` that buckets entries by their `Graphic` value. When checking an item, look up only the bucket for that item's graphic plus any wildcard entries (`Graphic == -1`).

#### New data structures
```
_graphicIndex: Dictionary<int, List<AutoLootConfigEntry>>
    Key = Graphic value (e.g., 0x0F0E for a gold coin)
    Value = List of entries with that graphic

_wildcardEntries: List<AutoLootConfigEntry>
    Entries where Graphic == -1 (match any graphic)
```

#### New IsOnLootList logic
```
1. Get item's graphic ID
2. Look up _graphicIndex[graphic] -> get 0-5 entries (typically)
3. Also get _wildcardEntries -> usually 0-2 entries
4. Check only these entries instead of all 500+
```

#### Rebuild trigger: `RebuildGraphicIndex()`
Must be called at these 6 mutation points:

| # | What changes | Where | How to trigger rebuild |
|---|-------------|-------|----------------------|
| 1 | Entry added | `AddAutoLootEntry()` line 139 | Call after `_autoLootItems.Add(item)` |
| 2 | Entry removed | `TryRemoveAutoLootEntry()` line 176 | Call after `_autoLootItems.RemoveAt(removeAt)` |
| 3 | Entries bulk imported | `ImportEntries()` line 480 | Call after `_autoLootItems.AddRange(newItems)` |
| 4 | Entry graphic changed | `AutoLootTabContent.cs` line 260 | Entry's graphic changed via UI |
| 5 | Entry hue changed | `AutoLootTabContent.cs` lines 277, 281 | Hue doesn't affect graphic index but matters for future fixes |
| 6 | Entry regex changed | `AutoLootTabContent.cs` lines 200, 307 | Regex doesn't affect graphic index but matters for future fixes |

**Note on mutation points 4-6:** These happen in the UI code (`AutoLootTabContent.cs`), not in `AutoLootManager.cs`. The UI directly modifies `entry.Graphic`, `entry.Hue`, and `entry.RegexSearch` on the config entry object. Two options:
- **Option A:** Add a method like `AutoLootManager.NotifyEntryChanged()` and call it from the UI after any property edit
- **Option B:** Make the entry properties use setters that trigger the rebuild. This is cleaner but requires changing `AutoLootConfigEntry` from simple auto-properties to backing-field properties, which requires updating the JSON serialization context.

**Recommended: Option A.** Simpler, no serialization impact.

Also must be called:
- After `Load()` completes (line 387, after `_loaded = true`)
- When `AutoLootList` setter is used (line 36)

#### Edge cases and pitfalls

**PITFALL 1: Wildcard graphic entries (Graphic == -1)**
- Entries with `Graphic == -1` match ANY item type
- These cannot be indexed — they must go in a separate `_wildcardEntries` list
- `IsOnLootList` must check BOTH the graphic-specific bucket AND the wildcard list
- If a user has 400 wildcard entries and 100 graphic-specific entries, the index only helps for the 100
- **Mitigation:** This is still a net win. Most real loot lists are graphic-specific. Even partial speedup is valuable.

**PITFALL 2: Public `AutoLootList` property bypass**
- `AutoLootList` property (line 36) exposes the raw list: `public List<AutoLootConfigEntry> AutoLootList { get => _autoLootItems; set => _autoLootItems = value; }`
- External code could call `.Add()`, `.Remove()`, `.Clear()` on this list directly, bypassing the rebuild
- The getter returns the actual list reference, not a copy
- **Current callers that SET the property:** Only `Load()` via JSON deserialization
- **Current callers that READ the property:** `AutoLootTabContent.cs` (for display), `NearbyLootGump.cs`, `GridContainer.cs`
- None of the current callers mutate through the getter except through `AutoLootManager` methods
- **Mitigation:** Change the setter to also call `RebuildGraphicIndex()`. The getter is fine — external code uses manager methods for mutations.

**PITFALL 3: Thread safety during Load()**
- `Load()` runs on `Task.Factory.StartNew()` (line 362) — a background thread
- It sets `_autoLootItems` on that thread, then sets `_loaded = true`
- If we rebuild the index inside `Load()`, the rebuild also runs on the background thread
- The index could be read from the main game thread while being built
- **Mitigation:** Build the index on the background thread BEFORE setting `_loaded = true`. Since `IsOnLootList()` checks `_loaded` first (line 115), the index won't be read until it's fully built.

**PITFALL 4: Entry with Graphic = 0**
- `Graphic` is `int`, default value is `0`
- `Graphic = 0` is a valid graphic ID (it means "no graphic" or "invisible")
- Make sure the index handles `0` as a normal key, not as a special case
- Don't confuse `0` (specific graphic) with `-1` (wildcard)

**PITFALL 5: Duplicate entries in index**
- `AddAutoLootEntry()` checks for duplicates (lines 135-137) before adding
- But `ImportEntries()` only checks against existing entries, not within the imported batch
- Multiple entries with the same graphic are fine — the index bucket just has multiple items
- This is not a bug, just a note

#### Acceptance criteria
- [ ] `IsOnLootList()` uses the graphic index instead of linear scan
- [ ] Wildcard entries (`Graphic == -1`) are checked for every item
- [ ] Index is rebuilt at all 6 mutation points plus Load() and setter
- [ ] `AutoLoot.json` format is unchanged
- [ ] No change to loot behavior — same items are looted as before
- [ ] Performance test: 500 entries, open corpse with 20 items — should complete in <2ms (down from ~50ms)

#### Risk: LOW

---

### Fix 2: Spatial Tracking for Scavenger

**Goal:** Replace the "scan every item in the world" approach in `OnPositionChanged` with a small tracked set of nearby ground items.

**Current code** (`AutoLootManager.cs:225-236`):
```csharp
private void OnPositionChanged(object sender, PositionChangedArgs e)
{
    if (!_loaded) return;
    if(ProfileManager.CurrentProfile.EnableScavenger)
        foreach (Item item in _world.Items.Values)  // SCANS ALL WORLD ITEMS
        {
            if (item == null || !item.OnGround || item.IsCorpse || item.IsLocked) continue;
            if (item.Distance >= 3) continue;
            CheckAndLoot(item);
        }
}
```

**Proposed change:** Maintain a `HashSet<uint>` of item serials that are ground items within a trackable radius. On position change, only iterate this set.

#### New data structures
```
_nearbyGroundItems: HashSet<uint>
    Contains serials of items that: are on the ground, not a corpse, not locked, within ~10 tiles
```

#### How the set stays up to date

**Items entering the set:**
- `OnItemCreatedOrUpdated`: If item is on ground, not corpse, not locked, within radius -> add serial
- `OnPositionChanged`: After checking tracked items, do a one-time scan to pick up items that entered range (only items crossing the distance threshold, not ALL items)

**Items leaving the set:**
- Item destroyed/removed from world -> remove serial
- Item picked up (leaves ground) -> remove serial via `OnItemUpdated`
- Player moves away (item now beyond radius) -> remove during iteration when `Distance >= radius`

**Set cleared entirely:**
- `OnSceneUnload()` — leaving the game world
- Scavenger mode toggled off

#### Detailed new OnPositionChanged logic
```
1. If scavenger disabled, return
2. Iterate _nearbyGroundItems:
   a. Get item from world by serial
   b. If item is null (destroyed) or no longer on ground -> remove from set, skip
   c. If item.Distance >= 10 (left tracking radius) -> remove from set, skip
   d. If item.Distance < 3 (within loot range) -> CheckAndLoot(item)
3. Scan world items for NEW items entering tracking radius:
   - Only items with Distance < 10 that aren't in the set yet
   - Add them to set
   - If Distance < 3, also CheckAndLoot
```

**Wait — this still scans all world items in step 3!**

**Better approach: Don't scan in OnPositionChanged at all.** Instead, rely on:
- `OnItemCreatedOrUpdated` already catches new ground items (line 254): `if (ProfileManager.CurrentProfile.EnableScavenger && i.OnGround && !i.IsCorpse && !i.IsLocked && i.Distance <= ProfileManager.CurrentProfile.AutoOpenCorpseRange) CheckAndLoot(i);`
- This already handles items appearing near the player

The only gap is: **items that were already on the ground before the player walked toward them.** These items don't fire `OnItemCreated` because they already exist.

**Revised approach:** Keep a broader tracking set (items within ~20 tiles). When position changes, only iterate this set to find items now within 3 tiles. The set itself gets populated from `OnItemCreated`/`OnItemUpdated` events. We need ONE initial scan when scavenger is first enabled to bootstrap the set.

#### Edge cases and pitfalls

**PITFALL 1: Items already on ground when scavenger enables**
- If the player enables scavenger mode, there may be items on the ground that never fired a creation event (they existed before the client tracked them)
- **Mitigation:** When scavenger is enabled, do one full world scan to populate `_nearbyGroundItems`. This is a one-time cost.

**PITFALL 2: Items that exist but player hasn't "seen" yet**
- UO servers send item data as the player moves into range
- Items at the edge of the visible area (18 tiles) fire `OnItemCreated` as they enter the client's awareness
- These WILL be caught by the `OnItemCreatedOrUpdated` handler — no gap here

**PITFALL 3: Items moving without OnItemUpdated**
- Some items can be moved by server-side scripts without triggering an update event
- These items would stay in the tracking set with their old serial but at a new position
- **Mitigation:** The distance check in the iteration loop (`item.Distance >= 10`) will eventually clean them up. If they moved INTO range, the next position change check will find them. If they moved OUT of range, they get pruned.

**PITFALL 4: Memory is not a concern**
- Ground items within 20 tiles is typically 10-100 serials (4 bytes each)
- `HashSet<uint>` overhead is ~50 bytes + 4 bytes per entry = negligible

**PITFALL 5: `_world.Items.Values` iteration safety**
- The current code iterates `_world.Items.Values` directly
- If `CheckAndLoot` causes an item to be removed from the world dictionary during iteration, this would throw `InvalidOperationException`
- In practice, looting only enqueues items (line 70) — actual movement happens in `Update()` on a timer
- But item destruction from OTHER sources (another player picks it up, server despawns it) could modify the collection
- **Mitigation:** The `HashSet<uint>` approach avoids this entirely — we iterate our own set, not the world dictionary. When we look up items by serial, we handle null (item gone) gracefully.

**PITFALL 6: Scavenger range vs tracking range**
- Scavenger loot range is `ProfileManager.CurrentProfile.AutoOpenCorpseRange` (usually 2-3 tiles)
- Tracking range should be larger (e.g., 20 tiles) so items are already tracked when player walks toward them
- If tracking range is too small, items won't be in the set when the player reaches them
- If tracking range is too large, the set grows unnecessarily
- **Mitigation:** Use a fixed tracking radius of ~20 tiles. This covers the visible screen area and gives plenty of buffer.

**PITFALL 7: `OnPositionChanged` fires very frequently**
- Every single tile of movement triggers this event
- If the player is running, this fires ~4 times per second
- The iteration loop must be cheap
- **Mitigation:** Iterating a `HashSet<uint>` of 50-100 entries + one dictionary lookup per entry is extremely fast (~0.01ms). Orders of magnitude faster than the current full world scan.

#### Acceptance criteria
- [ ] `OnPositionChanged` no longer iterates `_world.Items.Values`
- [ ] Nearby ground items are tracked in a `HashSet<uint>`
- [ ] Set is populated from `OnItemCreated`/`OnItemUpdated` events
- [ ] One-time full scan when scavenger is first enabled (bootstrap)
- [ ] Set is cleared on `OnSceneUnload()`
- [ ] Items outside tracking radius are pruned during iteration
- [ ] No change to which items get looted — same behavior as before
- [ ] Performance test: 10,000 world items, player walking with scavenger — should iterate <100 items (down from 10,000)

#### Risk: LOW-MEDIUM

---

### Fix 3: Match Cache (OPL-Aware)

**Goal:** Cache the result of `IsOnLootList()` per item serial so the same item isn't evaluated against the full loot list multiple times across different event triggers.

**Why items get checked multiple times:**
When a corpse opens, this sequence typically happens:
1. `OnOpenContainer` fires -> checks all items in corpse
2. `OnItemCreated` fires for each item -> checks again
3. `OnOPLReceived` fires for each item -> checks again
4. `OnItemUpdated` fires if item properties change -> checks again

For each of these, `CheckAndLoot()` calls `IsOnLootList()` which does the full linear scan (or indexed scan after Fix 1). The `_quickContainsLookup` set prevents the same item from being *queued for looting* twice, but it does NOT prevent the expensive *matching* from happening twice. The matching is the expensive part.

**Proposed change:** Add a cache that remembers match results per item serial.

#### New data structures
```
_matchCache: Dictionary<uint, AutoLootConfigEntry?>
    Key = item serial
    Value = matched entry (or null for "checked, no match")

_matchCacheHasOpl: HashSet<uint>
    Tracks whether the cached result was made WITH OPL data available
    If OPL wasn't available when cached, the result must be re-evaluated when OPL arrives
```

#### New IsOnLootList logic with cache
```
1. If _matchCache contains item serial:
   a. If result is non-null (match found) -> return it (positive cache hit)
   b. If result is null (no match):
      - Check if OPL is NOW available but WASN'T when we cached
      - If OPL is now available and wasn't before -> cache is stale, re-evaluate
      - Otherwise -> return null (negative cache hit, safe)
2. Cache miss -> run normal matching logic
3. Record whether OPL was available during this evaluation
4. Store result in cache
5. Return result
```

#### Cache invalidation triggers

**Must clear ENTIRE cache:**
| # | What changes | Where | Why |
|---|-------------|-------|-----|
| 1 | Entry added | `AddAutoLootEntry()` | New entry might match previously-rejected items |
| 2 | Entry removed | `TryRemoveAutoLootEntry()` | Removed entry might have been the match for cached items |
| 3 | Entries imported | `ImportEntries()` | Same as add |
| 4 | Entry graphic changed | `AutoLootTabContent.cs` line 260 | Match criteria changed |
| 5 | Entry hue changed | `AutoLootTabContent.cs` lines 277, 281 | Match criteria changed |
| 6 | Entry regex changed | `AutoLootTabContent.cs` lines 200, 307 | Match criteria changed |

**Must invalidate SINGLE ENTRY:**
| # | What happens | Where | Why |
|---|-------------|-------|-----|
| 7 | OPL received for item | `OnOPLReceived()` | Item's tooltip now available — previous "no match" may be wrong |

**Must clear cache periodically:**
| # | When | Why |
|---|------|-----|
| 8 | Every 5-10 seconds | Prevent unbounded memory growth |
| 9 | On `OnSceneUnload()` | World is changing, all serials are invalid |

#### Edge cases and pitfalls

**PITFALL 1: THE OPL TIMING PROBLEM (CRITICAL)**

This is the single most dangerous edge case in the entire PRD.

**The sequence:**
```
1. Corpse opens
2. OnItemCreated fires for "a longsword" (serial 0x12345)
3. CheckAndLoot -> IsOnLootList -> runs matching
4. Regex entry wants "Damage Increase" in tooltip
5. OPL hasn't arrived yet -> RegexCheck falls back to item.ItemData.Name ("longsword")
6. Regex doesn't match "longsword" -> returns null (no match)
7. *** CACHE STORES: serial 0x12345 -> null (no match) ***
8. 200ms later, OPL packet arrives: "a longsword\nDamage Increase 50%"
9. OnOPLReceived fires -> CheckAndLoot -> IsOnLootList
10. *** CACHE HIT: serial 0x12345 -> null ***
11. Item is SKIPPED despite matching the regex when OPL is present
12. Player never sees it looted. Silent failure.
```

**Why this is especially dangerous:**
- No error message. No crash. No visual feedback.
- The item just silently doesn't get looted.
- Players won't know it's happening unless they manually check corpses.
- It would appear to work for items that DON'T use regex (graphic+hue only matches), making it hard to diagnose.

**Solution options:**

**Option A: Never cache negative results (safest, least effective)**
- Only cache "yes, match entry X" results
- If cache miss, always re-evaluate
- Eliminates the OPL timing problem entirely
- But: items that genuinely don't match are re-evaluated every time (the most common case)
- Net effect: only eliminates redundant checks for items that DO match

**Option B: Track OPL availability in cache (recommended)**
- When caching, record whether OPL data was available at evaluation time
- Use `_matchCacheHasOpl` HashSet to track this
- When OPL arrives (`OnOPLReceived`), check if the cached result was made without OPL
- If so, invalidate that specific cache entry and re-evaluate
- Net effect: eliminates redundant checks for ALL items, handles OPL timing correctly

**Option C: Invalidate on every OPL receive (simple, slightly wasteful)**
- On `OnOPLReceived`, always remove that serial from the cache
- Don't bother tracking whether OPL was available before
- Simpler code, but items that already had OPL get unnecessarily re-evaluated
- Net effect: still good, slightly less optimal than Option B

**Recommended: Option B** for maximum safety with maximum cache effectiveness.

**PITFALL 2: ShouldAutoLoot bypass**
- `CheckAndLoot()` checks `item.ShouldAutoLoot` BEFORE `IsOnLootList()` (line 98-101)
- If GridHighlight's `LootOnMatch` set `ShouldAutoLoot = true`, the item is looted WITHOUT going through `IsOnLootList`
- The cache is never consulted for these items
- This is CORRECT behavior — don't cache ShouldAutoLoot results in the match cache
- **No action needed**, but important to understand

**PITFALL 3: Memory growth**
- Every item the player encounters gets a cache entry
- In a busy area, hundreds of items per minute
- `_recentlyLooted` already clears every 5 seconds (line 280-282)
- **Mitigation:** Clear `_matchCache` on the same 5-second timer. Piggyback on the existing clear cycle. Items that are still around will be re-evaluated (cheap if Fix 1 is in place) and re-cached.

**PITFALL 4: Thread safety**
- `Load()` runs on `Task.Factory.StartNew()` (line 362)
- It sets `_loaded = true` at the end
- The cache should not be accessed before `_loaded` is true
- `IsOnLootList()` already checks `_loaded` first (line 115), so this is safe
- But: if we call `RebuildGraphicIndex()` (Fix 1) from the background thread AND the cache clear happens there too, we need to ensure no race
- **Mitigation:** `_matchCache` should be a regular `Dictionary` (not concurrent) since all access is on the game thread AFTER `_loaded = true`. The `Load()` thread doesn't touch the cache.

**PITFALL 5: Item property changes**
- Items in UO can have their properties changed (imbuing, enhancing, item bless/curse)
- `OnItemUpdated` fires when this happens
- The cached match result may be based on old properties
- **Mitigation:** `OnItemUpdated` triggers `CheckAndLoot`, which should check the cache. If the item's OPL revision has changed since caching, invalidate and re-evaluate. OR: simply always invalidate on `OnItemUpdated` for that serial. Item updates are rare enough that this is fine.

**PITFALL 6: Interaction with `_quickContainsLookup` and `_recentlyLooted`**
- `CheckAndLoot()` line 89: `if (!_loaded || i == null || _quickContainsLookup.Contains(i.Serial)) return;`
- Items already queued for looting are short-circuited BEFORE reaching `IsOnLootList`
- The cache would never be consulted for these items
- This is correct — no double-caching concern
- `_recentlyLooted` prevents re-queuing in `LootItem()` (line 68), also before the cache would matter
- **No action needed**, but these guards mean the cache is only consulted for items NOT already being looted

**PITFALL 7: Cache and Fix 1 (Graphic Index) interaction**
- If Fix 1 is implemented first, `IsOnLootList` is already fast for graphic-specific entries
- The cache still helps by eliminating redundant evaluations across events
- But the marginal benefit is smaller — the uncached path is cheaper
- **Recommendation:** Implement Fix 1 first. If performance is sufficient, Fix 3 may not be needed. Measure before implementing.

#### Acceptance criteria
- [ ] Match results are cached per item serial
- [ ] OPL timing is handled: cache entries made without OPL are invalidated when OPL arrives
- [ ] Cache is cleared on all 6 loot list mutation points
- [ ] Cache is cleared periodically (every 5-10 seconds)
- [ ] Cache is cleared on `OnSceneUnload()`
- [ ] `ShouldAutoLoot` path is NOT affected by cache
- [ ] No change to loot behavior — same items are looted as before
- [ ] No silent item skipping due to OPL timing
- [ ] Performance test: corpse with 20 items, 4 events per item = 80 evaluations reduced to 20

#### Risk: MEDIUM-HIGH

---

## 4. Implementation Order and Dependencies

```
Fix 1 (Graphic Index)     Fix 2 (Spatial Tracking)
        |                          |
        |  (independent, can       |
        |   be done in parallel)   |
        v                          v
    Both provide foundation for Fix 3

Fix 3 (Match Cache)
    (benefits most when Fix 1 makes cache misses cheaper)
```

**Recommended order: Fix 1 -> Fix 2 -> Fix 3**

| Order | Fix | Time estimate | Files modified |
|-------|-----|--------------|----------------|
| 1st | Graphic Index | Small | `AutoLootManager.cs`, `AutoLootTabContent.cs` |
| 2nd | Spatial Tracking | Small-Medium | `AutoLootManager.cs` |
| 3rd | Match Cache | Medium | `AutoLootManager.cs`, `AutoLootTabContent.cs` |

**After each fix:** Measure performance. Fix 3 may not be needed if Fix 1 + Fix 2 are sufficient.

---

## 5. Files That Need Changes

### Primary files (will be modified)

| File | Path | What changes |
|------|------|-------------|
| AutoLootManager.cs | `src/ClassicUO.Client/Game/Managers/AutoLootManager.cs` | All 3 fixes: new data structures, new `IsOnLootList` logic, new `OnPositionChanged` logic, index rebuild methods, cache management |
| AutoLootTabContent.cs | `src/ClassicUO.Client/Game/UI/ImGuiControls/Agents/AutoLootTabContent.cs` | Notify manager when entry properties are edited (graphic, hue, regex) |

### Files that will NOT change but are important to understand

| File | Path | Why it matters |
|------|------|---------------|
| ObjectPropertiesListManager.cs | `src/ClassicUO.Client/Game/Managers/ObjectPropertiesListManager.cs` | Provides OPL data. `TryGetNameAndData()` is called by regex matching. Understanding its timing is critical for Fix 3. |
| GridHighLightData.cs | `src/ClassicUO.Client/Game/UI/Gumps/GridHighLight/GridHighLightData.cs` | `LootOnMatch` sets `item.ShouldAutoLoot` which bypasses `IsOnLootList()`. Not modified but must not be broken. |
| NearbyLootGump.cs | `src/ClassicUO.Client/Game/UI/NearbyLootGump.cs` | Calls `AddAutoLootEntry()` on Shift+Click. Will automatically benefit from Fix 1 rebuild trigger. |
| GridContainer.cs | `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs` | Same as NearbyLootGump — calls `AddAutoLootEntry()`. |
| RegexHelper.cs | `src/ClassicUO.Utility/RegexHelper.cs` | Caches compiled regex. Already optimized. No changes needed. |
| EventSink.cs | `src/ClassicUO.Client/Game/Managers/EventSink.cs` | Event definitions. No changes needed. |

---

## 6. Testing Strategy

### Manual testing scenarios

**Fix 1 (Graphic Index):**
1. Create a loot list with 100+ entries using different graphics
2. Add a wildcard entry (Graphic = -1) with a regex
3. Open a corpse containing items that match graphic-specific entries -> should loot correctly
4. Open a corpse containing items that match the wildcard entry -> should loot correctly
5. Edit an entry's graphic in the UI -> should still match correctly
6. Delete an entry -> should stop matching that entry
7. Import entries from clipboard -> should match correctly

**Fix 2 (Spatial Tracking):**
1. Enable scavenger mode
2. Drop items on the ground near player -> should be picked up
3. Walk toward items on the ground -> should be picked up within 3 tiles
4. Walk away from items -> should NOT pick them up
5. Have another player drop items near you while you're standing still -> should be picked up
6. Disable scavenger, drop items, re-enable scavenger -> should pick up nearby items

**Fix 3 (Match Cache):**
1. Open a corpse with regex-matching entries -> all matching items should be looted
2. Open a corpse, close it before OPL loads, wait, reopen -> items should still be looted after OPL arrives
3. Add a new loot entry while near a corpse -> previously-skipped items in open corpses may not retroactively match (acceptable limitation — close and reopen the corpse)
4. Rapid corpse opening (5 corpses in quick succession) -> all items should be matched correctly despite cache

### Regression checks
- Existing loot behavior must not change for any list size
- `AutoLoot.json` must remain compatible (load old configs, save new configs, cross-character import)
- Grid Highlight "Loot on Match" must continue working
- Scavenger mode must continue working
- Progress bar must continue working
- Grab bag destination must continue working
- Per-entry destination container must continue working

---

## 7. Rollback Plan

All three fixes are in-memory only. No config file changes. If a fix causes issues:
1. Revert the code change
2. Rebuild
3. User's `AutoLoot.json` is untouched — no data loss

No migration needed. No backwards compatibility concerns.

---

## 8. Future Considerations (Out of Scope)

These are NOT part of this PRD but could be addressed later:

- **Grid Highlight LINQ optimization:** `GridHighLightData.cs` uses LINQ in hot paths (lines 420-622). Replacing with manual loops would reduce garbage collection pressure. This is a separate optimization with its own scope.
- **String allocation in RegexCheck:** `search += name + data` (line 610) allocates a new string every time. Could use `string.Concat()` or `StringBuilder` for a minor improvement. Low priority since Fix 1 reduces how often this runs.
- **AutoLootConfigEntry.Equals improvement:** Current `Equals` (line 617) uses `==` for `RegexSearch` string comparison. This is fine for correctness but could use `StringComparer.Ordinal` for consistency.
- **Observable loot list:** Replace `List<AutoLootConfigEntry>` with a custom collection that fires events on mutation, eliminating the need to manually call rebuild/invalidation at each mutation point. Higher architectural change, save for a larger refactor.
