#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FPS.AI;
using FPS.Ballistics;
using FPS.Locomotion;
using FPS.Weapons;
using UnityEditor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests
{
    public class RenderCapture : InputTestFixture
    {
        [UnityTest]
        public IEnumerator CaptureHipAdsCanted()
        {
            string dir = Environment.GetEnvironmentVariable("FPS_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("FPS_CAPTURE_DIR not set");
            TestInput.IgnoreFocus();
            FPS.Gameplay.SaveProfileManager.DirectoryOverride = FreshProfileFolder(dir, "range");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/LocomotionTestRange.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var rig = Object.FindAnyObjectByType<PlayerRig>();
            var cc = rig.GetComponent<CharacterController>();
            SquadCoordinator[] squads = Object.FindObjectsByType<SquadCoordinator>();
            foreach (SquadCoordinator squad in squads) squad.gameObject.SetActive(false); // the opposing force joins at the end
            var playerHealth = rig.GetComponent<TacticalHealthAndArmorSystem>();
            TestWeapons.SetField(playerHealth, "maxHealth", 100000f);
            playerHealth.ResetState();
            cc.enabled = false;
            rig.transform.SetPositionAndRotation(new Vector3(20f, 0f, 2f), Quaternion.identity);
            cc.enabled = true;

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rig.PlayerCamera.targetTexture = rt;
            yield return new WaitForSeconds(2.5f);
            Save(rt, Path.Combine(dir, "hip.png"));

            Press(mouse.rightButton);
            yield return new WaitForSeconds(1.5f);
            Save(rt, Path.Combine(dir, "ads.png"));
            var ads = rig.GetComponent<ProceduralADSAndOpticsController>();
            Debug.Log($"[Capture] blend={ads.AdsBlend} scopeEnabled={ads.ScopeCamera.enabled} scopeFov={ads.ScopeCamera.fieldOfView} mainFov={rig.PlayerCamera.fieldOfView}");
            SaveHdr(ads.ScopeCamera.targetTexture, Path.Combine(dir, "scope.png"));

            Press(keyboard.xKey);
            Release(keyboard.xKey);
            yield return new WaitForSeconds(1f);
            Save(rt, Path.Combine(dir, "canted.png"));

            Release(mouse.rightButton);
            var weapons = rig.GetComponent<RealisticWeaponController>();
            weapons.ActiveState.MagazineRounds = 10;
            weapons.RequestReload(false);
            yield return new WaitForSeconds(0.45f);
            Save(rt, Path.Combine(dir, "reload.png"));

            yield return new WaitForSeconds(2.5f);
            weapons.SelectSlot(1);
            yield return new WaitForSeconds(1.5f);
            Save(rt, Path.Combine(dir, "marksman_hip.png"));
            Press(mouse.rightButton);
            yield return new WaitForSeconds(1.5f);
            Save(rt, Path.Combine(dir, "marksman_ads.png"));

            Release(mouse.rightButton);
            weapons.SelectSlot(0);
            yield return new WaitForSeconds(1.2f);
            cc.enabled = false;
            rig.transform.SetPositionAndRotation(new Vector3(-8f, 0f, 7f), Quaternion.identity);
            cc.enabled = true;
            yield return new WaitForSeconds(0.6f);
            for (int i = 0; i < 6; i++)
            {
                weapons.PullTrigger();
                weapons.ReleaseTrigger();
                yield return new WaitForSeconds(0.15f);
            }

            Save(rt, Path.Combine(dir, "impacts.png"));

            // Rail devices in the dim shoot house, facing its north wall.
            LogBuild("default", weapons);
            Teleport(rig, cc, new Vector3(-26f, 0f, 4f));
            weapons.ToggleDevices();
            yield return new WaitForSeconds(1f);
            Save(rt, Path.Combine(dir, "devices.png"));
            weapons.ToggleDevices();

            // Night vision in the shoot house: the goggles' view, then canted active aim by the infrared laser.
            var goggles = rig.GetComponent<NightVisionGoggles>();
            goggles.SetOn(true);
            yield return new WaitForSeconds(1.5f);
            Save(rt, Path.Combine(dir, "nvg.png"));
            Press(mouse.rightButton);
            yield return new WaitForSeconds(0.8f);
            Save(rt, Path.Combine(dir, "nvg_active_aim.png"));
            Debug.Log($"[Capture] nvg: blend={goggles.Blend} activeAim={rig.GetComponent<ProceduralADSAndOpticsController>().IsActiveAiming}");
            Release(mouse.rightButton);
            goggles.SetOn(false);
            yield return new WaitForSeconds(0.6f);

            // Inspection, a vault and a mantle on the traversal course, and the shoot house door.
            var locomotion = rig.GetComponent<RealisticLocomotionController>();
            Teleport(rig, cc, new Vector3(0f, 0f, -4f));
            locomotion.SetLook(0f, 0f);
            yield return new WaitForSeconds(0.4f);
            PressAndRelease(keyboard.iKey);
            yield return new WaitForSeconds(1f);
            Save(rt, Path.Combine(dir, "inspect_markings.png"));
            yield return new WaitForSeconds(1.4f);
            Save(rt, Path.Combine(dir, "inspect_port.png"));
            yield return new WaitForSeconds(1.3f);

            Teleport(rig, cc, new Vector3(-8f, 0f, -2.6f));
            locomotion.SetLook(0f, 10f);
            yield return new WaitForSeconds(0.3f);
            Press(keyboard.wKey);
            yield return null;
            PressAndRelease(keyboard.spaceKey);
            yield return new WaitForSeconds(0.22f);
            Save(rt, Path.Combine(dir, "vault.png"));
            yield return new WaitForSeconds(0.8f);
            Release(keyboard.wKey);

            Teleport(rig, cc, new Vector3(-16f, 0f, -2.8f));
            locomotion.SetLook(0f, 15f);
            yield return new WaitForSeconds(0.3f);
            Press(keyboard.wKey);
            yield return null;
            PressAndRelease(keyboard.spaceKey);
            yield return new WaitForSeconds(0.3f);
            Save(rt, Path.Combine(dir, "mantle.png"));
            yield return new WaitForSeconds(1f);
            Release(keyboard.wKey);
            Debug.Log($"[Capture] traversal: on the platform at y={rig.transform.position.y:F2}");

            Teleport(rig, cc, new Vector3(-26f, 0f, 1.6f));
            locomotion.SetLook(0f, 0f);
            yield return new WaitForSeconds(0.4f);
            PressAndRelease(keyboard.fKey);
            yield return new WaitForSeconds(1.2f);
            Save(rt, Path.Combine(dir, "door_open.png"));
            locomotion.SetLook(0f, 0f);

            // Live gunsmith swaps: a short suppressed CQB build, then a long compensated one.
            Teleport(rig, cc, new Vector3(20f, 0f, 2f));
            weapons.SetAttachment(0, AttachmentSlot.Barrel, Part("Barrel_10in"));
            weapons.SetAttachment(0, AttachmentSlot.Muzzle, Part("Muzzle_Suppressor"));
            weapons.SetAttachment(0, AttachmentSlot.Optic, Part("Optic_RedDot"));
            weapons.SetAttachment(0, AttachmentSlot.Underbarrel, Part("Grip_Vertical"));
            weapons.SetAttachment(0, AttachmentSlot.Stock, Part("Stock_Collapsed"));
            LogBuild("cqb", weapons);
            yield return new WaitForSeconds(1f);
            Save(rt, Path.Combine(dir, "cqb_hip.png"));
            Press(mouse.rightButton);
            yield return new WaitForSeconds(1.5f);
            Save(rt, Path.Combine(dir, "reddot_ads.png"));
            Release(mouse.rightButton);

            weapons.SetAttachment(0, AttachmentSlot.Barrel, Part("Barrel_20in"));
            weapons.SetAttachment(0, AttachmentSlot.Muzzle, Part("Muzzle_Compensator"));
            weapons.SetAttachment(0, AttachmentSlot.Optic, Part("Optic_Scope4x"));
            weapons.SetAttachment(0, AttachmentSlot.Underbarrel, Part("Grip_Angled"));
            weapons.SetAttachment(0, AttachmentSlot.Stock, Part("Stock_Fixed"));
            LogBuild("long", weapons);
            yield return new WaitForSeconds(1.2f);
            Save(rt, Path.Combine(dir, "long_hip.png"));
            weapons.PullTrigger();
            weapons.ReleaseTrigger();
            yield return null;
            Save(rt, Path.Combine(dir, "comp_shot.png"));

            // Opposing force: a shot from the yard's edge brings the squad round to fight.
            Teleport(rig, cc, new Vector3(-12f, 0f, 12f));
            foreach (SquadCoordinator squad in squads) squad.gameObject.SetActive(true);
            yield return new WaitForSeconds(0.5f);
            int roundsBefore = weapons.ActiveState.LoadedRounds;
            for (int i = 0; i < 3; i++)
            {
                weapons.PullTrigger();
                weapons.ReleaseTrigger();
                yield return new WaitForSeconds(0.15f);
            }

            Debug.Log($"[Capture] opfor alert: rounds {roundsBefore} -> {weapons.ActiveState.LoadedRounds}, action={weapons.CurrentAction}");
            foreach (SquadCoordinator squad in squads)
            {
                foreach (TacticalAIBrain brain in squad.Members)
                    Debug.Log($"[Capture] {brain.name}: aware={brain.Perception.Awareness:F2} state={brain.State} agent={brain.Body.Agent.isOnNavMesh}");
            }

            yield return new WaitForSeconds(2.5f);
            Save(rt, Path.Combine(dir, "ai_contact.png"));
            yield return new WaitForSeconds(4f);
            Save(rt, Path.Combine(dir, "ai_cover.png"));
            foreach (SquadCoordinator squad in squads)
            {
                foreach (TacticalAIBrain brain in squad.Members)
                    Debug.Log($"[Capture] {brain.name}: {brain.State} aware={brain.Perception.State} cover={brain.HasCover}/{brain.AtCover} peek={brain.IsPeeking} shots={brain.WeaponHandler.ShotsFired}");
            }

            Debug.Log($"[Capture] player health {playerHealth.Health}/{playerHealth.MaxHealth}");
            rig.PlayerCamera.targetTexture = null;

            // Close look at both operator variants, staged on open ground with their brains off.
            var staged = new List<GameObject>();
            foreach (string variant in new[] { "AI_Soldier_Friendly", "AI_Soldier" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/AI/{variant}.prefab");
                GameObject soldier = Object.Instantiate(prefab, new Vector3(staged.Count == 0 ? -4.6f : -3.4f, 0f, -12f), Quaternion.Euler(0f, 160f, 0f));
                soldier.GetComponent<TacticalAIBrain>().enabled = false;
                soldier.GetComponent<AIPerceptionSensor>().enabled = false;
                staged.Add(soldier);
            }

            var closeup = new GameObject("Operator Closeup").AddComponent<Camera>();
            closeup.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            Vector3 chest = new Vector3(-4f, 1.25f, -12f);
            closeup.transform.position = chest + new Vector3(0.6f, 0.2f, -3.2f);
            closeup.transform.LookAt(chest);
            closeup.fieldOfView = 45f;
            closeup.targetTexture = rt;
            yield return new WaitForSeconds(0.3f);
            Save(rt, Path.Combine(dir, "operator_closeup.png"));
            Object.Destroy(closeup.gameObject);
            foreach (GameObject soldier in staged) Object.Destroy(soldier);
            FPS.Gameplay.SaveProfileManager.DirectoryOverride = null;
        }

        [UnityTest]
        public IEnumerator CaptureGameModes()
        {
            string dir = Environment.GetEnvironmentVariable("FPS_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("FPS_CAPTURE_DIR not set");
            TestInput.IgnoreFocus();
            FPS.Gameplay.SaveProfileManager.DirectoryOverride = FreshProfileFolder(dir, "modes");
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();

            foreach (string mode in new[] { "Hardpoint", "SearchAndDestroy", "Raid" })
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode($"Assets/_Project/Scenes/Mode_{mode}.unity", new LoadSceneParameters(LoadSceneMode.Single));
                yield return null;
                var rig = Object.FindAnyObjectByType<PlayerRig>();
                var health = rig.GetComponent<TacticalHealthAndArmorSystem>();
                TestWeapons.SetField(health, "maxHealth", 100000f);
                health.ResetState();
                var director = Object.FindAnyObjectByType<FPS.Gameplay.MatchDirector>();
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rig.PlayerCamera.targetTexture = rt;

                float deadline = Time.time + 6f;
                while (director.Phase != FPS.Gameplay.MatchPhase.Live && Time.time < deadline) yield return null;
                yield return new WaitForSeconds(mode == "Hardpoint" ? 7f : 4f);
                Save(rt, Path.Combine(dir, $"mode_{mode}.png"));
                Debug.Log($"[Capture] {mode}: phase={director.Phase} round={director.Round} objective='{director.Mode.Objective}' " +
                          $"score={director.Score(0)}-{director.Score(1)} wins={director.RoundWins(0)}-{director.RoundWins(1)} alive={director.AliveCount(0)}v{director.AliveCount(1)}");

                if (mode == "Hardpoint")
                {
                    // A smoke screen thrown along the line of sight.
                    var equipment = rig.GetComponent<TacticalEquipmentSystem>();
                    equipment.SetLoadout(EquipmentSlot.Tactical,
                        AssetDatabase.LoadAssetAtPath<GrenadeDataSO>("Assets/_Project/Data/Equipment/Grenade_M18.asset"), 1);
                    equipment.BeginThrow(EquipmentSlot.Tactical);
                    yield return new WaitForSeconds(0.4f);
                    equipment.ReleaseThrow(EquipmentSlot.Tactical);
                    yield return new WaitForSeconds(5.5f);
                    Save(rt, Path.Combine(dir, "mode_Hardpoint_smoke.png"));
                    foreach (FPS.Gameplay.Combatant combatant in director.Combatants)
                    {
                        if (combatant.Brain != null)
                            Debug.Log($"[Capture] team {combatant.Team}: {combatant.Brain.State} alive={combatant.Alive} at {combatant.Position:F1}");
                    }
                }

                rig.PlayerCamera.targetTexture = null;
                rt.Release();
            }

            FPS.Gameplay.SaveProfileManager.DirectoryOverride = null;
        }

        [UnityTest]
        public IEnumerator CaptureArmory()
        {
            string dir = Environment.GetEnvironmentVariable("FPS_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("FPS_CAPTURE_DIR not set");
            TestInput.IgnoreFocus();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            FPS.Gameplay.SaveProfileManager.DirectoryOverride = FreshProfileFolder(dir, "armory");

            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Armory.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var rig = Object.FindAnyObjectByType<PlayerRig>();
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rig.PlayerCamera.targetTexture = rt; // retargeting restarts eye adaptation: give it time to settle
            yield return new WaitForSeconds(2.5f);
            Save(rt, Path.Combine(dir, "armory_workshop.png"));

            var bench = Object.FindAnyObjectByType<FPS.Gameplay.GunsmithWorkbenchController>();
            bench.BenchCamera.targetTexture = rt;
            bench.Interact(rig.gameObject);
            yield return new WaitForSeconds(0.5f);
            Save(rt, Path.Combine(dir, "armory_bench.png"));
            bench.SelectSlot(AttachmentSlot.Optic);
            foreach (AttachmentDataSO option in bench.Options)
            {
                if (option.name == "Optic_RedDot") bench.Fit(option);
            }

            bench.SelectSlot(AttachmentSlot.Muzzle);
            foreach (AttachmentDataSO option in bench.Options)
            {
                if (option.name == "Muzzle_Suppressor") bench.Fit(option);
            }

            bench.Rotate(new Vector2(-120f, 20f));
            yield return new WaitForSeconds(0.3f);
            Save(rt, Path.Combine(dir, "armory_bench_build.png"));
            Debug.Log($"[Capture] bench build: {bench.PreviewData.MassKg:F2} kg, flash {bench.PreviewData.MuzzleFlashMultiplier:F2}, nodes {bench.Nodes.Count}");
            bench.Apply();
            bench.Leave();
            bench.BenchCamera.targetTexture = null;

            var weapons = rig.GetComponent<RealisticWeaponController>();
            var cc = rig.GetComponent<CharacterController>();
            Teleport(rig, cc, new Vector3(7.2f, 0f, 0f));
            rig.GetComponent<RealisticLocomotionController>().SetLook(90f, 0f);
            yield return new WaitForSeconds(1.2f);
            for (int i = 0; i < 3; i++)
            {
                weapons.PullTrigger();
                weapons.ReleaseTrigger();
                yield return new WaitForSeconds(0.2f);
            }

            Save(rt, Path.Combine(dir, "armory_range.png"));
            Debug.Log($"[Capture] range: fitted {weapons.GetAttachments(weapons.ActiveSlot).Count} parts, rounds {weapons.ActiveState.LoadedRounds}");
            rig.PlayerCamera.targetTexture = null;
            FPS.Gameplay.SaveProfileManager.DirectoryOverride = null;
        }

        private static AttachmentDataSO Part(string partName) =>
            AssetDatabase.LoadAssetAtPath<AttachmentDataSO>($"Assets/_Project/Data/Weapons/Attachments/{partName}.asset");

        private static void LogBuild(string label, RealisticWeaponController weapons)
        {
            WeaponDataSO d = weapons.ActiveState.Data;
            Debug.Log($"[Capture] {label}: mass={d.MassKg:F2} ergo={d.Ergonomics:F0} barrel={d.BarrelLengthInches} inertia={d.MomentOfInertia:F3} " +
                      $"com={d.CenterOfMass:F3} ads={d.AdsTime:F3} v0={d.Caliber.MuzzleVelocity(d.BarrelLengthInches) * d.MuzzleVelocityMultiplier:F0} " +
                      $"flash={d.MuzzleFlashMultiplier:F2} recoil=({d.RecoilVerticalMultiplier:F2},{d.RecoilHorizontalMultiplier:F2},{d.RecoilBackMultiplier:F2})");
        }

        private static void Teleport(PlayerRig rig, CharacterController cc, Vector3 position)
        {
            cc.enabled = false;
            rig.transform.SetPositionAndRotation(position, Quaternion.identity);
            cc.enabled = true;
        }

        private static void Save(RenderTexture rt, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
        }

        /// <summary>An empty profile folder under the capture directory, so no saved loadout leaks between captures.</summary>
        private static string FreshProfileFolder(string dir, string name)
        {
            string folder = Path.Combine(dir, "profiles", name);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void SaveHdr(RenderTexture rt, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false, true);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            Color[] pixels = texture.GetPixels();
            float max = 0f;
            for (int i = 0; i < pixels.Length; i++) max = Mathf.Max(max, pixels[i].maxColorComponent);
            Debug.Log($"[Capture] scope max={max}");
            var output = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                c = new Color(c.r / (1f + c.r), c.g / (1f + c.g), c.b / (1f + c.b), 1f).gamma;
                pixels[i] = c;
            }
            output.SetPixels(pixels);
            output.Apply();
            File.WriteAllBytes(path, output.EncodeToPNG());
        }
    }
}
#endif
