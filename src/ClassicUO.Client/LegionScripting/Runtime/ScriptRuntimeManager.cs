using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.LegionScripting.Runtime.Host;

namespace ClassicUO.LegionScripting.Runtime;

internal sealed class ScriptRuntimeManager
{
    private readonly Dictionary<int, ScriptContext> _contexts = new();
    private readonly ScriptActionQueue _actionQueue;
    private readonly ScriptRuntimeOptions _options;
    private readonly List<ScriptRuntimeFault> _faults = new();
    private Func<long, ScriptWorldSnapshot> _snapshotProvider;

    private int _nextScriptId = 1;
    private long _currentTick;
    private long _eventSequence;
    private long _actionSequence;
    private RuntimeHostServices _host;
    private bool _isSuspended;

    public ScriptRuntimeManager(Func<long, ScriptWorldSnapshot> snapshotProvider = null, RuntimeHostServices host = null, ScriptRuntimeOptions options = null)
    {
        _snapshotProvider = snapshotProvider;
        _host = host;
        _options = options ?? new ScriptRuntimeOptions();
        _actionQueue = new ScriptActionQueue(_options.MaxActionsQueued);
        LatestSnapshot = ScriptWorldSnapshot.Empty;
        BindHostEvents(_host);
    }

    public long CurrentTick => _currentTick;

    public IReadOnlyCollection<ScriptContext> Contexts => _contexts.Values;

    public ScriptWorldSnapshot LatestSnapshot { get; private set; }

    public IReadOnlyList<ScriptRuntimeFault> Faults => _faults;

    public void SetSnapshotProvider(Func<long, ScriptWorldSnapshot> snapshotProvider)
    {
        _snapshotProvider = snapshotProvider;
    }

    public void SetHost(RuntimeHostServices host)
    {
        UnbindHostEvents(_host);
        _host = host;
        BindHostEvents(_host);
    }

    public void Suspend(string reason = "suspend")
    {
        _isSuspended = true;
        PublishEvent("host.lifecycle.suspended", reason);
    }

    public void Resume(string reason = "resume")
    {
        _isSuspended = false;
        PublishEvent("host.lifecycle.foreground", reason);
    }

    internal long NextActionSequence() => ++_actionSequence;

    internal void EnqueueAction(ScriptAction action) => _actionQueue.Enqueue(action);

    public ScriptContext StartScript(string name, Func<ScriptExecutionContext, ScriptDirective> step, ScriptPriority priority = ScriptPriority.Normal)
    {
        int id = _nextScriptId++;
        var context = new ScriptContext(id, name, priority, step);
        _contexts.Add(id, context);
        return context;
    }

    public bool CancelScript(int scriptId, string reason = "cancelled")
    {
        if (!_contexts.TryGetValue(scriptId, out ScriptContext context))
            return false;

        context.Cancel(reason);
        _host?.Telemetry?.PublishMetric($"runtime.script.{scriptId}.cancelled", reason);
        return true;
    }

    public bool InjectFault(int scriptId, string reason = "fault.injected")
    {
        if (!_contexts.TryGetValue(scriptId, out ScriptContext context))
            return false;

        context.SetFault(reason, _currentTick);
        _faults.Add(new ScriptRuntimeFault { ScriptId = scriptId, Reason = reason, Tick = _currentTick });
        PublishEvent("runtime.fault", new ScriptRuntimeFault { ScriptId = scriptId, Reason = reason, Tick = _currentTick });
        _host?.Telemetry?.PublishMetric($"runtime.script.{scriptId}.fault", reason);
        return true;
    }

    public void Subscribe(int scriptId, string eventType)
    {
        if (_contexts.TryGetValue(scriptId, out ScriptContext context))
            context.Subscribe(eventType);
    }

    public void PublishEvent(string eventType, object payload = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("eventType required", nameof(eventType));

        ScriptEvent scriptEvent = new()
        {
            Sequence = ++_eventSequence,
            EventType = eventType,
            Payload = payload
        };

        foreach (ScriptContext context in _contexts.Values)
        {
            if (context.IsSubscribedTo(eventType))
                context.EnqueueEvent(scriptEvent);
        }
    }

    public ScriptTickMetrics Tick(int maxStepsPerTick = 64)
    {
        DrainHostInputEvents();

        if (_host?.Lifecycle?.State == RuntimeLifecycleState.Suspended)
            _isSuspended = true;

        if (_isSuspended)
            return new ScriptTickMetrics { Tick = _currentTick, ExecutedSteps = 0, RunnableScripts = 0, PendingActions = _actionQueue.Count, DroppedActions = _actionQueue.DroppedActions };

        if (maxStepsPerTick < 1)
            maxStepsPerTick = 1;

        _currentTick++;

        if (_snapshotProvider != null)
            LatestSnapshot = _snapshotProvider(_currentTick) ?? ScriptWorldSnapshot.Empty;

        var metrics = new ScriptTickMetrics { Tick = _currentTick };

        List<ScriptContext> runnable = _contexts.Values.Where(c => c.IsRunnable(_currentTick)).ToList();
        metrics.RunnableScripts = runnable.Count;

        if (runnable.Count == 0)
            return metrics;

        int totalWeight = runnable.Sum(c => (int)c.Priority);
        foreach (ScriptContext context in runnable)
            context.AddDeficit((int)context.Priority);

        for (int i = 0; i < maxStepsPerTick; i++)
        {
            ScriptContext next = ChooseNextRunnable(_currentTick);
            if (next == null)
                break;

            next.ConsumeDeficit(totalWeight);
            next.Execute(this, _currentTick);
            metrics.ExecutedSteps++;

            EmitPerScriptTelemetry(next);
        }

        metrics.WatchdogFaults = ApplyWatchdogFaults();

        metrics.PendingActions = _actionQueue.Count;
        metrics.DroppedActions = _actionQueue.DroppedActions;
        _host?.Telemetry?.PublishMetric("runtime.tick.executed_steps", metrics.ExecutedSteps);
        _host?.Telemetry?.PublishMetric("runtime.tick.pending_actions", metrics.PendingActions);
        _host?.Telemetry?.PublishMetric("runtime.tick.watchdog_faults", metrics.WatchdogFaults);
        _host?.Telemetry?.PublishMetric("runtime.tick.dropped_actions", metrics.DroppedActions);
        _host?.Telemetry?.PublishMetric("runtime.active_contexts", _contexts.Count);
        return metrics;
    }

    public List<ScriptAction> DrainActions() => _actionQueue.DrainAll();

    private ScriptContext ChooseNextRunnable(long currentTick)
    {
        ScriptContext best = null;

        foreach (ScriptContext context in _contexts.Values)
        {
            if (!context.IsRunnable(currentTick))
                continue;

            if (best == null)
            {
                best = context;
                continue;
            }

            if (context.Deficit > best.Deficit)
            {
                best = context;
                continue;
            }

            if (context.Deficit == best.Deficit && context.LastRunSequence < best.LastRunSequence)
                best = context;
        }

        return best;
    }

    private void DrainHostInputEvents()
    {
        IReadOnlyList<RuntimeInputEvent> inputEvents = _host?.Input?.DrainEvents();

        if (inputEvents == null || inputEvents.Count == 0)
            return;

        foreach (RuntimeInputEvent inputEvent in inputEvents)
            PublishEvent("host.input", inputEvent);
    }

    private void BindHostEvents(RuntimeHostServices host)
    {
        if (host == null)
            return;

        if (host.Lifecycle != null)
            host.Lifecycle.StateChanged += OnLifecycleStateChanged;

        if (host.Network != null)
            host.Network.StateChanged += OnNetworkStateChanged;
    }

    private void UnbindHostEvents(RuntimeHostServices host)
    {
        if (host == null)
            return;

        if (host.Lifecycle != null)
            host.Lifecycle.StateChanged -= OnLifecycleStateChanged;

        if (host.Network != null)
            host.Network.StateChanged -= OnNetworkStateChanged;
    }

    private void OnLifecycleStateChanged(RuntimeLifecycleState state)
    {
        if (state == RuntimeLifecycleState.Suspended)
        {
            Suspend("host-lifecycle");
            return;
        }

        Resume("host-lifecycle");
    }

    private void OnNetworkStateChanged(RuntimeNetworkState state)
    {
        PublishEvent("host.network", state);
    }

    private int ApplyWatchdogFaults()
    {
        if (_options.WatchdogMaxWaitingTicks < 1)
            return 0;

        int faulted = 0;

        foreach (ScriptContext context in _contexts.Values)
        {
            if (context.State != ScriptState.Waiting)
                continue;

            long waitingTicks = _currentTick - context.StateChangedAtTick;
            if (waitingTicks < _options.WatchdogMaxWaitingTicks)
                continue;

            string reason = "watchdog.wait-timeout";
            context.SetFault(reason, _currentTick);
            ScriptRuntimeFault fault = new() { ScriptId = context.Id, Reason = reason, Tick = _currentTick };
            _faults.Add(fault);
            PublishEvent("runtime.fault", fault);
            _host?.Telemetry?.PublishMetric($"runtime.script.{context.Id}.fault", reason);
            faulted++;
        }

        return faulted;
    }

    private void EmitPerScriptTelemetry(ScriptContext context)
    {
        _host?.Telemetry?.PublishMetric($"runtime.script.{context.Id}.state", context.State.ToString());
        _host?.Telemetry?.PublishMetric($"runtime.script.{context.Id}.mailbox_depth", context.MailboxDepth);
        _host?.Telemetry?.PublishMetric($"runtime.script.{context.Id}.last_error", context.LastError ?? string.Empty);
    }
}
