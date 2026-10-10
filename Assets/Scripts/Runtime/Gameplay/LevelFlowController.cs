using System;
using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using Runtime.Boostrap;
using Runtime.Boostrap.Save;
using Runtime.Effects;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // Level loop: load saved level -> build -> physics win/merge check. A win saves progress and waits for the
    // UI (Continue -> back to the menu, which then opens the next level), running out of time waits for Retry.
    // Nothing advances by itself.
    public class LevelFlowController : MonoBehaviour
    {
        [Inject] private SaveService _save;
        [Inject] private SceneFlowService _sceneFlow;
        [Inject] private FxPlayer _fx;

        [SerializeField] private LevelBuilder levelBuilder;
        [SerializeField] private PinInputController input;
        [SerializeField] private MapFinishChecker finishChecker;
        [SerializeField] private TutorialController tutorial;
        [SerializeField] private LevelTimer timer;
        [Tooltip("0 = continue from saved progress. Set a number to always start on that level (testing).")]
        [SerializeField] private int startLevelOverride;

        private LevelRepository _levels;
        private bool _finished;

        public int CurrentLevel { get; private set; }

        // Level number as shown to the player (keeps counting past the last level file).
        public event Action<int> LevelStarted;
        public event Action<int> LevelWon;
        public event Action<int> LevelFailed;

        private void Start()
        {
            finishChecker.Won += OnWon;
            timer.TimeUp += OnOutOfTime;

            _levels = new LevelRepository();
            int saved = _save.Level;
            StartLevel(startLevelOverride > 0 ? startLevelOverride : Mathf.Max(1, saved));
            _sceneFlow.NotifyGameplayReady();
        }

        private void OnDestroy()
        {
            finishChecker.Won -= OnWon;
            timer.TimeUp -= OnOutOfTime;
        }

        public void StartLevel(int levelNumber)
        {
            tutorial.End();
            _finished = false;
            CurrentLevel = levelNumber;
            input.ResetState();
            var level = _levels.Load(levelNumber);
            levelBuilder.Build(level);
            finishChecker.Begin();
            timer.Begin(level.timeLimitSeconds);
            tutorial.Begin(level);
            input.Enabled = true;
            LevelStarted?.Invoke(levelNumber);
        }

        // Rebuilt behind the fader so the old board never visibly pops into the new one.
        public void Retry()
        {
            if (_sceneFlow.IsBusy) return;
            Lock();
            _sceneFlow.Transition(() => StartLevel(CurrentLevel)).Forget();
        }

        // Home, back button and Continue after a win (progress is already saved by then).
        public void ExitToMenu()
        {
            if (_sceneFlow.IsBusy) return;
            Lock();
            _sceneFlow.LoadMainMenu(ReleaseLevel).Forget();
        }

        // Every pooled view goes back to its pool before Gameplay unloads; pool holders and the solver live in
        // Boostrap, so nothing pooled is destroyed with this scene.
        private void ReleaseLevel()
        {
            levelBuilder.Clear();
            _fx.DespawnAll();
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
            if (!Finish()) return;
            _save.Level = CurrentLevel + 1;
            LevelWon?.Invoke(CurrentLevel);
        }

        private void OnOutOfTime()
        {
            if (Finish()) LevelFailed?.Invoke(CurrentLevel);
        }

        // Locks the board until the player picks Continue / Retry / Home.
        private bool Finish()
        {
            if (_finished) return false;
            Lock();
            return true;
        }

        private void Lock()
        {
            _finished = true;
            input.Enabled = false;
            finishChecker.Stop();
            timer.Stop();
            tutorial.End();
        }
    }
}
