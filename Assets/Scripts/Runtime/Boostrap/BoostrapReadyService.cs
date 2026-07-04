using Cysharp.Threading.Tasks;

namespace Runtime.Boostrap
{
    public class BoostrapReadyService
    {
        private readonly UniTaskCompletionSource _readySource = new UniTaskCompletionSource();

        public void SetReady() => _readySource.TrySetResult();

        public UniTask WaitUntilReady() => _readySource.Task;
    }
}