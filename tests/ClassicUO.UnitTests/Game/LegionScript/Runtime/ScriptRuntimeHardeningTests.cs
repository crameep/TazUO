using System.Collections.Generic;
using ClassicUO.LegionScripting.Runtime;
using ClassicUO.LegionScripting.Runtime.Host;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript.Runtime;

public class ScriptRuntimeHardeningTests
{
    [Fact]
    public void Watchdog_Should_Fault_Stuck_Waiting_Script_And_Allow_Others_To_Progress()
    {
        var runtime = new ScriptRuntimeManager(options: new ScriptRuntimeOptions { WatchdogMaxWaitingTicks = 3 });

        ScriptContext stuck = runtime.StartScript("stuck", _ => ScriptDirective.WaitForEvent("never-happens"));
        runtime.Subscribe(stuck.Id, "never-happens");

        int healthySteps = 0;
        runtime.StartScript("healthy", _ =>
        {
            healthySteps++;
            return ScriptDirective.Yield();
        });

        runtime.Tick(maxStepsPerTick: 2);
        runtime.Tick(maxStepsPerTick: 2);
        runtime.Tick(maxStepsPerTick: 2);
        ScriptTickMetrics metrics = runtime.Tick(maxStepsPerTick: 2);

        metrics.WatchdogFaults.Should().BeGreaterThan(0);
        stuck.State.Should().Be(ScriptState.Faulted);
        stuck.LastError.Should().Be("watchdog.wait-timeout");
        healthySteps.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Telemetry_Should_Emit_Runtime_And_PerScript_Metrics()
    {
        var telemetry = new RecordingTelemetrySink();
        var host = new RuntimeHostServices(new RuntimeAppLifecycleAdapter(), new RuntimeNetworkSessionAdapter(), new RuntimeTouchInputAdapter(), null, telemetry);
        var runtime = new ScriptRuntimeManager(host: host);

        runtime.StartScript("telemetry", execution =>
        {
            execution.EnqueueAction("noop");
            return ScriptDirective.Yield();
        });

        runtime.Tick(maxStepsPerTick: 1);

        telemetry.Names.Should().Contain("runtime.tick.executed_steps");
        telemetry.Names.Should().Contain("runtime.tick.pending_actions");
        telemetry.Names.Should().Contain("runtime.active_contexts");
        telemetry.Names.Should().Contain(name => name.Contains("runtime.script.") && name.EndsWith(".state"));
    }

    [Fact]
    public void FaultInjection_Should_Fault_Target_Script_Deterministically()
    {
        var runtime = new ScriptRuntimeManager();
        ScriptContext context = runtime.StartScript("inject", _ => ScriptDirective.Yield());

        runtime.InjectFault(context.Id, "fault.injected.test").Should().BeTrue();

        context.State.Should().Be(ScriptState.Faulted);
        context.LastError.Should().Be("fault.injected.test");
        runtime.Faults.Should().Contain(f => f.ScriptId == context.Id && f.Reason == "fault.injected.test");
    }

    [Fact]
    public void LongRun_Should_Keep_Tick_Bounded_And_ActionQueue_Capped()
    {
        var runtime = new ScriptRuntimeManager(options: new ScriptRuntimeOptions { MaxActionsQueued = 32, WatchdogMaxWaitingTicks = 1000 });

        runtime.StartScript("spammer", execution =>
        {
            for (int i = 0; i < 4; i++)
                execution.EnqueueAction("burst", i);

            return ScriptDirective.Yield();
        });

        ScriptTickMetrics lastMetrics = null;

        for (int i = 0; i < 200; i++)
        {
            lastMetrics = runtime.Tick(maxStepsPerTick: 1);
            lastMetrics.ExecutedSteps.Should().BeLessOrEqualTo(1);
            lastMetrics.PendingActions.Should().BeLessOrEqualTo(32);
        }

        lastMetrics.Should().NotBeNull();
        lastMetrics.DroppedActions.Should().BeGreaterThan(0);
    }

    private sealed class RecordingTelemetrySink : IRuntimeTelemetrySink
    {
        public List<string> Names { get; } = new();

        public void PublishMetric(string name, object value)
        {
            Names.Add(name);
        }
    }
}
