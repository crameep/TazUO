using System.Collections.Generic;
using System.Text.Json;
using ClassicUO.Configuration;
using Xunit;

namespace ClassicUO.UnitTests.Configuration;

public class ProfileSerializationTest
{
    [Fact]
    public void CustomDesktopSettings_RoundTripThroughProfileJson()
    {
        Profile original = new()
        {
            ColorblindMode = 3,
            SkillsSortColumn = 4,
            SkillsSortAscending = false,
            SkillsShowGroups = true,
            EnableFastDropModifier = true,
            PinnedItemButtonDefaultSize = 96,
            Grid_ShowCapacityBar = false,
            Grid_MaxContainerItems = 175,
            GridContainerTabsEnabled = false,
            GridContainerTabAutoOpen = 2,
            CombatMeterEnabled = false,
            CombatHudVisible = false,
            CombatHudAutoShow = false,
            CombatHudAutoHideDelay = 12,
            CombatFightIdleThreshold = 18,
            CombatMaxEvents = 12345,
            CombatExportPath = "CustomCombatLogs"
        };

        string json = JsonSerializer.Serialize(original, ProfileJsonContext.DefaultToUse.Profile);

        // Deserializing the entire runtime profile invokes unrelated UI-style setters that require
        // initialized graphics assets. Isolate the settings under test while preserving the exact
        // names and values emitted by the production serializer.
        string[] propertyNames =
        [
            nameof(Profile.ColorblindMode),
            nameof(Profile.SkillsSortColumn),
            nameof(Profile.SkillsSortAscending),
            nameof(Profile.SkillsShowGroups),
            nameof(Profile.EnableFastDropModifier),
            nameof(Profile.PinnedItemButtonDefaultSize),
            nameof(Profile.Grid_ShowCapacityBar),
            nameof(Profile.Grid_MaxContainerItems),
            nameof(Profile.GridContainerTabsEnabled),
            nameof(Profile.GridContainerTabAutoOpen),
            nameof(Profile.CombatMeterEnabled),
            nameof(Profile.CombatHudVisible),
            nameof(Profile.CombatHudAutoShow),
            nameof(Profile.CombatHudAutoHideDelay),
            nameof(Profile.CombatFightIdleThreshold),
            nameof(Profile.CombatMaxEvents),
            nameof(Profile.CombatExportPath)
        ];

        using JsonDocument document = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> customSettings = new();
        foreach (string propertyName in propertyNames)
        {
            string jsonName = ProfileJsonContext.DefaultToUse.Options.PropertyNamingPolicy.ConvertName(propertyName);
            Assert.True(document.RootElement.TryGetProperty(jsonName, out JsonElement value));
            customSettings.Add(jsonName, value.Clone());
        }

        string customSettingsJson = JsonSerializer.Serialize(customSettings);
        Profile restored = JsonSerializer.Deserialize(customSettingsJson, ProfileJsonContext.DefaultToUse.Profile);

        Assert.NotNull(restored);
        Assert.Equal(original.ColorblindMode, restored.ColorblindMode);
        Assert.Equal(original.SkillsSortColumn, restored.SkillsSortColumn);
        Assert.Equal(original.SkillsSortAscending, restored.SkillsSortAscending);
        Assert.Equal(original.SkillsShowGroups, restored.SkillsShowGroups);
        Assert.Equal(original.EnableFastDropModifier, restored.EnableFastDropModifier);
        Assert.Equal(original.PinnedItemButtonDefaultSize, restored.PinnedItemButtonDefaultSize);
        Assert.Equal(original.Grid_ShowCapacityBar, restored.Grid_ShowCapacityBar);
        Assert.Equal(original.Grid_MaxContainerItems, restored.Grid_MaxContainerItems);
        Assert.Equal(original.GridContainerTabsEnabled, restored.GridContainerTabsEnabled);
        Assert.Equal(original.GridContainerTabAutoOpen, restored.GridContainerTabAutoOpen);
        Assert.Equal(original.CombatMeterEnabled, restored.CombatMeterEnabled);
        Assert.Equal(original.CombatHudVisible, restored.CombatHudVisible);
        Assert.Equal(original.CombatHudAutoShow, restored.CombatHudAutoShow);
        Assert.Equal(original.CombatHudAutoHideDelay, restored.CombatHudAutoHideDelay);
        Assert.Equal(original.CombatFightIdleThreshold, restored.CombatFightIdleThreshold);
        Assert.Equal(original.CombatMaxEvents, restored.CombatMaxEvents);
        Assert.Equal(original.CombatExportPath, restored.CombatExportPath);
    }
}
