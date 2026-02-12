using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game;

namespace ClassicUO.Game.Managers;

/// <summary>
/// Dedicated queue for corpse double-click opens, decoupled from the ObjectActionQueue.
/// In UO, double-clicks and item moves are independent server-side actions with separate
/// rate limits. Running them through the same queue and cooldown caused corpse opens to
/// block item looting (and vice-versa), making auto-loot feel sluggish near multiple corpses.
/// </summary>
public static class CorpseOpenQueue
{
    private static readonly Queue<uint> _queue = new();
    private static long _nextOpenTime;

    private static long Cooldown => ProfileManager.CurrentProfile?.MoveMultiObjectDelay ?? 1000;

    public static void Enqueue(uint serial)
    {
        if (serial == 0) return;
        _queue.Enqueue(serial);
    }

    public static void Clear() => _queue.Clear();

    public static void Update()
    {
        if (_queue.Count == 0 || Time.Ticks < _nextOpenTime) return;

        uint serial = _queue.Dequeue();

        GameActions.DoubleClick(World.Instance, serial, ignoreWarMode: false, ignoreQueue: true);
        _nextOpenTime = Time.Ticks + Cooldown;
    }
}
