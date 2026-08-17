using System.Collections.Generic;
using ClassicUO.Game.UI;
using FluentAssertions;
using Microsoft.Xna.Framework;
using Xunit;

namespace ClassicUO.UnitTests.Game.UI
{
    public class SpatialNavigationTest
    {
        private static Rectangle At(int x, int y) => new(x, y, 20, 20);

        [Fact]
        public void Returns_Minus_One_When_No_Candidates()
        {
            SpatialNavigation.FindNext(At(0, 0), new List<Rectangle>(), NavDirection.Down).Should().Be(-1);
            SpatialNavigation.FindNext(At(0, 0), null, NavDirection.Down).Should().Be(-1);
        }

        [Fact]
        public void Returns_Minus_One_When_Nothing_Lies_In_That_Direction()
        {
            var candidates = new List<Rectangle> { At(0, 100) };

            SpatialNavigation.FindNext(At(0, 200), candidates, NavDirection.Down).Should().Be(-1);
        }

        [Fact]
        public void Picks_The_Nearest_In_Each_Direction()
        {
            Rectangle current = At(100, 100);

            var candidates = new List<Rectangle>
            {
                At(100, 40),  // 0 up
                At(100, 160), // 1 down
                At(40, 100),  // 2 left
                At(160, 100)  // 3 right
            };

            SpatialNavigation.FindNext(current, candidates, NavDirection.Up).Should().Be(0);
            SpatialNavigation.FindNext(current, candidates, NavDirection.Down).Should().Be(1);
            SpatialNavigation.FindNext(current, candidates, NavDirection.Left).Should().Be(2);
            SpatialNavigation.FindNext(current, candidates, NavDirection.Right).Should().Be(3);
        }

        [Fact]
        public void Prefers_The_Closer_Of_Two_In_Line()
        {
            Rectangle current = At(0, 0);

            var candidates = new List<Rectangle> { At(0, 200), At(0, 60) };

            SpatialNavigation.FindNext(current, candidates, NavDirection.Down).Should().Be(1);
        }

        /// <summary>
        /// The reason for the perpendicular penalty: a control directly below should win over a
        /// slightly nearer one far off to the side, or focus appears to wander sideways.
        /// </summary>
        [Fact]
        public void Prefers_Directly_Below_Over_Nearer_But_Offset()
        {
            Rectangle current = At(100, 0);

            var candidates = new List<Rectangle>
            {
                At(400, 40),  // 0 nearer vertically, far to the side
                At(100, 60)   // 1 directly below, slightly further
            };

            SpatialNavigation.FindNext(current, candidates, NavDirection.Down).Should().Be(1);
        }

        [Fact]
        public void Ignores_The_Currently_Focused_Rectangle()
        {
            Rectangle current = At(0, 0);

            var candidates = new List<Rectangle> { At(0, 0), At(0, 50) };

            SpatialNavigation.FindNext(current, candidates, NavDirection.Down).Should().Be(1);
        }

        /// <summary>Controls sharing a row must not be reachable by an up or down press.</summary>
        [Fact]
        public void Same_Row_Is_Not_Reachable_Vertically()
        {
            Rectangle current = At(100, 100);

            var candidates = new List<Rectangle> { At(200, 100) };

            SpatialNavigation.FindNext(current, candidates, NavDirection.Down).Should().Be(-1);
            SpatialNavigation.FindNext(current, candidates, NavDirection.Up).Should().Be(-1);
            SpatialNavigation.FindNext(current, candidates, NavDirection.Right).Should().Be(0);
        }

        [Fact]
        public void Navigation_Is_Reversible_On_A_Simple_Column()
        {
            var candidates = new List<Rectangle> { At(0, 0), At(0, 50), At(0, 100) };

            int down = SpatialNavigation.FindNext(candidates[0], candidates, NavDirection.Down);
            down.Should().Be(1);

            int backUp = SpatialNavigation.FindNext(candidates[down], candidates, NavDirection.Up);
            backUp.Should().Be(0);
        }

        [Fact]
        public void Works_With_Differently_Sized_Controls()
        {
            Rectangle current = new(0, 0, 200, 20);

            var candidates = new List<Rectangle>
            {
                new(0, 40, 10, 10),
                new(90, 40, 20, 20)
            };

            // Centre of the wide control is x=100, so the candidate centred at x=100 wins.
            SpatialNavigation.FindNext(current, candidates, NavDirection.Down).Should().Be(1);
        }
    }
}
