using DG.Tweening;
using Reflex.Attributes;
using Runtime.Audio;
using Runtime.Effects;
using Runtime.Gameplay;
using Runtime.Gameplay.PowerUps;
using UnityEngine;

namespace Runtime.UI
{
    // Ice missile to the clock, then the ice frame, clock decorations and countdown bar for as long as the timer is
    // frozen. Everything is driven by the timer's remaining freeze, so it pauses with it while a popup is open.
    // The frame closes in from outside the screen and breathes; nothing flies over the board.
    public class FreezeOverlay : MonoBehaviour
    {
        [Inject] private FxPlayer _fx;
        [Inject] private AudioPlayer _audio;

        [SerializeField] private TimeFreezePowerUp freeze;
        [SerializeField] private LevelTimer timer;
        [SerializeField] private Camera cam;

        [Header("Missile")]
        [SerializeField] private RectTransform missile;
        [SerializeField] private RectTransform missileStart;
        [SerializeField] private RectTransform clock;
        [SerializeField] private float missileSpin = 540f;

        [Header("Frozen look")]
        [SerializeField] private CanvasGroup[] fadeGroups;
        [SerializeField] private RectTransform countdownFill;
        [SerializeField] private float fadeIn = 0.5f;
        [SerializeField] private float fadeOut = 0.5f;

        [Header("Frame")]
        [SerializeField] private RectTransform frame;
        [SerializeField] private float frameStartScale = 1.18f;
        [SerializeField] private float frameInDuration = 0.45f;
        [SerializeField] private float framePulseScale = 1.025f;
        [SerializeField] private float framePulseDuration = 0.9f;

        [Tooltip("Distance in front of the camera the burst on the clock spawns at.")]
        [SerializeField] private float fxDepth = 3f;

        private Sequence _missileTween;
        private Sequence _frameTween;
        private float _alpha;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
            missile.gameObject.SetActive(false);
            SetAlpha(0f);
        }

        private void OnEnable()
        {
            freeze.Launched += OnLaunched;
            freeze.Landed += OnLanded;
        }

        private void OnDisable()
        {
            freeze.Launched -= OnLaunched;
            freeze.Landed -= OnLanded;
            _missileTween?.Kill();
            _frameTween?.Kill();
        }

        private void OnLaunched()
        {
            _missileTween?.Kill();
            missile.gameObject.SetActive(true);
            missile.position = missileStart.position;
            missile.localRotation = Quaternion.identity;
            _missileTween = DOTween.Sequence().SetLink(gameObject)
                .Join(missile.DOMove(clock.position, freeze.FlightDuration).SetEase(Ease.InQuad))
                .Join(missile.DORotate(new Vector3(0f, 0f, -missileSpin), freeze.FlightDuration, RotateMode.FastBeyond360));
            _audio.Play(SfxType.FreezeShoot);
        }

        private void OnLanded()
        {
            _missileTween?.Kill();
            missile.gameObject.SetActive(false);
            _fx.Play(FxType.FreezeBurst, ScreenToWorld(clock.position));
            _audio.Play(SfxType.FreezeHit);
            clock.DOKill(true);
            clock.DOPunchScale(Vector3.one * 0.2f, 0.35f, 6, 0.5f).SetLink(gameObject);

            _frameTween?.Kill();
            frame.localScale = Vector3.one * frameStartScale;
            _frameTween = DOTween.Sequence().SetLink(gameObject)
                .Append(frame.DOScale(1f, frameInDuration).SetEase(Ease.OutBack))
                .Append(frame.DOScale(framePulseScale, framePulseDuration).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo));
        }

        private void Update()
        {
            float remaining = timer.FreezeRemaining;
            if (remaining > 0f)
            {
                float duration = Mathf.Max(0.01f, timer.FreezeDuration);
                float elapsed = duration - remaining;
                float a = 1f;
                if (elapsed < fadeIn) a = DOVirtual.EasedValue(0f, 1f, elapsed / fadeIn, Ease.OutQuad);
                else if (remaining < fadeOut) a = DOVirtual.EasedValue(1f, 0f, 1f - remaining / fadeOut, Ease.InQuad);
                SetAlpha(a);
                countdownFill.anchorMax = new Vector2(remaining / duration, 1f);
            }
            else if (_alpha > 0f)
            {
                // Cleared early (level ended): fade out from wherever it is.
                SetAlpha(Mathf.MoveTowards(_alpha, 0f, Time.deltaTime / fadeOut));
            }
        }

        private Vector3 ScreenToWorld(Vector2 screen) =>
            cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, fxDepth));

        private void SetAlpha(float a)
        {
            _alpha = a;
            if (a <= 0.001f && _frameTween != null)
            {
                _frameTween.Kill();
                _frameTween = null;
                frame.localScale = Vector3.one;
            }
            foreach (var group in fadeGroups)
            {
                group.alpha = a;
                bool on = a > 0.001f;
                if (group.gameObject.activeSelf != on) group.gameObject.SetActive(on);
            }
        }
    }
}
