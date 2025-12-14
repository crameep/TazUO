using ImGuiNET;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class SoundFilterWindow : SingletonImGuiWindow<SoundFilterWindow>
    {
        private Profile profile;
        private int newSoundIdInput = 0;
        private bool showAddFilter = false;
        private List<int> filterList;
        private Dictionary<int, int> filterInputs = new Dictionary<int, int>();

        private SoundFilterWindow() : base("Sound Filter")
        {
            WindowFlags = ImGuiWindowFlags.AlwaysAutoResize;
            profile = ProfileManager.CurrentProfile;
            RefreshFilterList();
        }

        private void RefreshFilterList()
        {
            filterList = SoundFilterManager.Instance.FilteredSounds.OrderBy(x => x).ToList();

            // Initialize input dictionaries for existing filters
            foreach (int soundId in filterList)
                if (!filterInputs.ContainsKey(soundId))
                    filterInputs[soundId] = soundId;
        }

        public override void DrawContent()
        {
            if (profile == null)
            {
                ImGui.Text("Profile not loaded");
                return;
            }

            ImGui.Spacing();
            ImGui.TextWrapped("Sound Filter allows you to mute specific in-game sounds by their ID.");

            // Last played sound section
            ImGui.SeparatorText("Last Sound Played:");
            int lastSoundId = Client.Game.Audio.LastPlayedSoundId;

            if (lastSoundId >= 0)
            {
                ImGui.Text($"Sound ID: {lastSoundId}");
                ClipboardOnClick(lastSoundId.ToString());
                ImGui.SameLine();

                if (ImGui.Button($"Add Filter##AddLastSound"))
                {
                    SoundFilterManager.Instance.AddFilter(lastSoundId);
                    RefreshFilterList();
                }
                ImGuiComponents.Tooltip("Add this sound to the filter list");

                ImGui.SameLine();
                if (ImGui.Button($"Play Again##PlayLastSound")) Client.Game.Audio.PlaySound(lastSoundId, true);
                ImGuiComponents.Tooltip("Play this sound again");
            }
            else
                ImGui.TextDisabled("No sound played yet");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), "Tip:");
            ImGui.SameLine();
            ImGui.TextWrapped("Play a sound in-game to see its ID above, then click Copy or Add Filter.");

            // Add filter section
            ImGui.SeparatorText("Add Sound Filter:");

            if (ImGui.Button("Add Filter Entry")) showAddFilter = !showAddFilter;

            if (showAddFilter)
            {
                ImGui.Spacing();
                ImGui.Text("Sound ID:");
                ImGuiComponents.Tooltip("Enter the numeric ID of the sound to filter (0-65535)");

                ImGui.SetNextItemWidth(150);
                ImGui.InputInt("##NewSoundId", ref newSoundIdInput, 1, 10);

                // Clamp to valid range
                if (newSoundIdInput < 0) newSoundIdInput = 0;
                if (newSoundIdInput > 65535) newSoundIdInput = 65535;

                ImGui.Spacing();

                if (ImGui.Button("Add##AddFilter"))
                    if (newSoundIdInput >= 0)
                    {
                        SoundFilterManager.Instance.AddFilter(newSoundIdInput);
                        newSoundIdInput = 0;
                        showAddFilter = false;
                        RefreshFilterList();
                    }

                ImGui.SameLine();
                if (ImGui.Button("Cancel##AddFilter"))
                {
                    showAddFilter = false;
                    newSoundIdInput = 0;
                }
                ImGui.SameLine();
                if (ImGui.Button("Test Play##TestPlay")) Client.Game.Audio.PlaySound(newSoundIdInput, true);
            }

            ImGui.SeparatorText("Filtered Sounds:");

            if (filterList.Count == 0)
                ImGui.Text("No sounds filtered");
            else
            {
                ImGui.Text($"Total: {filterList.Count} sound(s) filtered");

                if (ImGui.Button("Clear All Filters"))
                {
                    SoundFilterManager.Instance.Clear();
                    RefreshFilterList();
                }

                ImGui.Spacing();

                // Table for filters
                if (ImGui.BeginTable("SoundFilterTable", 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                    new Vector2(0, ImGuiTheme.Dimensions.STANDARD_TABLE_SCROLL_HEIGHT)))
                {
                    ImGui.TableSetupColumn("Sound ID", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 200);
                    ImGui.TableSetupColumn("Delete", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableHeadersRow();

                    for (int i = filterList.Count - 1; i >= 0; i--)
                    {
                        int soundId = filterList[i];
                        ImGui.TableNextRow();

                        ImGui.TableNextColumn();

                        // Display sound ID with input for editing
                        if (!filterInputs.ContainsKey(soundId)) filterInputs[soundId] = soundId;

                        int inputValue = filterInputs[soundId];
                        ImGui.SetNextItemWidth(80);
                        ImGui.InputInt($"##SoundId{i}", ref inputValue, 0, 0);

                        // Clamp to valid range
                        if (inputValue < 0) inputValue = 0;
                        if (inputValue > 65535) inputValue = 65535;

                        filterInputs[soundId] = inputValue;

                        // Only commit changes on Enter or blur
                        if (ImGui.IsItemDeactivatedAfterEdit() && inputValue != soundId)
                        {
                            // Update the filter
                            SoundFilterManager.Instance.RemoveFilter(soundId);
                            SoundFilterManager.Instance.AddFilter(inputValue);

                            // Update our tracking
                            filterInputs.Remove(soundId);
                            filterInputs[inputValue] = inputValue;

                            RefreshFilterList();
                            break;
                        }

                        ImGui.TableNextColumn();

                        if (ImGui.Button($"Play##Play{i}")) Client.Game.Audio.PlaySound(soundId, true);

                        ImGuiComponents.Tooltip("Test play this sound (bypasses filter)");

                        ImGui.TableNextColumn();
                        if (ImGui.Button($"Delete##Delete{i}"))
                        {
                            SoundFilterManager.Instance.RemoveFilter(soundId);
                            filterInputs.Remove(soundId);
                            RefreshFilterList();
                            break;
                        }
                    }

                    ImGui.EndTable();
                }
            }
        }
    }
}
