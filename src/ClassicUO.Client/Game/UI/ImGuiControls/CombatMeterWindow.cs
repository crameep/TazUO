using System;
using System.Collections.Generic;
using System.Numerics;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI;
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

        private static readonly string[] TimeWindowLabels = { "Current Fight", "30s", "1m", "5m", "Session" };
        private static readonly uint[] TimeWindowMs = { 0, 30_000, 60_000, 300_000, uint.MaxValue };

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
                    DrawPerTargetTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Timeline"))
                {
                    DrawTimelineTab();
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

        private void DrawTimelineTab()
        {
            var tracker = CombatTracker.Instance;
            uint sessionMs = (uint)(tracker.SessionDuration * 1000f);

            if (sessionMs < 1000)
            {
                ImGui.Text("Waiting for data...");
                return;
            }

            uint windowMs = Math.Min(sessionMs, 300_000);
            var buckets = tracker.GetTimelineBuckets(windowMs);

            if (buckets.Count == 0)
            {
                ImGui.Text("No combat data yet.");
                return;
            }

            // Find max value for scaling
            int maxVal = 1;
            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].Dealt > maxVal) maxVal = buckets[i].Dealt;
                if (buckets[i].Taken > maxVal) maxVal = buckets[i].Taken;
                if (buckets[i].Healed > maxVal) maxVal = buckets[i].Healed;
            }

            // Legend row
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Dealt");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Taken");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.6f, 1.0f, 1.0f), "Healed");
            ImGui.SameLine();
            ImGui.Text($"  Peak: {maxVal}");

            float chartHeight = 150f;
            float barWidth = 8f;
            float groupSpacing = 2f;
            float groupWidth = barWidth * 3 + groupSpacing;
            float totalWidth = buckets.Count * groupWidth + groupSpacing;

            ImGui.BeginChild("TimelineScroll", new Vector2(-1, chartHeight + 20f), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            Vector2 cursorStart = ImGui.GetCursorScreenPos();
            float baselineY = cursorStart.Y + chartHeight;

            uint colorDealt = ImGui.GetColorU32(new Vector4(0.4f, 1.0f, 0.4f, 0.8f));
            uint colorTaken = ImGui.GetColorU32(new Vector4(1.0f, 0.4f, 0.4f, 0.8f));
            uint colorHealed = ImGui.GetColorU32(new Vector4(0.4f, 0.6f, 1.0f, 0.8f));
            uint colorFightLine = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.0f, 0.6f));

            uint cutoff = Time.Ticks > windowMs ? Time.Ticks - windowMs : 0;

            for (int i = 0; i < buckets.Count; i++)
            {
                float x = cursorStart.X + i * groupWidth + groupSpacing;

                // Dealt bar (green)
                if (buckets[i].Dealt > 0)
                {
                    float h = (buckets[i].Dealt / (float)maxVal) * chartHeight;
                    drawList.AddRectFilled(
                        new Vector2(x, baselineY - h),
                        new Vector2(x + barWidth, baselineY),
                        colorDealt
                    );
                }

                // Taken bar (red)
                if (buckets[i].Taken > 0)
                {
                    float h = (buckets[i].Taken / (float)maxVal) * chartHeight;
                    drawList.AddRectFilled(
                        new Vector2(x + barWidth, baselineY - h),
                        new Vector2(x + barWidth * 2, baselineY),
                        colorTaken
                    );
                }

                // Healed bar (blue)
                if (buckets[i].Healed > 0)
                {
                    float h = (buckets[i].Healed / (float)maxVal) * chartHeight;
                    drawList.AddRectFilled(
                        new Vector2(x + barWidth * 2, baselineY - h),
                        new Vector2(x + barWidth * 3, baselineY),
                        colorHealed
                    );
                }
            }

            // Draw fight boundary lines
            var fights = tracker.Fights;
            for (int f = 0; f < fights.Count; f++)
            {
                uint fightStart = fights[f].StartTime;
                if (fightStart < cutoff) continue;

                float offsetMs = fightStart - cutoff;
                float bucketIndex = offsetMs / 1000f;
                float lineX = cursorStart.X + bucketIndex * groupWidth + groupSpacing;

                drawList.AddLine(
                    new Vector2(lineX, cursorStart.Y),
                    new Vector2(lineX, baselineY),
                    colorFightLine
                );
            }

            // If currently in a fight, draw its start line too
            var currentFight = tracker.GetCurrentFightSummary();
            if (currentFight != null)
            {
                uint fightStart = currentFight.StartTime;
                if (fightStart >= cutoff)
                {
                    float offsetMs = fightStart - cutoff;
                    float bucketIndex = offsetMs / 1000f;
                    float lineX = cursorStart.X + bucketIndex * groupWidth + groupSpacing;

                    drawList.AddLine(
                        new Vector2(lineX, cursorStart.Y),
                        new Vector2(lineX, baselineY),
                        colorFightLine
                    );
                }
            }

            // Invisible buttons for hover tooltips
            for (int i = 0; i < buckets.Count; i++)
            {
                float x = cursorStart.X + i * groupWidth + groupSpacing;
                ImGui.SetCursorScreenPos(new Vector2(x, cursorStart.Y));

                ImGui.InvisibleButton($"tb_{i}", new Vector2(barWidth * 3, chartHeight));

                if (ImGui.IsItemHovered())
                {
                    uint elapsed = buckets[i].Timestamp > tracker.SessionStart
                        ? buckets[i].Timestamp - tracker.SessionStart
                        : 0;
                    int totalSec = (int)(elapsed / 1000);
                    int min = totalSec / 60;
                    int sec = totalSec % 60;

                    ImGui.BeginTooltip();
                    ImGui.Text($"[{min}:{sec:D2}]");
                    ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), $"Dealt: {buckets[i].Dealt}");
                    ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), $"Taken: {buckets[i].Taken}");
                    ImGui.TextColored(new Vector4(0.4f, 0.6f, 1.0f, 1.0f), $"Healed: {buckets[i].Healed}");
                    ImGui.EndTooltip();
                }
            }

            // Set dummy to ensure scroll area covers all bars
            ImGui.SetCursorScreenPos(new Vector2(cursorStart.X + totalWidth, baselineY));
            ImGui.Dummy(new Vector2(0, 0));

            ImGui.EndChild();
        }

        private void DrawFooter()
        {
            ImGui.Text("Session stats here");
        }
    }
}
