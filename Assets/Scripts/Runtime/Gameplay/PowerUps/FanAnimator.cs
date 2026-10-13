using DG.Tweening;
using UnityEngine;

namespace Runtime.Gameplay.PowerUps
{
    // The fan that "blows" the board over: pops in, sways and spins its rotor, pops out.
    public class FanAnimator : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform head;
        [SerializeField] private Transform rotor;
        [SerializeField] private float popDuration = 0.2f;
        [SerializeField] private float swayAngle = 12f;
        [SerializeField] private float swayDuration = 0.4f;
        [SerializeField] private float rotorTurnsPerSecond = 4f;

        private Sequence _sequence;
        private Quaternion _headRest;
        private Vector3 _scale;

        private void Awake()
        {
            _headRest = head.localRotation;
            _scale = body.localScale;
            body.gameObject.SetActive(false);
        }

        public void Play(float showDuration)
        {
            Stop();
            body.gameObject.SetActive(true);
            body.localScale = Vector3.zero;
            head.localRotation = _headRest * Quaternion.Euler(0f, -swayAngle, 0f);
            float turns = rotorTurnsPerSecond * showDuration;
            _sequence = DOTween.Sequence().SetTarget(this)
                .Insert(0f, body.DOScale(_scale, popDuration).SetEase(Ease.OutBack))
                .Insert(0f, head.DOLocalRotateQuaternion(_headRest * Quaternion.Euler(0f, swayAngle, 0f), swayDuration)
                    .SetEase(Ease.InOutSine).SetLoops(Mathf.Max(1, Mathf.RoundToInt(showDuration / swayDuration)), LoopType.Yoyo))
                .Insert(0f, rotor.DOLocalRotate(new Vector3(0f, 0f, 360f * turns), showDuration, RotateMode.LocalAxisAdd)
                    .SetEase(Ease.InOutQuad))
                .Insert(showDuration - popDuration, body.DOScale(0f, popDuration).SetEase(Ease.InBack))
                .OnComplete(Stop);
        }

        public void Stop()
        {
            _sequence?.Kill();
            _sequence = null;
            body.gameObject.SetActive(false);
            body.localScale = _scale;
            head.localRotation = _headRest;
        }

        private void OnDestroy() => _sequence?.Kill();
    }
}
