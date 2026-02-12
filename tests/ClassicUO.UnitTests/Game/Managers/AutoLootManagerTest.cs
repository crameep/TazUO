using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using FluentAssertions;
using Microsoft.Xna.Framework;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class AutoLootManagerTest
    {
        #region AutoLootConfigEntry - Default Values Tests

        [Fact]
        public void AutoLootConfigEntry_DefaultValues_ShouldBeCorrect()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry();

            // Assert
            entry.Name.Should().BeEmpty();
            entry.Graphic.Should().Be(0);
            entry.Hue.Should().Be(ushort.MaxValue);
            entry.RegexSearch.Should().BeEmpty();
            entry.DestinationContainer.Should().Be(0u);
            entry.Uid.Should().NotBeEmpty();
        }

        [Fact]
        public void AutoLootConfigEntry_DefaultPriority_ShouldBeNormal()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry();

            // Assert
            entry.Priority.Should().Be(AutoLootManager.AutoLootPriority.Normal);
        }

        [Fact]
        public void AutoLootConfigEntry_Uid_ShouldBeUnique()
        {
            // Arrange & Act
            var entry1 = new AutoLootManager.AutoLootConfigEntry();
            var entry2 = new AutoLootManager.AutoLootConfigEntry();

            // Assert
            entry1.Uid.Should().NotBe(entry2.Uid);
        }

        [Fact]
        public void AutoLootConfigEntry_Uid_ShouldBeValidGuid()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry();

            // Assert
            Guid.TryParse(entry.Uid, out _).Should().BeTrue();
        }

        #endregion

        #region AutoLootConfigEntry - Property Setting Tests

        [Fact]
        public void AutoLootConfigEntry_SetProperties_ShouldPersist()
        {
            // Arrange
            var entry = new AutoLootManager.AutoLootConfigEntry();
            string testUid = Guid.NewGuid().ToString();

            // Act
            entry.Name = "Gold Coin";
            entry.Graphic = 3821;
            entry.Hue = 1153;
            entry.RegexSearch = ".*gold.*";
            entry.DestinationContainer = 12345u;
            entry.Uid = testUid;

            // Assert
            entry.Name.Should().Be("Gold Coin");
            entry.Graphic.Should().Be(3821);
            entry.Hue.Should().Be(1153);
            entry.RegexSearch.Should().Be(".*gold.*");
            entry.DestinationContainer.Should().Be(12345u);
            entry.Uid.Should().Be(testUid);
        }

        [Fact]
        public void AutoLootConfigEntry_SetNegativeGraphic_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = -1
            };

            // Assert
            entry.Graphic.Should().Be(-1);
        }

        [Fact]
        public void AutoLootConfigEntry_WithMaxUIntDestinationContainer_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                DestinationContainer = uint.MaxValue
            };

            // Assert
            entry.DestinationContainer.Should().Be(uint.MaxValue);
        }

        #endregion

        #region AutoLootConfigEntry - Equals Tests

        [Fact]
        public void AutoLootConfigEntry_Equals_WithSameProperties_ShouldReturnTrue()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = ".*gold.*"
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = ".*gold.*"
            };

            // Act
            bool result = entry1.Equals(entry2);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void AutoLootConfigEntry_Equals_WithDifferentGraphic_ShouldReturnFalse()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = ""
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3822,
                Hue = 1153,
                RegexSearch = ""
            };

            // Act
            bool result = entry1.Equals(entry2);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void AutoLootConfigEntry_Equals_WithDifferentHue_ShouldReturnFalse()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = ""
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1154,
                RegexSearch = ""
            };

            // Act
            bool result = entry1.Equals(entry2);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void AutoLootConfigEntry_Equals_WithDifferentRegexSearch_ShouldReturnFalse()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = ".*gold.*"
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = ".*silver.*"
            };

            // Act
            bool result = entry1.Equals(entry2);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void AutoLootConfigEntry_Equals_DifferentPriority_ShouldStillBeEqual()
        {
            // Arrange — Priority is not part of match identity, so entries with
            // the same Graphic/Hue/Regex but different priorities are equal
            // (prevents duplicate imports that only differ in priority).
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = "",
                Priority = AutoLootManager.AutoLootPriority.Normal
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = "",
                Priority = AutoLootManager.AutoLootPriority.High
            };

            // Act
            bool result = entry1.Equals(entry2);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void AutoLootConfigEntry_Equals_IgnoresNameAndDestination()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = "",
                Name = "Entry 1",
                DestinationContainer = 100u
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 3821,
                Hue = 1153,
                RegexSearch = "",
                Name = "Entry 2",
                DestinationContainer = 200u
            };

            // Act
            bool result = entry1.Equals(entry2);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void AutoLootConfigEntry_Equals_WithEmptyRegexSearch_ShouldReturnTrue()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 0,
                RegexSearch = ""
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 0,
                RegexSearch = string.Empty
            };

            // Act
            bool result = entry1.Equals(entry2);

            // Assert
            result.Should().BeTrue();
        }

        #endregion

        #region AutoLootConfigEntry - String Handling Tests

        [Fact]
        public void AutoLootConfigEntry_WithSpecialCharactersInName_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Name = "Item with \"quotes\" and 'apostrophes'"
            };

            // Assert
            entry.Name.Should().Be("Item with \"quotes\" and 'apostrophes'");
        }

        [Fact]
        public void AutoLootConfigEntry_WithUnicodeCharactersInName_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Name = "黄金 💰 Gold"
            };

            // Assert
            entry.Name.Should().Be("黄金 💰 Gold");
        }

        [Fact]
        public void AutoLootConfigEntry_WithLongName_ShouldWork()
        {
            // Arrange
            string longName = new('A', 1000);

            // Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Name = longName
            };

            // Assert
            entry.Name.Should().Be(longName);
            entry.Name.Length.Should().Be(1000);
        }

        [Fact]
        public void AutoLootConfigEntry_WithComplexRegexPattern_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                RegexSearch = @"^(?=.*\bvanq\b)(?=.*\bkatana\b).*$"
            };

            // Assert
            entry.RegexSearch.Should().Be(@"^(?=.*\bvanq\b)(?=.*\bkatana\b).*$");
        }

        #endregion

        #region AutoLootConfigEntry - Edge Cases

        [Fact]
        public void AutoLootConfigEntry_WithZeroGraphic_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0
            };

            // Assert
            entry.Graphic.Should().Be(0);
        }

        [Fact]
        public void AutoLootConfigEntry_WithZeroHue_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Hue = 0
            };

            // Assert
            entry.Hue.Should().Be(0);
        }

        [Fact]
        public void AutoLootConfigEntry_WithMaxHue_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Hue = ushort.MaxValue
            };

            // Assert
            entry.Hue.Should().Be(ushort.MaxValue);
        }

        [Fact]
        public void AutoLootConfigEntry_WithNullName_ShouldNotThrow()
        {
            // Arrange
            var entry = new AutoLootManager.AutoLootConfigEntry();

            // Act
            Action act = () => entry.Name = null;

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void AutoLootConfigEntry_WithNullRegexSearch_ShouldNotThrow()
        {
            // Arrange
            var entry = new AutoLootManager.AutoLootConfigEntry();

            // Act
            Action act = () => entry.RegexSearch = null;

            // Assert
            act.Should().NotThrow();
        }

        #endregion

        #region JSON Serialization Tests

        [Fact]
        public void AutoLootConfigEntry_Serialization_ShouldPreserveAllProperties()
        {
            // Arrange
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Name = "Test Item",
                Graphic = 1234,
                Hue = 5678,
                RegexSearch = ".*test.*",
                DestinationContainer = 99999u,
                Uid = "test-uid-123"
            };

            // Act
            string json = JsonSerializer.Serialize(entry, AutoLootJsonContext.Default.AutoLootConfigEntry);
            AutoLootManager.AutoLootConfigEntry deserialized = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.AutoLootConfigEntry);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Name.Should().Be(entry.Name);
            deserialized.Graphic.Should().Be(entry.Graphic);
            deserialized.Hue.Should().Be(entry.Hue);
            deserialized.RegexSearch.Should().Be(entry.RegexSearch);
            deserialized.DestinationContainer.Should().Be(entry.DestinationContainer);
            deserialized.Uid.Should().Be(entry.Uid);
        }

        [Fact]
        public void AutoLootConfigEntry_ListSerialization_ShouldWork()
        {
            // Arrange
            var list = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new()
                {
                    Name = "Item 1",
                    Graphic = 100,
                    Hue = 200
                },
                new()
                {
                    Name = "Item 2",
                    Graphic = 300,
                    Hue = 400
                }
            };

            // Act
            string json = JsonSerializer.Serialize(list, AutoLootJsonContext.Default.ListAutoLootConfigEntry);
            List<AutoLootManager.AutoLootConfigEntry> deserialized = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.ListAutoLootConfigEntry);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Should().HaveCount(2);
            deserialized[0].Name.Should().Be("Item 1");
            deserialized[0].Graphic.Should().Be(100);
            deserialized[1].Name.Should().Be("Item 2");
            deserialized[1].Graphic.Should().Be(300);
        }

        [Fact]
        public void AutoLootConfigEntry_Serialization_WithSpecialCharacters_ShouldWork()
        {
            // Arrange
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Name = "Item with \"quotes\" and \nnewlines\t and tabs",
                RegexSearch = @"^\s*test\s*$"
            };

            // Act
            string json = JsonSerializer.Serialize(entry, AutoLootJsonContext.Default.AutoLootConfigEntry);
            AutoLootManager.AutoLootConfigEntry deserialized = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.AutoLootConfigEntry);

            // Assert
            deserialized.Name.Should().Be(entry.Name);
            deserialized.RegexSearch.Should().Be(entry.RegexSearch);
        }

        [Fact]
        public void AutoLootConfigEntry_Serialization_WithDefaultValues_ShouldWork()
        {
            // Arrange
            var entry = new AutoLootManager.AutoLootConfigEntry();

            // Act
            string json = JsonSerializer.Serialize(entry, AutoLootJsonContext.Default.AutoLootConfigEntry);
            AutoLootManager.AutoLootConfigEntry deserialized = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.AutoLootConfigEntry);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Name.Should().BeEmpty();
            deserialized.Graphic.Should().Be(0);
            deserialized.Hue.Should().Be(ushort.MaxValue);
            deserialized.RegexSearch.Should().BeEmpty();
            deserialized.DestinationContainer.Should().Be(0);
        }

        [Fact]
        public void JsonRoundTrip_PriorityPreserved()
        {
            // Arrange
            var entries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Name = "Low Item", Graphic = 100, Priority = AutoLootManager.AutoLootPriority.Low },
                new() { Name = "Normal Item", Graphic = 200, Priority = AutoLootManager.AutoLootPriority.Normal },
                new() { Name = "High Item", Graphic = 300, Priority = AutoLootManager.AutoLootPriority.High }
            };

            // Act
            string json = JsonSerializer.Serialize(entries, AutoLootJsonContext.Default.ListAutoLootConfigEntry);
            List<AutoLootManager.AutoLootConfigEntry> deserialized = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.ListAutoLootConfigEntry);

            // Assert
            deserialized.Should().HaveCount(3);
            deserialized[0].Priority.Should().Be(AutoLootManager.AutoLootPriority.Low);
            deserialized[1].Priority.Should().Be(AutoLootManager.AutoLootPriority.Normal);
            deserialized[2].Priority.Should().Be(AutoLootManager.AutoLootPriority.High);
        }

        [Fact]
        public void JsonBackwardCompat_MissingPriority_DefaultsToNormal()
        {
            // Arrange — JSON from an older config file that doesn't have Priority field
            string legacyJson = """
            {
                "Name": "Old Item",
                "Graphic": 1234,
                "Hue": 0,
                "RegexSearch": "",
                "DestinationContainer": 0,
                "Uid": "legacy-uid"
            }
            """;

            // Act
            AutoLootManager.AutoLootConfigEntry deserialized = JsonSerializer.Deserialize(legacyJson, AutoLootJsonContext.Default.AutoLootConfigEntry);

            // Assert — missing Priority should default to Normal
            deserialized.Should().NotBeNull();
            deserialized.Name.Should().Be("Old Item");
            deserialized.Graphic.Should().Be(1234);
            deserialized.Priority.Should().Be(AutoLootManager.AutoLootPriority.Normal,
                "legacy config files without Priority field should default to Normal");
        }

        #endregion

        #region Collection Tests

        [Fact]
        public void AutoLootConfigEntry_InList_ShouldSupportMultipleEntries()
        {
            // Arrange
            var list = new List<AutoLootManager.AutoLootConfigEntry>();

            // Act
            for (int i = 0; i < 100; i++)
            {
                list.Add(new AutoLootManager.AutoLootConfigEntry
                {
                    Name = $"Item {i}",
                    Graphic = i,
                    Hue = (ushort)i
                });
            }

            // Assert
            list.Should().HaveCount(100);
            list[50].Name.Should().Be("Item 50");
            list[99].Graphic.Should().Be(99);
        }

        [Fact]
        public void AutoLootConfigEntry_FindInList_UsingEquals_ShouldWork()
        {
            // Arrange
            var target = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 500,
                Hue = 600,
                RegexSearch = ".*test.*"
            };
            var list = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 100, Hue = 200, RegexSearch = "" },
                new() { Graphic = 500, Hue = 600, RegexSearch = ".*test.*" },
                new() { Graphic = 700, Hue = 800, RegexSearch = "" }
            };

            // Act
            bool found = false;
            foreach (AutoLootManager.AutoLootConfigEntry entry in list)
            {
                if (entry.Equals(target))
                {
                    found = true;
                    break;
                }
            }

            // Assert
            found.Should().BeTrue();
        }

        [Fact]
        public void AutoLootConfigEntry_RemoveFromList_ShouldWork()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry { Name = "Item 1", Graphic = 100 };
            var entry2 = new AutoLootManager.AutoLootConfigEntry { Name = "Item 2", Graphic = 200 };
            var list = new List<AutoLootManager.AutoLootConfigEntry> { entry1, entry2 };

            // Act
            list.Remove(entry1);

            // Assert
            list.Should().HaveCount(1);
            list[0].Should().Be(entry2);
        }

        #endregion

        #region Boundary Value Tests

        [Fact]
        public void AutoLootConfigEntry_WithMaxInt32Graphic_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = int.MaxValue
            };

            // Assert
            entry.Graphic.Should().Be(int.MaxValue);
        }

        [Fact]
        public void AutoLootConfigEntry_WithMinInt32Graphic_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = int.MinValue
            };

            // Assert
            entry.Graphic.Should().Be(int.MinValue);
        }

        [Fact]
        public void AutoLootConfigEntry_WithZeroDestinationContainer_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                DestinationContainer = 0u
            };

            // Assert
            entry.DestinationContainer.Should().Be(0u);
        }

        #endregion

        #region Equals Consistency Tests

        [Fact]
        public void AutoLootConfigEntry_Equals_ShouldBeReflexive()
        {
            // Arrange
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 200,
                RegexSearch = "test"
            };

            // Act & Assert
            entry.Equals(entry).Should().BeTrue();
        }

        [Fact]
        public void AutoLootConfigEntry_Equals_ShouldBeSymmetric()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 200,
                RegexSearch = "test"
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 200,
                RegexSearch = "test"
            };

            // Act & Assert
            entry1.Equals(entry2).Should().Be(entry2.Equals(entry1));
        }

        [Fact]
        public void AutoLootConfigEntry_Equals_ShouldBeTransitive()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 200,
                RegexSearch = "test"
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 200,
                RegexSearch = "test"
            };
            var entry3 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 200,
                RegexSearch = "test"
            };

            // Act & Assert
            if (entry1.Equals(entry2) && entry2.Equals(entry3))
            {
                entry1.Equals(entry3).Should().BeTrue();
            }
        }

        [Fact]
        public void AutoLootConfigEntry_Equals_MultipleCallsShouldBeConsistent()
        {
            // Arrange
            var entry1 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 200,
                RegexSearch = "test"
            };
            var entry2 = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 100,
                Hue = 200,
                RegexSearch = "test"
            };

            // Act
            bool result1 = entry1.Equals(entry2);
            bool result2 = entry1.Equals(entry2);
            bool result3 = entry1.Equals(entry2);

            // Assert
            result1.Should().Be(result2);
            result2.Should().Be(result3);
        }

        #endregion

        #region Empty and Null String Tests

        [Fact]
        public void AutoLootConfigEntry_WithEmptyStrings_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Name = string.Empty,
                RegexSearch = string.Empty
            };

            // Assert
            entry.Name.Should().BeEmpty();
            entry.RegexSearch.Should().BeEmpty();
        }

        [Fact]
        public void AutoLootConfigEntry_WithWhitespaceStrings_ShouldWork()
        {
            // Arrange & Act
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Name = "   ",
                RegexSearch = "\t\n\r"
            };

            // Assert
            entry.Name.Should().Be("   ");
            entry.RegexSearch.Should().Be("\t\n\r");
        }

        #endregion

        #region Graphic Index Tests

        private static AutoLootManager CreateTestManager()
        {
            return new AutoLootManager(testMode: true);
        }

        [Fact]
        public void GraphicIndex_ShouldBucketEntriesByGraphic()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 100, Name = "Item A" },
                new() { Graphic = 200, Name = "Item B" },
                new() { Graphic = 100, Name = "Item C" }
            };

            // Act
            manager.RebuildGraphicIndex();

            // Assert
            manager._graphicIndex.Should().HaveCount(2);
            manager._graphicIndex[100].Should().HaveCount(2);
            manager._graphicIndex[200].Should().HaveCount(1);
            manager._graphicIndex[100][0].Name.Should().Be("Item A");
            manager._graphicIndex[100][1].Name.Should().Be("Item C");
            manager._graphicIndex[200][0].Name.Should().Be("Item B");
            manager._wildcardEntries.Should().BeEmpty();
        }

        [Fact]
        public void GraphicIndex_WildcardEntries_ShouldBeSeparated()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 100, Name = "Specific Item" },
                new() { Graphic = -1, Name = "Wildcard Item 1" },
                new() { Graphic = -1, Name = "Wildcard Item 2" }
            };

            // Act
            manager.RebuildGraphicIndex();

            // Assert
            manager._graphicIndex.Should().HaveCount(1);
            manager._graphicIndex[100].Should().HaveCount(1);
            manager._wildcardEntries.Should().HaveCount(2);
            manager._wildcardEntries[0].Name.Should().Be("Wildcard Item 1");
            manager._wildcardEntries[1].Name.Should().Be("Wildcard Item 2");
        }

        [Fact]
        public void GraphicIndex_GraphicZero_ShouldBeNormalKeyNotWildcard()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 0, Name = "Zero Graphic Item" }
            };

            // Act
            manager.RebuildGraphicIndex();

            // Assert
            manager._graphicIndex.Should().ContainKey(0);
            manager._graphicIndex[0].Should().HaveCount(1);
            manager._graphicIndex[0][0].Name.Should().Be("Zero Graphic Item");
            manager._wildcardEntries.Should().BeEmpty();
        }

        [Fact]
        public void GraphicIndex_AddingEntry_ShouldRebuildIndex()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 100, Name = "Original" }
            };
            manager.RebuildGraphicIndex();
            manager._graphicIndex.Should().HaveCount(1);

            // Act - simulate adding an entry and rebuilding
            manager._mergedEntries.Add(new AutoLootManager.AutoLootConfigEntry { Graphic = 200, Name = "Added" });
            manager.RebuildGraphicIndex();

            // Assert
            manager._graphicIndex.Should().HaveCount(2);
            manager._graphicIndex[100].Should().HaveCount(1);
            manager._graphicIndex[200].Should().HaveCount(1);
            manager._graphicIndex[200][0].Name.Should().Be("Added");
        }

        [Fact]
        public void GraphicIndex_RemovingEntry_ShouldRebuildIndex()
        {
            // Arrange
            var manager = CreateTestManager();
            var entryToRemove = new AutoLootManager.AutoLootConfigEntry { Graphic = 200, Name = "To Remove" };
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 100, Name = "Keep" },
                entryToRemove
            };
            manager.RebuildGraphicIndex();
            manager._graphicIndex.Should().HaveCount(2);

            // Act - simulate removing an entry and rebuilding
            manager._mergedEntries.Remove(entryToRemove);
            manager.RebuildGraphicIndex();

            // Assert
            manager._graphicIndex.Should().HaveCount(1);
            manager._graphicIndex.Should().ContainKey(100);
            manager._graphicIndex.Should().NotContainKey(200);
        }

        [Fact]
        public void GraphicIndex_ImportingEntries_ShouldRebuildIndex()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 100, Name = "Existing" }
            };
            manager.RebuildGraphicIndex();
            manager._graphicIndex.Should().HaveCount(1);

            // Act - simulate importing entries and rebuilding
            var imported = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 200, Name = "Imported 1" },
                new() { Graphic = 300, Name = "Imported 2" },
                new() { Graphic = -1, Name = "Imported Wildcard" }
            };
            manager._mergedEntries.AddRange(imported);
            manager.RebuildGraphicIndex();

            // Assert
            manager._graphicIndex.Should().HaveCount(3);
            manager._graphicIndex[100].Should().HaveCount(1);
            manager._graphicIndex[200].Should().HaveCount(1);
            manager._graphicIndex[300].Should().HaveCount(1);
            manager._wildcardEntries.Should().HaveCount(1);
            manager._wildcardEntries[0].Name.Should().Be("Imported Wildcard");
        }

        [Fact]
        public void GraphicIndex_EmptyList_ShouldProduceEmptyIndex()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>();

            // Act
            manager.RebuildGraphicIndex();

            // Assert
            manager._graphicIndex.Should().BeEmpty();
            manager._wildcardEntries.Should().BeEmpty();
        }

        [Fact]
        public void GraphicIndex_AllWildcards_ShouldProduceEmptyGraphicIndexFullWildcardList()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = -1, Name = "Wildcard 1", RegexSearch = ".*gold.*" },
                new() { Graphic = -1, Name = "Wildcard 2", RegexSearch = ".*silver.*" },
                new() { Graphic = -1, Name = "Wildcard 3", RegexSearch = ".*iron.*" }
            };

            // Act
            manager.RebuildGraphicIndex();

            // Assert
            manager._graphicIndex.Should().BeEmpty();
            manager._wildcardEntries.Should().HaveCount(3);
        }

        [Fact]
        public void GraphicIndex_Rebuild_ShouldClearPreviousState()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 100, Name = "Old Item" },
                new() { Graphic = -1, Name = "Old Wildcard" }
            };
            manager.RebuildGraphicIndex();
            manager._graphicIndex.Should().HaveCount(1);
            manager._wildcardEntries.Should().HaveCount(1);

            // Act - replace all items and rebuild
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 999, Name = "New Item" }
            };
            manager.RebuildGraphicIndex();

            // Assert - old entries should be gone
            manager._graphicIndex.Should().HaveCount(1);
            manager._graphicIndex.Should().NotContainKey(100);
            manager._graphicIndex.Should().ContainKey(999);
            manager._wildcardEntries.Should().BeEmpty();
        }

        [Fact]
        public void GraphicIndex_MultipleEntriesSameGraphic_ShouldAllBeInSameBucket()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 500, Hue = 0, Name = "Item A" },
                new() { Graphic = 500, Hue = 100, Name = "Item B" },
                new() { Graphic = 500, Hue = 200, Name = "Item C" }
            };

            // Act
            manager.RebuildGraphicIndex();

            // Assert
            manager._graphicIndex.Should().HaveCount(1);
            manager._graphicIndex[500].Should().HaveCount(3);
        }

        [Fact]
        public void GraphicIndex_NotifyEntryChanged_ShouldRebuildIndex()
        {
            // Arrange
            var manager = CreateTestManager();
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 100, Name = "Item 1" }
            };
            manager.RebuildGraphicIndex();

            // Act - add an entry and call NotifyEntryChanged
            manager._mergedEntries.Add(new AutoLootManager.AutoLootConfigEntry { Graphic = 200, Name = "Item 2" });
            manager.NotifyEntryChanged();

            // Assert - index should reflect the new entry
            manager._graphicIndex.Should().HaveCount(2);
            manager._graphicIndex.Should().ContainKey(200);
        }

        #endregion

        #region Spatial Tracking Tests

        /// <summary>
        /// Creates a World with a dummy Player set at (playerX, playerY) for distance calculations.
        /// Uses RuntimeHelpers.GetUninitializedObject to create a PlayerMobile without calling
        /// its constructor (which requires FileManager).
        /// </summary>
        private static World CreateWorldWithPlayer(int playerX = 100, int playerY = 100)
        {
            Client.UnitTestingActive = true;
            var world = new World();

            // Create a PlayerMobile without calling its constructor (avoids FileManager dependency)
            var player = (PlayerMobile)RuntimeHelpers.GetUninitializedObject(typeof(PlayerMobile));

            // Set the World property on the player via the backing field.
            // BaseGameObject.World is a get-only auto property: public World World { get; }
            // The compiler generates a backing field named <World>k__BackingField.
            var backingField = typeof(BaseGameObject).GetField("<World>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (backingField == null)
                throw new InvalidOperationException("Could not find BaseGameObject.World backing field for test setup");
            backingField.SetValue(player, world);

            // Set Player on World via reflection (private setter)
            var playerProperty = typeof(World).GetProperty("Player");
            playerProperty.SetValue(world, player);

            // Set the world's RangeSize to represent the player's position for distance calculations
            world.RangeSize = new Point(playerX, playerY);

            return world;
        }

        /// <summary>
        /// Creates a ground item at (x, y) in the given world. Items default to movable (not locked),
        /// non-corpse, on-ground.
        /// </summary>
        private static Item CreateGroundItem(World world, uint serial, ushort x, ushort y, ushort graphic = 0x0EEA)
        {
            var item = Item.Create(world, serial);
            item.X = x;
            item.Y = y;
            item.Graphic = graphic;
            item.Flags = Flags.Movable; // Movable = not locked
            // Container defaults to 0xFFFF_FFFF which means OnGround = true
            world.Items.Add(item);
            return item;
        }

        private static AutoLootManager CreateTestManagerWithWorld(World world)
        {
            var manager = new AutoLootManager(world);
            manager._loaded = true;
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>();
            manager.RebuildGraphicIndex();
            return manager;
        }

        /// <summary>
        /// Safe cleanup for test worlds. World.Clear() calls Destroy() on entities which
        /// triggers network code and accesses fields that don't exist in test context
        /// (uninitialized PlayerMobile, no Client.Game, etc). This just clears the collections.
        /// </summary>
        private static void CleanupTestWorld(World world)
        {
            world.Items.Clear();
            world.Mobiles.Clear();
            world.OPL.Clear();
            var playerProperty = typeof(World).GetProperty("Player");
            playerProperty.SetValue(world, null);
        }

        [Fact]
        public void IsTrackableGroundItem_NullItem_ReturnsFalse()
        {
            AutoLootManager.IsTrackableGroundItem(null).Should().BeFalse();
        }

        [Fact]
        public void IsTrackableGroundItem_GroundMovableItem_ReturnsTrue()
        {
            Client.UnitTestingActive = true;
            var world = new World();
            var item = Item.Create(world, 1);
            item.Flags = Flags.Movable;
            // Container defaults to 0xFFFF_FFFF → OnGround = true
            // Graphic defaults to 0 → not a corpse (0x2006)

            AutoLootManager.IsTrackableGroundItem(item).Should().BeTrue();

            CleanupTestWorld(world);
        }

        [Fact]
        public void IsTrackableGroundItem_ItemInContainer_ReturnsFalse()
        {
            Client.UnitTestingActive = true;
            var world = new World();
            var item = Item.Create(world, 1);
            item.Flags = Flags.Movable;
            item.Container = 0x40000001; // Valid serial → OnGround = false

            AutoLootManager.IsTrackableGroundItem(item).Should().BeFalse();

            CleanupTestWorld(world);
        }

        [Fact]
        public void IsTrackableGroundItem_Corpse_ReturnsFalse()
        {
            Client.UnitTestingActive = true;
            var world = new World();
            var item = Item.Create(world, 1);
            item.Flags = Flags.Movable;
            item.Graphic = 0x2006; // Corpse graphic

            AutoLootManager.IsTrackableGroundItem(item).Should().BeFalse();

            CleanupTestWorld(world);
        }

        [Fact]
        public void IsTrackableGroundItem_MovableItem_IsNotLocked()
        {
            // IsLocked = (Flags & Flags.Movable) == 0 && ItemData.Weight > 90
            // When Flags.Movable is set, IsLocked short-circuits to false regardless of weight.
            // This verifies movable items pass the IsLocked check in IsTrackableGroundItem.
            Client.UnitTestingActive = true;
            var world = new World();
            var item = Item.Create(world, 1);
            item.Flags = Flags.Movable;

            AutoLootManager.IsTrackableGroundItem(item).Should().BeTrue();

            CleanupTestWorld(world);
        }

        [Fact]
        public void BootstrapNearbyGroundItems_PopulatesSetWithNearbyItems()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Item within tracking radius (distance = max(|105-100|, |105-100|) = 5 <= 20)
            CreateGroundItem(world, 1, 105, 105);
            // Item within tracking radius (distance = max(|110-100|, |100-100|) = 10 <= 20)
            CreateGroundItem(world, 2, 110, 100);

            manager.BootstrapNearbyGroundItems();

            manager._nearbyGroundItems.Should().HaveCount(2);
            manager._nearbyGroundItems.Should().Contain(1u);
            manager._nearbyGroundItems.Should().Contain(2u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void BootstrapNearbyGroundItems_ExcludesItemsBeyondTrackingRadius()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Item within radius (distance = 5)
            CreateGroundItem(world, 1, 105, 100);
            // Item beyond tracking radius (distance = max(|125-100|, |100-100|) = 25 > 20)
            CreateGroundItem(world, 2, 125, 100);
            // Item way beyond radius (distance = 50)
            CreateGroundItem(world, 3, 150, 100);

            manager.BootstrapNearbyGroundItems();

            manager._nearbyGroundItems.Should().HaveCount(1);
            manager._nearbyGroundItems.Should().Contain(1u);
            manager._nearbyGroundItems.Should().NotContain(2u);
            manager._nearbyGroundItems.Should().NotContain(3u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void BootstrapNearbyGroundItems_ExcludesItemsInContainers()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Ground item within radius
            CreateGroundItem(world, 1, 105, 100);

            // Item in a container (not on ground) within radius
            var containerItem = Item.Create(world, 2);
            containerItem.X = 105;
            containerItem.Y = 100;
            containerItem.Flags = Flags.Movable;
            containerItem.Container = 0x40000001; // Valid container serial
            world.Items.Add(containerItem);

            manager.BootstrapNearbyGroundItems();

            manager._nearbyGroundItems.Should().HaveCount(1);
            manager._nearbyGroundItems.Should().Contain(1u);
            manager._nearbyGroundItems.Should().NotContain(2u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void BootstrapNearbyGroundItems_ExcludesCorpses()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Normal ground item within radius
            CreateGroundItem(world, 1, 105, 100);

            // Corpse within radius
            CreateGroundItem(world, 2, 106, 100, graphic: 0x2006);

            manager.BootstrapNearbyGroundItems();

            manager._nearbyGroundItems.Should().HaveCount(1);
            manager._nearbyGroundItems.Should().Contain(1u);
            manager._nearbyGroundItems.Should().NotContain(2u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void BootstrapNearbyGroundItems_ClearsExistingSetBeforePopulating()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Pre-populate with a stale serial
            manager._nearbyGroundItems.Add(999u);

            CreateGroundItem(world, 1, 105, 100);

            manager.BootstrapNearbyGroundItems();

            manager._nearbyGroundItems.Should().HaveCount(1);
            manager._nearbyGroundItems.Should().Contain(1u);
            manager._nearbyGroundItems.Should().NotContain(999u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void BootstrapNearbyGroundItems_ItemAtExactRadiusBoundary_IsIncluded()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Item exactly at tracking radius (distance = 20 <= 20)
            CreateGroundItem(world, 1, 120, 100);

            manager.BootstrapNearbyGroundItems();

            manager._nearbyGroundItems.Should().HaveCount(1);
            manager._nearbyGroundItems.Should().Contain(1u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void BootstrapNearbyGroundItems_ItemJustBeyondRadius_IsExcluded()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Item just beyond tracking radius (distance = 21 > 20)
            CreateGroundItem(world, 1, 121, 100);

            manager.BootstrapNearbyGroundItems();

            manager._nearbyGroundItems.Should().BeEmpty();

            CleanupTestWorld(world);
        }

        [Fact]
        public void OnPositionChanged_PrunesItemsRemovedFromWorld()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Add items to world and tracking set
            CreateGroundItem(world, 1, 105, 100);
            CreateGroundItem(world, 2, 106, 100);
            manager._nearbyGroundItems.Add(1u);
            manager._nearbyGroundItems.Add(2u);

            // Remove item 2 from world (simulating item deletion)
            world.Items.Remove(2u);

            // Set up ProfileManager.CurrentProfile for the method
            SetProfileForScavenger();

            manager.OnPositionChanged(null, new PositionChangedArgs(new Vector3(100, 100, 0)));

            // Item 1 should remain, item 2 should be pruned (not found in world)
            manager._nearbyGroundItems.Should().Contain(1u);
            manager._nearbyGroundItems.Should().NotContain(2u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void OnPositionChanged_PrunesItemsMovedOutOfRange()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Add item within range
            var item = CreateGroundItem(world, 1, 105, 100);
            manager._nearbyGroundItems.Add(1u);

            // Move item far away (distance = 50 >= SCAVENGER_TRACKING_RADIUS)
            item.X = 150;
            item.Y = 100;

            SetProfileForScavenger();

            manager.OnPositionChanged(null, new PositionChangedArgs(new Vector3(100, 100, 0)));

            manager._nearbyGroundItems.Should().NotContain(1u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void OnPositionChanged_KeepsItemsStillInRange()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Add item within range (distance = 5)
            CreateGroundItem(world, 1, 105, 100);
            manager._nearbyGroundItems.Add(1u);

            SetProfileForScavenger();

            manager.OnPositionChanged(null, new PositionChangedArgs(new Vector3(100, 100, 0)));

            manager._nearbyGroundItems.Should().Contain(1u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void OnPositionChanged_EmptySet_TriggersBootstrap()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Add items to world but NOT to tracking set (set is empty)
            CreateGroundItem(world, 1, 105, 100);
            CreateGroundItem(world, 2, 106, 100);

            manager._nearbyGroundItems.Should().BeEmpty();

            SetProfileForScavenger();

            manager.OnPositionChanged(null, new PositionChangedArgs(new Vector3(100, 100, 0)));

            // Bootstrap should have populated the set
            manager._nearbyGroundItems.Should().Contain(1u);
            manager._nearbyGroundItems.Should().Contain(2u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void OnPositionChanged_WhenNotLoaded_DoesNothing()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = new AutoLootManager(world);
            manager._loaded = false; // Not loaded
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>();

            CreateGroundItem(world, 1, 105, 100);
            manager._nearbyGroundItems.Add(99u); // Stale entry

            SetProfileForScavenger();

            manager.OnPositionChanged(null, new PositionChangedArgs(new Vector3(100, 100, 0)));

            // Nothing should change — method returns early
            manager._nearbyGroundItems.Should().Contain(99u);
            manager._nearbyGroundItems.Should().HaveCount(1);

            CleanupTestWorld(world);
        }

        [Fact]
        public void NearbyGroundItems_ClearedOnSceneUnload()
        {
            // OnSceneUnload calls _nearbyGroundItems.Clear() among other things.
            // We can't call OnSceneUnload directly in tests because it also calls Save()
            // (which needs _savePath), unsubscribes events, and sets Instance = null.
            // Instead, we verify that the tracking set supports clearing, which is
            // the operation OnSceneUnload performs on it.
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            manager._nearbyGroundItems.Add(1u);
            manager._nearbyGroundItems.Add(2u);
            manager._nearbyGroundItems.Add(3u);
            manager._nearbyGroundItems.Should().HaveCount(3);

            // Simulate what OnSceneUnload does to the tracking set
            manager._nearbyGroundItems.Clear();

            manager._nearbyGroundItems.Should().BeEmpty();

            CleanupTestWorld(world);
        }

        [Fact]
        public void OnPositionChanged_PrunesItemsNoLongerOnGround()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Add item that's on ground and in range
            var item = CreateGroundItem(world, 1, 105, 100);
            manager._nearbyGroundItems.Add(1u);

            // Move item into a container (no longer on ground)
            item.Container = 0x40000001;

            SetProfileForScavenger();

            manager.OnPositionChanged(null, new PositionChangedArgs(new Vector3(100, 100, 0)));

            // Item should be pruned: OnPositionChanged checks item.OnGround during iteration
            // Actually, it checks (item == null || !item.OnGround || item.IsLocked)
            manager._nearbyGroundItems.Should().NotContain(1u);

            CleanupTestWorld(world);
        }

        /// <summary>
        /// Sets up ProfileManager.CurrentProfile with EnableScavenger = true via reflection.
        /// </summary>
        private static void SetProfileForScavenger()
        {
            var profile = new Profile
            {
                EnableScavenger = true,
                AutoOpenCorpseRange = 3
            };

            typeof(ProfileManager)
                .GetProperty("CurrentProfile", BindingFlags.Static | BindingFlags.Public)
                .SetValue(null, profile);
        }

        #endregion

        #region Match Cache Tests

        /// <summary>
        /// Creates an AutoLootManager with a World, loaded state, and loot entries configured.
        /// Items created via CreateGroundItem will have World set for OPL access.
        /// </summary>
        private static AutoLootManager CreateCacheTestManager(World world, List<AutoLootManager.AutoLootConfigEntry> entries)
        {
            var manager = new AutoLootManager(world);
            manager._loaded = true;
            manager._mergedEntries = entries;
            manager.RebuildGraphicIndex();
            // RebuildGraphicIndex calls ClearMatchCache, so cache starts empty
            return manager;
        }

        [Fact]
        public void MatchCache_CacheMiss_RunsMatchingAndStoresResult()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry { Graphic = 0x0EEA, Hue = ushort.MaxValue };
            var manager = CreateCacheTestManager(world, new List<AutoLootManager.AutoLootConfigEntry> { entry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            // First call — cache miss, should run matching and store result
            var result = manager.IsOnLootList(item);

            result.Should().Be(entry);
            manager._matchCache.Should().ContainKey(1u);
            manager._matchCache[1u].Should().Be(entry);

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_PositiveCacheHit_ReturnsStoredEntry()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry { Graphic = 0x0EEA, Hue = ushort.MaxValue };
            var manager = CreateCacheTestManager(world, new List<AutoLootManager.AutoLootConfigEntry> { entry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            // First call populates cache
            manager.IsOnLootList(item);

            // Second call should return cached result
            var result = manager.IsOnLootList(item);

            result.Should().Be(entry);

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_NegativeCacheHit_ReturnsNull()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry { Graphic = 0x1234, Hue = ushort.MaxValue };
            var manager = CreateCacheTestManager(world, new List<AutoLootManager.AutoLootConfigEntry> { entry });

            // Item with different graphic — won't match
            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            // First call — no match, caches null
            var result1 = manager.IsOnLootList(item);
            result1.Should().BeNull();
            manager._matchCache.Should().ContainKey(1u);

            // Second call — should return cached null
            var result2 = manager.IsOnLootList(item);
            result2.Should().BeNull();

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_NegativeCacheWithoutOpl_ReEvaluatesWhenOplArrives()
        {
            // CRITICAL: The OPL timing problem.
            // 1. Item created, no OPL yet
            // 2. Regex entry doesn't match (falls back to ItemData.Name)
            // 3. Cached as null WITHOUT OPL
            // 4. OPL arrives with matching properties
            // 5. Must detect stale cache and re-evaluate
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA,
                Hue = ushort.MaxValue,
                RegexSearch = "Damage Increase"
            };
            var manager = CreateCacheTestManager(world, new List<AutoLootManager.AutoLootConfigEntry> { entry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            // Step 1: No OPL — regex won't match (falls back to ItemData.Name which won't have "Damage Increase")
            var result1 = manager.IsOnLootList(item);
            result1.Should().BeNull("no OPL data, regex cannot match");
            manager._matchCacheHasOpl.Should().NotContain(1u, "OPL was not available during caching");

            // Step 2: OPL arrives with matching data
            world.OPL.Add(1u, 0, "A Longsword", "Damage Increase 50%", 0);

            // Step 3: Re-check — cache should detect OPL arrived and re-evaluate
            var result2 = manager.IsOnLootList(item);
            result2.Should().Be(entry, "OPL now available, regex should match");
            manager._matchCacheHasOpl.Should().Contain(1u, "OPL was available during re-evaluation");

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_NegativeCacheWithOpl_DoesNotReEvaluate()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA,
                Hue = ushort.MaxValue,
                RegexSearch = "Vanquishing"
            };
            var manager = CreateCacheTestManager(world, new List<AutoLootManager.AutoLootConfigEntry> { entry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            // Add OPL that does NOT match the regex
            world.OPL.Add(1u, 0, "A Longsword", "Damage Increase 50%", 0);

            // First call — no match WITH OPL present
            var result1 = manager.IsOnLootList(item);
            result1.Should().BeNull("regex doesn't match OPL data");
            manager._matchCacheHasOpl.Should().Contain(1u, "OPL was available when cached");

            // Second call — should return cached null (valid negative, OPL was present)
            var result2 = manager.IsOnLootList(item);
            result2.Should().BeNull("valid negative cache with OPL");

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_RegexEntryMatchesAfterOplArrives()
        {
            // Verify the full scenario: regex-only entry matches after OPL arrives
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = -1, // wildcard — matches any graphic
                Hue = ushort.MaxValue,
                RegexSearch = "vanq"
            };
            var manager = CreateCacheTestManager(world, new List<AutoLootManager.AutoLootConfigEntry> { entry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            // No OPL — regex falls back to ItemData.Name
            var result1 = manager.IsOnLootList(item);
            result1.Should().BeNull("no OPL, ItemData.Name unlikely to contain 'vanq'");

            // OPL arrives with vanquishing
            world.OPL.Add(1u, 0, "A Katana", "vanquishing", 0);

            // Re-evaluate — should now match
            var result2 = manager.IsOnLootList(item);
            result2.Should().Be(entry, "OPL now has 'vanq' in data");

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_ClearOnLootListMutation()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry { Graphic = 0x0EEA, Hue = ushort.MaxValue };
            var manager = CreateCacheTestManager(world, new List<AutoLootManager.AutoLootConfigEntry> { entry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            // Populate cache
            manager.IsOnLootList(item);
            manager._matchCache.Should().NotBeEmpty();

            // Mutate loot list — should clear cache
            manager._mergedEntries.Add(new AutoLootManager.AutoLootConfigEntry { Graphic = 0x1234 });
            manager.RebuildGraphicIndex();

            manager._matchCache.Should().BeEmpty("cache should be cleared on loot list mutation");
            manager._matchCacheHasOpl.Should().BeEmpty("OPL tracking should be cleared too");

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_SingleEntryInvalidationOnOplReceive()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry { Graphic = 0x0EEA, Hue = ushort.MaxValue };
            var manager = CreateCacheTestManager(world, new List<AutoLootManager.AutoLootConfigEntry> { entry });

            var item1 = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);
            var item2 = CreateGroundItem(world, 2, 106, 100, graphic: 0x0EEA);

            // Populate cache for both items
            manager.IsOnLootList(item1);
            manager.IsOnLootList(item2);
            manager._matchCache.Should().HaveCount(2);

            // Simulate single-serial invalidation (what OnOPLReceived does)
            manager._matchCache.Remove(1u);
            manager._matchCacheHasOpl.Remove(1u);

            // Item 1 should be evicted, item 2 should remain
            manager._matchCache.Should().NotContainKey(1u);
            manager._matchCache.Should().ContainKey(2u);

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_ClearMatchCacheClearsBothStructures()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Manually populate cache and OPL tracking
            manager._matchCache[1u] = new AutoLootManager.AutoLootConfigEntry { Graphic = 100 };
            manager._matchCache[2u] = null;
            manager._matchCacheHasOpl.Add(1u);

            manager.ClearMatchCache();

            manager._matchCache.Should().BeEmpty();
            manager._matchCacheHasOpl.Should().BeEmpty();

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_SceneUnloadClearsCache()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = CreateTestManagerWithWorld(world);

            // Populate cache
            manager._matchCache[1u] = new AutoLootManager.AutoLootConfigEntry { Graphic = 100 };
            manager._matchCacheHasOpl.Add(1u);

            // Simulate what OnSceneUnload does to cache
            manager.ClearMatchCache();

            manager._matchCache.Should().BeEmpty();
            manager._matchCacheHasOpl.Should().BeEmpty();

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_NotLoadedReturnsNull()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var manager = new AutoLootManager(world);
            manager._loaded = false;
            manager._mergedEntries = new List<AutoLootManager.AutoLootConfigEntry>
            {
                new() { Graphic = 0x0EEA, Hue = ushort.MaxValue }
            };

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            var result = manager.IsOnLootList(item);
            result.Should().BeNull("IsOnLootList returns null when not loaded");
            manager._matchCache.Should().BeEmpty("cache should not be populated when not loaded");

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_StoresBestMatch_NotFirstMatch()
        {
            // Arrange — two entries match the same item, Normal listed first, High listed second.
            // Cache must store the High-priority entry, not whichever matched first.
            var world = CreateWorldWithPlayer(100, 100);
            var normalEntry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                DestinationContainer = 1000,
                Priority = AutoLootManager.AutoLootPriority.Normal
            };
            var highEntry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                DestinationContainer = 2000,
                Priority = AutoLootManager.AutoLootPriority.High
            };
            // Normal listed first to ensure the code doesn't short-circuit on first match
            var manager = CreateCacheTestManager(world,
                new List<AutoLootManager.AutoLootConfigEntry> { normalEntry, highEntry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            // Act
            var result = manager.IsOnLootList(item);

            // Assert — cache should store the best (High) match, not the first (Normal)
            result.Should().Be(highEntry);
            manager._matchCache[1u].Should().Be(highEntry, "cache must store highest-priority match, not first match");

            CleanupTestWorld(world);
        }

        [Fact]
        public void MatchCache_MemoryGrowth_ClearPreventsUnboundedGrowth()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry { Graphic = 0x0EEA, Hue = ushort.MaxValue };
            var manager = CreateCacheTestManager(world, new List<AutoLootManager.AutoLootConfigEntry> { entry });

            // Add many items to the cache
            for (uint serial = 1; serial <= 100; serial++)
            {
                var item = CreateGroundItem(world, serial, 105, 100, graphic: 0x0EEA);
                manager.IsOnLootList(item);
            }

            manager._matchCache.Should().HaveCount(100);

            // Clear should prevent unbounded growth
            manager.ClearMatchCache();
            manager._matchCache.Should().BeEmpty();
            manager._matchCacheHasOpl.Should().BeEmpty();

            CleanupTestWorld(world);
        }

        #endregion

        #region Priority Queue Tests

        [Fact]
        public void PriorityQueue_DequeuesHighPriorityFirst()
        {
            // Arrange — simulate the same priority encoding used in LootItem()
            var pq = new PriorityQueue<(uint item, AutoLootManager.AutoLootConfigEntry entry), int>();

            var lowEntry = new AutoLootManager.AutoLootConfigEntry { Name = "Low", Priority = AutoLootManager.AutoLootPriority.Low };
            var normalEntry = new AutoLootManager.AutoLootConfigEntry { Name = "Normal", Priority = AutoLootManager.AutoLootPriority.Normal };
            var highEntry = new AutoLootManager.AutoLootConfigEntry { Name = "High", Priority = AutoLootManager.AutoLootPriority.High };

            // Enqueue in scrambled order: Normal, Low, High, Normal, Low, High
            pq.Enqueue((1, normalEntry), -(int)normalEntry.Priority);
            pq.Enqueue((2, lowEntry), -(int)lowEntry.Priority);
            pq.Enqueue((3, highEntry), -(int)highEntry.Priority);
            pq.Enqueue((4, normalEntry), -(int)normalEntry.Priority);
            pq.Enqueue((5, lowEntry), -(int)lowEntry.Priority);
            pq.Enqueue((6, highEntry), -(int)highEntry.Priority);

            // Act — dequeue all
            var results = new List<(uint item, AutoLootManager.AutoLootConfigEntry entry)>();
            while (pq.Count > 0)
                results.Add(pq.Dequeue());

            // Assert — High items first, then Normal, then Low
            results.Should().HaveCount(6);

            results[0].entry.Priority.Should().Be(AutoLootManager.AutoLootPriority.High);
            results[1].entry.Priority.Should().Be(AutoLootManager.AutoLootPriority.High);
            results[2].entry.Priority.Should().Be(AutoLootManager.AutoLootPriority.Normal);
            results[3].entry.Priority.Should().Be(AutoLootManager.AutoLootPriority.Normal);
            results[4].entry.Priority.Should().Be(AutoLootManager.AutoLootPriority.Low);
            results[5].entry.Priority.Should().Be(AutoLootManager.AutoLootPriority.Low);
        }

        [Fact]
        public void PriorityQueue_NullEntryDefaultsToNormalPriority()
        {
            // Arrange — when entry is null, LootItem uses -(int)AutoLootPriority.Normal
            var pq = new PriorityQueue<(uint item, AutoLootManager.AutoLootConfigEntry entry), int>();

            var highEntry = new AutoLootManager.AutoLootConfigEntry { Name = "High", Priority = AutoLootManager.AutoLootPriority.High };
            var lowEntry = new AutoLootManager.AutoLootConfigEntry { Name = "Low", Priority = AutoLootManager.AutoLootPriority.Low };

            // Null entry with Normal priority encoding
            int nullPri = -(int)AutoLootManager.AutoLootPriority.Normal;
            pq.Enqueue((1, null), nullPri);
            pq.Enqueue((2, highEntry), -(int)highEntry.Priority);
            pq.Enqueue((3, lowEntry), -(int)lowEntry.Priority);

            // Act
            var first = pq.Dequeue();
            var second = pq.Dequeue();
            var third = pq.Dequeue();

            // Assert — High first, then null (Normal priority), then Low
            first.entry.Should().Be(highEntry);
            second.entry.Should().BeNull();
            third.entry.Should().Be(lowEntry);
        }

        [Fact]
        public void PriorityQueue_SamePriority_AllDequeued()
        {
            // Arrange
            var pq = new PriorityQueue<(uint item, AutoLootManager.AutoLootConfigEntry entry), int>();

            var entry1 = new AutoLootManager.AutoLootConfigEntry { Name = "Normal A", Priority = AutoLootManager.AutoLootPriority.Normal };
            var entry2 = new AutoLootManager.AutoLootConfigEntry { Name = "Normal B", Priority = AutoLootManager.AutoLootPriority.Normal };
            var entry3 = new AutoLootManager.AutoLootConfigEntry { Name = "Normal C", Priority = AutoLootManager.AutoLootPriority.Normal };

            pq.Enqueue((1, entry1), -(int)entry1.Priority);
            pq.Enqueue((2, entry2), -(int)entry2.Priority);
            pq.Enqueue((3, entry3), -(int)entry3.Priority);

            // Act
            var results = new List<(uint item, AutoLootManager.AutoLootConfigEntry entry)>();
            while (pq.Count > 0)
                results.Add(pq.Dequeue());

            // Assert — all three should be dequeued (order doesn't matter for same priority)
            results.Should().HaveCount(3);
            results.Select(r => r.item).Should().BeEquivalentTo(new uint[] { 1, 2, 3 });
            results.Should().OnlyContain(r => r.entry.Priority == AutoLootManager.AutoLootPriority.Normal);
        }

        #endregion

        #region Priority Selection Tests (IsOnLootList returns highest-priority match)

        [Fact]
        public void IsOnLootList_TwoGraphicMatches_ReturnsHighestPriority()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var normalEntry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                DestinationContainer = 1000,
                Priority = AutoLootManager.AutoLootPriority.Normal
            };
            var highEntry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                DestinationContainer = 2000,
                Priority = AutoLootManager.AutoLootPriority.High
            };
            var manager = CreateCacheTestManager(world,
                new List<AutoLootManager.AutoLootConfigEntry> { normalEntry, highEntry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            var result = manager.IsOnLootList(item);

            result.Should().Be(highEntry, "High priority entry should win over Normal");

            CleanupTestWorld(world);
        }

        [Fact]
        public void IsOnLootList_WildcardHighBeatsGraphicNormal()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var graphicNormal = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                Priority = AutoLootManager.AutoLootPriority.Normal
            };
            var wildcardHigh = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = -1, Hue = ushort.MaxValue,
                Priority = AutoLootManager.AutoLootPriority.High
            };
            var manager = CreateCacheTestManager(world,
                new List<AutoLootManager.AutoLootConfigEntry> { graphicNormal, wildcardHigh });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            var result = manager.IsOnLootList(item);

            result.Should().Be(wildcardHigh, "Wildcard High should beat graphic-specific Normal");

            CleanupTestWorld(world);
        }

        [Fact]
        public void IsOnLootList_SingleMatch_StillReturnsCorrectly()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var entry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                Priority = AutoLootManager.AutoLootPriority.Low
            };
            var manager = CreateCacheTestManager(world,
                new List<AutoLootManager.AutoLootConfigEntry> { entry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            var result = manager.IsOnLootList(item);

            result.Should().Be(entry, "Single matching entry should be returned regardless of priority level");

            CleanupTestWorld(world);
        }

        [Fact]
        public void IsOnLootList_HighPriorityListedFirst_StillReturnsHighest()
        {
            // Verify order independence: high entry listed before normal entry
            var world = CreateWorldWithPlayer(100, 100);
            var highEntry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                DestinationContainer = 2000,
                Priority = AutoLootManager.AutoLootPriority.High
            };
            var normalEntry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                DestinationContainer = 1000,
                Priority = AutoLootManager.AutoLootPriority.Normal
            };
            var manager = CreateCacheTestManager(world,
                new List<AutoLootManager.AutoLootConfigEntry> { highEntry, normalEntry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            var result = manager.IsOnLootList(item);

            result.Should().Be(highEntry, "High priority should win regardless of list order");

            CleanupTestWorld(world);
        }

        [Fact]
        public void IsOnLootList_AllThreePriorities_ReturnsHighest()
        {
            var world = CreateWorldWithPlayer(100, 100);
            var lowEntry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                DestinationContainer = 1000,
                Priority = AutoLootManager.AutoLootPriority.Low
            };
            var normalEntry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                DestinationContainer = 2000,
                Priority = AutoLootManager.AutoLootPriority.Normal
            };
            var highEntry = new AutoLootManager.AutoLootConfigEntry
            {
                Graphic = 0x0EEA, Hue = ushort.MaxValue,
                DestinationContainer = 3000,
                Priority = AutoLootManager.AutoLootPriority.High
            };
            var manager = CreateCacheTestManager(world,
                new List<AutoLootManager.AutoLootConfigEntry> { lowEntry, normalEntry, highEntry });

            var item = CreateGroundItem(world, 1, 105, 100, graphic: 0x0EEA);

            var result = manager.IsOnLootList(item);

            result.Should().Be(highEntry, "High priority should win over Normal and Low");

            CleanupTestWorld(world);
        }

        #endregion

        #region AutoLootProfile Serialization Tests

        [Fact]
        public void AutoLootProfile_Serialization_ShouldPreserveAllFields()
        {
            // Arrange
            var profile = new AutoLootManager.AutoLootProfile
            {
                Name = "My Profile",
                IsActive = true,
                FileName = "should_not_serialize.json",
                Entries = new List<AutoLootManager.AutoLootConfigEntry>
                {
                    new()
                    {
                        Name = "Vanq Katana",
                        Graphic = 5118,
                        Hue = 0,
                        RegexSearch = @".*vanq.*",
                        Priority = AutoLootManager.AutoLootPriority.High,
                        DestinationContainer = 12345u
                    },
                    new()
                    {
                        Name = "Gold Coin",
                        Graphic = 3821,
                        Hue = ushort.MaxValue,
                        RegexSearch = "",
                        Priority = AutoLootManager.AutoLootPriority.Normal,
                        DestinationContainer = 0u
                    }
                }
            };

            // Act
            string json = JsonSerializer.Serialize(profile, AutoLootJsonContext.Default.AutoLootProfile);
            AutoLootManager.AutoLootProfile deserialized = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.AutoLootProfile);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Name.Should().Be("My Profile");
            deserialized.IsActive.Should().BeTrue();
            deserialized.Entries.Should().HaveCount(2);

            deserialized.Entries[0].Name.Should().Be("Vanq Katana");
            deserialized.Entries[0].Graphic.Should().Be(5118);
            deserialized.Entries[0].RegexSearch.Should().Be(@".*vanq.*");
            deserialized.Entries[0].Priority.Should().Be(AutoLootManager.AutoLootPriority.High);
            deserialized.Entries[0].DestinationContainer.Should().Be(12345u);

            deserialized.Entries[1].Name.Should().Be("Gold Coin");
            deserialized.Entries[1].Graphic.Should().Be(3821);
            deserialized.Entries[1].Priority.Should().Be(AutoLootManager.AutoLootPriority.Normal);

            // Verify FileName (JsonIgnore) is not in the serialized JSON
            json.Should().NotContain("FileName");
            json.Should().NotContain("should_not_serialize");
        }

        [Fact]
        public void AutoLootProfile_Serialization_InactiveProfile_ShouldPreserveIsActive()
        {
            // Arrange
            var profile = new AutoLootManager.AutoLootProfile
            {
                Name = "Disabled Profile",
                IsActive = false,
                Entries = new List<AutoLootManager.AutoLootConfigEntry>()
            };

            // Act
            string json = JsonSerializer.Serialize(profile, AutoLootJsonContext.Default.AutoLootProfile);
            AutoLootManager.AutoLootProfile deserialized = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.AutoLootProfile);

            // Assert
            deserialized.IsActive.Should().BeFalse();
            deserialized.Name.Should().Be("Disabled Profile");
            deserialized.Entries.Should().BeEmpty();
        }

        [Fact]
        public void AutoLootProfile_Serialization_FileNameIsExcluded()
        {
            // Arrange
            var profile = new AutoLootManager.AutoLootProfile
            {
                Name = "Test",
                FileName = "test_file.json"
            };

            // Act
            string json = JsonSerializer.Serialize(profile, AutoLootJsonContext.Default.AutoLootProfile);

            // Assert - FileName should not appear anywhere in the JSON
            json.Should().NotContain("FileName");
            json.Should().NotContain("test_file");

            // Verify deserialized profile has default FileName
            AutoLootManager.AutoLootProfile deserialized = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.AutoLootProfile);
            deserialized.FileName.Should().BeEmpty();
        }

        #endregion
    }
}
