using System.Collections.Generic;
using ClassicUO.LegionScripting.Runtime;
using ClassicUO.LegionScripting.Runtime.Host;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript.Runtime;

public class ScriptRuntimeLifecycleTests
{
    [Fact]
    public void Suspend_Resume_Should_Be_Deterministic_And_Not_Advance_Tick_When_Suspended()
    {
        var lifecycle = new RuntimeAppLifecycleAdapter();
        var host = new RuntimeHostServices(lifecycle, new RuntimeNetworkSessionAdapter(), new RuntimeTouchInputAdapter(), null, null);
        var runtime = new ScriptRuntimeManager(host: host);

        int executed = 0;

        runtime.StartScript("worker", _ =>
        {
            executed++;
            return ScriptDirective.Yield();
        });

        ScriptTickMetrics first = runtime.Tick(maxStepsPerTick: 1);
        first.Tick.Should().Be(1);
        executed.Should().Be(1);

        lifecycle.NotifySuspended();
        ScriptTickMetrics suspended = runtime.Tick(maxStepsPerTick: 10);

        suspended.Tick.Should().Be(1);
        suspended.ExecutedSteps.Should().Be(0);
        executed.Should().Be(1);

        lifecycle.NotifyForeground();
        ScriptTickMetrics resumed = runtime.Tick(maxStepsPerTick: 1);
        resumed.Tick.Should().Be(2);
        resumed.ExecutedSteps.Should().Be(1);
        executed.Should().Be(2);
    }

    [Fact]
    public void Host_Adapters_Should_Publish_Network_And_Input_Events()
    {
        var lifecycle = new RuntimeAppLifecycleAdapter();
        var network = new RuntimeNetworkSessionAdapter();
        var input = new RuntimeTouchInputAdapter();
        var host = new RuntimeHostServices(lifecycle, network, input, null, null);
        var runtime = new ScriptRuntimeManager(host: host);

        var events = new List<string>();

        ScriptContext context = runtime.StartScript("listener", execution =>
        {
            while (execution.TryDequeueEvent(out ScriptEvent scriptEvent))
                events.Add(scriptEvent.EventType);

            if (events.Count >= 2)
                return ScriptDirective.Complete();

            return ScriptDirective.Yield();
        });

        runtime.Subscribe(context.Id, "host.network");
        runtime.Subscribe(context.Id, "host.input");

        input.Enqueue(new RuntimeInputEvent(RuntimeInputKind.Tap, 100, 120));
        network.NotifyDisconnected();
        runtime.Tick(maxStepsPerTick: 4);

        events.Should().ContainInOrder("host.network", "host.input");
    }
}
