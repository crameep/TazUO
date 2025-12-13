using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    public class SoundFilterManager
    {
        private static SoundFilterManager _instance;
        private HashSet<int> _filteredSounds;
        private bool _isLoaded;

        public static SoundFilterManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new SoundFilterManager();
                return _instance;
            }
        }

        public HashSet<int> FilteredSounds
        {
            get
            {
                EnsureLoaded();
                return _filteredSounds;
            }
        }

        private SoundFilterManager()
        {
            _filteredSounds = new HashSet<int>();
            _isLoaded = false;
        }

        private void EnsureLoaded()
        {
            if (!_isLoaded)
            {
                Load();
            }
        }

        public void Load()
        {
            if (Client.Settings == null)
            {
                Log.Warn("SQLSettings not available for SoundFilterManager");
                _filteredSounds = new HashSet<int>();
                _isLoaded = true;
                return;
            }

            try
            {
                string json = Client.Settings.Get(SettingsScope.Account, Constants.SqlSettings.SOUND_FILTER_IDS, "[]");

                if (!string.IsNullOrWhiteSpace(json))
                {
                    _filteredSounds = JsonSerializer.Deserialize(json, HashSetIntContext.Default.HashSetInt32)
                        ?? new HashSet<int>();
                }
                else
                {
                    _filteredSounds = new HashSet<int>();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to load sound filters: {ex.Message}");
                _filteredSounds = new HashSet<int>();
            }

            _isLoaded = true;
        }

        public void Save()
        {
            if (Client.Settings == null)
            {
                Log.Warn("SQLSettings not available for SoundFilterManager save");
                return;
            }

            try
            {
                string json = JsonSerializer.Serialize(_filteredSounds, HashSetIntContext.Default.HashSetInt32);
                Client.Settings.Set(SettingsScope.Account, Constants.SqlSettings.SOUND_FILTER_IDS, json);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to save sound filters: {ex.Message}");
            }
        }

        public void AddFilter(int soundId)
        {
            EnsureLoaded();
            if (_filteredSounds.Add(soundId))
            {
                Save();
            }
        }

        public void RemoveFilter(int soundId)
        {
            EnsureLoaded();
            if (_filteredSounds.Remove(soundId))
            {
                Save();
            }
        }

        public bool IsSoundFiltered(int soundId)
        {
            EnsureLoaded();
            return _filteredSounds.Contains(soundId);
        }

        public void Clear()
        {
            EnsureLoaded();
            _filteredSounds.Clear();
            Save();
        }

        public void Reset()
        {
            _isLoaded = false;
            _filteredSounds.Clear();
        }
    }

    [JsonSerializable(typeof(HashSet<int>))]
    [JsonSourceGenerationOptions(
        WriteIndented = false,
        PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        IgnoreReadOnlyProperties = false,
        IncludeFields = false)]
    public partial class HashSetIntContext : JsonSerializerContext
    {
    }
}
