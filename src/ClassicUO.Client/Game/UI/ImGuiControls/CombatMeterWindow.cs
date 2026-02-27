using System;
using System.Collections.Generic;
using System.Numerics;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls
{
    internal class CombatMeterWindow : SingletonImGuiWindow<CombatMeterWindow>
    {
        // Filter state
        private bool _showDamageDealt = true;
        private bool _showDamageTaken = true;
        private bool _showHeals = true;
        private bool _showPets = true;
        private bool _showAllies = true;
        private bool _pinToBottom = true;
        private uint? _filterTargetSerial = null;

        // Time window fields (prepared for per-target tab)
        private int _selectedTimeWindow = 2;

        private static readonly string[] TimeWindowLabels = { "10s", "30s", "1m", "5m", "All" };
        private static readonly uint[] TimeWindowMs = { 10_000, 30_000, 60_000, 300_000, uint.MaxValue };

        private CombatMeterWindow() : base("Combat Meter")
        {
            WindowFlags = ImGuiWindowFlags.None;
        }

        public new void Draw()
        {
            var profile = ProfileManager.CurrentProfile;
            if (profile == null || !profile.CombatMeterEnabled)
                return;

            ImGui.SetNextWindowSize(new Vector2(500, 400), ImGuiCond.FirstUseEver);
            base.Draw();
        }

        public override void DrawContent()
        {
            if (ImGui.BeginTabBar("CombatMeterTabs"))
            {
                if (ImGui.BeginTabItem("Log"))
                {
                    DrawLogTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Per-Target"))
                {
                    ImGui.Text("Coming soon...");
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Timeline"))
                {
                    ImGui.Text("Coming soon...");
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.Separator();
            DrawFooter();
        }

        private void DrawLogTab()
        {
            // Filter checkboxes row
            ImGui.Checkbox("Dealt", ref _showDamageDealt);
            ImGui.SameLine();
            ImGui.Checkbox("Taken", ref _showDamageTaken);
            ImGui.SameLine();
            ImGui.Checkbox("Heals", ref _showHeals);
            ImGui.SameLine();
            ImGui.Checkbox("Pets", ref _showPets);
            ImGui.SameLine();
            ImGui.Checkbox("Allies", ref _showAllies);
            ImGui.SameLine();
            ImGui.Checkbox("Auto-scroll", ref _pinToBottom);

            if (_filterTargetSerial.HasValue)
            {
                ImGui.SameLine();
                if (ImGui.Button("Clear filter"))
                    _filterTargetSerial = null;
            }

            ImGui.BeginChild("LogScroll");

            var tracker = CombatTracker.Instance;
            IReadOnlyList<CombatEvent> events = tracker.Events;

            for (int i = 0; i < events.Count; i++)
            {
                CombatEvent e = events[i];

                if (!PassesFilter(e))
                    continue;

                Vector4 color = GetEventColor(e);
                string text = FormatEvent(e, tracker.SessionStart);

                ImGui.TextColored(color, text);
            }

            if (_pinToBottom && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20f)
                ImGui.SetScrollHereY(1.0f);

            ImGui.EndChild();
        }

        private bool PassesFilter(CombatEvent e)
        {
            if (_filterTargetSerial.HasValue && e.TargetSerial != _filterTargetSerial.Value)
                return false;

            if (e.IsHeal)
                return _showHeals;

            return e.Category switch
            {
                CombatCategory.Self => _showDamageTaken,
                CombatCategory.LastTarget => _showDamageDealt,
                CombatCategory.Pet => _showPets,
                CombatCategory.Ally => _showAllies,
                CombatCategory.Other => _showDamageDealt,
                _ => true
            };
        }

        private static Vector4 GetEventColor(CombatEvent e)
        {
            if (e.IsHeal)
                return new Vector4(0.4f, 0.6f, 1.0f, 1.0f);

            return e.Category switch
            {
                CombatCategory.Self => new Vector4(1.0f, 0.4f, 0.4f, 1.0f),
                CombatCategory.LastTarget => new Vector4(0.4f, 1.0f, 0.4f, 1.0f),
                CombatCategory.Pet => new Vector4(0.4f, 1.0f, 1.0f, 1.0f),
                CombatCategory.Ally => new Vector4(0.6f, 0.6f, 1.0f, 1.0f),
                _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
            };
        }

        private static string FormatEvent(CombatEvent e, uint sessionStart)
        {
            uint elapsedMs = e.Timestamp > sessionStart ? e.Timestamp - sessionStart : 0;
            int totalSeconds = (int)(elapsedMs / 1000);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            string time = $"[{minutes}:{seconds:D2}]";

            if (e.IsHeal)
            {
                if (e.Category == CombatCategory.Self)
                    return $"{time} You healed for {e.Amount}";

                return $"{time} {e.TargetName} healed for {e.Amount}";
            }

            if (e.Category == CombatCategory.Self)
                return $"{time} {e.TargetName} hits you for {e.Amount}";

            return $"{time} You hit {e.TargetName} for {e.Amount}";
        }

        private void DrawFooter()
        {
            ImGui.Text("Session stats here");
        }
    }
}
