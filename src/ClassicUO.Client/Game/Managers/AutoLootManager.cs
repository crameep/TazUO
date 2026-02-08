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
using System.Threading.Tasks;
using ClassicUO.Game.Managers.Structs;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    [JsonSerializable(typeof(AutoLootManager.AutoLootConfigEntry))]
    [JsonSerializable(typeof(List<AutoLootManager.AutoLootConfigEntry>))]
    [JsonSerializable(typeof(AutoLootManager.AutoLootPriority))]
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
        public List<AutoLootConfigEntry> AutoLootList { get => _autoLootItems; set { _autoLootItems = value; RebuildGraphicIndex(); } }

        private const int SCAVENGER_TRACKING_RADIUS = 20;

        private readonly HashSet<uint> _quickContainsLookup = new ();
        private readonly HashSet<uint> _recentlyLooted = new();
        internal readonly HashSet<uint> _nearbyGroundItems = new();
        private static readonly PriorityQueue<(uint item, AutoLootConfigEntry entry), int> _lootItems = new ();
        internal List<AutoLootConfigEntry> _autoLootItems = new ();
        internal Dictionary<int, List<AutoLootConfigEntry>> _graphicIndex = new();
        internal List<AutoLootConfigEntry> _wildcardEntries = new();
        internal readonly Dictionary<uint, AutoLootConfigEntry> _matchCache = new();
        internal readonly HashSet<uint> _matchCacheHasOpl = new();
        internal bool _loaded = false;
        private readonly string _savePath;
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
        }

        /// <summary>
        /// Internal constructor for unit testing. Does not initialize world or save path.
        /// </summary>
        internal AutoLootManager(bool testMode)
        {
        }

        /// <summary>
        /// Internal constructor for unit testing with a World instance.
        /// </summary>
        internal AutoLootManager(World world)
        {
            _world = world;
        }

        public void NotifyEntryChanged() => RebuildGraphicIndex();

        /// <summary>
        /// Lighter-weight notification for changes that don't affect graphic bucketing
        /// (e.g. hue or regex changes). Only clears the match cache so items are re-evaluated.
        /// </summary>
        public void NotifyMatchCriteriaChanged() => ClearMatchCache();

        public bool IsBeingLooted(uint serial) => _quickContainsLookup.Contains(serial);

        public void LootItem(uint serial)
        {
            Item item = _world.Items.Get(serial);
            if (item != null) LootItem(item, null);
        }

        public void LootItem(Item item, AutoLootConfigEntry entry = null)
        {
            if (item == null || !_recentlyLooted.Add(item.Serial) || !_quickContainsLookup.Add(item.Serial)) return;

            int pri = entry != null ? -(int)entry.Priority : -(int)AutoLootPriority.Normal;
            _lootItems.Enqueue((item.Serial, entry), pri);
            _currentLootTotalCount++;
            _nextClearRecents = Time.Ticks + 5000;
        }

        public void ForceLootContainer(uint serial)
        {
            Item cont = _world.Items.Get(serial);

            if (cont == null) return;

            for (LinkedObject i = cont.Items; i != null; i = i.Next) CheckAndLoot((Item)i);
        }

        internal static bool IsTrackableGroundItem(Item item)
        {
            return item != null && item.OnGround && !item.IsCorpse && !item.IsLocked;
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
        /// </summary>
        /// <param name="i">The item to check the loot list against</param>
        /// <returns>The matched AutoLootConfigEntry, or null if no match found</returns>
        internal AutoLootConfigEntry IsOnLootList(Item i)
        {
            if (!_loaded) return null;

            bool oplChecked = false;
            bool oplAvailable = false;

            // Check cache first
            if (_matchCache.TryGetValue(i.Serial, out AutoLootConfigEntry cached))
            {
                if (cached != null)
                    return cached; // Positive cache hit — already matched

                // Negative cache hit — check for stale OPL state
                oplAvailable = _world.OPL.TryGetNameAndData(i.Serial, out _, out _);
                oplChecked = true;
                bool hadOplWhenCached = _matchCacheHasOpl.Contains(i.Serial);

                if (!oplAvailable || hadOplWhenCached)
                    return null; // Valid negative cache

                // OPL is now available but wasn't when we cached — stale, fall through to re-evaluate
            }

            // Cache miss or stale negative cache — run matching logic
            if (!oplChecked)
                oplAvailable = _world.OPL.TryGetNameAndData(i.Serial, out _, out _);
            AutoLootConfigEntry result = null;

            if (_graphicIndex.TryGetValue(i.Graphic, out List<AutoLootConfigEntry> entries))
                foreach (AutoLootConfigEntry entry in entries)
                    if (entry.Match(i))
                    {
                        if (result == null || entry.Priority > result.Priority)
                            result = entry;
                        if (result.Priority == AutoLootPriority.High)
                            break;
                    }

            if (result == null || result.Priority != AutoLootPriority.High)
                foreach (AutoLootConfigEntry entry in _wildcardEntries)
                    if (entry.Match(i))
                    {
                        if (result == null || entry.Priority > result.Priority)
                            result = entry;
                        if (result.Priority == AutoLootPriority.High)
                            break;
                    }

            // Store result in cache
            _matchCache[i.Serial] = result;

            if (oplAvailable)
                _matchCacheHasOpl.Add(i.Serial);
            else
                _matchCacheHasOpl.Remove(i.Serial);

            return result;
        }

        /// <summary>
        /// Add an entry for auto looting to match against when opening corpses.
        /// </summary>
        /// <param name="graphic"></param>
        /// <param name="hue"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public AutoLootConfigEntry AddAutoLootEntry(ushort graphic = 0, ushort hue = ushort.MaxValue, string name = "")
        {
            var item = new AutoLootConfigEntry() { Graphic = graphic, Hue = hue, Name = name };

            foreach (AutoLootConfigEntry entry in _autoLootItems)
                if (entry.Equals(item))
                    return entry;

            _autoLootItems.Add(item);
            RebuildGraphicIndex();

            return item;
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
            int removeAt = -1;

            for (int i = 0; i < _autoLootItems.Count; i++)
                if (_autoLootItems[i].Uid == uid)
                    removeAt = i;

            if (removeAt > -1)
            {
                _autoLootItems.RemoveAt(removeAt);
                RebuildGraphicIndex();
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
            Load();
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
            _nearbyGroundItems.Clear();
            ClearMatchCache();
            Save();
            Instance = null;
        }

        /// <summary>
        /// Invoked whenever the player changes position.
        ///
        /// The other looter entry points are item update events but those are not enough;
        /// If the player opens a corpse and walks away a few steps, there wouldn't be any new events firing.
        ///
        /// This handler effectively allows re-triggering as soon as the corpses are back in range.
        /// </summary>
        internal void OnPositionChanged(object sender, PositionChangedArgs e)
        {
            if (!_loaded) return;

            if (ProfileManager.CurrentProfile.EnableScavenger)
            {
                if (_nearbyGroundItems.Count == 0)
                {
                    BootstrapNearbyGroundItems();
                    return;
                }

                List<uint> toRemove = null;
                List<Item> toLoot = null;

                // Snapshot to avoid collection-modified-during-enumeration if CheckAndLoot
                // triggers events that call OnItemCreatedOrUpdated (which modifies _nearbyGroundItems).
                uint[] snapshot = new uint[_nearbyGroundItems.Count];
                _nearbyGroundItems.CopyTo(snapshot);

                foreach (uint serial in snapshot)
                {
                    Item item = _world.Items.Get(serial);

                    if (item == null || !item.OnGround || item.IsLocked || item.IsCorpse)
                    {
                        (toRemove ??= new List<uint>()).Add(serial);
                        continue;
                    }

                    if (item.Distance >= SCAVENGER_TRACKING_RADIUS)
                    {
                        (toRemove ??= new List<uint>()).Add(serial);
                        continue;
                    }

                    if (item.Distance < ProfileManager.CurrentProfile.AutoOpenCorpseRange)
                        (toLoot ??= new List<Item>()).Add(item);
                }

                if (toRemove != null)
                    foreach (uint serial in toRemove)
                        _nearbyGroundItems.Remove(serial);

                // Loot after iteration is complete to avoid re-entrancy issues
                if (toLoot != null)
                    foreach (Item item in toLoot)
                        CheckAndLoot(item);
            }

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
                _matchCache.Remove(i.Serial);
                _matchCacheHasOpl.Remove(i.Serial);

                CheckCorpse(i);

                // Maintain spatial tracking set for scavenger.
                // Items within range are added to the set and will be checked for looting
                // during OnPositionChanged, so no immediate CheckAndLoot is needed here.
                if (ProfileManager.CurrentProfile.EnableScavenger)
                {
                    if (IsTrackableGroundItem(i) && i.Distance <= SCAVENGER_TRACKING_RADIUS)
                    {
                        _nearbyGroundItems.Add(i.Serial);
                    }
                    else
                    {
                        _nearbyGroundItems.Remove(i.Serial);
                    }
                }
            }
        }

        internal void BootstrapNearbyGroundItems()
        {
            _nearbyGroundItems.Clear();

            foreach (Item item in _world.Items.Values)
            {
                if (IsTrackableGroundItem(item) && item.Distance <= SCAVENGER_TRACKING_RADIUS)
                    _nearbyGroundItems.Add(item.Serial);
            }
        }

        private void OnOPLReceived(object sender, OPLEventArgs e)
        {
            if (!_loaded || !IsEnabled) return;
            _matchCache.Remove(e.Serial);
            _matchCacheHasOpl.Remove(e.Serial);
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
                    ClearMatchCache();
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
                ObjectActionQueue.Instance.Enqueue(new MoveRequest(moveItem.Serial, destinationSerial, moveItem.Amount).ToObjectActionQueueItem(), ActionPriority.LootItem);
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

        private void Load()
        {
            if (_loaded) return;

            Task.Factory.StartNew(() =>
            {
                string oldPath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Profiles", "AutoLoot.json");
                if(File.Exists(oldPath))
                    File.Move(oldPath, _savePath);

                List<AutoLootConfigEntry> loadedItems = null;

                if (!File.Exists(_savePath))
                {
                    loadedItems = new List<AutoLootConfigEntry>();
                    Log.Error("Auto loot save path not found, creating new..");
                }
                else
                {
                    Log.Info($"Loading: {_savePath}");
                    try
                    {
                        JsonHelper.Load(_savePath, AutoLootJsonContext.Default.ListAutoLootConfigEntry, out loadedItems);

                        if (loadedItems == null)
                        {
                            Log.Error("There was an error loading your auto loot config file, defaulted to no configs.");
                            loadedItems = new();
                        }
                    }
                    catch
                    {
                        Log.Error("There was an error loading your auto loot config file, please check it with a json validator.");
                        return; // Don't set _loaded, leave as false
                    }
                }

                // Assign loaded data and build index in a single batch to avoid
                // partial state visible to the game thread.
                _autoLootItems = loadedItems;
                RebuildGraphicIndex();
                _loaded = true;
            });
        }

        public void Save()
        {
            if (_loaded)
                try
                {
                    JsonHelper.SaveAndBackup(_autoLootItems, _savePath, AutoLootJsonContext.Default.ListAutoLootConfigEntry);
                }
                catch (Exception e) { Console.WriteLine(e.ToString()); }
        }

        internal void ClearMatchCache()
        {
            _matchCache.Clear();
            _matchCacheHasOpl.Clear();
        }

        public void RebuildGraphicIndex()
        {
            _graphicIndex.Clear();
            _wildcardEntries.Clear();
            ClearMatchCache();

            foreach (AutoLootConfigEntry entry in _autoLootItems)
            {
                if (entry.Graphic == -1)
                {
                    _wildcardEntries.Add(entry);
                }
                else
                {
                    if (!_graphicIndex.TryGetValue(entry.Graphic, out List<AutoLootConfigEntry> bucket))
                    {
                        bucket = new List<AutoLootConfigEntry>();
                        _graphicIndex[entry.Graphic] = bucket;
                    }
                    bucket.Add(entry);
                }
            }
        }

        public void ExportToFile(string filePath)
        {
            try
            {
                string fileData = JsonSerializer.Serialize(_autoLootItems, AutoLootJsonContext.Default.ListAutoLootConfigEntry);
                File.WriteAllText(filePath, fileData);
                GameActions.Print($"Autoloot configuration exported to: {filePath}", 0x48);
            }
            catch (Exception e)
            {
                GameActions.Print($"Error exporting autoloot configuration: {e.Message}", Constants.HUE_ERROR);
            }
        }

        public void ImportFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    GameActions.Print($"File not found: {filePath}", Constants.HUE_ERROR);
                    return;
                }

                string data = File.ReadAllText(filePath);
                List<AutoLootConfigEntry> importedItems = JsonSerializer.Deserialize(data, AutoLootJsonContext.Default.ListAutoLootConfigEntry);

                if (importedItems != null) ImportEntries(importedItems, $"file: {filePath}");
            }
            catch (Exception e)
            {
                GameActions.Print($"Error importing autoloot configuration: {e.Message}", Constants.HUE_ERROR);
            }
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

        private void ImportEntries(List<AutoLootConfigEntry> entries, string source)
        {
            var newItems = new List<AutoLootConfigEntry>();
            int duplicateCount = 0;

            foreach (AutoLootConfigEntry importedItem in entries)
            {
                bool isDuplicate = false;
                foreach (AutoLootConfigEntry existingItem in _autoLootItems)
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
                _autoLootItems.AddRange(newItems);
                RebuildGraphicIndex();
                Save();
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
        public string? GetJsonExport()
        {
            try
            {
                return JsonSerializer.Serialize(_autoLootItems, AutoLootJsonContext.Default.ListAutoLootConfigEntry);
            }
            catch (Exception e)
            {
                Log.Error($"Error exporting autoloot to JSON: {e}");
            }

            return null;
        }
        #nullable disable

        public bool ImportFromJson(string json)
        {
            try
            {
                List<AutoLootConfigEntry> importedItems = JsonSerializer.Deserialize(json, AutoLootJsonContext.Default.ListAutoLootConfigEntry);

                if (importedItems != null)
                {
                    ImportEntries(importedItems, "clipboard");
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error($"Error importing autoloot from JSON: {e}");
            }

            return false;
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
                else if (!Client.UnitTestingActive)
                    search = StringHelper.GetPluralAdjustedString(compareTo.ItemData.Name);

                return RegexHelper.GetRegex(RegexSearch, RegexOptions.Multiline).IsMatch(search);
            }

            public bool Equals(AutoLootConfigEntry other) => other.Graphic == Graphic && other.Hue == Hue && RegexSearch == other.RegexSearch;
        }
    }
}
