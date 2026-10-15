using FPS.Audio;
using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Weapons
{
    /// <summary>
    /// Own-weapon muzzle effects: a one-frame HDRP point-light flash that lights dark rooms, a burst of muzzle gas per
    /// shot, barrel heat that builds under sustained fire and drives heat shimmer, and breech smoke that lingers once
    /// firing stops while the barrel is still hot.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ViewmodelRig))]
    public sealed class MuzzleAndSmokeVFXController : MonoBehaviour
    {
        [Header("Flash")]
        [SerializeField] private Color flashColor = new Color(1f, 0.78f, 0.5f);
        [Tooltip("Luminous flux of the one-frame flash (lm).")]
        [SerializeField] private float flashLumen = 40000f;
        [SerializeField] private float flashRange = 14f;

        [Header("Materials")]
        [Tooltip("HDRP/Unlit transparent, 4×4 alpha flipbook.")]
        [SerializeField] private Material gasMaterial;
        [SerializeField] private Material smokeMaterial;
        [Tooltip("HDRP/Unlit transparent with distortion only (heat haze).")]
        [SerializeField] private Material shimmerMaterial;

        [Header("Gas")]
        [SerializeField, Min(1)] private int gasParticlesPerShot = 6;

        [Header("Barrel Heat")]
        [Tooltip("Heat (0-1) added by each shot.")]
        [SerializeField] private float heatPerShot = 0.035f;
        [Tooltip("Cooling time constant (s).")]
        [SerializeField] private float coolingTime = 12f;
        [SerializeField, Range(0f, 1f)] private float shimmerThreshold = 0.15f;
        [SerializeField] private float maxShimmerRate = 30f;
        [SerializeField, Range(0f, 1f)] private float smokeThreshold = 0.2f;
        [Tooltip("Quiet time after the last shot before breech smoke shows (s).")]
        [SerializeField] private float smokeDelay = 0.35f;
        [SerializeField] private float maxSmokeRate = 14f;

        private ViewmodelRig viewmodel;
        private Light flash;
        private Transform muzzleMount, breechMount;
        private ParticleSystem gas, shimmer, smoke;
        private int flashFrame = -1;
        private float lastShotTime = float.NegativeInfinity;

        /// <summary>Barrel heat, 0 cold to 1 glowing.</summary>
        public float Heat { get; private set; }
        public bool FlashVisible => flash.enabled;
        public int SmokeParticles => smoke.particleCount;
        public int ShimmerParticles => shimmer.particleCount;

        private void Awake()
        {
            viewmodel = GetComponent<ViewmodelRig>();
            muzzleMount = new GameObject("Muzzle FX").transform;
            muzzleMount.SetParent(transform, false);
            breechMount = new GameObject("Breech FX").transform;
            breechMount.SetParent(transform, false);

            flash = muzzleMount.gameObject.AddComponent<Light>();
            muzzleMount.gameObject.AddComponent<HDAdditionalLightData>();
            flash.type = LightType.Point;
            flash.lightUnit = LightUnit.Lumen;
            flash.intensity = flashLumen;
            flash.range = flashRange;
            flash.color = flashColor;
            flash.shadows = LightShadows.None;
            flash.enabled = false;

            gas = EffectParticles.Create("Muzzle Gas", muzzleMount, gasMaterial, new EffectParticles.Settings
            {
                Lifetime = new Vector2(0.05f, 0.14f), Speed = new Vector2(4f, 12f), Size = new Vector2(0.04f, 0.12f),
                SizeGrowth = 2.5f, ConeAngle = 14f, ConeRadius = 0.005f, MaxParticles = 64,
            });
            shimmer = EffectParticles.Create("Heat Shimmer", muzzleMount, shimmerMaterial, new EffectParticles.Settings
            {
                Lifetime = new Vector2(0.5f, 0.9f), Speed = new Vector2(0.15f, 0.4f), Size = new Vector2(0.04f, 0.08f),
                SizeGrowth = 2f, ConeAngle = 20f, ConeRadius = 0.01f, GravityModifier = -0.04f, NoiseStrength = 0.15f, MaxParticles = 64,
            });
            smoke = EffectParticles.Create("Breech Smoke", breechMount, smokeMaterial, new EffectParticles.Settings
            {
                Lifetime = new Vector2(1.4f, 2.4f), Speed = new Vector2(0.05f, 0.2f), Size = new Vector2(0.04f, 0.08f),
                SizeGrowth = 4f, ConeAngle = 25f, ConeRadius = 0.01f, GravityModifier = -0.03f, NoiseStrength = 0.3f, MaxParticles = 96,
            });
        }

        private void OnEnable() => EventBus<GunshotEvent>.Subscribe(OnGunshot);

        private void OnDisable()
        {
            EventBus<GunshotEvent>.Unsubscribe(OnGunshot);
            flash.enabled = false;
        }

        private void OnGunshot(in GunshotEvent shot)
        {
            if (shot.Shooter != transform) return;

            FollowSockets();
            flash.enabled = true;
            flashFrame = Time.frameCount;
            gas.Emit(gasParticlesPerShot);
            Heat = Mathf.Min(1f, Heat + heatPerShot);
            lastShotTime = Time.time;
        }

        private void LateUpdate()
        {
            FollowSockets();
            if (flash.enabled && Time.frameCount > flashFrame) flash.enabled = false; // exactly one rendered frame

            float dt = Time.deltaTime;
            Heat *= Mathf.Exp(-dt / coolingTime);

            ParticleSystem.EmissionModule shimmerEmission = shimmer.emission;
            shimmerEmission.rateOverTime = Heat > shimmerThreshold ? maxShimmerRate * (Heat - shimmerThreshold) / (1f - shimmerThreshold) : 0f;

            bool quiet = Time.time - lastShotTime > smokeDelay;
            ParticleSystem.EmissionModule smokeEmission = smoke.emission;
            smokeEmission.rateOverTime = quiet && Heat > smokeThreshold ? maxSmokeRate * (Heat - smokeThreshold) / (1f - smokeThreshold) : 0f;
        }

        private void FollowSockets()
        {
            WeaponModelRig weapon = viewmodel.Weapon;
            if (weapon == null) return;

            Transform muzzle = weapon.MuzzleSocket != null ? weapon.MuzzleSocket : weapon.transform;
            muzzleMount.SetPositionAndRotation(muzzle.position, muzzle.rotation);
            Transform port = weapon.ChamberEjectionSocket != null ? weapon.ChamberEjectionSocket : muzzle;
            breechMount.SetPositionAndRotation(port.position, Quaternion.identity);

            // Heat haze and smoke rise whatever the weapon's aim.
            shimmer.transform.rotation = Quaternion.LookRotation(Vector3.up);
            smoke.transform.rotation = Quaternion.LookRotation(Vector3.up);
        }
    }
}
