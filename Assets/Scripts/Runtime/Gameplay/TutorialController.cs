using DG.Tweening;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // The hand only suggests a move: it hides while a pin is held and comes back after the drop
    // until the level ends.
    public class TutorialController : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private PinInputController input;
        [SerializeField] private Transform hand;
        [SerializeField] private float handHeight = 1.2f;

        [Header("Hand animation")]
        [SerializeField] private float moveSourceDuration = 0.25f;
        [SerializeField] private float moveTargetDuration = 0.75f;
        [SerializeField] private float downScale = 0.6f;
        [SerializeField] private float scaleDuration = 0.3f;
        [SerializeField] private float delay = 0.1f;
        [SerializeField] private float showAgainDelay = 0.75f;

        private BoardModel.Pin _pin;
        private Vector3 _target;
        private Vector3 _handScale;
        private Sequence _sequence;
        private float _showAt;

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
            Show();
        }

        public void End()
        {
            Hide();
            _pin = null;
        }

        private void Update()
        {
            if (_pin == null) return;

            if (input.IsHoldingPin)
            {
                if (hand.gameObject.activeSelf) Hide();
                _showAt = Time.time + showAgainDelay;
            }
            else if (!hand.gameObject.activeSelf && Time.time >= _showAt)
            {
                Show();
            }
        }

        private void Show()
        {
            if (_pin.Removed)
            {
                End();
                return;
            }

            hand.position = PinPosition() + Vector3.up * handHeight;
            hand.localScale = _handScale;
            hand.gameObject.SetActive(true);
            PlayCycle();
        }

        private void Hide()
        {
            _sequence?.Kill();
            _sequence = null;
            if (hand != null) hand.gameObject.SetActive(false);
        }

        // Go to the pin (it may have moved), press, drag to the target slot, release, wait.
        private void PlayCycle()
        {
            if (_pin == null || _pin.Removed)
            {
                End();
                return;
            }

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

        private void OnDestroy() => End();
    }
}
