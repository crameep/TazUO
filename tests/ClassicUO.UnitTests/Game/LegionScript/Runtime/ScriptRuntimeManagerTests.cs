using System.Collections.Generic;
using ClassicUO.LegionScripting.Runtime;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript.Runtime;

public class ScriptRuntimeManagerTests
{
    [Fact]
    public void Scheduler_Should_Not_Starve_Low_Priority_Scripts()
    {
        var runtime = new ScriptRuntimeManager();

        int highSteps = 0;
        int lowSteps = 0;

        runtime.StartScript("high", _ =>
        {
            highSteps++;
            return ScriptDirective.Yield();
        }, ScriptPriority.High);

        runtime.StartScript("low", _ =>
        {
            lowSteps++;
            return ScriptDirective.Yield();
        }, ScriptPriority.Low);

        for (int i = 0; i < 200; i++)
            runtime.Tick(maxStepsPerTick: 1);

        lowSteps.Should().BeGreaterThan(0, "low priority scripts must still make forward progress");
        highSteps.Should().BeGreaterThan(lowSteps);
    }

    [Fact]
    public void Cancellation_And_Timeout_Should_Be_Deterministic()
    {
        var runtime = new ScriptRuntimeManager();

        ScriptContext cancelContext = runtime.StartScript("cancel-me", _ => ScriptDirective.Yield());
        ScriptContext waitContext = runtime.StartScript("wait-timeout", _ => ScriptDirective.WaitForEvent("target", timeoutTicks: 2));
        runtime.Subscribe(waitContext.Id, "target");

        runtime.CancelScript(cancelContext.Id, "requested").Should().BeTrue();

        runtime.Tick(); // tick 1
        runtime.Tick(); // tick 2
        runtime.Tick(); // tick 3 -> timeout trip on waitContext

        cancelContext.State.Should().Be(ScriptState.Cancelled);
        waitContext.State.Should().Be(ScriptState.Faulted);
        waitContext.LastError.Should().Be("timeout");
    }

    [Fact]
    public void Event_Mailbox_Should_Preserve_Publish_Order()
    {
        var runtime = new ScriptRuntimeManager();
        var seen = new List<int>();

        ScriptContext context = runtime.StartScript("event-reader", execution =>
        {
            while (execution.TryDequeueEvent(out ScriptEvent scriptEvent))
                seen.Add((int)scriptEvent.Payload);

            return ScriptDirective.Complete();
        });

        runtime.Subscribe(context.Id, "journal");

        runtime.PublishEvent("journal", 1);
        runtime.PublishEvent("journal", 2);
        runtime.PublishEvent("journal", 3);

        runtime.Tick();

        seen.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Tick_Should_Respect_Max_Steps_Per_Tick()
    {
        var runtime = new ScriptRuntimeManager();

        for (int i = 0; i < 100; i++)
            runtime.StartScript($"s-{i}", _ => ScriptDirective.Yield());

        ScriptTickMetrics metrics = runtime.Tick(maxStepsPerTick: 10);

        metrics.ExecutedSteps.Should().BeLessOrEqualTo(10);
        metrics.RunnableScripts.Should().Be(100);
    }

    [Fact]
    public void Three_Scripts_Should_Progress_Without_Blocking_Tick()
    {
        var runtime = new ScriptRuntimeManager();

        int a = 0;
        int b = 0;
        int c = 0;

        runtime.StartScript("a", _ =>
        {
            a++;
            return ScriptDirective.Yield();
        });

        runtime.StartScript("b", _ =>
        {
            b++;
            return ScriptDirective.Yield();
        });

        runtime.StartScript("c", _ =>
        {
            c++;
            return ScriptDirective.Yield();
        });

        for (int i = 0; i < 50; i++)
        {
            ScriptTickMetrics metrics = runtime.Tick(maxStepsPerTick: 3);
            metrics.ExecutedSteps.Should().BeLessOrEqualTo(3);
        }

        a.Should().BeGreaterThan(0);
        b.Should().BeGreaterThan(0);
        c.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Action_Queue_Should_Be_Authoritative_And_Ordered()
    {
        var runtime = new ScriptRuntimeManager();

        runtime.StartScript("a", execution =>
        {
            execution.EnqueueAction("cast", "greater-heal");
            return ScriptDirective.Complete();
        });

        runtime.StartScript("b", execution =>
        {
            execution.EnqueueAction("use-item", "heal-potion");
            return ScriptDirective.Complete();
        });

        runtime.Tick(maxStepsPerTick: 2);
        List<ScriptAction> actions = runtime.DrainActions();

        actions.Should().HaveCount(2);
        actions[0].Sequence.Should().BeLessThan(actions[1].Sequence);
        actions[0].ActionType.Should().Be("cast");
        actions[1].ActionType.Should().Be("use-item");
    }

    [Fact]
    public void Tick_Metrics_Should_Report_Pending_Actions_For_Bounded_Work()
    {
        var runtime = new ScriptRuntimeManager();

        runtime.StartScript("spammer", execution =>
        {
            execution.EnqueueAction("noop");
            return ScriptDirective.Yield();
        });

        ScriptTickMetrics metrics = runtime.Tick(maxStepsPerTick: 1);

        metrics.ExecutedSteps.Should().Be(1);
        metrics.PendingActions.Should().Be(1);
    }
}
