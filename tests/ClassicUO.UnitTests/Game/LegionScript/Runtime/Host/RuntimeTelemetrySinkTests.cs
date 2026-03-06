using System;
using System.IO;
using ClassicUO.LegionScripting.Runtime;
using ClassicUO.LegionScripting.Runtime.Host;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript.Runtime.Host;

public class RuntimeTelemetrySinkTests
{
    [Fact]
    public void TelemetrySink_Should_Write_Beta_Report_With_Fault_Buckets()
    {
        var sink = new RuntimeTelemetrySink();
        sink.PublishMetric("runtime.tick.executed_steps", 5);
        sink.PublishMetric("runtime.tick.pending_actions", 2);

        string logs = Path.Combine(Path.GetTempPath(), "tazuo-runtime-tests", Guid.NewGuid().ToString("N"));

        string reportPath = sink.WriteBetaReport(logs,
        [
            new ScriptRuntimeFault { ScriptId = 1, Reason = "watchdog.wait-timeout", Tick = 10 },
            new ScriptRuntimeFault { ScriptId = 2, Reason = "watchdog.wait-timeout", Tick = 12 }
        ]);

        File.Exists(reportPath).Should().BeTrue();
        string contents = File.ReadAllText(reportPath);
        contents.Should().Contain("watchdog.wait-timeout");
        contents.Should().Contain("REVIEW_REQUIRED");
    }
}
