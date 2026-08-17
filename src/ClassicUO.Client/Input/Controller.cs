using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SDL3;

namespace ClassicUO.Input
{
    internal static class Controller
    {
        public static bool Button_A { get; private set; }
        public static bool Button_B { get; private set; }
        public static bool Button_X { get; private set; }
        public static bool Button_Y { get; private set; }

        public static bool Button_Left { get; private set; }
        public static bool Button_Right { get; private set; }
        public static bool Button_Up { get; private set; }
        public static bool Button_Down { get; private set; }

        public static bool Button_LeftTrigger { get; private set; }
        public static bool Button_LeftBumper { get; private set; }

        public static bool Button_RightTrigger { get; private set; }
        public static bool Button_RightBumper { get; private set; }

        public static bool Button_LeftStick { get; private set; }
        public static bool Button_RightStick { get; private set; }

        // Synthetic button ids for the triggers, which SDL reports as axes rather than buttons.
        // Sits above every real button (highest is MISC6 at 25) so bindings cannot collide.
        // Triggers previously borrowed BACK and GUIDE, which worked only because the trigger axis
        // indices happen to equal those button indices, and made Back and Guide unbindable.
        public const SDL.SDL_GamepadButton LeftTriggerButton = (SDL.SDL_GamepadButton)200;
        public const SDL.SDL_GamepadButton RightTriggerButton = (SDL.SDL_GamepadButton)201;

        /// <summary>Left trigger travel, 0..1.</summary>
        public static float LeftTrigger { get; private set; }

        /// <summary>Right trigger travel, 0..1.</summary>
        public static float RightTrigger { get; private set; }

        private const float TRIGGER_PRESS_THRESHOLD = 0.65f;

        // Lower than the press threshold; a single threshold makes a trigger held near it chatter.
        private const float TRIGGER_RELEASE_THRESHOLD = 0.35f;

        private const float AXIS_MAX = 32767f;

        public static Dictionary<SDL.SDL_GamepadButton, bool> ButtonStates = new();

        // Highest player index FNA will report a pad on.
        private const int MAX_PLAYER_INDEX = 4;

        /// <summary>Pad currently driving input.</summary>
        public static PlayerIndex ActivePlayerIndex { get; private set; } = PlayerIndex.One;

        private static bool _hadConnectedPad;

        // Rescanning every slot every frame costs a GetState per slot for anyone running with
        // controller support on and no pad plugged in, which is the default. Hot-plug does not
        // need frame accuracy.
        private const uint RESCAN_INTERVAL_MS = 1000;

        private static uint _nextRescanTicks;

        /// <summary>
        /// State of the active pad, re-scanning the other slots when it goes away.
        /// </summary>
        /// <remarks>
        /// A pad is not always on index one: unplugging and replugging, or a wireless adapter
        /// claiming a slot, can leave it on a later index where hardcoding index one sees nothing.
        /// </remarks>
        public static GamePadState GetActiveState()
        {
            GamePadState state = GamePad.GetState(ActivePlayerIndex);

            if (state.IsConnected)
            {
                _hadConnectedPad = true;

                return state;
            }

            if (Time.Ticks < _nextRescanTicks)
            {
                return state;
            }

            _nextRescanTicks = Time.Ticks + RESCAN_INTERVAL_MS;

            for (int i = 0; i < MAX_PLAYER_INDEX; i++)
            {
                var index = (PlayerIndex)i;

                if (index == ActivePlayerIndex)
                {
                    continue;
                }

                GamePadState candidate = GamePad.GetState(index);

                if (!candidate.IsConnected)
                {
                    continue;
                }

                // Switching pads: whatever the old one was holding is no longer true.
                ResetButtons();
                ActivePlayerIndex = index;
                _hadConnectedPad = true;

                return candidate;
            }

            // Unplugged mid-press would otherwise leave the held button latched on forever.
            if (_hadConnectedPad)
            {
                ResetButtons();
                _hadConnectedPad = false;
            }

            return state;
        }

        /// <summary>Clears all held button and trigger state.</summary>
        public static void ResetButtons()
        {
            ButtonStates.Clear();

            Button_A = Button_B = Button_X = Button_Y = false;
            Button_Left = Button_Right = Button_Up = Button_Down = false;
            Button_LeftBumper = Button_RightBumper = false;
            Button_LeftTrigger = Button_RightTrigger = false;
            Button_LeftStick = Button_RightStick = false;

            LeftTrigger = 0f;
            RightTrigger = 0f;
        }

        /// <summary>Fired when any controller button goes down. Used by hotkey capture in the UI.</summary>
        public static event Action<SDL.SDL_GamepadButton> ButtonDownEvent;

        public static void OnButtonDown(SDL.SDL_GamepadButtonEvent e)
        {
            SetButtonState((SDL.SDL_GamepadButton)e.button, true);
            ButtonDownEvent?.Invoke((SDL.SDL_GamepadButton)e.button);
        }

        public static void OnButtonUp(SDL.SDL_GamepadButtonEvent e) => SetButtonState((SDL.SDL_GamepadButton)e.button, false);

        /// <summary>Records analog trigger travel; returns true when the debounced digital state changed.</summary>
        public static bool TryUpdateTrigger(SDL.SDL_GamepadAxis axis, short rawValue, out SDL.SDL_GamepadButton button, out bool pressed)
        {
            bool isLeft = axis == SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER;

            if (!isLeft && axis != SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER)
            {
                button = SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID;
                pressed = false;

                return false;
            }

            // Triggers rest at 0 and travel positive, so negative values are noise.
            float travel = Math.Clamp(rawValue / AXIS_MAX, 0f, 1f);

            button = isLeft ? LeftTriggerButton : RightTriggerButton;

            bool wasPressed = isLeft ? Button_LeftTrigger : Button_RightTrigger;

            if (isLeft)
            {
                LeftTrigger = travel;
            }
            else
            {
                RightTrigger = travel;
            }

            pressed = wasPressed
                ? travel > TRIGGER_RELEASE_THRESHOLD
                : travel >= TRIGGER_PRESS_THRESHOLD;

            if (pressed == wasPressed)
            {
                return false;
            }

            SetButtonState(button, pressed);

            if (pressed)
            {
                ButtonDownEvent?.Invoke(button);
            }

            return true;
        }

        // Rewrites bindings that stored a trigger as BACK/GUIDE. Safe unconditionally: those
        // buttons were consumed by the old workaround so could never have been bound deliberately.
        public static SDL.SDL_GamepadButton MigrateLegacyTriggerButton(SDL.SDL_GamepadButton button) => button switch
        {
            SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => LeftTriggerButton,
            SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE => RightTriggerButton,
            _ => button
        };

        /// <summary>Applies the trigger migration across an array, in place.</summary>
        public static SDL.SDL_GamepadButton[] MigrateLegacyTriggerButtons(SDL.SDL_GamepadButton[] buttons)
        {
            if (buttons == null)
            {
                return null;
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i] = MigrateLegacyTriggerButton(buttons[i]);
            }

            return buttons;
        }

        private static void SetButtonState(SDL.SDL_GamepadButton button, bool state)
        {
            ButtonStates[button] = state;

            switch (button)
            {
                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH:
                    Button_A = state;
                    break;
                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST:
                    Button_B = state;
                    break;
                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST:
                    Button_X = state;
                    break;
                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH:
                    Button_Y = state;
                    break;

                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT:
                    Button_Left = state;
                    break;
                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT:
                    Button_Right = state;
                    break;
                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP:
                    Button_Up = state;
                    break;
                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN:
                    Button_Down = state;
                    break;

                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER:
                    Button_LeftBumper = state;
                    break;
                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER:
                    Button_RightBumper = state;
                    break;

                case LeftTriggerButton:
                    Button_LeftTrigger = state;
                    break;
                case RightTriggerButton:
                    Button_RightTrigger = state;
                    break;

                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK:
                    Button_LeftStick = state;
                    break;
                case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK:
                    Button_RightStick = state;
                    break;
            }
        }

        public static bool IsButtonPressed(SDL.SDL_GamepadButton button) => ButtonStates.ContainsKey(button) && ButtonStates[button];

        public static bool AreButtonsPressed(int[] buttons, bool exact = true) => AreButtonsPressed(buttons.Select(x => (SDL.SDL_GamepadButton)x).ToArray(), exact);

        /// <summary>
        /// Check is the supplied list of buttons are currently pressed.
        /// </summary>
        /// <param name="buttons"></param>
        /// <param name="exact">If true, any other buttons pressed will make this return false</param>
        /// <returns></returns>
        public static bool AreButtonsPressed(SDL.SDL_GamepadButton[] buttons, bool exact = true)
        {
            bool finalstatus = true;

            foreach (SDL.SDL_GamepadButton button in buttons)
            {
                if (!IsButtonPressed(button))
                {
                    finalstatus = false;
                    break;
                }
            }

            if (exact)
            {
                SDL.SDL_GamepadButton[] allPressed = PressedButtons();

                if (allPressed.Length > buttons.Length)
                {
                    finalstatus = false;
                }
            }

            return finalstatus;
        }

        public static SDL.SDL_GamepadButton[] PressedButtons() => ButtonStates.Where(x => x.Value).Select(x => x.Key).ToArray();

        public static string GetButtonNames(SDL.SDL_GamepadButton[] buttons)
        {
            string keys = string.Empty;

            foreach (SDL.SDL_GamepadButton button in buttons)
            {
                switch (button)
                {
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH:
                        keys += "A";
                        break;
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST:
                        keys += "B";
                        break;
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST:
                        keys += "X";
                        break;
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH:
                        keys += "Y";
                        break;

                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT:
                        keys += "Left";
                        break;
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT:
                        keys += "Right";
                        break;
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP:
                        keys += "Up";
                        break;
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN:
                        keys += "Down";
                        break;


                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER:
                        keys += "LB";
                        break;
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER:
                        keys += "RB";
                        break;


                    case LeftTriggerButton:
                        keys += "LT";
                        break;
                    case RightTriggerButton:
                        keys += "RT";
                        break;

                    // Now genuinely bindable, since the triggers no longer borrow them.
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK:
                        keys += "Back";
                        break;
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE:
                        keys += "Guide";
                        break;

                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK:
                        keys += "LS";
                        break;
                    case SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK:
                        keys += "RS";
                        break;
                }

                keys += ", ";
            }

            if (keys.EndsWith(", "))
            {
                keys = keys.Substring(0, keys.Length - 2);
            }

            return keys;
        }
    }
}
