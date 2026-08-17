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

        // ControllerSensitivity is persisted meaning "pixels per frame", so its speed varied with
        // framerate. Scaling by the default 60 FPS reinterprets it as pixels per second, keeping
        // the familiar feel at 60 without having to migrate the stored value.
        private const float SensitivityReferenceFps = 60f;

        /// <summary>Which device last moved the pointer.</summary>
        public enum PointerSource
        {
            Mouse,
            Controller
        }

        // Whichever device moved most recently wins. Without this the two fight every frame: the
        // stick advances the cursor and the stationary OS position immediately drags it back.
        public static PointerSource ActiveSource { get; private set; } = PointerSource.Mouse;

        // Client-owned cursor, in WINDOW coordinates. Float so sub-pixel motion accumulates rather
        // than truncating away. Window coords deliberately: Position is rescaled to back buffer
        // coords each Update, so persisting a scaled value would reapply the ratio and run away.
        private static Vector2 _virtualCursor;

        /// <summary>Last OS cursor position seen, used to detect real mouse movement.</summary>
        private static Point _lastOsPosition;

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

                Point osPosition = new((int)x, (int)y);
                Vector2 stick = ReadControllerStick();

                if (HasMovedMeaningfully(osPosition, _lastOsPosition))
                {
                    _lastOsPosition = osPosition;
                    ActiveSource = PointerSource.Mouse;
                }
                else if (stick != Vector2.Zero)
                {
                    ActiveSource = PointerSource.Controller;
                }

                if (ActiveSource == PointerSource.Controller)
                {
                    AdvanceVirtualCursor(stick);
                }
                else
                {
                    // Track the real pointer so handing control back to the pad does not
                    // teleport the cursor.
                    _virtualCursor.X = osPosition.X;
                    _virtualCursor.Y = osPosition.Y;
                }

                Position.X = (int)_virtualCursor.X;
                Position.Y = (int)_virtualCursor.Y;
            }

            Position.X = (int)((double)Position.X * Client.Game.GraphicManager.PreferredBackBufferWidth / Client.Game.Window.ClientBounds.Width);

            Position.Y = (int)((double)Position.Y * Client.Game.GraphicManager.PreferredBackBufferHeight / Client.Game.Window.ClientBounds.Height);

            IsDragging = LButtonPressed || RButtonPressed || MButtonPressed;

            // Check for null first;
            // While a point comparison is not a 'heavy' operation, a null check should generally be quicker.
            if (Moved != null && previous != Position)
                Moved?.Invoke(null, new MouseMovedEventArgs(previous, Position));
        }

        /// <summary>Reads the right thumbstick, deadzoned and shaped, or zero when unavailable.</summary>
        private static Vector2 ReadControllerStick()
        {
            Profile profile = ProfileManager.CurrentProfile;

            if (profile == null || !profile.ControllerEnabled)
            {
                return Vector2.Zero;
            }

            GamePadState gamePadState = Controller.GetActiveState();

            if (!gamePadState.IsConnected)
            {
                return Vector2.Zero;
            }

            return ControllerAxis.Process(
                gamePadState.ThumbSticks.Right,
                profile.ControllerDeadzoneInner,
                profile.ControllerDeadzoneOuter,
                profile.ControllerCursorCurve
            );
        }

        /// <summary>Advances the client-owned cursor from the stick, clamped to the window.</summary>
        private static void AdvanceVirtualCursor(Vector2 stick)
        {
            // An idle stick still reaches here while the pad holds the pointer; re-warping every
            // frame to the same spot is pure waste and can make the cursor stutter.
            if (stick == Vector2.Zero)
            {
                return;
            }

            float pixelsPerSecond = ControllerSensitivity * SensitivityReferenceFps;

            // Thumbstick Y is positive up, screen Y is positive down.
            _virtualCursor.X += stick.X * pixelsPerSecond * Time.Delta;
            _virtualCursor.Y -= stick.Y * pixelsPerSecond * Time.Delta;

            ClampAndMirrorVirtualCursor();
        }

        // Warps do not always read back at exactly the requested pixel (display scaling rounds), and
        // an exact comparison would then see our own warp as physical movement and take the pointer
        // off the pad every frame. Deliberate mouse movement clears this easily.
        private const int MOUSE_MOVE_TOLERANCE = 2;

        private static bool HasMovedMeaningfully(Point current, Point previous)
            => Math.Abs(current.X - previous.X) > MOUSE_MOVE_TOLERANCE
               || Math.Abs(current.Y - previous.Y) > MOUSE_MOVE_TOLERANCE;

        /// <summary>Parks the cursor at a back buffer position and hands the pointer to the pad.</summary>
        public static void SnapVirtualCursorTo(Point backBufferPosition)
        {
            Rectangle client = Client.Game.Window.ClientBounds;
            int bufferWidth = Client.Game.GraphicManager.PreferredBackBufferWidth;
            int bufferHeight = Client.Game.GraphicManager.PreferredBackBufferHeight;

            if (bufferWidth <= 0 || bufferHeight <= 0)
            {
                return;
            }

            // Callers work in back buffer space; the virtual cursor is kept in window space.
            _virtualCursor.X = backBufferPosition.X * ((float)client.Width / bufferWidth);
            _virtualCursor.Y = backBufferPosition.Y * ((float)client.Height / bufferHeight);

            ActiveSource = PointerSource.Controller;

            ClampAndMirrorVirtualCursor();

            Position.X = backBufferPosition.X;
            Position.Y = backBufferPosition.Y;
        }

        /// <summary>Keeps the virtual cursor inside the window and mirrors it onto the OS cursor when visible.</summary>
        private static void ClampAndMirrorVirtualCursor()
        {
            Rectangle bounds = Client.Game.Window.ClientBounds;

            _virtualCursor.X = Math.Clamp(_virtualCursor.X, 0f, Math.Max(0f, bounds.Width - 1f));
            _virtualCursor.Y = Math.Clamp(_virtualCursor.Y, 0f, Math.Max(0f, bounds.Height - 1f));

            // With the mouse on its own thread the client hands the pointer graphic to SDL, so the
            // OS cursor is what the player sees and must be moved to match. Otherwise the client
            // draws its own cursor at Position and no warp is needed.
            if (!Settings.GlobalSettings.RunMouseInASeparateThread)
            {
                return;
            }

            int warpX = (int)_virtualCursor.X;
            int warpY = (int)_virtualCursor.Y;

            _isWarpingMouse = true;
            SDL.SDL_WarpMouseInWindow(Client.Game.Window.Handle, warpX, warpY);
            _isWarpingMouse = false;

            // Keep the baseline in step, or this warp reads back as physical mouse movement next
            // frame and yanks control away from the pad.
            _lastOsPosition = new Point(warpX, warpY);
        }
    }
}
