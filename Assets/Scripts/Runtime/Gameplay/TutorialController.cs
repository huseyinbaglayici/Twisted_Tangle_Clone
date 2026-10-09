using System.Collections.Generic;
using DG.Tweening;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // First-level tutorial, driven by the level json "tutorial" block:
    // every other pin is locked, a hand loops from the tutorial pin to the target slot until the level ends.
    // The target is only a hint — the pin can be dropped anywhere, but with one movable pin the solution is forced.
    public class TutorialController : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private Transform hand;             // hidden by default
        [SerializeField] private float handHeight = 1.2f;    // above lifted pins and ropes

        [Header("Hand animation")]
        [SerializeField] private float moveSourceDuration = 0.25f;
        [SerializeField] private float moveTargetDuration = 0.75f;
        [SerializeField] private float downScale = 0.6f;
        [SerializeField] private float scaleDuration = 0.3f;
        [SerializeField] private float delay = 0.1f;

        private readonly List<BoardModel.Pin> _lockedPins = new();
        private BoardModel.Pin _pin;
        private Vector3 _target;
        private Vector3 _handScale;
        private Sequence _sequence;

        public bool IsActive => _pin != null;

        private void Awake()
        {
            _handScale = hand.localScale;
            hand.gameObject.SetActive(false);
        }

        public void Begin(LevelJson json)
        {
            End();
            if (!json.HasTutorial || !level.Board.Pins.TryGetValue(json.tutorial.pinId, out var pin)) return;

            _pin = pin;
            _target = level.Grid.CellToWorld(json.tutorial.TargetCell);

            if (json.tutorial.lockOtherPins)
                foreach (var p in level.Board.Pins.Values)
                    if (p != pin && p.InputEnabled)
                    {
                        p.InputEnabled = false;
                        _lockedPins.Add(p);
                    }

            hand.position = PinPosition() + Vector3.up * handHeight;
            hand.localScale = _handScale;
            hand.gameObject.SetActive(true);
            PlayCycle();
        }

        public void End()
        {
            _sequence?.Kill();
            _sequence = null;
            if (hand != null) hand.gameObject.SetActive(false);

            // Unlock what we locked, unless its rope is already merging away.
            foreach (var p in _lockedPins)
                if (!p.Removed && !IsMerging(p))
                    p.InputEnabled = true;
            _lockedPins.Clear();
            _pin = null;
        }

        // One loop: go to the pin (it may have moved), press, drag to the target slot, release, wait.
        private void PlayCycle()
        {
            if (_pin == null || _pin.Removed) return;
            var up = Vector3.up * handHeight;
            _sequence = DOTween.Sequence().SetTarget(this)
                .Append(hand.DOMove(PinPosition() + up, moveSourceDuration))
                .Append(hand.DOScale(_handScale * downScale, scaleDuration))
                .Append(hand.DOMove(_target + up, moveTargetDuration))
                .Append(hand.DOScale(_handScale, scaleDuration))
                .AppendInterval(delay)
                .OnComplete(PlayCycle);
        }

        private Vector3 PinPosition() =>
            level.PinViews.TryGetValue(_pin.Id, out var view) ? view.transform.position : level.Grid.CellToWorld(_pin.Cell);

        private static bool IsMerging(BoardModel.Pin pin)
        {
            foreach (var r in pin.Ropes)
                if (r.Merging) return true;
            return false;
        }

        private void OnDestroy() => End();
    }
}
