using System;
using Cysharp.Threading.Tasks;
using Runtime.UI;
using UnityEngine.SceneManagement;

namespace Runtime.Boostrap
{
    public class SceneFlowService
    {
        private const string MainMenu = "MainMenu";
        private const string Gameplay = "Gameplay";

        private readonly ScreenFader _fader;
        private UniTaskCompletionSource _gameplayReady;
        private bool _busy;

        public SceneFlowService(ScreenFader fader) => _fader = fader;

        public bool IsBusy => _busy;

        public bool IsScreenCovered => _fader.IsCovered;

        public async UniTask Boot()
        {
            if (_busy) return;
            _busy = true;
            _fader.SetCovered();
            await LoadAdditive(MainMenu);
            _busy = false;
            await _fader.RevealSplash();
        }

        // beforeUnload runs once the screen is covered, before Gameplay goes away: the level returns its pooled
        // objects there, so nothing pooled can be destroyed with the scene.
        public async UniTask LoadMainMenu(Action beforeUnload = null)
        {
            if (_busy) return;
            _busy = true;
            await _fader.Cover();
            beforeUnload?.Invoke();
            await LoadAdditive(MainMenu);
            await Unload(Gameplay);
            _busy = false;
            await _fader.Reveal();
        }

        public async UniTask LoadGameplay()
        {
            if (_busy) return;
            _busy = true;
            await _fader.Cover();
            _gameplayReady = new UniTaskCompletionSource();
            await LoadAdditive(Gameplay);
            await _gameplayReady.Task;
            await Unload(MainMenu);
            _busy = false;
            await _fader.Reveal();
        }

        public async UniTask Transition(Action whileCovered)
        {
            if (_busy) return;
            _busy = true;
            await _fader.Cover();
            whileCovered?.Invoke();
            _busy = false;
            await _fader.Reveal();
        }

        public void NotifyGameplayReady() => _gameplayReady?.TrySetResult();

        private static async UniTask LoadAdditive(string scene)
        {
            if (!SceneManager.GetSceneByName(scene).isLoaded)
                await SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive).ToUniTask();
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(scene));
        }

        private static async UniTask Unload(string scene)
        {
            if (SceneManager.GetSceneByName(scene).isLoaded)
                await SceneManager.UnloadSceneAsync(scene).ToUniTask();
        }
    }
}
