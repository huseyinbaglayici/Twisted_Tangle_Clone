using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Runtime.UI
{
    // Full-screen loading curtain that lives for the whole session. Every scene swap (and a retry) happens
    // behind it: cover -> swap -> reveal, so the player never sees a board being built or torn down.
    [RequireComponent(typeof(CanvasGroup))]
    public class ScreenFader : MonoBehaviour
    {
        [Header("Cover")]
        [SerializeField] private float coverDelay = 0.02f;
        [SerializeField] private float coverDuration = 0.33f;
        [Tooltip("Cover is considered done after at least this long, so the curtain settles before work starts.")]
        [SerializeField] private float minCoverTime = 0.4f;

        [Header("Reveal")]
        [SerializeField] private float revealDelay = 0.25f;
        [SerializeField] private float revealDuration = 0.4f;
        [SerializeField] private Ease ease = Ease.InOutQuad;

        [Header("App start splash")]
        [SerializeField] private float splashHold = 1.25f;
        [SerializeField] private float splashFade = 0.15f;

        private CanvasGroup _group;
        private Tween _tween;

        public bool IsCovered => Group.alpha >= 0.999f;

        private CanvasGroup Group => _group != null ? _group : _group = GetComponent<CanvasGroup>();

        // App start: the curtain is already up when the first scene loads.
        public void SetCovered()
        {
            _tween?.Kill();
            Group.alpha = 1f;
            Group.blocksRaycasts = true;
        }

        public async UniTask Cover()
        {
            if (IsCovered) return;
            _tween?.Kill();
            Group.blocksRaycasts = true;
            _tween = Group.DOFade(1f, coverDuration).SetDelay(coverDelay).SetEase(ease)
                .SetUpdate(true).SetLink(gameObject);
            await UniTask.WhenAll(Completion(_tween),
                UniTask.Delay(Mathf.RoundToInt(minCoverTime * 1000), DelayType.UnscaledDeltaTime));
        }

        public UniTask Reveal() => Fade(0f, revealDelay, revealDuration);

        public UniTask RevealSplash() => Fade(0f, splashHold, splashFade);

        private async UniTask Fade(float to, float delay, float duration)
        {
            _tween?.Kill();
            var tween = _tween = Group.DOFade(to, duration).SetDelay(delay).SetEase(ease).SetUpdate(true)
                .SetLink(gameObject);
            await Completion(tween);
            if (_tween == tween) Group.blocksRaycasts = to > 0f;
        }

        // Finishes when the tween completes or gets killed (a newer fade took over).
        private static UniTask Completion(Tween tween)
        {
            var done = new UniTaskCompletionSource();
            tween.OnKill(() => done.TrySetResult());
            return done.Task;
        }
    }
}
