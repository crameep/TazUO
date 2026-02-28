# Auto-Loot Performance Analysis

## The Problem

The auto-loot system gets slow when you have a large loot list (200+ entries). The bigger the list, the worse the lag. At around 500+ entries you'll notice visible stuttering, and at 1000+ it becomes unusable.

## How Auto-Loot Works (Simple Version)

Think of auto-loot like a bouncer at a club with a guest list.

Every time something happens in the game — a corpse opens, an item appears, you take a step, or an item's tooltip loads — the system says "hey, check if this item should be looted." The bouncer then takes out the guest list and reads it **top to bottom**, checking every single entry: "Does this item match entry 1? No. Entry 2? No. Entry 3? No..." all the way to the end.

If you have 10 entries, that's fast. If you have 500 entries, that's 500 checks. And this happens for **every single item** in a corpse, and **every item on the ground** when you walk (scavenger mode), and sometimes the **same item gets checked multiple times** because different game events trigger the same check.

## The Five Bottlenecks We Found

### 1. The Guest List Problem (Linear Scan)

**File:** `AutoLootManager.cs`, lines 113-122

**What happens:** Every time an item needs checking, the system reads through your entire loot list one by one. Computer scientists call this O(n) — "the time it takes grows directly with the size of your list."

**Analogy:** Imagine looking up a name in a phone book by reading every page from the start, instead of flipping to the right letter. With 10 names it's fine. With 500, it's painful.

### 2. The Repeated Paperwork Problem (String Building + Regex)

**File:** `AutoLootManager.cs`, lines 606-615

**What happens:** For every entry that uses a text search (regex), the system builds a fresh text string by gluing the item's name and properties together, then runs the search pattern against it. Building that string creates temporary garbage that the system has to clean up later.

**Analogy:** Every time the bouncer checks a name, they photocopy the guest list entry and the person's ID, staple them together, read it, then throw it in the trash. Multiply by hundreds of items and entries.

### 3. The Walking Scan Problem (Scavenger World Iteration)

**File:** `AutoLootManager.cs`, lines 225-236

**What happens:** When scavenger mode is on, every single step your character takes triggers a scan of **every item in the entire game world** — not just nearby ones. Out of potentially thousands of items, only a handful are actually near you on the ground, but the system checks them all just to find those few.

**Analogy:** You're looking for loose change on the sidewalk, but instead of looking at your feet, you check every room in every building in the city, then ignore everything that isn't on the sidewalk near you.

### 4. The Grid Highlight LINQ Problem

**File:** `GridHighLightData.cs`, lines 420-622

**What happens:** The grid highlight system (which can also trigger auto-looting via "Loot on Match") uses a programming pattern called LINQ that creates temporary lists and dictionaries every time it checks an item. In hot code paths that run hundreds of times per second, this creates a lot of garbage for the system to clean up.

**Analogy:** Every time you check if a shirt matches your outfit, you write out your entire wardrobe on index cards, sort them into piles, compare the piles, then throw all the cards away. Every. Single. Time.

### 5. The Multiple-Check Problem (Redundant Event Triggers)

**File:** `AutoLootManager.cs`, lines 204-211

**What happens:** Four different game events can trigger a loot check on the same item:
- Item tooltip received (OPL)
- Item created
- Item updated
- Container opened

When a corpse opens, several of these fire for the same items, causing the same item to be checked against your entire loot list 2-3 times. The system already prevents items from being *looted* twice, but it doesn't prevent the expensive *checking* from happening twice.

**Analogy:** Three different people at the door all independently check the guest list for the same person, reach the same conclusion, and only the first one's answer matters.

---

## Three Proposed Fixes

### Fix 1: Sort the Guest List by Category (Graphic Index)

**What we'd change:** Instead of one big flat list, organize loot entries into buckets by item graphic ID (the item's appearance/type). When checking an item, only look at entries that could possibly match that item's type.

**Speed improvement:** Instead of checking all 500 entries, you'd only check the 2-3 entries that share the same graphic ID. Massive speedup for graphic-specific entries.

**What could go wrong:**

- **Wildcard entries (Graphic = -1):** Some entries are set to "match any graphic" — these have to be checked against every item regardless. They can't be bucketed. If you have many wildcard entries, the benefit shrinks.

- **Keeping the index in sync:** Six places in the code add, remove, or modify loot entries (the UI buttons for add/delete/import, plus editing an entry's graphic/hue/regex). Every one of those needs to rebuild the index. Since users only click these buttons occasionally, rebuilding is cheap — but if we miss one, the index goes stale and items get skipped or wrongly matched.

- **Public list access:** The loot list is exposed publicly (`AutoLootList` property), so external code *could* modify it directly and bypass the index rebuild. In practice only JSON loading does this, but it's a gotcha.

- **No save file changes:** The index is only in memory. Your `AutoLoot.json` file stays exactly the same. No risk of breaking existing configs.

- **Grid Highlight not affected:** The grid highlight "Loot on Match" feature bypasses the loot list entirely — it calls the loot function directly. So this change doesn't touch it.

**Risk: Low**

---

### Fix 2: Remember What You Already Checked (Match Cache)

**What we'd change:** Keep a dictionary that remembers "I already checked item #12345 and it didn't match" or "I already checked item #12345 and it matched entry X." When the same item is triggered by a second or third event, skip the expensive re-check.

**Speed improvement:** Eliminates all redundant checking from the multiple-event problem. Each item only gets fully evaluated once.

**What could go wrong:**

- **THE BIG ONE — Tooltip timing:** This is the most dangerous issue. When a corpse opens, the game creates items first, THEN loads their tooltips a moment later. If we check an item before its tooltip arrives, the regex search will fail (it only sees a generic name like "a longsword" instead of "a longsword, Damage Increase 50%"). If we cache that "no match" result, the item is **permanently skipped** — even when the tooltip arrives later with the full property text. This would cause the system to silently miss items it should be looting.

  **How to handle it:** Only cache positive matches ("yes, loot this"). Never cache "no match" results. OR: clear the cache entry for an item whenever its tooltip arrives. Both work, but both reduce the effectiveness of the cache.

- **Loot list changes:** When you add a new entry to your loot list, every "no match" in the cache could now be wrong. The entire cache must be cleared whenever the loot list changes. This is fine since list changes are rare.

- **Memory growth:** The cache grows with every item you encounter. Need to clear it periodically (every 5-10 seconds) or cap its size, similar to how `_recentlyLooted` already clears every 5 seconds.

- **Thread safety:** The loot list loads in a background thread. If the cache is accessed from both the background loader and the main game thread, it could corrupt. Need to use a thread-safe dictionary or ensure all access is on one thread.

**Risk: Medium-High** (the tooltip timing issue is subtle and easy to get wrong)

---

### Fix 3: Stop Searching the Whole World (Spatial Tracking)

**What we'd change:** Instead of scanning every item in the game world when you take a step, maintain a small list of "items that are on the ground near me." Only check those.

**Speed improvement:** Instead of iterating 5,000-10,000 world items per step, iterate maybe 10-50 nearby ground items. Eliminates the biggest source of per-step lag in scavenger mode.

**What could go wrong:**

- **Items appearing while standing still:** If someone drops an item near you but you don't move, the position-change event never fires. BUT this is already handled — the `OnItemCreated` event separately checks new items for scavenger pickup. So standing still is fine.

- **Walking toward existing items:** The position-change handler's purpose is to catch items that were *already* on the ground as you walk toward them. The nearby-items set needs to update as you move — items that were far away are now close. You'd either recalculate the nearby set on each step (still cheaper than scanning everything) or keep a wider radius and filter at check time.

- **Items picked up by others:** If another player picks up an item that's in your nearby set, the item leaves the world but your set still has its ID. You need to handle item removal events, or just do a "does this item still exist?" check when iterating (cheap).

- **Toggling scavenger mode:** If the user turns scavenger on, the nearby set might be empty or stale. Clear and rebuild it when the setting changes.

- **World changes / logging in:** The set needs to be cleared when leaving the game world (`OnSceneUnload`), which already handles event cleanup.

- **No save file changes:** Purely in-memory tracking. No config changes.

**Risk: Low-Medium** (worst case: occasionally miss a ground pickup, never causes crashes or wrong looting)

---

## Recommended Implementation Order

| Order | Fix | Why This Order |
|-------|-----|----------------|
| 1st | Graphic Index | Lowest risk, easiest to implement, immediate benefit for most users |
| 2nd | Spatial Tracking | Medium complexity, big payoff for scavenger users, low risk |
| 3rd | Match Cache | Highest payoff but most dangerous — the tooltip timing issue needs careful handling |

---

## Key Files

| File | What It Does |
|------|-------------|
| `src/ClassicUO.Client/Game/Managers/AutoLootManager.cs` | The core auto-loot system. All the bottlenecks and fixes live here. |
| `src/ClassicUO.Client/Game/Managers/ObjectPropertiesListManager.cs` | Stores item tooltips (OPL data). The auto-loot system reads from this. |
| `src/ClassicUO.Client/Game/UI/Gumps/GridHighLight/GridHighLightData.cs` | Grid highlight system. Can trigger auto-looting via "Loot on Match." |
| `src/ClassicUO.Client/Game/UI/ImGuiControls/Agents/AutoLootTabContent.cs` | The UI for managing your loot list. All user-facing add/remove/edit operations. |
| `src/ClassicUO.Client/Game/UI/NearbyLootGump.cs` | Nearby loot window. Shift-click adds to auto-loot list. |
| `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs` | Grid container. Shift-click adds to auto-loot list. |
| `src/ClassicUO.Utility/RegexHelper.cs` | Caches compiled regex patterns. Already optimized. |
| `src/ClassicUO.Client/Game/Managers/EventSink.cs` | Central event hub. Routes game events to auto-loot handlers. |

## Loot List Mutation Points (Where the List Changes)

These are the 6 places in code where the loot list gets modified. Any index or cache built on top of the list must be updated at ALL of these points:

1. **AddAutoLootEntry()** — `AutoLootManager.cs` line 139 — Adding a new entry
2. **TryRemoveAutoLootEntry()** — `AutoLootManager.cs` line 176 — Removing an entry
3. **ImportEntries()** — `AutoLootManager.cs` line 480 — Bulk import (file, clipboard, other character)
4. **UI graphic edit** — `AutoLootTabContent.cs` line 260 — User changes an entry's graphic ID
5. **UI hue edit** — `AutoLootTabContent.cs` lines 277, 281 — User changes an entry's color
6. **UI regex edit** — `AutoLootTabContent.cs` lines 200, 307 — User changes an entry's text search

---

## Status

- Analysis: Complete
- Implementation: Not started
- Next step: Implement Fix 1 (Graphic Index) when ready to resume
