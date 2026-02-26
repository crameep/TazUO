# Grid Container Tabs Design

## Problem

Opening bags inside bags creates a clutter of overlapping GridContainer windows. Players with organized backpacks (reagent bags, gem bags, scroll pouches) end up managing many separate windows.

## Solution

Sub-containers open as tabs within their parent GridContainer instead of spawning new windows.

## Approach: Tab Bar + SlotManager Swap

Each tab is backed by its own `GridSlotManager` instance. Switching tabs swaps which SlotManager's items are displayed in the shared `GridScrollArea`. This reuses the existing per-container management without changing GridItem, GridScrollArea, or the rendering pipeline.

## Layout

```
┌─────────────────────────────────────────┐
│  Bag Name          [Sort] [Grid] [Drop] │  20px label
│  [Search...                        ] [X]│  20px search
│  [Main ▾] [Regs ✕] [Gems ✕] [Scrolls ✕]│  25px tab bar (NEW)
├─────────────────────────────────────────┤
│  ┌───┐ ┌───┐ ┌───┐ ┌───┐ ┌───┐        │
│  │   │ │   │ │   │ │   │ │   │        │  scroll area
│  └───┘ └───┘ └───┘ └───┘ └───┘        │
└─────────────────────────────────────────┘
```

- Tab bar sits between search box and scroll area (Y=44, height=25px)
- Hidden when only one tab exists (no wasted space)
- `_scrollArea.Y` shifts down by tab bar height; `_scrollArea.Height` shrinks accordingly
- Tab bar uses `NiceButton` controls (same pattern as `ResizableJournal`)
- First tab = main container, always present, not closeable
- Sub-container tabs have a close [X] button
- Right-click a tab: context menu with "Rename" and "Close"
- Horizontal overflow: truncate long names, scroll if too many tabs

## Tab Data Structure

```csharp
class ContainerTab
{
    uint ContainerSerial      // sub-bag this tab represents
    string CustomName         // user rename (null = use OPL name)
    GridSlotManager Slots     // owns grid items for this container
    int ScrollPosition        // preserved when switching away
}
```

## Tab Switching

1. Save current tab's scroll position
2. Remove current SlotManager's GridItems from `_scrollArea`
3. Set active tab index
4. Add new tab's SlotManager's GridItems to `_scrollArea`
5. Restore that tab's scroll position
6. Update header (capacity bar, sort indicator) for active tab's container

## Opening Sub-Bags

- Double-click a sub-bag: check if tab already exists for that serial (switch to it), otherwise send `GameActions.DoubleClick` to server
- `OpenContainer` packet handler intercept: walk up `item.Container` chain to find nearest ancestor with an open GridContainer, add tab there instead of creating a new window
- Shift+double-click: bypass tab logic, open as separate window (static `_forceNewWindow` flag checked and cleared in packet handler)
- If no ancestor GridContainer is open, create a new window as before

## Flat Tabs at Any Depth

All sub-containers regardless of nesting depth become sibling tabs on the nearest ancestor GridContainer. Bag C inside Bag B inside Backpack = three flat tabs on the Backpack window. No nested tab bars.

Edge case: If Bag B is Shift+opened as its own window, Bag C inside it becomes a tab on Bag B's window (nearest open ancestor).

## Closing Tabs

- Close [X] button removes the tab, disposes its SlotManager and GridItems
- Switches to the previous tab or main tab
- Does NOT close the container server-side
- Closing the main GridContainer window disposes all tabs

## Auto-Open Modes

Profile setting `GridContainerTabAutoOpen` (int):
- `0` Manual: tabs only appear when user double-clicks a sub-bag
- `1` DirectChildren: auto-create tabs for immediate sub-containers on parent open
- `2` AllNested: recursively find all containers at any depth, flat-tab them all

## Settings

### Profile.cs (new)

- `GridContainerTabsEnabled` (bool, default `true`) — master toggle
- `GridContainerTabAutoOpen` (int, default `0`) — auto-open mode

### GridContainerSaveData.cs (additions)

```csharp
// Added to GridContainerEntry:
[JsonPropertyName("tb")]  public List<GridContainerTabEntry> Tabs { get; set; }
[JsonPropertyName("tbi")] public int CurrentTabIndex { get; set; }

// New class:
public class GridContainerTabEntry
{
    [JsonPropertyName("s")]  public uint ContainerSerial { get; set; }
    [JsonPropertyName("cn")] public string CustomName { get; set; }
    [JsonPropertyName("ls")] public Dictionary<uint, GridContainerSlotEntry> Slots { get; set; }
    [JsonPropertyName("sm")] public int SortMode { get; set; } = -1; // -1 = inherit parent
}
```

## Persistence

What persists across sessions:
- Open tab list and order
- Custom tab names
- Per-tab locked item positions
- Per-tab sort mode overrides (-1 = inherit parent)
- Last active tab index

What doesn't persist:
- Scroll position (resets to top)
- Tabs for containers that no longer exist (pruned on load)

## Inherit-but-Overridable Settings

Tabs default `SortMode = -1` meaning "use parent's sort mode." If the user changes sort on a specific tab, the override is stored. The sort dropdown in the header always reflects the active tab's effective sort mode.

## Files to Modify

1. **GridContainer.cs** — tab bar control, ContainerTab list, tab switching, SlotManager swap, UpdateUiPositions for tab bar height
2. **GridContainerSaveData.cs** — GridContainerTabEntry class, additions to GridContainerEntry, serializer context
3. **Profile.cs** — new settings
4. **OpenContainer.cs** — packet handler intercept to route sub-containers to parent tabs
5. **ModernOptionsGump.cs** — UI for new profile settings
