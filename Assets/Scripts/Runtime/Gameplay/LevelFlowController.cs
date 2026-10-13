using System;
using Cysharp.Threading.Tasks;
using Reflex.Attributes;
using Runtime.Boostrap;
using Runtime.Boostrap.Save;
using Runtime.Effects;
using Runtime.Gameplay.PowerUps;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    public class LevelFlowController : MonoBehaviour
    {
        [Inject] private SaveService _save;
        [Inject] private SceneFlowService _sceneFlow;
        [Inject] private FxPlayer _fx;

        [SerializeField] private LevelBuilder levelBuilder;
        [SerializeField] private PinInputController input;
        [SerializeField] private WinChecker winChecker;
        [SerializeField] private TutorialController tutorial;
        [SerializeField] private LevelTimer timer;
        [SerializeField] private PowerUpController powerUps;
        [Tooltip("0 = saved progress, otherwise always start on this level.")]
        [SerializeField] private int startLevelOverride;

        private LevelRepository _levels;
        private bool _finished;

        public int CurrentLevel { get; private set; }

        public event Action<int> LevelStarted;
        public event Action<int> LevelWon;
        public event Action<int> LevelFailed;

        private void Start()
        {
            winChecker.Won += OnWon;
            timer.TimeUp += OnOutOfTime;

            _levels = new LevelRepository();
            int saved = _save.Level;
            StartLevel(startLevelOverride > 0 ? startLevelOverride : Mathf.Max(1, saved));
            _sceneFlow.NotifyGameplayReady();
        }

        private void OnDestroy()
        {
            winChecker.Won -= OnWon;
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
            winChecker.Begin();
            timer.Begin(level.timeLimitSeconds);
            tutorial.Begin(level);
            powerUps.BeginLevel();
            input.Enabled = true;
            LevelStarted?.Invoke(levelNumber);
        }

        public void Retry()
        {
            if (_sceneFlow.IsBusy) return;
            Lock();
            _sceneFlow.Transition(() => StartLevel(CurrentLevel)).Forget();
        }

        public void ExitToMenu()
        {
            if (_sceneFlow.IsBusy) return;
            Lock();
            _sceneFlow.LoadMainMenu(ReleaseLevel).Forget();
        }

        // Pooled views go back to their pools before Gameplay unloads, otherwise they would be destroyed with it.
        private void ReleaseLevel()
        {
            levelBuilder.Clear();
            _fx.DespawnAll();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) ExitToMenu();
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

        private bool Finish()
        {
            if (_finished) return false;
            Lock();
            return true;
        }

        private void Lock()
        {
            _finished = true;
            powerUps.EndLevel();
            input.Enabled = false;
            winChecker.Stop();
            timer.Stop();
            tutorial.End();
        }
    }
}
