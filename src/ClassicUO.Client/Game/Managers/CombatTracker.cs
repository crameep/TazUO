using System;
using System.Collections.Generic;
using System.Text.Json;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers
{
    internal enum CombatCategory : byte
    {
        Self = 0,
        Pet = 1,
        Ally = 2,
        LastTarget = 3,
        Other = 4
    }

    internal readonly struct CombatEvent
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

    internal class FightSummary
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

    internal struct TargetBreakdown
    {
        public uint Serial;
        public string Name;
        public int Dealt;
        public int Taken;
        public int Kills;
        public int HitCount;
        public float AvgHit => HitCount > 0 ? (float)Dealt / HitCount : 0;
    }

    internal struct TimelineBucket
    {
        public uint Timestamp;
        public int Dealt;
        public int Taken;
        public int Healed;
    }

    internal class CombatTracker
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
            _currentFightDealt = 0;
            _currentFightTaken = 0;
            _currentFightHealed = 0;
            _currentFightKills = 0;

            EventSink.OnEntityDamage -= OnEntityDamage;
            EventSink.OnEntityDamage += OnEntityDamage;
        }

        public void SetMaxEvents(int max) => _maxEvents = max;
        public void SetFightIdleThreshold(int ms) => _fightIdleThresholdMs = ms;

        public static void Reset()
        {
            if (_instance != null)
                EventSink.OnEntityDamage -= _instance.OnEntityDamage;
            _instance = null;
        }

        private void OnEntityDamage(object sender, int damage)
        {
            // Check if damage would bring the target to 0 HP (Hits hasn't been updated yet when this fires)
            if (sender is Mobile mobile && (mobile.Hits - damage) <= 0 && mobile.Serial == World.Instance?.TargetManager?.LastAttack)
            {
                RecordKill();
            }
        }

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

        public void RecordKill()
        {
            if (_inFight)
                _currentFightKills++;
        }

        private void AddEvent(CombatEvent evt)
        {
            uint now = evt.Timestamp;

            // Fight detection: start new fight if not in one, or if idle threshold exceeded
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

        public void Update()
        {
            if (_inFight && Time.Ticks - _lastEventTime > (uint)_fightIdleThresholdMs)
                EndCurrentFight(_lastEventTime);
        }

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
                if (e.Category == CombatCategory.Self) continue;

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

        public List<TimelineBucket> GetTimelineBuckets(uint windowMs, uint bucketSizeMs = 1000)
        {
            if (bucketSizeMs == 0) bucketSizeMs = 1000;

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

        public string ExportSessionJson()
        {
            var fightExports = new List<FightExport>(_fights.Count);
            for (int i = 0; i < _fights.Count; i++)
            {
                var f = _fights[i];
                fightExports.Add(new FightExport
                {
                    DurationSec = f.DurationSeconds,
                    Dealt = f.TotalDealt,
                    Taken = f.TotalTaken,
                    Healed = f.TotalHealed,
                    Kills = f.Kills,
                    DPS = f.DPS
                });
            }

            // Include current fight if active
            if (_inFight)
            {
                var current = GetCurrentFightSummary();
                if (current != null)
                {
                    fightExports.Add(new FightExport
                    {
                        DurationSec = current.DurationSeconds,
                        Dealt = current.TotalDealt,
                        Taken = current.TotalTaken,
                        Healed = current.TotalHealed,
                        Kills = current.Kills,
                        DPS = current.DPS
                    });
                }
            }

            var eventExports = new List<EventExport>(_events.Count);
            for (int i = 0; i < _events.Count; i++)
            {
                var e = _events[i];
                eventExports.Add(new EventExport
                {
                    TimestampMs = e.Timestamp > _sessionStart ? e.Timestamp - _sessionStart : 0,
                    Target = e.TargetName,
                    TargetSerial = e.TargetSerial,
                    Amount = e.Amount,
                    Category = e.Category.ToString(),
                    IsHeal = e.IsHeal
                });
            }

            var export = new SessionExport
            {
                ExportTime = DateTime.Now.ToString("o"),
                SessionDurationSec = SessionDuration,
                TotalDealt = TotalDealt,
                TotalTaken = TotalTaken,
                TotalHealed = TotalHealed,
                TotalKills = TotalKills,
                Fights = fightExports,
                Events = eventExports
            };

            return JsonSerializer.Serialize(export, CombatExportContext.Default.SessionExport);
        }
    }

    internal class SessionExport
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

    internal class FightExport
    {
        public float DurationSec { get; set; }
        public int Dealt { get; set; }
        public int Taken { get; set; }
        public int Healed { get; set; }
        public int Kills { get; set; }
        public float DPS { get; set; }
    }

    internal class EventExport
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
    [System.Text.Json.Serialization.JsonSerializable(typeof(FightExport))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(EventExport))]
    internal partial class CombatExportContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}
