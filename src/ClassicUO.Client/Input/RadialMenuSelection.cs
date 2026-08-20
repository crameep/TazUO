using System;
using Microsoft.Xna.Framework;

namespace ClassicUO.Input
{
    /// <summary>Maps a stick direction onto a radial menu slot.</summary>
    /// <remarks>
    /// Pure so the wedge boundaries can be tested directly; getting these wrong shows up as a menu
    /// that picks the neighbouring slot near the edges, which is very hard to spot by eye.
    /// </remarks>
    internal static class RadialMenuSelection
    {
        /// <summary>No slot is selected until the stick leaves the centre.</summary>
        public const float DEFAULT_DEADZONE = 0.35f;

        public const int NO_SELECTION = -1;

        /// <summary>Slot under <paramref name="direction"/>, or <see cref="NO_SELECTION"/>.</summary>
        /// <remarks>Slot 0 sits at the top and slots run clockwise, matching how they are drawn.</remarks>
        public static int SlotFromDirection(Vector2 direction, int slotCount, float deadzone = DEFAULT_DEADZONE)
        {
            if (slotCount <= 0 || direction.Length() < deadzone)
            {
                return NO_SELECTION;
            }

            // Thumbstick Y is positive up, and atan2(x, y) puts zero at straight up and grows
            // clockwise, which is the order the slots are laid out in.
            float angle = (float)Math.Atan2(direction.X, direction.Y);

            if (angle < 0f)
            {
                angle += MathHelper.TwoPi;
            }

            float slice = MathHelper.TwoPi / slotCount;

            // Round rather than truncate so a slot is centred on its angle instead of starting at it.
            return (int)Math.Round(angle / slice) % slotCount;
        }

        /// <summary>Centre offset of a slot's label, given the ring radius.</summary>
        public static Vector2 SlotOffset(int slot, int slotCount, float radius)
        {
            if (slotCount <= 0)
            {
                return Vector2.Zero;
            }

            float angle = slot * (MathHelper.TwoPi / slotCount);

            // Screen Y grows downward, so the vertical term is negated to keep slot 0 at the top.
            return new Vector2((float)Math.Sin(angle) * radius, -(float)Math.Cos(angle) * radius);
        }
    }
}
