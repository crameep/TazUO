using System.Collections.Generic;

namespace ClassicUO.Utility;

public class ConcurrentPriorityQueue<TElement, TPriority>
{
    public bool IsEmpty => _isEmpty;

    private readonly PriorityQueue<TElement, (TPriority Priority, long Sequence)> _queue = new(new PrioritySequenceComparer<TPriority>());
    private readonly object _lock = new();
    private bool _isEmpty = true;
    private long _sequence;

    public void Enqueue(TElement element, TPriority priority)
    {
        lock (_lock)
        {
            _queue.Enqueue(element, (priority, _sequence++));
            _isEmpty = false;
        }
    }

    public bool TryDequeue(out TElement element, out TPriority priority)
    {
        lock (_lock)
        {
            bool res = _queue.TryDequeue(out element, out (TPriority Priority, long Sequence) compositePriority);
            priority = compositePriority.Priority;

            _isEmpty = _queue.Count == 0;

            return res;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
            _isEmpty = true;
        }
    }

    private class PrioritySequenceComparer<T> : IComparer<(T Priority, long Sequence)>
    {
        public int Compare((T Priority, long Sequence) x, (T Priority, long Sequence) y)
        {
            int priorityComparison = Comparer<T>.Default.Compare(x.Priority, y.Priority);
            if (priorityComparison != 0)
                return priorityComparison;

            return x.Sequence.CompareTo(y.Sequence);
        }
    }
}
