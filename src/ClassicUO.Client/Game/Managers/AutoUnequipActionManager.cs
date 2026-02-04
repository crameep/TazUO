using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Structs;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers;

public sealed partial class AutoUnequipActionManager : IDisposable
{
    private const string DBL_CLICK_INTERCEPTOR_KEY = "dclk";
    private const string CAST_SPELL_INTERCEPTOR_KEY = "cs";

    private struct Armament(uint serial, Layer layer)
    {
        public readonly uint Serial = serial;
        public readonly Layer Layer = layer;
    }

    public static AutoUnequipActionManager Instance { get; private set; }

    private readonly World _world;
    private readonly ConcurrentDictionary<string, Action> _interceptions = new();
    private uint _shouldFlush;
    private uint _isFlushing;
    private bool _disposed;

    public bool Enabled => ProfileManager.CurrentProfile?.AutoUnequipForActions ?? false;

    public AutoUnequipActionManager(World world)
    {
        _world = world;
        Instance = this;
    }

    public bool TryInterceptSpellCast(int spellIndex)
    {
        if (spellIndex is >= 100 and <= 678 or >= 700)
            return false;

        // Check if feature is enabled and player exists
        if ((!_disposed && !Enabled) || _world?.Player == null)
            return false;

        string taskKey = $"{CAST_SPELL_INTERCEPTOR_KEY}-{spellIndex}";
        return SetInterceptAndSignal(taskKey, () => GameActions.CastSpellDirect(spellIndex));
    }

    public bool TryInterceptDoubleClick(uint itemSerial, Action<uint> sendDoubleClickDelegate)
    {
        if ((!_disposed && !Enabled) || _world?.Player == null || !ShouldInterceptDblClick(itemSerial))
            return false;

        string taskKey = $"{DBL_CLICK_INTERCEPTOR_KEY}-{itemSerial}";
        return SetInterceptAndSignal(taskKey, () => sendDoubleClickDelegate(itemSerial));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // The flush queue can technically be active at this point, but it's the edge of edge cases
        // so ignoring this, for now.
        _interceptions.Clear();
        Interlocked.And(ref _shouldFlush, 0);
        Interlocked.And(ref _isFlushing, 0);

        _disposed = true;
    }

    private bool SetInterceptAndSignal(string taskKey, Action task)
    {
        bool wasEmpty = _shouldFlush == 0;
        bool interceptAdded = SetIntercept(taskKey, task);
        if (!interceptAdded)
            return false;

        if (!wasEmpty || _shouldFlush == 0)
            return false;

        Task.Run(FlushTasks);
        return true;
    }

    private bool SetIntercept(string taskKey, Action task)
    {
        if (_interceptions.TryAdd(taskKey, task))
        {
            if (_interceptions.Count == 1)
                Interlocked.Or(ref _shouldFlush, 1);
            return true;
        }

        if (!_interceptions.TryRemove(taskKey, out _))
        {
            Log.Warn("Failed to remove an existing double click intercept prior to updating");
            return false;
        }

        // A false here means a task already exist and we should update instead
        if (!_interceptions.TryAdd(taskKey, task))
        {
            Log.Warn("Failed to enqueue a double click intercept for item with serial");
            return false;
        }

        if (_interceptions.Count == 1)
            Interlocked.Or(ref _shouldFlush, 1);
        return true;
    }

    /// <summary>
    ///     A method to used to check whether a Double-Click should be intercepted
    /// </summary>
    /// <param name="serial">The clicked target's serial</param>
    /// <returns>True if the event should be intercepted, false otherwise</returns>
    private bool ShouldInterceptDblClick(uint serial) =>
        IsPotionItem(serial);

    private List<Armament> GetArmingState()
    {
        // Check if player has weapons equipped
        Item oneHanded = _world.Player.FindItemByLayer(Layer.OneHanded);
        Item twoHanded = _world.Player.FindItemByLayer(Layer.TwoHanded);

        var arms = new List<Armament>();
        if (oneHanded?.Serial != null)
            arms.Add(new Armament(oneHanded.Serial, Layer.OneHanded));

        if (twoHanded?.Serial != null)
            arms.Add(new Armament(twoHanded.Serial, Layer.TwoHanded));

        return arms;
    }

    private bool IsPotionItem(uint serial)
    {
        Item item = _world.Items.Get(serial);
        if (item == null)
            return false;

        // Check if item is 0xF06-0xF09 OR 0xF0B-0xF0C - these are the 'drinkable' potion graphics
        if (item.Graphic is (< 0xF06 or > 0xF09) and (< 0xF0B or > 0xF0C))
            return false;

        // Get the un-localized item name
        if (_world.OPL.TryGetNameAndData(item.Serial, out string name, out _))
            // Use a simple regular expression to heuristically determine if something is a potion.
            // To be expanded on with configuration.
            return name != null && IsPotionRegex().IsMatch(name);

        // Data doesn't exist in OPL cache - stay conservative and assume this is *not* a potion
        return false;
    }

    private void FlushTasks()
    {
        if (_disposed)
            return;

        if (Interlocked.CompareExchange(ref _isFlushing, 1, 0) != 0)
            return; // Flush already in progress

        Item backpack = _world.Player.Backpack;
        if (backpack == null)
        {
            ClearInterceptionsMap();
            return;
        }

        if (_interceptions.IsEmpty)
            return;

        IList<Armament> unEquipped = EnqueueUnequipIfNeeded();
        EnqueueInterceptedCalls();
        if (unEquipped.Count > 0)
            EnqueueReEquip(unEquipped);

        Interlocked.And(ref _isFlushing, 0);
    }

    private IList<Armament> EnqueueUnequipIfNeeded()
    {
        // First, fetch a 'fresh' state
        // Don't bother issuing an unequip command if we're not currently armed.
        IList<Armament> arms = GetArmingState();

        foreach (Armament arm in arms ?? [])
            ObjectActionQueue.Instance.Enqueue(
                CreateUnequipQueueItem(arm.Serial, arm.Layer),
                ActionPriority.EquipItem
            );

        return arms;
    }

    private ObjectActionQueueItem CreateUnequipQueueItem(uint serial, Layer layer) =>
        new(() =>
        {
            Item item = _world.Items.Get(serial);
            if (item == null || item.Container != _world.Player.Serial || item.Layer != layer)
                return;

            Item bp = _world.Player.Backpack;
            if (bp != null)
                new MoveRequest(serial, bp.Serial, item.Amount).Execute();
        });

    private void EnqueueReEquip(IList<Armament> arms)
    {
        foreach (Armament arm in arms)
            ObjectActionQueue.Instance.Enqueue(new ObjectActionQueueItem(() =>
            {
                Item item = _world.Items.Get(arm.Serial);
                Item backpackItem = _world.Player.Backpack;

                if (item != null && backpackItem != null && item.Container == backpackItem.Serial)
                    MoveRequest.EquipItem(arm.Serial, arm.Layer)?.Execute();
            }), ActionPriority.EquipItem);
    }

    private void ClearInterceptionsMap()
    {
        _interceptions.Clear();
        Interlocked.And(ref _shouldFlush, 0);
    }

    private void EnqueueInterceptedCalls()
    {
        foreach (Action task in _interceptions.Values)
            ObjectActionQueue.Instance.Enqueue(new ObjectActionQueueItem(task), ActionPriority.EquipItem);
        ClearInterceptionsMap();
    }

    [GeneratedRegex(@"(Strength|Agility|Heal|Cure|Nightsight|Refresh(ment)?)\s+Potion",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IsPotionRegex();
}
