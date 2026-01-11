using System;
using ClassicUO.LegionScripting;
using IronPython.Hosting;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript;

public class ApiTests
{
    private API api;

    /// <summary>
    /// Unit tests for various LegionScript APIs
    ///
    /// Currently, in test mode, the <see cref="API"/> class is 'un-reachable' from the outside (uses a new World instance)
    /// so not everything is testable
    /// </summary>
    public ApiTests()
    {
        Client.UnitTestingActive = true;
        api = new API(Python.CreateEngine());
    }
    
    [Fact]
    public void CurrentAbilityNames_Returns_Empty_String_When_No_Player()
    {
        // Basically check this doesn't crash when player mobile is gone
        Assert.Equivalent(api.CurrentAbilityNames(), Array.Empty<string>());
    }

    [Fact]
    public void API_KnownAbilityNames_Returns_Expected_Strings()
    {
        // This can be replaced with a call to Enum.GetNames but that would somewhat defeat the point.
        // Notice that ordering here is by binary value (None = 0, Invalid = FF)
        string[] expected =
        [
            "None", "ArmorIgnore", "BleedAttack", "ConcussionBlow", "CrushingBlow", "Disarm",
            "Dismount", "DoubleStrike", "InfectiousStrike", "MortalStrike", "MovingShot",
            "ParalyzingBlow", "ShadowStrike", "WhirlwindAttack", "RidingSwipe", "FrenziedWhirlwind",
            "Block", "DefenseMastery", "NerveStrike", "TalonStrike", "Feint", "DualWield", "DoubleShot",
            "ArmorPierce", "Bladeweave", "ForceArrow", "LightningArrow", "PsychicAttack", "SerpentArrow",
            "ForceOfNature", "InfusedThrow", "MysticArc", "Invalid"
        ];
        
        Assert.Equal(api.KnownAbilityNames(), expected);
    }
}
