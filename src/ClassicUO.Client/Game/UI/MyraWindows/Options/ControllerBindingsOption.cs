#nullable enable

using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers.Hotkeys;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Options;

/// <summary>
/// Editable list of controller button bindings for the options window.
/// </summary>
/// <remarks>
/// The assistant's hotkey tab lists every registered hotkey and stays the complete view. This is a
/// controller-only view of the same <see cref="HotKeyEntry"/> objects, so edits made in either place
/// are the same edit; a player setting up a pad should not have to leave the options window.
/// </remarks>
public static class ControllerBindingsOption
{
    // Presentation order, most-used first. Anything registered later falls in behind these.
    private static readonly string[] _order =
    {
        HotKeyRegistrar.ControllerRadialId,
        HotKeyRegistrar.ControllerClickLeftId,
        HotKeyRegistrar.ControllerClickRightId,
        HotKeyRegistrar.ControllerUiUpId,
        HotKeyRegistrar.ControllerUiDownId,
        HotKeyRegistrar.ControllerUiLeftId,
        HotKeyRegistrar.ControllerUiRightId,
        HotKeyRegistrar.ControllerTargetNextId,
        HotKeyRegistrar.ControllerTargetPrevId,
        HotKeyRegistrar.ControllerTargetFilterId,
        HotKeyRegistrar.ControllerTargetConfirmId,
        HotKeyRegistrar.ControllerTargetCancelId
    };

    private static HotkeyCaptureWindow? _captureWindow;

    public static Widget Build()
    {
        var root = new VerticalStackPanel { Spacing = 4 };

        Rebuild(root);

        return root;
    }

    private static void Rebuild(VerticalStackPanel root)
    {
        root.Widgets.Clear();

        List<HotKeyEntry> entries = HotKeys.AllRegistered()
            .Where(e => e.Category == HotKeyRegistrar.ControllerCategory)
            .OrderBy(e =>
            {
                int index = System.Array.IndexOf(_order, e.Id);
                return index < 0 ? _order.Length : index;
            })
            .ThenBy(e => e.Name)
            .ToList();

        // Hotkeys only register once a game scene loads, so this list is empty at the login screen.
        if (entries.Count == 0)
        {
            root.Widgets.Add(new MyraLabel(
                TazLang.Get("mog_movementtab_controller_bindings_ingame"),
                MyraLabel.TextStyle.P));

            return;
        }

        var grid = new MyraGrid { MaxWidth = 640 };
        grid.SetupWithHeaders(
            GridColumnInfo.Auto(TazLang.Get("mog_movementtab_controller_bindings_action")),
            GridColumnInfo.Auto(TazLang.Get("mog_movementtab_controller_bindings_button")),
            GridColumnInfo.Auto(""),
            GridColumnInfo.Auto("")
        );

        int row = 1;

        foreach (HotKeyEntry entry in entries)
        {
            HotKeyEntry local = entry;

            grid.AddWidget(new MyraLabel(local.Name, MyraLabel.TextStyle.P), row, 0);
            grid.AddWidget(new MyraLabel((local.Binding ?? new HotkeyBinding()).Describe(), MyraLabel.TextStyle.P), row, 1);

            grid.AddWidget(new MyraButton(
                TazLang.Get("mog_movementtab_controller_bindings_set"),
                () => StartCapture(root, local)), row, 2);

            grid.AddWidget(new MyraButton(
                TazLang.Get("mog_movementtab_controller_bindings_reset"),
                () => { local.ResetToDefault(); Rebuild(root); }), row, 3);

            row++;
        }

        root.Widgets.Add(grid);
    }

    private static void StartCapture(VerticalStackPanel root, HotKeyEntry entry)
    {
        if (_captureWindow is { IsDisposed: false })
        {
            _captureWindow.BringOnTop();

            return;
        }

        _captureWindow = new HotkeyCaptureWindow(
            prompt: entry.Name,
            existing: entry.Binding,
            onSaved: binding =>
            {
                entry.Binding = binding;
                HotKeys.Save();
                Rebuild(root);
            },
            capturesMouseEvents: false);
    }
}
