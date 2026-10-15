using FPS.Ballistics;
using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Audio
{
    /// <summary>
    /// Presents bullet impacts: a pooled HDRP decal from the surface's material (sized by calibre, parented to what was
    /// hit), a dust puff tinted by the surface, and the surface's impact sound delayed by its distance to the listener.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ImpactEffectsController : MonoBehaviour
    {
        private static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");
        private const float ReferenceDiameter = 0.0057f;

        [Header("Decals")]
        [SerializeField] private float decalSize = 0.06f;
        [SerializeField] private float decalDepth = 0.08f;
        [SerializeField] private float decalLifetime = 60f;
        [SerializeField, Min(1)] private int maxDecals = 96;

        [Header("Dust")]
        [Tooltip("HDRP/Unlit transparent material with a 4×4 alpha flipbook.")]
        [SerializeField] private Material dustMaterial;
        [SerializeField, Min(1)] private int dustParticles = 10;

        [Header("Audio")]
        [SerializeField] private float impactMaxDistance = 60f;
        [SerializeField] private float impactMinDistance = 2f;

        private DecalProjector decalTemplate;
        private ParticleSystem dustTemplate;
        private AudioEmitter emitterTemplate;
        private MaterialPropertyBlock tint;
        private Transform listener;

        public Material DustMaterial
        {
            get => dustMaterial;
            set => dustMaterial = value;
        }

        private void Awake()
        {
            tint = new MaterialPropertyBlock();
            var listenerComponent = GetComponentInChildren<AudioListener>(true);
            listener = listenerComponent != null ? listenerComponent.transform : transform;

            Transform templates = new GameObject("Impact Templates").transform;
            templates.gameObject.SetActive(false); // pooled clones wake when spawned
            templates.SetParent(transform, false);

            var decalObject = new GameObject("Impact Decal");
            decalObject.transform.SetParent(templates, false);
            decalTemplate = decalObject.AddComponent<DecalProjector>();

            dustTemplate = EffectParticles.Create("Impact Dust", templates, dustMaterial, new EffectParticles.Settings
            {
                Lifetime = new Vector2(0.5f, 1.1f), Speed = new Vector2(0.6f, 2.5f), Size = new Vector2(0.05f, 0.14f),
                SizeGrowth = 3f, ConeAngle = 35f, ConeRadius = 0.02f, GravityModifier = 0.15f, NoiseStrength = 0.2f,
                MaxParticles = 32,
            });

            var emitterObject = new GameObject("Impact Audio");
            emitterObject.transform.SetParent(templates, false);
            emitterObject.AddComponent<AudioSource>();
            emitterTemplate = emitterObject.AddComponent<AudioEmitter>();
        }

        private void Start() => ObjectPoolManager.Instance.Prewarm(decalTemplate, 16, maxDecals);

        private void OnEnable() => EventBus<ProjectileImpactEvent>.Subscribe(OnImpact);

        private void OnDisable() => EventBus<ProjectileImpactEvent>.Unsubscribe(OnImpact);

        private void OnImpact(in ProjectileImpactEvent impact)
        {
            SurfaceMaterialSO surface = impact.Surface;
            if (surface == null) return;

            ObjectPoolManager pool = ObjectPoolManager.Instance;
            float calibre = impact.Caliber != null ? impact.Caliber.BulletDiameterMeters / ReferenceDiameter : 1f;

            if (surface.ImpactDecal != null)
            {
                Quaternion rotation = Quaternion.LookRotation(-impact.Normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                Transform parent = impact.Collider != null ? impact.Collider.transform : null;
                DecalProjector decal = pool.Spawn(decalTemplate, impact.Point + impact.Normal * (decalDepth * 0.25f), rotation, parent, decalLifetime);
                float size = decalSize * calibre * Random.Range(0.85f, 1.15f) * (impact.Outcome == ImpactOutcome.Ricocheted ? 0.6f : 1f);
                decal.material = surface.ImpactDecal;
                decal.size = new Vector3(size, size, decalDepth);
            }

            if (dustMaterial != null)
            {
                ParticleSystem dust = pool.Spawn(dustTemplate, impact.Point, Quaternion.LookRotation(impact.Normal), null, 1.5f);
                var dustRenderer = dust.GetComponent<ParticleSystemRenderer>();
                dustRenderer.GetPropertyBlock(tint);
                tint.SetColor(UnlitColorId, surface.ImpactDustColor);
                dustRenderer.SetPropertyBlock(tint);
                if (!dust.isPlaying) dust.Play();
                dust.Emit(Mathf.RoundToInt(dustParticles * Mathf.Clamp(calibre, 0.7f, 1.6f)));
            }

            AudioClip clip = surface.RandomImpactClip();
            float distance = Vector3.Distance(impact.Point, listener.position);
            if (clip != null && distance <= impactMaxDistance)
            {
                float delay = AcousticPropagationSystem.PropagationDelay(distance);
                float pitch = Random.Range(0.92f, 1.08f);
                AudioEmitter emitter = pool.Spawn(emitterTemplate, impact.Point, Quaternion.identity, null, AudioEmitter.LifetimeFor(clip, delay, pitch));
                emitter.Play(clip, 0.9f, pitch, delay, impactMinDistance, impactMaxDistance);
            }
        }
    }
}
