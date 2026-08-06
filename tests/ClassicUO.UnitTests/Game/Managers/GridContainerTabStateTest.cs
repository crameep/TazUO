using System.Collections.Generic;
using System.Text.Json;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class GridContainerTabStateTest
{
    [Fact]
    public void GetSavedActiveTabSerial_AccountsForRootTabOffset()
    {
        GridContainerEntry entry = CreateEntryWithTabs();
        entry.CurrentTabIndex = 2;

        Assert.Equal(0x4000_0002u, entry.GetSavedActiveTabSerial());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(99)]
    public void GetSavedActiveTabSerial_InvalidOrRootIndexReturnsZero(int currentTabIndex)
    {
        GridContainerEntry entry = CreateEntryWithTabs();
        entry.CurrentTabIndex = currentTabIndex;

        Assert.Equal(0u, entry.GetSavedActiveTabSerial());
    }

    [Fact]
    public void TabState_RoundTripsWithoutLosingActiveTabOrPerTabData()
    {
        GridContainerEntry original = CreateEntryWithTabs();
        original.Serial = 0x4000_1000;
        original.CurrentTabIndex = 2;
        original.Tabs[1].CustomName = "Supplies";
        original.Tabs[1].SortMode = 3;
        original.Tabs[1].Slots[0x4000_2000] = new GridContainerSlotEntry
        {
            Serial = 0x4000_2000,
            Slot = 7,
            Locked = true
        };

        string json = JsonSerializer.Serialize(
            original,
            GridContainerSerializerContext.Default.GridContainerEntry
        );
        GridContainerEntry restored = JsonSerializer.Deserialize(
            json,
            GridContainerSerializerContext.Default.GridContainerEntry
        );

        Assert.NotNull(restored);
        Assert.Equal(0x4000_0002u, restored.GetSavedActiveTabSerial());
        Assert.Equal("Supplies", restored.Tabs[1].CustomName);
        Assert.Equal(3, restored.Tabs[1].SortMode);
        Assert.True(restored.Tabs[1].Slots[0x4000_2000].Locked);
        Assert.Equal(7, restored.Tabs[1].Slots[0x4000_2000].Slot);
    }

    [Theory]
    [InlineData("25 items\n12 stones", true, 25)]
    [InlineData("1 item", true, 1)]
    [InlineData("0 items", true, 0)]
    [InlineData("MAX ITEMS: 125", false, -1)]
    [InlineData("not a count", false, -1)]
    [InlineData(null, false, -1)]
    public void TryParseOplItemCount_RecognizesOnlyLeadingItemCounts(
        string data,
        bool expectedResult,
        int expectedCount
    )
    {
        bool result = GridContainer.TryParseOplItemCount(data, out int count);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedCount, count);
    }

    private static GridContainerEntry CreateEntryWithTabs() =>
        new()
        {
            Tabs = new List<GridContainerTabEntry>
            {
                new() { ContainerSerial = 0x4000_0001 },
                new() { ContainerSerial = 0x4000_0002 }
            }
        };
}
