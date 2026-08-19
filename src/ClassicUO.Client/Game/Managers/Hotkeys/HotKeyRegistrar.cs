using ClassicUO.Configuration;
using ClassicUO.Input;
using SDL3;

namespace ClassicUO.Game.Managers.Hotkeys
{
    /// <summary>
    /// Central place to register every system's hotkeys with <see cref="HotKeys"/>. Runs once after
    /// <see cref="HotKeys.Load"/> (see GameScene). New systems add their registrations here so there
    /// is a single, discoverable list of code-registered hotkeys. Ids are exposed as constants so
    /// consumers can cache the returned <see cref="HotKeyEntry"/> for fast IsPressed checks.
    /// </summary>
    public static class HotKeyRegistrar
    {
        #region Global
        public const string GlobalToggleId = "global.togglehotkeys";
        #endregion

        #region Grid container
        public const string GridMultiMoveId = "grid.multimove";
        public const string GridAutoLootId = "grid.autoloot";
        public const string GridLockSlotId = "grid.lockslot";
        public const string GridCompareId = "grid.compareequipped";
        #endregion

        #region Chat
        public const string ChatHistoryModId = "chat.historymodifier";
        #endregion

        #region Gumps
        public const string GumpModifierId = "gump.modifier";
        public const string GumpLockId = "gump.lock";
        public const string GumpOpacityId = "gump.opacityscroll";
        #endregion

        #region World map
        public const string WorldMapMarkerId = "worldmap.addmarker";
        public const string WorldMapPathfindId = "worldmap.pathfind";
        public const string WorldMapPathfindAppendId = "worldmap.pathfind.append";
        #endregion

        #region Controller
        public const string ControllerTargetNextId = "controller.target.next";
        public const string ControllerTargetPrevId = "controller.target.prev";
        public const string ControllerTargetFilterId = "controller.target.filter";
        public const string ControllerTargetConfirmId = "controller.target.confirm";
        public const string ControllerTargetCancelId = "controller.target.cancel";
        public const string ControllerClickLeftId = "controller.click.left";
        public const string ControllerClickRightId = "controller.click.right";
        public const string ControllerUiUpId = "controller.ui.up";
        public const string ControllerUiDownId = "controller.ui.down";
        public const string ControllerUiLeftId = "controller.ui.left";
        public const string ControllerUiRightId = "controller.ui.right";
        public const string ControllerCategory = "Controller";
        #endregion

        #region World interaction
        public const string FollowMobileId = "world.followmobile";
        public const string PathfindId = "world.pathfind";
        public const string ItemDragLockId = "world.itemdraglock";
        public const string ZoomScrollId = "world.zoomscroll";
        public const string SplitStackId = "world.splitstack";
        public const string ShopBulkId = "world.shopbulk";
        public const string ShowNameplatesId = "world.shownameplates";
        #endregion

        /// <summary>Register all systems' hotkeys. Safe to call again on profile switch.</summary>
        public static void RegisterAll()
        {
            RegisterGlobal();
            RegisterGridContainer();
            RegisterWorld();
            RegisterWorldMap();
            RegisterGumps();
            RegisterChat();
            RegisterController();
        }


        /// <summary>
        /// True when <paramref name="button"/> triggers the bound controller action.
        /// </summary>
        /// <remarks>
        /// Deliberately not <see cref="HotKeys.IsPressed"/>. The registry is only populated once a
        /// game scene loads, so polling it leaves the pad dead at the login screen, and it honours
        /// the global hotkey disable, which would strand a pad-only player with no way to click or
        /// navigate. An unregistered action falls back to its default button. Matching is not exact
        /// either: on a pad the player is often already holding something, and an exact match would
        /// silently swallow the press.
        /// </remarks>
        public static bool ControllerActionPressed(string id, SDL.SDL_GamepadButton button, SDL.SDL_GamepadButton fallback)
        {
            SDL.SDL_GamepadButton[] bound = HotKeys.Get(id)?.Binding?.ControllerButtons;

            if (bound == null || bound.Length == 0)
            {
                return button == fallback;
            }

            return System.Array.IndexOf(bound, button) >= 0 && Controller.AreButtonsPressed(bound, exact: false);
        }

        private static void RegisterController()
        {
            const string category = ControllerCategory;

            // Bumpers and face buttons are unassigned in the stock controller scheme, so they are
            // safe defaults. A user who has bound them to a macro can rebind these in options.
            // These are actions rather than held modifiers, so unlike ContextModifier they respect
            // the global hotkey disable.
            ControllerAction(ControllerTargetPrevId, "Select previous target", SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER, category);
            ControllerAction(ControllerTargetNextId, "Select next target", SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER, category);
            // Not Back or Guide: the legacy trigger migration rewrites those on every load. The stick
            // clicks are free now that clicking is a rebindable action rather than hardwired.
            ControllerAction(ControllerTargetFilterId, "Cycle target filter", SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK, category);
            ControllerAction(ControllerTargetConfirmId, "Use or attack selected target", SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, category);
            ControllerAction(ControllerTargetCancelId, "Cancel target or selection", SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST, category);

            ControllerAction(ControllerClickLeftId, "Left click", Controller.RightTriggerButton, category);
            ControllerAction(ControllerClickRightId, "Right click", Controller.LeftTriggerButton, category);

            ControllerAction(ControllerUiUpId, "Move UI focus up", SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP, category);
            ControllerAction(ControllerUiDownId, "Move UI focus down", SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN, category);
            ControllerAction(ControllerUiLeftId, "Move UI focus left", SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, category);
            ControllerAction(ControllerUiRightId, "Move UI focus right", SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, category);

            ReleaseDpadFromTargetFilter();
        }


        /// <summary>
        /// Frees the d-pad on profiles saved before it drove UI focus.
        /// </summary>
        /// <remarks>
        /// The filter cycle used to default to d-pad up, and every registered hotkey is persisted
        /// whether or not the player chose it, so the saved binding would otherwise be restored on
        /// top of the new default and fire alongside every focus move.
        /// </remarks>
        private static void ReleaseDpadFromTargetFilter()
        {
            HotKeyEntry entry = HotKeys.Get(ControllerTargetFilterId);
            SDL.SDL_GamepadButton[] buttons = entry?.Binding?.ControllerButtons;

            if (buttons == null)
            {
                return;
            }

            foreach (SDL.SDL_GamepadButton button in buttons)
            {
                if (button is SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP
                    or SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN
                    or SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT
                    or SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT)
                {
                    entry.ResetToDefault();
                    return;
                }
            }
        }

        private static void ControllerAction(string id, string name, SDL.SDL_GamepadButton button, string category)
            => HotKeys.Register(id, name, new HotkeyBinding { ControllerButtons = [button] }, category, checkConflicts: false);

        private static void RegisterChat()
        {
            // The keys are fixed (Q = previous, W = next); this rebinds the modifier held with them.
            ContextModifier(ChatHistoryModId, "Message-history modifier (with Q/W)", Modifier(ctrl: true), "Chat");
        }

        private static void RegisterGumps()
        {
            const string category = "Gumps";
            ContextModifier(GumpModifierId, "Move / detach / show lock icon", Modifier(alt: true), category);
            ContextModifier(GumpLockId, "Lock or unlock", Modifier(ctrl: true, alt: true), category);
            ContextModifier(GumpOpacityId, "Adjust opacity with wheel", Modifier(alt: true), category);
        }

        private static void RegisterWorldMap()
        {
            const string category = "World Map";
            ContextModifier(WorldMapMarkerId, "Add marker", Modifier(ctrl: true), category);
            ContextModifier(WorldMapPathfindId, "Pathfind to point", Modifier(ctrl: true), category);
            // Held together with the pathfind modifier to append a new segment onto the current route.
            ContextModifier(WorldMapPathfindAppendId, "Append to current path (with pathfind)", Modifier(shift: true), category);
        }

        private static void RegisterGlobal()
        {
            // Unbound by default. Pressing it toggles the shared Profile.PersistentDisableHotkeys flag,
            // turning every other hotkey off/on. It is exempt from that flag so it can always toggle back on.
            HotKeyEntry entry = HotKeys.Register(GlobalToggleId, "Toggle all hotkeys", new HotkeyBinding(), "Global", ToggleHotkeysEnabled);
            entry.IgnoresGlobalDisable = true;
        }

        public static void ToggleHotkeysEnabled()
        {
            Profile p = ProfileManager.CurrentProfile;
            if (p == null)
                return;

            p.PersistentDisableHotkeys = !p.PersistentDisableHotkeys;
            GameActions.Print($"Hotkeys {(p.PersistentDisableHotkeys ? "disabled" : "enabled")}.");
        }

        private static void RegisterGridContainer()
        {
            const string category = "Grid Container";
            ContextModifier(GridMultiMoveId, "Move multiple items", Modifier(alt: true), category);
            ContextModifier(GridAutoLootId, "Add item to autoloot", Modifier(shift: true), category);
            ContextModifier(GridLockSlotId, "Lock item in slot", Modifier(ctrl: true), category);
            ContextModifier(GridCompareId, "Compare item to equipped", Modifier(ctrl: true), category);
        }

        private static void RegisterWorld()
        {
            const string category = "World";
            ContextModifier(FollowMobileId, "Click to follow a mobile", Modifier(alt: true), category);
            ContextModifier(PathfindId, "Pathfind modifier", Modifier(shift: true), category);
            ContextModifier(ItemDragLockId, "Lock item drag position", Modifier(ctrl: true), category);
            ContextModifier(ZoomScrollId, "Zoom with mouse wheel", Modifier(ctrl: true), category);
            ContextModifier(SplitStackId, "Split stack modifier", Modifier(shift: true), category);
            ContextModifier(ShopBulkId, "Buy/sell entire stack", Modifier(shift: true), category);
            ContextModifier(ShowNameplatesId, "Show all nameplates", Modifier(ctrl: true, shift: true), category);
        }

        /// <summary>
        /// Register a context modifier (e.g. Alt-to-move-gumps, Shift-to-pathfind): excluded from
        /// conflict checks (the same modifier is fine in different situations) and exempt from the
        /// global hotkey shutoff, since these are UI/interaction conveniences rather than action
        /// hotkeys and shouldn't stop working when hotkeys are toggled off.
        /// </summary>
        private static void ContextModifier(string id, string name, HotkeyBinding binding, string category)
            => HotKeys.Register(id, name, binding, category, checkConflicts: false).IgnoresGlobalDisable = true;

        private static HotkeyBinding Modifier(bool ctrl = false, bool shift = false, bool alt = false)
            => new() { Ctrl = ctrl, Shift = shift, Alt = alt };
    }
}
