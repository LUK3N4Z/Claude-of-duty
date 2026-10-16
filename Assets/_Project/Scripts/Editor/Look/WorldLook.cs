using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace FPS.EditorTools
{
    /// <summary>
    /// The gritty, high-contrast photoreal look every scene builder applies (Modern Warfare 2019 reference):
    /// pipeline features (SSR, SSGI, contact shadows), one shared world volume — physically based sky under a
    /// procedural cloud layer that casts shadows, a sun calibrated in lux, histogram auto exposure, ACES, film grain
    /// 0.18, chromatic aberration 0.08, SSR, half-resolution ray-marched SSGI, contact and micro shadows, SSAO,
    /// split-toned grading and thin volumetric fog — plus contact shadows on lamps and box-projected reflection probes
    /// per room. Idempotent: rerunning updates the shared assets in place.
    /// </summary>
    internal static class WorldLook
    {
        private const string ProfilePath = "Assets/_Project/Settings/Rendering/WorldLook.asset";
        private const string LegacyProfilePath = "Assets/_Project/Settings/Rendering/TestRangeAtmosphere.asset";
        private const string HDRPAssetPath = "Assets/_Project/Settings/Rendering/HDRenderPipelineAsset.asset";
        private const string CloudMapPath = "Assets/_Project/Art/Sky/CloudMap.png";

        /// <summary>Clear-sky afternoon sun (lux).</summary>
        internal const float SunLux = 90000f;
        internal const float SunTemperature = 5600f;
        internal const float FilmGrainIntensity = 0.18f;
        internal const float AberrationIntensity = 0.08f;

        /// <summary>Turns on the HDRP asset features the look needs (camera frame settings already enable them).</summary>
        public static void EnablePipelineFeatures()
        {
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(HDRPAssetPath);
            if (asset == null) return;
            TestWeaponBuilder.Configure(asset, so =>
            {
                const string root = "m_RenderPipelineSettings.";
                TestWeaponBuilder.Set(so, root + "supportSSR", true);
                TestWeaponBuilder.Set(so, root + "supportSSGI", true);
                TestWeaponBuilder.Set(so, root + "supportSSAO", true);
                TestWeaponBuilder.Set(so, root + "supportVolumetrics", true);
                TestWeaponBuilder.Set(so, root + "supportDecals", true);
                TestWeaponBuilder.Set(so, root + "hdShadowInitParams.supportContactShadows", true);
            });
            AssetDatabase.SaveAssets();
        }

        /// <summary>The shared world volume profile, created or refreshed.</summary>
        public static VolumeProfile Profile()
        {
            ProjectSetupWizard.EnsureFolder("Assets/_Project/Settings/Rendering");
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) == null && AssetDatabase.LoadAssetAtPath<VolumeProfile>(LegacyProfilePath) != null)
                AssetDatabase.MoveAsset(LegacyProfilePath, ProfilePath); // keeps the GUID scenes reference
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            VisualEnvironment environment = Get<VisualEnvironment>(profile);
            environment.skyType.Override((int)SkyType.PhysicallyBased);
            environment.cloudType.Override((int)CloudType.CloudLayer);
            environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            PhysicallyBasedSky sky = Get<PhysicallyBasedSky>(profile);
            sky.groundTint.Override(new Color(0.2f, 0.18f, 0.15f));
            sky.aerosolTint.Override(new Color(0.88f, 0.86f, 0.82f)); // dusty haze toward the horizon
            sky.aerosolDensity.Override(0.05f); // hazy, pale afternoon sky rather than a deep blue one

            CloudLayer clouds = Get<CloudLayer>(profile);
            clouds.opacity.Override(0.85f);
            clouds.layerA.cloudMap.Override(CloudMap());
            clouds.layerA.opacityR.Override(1f);
            clouds.layerA.lighting.Override(true);
            clouds.layerA.steps.Override(8);
            clouds.layerA.thickness.Override(0.6f);
            clouds.layerA.castShadows.Override(true);
            clouds.shadowMultiplier.Override(0.8f);

            Exposure exposure = Get<Exposure>(profile);
            exposure.mode.Override(ExposureMode.AutomaticHistogram);
            exposure.meteringMode.Override(MeteringMode.CenterWeighted);
            exposure.limitMin.Override(4.5f); // lamp-lit interiors (~100 lux)
            exposure.limitMax.Override(15.5f); // noon sun
            exposure.compensation.Override(0.7f);
            exposure.adaptationMode.Override(AdaptationMode.Progressive);
            exposure.adaptationSpeedDarkToLight.Override(2.5f);
            exposure.adaptationSpeedLightToDark.Override(1.2f);
            exposure.histogramPercentages.Override(new Vector2(40f, 92f));

            Get<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);

            FilmGrain grain = Get<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(FilmGrainIntensity);
            grain.response.Override(0.8f);

            Get<ChromaticAberration>(profile).intensity.Override(AberrationIntensity);

            Bloom bloom = Get<Bloom>(profile);
            bloom.intensity.Override(0.12f);
            bloom.scatter.Override(0.65f);

            ColorAdjustments grading = Get<ColorAdjustments>(profile);
            grading.contrast.Override(14f);
            grading.saturation.Override(-12f);

            SplitToning toning = Get<SplitToning>(profile);
            toning.shadows.Override(new Color(0.42f, 0.47f, 0.52f)); // cool shadows
            toning.highlights.Override(new Color(0.56f, 0.51f, 0.44f)); // warm highlights
            toning.balance.Override(10f);

            Vignette vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);

            ScreenSpaceReflection reflections = Get<ScreenSpaceReflection>(profile);
            reflections.enabled.Override(true);
            reflections.tracing.Override(RayCastingMode.RayMarching);

            GlobalIllumination gi = Get<GlobalIllumination>(profile);
            gi.enable.Override(true);
            gi.tracing.Override(RayCastingMode.RayMarching);
            gi.fullResolutionSS.Override(false); // half resolution

            ContactShadows contact = Get<ContactShadows>(profile);
            contact.enable.Override(true);
            contact.length.Override(0.12f);
            contact.maxDistance.Override(40f);

            MicroShadowing micro = Get<MicroShadowing>(profile);
            micro.enable.Override(true);
            micro.opacity.Override(1f);

            ScreenSpaceAmbientOcclusion ambient = Get<ScreenSpaceAmbientOcclusion>(profile);
            ambient.intensity.Override(1.4f);
            ambient.radius.Override(1.2f);
            ambient.directLightingStrength.Override(0.15f);

            // Thin outdoors so the 300 m targets stay readable; interiors add their own dust.
            Fog fog = Get<Fog>(profile);
            fog.enabled.Override(true);
            fog.enableVolumetricFog.Override(true);
            fog.meanFreePath.Override(250f);
            fog.albedo.Override(new Color(0.8f, 0.82f, 0.85f));

            foreach (VolumeComponent component in profile.components) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>Adds the global world volume under <paramref name="level"/>.</summary>
        public static Volume AddVolume(Transform level)
        {
            var volumeObject = new GameObject("World Look (sky, exposure, grading, fog)");
            volumeObject.transform.SetParent(level, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = Profile();
            return volume;
        }

        /// <summary>
        /// Calibrates the scene's directional light as the sun: lux, colour temperature, soft shadows, contact shadows,
        /// angular size, and the given orientation. Returns it (null when the scene has none).
        /// </summary>
        public static Light ConfigureSun(Scene scene, Vector3 euler)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    if (light.type != LightType.Directional) continue;
                    if (!light.TryGetComponent(out HDAdditionalLightData data)) data = light.gameObject.AddComponent<HDAdditionalLightData>();
                    light.transform.rotation = Quaternion.Euler(euler);
                    light.lightUnit = LightUnit.Lux;
                    light.intensity = SunLux;
                    light.useColorTemperature = true;
                    light.colorTemperature = SunTemperature;
                    light.color = Color.white;
                    light.shadows = LightShadows.Soft;
                    data.angularDiameter = 0.53f;
                    data.interactsWithSky = true;
                    EnableContactShadows(light);
                    EditorUtility.SetDirty(light);
                    EditorUtility.SetDirty(data);
                    return light;
                }
            }

            return null;
        }

        /// <summary>Screen-space contact shadows on a light (small occluders ground properly).</summary>
        public static void EnableContactShadows(Light light)
        {
            if (!light.TryGetComponent(out HDAdditionalLightData data)) data = light.gameObject.AddComponent<HDAdditionalLightData>();
            data.useContactShadow.useOverride = true;
            data.useContactShadow.@override = true;
        }

        /// <summary>
        /// An interior: a box-projected reflection probe plus a local volume that cuts the open-sky ambient light the
        /// room's roof and walls block (without baked probes the sky would light the inside like the outside), so the
        /// room is lit by its lamps, its openings and screen-space GI. Centre and size in <paramref name="parent"/> space.
        /// </summary>
        public static ReflectionProbe Interior(Transform parent, string roomName, Vector3 center, Vector3 size)
        {
            ReflectionProbe probe = RoomProbe(parent, $"{roomName} Reflections", center, size);
            var volumeObject = new GameObject($"{roomName} Interior Light") { layer = 2 }; // Ignore Raycast: never hit by queries
            volumeObject.transform.SetParent(parent, false);
            volumeObject.transform.localPosition = center;
            var bounds = volumeObject.AddComponent<BoxCollider>();
            bounds.isTrigger = true;
            bounds.size = size;
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.blendDistance = 0.75f;
            volume.priority = 10f;
            volume.sharedProfile = InteriorProfile();
            return probe;
        }

        private static VolumeProfile InteriorProfile()
        {
            const string path = "Assets/_Project/Settings/Rendering/InteriorLight.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            IndirectLightingController indirect = Get<IndirectLightingController>(profile);
            indirect.indirectDiffuseLightingMultiplier.Override(0.18f);
            indirect.reflectionLightingMultiplier.Override(0.35f);
            EditorUtility.SetDirty(indirect);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>
        /// A box-projected reflection probe for a room: realtime, rendered once when enabled, influence and projection
        /// the room's box (centre and size in <paramref name="parent"/> space) with soft blends at the walls.
        /// </summary>
        public static ReflectionProbe RoomProbe(Transform parent, string probeName, Vector3 center, Vector3 size)
        {
            var probeObject = new GameObject(probeName);
            probeObject.transform.SetParent(parent, false);
            probeObject.transform.localPosition = center;
            var probe = probeObject.AddComponent<ReflectionProbe>();
            var data = probeObject.AddComponent<HDAdditionalReflectionData>();
            data.mode = ProbeSettings.Mode.Realtime;
            data.realtimeMode = ProbeSettings.RealtimeMode.OnEnable;
            data.influenceVolume.shape = InfluenceShape.Box;
            data.influenceVolume.boxSize = size;
            data.influenceVolume.boxBlendDistancePositive = Vector3.one * 0.5f;
            data.influenceVolume.boxBlendDistanceNegative = Vector3.one * 0.5f;
            data.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume = true; // box projection: reflections line up with the walls
            EditorUtility.SetDirty(data);
            return probe;
        }

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component)) return component;
            component = profile.Add<T>();
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>Lat-long cumulus map (R density) for the cloud layer, generated once.</summary>
        private static Texture2D CloudMap()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(CloudMapPath);
            if (existing != null) return existing;

            ProjectSetupWizard.EnsureFolder("Assets/_Project/Art/Sky");
            const int width = 1024, height = 512;
            // Fractal noise sits in a narrow band around 0.5, so cover by rank: the densest 40% of billows become cloud.
            var billow = new float[width * height];
            var wisp = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = y / (float)height;
                for (int x = 0; x < width; x++)
                {
                    float u = x / (float)width;
                    billow[y * width + x] = SurfaceFinishes.Fbm(u, v * 0.5f, 6, 5, 913);
                    wisp[y * width + x] = SurfaceFinishes.Fbm(u * 2f, v, 12, 3, 271);
                }
            }

            Vector2 billowRange = Percentiles(billow, 0.6f, 0.92f);
            Vector2 wispRange = Percentiles(wisp, 0.75f, 0.97f);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float horizon = SurfaceFinishes.Smoothstep(0f, 0.12f, y / (float)height); // thin out at the horizon line
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    float density = SurfaceFinishes.Smoothstep(billowRange.x, billowRange.y, billow[i]) * 0.9f
                                    + SurfaceFinishes.Smoothstep(wispRange.x, wispRange.y, wisp[i]) * 0.3f;
                    byte d = (byte)Mathf.RoundToInt(Mathf.Clamp01(density * horizon) * 255f);
                    pixels[i] = new Color32(d, d, d, 255);
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(MaskMapPacker.FullPath(CloudMapPath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(CloudMapPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(CloudMapPath);
            importer.sRGBTexture = false;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(CloudMapPath);
        }

        /// <summary>The values at two quantiles (0–1) of <paramref name="values"/>.</summary>
        private static Vector2 Percentiles(float[] values, float low, float high)
        {
            var sorted = (float[])values.Clone();
            System.Array.Sort(sorted);
            return new Vector2(sorted[(int)(low * (sorted.Length - 1))], sorted[(int)(high * (sorted.Length - 1))]);
        }
    }
}
