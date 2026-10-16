using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Audio
{
    /// <summary>
    /// A pooled decal that fades out over <see cref="Duration"/>: the glowing ring a round leaves in steel cools from
    /// white-orange to nothing (the projector's fade factor scales its emission with everything else).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DecalProjector))]
    public sealed class FadingDecal : MonoBehaviour, IPoolable
    {
        [Tooltip("Time to cool completely (s).")]
        [SerializeField, Min(0.05f)] private float duration = 3f;

        private DecalProjector projector;
        private float age;

        public float Duration => duration;
        public float Fade => projector != null ? projector.fadeFactor : 0f;

        public void OnSpawned()
        {
            age = 0f;
            if (projector == null) projector = GetComponent<DecalProjector>();
            projector.fadeFactor = 1f;
        }

        public void OnDespawned() => age = duration;

        private void Awake() => projector = GetComponent<DecalProjector>();

        private void Update()
        {
            if (age >= duration) return;
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / duration);
            projector.fadeFactor = (1f - t) * (1f - t); // Newtonian cooling: bright for a moment, then a long dull tail
        }
    }
}
