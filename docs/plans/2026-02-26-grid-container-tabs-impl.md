# Grid Container Tabs Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Sub-containers open as tabs within their parent GridContainer instead of spawning new windows.

**Architecture:** Each tab is backed by its own `GridSlotManager`. Switching tabs swaps which SlotManager's GridItems live in the shared `GridScrollArea`. A tab bar of `NiceButton` controls sits between the search box and scroll area, hidden when only one tab exists. The `OpenContainer` packet handler walks up the container chain to find a parent GridContainer and adds a tab there instead of creating a new window.

**Tech Stack:** C# / .NET 10, FNA (XNA), existing GridContainer/GridSlotManager/NiceButton controls

**Design doc:** `docs/plans/2026-02-26-grid-container-tabs-design.md`

---

## Task 1: Profile Settings & Save Data Model

Add the new profile settings and save data classes that the rest of the feature depends on. No behavioral changes yet.

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs:398-415` (grid container region)
- Modify: `src/ClassicUO.Client/Game/Managers/GridContainerSaveData.cs:286-436` (entry classes + serializer context)

**Step 1: Add profile settings**

In `Profile.cs`, inside the `#region GRID CONTAINER` block (after line ~415, before `#endregion`), add:

```csharp
public bool GridContainerTabsEnabled { get; set; } = true;
public int GridContainerTabAutoOpen { get; set; } = 0; // 0=Manual, 1=DirectChildren, 2=AllNested
```

**Step 2: Add GridContainerTabEntry class**

In `GridContainerSaveData.cs`, after the `GridContainerSlotEntry` class (after line ~422), add:

```csharp
public class GridContainerTabEntry
{
    [JsonPropertyName("s")]
    public uint ContainerSerial { get; set; }

    [JsonPropertyName("cn")]
    public string CustomName { get; set; }

    [JsonPropertyName("ls")]
    public Dictionary<uint, GridContainerSlotEntry> Slots { get; set; } = new();

    [JsonPropertyName("sm")]
    public int SortMode { get; set; } = -1; // -1 = inherit parent
}
```

**Step 3: Add tab fields to GridContainerEntry**

In the `GridContainerEntry` class, add these properties (after the `Slots` property):

```csharp
[JsonPropertyName("tb")]
public List<GridContainerTabEntry> Tabs { get; set; } = new();

[JsonPropertyName("tbi")]
public int CurrentTabIndex { get; set; }
```

**Step 4: Update the serializer context**

In `GridContainerSerializerContext` (line ~424), add attributes for the new types:

```csharp
[JsonSerializable(typeof(GridContainerTabEntry))]
[JsonSerializable(typeof(GridContainerTabEntry[]))]
[JsonSerializable(typeof(List<GridContainerTabEntry>))]
```

**Step 5: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors

**Step 6: Commit**

```
feat: add profile settings and save data model for grid container tabs
```

---

## Task 2: ContainerTab Class & Tab List in GridContainer

Add the runtime `ContainerTab` class and the tab list to `GridContainer`. Wire up the existing single SlotManager as "tab 0". No UI changes yet — the tab bar isn't drawn.

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs`

**Step 1: Add ContainerTab class**

Inside `GridContainer` class (after the `GridSlotManager` property declaration at line ~141), add:

```csharp
private class ContainerTab
{
    public uint ContainerSerial;
    public string CustomName;
    public GridSlotManager SlotManager;
    public int ScrollPosition;
    public GridSortMode SortMode;
    public bool SortModeOverridden;
    public NiceButton TabButton;
}

private readonly List<ContainerTab> _tabs = new();
private int _activeTabIndex;
```

**Step 2: Wrap existing SlotManager as tab 0**

In the constructor, after `SlotManager = new GridSlotManager(...)` (line ~481), add:

```csharp
_tabs.Add(new ContainerTab
{
    ContainerSerial = LocalSerial,
    SlotManager = SlotManager,
    SortMode = _sortMode,
    SortModeOverridden = false
});
_activeTabIndex = 0;
```

**Step 3: Add ActiveTab helper property**

Near the `ContainerTab` class:

```csharp
private ContainerTab ActiveTab => _tabs.Count > 0 ? _tabs[_activeTabIndex] : null;
```

**Step 4: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors

**Step 5: Commit**

```
feat: add ContainerTab class and wrap existing SlotManager as tab 0
```

---

## Task 3: Tab Bar UI Control

Add the visual tab bar — a row of `NiceButton` controls between the search box and scroll area. The tab bar is hidden when there's only one tab (main container). Adjust `UpdateUiPositions` to account for tab bar height.

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs`

**Step 1: Add tab bar constant and field**

With the other constants (line ~57):

```csharp
private const int TAB_BAR_HEIGHT = 25;
```

Add field (near other UI fields, line ~91):

```csharp
private readonly List<NiceButton> _tabButtons = new();
```

**Step 2: Add TabBarVisible property**

```csharp
private bool TabBarVisible => _tabs.Count > 1 && ProfileManager.CurrentProfile.GridContainerTabsEnabled;
private int EffectiveTabBarHeight => TabBarVisible ? TAB_BAR_HEIGHT : 0;
```

**Step 3: Update UpdateUiPositions**

In `UpdateUiPositions()` (line ~1122), change the scroll area Y and height calculations:

Replace:
```csharp
_scrollArea.Y = LABEL_HEIGHT + TOP_BAR_HEIGHT + _background.Y;
```
With:
```csharp
_scrollArea.Y = LABEL_HEIGHT + TOP_BAR_HEIGHT + EffectiveTabBarHeight + _background.Y;
```

Replace:
```csharp
_scrollArea.Height = adjustedHeight - LABEL_HEIGHT - TOP_BAR_HEIGHT;
```
With:
```csharp
_scrollArea.Height = adjustedHeight - LABEL_HEIGHT - TOP_BAR_HEIGHT - EffectiveTabBarHeight;
```

**Step 4: Add BuildTabBar and UpdateTabButtons methods**

```csharp
private void BuildTabBar()
{
    foreach (NiceButton btn in _tabButtons)
        btn.Dispose();
    _tabButtons.Clear();

    for (int i = 0; i < _tabs.Count; i++)
    {
        ContainerTab tab = _tabs[i];
        string label = GetTabLabel(tab);
        if (i > 0)
            label += " \u00D7"; // × close indicator

        int tabIndex = i; // capture for closure
        var btn = new NiceButton(0, 0, 80, TAB_BAR_HEIGHT, ButtonAction.Activate, label, 99)
        {
            ButtonParameter = tabIndex,
            IsSelectable = true,
            IsSelected = (i == _activeTabIndex),
            CanCloseWithRightClick = false
        };

        btn.MouseUp += (sender, e) =>
        {
            if (e.Button == MouseButtonType.Left)
            {
                // Check if click is on the × close area (last ~16px) for non-main tabs
                if (tabIndex > 0 && btn.MouseIsOver)
                {
                    int localX = Mouse.Position.X - btn.ScreenCoordinateX;
                    if (localX >= btn.Width - 16)
                    {
                        CloseTab(tabIndex);
                        return;
                    }
                }
                SwitchToTab(tabIndex);
            }
            else if (e.Button == MouseButtonType.Right)
            {
                ShowTabContextMenu(tabIndex);
            }
        };

        tab.TabButton = btn;
        _tabButtons.Add(btn);
        Add(btn);
    }

    PositionTabButtons();
    UpdateUiPositions();
}

private void PositionTabButtons()
{
    int xOffset = _borderWidth;
    int tabY = _borderWidth + LABEL_HEIGHT + TOP_BAR_HEIGHT;

    for (int i = 0; i < _tabButtons.Count; i++)
    {
        _tabButtons[i].X = xOffset;
        _tabButtons[i].Y = tabY;
        _tabButtons[i].IsVisible = TabBarVisible;
        xOffset += _tabButtons[i].Width + 2;
    }
}

private string GetTabLabel(ContainerTab tab)
{
    if (!string.IsNullOrEmpty(tab.CustomName))
        return tab.CustomName;

    Item item = World.Items.Get(tab.ContainerSerial);
    if (item != null && _world.OPL.TryGetNameAndData(item.Serial, out string name, out _))
        return TruncateLabel(name, 10);

    return tab.ContainerSerial == LocalSerial ? "Main" : "Bag";
}

private static string TruncateLabel(string text, int maxLen)
{
    if (string.IsNullOrEmpty(text)) return "Bag";
    // Strip HTML tags
    text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
    return text.Length <= maxLen ? text : text[..maxLen] + "..";
}
```

**Step 5: Call BuildTabBar after SlotManager init**

In the constructor, after the tab 0 setup from Task 2:

```csharp
BuildTabBar();
```

**Step 6: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors. Tab bar won't be visible yet because there's only 1 tab.

**Step 7: Commit**

```
feat: add tab bar UI control with layout positioning
```

---

## Task 4: Tab Switching

Implement `SwitchToTab` — the core mechanic that saves the current tab's state, detaches its GridItems from the scroll area, attaches the new tab's GridItems, and restores its state.

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs`

**Step 1: Implement SwitchToTab**

```csharp
private void SwitchToTab(int tabIndex)
{
    if (tabIndex < 0 || tabIndex >= _tabs.Count || tabIndex == _activeTabIndex)
        return;

    // Save current tab state
    ContainerTab currentTab = _tabs[_activeTabIndex];
    currentTab.ScrollPosition = _scrollArea.ScrollValue;

    // Detach current tab's grid items from scroll area
    foreach (GridItem gi in currentTab.SlotManager.GridSlots.Values)
        _scrollArea.Remove(gi);

    // Switch
    _activeTabIndex = tabIndex;
    ContainerTab newTab = _tabs[tabIndex];

    // Update active SlotManager reference
    SlotManager = newTab.SlotManager;

    // Update sort mode
    if (newTab.SortModeOverridden)
        _sortMode = newTab.SortMode;
    else
        _sortMode = _tabs[0].SortMode;

    // Attach new tab's grid items to scroll area
    foreach (GridItem gi in newTab.SlotManager.GridSlots.Values)
        _scrollArea.Add(gi);

    // Restore scroll position
    _scrollArea.ScrollValue = newTab.ScrollPosition;

    // Update header for active container
    UpdateActiveTabHeader();

    // Update tab button selection
    for (int i = 0; i < _tabButtons.Count; i++)
        _tabButtons[i].IsSelected = (i == _activeTabIndex);

    // Trigger item rebuild for the new tab
    InvalidateContents = true;
}
```

**Step 2: Implement UpdateActiveTabHeader**

```csharp
private void UpdateActiveTabHeader()
{
    ContainerTab tab = ActiveTab;
    if (tab == null) return;

    Item container = World.Items.Get(tab.ContainerSerial);
    if (container == null) return;

    _containerNameLabel.Text = GetContainerName();
    _containerNameLabel.SetTooltip(GetContainerName(true, false));
}
```

**Step 3: Update UpdateItems to use active tab's container**

The existing `UpdateItems` method (line ~806) uses `Container` (which is always the root container). It needs to operate on the active tab's container when a tab is selected. Modify the item fetching to use the active tab's container:

In `UpdateItems`, replace the line:

```csharp
List<Item> sortedContents = (ProfileManager.CurrentProfile is null || ProfileManager.CurrentProfile.GridContainerSearchMode == 0) && !string.IsNullOrEmpty(_searchBox.Text)
    ? SlotManager.SearchResults(_searchBox.Text)
    : GridSlotManager.GetItemsInContainer(World, Container, _sortMode, overrideSort);
```

With:

```csharp
Item activeContainer = ActiveTab != null ? World.Items.Get(ActiveTab.ContainerSerial) : Container;
if (activeContainer == null)
    activeContainer = Container;

List<Item> sortedContents = (ProfileManager.CurrentProfile is null || ProfileManager.CurrentProfile.GridContainerSearchMode == 0) && !string.IsNullOrEmpty(_searchBox.Text)
    ? SlotManager.SearchResults(_searchBox.Text)
    : GridSlotManager.GetItemsInContainer(World, activeContainer, _sortMode, overrideSort);
```

**Step 4: Add `Remove` method to `GridScrollArea` if it doesn't exist**

Check if `GridScrollArea` (which extends `Control`) has a `Remove` method. The base `Control` class should have it. If not, we need:

```csharp
// In GridScrollArea class — only add if Control base doesn't have Remove
public void Remove(Control c)
{
    Children?.Remove(c);
}
```

**Step 5: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors

**Step 6: Commit**

```
feat: implement tab switching with SlotManager swap
```

---

## Task 5: Adding and Closing Tabs

Implement `AddTab` (creates a new tab for a sub-container) and `CloseTab` (removes a tab). Also add the tab context menu with Rename and Close.

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs`

**Step 1: Implement AddTab**

```csharp
public void AddTab(uint containerSerial)
{
    if (!ProfileManager.CurrentProfile.GridContainerTabsEnabled)
        return;

    // Check if tab already exists
    for (int i = 0; i < _tabs.Count; i++)
    {
        if (_tabs[i].ContainerSerial == containerSerial)
        {
            SwitchToTab(i);
            return;
        }
    }

    Item container = World.Items.Get(containerSerial);
    if (container == null)
        return;

    // Look up saved tab data
    GridContainerTabEntry savedTab = _gridContainerEntry.Tabs.Find(t => t.ContainerSerial == containerSerial);

    // Determine sort mode
    GridSortMode sortMode = _sortMode; // inherit parent
    bool sortOverridden = false;
    if (savedTab != null && savedTab.SortMode >= 0)
    {
        sortMode = (GridSortMode)savedTab.SortMode;
        sortOverridden = true;
    }

    // Create SlotManager for this sub-container
    // We need a temporary GridContainerEntry for the sub-container's slots
    var subEntry = GridContainerSaveData.Instance.GetContainer(containerSerial);

    // Restore saved slot data from tab entry if available
    if (savedTab?.Slots != null)
    {
        foreach (var kvp in savedTab.Slots)
            subEntry.Slots[kvp.Key] = kvp.Value;
    }

    var slotManager = new GridSlotManager(World, containerSerial, this, _scrollArea);

    var tab = new ContainerTab
    {
        ContainerSerial = containerSerial,
        CustomName = savedTab?.CustomName,
        SlotManager = slotManager,
        SortMode = sortMode,
        SortModeOverridden = sortOverridden
    };

    _tabs.Add(tab);

    // Detach new tab's grid items from scroll area (they were auto-added by SlotManager constructor)
    // They'll be re-attached when the tab is switched to
    foreach (GridItem gi in slotManager.GridSlots.Values)
        _scrollArea.Remove(gi);

    BuildTabBar();
    SwitchToTab(_tabs.Count - 1);
}
```

**Step 2: Implement CloseTab**

```csharp
private void CloseTab(int tabIndex)
{
    if (tabIndex <= 0 || tabIndex >= _tabs.Count)
        return; // Can't close main tab

    ContainerTab tab = _tabs[tabIndex];

    // Save tab slot data before closing
    SaveTabData(tab);

    // If closing the active tab, switch away first
    if (_activeTabIndex == tabIndex)
    {
        int switchTo = tabIndex > 0 ? tabIndex - 1 : 0;
        SwitchToTab(switchTo);
    }
    else if (_activeTabIndex > tabIndex)
    {
        _activeTabIndex--; // Adjust index since we're removing before it
    }

    // Dispose grid items
    foreach (GridItem gi in tab.SlotManager.GridSlots.Values)
    {
        _scrollArea.Remove(gi);
        gi.Dispose();
    }

    _tabs.RemoveAt(tabIndex);
    BuildTabBar();
}
```

**Step 3: Implement SaveTabData helper**

```csharp
private void SaveTabData(ContainerTab tab)
{
    if (tab.ContainerSerial == LocalSerial)
        return; // Main tab uses the main entry

    var tabEntry = _gridContainerEntry.Tabs.Find(t => t.ContainerSerial == tab.ContainerSerial);
    if (tabEntry == null)
    {
        tabEntry = new GridContainerTabEntry { ContainerSerial = tab.ContainerSerial };
        _gridContainerEntry.Tabs.Add(tabEntry);
    }

    tabEntry.CustomName = tab.CustomName;
    tabEntry.SortMode = tab.SortModeOverridden ? (int)tab.SortMode : -1;
    tabEntry.Slots = new Dictionary<uint, GridContainerSlotEntry>();

    foreach (var kvp in tab.SlotManager.ItemPositions)
    {
        uint serial = kvp.Value;
        tabEntry.Slots[serial] = new GridContainerSlotEntry
        {
            Serial = serial,
            Slot = kvp.Key,
            Locked = tab.SlotManager.GridSlots.TryGetValue(kvp.Key, out GridItem gi) && gi.ItemGridLocked
        };
    }
}
```

**Step 4: Implement ShowTabContextMenu**

```csharp
private void ShowTabContextMenu(int tabIndex)
{
    ContainerTab tab = _tabs[tabIndex];
    var menu = new ContextMenuControl(this);

    menu.Add("Rename", () =>
    {
        UIManager.Add(new InputRequest(World, "Enter tab name", "Save", "Cancel", (result, text) =>
        {
            if (result == InputRequest.Result.BUTTON1 && !string.IsNullOrEmpty(text))
            {
                tab.CustomName = text;
                BuildTabBar();
            }
        })
        { X = Mouse.Position.X, Y = Mouse.Position.Y });
    });

    if (tabIndex > 0) // Can't close main tab
    {
        menu.Add("Close", () => CloseTab(tabIndex));
    }

    menu.Show();
}
```

**Step 5: Update Dispose to save all tab data and clean up**

In the existing `Dispose` method (line ~869), before the `base.Dispose()` call, add:

```csharp
// Save tab data
if (!_skipSave && !_isCorpse)
{
    _gridContainerEntry.CurrentTabIndex = _activeTabIndex;
    _gridContainerEntry.Tabs.Clear();
    for (int i = 1; i < _tabs.Count; i++) // Skip main tab (index 0)
        SaveTabData(_tabs[i]);

    // Dispose sub-tab SlotManagers
    for (int i = 1; i < _tabs.Count; i++)
    {
        foreach (GridItem gi in _tabs[i].SlotManager.GridSlots.Values)
            gi.Dispose();
    }
}
```

**Step 6: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors

**Step 7: Commit**

```
feat: implement AddTab, CloseTab, tab context menu with rename
```

---

## Task 6: OpenContainer Packet Handler Intercept

Route sub-container opens to the parent GridContainer as tabs instead of creating new windows. Add Shift+double-click escape hatch.

**Files:**
- Modify: `src/ClassicUO.Client/Network/PacketHandlers/OpenContainer.cs:199-218`
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs` (GridItem double-click, static flag)

**Step 1: Add static force-new-window flag to GridContainer**

In `GridContainer.cs`, add a static field:

```csharp
internal static bool ForceNewWindow;
```

**Step 2: Modify GridItem double-click to set flag on Shift**

In `GridItem.OnMouseDoubleClick` (line ~1352), before the `GameActions.DoubleClick` call, add:

```csharp
if (Keyboard.Shift && _item != null && _item.IsContainer)
    GridContainer.ForceNewWindow = true;
```

**Step 3: Add FindParentGridContainer static helper**

In `GridContainer.cs`:

```csharp
internal static GridContainer FindParentGridContainer(World world, uint containerSerial)
{
    Item item = world.Items.Get(containerSerial);
    if (item == null)
        return null;

    // Walk up the container chain
    uint parentSerial = item.Container;
    while (parentSerial != 0 && parentSerial != 0xFFFFFFFF)
    {
        GridContainer gc = UIManager.GetGump<GridContainer>(parentSerial);
        if (gc != null)
            return gc;

        Item parent = world.Items.Get(parentSerial);
        if (parent == null)
            break;

        parentSerial = parent.Container;
    }

    return null;
}
```

**Step 4: Modify OpenContainer.cs packet handler**

In `OpenContainer.cs`, replace the grid container creation block (lines ~211-218):

```csharp
if (ProfileManager.CurrentProfile.UseGridLayoutContainerGumps && graphic != 0x091A)
{
    GridContainer gridContainer = UIManager.GetGump<GridContainer>(serial);
    if (gridContainer != null)
    {
        gridContainer.RequestUpdateContents();
    }
    else if (GridContainer.ForceNewWindow)
    {
        GridContainer.ForceNewWindow = false;
        UIManager.Add(new GridContainer(world, serial, graphic));
    }
    else
    {
        GridContainer parentGC = GridContainer.FindParentGridContainer(world, serial);
        if (parentGC != null && ProfileManager.CurrentProfile.GridContainerTabsEnabled)
        {
            parentGC.AddTab(serial);
        }
        else
        {
            UIManager.Add(new GridContainer(world, serial, graphic));
        }
    }
}
```

**Step 5: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors

**Step 6: Commit**

```
feat: route sub-container opens to parent GridContainer as tabs
```

---

## Task 7: RequestUpdateContents for Tabs

When the server sends item updates for a sub-container that's open as a tab, route the update to the correct tab's SlotManager.

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs`

**Step 1: Override RequestUpdateContents routing**

Add a public method to handle updates for any tab:

```csharp
public void RequestUpdateContentsForTab(uint containerSerial)
{
    for (int i = 0; i < _tabs.Count; i++)
    {
        if (_tabs[i].ContainerSerial == containerSerial)
        {
            if (i == _activeTabIndex)
            {
                InvalidateContents = true;
            }
            else
            {
                // Rebuild the inactive tab's contents in the background
                Item container = World.Items.Get(containerSerial);
                if (container != null)
                {
                    GridSortMode sort = _tabs[i].SortModeOverridden ? _tabs[i].SortMode : _tabs[0].SortMode;
                    List<Item> items = GridSlotManager.GetItemsInContainer(World, container, sort, true);
                    _tabs[i].SlotManager.RebuildContainer(items, "", true);
                }
            }
            return;
        }
    }
}
```

**Step 2: Update OpenContainer handler to route updates to tabs**

In `OpenContainer.cs`, the existing check `if (gridContainer != null) gridContainer.RequestUpdateContents()` only handles the root container. After that check, also look for tab containers. Update the handler from Task 6 — in the `gridContainer != null` branch:

```csharp
if (gridContainer != null)
{
    gridContainer.RequestUpdateContents();
}
else
{
    // Check if this container is open as a tab in a parent GridContainer
    GridContainer parentGC = GridContainer.FindParentGridContainer(world, serial);
    if (parentGC != null)
    {
        // Check if it's already a tab
        bool isTab = false;
        for (int i = 0; i < parentGC._tabs.Count; i++)  // Need to expose or use a method
        {
            if (parentGC._tabs[i].ContainerSerial == serial)
            {
                parentGC.RequestUpdateContentsForTab(serial);
                isTab = true;
                break;
            }
        }

        if (!isTab)
        {
            // Not yet a tab — add or create new window
            if (GridContainer.ForceNewWindow)
            {
                GridContainer.ForceNewWindow = false;
                UIManager.Add(new GridContainer(world, serial, graphic));
            }
            else if (ProfileManager.CurrentProfile.GridContainerTabsEnabled)
            {
                parentGC.AddTab(serial);
            }
            else
            {
                UIManager.Add(new GridContainer(world, serial, graphic));
            }
        }
    }
    else if (GridContainer.ForceNewWindow)
    {
        GridContainer.ForceNewWindow = false;
        UIManager.Add(new GridContainer(world, serial, graphic));
    }
    else
    {
        UIManager.Add(new GridContainer(world, serial, graphic));
    }
}
```

**Step 3: Add HasTab method to GridContainer (cleaner than exposing _tabs)**

```csharp
internal bool HasTab(uint containerSerial)
{
    for (int i = 0; i < _tabs.Count; i++)
        if (_tabs[i].ContainerSerial == containerSerial)
            return true;
    return false;
}
```

Refactor the OpenContainer code to use `parentGC.HasTab(serial)` instead of iterating `_tabs` directly.

**Step 4: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors

**Step 5: Commit**

```
feat: route item updates to correct tab SlotManager
```

---

## Task 8: Auto-Open Tabs

Implement the auto-open modes (DirectChildren and AllNested) that automatically create tabs when a container is first opened.

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs`

**Step 1: Add AutoOpenTabs method**

```csharp
private void AutoOpenTabs()
{
    int autoOpen = ProfileManager.CurrentProfile.GridContainerTabAutoOpen;
    if (autoOpen == 0 || !ProfileManager.CurrentProfile.GridContainerTabsEnabled)
        return;

    Item root = Container;
    if (root == null)
        return;

    if (autoOpen == 1) // Direct children only
    {
        for (LinkedObject i = root.Items; i != null; i = i.Next)
        {
            var child = (Item)i;
            if (child.IsContainer && !child.IsDestroyed)
                AddTab(child.Serial);
        }
    }
    else if (autoOpen == 2) // All nested
    {
        AutoOpenRecursive(root);
    }
}

private void AutoOpenRecursive(Item container)
{
    for (LinkedObject i = container.Items; i != null; i = i.Next)
    {
        var child = (Item)i;
        if (child.IsContainer && !child.IsDestroyed)
        {
            AddTab(child.Serial);
            AutoOpenRecursive(child);
        }
    }
}
```

**Step 2: Call AutoOpenTabs at end of constructor**

After `BuildTabBar()` in the constructor:

```csharp
AutoOpenTabs();

// Restore last active tab
if (_gridContainerEntry.CurrentTabIndex > 0 && _gridContainerEntry.CurrentTabIndex < _tabs.Count)
    SwitchToTab(_gridContainerEntry.CurrentTabIndex);
```

**Step 3: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors

**Step 4: Commit**

```
feat: implement auto-open tab modes (direct children, all nested)
```

---

## Task 9: Options UI

Add the tab settings to the Modern Options gump so users can toggle tabs on/off and set the auto-open mode.

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/ModernOptionsGump.cs`

**Step 1: Add tab settings to Grid Container options section**

Find the Grid Containers section in `BuildTazUO()` (search for `GridEnableContPreview`). After that checkbox group, add:

```csharp
content.BlankLine();

content.AddToRight
(
    new CheckboxWithLabel("Enable Grid Container Tabs", 0, profile.GridContainerTabsEnabled, (b) => { profile.GridContainerTabsEnabled = b; }),
    true, page
);

content.Indent();

string[] autoOpenOptions = { "Manual", "Direct Children", "All Nested" };
content.AddToRight
(
    new ComboBoxWithLabel("Auto-Open Sub-Containers", 0, autoOpenOptions, profile.GridContainerTabAutoOpen, (i) => { profile.GridContainerTabAutoOpen = i; }),
    true, page
);

content.RemoveIndent();
```

**Note:** Check the exact control types available. `ComboBoxWithLabel` may have a different signature — look at existing usage patterns in `ModernOptionsGump.cs` for a combo box / dropdown that maps to an int. If a different pattern is used (like multiple radio buttons or a `SliderWithLabel`), follow that pattern.

**Step 2: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors

**Step 3: Commit**

```
feat: add grid container tab settings to options UI
```

---

## Task 10: Close Child Windows on Tab Add & Edge Cases

Handle edge cases: when a sub-container is opened as a tab, close any existing standalone window for it. When the main GridContainer closes, close all tabs. Handle container disposal (item destroyed, moved out of parent).

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/Gumps/GridContainer.cs`

**Step 1: Close existing standalone window when adding tab**

In `AddTab`, after checking for duplicate tabs, before creating the SlotManager:

```csharp
// Close any existing standalone GridContainer for this sub-container
UIManager.GetGump<GridContainer>(containerSerial)?.Dispose();
```

**Step 2: Update Dispose to close all child container windows**

The existing Dispose already closes child containers for bank boxes (line ~890). Extend this pattern to close all tab SlotManagers. This was partially done in Task 5, verify it works.

**Step 3: Handle item removal — if a sub-bag is moved out of the parent, close its tab**

In `UpdateItems` or in a periodic check, verify that each tab's container is still a descendant of the root container. If not, close the tab:

```csharp
private void PruneInvalidTabs()
{
    for (int i = _tabs.Count - 1; i > 0; i--) // Skip main tab
    {
        Item tabContainer = World.Items.Get(_tabs[i].ContainerSerial);
        if (tabContainer == null || tabContainer.IsDestroyed)
        {
            CloseTab(i);
            continue;
        }

        // Verify still a descendant of root container
        uint parentSerial = tabContainer.Container;
        bool isDescendant = false;
        while (parentSerial != 0 && parentSerial != 0xFFFFFFFF)
        {
            if (parentSerial == LocalSerial)
            {
                isDescendant = true;
                break;
            }
            Item parent = World.Items.Get(parentSerial);
            if (parent == null) break;
            parentSerial = parent.Container;
        }

        if (!isDescendant)
            CloseTab(i);
    }
}
```

Call `PruneInvalidTabs()` at the start of `UpdateItems()`.

**Step 4: Build**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors

**Step 5: Commit**

```
feat: handle edge cases - close standalone windows, prune invalid tabs
```

---

## Task 11: Integration Testing & Polish

Manual testing checklist and final polish. No TDD here — this is a UI feature that requires in-game testing.

**Testing checklist:**
- [ ] Open backpack grid container — no tab bar visible (only 1 tab)
- [ ] Double-click a sub-bag — tab bar appears with 2 tabs, sub-bag contents shown
- [ ] Click main tab — switches back to backpack items
- [ ] Click sub-bag tab — switches to sub-bag items
- [ ] Open a second sub-bag — 3 tabs visible
- [ ] Close a tab via X — tab removed, switched to previous
- [ ] Right-click tab → Rename — custom name shown
- [ ] Shift+double-click sub-bag — opens as separate window, not a tab
- [ ] Close main GridContainer — all tabs disposed
- [ ] Reopen container — persisted tabs restored with custom names
- [ ] Move a sub-bag out of backpack — its tab auto-closes
- [ ] Options: disable tabs — sub-bags open as separate windows again
- [ ] Options: set auto-open to DirectChildren — tabs auto-created on open
- [ ] Options: set auto-open to AllNested — deep sub-bags also tabbed
- [ ] Scroll position preserved when switching between tabs
- [ ] Sort mode override per-tab works correctly
- [ ] Locked items preserved per-tab across sessions

**Step 1: Build final version**

Run: `dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj`
Expected: 0 errors, 0 new warnings from our code

**Step 2: Final commit**

```
feat: grid container tabs - complete implementation
```
