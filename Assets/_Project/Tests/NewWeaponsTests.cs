#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using FPS.Ballistics;
using FPS.Core;
using FPS.Gameplay;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    /// <summary>M11 weapons: the 7.62 battle rifle, the 9 mm sidearm's reciprocating slide, and the armory weapon rack.</summary>
    public class NewWeaponsTests : InputTestFixture
    {
        private readonly List<Object> created = new List<Object>();
        private InputActionAsset actions;
        private InputReader reader;
        private ViewmodelRig viewmodel;
        private RealisticWeaponController controller;
        private int shots;
        private ShotFiredEvent lastShot;

        private static WeaponModelRig Model(string weapon) => AssetDatabase.LoadAssetAtPath<WeaponModelRig>($"Assets/_Project/Prefabs/Weapons/{weapon}.prefab");
        private static WeaponDataSO Data(string weapon) => AssetDatabase.LoadAssetAtPath<WeaponDataSO>($"Assets/_Project/Data/Weapons/{weapon}_Data.asset");

        public override void Setup()
        {
            base.Setup();
            TestInput.IgnoreFocus();
            reader = TestInput.CreateReader(out actions);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.layer = GameLayers.WorldGeometry;
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            created.Add(floor);

            var player = new GameObject("Player");
            created.Add(player);
            viewmodel = ViewmodelRig.Build(PlayerRig.Build(player, reader));
            controller = player.GetComponent<RealisticWeaponController>();
            Physics.SyncTransforms();
            shots = 0;
            EventBus<ShotFiredEvent>.Subscribe(OnShot);
        }

        public override void TearDown()
        {
            EventBus<ShotFiredEvent>.Unsubscribe(OnShot);
            foreach (ObjectPoolManager pool in Object.FindObjectsByType<ObjectPoolManager>()) Object.DestroyImmediate(pool.gameObject);
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            foreach (BallisticsSimulationManager simulation in Object.FindObjectsByType<BallisticsSimulationManager>()) simulation.Clear(); // no stray rounds into the next test
            Object.DestroyImmediate(reader);
            Object.DestroyImmediate(actions);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator BattleRifle_Fires762_AtItsBarrelVelocity_FromTwentyPlusOne()
        {
            WeaponDataSO data = Data("BattleRifle");
            controller.SetLoadout(0, Model("BattleRifle"), data);
            yield return new WaitForSeconds(data.DrawTime + 0.1f);

            WeaponState state = controller.ActiveState;
            Assert.AreEqual(21, state.LoadedRounds, "20-round magazine + 1");
            Assert.AreEqual("Caliber_762x51", state.Data.Caliber.name);
            Assert.IsNotNull(viewmodel.Weapon.PrimarySightSocket.GetComponentInChildren<WeaponSight>(), "default build mounts the red dot");

            controller.PullTrigger();
            yield return null;
            yield return null;
            controller.ReleaseTrigger();
            Assert.GreaterOrEqual(shots, 1);
            Assert.AreEqual(state.Data.Caliber.MuzzleVelocity(16f), lastShot.MuzzleVelocity, 0.5f, "16-inch barrel velocity");
            Assert.AreEqual(840f - 6f * 5.5f, lastShot.MuzzleVelocity, 1f);
        }

        [UnityTest]
        public IEnumerator BattleRifle_FoldsItsIronsUnderTheRedDot_SoNothingBlocksTheSightPicture()
        {
            WeaponDataSO data = Data("BattleRifle");
            controller.SetLoadout(0, Model("BattleRifle"), data);
            yield return new WaitForSeconds(data.DrawTime + 0.1f);

            WeaponModelRig weapon = viewmodel.Weapon;
            Assert.AreEqual(2, weapon.FoldingSights.Count, "rear ghost ring and front post");
            Transform aligner = weapon.GetSightAligner(0, out WeaponSight sight);
            Assert.IsNotNull(sight, "the red dot is the sight");
            foreach (Transform iron in weapon.FoldingSights)
                Assert.AreEqual(90f, Quaternion.Angle(Quaternion.identity, iron.localRotation), 0.5f, $"{iron.name} folded flat under the optic");

            // From the eye (eye relief behind the aligner) to the dot, no part of the rifle outside the optic may sit on the line.
            Vector3 eye = aligner.position - aligner.forward * sight.EyeRelief;
            foreach (MeshFilter part in weapon.GetComponentsInChildren<MeshFilter>())
            {
                if (part.transform.IsChildOf(sight.transform) || part.sharedMesh == null) continue;
                var local = new Ray(part.transform.InverseTransformPoint(eye), part.transform.InverseTransformDirection(aligner.forward));
                if (part.sharedMesh.bounds.IntersectRay(local, out float distance))
                    Assert.Greater(distance, sight.EyeRelief, $"{part.name} blocks the sight picture");
            }

            controller.SetAttachment(0, AttachmentSlot.Optic, null);
            yield return null;
            foreach (Transform iron in viewmodel.Weapon.FoldingSights)
                Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, iron.localRotation), 0.5f, $"{iron.name} stands up again without an optic");
        }

        [UnityTest]
        public IEnumerator Sidearm_SlideCyclesOnEachShot_AndLocksBackWhenEmpty()
        {
            WeaponDataSO data = Data("Sidearm");
            controller.SetLoadout(0, Model("Sidearm"), data);
            yield return new WaitForSeconds(data.DrawTime + 0.1f);

            var mechanism = viewmodel.Weapon.GetComponent<WeaponMechanism>();
            Transform slide = mechanism.Reciprocating;
            float rest = slide.localPosition.z;
            Assert.AreEqual(16, controller.ActiveState.LoadedRounds, "15 + 1");

            controller.PullTrigger();
            // The shot fires in Update and the slide moves in LateUpdate: watch the frames right after it.
            float maxTravel = 0f, minZ = rest;
            for (int frame = 0; frame < 30 && (shots == 0 || maxTravel <= 0f); frame++)
            {
                yield return null;
                maxTravel = Mathf.Max(maxTravel, mechanism.CarrierTravel);
                minZ = Mathf.Min(minZ, slide.localPosition.z);
            }

            controller.ReleaseTrigger();
            Assert.AreEqual(1, shots);
            Assert.Greater(maxTravel, 0f, "slide moving after the shot");
            Assert.Less(minZ, rest - 0.001f, "slide travelled rearward");
            Assert.AreEqual(Data("Sidearm").Caliber.MuzzleVelocity(4.02f), lastShot.MuzzleVelocity, 0.5f);

            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0f, mechanism.CarrierTravel, "slide back in battery");
            Assert.AreEqual(rest, slide.localPosition.z, 1e-4f);

            WeaponState state = controller.ActiveState;
            state.MagazineRounds = 0; // only the chambered round left
            controller.PullTrigger();
            yield return null;
            yield return null;
            controller.ReleaseTrigger();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(state.BoltLockedBack, "slide stop engaged on the empty magazine");
            Assert.AreEqual(1f, mechanism.CarrierTravel, "slide held open");
        }

        [UnityTest]
        public IEnumerator WeaponRack_SwapsTheSlotWeapon_AndNamesTheNextOne()
        {
            controller.SetLoadout(0, Model("TestRifle"), Data("TestRifle"));
            yield return new WaitForSeconds(0.6f);

            var rackObject = new GameObject("Rack");
            created.Add(rackObject);
            var rack = rackObject.AddComponent<WeaponRack>();
            rack.Configure(0, new[]
            {
                new WeaponRack.Option { model = Model("TestRifle"), data = Data("TestRifle") },
                new WeaponRack.Option { model = Model("BattleRifle"), data = Data("BattleRifle") },
            });

            GameObject player = controller.gameObject;
            Assert.IsTrue(rack.CanInteract(player));
            Assert.AreEqual("Take Battle Rifle 7.62", rack.Prompt);
            rack.Interact(player);
            yield return new WaitForSeconds(0.6f);

            Assert.IsTrue(controller.GetLoadoutWeapon(0, out _, out WeaponDataSO carried));
            Assert.AreEqual(Data("BattleRifle"), carried);
            Assert.AreEqual(21, controller.ActiveState.LoadedRounds, "fresh and loaded");
            Assert.AreEqual("Take Test Carbine 5.56", rack.Prompt);
            rack.Interact(player);
            controller.GetLoadoutWeapon(0, out _, out carried);
            Assert.AreEqual(Data("TestRifle"), carried, "the rack cycles");
        }

        private void OnShot(in ShotFiredEvent e)
        {
            shots++;
            lastShot = e;
        }
    }
}
#endif
