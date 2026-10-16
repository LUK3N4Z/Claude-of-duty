using FPS.Ballistics;
using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Audio
{
    /// <summary>
    /// Presents bullet impacts: a pooled HDRP decal from the surface's material (sized by calibre, parented to what was
    /// hit), a dust puff tinted by the surface, and the surface's impact sound delayed by its distance to the listener.
    /// Steel throws a spark shower (more on a ricochet) and keeps a glowing thermal ring that cools over three seconds;
    /// masonry and soil leave a lingering volumetric dust cloud lit by the scene.
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

        [Header("Steel")]
        [Tooltip("HDRP/Unlit additive spark sprite.")]
        [SerializeField] private Material sparkMaterial;
        [SerializeField] private Color sparkColor = new Color(6f, 3.2f, 1.1f, 1f);
        [SerializeField, Min(1)] private int sparksPerHit = 12;
        [Tooltip("HDRP/Decal, emissive only: the hot ring around a hole in steel.")]
        [SerializeField] private Material thermalDecalMaterial;
        [SerializeField] private float thermalSize = 0.045f;

        [Header("Lingering Dust")]
        [Tooltip("3D noise for the volumetric dust cloud (A = density).")]
        [SerializeField] private Texture3D dustVolumeMask;
        [SerializeField] private float dustCloudSize = 0.35f;
        [SerializeField] private float dustCloudLifetime = 3.5f;
        [Tooltip("Mean free path of a fresh cloud (m): smaller is thicker.")]
        [SerializeField] private float dustCloudFreePath = 0.5f;

        [Header("Audio")]
        [SerializeField] private float impactMaxDistance = 60f;
        [SerializeField] private float impactMinDistance = 2f;

        private DecalProjector decalTemplate;
        private ParticleSystem dustTemplate;
        private ParticleSystem sparkTemplate;
        private FadingDecal thermalTemplate;
        private VolumetricPuff cloudTemplate;
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
            // HDRP warns whenever a projector wakes without a material; each impact swaps in its surface's decal
            // before the frame renders, so the clones only need some material to wake with.
            decalTemplate.material = thermalDecalMaterial;

            dustTemplate = EffectParticles.Create("Impact Dust", templates, dustMaterial, new EffectParticles.Settings
            {
                Lifetime = new Vector2(0.5f, 1.1f), Speed = new Vector2(0.6f, 2.5f), Size = new Vector2(0.05f, 0.14f),
                SizeGrowth = 3f, ConeAngle = 35f, ConeRadius = 0.02f, GravityModifier = 0.15f, NoiseStrength = 0.2f,
                MaxParticles = 32,
            });

            sparkTemplate = EffectParticles.Create("Impact Sparks", templates, sparkMaterial, new EffectParticles.Settings
            {
                Lifetime = new Vector2(0.15f, 0.55f), Speed = new Vector2(2.5f, 9f), Size = new Vector2(0.004f, 0.01f),
                SizeGrowth = 0.25f, ConeAngle = 55f, ConeRadius = 0.004f, GravityModifier = 1f, MaxParticles = 48,
                Stretch = 0.035f, NoFlipbook = true, Collide = true,
            });

            var thermalObject = new GameObject("Thermal Ring");
            thermalObject.transform.SetParent(templates, false);
            var thermalProjector = thermalObject.AddComponent<DecalProjector>();
            thermalProjector.material = thermalDecalMaterial;
            thermalTemplate = thermalObject.AddComponent<FadingDecal>();

            var cloudObject = new GameObject("Dust Cloud");
            cloudObject.transform.SetParent(templates, false);
            var cloudFog = cloudObject.AddComponent<LocalVolumetricFog>();
            LocalVolumetricFogArtistParameters cloud = cloudFog.parameters;
            cloud.volumeMask = dustVolumeMask;
            cloud.textureTiling = Vector3.one;
            cloud.textureScrollingSpeed = new Vector3(0.05f, 0.12f, 0.03f);
            cloud.positiveFade = cloud.negativeFade = Vector3.one * 0.45f; // soft edges, no box
            cloud.falloffMode = LocalVolumetricFogFalloffMode.Exponential;
            cloud.distanceFadeStart = 40f;
            cloud.distanceFadeEnd = 60f;
            cloudFog.parameters = cloud;
            cloudTemplate = cloudObject.AddComponent<VolumetricPuff>();

            var emitterObject = new GameObject("Impact Audio");
            emitterObject.transform.SetParent(templates, false);
            emitterObject.AddComponent<AudioSource>();
            emitterTemplate = emitterObject.AddComponent<AudioEmitter>();
        }

        private void Start()
        {
            ObjectPoolManager pool = ObjectPoolManager.Instance;
            pool.Prewarm(decalTemplate, 16, maxDecals);
            pool.Prewarm(thermalTemplate, 4, 24);
            pool.Prewarm(cloudTemplate, 4, 16);
            pool.Prewarm(sparkTemplate, 4, 16);
        }

        private void OnEnable() => EventBus<ProjectileImpactEvent>.Subscribe(OnImpact);

        private void OnDisable() => EventBus<ProjectileImpactEvent>.Unsubscribe(OnImpact);

        private void OnImpact(in ProjectileImpactEvent impact)
        {
            SurfaceMaterialSO surface = impact.Surface;
            if (surface == null) return;
            // Rounds striking this body: the hit is felt, not seen from inside (no puffs or decals in the camera).
            if (impact.Collider != null && impact.Collider.transform.IsChildOf(transform)) return;

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

            bool ricochet = impact.Outcome == ImpactOutcome.Ricocheted;
            if (surface.Sparks && sparkMaterial != null)
            {
                // Sparks leave along the deflected path: the incoming round reflected about the surface.
                Vector3 incoming = impact.Velocity.sqrMagnitude > 1e-4f ? impact.Velocity.normalized : -impact.Normal;
                Vector3 spray = Vector3.Reflect(incoming, impact.Normal);
                ParticleSystem sparks = pool.Spawn(sparkTemplate, impact.Point, Quaternion.LookRotation(Vector3.Slerp(impact.Normal, spray, 0.6f)), null, 1f);
                var sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
                sparkRenderer.GetPropertyBlock(tint);
                tint.SetColor(UnlitColorId, sparkColor);
                sparkRenderer.SetPropertyBlock(tint);
                if (!sparks.isPlaying) sparks.Play();
                sparks.Emit(Mathf.RoundToInt(sparksPerHit * (ricochet ? 2f : 1f) * Mathf.Clamp(calibre, 0.7f, 1.8f)));

                if (!ricochet && thermalDecalMaterial != null)
                {
                    Transform parent = impact.Collider != null ? impact.Collider.transform : null;
                    FadingDecal ring = pool.Spawn(thermalTemplate, impact.Point + impact.Normal * (decalDepth * 0.25f),
                        Quaternion.LookRotation(-impact.Normal), parent, thermalTemplate.Duration);
                    float ringSize = thermalSize * Mathf.Clamp(calibre, 0.7f, 2f);
                    ring.GetComponent<DecalProjector>().size = new Vector3(ringSize, ringSize, decalDepth);
                }
            }

            if (surface.LingeringDust && dustVolumeMask != null && !ricochet)
            {
                VolumetricPuff cloud = pool.Spawn(cloudTemplate, impact.Point + impact.Normal * 0.12f, Quaternion.identity, null, dustCloudLifetime);
                Color dust = surface.ImpactDustColor;
                cloud.Play(new Color(dust.r, dust.g, dust.b, 1f), dustCloudSize * Mathf.Clamp(calibre, 0.8f, 1.6f), 3.5f, dustCloudFreePath,
                    dustCloudLifetime, impact.Normal * 0.6f + Vector3.up * 0.05f);
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
