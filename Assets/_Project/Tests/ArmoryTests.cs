using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FPS.Audio;
using FPS.Gameplay;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests
{
    public class ArmoryTests : AITestWorld
    {
        private string folder;

        public override void Setup()
        {
            base.Setup();
            folder = Path.Combine(Application.temporaryCachePath, "profile_tests_" + Guid.NewGuid().ToString("N"));
            SaveProfileManager.DirectoryOverride = folder;
        }

        public override void TearDown()
        {
            SaveProfileManager.DirectoryOverride = null;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator Profile_RoundTripsAsJson_AndSurvivesACorruptFile()
        {
            Assert.Throws<FormatException>(() => SaveProfileManager.FromJson(string.Empty));
            Assert.Throws<FormatException>(() => SaveProfileManager.FromJson("{\"version\":0}"));

            SaveProfileManager first = Manager();
            yield return null;
            Assert.IsFalse(first.LoadedFromDisk, "no file yet: defaults");
            first.Profile.blueprints.Add(new WeaponBlueprint { name = "Primary", weapon = "Rifle", attachments = new List<string> { "Optic_A", "Grip_B" } });
            first.Profile.loadout.primary = "Primary";
            first.Profile.loadout.lethal = "Frag";
            first.Profile.settings.fieldOfView = 90f;
            first.Profile.settings.upscaler = UpscalerMode.FSR2;
            first.Profile.settings.audioRange = AudioDynamicRange.NightMode;
            Assert.IsTrue(first.Save());
            Assert.IsTrue(File.Exists(first.FilePath));

            SaveProfileManager second = Manager();
            yield return null;
            Assert.IsTrue(second.LoadedFromDisk);
            PlayerProfile loaded = second.Profile;
            Assert.AreEqual("Rifle", loaded.Blueprint("Primary").weapon);
            CollectionAssert.AreEqual(new[] { "Optic_A", "Grip_B" }, loaded.Blueprint("Primary").attachments);
            Assert.AreEqual("Frag", loaded.loadout.lethal);
            Assert.AreEqual(90f, loaded.settings.fieldOfView);
            Assert.AreEqual(UpscalerMode.FSR2, loaded.settings.upscaler);
            Assert.AreEqual(AudioDynamicRange.NightMode, loaded.settings.audioRange);

            second.Profile.loadout.lethal = "Changed";
            Assert.IsTrue(second.Save());
            Assert.IsTrue(File.Exists(first.FilePath + ".bak"), "the previous save is kept");
            Assert.IsFalse(File.Exists(first.FilePath + ".tmp"));

            File.WriteAllText(first.FilePath, "{ this is not json");
            SaveProfileManager third = Manager();
            yield return null;
            Assert.IsTrue(third.LoadedFromDisk, "the backup steps in");
            Assert.AreEqual("Frag", third.Profile.loadout.lethal, "backup holds the save before last");
            Assert.IsTrue(File.Exists(first.FilePath + ".corrupt"), "the unreadable file is kept for inspection");
        }

        [UnityTest]
        public IEnumerator Profile_RestoresTheSavedBuild_AndSkipsMissingParts()
        {
            ArmoryCatalogSO catalog = Catalog(out WeaponModelRig model, out WeaponDataSO data, out AttachmentDataSO[] optics);
            RealisticWeaponController weapons = player.GetComponent<RealisticWeaponController>();
            weapons.SetLoadout(0, model, data, new[] { optics[0] });
            yield return new WaitForSeconds(0.2f);

            SaveProfileManager profiles = player.AddComponent<SaveProfileManager>();
            profiles.Configure(catalog, false);
            Assert.IsTrue(profiles.StoreLoadout(weapons, null));
            Assert.AreEqual(SaveProfileManager.PrimaryBlueprint, profiles.Profile.loadout.primary);
            Assert.AreEqual("Optic_A", profiles.Profile.Blueprint("Primary").attachments[0]);

            weapons.SetLoadout(0, model, data, new[] { optics[1] });
            profiles.Profile.Blueprint("Primary").attachments.Add("Retired_Part");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("unknown attachment 'Retired_Part'"));
            Assert.AreEqual(1, profiles.ApplyLoadout(weapons, null));
            CollectionAssert.AreEqual(new[] { optics[0] }, weapons.GetAttachments(0), "the saved build, minus the part that no longer exists");
        }

        [UnityTest]
        public IEnumerator Workbench_SwapsAPartLive_FitsItAndSaves()
        {
            ArmoryCatalogSO catalog = Catalog(out WeaponModelRig model, out WeaponDataSO data, out AttachmentDataSO[] optics);
            RealisticWeaponController weapons = player.GetComponent<RealisticWeaponController>();
            weapons.SetLoadout(0, model, data, new[] { optics[0] });
            SaveProfileManager profiles = player.AddComponent<SaveProfileManager>();
            profiles.Configure(catalog, false);
            yield return new WaitForSeconds(0.2f);

            GunsmithWorkbenchController bench = Bench(catalog);
            Assert.IsTrue(bench.CanInteract(player));
            bench.Interact(player);
            Assert.IsTrue(bench.IsActive);
            Assert.AreSame(bench, GunsmithWorkbenchController.Active);
            Assert.IsFalse(player.GetComponent<RealisticLocomotionController>().enabled, "the player stands at the bench");
            Assert.IsFalse(player.GetComponent<PlayerRig>().PlayerCamera.enabled);
            Assert.IsTrue(bench.BenchCamera.enabled);
            Assert.IsNotNull(bench.Preview);
            Assert.AreEqual(2, bench.Nodes.Count, "a marker on each mount (optic, underbarrel)");

            yield return null;
            WorkbenchNode node = bench.Nodes[0];
            Vector3 screen = bench.BenchCamera.WorldToScreenPoint(node.transform.position);
            Assert.IsTrue(bench.Pick(screen), "clicking the marker picks its mount");
            Assert.AreEqual(node.Slot, bench.SelectedSlot);

            bench.SelectSlot(AttachmentSlot.Optic);
            CollectionAssert.AreEquivalent(optics, bench.Options, "every optic in the catalog fits");
            float before = bench.PreviewData.MassKg;
            bench.Fit(optics[1]);
            CollectionAssert.Contains(bench.Build, optics[1]);
            CollectionAssert.DoesNotContain(bench.Build, optics[0], "one part per slot");
            Assert.AreEqual(before - optics[0].MassKg + optics[1].MassKg, bench.PreviewData.MassKg, 1e-4f, "stats follow the build");
            Assert.IsNotNull(bench.Preview.FindNode(optics[1].Prefab.name), "the part is on the model on the table");

            bench.Apply();
            CollectionAssert.Contains(weapons.GetAttachments(0), optics[1], "the carried weapon takes the build");
            Assert.IsTrue(File.Exists(profiles.FilePath), "and it is saved");
            Assert.AreEqual("Optic_B", profiles.Profile.Blueprint("Primary").attachments[0]);

            bench.Leave();
            Assert.IsNull(GunsmithWorkbenchController.Active);
            Assert.IsTrue(player.GetComponent<RealisticLocomotionController>().enabled, "control is back: off to the range");
            Assert.IsTrue(player.GetComponent<PlayerRig>().PlayerCamera.enabled);
            Assert.IsNull(bench.Preview);
        }

        [UnityTest]
        public IEnumerator SettingsMenu_AppliesEverySetting_AndTheMenuKeyPauses()
        {
            HDRPSettingsMenu menu = player.AddComponent<HDRPSettingsMenu>();
            yield return null;
            var ads = player.GetComponent<ProceduralADSAndOpticsController>();
            Camera view = player.GetComponent<PlayerRig>().PlayerCamera;

            GameSettings settings = menu.Settings;
            settings.fieldOfView = 90f;
            settings.scopeResolution = 2048;
            settings.motionBlur = MotionBlurLevel.High;
            settings.upscaler = UpscalerMode.DLSS;
            settings.renderScale = 67;
            settings.audioRange = AudioDynamicRange.NightMode;
            menu.Apply(settings);

            Assert.AreEqual(90f, ads.BaseFieldOfView);
            Assert.AreEqual(2048, ads.ScopeResolution);
            Assert.AreEqual(2048, ads.ScopeCamera.targetTexture.width, "the PiP target is rebuilt at the new size");
            MotionBlur blur = null;
            foreach (Volume volume in player.GetComponentsInChildren<Volume>())
            {
                if (volume.sharedProfile.TryGet(out MotionBlur found) && volume.name == "Graphics Settings") blur = found;
            }

            Assert.IsNotNull(blur);
            Assert.AreEqual(1f, blur.intensity.value);
            var cameraData = view.GetComponent<HDAdditionalCameraData>();
            Assert.IsTrue(cameraData.allowDynamicResolution, "upscaling renders below native");
            Assert.AreEqual(67f, menu.RenderPercentage);
            Assert.AreEqual(HDRPSettingsMenu.IsAvailable(UpscalerMode.DLSS), cameraData.allowDeepLearningSuperSampling, "DLSS only where present, else TAAU");
            Assert.AreEqual(AudioDynamicRange.NightMode, view.GetComponent<DynamicRangeProcessor>().Preset);

            var pipeline = (HDRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            GlobalDynamicResolutionSettings dynamicResolution = pipeline.currentPlatformRenderPipelineSettings.dynamicResolutionSettings;
            Assert.IsTrue(dynamicResolution.enabled, "the HDRP asset allows dynamic resolution");
            CollectionAssert.AreEqual(new[] { AdvancedUpscalers.DLSS, AdvancedUpscalers.FSR2 }, dynamicResolution.advancedUpscalersByPriority);

            settings.upscaler = UpscalerMode.Native;
            menu.Apply(settings);
            Assert.IsFalse(cameraData.allowDynamicResolution);
            Assert.AreEqual(100f, menu.RenderPercentage);

            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;
            Assert.IsTrue(menu.IsOpen, "Escape opens the options");
            Assert.IsFalse(player.GetComponent<RealisticLocomotionController>().enabled, "the game pauses for the player");
            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;
            Assert.IsFalse(menu.IsOpen);
            Assert.IsTrue(player.GetComponent<RealisticLocomotionController>().enabled);
        }

        [Test]
        public void DynamicRange_NightModeCompresses_WarTapesIsLoud_StudioIsTransparent()
        {
            Assert.AreEqual(-40f, DynamicRangeProcessor.OutputDb(AudioDynamicRange.StudioReference, -40f), 1e-4f);
            Assert.AreEqual(-6f, DynamicRangeProcessor.OutputDb(AudioDynamicRange.StudioReference, -6f), 1e-4f);
            float studioSpread = DynamicRangeProcessor.OutputDb(AudioDynamicRange.StudioReference, -6f) - DynamicRangeProcessor.OutputDb(AudioDynamicRange.StudioReference, -40f);
            float nightSpread = DynamicRangeProcessor.OutputDb(AudioDynamicRange.NightMode, -6f) - DynamicRangeProcessor.OutputDb(AudioDynamicRange.NightMode, -40f);
            Assert.Less(nightSpread, studioSpread * 0.5f, "night mode squeezes footsteps and gunfire together");
            Assert.Greater(DynamicRangeProcessor.OutputDb(AudioDynamicRange.NightMode, -40f), -40f, "quiet sounds lifted");
            Assert.Greater(DynamicRangeProcessor.OutputDb(AudioDynamicRange.WarTapes, -30f), -30f, "war tapes runs hot");

            var host = new GameObject("Mix");
            created.Add(host);
            host.AddComponent<AudioListener>().enabled = false;
            DynamicRangeProcessor processor = host.AddComponent<DynamicRangeProcessor>();
            foreach (AudioDynamicRange range in new[] { AudioDynamicRange.StudioReference, AudioDynamicRange.WarTapes, AudioDynamicRange.NightMode })
            {
                processor.Preset = range;
                var buffer = new float[2 * 48000];
                for (int i = 0; i < buffer.Length; i += 2) buffer[i] = buffer[i + 1] = Mathf.Sin(i * 0.05f) * 1.4f; // clipping-hot input
                processor.Process(buffer, 2);
                float peak = 0f;
                foreach (float sample in buffer) peak = Mathf.Max(peak, Mathf.Abs(sample));
                float ceiling = Mathf.Pow(10f, DynamicRangeProcessor.CurveFor(range).CeilingDb / 20f);
                Assert.LessOrEqual(peak, ceiling + 1e-4f, $"{range} never exceeds its ceiling");
            }
        }

        private SaveProfileManager Manager()
        {
            var host = new GameObject("Profile");
            created.Add(host);
            SaveProfileManager manager = host.AddComponent<SaveProfileManager>();
            return manager;
        }

        /// <summary>A test rifle with optic and underbarrel mounts, two optics and a grip in a catalog.</summary>
        private ArmoryCatalogSO Catalog(out WeaponModelRig model, out WeaponDataSO data, out AttachmentDataSO[] optics)
        {
            model = TestWeapons.CreateTemplate(created);
            Transform root = model.transform;
            UnityEngine.Object.DestroyImmediate(model.GetSightAligner(0, out _).GetComponent<WeaponSight>());
            Mount(model.GetSightAligner(0, out _), AttachmentSlot.Optic, Vector3.zero);
            Mount(root, AttachmentSlot.Underbarrel, new Vector3(0f, -0.03f, 0.25f));

            data = RifleData();
            data.name = "Rifle_Test";
            TestWeapons.SetField(data, "drawTime", 0.05f);
            optics = new[] { Part("Optic_A", AttachmentSlot.Optic, 0.15f), Part("Optic_B", AttachmentSlot.Optic, 0.5f) };
            AttachmentDataSO grip = Part("Grip_A", AttachmentSlot.Underbarrel, 0.1f);

            var catalog = ScriptableObject.CreateInstance<ArmoryCatalogSO>();
            created.Add(catalog);
            catalog.Configure(new[] { new ArmoryCatalogSO.WeaponEntry { model = model, data = data } }, new[] { optics[0], optics[1], grip },
                new GrenadeDataSO[0]);
            return catalog;
        }

        private AttachmentDataSO Part(string partName, AttachmentSlot slot, float massKg)
        {
            var model = new GameObject(partName + " Model");
            model.SetActive(false);
            created.Add(model);
            if (slot == AttachmentSlot.Optic) model.AddComponent<WeaponSight>().Configure(1f, 0.1f, 1.1f, null);

            var part = ScriptableObject.CreateInstance<AttachmentDataSO>();
            part.name = partName;
            created.Add(part);
            TestWeapons.SetField(part, "slot", slot);
            TestWeapons.SetField(part, "massKg", massKg);
            TestWeapons.SetField(part, "displayName", partName);
            TestWeapons.SetField(part, "prefab", model);
            return part;
        }

        private static void Mount(Transform parent, AttachmentSlot slot, Vector3 position)
        {
            var mount = new GameObject(AttachmentDataSO.MountName(slot)).transform;
            mount.SetParent(parent, false);
            mount.localPosition = position;
        }

        private GunsmithWorkbenchController Bench(ArmoryCatalogSO catalog)
        {
            var station = new GameObject("Workbench");
            created.Add(station);
            station.transform.position = new Vector3(0f, 1f, 30f);
            var turntable = new GameObject("Turntable").transform;
            turntable.SetParent(station.transform, false);
            var cameraObject = new GameObject("Bench Camera");
            cameraObject.transform.SetParent(station.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0.25f, -1.3f);
            cameraObject.transform.rotation = Quaternion.LookRotation(turntable.position - cameraObject.transform.position);
            var benchCamera = cameraObject.AddComponent<Camera>();
            benchCamera.enabled = false;

            GunsmithWorkbenchController bench = station.AddComponent<GunsmithWorkbenchController>();
            bench.Configure(catalog, benchCamera, turntable, null, null);
            return bench;
        }
    }
}
