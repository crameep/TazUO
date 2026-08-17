using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI
{
    /// <summary>Direction of a focus move requested by the d-pad.</summary>
    internal enum NavDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    /// <summary>
    /// Picks the next control to focus by position rather than declaration order.
    /// </summary>
    /// <remarks>
    /// Server-authored gumps place their contents at arbitrary coordinates and may overlap, so
    /// there is no meaningful tab order to follow; nearest-neighbour in the pressed direction is
    /// the only thing that behaves predictably across them.
    /// </remarks>
    internal static class SpatialNavigation
    {
        // Sideways drift costs more than distance travelled, so "down" prefers the control
        // directly below over a nearer one far off to the side.
        private const float PERPENDICULAR_PENALTY = 2f;

        /// <summary>Index of the best candidate in <paramref name="direction"/>, or -1 if none qualify.</summary>
        public static int FindNext(Rectangle current, IReadOnlyList<Rectangle> candidates, NavDirection direction)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return -1;
            }

            Vector2 from = Center(current);

            int best = -1;
            float bestScore = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                Rectangle candidate = candidates[i];

                if (candidate == current)
                {
                    continue;
                }

                Vector2 to = Center(candidate);

                float along = AlongAxis(from, to, direction);

                // Must actually lie in the pressed direction; ties at zero would let focus
                // bounce between two controls on the same row.
                if (along <= 0f)
                {
                    continue;
                }

                float across = AcrossAxis(from, to, direction);
                float score = along + (across * PERPENDICULAR_PENALTY);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private static Vector2 Center(Rectangle r) => new(r.X + (r.Width / 2f), r.Y + (r.Height / 2f));

        private static float AlongAxis(Vector2 from, Vector2 to, NavDirection direction) => direction switch
        {
            NavDirection.Up => from.Y - to.Y,
            NavDirection.Down => to.Y - from.Y,
            NavDirection.Left => from.X - to.X,
            NavDirection.Right => to.X - from.X,
            _ => 0f
        };

        private static float AcrossAxis(Vector2 from, Vector2 to, NavDirection direction) => direction switch
        {
            NavDirection.Up or NavDirection.Down => Math.Abs(to.X - from.X),
            NavDirection.Left or NavDirection.Right => Math.Abs(to.Y - from.Y),
            _ => 0f
        };
    }
}
