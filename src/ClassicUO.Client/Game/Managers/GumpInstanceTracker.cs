using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers;

/// <summary>
/// Tracks Gump instances by their concrete runtime type.
/// Register/Unregister from the base Gump class so subclasses are tracked automatically.
/// </summary>
public static class GumpInstanceTracker
{
    private static readonly Dictionary<Type, List<Gump>> _items = new();
    private static readonly Lock _lock = new();

    public static void Register(Gump item)
    {
        lock (_lock)
        {
            Type type = item.GetType();
            if (!_items.TryGetValue(type, out List<Gump> list))
            {
                list = new List<Gump>();
                _items[type] = list;
            }
            list.Add(item);
        }
    }

    public static void Unregister(Gump item)
    {
        lock (_lock)
        {
            if (_items.TryGetValue(item.GetType(), out List<Gump> list))
                list.Remove(item);
        }
    }

    /// <summary>
    /// Iterate through a snapshot of all live instances of the given type.
    /// Disposed instances are pruned automatically before this iteration.
    /// </summary>
    /// <returns>True if any instances existed</returns>
    public static bool ForEach<T>(Action<T> action, uint? serial = null) where T : Gump
    {
        Gump[] snapshot;
        int count;

        lock (_lock)
        {
            if (!_items.TryGetValue(typeof(T), out List<Gump> list))
                return false;

            list.RemoveAll(i => i.IsDisposed);
            count = list.Count;
            if (count == 0) return false;

            snapshot = ArrayPool<Gump>.Shared.Rent(count);
            list.CopyTo(snapshot, 0);
        }

        int c = 0;

        try
        {
            for (int i = 0; i < count; i++)
            {
                if(!serial.HasValue || serial.Value == snapshot[i].LocalSerial)
                {
                    action((T)snapshot[i]);
                    c++;
                }
            }
        }
        finally
        {
            Array.Clear(snapshot, 0, count);
            ArrayPool<Gump>.Shared.Return(snapshot);
        }

        return c != 0;
    }

    /// <summary>
    /// Get the first gump of this type, automatically prunes Disposed gumps.
    /// </summary>
    /// <param name="serial"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T? GetFirst<T>(uint? serial = null) where T : Gump
    {
        lock (_lock)
        {
            if (!_items.TryGetValue(typeof(T), out List<Gump> list))
                return null;

            list.RemoveAll(i => i.IsDisposed);

            if (list.Count <= 0) return null;

            if(!serial.HasValue)
                return list[0] as T;

            foreach(Gump gump in list)
                if (gump.LocalSerial == serial.Value)
                    return gump as T;

            return null;
        }
    }

    public static void Clear()
    {
        lock (_lock) _items.Clear();
    }
}
