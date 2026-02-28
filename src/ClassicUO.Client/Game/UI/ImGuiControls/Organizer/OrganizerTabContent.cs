using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Utility;
using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class OrganizerTabContent : TabContent
    {
        private int _selectedConfigIndex = -1;
        private OrganizerConfig _selectedConfig;
        private string _addItemGraphicInput = string.Empty;
        private string _addItemHueInput = string.Empty;
        private bool _showAddItemManual;
        private readonly Dictionary<string, bool> _groupExpanded = new(StringComparer.OrdinalIgnoreCase);

        public override void DrawContent()
        {
            if (OrganizerAgent.Instance == null)
            {
                ImGui.Text("Organizer Agent not loaded");
                return;
            }

            if (ImGui.BeginTable("OrganizerTable", 2, ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Organizers", ImGuiTableColumnFlags.WidthFixed, 300);
                ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                DrawOrganizerList();

                ImGui.TableSetColumnIndex(1);
                DrawOrganizerDetails();

                ImGui.EndTable();
            }
        }

        private void DrawOrganizerList()
        {
            ImGui.Separator();

            if (ImGui.Button("Add Organizer"))
            {
                OrganizerConfig newConfig = OrganizerAgent.Instance.NewOrganizerConfig();
                _selectedConfigIndex = OrganizerAgent.Instance.OrganizerConfigs.IndexOf(newConfig);
                _selectedConfig = newConfig;
            }

            ImGui.SeparatorText("Groups");

            var grouped = OrganizerAgent.Instance.OrganizerConfigs
                .Select((config, index) => new ConfigListRow(config, index))
                .GroupBy(row => OrganizerAgent.NormalizeGroup(row.Config.Group), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (IGrouping<string, ConfigListRow> group in grouped)
            {
                if (!_groupExpanded.ContainsKey(group.Key))
                    _groupExpanded[group.Key] = true;

                bool expanded = _groupExpanded[group.Key];
                string prefix = expanded ? "[-]" : "[+]";

                if (ImGui.Selectable($"{prefix} {group.Key}##group_{group.Key}", false))
                    _groupExpanded[group.Key] = !expanded;

                if (ImGui.BeginPopupContextItem($"group_ctx_{group.Key}"))
                {
                    if (ImGui.MenuItem("Enable All"))
                        SetGroupEnabled(group.Key, true);

                    if (ImGui.MenuItem("Disable All"))
                        SetGroupEnabled(group.Key, false);

                    if (ImGui.MenuItem("Run Group"))
                        OrganizerAgent.Instance.RunOrganizerGroup(group.Key);

                    ImGui.EndPopup();
                }

                if (!_groupExpanded[group.Key])
                    continue;

                foreach (ConfigListRow row in group.OrderBy(x => x.Config.Name, StringComparer.OrdinalIgnoreCase))
                {
                    bool isSelected = row.Index == _selectedConfigIndex;
                    string label = $"    {row.Config.Name}##Config{row.Index}";

                    bool isRunning = OrganizerAgent.Instance.RunState.IsRunning
                                     && string.Equals(OrganizerAgent.Instance.RunState.ConfigName, row.Config.Name, StringComparison.OrdinalIgnoreCase);

                    if (isRunning)
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.8f, 1.0f, 1.0f));
                    else if (row.Config.Enabled)
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 1.0f, 0.0f, 1.0f));

                    if (ImGui.Selectable(label, isSelected))
                    {
                        _selectedConfigIndex = row.Index;
                        _selectedConfig = row.Config;
                    }

                    if (isRunning || row.Config.Enabled)
                        ImGui.PopStyleColor();
                }
            }
        }

        private void SetGroupEnabled(string groupName, bool enabled)
        {
            foreach (OrganizerConfig config in OrganizerAgent.Instance.OrganizerConfigs)
            {
                if (string.Equals(OrganizerAgent.NormalizeGroup(config.Group), groupName, StringComparison.OrdinalIgnoreCase))
                    config.Enabled = enabled;
            }
        }

        private void DrawOrganizerDetails()
        {
            if (_selectedConfig == null || _selectedConfigIndex == -1)
            {
                ImGui.Text("Select an organizer to view details");
                return;
            }

            DrawProgress();

            bool enabled = _selectedConfig.Enabled;
            if (ImGui.Checkbox("Enabled", ref enabled))
                _selectedConfig.Enabled = enabled;

            string name = _selectedConfig.Name;
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("Name", ref name, 100))
                _selectedConfig.Name = name;

            string group = _selectedConfig.Group;
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("Group", ref group, 100))
                _selectedConfig.Group = group;

            bool recursive = _selectedConfig.Recursive;
            if (ImGui.Checkbox("Recursive", ref recursive))
                _selectedConfig.Recursive = recursive;

            if (ImGui.Button("Run Organizer"))
                OrganizerAgent.Instance?.RunOrganizer(_selectedConfig.Name);

            ImGui.SameLine();
            if (ImGui.Button("Duplicate"))
            {
                OrganizerConfig duplicated = OrganizerAgent.Instance?.DupeConfig(_selectedConfig);
                if (duplicated != null)
                {
                    _selectedConfigIndex = OrganizerAgent.Instance.OrganizerConfigs.IndexOf(duplicated);
                    _selectedConfig = duplicated;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Create Macro"))
            {
                OrganizerAgent.Instance?.CreateOrganizerMacroButton(_selectedConfig.Name);
                GameActions.Print($"Created Organizer Macro: {_selectedConfig.Name}");
            }

            ImGui.SameLine();
            if (ImGui.Button("Import"))
            {
                string json = Clipboard.GetClipboardText();

                if (json.NotNullNotEmpty() && OrganizerAgent.Instance.ImportFromJson(json))
                    return;

                GameActions.Print("Your clipboard does not have a valid export copied.", Constants.HUE_ERROR);
            }

            ImGui.SameLine();
            if (ImGui.Button("Export"))
            {
                OrganizerAgent.Instance.GetJsonExport(_selectedConfig)?.CopyToClipboard();
                GameActions.Print("Exported organizer to your clipboard!", Constants.HUE_SUCCESS);
            }

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
            if (ImGui.Button("Delete"))
            {
                OrganizerAgent.Instance?.DeleteConfig(_selectedConfig);
                _selectedConfig = null;
                _selectedConfigIndex = -1;
            }

            ImGui.PopStyleColor();

            DrawContainerSettings();

            ImGui.NewLine();
            DrawItemsSection();
        }

        private void DrawProgress()
        {
            OrganizerRunState runState = OrganizerAgent.Instance.RunState;
            if (!runState.IsRunning)
                return;

            int completed = runState.ItemsMoved + runState.ItemsSkipped;
            float progress = runState.TotalItems <= 0 ? 0f : Math.Clamp((float)completed / runState.TotalItems, 0f, 1f);

            ImGui.Text($"Organizing '{runState.ConfigName}': {runState.ItemsMoved}/{runState.TotalItems} moved ({runState.ItemsSkipped} skipped)");
            ImGui.ProgressBar(progress, new Vector2(-1, 0));
            ImGui.Separator();
        }

        private void DrawContainerSettings()
        {
            ImGui.SeparatorText("Container Settings");

            if (ImGui.Button("Set Source Container"))
            {
                GameActions.Print("Select [SOURCE] Container", 82);
                World.Instance.TargetManager.SetTargeting(source =>
                {
                    if (source is not Entity sourceEntity || !SerialHelper.IsItem(sourceEntity))
                    {
                        GameActions.Print("Only items can be selected!");
                        return;
                    }

                    _selectedConfig.SourceContSerial = sourceEntity.Serial;
                    GameActions.Print($"Source container set to 0x{sourceEntity.Serial:X4} ({sourceEntity.Name})", Constants.HUE_SUCCESS);
                });
            }

            DrawConfigDestinationControls();

            if (_selectedConfig.SourceContSerial != 0)
                ImGui.Text($"Source: (0x{_selectedConfig.SourceContSerial:X4})");
            else
                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Source: Your backpack");
        }

        private void DrawConfigDestinationControls()
        {
            DestType uiType = _selectedConfig.DestinationType == DestType.Tome ? DestType.Tome : DestType.Container;

            ImGui.Text("Default Destination:");
            ImGui.SameLine();
            if (ImGui.BeginCombo("##cfg_dest_type", uiType == DestType.Tome ? "Tome" : "Container"))
            {
                if (ImGui.Selectable("Container", uiType == DestType.Container))
                    _selectedConfig.DestinationType = DestType.Container;

                if (ImGui.Selectable("Tome", uiType == DestType.Tome))
                    _selectedConfig.DestinationType = DestType.Tome;

                ImGui.EndCombo();
            }

            if (_selectedConfig.DestinationType == DestType.Tome)
            {
                string selectedTome = _selectedConfig.TomeDefinitionName;
                DrawTomeSelector("##cfg_tome", ref selectedTome);
                _selectedConfig.TomeDefinitionName = selectedTome;
            }
            else
            {
                ImGui.SameLine();
                if (ImGui.Button("Set Destination"))
                {
                    GameActions.Print("Select [DESTINATION] Container or Pack Animal", 82);
                    World.Instance.TargetManager.SetTargeting(destination =>
                    {
                        if (destination is not Entity destEntity || (!SerialHelper.IsItem(destEntity) && !SerialHelper.IsMobile(destEntity)))
                        {
                            GameActions.Print("Select a container or pack animal!");
                            return;
                        }

                        _selectedConfig.DestContSerial = destEntity.Serial;
                        string label = SerialHelper.IsMobile(destEntity) ? "Pack animal" : "Container";
                        GameActions.Print($"Destination set to {label}: 0x{destEntity.Serial:X4} ({destEntity.Name})", Constants.HUE_SUCCESS);
                    });
                }

                string destLabel = _selectedConfig.DestContSerial != 0
                    ? GetDestinationLabel(_selectedConfig.DestContSerial)
                    : "Not set";

                ImGui.SameLine();
                ImGui.Text($"Current: {destLabel}");
            }
        }

        private void DrawItemsSection()
        {
            ImGui.SeparatorText("Items to Organize");

            if (ImGui.Button("Target Item to Add"))
            {
                World.Instance.TargetManager.SetTargeting(obj =>
                {
                    if (obj is not Entity objEntity || !SerialHelper.IsItem(objEntity))
                    {
                        GameActions.Print("Only items can be added!");
                        return;
                    }

                    OrganizerItemConfig newItemConfig = _selectedConfig.NewItemConfig();
                    newItemConfig.Graphic = objEntity.Graphic;
                    newItemConfig.Hue = objEntity.Hue;
                    newItemConfig.Name = objEntity.Name ?? string.Empty;

                    GameActions.Print($"Added item: Graphic {objEntity.Graphic:X}, Hue {objEntity.Hue:X}");
                });
            }

            ImGui.SameLine();
            if (ImGui.Button("Scan Container"))
            {
                GameActions.Print("Select a container to scan", 82);
                World.Instance.TargetManager.SetTargeting(target =>
                {
                    if (target is not Entity entity || !SerialHelper.IsItem(entity))
                    {
                        GameActions.Print("Only containers can be scanned.", Constants.HUE_ERROR);
                        return;
                    }

                    int added = OrganizerAgent.Instance.ScanContainerIntoConfig(_selectedConfig, entity.Serial);
                    GameActions.Print($"Added {added} item types from container.", Constants.HUE_SUCCESS);
                });
            }

            ImGui.SameLine();
            if (ImGui.Button("Add Item Manually"))
                _showAddItemManual = !_showAddItemManual;

            DrawManualAddSection();

            DrawItemsTable();
        }

        private void DrawManualAddSection()
        {
            if (!_showAddItemManual)
                return;

            ImGui.SeparatorText("Manual Entry");

            ImGui.Text("Graphic:");
            ImGui.SetNextItemWidth(110);
            ImGui.InputText("##graphic", ref _addItemGraphicInput, 16);

            ImGui.Text("Hue:");
            ImGui.SetNextItemWidth(110);
            ImGui.InputText("##hue", ref _addItemHueInput, 10);

            if (ImGui.Button("Add"))
            {
                if (TryParseGraphic(_addItemGraphicInput, out int graphic))
                {
                    OrganizerItemConfig newItemConfig = _selectedConfig.NewItemConfig();
                    newItemConfig.Graphic = graphic;
                    newItemConfig.Hue = TryParseHue(_addItemHueInput, out ushort hue) ? hue : ushort.MaxValue;
                    _addItemGraphicInput = string.Empty;
                    _addItemHueInput = string.Empty;
                    _showAddItemManual = false;
                }
                else
                {
                    GameActions.Print("Invalid graphic value.", Constants.HUE_ERROR);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _addItemGraphicInput = string.Empty;
                _addItemHueInput = string.Empty;
                _showAddItemManual = false;
            }
        }

        private void DrawItemsTable()
        {
            if (!ImGui.BeginTable("ItemsTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, ImGuiTheme.Dimensions.STANDARD_TABLE_SCROLL_HEIGHT)))
                return;

            ImGui.TableSetupColumn("Graphic", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Hue", ImGuiTableColumnFlags.WidthFixed, 65);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Regex", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 65);
            ImGui.TableSetupColumn("Destination", ImGuiTableColumnFlags.WidthFixed, 170);
            ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("Del", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableHeadersRow();

            for (int i = _selectedConfig.ItemConfigs.Count - 1; i >= 0; i--)
            {
                OrganizerItemConfig itemConfig = _selectedConfig.ItemConfigs[i];
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                bool drewArt = itemConfig.Graphic >= 0
                               && itemConfig.Graphic <= ushort.MaxValue
                               && DrawArt((ushort)itemConfig.Graphic, new Vector2(50, 50));

                if (!drewArt)
                    ImGui.Text(itemConfig.Graphic < 0 ? "ANY" : $"{itemConfig.Graphic:X4}");

                ImGui.TableSetColumnIndex(1);
                string hueText = itemConfig.Hue == ushort.MaxValue ? "ANY" : $"0x{itemConfig.Hue:X4}";
                if (ImGui.InputText($"##Hue{i}", ref hueText, 12) && TryParseHue(hueText, out ushort hue))
                    itemConfig.Hue = hue;

                ImGui.TableSetColumnIndex(2);
                string name = itemConfig.Name;
                if (ImGui.InputText($"##Name{i}", ref name, 64))
                    itemConfig.Name = name;

                ImGui.TableSetColumnIndex(3);
                string regex = itemConfig.RegexSearch;
                if (ImGui.InputText($"##Regex{i}", ref regex, 80))
                    itemConfig.RegexSearch = regex;

                ImGui.TableSetColumnIndex(4);
                int amount = itemConfig.Amount;
                if (ImGui.InputInt($"##Amount{i}", ref amount, 0, 0))
                    itemConfig.Amount = (ushort)Math.Max(0, Math.Min(65535, amount));

                ImGui.TableSetColumnIndex(5);
                DrawItemDestinationEditor(itemConfig, i);

                ImGui.TableSetColumnIndex(6);
                bool enabled = itemConfig.Enabled;
                if (ImGui.Checkbox($"##Enabled{i}", ref enabled))
                    itemConfig.Enabled = enabled;

                ImGui.TableSetColumnIndex(7);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
                if (ImGui.Button($"X##Delete{i}"))
                    _selectedConfig.DeleteItemConfig(itemConfig);

                ImGui.PopStyleColor();
            }

            ImGui.EndTable();
        }

        private void DrawItemDestinationEditor(OrganizerItemConfig itemConfig, int rowIndex)
        {
            string comboLabel = GetItemDestinationLabel(itemConfig);
            if (ImGui.BeginCombo($"##dest_{rowIndex}", comboLabel))
            {
                if (ImGui.Selectable("Config Default", itemConfig.DestinationType == DestType.ConfigDefault))
                {
                    itemConfig.DestinationType = DestType.ConfigDefault;
                    itemConfig.TomeDefinitionName = string.Empty;
                }

                if (ImGui.Selectable("Container", itemConfig.DestinationType == DestType.Container))
                {
                    itemConfig.DestinationType = DestType.Container;
                    itemConfig.TomeDefinitionName = string.Empty;
                }

                if (ImGui.BeginMenu("Tomes"))
                {
                    foreach (TomeDefinition tome in TomeManager.Instance?.TomeDefinitions ?? Enumerable.Empty<TomeDefinition>())
                    {
                        bool selected = itemConfig.DestinationType == DestType.Tome
                                        && string.Equals(itemConfig.TomeDefinitionName, tome.Name, StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(tome.Name, selected))
                        {
                            itemConfig.DestinationType = DestType.Tome;
                            itemConfig.TomeDefinitionName = tome.Name;
                        }
                    }

                    ImGui.EndMenu();
                }

                ImGui.EndCombo();
            }

            if (itemConfig.DestinationType == DestType.Container)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"Set##SetDest{rowIndex}"))
                {
                    OrganizerItemConfig currentItemConfig = itemConfig;
                    GameActions.Print("Select [DESTINATION] Container or Pack Animal", 82);
                    World.Instance.TargetManager.SetTargeting(destination =>
                    {
                        if (destination is not Entity destEntity || (!SerialHelper.IsItem(destEntity) && !SerialHelper.IsMobile(destEntity)))
                        {
                            GameActions.Print("Select a container or pack animal!");
                            return;
                        }

                        currentItemConfig.DestContSerial = destEntity.Serial;
                        GameActions.Print($"Per-item destination set to 0x{destEntity.Serial:X4}", Constants.HUE_SUCCESS);
                    });
                }
            }
        }

        private static string GetItemDestinationLabel(OrganizerItemConfig itemConfig)
        {
            return itemConfig.DestinationType switch
            {
                DestType.Container => itemConfig.DestContSerial == 0 ? "Container" : $"Container: 0x{itemConfig.DestContSerial:X4}",
                DestType.Tome => string.IsNullOrWhiteSpace(itemConfig.TomeDefinitionName) ? "Tome" : itemConfig.TomeDefinitionName,
                _ => "Config Default"
            };
        }

        private static bool TryParseGraphic(string input, out int graphic)
        {
            if (string.Equals(input?.Trim(), "ANY", StringComparison.OrdinalIgnoreCase) || input?.Trim() == "-1")
            {
                graphic = -1;
                return true;
            }

            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out graphic))
                return true;

            string text = input?.Trim();
            if (text != null && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out graphic);

            return int.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out graphic);
        }

        private static bool TryParseHue(string input, out ushort hue)
        {
            if (string.IsNullOrWhiteSpace(input)
                || string.Equals(input.Trim(), "ANY", StringComparison.OrdinalIgnoreCase)
                || input.Trim() == "-1")
            {
                hue = ushort.MaxValue;
                return true;
            }

            string text = input.Trim();

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ushort.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hue);

            if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hue))
                return true;

            return ushort.TryParse(text, out hue);
        }

        private void DrawTomeSelector(string id, ref string selectedTome)
        {
            string label = string.IsNullOrWhiteSpace(selectedTome) ? "Select Tome" : selectedTome;
            ImGui.SameLine();

            if (!ImGui.BeginCombo(id, label))
                return;

            foreach (TomeDefinition tome in TomeManager.Instance?.TomeDefinitions ?? Enumerable.Empty<TomeDefinition>())
            {
                bool selected = string.Equals(selectedTome, tome.Name, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(tome.Name, selected))
                    selectedTome = tome.Name;
            }

            ImGui.EndCombo();
        }

        private static string GetDestinationLabel(uint serial)
        {
            if (serial == 0)
                return "Not set";

            Mobile mob = World.Instance?.Mobiles.Get(serial);
            if (mob != null)
                return $"{mob.Name ?? "Pack Animal"} (0x{serial:X4})";

            Item item = World.Instance?.Items.Get(serial);
            if (item != null)
                return $"{item.Name ?? "Container"} (0x{serial:X4})";

            return $"0x{serial:X4}";
        }

        private readonly record struct ConfigListRow(OrganizerConfig Config, int Index);
    }
}
