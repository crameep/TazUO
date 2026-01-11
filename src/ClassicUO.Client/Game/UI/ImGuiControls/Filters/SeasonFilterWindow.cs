using ImGuiNET;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game;
using System;
using System.Numerics;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class SeasonFilterWindow : SingletonImGuiWindow<SeasonFilterWindow>
    {
        private Profile profile;
        private readonly string[] seasonNames = { "Spring", "Summer", "Fall", "Winter", "Desolation" };
        private readonly Season[] allSeasons =
        {
            Season.Spring,
            Season.Summer,
            Season.Fall,
            Season.Winter,
            Season.Desolation
        };

        private SeasonFilterWindow() : base("Season Filter")
        {
            WindowFlags = ImGuiWindowFlags.AlwaysAutoResize;
            profile = ProfileManager.CurrentProfile;
        }

        private int GetComboIndex(Season incoming)
        {
            // Check if there's a filter for this season
            if (SeasonFilter.Instance.Filters.TryGetValue(incoming, out Season replacement))
            {
                // Find the index of the replacement season in our array
                // Add 1 because index 0 is "None"
                for (int i = 0; i < allSeasons.Length; i++)
                {
                    if (allSeasons[i] == replacement)
                        return i + 1;
                }
            }

            // No filter, return 0 (None)
            return 0;
        }

        private void SetComboValue(Season incoming, int selectedIndex)
        {
            if (selectedIndex == 0)
            {
                // "None" selected - remove filter
                SeasonFilter.Instance.RemoveFilter(incoming);
            }
            else
            {
                // Season selected - set filter
                Season replacement = allSeasons[selectedIndex - 1];
                SeasonFilter.Instance.SetFilter(incoming, replacement);
            }
        }

        public override void DrawContent()
        {
            if (profile == null)
            {
                ImGui.Text("Profile not loaded");
                return;
            }

            ImGui.Spacing();
            ImGui.TextWrapped("Override seasons sent by the server. For example, if the server sends Winter, you can display Fall instead.");

            ImGui.Spacing();

            if (ImGui.Button("Clear All Filters"))
            {
                SeasonFilter.Instance.Clear();
            }
            ImGuiComponents.Tooltip("Remove all season filters and display seasons as sent by the server");

            ImGui.Spacing();
            ImGui.SeparatorText("Season Filters:");

            // Create table with season filters
            if (ImGui.BeginTable("SeasonFilterTable", 2,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg,
                new Vector2(400, 0)))
            {
                ImGui.TableSetupColumn("When Server Sends", ImGuiTableColumnFlags.WidthFixed, 180);
                ImGui.TableSetupColumn("Show As", ImGuiTableColumnFlags.WidthFixed, 180);
                ImGui.TableHeadersRow();

                // Create a row for each season
                for (int i = 0; i < allSeasons.Length; i++)
                {
                    Season currentSeason = allSeasons[i];
                    ImGui.TableNextRow();

                    // Column 1: Season name
                    ImGui.TableNextColumn();
                    ImGui.Text(seasonNames[i]);

                    // Column 2: Combo dropdown
                    ImGui.TableNextColumn();

                    // Create combo options: None + all seasons
                    string[] comboOptions = new string[allSeasons.Length + 1];
                    comboOptions[0] = "None";
                    for (int j = 0; j < seasonNames.Length; j++)
                    {
                        comboOptions[j + 1] = seasonNames[j];
                    }

                    int currentSelection = GetComboIndex(currentSeason);
                    ImGui.SetNextItemWidth(150);

                    if (ImGui.Combo($"##{seasonNames[i]}Combo", ref currentSelection, comboOptions, comboOptions.Length))
                    {
                        SetComboValue(currentSeason, currentSelection);
                    }

                    // Add tooltip explaining what this filter does
                    if (currentSelection > 0)
                    {
                        string replacementName = seasonNames[currentSelection - 1];
                        ImGuiComponents.Tooltip($"When server sends {seasonNames[i]}, display {replacementName} graphics instead");
                    }
                    else
                    {
                        ImGuiComponents.Tooltip($"No filter - display {seasonNames[i]} as normal");
                    }
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.Spacing();

            // Info section
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Info:");
            ImGui.TextWrapped("• Select 'None' to disable a filter and show the original season");
            ImGui.TextWrapped("• Select a season to replace it with a different one");
            ImGui.TextWrapped("• Example: Set Winter → Summer to never see winter graphics");
        }
    }
}
