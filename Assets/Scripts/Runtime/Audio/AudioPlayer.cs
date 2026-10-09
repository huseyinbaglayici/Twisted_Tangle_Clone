using System.Collections.Generic;
using Reflex.Attributes;
using Runtime.Boostrap.Save;
using UnityEngine;

namespace Runtime.Audio
{
    // Plays short 2D sound effects by type. A few pooled sources so quick overlapping sounds don't cut each other.
    public class AudioPlayer : MonoBehaviour
    {
        [Inject] private SaveService _save;

        [SerializeField] private List<SfxClip> clips = new List<SfxClip>();
        [SerializeField, Min(1)] private int sourceCount = 6;

        private readonly Dictionary<SfxType, SfxClip> _byType = new Dictionary<SfxType, SfxClip>();
        private AudioSource[] _sources;
        private int _next;
        private bool? _soundOn;

        public bool SoundOn
        {
            // Read once and keep it, no need to hit the save file on every play.
            get => _soundOn ??= _save == null || _save.SoundOn;
            set
            {
                _soundOn = value;
                if (_save != null) _save.SoundOn = value;
                if (!value) StopAll();
            }
        }

        private void Awake()
        {
            foreach (var entry in clips)
                if (entry.clip != null) _byType[entry.type] = entry;

            _sources = new AudioSource[sourceCount];
            for (int i = 0; i < sourceCount; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _sources[i] = source;
            }
        }

        public void Play(SfxType type)
        {
            if (!SoundOn || !_byType.TryGetValue(type, out var entry)) return;

            var source = NextSource();
            source.clip = entry.clip;
            source.volume = entry.volume;
            source.pitch = Random.Range(entry.pitchRange.x, entry.pitchRange.y);
            source.Play();
        }

        public void StopAll()
        {
            if (_sources == null) return;
            foreach (var source in _sources) source.Stop();
        }

        // Prefer an idle source; if all are busy, steal the oldest one in round-robin order.
        private AudioSource NextSource()
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                int index = (_next + i) % _sources.Length;
                if (_sources[index].isPlaying) continue;
                _next = (index + 1) % _sources.Length;
                return _sources[index];
            }

            var stolen = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            return stolen;
        }
    }
}
