using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using ClassicUO.Game.Managers.Structs;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    [JsonSerializable(typeof(AutoLootManager.AutoLootConfigEntry))]
    [JsonSerializable(typeof(List<AutoLootManager.AutoLootConfigEntry>))]
    [JsonSerializable(typeof(AutoLootManager.AutoLootPriority))]
    [JsonSerializable(typeof(AutoLootManager.AutoLootProfile))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    public partial class AutoLootJsonContext : JsonSerializerContext
    {
    }

    public class AutoLootManager
    {
        public static AutoLootManager Instance
        {
            get
            {
                if (field == null)
                    field = new();
                return field;
            }
            private set => field = value;
        }
        public List<AutoLootConfigEntry> AutoLootList { get => _mergedEntries; }

        private readonly HashSet<uint> _quickContainsLookup = new ();
        private readonly HashSet<uint> _recentlyLooted = new();
        private static readonly Queue<(uint item, AutoLootConfigEntry entry)> _lootItems = new ();
        private volatile List<AutoLootConfigEntry> _mergedEntries = new ();
        private volatile int _activeProfileCount = 0;
        private volatile bool _loaded = false;
        public bool Loaded => _loaded;
        private readonly string _savePath;
        private readonly string _profilesDir;
        private string _migrationSourcePath;
        public bool NeedsMigration => _migrationSourcePath != null;
        public string MigrationSourcePath => _migrationSourcePath;

        public List<AutoLootProfile> Profiles { get; set; } = new();
        public AutoLootProfile SelectedProfile { get; set; }
        private long _nextLootTime = Time.Ticks;
        private long _nextClearRecents = Time.Ticks + 5000;
        private ProgressBarGump _progressBarGump;
        private int _currentLootTotalCount = 0;
        private bool IsEnabled => ProfileManager.CurrentProfile.EnableAutoLoot;

        private readonly World _world;

        private AutoLootManager()
        {
            _world = Client.Game.UO.World;
            _savePath = Path.Combine(ProfileManager.ProfilePath, "AutoLoot.json");
            _profilesDir = Path.Combine(ProfileManager.ProfilePath, "AutoLootProfiles");
        }

        public bool IsBeingLooted(uint serial) => _quickContainsLookup.Contains(serial);

        public void LootItem(uint serial)
        {
            Item item = _world.Items.Get(serial);
            if (item != null) LootItem(item, null);
        }

        public void LootItem(Item item, AutoLootConfigEntry entry = null)
        {
            if (item == null || !_recentlyLooted.Add(item.Serial) || !_quickContainsLookup.Add(item.Serial)) return;

            _lootItems.Enqueue((item, entry));
            _currentLootTotalCount++;
            _nextClearRecents = Time.Ticks + 5000;
        }

        public void ForceLootContainer(uint serial)
        {
            Item cont = _world.Items.Get(serial);

            if (cont == null) return;

            for (LinkedObject i = cont.Items; i != null; i = i.Next) CheckAndLoot((Item)i);
        }

        /// <summary>
        /// Check an item against the loot list, if it needs to be auto looted it will be.
        /// </summary>
        private void CheckAndLoot(Item i)
        {
            if (!_loaded || i == null || _quickContainsLookup.Contains(i.Serial)) return;

            if(i.IsCorpse)
            {
                HandleCorpse(i);

                return;
            }

            if (i.ShouldAutoLoot)
            {
                LootItem(i, null);
                return;
            }

            AutoLootConfigEntry entry = IsOnLootList(i);
            if (entry != null) LootItem(i, entry);
        }

        /// <summary>
        /// Check if an item is on the auto loot list.
        /// Single active profile: returns first match (fast path).
        /// Multiple active profiles: returns the match with highest Priority.
        /// </summary>
        /// <param name="i">The item to check the loot list against</param>
        /// <returns>The matched AutoLootConfigEntry, or null if no match found</returns>
        private AutoLootConfigEntry IsOnLootList(Item i)
        {
            if (!_loaded) return null;

            List<AutoLootConfigEntry> entries = _mergedEntries;
            bool multipleActiveProfiles = _activeProfileCount > 1;

            if (!multipleActiveProfiles)
            {
                foreach (AutoLootConfigEntry entry in entries)
                    if (entry.Match(i))
                        return entry;

                return null;
            }

            AutoLootConfigEntry bestMatch = null;

            foreach (AutoLootConfigEntry entry in entries)
            {
                if (entry.Match(i) && (bestMatch == null || entry.Priority > bestMatch.Priority))
                    bestMatch = entry;
            }

            return bestMatch;
        }

        /// <summary>
        /// Add an entry for auto looting to match against when opening corpses.
        /// Routes to SelectedProfile's entry list.
        /// </summary>
        /// <param name="graphic"></param>
        /// <param name="hue"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public AutoLootConfigEntry AddAutoLootEntry(ushort graphic = 0, ushort hue = ushort.MaxValue, string name = "")
        {
            AutoLootProfile profile = EnsureSelectedProfile();
            if (profile == null)
                return null;

            var item = new AutoLootConfigEntry() { Graphic = graphic, Hue = hue, Name = name };

            foreach (AutoLootConfigEntry entry in profile.Entries)
                if (entry.Equals(item))
                    return entry;

            profile.Entries.Add(item);
            RebuildMergedList();
            SaveProfile(profile);

            return item;
        }

        private AutoLootProfile EnsureSelectedProfile()
        {
            if (SelectedProfile != null)
                return SelectedProfile;

            if (!_loaded)
            {
                Log.Warn("AddAutoLootEntry called before profiles are loaded");
                return null;
            }

            if (Profiles.Count > 0)
            {
                SelectedProfile = Profiles[0];
                return SelectedProfile;
            }

            SelectedProfile = CreateProfile("Default");
            return SelectedProfile;
        }

        /// <summary>
        /// Search through a corpse and check items that need to be looted.
        /// Only call this after checking that autoloot IsEnabled
        /// Note: This method doesn't gurantee to process all itmes in the corpse,
        /// because `corpse.Items` is populated via `AddItemToContainer` packet, thus
        /// it may not have all items yet when the method is called.
        /// </summary>
        /// <param name="corpse"></param>
        private void HandleCorpse(Item corpse)
        {
            if (corpse is not { IsCorpse: true }) return;

            if (corpse.Distance > ProfileManager.CurrentProfile.AutoOpenCorpseRange)
            {
                World.Instance?.Player?.AutoOpenedCorpses.Remove(corpse); //Retry if the distance was too great to loot
                return;
            }

            if (corpse.IsHumanCorpse && !ProfileManager.CurrentProfile.AutoLootHumanCorpses) return;

            for (LinkedObject i = corpse.Items; i != null; i = i.Next)
                CheckAndLoot((Item)i);
            
            if(ProfileManager.CurrentProfile.HueCorpseAfterAutoloot)
                corpse.Hue = 73;
        }

        public void TryRemoveAutoLootEntry(string uid)
        {
            if (!_loaded)
                return;

            AutoLootProfile profile = SelectedProfile;
            if (profile == null)
                return;

            int removeAt = -1;

            for (int i = 0; i < profile.Entries.Count; i++)
            {
                if (profile.Entries[i].Uid == uid)
                {
                    removeAt = i;
                    break;
                }
            }

            if (removeAt > -1)
            {
                profile.Entries.RemoveAt(removeAt);
                RebuildMergedList();
                SaveProfile(profile);
            }
        }

        /// <summary>
        /// Checks if item is a corpse, or if its root container is corpse and handles them appropriately.
        /// </summary>
        /// <param name="i"></param>
        private void CheckCorpse(Item i)
        {
            if (i == null) return;

            if (i.IsCorpse)
            {
                HandleCorpse(i);
                return;
            }

            Item root = _world.Items.Get(i.RootContainer);
            if (root != null && root.IsCorpse)
            {
                // Check the item that triggered this call directly
                CheckAndLoot(i);
                // A defensive safety net to ensure all items in the corpse are processed
                HandleCorpse(root);
                return;
            }
        }

        public void OnSceneLoad()
        {
            LoadProfiles();
            EventSink.OPLOnReceive += OnOPLReceived;
            EventSink.OnItemCreated += OnItemCreatedOrUpdated;
            EventSink.OnItemUpdated += OnItemCreatedOrUpdated;
            EventSink.OnOpenContainer += OnOpenContainer;
            EventSink.OnPositionChanged += OnPositionChanged;
        }

        public void OnSceneUnload()
        {
            EventSink.OPLOnReceive -= OnOPLReceived;
            EventSink.OnItemCreated -= OnItemCreatedOrUpdated;
            EventSink.OnItemUpdated -= OnItemCreatedOrUpdated;
            EventSink.OnOpenContainer -= OnOpenContainer;
            EventSink.OnPositionChanged -= OnPositionChanged;
            SaveAll();
            Instance = null;
        }

        /// <summary>
        /// Invoked whenever the player changes position.
        ///
        /// The other looter entry points are item update events but those are not enough;
        /// If the player opens a corpse and walks away a few steps, there wouldn't be any new events firing.
        ///
        /// This handler effectively allows re-triggering as soon as the corpses are back in range.
        ///
        /// Note that this venue kicks in only when distance is less than 3.
        /// </summary>
        /// <param name="sender">The source event sink</param>
        /// <param name="e">The position change event arguments</param>
        private void OnPositionChanged(object sender, PositionChangedArgs e)
        {
            if (!_loaded) return;

            if (ProfileManager.CurrentProfile.EnableScavenger)
                foreach (Item item in _world.Items.Values)
                    if (item != null && item.OnGround && !item.IsLocked && !item.IsCorpse && item.Distance < 3)
                        CheckAndLoot(item);

            if (IsEnabled)
                foreach (Item corpse in _world.GetCorpseSnapshot())
                    CheckCorpse(corpse);
        }

        private void OnOpenContainer(object sender, uint e)
        {
            if (!_loaded || !IsEnabled) return;

            CheckCorpse((Item)sender);
        }

        private void OnItemCreatedOrUpdated(object sender, EventArgs e)
        {
            if (!_loaded || !IsEnabled) return;

            if (sender is Item i)
            {
                CheckCorpse(i);

                // Check for ground items to auto-loot (scavenger functionality)
                if (ProfileManager.CurrentProfile.EnableScavenger && i.OnGround && !i.IsCorpse && !i.IsLocked && i.Distance <= ProfileManager.CurrentProfile.AutoOpenCorpseRange) CheckAndLoot(i);
            }
        }

        private void OnOPLReceived(object sender, OPLEventArgs e)
        {
            if (!_loaded || !IsEnabled) return;
            Item item = _world.Items.Get(e.Serial);
            if (item != null)
                CheckCorpse(item);
        }

        public void Update()
        {
            if (!_loaded || !IsEnabled || !_world.InGame) return;

            if (_nextLootTime > Time.Ticks) return;

            if (Client.Game.UO.GameCursor.ItemHold.Enabled)
                return; //Prevent moving stuff while holding an item.

            if (_lootItems.Count == 0)
            {
                _progressBarGump?.Dispose();
                if (Time.Ticks > _nextClearRecents)
                {
                    _recentlyLooted.Clear();
                    _nextClearRecents = Time.Ticks + 5000;
                }
                return;
            }

            (uint item, AutoLootConfigEntry entry) = _lootItems.Dequeue();
            if (item == 0) return;

            if (_lootItems.Count == 0) //Que emptied out
                _currentLootTotalCount = 0;

            _quickContainsLookup.Remove(item);

            Item moveItem = _world.Items.Get(item);

            if (moveItem == null)
                return;

            CreateProgressBar();

            if (_progressBarGump is { IsDisposed: false }) _progressBarGump.CurrentPercentage = 1 - ((double)_lootItems.Count / (double)_currentLootTotalCount);

            if (moveItem.Distance > ProfileManager.CurrentProfile.AutoOpenCorpseRange)
            {
                Item rc = _world.Items.Get(moveItem.RootContainer);
                if (rc != null && rc.Distance > ProfileManager.CurrentProfile.AutoOpenCorpseRange)
                {
                    if (rc.IsCorpse)
                        World.Instance?.Player?.AutoOpenedCorpses.Remove(rc); //Allow reopening this corpse, we got too far away to finish looting..
                    _recentlyLooted.Remove(item);
                    return;
                }
            }

            uint destinationSerial = 0;

            //If this entry has a specific container, use it
            if (entry != null && entry.DestinationContainer != 0)
            {
                Item itemDestContainer = _world.Items.Get(entry.DestinationContainer);
                if (itemDestContainer != null) destinationSerial = entry.DestinationContainer;
            }

            if (destinationSerial == 0 && ProfileManager.CurrentProfile.GrabBagSerial != 0)
            {
                Item grabBag = _world.Items.Get(ProfileManager.CurrentProfile.GrabBagSerial);
                if (grabBag != null) destinationSerial = ProfileManager.CurrentProfile.GrabBagSerial;
            }

            if (destinationSerial == 0)
            {
                Item backpack = _world.Player.Backpack;
                if (backpack != null) destinationSerial = backpack.Serial;
            }

            if (destinationSerial != 0)
            {
                ActionPriority lootPriority = entry?.Priority switch
                {
                    AutoLootPriority.High => ActionPriority.LootItemHigh,
                    AutoLootPriority.Low => ActionPriority.LootItem,
                    _ => ActionPriority.LootItemMedium,
                };
                ObjectActionQueue.Instance.Enqueue(new MoveRequest(moveItem.Serial, destinationSerial, moveItem.Amount).ToObjectActionQueueItem(), lootPriority);
            }
            else
                GameActions.Print("Could not find a container to loot into. Try setting a grab bag.");

            _nextLootTime = Time.Ticks + ProfileManager.CurrentProfile.MoveMultiObjectDelay;
        }

        private void CreateProgressBar()
        {
            if (ProfileManager.CurrentProfile.EnableAutoLootProgressBar && (_progressBarGump == null || _progressBarGump.IsDisposed))
            {
                _progressBarGump = new ProgressBarGump(_world, "Auto looting...", 0)
                {
                    Y = (ProfileManager.CurrentProfile.GameWindowPosition.Y + ProfileManager.CurrentProfile.GameWindowSize.Y) - 150,
                    ForegrouneColor = Color.DarkOrange
                };
                _progressBarGump.CenterXInViewPort();
                UIManager.Add(_progressBarGump);
            }
        }

        private void LoadProfiles()
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    Directory.CreateDirectory(_profilesDir);
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to create auto loot profiles directory: {e.Message}");
                    return;
                }

                // Migration state detection: check conditions in priority order
                string[] files;
                try
                {
                    files = Directory.GetFiles(_profilesDir, "*.json");
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to scan auto loot profiles directory: {e.Message}");
                    return;
                }

                if (files.Length > 0)
                {
                    // State 1: Directory has .json files — normal load, no migration
                    Log.Info("AutoLoot profiles directory found with existing profiles, loading normally.");
                }
                else
                {
                    // Directory is empty (or only has .backup files).
                    // Check for legacy AutoLoot.json files to migrate.
                    string ancientPath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Profiles", "AutoLoot.json");

                    if (File.Exists(_savePath))
                    {
                        // State 3: AutoLoot.json at ProfileManager.ProfilePath — flag for migration
                        _migrationSourcePath = _savePath;
                        Log.Info($"Legacy AutoLoot.json found at profile path, flagged for migration: {_savePath}");

                        if (File.Exists(ancientPath))
                            Log.Warn($"Legacy AutoLoot.json also exists at ancient path ({ancientPath}), using profile path version.");
                    }
                    else if (File.Exists(ancientPath))
                    {
                        // State 4: AutoLoot.json at ancient executable path — READ only, flag for migration
                        _migrationSourcePath = ancientPath;
                        Log.Info($"Legacy AutoLoot.json found at ancient path, flagged for migration: {ancientPath}");
                    }
                    else
                    {
                        // State 2/5: No legacy files exist — create empty Default profile
                        Log.Info("No existing auto loot data found, creating Default profile.");
                        var defaultProfile = CreateDefaultProfile();
                        SaveProfile(defaultProfile);
                        files = new[] { Path.Combine(_profilesDir, defaultProfile.FileName) };
                    }
                }

                // Perform migration if a legacy file was detected
                if (_migrationSourcePath != null)
                {
                    files = MigrateToDefaultProfile(files);
                }
                var loadedProfiles = new List<AutoLootProfile>();

                foreach (string file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        AutoLootProfile profile = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.AutoLootProfile);

                        if (profile == null)
                        {
                            Log.Error($"Failed to deserialize auto loot profile: {file}");
                            continue;
                        }

                        profile.FileName = Path.GetFileName(file);
                        loadedProfiles.Add(profile);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Error loading auto loot profile '{file}': {e.Message}");
                    }
                }

                Profiles = loadedProfiles;
                // Build merged list before marking as loaded so other threads
                // see a populated _mergedEntries when they check _loaded.
                // Note: RebuildMergedList() checks _loaded, so we temporarily
                // build the list directly here.
                var newList = new List<AutoLootConfigEntry>();
                int activeCount = 0;
                foreach (AutoLootProfile profile in loadedProfiles)
                {
                    if (profile.IsActive)
                    {
                        newList.AddRange(profile.Entries);
                        activeCount++;
                    }
                }
                _activeProfileCount = activeCount;
                _mergedEntries = newList;
                _loaded = true;
            });
        }

        private AutoLootProfile CreateDefaultProfile()
        {
            var profile = new AutoLootProfile
            {
                Name = "Default",
                IsActive = true,
                Entries = new List<AutoLootConfigEntry>(),
                FileName = "Default.json"
            };
            return profile;
        }

        /// <summary>
        /// Migrates entries from a legacy AutoLoot.json into a new Default profile.
        /// Reads the legacy file via JsonHelper.Load (which tries backup files too),
        /// creates a Default profile with those entries, and saves it to the profiles directory.
        /// The original legacy file is left untouched.
        /// </summary>
        /// <param name="currentFiles">The current array of .json files in the profiles directory</param>
        /// <returns>Updated files array including the newly created Default.json</returns>
        private string[] MigrateToDefaultProfile(string[] currentFiles)
        {
            List<AutoLootConfigEntry> legacyEntries = null;

            try
            {
                if (JsonHelper.Load(_migrationSourcePath, AutoLootJsonContext.Default.ListAutoLootConfigEntry, out legacyEntries))
                {
                    legacyEntries ??= new List<AutoLootConfigEntry>();
                }
                else
                {
                    Log.Error($"Failed to load legacy AutoLoot.json from '{_migrationSourcePath}', creating empty Default profile.");
                    legacyEntries = new List<AutoLootConfigEntry>();
                }
            }
            catch (Exception e)
            {
                Log.Error($"Error reading legacy AutoLoot.json from '{_migrationSourcePath}': {e.Message}");
                legacyEntries = new List<AutoLootConfigEntry>();
            }

            var defaultProfile = new AutoLootProfile
            {
                Name = "Default",
                IsActive = true,
                Entries = legacyEntries,
                FileName = "Default.json"
            };

            SaveProfile(defaultProfile);

            int entryCount = legacyEntries.Count;
            Log.Info($"Migrated {entryCount} entries from AutoLoot.json to Default profile.");
            GameActions.Print($"Migrated {entryCount} auto loot entries to Default profile.", 0x48);

            _migrationSourcePath = null;

            // currentFiles is always empty during migration (states 3/4 only trigger
            // when no .json files exist in the profiles dir), so just return the new file
            return new[] { Path.Combine(_profilesDir, defaultProfile.FileName) };
        }

        public void SaveProfile(AutoLootProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.FileName))
            {
                Log.Error($"Cannot save profile: {(profile == null ? "null profile" : "empty FileName")}");
                return;
            }

            try
            {
                if (!Directory.Exists(_profilesDir))
                    Directory.CreateDirectory(_profilesDir);

                string fullPath = Path.Combine(_profilesDir, profile.FileName);
                JsonHelper.SaveAndBackup(profile, fullPath, AutoLootJsonContext.Default.AutoLootProfile);
            }
            catch (Exception e)
            {
                Log.Error($"Error saving profile '{profile.Name}': {e.Message}");
            }
        }

        /// <summary>
        /// Rebuilds the merged entry list from all active profiles.
        /// Uses atomic reference swap to prevent ConcurrentModificationException
        /// when IsOnLootList iterates on the network thread while the UI triggers a rebuild.
        /// </summary>
        public void RebuildMergedList()
        {
            if (!_loaded)
                return;

            var newList = new List<AutoLootConfigEntry>();
            int activeCount = 0;

            foreach (AutoLootProfile profile in Profiles)
            {
                if (profile.IsActive)
                {
                    newList.AddRange(profile.Entries);
                    activeCount++;
                }
            }

            _activeProfileCount = activeCount;
            _mergedEntries = newList;
        }

        public void SaveAll()
        {
            if (!_loaded)
                return;

            foreach (AutoLootProfile profile in Profiles)
            {
                SaveProfile(profile);
            }
        }

        public AutoLootProfile CreateProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "New Profile";

            string uniqueName = GetUniqueName(name);
            string fileName = SanitizeFileName(uniqueName);

            var profile = new AutoLootProfile
            {
                Name = uniqueName,
                IsActive = true,
                Entries = new List<AutoLootConfigEntry>(),
                FileName = fileName
            };

            Profiles.Add(profile);
            SaveProfile(profile);

            return profile;
        }

        public void DeleteProfile(AutoLootProfile profile)
        {
            if (profile == null || Profiles.Count <= 1)
                return;

            if (!string.IsNullOrWhiteSpace(profile.FileName))
            {
                string basePath = Path.Combine(_profilesDir, profile.FileName);

                try
                {
                    if (File.Exists(basePath))
                        File.Delete(basePath);

                    for (int i = 1; i <= 3; i++)
                    {
                        string backupPath = basePath + ".backup" + i;
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Error deleting profile files for '{profile.Name}': {e.Message}");
                }
            }

            Profiles.Remove(profile);

            if (SelectedProfile == profile)
                SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;

            RebuildMergedList();
        }

        public void RenameProfile(AutoLootProfile profile, string newName)
        {
            if (profile == null || string.IsNullOrWhiteSpace(newName))
                return;

            string uniqueName = GetUniqueName(newName);
            string newFileName = SanitizeFileName(uniqueName, profile.FileName);

            // Delete old files from disk
            if (!string.IsNullOrWhiteSpace(profile.FileName))
            {
                string oldBasePath = Path.Combine(_profilesDir, profile.FileName);

                try
                {
                    if (File.Exists(oldBasePath))
                        File.Delete(oldBasePath);

                    for (int i = 1; i <= 3; i++)
                    {
                        string backupPath = oldBasePath + ".backup" + i;
                        if (File.Exists(backupPath))
                            File.Delete(backupPath);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Error deleting old profile files during rename of '{profile.Name}': {e.Message}");
                }
            }

            profile.Name = uniqueName;
            profile.FileName = newFileName;
            SaveProfile(profile);
        }


        public void ImportFromOtherCharacter(string characterName, List<AutoLootConfigEntry> entries)
        {
            try
            {
                if (entries != null && entries.Count > 0)
                    ImportEntries(entries, $"character: {characterName}");
                else
                    GameActions.Print($"No autoloot entries found for character: {characterName}", Constants.HUE_ERROR);
            }
            catch (Exception e)
            {
                GameActions.Print($"Error importing from other character: {e.Message}", Constants.HUE_ERROR);
            }
        }

        private void ImportEntries(List<AutoLootConfigEntry> entries, string source, AutoLootProfile targetProfile = null)
        {
            targetProfile ??= EnsureSelectedProfile();
            if (targetProfile == null)
            {
                GameActions.Print("No profile available to import into.", Constants.HUE_ERROR);
                return;
            }

            var newItems = new List<AutoLootConfigEntry>();
            int duplicateCount = 0;

            foreach (AutoLootConfigEntry importedItem in entries)
            {
                bool isDuplicate = false;
                foreach (AutoLootConfigEntry existingItem in targetProfile.Entries)
                    if (existingItem.Equals(importedItem))
                    {
                        isDuplicate = true;
                        duplicateCount++;
                        break;
                    }

                if (!isDuplicate) newItems.Add(importedItem);
            }

            if (newItems.Count > 0)
            {
                targetProfile.Entries.AddRange(newItems);
                RebuildMergedList();
                SaveProfile(targetProfile);
            }

            string message = $"Imported {newItems.Count} new autoloot entries from {source}";
            if (duplicateCount > 0) message += $" ({duplicateCount} duplicates skipped)";
            GameActions.Print(message, 0x48);
        }

        public List<AutoLootConfigEntry> LoadOtherCharacterConfig(string characterPath)
        {
            try
            {
                string configPath = Path.Combine(characterPath, "AutoLoot.json");
                if (File.Exists(configPath))
                {
                    string data = File.ReadAllText(configPath);
                    List<AutoLootConfigEntry> items = JsonSerializer.Deserialize(data, AutoLootJsonContext.Default.ListAutoLootConfigEntry);
                    return items ?? new List<AutoLootConfigEntry>();
                }
            }
            catch (Exception e)
            {
                GameActions.Print($"Error loading autoloot config from {characterPath}: {e.Message}", Constants.HUE_ERROR);
            }
            return new List<AutoLootConfigEntry>();
        }

        public Dictionary<string, List<AutoLootConfigEntry>> GetOtherCharacterConfigs()
        {
            var otherConfigs = new Dictionary<string, List<AutoLootConfigEntry>>();

            string rootpath;
            if (string.IsNullOrWhiteSpace(Settings.GlobalSettings.ProfilesPath))
                rootpath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Profiles");
            else
                rootpath = Settings.GlobalSettings.ProfilesPath;

            string currentCharacterName = ProfileManager.CurrentProfile?.CharacterName ?? "";
            Dictionary<string, string> characterPaths = Utility.Extensions.GetAllCharacterPaths(rootpath);

            foreach (KeyValuePair<string, string> kvp in characterPaths)
            {
                string characterName = kvp.Key;
                string characterPath = kvp.Value;

                if (characterPath == ProfileManager.ProfilePath)
                    continue;

                List<AutoLootConfigEntry> configs = LoadOtherCharacterConfig(characterPath);
                if (configs.Count > 0) otherConfigs[characterName] = configs;
            }

            return otherConfigs;
        }

        #nullable enable
        public string? GetProfileJsonExport(AutoLootProfile profile)
        {
            if (profile == null)
                return null;

            try
            {
                return JsonSerializer.Serialize(profile, AutoLootJsonContext.Default.AutoLootProfile);
            }
            catch (Exception e)
            {
                Log.Error($"Error exporting autoloot profile to JSON: {e}");
            }

            return null;
        }
        #nullable disable

        public AutoLootProfile ImportProfileFromClipboard(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !_loaded)
                return null;

            // Try new format: full AutoLootProfile
            try
            {
                AutoLootProfile profile = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.AutoLootProfile);
                if (profile != null && profile.Entries != null)
                    return FinalizeImportedProfile(profile);
            }
            catch (Exception e)
            {
                Log.Trace($"Clipboard is not an AutoLootProfile: {e.Message}");
            }

            // Try legacy format: List<AutoLootConfigEntry>
            try
            {
                List<AutoLootConfigEntry> entries = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.ListAutoLootConfigEntry);
                if (entries != null)
                {
                    var profile = new AutoLootProfile
                    {
                        Name = "Imported",
                        Entries = entries
                    };
                    return FinalizeImportedProfile(profile);
                }
            }
            catch (Exception e)
            {
                Log.Trace($"Clipboard is not a List<AutoLootConfigEntry>: {e.Message}");
            }

            return null;
        }

        private AutoLootProfile FinalizeImportedProfile(AutoLootProfile profile)
        {
            profile.Name = GetUniqueName(string.IsNullOrWhiteSpace(profile.Name) ? "Imported" : profile.Name);
            profile.IsActive = false;
            profile.FileName = SanitizeFileName(profile.Name);
            Profiles.Add(profile);
            RebuildMergedList();
            SaveProfile(profile);
            return profile;
        }

        public string GetUniqueName(string baseName)
        {
            string name = baseName;
            int i = 1;
            while (Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                name = $"{baseName} ({i++})";
            return name;
        }

        public string SanitizeFileName(string name, string existingFileName = null)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray());

            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "profile";

            string fileName = sanitized + ".json";

            if (!Directory.Exists(_profilesDir))
                return fileName;

            int counter = 1;
            while (counter < 10000)
            {
                string fullPath = Path.Combine(_profilesDir, fileName);
                if (!File.Exists(fullPath))
                    return fileName;

                if (existingFileName != null && string.Equals(fileName, existingFileName, StringComparison.OrdinalIgnoreCase))
                    return fileName;

                fileName = $"{sanitized}_{counter}.json";
                counter++;
            }

            return $"{sanitized}_{DateTime.Now.Ticks}.json";
        }

        public enum AutoLootPriority { Low = 0, Normal = 1, High = 2 }

        public class AutoLootConfigEntry
        {
            public string Name { get; set; } = "";
            public int Graphic { get; set; } = 0;
            public ushort Hue { get; set; } = ushort.MaxValue;
            public string RegexSearch { get; set; } = string.Empty;
            public uint DestinationContainer { get; set; } = 0;
            public AutoLootPriority Priority { get; set; } = AutoLootPriority.Normal;
            private bool RegexMatch => !string.IsNullOrEmpty(RegexSearch);
            /// <summary>
            /// Do not set this manually.
            /// </summary>
            public string Uid { get; set; } = Guid.NewGuid().ToString();

            public bool Match(Item compareTo)
            {
                if (Graphic != -1 && Graphic != compareTo.Graphic) return false;

                if (!HueCheck(compareTo.Hue)) return false;

                if (RegexMatch && !RegexCheck(compareTo.World, compareTo)) return false;

                return true;
            }

            private bool HueCheck(ushort value)
            {
                if (Hue == ushort.MaxValue) //Ignore hue.
                    return true;
                else if (Hue == value) //Hue must match, and it does
                    return true;
                else //Hue is not ignored, and does not match
                    return false;
            }

            private bool RegexCheck(World world, Item compareTo)
            {
                string search = "";
                if (world.OPL.TryGetNameAndData(compareTo, out string name, out string data))
                    search += name + data;
                else
                    search = StringHelper.GetPluralAdjustedString(compareTo.ItemData.Name);

                return RegexHelper.GetRegex(RegexSearch, RegexOptions.Multiline).IsMatch(search);
            }

            public bool Equals(AutoLootConfigEntry other) => other.Graphic == Graphic && other.Hue == Hue && RegexSearch == other.RegexSearch;
        }

        public class AutoLootProfile
        {
            public string Name { get; set; } = "";
            public bool IsActive { get; set; } = true;
            public List<AutoLootConfigEntry> Entries { get; set; } = new();

            [JsonIgnore]
            public string FileName { get; set; } = "";
        }
    }
}
