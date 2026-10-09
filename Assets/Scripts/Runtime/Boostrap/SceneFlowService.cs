using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Runtime.Boostrap
{
    // Boostrap stays loaded for the whole session (pools, solver, EventSystem, AudioListener);
    // MainMenu and Gameplay are swapped on top of it additively, never both left loaded.
    public class SceneFlowService
    {
        private const string MainMenu = "MainMenu";
        private const string Gameplay = "Gameplay";

        private UniTaskCompletionSource _gameplayReady;
        private bool _busy;

        public async UniTask LoadMainMenu()
        {
            if (_busy) return;
            _busy = true;
            await LoadAdditive(MainMenu);
            await Unload(Gameplay);
            _busy = false;
        }

        // The menu (and its loading panel) stays up until the level is built, then it is unloaded.
        public async UniTask LoadGameplay()
        {
            if (_busy) return;
            _busy = true;
            _gameplayReady = new UniTaskCompletionSource();
            await LoadAdditive(Gameplay);
            await _gameplayReady.Task;
            await Unload(MainMenu);
            _busy = false;
        }

        // Called by the gameplay scene once its first level is on screen.
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
