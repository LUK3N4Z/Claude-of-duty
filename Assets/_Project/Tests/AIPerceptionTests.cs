using System.Collections;
using FPS.AI;
using FPS.Audio;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class AIPerceptionTests : AITestWorld
    {
        [UnityTest]
        public IEnumerator Sight_DaylightRevealsButADarkAlcoveHides_UntilAFlashlightGivesItAway()
        {
            DarkAlcove(Vector3.zero);
            Physics.SyncTransforms();
            Assert.Less(AIIllumination.Sample(new Vector3(0f, 1.36f, 0f), 0.35f), 0.05f, "inside the alcove is dark");
            Assert.Greater(AIIllumination.Sample(new Vector3(0f, 1.36f, -6f), 0.35f), 0.95f, "out front is sunlit");

            AIPerceptionSensor watcher = Observer(new Vector3(0f, 1.6f, -25f), Vector3.forward);
            yield return new WaitForSeconds(2f);
            Assert.IsFalse(watcher.TargetIdentifiedNow, "a still figure in a dark corner at 25 m stays unseen");

            MovePlayer(new Vector3(0f, 0f, -6f));
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(watcher.TargetIdentifiedNow, "the same figure in daylight is identified");

            AIPerceptionSensor second = Observer(new Vector3(2f, 1.6f, -25f), Vector3.forward);
            MovePlayer(Vector3.zero);
            var light = new GameObject("Torch");
            light.transform.SetParent(player.transform, false);
            light.transform.SetLocalPositionAndRotation(new Vector3(0.1f, 1.4f, -0.3f), Quaternion.Euler(0f, 180f, 0f)); // shining out of the alcove
            TacticalDevice torch = light.AddComponent<TacticalDevice>();
            torch.SetOn(true);
            Physics.SyncTransforms();
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(second.TargetIdentifiedNow, "a lit flashlight pointed out of the dark gives its carrier away");
        }

        [UnityTest]
        public IEnumerator Sight_CrouchedTargetInPeripheralVisionIsNoticedFarSlower()
        {
            MovePlayer(new Vector3(0f, 0f, 30f));
            AIPerceptionSensor focused = Observer(new Vector3(0f, 1.6f, 0f), Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            float standingInFocus = focused.Awareness;
            Object.Destroy(focused.gameObject);

            Assert.IsTrue(player.GetComponent<StanceAndLeanController>().TrySetStance(Stance.Crouch));
            AIPerceptionSensor peripheral = Observer(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 55f, 0f) * Vector3.forward);
            yield return new WaitForSeconds(0.5f);
            float crouchedPeripheral = peripheral.Awareness;

            Assert.Greater(standingInFocus, 0.2f);
            Assert.Greater(standingInFocus, crouchedPeripheral * 3f, $"standing/focus {standingInFocus} vs crouched/peripheral {crouchedPeripheral}");
        }

        [UnityTest]
        public IEnumerator Hearing_FootstepsScaleWithMovement_AndSuppressorsShrinkGunshotRange()
        {
            AIPerceptionSensor behind = Observer(new Vector3(0f, 1.6f, -3f), Vector3.back); // facing away
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0f, behind.Awareness, "standing still makes no noise");

            Press(keyboard.wKey);
            yield return new WaitForSeconds(1.2f);
            Release(keyboard.wKey);
            Assert.Greater(behind.Awareness, 0.15f, "walking footsteps are heard");
            Assert.IsFalse(behind.TargetIdentifiedNow, "sound alone never identifies");

            var open = ScriptableObject.CreateInstance<WeaponAudioProfileSO>();
            var suppressed = ScriptableObject.CreateInstance<WeaponAudioProfileSO>();
            created.Add(open);
            created.Add(suppressed);
            TestWeapons.SetField(suppressed, "maxAudibleDistance", 300f);
            TestWeapons.SetField(suppressed, "loudness", 0.35f);

            Vector3 shooter = player.transform.position + Vector3.up * 1.4f;
            Transform gun = playerTarget.Chest; // any part of the player identifies the shooter
            AIPerceptionSensor far = Observer(shooter + new Vector3(400f, 0.2f, 0f), Vector3.right);
            AIPerceptionSensor mid = Observer(shooter + new Vector3(-120f, 0.2f, 0f), Vector3.left);
            yield return null;
            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = shooter, Direction = Vector3.forward, Shooter = gun, Profile = suppressed });
            Assert.AreEqual(0f, far.Awareness, "a suppressed shot is not heard at 400 m");
            Assert.GreaterOrEqual(mid.Awareness, 0.75f, "but is at 120 m");
            Assert.Less(Vector3.Distance(mid.LastKnownPosition, playerTarget.AimPoint), 120f * 0.3f + 0.01f, "located only roughly");

            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = shooter, Direction = Vector3.forward, Shooter = gun, Profile = open });
            Assert.Greater(far.Awareness, 0f, "an unsuppressed shot is heard at 400 m");
            Assert.Less(Vector3.Distance(mid.LastKnownPosition, playerTarget.AimPoint), 120f * 0.08f + 0.01f, "and located within 8% of the distance");
        }

        [UnityTest]
        public IEnumerator MuzzleFlash_PinsAHiddenShooter_ButASuppressedShotOnlyAlerts()
        {
            DarkAlcove(Vector3.zero);
            Physics.SyncTransforms();
            var profile = ScriptableObject.CreateInstance<WeaponAudioProfileSO>();
            created.Add(profile);
            AIPerceptionSensor flashSeen = Observer(new Vector3(0f, 1.6f, -30f), Vector3.forward);
            yield return new WaitForSeconds(0.3f);
            Vector3 muzzle = new Vector3(0.2f, 1.4f, -0.6f);

            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = muzzle, Direction = Vector3.back, Shooter = playerTarget.Chest, Profile = profile, MuzzleFlash = 1f });
            Assert.IsTrue(flashSeen.TargetIdentifiedNow, "the flash gives the shooter away in the dark");
            Assert.Less(Vector3.Distance(flashSeen.LastKnownPosition, playerTarget.AimPoint), 0.01f);

            AIPerceptionSensor suppressed = Observer(new Vector3(3f, 1.6f, -30f), Vector3.forward);
            yield return new WaitForSeconds(0.3f);
            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = muzzle, Direction = Vector3.back, Shooter = playerTarget.Chest, Profile = profile, MuzzleFlash = 0f });
            Assert.IsFalse(suppressed.TargetIdentifiedNow, "no flash: heard, not seen");
            Assert.AreEqual(AwarenessState.Alerted, suppressed.State);
        }

        [UnityTest]
        public IEnumerator Flashbang_BlindsAndDeafens_FacingEyesWorst()
        {
            AIPerceptionSensor facing = Observer(new Vector3(0f, 1.6f, -5f), Vector3.forward);
            AIPerceptionSensor away = Observer(new Vector3(1f, 1.6f, -5f), Vector3.back);
            Wall(new Vector3(-3.5f, 1.5f, 0f), new Vector3(0.3f, 3f, 6f));
            AIPerceptionSensor walled = Observer(new Vector3(-5f, 1.6f, 0f), Vector3.right);
            Physics.SyncTransforms();
            yield return null;

            EventBus<FlashbangEvent>.Raise(new FlashbangEvent { Position = new Vector3(0f, 0.1f, 0f), Intensity = 1f });
            Assert.IsTrue(facing.IsStunned, "looking at the bang");
            Assert.Greater(facing.Blindness, away.Blindness * 2f);
            Assert.Less(walled.Blindness, 0.2f, "a wall leaves only bounce light");
            Assert.Greater(walled.Deafness, 0.5f, "but the bang still deafens through it");

            float blind = FlashbangEvent.Exposure(Vector3.zero, 1f, new Vector3(0f, 1.6f, -30f), Vector3.forward, out float deaf);
            Assert.AreEqual(0f, blind, "beyond 18 m the flash does not blind");
            Assert.AreEqual(0f, deaf, "beyond 25 m the bang does not deafen");

            yield return new WaitForSeconds(6f);
            Assert.IsFalse(facing.IsStunned, "sight comes back");
        }

        [Test]
        public void GrenadeThrows_LandOnTarget_LowAndHighArc()
        {
            Vector3 origin = new Vector3(0f, 1.9f, 0f);
            Vector3 target = new Vector3(4f, 0.1f, 12f);
            foreach (bool high in new[] { false, true })
            {
                Assert.IsTrue(GrenadeBallistics.SolveLaunch(origin, target, 16f, high, out Vector3 velocity));
                Assert.AreEqual(16f, velocity.magnitude, 1e-3f);
                float time = GrenadeBallistics.FlightTime(origin, target, velocity);
                Assert.Less(Vector3.Distance(GrenadeBallistics.Position(origin, velocity, time), target), 0.02f, high ? "high arc" : "low arc");
            }

            Assert.IsFalse(GrenadeBallistics.SolveLaunch(origin, new Vector3(0f, 0f, 60f), 16f, false, out _), "out of throwing range");
        }

        [Test]
        public void SliceArc_SweepsFromTheWallToTheDoorAxis()
        {
            var door = new GameObject("Door").transform;
            created.Add(door.gameObject);
            var points = new Vector3[4];
            Assert.AreEqual(4, CQBRoom.SliceArc(door, 1f, 1.5f, 70f, points, 4));

            for (int i = 0; i < 4; i++) Assert.AreEqual(1.5f, Vector3.Distance(points[i], door.position), 1e-4f);
            Assert.Greater(points[0].x, 1.3f, "starts beside the door, against the wall on the stack side");
            Assert.Less(points[0].z, 0f, "outside the room");
            Assert.AreEqual(0f, points[3].x, 1e-4f, "ends square in front of the doorway");
            for (int i = 1; i < 4; i++) Assert.Less(points[i].x, points[i - 1].x, "each step slices a further wedge");

            // From the first slice the aim crosses the doorway toward the far, opposite side of the room.
            Vector3 aim = CQBRoom.SliceAim(door, points[0], 4f);
            Assert.Less(aim.x, 0f);
            Assert.Greater(aim.z, 0f);
        }
    }
}
