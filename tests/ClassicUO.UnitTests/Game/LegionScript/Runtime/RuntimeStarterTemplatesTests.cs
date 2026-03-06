using ClassicUO.LegionScripting.Runtime;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript.Runtime;

public class RuntimeStarterTemplatesTests
{
    [Fact]
    public void Healer_Template_Should_Enqueue_Heal_And_Target_When_Low_HP()
    {
        var runtime = new ScriptRuntimeManager(tick => new ScriptWorldSnapshot(
            tick,
            new ScriptPlayerSnapshot(0x00000001, hits: 20, hitsMax: 100, mana: 40, manaMax: 40, stamina: 30, staminaMax: 30, new ScriptPosition(10, 10, 0)),
            null,
            null));

        runtime.StartScript("starter:healer", RuntimeStarterTemplates.CreateHealerTemplate(healBelowPercent: 60, cooldownTicks: 1));

        runtime.Tick(maxStepsPerTick: 1);
        var actions = runtime.DrainActions();

        actions.Should().Contain(a => a.ActionType == RuntimeScriptApi.ActionCastSpell);
        actions.Should().Contain(a => a.ActionType == RuntimeScriptApi.ActionTargetSerial);
    }

    [Fact]
    public void Potion_Template_Should_Enqueue_UsePotion_When_Low_HP()
    {
        var runtime = new ScriptRuntimeManager(tick => new ScriptWorldSnapshot(
            tick,
            new ScriptPlayerSnapshot(0x00000001, hits: 15, hitsMax: 100, mana: 40, manaMax: 40, stamina: 30, staminaMax: 30, new ScriptPosition(10, 10, 0)),
            null,
            null));

        runtime.StartScript("starter:potion", RuntimeStarterTemplates.CreatePotionTemplate("0x40000001", drinkBelowPercent: 35, cooldownTicks: 1));

        runtime.Tick(maxStepsPerTick: 1);
        var actions = runtime.DrainActions();

        actions.Should().Contain(a => a.ActionType == RuntimeScriptApi.ActionUsePotion);
    }
}
