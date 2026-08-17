using ClassicUO.Game.Data;
using ClassicUO.Input;
using FluentAssertions;
using Microsoft.Xna.Framework;
using Xunit;

namespace ClassicUO.UnitTests.Input
{
    public class ControllerAxisTest
    {
        private const float Tolerance = 0.0001f;

        // ------------------------------------------------------------------
        // ApplyRadialDeadzone
        // ------------------------------------------------------------------

        [Fact]
        public void Deadzone_Zero_Input_Returns_Zero()
        {
            ControllerAxis.ApplyRadialDeadzone(Vector2.Zero, 0.25f, 1f).Should().Be(Vector2.Zero);
        }

        [Fact]
        public void Deadzone_Below_Inner_Returns_Zero()
        {
            Vector2 result = ControllerAxis.ApplyRadialDeadzone(new Vector2(0.1f, 0f), 0.25f, 1f);

            result.Should().Be(Vector2.Zero);
        }

        [Fact]
        public void Deadzone_At_Inner_Returns_Zero()
        {
            Vector2 result = ControllerAxis.ApplyRadialDeadzone(new Vector2(0.25f, 0f), 0.25f, 1f);

            result.Length().Should().BeApproximately(0f, Tolerance);
        }

        /// <summary>
        /// The whole point of a rescaled deadzone: crossing the inner edge must ramp up from
        /// zero rather than snapping straight to the raw magnitude. A naive implementation that
        /// simply passes the raw vector through once it clears the threshold jumps to 0.25 here.
        /// </summary>
        [Fact]
        public void Deadzone_Just_Above_Inner_Does_Not_Jump()
        {
            Vector2 result = ControllerAxis.ApplyRadialDeadzone(new Vector2(0.2501f, 0f), 0.25f, 1f);

            result.Length().Should().BeLessThan(0.01f);
        }

        [Fact]
        public void Deadzone_At_Outer_Returns_Full_Magnitude()
        {
            Vector2 result = ControllerAxis.ApplyRadialDeadzone(new Vector2(1f, 0f), 0.25f, 1f);

            result.Length().Should().BeApproximately(1f, Tolerance);
        }

        [Fact]
        public void Deadzone_Beyond_Outer_Is_Clamped_To_One()
        {
            // Diagonal raw input has magnitude ~1.414, which real sticks do report.
            Vector2 result = ControllerAxis.ApplyRadialDeadzone(new Vector2(1f, 1f), 0.25f, 1f);

            result.Length().Should().BeApproximately(1f, Tolerance);
        }

        [Fact]
        public void Deadzone_Preserves_Direction()
        {
            Vector2 raw = new Vector2(0.6f, 0.8f); // magnitude exactly 1.0
            Vector2 result = ControllerAxis.ApplyRadialDeadzone(raw, 0.25f, 1f);

            Vector2 rawNormalised = Vector2.Normalize(raw);
            Vector2 resultNormalised = Vector2.Normalize(result);

            resultNormalised.X.Should().BeApproximately(rawNormalised.X, Tolerance);
            resultNormalised.Y.Should().BeApproximately(rawNormalised.Y, Tolerance);
        }

        /// <summary>
        /// Guards the defect this replaces. A per-axis threshold of 0.3 rejects (0.25, 0.25)
        /// because neither component clears it, even though the stick is pushed to magnitude
        /// 0.354. A radial deadzone accepts it.
        /// </summary>
        [Fact]
        public void Deadzone_Is_Radial_Not_Per_Axis()
        {
            Vector2 result = ControllerAxis.ApplyRadialDeadzone(new Vector2(0.25f, 0.25f), 0.2f, 1f);

            result.Should().NotBe(Vector2.Zero);
        }

        [Fact]
        public void Deadzone_Is_Monotonic_In_Magnitude()
        {
            float previous = -1f;

            for (float m = 0f; m <= 1f; m += 0.05f)
            {
                float current = ControllerAxis.ApplyRadialDeadzone(new Vector2(m, 0f), 0.25f, 1f).Length();

                current.Should().BeGreaterThanOrEqualTo(previous);
                previous = current;
            }
        }

        [Fact]
        public void Deadzone_Handles_Inner_Equal_To_Outer_Without_Dividing_By_Zero()
        {
            Vector2 result = ControllerAxis.ApplyRadialDeadzone(new Vector2(0.5f, 0f), 0.5f, 0.5f);

            float length = result.Length();
            float.IsNaN(length).Should().BeFalse();
            float.IsInfinity(length).Should().BeFalse();
        }

        // ------------------------------------------------------------------
        // ApplyResponseCurve
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0f)]
        [InlineData(0.25f)]
        [InlineData(0.5f)]
        [InlineData(1f)]
        public void Curve_Exponent_One_Is_Identity(float magnitude)
        {
            ControllerAxis.ApplyResponseCurve(magnitude, 1f).Should().BeApproximately(magnitude, Tolerance);
        }

        [Fact]
        public void Curve_Preserves_Endpoints()
        {
            ControllerAxis.ApplyResponseCurve(0f, 2.5f).Should().BeApproximately(0f, Tolerance);
            ControllerAxis.ApplyResponseCurve(1f, 2.5f).Should().BeApproximately(1f, Tolerance);
        }

        [Fact]
        public void Curve_Above_One_Gives_Finer_Control_Near_Centre()
        {
            // Higher exponent must pull mid-range values down, so small stick
            // deflections produce proportionally smaller output.
            ControllerAxis.ApplyResponseCurve(0.5f, 2f).Should().BeLessThan(0.5f);
        }

        [Fact]
        public void Curve_Is_Monotonic()
        {
            float previous = -1f;

            for (float m = 0f; m <= 1f; m += 0.05f)
            {
                float current = ControllerAxis.ApplyResponseCurve(m, 2f);

                current.Should().BeGreaterThanOrEqualTo(previous);
                previous = current;
            }
        }

        // ------------------------------------------------------------------
        // ToOctant
        //
        // The expected mapping reproduces the behaviour of the threshold cascade this
        // replaces, so existing muscle memory is preserved. Thumbstick Y is positive up.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(1f, 0f, Direction.Right)]
        [InlineData(1f, 1f, Direction.North)]
        [InlineData(0f, 1f, Direction.Up)]
        [InlineData(-1f, 1f, Direction.West)]
        [InlineData(-1f, 0f, Direction.Left)]
        [InlineData(-1f, -1f, Direction.South)]
        [InlineData(0f, -1f, Direction.Down)]
        [InlineData(1f, -1f, Direction.East)]
        public void Octant_Maps_The_Eight_Compass_Pushes(float x, float y, Direction expected)
        {
            ControllerAxis.ToOctant(new Vector2(x, y)).Should().Be(expected);
        }

        [Theory]
        [InlineData(1f, 0.2f, Direction.Right)]   // slightly up of due right
        [InlineData(1f, -0.2f, Direction.Right)]  // slightly down of due right
        [InlineData(0.2f, 1f, Direction.Up)]
        [InlineData(-0.2f, 1f, Direction.Up)]
        public void Octant_Is_Stable_Within_A_Sector(float x, float y, Direction expected)
        {
            ControllerAxis.ToOctant(new Vector2(x, y)).Should().Be(expected);
        }

        [Fact]
        public void Octant_Is_Independent_Of_Magnitude()
        {
            Direction near = ControllerAxis.ToOctant(new Vector2(0.05f, 0.05f));
            Direction far = ControllerAxis.ToOctant(new Vector2(0.95f, 0.95f));

            near.Should().Be(far);
        }

        /// <summary>
        /// Every sector must be the same angular width. Sampling the centre of each 45 degree
        /// sector must yield eight distinct directions covering the whole enum.
        /// </summary>
        [Fact]
        public void Octant_Sectors_Are_Symmetric_And_Cover_All_Eight_Directions()
        {
            var seen = new System.Collections.Generic.HashSet<Direction>();

            for (int i = 0; i < 8; i++)
            {
                double angle = i * System.Math.PI / 4d;
                var v = new Vector2((float)System.Math.Cos(angle), (float)System.Math.Sin(angle));

                seen.Add(ControllerAxis.ToOctant(v));
            }

            seen.Should().HaveCount(8);
        }

        // ------------------------------------------------------------------
        // ShouldRun
        // ------------------------------------------------------------------

        /// <summary>
        /// The defect this replaces: run was decided per-axis, so a diagonal push of
        /// (0.45, 0.45) walked while a cardinal 0.55 ran. Both sit either side of a
        /// magnitude threshold of 0.5, and the diagonal is the *further* push
        /// (magnitude 0.636 vs 0.55), so it must run too.
        /// </summary>
        [Fact]
        public void Run_Threshold_Is_Radial_Not_Per_Axis()
        {
            ControllerAxis.ShouldRun(new Vector2(0.45f, 0.45f), 0.5f).Should().BeTrue();
            ControllerAxis.ShouldRun(new Vector2(0.55f, 0f), 0.5f).Should().BeTrue();
        }

        [Fact]
        public void Run_Is_False_Below_Threshold()
        {
            ControllerAxis.ShouldRun(new Vector2(0.3f, 0f), 0.5f).Should().BeFalse();
            ControllerAxis.ShouldRun(new Vector2(0.3f, 0.3f), 0.5f).Should().BeFalse();
        }
    }
}
