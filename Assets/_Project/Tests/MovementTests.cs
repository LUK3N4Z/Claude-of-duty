#if UNITY_EDITOR
using System.Collections;
using FPS.AI;
using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using FPS.EditorTools;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    /// <summary>
    /// M14 movement and handling: vault, mantle and jump on Jump/Climb, the slide cancel's quick ready, compressed ready
    /// near walls, weapon inspection, the shot's camera kick, hand tremor, and physical doors (tap, crack-peek, bash).
    /// </summary>
    public class MovementTests : AITestWorld
    {
        private Mouse mouse;
        private MantleAndVaultController traversal;
        private RealisticLocomotionController locomotion;
        private RealisticWeaponController weapons;
        private int shots;
        private float measuredKick;

        public override void Setup()
        {
            base.Setup();
            mouse = InputSystem.AddDevice<Mouse>();
            traversal = player.GetComponent<MantleAndVaultController>();
            locomotion = player.GetComponent<RealisticLocomotionController>();
            weapons = player.GetComponent<RealisticWeaponController>();
            shots = 0;
            EventBus<ShotFiredEvent>.Subscribe(OnShot);
        }

        public override void TearDown()
        {
            EventBus<ShotFiredEvent>.Unsubscribe(OnShot);
            base.TearDown();
        }

        // ---- Traversal ----

        [Test]
        public void Obstacles_ClassifyAsVaultMantleOrNothing()
        {
            Assert.AreEqual(TraversalKind.None, MantleAndVaultController.Classify(0.3f, 0.3f, true, 0.5f, 1.1f, 2.1f, 1f), "a step");
            Assert.AreEqual(TraversalKind.Vault, MantleAndVaultController.Classify(0.9f, 0.35f, true, 0.5f, 1.1f, 2.1f, 1f));
            Assert.AreEqual(TraversalKind.Mantle, MantleAndVaultController.Classify(0.9f, 2.4f, false, 0.5f, 1.1f, 2.1f, 1f), "too deep: climb onto it");
            Assert.AreEqual(TraversalKind.Mantle, MantleAndVaultController.Classify(1.6f, 3f, false, 0.5f, 1.1f, 2.1f, 1f));
            Assert.AreEqual(TraversalKind.None, MantleAndVaultController.Classify(2.6f, 3f, false, 0.5f, 1.1f, 2.1f, 1f), "out of reach");
        }

        [UnityTest]
        public IEnumerator Space_VaultsTheLowWall_WithAOneHandedCarryAndAPlantedHand()
        {
            Wall(new Vector3(0f, 0.45f, 1f), new Vector3(3f, 0.9f, 0.35f));
            Physics.SyncTransforms();
            Press(keyboard.wKey);
            yield return null;
            PressAndRelease(keyboard.spaceKey);
            yield return null;
            Assert.AreEqual(TraversalKind.Vault, traversal.Kind);

            float carry = 0f, hand = 0f, roll = 0f;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                carry = Mathf.Max(carry, traversal.CarryBlend);
                hand = Mathf.Max(hand, traversal.HandBlend);
                roll = Mathf.Max(roll, traversal.CameraRoll);
                yield return null;
            }

            Release(keyboard.wKey);
            Assert.IsFalse(traversal.IsActive);
            Assert.Greater(player.transform.position.z, 1.5f, "over the wall");
            Assert.Less(player.transform.position.y, 0.1f, "and down on the far side");
            Assert.Greater(carry, 0.8f, "rifle in a one-handed side carry");
            Assert.Greater(hand, 0.5f, "support hand planted on the top");
            Assert.Greater(roll, 3f, "the view leans through the vault");
        }

        [UnityTest]
        public IEnumerator Space_MantlesOntoAPlatform_AndJumpsWhenNothingIsInReach()
        {
            Wall(new Vector3(0f, 0.8f, 2.3f), new Vector3(3f, 1.6f, 3f)); // face at z = 0.8
            Physics.SyncTransforms();
            Press(keyboard.wKey);
            yield return null;
            PressAndRelease(keyboard.spaceKey);
            yield return null;
            Assert.AreEqual(TraversalKind.Mantle, traversal.Kind);
            float dip = 0f;
            while (traversal.IsActive)
            {
                dip = Mathf.Max(dip, traversal.CameraPitch);
                yield return null;
            }

            Release(keyboard.wKey);
            Assert.AreEqual(1.6f, player.transform.position.y, 0.1f, "standing on the platform");
            Assert.Greater(player.transform.position.z, 0.8f);
            Assert.Greater(dip, 5f, "the view dips with the pull-up");

            MovePlayer(new Vector3(10f, 0f, 0f));
            Wall(new Vector3(10f, 1.3f, 1f), new Vector3(3f, 2.6f, 0.3f)); // too tall to climb
            Physics.SyncTransforms();
            yield return null;
            Press(keyboard.wKey);
            yield return null;
            PressAndRelease(keyboard.spaceKey);
            float peak = 0f;
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                peak = Mathf.Max(peak, player.transform.position.y);
                Assert.IsFalse(traversal.IsActive, "out of reach: no mantle");
                yield return null;
            }

            Release(keyboard.wKey);
            Assert.Greater(peak, 0.25f, "jumped instead");
            Assert.IsTrue(locomotion.IsGrounded, "and landed");
        }

        // ---- Slides ----

        [UnityTest]
        public IEnumerator SlideCancel_ByJump_ComesUpIntoACrouchedReadyIn015Seconds()
        {
            WeaponDataSO data = Data(0.5f);
            weapons.SetLoadout(0, TestWeapons.CreateTemplate(created), data);
            yield return new WaitForSeconds(0.15f);

            Press(keyboard.wKey);
            yield return null;
            Press(keyboard.leftShiftKey);
            yield return new WaitForSeconds(0.1f);
            Release(keyboard.leftShiftKey);
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(locomotion.IsSprinting, $"pace {locomotion.Pace}, {locomotion.PlanarSpeed:F1} m/s, stance {locomotion.StanceController.Current}");
            PressAndRelease(keyboard.cKey);
            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(locomotion.IsSliding);

            PressAndRelease(keyboard.spaceKey);
            yield return null;
            Release(keyboard.wKey);
            Assert.IsFalse(locomotion.IsSliding, "jump cancels the slide");
            Assert.AreEqual(Stance.Crouch, locomotion.StanceController.Current, "into a crouch");
            Assert.AreEqual(Time.time, locomotion.LastSlideCancelTime, 0.05f);

            weapons.PullTrigger();
            weapons.ReleaseTrigger();
            yield return new WaitForSeconds(0.06f);
            Assert.AreEqual(0, shots, "the weapon is still coming up");
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1, shots, "ready after 0.15 s, not the 0.5 s sprint-to-fire");
        }

        // ---- Compressed ready ----

        [UnityTest]
        public IEnumerator CompressedReady_FiresButKeepsTheSightsOff_ATuckBlocksTheShot()
        {
            weapons.SetLoadout(0, TestWeapons.CreateTemplate(created), Data(0f));
            yield return new WaitForSeconds(0.4f);
            var inertia = player.GetComponent<RealisticWeaponInertia>();
            var ads = player.GetComponent<ProceduralADSAndOpticsController>();
            var viewmodel = player.GetComponent<ViewmodelRig>();
            float muzzle = viewmodel.Weapon.MuzzleSocket.position.z;

            Transform wall = Wall(new Vector3(0f, 1.5f, muzzle - 0.1f + 0.05f), new Vector3(3f, 3f, 0.1f)); // 10 cm into the barrel
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(inertia.IsCompressed, "pulled back into a compressed ready");
            Assert.IsFalse(inertia.IsTucked);

            Press(mouse.rightButton);
            yield return new WaitForSeconds(0.5f);
            Assert.Less(ads.AdsBlend, 0.05f, "stock off the shoulder: no sights");
            weapons.PullTrigger();
            weapons.ReleaseTrigger();
            yield return new WaitForSeconds(0.1f);
            Release(mouse.rightButton);
            Assert.AreEqual(1, shots, "still fires at contact distance");

            wall.position = new Vector3(0f, 1.5f, muzzle - 0.5f + 0.05f);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(inertia.IsTucked, "deeper: full tuck");
            weapons.PullTrigger();
            weapons.ReleaseTrigger();
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(1, shots, "a tucked muzzle is off target");
        }

        // ---- Inspect, camera kick, tremor ----

        [UnityTest]
        public IEnumerator Inspect_ShowsBothSides_AndAimingCancelsIt()
        {
            RealisticWeaponController.InspectPose(0f, 3.5f, out Vector3 start, out Vector3 startEuler);
            RealisticWeaponController.InspectPose(1f, 3.5f, out _, out Vector3 markings);
            RealisticWeaponController.InspectPose(2.5f, 3.5f, out _, out Vector3 port);
            RealisticWeaponController.InspectPose(3.5f, 3.5f, out Vector3 end, out Vector3 endEuler);
            Assert.AreEqual(Vector3.zero, start + startEuler);
            Assert.Less(markings.z, -30f, "tilted to show the left side");
            Assert.Greater(port.z, 60f, "rolled over to the ejection port");
            Assert.AreEqual(Vector3.zero, end + endEuler, "back to the ready");

            weapons.SetLoadout(0, TestWeapons.CreateTemplate(created), Data(0f));
            yield return new WaitForSeconds(0.15f);
            PressAndRelease(keyboard.iKey);
            yield return null;
            Assert.AreEqual(WeaponAction.Inspect, weapons.CurrentAction);
            yield return new WaitForSeconds(1f);
            Transform action = player.GetComponent<ViewmodelRig>().ActionPivot;
            Assert.Greater(Quaternion.Angle(Quaternion.identity, action.localRotation), 20f, "the weapon turns in the hands");
            yield return new WaitForSeconds(2.7f);
            Assert.AreEqual(WeaponAction.None, weapons.CurrentAction, "3.5 s and done");

            PressAndRelease(keyboard.iKey);
            yield return null;
            Press(mouse.rightButton);
            yield return null;
            yield return null;
            Release(mouse.rightButton);
            Assert.AreEqual(WeaponAction.None, weapons.CurrentAction, "aiming cuts the inspection short");
        }

        [UnityTest]
        public IEnumerator UnsuppressedShot_KicksTheFov_SuppressedDoesNot()
        {
            Assert.AreEqual(1f, ProceduralADSAndOpticsController.ShotKickScale(1700f, 1700f), 1e-4f);
            Assert.AreEqual(1.8f, ProceduralADSAndOpticsController.ShotKickScale(6000f, 1700f), 1e-4f, ".300 WM kicks hardest (clamped)");
            Assert.AreEqual(1f, ProceduralADSAndOpticsController.ShotKickEnvelope(0.005f, 0.04f), 1e-4f, "peaks in 5 ms");
            Assert.AreEqual(0f, ProceduralADSAndOpticsController.ShotKickEnvelope(0.04f, 0.04f), "gone after 40 ms");

            weapons.SetLoadout(0, TestWeapons.CreateTemplate(created), Data(0f));
            yield return new WaitForSeconds(0.15f);
            var ads = player.GetComponent<ProceduralADSAndOpticsController>();
            Assert.AreEqual(0f, ads.ShotKickDegrees);

            yield return MeasureKick(ads, 1f);
            Assert.Greater(measuredKick, 0.2f, "the blast punches the view");
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(0f, ads.ShotKickDegrees, "and it is over in 40 ms");
            yield return MeasureKick(ads, 0f);
            Assert.AreEqual(0f, measuredKick, "a suppressed shot does not kick");
        }

        [Test]
        public void HandTremor_IsAFastSmallMicroMotion()
        {
            const float amplitude = 0.05f;
            int crossings = 0;
            float peak = 0f, energy = 0f, previous = 0f;
            for (int i = 0; i < 2000; i++)
            {
                Vector2 tremor = HeadbobAndBreathingSystem.HandTremor(i * 0.001f, amplitude, 9f);
                peak = Mathf.Max(peak, tremor.magnitude);
                energy += tremor.x * tremor.x;
                if (i > 0 && (tremor.x < 0f) != (previous < 0f)) crossings++;
                previous = tremor.x;
            }

            Assert.Less(peak, amplitude * 1.6f, "micro motion");
            Assert.Greater(Mathf.Sqrt(energy / 2000f), amplitude * 0.1f, "but always there");
            Assert.That(crossings / 2f, Is.InRange(5f, 60f), "a physiological 8-12 Hz band, not a slow sway");
        }

        // ---- Doors ----

        [UnityTest]
        public IEnumerator Door_TapOpensAway_HoldAndScrollCracksIt_VBashesAndStuns()
        {
            TacticalDoor door = DoorBuilder.Doorway(level, "Test Door", new Vector3(0f, 0f, 2f), Quaternion.identity, 1.6f, 3f, 0.3f, "Concrete");
            Physics.SyncTransforms();
            yield return null;
            Assert.IsNotNull(player.GetComponent<Interactor>().Focused, "the door is in reach");

            PressAndRelease(keyboard.fKey);
            yield return new WaitForSeconds(1.2f);
            Assert.Less(door.Angle, -80f, "tap: opens away from the player (toward +Z)");
            PressAndRelease(keyboard.fKey);
            yield return new WaitForSeconds(1.5f);
            Assert.Less(Mathf.Abs(door.Angle), 5f, "tap again: closed");

            float pace = locomotion.PaceScale;
            Press(keyboard.fKey);
            yield return null;
            for (int i = 0; i < 3; i++)
            {
                Set(mouse.scroll, new Vector2(0f, 120f));
                yield return null;
                Set(mouse.scroll, Vector2.zero);
                yield return null;
            }

            Release(keyboard.fKey);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(-18f, door.Angle, 2f, "three scroll steps: cracked 18 degrees to peek");
            Assert.AreEqual(pace, locomotion.PaceScale, "the scroll went to the door, not the walking pace");

            PressAndRelease(keyboard.fKey);
            yield return new WaitForSeconds(1.5f);
            AIPerceptionSensor perception = Observer(new Vector3(0f, 1.6f, 2.7f), Vector3.forward); // someone right behind it
            perception.gameObject.layer = GameLayers.Player;
            perception.gameObject.AddComponent<SphereCollider>().radius = 0.3f;
            MovePlayer(new Vector3(0f, 0f, 0.8f)); // within arm's reach
            yield return null;

            PressAndRelease(keyboard.vKey);
            yield return null;
            Assert.AreEqual(1, door.Bashes, "V facing a door bashes it instead of mounting");
            Assert.IsFalse(locomotion.StanceController.IsMounted);
            yield return new WaitForSeconds(0.15f);
            Assert.Less(door.Angle, -45f, "slammed open");
            Assert.IsTrue(perception.IsStunned, "the soldier behind it is knocked senseless");
        }

        [UnityTest]
        public IEnumerator SprintingIntoADoor_BashesItOpen()
        {
            TacticalDoor door = DoorBuilder.Doorway(level, "Test Door", new Vector3(0f, 0f, 7f), Quaternion.identity, 1.6f, 3f, 0.3f, "Concrete");
            Physics.SyncTransforms();
            Press(keyboard.wKey);
            yield return null;
            Press(keyboard.leftShiftKey);
            yield return new WaitForSeconds(0.1f);
            Release(keyboard.leftShiftKey);
            float fastest = 0f;
            for (float t = 0f; t < 2.1f; t += Time.deltaTime)
            {
                fastest = Mathf.Max(fastest, locomotion.PlanarSpeed);
                yield return null;
            }

            Release(keyboard.wKey);
            Assert.GreaterOrEqual(door.Bashes, 1, $"ran it open (top speed {fastest:F1} m/s)");
            Assert.Greater(player.transform.position.z, 6f, "and through");
        }

        // ---- Helpers ----

        private IEnumerator MeasureKick(ProceduralADSAndOpticsController ads, float flash)
        {
            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = ads.transform.position, Shooter = player.transform, MuzzleFlash = flash, RoundsLeft = -1 });
            measuredKick = 0f;
            for (int i = 0; i < 4; i++)
            {
                yield return null; // each read sees the previous frame's LateUpdate
                measuredKick = Mathf.Max(measuredKick, ads.ShotKickDegrees);
            }
        }

        private WeaponDataSO Data(float sprintToFire)
        {
            var data = ScriptableObject.CreateInstance<WeaponDataSO>();
            var caliber = ScriptableObject.CreateInstance<CaliberDataSO>();
            created.Add(data);
            created.Add(caliber);
            TestWeapons.SetField(data, "caliber", caliber);
            TestWeapons.SetField(data, "fireModes", new[] { FireMode.Semi });
            TestWeapons.SetField(data, "magazineCapacity", 10);
            TestWeapons.SetField(data, "sprintToFireTime", sprintToFire);
            TestWeapons.SetField(data, "drawTime", 0.05f);
            TestWeapons.SetField(data, "holsterTime", 0.05f);
            return data;
        }

        private void OnShot(in ShotFiredEvent shot)
        {
            if (shot.Shooter == player.transform) shots++;
        }
    }
}
#endif
