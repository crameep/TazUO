# Combat Meter & Combat Log Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a DPS meter, combat log, and combat analytics system that tracks all damage and healing events with real-time stats, per-target breakdowns, a timeline graph, and manual JSON export.

**Architecture:** Event-sourced — every damage/heal is recorded as an immutable `CombatEvent` in a capped list. All analytics (DPS, fight detection, per-target breakdowns, graphs) are computed as queries over this event stream. UI is split into a compact HUD overlay and a full analytics ImGui window.

**Tech Stack:** C# / .NET 10, ImGui via ImGuiNET, existing EventSink event system, existing `Time.Ticks` for timestamps.

**Design doc:** `docs/plans/2026-02-27-combat-meter-design.md`

---

### Task 1: CombatTracker Data Model and Core Manager

**Files:**
- Create: `src/ClassicUO.Client/Game/Managers/CombatTracker.cs`

**Step 1: Create CombatEvent struct and CombatCategory enum**

```csharp
using System;
using System.Collections.Generic;

namespace ClassicUO.Game.Managers;

public enum CombatCategory : byte
{
    Self = 0,
    Pet = 1,
    Ally = 2,
    LastTarget = 3,
    Other = 4
}

public readonly struct CombatEvent
{
    public readonly uint Timestamp;
    public readonly uint TargetSerial;
    public readonly ushort Amount;
    public readonly CombatCategory Category;
    public readonly bool IsHeal;
    public readonly string TargetName;

    public CombatEvent(uint timestamp, uint targetSerial, ushort amount, CombatCategory category, bool isHeal, string targetName)
    {
        Timestamp = timestamp;
        TargetSerial = targetSerial;
        Amount = amount;
        Category = category;
        IsHeal = isHeal;
        TargetName = targetName ?? string.Empty;
    }
}
```

**Step 2: Create FightSummary and CombatTracker skeleton**

```csharp
public class FightSummary
{
    public uint StartTime;
    public uint EndTime;
    public int TotalDealt;
    public int TotalTaken;
    public int TotalHealed;
    public int Kills;

    public float DurationSeconds => Math.Max((EndTime - StartTime) / 1000f, 0.001f);
    public float DPS => TotalDealt / DurationSeconds;
    public float DTPS => TotalTaken / DurationSeconds;
}

public class CombatTracker
{
    private static CombatTracker _instance;
    public static CombatTracker Instance => _instance ??= new CombatTracker();

    private readonly List<CombatEvent> _events = new();
    private readonly List<FightSummary> _fights = new();

    private uint _lastEventTime;
    private uint _currentFightStart;
    private bool _inFight;
    private int _currentFightDealt;
    private int _currentFightTaken;
    private int _currentFightHealed;
    private int _currentFightKills;

    private uint _sessionStart;

    public IReadOnlyList<CombatEvent> Events => _events;
    public IReadOnlyList<FightSummary> Fights => _fights;
    public bool InFight => _inFight;
    public uint SessionStart => _sessionStart;

    private int _maxEvents = 10000;
    private int _fightIdleThresholdMs = 10000;

    public void Initialize()
    {
        _sessionStart = Time.Ticks;
        _events.Clear();
        _fights.Clear();
        _inFight = false;
        _lastEventTime = 0;
    }

    public void SetMaxEvents(int max) => _maxEvents = max;
    public void SetFightIdleThreshold(int ms) => _fightIdleThresholdMs = ms;

    public static void Reset()
    {
        _instance = null;
    }
}
```

**Step 3: Add RecordDamage and RecordHeal methods with fight detection**

Add these methods to `CombatTracker`:

```csharp
    public void RecordDamage(uint targetSerial, ushort amount, CombatCategory category, string targetName)
    {
        if (amount == 0) return;

        var evt = new CombatEvent(Time.Ticks, targetSerial, amount, category, false, targetName);
        AddEvent(evt);
    }

    public void RecordHeal(uint targetSerial, ushort amount, string targetName)
    {
        if (amount == 0) return;

        var category = CombatCategory.Other;
        if (targetSerial == World.Instance?.Player?.Serial)
            category = CombatCategory.Self;

        var evt = new CombatEvent(Time.Ticks, targetSerial, amount, category, true, targetName);
        AddEvent(evt);
    }

    private void AddEvent(CombatEvent evt)
    {
        uint now = evt.Timestamp;

        // Fight detection
        if (!_inFight || (now - _lastEventTime > (uint)_fightIdleThresholdMs))
        {
            // End previous fight if active
            if (_inFight)
                EndCurrentFight(_lastEventTime);

            // Start new fight
            _inFight = true;
            _currentFightStart = now;
            _currentFightDealt = 0;
            _currentFightTaken = 0;
            _currentFightHealed = 0;
            _currentFightKills = 0;
        }

        _lastEventTime = now;
        _events.Add(evt);

        // Track fight stats
        if (evt.IsHeal)
        {
            _currentFightHealed += evt.Amount;
        }
        else if (evt.Category == CombatCategory.Self)
        {
            _currentFightTaken += evt.Amount;
        }
        else if (evt.Category == CombatCategory.LastTarget)
        {
            _currentFightDealt += evt.Amount;
        }

        // Prune if over cap
        if (_events.Count > _maxEvents)
            _events.RemoveRange(0, 2000);
    }

    private void EndCurrentFight(uint endTime)
    {
        if (!_inFight) return;

        _fights.Add(new FightSummary
        {
            StartTime = _currentFightStart,
            EndTime = endTime,
            TotalDealt = _currentFightDealt,
            TotalTaken = _currentFightTaken,
            TotalHealed = _currentFightHealed,
            Kills = _currentFightKills
        });

        _inFight = false;
    }

    public void RecordKill()
    {
        if (_inFight)
            _currentFightKills++;
    }
```

**Step 4: Add Update method for idle fight detection**

```csharp
    public void Update()
    {
        if (_inFight && Time.Ticks - _lastEventTime > (uint)_fightIdleThresholdMs)
            EndCurrentFight(_lastEventTime);
    }
```

**Step 5: Add query methods**

```csharp
    public float GetDPS(uint windowMs)
    {
        uint now = Time.Ticks;
        uint cutoff = now > windowMs ? now - windowMs : 0;
        int total = 0;

        for (int i = _events.Count - 1; i >= 0; i--)
        {
            if (_events[i].Timestamp < cutoff) break;
            if (!_events[i].IsHeal && _events[i].Category == CombatCategory.LastTarget)
                total += _events[i].Amount;
        }

        float seconds = Math.Max(windowMs / 1000f, 1f);
        return total / seconds;
    }

    public float GetDTPS(uint windowMs)
    {
        uint now = Time.Ticks;
        uint cutoff = now > windowMs ? now - windowMs : 0;
        int total = 0;

        for (int i = _events.Count - 1; i >= 0; i--)
        {
            if (_events[i].Timestamp < cutoff) break;
            if (!_events[i].IsHeal && _events[i].Category == CombatCategory.Self)
                total += _events[i].Amount;
        }

        float seconds = Math.Max(windowMs / 1000f, 1f);
        return total / seconds;
    }

    public float GetHPS(uint windowMs)
    {
        uint now = Time.Ticks;
        uint cutoff = now > windowMs ? now - windowMs : 0;
        int total = 0;

        for (int i = _events.Count - 1; i >= 0; i--)
        {
            if (_events[i].Timestamp < cutoff) break;
            if (_events[i].IsHeal && _events[i].Category == CombatCategory.Self)
                total += _events[i].Amount;
        }

        float seconds = Math.Max(windowMs / 1000f, 1f);
        return total / seconds;
    }

    public float GetCurrentFightDuration()
    {
        if (!_inFight) return 0f;
        return (Time.Ticks - _currentFightStart) / 1000f;
    }

    public FightSummary GetCurrentFightSummary()
    {
        if (!_inFight) return null;
        return new FightSummary
        {
            StartTime = _currentFightStart,
            EndTime = Time.Ticks,
            TotalDealt = _currentFightDealt,
            TotalTaken = _currentFightTaken,
            TotalHealed = _currentFightHealed,
            Kills = _currentFightKills
        };
    }
```

**Step 6: Add per-target breakdown query**

```csharp
    public struct TargetBreakdown
    {
        public uint Serial;
        public string Name;
        public int Dealt;
        public int Taken;
        public int Kills;
        public int HitCount;
        public float AvgHit => HitCount > 0 ? (float)Dealt / HitCount : 0;
    }

    public List<TargetBreakdown> GetPerTargetBreakdown(uint windowMs)
    {
        uint now = Time.Ticks;
        uint cutoff = now > windowMs ? now - windowMs : 0;
        var map = new Dictionary<uint, TargetBreakdown>();

        for (int i = _events.Count - 1; i >= 0; i--)
        {
            var e = _events[i];
            if (e.Timestamp < cutoff) break;
            if (e.IsHeal) continue;
            if (e.Category == CombatCategory.Self) continue; // Self damage tracked separately

            if (!map.TryGetValue(e.TargetSerial, out var tb))
            {
                tb = new TargetBreakdown { Serial = e.TargetSerial, Name = e.TargetName };
            }

            tb.Dealt += e.Amount;
            tb.HitCount++;
            map[e.TargetSerial] = tb;
        }

        var result = new List<TargetBreakdown>(map.Values);
        result.Sort((a, b) => b.Dealt.CompareTo(a.Dealt));
        return result;
    }
```

**Step 7: Add session totals and export**

```csharp
    public int TotalDealt
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _events.Count; i++)
                if (!_events[i].IsHeal && _events[i].Category == CombatCategory.LastTarget)
                    total += _events[i].Amount;
            return total;
        }
    }

    public int TotalTaken
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _events.Count; i++)
                if (!_events[i].IsHeal && _events[i].Category == CombatCategory.Self)
                    total += _events[i].Amount;
            return total;
        }
    }

    public int TotalHealed
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _events.Count; i++)
                if (_events[i].IsHeal && _events[i].Category == CombatCategory.Self)
                    total += _events[i].Amount;
            return total;
        }
    }

    public int TotalKills
    {
        get
        {
            int total = 0;
            foreach (var f in _fights) total += f.Kills;
            if (_inFight) total += _currentFightKills;
            return total;
        }
    }

    public float SessionDuration => (Time.Ticks - _sessionStart) / 1000f;
```

**Step 8: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/CombatTracker.cs
git commit -m "feat: add CombatTracker data model and core manager"
```

---

### Task 2: Hook CombatTracker into Existing Event Pipeline

**Files:**
- Modify: `src/ClassicUO.Client/Game/GameObjects/EntityTextContainer.cs:77-114`
- Modify: `src/ClassicUO.Client/Network/PacketHandlers/UpdateHitpoints.cs:30-36`

**Step 1: Add CombatTracker recording to OverheadDamage.Add()**

In `EntityTextContainer.cs`, inside the `Add(int damage)` method, after the hue categorization logic (after line 98) and before the dps string (line 99), add CombatTracker recording. The category can be derived from the same hue logic:

After the existing hue assignment block (lines 83-98), add:

```csharp
            // Record to combat tracker
            var combatCategory = CombatCategory.Other;
            if (ReferenceEquals(Parent, _world.Player))
                combatCategory = CombatCategory.Self;
            else if (Parent is Mobile cmob)
            {
                if (cmob.Serial == _world.TargetManager.LastAttack)
                    combatCategory = CombatCategory.LastTarget;
                else if (cmob.NotorietyFlag == NotorietyFlag.Ally)
                    combatCategory = CombatCategory.Ally;
                else if (cmob.IsRenamable && cmob.NotorietyFlag != NotorietyFlag.Invulnerable && cmob.NotorietyFlag != NotorietyFlag.Enemy)
                    combatCategory = CombatCategory.Pet;
            }
            CombatTracker.Instance.RecordDamage(Parent.Serial, (ushort)damage, combatCategory, name);
```

Note: This must go after the `name` variable is set (line 84/90) and after the hue categorization. Place it right before line 99 (`string dps = ...`).

**Step 2: Add heal recording to UpdateHitpoints handler**

In `UpdateHitpoints.cs`, after the existing bandage check block (lines 31-35), add:

```csharp
        // Record heals to combat tracker
        if (SerialHelper.IsMobile(entity.Serial) && entity.Hits > oldHits)
        {
            ushort healAmount = (ushort)(entity.Hits - oldHits);
            string healName = (entity as Mobile)?.Name ?? string.Empty;
            CombatTracker.Instance.RecordHeal(entity.Serial, healAmount, healName);
        }
```

Add `using ClassicUO.Game.Managers;` to the top of UpdateHitpoints.cs if not already present.

**Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/GameObjects/EntityTextContainer.cs src/ClassicUO.Client/Network/PacketHandlers/UpdateHitpoints.cs
git commit -m "feat: hook CombatTracker into damage and heal event pipelines"
```

---

### Task 3: Profile Settings for Combat Meter

**Files:**
- Modify: `src/ClassicUO.Client/Configuration/Profile.cs:451-452`

**Step 1: Add combat meter settings to Profile.cs**

After the `ShowDPS` property (line 451) and before the `#endregion` on line 452, add:

```csharp

        // Combat Meter
        public bool CombatMeterEnabled { get; set; } = true;
        public bool CombatHudVisible { get; set; } = true;
        public bool CombatHudAutoShow { get; set; } = true;
        public int CombatHudAutoHideDelay { get; set; } = 5;
        public int CombatFightIdleThreshold { get; set; } = 10;
        public int CombatMaxEvents { get; set; } = 10000;
        public string CombatExportPath { get; set; } = "CombatLogs";
```

**Step 2: Commit**

```bash
git add src/ClassicUO.Client/Configuration/Profile.cs
git commit -m "feat: add combat meter profile settings"
```

---

### Task 4: CombatTracker Initialization and Update Loop

**Files:**
- Modify: Find where other managers are initialized on login/world load (search for `EventSink.OnPlayerCreated` subscriptions or game scene initialization)

**Step 1: Find the initialization point**

Search for where managers like `BandageManager` are initialized. The CombatTracker needs:
- `Initialize()` called when the player logs in
- `Update()` called each game tick
- `Reset()` called on disconnect

Look at `GameScene.cs` or wherever `EventSink.OnPlayerCreated` is handled. Subscribe:

```csharp
EventSink.OnPlayerCreated += (_, _) =>
{
    CombatTracker.Instance.Initialize();
    var profile = ProfileManager.CurrentProfile;
    if (profile != null)
    {
        CombatTracker.Instance.SetMaxEvents(profile.CombatMaxEvents);
        CombatTracker.Instance.SetFightIdleThreshold(profile.CombatFightIdleThreshold * 1000);
    }
};
```

Add `CombatTracker.Instance.Update()` to the game update loop (wherever `WorldTextManager.Update()` or similar is called in the game tick).

Add `CombatTracker.Reset()` on disconnect (where `EventSink.OnDisconnected` is handled).

**Step 2: Commit**

```bash
git add -A
git commit -m "feat: initialize and update CombatTracker in game loop"
```

---

### Task 5: Combat HUD Overlay

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/ImGuiControls/CombatHudOverlay.cs`

**Step 1: Create the HUD overlay ImGui window**

```csharp
using System.Numerics;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls;

public class CombatHudOverlay : SingletonImGuiWindow<CombatHudOverlay>
{
    private float _hideTimer;
    private bool _wasInFight;
    private const uint DPS_WINDOW_MS = 15000;

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
                _hideTimer = profile.CombatHudAutoHideDelay;
                _wasInFight = true;
                IsVisible = true;
            }
            else if (_wasInFight)
            {
                _hideTimer -= Time.Delta;
                if (_hideTimer <= 0)
                {
                    _wasInFight = false;
                    IsVisible = false;
                }
            }
            else
            {
                IsVisible = false;
            }
        }
        else
        {
            IsVisible = true;
        }

        if (!IsVisible) return;

        ImGui.SetNextWindowSize(new Vector2(160, 0), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.7f);
        base.Draw();
    }

    public override void DrawContent()
    {
        var tracker = CombatTracker.Instance;

        float dps = tracker.GetDPS(DPS_WINDOW_MS);
        float dtps = tracker.GetDTPS(DPS_WINDOW_MS);
        float hps = tracker.GetHPS(DPS_WINDOW_MS);
        float fightDur = tracker.GetCurrentFightDuration();

        // DPS - green
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1.0f, 0.4f, 1.0f));
        ImGui.Text($"DPS:  {dps:F1}");
        ImGui.PopStyleColor();

        // DTPS - red
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.4f, 0.4f, 1.0f));
        ImGui.Text($"DTPS: {dtps:F1}");
        ImGui.PopStyleColor();

        // HPS - blue
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.6f, 1.0f, 1.0f));
        ImGui.Text($"HPS:  {hps:F1}");
        ImGui.PopStyleColor();

        // Fight timer
        if (tracker.InFight)
        {
            int mins = (int)(fightDur / 60);
            int secs = (int)(fightDur % 60);
            ImGui.Text($"Fight: {mins}:{secs:D2}");
        }

        // Right-click hint
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            CombatMeterWindow.Show();
        }
    }
}
```

**Step 2: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/ImGuiControls/CombatHudOverlay.cs
git commit -m "feat: add compact combat HUD overlay"
```

---

### Task 6: Combat Meter Window — Log Tab

**Files:**
- Create: `src/ClassicUO.Client/Game/UI/ImGuiControls/CombatMeterWindow.cs`

**Step 1: Create CombatMeterWindow with log tab**

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls;

public class CombatMeterWindow : SingletonImGuiWindow<CombatMeterWindow>
{
    // Filter state
    private bool _showDamageDealt = true;
    private bool _showDamageTaken = true;
    private bool _showHeals = true;
    private bool _showPets = true;
    private bool _showAllies = true;
    private bool _pinToBottom = true;
    private uint? _filterTargetSerial;

    // Time window selection for per-target tab
    private int _selectedTimeWindow; // 0=current fight, 1=30s, 2=1m, 3=5m, 4=session
    private static readonly string[] TimeWindowLabels = { "Current Fight", "30s", "1m", "5m", "Session" };
    private static readonly uint[] TimeWindowMs = { 0, 30000, 60000, 300000, uint.MaxValue };

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
        if (ImGui.BeginTabBar("CombatTabs"))
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
        // Filters
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
            if (ImGui.SmallButton("Clear filter"))
                _filterTargetSerial = null;
        }

        ImGui.Separator();

        // Scrolling log
        if (ImGui.BeginChild("LogScroll", new Vector2(0, -1), ImGuiChildFlags.None))
        {
            var events = CombatTracker.Instance.Events;

            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];

                if (!PassesFilter(e)) continue;

                Vector4 color = GetEventColor(e);
                string text = FormatEvent(e);

                ImGui.PushStyleColor(ImGuiCol.Text, color);
                ImGui.TextWrapped(text);
                ImGui.PopStyleColor();
            }

            if (_pinToBottom && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20)
                ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();
    }

    private bool PassesFilter(CombatEvent e)
    {
        if (_filterTargetSerial.HasValue && e.TargetSerial != _filterTargetSerial.Value)
            return false;

        if (e.IsHeal) return _showHeals;

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
        if (e.IsHeal) return new Vector4(0.4f, 0.6f, 1.0f, 1.0f); // Blue

        return e.Category switch
        {
            CombatCategory.Self => new Vector4(1.0f, 0.4f, 0.4f, 1.0f),       // Red
            CombatCategory.LastTarget => new Vector4(0.4f, 1.0f, 0.4f, 1.0f), // Green
            CombatCategory.Pet => new Vector4(0.4f, 1.0f, 1.0f, 1.0f),        // Cyan
            CombatCategory.Ally => new Vector4(0.6f, 0.6f, 1.0f, 1.0f),       // Light blue
            _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)                          // White
        };
    }

    private static string FormatEvent(CombatEvent e)
    {
        // Convert timestamp to relative time
        uint sessionTime = e.Timestamp - CombatTracker.Instance.SessionStart;
        int totalSec = (int)(sessionTime / 1000);
        int min = totalSec / 60;
        int sec = totalSec % 60;
        string time = $"[{min}:{sec:D2}]";

        if (e.IsHeal)
        {
            string target = e.Category == CombatCategory.Self ? "You" : e.TargetName;
            return $"{time} {target} healed for {e.Amount}";
        }

        if (e.Category == CombatCategory.Self)
            return $"{time} {e.TargetName} hits you for {e.Amount}";

        return $"{time} You hit {e.TargetName} for {e.Amount}";
    }
```

**Step 2: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/ImGuiControls/CombatMeterWindow.cs
git commit -m "feat: add CombatMeterWindow with log tab"
```

---

### Task 7: Combat Meter Window — Per-Target Tab

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/ImGuiControls/CombatMeterWindow.cs`

**Step 1: Implement DrawPerTargetTab()**

Add this method to `CombatMeterWindow`:

```csharp
    private void DrawPerTargetTab()
    {
        // Time window selector
        ImGui.Text("Window:");
        ImGui.SameLine();
        for (int i = 0; i < TimeWindowLabels.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            bool selected = _selectedTimeWindow == i;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Current.Primary);
            if (ImGui.SmallButton(TimeWindowLabels[i]))
                _selectedTimeWindow = i;
            if (selected) ImGui.PopStyleColor();
        }

        ImGui.Separator();

        uint windowMs = _selectedTimeWindow == 0
            ? (CombatTracker.Instance.InFight ? (Time.Ticks - CombatTracker.Instance.GetCurrentFightSummary()?.StartTime ?? Time.Ticks) : 0)
            : TimeWindowMs[_selectedTimeWindow];

        if (windowMs == uint.MaxValue)
            windowMs = (uint)(Time.Ticks - CombatTracker.Instance.SessionStart);

        var breakdown = CombatTracker.Instance.GetPerTargetBreakdown(windowMs);

        if (ImGui.BeginTable("PerTarget", 5, ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.DefaultSort, 0.35f);
            ImGui.TableSetupColumn("Dealt", ImGuiTableColumnFlags.None, 0.18f);
            ImGui.TableSetupColumn("Taken", ImGuiTableColumnFlags.None, 0.18f);
            ImGui.TableSetupColumn("Kills", ImGuiTableColumnFlags.None, 0.12f);
            ImGui.TableSetupColumn("Avg Hit", ImGuiTableColumnFlags.None, 0.17f);
            ImGui.TableHeadersRow();

            int totalDealt = 0, totalTaken = 0, totalKills = 0, totalHits = 0;

            foreach (var tb in breakdown)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                // Clickable name to filter log
                if (ImGui.Selectable(string.IsNullOrEmpty(tb.Name) ? $"0x{tb.Serial:X8}" : tb.Name, false))
                    _filterTargetSerial = tb.Serial;

                ImGui.TableNextColumn();
                ImGui.Text(tb.Dealt.ToString("N0"));
                ImGui.TableNextColumn();
                ImGui.Text(tb.Taken.ToString("N0"));
                ImGui.TableNextColumn();
                ImGui.Text(tb.Kills.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(tb.AvgHit.ToString("F1"));

                totalDealt += tb.Dealt;
                totalHits += tb.HitCount;
            }

            // Totals row
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0.6f, 1f));
            ImGui.Text("Session Total");
            ImGui.TableNextColumn();
            ImGui.Text(totalDealt.ToString("N0"));
            ImGui.TableNextColumn();
            ImGui.Text("-");
            ImGui.TableNextColumn();
            ImGui.Text("-");
            ImGui.TableNextColumn();
            ImGui.Text(totalHits > 0 ? ((float)totalDealt / totalHits).ToString("F1") : "-");
            ImGui.PopStyleColor();

            ImGui.EndTable();
        }
    }
```

**Step 2: Commit**

```bash
git add src/ClassicUO.Client/Game/UI/ImGuiControls/CombatMeterWindow.cs
git commit -m "feat: add per-target breakdown tab to combat meter"
```

---

### Task 8: Combat Meter Window — Timeline Tab

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/ImGuiControls/CombatMeterWindow.cs`

**Step 1: Add GetTimelineBuckets query to CombatTracker**

Add to `CombatTracker.cs`:

```csharp
    public struct TimelineBucket
    {
        public uint Timestamp;
        public int Dealt;
        public int Taken;
        public int Healed;
    }

    public List<TimelineBucket> GetTimelineBuckets(uint windowMs, uint bucketSizeMs = 1000)
    {
        uint now = Time.Ticks;
        uint cutoff = now > windowMs ? now - windowMs : 0;
        var buckets = new Dictionary<uint, TimelineBucket>();

        for (int i = _events.Count - 1; i >= 0; i--)
        {
            var e = _events[i];
            if (e.Timestamp < cutoff) break;

            uint bucketKey = (e.Timestamp - cutoff) / bucketSizeMs;

            if (!buckets.TryGetValue(bucketKey, out var b))
                b = new TimelineBucket { Timestamp = cutoff + bucketKey * bucketSizeMs };

            if (e.IsHeal)
                b.Healed += e.Amount;
            else if (e.Category == CombatCategory.Self)
                b.Taken += e.Amount;
            else if (e.Category == CombatCategory.LastTarget)
                b.Dealt += e.Amount;

            buckets[bucketKey] = b;
        }

        var result = new List<TimelineBucket>(buckets.Values);
        result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }
```

**Step 2: Implement DrawTimelineTab()**

Add to `CombatMeterWindow`:

```csharp
    private float _timelineScrollX;

    private void DrawTimelineTab()
    {
        var tracker = CombatTracker.Instance;
        uint sessionMs = (uint)(Time.Ticks - tracker.SessionStart);
        if (sessionMs < 1000) { ImGui.Text("Waiting for data..."); return; }

        uint windowMs = Math.Min(sessionMs, 300000); // Show last 5 minutes max
        var buckets = tracker.GetTimelineBuckets(windowMs);

        if (buckets.Count == 0) { ImGui.Text("No combat data yet."); return; }

        // Find max value for scaling
        int maxVal = 1;
        foreach (var b in buckets)
        {
            maxVal = Math.Max(maxVal, Math.Max(b.Dealt, Math.Max(b.Taken, b.Healed)));
        }

        float availWidth = ImGui.GetContentRegionAvail().X;
        float availHeight = ImGui.GetContentRegionAvail().Y - 30; // Leave room for legend
        float barWidth = 8f;
        float barSpacing = 2f;
        float totalWidth = buckets.Count * (barWidth * 3 + barSpacing);

        // Draw legend
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1.0f, 0.4f, 1.0f));
        ImGui.Text("Dealt");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.4f, 0.4f, 1.0f));
        ImGui.Text("Taken");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.6f, 1.0f, 1.0f));
        ImGui.Text("Healed");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.Text($"  Peak: {maxVal}");

        // Draw bars using DrawList
        if (ImGui.BeginChild("TimelineScroll", new Vector2(0, availHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            var drawList = ImGui.GetWindowDrawList();
            var cursor = ImGui.GetCursorScreenPos();
            float baseY = cursor.Y + availHeight - 20;

            // Fight boundary lines
            foreach (var fight in tracker.Fights)
            {
                float startX = cursor.X + ((fight.StartTime - (Time.Ticks - windowMs)) / 1000f) * (barWidth * 3 + barSpacing);
                float endX = cursor.X + ((fight.EndTime - (Time.Ticks - windowMs)) / 1000f) * (barWidth * 3 + barSpacing);

                drawList.AddLine(new Vector2(startX, cursor.Y), new Vector2(startX, baseY), ImGui.GetColorU32(new Vector4(1, 1, 0, 0.5f)));
                drawList.AddLine(new Vector2(endX, cursor.Y), new Vector2(endX, baseY), ImGui.GetColorU32(new Vector4(1, 1, 0, 0.3f)));
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                var b = buckets[i];
                float x = cursor.X + i * (barWidth * 3 + barSpacing);

                float dealtH = (b.Dealt / (float)maxVal) * (availHeight - 20);
                float takenH = (b.Taken / (float)maxVal) * (availHeight - 20);
                float healH = (b.Healed / (float)maxVal) * (availHeight - 20);

                // Dealt (green)
                if (dealtH > 0)
                    drawList.AddRectFilled(
                        new Vector2(x, baseY - dealtH),
                        new Vector2(x + barWidth, baseY),
                        ImGui.GetColorU32(new Vector4(0.4f, 1.0f, 0.4f, 0.8f)));

                // Taken (red)
                if (takenH > 0)
                    drawList.AddRectFilled(
                        new Vector2(x + barWidth, baseY - takenH),
                        new Vector2(x + barWidth * 2, baseY),
                        ImGui.GetColorU32(new Vector4(1.0f, 0.4f, 0.4f, 0.8f)));

                // Healed (blue)
                if (healH > 0)
                    drawList.AddRectFilled(
                        new Vector2(x + barWidth * 2, baseY - healH),
                        new Vector2(x + barWidth * 3, baseY),
                        ImGui.GetColorU32(new Vector4(0.4f, 0.6f, 1.0f, 0.8f)));

                // Hover tooltip
                ImGui.SetCursorScreenPos(new Vector2(x, cursor.Y));
                ImGui.InvisibleButton($"bar_{i}", new Vector2(barWidth * 3, availHeight - 20));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Dealt: {b.Dealt}\nTaken: {b.Taken}\nHealed: {b.Healed}");
                }
            }

            // Set dummy to force scroll area
            ImGui.SetCursorScreenPos(new Vector2(cursor.X + totalWidth, cursor.Y));
            ImGui.Dummy(new Vector2(1, 1));
        }
        ImGui.EndChild();
    }
```

**Step 2: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/CombatTracker.cs src/ClassicUO.Client/Game/UI/ImGuiControls/CombatMeterWindow.cs
git commit -m "feat: add timeline graph tab to combat meter"
```

---

### Task 9: Combat Meter Window — Footer and Export

**Files:**
- Modify: `src/ClassicUO.Client/Game/UI/ImGuiControls/CombatMeterWindow.cs`
- Modify: `src/ClassicUO.Client/Game/Managers/CombatTracker.cs`

**Step 1: Add JSON export to CombatTracker**

Add to `CombatTracker.cs`:

```csharp
    public string ExportSessionJson()
    {
        var export = new SessionExport
        {
            ExportTime = DateTime.Now.ToString("o"),
            SessionDurationSec = SessionDuration,
            TotalDealt = TotalDealt,
            TotalTaken = TotalTaken,
            TotalHealed = TotalHealed,
            TotalKills = TotalKills,
            Fights = new List<FightExport>(),
            Events = new List<EventExport>()
        };

        foreach (var f in _fights)
        {
            export.Fights.Add(new FightExport
            {
                DurationSec = f.DurationSeconds,
                Dealt = f.TotalDealt,
                Taken = f.TotalTaken,
                Healed = f.TotalHealed,
                Kills = f.Kills,
                DPS = f.DPS
            });
        }

        foreach (var e in _events)
        {
            export.Events.Add(new EventExport
            {
                TimestampMs = e.Timestamp - _sessionStart,
                Target = e.TargetName,
                TargetSerial = e.TargetSerial,
                Amount = e.Amount,
                Category = e.Category.ToString(),
                IsHeal = e.IsHeal
            });
        }

        return System.Text.Json.JsonSerializer.Serialize(export, CombatExportContext.Default.SessionExport);
    }
```

Add the export data classes and JSON context at the bottom of `CombatTracker.cs`:

```csharp
public class SessionExport
{
    public string ExportTime { get; set; }
    public float SessionDurationSec { get; set; }
    public int TotalDealt { get; set; }
    public int TotalTaken { get; set; }
    public int TotalHealed { get; set; }
    public int TotalKills { get; set; }
    public List<FightExport> Fights { get; set; }
    public List<EventExport> Events { get; set; }
}

public class FightExport
{
    public float DurationSec { get; set; }
    public int Dealt { get; set; }
    public int Taken { get; set; }
    public int Healed { get; set; }
    public int Kills { get; set; }
    public float DPS { get; set; }
}

public class EventExport
{
    public uint TimestampMs { get; set; }
    public string Target { get; set; }
    public uint TargetSerial { get; set; }
    public ushort Amount { get; set; }
    public string Category { get; set; }
    public bool IsHeal { get; set; }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(SessionExport))]
internal partial class CombatExportContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
```

**Step 2: Implement DrawFooter()**

Add to `CombatMeterWindow`:

```csharp
    private void DrawFooter()
    {
        var tracker = CombatTracker.Instance;

        float dur = tracker.SessionDuration;
        int min = (int)(dur / 60);
        int sec = (int)(dur % 60);

        ImGui.Text($"Session: {min}:{sec:D2}");
        ImGui.SameLine();
        ImGui.Text($"| Dealt: {tracker.TotalDealt:N0}");
        ImGui.SameLine();
        ImGui.Text($"| Taken: {tracker.TotalTaken:N0}");
        ImGui.SameLine();
        ImGui.Text($"| Healed: {tracker.TotalHealed:N0}");
        ImGui.SameLine();
        ImGui.Text($"| Kills: {tracker.TotalKills}");

        ImGui.SameLine();
        if (ImGui.SmallButton("Export"))
        {
            try
            {
                var profile = ProfileManager.CurrentProfile;
                string dir = profile?.CombatExportPath ?? "CombatLogs";
                if (!System.IO.Path.IsPathRooted(dir))
                    dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir);

                System.IO.Directory.CreateDirectory(dir);
                string filename = $"combat_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json";
                string path = System.IO.Path.Combine(dir, filename);
                System.IO.File.WriteAllText(path, tracker.ExportSessionJson());
                GameActions.Print($"Combat log exported to {path}", 0x0044);
            }
            catch (Exception ex)
            {
                GameActions.Print($"Export failed: {ex.Message}", 0x0021);
            }
        }
    }
```

**Step 3: Commit**

```bash
git add src/ClassicUO.Client/Game/Managers/CombatTracker.cs src/ClassicUO.Client/Game/UI/ImGuiControls/CombatMeterWindow.cs
git commit -m "feat: add session footer and JSON export to combat meter"
```

---

### Task 10: Register ImGui Windows and Add Kill Tracking

**Files:**
- Find and modify: wherever ImGui singletons or the HUD is registered (likely `GameScene.cs` or `ImGuiManager` initialization)
- Modify: `src/ClassicUO.Client/Game/Managers/CombatTracker.cs` (subscribe to death events for kill counting)

**Step 1: Register CombatHudOverlay as a singleton**

The HUD overlay should be created when the player logs in, alongside CombatTracker initialization. In the same place where `CombatTracker.Instance.Initialize()` is called (from Task 4), also do:

```csharp
CombatHudOverlay.GetInstance(); // Registers singleton via constructor
```

**Step 2: Add a way to open CombatMeterWindow**

Add a menu item or keyboard shortcut to open the full window. The simplest approach is to add it where other windows are opened (e.g., in the game menu or via right-click on the HUD overlay, which is already implemented).

Also consider adding it to the Assistant Window or options menu. Look at how `ScriptManagerWindow.Show()` is called and follow the same pattern.

**Step 3: Hook kill detection**

In `CombatTracker`, subscribe to a death/destroy event when a LastTarget entity dies. The simplest approach: when a damage event causes the target entity's HP to reach 0 (or the entity is destroyed shortly after being our last target), increment kills.

Alternative: check in `Update()` if the last attack target is now dead.

```csharp
    // In CombatTracker, add to Initialize():
    EventSink.OnEntityDamage += OnEntityDamage;

    private void OnEntityDamage(object sender, int damage)
    {
        if (sender is Mobile mobile && mobile.Hits <= 0 && mobile.Serial == World.Instance?.TargetManager?.LastAttack)
        {
            RecordKill();
        }
    }
```

Note: Unsubscribe in `Reset()`:
```csharp
    EventSink.OnEntityDamage -= _instance?.OnEntityDamage;
```

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: register combat meter windows and add kill tracking"
```

---

### Task 11: Build Verification and Polish

**Step 1: Build the project**

```bash
dotnet build src/ClassicUO.Client/ClassicUO.Client.csproj -c Debug
```

Fix any compilation errors.

**Step 2: Review all new files for consistency**

- Ensure all `using` statements are correct
- Ensure JSON serialization context is properly defined (per CLAUDE.md requirement)
- Ensure no license headers on new files (per CLAUDE.md)
- Check that `CombatTracker.Reset()` properly cleans up event subscriptions
- Verify `CombatHudOverlay.Dispose()` is handled by base class

**Step 3: Test in-game**

- Open the game, verify HUD overlay appears when combat starts
- Verify damage events appear in the log tab
- Verify per-target breakdown populates
- Verify timeline renders bars
- Verify export creates a valid JSON file
- Verify HUD auto-hides after fight ends

**Step 4: Final commit**

```bash
git add -A
git commit -m "fix: build fixes and polish for combat meter"
```

---

## Summary

| Task | Description | New Files | Modified Files |
|------|------------|-----------|----------------|
| 1 | CombatTracker data model + core | CombatTracker.cs | — |
| 2 | Hook into damage/heal pipeline | — | EntityTextContainer.cs, UpdateHitpoints.cs |
| 3 | Profile settings | — | Profile.cs |
| 4 | Init + update loop | — | GameScene.cs or similar |
| 5 | Combat HUD overlay | CombatHudOverlay.cs | — |
| 6 | Combat meter log tab | CombatMeterWindow.cs | — |
| 7 | Per-target tab | — | CombatMeterWindow.cs |
| 8 | Timeline tab | — | CombatMeterWindow.cs, CombatTracker.cs |
| 9 | Footer + export | — | CombatMeterWindow.cs, CombatTracker.cs |
| 10 | Registration + kill tracking | — | GameScene.cs, CombatTracker.cs |
| 11 | Build + polish | — | Various |
