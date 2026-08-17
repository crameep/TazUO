using System;
using ClassicUO.Game.Data;
using Microsoft.Xna.Framework;

namespace ClassicUO.Input
{
    /// <summary>
    /// Pure analog-stick maths shared by character movement and cursor motion.
    /// <para>
    /// Kept free of game state so the behaviour can be unit tested without a running
    /// game loop. Callers are expected to feed raw stick values straight from the pad
    /// and use the processed result for both direction and speed.
    /// </para>
    /// </summary>
    internal static class ControllerAxis
    {
        /// <summary>Below this the maths is treated as a zero-length vector.</summary>
        private const float Epsilon = 0.0001f;

        /// <summary>
        /// Octant index (counter-clockwise from due right) to UO direction.
        /// <para>
        /// Reproduces the mapping of the threshold cascade this replaces, so existing
        /// muscle memory is preserved: pushing up walks <see cref="Direction.Up"/>,
        /// up-right walks <see cref="Direction.North"/>, and so on around the circle.
        /// </para>
        /// </summary>
        private static readonly Direction[] _octantToDirection =
        {
            Direction.Right, // 0 degrees
            Direction.North, // 45
            Direction.Up,    // 90
            Direction.West,  // 135
            Direction.Left,  // 180
            Direction.South, // 225
            Direction.Down,  // 270
            Direction.East   // 315
        };

        /// <summary>
        /// Applies a radial deadzone and rescales the remaining travel back across the full
        /// 0..1 range.
        /// <para>
        /// The deadzone is radial rather than per-axis, so the dead region is a circle and a
        /// diagonal push is treated the same as a cardinal one of equal magnitude. Rescaling
        /// means output ramps up smoothly from zero as the stick leaves the dead region
        /// instead of snapping to the raw magnitude, which is what produces the
        /// characteristic lurch of a naive deadzone.
        /// </para>
        /// </summary>
        /// <param name="raw">Raw stick vector, Y positive up.</param>
        /// <param name="inner">Magnitude below which input is discarded.</param>
        /// <param name="outer">Magnitude treated as fully deflected.</param>
        /// <returns>A vector in the same direction as <paramref name="raw"/> with magnitude 0..1.</returns>
        public static Vector2 ApplyRadialDeadzone(Vector2 raw, float inner, float outer)
        {
            float magnitude = raw.Length();

            if (magnitude <= inner || magnitude <= Epsilon)
            {
                return Vector2.Zero;
            }

            Vector2 direction = raw / magnitude;
            float range = outer - inner;

            // Degenerate configuration (inner >= outer): anything clearing the inner edge
            // is simply full deflection rather than a division by zero.
            if (range <= Epsilon)
            {
                return direction;
            }

            float scaled = MathHelper.Clamp((magnitude - inner) / range, 0f, 1f);

            return direction * scaled;
        }

        /// <summary>
        /// Shapes a 0..1 magnitude with a power curve.
        /// <para>
        /// An exponent of 1 is linear. Higher values reduce mid-range output, giving finer
        /// control near centre while still reaching full speed at full deflection, which is
        /// what makes a stick-driven cursor usable for small adjustments.
        /// </para>
        /// </summary>
        public static float ApplyResponseCurve(float magnitude, float exponent)
        {
            if (magnitude <= 0f)
            {
                return 0f;
            }

            if (exponent <= 0f || Math.Abs(exponent - 1f) < Epsilon)
            {
                return magnitude;
            }

            return (float)Math.Pow(magnitude, exponent);
        }

        /// <summary>
        /// Maps a stick vector to one of the eight UO walk directions by angle.
        /// <para>
        /// Every sector is an equal 45 degrees, unlike the threshold cascade this replaces
        /// where sector shape depended on branch order. Magnitude is ignored; gate on
        /// <see cref="ApplyRadialDeadzone"/> before calling.
        /// </para>
        /// </summary>
        public static Direction ToOctant(Vector2 dir)
        {
            if (dir.LengthSquared() <= Epsilon * Epsilon)
            {
                return _octantToDirection[0];
            }

            double angle = Math.Atan2(dir.Y, dir.X);

            if (angle < 0d)
            {
                angle += Math.PI * 2d;
            }

            int octant = (int)Math.Round(angle / (Math.PI / 4d)) % 8;

            return _octantToDirection[octant];
        }

        /// <summary>
        /// Whether a processed stick vector should run rather than walk.
        /// <para>
        /// Compares magnitude against the threshold, so the walk/run boundary is a circle.
        /// The previous per-axis test made it a square, which meant a diagonal push had to
        /// travel considerably further than a cardinal one before it would run.
        /// </para>
        /// </summary>
        public static bool ShouldRun(Vector2 processed, float threshold)
        {
            return processed.LengthSquared() >= threshold * threshold;
        }

        /// <summary>
        /// Convenience wrapper applying <see cref="ApplyRadialDeadzone"/> then
        /// <see cref="ApplyResponseCurve"/>, preserving direction.
        /// </summary>
        public static Vector2 Process(Vector2 raw, float inner, float outer, float exponent)
        {
            Vector2 deadzoned = ApplyRadialDeadzone(raw, inner, outer);

            if (deadzoned == Vector2.Zero)
            {
                return Vector2.Zero;
            }

            float magnitude = deadzoned.Length();
            float shaped = ApplyResponseCurve(magnitude, exponent);

            return (deadzoned / magnitude) * shaped;
        }
    }
}
