# PRD: Auto-Loot Priority Tiers

## Problem
The auto-loot system processes items in FIFO order — whatever the server sends first gets looted first. With large loot lists (500+ entries), valuable items (rares, gold, reagents) can get stuck behind low-value junk. If a player walks away or a corpse decays before the queue drains, the important items are lost.

## Goal
Add tier-based priority (High / Normal / Low) to auto-loot entries so high-value items are always grabbed first, without degrading the performance optimizations already in place.

## User Experience

### Priority Tiers
- **High** — looted first (rares, gold, reagents)
- **Normal** — default for all existing and new entries
- **Low** — looted last (bulk filler, common drops)

### UI Changes
- New "Priority" dropdown column in the auto-loot entry table (AutoLootTabContent)
- Dropdown options: High, Normal, Low
- Defaults to Normal for new entries and all existing configs (backward-compatible)

### Matching Behavior
When multiple loot entries match the same item, the highest-priority entry wins. This determines which destination container the item goes to.

## Technical Design

### Data Model
Add enum and property to `AutoLootConfigEntry`:
```csharp
public enum AutoLootPriority { Low = 0, Normal = 1, High = 2 }
// New property on AutoLootConfigEntry:
public AutoLootPriority Priority { get; set; } = AutoLootPriority.Normal;
```

### Queue Change
Replace `Queue<(uint, AutoLootConfigEntry)>` (FIFO) with `PriorityQueue<(uint, AutoLootConfigEntry), int>` (min-heap). Negate enum value as sort key so High dequeues first.

**File:** `src/ClassicUO.Client/Game/Managers/AutoLootManager.cs`
- Line 43: Replace queue declaration
- Line 100 (`LootItem`): Enqueue with priority key `-(int)entry.Priority`
- Line 440 (`Update`): `PriorityQueue.Dequeue()` returns element directly, syntax unchanged

### Best-Match in IsOnLootList()
Change `IsOnLootList()` (lines 177-191) to iterate all matching entries in the graphic bucket and wildcard list, returning the highest-priority match instead of breaking on the first match.

**File:** `src/ClassicUO.Client/Game/Managers/AutoLootManager.cs`

### UI Column
Add Priority combo box column to the entry table.

**File:** `src/ClassicUO.Client/Game/UI/ImGuiControls/Agents/AutoLootTabContent.cs`

### What Does NOT Change
- Graphic index — unaffected (priority doesn't change bucketing)
- Spatial tracking (`_nearbyGroundItems`) — unaffected
- Match cache structure — still one entry per serial, stores best match
- `_quickContainsLookup` / `_recentlyLooted` — unaffected
- Thread safety model — unaffected
- JSON serialization — source generator picks up new property automatically
- Existing config files — backward-compatible, missing field defaults to Normal

## Key Files
| File | Change |
|------|--------|
| `src/ClassicUO.Client/Game/Managers/AutoLootManager.cs` | Enum, property, queue swap, best-match logic |
| `src/ClassicUO.Client/Game/UI/ImGuiControls/Agents/AutoLootTabContent.cs` | Priority dropdown column |
| `tests/ClassicUO.UnitTests/Game/Managers/AutoLootManagerTest.cs` | New priority tests |

## Testing
1. **Build:** `dotnet build -c Debug` compiles cleanly
2. **Unit tests:** `dotnet test tests/ClassicUO.UnitTests/` — existing tests pass
3. **New unit tests:**
   - PriorityQueue ordering: High items dequeue before Normal before Low
   - Best-match: highest-priority entry returned when multiple entries match same item
   - Backward compat: entries without Priority field default to Normal
4. **In-game:** Create two entries for same graphic — High with container A, Low with container B. Kill mob, confirm High entry's container is used and item looted first.

## Risks
- **PriorityQueue is not stable** — items with equal priority may not preserve insertion order. This is acceptable since within the same tier, order doesn't matter meaningfully.
- **Best-match iterates full bucket** instead of short-circuiting — graphic buckets are typically small (1-5 entries per graphic), so the cost is negligible.
