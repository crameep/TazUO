using System;
using System.Threading;
using ClassicUO.Game.Managers;
using ClassicUO.LegionScripting;
using IronPython.Hosting;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript;

[Collection(MainThreadCollection.Name)]
public class ApiTests
{
    private LegionAPI api;

    /// <summary>
    /// Unit tests for various LegionScript APIs
    ///
    /// Currently, in test mode, the <see cref="LegionAPI"/> class is 'un-reachable' from the outside (uses a new World instance)
    /// so not everything is testable
    /// </summary>
    public ApiTests()
    {
        Client.UnitTestingActive = true;
        api = new LegionAPI(new PythonCallbackChannel(Python.CreateEngine()), null);
    }
    
    [Fact]
    public void CurrentAbilityNames_Returns_Empty_String_When_No_Player()
    {
        // Basically check this doesn't crash when the player mobile is gone
        Assert.Empty(api.CurrentAbilityNames());
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
        
        Assert.Equal(expected, api.KnownAbilityNames());
    }

    [Fact]
    public void OrganizerGroup_WithEmptyName_DoesNotThrow()
    {
        api.OrganizerGroup("");
    }

    [Fact]
    public void OrganizerGroup_WithWhitespaceName_DoesNotThrow()
    {
        api.OrganizerGroup("   ");
    }

    [Fact]
    public void OrganizerGroup_WithValidName_DoesNotThrow()
    {
        api.OrganizerGroup("Reagents");
    }

    [Fact]
    public void PreTarget_WithoutType_MatchesAnyServerTargetType()
    {
        api.PreTarget(0x0000_1234);

        Assert.True(SpinWait.SpinUntil(
            () => TargetManager.NextAutoTarget.TargetSerial == 0x0000_1234u,
            TimeSpan.FromSeconds(1)
        ));
        Assert.Equal(0x0000_1234u, TargetManager.NextAutoTarget.TargetSerial);
        Assert.True(TargetManager.NextAutoTarget.MatchAnyType);

        api.CancelPreTarget();
        Assert.True(SpinWait.SpinUntil(
            () => !TargetManager.NextAutoTarget.IsSet,
            TimeSpan.FromSeconds(1)
        ));
    }

    [Fact]
    public void UseObjectOnTarget_RejectsInvalidSerials()
    {
        Assert.False(api.UseObjectOnTarget(0, 0x0000_1234));
        Assert.False(api.UseObjectOnTarget(0x4000_1234, 0));
    }
}
