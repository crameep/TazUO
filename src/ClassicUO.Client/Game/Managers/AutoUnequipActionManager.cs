using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers
{
    public sealed class AutoUnequipActionManager
    {
        public static AutoUnequipActionManager Instance { get; private set; }

        private readonly World _world;
        private uint _oneHandedSerial = 0;
        private uint _twoHandedSerial = 0;
        private bool _isProcessing = false;
        private int _pendingSpellIndex = -1;
        private SequenceState _sequenceState = SequenceState.Unequipping;

        private enum SequenceState
        {
            Unequipping,  // Currently unequipping weapons
            Casting,      // Cast action has been queued/executed
            Reequipping   // Currently reequipping weapons
        }

        public AutoUnequipActionManager(World world)
        {
            _world = world;
            Instance = this;
        }

        public bool TryInterceptSpellCast(int spellIndex)
        {
            if (spellIndex is >= 100 and <= 678 or >= 700)
                return false;

            // Check if feature is enabled
            if (!(ProfileManager.CurrentProfile?.AutoUnequipForActions ?? false))
                return false;

            // Check if player exists
            if (_world?.Player == null)
                return false;

            // Handle mid-sequence interruptions
            if (_isProcessing)
            {
                if (_sequenceState == SequenceState.Unequipping)
                {
                    // Replace pending spell, continue unequipping
                    _pendingSpellIndex = spellIndex;
                    return true;
                }
                else if (_sequenceState == SequenceState.Casting)
                {
                    // Hands already free, cast new spell directly and skip reequip
                    GameActions.CastSpellDirect(spellIndex);
                    _pendingSpellIndex = spellIndex;
                    return true;
                }
                // If reequipping, don't intercept - let it complete normally
                return false;
            }

            // Check if player has weapons equipped
            Item oneHanded = _world.Player.FindItemByLayer(Layer.OneHanded);
            Item twoHanded = _world.Player.FindItemByLayer(Layer.TwoHanded);

            // If no weapons equipped, don't intercept
            if (oneHanded == null && twoHanded == null)
                return false;

            // Store the serials for reequipping (only if items exist)
            _oneHandedSerial = oneHanded?.Serial ?? 0;
            _twoHandedSerial = twoHanded?.Serial ?? 0;
            _pendingSpellIndex = spellIndex;
            _isProcessing = true;
            _sequenceState = SequenceState.Unequipping;

            // Enqueue the sequence: unequip -> cast -> reequip
            EnqueueUnequipCastReequip();

            return true; // We intercepted the cast
        }

        private void EnqueueUnequipCastReequip()
        {
            Item backpack = _world.Player.Backpack;
            if (backpack == null)
            {
                Reset();
                return;
            }

            // Action 1: Unequip one-handed weapon (if equipped)
            if (_oneHandedSerial != 0)
            {
                GlobalPriorityQueue.Instance.Enqueue(() =>
                {
                    Item item = _world.Items.Get(_oneHandedSerial);
                    if (item != null && item.Container == _world.Player.Serial && item.Layer == Layer.OneHanded)
                    {
                        Item bp = _world.Player.Backpack;
                        if (bp != null)
                        {
                            MoveItemQueue.Instance.Enqueue(_oneHandedSerial, bp.Serial, item.Amount);
                        }
                    }
                });
            }

            // Action 2: Unequip two-handed weapon (if equipped)
            if (_twoHandedSerial != 0)
            {
                GlobalPriorityQueue.Instance.Enqueue(() =>
                {
                    Item item = _world.Items.Get(_twoHandedSerial);
                    if (item != null && item.Container == _world.Player.Serial && item.Layer == Layer.TwoHanded)
                    {
                        Item bp = _world.Player.Backpack;
                        if (bp != null)
                        {
                            MoveItemQueue.Instance.Enqueue(_twoHandedSerial, bp.Serial, item.Amount);
                        }
                    }
                });
            }

            // Action 3: Cast the spell (after unequipping)
            GlobalPriorityQueue.Instance.Enqueue(() =>
            {
                if (_pendingSpellIndex >= 0)
                {
                    GameActions.CastSpellDirect(_pendingSpellIndex);
                }
                _sequenceState = SequenceState.Casting;
            });

            // Action 4: Reequip one-handed weapon (check state first)
            if (_oneHandedSerial != 0)
            {
                GlobalPriorityQueue.Instance.Enqueue(() =>
                {
                    // If state changed, skip reequip
                    if (_sequenceState != SequenceState.Casting)
                        return;

                    _sequenceState = SequenceState.Reequipping;

                    Item item = _world.Items.Get(_oneHandedSerial);
                    Item backpackItem = _world.Player.Backpack;

                    if (item != null && backpackItem != null && item.Container == backpackItem.Serial)
                    {
                        MoveItemQueue.Instance.EnqueueEquipSingle(_oneHandedSerial, Layer.OneHanded);
                    }
                });
            }
            else
            {
                // No one-handed weapon, but still transition state
                GlobalPriorityQueue.Instance.Enqueue(() =>
                {
                    if (_sequenceState == SequenceState.Casting)
                        _sequenceState = SequenceState.Reequipping;
                });
            }

            // Action 5: Reequip two-handed weapon (check state first)
            if (_twoHandedSerial != 0)
            {
                GlobalPriorityQueue.Instance.Enqueue(() =>
                {
                    // If state changed or not reequipping, skip
                    if (_sequenceState != SequenceState.Reequipping)
                        return;

                    Item item = _world.Items.Get(_twoHandedSerial);
                    Item backpackItem = _world.Player.Backpack;

                    if (item != null && backpackItem != null && item.Container == backpackItem.Serial)
                    {
                        MoveItemQueue.Instance.EnqueueEquipSingle(_twoHandedSerial, Layer.TwoHanded);
                    }
                });
            }

            // Action 6: Cleanup
            GlobalPriorityQueue.Instance.Enqueue(() =>
            {
                Reset();
            });
        }

        private void Reset()
        {
            _isProcessing = false;
            _oneHandedSerial = 0;
            _twoHandedSerial = 0;
            _pendingSpellIndex = -1;
            _sequenceState = SequenceState.Unequipping;
        }

        public void Clear() => Reset();
    }
}
