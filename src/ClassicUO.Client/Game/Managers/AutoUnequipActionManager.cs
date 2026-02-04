using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Structs;

namespace ClassicUO.Game.Managers;

public sealed partial class AutoUnequipActionManager : IDisposable
{
    private struct Armament(uint serial, Layer layer)
    {
        public readonly uint Serial = serial;
        public readonly Layer Layer = layer;
    }

    public static AutoUnequipActionManager Instance { get; private set; }

    private readonly World _world;

    private readonly CancellationTokenSource _cTokenSource = new();

    private readonly Channel<Action> _flushChannel = Channel.CreateUnbounded<Action>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
    );

    private bool _disposed;

    public bool Enabled => ProfileManager.CurrentProfile?.AutoUnequipForActions ?? false;

    public AutoUnequipActionManager(World world)
    {
        _world = world;
        Instance = this;
        Task.Run(ActionConsumer);
    }

    public bool TryInterceptSpellCast(int spellIndex)
    {
        if (spellIndex is >= 100 and <= 678 or >= 700)
            return false;

        // Check if feature is enabled and player exists
        if ((!_disposed && !Enabled) || _world?.Player == null)
            return false;

        return _flushChannel.Writer.TryWrite(() => GameActions.CastSpellDirect(spellIndex));
    }

    public bool TryInterceptDoubleClick(uint itemSerial, Action<uint> sendDoubleClickDelegate)
    {
        if ((!_disposed && !Enabled) || _world?.Player == null || !ShouldInterceptDblClick(itemSerial))
            return false;

        return _flushChannel.Writer.TryWrite(() => sendDoubleClickDelegate(itemSerial));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _cTokenSource.Cancel();
        _flushChannel.Writer.Complete();
        Task.WaitAll(_flushChannel.Reader.Completion);
        Instance = null;
        _disposed = true;
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

    private async ValueTask ActionConsumer()
    {
        try
        {
            while (!_cTokenSource.IsCancellationRequested && await _flushChannel.Reader.WaitToReadAsync())
            {
                // A micro-delay to let producers settle, in case of a series of actions
                await Task.Delay(50);
                var tasks = new List<Action>();
                while (_flushChannel.Reader.TryRead(out Action task))
                    tasks.Add(task);
                ExecuteBatchedTasks(tasks);
                await Task.Delay(250); // A short delay to avoid spammy edge cases
            }
        }
        catch (TaskCanceledException)
        {
            // Done
        }
    }

    private void ExecuteBatchedTasks(List<Action> tasks)
    {
        if (_disposed || _world?.Player?.Backpack == null)
            return;

        _cTokenSource.Token.ThrowIfCancellationRequested();

        // Enqueue a disarm if necessary
        IList<Armament> unEquipped = EnqueueUnequipIfNeeded();
        _cTokenSource.Token.ThrowIfCancellationRequested();

        // Enqueue all batched actions
        foreach (Action task in tasks)
            ObjectActionQueue.Instance.Enqueue(new ObjectActionQueueItem(task), ActionPriority.EquipItem);
        _cTokenSource.Token.ThrowIfCancellationRequested();

        // Enqueue a re-arm if necessary
        if (unEquipped.Count > 0)
            EnqueueReEquip(unEquipped);
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

    [GeneratedRegex(@"(Strength|Agility|Heal|Cure|Nightsight|Refresh(ment)?)\s+Potion",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IsPotionRegex();
}
