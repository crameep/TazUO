// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Xml;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using Microsoft.Xna.Framework;
using SDL3;

namespace ClassicUO.Game.UI.Gumps;

internal static class PinnedItemButtonHelper
{
    internal static void DisableAutoSizing(Control control)
    {
        if (control != null)
        {
            control.WantUpdateSize = false;
        }
    }

    internal static void ApplyManualSize(Control control, int size)
    {
        if (control == null)
        {
            return;
        }

        int clampedSize = ClampSize(size);
        control.Width = clampedSize;
        control.Height = clampedSize;
        control.WantUpdateSize = false;
    }

    internal static int GetDefaultSizeFromProfile(int fallbackSize)
    {
        int configuredSize = ProfileManager.CurrentProfile?.PinnedItemButtonDefaultSize ?? fallbackSize;

        return ClampSize(configuredSize);
    }

    internal static int ResolveRestoredSize(string savedSize, int profileDefaultSize)
    {
        if (int.TryParse(savedSize, out int parsedSize))
        {
            return ClampSize(parsedSize);
        }

        return ClampSize(profileDefaultSize);
    }

    internal static void UpdateProfileDefaultSize(Profile profile, int size)
    {
        if (profile == null)
        {
            return;
        }

        profile.PinnedItemButtonDefaultSize = ClampSize(size);
    }

    internal static Item ResolveTargetItem(
        uint playerSerial,
        uint itemSerial,
        Func<uint, Item> serialResolver,
        Func<ushort, ushort?, Item> fallbackResolver,
        ushort graphic,
        ushort hue
    )
    {
        if (graphic == 0)
        {
            return null;
        }

        if (SerialHelper.IsValid(itemSerial) && serialResolver != null)
        {
            Item serialItem = serialResolver(itemSerial);

            if (IsItemOwnedByPlayer(playerSerial, serialItem))
            {
                return serialItem;
            }
        }

        if (fallbackResolver == null)
        {
            return null;
        }

        ushort? hueFilter = hue == ushort.MaxValue ? null : hue;

        return fallbackResolver(graphic, hueFilter);
    }

    internal static Item ResolveTargetItem(World world, uint itemSerial, ushort graphic, ushort hue)
    {
        if (world?.Player == null || graphic == 0)
        {
            return null;
        }

        return ResolveTargetItem(
            world.Player.Serial,
            itemSerial,
            world.Items.Get,
            world.Player.FindItemByGraphicAndHue,
            graphic,
            hue
        );
    }

    internal static bool IsItemOwnedByPlayer(World world, Item item)
    {
        if (world?.Player == null)
        {
            return false;
        }

        return IsItemOwnedByPlayer(world.Player.Serial, item);
    }

    internal static bool IsItemOwnedByPlayer(uint playerSerial, Item item)
    {
        if (!SerialHelper.IsValid(playerSerial) || item == null || item.IsDestroyed)
        {
            return false;
        }

        return item.RootContainer == playerSerial;
    }

    internal static Macro FindHotkeyConflict(
        MacroManager macroManager,
        Macro currentMacro,
        SDL.SDL_Keycode key,
        MouseButtonType mouseButton,
        bool wheelScroll,
        bool wheelUp,
        bool alt,
        bool ctrl,
        bool shift
    )
    {
        if (macroManager == null)
        {
            return null;
        }

        Macro conflict = null;

        if (key != SDL.SDL_Keycode.SDLK_UNKNOWN)
        {
            conflict = macroManager.FindMacro(key, alt, ctrl, shift);
        }
        else if (mouseButton != MouseButtonType.None)
        {
            conflict = macroManager.FindMacro(mouseButton, alt, ctrl, shift);
        }
        else if (wheelScroll)
        {
            conflict = macroManager.FindMacro(wheelUp, alt, ctrl, shift);
        }

        return conflict == currentMacro ? null : conflict;
    }

    internal static string BuildUseTypePattern(ushort graphic, ushort hue)
    {
        return $"0x{graphic:X4} {hue}";
    }

    internal static int ClampSize(int size)
    {
        return Math.Clamp(size, PinnedItemButtonGump.MinSize, PinnedItemButtonGump.MaxSize);
    }

    internal static string ToReadableHotkey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text
            .Replace("Left Mouse Button", "LMB")
            .Replace("Right Mouse Button", "RMB")
            .Replace("Middle Mouse Button", "MMB")
            .Replace("Mouse Wheel Up", "MW Up")
            .Replace("Mouse Wheel Down", "MW Down")
            .Replace("Control", "Ctrl");
    }
}

public sealed class PinnedItemButtonGump : AnchorableGump
{
    internal const int MinSize = 36;
    internal const int MaxSize = 140;
    internal const int DefaultSize = 72;

    private ushort _graphic;
    private ushort _hue;
    private uint _itemSerial;
    private int _size = DefaultSize;
    private string _macroName = string.Empty;
    private Label _hotkeyLabel;

    public PinnedItemButtonGump(World world) : base(world, 0, 0)
    {
        CanMove = true;
        AcceptMouseInput = true;
        CanCloseWithRightClick = false;
        WidthMultiplier = 1;
        HeightMultiplier = 1;
        AnchorType = ANCHOR_TYPE.PINNED_ITEM;
        PinnedItemButtonHelper.DisableAutoSizing(this);
        SetSize(PinnedItemButtonHelper.GetDefaultSizeFromProfile(DefaultSize), false);
        SetInScreen();
    }

    public PinnedItemButtonGump(World world, Item item) : this(world)
    {
        SetFromItem(item);
    }

    public override GumpType GumpType => GumpType.PinnedItemButton;

    public static void CreateFromItem(World world, Item item)
    {
        if (world == null || item == null)
        {
            return;
        }

        var gump = new PinnedItemButtonGump(world, item);
        Point uiPos = UIManager.ScreenToUI(Mouse.Position);
        gump.X = uiPos.X;
        gump.Y = uiPos.Y;
        gump.SetInScreen();
        UIManager.Add(gump);
    }

    public override bool Draw(UltimaBatcher2D batcher, int x, int y)
    {
        Color backgroundColor = MouseIsOver ? new Color(70, 70, 70, 220) : new Color(40, 40, 40, 220);
        batcher.Draw(
            SolidColorTextureCache.GetTexture(backgroundColor),
            new Rectangle(x, y, Width, Height),
            ShaderHueTranslator.GetHueVector(0)
        );

        Color borderColor = IsLocked ? new Color(220, 120, 30) : new Color(120, 120, 120);
        batcher.Draw(
            SolidColorTextureCache.GetTexture(borderColor),
            new Rectangle(x, y, Width, 1),
            ShaderHueTranslator.GetHueVector(0)
        );
        batcher.Draw(
            SolidColorTextureCache.GetTexture(borderColor),
            new Rectangle(x, y + Height - 1, Width, 1),
            ShaderHueTranslator.GetHueVector(0)
        );
        batcher.Draw(
            SolidColorTextureCache.GetTexture(borderColor),
            new Rectangle(x, y, 1, Height),
            ShaderHueTranslator.GetHueVector(0)
        );
        batcher.Draw(
            SolidColorTextureCache.GetTexture(borderColor),
            new Rectangle(x + Width - 1, y, 1, Height),
            ShaderHueTranslator.GetHueVector(0)
        );

        DrawItemIcon(batcher, x, y);

        if (_hotkeyLabel != null)
        {
            int badgeX = x + Math.Max(0, _hotkeyLabel.X - 2);
            int badgeY = y + Math.Max(0, _hotkeyLabel.Y - 1);
            int badgeW = Math.Min(Width - 2, _hotkeyLabel.Width + 4);
            int badgeH = _hotkeyLabel.Height + 2;

            if (badgeW > 0 && badgeH > 0)
            {
                batcher.Draw(
                    SolidColorTextureCache.GetTexture(new Color(0, 0, 0, 170)),
                    new Rectangle(badgeX, badgeY, badgeW, badgeH),
                    ShaderHueTranslator.GetHueVector(0)
                );
            }
        }

        return base.Draw(batcher, x, y);
    }

    protected override void OnMouseWheel(MouseEventType delta)
    {
        base.OnMouseWheel(delta);

        if (Keyboard.Alt || IsLocked)
        {
            return;
        }

        if (delta == MouseEventType.WheelScrollUp)
        {
            SetSize(_size + 4);
        }
        else if (delta == MouseEventType.WheelScrollDown)
        {
            SetSize(_size - 4);
        }
    }

    protected override void OnMouseUp(int x, int y, MouseButtonType button)
    {
        base.OnMouseUp(x, y, button);

        if (button == MouseButtonType.Right)
        {
            ShowContextMenu();
            return;
        }

        if (button != MouseButtonType.Left)
        {
            return;
        }

        Point offset = Mouse.LDragOffset;

        if (
            Math.Abs(offset.X) >= Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
            || Math.Abs(offset.Y) >= Constants.MIN_PICKUP_DRAG_DISTANCE_PIXELS
        )
        {
            return;
        }

        UsePinnedItem();
    }

    protected override void OnLockedChanged()
    {
        // Keep right click for context menu, even when unlocked.
        CanCloseWithRightClick = false;
    }

    public override void Save(XmlTextWriter writer)
    {
        base.Save(writer);
        writer.WriteAttributeString("graphic", _graphic.ToString());
        writer.WriteAttributeString("hue", _hue.ToString());
        writer.WriteAttributeString("itemSerial", _itemSerial.ToString());
        writer.WriteAttributeString("size", _size.ToString());

        if (!string.IsNullOrEmpty(_macroName))
        {
            writer.WriteAttributeString("macroName", _macroName);
        }
    }

    public override void Restore(XmlElement xml)
    {
        base.Restore(xml);

        if (ushort.TryParse(xml.GetAttribute("graphic"), out ushort graphic))
        {
            _graphic = graphic;
        }

        if (ushort.TryParse(xml.GetAttribute("hue"), out ushort hue))
        {
            _hue = hue;
        }

        if (uint.TryParse(xml.GetAttribute("itemSerial"), out uint itemSerial))
        {
            _itemSerial = itemSerial;
        }

        _size = PinnedItemButtonHelper.ResolveRestoredSize(
            xml.GetAttribute("size"),
            PinnedItemButtonHelper.GetDefaultSizeFromProfile(DefaultSize)
        );

        _macroName = xml.GetAttribute("macroName") ?? string.Empty;

        SetSize(_size, false, false);
        RefreshHotkeyLabel();
    }

    private void DrawItemIcon(UltimaBatcher2D batcher, int x, int y)
    {
        if (_graphic == 0)
        {
            return;
        }

        ref readonly SpriteInfo texture = ref Client.Game.UO.Arts.GetArt(_graphic);

        if (texture.Texture == null)
        {
            return;
        }

        Rectangle realBounds = Client.Game.UO.Arts.GetRealArtBounds(_graphic);

        if (realBounds.Width <= 0 || realBounds.Height <= 0)
        {
            realBounds = new Rectangle(0, 0, texture.UV.Width, texture.UV.Height);
        }

        int padding = 6;
        int maxWidth = Math.Max(1, Width - (padding << 1));
        int maxHeight = Math.Max(1, Height - (padding << 1));

        float scale = Math.Min((float)maxWidth / realBounds.Width, (float)maxHeight / realBounds.Height);
        int drawWidth = Math.Max(1, (int)(realBounds.Width * scale));
        int drawHeight = Math.Max(1, (int)(realBounds.Height * scale));

        int drawX = x + ((Width - drawWidth) >> 1);
        int drawY = y + ((Height - drawHeight) >> 1);

        Rectangle source = new Rectangle(
            texture.UV.X + realBounds.X,
            texture.UV.Y + realBounds.Y,
            realBounds.Width,
            realBounds.Height
        );

        bool isPartialHue = _graphic < Client.Game.UO.FileManager.TileData.StaticData.Length
                            && Client.Game.UO.FileManager.TileData.StaticData[_graphic].IsPartialHue;

        Vector3 hueVector = ShaderHueTranslator.GetHueVector(_hue, isPartialHue, 1f);

        batcher.Draw(texture.Texture, new Rectangle(drawX, drawY, drawWidth, drawHeight), source, hueVector);
    }

    private void SetFromItem(Item item)
    {
        if (item == null)
        {
            return;
        }

        _graphic = item.Graphic;
        _hue = item.Hue;
        _itemSerial = item.Serial;

        EnsureMacroUseTypePattern();
        RefreshHotkeyLabel();
        SetTooltip(item);
    }

    private void UsePinnedItem()
    {
        Item item = PinnedItemButtonHelper.ResolveTargetItem(World, _itemSerial, _graphic, _hue);

        if (item == null)
        {
            return;
        }

        _itemSerial = item.Serial;
        GameActions.DoubleClick(World, item);
    }

    private void SetSize(int size, bool updateProfileDefault = true, bool propagateToLinked = true)
    {
        _size = PinnedItemButtonHelper.ClampSize(size);
        PinnedItemButtonHelper.ApplyManualSize(this, _size);
        GroupMatrixWidth = _size;
        GroupMatrixHeight = _size;

        if (propagateToLinked)
        {
            AnchorManager.AnchorGroup anchorGroup = UIManager.AnchorManager[this];

            if (anchorGroup != null)
            {
                UIManager.ForEach<AnchorableGump>(gump =>
                {
                    if (
                        gump != this
                        && gump is PinnedItemButtonGump linkedPinnedItem
                        && UIManager.AnchorManager[gump] == anchorGroup
                    )
                    {
                        linkedPinnedItem.SetSize(_size, updateProfileDefault: false, propagateToLinked: false);
                    }
                });
            }
        }

        if (updateProfileDefault)
        {
            PinnedItemButtonHelper.UpdateProfileDefaultSize(ProfileManager.CurrentProfile, _size);
        }

        RefreshHotkeyLabel();
        SetInScreen();
    }

    private void ShowContextMenu()
    {
        var contextMenu = new ContextMenuControl(this);

        contextMenu.Add("Use", UsePinnedItem);
        contextMenu.Add("Set Hotkey", OpenHotkeyEditor);
        contextMenu.Add("Clear Hotkey", ClearHotkey);

        var sizeMenu = new System.Collections.Generic.List<ContextMenuItemEntry>
        {
            new("Small", () => SetSize(48)),
            new("Medium", () => SetSize(72)),
            new("Large", () => SetSize(96)),
            new("XL", () => SetSize(120)),
            new("Smaller", () => SetSize(_size - 8)),
            new("Larger", () => SetSize(_size + 8))
        };

        contextMenu.Add("Size", sizeMenu);
        contextMenu.Add(IsLocked ? "Unlock" : "Lock", () => IsLocked = !IsLocked);
        contextMenu.Add("Remove", RemovePinnedButton);
        contextMenu.Show();
    }

    private void OpenHotkeyEditor()
    {
        UIManager.Add(new PinnedItemHotkeyEditorGump(World, this));
    }

    private void ClearHotkey()
    {
        Macro macro = GetAssociatedMacro();

        if (macro == null)
        {
            return;
        }

        macro.Key = SDL.SDL_Keycode.SDLK_UNKNOWN;
        macro.MouseButton = MouseButtonType.None;
        macro.WheelScroll = false;
        macro.WheelUp = false;
        macro.Alt = false;
        macro.Ctrl = false;
        macro.Shift = false;
        macro.ControllerButtons = null;

        RefreshHotkeyLabel();
    }

    private void RemovePinnedButton()
    {
        Macro macro = GetAssociatedMacro();

        if (macro != null)
        {
            World.Macros.Remove(macro);
        }

        Dispose();
    }

    private void EnsureMacroUseTypePattern()
    {
        Macro macro = GetAssociatedMacro();

        if (macro == null)
        {
            return;
        }

        EnsureUseTypeMacroAction(macro);
        macro.Graphic = _graphic;
        macro.Hue = _hue;
    }

    private Macro GetAssociatedMacro()
    {
        if (string.IsNullOrWhiteSpace(_macroName))
        {
            return null;
        }

        return World.Macros.FindMacro(_macroName);
    }

    private Macro GetOrCreateAssociatedMacro()
    {
        Macro macro = GetAssociatedMacro();

        if (macro != null)
        {
            EnsureUseTypeMacroAction(macro);
            return macro;
        }

        macro = new Macro($"PinnedItem_{Guid.NewGuid():N}");
        macro.PushToBack(new MacroObjectString(MacroType.UseType, MacroSubType.MSC_NONE, PinnedItemButtonHelper.BuildUseTypePattern(_graphic, _hue)));
        macro.Graphic = _graphic;
        macro.Hue = _hue;
        World.Macros.PushToBack(macro);
        _macroName = macro.Name;

        return macro;
    }

    private void EnsureUseTypeMacroAction(Macro macro)
    {
        if (macro == null)
        {
            return;
        }

        string pattern = PinnedItemButtonHelper.BuildUseTypePattern(_graphic, _hue);

        if (macro.Items is MacroObjectString action && action.Code == MacroType.UseType)
        {
            action.Text = pattern;
            return;
        }

        macro.Clear();
        macro.PushToBack(new MacroObjectString(MacroType.UseType, MacroSubType.MSC_NONE, pattern));
    }

    private bool AssignHotkey(HotkeyBox hotkeyBox)
    {
        bool shift = (hotkeyBox.Mod & SDL.SDL_Keymod.SDL_KMOD_SHIFT) != SDL.SDL_Keymod.SDL_KMOD_NONE;
        bool alt = (hotkeyBox.Mod & SDL.SDL_Keymod.SDL_KMOD_ALT) != SDL.SDL_Keymod.SDL_KMOD_NONE;
        bool ctrl = (hotkeyBox.Mod & SDL.SDL_Keymod.SDL_KMOD_CTRL) != SDL.SDL_Keymod.SDL_KMOD_NONE;

        Macro current = GetAssociatedMacro();
        Macro conflict = PinnedItemButtonHelper.FindHotkeyConflict(
            World.Macros,
            current,
            hotkeyBox.Key,
            hotkeyBox.MouseButton,
            hotkeyBox.WheelScroll,
            hotkeyBox.WheelUp,
            alt,
            ctrl,
            shift
        );

        if (conflict != null)
        {
            UIManager.Add(
                new MessageBoxGump(
                    World,
                    250,
                    150,
                    string.Format(ResGumps.ThisKeyCombinationAlreadyExists, conflict.Name),
                    null
                )
            );

            return false;
        }

        Macro macro = GetOrCreateAssociatedMacro();
        macro.Key = hotkeyBox.Key;
        macro.MouseButton = hotkeyBox.MouseButton;
        macro.WheelScroll = hotkeyBox.WheelScroll;
        macro.WheelUp = hotkeyBox.WheelUp;
        macro.Shift = shift;
        macro.Alt = alt;
        macro.Ctrl = ctrl;
        macro.ControllerButtons = hotkeyBox.Buttons;
        macro.Graphic = _graphic;
        macro.Hue = _hue;

        EnsureUseTypeMacroAction(macro);
        RefreshHotkeyLabel();

        return true;
    }

    private void RefreshHotkeyLabel()
    {
        _hotkeyLabel?.Dispose();
        _hotkeyLabel = null;
        PinnedItemButtonHelper.DisableAutoSizing(this);

        Macro macro = GetAssociatedMacro();

        if (macro == null)
        {
            return;
        }

        string text = BuildHotkeyText(macro);
        text = PinnedItemButtonHelper.ToReadableHotkey(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _hotkeyLabel = new Label(text, true, 0x0035, 0, 1, FontStyle.BlackBorder)
        {
            AcceptMouseInput = false,
            X = 2,
            Y = 2
        };

        Add(_hotkeyLabel);
        PinnedItemButtonHelper.DisableAutoSizing(this);
    }

    private static string BuildHotkeyText(Macro macro)
    {
        SDL.SDL_Keymod mod = SDL.SDL_Keymod.SDL_KMOD_NONE;

        if (macro.Shift)
        {
            mod |= SDL.SDL_Keymod.SDL_KMOD_SHIFT;
        }

        if (macro.Alt)
        {
            mod |= SDL.SDL_Keymod.SDL_KMOD_ALT;
        }

        if (macro.Ctrl)
        {
            mod |= SDL.SDL_Keymod.SDL_KMOD_CTRL;
        }

        if (macro.Key != SDL.SDL_Keycode.SDLK_UNKNOWN)
        {
            return KeysTranslator.TryGetKey(macro.Key, mod);
        }

        if (macro.MouseButton != MouseButtonType.None)
        {
            return KeysTranslator.GetMouseButton(macro.MouseButton, mod);
        }

        if (macro.WheelScroll)
        {
            return KeysTranslator.GetMouseWheel(macro.WheelUp, mod);
        }

        return string.Empty;
    }

    private sealed class PinnedItemHotkeyEditorGump : Gump
    {
        private readonly PinnedItemButtonGump _owner;
        private readonly HotkeyBox _hotkeyBox;

        public PinnedItemHotkeyEditorGump(World world, PinnedItemButtonGump owner) : base(world, 0, 0)
        {
            _owner = owner;
            Width = 240;
            Height = 85;
            CanMove = true;
            AcceptMouseInput = true;
            AcceptKeyboardInput = true;
            CanCloseWithRightClick = true;
            IsModal = true;
            ModalClickOutsideAreaClosesThisControl = true;

            Add(new AlphaBlendControl(0.85f) { Width = Width, Height = Height });
            Add(new Label("Set hotkey for pinned item", true, 0x0481) { X = 12, Y = 10 });

            _hotkeyBox = new HotkeyBox { X = 12, Y = 35 };
            _hotkeyBox.HotkeyChanged += OnHotkeyChanged;
            _hotkeyBox.HotkeyCancelled += (_, _) => Dispose();
            Add(_hotkeyBox);

            Point parentPosition = owner.Location;
            X = parentPosition.X + owner.Width + 8;
            Y = parentPosition.Y;
            SetInScreen();
        }

        public override bool ShouldBeSaved => false;

        private void OnHotkeyChanged(object sender, EventArgs e)
        {
            if (_owner.AssignHotkey(_hotkeyBox))
            {
                Dispose();
            }
        }
    }
}
