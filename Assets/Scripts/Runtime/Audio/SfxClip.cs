using System;
using UnityEngine;

namespace Runtime.Audio
{
    // One row in the AudioPlayer list: which clip a SfxType plays and how loud / how varied.
    [Serializable]
    public class SfxClip
    {
        public SfxType type;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random pitch picked per play, keeps repeated sounds from feeling robotic.")]
        public Vector2 pitchRange = new Vector2(1f, 1f);
    }
}
