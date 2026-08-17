using System.Collections.Generic;
using ClassicUO.Game;
using ClassicUO.Game.Managers;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game
{
    public class ControllerTargetSelectionTest
    {
        private static readonly List<uint> Three = new() { 10u, 20u, 30u };

        [Fact]
        public void Cycle_Empty_List_Selects_Nothing()
        {
            ControllerTargetSelection.Cycle(new List<uint>(), 10u, 1).Should().Be(0u);
            ControllerTargetSelection.Cycle(null, 10u, 1).Should().Be(0u);
        }

        [Fact]
        public void Cycle_Forward_Advances()
        {
            ControllerTargetSelection.Cycle(Three, 10u, 1).Should().Be(20u);
            ControllerTargetSelection.Cycle(Three, 20u, 1).Should().Be(30u);
        }

        [Fact]
        public void Cycle_Forward_Wraps()
        {
            ControllerTargetSelection.Cycle(Three, 30u, 1).Should().Be(10u);
        }

        [Fact]
        public void Cycle_Backward_Wraps()
        {
            ControllerTargetSelection.Cycle(Three, 10u, -1).Should().Be(30u);
            ControllerTargetSelection.Cycle(Three, 30u, -1).Should().Be(20u);
        }

        [Fact]
        public void Cycle_Zero_Direction_Holds()
        {
            ControllerTargetSelection.Cycle(Three, 20u, 0).Should().Be(20u);
        }

        /// <summary>
        /// A target that dies or walks out of range must not swallow the input; the next press
        /// should still land on something.
        /// </summary>
        [Fact]
        public void Cycle_Stale_Selection_Re_Enters_From_The_Matching_End()
        {
            ControllerTargetSelection.Cycle(Three, 999u, 1).Should().Be(10u);
            ControllerTargetSelection.Cycle(Three, 999u, -1).Should().Be(30u);
        }

        [Fact]
        public void Cycle_Single_Candidate_Stays_Put()
        {
            var one = new List<uint> { 42u };

            ControllerTargetSelection.Cycle(one, 42u, 1).Should().Be(42u);
            ControllerTargetSelection.Cycle(one, 42u, -1).Should().Be(42u);
        }

        [Fact]
        public void Cycle_Unselected_Start_Picks_First()
        {
            ControllerTargetSelection.Cycle(Three, 0u, 1).Should().Be(10u);
        }

        [Fact]
        public void Cycle_Full_Loop_Returns_To_Start()
        {
            uint current = 10u;

            for (int i = 0; i < Three.Count; i++)
            {
                current = ControllerTargetSelection.Cycle(Three, current, 1);
            }

            current.Should().Be(10u);
        }

        // ------------------------------------------------------------------
        // Filters
        // ------------------------------------------------------------------

        [Fact]
        public void Filter_Cycles_Forward_And_Wraps()
        {
            ControllerTargetSelection.CycleFilter(ScanTypeObject.Hostile, 1).Should().Be(ScanTypeObject.Mobiles);
            ControllerTargetSelection.CycleFilter(ScanTypeObject.Mobiles, 1).Should().Be(ScanTypeObject.Objects);
            ControllerTargetSelection.CycleFilter(ScanTypeObject.Objects, 1).Should().Be(ScanTypeObject.Hostile);
        }

        [Fact]
        public void Filter_Cycles_Backward_And_Wraps()
        {
            ControllerTargetSelection.CycleFilter(ScanTypeObject.Hostile, -1).Should().Be(ScanTypeObject.Objects);
            ControllerTargetSelection.CycleFilter(ScanTypeObject.Objects, -1).Should().Be(ScanTypeObject.Mobiles);
        }

        /// <summary>A filter outside the cycle (set by a macro) must still advance somewhere valid.</summary>
        [Fact]
        public void Filter_Outside_The_Cycle_Enters_At_The_Start()
        {
            ControllerTargetSelection.CycleFilter(ScanTypeObject.Party, 1)
                .Should().Be(ControllerTargetSelection.FilterOrder[1]);
        }

        [Fact]
        public void Filter_Order_Has_No_Duplicates()
        {
            ControllerTargetSelection.FilterOrder.Should().OnlyHaveUniqueItems();
        }
    }
}
