using System;
using UnityEngine;

namespace Runtime.Gameplay
{
    // At zero it waits until nothing is held or merging for timeUpGrace, since a last merge can still win.
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
        public bool Frozen { get; set; }

        public void Begin(int seconds)
        {
            HasLimit = seconds > 0;
            SecondsLeft = Mathf.Max(0, seconds);
            _counting = _checking = _stopped = false;
            _secondTimer = _graceTimer = 0f;
            SecondsChanged?.Invoke(HasLimit ? SecondsLeft : -1);
        }

        public void Stop()
        {
            _stopped = true;
            _counting = _checking = false;
        }

        private void Update()
        {
            if (!HasLimit || _stopped || Frozen) return;
            float dt = Time.deltaTime;

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
