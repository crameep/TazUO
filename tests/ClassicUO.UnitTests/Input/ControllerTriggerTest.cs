using ClassicUO.Input;
using FluentAssertions;
using SDL3;
using Xunit;

namespace ClassicUO.UnitTests.Input
{
    /// <summary>
    /// <see cref="Controller"/> holds static state, so each test drives the trigger fully
    /// released first rather than relying on the order tests happen to run in.
    /// </summary>
    public class ControllerTriggerTest
    {
        private const short Max = 32767;

        private static void Release(SDL.SDL_GamepadAxis axis)
        {
            Controller.TryUpdateTrigger(axis, 0, out _, out _);
        }

        private static bool Push(SDL.SDL_GamepadAxis axis, float travel, out SDL.SDL_GamepadButton button, out bool pressed)
        {
            return Controller.TryUpdateTrigger(axis, (short)(travel * Max), out button, out pressed);
        }

        [Fact]
        public void Trigger_Reports_Analog_Travel()
        {
            Release(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.5f, out _, out _);

            Controller.LeftTrigger.Should().BeApproximately(0.5f, 0.001f);
        }

        [Fact]
        public void Trigger_Left_And_Right_Are_Independent()
        {
            Release(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
            Release(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);

            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.8f, out _, out _);

            Controller.LeftTrigger.Should().BeApproximately(0.8f, 0.001f);
            Controller.RightTrigger.Should().BeApproximately(0f, 0.001f);
        }

        [Fact]
        public void Trigger_Press_Crosses_At_Upper_Threshold()
        {
            Release(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);

            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.5f, out _, out _)
                .Should().BeFalse("0.5 is below the press threshold");

            bool changed = Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.7f, out SDL.SDL_GamepadButton button, out bool pressed);

            changed.Should().BeTrue();
            pressed.Should().BeTrue();
            button.Should().Be(Controller.LeftTriggerButton);
            Controller.Button_LeftTrigger.Should().BeTrue();
        }

        /// <summary>
        /// The point of separate press and release thresholds: a trigger resting between them
        /// must hold its current state rather than flapping every frame.
        /// </summary>
        [Fact]
        public void Trigger_Holds_State_Between_Thresholds()
        {
            Release(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.7f, out _, out _);

            // Falling back into the dead band must not release it.
            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.5f, out _, out _).Should().BeFalse();
            Controller.Button_LeftTrigger.Should().BeTrue();

            // Nor should rising back through the press threshold re-fire it.
            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.7f, out _, out _).Should().BeFalse();
            Controller.Button_LeftTrigger.Should().BeTrue();
        }

        [Fact]
        public void Trigger_Releases_Below_Lower_Threshold()
        {
            Release(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.9f, out _, out _);

            bool changed = Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.2f, out SDL.SDL_GamepadButton button, out bool pressed);

            changed.Should().BeTrue();
            pressed.Should().BeFalse();
            button.Should().Be(Controller.LeftTriggerButton);
            Controller.Button_LeftTrigger.Should().BeFalse();
        }

        [Fact]
        public void Trigger_Ignores_Non_Trigger_Axes()
        {
            Controller.TryUpdateTrigger(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, Max, out _, out _)
                .Should().BeFalse();
            Controller.TryUpdateTrigger(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY, Max, out _, out _)
                .Should().BeFalse();
        }

        [Fact]
        public void Trigger_Clamps_Negative_Noise_To_Zero()
        {
            Release(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER);
            Controller.TryUpdateTrigger(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER, -5000, out _, out _);

            Controller.RightTrigger.Should().Be(0f);
        }

        /// <summary>
        /// Guards the collision this replaces: the synthetic identifiers must not overlap any
        /// real button, or a binding on one would fire the other.
        /// </summary>
        [Fact]
        public void Trigger_Identifiers_Do_Not_Collide_With_Real_Buttons()
        {
            ((int)Controller.LeftTriggerButton).Should().BeGreaterThan((int)SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT);
            ((int)Controller.RightTriggerButton).Should().BeGreaterThan((int)SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT);
            Controller.LeftTriggerButton.Should().NotBe(Controller.RightTriggerButton);
        }

        // ------------------------------------------------------------------
        // Legacy binding migration
        // ------------------------------------------------------------------

        [Fact]
        public void Migration_Maps_Back_And_Guide_Onto_Triggers()
        {
            Controller.MigrateLegacyTriggerButton(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK)
                .Should().Be(Controller.LeftTriggerButton);

            Controller.MigrateLegacyTriggerButton(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE)
                .Should().Be(Controller.RightTriggerButton);
        }

        [Theory]
        [InlineData(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH)]
        [InlineData(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START)]
        [InlineData(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER)]
        [InlineData(SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP)]
        public void Migration_Leaves_Other_Buttons_Alone(SDL.SDL_GamepadButton button)
        {
            Controller.MigrateLegacyTriggerButton(button).Should().Be(button);
        }

        [Fact]
        public void Migration_Rewrites_Whole_Arrays()
        {
            var buttons = new[]
            {
                SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH,
                SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK,
                SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE
            };

            SDL.SDL_GamepadButton[] result = Controller.MigrateLegacyTriggerButtons(buttons);

            result.Should().Equal(
                SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH,
                Controller.LeftTriggerButton,
                Controller.RightTriggerButton);
        }

        [Fact]
        public void Migration_Tolerates_Null()
        {
            Controller.MigrateLegacyTriggerButtons(null).Should().BeNull();
        }

        // ------------------------------------------------------------------
        // Reset
        // ------------------------------------------------------------------

        /// <summary>A pad unplugged mid-press would otherwise leave the button latched on.</summary>
        [Fact]
        public void Reset_Clears_Held_Buttons_And_Triggers()
        {
            Release(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER);
            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.9f, out _, out _);
            Controller.OnButtonDown(new SDL.SDL_GamepadButtonEvent { button = (byte)SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH });

            Controller.Button_LeftTrigger.Should().BeTrue();
            Controller.Button_A.Should().BeTrue();

            Controller.ResetButtons();

            Controller.Button_LeftTrigger.Should().BeFalse();
            Controller.Button_A.Should().BeFalse();
            Controller.LeftTrigger.Should().Be(0f);
            Controller.RightTrigger.Should().Be(0f);
            Controller.PressedButtons().Should().BeEmpty();
        }

        [Fact]
        public void Reset_Then_Press_Fires_A_Fresh_Transition()
        {
            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.9f, out _, out _);
            Controller.ResetButtons();

            // Without the reset clearing state this would be a no-op rather than a press.
            Push(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.9f, out _, out bool pressed)
                .Should().BeTrue();
            pressed.Should().BeTrue();
        }

        [Fact]
        public void Trigger_Names_Are_Distinct_From_Back_And_Guide()
        {
            Controller.GetButtonNames(new[] { Controller.LeftTriggerButton }).Should().Be("LT");
            Controller.GetButtonNames(new[] { Controller.RightTriggerButton }).Should().Be("RT");
            Controller.GetButtonNames(new[] { SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK }).Should().Be("Back");
            Controller.GetButtonNames(new[] { SDL.SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE }).Should().Be("Guide");
        }
    }
}
