using System;
using ClassicUO.Game.UI.ImGuiControls;
using ClassicUO.Utility;

namespace ClassicUO.Game.Managers;

public class ObjectActionQueue : ConcurrentPriorityQueue<ObjectActionQueueItem, ActionPriority>
{
    public static ObjectActionQueue Instance { get; } = new();

    private ObjectActionQueue() { }

    public void Update()
    {
        if (IsEmpty || GlobalActionCooldown.IsOnCooldown) return; //Quick bool return if empty to avoid checking the queue when unnecessary

        while (TryDequeue(out ObjectActionQueueItem item, out ActionPriority _))
        {
            if (item.Canceled)
            {
                item.AfterInvoked?.Invoke(item);
                continue;
            }

            item.Action?.Invoke();
            item.AfterInvoked?.Invoke(item);
            GlobalActionCooldown.BeginCooldown();
            break;
        }
    }

    public void Reset() => Clear();
}

/// <summary>
///
/// </summary>
/// <param name="action">The action to perform</param>
/// <param name="afterInvoked">Called after the action was performed, will be called weather it was canceled or not.</param>
public class ObjectActionQueueItem(Action action, Action<ObjectActionQueueItem> afterInvoked = null)
{
    public Action Action { get; } = action;
    public Action<ObjectActionQueueItem> AfterInvoked { get; } = afterInvoked;
    public bool Canceled { get; private set; }

    public void SetCanceled(bool canceled = true) => Canceled = canceled;
}

public enum ActionPriority
{
    Immediate,
    UseItem,
    EquipItem,
    MoveItem,
}
