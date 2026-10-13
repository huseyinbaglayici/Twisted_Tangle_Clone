using System;
using Reflex.Attributes;
using Runtime.Boostrap;
using UnityEngine;

namespace Runtime.Gameplay.PowerUps
{
    // Owns the per-level counts and the single power-up in use. While one is in use the level timer holds and pins
    // can't be dragged; from Started on, natural merges and the win check wait until it finishes.
    public class PowerUpController : MonoBehaviour
    {
        [Inject] private SceneFlowService _sceneFlow;

        [SerializeField] private LevelBuilder level;
        [SerializeField] private PinInputController input;
        [SerializeField] private WinChecker winChecker;
        [SerializeField] private LevelTimer timer;
        [SerializeField] private ScissorsPowerUp scissors;
        [SerializeField] private int countPerLevel = 3;

        private readonly int[] _counts = new int[Enum.GetValues(typeof(PowerUpType)).Length];
        private bool _levelRunning;
        private bool _boardBusy;

        public PowerUpType Active { get; private set; }
        public PowerUpState State { get; private set; }

        // Any count, state or availability change; the HUD redraws its buttons from it.
        public event Action Changed;

        public bool IsInUse => State != PowerUpState.Idle;

        public int Count(PowerUpType type) => _counts[(int)type];

        public void BeginLevel()
        {
            Cancel();
            for (int i = 0; i < _counts.Length; i++) _counts[i] = countPerLevel;
            _levelRunning = true;
            Changed?.Invoke();
        }

        public void EndLevel()
        {
            Cancel();
            _levelRunning = false;
            Changed?.Invoke();
        }

        public bool CanRequest(PowerUpType type) =>
            _levelRunning && !_boardBusy && !IsInUse && Count(type) > 0 && IsUsable(type);

        // Same button again while waiting for a target or confirm cancels it.
        public void Toggle(PowerUpType type)
        {
            if (State == PowerUpState.Requested && Active == type)
            {
                Cancel();
                return;
            }

            if (!CanRequest(type)) return;
            Active = type;
            SetState(PowerUpState.Requested);
            input.Blocked = true;
            timer.Held = true;
            switch (type)
            {
                case PowerUpType.Scissors: scissors.Request(); break;
            }
        }

        public void Confirm()
        {
            if (State != PowerUpState.Requested) return;
        }

        // Called by the power-up once its effect really starts: the count is spent here, not on request.
        public void Begin()
        {
            _counts[(int)Active]--;
            timer.StartCountdown();
            winChecker.MergesBlocked = Active != PowerUpType.TimeFreeze;
            SetState(PowerUpState.Started);
        }

        public void Perform() => SetState(PowerUpState.Performed);

        public void Finish() => End();

        public void Cancel()
        {
            if (!IsInUse) return;
            switch (Active)
            {
                case PowerUpType.Scissors: scissors.Cancel(); break;
            }
            End();
        }

        private void End()
        {
            winChecker.MergesBlocked = false;
            input.Blocked = false;
            timer.Held = false;
            SetState(PowerUpState.Idle);
        }

        private bool IsUsable(PowerUpType type) => true;

        private void SetState(PowerUpState state)
        {
            State = state;
            Changed?.Invoke();
        }

        private void Update()
        {
            bool busy = _sceneFlow.IsBusy || winChecker.Succeeded || IsAnyRopeMerging();
            if (busy == _boardBusy) return;
            _boardBusy = busy;
            Changed?.Invoke();
        }

        private bool IsAnyRopeMerging()
        {
            foreach (var rope in level.Board.Ropes)
                if (rope.Merging) return true;
            return false;
        }
    }
}
