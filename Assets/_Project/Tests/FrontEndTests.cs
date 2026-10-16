#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using FPS.AI;
using FPS.Core;
using FPS.EditorTools;
using FPS.Gameplay;
using FPS.Locomotion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests
{
    /// <summary>
    /// M15 front end: AI difficulty presets, the complete settings (look, volumes, phosphor, difficulty), key rebinding
    /// persisted in the profile, map schematics, and the deployment table → loading screen → operation → Return to Armory
    /// scene flow.
    /// </summary>
    public class FrontEndTests : AITestWorld
    {
        private string folder;

        public override void Setup()
        {
            base.Setup();
            folder = Path.Combine(Application.temporaryCachePath, "frontend_tests_" + Guid.NewGuid().ToString("N"));
            SaveProfileManager.DirectoryOverride = folder;
        }

        public override void TearDown()
        {
            CombatDifficulty.Current = AIDifficulty.Regular;
            AudioLevels.Master = AudioLevels.Effects = AudioLevels.Voice = 1f;
            AudioListener.volume = 1f;
            SaveProfileManager.DirectoryOverride = null;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            base.TearDown();
        }

        // ---- Difficulty ----

        [Test]
        public void Difficulty_PresetsGetSharperFromRecruitToRealism()
        {
            AIDifficultyProfile regular = CombatDifficulty.For(AIDifficulty.Regular);
            Assert.AreEqual(1f, regular.Awareness);
            Assert.AreEqual(1f, regular.AimSpread);
            for (int i = 1; i < 4; i++)
            {
                AIDifficultyProfile easier = CombatDifficulty.For((AIDifficulty)(i - 1)), harder = CombatDifficulty.For((AIDifficulty)i);
                Assert.Greater(harder.Awareness, easier.Awareness, "spots you sooner");
                Assert.Greater(harder.SightRange, easier.SightRange, "and farther");
                Assert.Less(harder.AimSpread, easier.AimSpread, "groups tighter");
                Assert.Less(harder.Reaction, easier.Reaction, "fires sooner");
            }
        }

        [UnityTest]
        public IEnumerator Difficulty_ChangesHowFastSoldiersSpotYou()
        {
            AIPerceptionSensor recruitEye = Observer(new Vector3(-1f, 1.6f, 35f), Vector3.back);
            CombatDifficulty.Current = AIDifficulty.Recruit;
            yield return new WaitForSeconds(0.6f);
            float recruit = recruitEye.Awareness;

            AIPerceptionSensor realismEye = Observer(new Vector3(1f, 1.6f, 35f), Vector3.back);
            CombatDifficulty.Current = AIDifficulty.Realism;
            yield return new WaitForSeconds(0.6f);
            Assert.Greater(realismEye.Awareness, recruit * 1.5f, "a Realism soldier spots the same player far faster");
        }

        // ---- Settings ----

        [UnityTest]
        public IEnumerator Settings_ApplyLookVolumesPhosphorAndDifficulty()
        {
            var goggles = player.AddComponent<NightVisionGoggles>();
            var menu = player.AddComponent<HDRPSettingsMenu>();
            yield return null;
            GameSettings settings = menu.Settings;
            settings.lookSensitivity = 1.8f;
            settings.adsSensitivity = 0.7f;
            settings.masterVolume = 0.5f;
            settings.effectsVolume = 0.8f;
            settings.voiceVolume = 0.3f;
            settings.nightVisionPhosphor = PhosphorColor.Green;
            settings.aiDifficulty = AIDifficulty.Hardened;
            menu.Apply(settings);

            var locomotion = player.GetComponent<RealisticLocomotionController>();
            Assert.AreEqual(1.8f, locomotion.SensitivityScale);
            Assert.AreEqual(0.7f, locomotion.AdsSensitivityScale);
            Assert.AreEqual(0.4f, AudioListener.volume, 1e-4f, "effects play at master x effects");
            Assert.AreEqual(0.15f, AudioLevels.VoiceVolume, 1e-4f, "voices at master x voice");
            Assert.AreEqual(PhosphorColor.Green, goggles.Phosphor);
            Assert.AreEqual(AIDifficulty.Hardened, CombatDifficulty.Current);

            TacticalAIBrain soldier = Soldier(new Vector3(0f, 0f, 30f), 180f);
            yield return null;
            AudioSource voice = soldier.GetComponent<AIBody>().Head.GetComponent<AudioSource>();
            Assert.IsTrue(voice.ignoreListenerVolume, "voices keep their own volume");

            menu.Open();
            for (int tab = 0; tab < 4; tab++) menu.ShowTab(tab);
            Assert.AreEqual(3, menu.Tab);
            menu.Close();
        }

        [UnityTest]
        public IEnumerator Rebinding_MovesAKey_TakesSharedHoldsAlong_PersistsAndResets()
        {
            player.AddComponent<SaveProfileManager>();
            var menu = player.AddComponent<HDRPSettingsMenu>();
            yield return null;
            int jumps = 0;
            reader.JumpPressed += () => jumps++;

            menu.Rebind(4); // Jump / climb
            Assert.IsTrue(reader.IsRebinding);
            yield return null;
            Press(keyboard.kKey);
            yield return null;
            Release(keyboard.kKey);
            yield return null;
            Assert.IsFalse(reader.IsRebinding);
            Assert.AreEqual("K", menu.BindingKey(4));
            PressAndRelease(keyboard.spaceKey);
            yield return null;
            Assert.AreEqual(0, jumps, "space no longer jumps");
            PressAndRelease(keyboard.kKey);
            yield return null;
            Assert.AreEqual(1, jumps, "K does");

            menu.Rebind(10); // Reload: the magazine check (hold R) rides along
            yield return null;
            Press(keyboard.yKey);
            yield return null;
            Release(keyboard.yKey);
            yield return null;
            Assert.AreEqual("Y", menu.BindingKey(10));
            int check = reader.KeyboardBinding("CheckMagazine");
            Assert.AreEqual("Y", reader.BindingName("CheckMagazine", check), "tap and hold stay on one key");

            string saved = File.ReadAllText(player.GetComponent<SaveProfileManager>().FilePath);
            StringAssert.Contains("<Keyboard>/k", saved, "bindings are in profile.json");

            menu.Rebind(5);
            yield return null;
            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;
            Assert.IsFalse(reader.IsRebinding, "Escape cancels");
            Assert.AreEqual("Left Shift", menu.BindingKey(5), "and keeps the old key");
            Assert.IsFalse(menu.IsOpen, "without opening the menu");

            menu.ResetBindings();
            Assert.AreEqual("Space", menu.BindingKey(4));
            Assert.AreEqual(string.Empty, menu.Settings.bindingOverrides.Replace("{\"bindings\":[]}", string.Empty));
        }

        // ---- Deployment and scene flow ----

        [Test]
        public void MapSchematics_ExistForEveryOperation_WithObjectivesMarked()
        {
            foreach (string scene in new[] { "Mode_Raid", "Mode_SearchAndDestroy", "Mode_Hardpoint", "LocomotionTestRange" })
            {
                var map = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPreviewBuilder.PathFor(scene));
                Assert.IsNotNull(map, $"{scene}: run the scene builders");
                Assert.AreEqual(512, map.width);
                if (scene == "LocomotionTestRange") continue;
                var copy = new Texture2D(2, 2);
                Assert.IsTrue(copy.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(map))));
                bool amber = false, structure = false;
                foreach (Color32 pixel in copy.GetPixels32())
                {
                    amber |= pixel.r > 200 && pixel.g > 140 && pixel.b < 90;
                    structure |= pixel.g > 180 && pixel.b > 200 && pixel.r < 150;
                }

                Object.DestroyImmediate(copy);
                Assert.IsTrue(amber, $"{scene}: objectives marked");
                Assert.IsTrue(structure, $"{scene}: buildings drawn");
            }
        }

        [UnityTest]
        public IEnumerator Deploy_LoadsTheOperationBehindTheLoadingScreen_ThenReturnsToTheArmory()
        {
            player.AddComponent<SaveProfileManager>();
            var stationObject = new GameObject("Deployment Table");
            created.Add(stationObject);
            var view = new GameObject("Station Camera").AddComponent<Camera>();
            created.Add(view.gameObject);
            stationObject.SetActive(false);
            var station = stationObject.AddComponent<DeploymentStation>();
            station.Configure(new[]
            {
                new DeploymentStation.Operation { title = "Tactical Raid", scene = "Mode_Raid", briefing = "Raid" },
                new DeploymentStation.Operation
                {
                    title = "Locomotion Test Range", scene = "LocomotionTestRange", briefing = "Range",
                    preview = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPreviewBuilder.PathFor("LocomotionTestRange")),
                },
            }, view, null);
            stationObject.SetActive(true);
            yield return null;

            station.Enter(player.GetComponent<PlayerRig>());
            Assert.IsTrue(station.IsActive);
            Assert.IsFalse(player.GetComponent<RealisticLocomotionController>().enabled, "the player is at the table");
            station.SelectOperation(1);
            station.SelectDifficulty(AIDifficulty.Hardened);
            station.Deploy();

            Assert.IsTrue(SceneFlow.IsLoading);
            Assert.AreEqual("LocomotionTestRange", SceneFlow.TargetScene);
            Assert.AreEqual(AIDifficulty.Hardened, CombatDifficulty.Current);
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(SceneFlow.LoadingScreenVisible, "loading screen up");
            Assert.IsNotEmpty(SceneFlow.CurrentTip, "with a field note");

            yield return WaitForScene("LocomotionTestRange");
            Assert.IsFalse(SceneFlow.LoadingScreenVisible, "and gone once the operation is up");
            var rangeMenu = Object.FindAnyObjectByType<HDRPSettingsMenu>();
            Assert.IsNotNull(rangeMenu);
            Assert.AreEqual(AIDifficulty.Hardened, rangeMenu.Settings.aiDifficulty, "difficulty saved with the profile");

            rangeMenu.ReturnToArmory();
            Assert.AreEqual(SceneFlow.ArmoryScene, SceneFlow.TargetScene);
            yield return WaitForScene(SceneFlow.ArmoryScene);
            Assert.IsNotNull(Object.FindAnyObjectByType<DeploymentStation>(), "back at the hub, the table waits");

            // Leave the hub so later tests start from an empty scene.
            Scene armory = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Front End Test Cleanup"));
            yield return SceneManager.UnloadSceneAsync(armory);
        }

        private static IEnumerator WaitForScene(string sceneName)
        {
            float deadline = Time.realtimeSinceStartup + 40f;
            while ((SceneManager.GetActiveScene().name != sceneName || SceneFlow.IsLoading) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name, "loaded in time");
            Assert.IsFalse(SceneFlow.IsLoading);
        }
    }
}
#endif
