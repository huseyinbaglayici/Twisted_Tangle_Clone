using Reflex.Attributes;
using Runtime.Audio;
using Runtime.Effects;
using Runtime.Entities;
using UnityEngine;

namespace Runtime.Gameplay
{
    public class GameplayFeedback : MonoBehaviour
    {
        [Inject] private FxPlayer _fx;
        [Inject] private AudioPlayer _audio;

        [SerializeField] private LevelFlowController flow;
        [SerializeField] private LevelBuilder level;
        [SerializeField] private PinInputController input;
        [SerializeField] private RopeMergeAnimator merge;
        [SerializeField] private Vector3 confettiOffset = new(0f, 1f, 0f);

        // Awake, not Start: the flow already starts the first level in its own Start.
        private void Awake()
        {
            flow.LevelStarted += OnLevelStarted;
            flow.LevelWon += OnLevelWon;
            input.PinPicked += OnPinPicked;
            input.PinDropped += OnPinDropped;
            merge.MergeStarted += OnMergeStarted;
            merge.MergePopped += OnMergePopped;
        }

        private void OnDestroy()
        {
            flow.LevelStarted -= OnLevelStarted;
            flow.LevelWon -= OnLevelWon;
            input.PinPicked -= OnPinPicked;
            input.PinDropped -= OnPinDropped;
            merge.MergeStarted -= OnMergeStarted;
            merge.MergePopped -= OnMergePopped;
            if (_fx != null) _fx.DespawnAll();
        }

        private void OnLevelStarted(int levelNumber)
        {
            _fx.DespawnAll();
            _audio.Play(SfxType.LevelStart);
        }

        private void OnLevelWon(int levelNumber)
        {
            _fx.Play(FxType.LevelWinConfetti, level.Grid.Origin + confettiOffset);
            _audio.Play(SfxType.LevelComplete);
        }

        private void OnPinPicked(PinEntity pin)
        {
            _fx.Play(FxType.PinPick, pin.transform.position);
            _audio.Play(SfxType.PinPick);
        }

        private void OnPinDropped(PinEntity pin)
        {
            _fx.Play(FxType.PinDrop, level.Grid.CellToWorld(pin.Model.Cell));
            _audio.Play(SfxType.PinDrop);
        }

        private void OnMergeStarted(Vector3 position) => _audio.Play(SfxType.RopeMerge);

        private void OnMergePopped(Vector3 position, Color ropeColor) => _fx.Play(FxType.MergePop, position, ropeColor);
    }
}
