using FPS.Audio;
using FPS.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FPS.Weapons
{
    /// <summary>
    /// Own-weapon muzzle effects, staged like a real muzzle blast: a one-frame HDRP point-light flash that lights dark
    /// rooms; the supersonic gas shock, a bright cone that punches out of the muzzle and collapses in ~45 ms; a burst
    /// of muzzle gas; glowing carbon embers thrown forward on ballistic arcs; and volumetric smoke wisps (local fog, so
    /// the sun, lamps and the weapon light illuminate them). Barrel heat builds under sustained fire and drives heat
    /// shimmer, and breech smoke lingers once firing stops while the barrel is still hot. Suppressors trap most of it.
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

        [Header("Blast Stages")]
        [Tooltip("Cone mesh along +Z, 1 m long at full size.")]
        [SerializeField] private Mesh shockMesh;
        [Tooltip("HDRP/Unlit additive, faded through _UnlitColor.")]
        [SerializeField] private Material shockMaterial;
        [SerializeField] private Color shockColor = new Color(3.5f, 2.2f, 1.2f, 1f);
        [Tooltip("Shock cone lifetime (s).")]
        [SerializeField] private float shockDuration = 0.045f;
        [Tooltip("Shock cone size at full flash (m along the bore, m across).")]
        [SerializeField] private Vector2 shockSize = new Vector2(0.22f, 0.07f);
        [SerializeField] private Material emberMaterial;
        [SerializeField] private Color emberColor = new Color(8f, 3f, 0.8f, 1f);
        [SerializeField, Min(0)] private int embersPerShot = 7;
        [Tooltip("3D noise mask for the volumetric smoke wisps (A = density).")]
        [SerializeField] private Texture3D smokeVolumeMask;
        [Tooltip("Minimum time between volumetric wisps (s): keeps the fog volume count bounded on full auto.")]
        [SerializeField] private float wispInterval = 0.12f;

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
        private ParticleSystem gas, shimmer, smoke, embers;
        private Renderer shock;
        private MaterialPropertyBlock shockBlock, emberBlock;
        private VolumetricPuff wispTemplate;
        private int flashFrame = -1;
        private float lastShotTime = float.NegativeInfinity, shockTime = float.NegativeInfinity, lastWispTime = float.NegativeInfinity;
        private float shockScale;
        private static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");

        /// <summary>Barrel heat, 0 cold to 1 glowing.</summary>
        public float Heat { get; private set; }
        public bool FlashVisible => flash.enabled;
        public int SmokeParticles => smoke.particleCount;
        public int ShimmerParticles => shimmer.particleCount;
        public int EmberParticles => embers.particleCount;
        public bool ShockVisible => shock != null && shock.enabled;

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
            embers = EffectParticles.Create("Muzzle Embers", muzzleMount, emberMaterial, new EffectParticles.Settings
            {
                Lifetime = new Vector2(0.2f, 0.7f), Speed = new Vector2(2f, 9f), Size = new Vector2(0.003f, 0.008f),
                SizeGrowth = 0.3f, ConeAngle = 16f, ConeRadius = 0.004f, GravityModifier = 0.5f, MaxParticles = 96,
                Stretch = 0.02f, NoFlipbook = true,
            });
            emberBlock = new MaterialPropertyBlock();
            var emberRenderer = embers.GetComponent<ParticleSystemRenderer>();
            emberRenderer.GetPropertyBlock(emberBlock);
            emberBlock.SetColor(UnlitColorId, emberColor);
            emberRenderer.SetPropertyBlock(emberBlock);

            var shockObject = new GameObject("Muzzle Shock");
            shockObject.transform.SetParent(muzzleMount, false);
            shockObject.AddComponent<MeshFilter>().sharedMesh = shockMesh;
            var shockRenderer = shockObject.AddComponent<MeshRenderer>();
            shockRenderer.sharedMaterial = shockMaterial;
            shockRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shockRenderer.receiveShadows = false;
            shockRenderer.enabled = false;
            shock = shockRenderer;
            shockBlock = new MaterialPropertyBlock();

            Transform templates = new GameObject("Muzzle Templates").transform;
            templates.gameObject.SetActive(false);
            templates.SetParent(transform, false);
            var wispObject = new GameObject("Smoke Wisp");
            wispObject.transform.SetParent(templates, false);
            var wispFog = wispObject.AddComponent<LocalVolumetricFog>();
            LocalVolumetricFogArtistParameters wisp = wispFog.parameters;
            wisp.volumeMask = smokeVolumeMask;
            wisp.textureTiling = Vector3.one;
            wisp.textureScrollingSpeed = new Vector3(0.02f, 0.15f, 0.04f);
            wisp.positiveFade = wisp.negativeFade = Vector3.one * 0.45f;
            wisp.falloffMode = LocalVolumetricFogFalloffMode.Exponential;
            wisp.distanceFadeStart = 30f;
            wisp.distanceFadeEnd = 45f;
            wispFog.parameters = wisp;
            wispTemplate = wispObject.AddComponent<VolumetricPuff>();
        }

        private void Start() => ObjectPoolManager.Instance.Prewarm(wispTemplate, 3, 12);

        private void OnEnable() => EventBus<GunshotEvent>.Subscribe(OnGunshot);

        private void OnDisable()
        {
            EventBus<GunshotEvent>.Unsubscribe(OnGunshot);
            flash.enabled = false;
            if (shock != null) shock.enabled = false;
        }

        private void OnGunshot(in GunshotEvent shot)
        {
            if (shot.Shooter != transform) return;

            FollowSockets();
            float flashScale = viewmodel.Data != null ? viewmodel.Data.MuzzleFlashMultiplier : 1f;
            if (flashScale > 0.05f)
            {
                flash.intensity = flashLumen * flashScale;
                flash.enabled = true;
                flashFrame = Time.frameCount;
            }

            int gasCount = Mathf.RoundToInt(gasParticlesPerShot * Mathf.Max(flashScale, 0.25f)); // suppressors still vent a little gas
            if (gasCount > 0) gas.Emit(gasCount);

            // The shock and embers scale with the unburnt-powder flash; a suppressor keeps them inside the can.
            if (shockMesh != null && shockMaterial != null && flashScale > 0.05f)
            {
                shockScale = Mathf.Sqrt(flashScale);
                shockTime = Time.time;
                shock.enabled = true;
            }

            int emberCount = Mathf.RoundToInt(embersPerShot * flashScale * Random.Range(0.6f, 1.4f));
            if (emberCount > 0 && emberMaterial != null) embers.Emit(emberCount);

            if (smokeVolumeMask != null && Time.time - lastWispTime >= wispInterval)
            {
                lastWispTime = Time.time;
                Transform muzzle = muzzleMount;
                VolumetricPuff puff = ObjectPoolManager.Instance.Spawn(wispTemplate, muzzle.position + muzzle.forward * 0.08f, Quaternion.identity, null, 2.4f);
                puff.Play(new Color(0.62f, 0.62f, 0.6f, 1f), 0.1f, 7f, Mathf.Lerp(3.5f, 1.8f, Heat), 2.4f, muzzle.forward * 1.4f + Vector3.up * 0.15f);
            }
            Heat = Mathf.Min(1f, Heat + heatPerShot);
            lastShotTime = Time.time;
        }

        private void LateUpdate()
        {
            FollowSockets();
            if (flash.enabled && Time.frameCount > flashFrame) flash.enabled = false; // exactly one rendered frame
            UpdateShock();

            float dt = Time.deltaTime;
            Heat *= Mathf.Exp(-dt / coolingTime);

            ParticleSystem.EmissionModule shimmerEmission = shimmer.emission;
            shimmerEmission.rateOverTime = Heat > shimmerThreshold ? maxShimmerRate * (Heat - shimmerThreshold) / (1f - shimmerThreshold) : 0f;

            bool quiet = Time.time - lastShotTime > smokeDelay;
            ParticleSystem.EmissionModule smokeEmission = smoke.emission;
            smokeEmission.rateOverTime = quiet && Heat > smokeThreshold ? maxSmokeRate * (Heat - smokeThreshold) / (1f - smokeThreshold) : 0f;
        }

        private void UpdateShock()
        {
            if (!shock.enabled) return;
            float t = (Time.time - shockTime) / shockDuration;
            if (t >= 1f)
            {
                shock.enabled = false;
                return;
            }

            // Punches out to full length in the first third, then collapses and fades.
            float grow = t < 0.33f ? t / 0.33f : 1f;
            float length = shockSize.x * shockScale * Mathf.Lerp(0.35f, 1f, grow);
            float width = shockSize.y * shockScale * Mathf.Lerp(0.6f, 1.2f, t);
            shock.transform.localScale = new Vector3(width, width, length);
            shock.GetPropertyBlock(shockBlock);
            shockBlock.SetColor(UnlitColorId, shockColor * ((1f - t) * (1f - t)));
            shock.SetPropertyBlock(shockBlock);
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
