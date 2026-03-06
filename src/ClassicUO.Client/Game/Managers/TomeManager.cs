using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using ClassicUO.Configuration;
using ClassicUO.Utility;

namespace ClassicUO.Game.Managers
{
    internal sealed class TomeManager
    {
        public static TomeManager Instance { get; private set; }

        public List<TomeDefinition> TomeDefinitions { get; private set; } = new();

        private readonly string _savePath;

        private TomeManager(string savePath)
        {
            _savePath = savePath;
        }

        internal static TomeManager CreateForTests(string savePath) => new(savePath);

        private static string GetDataPath()
        {
            string dataPath = ProfileManager.ProfilePath;
            if (!Directory.Exists(dataPath))
                Directory.CreateDirectory(dataPath);
            return dataPath;
        }

        public static void Load()
        {
            string savePath = Path.Combine(GetDataPath(), "TomeDefinitions.json");
            Instance = new TomeManager(savePath);
            Instance.LoadInternal();
        }

        public static void Unload()
        {
            Instance?.Save();
            Instance = null;
        }

        internal void LoadInternal()
        {
            if (JsonHelper.Load<List<TomeDefinition>>(_savePath, TomeDefinitionContext.Default.ListTomeDefinition, out List<TomeDefinition> definitions))
                TomeDefinitions = definitions ?? new List<TomeDefinition>();
        }

        public void Save() => JsonHelper.SaveAndBackup(TomeDefinitions, _savePath, TomeDefinitionContext.Default.ListTomeDefinition);

        #nullable enable
        public string? GetJsonExport(TomeDefinition definition)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(definition, TomeDefinitionContext.Default.TomeDefinition);
            }
            catch (Exception)
            {
                // ignored
            }

            return null;
        }

        public string? GetJsonExportAll()
        {
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(TomeDefinitions, TomeDefinitionContext.Default.ListTomeDefinition);
            }
            catch (Exception)
            {
                // ignored
            }

            return null;
        }
        #nullable disable

        public bool ImportFromJson(string json)
        {
            try
            {
                TomeDefinition imported = System.Text.Json.JsonSerializer.Deserialize(json, TomeDefinitionContext.Default.TomeDefinition);
                if (imported != null)
                {
                    imported.Name = GetUniqueName(imported.Name);
                    TomeDefinitions.Add(imported);
                    Save();
                    return true;
                }
            }
            catch (Exception)
            {
                // ignored – fall through to try list format
            }

            try
            {
                List<TomeDefinition> importedList = System.Text.Json.JsonSerializer.Deserialize(json, TomeDefinitionContext.Default.ListTomeDefinition);
                if (importedList == null || importedList.Count == 0)
                    return false;

                foreach (TomeDefinition tome in importedList)
                {
                    tome.Name = GetUniqueName(tome.Name);
                    TomeDefinitions.Add(tome);
                }

                Save();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string GetUniqueName(string baseName)
        {
            string normalizedBaseName = string.IsNullOrWhiteSpace(baseName) ? "Migrated Tome" : baseName.Trim();
            string name = normalizedBaseName;
            int i = 2;

            while (TomeDefinitions.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                name = $"{normalizedBaseName} ({i++})";

            return name;
        }

        internal TomeDefinition GetOrCreateFromLegacy(uint tomeSerial, uint gumpId, int addButtonId)
        {
            TomeDefinition existing = TomeDefinitions.FirstOrDefault(
                t => t.TomeSerial == tomeSerial
                    && t.GumpId == gumpId
                    && t.AddButtonId == addButtonId
            );

            if (existing != null)
                return existing;

            var definition = new TomeDefinition
            {
                Name = GetUniqueName("Migrated Tome"),
                TomeSerial = tomeSerial,
                GumpId = gumpId,
                AddButtonId = addButtonId,
                Mode = TomeMode.TargetEach,
                Delay = 1000,
                RequiresWalk = false
            };

            TomeDefinitions.Add(definition);
            return definition;
        }
    }

    internal class TomeDefinition
    {
        public string Name { get; set; } = string.Empty;
        public uint TomeSerial { get; set; }
        public uint GumpId { get; set; }
        public int AddButtonId { get; set; }
        public TomeMode Mode { get; set; } = TomeMode.TargetEach;
        public uint TargetSerial { get; set; }
        public int Delay { get; set; } = 1000;
        public bool RequiresWalk { get; set; }
    }

    internal enum TomeMode
    {
        FillAll = 0,
        TargetEach = 1,
        TargetContainer = 2,
        TargetRepeat = 3
    }

    [JsonSerializable(typeof(List<TomeDefinition>))]
    [JsonSerializable(typeof(TomeDefinition))]
    [JsonSerializable(typeof(TomeMode))]
    internal partial class TomeDefinitionContext : JsonSerializerContext
    {
    }
}
