using System.Collections.Generic;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Input;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Controller target selection: cycles nearby entities and parks the cursor on the current one.
    /// </summary>
    /// <remarks>
    /// Snapping the cursor rather than drawing a separate highlight means every existing hover,
    /// tooltip and click path keeps working unchanged.
    /// </remarks>
    internal sealed class ControllerTargetManager
    {
        // Beyond this the entity is off screen at any sane zoom, so cycling to it would strand the cursor.
        private const int MAX_RANGE = 20;

        private readonly World _world;
        private readonly List<uint> _candidates = new();
        private bool _wasTargeting;

        public ControllerTargetManager(World world) => _world = world;

        /// <summary>Currently selected entity serial, or 0.</summary>
        public uint SelectedSerial { get; private set; }

        public ScanTypeObject Filter { get; private set; } = ScanTypeObject.Hostile;

        public Entity Selected => SelectedSerial == 0 ? null : _world.Get(SelectedSerial);

        /// <summary>Drops a stale selection and pre-selects when the server opens a target cursor.</summary>
        public void Update()
        {
            if (SelectedSerial != 0)
            {
                Entity entity = _world.Get(SelectedSerial);

                if (entity == null || entity.IsDestroyed || entity.Distance > MAX_RANGE)
                {
                    SelectedSerial = 0;
                }
            }

            bool targeting = _world.TargetManager.IsTargeting;

            // Entering target mode is the moment selection is most useful, so seed it rather than
            // making the player cycle from nothing while the cursor is up.
            if (targeting && !_wasTargeting)
            {
                PreselectForTargeting();
            }

            _wasTargeting = targeting;
        }

        /// <summary>Moves the selection through the candidate list and snaps the cursor to it.</summary>
        public void CycleTarget(int direction)
        {
            BuildCandidates();

            SelectedSerial = ControllerTargetSelection.Cycle(_candidates, SelectedSerial, direction);

            SnapCursorToSelection();
        }

        /// <summary>Switches candidate category and selects the nearest match in it.</summary>
        public void CycleFilter(int direction)
        {
            Filter = ControllerTargetSelection.CycleFilter(Filter, direction);
            SelectedSerial = 0;

            CycleTarget(1);
        }

        /// <summary>
        /// Acts on the selection: answers a server target cursor when one is open, otherwise
        /// attacks a hostile mobile or double-clicks anything else.
        /// </summary>
        public bool ConfirmSelection()
        {
            Entity entity = Selected;

            if (entity == null)
            {
                return false;
            }

            if (_world.TargetManager.IsTargeting)
            {
                _world.TargetManager.Target(entity.Serial);

                return true;
            }

            if (entity is Mobile mobile && IsHostile(mobile))
            {
                GameActions.Attack(_world, mobile.Serial);
            }
            else
            {
                GameActions.DoubleClick(_world, entity.Serial);
            }

            return true;
        }

        /// <summary>Cancels an open target cursor, otherwise clears the selection.</summary>
        public void Cancel()
        {
            if (_world.TargetManager.IsTargeting)
            {
                _world.TargetManager.CancelTarget();

                return;
            }

            SelectedSerial = 0;
        }

        private void PreselectForTargeting()
        {
            uint last = _world.TargetManager.LastTargetInfo.Serial;

            if (last != 0 && IsSelectable(_world.Get(last)))
            {
                SelectedSerial = last;
                SnapCursorToSelection();

                return;
            }

            BuildCandidates();

            if (_candidates.Count > 0)
            {
                SelectedSerial = _candidates[0];
                SnapCursorToSelection();
            }
        }

        /// <summary>Rebuilds the candidate list, nearest first.</summary>
        /// <remarks>
        /// Sorted by distance then serial so the order is total and stable; an unstable order makes
        /// cycling feel broken because the same press lands somewhere different each time.
        /// </remarks>
        private void BuildCandidates()
        {
            _candidates.Clear();

            if (Filter == ScanTypeObject.Objects)
            {
                foreach (Item item in _world.Items.Values)
                {
                    if (item.IsMulti || item.IsDestroyed || !item.OnGround || item.Distance > MAX_RANGE)
                    {
                        continue;
                    }

                    _candidates.Add(item.Serial);
                }
            }
            else
            {
                foreach (Mobile mobile in _world.Mobiles.Values)
                {
                    if (mobile.IsDestroyed || mobile == _world.Player || mobile.Distance > MAX_RANGE)
                    {
                        continue;
                    }

                    if (Filter == ScanTypeObject.Hostile && !IsHostile(mobile))
                    {
                        continue;
                    }

                    _candidates.Add(mobile.Serial);
                }
            }

            _candidates.Sort(CompareByDistanceThenSerial);
        }

        private int CompareByDistanceThenSerial(uint a, uint b)
        {
            Entity ea = _world.Get(a);
            Entity eb = _world.Get(b);

            int da = ea?.Distance ?? int.MaxValue;
            int db = eb?.Distance ?? int.MaxValue;

            return da != db ? da.CompareTo(db) : a.CompareTo(b);
        }

        private bool IsSelectable(Entity entity)
            => entity != null && !entity.IsDestroyed && entity.Distance <= MAX_RANGE;

        private static bool IsHostile(Mobile mobile)
            => mobile.NotorietyFlag != NotorietyFlag.Ally
               && mobile.NotorietyFlag != NotorietyFlag.Innocent
               && mobile.NotorietyFlag != NotorietyFlag.Invulnerable;

        /// <summary>Parks the cursor over the selected entity so hover, tooltips and clicks follow it.</summary>
        private void SnapCursorToSelection()
        {
            Entity entity = Selected;

            if (entity == null || Client.Game?.Scene?.Camera == null)
            {
                return;
            }

            // Same anchor the health bars use: sprite origin plus half a tile.
            Point p = entity.RealScreenPosition;
            p.X += (int)entity.Offset.X + 22 + 5;
            p.Y += (int)(entity.Offset.Y - entity.Offset.Z) + 22 + 5;

            Renderer.Camera camera = Client.Game.Scene.Camera;

            p = camera.WorldToScreen(p);

            // WorldToScreen is viewport-relative; Mouse.Position is not.
            p.X += camera.Bounds.X;
            p.Y += camera.Bounds.Y;

            Mouse.SnapVirtualCursorTo(p);
        }
    }
}
