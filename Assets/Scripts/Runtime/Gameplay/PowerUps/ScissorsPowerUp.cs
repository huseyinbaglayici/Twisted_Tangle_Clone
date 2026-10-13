using System.Collections.Generic;
using DG.Tweening;
using Reflex.Attributes;
using Runtime.Audio;
using Runtime.Effects;
using Runtime.Entities;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Runtime.Gameplay.PowerUps
{
    // Requested: every rope blinks and the next tap on a rope picks it. The scissors fly in, the rope is torn when
    // the blades snap, force-merged a moment later, and the power-up finishes once the merge is well under way.
    public class ScissorsPowerUp : MonoBehaviour
    {
        [Inject] private FxPlayer _fx;
        [Inject] private AudioPlayer _audio;

        [SerializeField] private PowerUpController controller;
        [SerializeField] private LevelBuilder level;
        [SerializeField] private WinChecker winChecker;
        [SerializeField] private ScissorsAnimator scissors;
        [SerializeField] private Camera cam;

        [Tooltip("Tap distance to a rope's projected segments, as a fraction of the screen width.")]
        [SerializeField] private float pickDistance = 0.025f;
        [Tooltip("Rest length scale of the two torn pieces, so they pull back towards their pins.")]
        [SerializeField] private float cutShrink = 0.5f;
        [SerializeField] private float mergeDelay = 0.45f;
        [SerializeField] private float finishDelay = 1f;

        private readonly List<Vector3> _points = new();
        private bool _picking;
        private RopeEntity _target;
        private int _segment;
        private Vector3 _cutPoint;
        private Sequence _sequence;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
        }

        public void Request()
        {
            _picking = true;
            SetBlinkAll(true);
        }

        public void Cancel()
        {
            _picking = false;
            _sequence?.Kill();
            _sequence = null;
            SetBlinkAll(false);
            scissors.Stop();
            _target = null;
        }

        private void Update()
        {
            if (!_picking || !Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (!TryPick(Input.mousePosition, out _target, out _segment, out _cutPoint)) return;

            _picking = false;
            controller.Begin();
            SetBlinkAll(false);
            _target.SetBlink(true);
            scissors.Play(_cutPoint, OnCut);
        }

        private void OnCut()
        {
            controller.Perform();
            var rope = _target != null ? _target.Model : null;
            if (rope == null || rope.Merged || rope.Merging)
            {
                controller.Finish();
                return;
            }

            _target.SetBlink(false);
            _target.Cut(_segment, cutShrink);
            _fx.Play(FxType.ScissorsCut, _cutPoint);
            _audio.Play(SfxType.ScissorsCut);
            _sequence = DOTween.Sequence().SetTarget(this)
                .AppendInterval(mergeDelay)
                .AppendCallback(() => winChecker.ForceMerge(rope))
                .AppendInterval(Mathf.Max(0f, finishDelay - mergeDelay))
                .AppendCallback(() =>
                {
                    _target = null;
                    controller.Finish();
                });
        }

        // Closest projected rope segment to the tap, within pickDistance.
        private bool TryPick(Vector2 screen, out RopeEntity best, out int segment, out Vector3 point)
        {
            best = null;
            segment = -1;
            point = default;
            float bestDistance = pickDistance * Screen.width;

            foreach (var view in level.RopeViews.Values)
            {
                var model = view.Model;
                if (model == null || model.Merged || model.Merging || !view.IsReady) continue;
                view.CopyParticlePositions(_points);
                for (int i = 0; i + 1 < _points.Count; i++)
                {
                    Vector3 a = cam.WorldToScreenPoint(_points[i]), b = cam.WorldToScreenPoint(_points[i + 1]);
                    float d = DistanceToSegment(screen, a, b, out float t);
                    if (d >= bestDistance) continue;
                    bestDistance = d;
                    best = view;
                    segment = i;
                    point = Vector3.Lerp(_points[i], _points[i + 1], t);
                }
            }
            return best != null;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            var ab = b - a;
            float len = ab.sqrMagnitude;
            t = len > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        private void SetBlinkAll(bool on)
        {
            foreach (var view in level.RopeViews.Values)
                view.SetBlink(on && view.Model != null && !view.Model.Merged && !view.Model.Merging);
        }

        private void OnDestroy() => _sequence?.Kill();
    }
}
