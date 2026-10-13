using DG.Tweening;
using Reflex.Attributes;
using Runtime.Audio;
using UnityEngine;

namespace Runtime.Gameplay.PowerUps
{
    // Confirm shows the fan; a beat later the board flips over and the power-up finishes when it lands.
    public class ShufflePowerUp : MonoBehaviour
    {
        [Inject] private AudioPlayer _audio;

        [SerializeField] private PowerUpController controller;
        [SerializeField] private MapFlipper flipper;
        [SerializeField] private FanAnimator fan;
        [SerializeField] private TutorialController tutorial;
        [SerializeField] private float actionDelay = 0.45f;
        [SerializeField] private float fanDuration = 2f;

        private Tween _delay;

        public void Confirm()
        {
            controller.Begin();
            fan.Play(fanDuration);
            _delay = DOVirtual.DelayedCall(actionDelay, Flip).SetTarget(this);
        }

        public void Cancel()
        {
            _delay?.Kill();
            _delay = null;
            fan.Stop();
            flipper.Stop();
        }

        private void Flip()
        {
            _delay = null;
            controller.Perform();
            // The hint points at a slot that is about to move.
            tutorial.End();
            _audio.Play(SfxType.Shuffle);
            flipper.Flip(controller.Finish);
        }

        private void OnDestroy() => _delay?.Kill();
    }
}
