using System.Threading;
using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using Runtime.Boostrap;
using Runtime.Boostrap.Save;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // Level loop: load saved level -> build -> physics win/merge check -> on win save + build the next one.
    public class LevelFlowController : MonoBehaviour
    {
        [Inject] private SaveService _save;
        [Inject] private SceneFlowService _sceneFlow;

        [SerializeField] private LevelBuilder levelBuilder;
        [SerializeField] private PinInputController input;
        [SerializeField] private MapFinishChecker finishChecker;
        [SerializeField] private TutorialController tutorial;
        [SerializeField] private float winDelay = 0.6f;
        [SerializeField] private float failRetryDelay = 1f;
        [Tooltip("0 = continue from saved progress. Set a number to always start on that level (testing).")]
        [SerializeField] private int startLevelOverride;

        private LevelRepository _levels;
        private CancellationTokenSource _pendingStart;
        private bool _finished;

        public int CurrentLevel { get; private set; }

        private void Start()
        {
            finishChecker.Won += OnWon;
            finishChecker.OutOfMoves += OnOutOfMoves;

            _levels = new LevelRepository();
            int saved = _save.Level;
            StartLevel(startLevelOverride > 0 ? startLevelOverride : Mathf.Max(1, saved));
            _sceneFlow.NotifyGameplayReady();
        }

        private void OnDestroy()
        {
            finishChecker.Won -= OnWon;
            finishChecker.OutOfMoves -= OnOutOfMoves;
            CancelPendingStart();
        }

        public void StartLevel(int levelNumber)
        {
            CancelPendingStart();
            tutorial.End();
            _finished = false;
            CurrentLevel = levelNumber;
            input.ResetState();
            var level = _levels.Load(levelNumber);
            levelBuilder.Build(level);
            finishChecker.Begin();
            tutorial.Begin(level);
            input.Enabled = true;
        }

        public void Retry() => StartLevel(CurrentLevel);

        public void ExitToMenu()
        {
            _finished = true;
            input.Enabled = false;
            finishChecker.Stop();
            tutorial.End();
            _sceneFlow.LoadMainMenu().Forget();
        }

        // Escape = Android back button.
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) ExitToMenu();
        }

        public void Undo()
        {
            if (!_finished) input.Undo();
        }

        private void OnWon()
        {
            if (_finished) return;
            _finished = true;
            input.Enabled = false;
            tutorial.End();
            _save.Level = CurrentLevel + 1;
            StartAfterDelay(CurrentLevel + 1, winDelay).Forget();
        }

        // No fail popup yet: restart the same level.
        private void OnOutOfMoves()
        {
            if (_finished) return;
            _finished = true;
            input.Enabled = false;
            tutorial.End();
            StartAfterDelay(CurrentLevel, failRetryDelay).Forget();
        }

        private async UniTaskVoid StartAfterDelay(int levelNumber, float delay)
        {
            CancelPendingStart();
            _pendingStart = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            bool cancelled = await UniTask.Delay((int)(delay * 1000), cancellationToken: _pendingStart.Token)
                .SuppressCancellationThrow();
            if (!cancelled) StartLevel(levelNumber);
        }

        private void CancelPendingStart()
        {
            _pendingStart?.Cancel();
            _pendingStart?.Dispose();
            _pendingStart = null;
        }
    }
}
