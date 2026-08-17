using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Synthetic button identifier for the left trigger.
        /// </summary>
        /// <remarks>
        /// SDL reports triggers as axes, not buttons, so binding one requires an identifier
        /// that fits in <see cref="SDL.SDL_GamepadButton"/>. Triggers previously borrowed
        /// <c>BACK</c> and <c>GUIDE</c>, which works only because the trigger axis indices (4
        /// and 5) happen to equal those button indices — at the cost of making Back and Guide
        /// unbindable, since a real press of either was indistinguishable from a trigger pull.
        /// These values sit above every real button (the highest is <c>MISC6</c> at 25) so
        /// there is no collision. See <see cref="MigrateLegacyTriggerButton"/> for how
        /// existing profiles are carried across.
        /// </remarks>
        public const SDL.SDL_GamepadButton LeftTriggerButton = (SDL.SDL_GamepadButton)200;

        /// <summary>Synthetic button identifier for the right trigger. See <see cref="LeftTriggerButton"/>.</summary>
        public const SDL.SDL_GamepadButton RightTriggerButton = (SDL.SDL_GamepadButton)201;

        /// <summary>Left trigger travel, 0..1.</summary>
        public static float LeftTrigger { get; private set; }

        /// <summary>Right trigger travel, 0..1.</summary>
        public static float RightTrigger { get; private set; }

        /// <summary>Travel at which an unpressed trigger becomes pressed.</summary>
        private const float TRIGGER_PRESS_THRESHOLD = 0.65f;

        /// <summary>
        /// Travel at which a pressed trigger releases. Deliberately lower than the press
        /// threshold: a single threshold makes a trigger held near the boundary chatter
        /// between down and up every frame.
        /// </summary>
        private const float TRIGGER_RELEASE_THRESHOLD = 0.35f;

        /// <summary>Largest magnitude SDL reports for an axis.</summary>
        private const float AXIS_MAX = 32767f;

        public static Dictionary<SDL.SDL_GamepadButton, bool> ButtonStates = new();

        /// <summary>Fired when any controller button goes down. Used by hotkey capture in the UI.</summary>
        public static event Action<SDL.SDL_GamepadButton> ButtonDownEvent;

        public static void OnButtonDown(SDL.SDL_GamepadButtonEvent e)
        {
            SetButtonState((SDL.SDL_GamepadButton)e.button, true);
            ButtonDownEvent?.Invoke((SDL.SDL_GamepadButton)e.button);
        }

        public static void OnButtonUp(SDL.SDL_GamepadButtonEvent e) => SetButtonState((SDL.SDL_GamepadButton)e.button, false);

        /// <summary>
        /// Records analog trigger travel and reports whether that crossed a digital threshold.
        /// </summary>
        /// <param name="axis">The axis that moved; anything other than a trigger is ignored.</param>
        /// <param name="rawValue">Raw SDL axis value.</param>
        /// <param name="button">The synthetic trigger button whose state changed.</param>
        /// <param name="pressed">The new digital state.</param>
        /// <returns>
        /// True when the digital state changed and the caller should dispatch a button event.
        /// Analog travel is always recorded regardless of the return value.
        /// </returns>
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

        /// <summary>
        /// Rewrites a persisted binding that used <c>BACK</c> or <c>GUIDE</c> as a trigger
        /// stand-in onto the dedicated trigger identifiers.
        /// </summary>
        /// <remarks>
        /// Safe to apply unconditionally to saved data: before this change a real Back or
        /// Guide press was consumed by the trigger workaround and so could never be bound
        /// deliberately, which means any stored occurrence of either must have meant a trigger.
        /// </remarks>
        public static SDL.SDL_GamepadButton MigrateLegacyTriggerButton(SDL.SDL_GamepadButton button) => button switch
        {
            SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => LeftTriggerButton,
            SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE => RightTriggerButton,
            _ => button
        };

        /// <summary>Applies <see cref="MigrateLegacyTriggerButton"/> across an array, in place.</summary>
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
