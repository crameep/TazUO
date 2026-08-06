// SPDX-License-Identifier: BSD-2-Clause

#nullable enable
using System;
using System.Globalization;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

internal static class TomeTabContent
{
    public static Widget Build()
    {
        var listPanel = new VerticalStackPanel { Spacing = 4 };
        var detailsPanel = new VerticalStackPanel { Spacing = 5 };
        TomeDefinition? selected = null;

        string GetUniqueName(string baseName)
        {
            string name = baseName;
            int suffix = 2;
            while (TomeManager.Instance.TomeDefinitions.Exists(tome =>
                       string.Equals(tome.Name, name, StringComparison.OrdinalIgnoreCase)))
                name = $"{baseName} ({suffix++})";
            return name;
        }

        void Save() => TomeManager.Instance?.Save();

        void BuildList()
        {
            listPanel.Widgets.Clear();
            if (TomeManager.Instance == null)
            {
                listPanel.Widgets.Add(new MyraLabel("Tome manager is not loaded.", MyraLabel.TextStyle.P));
                return;
            }

            var actions = new HorizontalStackPanel { Spacing = 4 };
            actions.Widgets.Add(new MyraButton("Add Tome", () =>
            {
                selected = new TomeDefinition { Name = GetUniqueName("Tome") };
                TomeManager.Instance.TomeDefinitions.Add(selected);
                Save();
                BuildList();
                BuildDetails();
            }));
            actions.Widgets.Add(new MyraButton("Import", () =>
            {
                string? json = Clipboard.GetClipboardText();
                if (json.NotNullNotEmpty() && TomeManager.Instance.ImportFromJson(json))
                {
                    BuildList();
                    GameActions.Print("Imported tome definition(s).", Constants.HUE_SUCCESS);
                }
                else
                    GameActions.Print("Clipboard does not contain a valid tome export.", Constants.HUE_ERROR);
            }));
            actions.Widgets.Add(new MyraButton("Export All", () =>
            {
                TomeManager.Instance.GetJsonExportAll()?.CopyToClipboard();
                GameActions.Print("Exported all tome definitions.", Constants.HUE_SUCCESS);
            }));
            listPanel.Widgets.Add(actions);

            foreach (TomeDefinition tome in TomeManager.Instance.TomeDefinitions)
            {
                TomeDefinition captured = tome;
                listPanel.Widgets.Add(new MyraButton(tome.Name, () =>
                {
                    selected = captured;
                    BuildDetails();
                }));
            }
        }

        void BuildDetails()
        {
            detailsPanel.Widgets.Clear();
            if (selected == null)
            {
                detailsPanel.Widgets.Add(new MyraLabel("Select a tome definition to edit.", MyraLabel.TextStyle.P));
                return;
            }

            var name = new MyraInputBox { Text = selected.Name, MinWidth = 220 };
            name.TextChangedByUser += (_, _) =>
            {
                selected.Name = name.Text;
                Save();
            };
            detailsPanel.Widgets.Add(new MyraLabel("Name:", MyraLabel.TextStyle.P));
            detailsPanel.Widgets.Add(name);

            var tomeRow = new HorizontalStackPanel { Spacing = 5 };
            tomeRow.Widgets.Add(new MyraLabel($"Tome: 0x{selected.TomeSerial:X8}", MyraLabel.TextStyle.P));
            tomeRow.Widgets.Add(new MyraButton("Target Tome", () =>
            {
                World.Instance.TargetManager.SetTargeting(targeted =>
                {
                    if (targeted is not Entity entity || !SerialHelper.IsItem(entity))
                    {
                        GameActions.Print("Only item tomes can be selected.", Constants.HUE_ERROR);
                        return;
                    }

                    selected.TomeSerial = entity.Serial;
                    Save();
                    BuildDetails();
                });
            }));
            detailsPanel.Widgets.Add(tomeRow);

            var mappingRow = new HorizontalStackPanel { Spacing = 5 };
            var gumpId = new MyraInputBox
            {
                Text = selected.GumpId == 0 ? "" : $"0x{selected.GumpId:X8}",
                HintText = "Gump ID",
                MinWidth = 120
            };
            gumpId.TextChangedByUser += (_, _) =>
            {
                if (!TryParseUInt(gumpId.Text, out uint value)) return;
                selected.GumpId = value;
                Save();
            };
            var buttonId = new MyraInputBox
            {
                Text = selected.AddButtonId.ToString(CultureInfo.InvariantCulture),
                HintText = "Button ID",
                MinWidth = 90
            };
            buttonId.TextChangedByUser += (_, _) =>
            {
                if (!int.TryParse(buttonId.Text, out int value)) return;
                selected.AddButtonId = value;
                Save();
            };
            mappingRow.Widgets.Add(gumpId);
            mappingRow.Widgets.Add(buttonId);
            mappingRow.Widgets.Add(new MyraButton("Use Last Clicked Button", () =>
            {
                if (GumpButtonCapture.Sequence == 0)
                {
                    GameActions.Print("Click the desired button on the tome gump first.", Constants.HUE_ERROR);
                    return;
                }

                selected.GumpId = GumpButtonCapture.LastGumpId;
                selected.AddButtonId = GumpButtonCapture.LastButtonId;
                Save();
                BuildDetails();
                GameActions.Print(
                    $"Captured gump 0x{selected.GumpId:X8}, button {selected.AddButtonId}.",
                    Constants.HUE_SUCCESS
                );
            }) { Tooltip = "Open the tome, click its add/fill button, then use this action." });
            detailsPanel.Widgets.Add(new MyraLabel("Gump mapping:", MyraLabel.TextStyle.P));
            detailsPanel.Widgets.Add(mappingRow);

            var modeCombo = new ComboView { MinWidth = 180 };
            foreach (TomeMode mode in Enum.GetValues<TomeMode>())
                modeCombo.ListView.Widgets.Add(new Label { Text = mode.ToString() });
            modeCombo.ListView.SelectedIndex = (int)selected.Mode;
            modeCombo.ListView.SelectedIndexChanged += (_, _) =>
            {
                int? index = modeCombo.ListView.SelectedIndex;
                if (!index.HasValue) return;
                selected.Mode = (TomeMode)index.Value;
                Save();
                BuildDetails();
            };
            detailsPanel.Widgets.Add(new MyraLabel("Mode:", MyraLabel.TextStyle.P));
            detailsPanel.Widgets.Add(modeCombo);

            if (selected.Mode == TomeMode.TargetContainer)
            {
                var targetRow = new HorizontalStackPanel { Spacing = 5 };
                targetRow.Widgets.Add(new MyraLabel(
                    selected.TargetSerial == 0 ? "Target: self" : $"Target: 0x{selected.TargetSerial:X8}",
                    MyraLabel.TextStyle.P
                ));
                targetRow.Widgets.Add(new MyraButton("Target Container", () =>
                {
                    World.Instance.TargetManager.SetTargeting(targeted =>
                    {
                        if (targeted is not Entity entity || !SerialHelper.IsItem(entity)) return;
                        selected.TargetSerial = entity.Serial;
                        Save();
                        BuildDetails();
                    });
                }));
                targetRow.Widgets.Add(new MyraButton("Use Self", () =>
                {
                    selected.TargetSerial = 0;
                    Save();
                    BuildDetails();
                }));
                detailsPanel.Widgets.Add(targetRow);
            }

            var behaviorRow = new HorizontalStackPanel { Spacing = 5 };
            var delay = new SpinButton
            {
                Integer = true,
                Minimum = 0,
                Maximum = 60000,
                Value = selected.Delay,
                MinWidth = 90
            };
            delay.ValueChangedByUser += (_, _) =>
            {
                selected.Delay = Math.Max(0, (int)(delay.Value ?? 1000));
                Save();
            };
            behaviorRow.Widgets.Add(new MyraLabel("Delay (ms):", MyraLabel.TextStyle.P));
            behaviorRow.Widgets.Add(delay);
            behaviorRow.Widgets.Add(MyraCheckButton.CreateWithCallback(
                selected.RequiresWalk,
                requiresWalk =>
                {
                    selected.RequiresWalk = requiresWalk;
                    Save();
                },
                "Walk to tome"
            ));
            detailsPanel.Widgets.Add(behaviorRow);

            var actions = new HorizontalStackPanel { Spacing = 5 };
            actions.Widgets.Add(new MyraButton("Export", () =>
            {
                TomeManager.Instance.GetJsonExport(selected)?.CopyToClipboard();
                GameActions.Print("Exported tome definition.", Constants.HUE_SUCCESS);
            }));
            actions.Widgets.Add(MyraStyle.ApplyButtonDangerStyle(new MyraButton("Delete", () =>
            {
                TomeManager.Instance.TomeDefinitions.Remove(selected);
                selected = null;
                Save();
                BuildList();
                BuildDetails();
            })));
            detailsPanel.Widgets.Add(actions);
        }

        BuildList();
        BuildDetails();
        var root = new HorizontalStackPanel { Spacing = MyraStyle.STANDARD_SPACING };
        root.Widgets.Add(new ScrollViewer { Width = 280, MaxHeight = 500, Content = listPanel });
        root.Widgets.Add(new ScrollViewer { MinWidth = 500, MaxHeight = 500, Content = detailsPanel });
        return root;
    }

    private static bool TryParseUInt(string value, out uint result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string text = value.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result)
            : uint.TryParse(text, out result);
    }
}
