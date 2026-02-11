using ImGuiNET;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.GameObjects;
using ClassicUO.Utility;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class AutoLootTabContent : TabContent
    {
        private Profile profile;
        private bool enableAutoLoot, enableScavenger, enableProgressBar, autoLootHumanCorpses, hueAfterProcess;

        private string newGraphicInput = "";
        private string newHueInput = "";
        private string newRegexInput = "";

        private AutoLootManager.AutoLootProfile _selectedProfile;
        private AutoLootManager.AutoLootProfile _contextMenuProfile;
        private int _selectedProfileIndex = -1;
        private bool showAddEntry = false;
        private Dictionary<string, string> entryGraphicInputs = new Dictionary<string, string>();
        private Dictionary<string, string> entryHueInputs = new Dictionary<string, string>();
        private Dictionary<string, string> entryRegexInputs = new Dictionary<string, string>();
        private Dictionary<string, string> entryDestinationInputs = new Dictionary<string, string>();
        private bool showCharacterImportPopup = false;
        private bool _showRenamePopup = false;
        private bool _showDeletePopup = false;
        private string _renameInput = "";
        private static readonly string[] PriorityLabels = { "Low", "Normal", "High" };

        public AutoLootTabContent()
        {
            profile = ProfileManager.CurrentProfile;

            enableAutoLoot = profile.EnableAutoLoot;
            enableScavenger = profile.EnableScavenger;
            enableProgressBar = profile.EnableAutoLootProgressBar;
            autoLootHumanCorpses = profile.AutoLootHumanCorpses;
            hueAfterProcess = profile.HueCorpseAfterAutoloot;
        }

        public override void DrawContent()
        {
            if (profile == null)
            {
                ImGui.Text("Profile not loaded");
                return;
            }
            // Main settings
            ImGui.Spacing();
            if (ImGui.Checkbox("Enable Auto Loot", ref enableAutoLoot))
                profile.EnableAutoLoot = enableAutoLoot;
            ImGuiComponents.Tooltip("Auto Loot allows you to automatically pick up items from corpses based on configured criteria.");

            ImGui.SameLine();

            if (ImGui.Button("Set Grab Bag"))
            {
                GameActions.Print(Client.Game.UO.World, "Target container to grab items into");
                Client.Game.UO.World.TargetManager.SetTargeting(CursorTarget.SetGrabBag, 0, TargetType.Neutral);
            }
            ImGuiComponents.Tooltip("Choose a container to grab items into");

            ImGui.SeparatorText("Options:");
            if (ImGui.Checkbox("Enable Scavenger", ref enableScavenger))
                profile.EnableScavenger = enableScavenger;
            ImGuiComponents.Tooltip("Scavenger option allows to pick objects from ground.");

            ImGui.SameLine();


            if (ImGui.Checkbox("Enable progress bar", ref enableProgressBar))
                profile.EnableAutoLootProgressBar = enableProgressBar;
            ImGuiComponents.Tooltip("Shows a progress bar gump.");


            if (ImGui.Checkbox("Auto loot human corpses", ref autoLootHumanCorpses))
                profile.AutoLootHumanCorpses = autoLootHumanCorpses;
            ImGuiComponents.Tooltip("Auto loots human corpses.");

            ImGui.SameLine();
            if (ImGui.Checkbox("Hue corpse after processing", ref hueAfterProcess))
                profile.HueCorpseAfterAutoloot = hueAfterProcess;
            ImGuiComponents.Tooltip("Hue corpses after processing to make it easier to see if autoloot has processed them.");

            // Loading state check
            if (!AutoLootManager.Instance.Loaded && AutoLootManager.Instance.Profiles.Count == 0)
            {
                ImGui.Text("Loading...");
                return;
            }

            // Lazy-init: auto-select first profile when profiles become available
            if (_selectedProfile == null)
            {
                var profiles = AutoLootManager.Instance.Profiles;
                if (profiles.Count > 0)
                {
                    _selectedProfile = profiles[0];
                    _selectedProfileIndex = 0;
                    AutoLootManager.Instance.SelectedProfile = _selectedProfile;
                }
            }

            // 2-column layout: left = profile sidebar, right = entry table
            if (ImGui.BeginTable("AutoLootProfileTable", 2, ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Profiles", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                DrawProfileSidebar();

                ImGui.TableSetColumnIndex(1);
                DrawEntryTable();

                ImGui.EndTable();
            }

            // Character import popup
            if (showCharacterImportPopup)
            {
                ImGui.OpenPopup("Import from Character");
                showCharacterImportPopup = false;
            }

            if (ImGui.BeginPopupModal("Import from Character"))
            {
                Dictionary<string, List<AutoLootManager.AutoLootConfigEntry>> otherConfigs = AutoLootManager.Instance.GetOtherCharacterConfigs();

                if (otherConfigs.Count == 0)
                {
                    ImGui.Text("No other character autoloot configurations found.");
                    if (ImGui.Button("OK"))
                    {
                        ImGui.CloseCurrentPopup();
                    }
                }
                else
                {
                    ImGui.Text("Select a character to import autoloot configuration from:");
                    ImGui.Separator();

                    foreach (KeyValuePair<string, List<AutoLootManager.AutoLootConfigEntry>> characterConfig in otherConfigs.OrderBy(c => c.Key))
                    {
                        string characterName = characterConfig.Key;
                        List<AutoLootManager.AutoLootConfigEntry> configs = characterConfig.Value;

                        if (ImGui.Button($"{characterName} ({configs.Count} items)"))
                        {
                            AutoLootManager.Instance.ImportFromOtherCharacter(characterName, configs);
                            // Clear input dictionaries to refresh with new data
                            entryGraphicInputs.Clear();
                            entryHueInputs.Clear();
                            entryRegexInputs.Clear();
                            entryDestinationInputs.Clear();
                            ImGui.CloseCurrentPopup();
                        }
                    }

                    ImGui.Separator();
                    if (ImGui.Button("Cancel"))
                    {
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.EndPopup();
            }

            // Rename profile popup
            if (_showRenamePopup)
            {
                ImGui.OpenPopup("Rename Profile");
                _showRenamePopup = false;
            }

            if (ImGui.BeginPopupModal("Rename Profile"))
            {
                ImGui.Text("Enter new profile name:");
                ImGui.SetNextItemWidth(250);
                ImGui.InputText("##RenameInput", ref _renameInput, 128);

                if (ImGui.Button("OK"))
                {
                    if (_contextMenuProfile != null && !string.IsNullOrWhiteSpace(_renameInput))
                    {
                        AutoLootManager.Instance.RenameProfile(_contextMenuProfile, _renameInput);
                    }
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            // Delete profile confirmation popup
            if (_showDeletePopup)
            {
                ImGui.OpenPopup("Delete Profile");
                _showDeletePopup = false;
            }

            if (ImGui.BeginPopupModal("Delete Profile"))
            {
                ImGui.Text($"Delete profile '{_contextMenuProfile?.Name}'?");

                if (ImGui.Button("Confirm"))
                {
                    if (_contextMenuProfile != null)
                    {
                        bool wasSelected = _contextMenuProfile == _selectedProfile;
                        AutoLootManager.Instance.DeleteProfile(_contextMenuProfile);

                        if (wasSelected)
                        {
                            var profiles = AutoLootManager.Instance.Profiles;
                            if (profiles.Count > 0)
                            {
                                _selectedProfile = profiles[0];
                                _selectedProfileIndex = 0;
                                AutoLootManager.Instance.SelectedProfile = _selectedProfile;
                            }
                            else
                            {
                                _selectedProfile = null;
                                _selectedProfileIndex = -1;
                                AutoLootManager.Instance.SelectedProfile = null;
                            }

                            entryGraphicInputs.Clear();
                            entryHueInputs.Clear();
                            entryRegexInputs.Clear();
                            entryDestinationInputs.Clear();
                        }
                    }

                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private void DrawProfileSidebar()
        {
            var profiles = AutoLootManager.Instance.Profiles;

            for (int i = 0; i < profiles.Count; i++)
            {
                AutoLootManager.AutoLootProfile profile = profiles[i];
                bool isActive = profile.IsActive;

                if (ImGui.Checkbox($"##Active{i}", ref isActive))
                {
                    profile.IsActive = isActive;
                    AutoLootManager.Instance.SaveProfile(profile);
                    AutoLootManager.Instance.RebuildMergedList();
                }

                ImGui.SameLine();

                bool isSelected = _selectedProfile == profile;
                if (ImGui.Selectable(profile.Name + $"##Profile{i}", isSelected))
                {
                    SelectProfile(profile, i);
                }

                if (ImGui.BeginPopupContextItem($"##ProfileCtx{i}"))
                {
                    _contextMenuProfile = profile;

                    if (ImGui.MenuItem("Rename"))
                    {
                        _showRenamePopup = true;
                        _renameInput = _contextMenuProfile.Name;
                    }

                    bool canDelete = profiles.Count > 1;
                    if (!canDelete) ImGui.BeginDisabled();
                    if (ImGui.MenuItem("Delete"))
                    {
                        _showDeletePopup = true;
                    }
                    if (!canDelete) ImGui.EndDisabled();

                    ImGui.Separator();

                    if (ImGui.MenuItem("Export to Clipboard"))
                    {
                    }

                    if (ImGui.MenuItem("Import from Clipboard"))
                    {
                    }

                    ImGui.EndPopup();
                }
            }

            ImGui.Separator();

            if (ImGui.Button("New Profile"))
            {
                AutoLootManager.AutoLootProfile newProfile = AutoLootManager.Instance.CreateProfile("New Profile");
                SelectProfile(newProfile, profiles.Count - 1);
            }
        }

        private void SelectProfile(AutoLootManager.AutoLootProfile profile, int index)
        {
            _selectedProfile = profile;
            _selectedProfileIndex = index;
            AutoLootManager.Instance.SelectedProfile = profile;

            entryGraphicInputs.Clear();
            entryHueInputs.Clear();
            entryRegexInputs.Clear();
            entryDestinationInputs.Clear();
        }

        private void DrawEntryTable()
        {
            if (_selectedProfile == null)
            {
                ImGui.Text("Select a profile to view entries");
                return;
            }

            ImGui.SeparatorText("Entries:");

            if (ImGui.Button("Import"))
            {
                string json = Clipboard.GetClipboardText();

                if(json.NotNullNotEmpty() && AutoLootManager.Instance.ImportFromJson(json))
                {
                    GameActions.Print("Imported loot list!", Constants.HUE_SUCCESS);
                    entryGraphicInputs.Clear();
                    entryHueInputs.Clear();
                    entryRegexInputs.Clear();
                    entryDestinationInputs.Clear();
                    return;
                }

                GameActions.Print("Your clipboard does not have a valid export copied.", Constants.HUE_ERROR);
            }
            ImGuiComponents.Tooltip("Import from your clipboard, must have a valid export copied.");

            if (_selectedProfile.Entries.Count > 0)
            {
                ImGui.SameLine();
                if (ImGui.Button("Export"))
                {
                    AutoLootManager.Instance.GetJsonExport()?.CopyToClipboard();
                    GameActions.Print("Exported loot list to your clipboard!", Constants.HUE_SUCCESS);
                }
                ImGuiComponents.Tooltip("Export your list to your clipboard.");
            }

            ImGui.SameLine();
            if (ImGui.Button("Import from Character"))
            {
                showCharacterImportPopup = true;
            }
            ImGuiComponents.Tooltip("Import autoloot configuration from another character.");

            if (ImGui.Button("Add Manual Entry"))
            {
                showAddEntry = !showAddEntry;
            }
            ImGui.SameLine();
            if (ImGui.Button("Add from Target"))
            {
                AutoLootManager.AutoLootProfile targetProfile = _selectedProfile;
                World.Instance.TargetManager.SetTargeting((targetedItem) =>
                {
                    if (targetedItem != null && targetedItem is Entity targetedEntity)
                    {
                        if (SerialHelper.IsItem(targetedEntity))
                        {
                            var newEntry = new AutoLootManager.AutoLootConfigEntry
                            {
                                Graphic = targetedEntity.Graphic,
                                Hue = targetedEntity.Hue,
                                Name = targetedEntity.Name
                            };

                            foreach (AutoLootManager.AutoLootConfigEntry existing in targetProfile.Entries)
                                if (existing.Equals(newEntry))
                                    return;

                            targetProfile.Entries.Add(newEntry);
                            AutoLootManager.Instance.RebuildMergedList();
                            AutoLootManager.Instance.SaveProfile(targetProfile);
                        }
                    }
                });
            }

            if (showAddEntry)
            {
                ImGui.SeparatorText("Add New Entry:");
                ImGui.Spacing();

                ImGui.BeginGroup();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Graphic:");
                ImGui.SameLine();
                ImGuiComponents.Tooltip("Item Graphic");
                ImGui.SetNextItemWidth(70);
                ImGui.InputText("##NewGraphic", ref newGraphicInput, 10);
                ImGui.EndGroup();

                ImGui.SameLine();

                ImGui.BeginGroup();
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Hue:");
                ImGui.SameLine();

                ImGuiComponents.Tooltip("Set -1 to match any Hue");
                ImGui.SetNextItemWidth(70);
                ImGui.InputText("##NewHue", ref newHueInput, 10);
                ImGui.EndGroup();

                ImGui.Text("Regex:");
                ImGui.InputText("##NewRegex", ref newRegexInput, 500);

                ImGui.Spacing();

                if (ImGui.Button("Add##AddEntry"))
                {
                    if (StringHelper.TryParseInt(newGraphicInput, out int graphic))
                    {
                        ushort hue = ushort.MaxValue;
                        if (!string.IsNullOrEmpty(newHueInput) && newHueInput != "-1")
                        {
                            ushort.TryParse(newHueInput, out hue);
                        }

                        var newEntry = new AutoLootManager.AutoLootConfigEntry
                        {
                            Graphic = graphic,
                            Hue = hue,
                            RegexSearch = newRegexInput
                        };

                        bool isDuplicate = false;
                        foreach (AutoLootManager.AutoLootConfigEntry existing in _selectedProfile.Entries)
                            if (existing.Equals(newEntry))
                            {
                                isDuplicate = true;
                                break;
                            }

                        if (!isDuplicate)
                        {
                            _selectedProfile.Entries.Add(newEntry);
                            AutoLootManager.Instance.RebuildMergedList();
                            AutoLootManager.Instance.SaveProfile(_selectedProfile);
                        }

                        newGraphicInput = "";
                        newHueInput = "";
                        newRegexInput = "";
                        showAddEntry = false;
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel##AddEntry"))
                {
                    showAddEntry = false;
                    newGraphicInput = "";
                    newHueInput = "";
                    newRegexInput = "";
                }
            }

            ImGui.SeparatorText("Current Auto Loot Entries:");
            // List of current entries

            if (_selectedProfile.Entries.Count == 0)
            {
                ImGui.Text("No entries configured");
            }
            else
            // Table headers
            if (ImGui.BeginTable("AutoLootTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, ImGuiTheme.Dimensions.STANDARD_TABLE_SCROLL_HEIGHT)))
            {
                ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, 52);
                ImGui.TableSetupColumn("Graphic", ImGuiTableColumnFlags.WidthFixed, ImGuiTheme.Dimensions.STANDARD_INPUT_WIDTH);
                ImGui.TableSetupColumn("Hue", ImGuiTableColumnFlags.WidthFixed, ImGuiTheme.Dimensions.STANDARD_INPUT_WIDTH);
                ImGui.TableSetupColumn("Regex", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Priority", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Destination", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableHeadersRow();

                for (int i = _selectedProfile.Entries.Count - 1; i >= 0; i--)
                {
                    AutoLootManager.AutoLootConfigEntry entry = _selectedProfile.Entries[i];
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    if (!DrawArt((ushort)entry.Graphic, new Vector2(50, 50)))
                        ImGui.Text($"{entry.Graphic:X4}");
                    SetTooltip(entry.Name);

                    ImGui.TableNextColumn();
                    // Initialize input string if not exists
                    if (!entryGraphicInputs.ContainsKey(entry.Uid))
                    {
                        entryGraphicInputs[entry.Uid] = entry.Graphic.ToString();
                    }
                    string graphicStr = entryGraphicInputs[entry.Uid];
                    if (ImGui.InputText($"##Graphic{i}", ref graphicStr, 10))
                    {
                        entryGraphicInputs[entry.Uid] = graphicStr;
                        if (StringHelper.TryParseInt(graphicStr, out int newGraphic))
                        {
                            entry.Graphic = newGraphic;
                        }
                    }
                    SetTooltip("Set to -1 to match any graphic.");

                    ImGui.TableNextColumn();
                    // Initialize input string if not exists
                    if (!entryHueInputs.ContainsKey(entry.Uid))
                    {
                        entryHueInputs[entry.Uid] = entry.Hue == ushort.MaxValue ? "-1" : entry.Hue.ToString();
                    }
                    string hueStr = entryHueInputs[entry.Uid];
                    if (ImGui.InputText($"##Hue{i}", ref hueStr, 10))
                    {
                        entryHueInputs[entry.Uid] = hueStr;
                        if (hueStr == "-1")
                        {
                            entry.Hue = ushort.MaxValue;
                        }
                        else if (ushort.TryParse(hueStr, out ushort newHue))
                        {
                            entry.Hue = newHue;
                        }
                    }
                    SetTooltip("Set to -1 to match any hue.");

                    ImGui.TableNextColumn();
                    // Initialize input string if not exists
                    if (!entryRegexInputs.ContainsKey(entry.Uid))
                    {
                        entryRegexInputs[entry.Uid] = entry.RegexSearch ?? "";
                    }
                    string regexStr = entryRegexInputs[entry.Uid];


                    if (ImGui.Button($"Edit##{i}"))
                    {
                        ImGui.OpenPopup($"RegexEditor##{i}");
                    }

                    if (ImGui.BeginPopup($"RegexEditor##{i}"))
                    {
                        ImGui.TextColored(ImGuiTheme.Current.Primary, "Regex Editor:");

                        if (ImGui.InputTextMultiline($"##Regex{i}", ref regexStr, 500, new Vector2(300, 100)))
                        {
                            entryRegexInputs[entry.Uid] = regexStr;
                            entry.RegexSearch = regexStr;
                        }

                        if (ImGui.Button("Close"))
                            ImGui.CloseCurrentPopup();

                        ImGui.EndPopup();
                    }

                    ImGui.TableNextColumn();
                    int priorityIndex = (int)entry.Priority;
                    ImGui.SetNextItemWidth(70);
                    if (ImGui.Combo($"##Priority{i}", ref priorityIndex, PriorityLabels, PriorityLabels.Length))
                    {
                        entry.Priority = (AutoLootManager.AutoLootPriority)priorityIndex;
                    }

                    ImGui.TableNextColumn();
                    // Initialize input string if not exists
                    if (!entryDestinationInputs.ContainsKey(entry.Uid))
                    {
                        entryDestinationInputs[entry.Uid] = entry.DestinationContainer == 0 ? "" : $"0x{entry.DestinationContainer:X}";
                    }
                    string destStr = entryDestinationInputs[entry.Uid];
                    ImGui.SetNextItemWidth(80);
                    if (ImGui.InputText($"##Dest{i}", ref destStr, 20))
                    {
                        entryDestinationInputs[entry.Uid] = destStr;
                        if (string.IsNullOrWhiteSpace(destStr))
                        {
                            entry.DestinationContainer = 0;
                        }
                        else if (uint.TryParse(destStr.Replace("0x", "").Replace("0X", ""), System.Globalization.NumberStyles.HexNumber, null, out uint destSerial))
                        {
                            entry.DestinationContainer = destSerial;
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"Target##Dest{i}"))
                    {
                        World.Instance.TargetManager.SetTargeting((targetedContainer) =>
                        {
                            if (targetedContainer != null && targetedContainer is Entity targetedEntity)
                            {
                                if (SerialHelper.IsItem(targetedEntity))
                                {
                                    entry.DestinationContainer = targetedEntity.Serial;
                                    entryDestinationInputs[entry.Uid] = $"0x{targetedEntity.Serial:X}";
                                }
                            }
                        });
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##Delete{i}"))
                    {
                        _selectedProfile.Entries.Remove(entry);
                        AutoLootManager.Instance.RebuildMergedList();
                        AutoLootManager.Instance.SaveProfile(_selectedProfile);
                        // Clean up input dictionaries
                        entryGraphicInputs.Remove(entry.Uid);
                        entryHueInputs.Remove(entry.Uid);
                        entryRegexInputs.Remove(entry.Uid);
                        entryDestinationInputs.Remove(entry.Uid);
                    }
                }

                ImGui.EndTable();
            }
        }
    }
}
