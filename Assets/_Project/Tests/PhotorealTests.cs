#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using FPS.EditorTools;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    /// <summary>
    /// M12 photoreal look: the world volume and pipeline features, interiors, weathered environment materials, edge wear,
    /// combat VFX (sparks, thermal rings, dust clouds, muzzle shock, embers, wisps) and the scope lens wiring.
    /// </summary>
    public class PhotorealTests
    {
        private const string WorldLookPath = "Assets/_Project/Settings/Rendering/WorldLook.asset";
        private const float Triplanar = 5f;

        private readonly List<Object> created = new List<Object>();
        private InputActionAssetHolder input;
        private GameObject player;

        [SetUp]
        public void SetUp()
        {
            input = new InputActionAssetHolder();
            player = new GameObject("Player");
            created.Add(player);
            ViewmodelRig.Build(PlayerRig.Build(player, input.Reader));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ObjectPoolManager pool in Object.FindObjectsByType<ObjectPoolManager>()) Object.DestroyImmediate(pool.gameObject);
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            input.Dispose();
        }

        // ---- World look ----

        [Test]
        public void WorldLook_IsCalibrated_AndThePipelineSupportsIt()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(WorldLookPath);
            Assert.IsNotNull(profile, "run 5. Build Test Range");
            Assert.IsTrue(profile.TryGet(out VisualEnvironment environment));
            Assert.AreEqual((int)SkyType.PhysicallyBased, environment.skyType.value);
            Assert.AreEqual((int)CloudType.CloudLayer, environment.cloudType.value);
            Assert.IsTrue(profile.TryGet(out Exposure exposure));
            Assert.AreEqual(ExposureMode.AutomaticHistogram, exposure.mode.value);
            Assert.IsTrue(profile.TryGet(out Tonemapping tonemapping));
            Assert.AreEqual(TonemappingMode.ACES, tonemapping.mode.value);
            Assert.IsTrue(profile.TryGet(out FilmGrain grain));
            Assert.AreEqual(0.18f, grain.intensity.value, 1e-4f);
            Assert.IsTrue(profile.TryGet(out ChromaticAberration aberration));
            Assert.AreEqual(0.08f, aberration.intensity.value, 1e-4f);
            Assert.IsTrue(profile.TryGet(out ScreenSpaceReflection reflections) && reflections.enabled.value, "SSR");
            Assert.IsTrue(profile.TryGet(out GlobalIllumination gi) && gi.enable.value, "SSGI");
            Assert.IsTrue(profile.TryGet(out ContactShadows contact) && contact.enable.value, "contact shadows");
            Assert.IsTrue(profile.TryGet(out MicroShadowing micro) && micro.enable.value, "micro shadows");

            var asset = (HDRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
            RenderPipelineSettings settings = asset.currentPlatformRenderPipelineSettings;
            Assert.IsTrue(settings.supportSSR);
            Assert.IsTrue(settings.supportSSGI);
            Assert.IsTrue(settings.supportSSAO);
            Assert.IsTrue(settings.supportVolumetrics);
            Assert.IsTrue(settings.supportDecals);
            Assert.IsTrue(settings.hdShadowInitParams.supportContactShadows);
        }

        [Test]
        public void CloudMap_CoversPartOfTheSky_AndClearsAtTheHorizon()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(WorldLookPath);
            Assert.IsTrue(profile.TryGet(out CloudLayer clouds));
            Texture2D map = Pixels(clouds.layerA.cloudMap.value);
            Color32[] pixels = map.GetPixels32();
            int covered = 0, horizonCovered = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].r <= 64) continue;
                covered++;
                if (i / map.width < 4) horizonCovered++; // bottom rows: the horizon line
            }

            float coverage = covered / (float)pixels.Length;
            Assert.That(coverage, Is.InRange(0.15f, 0.7f), "broken cumulus, neither clear nor overcast");
            Assert.AreEqual(0, horizonCovered, "clouds thin out to nothing at the horizon");
            Object.DestroyImmediate(map);
        }

        [Test]
        public void Interior_AddsABoxProjectedProbe_AndCutsTheSkyAmbient()
        {
            var level = new GameObject("Level").transform;
            created.Add(level.gameObject);
            var size = new Vector3(6f, 3f, 8f);
            ReflectionProbe probe = WorldLook.Interior(level, "Room", new Vector3(0f, 1.5f, 0f), size);

            var data = probe.GetComponent<HDAdditionalReflectionData>();
            Assert.AreEqual(ProbeSettings.Mode.Realtime, data.mode);
            Assert.AreEqual(ProbeSettings.RealtimeMode.OnEnable, data.realtimeMode);
            Assert.AreEqual(InfluenceShape.Box, data.influenceVolume.shape);
            Assert.AreEqual(size, data.influenceVolume.boxSize);
            Assert.IsTrue(data.settingsRaw.proxySettings.useInfluenceVolumeAsProxyVolume, "box projection");

            Volume volume = level.Find("Room Interior Light").GetComponent<Volume>();
            Assert.IsFalse(volume.isGlobal);
            var bounds = volume.GetComponent<BoxCollider>();
            Assert.IsTrue(bounds.isTrigger);
            Assert.AreEqual(size, bounds.size);
            Assert.AreEqual(2, volume.gameObject.layer, "Ignore Raycast: bullets and lasers pass the trigger");
            Assert.IsTrue(volume.sharedProfile.TryGet(out IndirectLightingController indirect));
            Assert.Less(indirect.indirectDiffuseLightingMultiplier.value, 0.5f, "roofed rooms are not lit by the open sky");
            Assert.Less(indirect.reflectionLightingMultiplier.value, 1f);
        }

        // ---- Environment materials ----

        [Test]
        public void EnvironmentDressing_IsTriplanarAndHeightBlended_WetOnlyOnTheGround()
        {
            var level = new GameObject("Level").transform;
            created.Add(level.gameObject);
            Renderer floor = Blockout(level, "Floor");
            Renderer wall = Blockout(level, "Wall");
            Renderer prop = Blockout(level, "Prop Wall");
            prop.gameObject.layer = GameLayers.Interactable;

            Assert.AreEqual(2, EnvironmentMaterials.Dress(level, EnvironmentSurface.Concrete), "world geometry only");
            Material floorMaterial = floor.sharedMaterial, wallMaterial = wall.sharedMaterial;
            Assert.AreEqual("HDRP/LayeredLit", wallMaterial.shader.name);
            Assert.AreEqual(Triplanar, wallMaterial.GetFloat("_UVBase0"), "no stretching on scaled cubes");
            Assert.AreEqual(Triplanar, wallMaterial.GetFloat("_UVBlendMask"));
            Assert.AreEqual(1f, wallMaterial.GetFloat("_UseHeightBasedBlend"));
            Assert.AreEqual(2f, wallMaterial.GetFloat("_LayerCount"), "walls: concrete + grime, dry");
            Assert.AreEqual(3f, floorMaterial.GetFloat("_LayerCount"), "concrete floors puddle");
            Assert.AreSame(wallMaterial.GetTexture("_BaseColorMap0"), floorMaterial.GetTexture("_BaseColorMap0"), "same concrete");

            EnvironmentMaterials.Dress(level, EnvironmentSurface.Dirt);
            Assert.AreEqual("Dirt", floor.sharedMaterial.name);
            Assert.AreEqual(3f, floor.sharedMaterial.GetFloat("_LayerCount"));
            Assert.AreSame(wallMaterial, wall.sharedMaterial);

            Texture2D weather = Pixels(wallMaterial.GetTexture("_LayerMaskMap"));
            Color32[] pixels = weather.GetPixels32();
            int grimy = 0, wet = 0;
            foreach (Color32 pixel in pixels)
            {
                if (pixel.r > 64) grimy++;
                if (pixel.g > 64) wet++;
            }

            Assert.That(grimy / (float)pixels.Length, Is.InRange(0.05f, 0.6f), "grime in patches");
            Assert.That(wet / (float)pixels.Length, Is.InRange(0.03f, 0.4f), "puddles in patches");
            Assert.AreEqual(255, pixels[0].a, "alpha is the main layer's weight: the surface itself is always there");
            Object.DestroyImmediate(weather);
        }

        [Test]
        public void EdgeWear_MarksChamfersInUV1_AndWornFinishesReadIt()
        {
            var kit = new MeshKit();
            kit.Box(Vector3.zero, new Vector3(0.1f, 0.06f, 0.2f), 0.004f);
            Mesh box = kit.ToMesh("box");
            var wear = new List<Vector2>();
            box.GetUVs(1, wear);
            Vector3[] normals = box.normals;
            Assert.AreEqual(box.vertexCount, wear.Count);
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 n = normals[i];
                bool face = Mathf.Max(Mathf.Abs(n.x), Mathf.Max(Mathf.Abs(n.y), Mathf.Abs(n.z))) > 0.999f;
                Assert.AreEqual(face ? 0.25f : 0.75f, wear[i].x, 1e-4f, face ? "broad faces keep their finish" : "chamfers wear");
            }

            Object.DestroyImmediate(box);

            var lathe = new MeshKit();
            lathe.Cylinder(0.02f, -0.05f, 0.1f, 24, 0.002f);
            Mesh cylinder = lathe.ToMesh("cylinder");
            cylinder.GetUVs(1, wear);
            int edges = 0;
            foreach (Vector2 uv in wear) edges += uv.x > 0.5f ? 1 : 0;
            Assert.That(edges, Is.InRange(1, wear.Count - 1), "the chamfered lips wear, the barrel wall does not");
            Object.DestroyImmediate(cylinder);

            Material steel = SurfaceFinishes.Get(SurfaceFinish.ParkerizedSteel);
            Assert.AreEqual(1f, steel.GetFloat("_UVDetail"), "detail map sampled with UV1");
            Assert.AreSame(SurfaceFinishes.EdgeWearDetail(), steel.GetTexture("_DetailMap"));
            Assert.AreNotSame(SurfaceFinishes.EdgeWearDetail(), SurfaceFinishes.Get(SurfaceFinish.Rubber).GetTexture("_DetailMap"));

            Texture2D detail = Pixels(SurfaceFinishes.EdgeWearDetail());
            Color32[] texels = detail.GetPixels32();
            Assert.AreEqual(128f, texels[detail.height / 2 * detail.width + detail.width / 4].r, 1f, "left half is neutral");
            int worn = 0;
            for (int y = 0; y < detail.height; y += 4) worn += texels[y * detail.width + detail.width * 3 / 4].r > 170 ? 1 : 0;
            Assert.Greater(worn, 0, "right half brightens toward bare metal");
            Object.DestroyImmediate(detail);
        }

        [Test]
        public void ScopeLens_IsWiredToThePlayer_AndTheOpticsCarryTheirReticles()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/Player.prefab");
            var lensShader = (Shader)new SerializedObject(prefab.GetComponent<ProceduralADSAndOpticsController>()).FindProperty("lensShader").objectReferenceValue;
            Assert.IsNotNull(lensShader);
            Assert.AreEqual("Hidden/FPS/ScopeLens", lensShader.name);
            Assert.IsNotNull(prefab.GetComponent<NightVisionGoggles>());
            Assert.AreEqual(1, (int)ReticleStyle.DuplexDot, "shader contract: _ReticleStyle 1 = dot");
            Assert.AreEqual(2, (int)ReticleStyle.Chevron, "shader contract: _ReticleStyle 2 = chevron");

            WeaponSight marksman = MagnifiedSight("Assets/_Project/Prefabs/Weapons/TestMarksman.prefab");
            Assert.AreEqual(ReticleStyle.DuplexDot, marksman.Reticle);
            WeaponSight scope = MagnifiedSight("Assets/_Project/Prefabs/Weapons/Attachments/Optic_Scope4x.prefab");
            Assert.AreEqual(ReticleStyle.Chevron, scope.Reticle);
            Assert.Greater(scope.ReticleGlow.r, 1f, "HDR illumination");
        }

        // ---- Combat VFX ----

        [UnityTest]
        public IEnumerator SteelSparksAndGlows_TheRingCools_MasonryLeavesADustCloud()
        {
            Material spark = Track(new Material(Shader.Find("HDRP/Unlit")));
            Material thermal = Track(new Material(Shader.Find("HDRP/Decal")));
            var mask = Track(new Texture3D(4, 4, 4, TextureFormat.RGBA32, false));
            var host = new GameObject("Impacts");
            created.Add(host);
            host.SetActive(false); // configure before Awake builds the templates
            var impacts = host.AddComponent<ImpactEffectsController>();
            TestWeapons.SetField(impacts, "sparkMaterial", spark);
            TestWeapons.SetField(impacts, "thermalDecalMaterial", thermal);
            TestWeapons.SetField(impacts, "dustVolumeMask", mask);
            host.SetActive(true);
            yield return null;

            SurfaceMaterialSO steel = Surface(sparks: true, dust: false);
            SurfaceMaterialSO concrete = Surface(sparks: false, dust: true);
            var point = new Vector3(0f, 1f, 6f);
            Impact(steel, point, ImpactOutcome.Stopped);
            yield return null;

            FadingDecal ring = ActiveNear<FadingDecal>(point);
            Assert.IsNotNull(ring, "hot ring around the hole");
            Assert.Greater(ring.Fade, 0.9f);
            Assert.Greater(SparkCount(spark), 0, "spark shower");
            Assert.IsNull(ActiveNear<VolumetricPuff>(point), "steel throws no dust cloud");

            yield return new WaitForSeconds(ring.Duration * 0.5f);
            Assert.That(ring.Fade, Is.InRange(0.1f, 0.4f), "Newtonian cooling: a quarter of the glow at half time");
            yield return new WaitForSeconds(ring.Duration * 0.6f);
            Assert.IsTrue(!ring.gameObject.activeInHierarchy || ring.Fade < 0.01f, "cooled and returned to the pool");

            Impact(steel, point + Vector3.right, ImpactOutcome.Ricocheted);
            yield return null;
            Assert.IsNull(ActiveNear<FadingDecal>(point + Vector3.right), "a glancing round leaves no thermal ring");

            Impact(concrete, point + Vector3.left, ImpactOutcome.Stopped);
            yield return null;
            VolumetricPuff cloud = ActiveNear<VolumetricPuff>(point + Vector3.left);
            Assert.IsNotNull(cloud, "masonry dust hangs in the air");
            float size = cloud.Size.x, freePath = cloud.MeanFreePath;
            yield return new WaitForSeconds(1f);
            Assert.Greater(cloud.Size.x, size * 1.5f, "the cloud spreads");
            Assert.Greater(cloud.MeanFreePath, freePath, "and thins");
        }

        [UnityTest]
        public IEnumerator Gunshot_PunchesAShockCone_ThrowsEmbers_AndLeavesASmokeWisp()
        {
            var vfx = player.GetComponent<MuzzleAndSmokeVFXController>();
            var data = Track(ScriptableObject.CreateInstance<WeaponDataSO>());
            player.GetComponent<ViewmodelRig>().Equip(TestWeapons.CreateTemplate(created), data);
            Mesh cone = Track(new Mesh());
            TestWeapons.SetField(vfx, "shockMesh", cone);
            TestWeapons.SetField(vfx, "shockMaterial", Track(new Material(Shader.Find("HDRP/Unlit"))));
            TestWeapons.SetField(vfx, "emberMaterial", Track(new Material(Shader.Find("HDRP/Unlit"))));
            TestWeapons.SetField(vfx, "smokeVolumeMask", Track(new Texture3D(4, 4, 4, TextureFormat.RGBA32, false)));
            yield return null;

            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = Vector3.zero, Shooter = player.transform });
            Assert.IsTrue(vfx.ShockVisible, "muzzle shock cone");
            Assert.Greater(vfx.EmberParticles, 0, "glowing carbon embers");
            Assert.IsNotNull(ActiveNear<VolumetricPuff>(player.GetComponent<ViewmodelRig>().Weapon.MuzzleSocket.position), "volumetric smoke wisp");

            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(vfx.ShockVisible, "the shock lasts a few frames");
        }

        // ---- Helpers ----

        private T Track<T>(T asset) where T : Object
        {
            created.Add(asset);
            return asset;
        }

        private SurfaceMaterialSO Surface(bool sparks, bool dust)
        {
            var surface = Track(ScriptableObject.CreateInstance<SurfaceMaterialSO>());
            TestWeapons.SetField(surface, "sparks", sparks);
            TestWeapons.SetField(surface, "lingeringDust", dust);
            return surface;
        }

        private static void Impact(SurfaceMaterialSO surface, Vector3 point, ImpactOutcome outcome) =>
            EventBus<ProjectileImpactEvent>.Raise(new ProjectileImpactEvent
            {
                Point = point, Normal = Vector3.back, Velocity = Vector3.forward * 800f, Surface = surface, Outcome = outcome,
            });

        /// <summary>An active instance within 0.5 m of <paramref name="point"/> (pooled effects from earlier tests may linger elsewhere).</summary>
        private static T ActiveNear<T>(Vector3 point) where T : Component
        {
            foreach (T found in Object.FindObjectsByType<T>())
            {
                if (found.gameObject.activeInHierarchy && Vector3.Distance(found.transform.position, point) < 0.5f) return found;
            }

            return null;
        }

        private static int SparkCount(Material spark)
        {
            int count = 0;
            foreach (ParticleSystem system in Object.FindObjectsByType<ParticleSystem>())
            {
                if (system.gameObject.activeInHierarchy && system.GetComponent<ParticleSystemRenderer>().sharedMaterial == spark)
                    count += system.particleCount;
            }

            return count;
        }

        private Renderer Blockout(Transform level, string objectName)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.layer = GameLayers.WorldGeometry;
            cube.transform.SetParent(level, false);
            return cube.GetComponent<Renderer>();
        }

        private static WeaponSight MagnifiedSight(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, prefabPath);
            foreach (WeaponSight sight in prefab.GetComponentsInChildren<WeaponSight>(true))
            {
                if (sight.Magnification > 1.01f) return sight;
            }

            Assert.Fail($"{prefabPath} has no magnified sight");
            return null;
        }

        /// <summary>A readable copy of a generated texture, decoded from its PNG on disk.</summary>
        private static Texture2D Pixels(Texture texture)
        {
            Assert.IsNotNull(texture);
            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Assert.IsTrue(copy.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(texture))));
            return copy;
        }

        /// <summary>The project's input actions behind a fresh InputReader, both destroyed on dispose.</summary>
        private sealed class InputActionAssetHolder : System.IDisposable
        {
            private readonly UnityEngine.InputSystem.InputActionAsset actions;

            public InputActionAssetHolder() => Reader = TestInput.CreateReader(out actions);

            public InputReader Reader { get; }

            public void Dispose()
            {
                Object.DestroyImmediate(Reader);
                Object.DestroyImmediate(actions);
            }
        }
    }
}
#endif
