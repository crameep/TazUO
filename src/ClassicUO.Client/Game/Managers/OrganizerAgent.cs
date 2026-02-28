using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Timers;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Structs;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Utility;

namespace ClassicUO.Game.Managers
{
    internal class OrganizerAgent
    {
        public static OrganizerAgent Instance
        {
            get
            {
                if (field == null)
                    field = new();
                return field;
            }
            private set => field = value;
        }

        public List<OrganizerConfig> OrganizerConfigs { get; private set; } = new();
        public OrganizerRunState RunState { get; } = new();

        private readonly TomeActionRunner _tomeActionRunner = new();
        private int _pendingRunActions;
        private bool _activeRunGuard;

        private static string GetDataPath()
        {
            string dataPath = ProfileManager.ProfilePath;
            if (!Directory.Exists(dataPath))
                Directory.CreateDirectory(dataPath);
            return dataPath;
        }

        public static void Load()
        {
            Instance = new OrganizerAgent();
            string newPath = Path.Combine(GetDataPath(), "OrganizerConfig.json");
            string oldPath = Path.Combine(CUOEnviroment.ExecutablePath, "Data");
            if(File.Exists(oldPath))
                File.Move(oldPath, newPath);

            if (JsonHelper.Load<List<OrganizerConfig>>(newPath, OrganizerAgentContext.Default.ListOrganizerConfig, out List<OrganizerConfig> configs))
                Instance.OrganizerConfigs = configs;

            TomeManager.Load();

            if (MigrateLegacyTomes(Instance.OrganizerConfigs, TomeManager.Instance))
            {
                TomeManager.Instance.Save();
                Instance.Save();
            }
        }

        public void OrganizerCommand(string[] args)
        {
            if (args is not { Length: > 1 })
            {
                // Run all organizers
                Instance?.RunOrganizer();
                return;
            }

            if (string.Equals(args[1], "group", StringComparison.OrdinalIgnoreCase) && args.Length < 3)
            {
                if (World.Instance != null)
                    GameActions.Print(World.Instance, "Usage: -organize group <name>", Constants.HUE_ERROR);
                return;
            }

            if (TryParseGroupCommand(args, out string groupName))
            {
                Instance?.RunOrganizerGroup(groupName);
                return;
            }

            if (int.TryParse(args[1], out int index))
            {
                // Run organizer by index
                Instance?.RunOrganizer(index);
            }
            else
            {
                // Run organizer by name - join all args after command
                string name = string.Join(" ", args.Skip(1));
                Instance?.RunOrganizer(name);
            }
        }

        public void Save() => JsonHelper.SaveAndBackup(OrganizerConfigs, Path.Combine(GetDataPath(), "OrganizerConfig.json"), OrganizerAgentContext.Default.ListOrganizerConfig);

        public void Update()
        {
            _tomeActionRunner.Update();
            TryCompleteRunState();
            ReleaseRunGuardIfIdle();
        }

        public static void Unload()
        {
            Instance?.Save();
            TomeManager.Unload();
            Instance = null;
        }

        public OrganizerConfig NewOrganizerConfig()
        {
            var config = new OrganizerConfig();
            OrganizerConfigs.Add(config);
            return config;
        }

        public void DeleteConfig(OrganizerConfig config)
        {
            if (config != null)
            {
                OrganizerConfigs?.Remove(config);
            }
        }

        public OrganizerConfig DupeConfig(OrganizerConfig config)
        {
            if (config == null) return null;

            var dupedConfig = new OrganizerConfig
            {
                Name = GetUniqueName(config.Name + " Copy"),
                Group = config.Group,
                Recursive = config.Recursive,
                SourceContSerial = config.SourceContSerial,
                DestContSerial = config.DestContSerial,
                DestinationType = config.DestinationType,
                TomeDefinitionName = config.TomeDefinitionName,
                TomeSerial = config.TomeSerial,
                TomeGumpId = config.TomeGumpId,
                TomeAddButtonId = config.TomeAddButtonId,
                Enabled = false,
                ItemConfigs = config.ItemConfigs.Select(c => new OrganizerItemConfig
                {
                    Graphic = c.Graphic,
                    Hue = c.Hue,
                    Name = c.Name,
                    RegexSearch = c.RegexSearch,
                    Amount = c.Amount,
                    Enabled = c.Enabled,
                    DestContSerial = c.DestContSerial,
                    DestinationType = c.DestinationType,
                    TomeDefinitionName = c.TomeDefinitionName
                }).ToList()
            };
            OrganizerConfigs.Add(dupedConfig);
            return dupedConfig;
        }

        public int ScanContainerIntoConfig(OrganizerConfig config, uint containerSerial)
        {
            if (config == null || containerSerial == 0)
                return 0;

            Item container = World.Instance?.Items.Get(containerSerial);
            if (container == null)
                return 0;

            var existing = new HashSet<(int Graphic, ushort Hue)>(config.ItemConfigs.Select(i => (i.Graphic, i.Hue)));
            int added = 0;

            for (Item item = (Item)container.Items; item != null; item = (Item)item.Next)
            {
                (int Graphic, ushort Hue) key = (item.Graphic, item.Hue);
                if (!existing.Add(key))
                    continue;

                OrganizerItemConfig newConfig = config.NewItemConfig();
                newConfig.Graphic = item.Graphic;
                newConfig.Hue = item.Hue;
                newConfig.Enabled = true;
                newConfig.Amount = 0;

                if (World.Instance?.OPL.TryGetNameAndData(item.Serial, out string oplName, out _) == true && !string.IsNullOrWhiteSpace(oplName))
                    newConfig.Name = oplName.Trim();
                else if (!Client.UnitTestingActive)
                    newConfig.Name = StringHelper.GetPluralAdjustedString(item.ItemData.Name);
                else
                    newConfig.Name = $"0x{item.Graphic:X4}";

                added++;
            }

            return added;
        }

        private string GetUniqueName(string baseName)
        {
            string name = baseName;
            int i = 2;
            while (OrganizerConfigs.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                name = $"{baseName} ({i++})";
            return name;
        }

        public void CreateOrganizerMacroButton(string name)
        {
            var macroManager = MacroManager.TryGetMacroManager(World.Instance);

            if (macroManager == null) return;

            OrganizerConfig config = OrganizerConfigs.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (config == null) return;
            int index = OrganizerConfigs.IndexOf(config);
            var macro = new Macro($"Organizer: {config.Name}", SDL3.SDL.SDL_Keycode.SDLK_UNKNOWN, false, false, false) { Items = new MacroObjectString(MacroType.ClientCommand, MacroSubType.MSC_NONE, $"organize {index}") };

            macroManager.PushToBack(macro);
            UIManager.Add(new MacroButtonGump(World.Instance, macro, Mouse.Position.X, Mouse.Position.Y));
        }
        /// <summary>
        /// Resolves a destination serial to an Item container.
        /// Supports both item containers and pack animals (mobile → backpack).
        /// The out parameter dropSerial is the serial to use in MoveRequest (the mobile
        /// serial for pack animals, so the server routes to its backpack).
        /// </summary>
        private static Item ResolveDestination(uint serial, out uint dropSerial)
        {
            dropSerial = serial;

            // Try as item first
            Item item = World.Instance.Items.Get(serial);
            if (item != null) return item;

            // Try as mobile (pack animal) — get its backpack for item counting
            Mobile mob = World.Instance.Mobiles.Get(serial);
            if (mob != null)
            {
                Item pack = mob.Backpack;
                if (pack != null)
                {
                    // Use the mobile serial for the drop packet so the server routes it
                    dropSerial = mob.Serial;
                    return pack;
                }
            }

            return null;
        }

        public void ListOrganizers()
        {
            foreach (string line in BuildOrganizerListLines())
                GameActions.Print(World.Instance, line);
        }

        internal List<string> BuildOrganizerListLines()
        {
            var lines = new List<string>();

            if (OrganizerConfigs.Count == 0)
            {
                lines.Add("No organizers configured.");
                return lines;
            }

            lines.Add($"Available organizers ({OrganizerConfigs.Count}):");

            var grouped = OrganizerConfigs
                .Select((config, index) => new { config, index })
                .GroupBy(x => string.IsNullOrWhiteSpace(x.config.Group) ? "General" : x.config.Group.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in grouped)
            {
                lines.Add($" Group: {group.Key}");
                foreach (var entry in group.OrderBy(x => x.config.Name, StringComparer.OrdinalIgnoreCase))
                {
                    OrganizerConfig config = entry.config;
                    string status = config.Enabled ? "enabled" : "disabled";
                    int itemCount = config.ItemConfigs.Count(ic => ic.Enabled);
                    lines.Add($"  {entry.index}: '{config.Name}' ({status}, {itemCount} item types, destination: {config.DestContSerial:X})");
                }
            }

            return lines;
        }

        public int RunOrganizerGroup(string groupName)
        {
            if (!TryEnterRunGuard())
            {
                if (World.Instance != null)
                    GameActions.Print(World.Instance, "Organizer run already in progress.", Constants.HUE_ERROR);
                return 0;
            }

            if (string.IsNullOrWhiteSpace(groupName))
            {
                if (World.Instance != null)
                    GameActions.Print(World.Instance, "Group name cannot be empty.", Constants.HUE_ERROR);
                ReleaseRunGuardIfIdle();
                return 0;
            }

            List<OrganizerConfig> configs = GetEnabledConfigsByGroup(groupName).ToList();

            if (configs.Count == 0)
            {
                if (World.Instance != null)
                    GameActions.Print(World.Instance, $"No enabled organizers found in group '{groupName}'.", Constants.HUE_ERROR);
                ReleaseRunGuardIfIdle();
                return 0;
            }

            int started = 0;
            foreach (OrganizerConfig config in configs)
            {
                RunSingleOrganizer(config);
                started++;
            }

            if (started > 0)
            {
                if (World.Instance != null)
                    GameActions.Print(World.Instance, $"Started {started} organizer(s) in group '{groupName}'.", Constants.HUE_SUCCESS);
            }

            ReleaseRunGuardIfIdle();

            return started;
        }

        public IEnumerable<OrganizerConfig> GetEnabledConfigsByGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return Enumerable.Empty<OrganizerConfig>();

            string normalized = groupName.Trim();
            return OrganizerConfigs.Where(c => c.Enabled && string.Equals(NormalizeGroup(c.Group), normalized, StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeGroup(string group)
        {
            return string.IsNullOrWhiteSpace(group) ? "General" : group.Trim();
        }

        internal static bool TryParseGroupCommand(string[] args, out string groupName)
        {
            groupName = string.Empty;

            if (args == null || args.Length < 3)
                return false;

            if (!string.Equals(args[1], "group", StringComparison.OrdinalIgnoreCase))
                return false;

            groupName = string.Join(" ", args.Skip(2)).Trim();
            return groupName.Length > 0;
        }

        public void RunOrganizer()
        {
            if (!TryEnterRunGuard())
            {
                if (World.Instance != null)
                    GameActions.Print(World.Instance, "Organizer run already in progress.", Constants.HUE_ERROR);
                return;
            }

            Item backpack = World.Instance.Player?.Backpack;
            if (backpack == null)
            {
                GameActions.Print(World.Instance, "Cannot find player backpack.");
                ReleaseRunGuardIfIdle();
                return;
            }

            int totalOrganized = 0;
            foreach (OrganizerConfig config in OrganizerConfigs)
            {
                if (!config.Enabled) continue;

                Item sourceCont = config.SourceContSerial != 0
                    ? World.Instance.Items.Get(config.SourceContSerial)
                    : backpack;

                if (sourceCont == null)
                {
                    GameActions.Print(World.Instance, $"Cannot find source container for organizer '{config.Name}'.");
                    continue;
                }

                Item destCont;
                uint destDropSerial;
                if (config.DestContSerial != 0)
                {
                    destCont = ResolveDestination(config.DestContSerial, out destDropSerial);
                    if (destCont == null)
                    {
                        GameActions.Print($"Cannot find destination for organizer '{config.Name}'. Using backpack as default.");
                        destCont = backpack;
                        destDropSerial = backpack.Serial;
                    }
                }
                else
                {
                    destCont = backpack;
                    destDropSerial = backpack.Serial;
                }

                totalOrganized += OrganizeItems(sourceCont, destCont, destDropSerial, config);
            }

            if (totalOrganized == 0)
            {
                GameActions.Print(World.Instance, "No items were organized.", 33);
            }

            ReleaseRunGuardIfIdle();
        }

        public void RunOrganizer(string name, uint source = 0, uint dest = 0)
        {
            if (!TryEnterRunGuard())
            {
                if (World.Instance != null)
                    GameActions.Print(World.Instance, "Organizer run already in progress.", Constants.HUE_ERROR);
                return;
            }

            OrganizerConfig config = OrganizerConfigs.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (config == null)
            {
                GameActions.Print(World.Instance, $"Organizer '{name}' not found.", 33);
                ReleaseRunGuardIfIdle();
                return;
            }

            RunSingleOrganizer(config, source, dest);
            ReleaseRunGuardIfIdle();
        }

        public void RunOrganizer(int index)
        {
            if (!TryEnterRunGuard())
            {
                if (World.Instance != null)
                    GameActions.Print(World.Instance, "Organizer run already in progress.", Constants.HUE_ERROR);
                return;
            }

            if (index < 0 || index >= OrganizerConfigs.Count)
            {
                GameActions.Print(World.Instance, $"Organizer index {index} is out of range. Available organizers: 0-{OrganizerConfigs.Count - 1}", 33);
                ReleaseRunGuardIfIdle();
                return;
            }

            OrganizerConfig config = OrganizerConfigs[index];
            RunSingleOrganizer(config);
            ReleaseRunGuardIfIdle();
        }

        private int OrganizeItems(Item sourceCont, Item destCont, uint destDropSerial, OrganizerConfig config)
        {
            Item backpack = World.Instance.Player?.Backpack;
            if (backpack == null)
                return 0;

            var itemsToMoveByDestination = new Dictionary<uint, List<(Item Item, ushort Amount, OrganizerItemConfig Config)>>();
            var itemsToTomeByName = new Dictionary<string, List<Item>>(StringComparer.OrdinalIgnoreCase);
            int skippedItems = 0;

            // First pass: identify items to move and group by destination
            foreach (Item item in EnumerateSourceItems(sourceCont, config.Recursive, destDropSerial))
            {
                foreach (OrganizerItemConfig itemConfig in config.ItemConfigs)
                {
                    if (itemConfig.Enabled && itemConfig.IsMatch(World.Instance, item))
                    {
                        ResolvedDestination destination = ResolveDestination(config, itemConfig, destDropSerial);
                        if (destination.Type == DestType.Tome)
                        {
                            if (string.IsNullOrWhiteSpace(destination.TomeDefinitionName) || TomeManager.Instance == null)
                            {
                                skippedItems++;
                                break;
                            }

                            TomeDefinition tome = TomeManager.Instance.TomeDefinitions.FirstOrDefault(
                                t => string.Equals(t.Name, destination.TomeDefinitionName, StringComparison.OrdinalIgnoreCase)
                            );

                            if (tome == null)
                            {
                                skippedItems++;
                                break;
                            }

                            if (!itemsToTomeByName.TryGetValue(tome.Name, out List<Item> tomeItems))
                            {
                                tomeItems = new List<Item>();
                                itemsToTomeByName[tome.Name] = tomeItems;
                            }

                            tomeItems.Add(item);
                            break;
                        }

                        uint itemDestSerial = destination.ContainerSerial;

                        if (!itemsToMoveByDestination.ContainsKey(itemDestSerial))
                            itemsToMoveByDestination[itemDestSerial] = new List<(Item, ushort, OrganizerItemConfig)>();

                        itemsToMoveByDestination[itemDestSerial].Add((item, 0, itemConfig));
                        break; // Avoid processing the same item multiple times
                    }
                }
            }

            int totalItemsMoved = 0;
            int totalPlanned = skippedItems;

            // Second pass: process each destination group
            foreach (KeyValuePair<uint, List<(Item Item, ushort Amount, OrganizerItemConfig Config)>> kvp in itemsToMoveByDestination)
            {
                uint destinationSerial = kvp.Key;
                List<(Item Item, ushort Amount, OrganizerItemConfig Config)> itemsForThisDest = kvp.Value;

                // Resolve the destination — supports both containers and pack animals
                Item thisDestCont = ResolveDestination(destinationSerial, out uint thisDropSerial);
                if (thisDestCont == null)
                {
                    GameActions.Print($"Cannot find destination {destinationSerial:X}. Using backpack as default.");
                    thisDestCont = backpack;
                    thisDropSerial = backpack?.Serial ?? 0;
                    if (thisDestCont == null)
                    {
                        skippedItems += itemsForThisDest.Count;
                        continue;
                    }
                }

                var destItems = (Item)thisDestCont.Items;
                bool sameContainer = sourceCont.Serial == thisDestCont.Serial;

                if (sameContainer)
                {
                    // Same container logic
                    foreach ((Item item, ushort _, OrganizerItemConfig itemConfig) in itemsForThisDest)
                    {
                        if (!item.ItemData.IsStackable) continue; // non-stackable items can't be organized in the same container

                        ushort amountToMove = itemConfig.Amount > 0 ? itemConfig.Amount : ushort.MaxValue;
                        EnqueueMoveRequest(new MoveRequest(item.Serial, thisDropSerial, amountToMove));
                        totalItemsMoved++;
                        totalPlanned++;
                    }
                }
                else
                {
                    // Build a lookup of existing item counts in the destination container
                    var destItemCounts = new Dictionary<(ushort Graphic, ushort Hue), int>();
                    for (Item item = destItems; item != null; item = (Item)item.Next)
                    {
                        (ushort Graphic, ushort Hue) key = (item.Graphic, item.Hue);
                        if (destItemCounts.ContainsKey(key))
                            destItemCounts[key] += item.Amount;
                        else
                            destItemCounts[key] = item.Amount;
                    }

                    // Determine which items to move based on config and existing counts in destination
                    foreach ((Item item, ushort _, OrganizerItemConfig itemConfig) in itemsForThisDest)
                    {
                        if (itemConfig.Amount == 0)
                        {
                            // Move all items of this type
                            EnqueueMoveRequest(new MoveRequest(item.Serial, thisDropSerial, ushort.MaxValue));
                            totalItemsMoved++;
                            totalPlanned++;
                        }
                        else
                        {
                            // Move up to the configured amount, considering existing items in destination
                            destItemCounts.TryGetValue((item.Graphic, item.Hue), out int existingCount);
                            int toMove = itemConfig.Amount - existingCount;
                            if (toMove > 0)
                            {
                                ushort actualAmount = (ushort)Math.Min(toMove, item.Amount);
                                EnqueueMoveRequest(new MoveRequest(item.Serial, thisDropSerial, actualAmount));
                                // Update the count to avoid over-moving if multiple stacks exist in source
                                destItemCounts[(item.Graphic, item.Hue)] = existingCount + actualAmount;
                                totalItemsMoved++;
                                totalPlanned++;
                            }
                        }
                    }
                }
            }

            foreach ((string tomeName, List<Item> matchedItems) in itemsToTomeByName)
            {
                TomeDefinition tome = TomeManager.Instance?.TomeDefinitions.FirstOrDefault(
                    t => string.Equals(t.Name, tomeName, StringComparison.OrdinalIgnoreCase)
                );

                if (tome == null)
                {
                    skippedItems += matchedItems.Count;
                    continue;
                }

                List<uint> serials = matchedItems.Select(i => i.Serial).Where(s => s != 0).Distinct().ToList();
                if (serials.Count == 0)
                    continue;

                totalPlanned += serials.Count;
                _pendingRunActions += serials.Count;

                _tomeActionRunner.Enqueue(
                    new TomeOperation
                    {
                        Definition = tome,
                        ItemSerials = serials,
                        OnFinished = result =>
                        {
                            RunState.ItemsMoved += result.Moved;
                            RunState.ItemsSkipped += result.Skipped;
                            _pendingRunActions = Math.Max(0, _pendingRunActions - (result.Moved + result.Skipped));

                            if (!result.Success && !string.IsNullOrWhiteSpace(result.Reason))
                                GameActions.Print(World.Instance, $"Tome operation failed: {result.Reason}", Constants.HUE_ERROR);

                            TryCompleteRunState();
                        }
                    }
                );
            }

            if (totalPlanned > 0)
                StartRunState(config.Name, totalPlanned, skippedItems);

            if (skippedItems > 0)
                GameActions.Print(World.Instance, $"Organizer '{config.Name}' skipped {skippedItems} items.", Constants.HUE_ERROR);

            if (totalPlanned > 0)
            {
                GameActions.Print($"Organizing {totalPlanned} items from '{config.Name}'...", Constants.HUE_SUCCESS);
            }

            return totalPlanned;
        }

        internal static IEnumerable<Item> EnumerateSourceItems(Item sourceCont, bool recursive, uint skipContainerSerial)
        {
            if (sourceCont == null)
                yield break;

            if (!recursive)
            {
                for (Item item = (Item)sourceCont.Items; item != null; item = (Item)item.Next)
                    yield return item;

                yield break;
            }

            var stack = new Stack<Item>();
            var visitedContainers = new HashSet<uint> { sourceCont.Serial };

            for (Item item = (Item)sourceCont.Items; item != null; item = (Item)item.Next)
                stack.Push(item);

            while (stack.Count > 0)
            {
                Item item = stack.Pop();
                if (item == null)
                    continue;

                yield return item;

                if (item.Serial == skipContainerSerial)
                    continue;

                if (item.Items == null)
                    continue;

                if (!visitedContainers.Add(item.Serial))
                    continue;

                for (Item nested = (Item)item.Items; nested != null; nested = (Item)nested.Next)
                    stack.Push(nested);
            }
        }

        internal static bool MigrateLegacyTomes(List<OrganizerConfig> configs, TomeManager tomeManager)
        {
            if (configs == null || tomeManager == null)
                return false;

            bool migratedAny = false;

            foreach (OrganizerConfig config in configs)
            {
                if (config == null || config.TomeSerial == 0)
                    continue;

                TomeDefinition definition = tomeManager.GetOrCreateFromLegacy(config.TomeSerial, config.TomeGumpId, config.TomeAddButtonId);

                config.DestinationType = DestType.Tome;
                config.TomeDefinitionName = definition.Name;
                config.TomeSerial = 0;
                config.TomeGumpId = 0;
                config.TomeAddButtonId = 0;
                migratedAny = true;
            }

            return migratedAny;
        }

        private void RunSingleOrganizer(OrganizerConfig config, uint source = 0, uint dest = 0)
        {
            if (!config.Enabled)
            {
                GameActions.Print(World.Instance, $"Organizer '{config.Name}' is disabled.", Constants.HUE_ERROR);
                return;
            }

            Item backpack = World.Instance.Player?.Backpack;
            if (backpack == null)
            {
                GameActions.Print(World.Instance, "Cannot find player backpack.");
                return;
            }

            Item sourceCont = source != 0
                ? World.Instance.Items.Get(source)
                : config.SourceContSerial != 0
                    ? World.Instance.Items.Get(config.SourceContSerial)
                    : backpack;

            if (sourceCont == null)
            {
                GameActions.Print($"Cannot find source container for organizer '{config.Name}'.");
                return;
            }

            uint destSerial = dest != 0 ? dest : config.DestContSerial;
            Item destCont;
            uint destDropSerial;
            if (destSerial != 0)
            {
                destCont = ResolveDestination(destSerial, out destDropSerial);
                if (destCont == null)
                {
                    GameActions.Print(World.Instance, $"Cannot find destination for organizer '{config.Name}' (Serial: {config.DestContSerial:X})", Constants.HUE_ERROR);
                    return;
                }
            }
            else
            {
                destCont = backpack;
                destDropSerial = backpack.Serial;
            }

            int organized = OrganizeItems(sourceCont, destCont, destDropSerial, config);
            if (organized == 0)
            {
                GameActions.Print(World.Instance, $"No items were organized by '{config.Name}'.", Constants.HUE_ERROR);
            }
        }

        private void EnqueueMoveRequest(MoveRequest moveRequest)
        {
            _pendingRunActions++;
            ObjectActionQueue.Instance.Enqueue(
                new ObjectActionQueueItem(
                    moveRequest.Execute,
                    _ =>
                    {
                        RunState.ItemsMoved++;
                        _pendingRunActions = Math.Max(0, _pendingRunActions - 1);
                        TryCompleteRunState();
                    }
                ),
                ActionPriority.MoveItem
            );
        }

        private void StartRunState(string configName, int totalItems, int skipped)
        {
            if (!RunState.IsRunning)
            {
                RunState.ConfigName = configName;
                RunState.TotalItems = totalItems;
                RunState.ItemsMoved = 0;
                RunState.ItemsSkipped = skipped;
                RunState.StartTime = DateTime.UtcNow;
                RunState.IsRunning = true;
            }
            else
            {
                RunState.TotalItems += totalItems;
                RunState.ItemsSkipped += skipped;

                if (!string.Equals(RunState.ConfigName, configName, StringComparison.OrdinalIgnoreCase))
                    RunState.ConfigName = "Multiple Organizers";
            }

            TryCompleteRunState();
        }

        private void TryCompleteRunState()
        {
            if (!RunState.IsRunning || _pendingRunActions > 0)
                return;

            RunState.IsRunning = false;
            _activeRunGuard = false;
            GameActions.Print(
                World.Instance,
                $"Organizer '{RunState.ConfigName}' complete: moved {RunState.ItemsMoved} items ({RunState.ItemsSkipped} skipped).",
                Constants.HUE_SUCCESS
            );
        }

        private bool TryEnterRunGuard()
        {
            if (IsRunInProgress())
                return false;

            _activeRunGuard = true;
            return true;
        }

        private bool IsRunInProgress() => _activeRunGuard || RunState.IsRunning || _pendingRunActions > 0;

        private void ReleaseRunGuardIfIdle()
        {
            if (_pendingRunActions <= 0 && !RunState.IsRunning)
                _activeRunGuard = false;
        }

        internal bool TryEnterRunGuardForTests() => TryEnterRunGuard();
        internal void ReleaseRunGuardIfIdleForTests() => ReleaseRunGuardIfIdle();
        internal bool IsRunInProgressForTests() => IsRunInProgress();
        internal void ForceStartRunStateForTests(string configName, int totalItems, int skipped) => StartRunState(configName, totalItems, skipped);
        internal void ForceSetPendingRunActionsForTests(int pendingCount) => _pendingRunActions = Math.Max(0, pendingCount);
        internal void ForceTryCompleteRunStateForTests() => TryCompleteRunState();
        internal void ResetRunTrackingForTests()
        {
            _activeRunGuard = false;
            _pendingRunActions = 0;
            RunState.ConfigName = string.Empty;
            RunState.TotalItems = 0;
            RunState.ItemsMoved = 0;
            RunState.ItemsSkipped = 0;
            RunState.StartTime = default;
            RunState.IsRunning = false;
        }

        internal static ResolvedDestination ResolveDestination(OrganizerConfig config, OrganizerItemConfig itemConfig, uint defaultContainerSerial)
        {
            if (itemConfig.DestinationType == DestType.Tome)
                return ResolvedDestination.ForTome(itemConfig.TomeDefinitionName);

            if (itemConfig.DestinationType == DestType.Container)
                return ResolvedDestination.ForContainer(itemConfig.DestContSerial != 0 ? itemConfig.DestContSerial : defaultContainerSerial);

            if (itemConfig.DestContSerial != 0)
                return ResolvedDestination.ForContainer(itemConfig.DestContSerial);

            if (config.DestinationType == DestType.Tome)
                return ResolvedDestination.ForTome(config.TomeDefinitionName);

            if (config.DestinationType == DestType.Container)
                return ResolvedDestination.ForContainer(config.DestContSerial != 0 ? config.DestContSerial : defaultContainerSerial);

            uint fallback = config.DestContSerial != 0 ? config.DestContSerial : defaultContainerSerial;
            return ResolvedDestination.ForContainer(fallback);
        }

        #nullable enable
        public string? GetJsonExport(OrganizerConfig config)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(config, OrganizerAgentContext.Default.OrganizerConfig);
            }
            catch (Exception e)
            {
                Utility.Logging.Log.Error($"Error exporting organizer to JSON: {e}");
            }

            return null;
        }
        #nullable disable

        public bool ImportFromJson(string json)
        {
            try
            {
                OrganizerConfig importedConfig = System.Text.Json.JsonSerializer.Deserialize(json, OrganizerAgentContext.Default.OrganizerConfig);

                if (importedConfig != null)
                {
                    importedConfig.Name = GetUniqueName(importedConfig.Name);
                    importedConfig.Enabled = false;
                    OrganizerConfigs.Add(importedConfig);
                    GameActions.Print($"Imported organizer '{importedConfig.Name}' with {importedConfig.ItemConfigs.Count} items!", Constants.HUE_SUCCESS);
                    return true;
                }
            }
            catch (Exception e)
            {
                Utility.Logging.Log.Error($"Error importing organizer from JSON: {e}");
            }

            return false;
        }

    }

    [JsonSerializable(typeof(List<OrganizerConfig>))]
    [JsonSerializable(typeof(OrganizerConfig))]
    [JsonSerializable(typeof(DestType))]
    [JsonSerializable(typeof(OrganizerRunState))]
    [JsonSerializable(typeof(List<TomeDefinition>))]
    [JsonSerializable(typeof(TomeDefinition))]
    [JsonSerializable(typeof(TomeMode))]
    internal partial class OrganizerAgentContext : JsonSerializerContext
    { }

    internal class OrganizerConfig
    {
        public string Name { get; set; } = "Organizer";
        public string Group { get; set; } = string.Empty;
        public bool Recursive { get; set; }
        public uint SourceContSerial { get; set; }
        public uint DestContSerial { get; set; }
        public DestType DestinationType { get; set; } = DestType.ConfigDefault;
        public string TomeDefinitionName { get; set; } = string.Empty;

        // Legacy tome fields (migrated into TomeDefinitions).
        public uint TomeSerial { get; set; }
        public uint TomeGumpId { get; set; }
        public int TomeAddButtonId { get; set; }
        public bool Enabled { get; set; } = true;
        public List<OrganizerItemConfig> ItemConfigs { get; set; } = new List<OrganizerItemConfig>();

        public OrganizerItemConfig NewItemConfig()
        {
            var config = new OrganizerItemConfig();
            ItemConfigs.Add(config);
            return config;
        }

        public void DeleteItemConfig(OrganizerItemConfig config)
        {
            if (config != null)
            {
                ItemConfigs?.Remove(config);
            }
        }
    }

    internal class OrganizerItemConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Graphic { get; set; }
        public ushort Hue { get; set; } = ushort.MaxValue;
        public string RegexSearch { get; set; } = string.Empty;
        public ushort Amount { get; set; } = 0; // 0 = move all; otherwise move up to this amount
        public bool Enabled { get; set; } = true;
        public DestType DestinationType { get; set; } = DestType.ConfigDefault;
        public uint DestContSerial { get; set; } = 0; // 0 = use configuration's destination
        public string TomeDefinitionName { get; set; } = string.Empty;

        public bool IsMatch(int graphic, ushort hue, string searchText = "")
        {
            if (Graphic != -1 && Graphic != graphic)
                return false;

            if (Hue != ushort.MaxValue && hue != Hue)
                return false;

            if (string.IsNullOrWhiteSpace(RegexSearch))
                return true;

            return RegexHelper.GetRegex(RegexSearch, RegexOptions.Multiline).IsMatch(searchText ?? string.Empty);
        }

        public bool IsMatch(World world, Item compareTo)
        {
            if (compareTo == null)
                return false;

            if (string.IsNullOrWhiteSpace(RegexSearch))
                return IsMatch(compareTo.Graphic, compareTo.Hue);

            string search = string.Empty;
            if (world?.OPL.TryGetNameAndData(compareTo.Serial, out string name, out string data) == true)
                search = name + data;
            else if (!Client.UnitTestingActive)
                search = StringHelper.GetPluralAdjustedString(compareTo.ItemData.Name);

            return IsMatch(compareTo.Graphic, compareTo.Hue, search);
        }
    }

    internal enum DestType
    {
        ConfigDefault = 0,
        Container = 1,
        Tome = 2
    }

    internal sealed class OrganizerRunState
    {
        public string ConfigName { get; set; } = string.Empty;
        public int TotalItems { get; set; }
        public int ItemsMoved { get; set; }
        public int ItemsSkipped { get; set; }
        public DateTime StartTime { get; set; }
        public bool IsRunning { get; set; }
    }

    internal readonly struct ResolvedDestination
    {
        private ResolvedDestination(DestType type, uint containerSerial, string tomeDefinitionName)
        {
            Type = type;
            ContainerSerial = containerSerial;
            TomeDefinitionName = tomeDefinitionName ?? string.Empty;
        }

        public DestType Type { get; }
        public uint ContainerSerial { get; }
        public string TomeDefinitionName { get; }

        public static ResolvedDestination ForContainer(uint serial) => new(DestType.Container, serial, string.Empty);
        public static ResolvedDestination ForTome(string name) => new(DestType.Tome, 0, name);
    }
}
