using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Game;

namespace ClassicUO.LegionScripting.Runtime;

internal readonly struct ScriptPosition
{
    public ScriptPosition(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public int X { get; }
    public int Y { get; }
    public int Z { get; }
}

internal sealed class ScriptPlayerSnapshot
{
    public ScriptPlayerSnapshot(uint serial, int hits, int hitsMax, int mana, int manaMax, int stamina, int staminaMax, ScriptPosition position)
    {
        Serial = serial;
        Hits = hits;
        HitsMax = hitsMax;
        Mana = mana;
        ManaMax = manaMax;
        Stamina = stamina;
        StaminaMax = staminaMax;
        Position = position;
    }

    public uint Serial { get; }
    public int Hits { get; }
    public int HitsMax { get; }
    public int Mana { get; }
    public int ManaMax { get; }
    public int Stamina { get; }
    public int StaminaMax { get; }
    public ScriptPosition Position { get; }
}

internal sealed class ScriptMobileSnapshot
{
    public ScriptMobileSnapshot(uint serial, ushort graphic, int notoriety, int distance, int hits, int hitsMax, ScriptPosition position)
    {
        Serial = serial;
        Graphic = graphic;
        Notoriety = notoriety;
        Distance = distance;
        Hits = hits;
        HitsMax = hitsMax;
        Position = position;
    }

    public uint Serial { get; }
    public ushort Graphic { get; }
    public int Notoriety { get; }
    public int Distance { get; }
    public int Hits { get; }
    public int HitsMax { get; }
    public ScriptPosition Position { get; }
}

internal sealed class ScriptItemSnapshot
{
    public ScriptItemSnapshot(uint serial, ushort graphic, ushort hue, int amount, int distance, ScriptPosition position)
    {
        Serial = serial;
        Graphic = graphic;
        Hue = hue;
        Amount = amount;
        Distance = distance;
        Position = position;
    }

    public uint Serial { get; }
    public ushort Graphic { get; }
    public ushort Hue { get; }
    public int Amount { get; }
    public int Distance { get; }
    public ScriptPosition Position { get; }
}

internal sealed class ScriptWorldSnapshot
{
    public static ScriptWorldSnapshot Empty { get; } = new(0, null, Array.Empty<ScriptMobileSnapshot>(), Array.Empty<ScriptItemSnapshot>());

    public ScriptWorldSnapshot(long tick, ScriptPlayerSnapshot player, IEnumerable<ScriptMobileSnapshot> mobiles, IEnumerable<ScriptItemSnapshot> items)
    {
        Tick = tick;
        Player = player;
        Mobiles = (mobiles ?? Enumerable.Empty<ScriptMobileSnapshot>()).ToArray();
        Items = (items ?? Enumerable.Empty<ScriptItemSnapshot>()).ToArray();
    }

    public long Tick { get; }
    public ScriptPlayerSnapshot Player { get; }
    public IReadOnlyList<ScriptMobileSnapshot> Mobiles { get; }
    public IReadOnlyList<ScriptItemSnapshot> Items { get; }

    public static ScriptWorldSnapshot Create(World world, long tick, int maxMobiles = 64, int maxItems = 128)
    {
        if (world?.Player == null)
            return new ScriptWorldSnapshot(tick, null, Array.Empty<ScriptMobileSnapshot>(), Array.Empty<ScriptItemSnapshot>());

        ScriptPosition ToPosition(ushort x, ushort y, sbyte z) => new(x, y, z);

        ScriptPlayerSnapshot player = new(
            world.Player.Serial,
            world.Player.Hits,
            world.Player.HitsMax,
            world.Player.Mana,
            world.Player.ManaMax,
            world.Player.Stamina,
            world.Player.StaminaMax,
            ToPosition(world.Player.X, world.Player.Y, world.Player.Z));

        ScriptMobileSnapshot[] mobiles = world.Mobiles.Values
            .Where(m => m != null && m.Serial != world.Player.Serial)
            .OrderBy(m => m.Distance)
            .ThenBy(m => m.Serial)
            .Take(Math.Max(0, maxMobiles))
            .Select(m => new ScriptMobileSnapshot(
                m.Serial,
                m.Graphic,
                (int)m.NotorietyFlag,
                m.Distance,
                m.Hits,
                m.HitsMax,
                ToPosition(m.X, m.Y, m.Z)))
            .ToArray();

        ScriptItemSnapshot[] items = world.Items.Values
            .Where(i => i != null && i.Container == 0)
            .OrderBy(i => i.Distance)
            .ThenBy(i => i.Serial)
            .Take(Math.Max(0, maxItems))
            .Select(i => new ScriptItemSnapshot(
                i.Serial,
                i.Graphic,
                i.Hue,
                i.Amount,
                i.Distance,
                ToPosition(i.X, i.Y, i.Z)))
            .ToArray();

        return new ScriptWorldSnapshot(tick, player, mobiles, items);
    }
}
