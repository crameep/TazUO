using System.Collections.Generic;
using ClassicUO.LegionScripting.Runtime;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript.Runtime;

public class ScriptWorldSnapshotTests
{
    [Fact]
    public void Snapshot_Should_Copy_Collections_Immutably()
    {
        var mobiles = new List<ScriptMobileSnapshot>
        {
            new(0x01020304, 0x0190, notoriety: 1, distance: 5, hits: 50, hitsMax: 100, new ScriptPosition(100, 100, 0))
        };

        var items = new List<ScriptItemSnapshot>
        {
            new(0x40000001, 0x0F0C, 0, amount: 3, distance: 2, new ScriptPosition(101, 100, 0))
        };

        ScriptWorldSnapshot snapshot = new(10, null, mobiles, items);

        mobiles.Clear();
        items.Clear();

        snapshot.Mobiles.Should().HaveCount(1);
        snapshot.Items.Should().HaveCount(1);
        snapshot.Mobiles[0].Serial.Should().Be(0x01020304);
        snapshot.Items[0].Serial.Should().Be(0x40000001);
    }

    [Fact]
    public void Runtime_Should_Expose_Latest_Snapshot_To_Script_Context()
    {
        var runtime = new ScriptRuntimeManager(tick => new ScriptWorldSnapshot(
            tick,
            new ScriptPlayerSnapshot(0x00000001, 40, 100, 30, 50, 20, 40, new ScriptPosition(1, 2, 3)),
            new[] { new ScriptMobileSnapshot(0x00000002, 0x0190, 3, 4, 20, 40, new ScriptPosition(2, 3, 0)) },
            new[] { new ScriptItemSnapshot(0x40000001, 0x0F0C, 0, 2, 1, new ScriptPosition(3, 4, 0)) }));

        ScriptWorldSnapshot seen = null;

        runtime.StartScript("snapshot-reader", execution =>
        {
            seen = execution.Snapshot;
            return ScriptDirective.Complete();
        });

        runtime.Tick();

        seen.Should().NotBeNull();
        seen.Tick.Should().Be(1);
        seen.Player.Should().NotBeNull();
        seen.Player.Hits.Should().Be(40);
        seen.Mobiles.Should().HaveCount(1);
        seen.Items.Should().HaveCount(1);
    }
}
