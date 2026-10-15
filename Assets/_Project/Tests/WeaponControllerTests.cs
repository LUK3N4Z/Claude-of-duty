using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    internal static class TestWeapons
    {
        public const float EyeRelief = 0.07f;

        /// <summary>Socketed weapon template: 1 m from stock to muzzle, primary sight and 45° canted sight.</summary>
        public static WeaponModelRig CreateTemplate(List<Object> created)
        {
            var root = new GameObject("TestWeapon");
            created.Add(root);
            Socket(root.transform, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.6f), Quaternion.identity);
            Socket(root.transform, WeaponModelRig.StockPivotSocketName, new Vector3(0f, -0.02f, -0.4f), Quaternion.identity);
            Socket(root.transform, WeaponModelRig.PrimarySightSocketName, new Vector3(0f, 0.075f, -0.06f), Quaternion.identity)
                .gameObject.AddComponent<WeaponSight>().Configure(1f, EyeRelief, 1.25f, null);
            Socket(root.transform, WeaponModelRig.CantedSightSocketName, new Vector3(0.042f, 0.042f, -0.09f), Quaternion.Euler(0f, 0f, -45f))
                .gameObject.AddComponent<WeaponSight>().Configure(1f, 0.09f, 1.1f, null);
            var rig = root.AddComponent<WeaponModelRig>();
            rig.EnsureSockets();
            return rig;
        }

        public static void SetField(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{field} does not exist");
            info.SetValue(target, value);
        }

        private static Transform Socket(Transform parent, string socketName, Vector3 position, Quaternion rotation)
        {
            var socket = new GameObject(socketName).transform;
            socket.SetParent(parent, false);
            socket.SetLocalPositionAndRotation(position, rotation);
            return socket;
        }
    }

    public class WeaponControllerTests : InputTestFixture
    {
        private readonly List<Object> created = new List<Object>();
        private readonly List<WeaponActionType> weaponActions = new List<WeaponActionType>();
        private Keyboard keyboard;
        private Mouse mouse;
        private InputActionAsset actions;
        private InputReader reader;
        private ViewmodelRig viewmodel;
        private RealisticWeaponController controller;
        private int shots;
        private ShotFiredEvent lastShot;
        private Vector3 muzzleAtShot, muzzleForwardAtShot;

        public override void Setup()
        {
            base.Setup();
            TestInput.IgnoreFocus();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
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
            weaponActions.Clear();
            EventBus<ShotFiredEvent>.Subscribe(OnShot);
            EventBus<WeaponActionEvent>.Subscribe(OnWeaponAction);
        }

        public override void TearDown()
        {
            EventBus<ShotFiredEvent>.Unsubscribe(OnShot);
            EventBus<WeaponActionEvent>.Unsubscribe(OnWeaponAction);
            foreach (ObjectPoolManager pool in Object.FindObjectsByType<ObjectPoolManager>())
                Object.DestroyImmediate(pool.gameObject);
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            Object.DestroyImmediate(reader);
            Object.DestroyImmediate(actions);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator ClosedBolt_FiresMagazinePlusOne_ThenLocksBack()
        {
            yield return Equip(Data(BoltType.ClosedBolt, FireMode.Auto));
            WeaponState state = controller.ActiveState;
            Assert.AreEqual(6, state.LoadedRounds, "5-round magazine + 1 chambered");

            controller.PullTrigger();
            yield return new WaitForSeconds(1f);
            controller.ReleaseTrigger();

            Assert.AreEqual(6, shots);
            Assert.IsTrue(state.BoltLockedBack);
            Assert.IsFalse(state.CanFire);
            Assert.AreEqual(1, Count(WeaponActionType.DryFire));
        }

        [UnityTest]
        public IEnumerator EmptyReload_RunsRemoveInsertAndChargeStages()
        {
            yield return Equip(Data(BoltType.ClosedBolt, FireMode.Auto));
            WeaponState state = controller.ActiveState;
            state.MagazineRounds = 0;
            state.RoundChambered = false;
            state.BoltLockedBack = true;

            controller.RequestReload(false);
            Assert.AreEqual(ReloadStage.MagazineRemoved, controller.ReloadTarget);
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(WeaponAction.None, controller.CurrentAction);
            Assert.IsTrue(state.HasMagazine && state.RoundChambered && !state.BoltLockedBack);
            Assert.AreEqual(4, state.MagazineRounds, "one round went into the chamber");
            Assert.AreEqual(1, state.SpareMagazines.Count, "the empty magazine is not kept");
            Assert.AreEqual(1, Count(WeaponActionType.MagazineRemoved));
            Assert.AreEqual(1, Count(WeaponActionType.MagazineInserted));
            Assert.AreEqual(1, Count(WeaponActionType.BoltCycled));
        }

        [UnityTest]
        public IEnumerator TacticalReload_KeepsChamberedRound_AndRetainsPartialMagazine()
        {
            yield return Equip(Data(BoltType.ClosedBolt, FireMode.Auto));
            WeaponState state = controller.ActiveState;
            state.MagazineRounds = 2;

            controller.RequestReload(false);
            yield return new WaitForSeconds(0.4f);

            Assert.AreEqual(6, state.LoadedRounds);
            CollectionAssert.Contains(state.SpareMagazines, 2);
            Assert.AreEqual(0, Count(WeaponActionType.BoltCycled), "no charging needed with a round chambered");
        }

        [UnityTest]
        public IEnumerator InterruptedReload_ResumesFromLastCompletedStage()
        {
            yield return Equip(Data(BoltType.ClosedBolt, FireMode.Auto));
            controller.SetLoadout(1, TestWeapons.CreateTemplate(created), Data(BoltType.ClosedBolt, FireMode.Semi));
            controller.ActiveState.MagazineRounds = 2; // a full magazine would not be reloaded at all

            controller.RequestReload(false);
            yield return new WaitForSeconds(0.15f); // magazine out, new one not yet seated
            controller.SelectSlot(1);
            yield return new WaitForSeconds(0.25f);

            Assert.AreEqual(1, controller.ActiveSlot);
            WeaponState first = controller.GetState(0);
            Assert.AreEqual(ReloadStage.MagazineRemoved, first.CompletedStage);
            Assert.IsFalse(first.HasMagazine);
            Assert.IsTrue(first.CanFire, "the chambered round is still there");

            controller.SelectSlot(0);
            yield return new WaitForSeconds(0.25f);
            controller.RequestReload(false);
            Assert.AreEqual(ReloadStage.MagazineInserted, controller.ReloadTarget, "removal is not repeated");
            yield return new WaitForSeconds(0.25f);

            Assert.IsTrue(first.HasMagazine);
            Assert.IsFalse(first.ReloadPending);
        }

        [UnityTest]
        public IEnumerator DoubleTapReload_DropsPhysicalMagazine()
        {
            var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "DroppedMagazineTemplate";
            var body = template.AddComponent<Rigidbody>();
            template.SetActive(false);
            created.Add(template);

            WeaponDataSO data = Data(BoltType.ClosedBolt, FireMode.Auto);
            TestWeapons.SetField(data, "droppedMagazinePrefab", body);
            yield return Equip(data);
            WeaponState state = controller.ActiveState;
            state.MagazineRounds = 3;

            controller.RequestReload(false);
            controller.RequestReload(true); // second tap upgrades to a speed reload
            yield return new WaitForSeconds(0.3f);

            CollectionAssert.DoesNotContain(state.SpareMagazines, 3, "a dropped magazine is not retained");
            Assert.AreEqual(1, Count(WeaponActionType.MagazineDropped));
            Assert.AreEqual(0, Count(WeaponActionType.MagazineRemoved));
            bool spawned = false;
            foreach (Rigidbody rigidbody in Object.FindObjectsByType<Rigidbody>())
                spawned |= rigidbody.name == template.name && rigidbody.gameObject.activeInHierarchy;
            Assert.IsTrue(spawned, "no physical magazine on the ground");
        }

        [UnityTest]
        public IEnumerator MagazineCheck_ReportsEstimate_WhileHeld()
        {
            yield return Equip(Data(BoltType.ClosedBolt, FireMode.Auto));
            controller.ActiveState.MagazineRounds = 2;

            controller.SetMagazineCheck(true);
            yield return new WaitForSeconds(0.15f);
            Assert.AreEqual(MagazineEstimate.AboutHalf, controller.LastEstimate);
            Assert.AreEqual(1, Count(WeaponActionType.MagazineChecked));
            Assert.IsTrue(controller.BlocksAiming);

            controller.SetMagazineCheck(false);
            yield return null;
            yield return null;
            Assert.AreEqual(WeaponAction.None, controller.CurrentAction);

            Assert.AreEqual(MagazineEstimate.Empty, WeaponState.Estimate(0, 30));
            Assert.AreEqual(MagazineEstimate.NearlyEmpty, WeaponState.Estimate(3, 30));
            Assert.AreEqual(MagazineEstimate.AboutHalf, WeaponState.Estimate(15, 30));
            Assert.AreEqual(MagazineEstimate.NearlyFull, WeaponState.Estimate(27, 30));
            Assert.AreEqual(MagazineEstimate.Full, WeaponState.Estimate(30, 30));
        }

        [UnityTest]
        public IEnumerator OpenBolt_HasNoPlusOne_AndNeedsChargingAfterRunningDry()
        {
            yield return Equip(Data(BoltType.OpenBolt, FireMode.Auto));
            WeaponState state = controller.ActiveState;
            Assert.AreEqual(5, state.LoadedRounds);

            controller.PullTrigger();
            yield return new WaitForSeconds(1f);
            controller.ReleaseTrigger();
            Assert.AreEqual(5, shots);
            Assert.IsFalse(state.RoundChambered, "bolt rode forward on the empty magazine");

            controller.RequestReload(false);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1, Count(WeaponActionType.BoltCycled));
            Assert.IsTrue(state.CanFire);
            Assert.AreEqual(5, state.LoadedRounds);
        }

        [UnityTest]
        public IEnumerator BoltAction_WorksTheBoltBetweenShots()
        {
            yield return Equip(Data(BoltType.ManualBoltAction, FireMode.Semi));
            WeaponState state = controller.ActiveState;

            controller.PullTrigger();
            controller.ReleaseTrigger();
            yield return null;
            Assert.AreEqual(1, shots);
            Assert.IsFalse(state.RoundChambered, "spent case still in the chamber");

            yield return new WaitForSeconds(0.35f);
            Assert.AreEqual(1, Count(WeaponActionType.BoltCycled));
            Assert.IsTrue(state.RoundChambered);
            Assert.AreEqual(4, state.MagazineRounds);

            controller.PullTrigger();
            controller.ReleaseTrigger();
            yield return null;
            Assert.AreEqual(2, shots);
        }

        [UnityTest]
        public IEnumerator Burst_FiresExactlyBurstCount()
        {
            WeaponDataSO data = Data(BoltType.ClosedBolt, FireMode.Burst);
            TestWeapons.SetField(data, "magazineCapacity", 30);
            yield return Equip(data);

            controller.PullTrigger();
            controller.ReleaseTrigger();
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(3, shots);
        }

        [UnityTest]
        public IEnumerator MouseFire_LeavesTheMuzzleAlongTheBore()
        {
            WeaponDataSO data = Data(BoltType.ClosedBolt, FireMode.Semi);
            yield return Equip(data);

            Press(mouse.leftButton);
            Release(mouse.leftButton);
            yield return null;

            Assert.AreEqual(1, shots);
            Assert.Less(Vector3.Distance(lastShot.Origin, muzzleAtShot), 1e-4f);
            Assert.LessOrEqual(Vector3.Angle(lastShot.Direction, muzzleForwardAtShot), data.MoaAccuracy / 120f + 1e-3f);
            Assert.AreEqual(data.Caliber.MuzzleVelocity(data.BarrelLengthInches), lastShot.MuzzleVelocity, 1e-3f);
            Assert.AreSame(controller.transform, lastShot.Shooter);
        }

        [UnityTest]
        public IEnumerator ReloadKeyTap_StartsAReload()
        {
            yield return Equip(Data(BoltType.ClosedBolt, FireMode.Auto));
            controller.ActiveState.MagazineRounds = 2;

            int presses = 0;
            reader.ReloadPressed += () => presses++;
            double start = currentTime;
            Press(keyboard.rKey);
            currentTime = start + 0.1;
            Release(keyboard.rKey);
            yield return null; // [UnityTest] input is processed with the player loop
            Assert.AreEqual(1, presses, "R tap not seen by the InputReader");
            Assert.AreEqual(WeaponAction.Reload, controller.CurrentAction);
        }

        [Test]
        public void TwoBoneIK_ReachesTargets_AndClampsUnreachableOnes()
        {
            Vector3 joint = TwoBoneIKSolver.SolveJoint(Vector3.zero, 0.3f, 0.3f, new Vector3(0f, 0f, 0.4f), Vector3.down, out Vector3 end);
            Assert.Less(Vector3.Distance(end, new Vector3(0f, 0f, 0.4f)), 1e-3f);
            Assert.AreEqual(0.3f, joint.magnitude, 1e-3f);
            Assert.AreEqual(0.3f, Vector3.Distance(joint, end), 1e-3f);
            Assert.Less(joint.y, 0f, "elbow bends toward the hint");

            TwoBoneIKSolver.SolveJoint(Vector3.zero, 0.3f, 0.3f, new Vector3(0f, 0f, 2f), Vector3.down, out end);
            Assert.AreEqual(0.6f, end.z, 1e-3f);
        }

        [Test]
        public void Caliber_MuzzleVelocityScalesWithBarrelLength()
        {
            var caliber = ScriptableObject.CreateInstance<CaliberDataSO>(); // 5.56 defaults: 940 m/s from 20", 7.5 m/s per inch
            created.Add(caliber);
            Assert.AreEqual(940f, caliber.MuzzleVelocity(20f), 1e-3f);
            Assert.AreEqual(898.75f, caliber.MuzzleVelocity(14.5f), 1e-3f);
            Assert.AreEqual(470f, caliber.MuzzleVelocity(-100f), 1e-3f, "never below half the reference");
        }

        private IEnumerator Equip(WeaponDataSO data)
        {
            controller.SetLoadout(0, TestWeapons.CreateTemplate(created), data);
            yield return new WaitForSeconds(0.12f); // draw
        }

        private WeaponDataSO Data(BoltType bolt, FireMode mode)
        {
            var data = ScriptableObject.CreateInstance<WeaponDataSO>();
            var caliber = ScriptableObject.CreateInstance<CaliberDataSO>();
            created.Add(data);
            created.Add(caliber);
            TestWeapons.SetField(data, "caliber", caliber);
            TestWeapons.SetField(data, "boltType", bolt);
            TestWeapons.SetField(data, "fireModes", new[] { mode });
            TestWeapons.SetField(data, "ergonomics", 50f); // manipulation scale exactly 1
            TestWeapons.SetField(data, "roundsPerMinute", 600f);
            TestWeapons.SetField(data, "magazineCapacity", 5);
            TestWeapons.SetField(data, "spareMagazines", 2);
            TestWeapons.SetField(data, "sprintToFireTime", 0f);
            TestWeapons.SetField(data, "drawTime", 0.05f);
            TestWeapons.SetField(data, "holsterTime", 0.05f);
            TestWeapons.SetField(data, "fireModeSwitchTime", 0.05f);
            TestWeapons.SetField(data, "magazineCheckTime", 0.05f);
            TestWeapons.SetField(data, "manualCycleTime", 0.1f);
            TestWeapons.SetField(data, "magazineRemoveTime", 0.1f);
            TestWeapons.SetField(data, "speedMagazineRemoveTime", 0.05f);
            TestWeapons.SetField(data, "magazineInsertTime", 0.1f);
            TestWeapons.SetField(data, "boltCycleTime", 0.1f);
            return data;
        }

        private int Count(WeaponActionType type)
        {
            int count = 0;
            foreach (WeaponActionType action in weaponActions) count += action == type ? 1 : 0;
            return count;
        }

        private void OnShot(in ShotFiredEvent evt)
        {
            shots++;
            lastShot = evt;
            Transform muzzle = viewmodel.Weapon.MuzzleSocket;
            muzzleAtShot = muzzle.position;
            muzzleForwardAtShot = muzzle.forward;
        }

        private void OnWeaponAction(in WeaponActionEvent evt) => weaponActions.Add(evt.Type);
    }
}
