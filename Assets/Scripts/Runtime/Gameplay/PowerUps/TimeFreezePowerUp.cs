using System;
using DG.Tweening;
using UnityEngine;

namespace Runtime.Gameplay.PowerUps
{
    // Confirm launches the ice missile; when it reaches the clock the level timer freezes and the power-up is done
    // (the button stays dimmed until the freeze runs out).
    public class TimeFreezePowerUp : MonoBehaviour
    {
        [SerializeField] private PowerUpController controller;
        [SerializeField] private LevelTimer timer;
        [SerializeField] private float flightDuration = 0.7f;
        [SerializeField] private float freezeDuration = 10f;

        private Tween _flight;

        public event Action Launched;
        public event Action Landed;

        public float FlightDuration => flightDuration;

        public void Confirm()
        {
            controller.Begin();
            Launched?.Invoke();
            _flight = DOVirtual.DelayedCall(flightDuration, Land).SetTarget(this);
        }

        public void Cancel()
        {
            _flight?.Kill();
            _flight = null;
        }

        private void Land()
        {
            _flight = null;
            controller.Perform();
            timer.FreezeFor(freezeDuration);
            controller.Finish();
            Landed?.Invoke();
        }

        private void OnDestroy() => _flight?.Kill();
    }
}
