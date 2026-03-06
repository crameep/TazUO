using System;

namespace ClassicUO.LegionScripting.Runtime;

internal static class RuntimeStarterTemplates
{
    public static Func<ScriptExecutionContext, ScriptDirective> CreateHealerTemplate(int healBelowPercent = 60, int cooldownTicks = 8)
    {
        long nextAllowedTick = 0;

        return execution =>
        {
            ScriptPlayerSnapshot player = execution.Snapshot?.Player;
            if (player == null || player.HitsMax <= 0)
                return RuntimeScriptApi.Wait(2);

            int hpPercent = (int)Math.Floor((double)player.Hits * 100 / Math.Max(1, player.HitsMax));

            if (hpPercent <= healBelowPercent && execution.CurrentTick >= nextAllowedTick)
            {
                nextAllowedTick = execution.CurrentTick + Math.Max(1, cooldownTicks);
                RuntimeScriptApi.Heal(execution, "greater heal");
                return RuntimeScriptApi.Target(execution, player.Serial);
            }

            return RuntimeScriptApi.Wait(1);
        };
    }

    public static Func<ScriptExecutionContext, ScriptDirective> CreatePotionTemplate(string potionSerialHex, int drinkBelowPercent = 35, int cooldownTicks = 20)
    {
        long nextAllowedTick = 0;

        return execution =>
        {
            ScriptPlayerSnapshot player = execution.Snapshot?.Player;
            if (player == null || player.HitsMax <= 0)
                return RuntimeScriptApi.Wait(2);

            int hpPercent = (int)Math.Floor((double)player.Hits * 100 / Math.Max(1, player.HitsMax));

            if (hpPercent <= drinkBelowPercent && execution.CurrentTick >= nextAllowedTick)
            {
                nextAllowedTick = execution.CurrentTick + Math.Max(1, cooldownTicks);
                return RuntimeScriptApi.DrinkPotion(execution, potionSerialHex);
            }

            return RuntimeScriptApi.Wait(1);
        };
    }
}
