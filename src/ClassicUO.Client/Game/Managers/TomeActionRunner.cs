using System;
using System.Collections.Generic;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers
{
    internal sealed class TomeActionRunner
    {
        private const int DEFAULT_STATE_TIMEOUT_MS = 5000;

        private readonly Queue<TomeOperation> _operations = new();
        private readonly ITomeRuntime _runtime;
        private readonly Func<long> _timeProvider;

        private TomeOperation _currentOperation;
        private Queue<uint> _pendingItemSerials;
        private uint _currentItemSerial;
        private RunnerState _state = RunnerState.Idle;
        private long _stateEnteredAt;
        private long _delayUntil;
        private int _movedInCurrentOperation;
        private bool _walkingRequested;

        internal TomeActionRunner(ITomeRuntime runtime = null, Func<long> timeProvider = null)
        {
            _runtime = runtime ?? new LiveTomeRuntime();
            _timeProvider = timeProvider ?? (() => Time.Ticks);
        }

        public int PendingOperations => _operations.Count + (_state == RunnerState.Idle ? 0 : 1);

        public void Enqueue(TomeOperation operation)
        {
            if (operation?.Definition == null)
                return;

            _operations.Enqueue(operation);
        }

        public void Update()
        {
            if (_state == RunnerState.Idle)
            {
                if (_operations.Count == 0)
                    return;

                BeginOperation(_operations.Dequeue());
            }

            if (IsTimedState(_state) && IsTimedOut(DEFAULT_STATE_TIMEOUT_MS))
            {
                FailCurrentOperation("timed out waiting for tome response");
                return;
            }

            switch (_state)
            {
                case RunnerState.WalkingToTome:
                    HandleWalking();
                    break;
                case RunnerState.WaitingForGump:
                    HandleWaitingForGump();
                    break;
                case RunnerState.WaitingForTarget:
                    HandleWaitingForTarget();
                    break;
                case RunnerState.WaitingForDelay:
                    HandleWaitingForDelay();
                    break;
            }
        }

        private void BeginOperation(TomeOperation operation)
        {
            _currentOperation = operation;
            _pendingItemSerials = new Queue<uint>(operation.ItemSerials ?? new List<uint>());
            _movedInCurrentOperation = 0;
            _currentItemSerial = 0;
            _walkingRequested = false;

            if (operation.Definition.RequiresWalk)
            {
                SetState(RunnerState.WalkingToTome);
                return;
            }

            StartOperationFlow();
        }

        private void HandleWalking()
        {
            if (_runtime.IsWithinTomeRange(_currentOperation.Definition.TomeSerial))
            {
                StartOperationFlow();
                return;
            }

            if (_walkingRequested)
                return;

            _walkingRequested = true;
            if (!_runtime.RequestWalkToTome(_currentOperation.Definition.TomeSerial))
                FailCurrentOperation("could not start walking to tome");
        }

        private void StartOperationFlow()
        {
            switch (_currentOperation.Definition.Mode)
            {
                case TomeMode.FillAll:
                    ArmGumpAndUseTome();
                    SetState(RunnerState.WaitingForGump);
                    break;

                case TomeMode.TargetContainer:
                    ArmGumpAndUseTome();
                    uint targetSerial = _currentOperation.Definition.TargetSerial == 0
                        ? _runtime.GetPlayerSerial()
                        : _currentOperation.Definition.TargetSerial;

                    _runtime.ArmAutoTarget(targetSerial);
                    SetState(RunnerState.WaitingForGump);
                    break;

                case TomeMode.TargetEach:
                    if (!TryDequeueNextItem(out _currentItemSerial))
                    {
                        CompleteCurrentOperation();
                        return;
                    }

                    ArmGumpAndUseTome();
                    _runtime.ArmAutoTarget(_currentItemSerial);
                    SetState(RunnerState.WaitingForGump);
                    break;

                case TomeMode.TargetRepeat:
                    ArmGumpAndUseTome();
                    if (!TryDequeueNextItem(out _currentItemSerial))
                    {
                        CompleteCurrentOperation();
                        return;
                    }

                    _runtime.ArmAutoTarget(_currentItemSerial);
                    SetState(RunnerState.WaitingForGump);
                    break;
            }
        }

        private void HandleWaitingForGump()
        {
            if (_runtime.IsGumpActionPending)
                return;

            switch (_currentOperation.Definition.Mode)
            {
                case TomeMode.FillAll:
                    _movedInCurrentOperation += GetOperationItemCount();
                    CompleteCurrentOperation();
                    break;

                case TomeMode.TargetContainer:
                    SetState(RunnerState.WaitingForTarget);
                    break;

                case TomeMode.TargetEach:
                case TomeMode.TargetRepeat:
                    SetState(RunnerState.WaitingForTarget);
                    break;
            }
        }

        private void HandleWaitingForTarget()
        {
            if (_runtime.IsAutoTargetPending)
                return;

            switch (_currentOperation.Definition.Mode)
            {
                case TomeMode.TargetContainer:
                    _movedInCurrentOperation += GetOperationItemCount();
                    CompleteCurrentOperation();
                    break;

                case TomeMode.TargetEach:
                    _movedInCurrentOperation++;

                    if (TryDequeueNextItem(out _currentItemSerial))
                    {
                        BeginDelay(ResumeTargetEachCycle);
                    }
                    else
                    {
                        CompleteCurrentOperation();
                    }

                    break;

                case TomeMode.TargetRepeat:
                    _movedInCurrentOperation++;

                    if (TryDequeueNextItem(out _currentItemSerial))
                    {
                        BeginDelay(ArmRepeatTargetAndWait);
                    }
                    else
                    {
                        _runtime.CancelTargeting();
                        CompleteCurrentOperation();
                    }

                    break;
            }
        }

        private Action _resumeAfterDelay;

        private void BeginDelay(Action resumeAction)
        {
            _resumeAfterDelay = resumeAction;
            int delay = Math.Max(0, _currentOperation.Definition.Delay);
            _delayUntil = _timeProvider() + delay;
            SetState(RunnerState.WaitingForDelay);
        }

        private void HandleWaitingForDelay()
        {
            if (_timeProvider() < _delayUntil)
                return;

            Action resume = _resumeAfterDelay;
            _resumeAfterDelay = null;
            resume?.Invoke();
        }

        private void ResumeTargetEachCycle()
        {
            ArmGumpAndUseTome();
            _runtime.ArmAutoTarget(_currentItemSerial);
            SetState(RunnerState.WaitingForGump);
        }

        private void ArmRepeatTargetAndWait()
        {
            _runtime.ArmAutoTarget(_currentItemSerial);
            SetState(RunnerState.WaitingForTarget);
        }

        private bool TryDequeueNextItem(out uint serial)
        {
            while (_pendingItemSerials.Count > 0)
            {
                serial = _pendingItemSerials.Dequeue();
                if (serial != 0)
                    return true;
            }

            serial = 0;
            return false;
        }

        private void ArmGumpAndUseTome()
        {
            TomeDefinition definition = _currentOperation.Definition;
            _runtime.ArmNextGump(definition.GumpId, definition.AddButtonId);
            _runtime.UseTome(definition.TomeSerial);
        }

        private void FailCurrentOperation(string reason)
        {
            int moved = _movedInCurrentOperation;
            int skipped = Math.Max(0, GetOperationItemCount() - moved);
            CloseCurrentTomeGump();
            _runtime.CancelTargeting();
            _runtime.ResetPendingAutomation();
            _currentOperation.OnFinished?.Invoke(new TomeOperationResult(false, moved, skipped, reason));
            ResetToIdle();
        }

        private void CompleteCurrentOperation()
        {
            int moved = _movedInCurrentOperation;
            int skipped = Math.Max(0, GetOperationItemCount() - moved);
            CloseCurrentTomeGump();
            _runtime.ResetPendingAutomation();
            _currentOperation.OnFinished?.Invoke(new TomeOperationResult(true, moved, skipped, string.Empty));
            ResetToIdle();
        }

        private void CloseCurrentTomeGump()
        {
            uint gumpId = _currentOperation?.Definition?.GumpId ?? 0;
            uint tomeSerial = _currentOperation?.Definition?.TomeSerial ?? 0;
            _runtime.CloseTomeGump(gumpId, tomeSerial);
        }

        private int GetOperationItemCount()
        {
            if (_currentOperation?.ItemSerials == null)
                return 0;

            return _currentOperation.ItemSerials.Count;
        }

        private void ResetToIdle()
        {
            _currentOperation = null;
            _pendingItemSerials = null;
            _currentItemSerial = 0;
            _resumeAfterDelay = null;
            _delayUntil = 0;
            _walkingRequested = false;
            SetState(RunnerState.Idle);
        }

        private bool IsTimedState(RunnerState state)
        {
            return state == RunnerState.WalkingToTome
                   || state == RunnerState.WaitingForGump
                   || state == RunnerState.WaitingForTarget;
        }

        private bool IsTimedOut(int timeoutMs)
        {
            return _timeProvider() - _stateEnteredAt >= timeoutMs;
        }

        private void SetState(RunnerState state)
        {
            _state = state;
            _stateEnteredAt = _timeProvider();
        }

        private enum RunnerState
        {
            Idle = 0,
            WalkingToTome = 1,
            WaitingForGump = 2,
            WaitingForTarget = 3,
            WaitingForDelay = 4
        }
    }

    internal sealed class TomeOperation
    {
        public TomeDefinition Definition { get; init; }
        public List<uint> ItemSerials { get; init; } = new();
        public Action<TomeOperationResult> OnFinished { get; init; }
    }

    internal readonly struct TomeOperationResult
    {
        public TomeOperationResult(bool success, int moved, int skipped, string reason)
        {
            Success = success;
            Moved = moved;
            Skipped = skipped;
            Reason = reason ?? string.Empty;
        }

        public bool Success { get; }
        public int Moved { get; }
        public int Skipped { get; }
        public string Reason { get; }
    }

    internal interface ITomeRuntime
    {
        bool IsGumpActionPending { get; }
        bool IsAutoTargetPending { get; }
        void ArmNextGump(uint gumpId, int buttonId);
        void ArmAutoTarget(uint targetSerial);
        void UseTome(uint tomeSerial);
        bool RequestWalkToTome(uint tomeSerial);
        bool IsWithinTomeRange(uint tomeSerial);
        uint GetPlayerSerial();
        void CancelTargeting();
        void ResetPendingAutomation();
        void CloseTomeGump(uint gumpId, uint tomeSerial);
    }

    internal sealed class LiveTomeRuntime : ITomeRuntime
    {
        public bool IsGumpActionPending => NextGumpConfig.Enabled;

        public bool IsAutoTargetPending => TargetManager.NextAutoTarget.IsSet;

        public void ArmNextGump(uint gumpId, int buttonId)
        {
            NextGumpConfig.Reset();

            // Never arm wildcard gump automation. Serial==0 matches any server gump.
            if (gumpId == 0)
                return;

            NextGumpConfig.Enabled = true;
            NextGumpConfig.Serial = gumpId;
            NextGumpConfig.AutoClose = true;
            NextGumpConfig.AutoRespondButton = buttonId;
            NextGumpConfig.AutoRespond = true;
        }

        public void ArmAutoTarget(uint targetSerial)
        {
            TargetManager.NextAutoTarget.Set(targetSerial);
        }

        public void UseTome(uint tomeSerial)
        {
            ObjectActionQueueItem action = ObjectActionQueueItem.DoubleClick(tomeSerial, true);
            if (action != null)
                ObjectActionQueue.Instance.Enqueue(action, ActionPriority.UseItem);
        }

        public bool RequestWalkToTome(uint tomeSerial)
        {
            World world = World.Instance;
            Item tome = world?.Items.Get(tomeSerial);
            if (world?.Player == null || tome == null)
                return false;

            return world.Player.Pathfinder.WalkTo(tome.X, tome.Y, tome.Z, 1);
        }

        public bool IsWithinTomeRange(uint tomeSerial)
        {
            Item tome = World.Instance?.Items.Get(tomeSerial);
            if (tome == null)
                return false;

            return tome.Distance <= 2;
        }

        public uint GetPlayerSerial()
        {
            return World.Instance?.Player?.Serial ?? 0;
        }

        public void CancelTargeting()
        {
            World.Instance?.TargetManager.CancelTarget();
        }

        public void ResetPendingAutomation()
        {
            TargetManager.NextAutoTarget.Clear();
            NextGumpConfig.Reset();
        }

        public void CloseTomeGump(uint gumpId, uint tomeSerial)
        {
            World world = World.Instance;
            if (world == null)
                return;

            if (gumpId == 0)
                return;

            Gump gump = UIManager.GetGumpServer(gumpId) ?? UIManager.GetGump(gumpId);

            if (gump != null)
            {
                if (gump.ServerSerial != 0)
                    GameActions.ReplyGump(world, gump.LocalSerial, gump.ServerSerial, 0);

                gump.Dispose();
            }
        }
    }
}
