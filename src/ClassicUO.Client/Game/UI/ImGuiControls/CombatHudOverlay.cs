using System;
using System.Numerics;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class CombatHudOverlay : SingletonImGuiWindow<CombatHudOverlay>
    {
        private static readonly Vector4 ColorDPS = new Vector4(0.4f, 1.0f, 0.4f, 1.0f);
        private static readonly Vector4 ColorDTPS = new Vector4(1.0f, 0.4f, 0.4f, 1.0f);
        private static readonly Vector4 ColorHPS = new Vector4(0.4f, 0.6f, 1.0f, 1.0f);

        private bool _autoShown;
        private uint _lastFightEndTick;

        private CombatHudOverlay() : base("##CombatHud")
        {
            WindowFlags = ImGuiWindowFlags.NoTitleBar
                        | ImGuiWindowFlags.NoResize
                        | ImGuiWindowFlags.AlwaysAutoResize
                        | ImGuiWindowFlags.NoScrollbar
                        | ImGuiWindowFlags.NoFocusOnAppearing
                        | ImGuiWindowFlags.NoNav;
        }

        public new void Draw()
        {
            var profile = ProfileManager.CurrentProfile;
            if (profile == null || !profile.CombatMeterEnabled || !profile.CombatHudVisible)
                return;

            var tracker = CombatTracker.Instance;

            // Auto-show/hide logic
            if (profile.CombatHudAutoShow)
            {
                if (tracker.InFight)
                {
                    if (!_autoShown)
                    {
                        _autoShown = true;
                        IsVisible = true;
                    }

                    _lastFightEndTick = Time.Ticks;
                }
                else if (_autoShown)
                {
                    uint elapsedMs = Time.Ticks - _lastFightEndTick;
                    if (elapsedMs > (uint)(profile.CombatHudAutoHideDelay * 1000))
                    {
                        _autoShown = false;
                        IsVisible = false;
                        return;
                    }
                }
                else
                {
                    // Not in fight and not auto-shown, nothing to display
                    return;
                }
            }

            ImGui.SetNextWindowSize(new Vector2(160, 0), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowBgAlpha(0.7f);
            base.Draw();
        }

        public override void DrawContent()
        {
            var tracker = CombatTracker.Instance;

            float dps = tracker.GetDPS(15000);
            float dtps = tracker.GetDTPS(15000);
            float hps = tracker.GetHPS(15000);

            ImGui.TextColored(ColorDPS, $"DPS:  {dps,6:F1}");
            ImGui.TextColored(ColorDTPS, $"DTPS: {dtps,6:F1}");
            ImGui.TextColored(ColorHPS, $"HPS:  {hps,6:F1}");

            if (tracker.InFight)
            {
                float duration = tracker.GetCurrentFightDuration();
                int minutes = (int)(duration / 60f);
                int seconds = (int)(duration % 60f);
                ImGui.Separator();
                ImGui.Text($"Fight: {minutes}:{seconds:D2}");
            }

            // Right-click to open the full combat meter window
            if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                CombatMeterWindow.Show();
            }
        }
    }
}
