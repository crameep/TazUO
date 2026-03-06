using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClassicUO.Utility.Logging;
using ClassicUO.LegionScripting.Runtime;

namespace ClassicUO.LegionScripting.Runtime.Host;

internal sealed class RuntimeAppLifecycleAdapter : IRuntimeAppLifecycle
{
    public RuntimeLifecycleState State { get; private set; } = RuntimeLifecycleState.Foreground;

    public event Action<RuntimeLifecycleState> StateChanged;

    public void NotifySuspended()
    {
        if (State == RuntimeLifecycleState.Suspended)
            return;

        State = RuntimeLifecycleState.Suspended;
        StateChanged?.Invoke(State);
    }

    public void NotifyForeground()
    {
        if (State == RuntimeLifecycleState.Foreground)
            return;

        State = RuntimeLifecycleState.Foreground;
        StateChanged?.Invoke(State);
    }
}

internal sealed class RuntimeNetworkSessionAdapter : IRuntimeNetworkSession
{
    public RuntimeNetworkState State { get; private set; } = RuntimeNetworkState.Connected;

    public event Action<RuntimeNetworkState> StateChanged;

    public void NotifyConnected()
    {
        if (State == RuntimeNetworkState.Connected)
            return;

        State = RuntimeNetworkState.Connected;
        StateChanged?.Invoke(State);
    }

    public void NotifyDisconnected()
    {
        if (State == RuntimeNetworkState.Disconnected)
            return;

        State = RuntimeNetworkState.Disconnected;
        StateChanged?.Invoke(State);
    }

    public void NotifyReconnecting()
    {
        if (State == RuntimeNetworkState.Reconnecting)
            return;

        State = RuntimeNetworkState.Reconnecting;
        StateChanged?.Invoke(State);
    }
}

internal sealed class RuntimeTouchInputAdapter : IRuntimeInputAdapter
{
    private readonly Queue<RuntimeInputEvent> _events = new();

    public void Enqueue(RuntimeInputEvent inputEvent)
    {
        _events.Enqueue(inputEvent);
    }

    public IReadOnlyList<RuntimeInputEvent> DrainEvents()
    {
        if (_events.Count == 0)
            return Array.Empty<RuntimeInputEvent>();

        var drained = new List<RuntimeInputEvent>(_events.Count);
        while (_events.Count > 0)
            drained.Add(_events.Dequeue());

        return drained;
    }
}

internal sealed class RuntimeStoragePaths : IRuntimeStoragePaths
{
    public RuntimeStoragePaths(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("rootPath is required", nameof(rootPath));

        string baseRoot = rootPath;
        string mobileRoot = Environment.GetEnvironmentVariable("TAZUO_IOS_SANDBOX_ROOT");
        if (!string.IsNullOrWhiteSpace(mobileRoot))
            baseRoot = mobileRoot;

        ScriptsPath = Path.Combine(baseRoot, "LegionScripts");
        LogsPath = Path.Combine(baseRoot, "Data", "LegionScripting", "Logs");
        SettingsPath = Path.Combine(baseRoot, "Data", "lscript.json");

        Directory.CreateDirectory(ScriptsPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
    }

    public string ScriptsPath { get; }
    public string LogsPath { get; }
    public string SettingsPath { get; }
}

internal sealed class RuntimeTelemetrySink : IRuntimeTelemetrySink
{
    private readonly Dictionary<string, object> _lastValues = new();

    public void PublishMetric(string name, object value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        _lastValues[name] = value;

        Log.Trace($"[RuntimeTelemetry] {name}={value}");
    }

    public IReadOnlyDictionary<string, object> Snapshot()
    {
        return new Dictionary<string, object>(_lastValues);
    }

    public string WriteBetaReport(string logsPath, IReadOnlyList<ScriptRuntimeFault> faults)
    {
        if (string.IsNullOrWhiteSpace(logsPath))
            return string.Empty;

        Directory.CreateDirectory(logsPath);

        var groupedFaults = (faults ?? Array.Empty<ScriptRuntimeFault>())
            .GroupBy(f => f.Reason ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        var report = new
        {
            generated_at_utc = DateTime.UtcNow,
            metrics = _lastValues,
            fault_count = faults?.Count ?? 0,
            fault_buckets = groupedFaults,
            release_gate = (faults?.Count ?? 0) == 0 ? "GREEN" : "REVIEW_REQUIRED"
        };

        string path = Path.Combine(logsPath, $"runtime-beta-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
