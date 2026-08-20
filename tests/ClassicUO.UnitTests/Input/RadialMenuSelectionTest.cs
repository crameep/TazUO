using ClassicUO.Input;
using FluentAssertions;
using Microsoft.Xna.Framework;
using Xunit;

namespace ClassicUO.UnitTests.Input
{
    public class RadialMenuSelectionTest
    {
        [Fact]
        public void Centre_Selects_Nothing()
        {
            RadialMenuSelection.SlotFromDirection(Vector2.Zero, 8).Should().Be(RadialMenuSelection.NO_SELECTION);
        }

        [Fact]
        public void Just_Inside_The_Deadzone_Selects_Nothing()
        {
            var justInside = new Vector2(0f, RadialMenuSelection.DEFAULT_DEADZONE - 0.01f);

            RadialMenuSelection.SlotFromDirection(justInside, 8).Should().Be(RadialMenuSelection.NO_SELECTION);
        }

        [Theory]
        [InlineData(0f, 1f, 0)]    // up
        [InlineData(1f, 1f, 1)]    // up-right
        [InlineData(1f, 0f, 2)]    // right
        [InlineData(1f, -1f, 3)]   // down-right
        [InlineData(0f, -1f, 4)]   // down
        [InlineData(-1f, -1f, 5)]  // down-left
        [InlineData(-1f, 0f, 6)]   // left
        [InlineData(-1f, 1f, 7)]   // up-left
        public void Each_Compass_Direction_Picks_Its_Slot(float x, float y, int expected)
        {
            RadialMenuSelection.SlotFromDirection(new Vector2(x, y), 8).Should().Be(expected);
        }

        /// <summary>Slot 0 straddles the wrap point, so both sides of straight up must land on it.</summary>
        [Fact]
        public void Slot_Zero_Is_Centred_On_Up_Not_Started_By_It()
        {
            RadialMenuSelection.SlotFromDirection(new Vector2(0.1f, 1f), 8).Should().Be(0);
            RadialMenuSelection.SlotFromDirection(new Vector2(-0.1f, 1f), 8).Should().Be(0);
        }

        [Fact]
        public void Works_With_Fewer_Slots()
        {
            RadialMenuSelection.SlotFromDirection(new Vector2(0f, 1f), 4).Should().Be(0);
            RadialMenuSelection.SlotFromDirection(new Vector2(1f, 0f), 4).Should().Be(1);
            RadialMenuSelection.SlotFromDirection(new Vector2(0f, -1f), 4).Should().Be(2);
            RadialMenuSelection.SlotFromDirection(new Vector2(-1f, 0f), 4).Should().Be(3);
        }

        [Fact]
        public void Never_Returns_An_Out_Of_Range_Slot()
        {
            for (int degrees = 0; degrees < 360; degrees++)
            {
                float radians = MathHelper.ToRadians(degrees);
                var direction = new Vector2((float)System.Math.Sin(radians), (float)System.Math.Cos(radians));

                int slot = RadialMenuSelection.SlotFromDirection(direction, 8);

                slot.Should().BeInRange(0, 7);
            }
        }

        [Fact]
        public void Slot_Offsets_Place_Zero_Above_Centre_And_Run_Clockwise()
        {
            Vector2 up = RadialMenuSelection.SlotOffset(0, 8, 100f);
            up.X.Should().BeApproximately(0f, 0.01f);
            up.Y.Should().BeApproximately(-100f, 0.01f);

            Vector2 right = RadialMenuSelection.SlotOffset(2, 8, 100f);
            right.X.Should().BeApproximately(100f, 0.01f);
            right.Y.Should().BeApproximately(0f, 0.01f);
        }

        /// <summary>A slot's own offset direction must select that same slot, or aim and draw disagree.</summary>
        [Fact]
        public void Drawing_And_Selection_Agree()
        {
            for (int slot = 0; slot < 8; slot++)
            {
                Vector2 offset = RadialMenuSelection.SlotOffset(slot, 8, 1f);

                // Offsets are in screen space (Y down); the stick reports Y up.
                var stick = new Vector2(offset.X, -offset.Y);

                RadialMenuSelection.SlotFromDirection(stick, 8).Should().Be(slot);
            }
        }
    }
}
