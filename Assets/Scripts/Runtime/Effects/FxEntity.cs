using Runtime.Boostrap.Pool;
using UnityEngine;

namespace Runtime.Effects
{
    // One pooled particle effect (root system + children). It returns itself to its pool once every
    // system in the hierarchy is dead, so callers just fire and forget.
    [RequireComponent(typeof(ParticleSystem))]
    public class FxEntity : MonoBehaviour, IPoolable
    {
        [SerializeField] private ParticleSystem root;
        [SerializeField] private ParticleSystem[] tintTargets; // systems whose start colour follows the Play tint

        private ParticleSystem.MinMaxGradient[] _defaultColors;
        private PoolService<FxEntity> _pool;
        private bool _playing;

        private void Awake()
        {
            if (root == null) root = GetComponent<ParticleSystem>();
            tintTargets ??= new ParticleSystem[0];
            _defaultColors = new ParticleSystem.MinMaxGradient[tintTargets.Length];
            for (int i = 0; i < tintTargets.Length; i++)
                _defaultColors[i] = tintTargets[i].main.startColor;
        }

        public void Bind(PoolService<FxEntity> pool) => _pool = pool;

        public void Play(Vector3 position, Color? tint)
        {
            transform.position = position;
            ApplyTint(tint);
            root.Play(true);
            _playing = true;
        }

        private void LateUpdate()
        {
            if (!_playing || root.IsAlive(true)) return;
            _playing = false;
            _pool?.Enqueue(this);
        }

        // Tinted systems get a random start colour between the tint and a lighter version of it.
        private void ApplyTint(Color? tint)
        {
            for (int i = 0; i < tintTargets.Length; i++)
            {
                var main = tintTargets[i].main;
                main.startColor = tint.HasValue
                    ? new ParticleSystem.MinMaxGradient(tint.Value, Color.Lerp(tint.Value, Color.white, 0.4f))
                    : _defaultColors[i];
            }
        }

        public void Spawn() => root.Clear(true);

        public void Despawn()
        {
            _playing = false;
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
