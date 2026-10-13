using System;
using UnityEngine;

namespace Runtime.Gameplay
{
    // At zero it waits until nothing is held or merging for timeUpGrace, since a last merge can still win.
    // Paused (a popup is open) stops everything, the freeze countdown too; Held (a power-up in use) and an active
    // freeze only stop the level clock.
    public class LevelTimer : MonoBehaviour
    {
        [SerializeField] private PinInputController input;
        [SerializeField] private LevelBuilder level;
        [SerializeField] private float timeUpGrace = 0.2f;

        private bool _counting, _checking, _stopped;
        private float _secondTimer, _graceTimer;

        public event Action<int> SecondsChanged;
        public event Action TimeUp;

        public bool HasLimit { get; private set; }
        public int SecondsLeft { get; private set; }
        public bool Paused { get; set; }
        public bool Held { get; set; }
        public float FreezeDuration { get; private set; }
        public float FreezeRemaining { get; private set; }
        public bool IsFreezeActive => FreezeRemaining > 0f;

        public void Begin(int seconds)
        {
            HasLimit = seconds > 0;
            SecondsLeft = Mathf.Max(0, seconds);
            _counting = _checking = _stopped = false;
            _secondTimer = _graceTimer = 0f;
            FreezeRemaining = 0f;
            SecondsChanged?.Invoke(HasLimit ? SecondsLeft : -1);
        }

        public void Stop()
        {
            _stopped = true;
            _counting = _checking = false;
            FreezeRemaining = 0f;
        }

        // Using a power-up counts as the first move.
        public void StartCountdown()
        {
            if (HasLimit && !_stopped && !_checking) _counting = true;
        }

        public void FreezeFor(float seconds)
        {
            FreezeDuration = seconds;
            FreezeRemaining = seconds;
        }

        private void Update()
        {
            if (!HasLimit || _stopped || Paused) return;
            float dt = Time.deltaTime;
            if (IsFreezeActive)
            {
                FreezeRemaining = Mathf.Max(0f, FreezeRemaining - dt);
                return;
            }
            if (Held) return;

            if (!_counting && !_checking && input.IsHoldingPin) _counting = true;
            if (_counting) Countdown(dt);

            if (!_checking || IsBoardBusy()) return;
            _graceTimer += dt;
            if (_graceTimer < timeUpGrace) return;
            Stop();
            TimeUp?.Invoke();
        }

        private void Countdown(float dt)
        {
            _secondTimer += dt;
            if (_secondTimer < 1f) return;
            _secondTimer = 0f;
            SecondsLeft = Mathf.Max(0, SecondsLeft - 1);
            SecondsChanged?.Invoke(SecondsLeft);
            if (SecondsLeft > 0) return;
            _counting = false;
            _checking = true;
            _graceTimer = 0f;
        }

        private bool IsBoardBusy()
        {
            if (input.IsHoldingPin) return true;
            foreach (var rope in level.Board.Ropes)
                if (rope.Merging) return true;
            return false;
        }
    }
}
