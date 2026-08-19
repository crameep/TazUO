using System;
using System.Collections.Generic;
using ClassicUO.Game.UI;
using ClassicUO.Input;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// Moves the pointer between interactive controls with the d-pad.
    /// </summary>
    /// <remarks>
    /// There is no separate focus state: navigating simply parks the cursor on the chosen control,
    /// so every existing hover, tooltip and click path keeps working untouched.
    /// </remarks>
    internal static class ControllerUINavigator
    {
        // Controls smaller than this are decorative slivers that focus should never stop on.
        private const int MIN_CANDIDATE_SIZE = 4;

        private static readonly List<Rectangle> _candidates = new();

        /// <summary>Moves the cursor to the nearest interactive control, returning false if none.</summary>
        public static bool Navigate(NavDirection direction)
        {
            IGui scope = FindScope();

            if (scope == null)
            {
                return false;
            }

            _candidates.Clear();

            // Myra windows (options, assistant) keep their own widget tree rather than IGui
            // children, so walking Children finds nothing in them.
            if (scope is UI.Controls.MyraControl myra)
            {
                myra.CollectControllerTargets(_candidates);
            }
            else
            {
                Collect(scope, _candidates);
            }

            if (_candidates.Count == 0)
            {
                return false;
            }

            Rectangle current = CurrentRect();

            // Cursor is outside the gump, so there is nothing to move away from; the first press
            // should pull it into the UI rather than fail on a direction test.
            if (!current.Intersects(ScreenBounds(scope)))
            {
                return SnapTo(_candidates[Nearest(current, _candidates)]);
            }

            int next = SpatialNavigation.FindNext(current, _candidates, direction);

            return next >= 0 && SnapTo(_candidates[next]);
        }

        private static bool SnapTo(Rectangle rect)
        {
            if (rect.Width <= 0)
            {
                return false;
            }

            Mouse.SnapVirtualCursorTo(new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)));

            return true;
        }

        private static int Nearest(Rectangle from, List<Rectangle> candidates)
        {
            int best = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                float dx = candidates[i].Center.X - from.Center.X;
                float dy = candidates[i].Center.Y - from.Center.Y;
                float distance = (dx * dx) + (dy * dy);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>The gump under the cursor, else the topmost one. Gumps are ordered front first.</summary>
        private static IGui FindScope()
        {
            Rectangle cursor = CurrentRect();
            IGui topmost = null;

            for (LinkedListNode<IGui> node = UIManager.Gumps.First; node != null; node = node.Next)
            {
                IGui gump = node.Value;

                if (!IsUsable(gump))
                {
                    continue;
                }

                topmost ??= gump;

                if (ScreenBounds(gump).Intersects(cursor))
                {
                    return gump;
                }
            }

            return topmost;
        }

        private static void Collect(IGui control, List<Rectangle> results)
        {
            List<IGui> children = control.Children;

            if (children == null)
            {
                return;
            }

            for (int i = 0; i < children.Count; i++)
            {
                IGui child = children[i];

                if (child == null || !IsUsable(child))
                {
                    continue;
                }

                int before = results.Count;
                Collect(child, results);

                // Only leaf-most interactive controls qualify: a gump background also accepts the
                // mouse, and including it would let focus land on empty space.
                if (results.Count == before && IsCandidate(child))
                {
                    results.Add(ScreenBounds(child));
                }
            }
        }

        private static bool IsUsable(IGui control)
            => !control.IsDisposed && control.IsVisible && control.IsEnabled;

        private static bool IsCandidate(IGui control)
            => control.AcceptMouseInput
               && control.Width >= MIN_CANDIDATE_SIZE
               && control.Height >= MIN_CANDIDATE_SIZE;

        private static Rectangle ScreenBounds(IGui control)
            => new(control.ScreenCoordinateX, control.ScreenCoordinateY, control.Width, control.Height);

        private static Rectangle CurrentRect() => new(Mouse.Position.X, Mouse.Position.Y, 1, 1);
    }
}
