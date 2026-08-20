using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers
{
    /// <summary>On-disk shape of <c>radialmenu.json</c>, persisted per profile.</summary>
    public sealed class RadialMenuFile
    {
        public int Version { get; set; }

        /// <summary>Macro name per slot, indexed by slot. Empty or missing means an unused slot.</summary>
        public List<string> Slots { get; set; } = new();
    }

    [JsonSourceGenerationOptions(WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(RadialMenuFile))]
    internal partial class RadialMenuJsonContext : JsonSerializerContext
    {
    }

    /// <summary>
    /// Which macro sits in each slot of the controller radial menu.
    /// </summary>
    /// <remarks>
    /// Slots hold macro names rather than macro references because macros are reloaded per profile
    /// and rebuilt on edit, so a held reference would go stale; a name survives both.
    /// </remarks>
    internal static class RadialMenuManager
    {
        public const int SLOT_COUNT = 8;

        private static readonly string[] _slots = new string[SLOT_COUNT];

        public static string FilePath => Path.Combine(ProfileManager.ProfilePath, "radialmenu.json");

        public static string GetSlot(int slot)
            => slot >= 0 && slot < SLOT_COUNT ? _slots[slot] : null;

        public static void SetSlot(int slot, string macroName)
        {
            if (slot < 0 || slot >= SLOT_COUNT)
            {
                return;
            }

            _slots[slot] = string.IsNullOrWhiteSpace(macroName) ? null : macroName;

            Save();
        }

        /// <summary>True when at least one slot is filled, so an empty menu is not shown.</summary>
        public static bool HasAnySlot()
        {
            foreach (string slot in _slots)
            {
                if (!string.IsNullOrEmpty(slot))
                {
                    return true;
                }
            }

            return false;
        }

        public static void Load()
        {
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                _slots[i] = null;
            }

            RadialMenuFile file = ConfigurationResolver.Load<RadialMenuFile>(FilePath, RadialMenuJsonContext.Default.RadialMenuFile);

            if (file?.Slots == null)
            {
                return;
            }

            for (int i = 0; i < SLOT_COUNT && i < file.Slots.Count; i++)
            {
                _slots[i] = string.IsNullOrWhiteSpace(file.Slots[i]) ? null : file.Slots[i];
            }
        }

        public static void Save()
        {
            var file = new RadialMenuFile { Version = 1 };

            for (int i = 0; i < SLOT_COUNT; i++)
            {
                file.Slots.Add(_slots[i] ?? string.Empty);
            }

            ConfigurationResolver.Save(file, FilePath, RadialMenuJsonContext.Default.RadialMenuFile);
        }
    }
}
