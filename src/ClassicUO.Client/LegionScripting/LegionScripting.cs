using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Managers;
using ClassicUO.Network;
using ClassicUO.Utility.Logging;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClassicUO.Game.UI;
using ClassicUO.Game.UI.ImGuiControls.Legion;
using ClassicUO.LegionScripting.Runtime;
using ClassicUO.LegionScripting.Runtime.Host;
using ClassicUO.LegionScripting.PyClasses;
using ClassicUO.Utility;
using Microsoft.Scripting;

namespace ClassicUO.LegionScripting
{
    [JsonSerializable(typeof(LScriptSettings))]
    public partial class LScriptJsonContext : JsonSerializerContext
    {
    }

    internal static class LegionScripting
    {
        public static string ScriptPath;
        public static LScriptSettings LScriptSettings { get; private set; }
        public static readonly List<ScriptFile> LoadedScripts = [];
        public static List<ScriptFile> RunningScripts { get; } = [];
        public static readonly Dictionary<int, ScriptFile> PyThreads = new();

        private static bool _enabled, _loaded;
        private static World _world;
        private static ScriptRuntimeManager _runtime = new();
        private static LegacyScriptRuntimeAdapter _legacyAdapter = new(new ScriptRuntimeManager());
        private static ScriptTickMetrics _lastRuntimeMetrics = new() { Tick = 0 };
        private static RuntimeHostServices _host;
        private static RuntimeAppLifecycleAdapter _lifecycleAdapter = new();
        private static RuntimeNetworkSessionAdapter _networkAdapter = new();
        private static RuntimeTouchInputAdapter _inputAdapter = new();
        private static RuntimeStoragePaths _storagePaths;

        internal static ScriptRuntimeManager RuntimeManager => _runtime;
        internal static ScriptTickMetrics LastRuntimeMetrics => _lastRuntimeMetrics;
        internal static RuntimeHostServices HostServices => _host;

        internal static void NotifyLifecycleSuspended() => _lifecycleAdapter.NotifySuspended();

        internal static void NotifyLifecycleForeground() => _lifecycleAdapter.NotifyForeground();

        internal static void NotifyNetworkDisconnected() => _networkAdapter.NotifyDisconnected();

        internal static void NotifyNetworkConnected() => _networkAdapter.NotifyConnected();

        internal static void NotifyNetworkReconnecting() => _networkAdapter.NotifyReconnecting();

        internal static void NotifyTouchInput(RuntimeInputEvent inputEvent) => _inputAdapter.Enqueue(inputEvent);

        public static void Init(World world)
        {
            _world = world;
            _storagePaths = new RuntimeStoragePaths(CUOEnviroment.ExecutablePath);
            _host = new RuntimeHostServices(_lifecycleAdapter, _networkAdapter, _inputAdapter, _storagePaths, new RuntimeTelemetrySink());
            _runtime = new ScriptRuntimeManager(tick => ScriptWorldSnapshot.Create(_world, tick), _host);
            _legacyAdapter = new LegacyScriptRuntimeAdapter(_runtime);
            _lifecycleAdapter.NotifyForeground();
            Task.Factory.StartNew(Python.CreateEngine); //This is to preload engine stuff, helps with faster script startup later
            ScriptPath = _storagePaths.ScriptsPath;

            if (!_loaded)
            {
                EventSink.JournalEntryAdded += EventSink_JournalEntryAdded;
                EventSink.SoundPlayed += EventSink_SoundPlayed;
                _loaded = true;
            }

            LoadScriptsFromFile();
            LoadLScriptSettings();
            AutoPlayGlobal();
            AutoPlayChar();
            _enabled = true;

            world.CommandManager.Register
            (
                "playlscript", a =>
                {
                    if (a.Length < 2)
                    {
                        GameActions.Print(world, "Usage: playlscript <filename>");

                        return;
                    }

                    foreach (ScriptFile f in LoadedScripts)
                        if (f.FileName == string.Join(" ", a.Skip(1)))
                        {
                            PlayScript(f);

                            return;
                        }
                }
            );

            world.CommandManager.Register
            (
                "stoplscript", a =>
                {
                    if (a.Length < 2)
                    {
                        GameActions.Print(world, "Usage: stoplscript <filename>");

                        return;
                    }

                    foreach (ScriptFile sf in RunningScripts)
                        if (sf.FileName == string.Join(" ", a.Skip(1)))
                        {
                            StopScript(sf);

                            return;
                        }
                }
            );

            world.CommandManager.Register
            (
                "togglelscript", a =>
                {
                    if (a.Length < 2)
                    {
                        GameActions.Print(world, "Usage: togglelscript <filename>");

                        return;
                    }

                    foreach (ScriptFile sf in RunningScripts)
                        if (sf.FileName == string.Join(" ", a.Skip(1)))
                        {
                            StopScript(sf);

                            return;
                        }

                    foreach (ScriptFile f in LoadedScripts)
                        if (f.FileName == string.Join(" ", a.Skip(1)))
                        {
                            PlayScript(f);

                            return;
                        }
                }
            );

            world.CommandManager.Register
            (
                "stopall", a =>
                {
                    if (RunningScripts.Count == 0)
                    {
                        GameActions.Print(world, "No scripts are currently running.");
                        return;
                    }

                    int count = RunningScripts.Count;
                    // Create a copy of the list to avoid modification during iteration
                    var scriptsToStop = RunningScripts.ToList();

                    foreach (ScriptFile sf in scriptsToStop)
                    {
                        StopScript(sf);
                    }

                    GameActions.Print(world, $"Stopped {count} running script(s).");
                }
            );
        }

        private static void EventSink_JournalEntryAdded(object sender, JournalEntry e)
        {
            if (e is null)
                return;

            foreach (ScriptFile script in RunningScripts)
            {
                script?.ScopedApi?.JournalEntries.Enqueue(new PyJournalEntry(e));

                while (script?.ScopedApi?.JournalEntries.Count > ProfileManager.CurrentProfile.MaxJournalEntries) script.ScopedApi?.JournalEntries.TryDequeue(out _);
            }
        }

        private static void EventSink_SoundPlayed(object sender, SoundEventArgs e)
        {
            if (e is null)
                return;

            foreach (ScriptFile script in RunningScripts)
            {
                script?.ScopedApi?.SoundEntries.Enqueue(new PySoundEntry(e));

                while (script?.ScopedApi?.SoundEntries.Count > ProfileManager.CurrentProfile.MaxSoundEntries) script.ScopedApi?.SoundEntries.TryDequeue(out _);
            }
        }

        public static void LoadScriptsFromFile()
        {
            if (!Directory.Exists(ScriptPath))
                Directory.CreateDirectory(ScriptPath);

            LoadedScripts.RemoveAll(ls => !ls.FileExists());

            List<string> groups = [ScriptPath, .. HandleScriptsInDirectory(ScriptPath)];

            var subgroups = new List<string>();

            //First level directory(groups)
            foreach (string file in groups)
                subgroups.AddRange(HandleScriptsInDirectory(file));

            foreach (string file in subgroups)
                HandleScriptsInDirectory(file); //No third level supported, ignore directories
        }

        private static void AddScriptFromFile(string path)
        {
            string p = Path.GetDirectoryName(path);
            string fname = Path.GetFileName(path);

            LoadedScripts.Add(new ScriptFile(_world, p, fname));
        }

        /// <summary>
        /// Returns a list of sub directories
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static List<string> HandleScriptsInDirectory(string path)
        {
            var loadedScripts = new HashSet<string>();

            foreach (ScriptFile script in LoadedScripts)
                loadedScripts.Add(script.FullPath);

            var groups = new List<string>();

            foreach (string file in Directory.EnumerateFileSystemEntries(path))
            {
                string fname = Path.GetFileName(file);

                if (fname == "API.py" || fname.StartsWith("_"))
                    continue;

                if (file.EndsWith(".lscript") || file.EndsWith(".py"))
                {
                    if (loadedScripts.Contains(file))
                        continue;

                    AddScriptFromFile(file);
                    loadedScripts.Add(file);
                }
                else if (Directory.Exists(file)) groups.Add(file);
            }

            return groups;
        }

        public static void SetAutoPlay(ScriptFile script, bool global, bool enabled)
        {
            if (global)
            {
                if (enabled)
                {
                    if (!LScriptSettings.GlobalAutoStartScripts.Contains(script.FileName))
                        LScriptSettings.GlobalAutoStartScripts.Add(script.FileName);
                }
                else
                    LScriptSettings.GlobalAutoStartScripts.Remove(script.FileName);
            }
            else
            {
                if (LScriptSettings.CharAutoStartScripts.ContainsKey(GetAccountCharName()))
                {
                    if (enabled)
                    {
                        if (!LScriptSettings.CharAutoStartScripts[GetAccountCharName()].Contains(script.FileName))
                            LScriptSettings.CharAutoStartScripts[GetAccountCharName()].Add(script.FileName);
                    }
                    else
                        LScriptSettings.CharAutoStartScripts[GetAccountCharName()].Remove(script.FileName);
                }
                else
                {
                    if (enabled)
                        LScriptSettings.CharAutoStartScripts.Add
                        (
                            GetAccountCharName(), [script.FileName]
                        );
                }
            }
        }

        public static bool AutoLoadEnabled(ScriptFile script, bool global)
        {
            if (!_enabled)
                return false;

            if (global)
                return LScriptSettings.GlobalAutoStartScripts.Contains(script.FileName);

            if (LScriptSettings.CharAutoStartScripts.TryGetValue(GetAccountCharName(), out List<string> scripts)) return scripts.Contains(script.FileName);

            return false;
        }

        private static void AutoPlayGlobal()
        {
            foreach (string script in LScriptSettings.GlobalAutoStartScripts)
                foreach (ScriptFile f in LoadedScripts)
                    if (f.FileName == script)
                        PlayScript(f);
        }

        private static void AutoPlayChar()
        {
            if (_world.Player == null)
                return;

            if (!LScriptSettings.CharAutoStartScripts.TryGetValue(GetAccountCharName(), out List<string> scripts)) return;

            foreach (ScriptFile f in LoadedScripts)
                if (scripts.Contains(f.FileName))
                    PlayScript(f);
        }

        private static string GetAccountCharName() => ProfileManager.CurrentProfile.Username + ProfileManager.CurrentProfile.CharacterName;

        public static bool IsGroupCollapsed(string group, string subgroup = "")
        {
            string path = group;

            if (!string.IsNullOrEmpty(subgroup))
                path += "/" + subgroup;

            return LScriptSettings.GroupCollapsed.GetValueOrDefault(path, false);
        }

        public static void SetGroupCollapsed(string group, string subgroup = "", bool expanded = false)
        {
            string path = group;

            if (!string.IsNullOrEmpty(subgroup))
                path += "/" + subgroup;

            LScriptSettings.GroupCollapsed[path] = expanded;
        }

        private static void LoadLScriptSettings()
        {
            string path = _storagePaths?.SettingsPath ?? Path.Combine(CUOEnviroment.ExecutablePath, "Data", "lscript.json");

            try
            {
                if (File.Exists(path))
                {
                    LScriptSettings = JsonSerializer.Deserialize(File.ReadAllText(path), LScriptJsonContext.Default.LScriptSettings);

                    for (int i = 0; i < LScriptSettings.CharAutoStartScripts.Count; i++)
                    {
                        KeyValuePair<string, List<string>> val = LScriptSettings.CharAutoStartScripts.ElementAt(i);
                        val.Value.RemoveAll(script => LoadedScripts.All(s => s.FileName != script));
                    }

                    LScriptSettings.GlobalAutoStartScripts.RemoveAll(script => LoadedScripts.All(s => s.FileName != script));

                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Unexpected error: {ex}");
            }

            LScriptSettings = new LScriptSettings();
        }

        private static void SaveScriptSettings()
        {
            string path = _storagePaths?.SettingsPath ?? Path.Combine(CUOEnviroment.ExecutablePath, "Data", "lscript.json");

            string json = JsonSerializer.Serialize(LScriptSettings, LScriptJsonContext.Default.LScriptSettings);

            try
            {
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Log.Error($"Error saving lscript settings: {e}");
            }
        }

        public static void Unload()
        {
            while (RunningScripts.Count > 0)
                StopScript(RunningScripts[0]);

            PyThreads.Clear();

            SaveScriptSettings();

            _enabled = false;

            _runtime = new ScriptRuntimeManager();
            _legacyAdapter = new LegacyScriptRuntimeAdapter(_runtime);
            _lastRuntimeMetrics = new ScriptTickMetrics { Tick = 0 };
            _host = null;
            _storagePaths = null;
        }

        public static void PlayScript(ScriptFile script)
        {
            _legacyAdapter.PlayScript(script, PlayScriptLegacy);
        }

        private static void PlayScriptLegacy(ScriptFile script)
        {
            if (script == null)
                return;

            if (RunningScripts.Contains(script)) //Already playing
                return;

            if (script.PythonThread == null || !script.PythonThread.IsAlive)
            {
                script.ReadFromFile();
                script.PythonThread = new Thread(() => ExecutePythonScript(script))
                {
                    IsBackground = true
                };

                if (!PyThreads.TryAdd(script.PythonThread.ManagedThreadId, script))
                    PyThreads[script.PythonThread.ManagedThreadId] = script;

                script.PythonThread.Start();
            }

            RunningScripts.Add(script);
        }

        private static void ExecutePythonScript(ScriptFile script)
        {
            script.SetupPythonEngine();
            script.SetupPythonScope();

            try
            {
                ScriptSource source = script.PythonEngine.CreateScriptSourceFromString(script.FileContentsJoined, script.FullPath, SourceCodeKind.File);
                source?.Execute(script.PythonScope);
            }
            catch (ThreadInterruptedException) { }
            catch (ThreadAbortException) { }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                ShowScriptError(script, e);
            }

            MainThreadQueue.EnqueueAction(() => { StopScript(script); });
        }

        /// <summary>
        /// Formats a script execution exception returned by IronPython/ScriptHost
        /// </summary>
        /// <param name="script">The script that triggered the error</param>
        /// <param name="e">The thrown error</param>
        private static void ShowScriptError(ScriptFile script, Exception e)
        {
            GameActions.Print(_world, $"Legion Script '{script.FileName}' encountered an error.", Constants.HUE_ERROR);

            ExceptionOperations eo = script.PythonEngine.GetService<ExceptionOperations>();
            if (eo != null)
            {
                string formattedEx = eo.FormatException(e);
                Log.Warn(formattedEx);

                Regex exParserRx = RegexHelper.GetRegex("File \"(?<filepath>.+?)\", line (?<lineno>\\d+)", RegexOptions.Compiled | RegexOptions.Multiline);

                MatchCollection matches = exParserRx.Matches(formattedEx);
                var errorLocations = new List<ScriptErrorLocation>();

                bool first = true;
                foreach (Match match in matches)
                {
                    string filePath = match.Groups["filepath"].Value;

                    // Skip internal IronPython frames (e.g. File "<string>", ...)
                    if (filePath.StartsWith("<"))
                        continue;

                    if (!int.TryParse(match.Groups["lineno"].Value, out int lineNumber))
                        continue;

                    string fileName = Path.GetFileName(filePath);
                    string lineContent = "";

                    if (TryReadFileLines(filePath, out string[] fileLines))
                        lineContent = GetContents(fileLines, first? lineNumber + 1 : lineNumber); //Offset for removal of import API line

                    errorLocations.Add(new ScriptErrorLocation(fileName, filePath, lineNumber, lineContent));

                    first = false;
                }

                if (errorLocations.Count > 0)
                {
                    ImGuiManager.AddWindow(new ScriptErrorWindow(new ScriptErrorDetails(e.Message, errorLocations, script)));
                }
                else
                    GameActions.Print(_world, formattedEx, Constants.HUE_ERROR);
            }
            else
                GameActions.Print(_world, e.Message, Constants.HUE_ERROR);

            if (e.InnerException != null)
                ShowScriptError(script, e.InnerException);
        }

        private static string GetContents(string[] lines, int line, int outerLines = 1)
        {
            var sb = new StringBuilder();
            int errorIndex = line - 1;

            for (int i = errorIndex - outerLines; i <= errorIndex + outerLines; i++)
            {
                if (i < 0 || i >= lines.Length)
                    continue;

                sb.AppendLine(i == errorIndex ? lines[i] + "  <-- Error line" : lines[i]);
            }

            return sb.ToString();
        }

        private static bool TryReadFileLines(string filePath, out string[] lines)
        {
            try
            {
                lines = File.ReadAllText(filePath).Split("\n");
                return true;
            }
            catch
            {
                lines = null;
                return false;
            }
        }

        public static void StopScript(ScriptFile script)
        {
            _legacyAdapter.StopScript(script, StopScriptLegacy);
        }

        private static void StopScriptLegacy(ScriptFile script)
        {
            if (script == null) return;

            RunningScripts.Remove(script);

            if (script.PythonThread is { IsAlive: true })
            {
                if (script.ScopedApi != null)
                {
                    script.ScopedApi.StopRequested = true;
                    script.ScopedApi.CancellationToken.Cancel();
                }

                if (script.PythonEngine != null)
                    script.PythonEngine.Runtime.Shutdown();

                script.PythonThread.Interrupt();
                script.PythonThread.Join(3000);
            }
            else
            {
                if (script.PythonThread != null)
                    PyThreads.Remove(script.PythonThread.ManagedThreadId);
                script.PythonScriptStopped();
                script.PythonThread = null;
            }
        }

        public static void TickRuntime(int maxStepsPerTick = 8, int maxActionsPerTick = 16)
        {
            if (!_enabled || _world == null || !_world.InGame)
                return;

            ScriptTickMetrics metrics = _runtime.Tick(maxStepsPerTick);
            _lastRuntimeMetrics = metrics;
            List<ScriptAction> actions = _runtime.DrainActions();

            if (maxActionsPerTick < 1)
                maxActionsPerTick = 1;

            int executed = 0;
            foreach (ScriptAction action in actions)
            {
                if (executed >= maxActionsPerTick)
                    break;

                ExecuteRuntimeAction(action);
                executed++;
            }

            ScriptingInfoGump.AddOrUpdateInfo("Runtime Contexts", _runtime.Contexts.Count);
            ScriptingInfoGump.AddOrUpdateInfo("Runtime Tick", metrics.Tick);
            ScriptingInfoGump.AddOrUpdateInfo("Runtime Steps", metrics.ExecutedSteps);
            ScriptingInfoGump.AddOrUpdateInfo("Runtime Pending Actions", Math.Max(0, actions.Count - executed));
            ScriptingInfoGump.AddOrUpdateInfo("Runtime Legacy Tracked", _legacyAdapter.TrackedScripts);
            ScriptingInfoGump.AddOrUpdateInfo("Runtime Lifecycle", _lifecycleAdapter.State.ToString());
            ScriptingInfoGump.AddOrUpdateInfo("Runtime Network", _networkAdapter.State.ToString());
        }

        private static void ExecuteRuntimeAction(ScriptAction action)
        {
            if (action == null)
                return;

            switch (action.ActionType)
            {
                case RuntimeScriptApi.ActionCastSpell:
                    if (action.Payload is RuntimeCastSpellAction castAction)
                        GameActions.CastSpellByName(castAction.SpellName, partialMatch: true);
                    break;

                case RuntimeScriptApi.ActionTargetSerial:
                    if (action.Payload is RuntimeTargetAction targetAction)
                        AsyncNetClient.Socket.Send_TargetSelectedObject(targetAction.Serial, targetAction.Serial);
                    break;

                case RuntimeScriptApi.ActionUsePotion:
                    if (action.Payload is RuntimeUsePotionAction potionAction)
                        ExecutePotionAction(potionAction);
                    break;
            }
        }

        private static void ExecutePotionAction(RuntimeUsePotionAction potionAction)
        {
            if (potionAction == null || string.IsNullOrWhiteSpace(potionAction.PotionName) || _world?.Player == null)
                return;

            // Accept raw serial forms for deterministic use in migrated scripts.
            string token = potionAction.PotionName.Trim();
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                token = token.Substring(2);

            if (uint.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out uint serialHex))
            {
                GameActions.DoubleClickQueued(serialHex);
                return;
            }

            if (uint.TryParse(potionAction.PotionName, out uint serialDec))
            {
                GameActions.DoubleClickQueued(serialDec);
            }
        }

        public static void DownloadApiPy() => Task.Run
            (() =>
                {
                    try
                    {
                        var client = new System.Net.WebClient();
                        string api = client.DownloadString(new Uri("https://raw.githubusercontent.com/PlayTazUO/TazUO/refs/heads/dev/src/ClassicUO.Client/LegionScripting/docs/API.py"));
                        File.WriteAllText(Path.Combine(CUOEnviroment.ExecutablePath, "LegionScripts", "API.py"), api);
                        MainThreadQueue.EnqueueAction(() => { GameActions.Print(_world, "Updated API!"); });
                    }
                    catch (Exception ex)
                    {
                        MainThreadQueue.EnqueueAction(() => { GameActions.Print(_world, "Failed to update the API..", 32); });
                        Log.Error(ex.ToString());
                    }

                }
            );
    }
}
