using ClassicUO.Game.Managers.Structs;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers.Structs
{
    public class MoveRequestTest
    {
        [Fact]
        public void ResolvePickupAmount_NonStackable_ShouldAlwaysReturnOne()
        {
            MoveRequest.ResolvePickupAmount(0, 1, false).Should().Be(1);
            MoveRequest.ResolvePickupAmount(ushort.MaxValue, 1, false).Should().Be(1);
            MoveRequest.ResolvePickupAmount(10, 1, false).Should().Be(1);
        }

        [Fact]
        public void ResolvePickupAmount_NonStackableFlagWithStackAmount_ShouldTreatAsStack()
        {
            // Some shards may report stackable data inconsistently; if amount > 1 we should still move as stack.
            MoveRequest.ResolvePickupAmount(0, 12, false).Should().Be(12);
            MoveRequest.ResolvePickupAmount(ushort.MaxValue, 12, false).Should().Be(ushort.MaxValue);
            MoveRequest.ResolvePickupAmount(8, 12, false).Should().Be(8);
        }

        [Fact]
        public void ResolvePickupAmount_StackableSentinels_ShouldUseAvailableAmount()
        {
            MoveRequest.ResolvePickupAmount(0, 7, true).Should().Be(7);
            MoveRequest.ResolvePickupAmount(ushort.MaxValue, 7, true).Should().Be(ushort.MaxValue);
        }

        [Fact]
        public void ResolvePickupAmount_StackableExplicitAmount_ShouldClampToAvailable()
        {
            MoveRequest.ResolvePickupAmount(5, 10, true).Should().Be(5);
            MoveRequest.ResolvePickupAmount(15, 10, true).Should().Be(10);
        }
    }
}
