using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps.GridContainers;
using ClassicUO.Game.UI.MyraWindows;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps;

public partial class GridContainer
{
    private const int TAB_BAR_HEIGHT = 25;
    private const int CAPACITY_BAR_HEIGHT = 3;
    private const int CAPACITY_BAR_OVERLAP = 3;
    private const int TAB_BUTTON_WIDTH = 80;
    private const int TAB_BUTTON_GAP = 4;

    internal static uint ForceNewWindowSerial;

    private readonly List<ContainerTab> _tabs = new();
    private readonly List<NiceButton> _tabButtons = new();
    private readonly List<HitBox> _tabCloseButtons = new();
    private int _activeTabIndex;
    private int _tabRowCount = 1;
    private uint _pendingActiveTabSerial;

    private ContainerTab ActiveTab => _tabs.Count == 0 ? null : _tabs[_activeTabIndex];
    private Item ActiveContainer => ActiveTab == null ? Container : World.Items.Get(ActiveTab.ContainerSerial) ?? Container;
    private uint ActiveContainerSerial => ActiveTab?.ContainerSerial ?? LocalSerial;
    private bool TabBarVisible => _tabs.Count > 1 && ProfileManager.CurrentProfile.GridContainerTabsEnabled;
    private int EffectiveTabBarHeight => TabBarVisible ? TAB_BAR_HEIGHT * _tabRowCount : 0;

    internal GridSlotManager RootSlotManager => _tabs.Count == 0 ? SlotManager : _tabs[0].SlotManager;
    internal GridSortMode RootSortMode => _tabs.Count == 0 ? _sortMode : _tabs[0].SortMode;

    private void InitializeContainerTabs()
    {
        if (_tabs.Count != 0)
            return;

        _tabs.Add(new ContainerTab
        {
            ContainerSerial = LocalSerial,
            SlotManager = SlotManager,
            SortMode = _sortMode
        });

        _pendingActiveTabSerial = ProfileManager.CurrentProfile.GridContainerTabsEnabled
            ? _gridContainerEntry.GetSavedActiveTabSerial()
            : 0;
        RestoreSavedTabsIfAvailable();
        AutoOpenTabs();
        BuildTabBar();
        TryRestorePendingActiveTab();
    }

    /// <summary>
    /// Restores saved tabs as their item objects arrive. Container contents are packet-driven, so
    /// this is intentionally safe to call after every root-content refresh.
    /// </summary>
    private void RestoreSavedTabsIfAvailable()
    {
        if (!ProfileManager.CurrentProfile.GridContainerTabsEnabled || _gridContainerEntry?.Tabs == null)
            return;

        foreach (GridContainerTabEntry savedTab in new List<GridContainerTabEntry>(_gridContainerEntry.Tabs))
        {
            if (!HasTab(savedTab.ContainerSerial)
                && World.Items.Get(savedTab.ContainerSerial) is Item item
                && !item.IsDestroyed
                && IsDescendantOfRoot(item))
            {
                AddTabCore(savedTab.ContainerSerial, false);
            }
        }

        // Nested container objects often arrive after the root gump is constructed. Restore by
        // serial instead of clamping the saved index against the tabs that happen to exist now.
        TryRestorePendingActiveTab();
    }

    private void AutoOpenTabs()
    {
        int autoOpen = ProfileManager.CurrentProfile.GridContainerTabAutoOpen;
        if (autoOpen == 0 || !ProfileManager.CurrentProfile.GridContainerTabsEnabled || Container == null)
            return;

        if (autoOpen == 1)
        {
            for (LinkedObject node = Container.Items; node != null; node = node.Next)
            {
                if (node is Item child && child.ItemData.IsContainer && !child.IsDestroyed)
                    AddTabCore(child.Serial, false);
            }
        }
        else
        {
            AutoOpenRecursive(Container, new HashSet<uint>(), 100);
        }
    }

    private void AutoOpenRecursive(Item container, HashSet<uint> visited, int remainingDepth)
    {
        if (container == null || remainingDepth <= 0 || !visited.Add(container.Serial))
            return;

        for (LinkedObject node = container.Items; node != null; node = node.Next)
        {
            if (node is not Item child || !child.ItemData.IsContainer || child.IsDestroyed)
                continue;

            AddTabCore(child.Serial, false);
            AutoOpenRecursive(child, visited, remainingDepth - 1);
        }
    }

    private void TryRestorePendingActiveTab()
    {
        if (_pendingActiveTabSerial == 0)
            return;

        for (int i = 1; i < _tabs.Count; i++)
        {
            if (_tabs[i].ContainerSerial != _pendingActiveTabSerial)
                continue;

            _pendingActiveTabSerial = 0;
            SwitchToTab(i);
            return;
        }
    }

    public void AddTab(uint containerSerial) => AddTabCore(containerSerial, true);

    private void AddTabCore(uint containerSerial, bool activate)
    {
        if (!ProfileManager.CurrentProfile.GridContainerTabsEnabled || containerSerial == LocalSerial)
            return;

        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].ContainerSerial != containerSerial)
                continue;

            if (activate)
                SwitchToTab(i);
            return;
        }

        Item container = World.Items.Get(containerSerial);
        if (container == null || container.IsDestroyed || !IsDescendantOfRoot(container))
            return;

        UIManager.GetGump<GridContainer>(containerSerial)?.Dispose();

        GridContainerTabEntry savedTab = _gridContainerEntry.Tabs.Find(t => t.ContainerSerial == containerSerial);
        if (savedTab == null)
        {
            savedTab = new GridContainerTabEntry { ContainerSerial = containerSerial };
            _gridContainerEntry.Tabs.Add(savedTab);
        }

        GridSortMode sortMode = savedTab.SortMode >= 0 ? (GridSortMode)savedTab.SortMode : _tabs[0].SortMode;
        var slotManager = new GridSlotManager(World, containerSerial, this, _scrollArea, savedTab.Slots);

        // GridSlotManager creates controls eagerly. Inactive tabs keep their controls detached.
        foreach (GridItem gridItem in slotManager.GridSlots.Values)
            _scrollArea.Remove(gridItem);

        _tabs.Add(new ContainerTab
        {
            ContainerSerial = containerSerial,
            CustomName = savedTab.CustomName,
            SlotManager = slotManager,
            SortMode = sortMode,
            SortModeOverridden = savedTab.SortMode >= 0
        });

        BuildTabBar();
        if (activate)
            SwitchToTab(_tabs.Count - 1);
    }

    private bool IsDescendantOfRoot(Item item)
    {
        uint parentSerial = item.Container;
        int remainingDepth = 100;

        while (SerialHelper.IsValid(parentSerial) && remainingDepth-- > 0)
        {
            if (parentSerial == LocalSerial)
                return true;

            Item parent = World.Items.Get(parentSerial);
            if (parent == null)
                return false;

            parentSerial = parent.Container;
        }

        return false;
    }

    private void SwitchToTab(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= _tabs.Count)
            return;

        // Any explicit tab selection wins over a saved tab that has not arrived yet.
        // TryRestorePendingActiveTab clears this first, so the deferred restore still works.
        _pendingActiveTabSerial = 0;
        if (tabIndex == _activeTabIndex)
            return;

        ContainerTab currentTab = _tabs[_activeTabIndex];
        currentTab.ScrollPosition = _scrollArea.ScrollValue;
        foreach (GridItem gridItem in currentTab.SlotManager.GridSlots.Values)
            _scrollArea.Remove(gridItem);

        _activeTabIndex = tabIndex;
        ContainerTab newTab = _tabs[tabIndex];
        SlotManager = newTab.SlotManager;
        _sortMode = newTab.SortModeOverridden ? newTab.SortMode : _tabs[0].SortMode;

        foreach (GridItem gridItem in newTab.SlotManager.GridSlots.Values)
            _scrollArea.Add(gridItem);

        _scrollArea.ScrollValue = newTab.ScrollPosition;
        foreach (NiceButton button in _tabButtons)
            button.IsSelected = button.ButtonParameter == _activeTabIndex;

        _sortContents.ContextMenu = GenSortContextMenu();
        _sortContents.SetTooltip(SortButtonTooltip);
        UpdateContainerNameLabel();
        InvalidateContents = true;
    }

    /// <summary>
    /// Tab buttons use <see cref="ButtonAction.Activate"/> so they can carry their tab index in
    /// <see cref="Control.ButtonParameter"/>. That makes <see cref="NiceButton.OnMouseUp"/> bubble an
    /// OnButtonClick up to the gump, and the base <see cref="Gump.OnButtonClick"/> treats any gump with a
    /// non-zero LocalSerial as a server gump: it replies to the server and disposes itself. GridContainer
    /// is a client-side gump whose LocalSerial is the container item, so that path would close the whole
    /// container the moment a tab is clicked. Tab activation is handled by the button's own MouseUp
    /// handler in <see cref="BuildTabBar"/>, so this override intentionally does nothing.
    /// </summary>
    public override void OnButtonClick(int buttonID)
    {
    }

    private void BuildTabBar()
    {
        foreach (NiceButton button in _tabButtons)
        {
            Remove(button);
            button.Dispose();
        }
        _tabButtons.Clear();

        foreach (HitBox closeButton in _tabCloseButtons)
        {
            Remove(closeButton);
            closeButton.Dispose();
        }
        _tabCloseButtons.Clear();

        if (!TabBarVisible)
        {
            _tabRowCount = 1;
            LayoutControls();
            return;
        }

        for (int i = 0; i < _tabs.Count; i++)
        {
            int tabIndex = i;
            string label = GetTabLabel(_tabs[i]);
            if (i > 0)
                label += " ×";

            var button = new NiceButton(0, 0, TAB_BUTTON_WIDTH, TAB_BAR_HEIGHT, ButtonAction.Activate, label, 7319)
            {
                ButtonParameter = tabIndex,
                IsSelectable = true,
                IsSelected = i == _activeTabIndex,
                CanCloseWithRightClick = false,
                DisplayBorder = true
            };

            Item tabItem = World.Items.Get(_tabs[i].ContainerSerial);
            if (tabItem?.Hue > 0)
            {
                uint packedColor = Client.Game.UO.FileManager.Hues.GetHueColorRgba8888(31, tabItem.Hue);
                button.BackgroundColor = new Color { PackedValue = packedColor };
            }

            button.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtonType.Left)
                    SwitchToTab(tabIndex);
                else if (e.Button == MouseButtonType.Right)
                    ShowTabContextMenu(tabIndex);
            };

            _tabButtons.Add(button);
            Add(button);

            if (i == 0)
                continue;

            var closeButton = new HitBox(0, 0, 16, TAB_BAR_HEIGHT)
            {
                CanCloseWithRightClick = false,
                Alpha = 0f
            };
            closeButton.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtonType.Left)
                    CloseTab(tabIndex);
            };
            _tabCloseButtons.Add(closeButton);
            Add(closeButton);
        }

        PositionTabButtons();
        LayoutControls();
    }

    private void PositionTabButtons()
    {
        if (!TabBarVisible)
            return;

        int x = _borderWidth;
        int y = _borderWidth + LABEL_HEIGHT + TOP_BAR_HEIGHT;
        int maxX = Width - _borderWidth;
        int rows = 1;
        int closeIndex = 0;

        for (int i = 0; i < _tabButtons.Count; i++)
        {
            NiceButton button = _tabButtons[i];
            if (x + button.Width > maxX && i > 0)
            {
                x = _borderWidth;
                y += TAB_BAR_HEIGHT;
                rows++;
            }

            button.X = x;
            button.Y = y;
            button.IsVisible = !_isMinimized;

            if (i > 0 && closeIndex < _tabCloseButtons.Count)
            {
                HitBox closeButton = _tabCloseButtons[closeIndex++];
                closeButton.X = x + button.Width - closeButton.Width;
                closeButton.Y = y;
                closeButton.IsVisible = !_isMinimized;
            }

            x += button.Width + TAB_BUTTON_GAP;
        }

        _tabRowCount = rows;
    }

    private string GetTabLabel(ContainerTab tab)
    {
        if (!string.IsNullOrWhiteSpace(tab.CustomName))
            return tab.CustomName.Truncate(10);
        if (tab.ContainerSerial == LocalSerial)
            return TazLang.Get("gridtab_main", "Main");

        Item item = World.Items.Get(tab.ContainerSerial);
        if (item != null && World.OPL.TryGetNameAndData(item.Serial, out string name, out _) && !string.IsNullOrWhiteSpace(name))
            return name.Truncate(10);
        if (!string.IsNullOrWhiteSpace(item?.Name))
            return item.Name.Truncate(10);
        return TazLang.Get("gridtab_bag", "Bag");
    }

    private void ShowTabContextMenu(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= _tabs.Count)
            return;

        ContainerTab tab = _tabs[tabIndex];
        var menu = new ContextMenuControl(this);
        menu.Add(TazLang.Get("gridtab_rename", "Rename Tab"), () =>
        {
            new PromptPopupWindow(
                TazLang.Get("gridtab_rename", "Rename Tab"),
                TazLang.Get("gridtab_rename_prompt", "Enter a custom name for this tab."),
                name =>
                {
                    tab.CustomName = name?.Trim();
                    SaveTabData(tab);
                    BuildTabBar();
                    UpdateContainerNameLabel();
                },
                TazLang.Get("gridcontainer_save", "Save"),
                TazLang.Get("gridcontainer_reset", "Reset"),
                () =>
                {
                    tab.CustomName = null;
                    SaveTabData(tab);
                    BuildTabBar();
                    UpdateContainerNameLabel();
                },
                tab.CustomName ?? string.Empty
            );
        });

        if (tabIndex > 0)
            menu.Add(TazLang.Get("gridtab_close", "Close Tab"), () => CloseTab(tabIndex));

        menu.Show();
    }

    private void CloseTab(int tabIndex)
    {
        if (tabIndex <= 0 || tabIndex >= _tabs.Count)
            return;

        ContainerTab tab = _tabs[tabIndex];
        bool wasActive = tabIndex == _activeTabIndex;
        if (wasActive)
            SwitchToTab(Math.Max(0, tabIndex - 1));

        foreach (GridItem gridItem in tab.SlotManager.GridSlots.Values)
        {
            _scrollArea.Remove(gridItem);
            gridItem.Dispose();
        }

        _tabs.RemoveAt(tabIndex);
        _gridContainerEntry.Tabs.RemoveAll(t => t.ContainerSerial == tab.ContainerSerial);

        if (_activeTabIndex > tabIndex)
            _activeTabIndex--;

        BuildTabBar();
        PersistAllTabs();
    }

    private void CloseTabBySerial(uint containerSerial)
    {
        for (int i = 1; i < _tabs.Count; i++)
        {
            if (_tabs[i].ContainerSerial == containerSerial)
            {
                CloseTab(i);
                return;
            }
        }
    }

    private void PruneInvalidTabs()
    {
        for (int i = _tabs.Count - 1; i > 0; i--)
        {
            Item item = World.Items.Get(_tabs[i].ContainerSerial);
            if (item == null || item.IsDestroyed || !IsDescendantOfRoot(item))
                CloseTab(i);
        }
    }

    private void SaveTabData(ContainerTab tab)
    {
        if (tab.ContainerSerial == LocalSerial)
            return;

        GridContainerTabEntry entry = _gridContainerEntry.Tabs.Find(t => t.ContainerSerial == tab.ContainerSerial);
        if (entry == null)
        {
            entry = new GridContainerTabEntry { ContainerSerial = tab.ContainerSerial };
            _gridContainerEntry.Tabs.Add(entry);
        }

        entry.CustomName = tab.CustomName;
        entry.SortMode = tab.SortModeOverridden ? (int)tab.SortMode : -1;
        entry.Slots.Clear();

        foreach (KeyValuePair<int, uint> position in tab.SlotManager.ItemPositions)
        {
            entry.Slots[position.Value] = new GridContainerSlotEntry
            {
                Serial = position.Value,
                Slot = position.Key,
                Locked = tab.SlotManager.GridSlots.TryGetValue(position.Key, out GridItem item) && item.ItemGridLocked
            };
        }
    }

    private void PersistAllTabs()
    {
        if (_tabs.Count == 0 || _isCorpse)
            return;

        _gridContainerEntry.CurrentTabIndex = _activeTabIndex;
        for (int i = 1; i < _tabs.Count; i++)
            SaveTabData(_tabs[i]);
    }

    private void SaveAndDisposeTabs()
    {
        PersistAllTabs();

        for (int i = 1; i < _tabs.Count; i++)
        {
            foreach (GridItem gridItem in _tabs[i].SlotManager.GridSlots.Values)
            {
                _scrollArea.Remove(gridItem);
                gridItem.Dispose();
            }
        }
    }

    private void PersistActiveTabSort()
    {
        if (ActiveTab == null)
            return;

        ActiveTab.SortMode = _sortMode;
        ActiveTab.SortModeOverridden = _activeTabIndex > 0;
        if (_activeTabIndex == 0)
        {
            foreach (ContainerTab tab in _tabs)
            {
                if (!tab.SortModeOverridden)
                    tab.SortMode = _sortMode;
            }
        }
        else
        {
            SaveTabData(ActiveTab);
        }
    }

    private void MaintainContainerTabs()
    {
        RestoreSavedTabsIfAvailable();
        AutoOpenTabs();
        PruneInvalidTabs();
    }

    private void RefreshTabsForOptions()
    {
        if (!ProfileManager.CurrentProfile.GridContainerTabsEnabled && _activeTabIndex > 0)
            SwitchToTab(0);
        BuildTabBar();
    }

    private void SetTabControlsVisible(bool visible)
    {
        foreach (NiceButton button in _tabButtons)
            button.IsVisible = visible && TabBarVisible;
        foreach (HitBox button in _tabCloseButtons)
            button.IsVisible = visible && TabBarVisible;
    }

    private string ResolveActiveRawName()
    {
        if (ActiveTab != null && ActiveTab.ContainerSerial != LocalSerial && !string.IsNullOrWhiteSpace(ActiveTab.CustomName))
            return ActiveTab.CustomName;
        if (ActiveTab?.ContainerSerial == LocalSerial && GridContainerEntry?.CustomName.NotNullNotEmpty() == true)
            return GridContainerEntry.CustomName;
        return !string.IsNullOrWhiteSpace(ActiveContainer?.Name) ? ActiveContainer.Name : "a container";
    }

    private void SetActiveContainerCustomName(string name)
    {
        if (ActiveTab == null || ActiveTab.ContainerSerial == LocalSerial)
            _gridContainerEntry.CustomName = name;
        else
        {
            ActiveTab.CustomName = name;
            SaveTabData(ActiveTab);
        }

        BuildTabBar();
        UpdateContainerNameLabel();
    }

    private int GetOplItemCount()
    {
        Item container = ActiveContainer;
        if (container == null || !World.OPL.TryGetNameAndData(container.Serial, out _, out string data) || string.IsNullOrEmpty(data))
            return -1;

        return TryParseOplItemCount(data, out int count) ? count : -1;
    }

    internal static bool TryParseOplItemCount(string data, out int count)
    {
        count = -1;
        if (string.IsNullOrEmpty(data))
            return false;

        foreach (string line in data.Split('\n'))
        {
            string trimmed = line.Trim();
            int index = trimmed.IndexOf(" item", StringComparison.OrdinalIgnoreCase);
            if (index > 0
                && int.TryParse(trimmed.AsSpan(0, index), out int parsed)
                && parsed >= 0)
            {
                count = parsed;
                return true;
            }
        }

        return false;
    }

    private void DrawCapacityBar(UltimaBatcher2D batcher, int x, int y)
    {
        if (_isMinimized || !ProfileManager.CurrentProfile.Grid_ShowCapacityBar)
            return;

        int count = GetOplItemCount();
        if (count < 0)
            return;

        int max = ProfileManager.CurrentProfile.Grid_MaxContainerItems;
        float ratio = Math.Clamp(count / (float)max, 0f, 1f);
        int barX = x + _borderWidth;
        int barY = y + _borderWidth + LABEL_HEIGHT - CAPACITY_BAR_OVERLAP;
        int barWidth = Width - (_borderWidth * 2);

        batcher.Draw(
            SolidColorTextureCache.GetTexture(Color.Black),
            new Rectangle(barX, barY, barWidth, CAPACITY_BAR_HEIGHT),
            ShaderHueTranslator.GetHueVector(0, false, 0.6f)
        );

        Color fillColor = ratio switch
        {
            < 0.5f => Color.Green,
            < 0.8f => Color.Yellow,
            < 0.95f => Color.Orange,
            _ => Color.Red
        };
        int fillWidth = (int)(barWidth * ratio);
        if (fillWidth > 0)
        {
            batcher.Draw(
                SolidColorTextureCache.GetTexture(fillColor),
                new Rectangle(barX, barY, fillWidth, CAPACITY_BAR_HEIGHT),
                ShaderHueTranslator.GetHueVector(0, false, 0.8f)
            );
        }
    }

    internal static GridContainer FindParentGridContainer(World world, uint containerSerial)
    {
        Item item = world.Items.Get(containerSerial);
        if (item == null)
            return null;

        uint parentSerial = item.Container;
        int remainingDepth = 100;
        while (SerialHelper.IsValid(parentSerial) && remainingDepth-- > 0)
        {
            GridContainer parentGump = UIManager.GetGump<GridContainer>(parentSerial);
            if (parentGump != null)
                return parentGump;

            Item parent = world.Items.Get(parentSerial);
            if (parent == null)
                break;
            parentSerial = parent.Container;
        }

        return null;
    }

    internal static void NotifyContainerUpdate(World world, uint containerSerial)
    {
        GridContainer gump = UIManager.GetGump<GridContainer>(containerSerial);
        if (gump != null)
        {
            gump.RequestUpdateContents();
            return;
        }

        FindParentGridContainer(world, containerSerial)?.RequestUpdateContentsForTab(containerSerial);
    }

    internal bool HasTab(uint containerSerial)
    {
        foreach (ContainerTab tab in _tabs)
        {
            if (tab.ContainerSerial == containerSerial)
                return true;
        }
        return false;
    }

    private bool ActivateTab(uint containerSerial)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].ContainerSerial == containerSerial)
            {
                SwitchToTab(i);
                RequestUpdateContentsForTab(containerSerial);
                return true;
            }
        }
        return false;
    }

    public void RequestUpdateContentsForTab(uint containerSerial)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            ContainerTab tab = _tabs[i];
            if (tab.ContainerSerial != containerSerial)
                continue;

            if (i == _activeTabIndex)
            {
                InvalidateContents = true;
                return;
            }

            Item container = World.Items.Get(containerSerial);
            if (container == null)
                return;

            GridSortMode sort = tab.SortModeOverridden ? tab.SortMode : _tabs[0].SortMode;
            List<Item> items = GridSlotManager.GetItemsInContainer(World, container, sort, true);
            tab.SlotManager.RebuildContainer(items, string.Empty, true);
            foreach (GridItem gridItem in tab.SlotManager.GridSlots.Values)
                _scrollArea.Remove(gridItem);
            return;
        }

        // An arriving nested item may make a previously saved tab restorable.
        RestoreSavedTabsIfAvailable();
    }

    private void HandleTabbedObjectMessage(Entity parent, string text, ushort hue)
    {
        if (parent == null)
            return;

        foreach (ContainerTab tab in _tabs)
        {
            GridItem item = tab.SlotManager.FindItem(parent.Serial);
            if (item != null)
            {
                item.AddText(text, hue);
                return;
            }
        }
    }

    private sealed class ContainerTab
    {
        public uint ContainerSerial;
        public string CustomName;
        public GridSlotManager SlotManager;
        public int ScrollPosition;
        public GridSortMode SortMode;
        public bool SortModeOverridden;
    }
}
