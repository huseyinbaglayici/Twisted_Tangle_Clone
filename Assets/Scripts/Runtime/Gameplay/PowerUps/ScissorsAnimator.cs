using System;
using DG.Tweening;
using UnityEngine;

namespace Runtime.Gameplay.PowerUps
{
    // Flies the scissors model along a curve onto the cut point, opens and snaps the blades, then pops it away.
    // The path comes from the side away from the cut, mirrored for cuts on the right half.
    public class ScissorsAnimator : MonoBehaviour
    {
        [Header("Rig")]
        [SerializeField] private Transform mover;
        [SerializeField] private Transform mirror;
        [SerializeField] private Transform punch;
        [SerializeField] private Transform shrink;
        [SerializeField] private Transform rightBlade;
        [SerializeField] private Transform leftBlade;
        [SerializeField] private TrailRenderer[] trails;

        [Header("Path (offsets from the cut point, coming from +x)")]
        [SerializeField] private Vector3[] pathOffsets =
        {
            new(4.5f, 4f, 1.5f), new(2.2f, 2.2f, 0.9f), new(0.8f, 0.9f, 0.3f), new(0f, 0.35f, 0f)
        };
        [SerializeField] private float pathDuration = 1.2f;
        [SerializeField] private Ease pathEase = Ease.OutSine;

        [Header("Blades")]
        [SerializeField] private float rotateAmount = 35f;
        [SerializeField] private float openDelay = 0.3f;
        [SerializeField] private float openDuration = 0.4f;
        [SerializeField] private float closeDuration = 0.2f;

        [Header("Punch / hide")]
        [SerializeField] private float punchDelay = 0.93f;
        [SerializeField] private Vector3 punchAmount = new(0.1f, 0.2f, 0.1f);
        [SerializeField] private float punchDuration = 1f;
        [SerializeField] private int punchVibrato = 8;
        [SerializeField] private float hideDelay = 1.15f;
        [SerializeField] private float hideDuration = 0.2f;

        private Sequence _sequence;
        private Quaternion _rightRest, _leftRest;

        private void Awake()
        {
            _rightRest = rightBlade.localRotation;
            _leftRest = leftBlade.localRotation;
            Hide();
        }

        // onCut fires when the blades snap shut over the cut point.
        public void Play(Vector3 cutPoint, Action onCut)
        {
            Stop();
            float side = cutPoint.x > 0f ? -1f : 1f;
            var path = new Vector3[pathOffsets.Length];
            for (int i = 0; i < path.Length; i++)
            {
                var o = pathOffsets[i];
                path[i] = cutPoint + new Vector3(o.x * side, o.y, o.z);
            }

            mirror.localScale = new Vector3(side, 1f, 1f);
            mover.position = path[0];
            mover.rotation = Quaternion.LookRotation(path[1] - path[0]);
            punch.localScale = Vector3.one;
            shrink.localScale = Vector3.one;
            rightBlade.localRotation = _rightRest;
            leftBlade.localRotation = _leftRest;
            mover.gameObject.SetActive(true);
            SetTrails(true);

            float snapAt = openDelay + openDuration + closeDuration;
            var open = new Vector3(0f, rotateAmount, 0f);
            _sequence = DOTween.Sequence().SetTarget(this)
                .Insert(0f, mover.DOPath(path, pathDuration, PathType.CatmullRom, PathMode.Full3D, 10)
                    .SetLookAt(0.01f).SetEase(pathEase))
                .Insert(openDelay, rightBlade.DOLocalRotate(open, openDuration).SetEase(Ease.OutQuad))
                .Insert(openDelay, leftBlade.DOLocalRotate(-open, openDuration).SetEase(Ease.OutQuad))
                .Insert(openDelay + openDuration, rightBlade.DOLocalRotateQuaternion(_rightRest, closeDuration).SetEase(Ease.InExpo))
                .Insert(openDelay + openDuration, leftBlade.DOLocalRotateQuaternion(_leftRest, closeDuration).SetEase(Ease.InExpo))
                .InsertCallback(snapAt, () => onCut?.Invoke())
                .Insert(punchDelay, punch.DOPunchScale(punchAmount, punchDuration, punchVibrato, 1f).SetEase(Ease.OutQuad))
                .Insert(hideDelay, shrink.DOScale(0f, hideDuration).SetEase(Ease.InSine))
                .InsertCallback(hideDelay + hideDuration, Hide);
        }

        public void Stop()
        {
            _sequence?.Kill();
            _sequence = null;
            Hide();
        }

        private void Hide()
        {
            SetTrails(false);
            mover.gameObject.SetActive(false);
        }

        private void SetTrails(bool on)
        {
            foreach (var trail in trails)
            {
                if (trail == null) continue;
                trail.Clear();
                trail.emitting = on;
            }
        }

        private void OnDestroy() => _sequence?.Kill();
    }
}
