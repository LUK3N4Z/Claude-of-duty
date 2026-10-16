using System.Collections;
using System.Collections.Generic;
using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using FPS.UI;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class EffectsTests
    {
        private readonly List<Object> created = new List<Object>();
        private GameObject player;
        private PlayerRig rig;

        [SetUp]
        public void SetUp()
        {
            InputReader reader = TestInput.CreateReader(out InputActionAsset actions);
            created.Add(actions);
            created.Add(reader);
            player = new GameObject("Player");
            created.Add(player);
            rig = PlayerRig.Build(player, reader);
            ViewmodelRig.Build(rig);
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
        }

        [Test]
        public void Environment_ClassifiesRoomsCanyonsAndOpenGround()
        {
            Vector3 room = AcousticPropagationSystem.ClassifyEnvironment(true, 1f, 3f);
            Vector3 canyon = AcousticPropagationSystem.ClassifyEnvironment(false, 0.75f, 12f);
            Vector3 field = AcousticPropagationSystem.ClassifyEnvironment(false, 0f, 60f);
            Assert.AreEqual(1f, room.x, 1e-3f);
            Assert.Greater(canyon.y, 0.6f);
            Assert.AreEqual(1f, field.z, 1e-3f);
            Assert.AreEqual(1f, canyon.x + canyon.y + canyon.z, 1e-4f);
            Assert.AreEqual(1f, AcousticPropagationSystem.PropagationDelay(343f), 1e-4f);
        }

        [UnityTest]
        public IEnumerator DistantGunshot_ArrivesAtTheSpeedOfSound_AsTheDistantReport()
        {
            WeaponAudioProfileSO profile = Profile(out AudioClip close, out AudioClip distant);
            Vector3 ear = rig.PlayerCamera.transform.position;
            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = ear + Vector3.forward * 343f, Profile = profile, Shooter = null });
            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = ear + Vector3.right * 30f, Profile = profile, Shooter = null });
            yield return null;

            AudioEmitter far = null, near = null;
            foreach (AudioEmitter emitter in Object.FindObjectsByType<AudioEmitter>())
            {
                if (!emitter.gameObject.activeInHierarchy) continue;
                if (emitter.Clip == distant) far = emitter;
                if (emitter.Clip == close) near = emitter;
            }

            Assert.IsNotNull(far, "343 m away is heard as the distant report");
            Assert.AreEqual(1f, far.Delay, 0.01f);
            Assert.IsNotNull(near, "30 m away is heard as the close report");
            Assert.AreEqual(30f / 343f, near.Delay, 0.01f);
        }

        [UnityTest]
        public IEnumerator NearMissCrack_PlaysAndSuppressionMufflesThenClears()
        {
            var acoustics = player.GetComponent<AcousticPropagationSystem>();
            AudioClip crack = AudioClip.Create("Crack", 441, 1, 44100, false);
            created.Add(crack);
            TestWeapons.SetField(player.GetComponent<SupersonicCrackEmitter>(), "crackClips", new[] { crack }); // cracks moved there in M13
            Transform ear = rig.PlayerCamera.transform;

            EventBus<SuppressionEvent>.Raise(new SuppressionEvent
            {
                Listener = ear, Intensity = 1f, Point = ear.position + Vector3.right, Direction = Vector3.forward, Speed = 860f,
            });
            yield return null;
            yield return null;

            bool cracked = false;
            foreach (AudioEmitter emitter in Object.FindObjectsByType<AudioEmitter>()) cracked |= emitter.gameObject.activeInHierarchy && emitter.Clip == crack;
            Assert.IsTrue(cracked);
            Assert.Less(acoustics.CutoffFrequency, 5000f);

            yield return new WaitForSeconds(2f);
            Assert.Greater(acoustics.CutoffFrequency, 15000f);
        }

        [UnityTest]
        public IEnumerator Impact_LeavesADecalAndADelayedImpactSound()
        {
            var surface = ScriptableObject.CreateInstance<SurfaceMaterialSO>();
            var decal = new Material(Shader.Find("HDRP/Decal"));
            AudioClip thud = AudioClip.Create("Thud", 441, 1, 44100, false);
            created.Add(surface);
            created.Add(decal);
            created.Add(thud);
            TestWeapons.SetField(surface, "impactDecal", decal);
            TestWeapons.SetField(surface, "impactClips", new[] { thud });

            Vector3 point = rig.PlayerCamera.transform.position + Vector3.forward * 34.3f;
            EventBus<ProjectileImpactEvent>.Raise(new ProjectileImpactEvent { Point = point, Normal = Vector3.back, Surface = surface, Outcome = ImpactOutcome.Stopped });
            yield return null;

            bool marked = false;
            foreach (DecalProjector projector in Object.FindObjectsByType<DecalProjector>())
                marked |= projector.gameObject.activeInHierarchy && projector.material == decal && Vector3.Distance(projector.transform.position, point) < 0.1f;
            Assert.IsTrue(marked, "no bullet mark");

            AudioEmitter sound = null;
            foreach (AudioEmitter emitter in Object.FindObjectsByType<AudioEmitter>())
                if (emitter.gameObject.activeInHierarchy && emitter.Clip == thud) sound = emitter;
            Assert.IsNotNull(sound);
            Assert.AreEqual(0.1f, sound.Delay, 0.01f);
        }

        [UnityTest]
        public IEnumerator MuzzleFlash_LastsOneFrame_HeatBuildsBreechSmoke()
        {
            var vfx = player.GetComponent<MuzzleAndSmokeVFXController>();
            var data = ScriptableObject.CreateInstance<WeaponDataSO>();
            created.Add(data);
            player.GetComponent<ViewmodelRig>().Equip(TestWeapons.CreateTemplate(created), data);

            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = Vector3.zero, Shooter = player.transform });
            Assert.IsTrue(vfx.FlashVisible);
            yield return null;
            yield return null;
            Assert.IsFalse(vfx.FlashVisible, "flash must not outlive its frame");

            for (int i = 0; i < 12; i++) EventBus<GunshotEvent>.Raise(new GunshotEvent { Shooter = player.transform });
            Assert.Greater(vfx.Heat, 0.4f);
            yield return new WaitForSeconds(0.9f);
            Assert.Greater(vfx.SmokeParticles, 0, "hot barrel should smoke once firing stops");
        }

        [UnityTest]
        public IEnumerator Hud_ShowsMagazineCheckBriefly_AndCompassBearing()
        {
            var hud = player.AddComponent<DiegeticTacticalHUD>();
            yield return null;
            Assert.AreEqual("000", hud.BearingText);
            Assert.AreEqual(0f, hud.StatusAlpha);

            EventBus<WeaponActionEvent>.Raise(new WeaponActionEvent
            {
                Type = WeaponActionType.MagazineChecked, Owner = player.transform, Value = (int)MagazineEstimate.AboutHalf,
            });
            yield return null;
            Assert.AreEqual("MAG ABOUT HALF", hud.StatusText);
            Assert.AreEqual(1f, hud.StatusAlpha);

            yield return new WaitForSeconds(2.6f);
            Assert.AreEqual(0f, hud.StatusAlpha);
        }

        [UnityTest]
        public IEnumerator LookingAtAmmoCrate_PromptsAndResupplies()
        {
            var hud = player.AddComponent<DiegeticTacticalHUD>();
            var weapons = player.GetComponent<RealisticWeaponController>();
            WeaponDataSO data = ScriptableObject.CreateInstance<WeaponDataSO>();
            created.Add(data);
            weapons.SetLoadout(0, TestWeapons.CreateTemplate(created), data);
            weapons.ActiveState.SpareMagazines.Clear();

            Transform eye = rig.PlayerCamera.transform;
            var crate = new GameObject("Crate") { layer = GameLayers.Interactable };
            created.Add(crate);
            crate.transform.position = eye.position + eye.forward * 1.2f;
            crate.AddComponent<BoxCollider>();
            crate.AddComponent<AmmoCrate>();
            Physics.SyncTransforms();
            yield return null;
            yield return null;

            var interactor = player.GetComponent<Interactor>();
            Assert.IsNotNull(interactor.Focused);
            Assert.AreEqual("[F]  Resupply ammunition", hud.PromptText);

            interactor.Interact();
            Assert.AreEqual(data.SpareMagazines, weapons.ActiveState.SpareMagazines.Count);
        }

        private WeaponAudioProfileSO Profile(out AudioClip close, out AudioClip distant)
        {
            var profile = ScriptableObject.CreateInstance<WeaponAudioProfileSO>();
            close = AudioClip.Create("Close", 441, 1, 44100, false);
            distant = AudioClip.Create("Distant", 441, 1, 44100, false);
            created.Add(profile);
            created.Add(close);
            created.Add(distant);
            TestWeapons.SetField(profile, "pressureTransients", new[] { close });
            TestWeapons.SetField(profile, "distantReports", new[] { distant });
            return profile;
        }
    }
}
