# Combat Meter & Combat Log — Design Document

**Date:** 2026-02-27
**Branch:** TBD
**Status:** Approved

## Overview

A DPS meter, combat log, and combat analytics system for TazUO. Tracks all damage and healing events the client sees, provides real-time DPS/DTPS/HPS readouts, per-target breakdowns, a scrolling combat log, and a damage timeline graph. Includes both a compact HUD overlay for mid-combat glancing and a full analytics ImGui window.

## Use Cases

- **PvE:** Track DPS while farming, see kill times, compare weapon/spell effectiveness
- **PvP:** Monitor damage dealt/taken in fights, analyze burst windows, track healing received
- **General:** Session-level combat statistics with manual JSON export

## Architecture: Event-Sourced Combat Tracker (Approach A)

Every damage and heal event is recorded as an immutable `CombatEvent` in a time-indexed list. All analytics (DPS, fight detection, per-target breakdowns, graphs) are computed as queries over this event stream.

### Data Model

```csharp
struct CombatEvent
{
    long   Timestamp;      // Time.Ticks (ms)
    uint   TargetSerial;   // Who got hit/healed
    ushort Amount;         // Damage or heal amount
    byte   Category;       // Self=0, Pet=1, Ally=2, LastTarget=3, Other=4
    bool   IsHeal;         // false=damage, true=heal
}
```

- Cap at 10K events; when hit, drop oldest 2K
- ~48 bytes per event, ~480KB at cap

### Fight Detection

- Fight starts: first combat event after 10+ seconds idle (configurable)
- Fight ends: 10 seconds with no events
- Each fight stored as `FightSummary` with start/end timestamps, computed from event stream

### Damage Attribution

Two views:
1. **"Your Estimated DPS"** — damage where `Category == LastTarget` (heuristic: damage to your attack target is probably yours)
2. **"All Combat"** — everything categorized by who got hit (self, pets, allies, enemies)

### Healing Detection

HP update packets (`0xA1`) already detect `oldHits != entity.Hits`. When `entity.Hits > oldHits`, record a heal event with `Amount = entity.Hits - oldHits`.

## Core Components

### CombatTracker (Manager)

Singleton manager in `Game/Managers/CombatTracker.cs`.

**Event Sources:**
- `EventSink.OnEntityDamage` → record damage events
- `UpdateHitpoints` handler → detect HP increase → record heal events

**Data:**
- `List<CombatEvent> _events` (capped at 10K)
- `FightState _currentFight` (tracking current fight start/idle timer)
- `List<FightSummary> _fights` (completed fights)

**Queries (computed on demand):**
- `GetDPS(window)` → damage to last target / sec
- `GetDTPS(window)` → damage to self / sec
- `GetHPS(window)` → heals to self / sec
- `GetEventsInRange(start, end)`
- `GetPerTargetBreakdown(window)`
- `GetFights()` → fight list with summaries
- `ExportSession()` → JSON for manual save

### CombatHudOverlay (ImGui)

Small floating overlay, always visible during combat:

```
┌─────────────────────┐
│ DPS:  45.2  ▲       │
│ DTPS: 12.8  ▼       │
│ HPS:   8.3  +       │
│ Fight: 1:23         │
└─────────────────────┘
```

- No title bar, semi-transparent background
- Auto-show on fight start, fade after configurable delay (default 5s)
- Always-on toggle available
- Right-click opens full CombatMeterWindow
- Color-coded: DPS green, DTPS red, HPS blue

### CombatMeterWindow (ImGui)

Full analytics window with 3 tabs:

**Log Tab:**
- Scrolling event feed with timestamps
- Color-coded by category (same hues as overhead damage)
- Filter checkboxes: damage dealt / damage taken / heals / pets / allies
- Auto-scroll with pin-to-bottom toggle

**Per-Target Tab:**
- Sortable table: Target, Dealt, Taken, Kills, Avg Hit
- Click row to filter log to that target
- Time window selector: Current Fight / Last 30s / 1m / 5m / Session
- Session totals row

**Timeline Tab:**
- Bar chart via ImGui DrawList (1-second resolution)
- Green = damage dealt, Red = damage taken, Blue = healing
- Fight boundaries as vertical lines with labels
- Horizontal scroll for session history
- Hover for exact values

**Footer (all tabs):**
- Session duration, total damage dealt/taken/healed, total kills
- "Export Session" button → JSON to configurable path

## Profile Settings

```csharp
bool   CombatMeterEnabled = true;
bool   CombatHudVisible = true;
bool   CombatHudAutoShow = true;
int    CombatHudAutoHideDelay = 5;
int    CombatFightIdleThreshold = 10;
int    CombatMaxEvents = 10000;
string CombatExportPath = "CombatLogs";
```

## Integration Points

Only 3 touch points in existing code:

1. **`EventSink.OnEntityDamage`** — CombatTracker subscribes. No changes to existing code.
2. **`UpdateHitpoints.Receive()`** — Add one line: when `entity.Hits > oldHits`, call `CombatTracker.Instance.RecordHeal(entity, delta)`.
3. **`ImGuiManager`** — Register CombatHudOverlay and CombatMeterWindow (standard pattern).

## File Layout

```
src/ClassicUO.Client/
├── Game/
│   ├── Managers/
│   │   └── CombatTracker.cs
│   └── UI/
│       └── ImGuiControls/
│           ├── CombatMeterWindow.cs
│           └── CombatHudOverlay.cs
└── Configuration/
    └── Profile.cs                  # + new settings
```

3 new files, 1 small edit to UpdateHitpoints.cs, profile additions, ImGui registration.

## Future (Phase 2)

- Hybrid storage: add aggregated time buckets for long sessions (Approach C)
- Per-session persistence to disk
- More graph types (pie charts for damage distribution, etc.)
