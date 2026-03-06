using ClassicUO.LegionScripting.Runtime.Host;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript.Runtime.Host;

public class RuntimeHostAdaptersTests
{
    [Fact]
    public void Lifecycle_Adapter_Should_Be_Idempotent_And_Observable()
    {
        var lifecycle = new RuntimeAppLifecycleAdapter();
        int transitions = 0;

        lifecycle.StateChanged += _ => transitions++;

        lifecycle.NotifySuspended();
        lifecycle.NotifySuspended();
        lifecycle.NotifyForeground();
        lifecycle.NotifyForeground();

        transitions.Should().Be(2);
        lifecycle.State.Should().Be(RuntimeLifecycleState.Foreground);
    }

    [Fact]
    public void Touch_Adapter_Should_Drain_Events_In_Order()
    {
        var input = new RuntimeTouchInputAdapter();

        input.Enqueue(new RuntimeInputEvent(RuntimeInputKind.Tap, 10, 20));
        input.Enqueue(new RuntimeInputEvent(RuntimeInputKind.LongPress, 30, 40));

        var drained = input.DrainEvents();

        drained.Should().HaveCount(2);
        drained[0].Kind.Should().Be(RuntimeInputKind.Tap);
        drained[1].Kind.Should().Be(RuntimeInputKind.LongPress);
        input.DrainEvents().Should().BeEmpty();
    }
}
