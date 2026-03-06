using System;
using System.Collections.Generic;

namespace ClassicUO.LegionScripting.Runtime.Host;

internal enum RuntimeLifecycleState
{
    Foreground = 0,
    Suspended = 1
}

internal enum RuntimeNetworkState
{
    Connected = 0,
    Disconnected = 1,
    Reconnecting = 2
}

internal enum RuntimeInputKind
{
    Tap = 0,
    LongPress = 1,
    DragStart = 2,
    DragMove = 3,
    DragEnd = 4
}

internal readonly struct RuntimeInputEvent
{
    public RuntimeInputEvent(RuntimeInputKind kind, int x, int y, int pointerId = 0)
    {
        Kind = kind;
        X = x;
        Y = y;
        PointerId = pointerId;
    }

    public RuntimeInputKind Kind { get; }
    public int X { get; }
    public int Y { get; }
    public int PointerId { get; }
}

internal interface IRuntimeAppLifecycle
{
    RuntimeLifecycleState State { get; }
    event Action<RuntimeLifecycleState> StateChanged;
}

internal interface IRuntimeNetworkSession
{
    RuntimeNetworkState State { get; }
    event Action<RuntimeNetworkState> StateChanged;
}

internal interface IRuntimeInputAdapter
{
    IReadOnlyList<RuntimeInputEvent> DrainEvents();
}

internal interface IRuntimeStoragePaths
{
    string ScriptsPath { get; }
    string LogsPath { get; }
    string SettingsPath { get; }
}

internal interface IRuntimeTelemetrySink
{
    void PublishMetric(string name, object value);
}

internal sealed class RuntimeHostServices
{
    public RuntimeHostServices(
        IRuntimeAppLifecycle lifecycle,
        IRuntimeNetworkSession network,
        IRuntimeInputAdapter input,
        IRuntimeStoragePaths storage,
        IRuntimeTelemetrySink telemetry)
    {
        Lifecycle = lifecycle;
        Network = network;
        Input = input;
        Storage = storage;
        Telemetry = telemetry;
    }

    public IRuntimeAppLifecycle Lifecycle { get; }
    public IRuntimeNetworkSession Network { get; }
    public IRuntimeInputAdapter Input { get; }
    public IRuntimeStoragePaths Storage { get; }
    public IRuntimeTelemetrySink Telemetry { get; }
}
