// SPDX-License-Identifier: BSD-2-Clause

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows;

internal sealed class CombatMeterWindow : MyraControl
{
    private const uint RefreshInterval = 500;

    private readonly MyraLabel _summary;
    private readonly MyraInputBox _targets;
    private readonly MyraInputBox _timeline;
    private readonly MyraInputBox _events;
    private uint _lastRefresh;

    public static void Show()
    {
        foreach (IGui gui in UIManager.Gumps)
        {
            if (gui is not CombatMeterWindow window) continue;
            window.IsVisible = true;
            window.BringOnTop();
            return;
        }

        UIManager.Add(new CombatMeterWindow());
    }

    private CombatMeterWindow() : base("Combat Meter")
    {
        CanBeSaved = true;
        Profile profile = ProfileManager.CurrentProfile;

        var settings = new HorizontalStackPanel { Spacing = MyraStyle.STANDARD_SPACING };
        settings.Widgets.Add(MyraCheckButton.CreateWithCallback(
            profile.CombatMeterEnabled,
            enabled => profile.CombatMeterEnabled = enabled,
            "Track combat"
        ));
        settings.Widgets.Add(MyraCheckButton.CreateWithCallback(
            profile.CombatHudVisible,
            visible =>
            {
                profile.CombatHudVisible = visible;
                CombatHudWindow.Ensure();
            },
            "Show HUD"
        ));
        settings.Widgets.Add(MyraCheckButton.CreateWithCallback(
            profile.CombatHudAutoShow,
            autoShow => profile.CombatHudAutoShow = autoShow,
            "HUD only in combat"
        ));

        var idleSpinner = new SpinButton
        {
            Integer = true,
            Minimum = 1,
            Maximum = 120,
            Value = profile.CombatFightIdleThreshold,
            MinWidth = 70,
            Tooltip = "Seconds without an event before the current fight ends."
        };
        idleSpinner.ValueChangedByUser += (_, _) =>
        {
            profile.CombatFightIdleThreshold = (int)(idleSpinner.Value ?? 10);
            CombatTracker.Instance.SetFightIdleThreshold(profile.CombatFightIdleThreshold * 1000);
        };
        settings.Widgets.Add(new MyraLabel("Fight idle (s):", MyraLabel.TextStyle.P));
        settings.Widgets.Add(idleSpinner);

        var actions = new HorizontalStackPanel { Spacing = MyraStyle.STANDARD_SPACING };
        actions.Widgets.Add(new MyraButton("Refresh", Refresh));
        actions.Widgets.Add(new MyraButton("Reset session", () =>
        {
            CombatTracker.Instance.Initialize();
            Refresh();
        }));
        actions.Widgets.Add(new MyraButton("Export JSON", Export));
        actions.Widgets.Add(new MyraButton("Show HUD", CombatHudWindow.Show));

        _summary = new MyraLabel(string.Empty, MyraLabel.TextStyle.H3);
        _targets = CreateOutputBox(450, 120);
        _timeline = CreateOutputBox(450, 120);
        _events = CreateOutputBox(600, 260);

        var tabs = new MyraTabControl();
        tabs.AddTab("Event Log", () => new ScrollViewer { MaxHeight = 300, Content = _events });
        tabs.AddTab("Per Target", () => new ScrollViewer { MaxHeight = 180, Content = _targets });
        tabs.AddTab("Timeline", () => new ScrollViewer { MaxHeight = 180, Content = _timeline });
        tabs.SelectFirst();

        var root = new VerticalStackPanel
        {
            Spacing = MyraStyle.STANDARD_SPACING,
            Padding = new Myra.Graphics2D.Thickness(6)
        };
        root.Widgets.Add(settings);
        root.Widgets.Add(actions);
        root.Widgets.Add(_summary);
        root.Widgets.Add(tabs);

        SetRootContent(root);
        CenterInViewPort();
        Refresh();
    }

    public override void Update()
    {
        base.Update();
        if (Time.Ticks - _lastRefresh < RefreshInterval) return;
        Refresh();
    }

    private static MyraInputBox CreateOutputBox(int width, int height) => new()
    {
        Multiline = true,
        Readonly = true,
        MinWidth = width,
        MinHeight = height
    };

    private void Refresh()
    {
        _lastRefresh = Time.Ticks;
        CombatTracker tracker = CombatTracker.Instance;
        FightSummary? fight = tracker.GetCurrentFightSummary();
        string fightText = fight == null
            ? "No active fight"
            : $"Current: {fight.DurationSeconds:F1}s | {fight.TotalDealt} dealt | {fight.TotalTaken} taken | {fight.TotalHealed} healed";
        _summary.Text =
            $"{fightText}\nSession: {tracker.TotalDealt} dealt | {tracker.TotalTaken} taken | " +
            $"{tracker.TotalHealed} healed | {tracker.TotalKills} kills | " +
            $"DPS {tracker.GetDPS(15000):F1} / DTPS {tracker.GetDTPS(15000):F1} / HPS {tracker.GetHPS(15000):F1}";

        var targets = new StringBuilder("Target                         Serial       Dealt   Hits   Avg\n");
        foreach (TargetBreakdown target in tracker.GetPerTargetBreakdown(uint.MaxValue))
            targets.AppendLine($"{target.Name,-28} 0x{target.Serial:X8} {target.Dealt,7} {target.HitCount,6} {target.AvgHit,6:F1}");
        _targets.Text = targets.ToString();

        var timeline = new StringBuilder("Time       Dealt   Taken  Healed\n");
        foreach (TimelineBucket bucket in tracker.GetTimelineBuckets(300000, 5000))
        {
            uint elapsed = bucket.Timestamp > tracker.SessionStart ? bucket.Timestamp - tracker.SessionStart : 0;
            timeline.AppendLine($"{elapsed / 60000}:{elapsed / 1000 % 60:D2} {bucket.Dealt,9} {bucket.Taken,7} {bucket.Healed,7}");
        }
        _timeline.Text = timeline.ToString();

        IReadOnlyList<CombatEvent> combatEvents = tracker.Events;
        int start = Math.Max(0, combatEvents.Count - 250);
        var events = new StringBuilder();
        for (int index = start; index < combatEvents.Count; index++)
        {
            CombatEvent combatEvent = combatEvents[index];
            uint elapsed = combatEvent.Timestamp > tracker.SessionStart
                ? combatEvent.Timestamp - tracker.SessionStart
                : 0;
            string action = combatEvent.IsHeal ? "HEAL" : combatEvent.Category == CombatCategory.Self ? "TAKE" : "HIT ";
            events.AppendLine(
                $"[{elapsed / 60000}:{elapsed / 1000 % 60:D2}] {action} {combatEvent.Amount,5}  " +
                $"{combatEvent.TargetName} (0x{combatEvent.TargetSerial:X8})"
            );
        }
        _events.Text = events.ToString();
    }

    private static void Export()
    {
        try
        {
            string directory = ProfileManager.CurrentProfile?.CombatExportPath ?? "CombatLogs";
            if (!Path.IsPathRooted(directory))
                directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, directory);

            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"combat_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
            File.WriteAllText(path, CombatTracker.Instance.ExportSessionJson());
            GameActions.Print($"Combat log exported to {path}", Constants.HUE_SUCCESS);
        }
        catch (Exception exception)
        {
            GameActions.Print($"Combat export failed: {exception.Message}", Constants.HUE_ERROR);
        }
    }
}

internal sealed class CombatHudWindow : MyraControl
{
    private const uint RefreshInterval = 250;

    private readonly MyraLabel _dps;
    private readonly MyraLabel _dtps;
    private readonly MyraLabel _hps;
    private readonly MyraLabel _fight;
    private uint _lastRefresh;
    private uint _lastFightTick;

    public static void Ensure()
    {
        foreach (IGui gui in UIManager.Gumps)
            if (gui is CombatHudWindow)
                return;

        UIManager.Add(new CombatHudWindow());
    }

    public static void Show()
    {
        Ensure();
        ProfileManager.CurrentProfile.CombatHudVisible = true;

        foreach (IGui gui in UIManager.Gumps)
        {
            if (gui is not CombatHudWindow window) continue;
            window.IsVisible = true;
            window.BringOnTop();
            return;
        }
    }

    private CombatHudWindow() : base("Combat HUD")
    {
        CanBeSaved = true;
        _dps = new MyraLabel("DPS: 0.0", MyraLabel.TextStyle.H3);
        _dtps = new MyraLabel("DTPS: 0.0", MyraLabel.TextStyle.H3);
        _hps = new MyraLabel("HPS: 0.0", MyraLabel.TextStyle.H3);
        _fight = new MyraLabel("", MyraLabel.TextStyle.P);

        var root = new VerticalStackPanel
        {
            Spacing = 2,
            Padding = new Myra.Graphics2D.Thickness(5)
        };
        root.Widgets.Add(_dps);
        root.Widgets.Add(_dtps);
        root.Widgets.Add(_hps);
        root.Widgets.Add(_fight);
        root.Widgets.Add(new MyraButton("Open meter", CombatMeterWindow.Show));
        SetRootContent(root);
        SetPosition(20, 80);
    }

    public override void Update()
    {
        base.Update();
        if (Time.Ticks - _lastRefresh < RefreshInterval) return;
        _lastRefresh = Time.Ticks;

        Profile? profile = ProfileManager.CurrentProfile;
        if (profile == null || !profile.CombatMeterEnabled || !profile.CombatHudVisible)
        {
            IsVisible = false;
            return;
        }

        CombatTracker tracker = CombatTracker.Instance;
        if (tracker.InFight)
            _lastFightTick = Time.Ticks;

        bool withinAutoHide = _lastFightTick != 0
                              && Time.Ticks - _lastFightTick <= (uint)Math.Max(0, profile.CombatHudAutoHideDelay) * 1000;
        IsVisible = !profile.CombatHudAutoShow || tracker.InFight || withinAutoHide;
        if (!IsVisible) return;

        _dps.Text = $"DPS:  {tracker.GetDPS(15000),6:F1}";
        _dtps.Text = $"DTPS: {tracker.GetDTPS(15000),6:F1}";
        _hps.Text = $"HPS:  {tracker.GetHPS(15000),6:F1}";
        float seconds = tracker.GetCurrentFightDuration();
        _fight.Text = tracker.InFight ? $"Fight: {(int)seconds / 60}:{(int)seconds % 60:D2}" : "Fight ended";
    }
}
