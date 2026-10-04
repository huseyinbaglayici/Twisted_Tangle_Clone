using Cysharp.Threading.Tasks;

namespace Runtime.Gameplay.Managers
{
    public class GameplayReadyService
    {
        private UniTaskCompletionSource _readySource = new UniTaskCompletionSource();

        public void Reset() => _readySource = new UniTaskCompletionSource();

        public void SetReady() => _readySource.TrySetResult();

        public UniTask WaitUntilReady() => _readySource.Task;
    }
}
