using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using FluentAssertions;
using SDL3;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI;

public class PinnedItemButtonHelperTest
{
    [Fact]
    public void ResolveTargetItem_ShouldPreferOwnedSerialMatch()
    {
        const uint playerSerial = 0x0000_0001;
        var world = new World();

        Item backpack = CreateItem(world, 0x4000_0001, 0x0E75, 0x0000, playerSerial);
        Item serialItem = CreateItem(world, 0x4000_0002, 0x0F52, 0x0123, backpack.Serial);
        Item fallbackItem = CreateItem(world, 0x4000_0003, 0x0F52, 0x0123, backpack.Serial);

        var items = new Dictionary<uint, Item>
        {
            [backpack.Serial] = backpack,
            [serialItem.Serial] = serialItem,
            [fallbackItem.Serial] = fallbackItem
        };

        Item resolved = PinnedItemButtonHelper.ResolveTargetItem(
            playerSerial,
            serialItem.Serial,
            serial => items.TryGetValue(serial, out Item item) ? item : null,
            (graphic, hue) => fallbackItem,
            0x0F52,
            0x0123
        );

        resolved.Should().Be(serialItem);
    }

    [Fact]
    public void ResolveTargetItem_ShouldFallback_WhenSerialIsNotOwnedByPlayer()
    {
        const uint playerSerial = 0x0000_0001;
        var world = new World();

        Item foreignItem = CreateItem(world, 0x4000_0010, 0x0F52, 0x0123, uint.MaxValue);
        Item fallbackItem = CreateItem(world, 0x4000_0011, 0x0F52, 0x0123, playerSerial);

        Item resolved = PinnedItemButtonHelper.ResolveTargetItem(
            playerSerial,
            foreignItem.Serial,
            _ => foreignItem,
            (graphic, hue) => fallbackItem,
            0x0F52,
            0x0123
        );

        resolved.Should().Be(fallbackItem);
    }

    [Fact]
    public void FindHotkeyConflict_ShouldReturnConflictingMacro_AndIgnoreCurrentMacro()
    {
        var world = new World();

        var existing = new Macro("Existing")
        {
            Key = SDL.SDL_Keycode.SDLK_F7,
            Ctrl = true
        };
        existing.PushToBack(new MacroObject(MacroType.LastObject, MacroSubType.MSC_NONE));
        world.Macros.PushToBack(existing);

        var current = new Macro("Current")
        {
            Key = SDL.SDL_Keycode.SDLK_F8,
            Alt = true
        };
        current.PushToBack(new MacroObject(MacroType.LastTarget, MacroSubType.MSC_NONE));
        world.Macros.PushToBack(current);

        Macro conflict = PinnedItemButtonHelper.FindHotkeyConflict(
            world.Macros,
            current,
            SDL.SDL_Keycode.SDLK_F7,
            MouseButtonType.None,
            false,
            false,
            alt: false,
            ctrl: true,
            shift: false
        );

        conflict.Should().Be(existing);

        Macro selfConflict = PinnedItemButtonHelper.FindHotkeyConflict(
            world.Macros,
            current,
            SDL.SDL_Keycode.SDLK_F8,
            MouseButtonType.None,
            false,
            false,
            alt: true,
            ctrl: false,
            shift: false
        );

        selfConflict.Should().BeNull();
    }

    [Theory]
    [InlineData("120", 72, 120)]
    [InlineData("999", 72, PinnedItemButtonGump.MaxSize)]
    [InlineData("1", 72, PinnedItemButtonGump.MinSize)]
    [InlineData("", 96, 96)]
    [InlineData("invalid", 20, PinnedItemButtonGump.MinSize)]
    public void ResolveRestoredSize_ShouldPreferSavedValueAndClampInvalidInputs(
        string savedSize,
        int profileDefault,
        int expected
    )
    {
        int resolved = PinnedItemButtonHelper.ResolveRestoredSize(savedSize, profileDefault);

        resolved.Should().Be(expected);
    }

    [Fact]
    public void UpdateProfileDefaultSize_ShouldClampAssignedValue()
    {
        var profile = new Profile();

        PinnedItemButtonHelper.UpdateProfileDefaultSize(profile, 999);
        profile.PinnedItemButtonDefaultSize.Should().Be(PinnedItemButtonGump.MaxSize);

        PinnedItemButtonHelper.UpdateProfileDefaultSize(profile, 1);
        profile.PinnedItemButtonDefaultSize.Should().Be(PinnedItemButtonGump.MinSize);
    }

    [Fact]
    public void DisableAutoSizing_ShouldPreventChildDrivenShrink()
    {
        const int initialSize = 72;

        var autoSizeControl = new Area
        {
            Width = initialSize,
            Height = initialSize,
            WantUpdateSize = true
        };
        autoSizeControl.Add(new Area { X = 2, Y = 56, Width = 8, Height = 8 });
        autoSizeControl.Update();
        autoSizeControl.Width.Should().Be(10);
        autoSizeControl.Height.Should().Be(64);

        var pinnedLikeControl = new Area
        {
            Width = initialSize,
            Height = initialSize,
            WantUpdateSize = true
        };
        pinnedLikeControl.Add(new Area { X = 2, Y = 56, Width = 8, Height = 8 });

        PinnedItemButtonHelper.DisableAutoSizing(pinnedLikeControl);
        pinnedLikeControl.Update();

        pinnedLikeControl.Width.Should().Be(initialSize);
        pinnedLikeControl.Height.Should().Be(initialSize);
    }

    [Fact]
    public void ApplyManualSize_ShouldClampAndDisableAutoSizing()
    {
        var control = new Area { WantUpdateSize = true };

        PinnedItemButtonHelper.ApplyManualSize(control, 999);
        control.Width.Should().Be(PinnedItemButtonGump.MaxSize);
        control.Height.Should().Be(PinnedItemButtonGump.MaxSize);
        control.WantUpdateSize.Should().BeFalse();

        PinnedItemButtonHelper.ApplyManualSize(control, 1);
        control.Width.Should().Be(PinnedItemButtonGump.MinSize);
        control.Height.Should().Be(PinnedItemButtonGump.MinSize);
    }

    [Theory]
    [InlineData("Control+Left Mouse Button", "Ctrl+LMB")]
    [InlineData("Alt+Mouse Wheel Up", "Alt+MW Up")]
    [InlineData("Shift+Right Mouse Button", "Shift+RMB")]
    public void ToReadableHotkey_ShouldCondenseVerboseMouseNames(string input, string expected)
    {
        PinnedItemButtonHelper.ToReadableHotkey(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(72, 72, 1)]
    [InlineData(-72, 72, -1)]
    [InlineData(150, 72, 2)]
    [InlineData(0, 72, 0)]
    [InlineData(80, 0, 0)]
    public void CalculateGridOffset_ShouldRoundUsingPreviousSize(int delta, int previousSize, int expected)
    {
        PinnedItemButtonHelper.CalculateGridOffset(delta, previousSize).Should().Be(expected);
    }

    private static Item CreateItem(World world, uint serial, ushort graphic, ushort hue, uint container)
    {
        ClassicUO.Client.UnitTestingActive = true;

        Item item = Item.Create(world, serial);
        item.Graphic = graphic;
        item.Hue = hue;
        item.Container = container;
        world.Items[serial] = item;
        return item;
    }
}
