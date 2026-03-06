using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassicUO.LegionScripting.Runtime;

internal sealed class ScriptRuntimeManager
{
    private readonly Dictionary<int, ScriptContext> _contexts = new();
    private readonly ScriptActionQueue _actionQueue = new();
    private Func<long, ScriptWorldSnapshot> _snapshotProvider;

    private int _nextScriptId = 1;
    private long _currentTick;
    private long _eventSequence;
    private long _actionSequence;

    public ScriptRuntimeManager(Func<long, ScriptWorldSnapshot> snapshotProvider = null)
    {
        _snapshotProvider = snapshotProvider;
        LatestSnapshot = ScriptWorldSnapshot.Empty;
    }

    public long CurrentTick => _currentTick;

    public IReadOnlyCollection<ScriptContext> Contexts => _contexts.Values;

    public ScriptWorldSnapshot LatestSnapshot { get; private set; }

    public void SetSnapshotProvider(Func<long, ScriptWorldSnapshot> snapshotProvider)
    {
        _snapshotProvider = snapshotProvider;
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
        }

        metrics.PendingActions = _actionQueue.Count;
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
}
