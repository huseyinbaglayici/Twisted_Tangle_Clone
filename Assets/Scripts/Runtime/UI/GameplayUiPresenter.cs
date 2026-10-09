using DG.Tweening;
using Reflex.Attributes;
using Runtime.Audio;
using Runtime.Boostrap.Save;
using Runtime.Gameplay;
using UnityEngine;

namespace Runtime.UI
{
    // Connects the gameplay UI views to the level flow: shows level / moves left, routes button clicks,
    // opens the complete / fail panels and stores the settings switches.
    public class GameplayUiPresenter : MonoBehaviour
    {
        [Inject] private SaveService _save;

        [Header("Gameplay (looked up in the scene when left empty)")]
        [SerializeField] private LevelFlowController flow;
        [SerializeField] private MapFinishChecker finishChecker;
        [SerializeField] private LevelBuilder levelBuilder;
        [SerializeField] private AudioPlayer audioPlayer;

        [Header("Views")]
        [SerializeField] private GameplayHud hud;
        [SerializeField] private LevelCompletePanel completePanel;
        [SerializeField] private LevelFailPanel failPanel;
        [SerializeField] private SettingsPopup settings;

        [Header("Timing")]
        [SerializeField] private float completePanelDelay = 0.3f;
        [SerializeField] private float failPanelDelay = 0.1f;

        private int _shownLevel = -1;
        private int _shownMovesLeft = -1;
        private int _resultLevel = -1;
        private Tween _pendingPanel;

        private void Awake()
        {
            if (flow == null) flow = FindFirstObjectByType<LevelFlowController>();
            if (finishChecker == null) finishChecker = FindFirstObjectByType<MapFinishChecker>();
            if (levelBuilder == null) levelBuilder = FindFirstObjectByType<LevelBuilder>();
            if (audioPlayer == null) audioPlayer = FindFirstObjectByType<AudioPlayer>();
        }

        private void Start()
        {
            hud.RetryClicked += OnRetryClicked;
            hud.UndoClicked += OnUndoClicked;
            hud.SettingsClicked += OnSettingsClicked;
            completePanel.ContinueClicked += OnContinueClicked;
            failPanel.RetryClicked += OnFailRetryClicked;
            failPanel.HomeClicked += OnHomeClicked;
            settings.HomeClicked += OnHomeClicked;
            settings.SoundChanged += OnSoundChanged;
            settings.HapticsChanged += OnHapticsChanged;
            settings.Closed += PlayClick;

            if (finishChecker != null)
            {
                finishChecker.Won += OnWon;
                finishChecker.OutOfMoves += OnOutOfMoves;
            }

            completePanel.Hide(true);
            failPanel.Hide(true);
            settings.Hide(true);
            hud.Show();
            Refresh();
        }

        private void OnDestroy()
        {
            _pendingPanel?.Kill();
            if (finishChecker == null) return;
            finishChecker.Won -= OnWon;
            finishChecker.OutOfMoves -= OnOutOfMoves;
        }

        private void Update() => Refresh();

        // Polls the flow: a new level number, or moves left going back up (restart), means a level (re)started.
        private void Refresh()
        {
            if (flow == null || levelBuilder == null) return;

            int level = flow.CurrentLevel;
            var board = levelBuilder.Board;
            int movesLeft = board.MoveLimit > 0 ? Mathf.Max(0, board.MoveLimit - board.MovesUsed) : -1;

            if (level != _shownLevel || movesLeft > _shownMovesLeft) OnLevelStarted(level);
            _shownLevel = level;
            _shownMovesLeft = movesLeft;
            hud.SetMoves(movesLeft);
        }

        private void OnLevelStarted(int level)
        {
            _pendingPanel?.Kill();
            _resultLevel = -1;
            hud.SetLevel(level);
            if (completePanel.IsVisible) completePanel.Hide();
            if (failPanel.IsVisible) failPanel.Hide();
        }

        // ---------- level result ----------
        private void OnWon()
        {
            int level = flow.CurrentLevel;
            _resultLevel = level;
            _pendingPanel?.Kill();
            _pendingPanel = DOVirtual.DelayedCall(completePanelDelay, () =>
            {
                if (flow.CurrentLevel == level) completePanel.Show(level);
            }).SetLink(gameObject);
        }

        private void OnOutOfMoves()
        {
            int level = flow.CurrentLevel;
            _resultLevel = level;
            _pendingPanel?.Kill();
            _pendingPanel = DOVirtual.DelayedCall(failPanelDelay, () =>
            {
                if (flow.CurrentLevel == level) failPanel.Show(level);
            }).SetLink(gameObject);
        }

        // The flow may already have moved on by itself; only start the next level if it has not.
        private void OnContinueClicked()
        {
            PlayClick();
            completePanel.Hide();
            if (_resultLevel > 0 && flow.CurrentLevel == _resultLevel)
                flow.StartLevel(_save != null ? Mathf.Max(_resultLevel + 1, _save.Level) : _resultLevel + 1);
        }

        private void OnFailRetryClicked()
        {
            PlayClick();
            failPanel.Hide();
            if (_resultLevel > 0) flow.Retry();
        }

        // ---------- hud ----------
        private void OnRetryClicked()
        {
            PlayClick();
            flow.Retry();
        }

        private void OnUndoClicked()
        {
            PlayClick();
            flow.Undo();
        }

        private void OnSettingsClicked()
        {
            PlayClick();
            bool soundOn = audioPlayer != null ? audioPlayer.SoundOn : _save == null || _save.SoundOn;
            bool hapticsOn = _save == null || _save.HapticsOn;
            settings.Show(soundOn, hapticsOn);
        }

        private void OnHomeClicked()
        {
            PlayClick();
            flow.ExitToMenu();
        }

        // ---------- settings ----------
        private void OnSoundChanged(bool on)
        {
            if (audioPlayer != null) audioPlayer.SoundOn = on;
            else if (_save != null) _save.SoundOn = on;
            PlayClick();
        }

        private void OnHapticsChanged(bool on)
        {
            if (_save != null) _save.HapticsOn = on;
            PlayClick();
        }

        private void PlayClick()
        {
            if (audioPlayer != null) audioPlayer.Play(SfxType.ButtonClick);
        }
    }
}
