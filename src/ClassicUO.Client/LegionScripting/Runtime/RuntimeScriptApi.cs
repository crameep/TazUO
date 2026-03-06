using System;

namespace ClassicUO.LegionScripting.Runtime;

internal static class RuntimeScriptApi
{
    public const string ActionCastSpell = "spell.cast";
    public const string ActionUsePotion = "item.use_potion";
    public const string ActionTargetSerial = "target.serial";

    public static ScriptDirective Wait(int ticks)
    {
        return ScriptDirective.WaitTicksFor(Math.Max(0, ticks));
    }

    public static ScriptDirective WaitForEvent(string eventType, int? timeoutTicks = null)
    {
        return ScriptDirective.WaitForEvent(eventType, timeoutTicks);
    }

    public static ScriptDirective Heal(ScriptExecutionContext execution, string spellName = "greater_heal")
    {
        execution.EnqueueAction(ActionCastSpell, new RuntimeCastSpellAction(spellName));
        return ScriptDirective.Yield();
    }

    public static ScriptDirective DrinkPotion(ScriptExecutionContext execution, string potionName = "greater_heal")
    {
        execution.EnqueueAction(ActionUsePotion, new RuntimeUsePotionAction(potionName));
        return ScriptDirective.Yield();
    }

    public static ScriptDirective Target(ScriptExecutionContext execution, uint serial)
    {
        execution.EnqueueAction(ActionTargetSerial, new RuntimeTargetAction(serial));
        return ScriptDirective.Yield();
    }
}

internal sealed class RuntimeCastSpellAction
{
    public RuntimeCastSpellAction(string spellName)
    {
        SpellName = spellName ?? string.Empty;
    }

    public string SpellName { get; }
}

internal sealed class RuntimeUsePotionAction
{
    public RuntimeUsePotionAction(string potionName)
    {
        PotionName = potionName ?? string.Empty;
    }

    public string PotionName { get; }
}

internal sealed class RuntimeTargetAction
{
    public RuntimeTargetAction(uint serial)
    {
        Serial = serial;
    }

    public uint Serial { get; }
}
