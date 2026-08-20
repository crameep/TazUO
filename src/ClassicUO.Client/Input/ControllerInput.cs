using ClassicUO.Configuration;
using ClassicUO.Game.Managers;

namespace ClassicUO.Input
{
    /// <summary>Shared gates for controller actions.</summary>
    internal static class ControllerInput
    {
        /// <summary>True when controller input should be acted on at all.</summary>
        /// <remarks>
        /// A profile only exists once a character logs in, so requiring one left the pad completely
        /// dead on the title and login screens. Absent a profile the pad is live; a loaded profile
        /// is the only thing that turns it off.
        /// </remarks>
        public static bool IsEnabled()
        {
            Profile profile = ProfileManager.CurrentProfile;

            return profile == null || profile.ControllerEnabled;
        }

        /// <summary>True while a control is actually consuming typed input.</summary>
        /// <remarks>
        /// Not a plain null check on the focused control. The system chat box takes keyboard focus
        /// whenever the player clicks away from anything else, and login and options windows always
        /// have a focused control, so "something has focus" is true nearly always and using it as a
        /// gate disabled every controller action permanently. AcceptKeyboardInput is false on a
        /// text box that is not editable, which is the state chat rests in when it is not open.
        /// </remarks>
        public static bool TextEntryHasFocus()
            => UIManager.KeyboardFocusControl is { AcceptKeyboardInput: true };

        /// <summary>True when the focused control drives the d-pad itself.</summary>
        /// <remarks>
        /// Text focus deliberately does not block the d-pad: a text box ignores it entirely, and
        /// the login screen keeps its account field focused, so treating focus as a block left the
        /// d-pad useless exactly where it is needed most.
        /// </remarks>
        public static bool DPadIsClaimed()
            => UIManager.KeyboardFocusControl?.HandlesControllerDPad == true;
    }
}
