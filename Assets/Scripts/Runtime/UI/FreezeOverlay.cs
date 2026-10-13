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

        [Header("Edge snow")]
        [SerializeField] private float snowInterval = 0.3f;
        [Tooltip("Distance in front of the camera the snow bursts spawn at.")]
        [SerializeField] private float snowDepth = 3f;
        [Tooltip("How far in from the screen edge (fraction of width / height) the snow spawns.")]
        [SerializeField] private Vector2 snowInset = new(0.08f, 0.06f);

        private Sequence _missileTween;
        private float _alpha;
        private float _snowTimer;

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
                if (!timer.Paused) SpawnSnow();
            }
            else if (_alpha > 0f)
            {
                // Cleared early (level ended): fade out from wherever it is.
                SetAlpha(Mathf.MoveTowards(_alpha, 0f, Time.deltaTime / fadeOut));
            }
        }

        private void SpawnSnow()
        {
            _snowTimer -= Time.deltaTime;
            if (_snowTimer > 0f) return;
            _snowTimer = snowInterval;

            // A random point on a rect inset from the screen edges.
            float w = Screen.width, h = Screen.height;
            float x0 = w * snowInset.x, x1 = w - x0, y0 = h * snowInset.y, y1 = h - y0;
            Vector2 p = Random.Range(0, 4) switch
            {
                0 => new Vector2(x0, Random.Range(y0, y1)),
                1 => new Vector2(x1, Random.Range(y0, y1)),
                2 => new Vector2(Random.Range(x0, x1), y0),
                _ => new Vector2(Random.Range(x0, x1), y1)
            };
            _fx.Play(FxType.FreezeSnow, ScreenToWorld(p));
        }

        private Vector3 ScreenToWorld(Vector2 screen) =>
            cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, snowDepth));

        private void SetAlpha(float a)
        {
            _alpha = a;
            foreach (var group in fadeGroups)
            {
                group.alpha = a;
                bool on = a > 0.001f;
                if (group.gameObject.activeSelf != on) group.gameObject.SetActive(on);
            }
        }
    }
}
