using System;
using ClassicUO.Game.Data;
using Microsoft.Xna.Framework;

namespace ClassicUO.Input
{
    /// <summary>Pure analog-stick maths shared by character movement and cursor motion.</summary>
    internal static class ControllerAxis
    {
        private const float Epsilon = 0.0001f;

        // Octant index counter-clockwise from due right. Reproduces the mapping of the threshold
        // cascade this replaces, so existing muscle memory is preserved.
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

        /// <summary>Applies a radial deadzone and rescales the remaining travel back across 0..1.</summary>
        public static Vector2 ApplyRadialDeadzone(Vector2 raw, float inner, float outer)
        {
            float magnitude = raw.Length();

            if (magnitude <= inner || magnitude <= Epsilon)
            {
                return Vector2.Zero;
            }

            Vector2 direction = raw / magnitude;
            float range = outer - inner;

            // Degenerate config (inner >= outer): treat anything past the edge as full deflection.
            if (range <= Epsilon)
            {
                return direction;
            }

            // Rescaling is what stops the output jumping straight to the raw magnitude at the edge.
            return direction * MathHelper.Clamp((magnitude - inner) / range, 0f, 1f);
        }

        /// <summary>Shapes a 0..1 magnitude with a power curve; higher exponents give finer control near centre.</summary>
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

        /// <summary>Maps a stick vector to one of the eight walk directions by angle, ignoring magnitude.</summary>
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

            return _octantToDirection[(int)Math.Round(angle / (Math.PI / 4d)) % 8];
        }

        /// <summary>Whether a processed stick vector should run; radial, so the boundary is a circle.</summary>
        public static bool ShouldRun(Vector2 processed, float threshold)
        {
            return processed.LengthSquared() >= threshold * threshold;
        }

        /// <summary>Deadzone then response curve, preserving direction.</summary>
        public static Vector2 Process(Vector2 raw, float inner, float outer, float exponent)
        {
            Vector2 deadzoned = ApplyRadialDeadzone(raw, inner, outer);

            if (deadzoned == Vector2.Zero)
            {
                return Vector2.Zero;
            }

            float magnitude = deadzoned.Length();

            return (deadzoned / magnitude) * ApplyResponseCurve(magnitude, exponent);
        }
    }
}
