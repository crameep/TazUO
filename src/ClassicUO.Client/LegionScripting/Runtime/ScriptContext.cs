using System;
using System.Collections.Generic;

namespace ClassicUO.LegionScripting.Runtime;

internal sealed class ScriptContext
{
    private readonly Queue<ScriptEvent> _mailbox = new();
    private readonly HashSet<string> _subscriptions = [];
    private readonly Func<ScriptExecutionContext, ScriptDirective> _step;

    private long _runSequence;
    private int _deficit;
    private long _resumeAtTick;
    private string _waitingForEventType;
    private long? _waitDeadlineTick;

    public ScriptContext(int id, string name, ScriptPriority priority, Func<ScriptExecutionContext, ScriptDirective> step)
    {
        Id = id;
        Name = name;
        Priority = priority;
        _step = step ?? throw new ArgumentNullException(nameof(step));
        State = ScriptState.Ready;
    }

    public int Id { get; }

    public string Name { get; }

    public ScriptPriority Priority { get; }

    public ScriptState State { get; private set; }

    public string LastError { get; private set; }

    internal int Deficit => _deficit;

    internal long LastRunSequence => _runSequence;

    internal void AddDeficit(int amount)
    {
        _deficit += amount;
    }

    internal void ConsumeDeficit(int totalWeight)
    {
        _deficit -= totalWeight;
    }

    internal void Subscribe(string eventType)
    {
        if (!string.IsNullOrWhiteSpace(eventType))
            _subscriptions.Add(eventType);
    }

    internal bool IsSubscribedTo(string eventType)
    {
        return _subscriptions.Contains(eventType);
    }

    internal void EnqueueEvent(ScriptEvent scriptEvent)
    {
        _mailbox.Enqueue(scriptEvent);
    }

    internal bool TryDequeueEvent(out ScriptEvent scriptEvent)
    {
        if (_mailbox.Count > 0)
        {
            scriptEvent = _mailbox.Dequeue();
            return true;
        }

        scriptEvent = null;
        return false;
    }

    internal bool IsRunnable(long currentTick)
    {
        if (State is ScriptState.Cancelled or ScriptState.Faulted or ScriptState.Completed)
            return false;

        if (State == ScriptState.Waiting)
        {
            if (_waitDeadlineTick.HasValue && currentTick >= _waitDeadlineTick.Value)
            {
                State = ScriptState.Faulted;
                LastError = "timeout";
                return false;
            }

            if (_waitingForEventType != null)
            {
                foreach (ScriptEvent scriptEvent in _mailbox)
                {
                    if (scriptEvent.EventType == _waitingForEventType)
                    {
                        State = ScriptState.Ready;
                        _waitingForEventType = null;
                        _waitDeadlineTick = null;
                        break;
                    }
                }
            }
            else if (currentTick >= _resumeAtTick)
            {
                State = ScriptState.Ready;
                _waitDeadlineTick = null;
            }
        }

        return State is ScriptState.Ready or ScriptState.Running;
    }

    internal void Cancel(string reason)
    {
        State = ScriptState.Cancelled;
        LastError = reason;
    }

    internal void Execute(ScriptRuntimeManager runtime, long currentTick)
    {
        if (State is ScriptState.Cancelled or ScriptState.Faulted or ScriptState.Completed)
            return;

        State = ScriptState.Running;
        _runSequence++;

        try
        {
            ScriptDirective directive = _step(new ScriptExecutionContext(this, runtime, currentTick)) ?? ScriptDirective.Yield();

            switch (directive.Kind)
            {
                case ScriptDirectiveKind.Yield:
                    State = ScriptState.Ready;
                    break;

                case ScriptDirectiveKind.WaitTicks:
                    State = ScriptState.Waiting;
                    _resumeAtTick = currentTick + Math.Max(0, directive.WaitTicks);
                    _waitingForEventType = null;
                    _waitDeadlineTick = directive.TimeoutTicks.HasValue
                        ? currentTick + Math.Max(0, directive.TimeoutTicks.Value)
                        : null;
                    break;

                case ScriptDirectiveKind.WaitForEvent:
                    State = ScriptState.Waiting;
                    _waitingForEventType = directive.EventType;
                    _waitDeadlineTick = directive.TimeoutTicks.HasValue
                        ? currentTick + Math.Max(0, directive.TimeoutTicks.Value)
                        : null;
                    break;

                case ScriptDirectiveKind.Complete:
                    State = ScriptState.Completed;
                    break;
            }
        }
        catch (Exception ex)
        {
            State = ScriptState.Faulted;
            LastError = ex.Message;
        }
    }
}
