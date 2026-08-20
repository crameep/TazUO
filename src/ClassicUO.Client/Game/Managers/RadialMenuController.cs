using ClassicUO.Configuration;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Drives the controller radial menu: hold to open, aim with the left stick, release to run.
    /// </summary>
    /// <remarks>
    /// Aimed with the left stick rather than the right so the cursor does not wander while choosing,
    /// which means movement is suppressed for as long as the menu is open.
    /// </remarks>
    internal sealed class RadialMenuController
    {
        private readonly World _world;

        private RadialMenuGump _gump;

        public RadialMenuController(World world) => _world = world;

        public bool IsOpen => _gump is { IsDisposed: false };

        /// <summary>Opens the menu, or does nothing when every slot is empty.</summary>
        public bool Open()
        {
            if (IsOpen || !RadialMenuManager.HasAnySlot())
            {
                return false;
            }

            _gump = new RadialMenuGump(_world);

            UIManager.Add(_gump);

            return true;
        }

        /// <summary>Runs the aimed slot's macro and closes. Releasing at the centre cancels.</summary>
        public bool Activate()
        {
            if (!IsOpen)
            {
                return false;
            }

            int slot = _gump.Selected;

            Close();

            if (slot == RadialMenuSelection.NO_SELECTION)
            {
                return true;
            }

            string name = RadialMenuManager.GetSlot(slot);

            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            foreach (Macro macro in _world.Macros.GetAllMacros())
            {
                if (macro.Name == name && macro.Items is MacroObject item)
                {
                    // Same sequence the scene uses for a keyboard or button macro.
                    _world.Macros.SetMacroToExecute(item);
                    _world.Macros.WaitingBandageTarget = false;
                    _world.Macros.WaitForTargetTimer = 0;
                    _world.Macros.Update();

                    break;
                }
            }

            return true;
        }

        public void Close()
        {
            _gump?.Dispose();
            _gump = null;
        }

        /// <summary>Feeds the current stick direction to the open menu.</summary>
        public void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            GamePadState state = Controller.GetActiveState();

            if (!state.IsConnected)
            {
                Close();

                return;
            }

            Profile profile = ProfileManager.CurrentProfile;

            Vector2 stick = ControllerAxis.ApplyRadialDeadzone(
                state.ThumbSticks.Left,
                profile?.ControllerDeadzoneInner ?? 0.20f,
                profile?.ControllerDeadzoneOuter ?? 0.95f);

            _gump.Aim(stick);
        }
    }
}
