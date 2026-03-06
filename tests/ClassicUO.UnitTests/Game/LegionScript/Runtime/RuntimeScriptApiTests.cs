using System.Collections.Generic;
using ClassicUO.LegionScripting.Runtime;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript.Runtime;

public class RuntimeScriptApiTests
{
    [Fact]
    public void Wait_Primitive_Should_Return_WaitTicks_Directive()
    {
        ScriptDirective directive = RuntimeScriptApi.Wait(3);

        directive.Kind.Should().Be(ScriptDirectiveKind.WaitTicks);
        directive.WaitTicks.Should().Be(3);
    }

    [Fact]
    public void Core_Primitives_Should_Enqueue_Authoritative_Actions()
    {
        var runtime = new ScriptRuntimeManager();
        int phase = 0;

        runtime.StartScript("core-api", execution =>
        {
            switch (phase++)
            {
                case 0:
                    return RuntimeScriptApi.Heal(execution, "greater_heal");
                case 1:
                    return RuntimeScriptApi.DrinkPotion(execution, "0x40000001");
                case 2:
                    return RuntimeScriptApi.Target(execution, 0x40000002);
                default:
                    return ScriptDirective.Complete();
            }
        });

        runtime.Tick(maxStepsPerTick: 1);
        runtime.Tick(maxStepsPerTick: 1);
        runtime.Tick(maxStepsPerTick: 1);

        List<ScriptAction> actions = runtime.DrainActions();

        actions.Should().HaveCount(3);
        actions[0].ActionType.Should().Be(RuntimeScriptApi.ActionCastSpell);
        actions[1].ActionType.Should().Be(RuntimeScriptApi.ActionUsePotion);
        actions[2].ActionType.Should().Be(RuntimeScriptApi.ActionTargetSerial);

        actions[0].Payload.Should().BeOfType<RuntimeCastSpellAction>();
        actions[1].Payload.Should().BeOfType<RuntimeUsePotionAction>();
        actions[2].Payload.Should().BeOfType<RuntimeTargetAction>();
    }
}
