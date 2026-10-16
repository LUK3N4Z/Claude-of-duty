using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Weapons
{
    /// <summary>
    /// A live grenade: a pooled rigid body that bounces and rolls until its fuse runs out. Fragmentation grenades launch
    /// real fragment projectiles into the ballistics simulation, apply blast overpressure to bodies in the open and a
    /// concussive push to loose objects; stun grenades raise a <see cref="FlashbangEvent"/>. Both flash, report through
    /// the acoustic system and suppress nearby listeners. Smoke grenades burn quietly into a volumetric cloud that is
    /// also a <see cref="VisionObstruction"/> for AI sight.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ThrownGrenade : MonoBehaviour, IPoolable
    {
        private const int MaxBlastColliders = 64;
        private const int MaxBlastVictims = 16;
        private static readonly Collider[] BlastHits = new Collider[MaxBlastColliders];
        private static readonly TacticalHealthAndArmorSystem[] Victims = new TacticalHealthAndArmorSystem[MaxBlastVictims];
        private static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");
        private static readonly Collider[] PushHits = new Collider[MaxBlastColliders];

        [Tooltip("Time the spent grenade stays in the pool's hands so the smoke can drift (s).")]
        [SerializeField, Min(0.1f)] private float lingerTime = 4f;
        [SerializeField, Min(0.01f)] private float flashDuration = 0.06f;
        [SerializeField, Range(0, 64)] private int smokePuffs = 24;

        private Rigidbody body;
        private MeshRenderer[] meshes;
        private Collider[] colliders;
        private Light flash;
        private ParticleSystem smoke;
        private MaterialPropertyBlock smokeTint;
        private GrenadeDataSO data;
        private float detonateAt, flashOffAt, releaseAt, smokeStart;
        private LocalVolumetricFog smokeVolume;
        private int smokeCloud;

        public GrenadeDataSO Data => data;
        public Transform Thrower { get; private set; }
        public bool IsLive { get; private set; }
        public bool HasDetonated { get; private set; }
        public Vector3 DetonationPoint { get; private set; }
        /// <summary>Seconds until the fuse runs out (0 once detonated).</summary>
        public float TimeToDetonation => IsLive ? Mathf.Max(0f, detonateAt - Time.time) : 0f;

        /// <summary>
        /// Releases the spoon and throws: <paramref name="cookedTime"/> seconds of fuse already burnt in the hand.
        /// </summary>
        public void Throw(GrenadeDataSO grenade, Transform thrower, Vector3 velocity, float cookedTime = 0f)
        {
            data = grenade;
            Thrower = thrower;
            IsLive = true;
            HasDetonated = false;
            detonateAt = Time.time + Mathf.Max(0.05f, grenade.FuseTime - cookedTime);
            SetSolid(true);
            body.isKinematic = false;
            body.mass = grenade.MassKg;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 12f;
            EnsureSmoke(grenade.SmokeMaterial);
        }

        public void OnSpawned()
        {
            IsLive = false;
            HasDetonated = false;
            flash.enabled = false;
        }

        public void OnDespawned()
        {
            IsLive = false;
            flash.enabled = false;
            body.isKinematic = true;
            if (smoke != null)
            {
                smoke.Clear(true);
                ParticleSystem.EmissionModule emission = smoke.emission;
                emission.rateOverTime = 0f;
            }

            EndSmoke();
        }

        private void OnDisable() => EndSmoke();

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            meshes = GetComponentsInChildren<MeshRenderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);

            var flashObject = new GameObject("Detonation Flash");
            flashObject.transform.SetParent(transform, false);
            flash = flashObject.AddComponent<Light>();
            flashObject.AddComponent<HDAdditionalLightData>();
            flash.type = LightType.Point;
            flash.lightUnit = LightUnit.Lumen;
            flash.range = 30f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
            smokeTint = new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (IsLive && Time.time >= detonateAt) Detonate();
            if (!HasDetonated) return;

            if (flash.enabled && Time.time >= flashOffAt) flash.enabled = false;
            if (smokeCloud != 0) UpdateSmoke();
            if (Time.time >= releaseAt) ObjectPoolManager.Instance.Release(gameObject);
        }

        private void Detonate()
        {
            IsLive = false;
            HasDetonated = true;
            Vector3 point = transform.position;
            DetonationPoint = point;
            if (data.Type == GrenadeType.Smoke)
            {
                StartSmoke(point); // the canister keeps lying where it stopped
                return;
            }

            body.isKinematic = true;
            SetSolid(false);

            flash.transform.rotation = Quaternion.identity;
            flash.intensity = data.FlashLumen;
            flash.enabled = data.FlashLumen > 0f;
            flashOffAt = Time.time + flashDuration;
            releaseAt = Time.time + lingerTime;

            if (smoke != null && smokePuffs > 0)
            {
                smoke.transform.rotation = Quaternion.LookRotation(Vector3.up);
                smoke.Emit(smokePuffs);
            }

            EventBus<GunshotEvent>.Raise(new GunshotEvent
            {
                Position = point,
                Direction = Vector3.up,
                Shooter = transform,
                Profile = data.DetonationAudio,
                MuzzleFlash = 1f,
            });

            if (data.Type == GrenadeType.Fragmentation)
            {
                LaunchFragments(point);
                ApplyBlast(point);
                Push(point);
            }
            else
            {
                EventBus<FlashbangEvent>.Raise(new FlashbangEvent { Position = point, Intensity = data.FlashIntensity });
            }

            Suppress(point);
        }

        private void LaunchFragments(Vector3 point)
        {
            CaliberDataSO caliber = data.FragmentCaliber;
            if (caliber == null) return;

            Vector3 origin = point + Vector3.up * 0.05f;
            for (int i = 0; i < data.FragmentCount; i++)
            {
                Vector3 direction = Random.onUnitSphere;
                if (direction.y < 0f) direction.y *= 0.3f; // the ground soaks up the downward cone
                EventBus<ShotFiredEvent>.Raise(new ShotFiredEvent
                {
                    Origin = origin,
                    Direction = direction.normalized,
                    MuzzleVelocity = data.FragmentVelocity * Random.Range(0.75f, 1.05f),
                    Caliber = caliber,
                    Shooter = null, // fragments do not care who pulled the pin
                });
            }
        }

        private void ApplyBlast(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, data.BlastRadius, BlastHits, 1 << GameLayers.Hitbox, QueryTriggerInteraction.Collide);
            int victims = 0;
            Vector3 source = point + Vector3.up * 0.1f;
            for (int i = 0; i < count && victims < MaxBlastVictims; i++)
            {
                if (!BlastHits[i].TryGetComponent(out Hitbox hitbox) || hitbox.Owner == null || IsVictim(hitbox.Owner, victims)) continue;

                Vector3 contact = BlastHits[i].ClosestPoint(source);
                // Overpressure is shadowed by solid cover between the blast and the body.
                if (Physics.Linecast(source, contact, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore)) continue;

                Victims[victims++] = hitbox.Owner;
                Vector3 offset = contact - point;
                float distance = offset.magnitude;
                hitbox.Owner.ApplyDamage(HitZone.Thorax, data.BlastDamageAt(distance), contact, distance > 1e-3f ? offset / distance : Vector3.up);
            }

            for (int i = 0; i < victims; i++) Victims[i] = null;
        }

        /// <summary>The concussive wave shoves loose bodies: casings, magazines, dropped weapons, the dead.</summary>
        private void Push(Vector3 point)
        {
            float radius = data.BlastRadius * 2f;
            int count = Physics.OverlapSphereNonAlloc(point, radius, PushHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody loose = PushHits[i].attachedRigidbody;
                if (loose == null || loose.isKinematic || loose == body) continue;
                loose.AddExplosionForce(data.BlastDamage * 4f, point, radius, 0.5f, ForceMode.Impulse);
            }
        }

        private void StartSmoke(Vector3 point)
        {
            smokeStart = Time.time;
            releaseAt = smokeStart + data.SmokeDuration + data.SmokeFade;
            if (smokeVolume == null)
            {
                var volumeObject = new GameObject("Smoke Cloud");
                volumeObject.transform.SetParent(transform, false);
                smokeVolume = volumeObject.AddComponent<LocalVolumetricFog>();
            }

            smokeVolume.enabled = true;
            if (smokeCloud == 0) smokeCloud = VisionObstruction.Add();
            if (smoke != null)
            {
                smoke.transform.rotation = Quaternion.LookRotation(Vector3.up);
                ParticleSystem.EmissionModule emission = smoke.emission;
                emission.rateOverTime = 6f;
                if (!smoke.isPlaying) smoke.Play();
            }

            UpdateSmoke();
        }

        private void UpdateSmoke()
        {
            float age = Time.time - smokeStart;
            float build = Mathf.SmoothStep(0f, 1f, age / data.SmokeBuildUp);
            float fade = 1f - Mathf.Clamp01((age - data.SmokeDuration) / data.SmokeFade);
            float radius = data.SmokeRadius * Mathf.Lerp(0.3f, 1f, build);
            float density = build * fade;
            // The cloud sits on the ground and grows upward as it spreads.
            Vector3 center = transform.position + Vector3.up * radius * 0.6f;
            VisionObstruction.Set(smokeCloud, center, radius, data.SmokeExtinction * density);

            smokeVolume.transform.SetPositionAndRotation(center, Quaternion.identity);
            LocalVolumetricFogArtistParameters parameters = smokeVolume.parameters;
            parameters.size = Vector3.one * radius * 2f;
            parameters.falloffMode = LocalVolumetricFogFalloffMode.Exponential;
            parameters.positiveFade = Vector3.one * 0.35f;
            parameters.negativeFade = Vector3.one * 0.35f;
            parameters.meanFreePath = density > 0.01f ? 1f / (data.SmokeExtinction * density) : 1000f;
            parameters.albedo = new Color(data.SmokeColor.r, data.SmokeColor.g, data.SmokeColor.b);
            smokeVolume.parameters = parameters;

            if (smoke != null && age > data.SmokeDuration)
            {
                ParticleSystem.EmissionModule emission = smoke.emission;
                emission.rateOverTime = 0f;
            }
        }

        private void EndSmoke()
        {
            if (smokeCloud != 0) VisionObstruction.Remove(smokeCloud);
            smokeCloud = 0;
            if (smokeVolume != null) smokeVolume.enabled = false;
        }

        private static bool IsVictim(TacticalHealthAndArmorSystem owner, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (Victims[i] == owner) return true;
            }

            return false;
        }

        private void Suppress(Vector3 point)
        {
            float radius = data.SuppressionRadius;
            if (radius <= 0f) return;

            for (int i = 0; i < NearMissListeners.Count; i++)
            {
                Transform listener = NearMissListeners.GetListener(i);
                Vector3 offset = listener.position - point;
                float distance = offset.magnitude;
                if (distance > radius) continue;

                EventBus<SuppressionEvent>.Raise(new SuppressionEvent
                {
                    Listener = listener,
                    Intensity = Mathf.Clamp01(1.1f - distance / radius),
                    Point = point,
                    Direction = distance > 1e-3f ? offset / distance : Vector3.up,
                });
            }
        }

        private void SetSolid(bool solid)
        {
            for (int i = 0; i < meshes.Length; i++) meshes[i].enabled = solid;
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = solid;
        }

        private void EnsureSmoke(Material material)
        {
            if (smoke != null || material == null) return;

            bool screen = data.Type == GrenadeType.Smoke;
            smoke = EffectParticles.Create(screen ? "Smoke Screen" : "Detonation Smoke", transform, material, screen
                ? new EffectParticles.Settings
                {
                    // Billowing canister smoke: slow, large and long-lived around the volumetric cloud.
                    Lifetime = new Vector2(6f, 9f),
                    Speed = new Vector2(0.4f, 1.6f),
                    Size = new Vector2(1.5f, 2.8f),
                    SizeGrowth = 2.2f,
                    ConeAngle = 60f,
                    ConeRadius = 0.3f,
                    GravityModifier = -0.01f,
                    NoiseStrength = 0.3f,
                    MaxParticles = 200,
                }
                : new EffectParticles.Settings
                {
                    Lifetime = new Vector2(1.8f, 3.5f),
                    Speed = new Vector2(1.5f, 6f),
                    Size = new Vector2(0.6f, 1.4f),
                    SizeGrowth = 3.5f,
                    ConeAngle = 70f,
                    ConeRadius = 0.2f,
                    GravityModifier = -0.02f,
                    NoiseStrength = 0.4f,
                    MaxParticles = 64,
                });
            var smokeRenderer = smoke.GetComponent<ParticleSystemRenderer>();
            smokeRenderer.GetPropertyBlock(smokeTint);
            smokeTint.SetColor(UnlitColorId, data.SmokeColor);
            smokeRenderer.SetPropertyBlock(smokeTint);
            smokeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
