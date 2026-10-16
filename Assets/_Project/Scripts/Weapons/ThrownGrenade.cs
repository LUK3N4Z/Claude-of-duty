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
    /// real fragment projectiles into the ballistics simulation and apply blast overpressure to bodies in the open;
    /// stun grenades raise a <see cref="FlashbangEvent"/>. Both flash, report through the acoustic system and suppress
    /// nearby listeners.
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
        private float detonateAt, flashOffAt, releaseAt;

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
            if (smoke != null) smoke.Clear(true);
        }

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
            if (Time.time >= releaseAt) ObjectPoolManager.Instance.Release(gameObject);
        }

        private void Detonate()
        {
            IsLive = false;
            HasDetonated = true;
            Vector3 point = transform.position;
            DetonationPoint = point;
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

            smoke = EffectParticles.Create("Detonation Smoke", transform, material, new EffectParticles.Settings
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
