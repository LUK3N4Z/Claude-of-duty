using FPS.Core;
using UnityEngine;

namespace FPS.Audio
{
    /// <summary>Pooled one-shot 3D sound with an optional start delay (sound travelling at 343 m/s).</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioEmitter : MonoBehaviour, IPoolable
    {
        private AudioSource source;

        public AudioClip Clip => source.clip;
        /// <summary>Delay before the sound starts (s).</summary>
        public float Delay { get; private set; }

        /// <summary>Lifetime to give the pool so the emitter returns after the clip finished.</summary>
        public static float LifetimeFor(AudioClip clip, float delay, float pitch) => delay + clip.length / Mathf.Max(0.1f, pitch) + 0.1f;

        public void Play(AudioClip clip, float volume, float pitch, float delay, float minDistance, float maxDistance)
        {
            Delay = delay;
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            if (delay > 0f) source.PlayDelayed(delay);
            else source.Play();
        }

        public void OnSpawned()
        {
        }

        public void OnDespawned()
        {
            source.Stop();
            source.clip = null;
        }

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
        }
    }
}
