// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Configuration;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Myra.Graphics2D.UI;
using SDL3;

namespace ClassicUO.Input
{
    internal static class Mouse
    {
        public const int MOUSE_DELAY_DOUBLE_CLICK = 350;

        /// <summary>
        /// Invoked whenever the mouse position changes
        /// </summary>
        public static event EventHandler<MouseMovedEventArgs> Moved;

        /// <summary>
        /// Invoked whenever the left mouse button is pressed or released
        /// </summary>
        public static event EventHandler<MouseLeftButtonClickStateChangedEventArgs> LeftButtonClickStateChanged;

        /// <summary>
        /// Invoked whenever any mouse button is pressed. Used by hotkey capture in the UI.
        /// </summary>
        public static event Action<MouseButtonType> ButtonDownEvent;

        /// <summary>
        /// Invoked on mouse wheel scroll; the argument is true when scrolled up. Used by hotkey capture.
        /// </summary>
        public static event Action<bool> WheelEvent;

        /// <summary>Raise <see cref="WheelEvent"/>. Called from the SDL wheel dispatch.</summary>
        public static void RaiseWheelEvent(bool up) => WheelEvent?.Invoke(up);

        public static MouseInfo GetMyraMouseInfo()
        {
            var info = new MouseInfo();

            info.IsLeftButtonDown = LButtonPressed;
            info.IsRightButtonDown = RButtonPressed;
            info.IsMiddleButtonDown = MButtonPressed;
            info.Position = Position;

            MouseState fnaMouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();

            info.Wheel = fnaMouseState.ScrollWheelValue;

            return info;
        }

        /* Log a button press event at the given time. */
        public static void ButtonPress(MouseButtonType type)
        {
            CancelDoubleClick = false;

            switch (type)
            {
                case MouseButtonType.Left:
                    LButtonPressed = true;
                    LClickPosition = Position;

                    break;

                case MouseButtonType.Middle:
                    MButtonPressed = true;
                    MClickPosition = Position;

                    break;

                case MouseButtonType.Right:
                    RButtonPressed = true;
                    RClickPosition = Position;

                    break;

                case MouseButtonType.XButton1:
                    XButton1Pressed = true;
                    XButtonPressed = true;

                    break;

                case MouseButtonType.XButton2:
                    XButton2Pressed = true;
                    XButtonPressed = true;

                    break;
            }

            ButtonDownEvent?.Invoke(type);

            SDL.SDL_CaptureMouse(true);
        }

        /* Log a button release event at the given time */
        public static void ButtonRelease(MouseButtonType type)
        {
            switch (type)
            {
                case MouseButtonType.Left:
                    LButtonPressed = false;

                    break;

                case MouseButtonType.Middle:
                    MButtonPressed = false;

                    break;

                case MouseButtonType.Right:
                    RButtonPressed = false;

                    break;

                case MouseButtonType.XButton1:
                    XButton1Pressed = false;
                    XButtonPressed = XButton2Pressed;

                    break;

                case MouseButtonType.XButton2:
                    XButton2Pressed = false;
                    XButtonPressed = XButton1Pressed;

                    break;
            }

            if (!(LButtonPressed || RButtonPressed || MButtonPressed))
            {
                SDL.SDL_CaptureMouse(false);
            }
        }

        public static Point Position;

        public static Point LClickPosition;

        public static Point RClickPosition;

        public static Point MClickPosition;

        public static uint LastLeftButtonClickTime { get; set; }

        public static uint LastMidButtonClickTime { get; set; }

        public static uint LastRightButtonClickTime { get; set; }

        public static bool CancelDoubleClick { get; set; }

        public static bool LButtonPressed
        {
            get;
            set
            {
                if (field == value)
                    return;

                var eArgs = new MouseLeftButtonClickStateChangedEventArgs(field, value);

                field = value;
                LeftButtonClickStateChanged?.Invoke(null, eArgs);
            }
        }

        public static bool RButtonPressed { get; set; }

        public static bool MButtonPressed { get; set; }

        public static bool XButtonPressed { get; set; }

        public static bool XButton1Pressed { get; set; }

        public static bool XButton2Pressed { get; set; }

        public static bool IsDragging { get; set; }

        public static Point LDragOffset => LButtonPressed ? Position - LClickPosition : Point.Zero;

        public static Point RDragOffset => RButtonPressed ? Position - RClickPosition : Point.Zero;

        public static Point MDragOffset => MButtonPressed ? Position - MClickPosition : Point.Zero;

        public static bool MouseInWindow { get; set; }

        public static int ControllerSensitivity { get; set; } = 10;

        /// <summary>
        /// Frames per second the legacy per-frame sensitivity value was implicitly tuned
        /// against (<see cref="Configuration.Settings.FPS"/> defaults to 60).
        /// </summary>
        /// <remarks>
        /// <see cref="ControllerSensitivity"/> is serialized in user profiles and used to mean
        /// "pixels per frame", so its effective speed varied with framerate. Multiplying by
        /// this constant reinterprets the stored value as pixels per second, which keeps the
        /// familiar feel at 60 FPS while making it framerate independent everywhere else. This
        /// avoids having to migrate the persisted value.
        /// </remarks>
        private const float SensitivityReferenceFps = 60f;

        /// <summary>
        /// Fractional pixels carried between frames.
        /// </summary>
        /// <remarks>
        /// Cursor position is integral, but delta-time motion routinely produces sub-pixel
        /// steps at low stick deflection. Truncating each frame would silently discard them
        /// and the cursor would refuse to move slowly at all.
        /// </remarks>
        private static Vector2 _controllerSubPixel;

        private static bool _isWarpingMouse = false;

        public static void Update()
        {
            if (_isWarpingMouse)
                return;

            Point previous = Position;

            if (!MouseInWindow)
            {
                SDL.SDL_GetGlobalMouseState(out float x, out float y);
                SDL.SDL_GetWindowPosition(Client.Game.Window.Handle, out int winX, out int winY);
                Position.X = (int)x - winX;
                Position.Y = (int)y - winY;
            }
            else
            {
                SDL.SDL_GetMouseState(out float x, out float y);
                Position.X = (int)x;
                Position.Y = (int)y;
                UpdateControllerCursor();
            }

            Position.X = (int)((double)Position.X * Client.Game.GraphicManager.PreferredBackBufferWidth / Client.Game.Window.ClientBounds.Width);

            Position.Y = (int)((double)Position.Y * Client.Game.GraphicManager.PreferredBackBufferHeight / Client.Game.Window.ClientBounds.Height);

            IsDragging = LButtonPressed || RButtonPressed || MButtonPressed;

            // Check for null first;
            // While a point comparison is not a 'heavy' operation, a null check should generally be quicker.
            if (Moved != null && previous != Position)
                Moved?.Invoke(null, new MouseMovedEventArgs(previous, Position));
        }

        /// <summary>
        /// Advances the cursor from the right thumbstick, framerate independently.
        /// </summary>
        private static void UpdateControllerCursor()
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || !profile.ControllerEnabled)
            {
                _controllerSubPixel = Vector2.Zero;

                return;
            }

            GamePadState gamePadState = GamePad.GetState(PlayerIndex.One);

            if (!gamePadState.IsConnected)
            {
                _controllerSubPixel = Vector2.Zero;

                return;
            }

            Vector2 stick = ControllerAxis.Process(
                gamePadState.ThumbSticks.Right,
                profile.ControllerDeadzoneInner,
                profile.ControllerDeadzoneOuter,
                profile.ControllerCursorCurve
            );

            if (stick == Vector2.Zero)
            {
                // Drop the carried fraction so a released stick cannot nudge the cursor on
                // the next frame it is touched.
                _controllerSubPixel = Vector2.Zero;

                return;
            }

            float pixelsPerSecond = ControllerSensitivity * SensitivityReferenceFps;

            // Thumbstick Y is positive up, screen Y is positive down.
            _controllerSubPixel.X += stick.X * pixelsPerSecond * Time.Delta;
            _controllerSubPixel.Y -= stick.Y * pixelsPerSecond * Time.Delta;

            int stepX = (int)_controllerSubPixel.X;
            int stepY = (int)_controllerSubPixel.Y;

            if (stepX == 0 && stepY == 0)
            {
                // Sub-pixel motion this frame; keep accumulating rather than truncating it away.
                return;
            }

            _controllerSubPixel.X -= stepX;
            _controllerSubPixel.Y -= stepY;

            Position.X += stepX;
            Position.Y += stepY;

            _isWarpingMouse = true;
            SDL.SDL_WarpMouseInWindow(Client.Game.Window.Handle, Position.X, Position.Y);
            _isWarpingMouse = false;
        }
    }
}
