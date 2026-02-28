using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClassicUO.Game;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class OrganizerAgentTest
    {
        public OrganizerAgentTest()
        {
            OrganizerAgent agent = OrganizerAgent.Instance;
            agent.ResetRunTrackingForTests();
            agent.OrganizerConfigs.Clear();
        }

        [Fact]
        public void OrganizerConfig_Defaults_ShouldBeBackwardCompatible()
        {
            var config = new OrganizerConfig();
            var item = new OrganizerItemConfig();

            config.Group.Should().BeEmpty();
            config.Recursive.Should().BeFalse();
            config.DestinationType.Should().Be(DestType.ConfigDefault);
            config.TomeDefinitionName.Should().BeEmpty();

            item.Name.Should().BeEmpty();
            item.Graphic.Should().Be(0);
            item.Hue.Should().Be(ushort.MaxValue);
            item.RegexSearch.Should().BeEmpty();
            item.DestinationType.Should().Be(DestType.ConfigDefault);
            item.TomeDefinitionName.Should().BeEmpty();
        }

        [Fact]
        public void OrganizerConfig_DeserializeLegacyJson_ShouldPopulateNewDefaults()
        {
            const string legacyJson = """
            {
              "Name": "Legacy",
              "SourceContSerial": 1,
              "DestContSerial": 2,
              "Enabled": true,
              "ItemConfigs": [
                {
                  "Graphic": 3821,
                  "Hue": 65535,
                  "Amount": 0,
                  "Enabled": true,
                  "DestContSerial": 0
                }
              ]
            }
            """;

            OrganizerConfig config = JsonSerializer.Deserialize(legacyJson, OrganizerAgentContext.Default.OrganizerConfig);

            config.Should().NotBeNull();
            config.Group.Should().BeEmpty();
            config.Recursive.Should().BeFalse();
            config.DestinationType.Should().Be(DestType.ConfigDefault);
            config.TomeDefinitionName.Should().BeEmpty();

            config.ItemConfigs.Should().HaveCount(1);
            OrganizerItemConfig item = config.ItemConfigs[0];
            item.Name.Should().BeEmpty();
            item.RegexSearch.Should().BeEmpty();
            item.DestinationType.Should().Be(DestType.ConfigDefault);
            item.TomeDefinitionName.Should().BeEmpty();
        }

        [Fact]
        public void OrganizerConfig_RoundTrip_ShouldPreserveNewFields()
        {
            var original = new OrganizerConfig
            {
                Name = "RoundTrip",
                Group = "Mats",
                Recursive = true,
                DestinationType = DestType.Tome,
                TomeDefinitionName = "Reagent Tome",
                ItemConfigs = new List<OrganizerItemConfig>
                {
                    new()
                    {
                        Name = "Vanq",
                        Graphic = -1,
                        Hue = ushort.MaxValue,
                        RegexSearch = "vanq",
                        DestinationType = DestType.Container,
                        DestContSerial = 0x40001234,
                        TomeDefinitionName = ""
                    }
                }
            };

            string json = JsonSerializer.Serialize(original, OrganizerAgentContext.Default.OrganizerConfig);
            OrganizerConfig roundTrip = JsonSerializer.Deserialize(json, OrganizerAgentContext.Default.OrganizerConfig);

            roundTrip.Should().NotBeNull();
            roundTrip.Name.Should().Be("RoundTrip");
            roundTrip.Group.Should().Be("Mats");
            roundTrip.Recursive.Should().BeTrue();
            roundTrip.DestinationType.Should().Be(DestType.Tome);
            roundTrip.TomeDefinitionName.Should().Be("Reagent Tome");
            roundTrip.ItemConfigs.Should().HaveCount(1);
            roundTrip.ItemConfigs[0].Graphic.Should().Be(-1);
            roundTrip.ItemConfigs[0].RegexSearch.Should().Be("vanq");
            roundTrip.ItemConfigs[0].DestinationType.Should().Be(DestType.Container);
            roundTrip.ItemConfigs[0].DestContSerial.Should().Be(0x40001234);
        }

        [Fact]
        public void OrganizerItemConfig_IsMatch_ShouldSupportGraphicHueAndRegexWildcards()
        {
            var wildcardGraphic = new OrganizerItemConfig
            {
                Graphic = -1,
                Hue = ushort.MaxValue,
                RegexSearch = "vanq"
            };

            wildcardGraphic.IsMatch(0x1B73, 0x0455, "powerful vanq katana").Should().BeTrue();
            wildcardGraphic.IsMatch(0x1B73, 0x0455, "ordinary katana").Should().BeFalse();

            var exactGraphicAnyHue = new OrganizerItemConfig
            {
                Graphic = 0x0EED,
                Hue = ushort.MaxValue,
                RegexSearch = string.Empty
            };

            exactGraphicAnyHue.IsMatch(0x0EED, 0x0000).Should().BeTrue();
            exactGraphicAnyHue.IsMatch(0x0EED, 0x0044).Should().BeTrue();
            exactGraphicAnyHue.IsMatch(0x0EEF, 0x0044).Should().BeFalse();
        }

        [Fact]
        public void MigrateLegacyTomes_ShouldCreateDefinitionAssignDestinationAndBeIdempotent()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string savePath = Path.Combine(tempDir, "TomeDefinitions.json");

            try
            {
                TomeManager tomeManager = TomeManager.CreateForTests(savePath);
                var configs = new List<OrganizerConfig>
                {
                    new()
                    {
                        Name = "Legacy",
                        TomeSerial = 0x40000001,
                        TomeGumpId = 0x12345678,
                        TomeAddButtonId = 42
                    }
                };

                bool migrated = OrganizerAgent.MigrateLegacyTomes(configs, tomeManager);
                migrated.Should().BeTrue();
                tomeManager.TomeDefinitions.Should().HaveCount(1);

                OrganizerConfig migratedConfig = configs[0];
                TomeDefinition definition = tomeManager.TomeDefinitions[0];
                migratedConfig.DestinationType.Should().Be(DestType.Tome);
                migratedConfig.TomeDefinitionName.Should().Be(definition.Name);
                migratedConfig.TomeSerial.Should().Be(0u);
                migratedConfig.TomeGumpId.Should().Be(0u);
                migratedConfig.TomeAddButtonId.Should().Be(0);

                bool migratedSecondPass = OrganizerAgent.MigrateLegacyTomes(configs, tomeManager);
                migratedSecondPass.Should().BeFalse();
                tomeManager.TomeDefinitions.Should().HaveCount(1);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void TomeManager_SaveLoadRoundTrip_ShouldPersistDefinitions()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string savePath = Path.Combine(tempDir, "TomeDefinitions.json");

            try
            {
                TomeManager writeManager = TomeManager.CreateForTests(savePath);
                writeManager.TomeDefinitions.Add(new TomeDefinition
                {
                    Name = "Reagent Tome",
                    TomeSerial = 0x40000010,
                    GumpId = 0x10000001,
                    AddButtonId = 5,
                    Mode = TomeMode.TargetEach,
                    TargetSerial = 0,
                    Delay = 800,
                    RequiresWalk = true
                });

                writeManager.Save();

                TomeManager readManager = TomeManager.CreateForTests(savePath);
                readManager.LoadInternal();

                readManager.TomeDefinitions.Should().HaveCount(1);
                TomeDefinition definition = readManager.TomeDefinitions[0];
                definition.Name.Should().Be("Reagent Tome");
                definition.TomeSerial.Should().Be(0x40000010);
                definition.GumpId.Should().Be(0x10000001);
                definition.AddButtonId.Should().Be(5);
                definition.Mode.Should().Be(TomeMode.TargetEach);
                definition.Delay.Should().Be(800);
                definition.RequiresWalk.Should().BeTrue();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void EnumerateSourceItems_Recursive_ShouldSkipDestinationContainerBranch()
        {
            bool originalUnitTestMode = ClassicUO.Client.UnitTestingActive;
            ClassicUO.Client.UnitTestingActive = true;

            try
            {
                var world = new World();
                var source = Item.Create(world, 0x40000001);
                var top = Item.Create(world, 0x40000002);
                var nestedContainer = Item.Create(world, 0x40000003);
                var nestedItem = Item.Create(world, 0x40000004);
                var skippedContainer = Item.Create(world, 0x40000005);
                var skippedNested = Item.Create(world, 0x40000006);

                source.PushToBack(top);
                source.PushToBack(skippedContainer);
                top.PushToBack(nestedContainer);
                nestedContainer.PushToBack(nestedItem);
                skippedContainer.PushToBack(skippedNested);

                List<uint> serials = new(OrganizerAgent.EnumerateSourceItems(source, true, skippedContainer.Serial).Select(i => i.Serial));

                serials.Should().Contain(top.Serial);
                serials.Should().Contain(nestedContainer.Serial);
                serials.Should().Contain(nestedItem.Serial);
                serials.Should().Contain(skippedContainer.Serial);
                serials.Should().NotContain(skippedNested.Serial);
            }
            finally
            {
                ClassicUO.Client.UnitTestingActive = originalUnitTestMode;
            }
        }

        [Fact]
        public void ResolveDestination_ShouldSupportMixedContainerAndTomeDestinations()
        {
            var config = new OrganizerConfig
            {
                DestinationType = DestType.Tome,
                TomeDefinitionName = "Default Tome",
                DestContSerial = 0x40000111
            };

            var itemContainerOverride = new OrganizerItemConfig
            {
                DestinationType = DestType.Container,
                DestContSerial = 0x40000222
            };

            var itemTomeOverride = new OrganizerItemConfig
            {
                DestinationType = DestType.Tome,
                TomeDefinitionName = "Scroll Tome"
            };

            var itemLegacyDefault = new OrganizerItemConfig();

            ResolvedDestination containerResolved = OrganizerAgent.ResolveDestination(config, itemContainerOverride, 0x4000AAAA);
            ResolvedDestination tomeResolved = OrganizerAgent.ResolveDestination(config, itemTomeOverride, 0x4000AAAA);
            ResolvedDestination legacyDefaultResolved = OrganizerAgent.ResolveDestination(config, itemLegacyDefault, 0x4000AAAA);

            containerResolved.Type.Should().Be(DestType.Container);
            containerResolved.ContainerSerial.Should().Be(0x40000222);

            tomeResolved.Type.Should().Be(DestType.Tome);
            tomeResolved.TomeDefinitionName.Should().Be("Scroll Tome");

            legacyDefaultResolved.Type.Should().Be(DestType.Tome);
            legacyDefaultResolved.TomeDefinitionName.Should().Be("Default Tome");
        }

        [Fact]
        public void IsOutsideBackpackDestination_ShouldTreatGroundAndMobileDropsAsExternal()
        {
            bool originalUnitTestMode = ClassicUO.Client.UnitTestingActive;
            ClassicUO.Client.UnitTestingActive = true;

            try
            {
                var world = new World();
                var groundContainer = Item.Create(world, 0x40000010);

                OrganizerAgent.IsOutsideBackpackDestination(groundContainer, groundContainer.Serial).Should().BeTrue();

                var bagContainer = Item.Create(world, 0x40000011);
                bagContainer.Container = 0x40000099;
                OrganizerAgent.IsOutsideBackpackDestination(bagContainer, bagContainer.Serial).Should().BeFalse();

                OrganizerAgent.IsOutsideBackpackDestination(bagContainer, 0x00000042).Should().BeTrue();
            }
            finally
            {
                ClassicUO.Client.UnitTestingActive = originalUnitTestMode;
            }
        }

        [Fact]
        public void IsDestinationWithinMoveRange_ShouldAlwaysAllowInternalContainers()
        {
            bool originalUnitTestMode = ClassicUO.Client.UnitTestingActive;
            ClassicUO.Client.UnitTestingActive = true;

            try
            {
                var world = new World();
                var bagContainer = Item.Create(world, 0x40000022);
                bagContainer.Container = 0x400000AA;

                OrganizerAgent.IsDestinationWithinMoveRange(bagContainer, bagContainer.Serial).Should().BeTrue();
            }
            finally
            {
                ClassicUO.Client.UnitTestingActive = originalUnitTestMode;
            }
        }

        [Fact]
        public void TomeActionRunner_TargetEach_ShouldProcessAllItemsAndReportMovedCount()
        {
            var runtime = new FakeTomeRuntime();
            long ticks = 0;
            var runner = new TomeActionRunner(runtime, () => ticks);
            TomeOperationResult? result = null;

            runner.Enqueue(new TomeOperation
            {
                Definition = new TomeDefinition
                {
                    TomeSerial = 0x40001234,
                    GumpId = 0x11111111,
                    AddButtonId = 12,
                    Mode = TomeMode.TargetEach,
                    Delay = 50
                },
                ItemSerials = new List<uint> { 1, 2 },
                OnFinished = r => result = r
            });

            runner.Update();
            runtime.IsGumpActionPending = false;
            runner.Update();
            runtime.IsAutoTargetPending = false;
            runner.Update();
            ticks += 50;
            runner.Update();
            runtime.IsGumpActionPending = false;
            runner.Update();
            runtime.IsAutoTargetPending = false;
            runner.Update();

            result.Should().NotBeNull();
            result.Value.Success.Should().BeTrue();
            result.Value.Moved.Should().Be(2);
            result.Value.Skipped.Should().Be(0);
            runtime.UseTomeCalls.Should().Be(2);
            runtime.CloseGumpCalls.Should().Be(1);
            runtime.LastClosedGumpId.Should().Be(0x11111111);
            runtime.LastClosedTomeSerial.Should().Be(0x40001234);
        }

        [Fact]
        public void TomeActionRunner_TargetRepeat_ShouldArmRepeatedTargetsAndCancelAtEnd()
        {
            var runtime = new FakeTomeRuntime();
            long ticks = 0;
            var runner = new TomeActionRunner(runtime, () => ticks);
            TomeOperationResult? result = null;

            runner.Enqueue(new TomeOperation
            {
                Definition = new TomeDefinition
                {
                    TomeSerial = 0x40001234,
                    GumpId = 0x11111111,
                    AddButtonId = 12,
                    Mode = TomeMode.TargetRepeat,
                    Delay = 10
                },
                ItemSerials = new List<uint> { 10, 11, 12 },
                OnFinished = r => result = r
            });

            runner.Update();
            runtime.IsGumpActionPending = false;
            runner.Update();

            runtime.IsAutoTargetPending = false;
            runner.Update();
            ticks += 10;
            runner.Update();

            runtime.IsAutoTargetPending = false;
            runner.Update();
            ticks += 10;
            runner.Update();

            runtime.IsAutoTargetPending = false;
            runner.Update();

            result.Should().NotBeNull();
            result.Value.Success.Should().BeTrue();
            result.Value.Moved.Should().Be(3);
            runtime.ArmedTargets.Should().ContainInOrder(10u, 11u, 12u);
            runtime.CancelCalls.Should().Be(1);
            runtime.CloseGumpCalls.Should().Be(1);
        }

        [Fact]
        public void TomeActionRunner_FillAll_Timeout_ShouldSkipOperation()
        {
            var runtime = new FakeTomeRuntime();
            long ticks = 0;
            var runner = new TomeActionRunner(runtime, () => ticks);
            TomeOperationResult? result = null;

            runner.Enqueue(new TomeOperation
            {
                Definition = new TomeDefinition
                {
                    TomeSerial = 0x40001234,
                    GumpId = 0x11111111,
                    AddButtonId = 12,
                    Mode = TomeMode.FillAll,
                    Delay = 0
                },
                ItemSerials = new List<uint> { 101, 102, 103 },
                OnFinished = r => result = r
            });

            runner.Update();
            ticks += 5001;
            runner.Update();

            result.Should().NotBeNull();
            result.Value.Success.Should().BeFalse();
            result.Value.Moved.Should().Be(0);
            result.Value.Skipped.Should().Be(3);
            runtime.CloseGumpCalls.Should().Be(1);
        }

        [Fact]
        public void TomeActionRunner_TargetContainer_WithWalk_ShouldWalkThenComplete()
        {
            var runtime = new FakeTomeRuntime
            {
                InRange = false,
                PlayerSerial = 0x00000077
            };
            long ticks = 0;
            var runner = new TomeActionRunner(runtime, () => ticks);
            TomeOperationResult? result = null;

            runner.Enqueue(new TomeOperation
            {
                Definition = new TomeDefinition
                {
                    TomeSerial = 0x40001234,
                    GumpId = 0x11111111,
                    AddButtonId = 12,
                    Mode = TomeMode.TargetContainer,
                    TargetSerial = 0,
                    RequiresWalk = true
                },
                ItemSerials = new List<uint> { 201, 202 },
                OnFinished = r => result = r
            });

            runner.Update();
            runtime.WalkRequests.Should().Be(1);

            runtime.InRange = true;
            runner.Update();
            runtime.IsGumpActionPending = false;
            runner.Update();
            runtime.IsAutoTargetPending = false;
            runner.Update();

            result.Should().NotBeNull();
            result.Value.Success.Should().BeTrue();
            result.Value.Moved.Should().Be(2);
            runtime.ArmedTargets.Should().ContainSingle().Which.Should().Be(0x00000077);
            runtime.CloseGumpCalls.Should().Be(1);
        }

        [Fact]
        public void OrganizerRunState_Defaults_ShouldStartEmpty()
        {
            var runState = new OrganizerRunState();

            runState.ConfigName.Should().BeEmpty();
            runState.TotalItems.Should().Be(0);
            runState.ItemsMoved.Should().Be(0);
            runState.ItemsSkipped.Should().Be(0);
            runState.IsRunning.Should().BeFalse();
        }

        [Fact]
        public void NormalizeGroup_ShouldMapBlankToGeneral()
        {
            OrganizerAgent.NormalizeGroup(null).Should().Be("General");
            OrganizerAgent.NormalizeGroup("").Should().Be("General");
            OrganizerAgent.NormalizeGroup("  ").Should().Be("General");
            OrganizerAgent.NormalizeGroup(" Reagents ").Should().Be("Reagents");
        }

        [Fact]
        public void GetEnabledConfigsByGroup_ShouldFilterByGroupAndEnabled()
        {
            OrganizerAgent agent = OrganizerAgent.Instance;
            agent.OrganizerConfigs.Clear();

            agent.OrganizerConfigs.Add(new OrganizerConfig { Name = "A", Group = "Reagents", Enabled = true });
            agent.OrganizerConfigs.Add(new OrganizerConfig { Name = "B", Group = "Reagents", Enabled = false });
            agent.OrganizerConfigs.Add(new OrganizerConfig { Name = "C", Group = "Scrolls", Enabled = true });

            List<string> names = agent.GetEnabledConfigsByGroup("Reagents").Select(c => c.Name).ToList();
            names.Should().BeEquivalentTo(new[] { "A" });
        }

        [Fact]
        public void TryParseGroupCommand_ShouldParseValidFormAndRejectInvalid()
        {
            OrganizerAgent.TryParseGroupCommand(new[] { "organize", "group", "Reagents" }, out string groupName).Should().BeTrue();
            groupName.Should().Be("Reagents");

            OrganizerAgent.TryParseGroupCommand(new[] { "organize", "group", "High", "Value" }, out string groupName2).Should().BeTrue();
            groupName2.Should().Be("High Value");

            OrganizerAgent.TryParseGroupCommand(new[] { "organize", "group" }, out _).Should().BeFalse();
            OrganizerAgent.TryParseGroupCommand(new[] { "organize", "notgroup", "Reagents" }, out _).Should().BeFalse();
            OrganizerAgent.TryParseGroupCommand(Array.Empty<string>(), out _).Should().BeFalse();
        }

        [Fact]
        public void BuildOrganizerListLines_ShouldIncludeGroupedHeadersAndIndexes()
        {
            OrganizerAgent agent = OrganizerAgent.Instance;
            agent.OrganizerConfigs.Clear();

            agent.OrganizerConfigs.Add(new OrganizerConfig { Name = "Bandages", Group = "Healing", Enabled = true });
            agent.OrganizerConfigs.Add(new OrganizerConfig { Name = "Regs", Group = "Reagents", Enabled = false });
            agent.OrganizerConfigs.Add(new OrganizerConfig { Name = "Misc", Group = "", Enabled = true });

            List<string> lines = agent.BuildOrganizerListLines();

            lines.Should().Contain(line => line.StartsWith("Available organizers (3):"));
            lines.Should().Contain(" Group: General");
            lines.Should().Contain(" Group: Healing");
            lines.Should().Contain(" Group: Reagents");
            lines.Should().Contain(line => line.Contains("'Bandages'"));
            lines.Should().Contain(line => line.Contains("'Regs'"));
            lines.Should().Contain(line => line.Contains("'Misc'"));
        }

        [Fact]
        public void RunGuard_ShouldRejectOverlappingAttempts_AndUnlockWhenIdle()
        {
            OrganizerAgent agent = OrganizerAgent.Instance;
            agent.ResetRunTrackingForTests();

            agent.TryEnterRunGuardForTests().Should().BeTrue();
            agent.IsRunInProgressForTests().Should().BeTrue();

            agent.TryEnterRunGuardForTests().Should().BeFalse();

            agent.ReleaseRunGuardIfIdleForTests();
            agent.IsRunInProgressForTests().Should().BeFalse();

            agent.TryEnterRunGuardForTests().Should().BeTrue();
            agent.ReleaseRunGuardIfIdleForTests();
        }

        [Fact]
        public void RunState_AggregationAcrossMultipleConfigs_ShouldCompleteAndUnlock()
        {
            OrganizerAgent agent = OrganizerAgent.Instance;
            agent.ResetRunTrackingForTests();

            agent.TryEnterRunGuardForTests().Should().BeTrue();
            // Aggregation is only entered when a second run starts while there are pending actions.
            agent.ForceSetPendingRunActionsForTests(3);
            agent.ForceStartRunStateForTests("ConfigA", 3, 1);

            agent.ForceStartRunStateForTests("ConfigB", 2, 0);

            agent.RunState.IsRunning.Should().BeTrue();
            agent.RunState.ConfigName.Should().Be("Multiple Organizers");
            agent.RunState.TotalItems.Should().Be(5);
            agent.RunState.ItemsSkipped.Should().Be(1);

            agent.ForceSetPendingRunActionsForTests(0);
            agent.ForceTryCompleteRunStateForTests();

            agent.RunState.IsRunning.Should().BeFalse();
            agent.IsRunInProgressForTests().Should().BeFalse();
        }

        [Fact]
        public void PublicEntryPoints_ShouldRejectWhileActiveRunIsSet()
        {
            OrganizerAgent agent = OrganizerAgent.Instance;
            agent.ResetRunTrackingForTests();
            agent.RunState.IsRunning = true;

            int groupResult = agent.RunOrganizerGroup("Anything");
            groupResult.Should().Be(0);

            // Should not throw when blocked by active run state
            agent.RunOrganizer("Missing Name");
            agent.RunOrganizer(0);
            agent.OrganizerCommand(new[] { "organize", "group", "Anything" });

            agent.RunState.IsRunning = false;
            agent.ReleaseRunGuardIfIdleForTests();
        }

        [Fact]
        public void OrganizerCommand_GroupWithoutName_ShouldNotAttemptRunAndShouldNotLock()
        {
            OrganizerAgent agent = OrganizerAgent.Instance;
            agent.ResetRunTrackingForTests();

            agent.OrganizerCommand(new[] { "organize", "group" });

            agent.IsRunInProgressForTests().Should().BeFalse();
        }
    }

    internal sealed class FakeTomeRuntime : ITomeRuntime
    {
        public bool IsGumpActionPending { get; set; } = true;
        public bool IsAutoTargetPending { get; set; } = true;
        public bool InRange { get; set; } = true;
        public uint PlayerSerial { get; set; } = 0x1234;
        public int UseTomeCalls { get; private set; }
        public int WalkRequests { get; private set; }
        public int CancelCalls { get; private set; }
        public int CloseGumpCalls { get; private set; }
        public uint LastClosedGumpId { get; private set; }
        public uint LastClosedTomeSerial { get; private set; }
        public List<uint> ArmedTargets { get; } = new();

        public void ArmNextGump(uint gumpId, int buttonId)
        {
            IsGumpActionPending = true;
        }

        public void ArmAutoTarget(uint targetSerial)
        {
            ArmedTargets.Add(targetSerial);
            IsAutoTargetPending = true;
        }

        public void UseTome(uint tomeSerial)
        {
            UseTomeCalls++;
        }

        public bool RequestWalkToTome(uint tomeSerial)
        {
            WalkRequests++;
            return true;
        }

        public bool IsWithinTomeRange(uint tomeSerial)
        {
            return InRange;
        }

        public uint GetPlayerSerial()
        {
            return PlayerSerial;
        }

        public void CancelTargeting()
        {
            CancelCalls++;
            IsAutoTargetPending = false;
        }

        public void ResetPendingAutomation()
        {
            IsAutoTargetPending = false;
            IsGumpActionPending = false;
        }

        public void CloseTomeGump(uint gumpId, uint tomeSerial)
        {
            CloseGumpCalls++;
            LastClosedGumpId = gumpId;
            LastClosedTomeSerial = tomeSerial;
        }
    }
}
