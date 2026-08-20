#nullable enable

using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Options;

/// <summary>Assigns a macro to each slot of the controller radial menu.</summary>
public static class RadialMenuOption
{
    // Slot order matches the ring: slot 0 at the top, running clockwise.
    private static readonly string[] _slotNames =
    {
        "Up", "Up-Right", "Right", "Down-Right", "Down", "Down-Left", "Left", "Up-Left"
    };

    public static Widget Build()
    {
        var root = new VerticalStackPanel { Spacing = 4 };

        List<string> macroNames = World.Instance?.Macros?.GetAllMacros()
            .Select(m => m.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n)
            .ToList() ?? new List<string>();

        if (macroNames.Count == 0)
        {
            root.Widgets.Add(new MyraLabel(
                TazLang.Get("mog_movementtab_controller_radial_nomacros"),
                MyraLabel.TextStyle.P));

            return root;
        }

        var grid = new MyraGrid { MaxWidth = 640 };
        grid.SetupWithHeaders(
            GridColumnInfo.Auto(TazLang.Get("mog_movementtab_controller_radial_direction")),
            GridColumnInfo.Auto(TazLang.Get("mog_movementtab_controller_radial_macro"))
        );

        string none = TazLang.Get("mog_movementtab_controller_radial_none");

        for (int slot = 0; slot < RadialMenuManager.SLOT_COUNT; slot++)
        {
            int local = slot;

            grid.AddWidget(new MyraLabel(_slotNames[slot], MyraLabel.TextStyle.P), slot + 1, 0);

            var combo = new ComboBox { Width = 220 };

            combo.Items.Add(new ListItem(none));

            foreach (string name in macroNames)
            {
                combo.Items.Add(new ListItem(name));
            }

            string current = RadialMenuManager.GetSlot(slot) ?? string.Empty;
            int index = macroNames.IndexOf(current);

            combo.SelectedIndex = index >= 0 ? index + 1 : 0;

            combo.SelectedIndexChanged += (_, _) =>
            {
                int selected = combo.SelectedIndex ?? 0;

                RadialMenuManager.SetSlot(local, selected <= 0 ? null : macroNames[selected - 1]);
            };

            grid.AddWidget(combo, slot + 1, 1);
        }

        root.Widgets.Add(grid);

        return root;
    }
}
