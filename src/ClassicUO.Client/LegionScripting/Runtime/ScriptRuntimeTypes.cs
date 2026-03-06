using System;
using System.Collections.Generic;

namespace ClassicUO.LegionScripting.Runtime;

internal enum ScriptPriority
{
    Low = 1,
    Normal = 2,
    High = 3
}

internal enum ScriptState
{
    Ready,
    Running,
    Waiting,
    Cancelled,
    Faulted,
    Completed
}

internal enum ScriptDirectiveKind
{
    Yield,
    WaitTicks,
    WaitForEvent,
    Complete
}

internal sealed class ScriptDirective
{
    public ScriptDirectiveKind Kind { get; private init; }
    public int WaitTicks { get; private init; }
    public string EventType { get; private init; }
    public int? TimeoutTicks { get; private init; }

    public static ScriptDirective Yield() => new() { Kind = ScriptDirectiveKind.Yield };

    public static ScriptDirective WaitTicksFor(int waitTicks) => new() { Kind = ScriptDirectiveKind.WaitTicks, WaitTicks = Math.Max(0, waitTicks) };

    public static ScriptDirective WaitForEvent(string eventType, int? timeoutTicks = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("eventType is required", nameof(eventType));

        return new ScriptDirective { Kind = ScriptDirectiveKind.WaitForEvent, EventType = eventType, TimeoutTicks = timeoutTicks };
    }

    public static ScriptDirective Complete() => new() { Kind = ScriptDirectiveKind.Complete };
}

internal sealed class ScriptEvent
{
    public long Sequence { get; init; }
    public string EventType { get; init; }
    public object Payload { get; init; }
}

internal sealed class ScriptAction
{
    public int SourceScriptId { get; init; }
    public long Sequence { get; init; }
    public string ActionType { get; init; }
    public object Payload { get; init; }
}

internal sealed class ScriptTickMetrics
{
    public long Tick { get; init; }
    public int ExecutedSteps { get; set; }
    public int RunnableScripts { get; set; }
    public int PendingActions { get; set; }
}

internal sealed class ScriptActionQueue
{
    private readonly Queue<ScriptAction> _queue = new();

    public int Count => _queue.Count;

    public void Enqueue(ScriptAction action)
    {
        _queue.Enqueue(action);
    }

    public List<ScriptAction> DrainAll()
    {
        var list = new List<ScriptAction>(_queue.Count);

        while (_queue.Count > 0)
            list.Add(_queue.Dequeue());

        return list;
    }
}

internal sealed class ScriptExecutionContext
{
    private readonly ScriptContext _context;
    private readonly ScriptRuntimeManager _runtime;

    public ScriptExecutionContext(ScriptContext context, ScriptRuntimeManager runtime, long currentTick)
    {
        _context = context;
        _runtime = runtime;
        CurrentTick = currentTick;
    }

    public long CurrentTick { get; }

    public ScriptWorldSnapshot Snapshot => _runtime.LatestSnapshot;

    public bool TryDequeueEvent(out ScriptEvent scriptEvent) => _context.TryDequeueEvent(out scriptEvent);

    public void EnqueueAction(string actionType, object payload = null)
    {
        _runtime.EnqueueAction(new ScriptAction
        {
            SourceScriptId = _context.Id,
            Sequence = _runtime.NextActionSequence(),
            ActionType = actionType,
            Payload = payload
        });
    }
}
