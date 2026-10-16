using System.Collections;
using FPS.AI;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class EquipmentTests : AITestWorld
    {
        private RealisticWeaponController weapons;
        private TacticalEquipmentSystem equipment;
        private WeaponDataSO rifle;

        private IEnumerator ArmPlayer(GrenadeDataSO lethal, GrenadeDataSO tactical)
        {
            weapons = player.GetComponent<RealisticWeaponController>();
            rifle = RifleData();
            TestWeapons.SetField(rifle, "drawTime", 0.05f);
            weapons.SetLoadout(0, TestWeapons.CreateTemplate(created), rifle);
            equipment = player.AddComponent<TacticalEquipmentSystem>();
            equipment.SetLoadout(EquipmentSlot.Lethal, lethal, 2);
            if (tactical != null) equipment.SetLoadout(EquipmentSlot.Tactical, tactical, 2);
            yield return new WaitForSeconds(0.2f);
        }

        [UnityTest]
        public IEnumerator CookedFrag_KeepsItsBurntFuse_AndOvercookingGoesOffInHand()
        {
            GrenadeDataSO frag = GrenadeData(GrenadeType.Fragmentation, 3f);
            yield return ArmPlayer(frag, null);

            float pressed = Time.time;
            Press(keyboard.gKey);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(WeaponAction.Equipment, weapons.CurrentAction, "the rifle is lowered while the grenade is in hand");
            Assert.IsTrue(equipment.IsCooking, "pin out, spoon gone: cooking");
            int roundsBefore = weapons.ActiveState.LoadedRounds;
            weapons.PullTrigger();
            weapons.ReleaseTrigger();
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(roundsBefore, weapons.ActiveState.LoadedRounds, "no shooting with a grenade in hand");

            Release(keyboard.gKey);
            yield return new WaitForSeconds(0.2f);
            ThrownGrenade thrown = equipment.LastThrown;
            Assert.IsNotNull(thrown, "thrown on release");
            Assert.AreEqual(1, equipment.Count(EquipmentSlot.Lethal));
            // The fuse started when the spoon flew (pin pulled 0.35 s after the press), not when the grenade left the hand.
            Assert.AreEqual(pressed + 0.35f + 3f, Time.time + thrown.TimeToDetonation, 0.15f, "cooked time carries over");
            Assert.Greater(thrown.GetComponent<Rigidbody>().linearVelocity.magnitude, 5f, "it flies");

            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(WeaponAction.None, weapons.CurrentAction, "the rifle comes back up");

            // Hold it too long: it goes off in hand.
            Press(keyboard.gKey);
            yield return new WaitForSeconds(0.35f + 3f + 0.3f);
            ThrownGrenade cookedOff = equipment.LastThrown;
            Assert.AreNotSame(thrown, cookedOff);
            Assert.IsTrue(cookedOff.HasDetonated);
            Assert.Less(Vector3.Distance(cookedOff.DetonationPoint, player.GetComponent<PlayerRig>().PlayerCamera.transform.position), 1.5f, "in the hand");
            Release(keyboard.gKey);
            Assert.AreEqual(0, equipment.Count(EquipmentSlot.Lethal));
        }

        [UnityTest]
        public IEnumerator SmokeScreen_HidesThePlayerFromAI_UntilItClears()
        {
            var cloud = VisionObstruction.Add();
            VisionObstruction.Set(cloud, new Vector3(0f, 1f, 0f), 2f, 1.5f);
            Assert.Less(VisionObstruction.Transmittance(new Vector3(-5f, 1f, 0f), new Vector3(5f, 1f, 0f)), 0.01f, "through the middle");
            Assert.AreEqual(1f, VisionObstruction.Transmittance(new Vector3(-5f, 3.5f, 0f), new Vector3(5f, 3.5f, 0f)), 1e-5f, "above it");
            float edge = VisionObstruction.Transmittance(new Vector3(-5f, 2.8f, 0f), new Vector3(5f, 2.8f, 0f));
            Assert.That(edge, Is.InRange(0.05f, 0.9f), "through its edge: partly");
            VisionObstruction.Remove(cloud);

            GrenadeDataSO smoke = GrenadeData(GrenadeType.Smoke, 0.2f);
            TestWeapons.SetField(smoke, "smokeBuildUp", 0.5f);
            TestWeapons.SetField(smoke, "smokeRadius", 3f);
            ThrownGrenade canister = ObjectPoolManager.Instance.Spawn(smoke.Prefab, new Vector3(0f, 0.1f, 8f), Quaternion.identity);
            canister.Throw(smoke, null, Vector3.zero);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(1, VisionObstruction.Count, "the cloud is up");

            AIPerceptionSensor watcher = Observer(new Vector3(0f, 1.6f, 16f), Vector3.back);
            yield return new WaitForSeconds(2.5f);
            Assert.IsFalse(watcher.TargetIdentifiedNow, "a standing figure behind the smoke stays unseen");

            ObjectPoolManager.Instance.Release(canister);
            Assert.AreEqual(0, VisionObstruction.Count, "the cloud is gone");
            yield return new WaitForSeconds(2f);
            Assert.IsTrue(watcher.TargetIdentifiedNow, "and the figure is seen at once");
        }

        [Test]
        public void Flashbang_OnScreenBlindsFully_OffScreenLess_BehindLeast()
        {
            Camera view = player.GetComponent<PlayerRig>().PlayerCamera;
            Vector3 eye = view.transform.position;
            float ahead = FlashbangEvent.ScreenExposure(view, eye + view.transform.forward * 5f + Vector3.right, 1f, out float deaf);
            float side = FlashbangEvent.ScreenExposure(view, eye + Quaternion.Euler(0f, 100f, 0f) * view.transform.forward * 5f, 1f, out _);
            float behind = FlashbangEvent.ScreenExposure(view, eye - view.transform.forward * 5f, 1f, out _);

            Assert.Greater(ahead, 0.85f, "anywhere on screen is a full flash");
            Assert.Greater(deaf, 0.8f);
            Assert.Less(side, ahead);
            Assert.Less(behind, side);
            Assert.Greater(behind, 0.2f, "bounce light behind the head still dazzles");
        }

        [UnityTest]
        public IEnumerator FallenSoldiersRifle_GivesUpItsMagazines()
        {
            GrenadeDataSO frag = GrenadeData(GrenadeType.Fragmentation, 3f);
            yield return ArmPlayer(frag, null);
            BakeNavMesh();
            TacticalAIBrain soldier = Soldier(new Vector3(0f, 0f, 30f), 0f, null, null, null, rifle);
            yield return new WaitForSeconds(0.2f);

            soldier.GetComponent<FPS.Ballistics.TacticalHealthAndArmorSystem>().ApplyDamage(FPS.Ballistics.HitZone.Head, 500f, soldier.Body.Head.position, Vector3.forward);
            yield return new WaitForSeconds(0.3f);
            DroppedWeapon loot = Object.FindAnyObjectByType<DroppedWeapon>();
            Assert.IsNotNull(loot, "the fallen rifle can be looted");
            Assert.AreEqual(4, loot.Remaining, "the magazine in the rifle and three spares");

            WeaponState state = weapons.ActiveState;
            int before = state.SpareMagazines.Count;
            Assert.IsTrue(loot.CanInteract(player));
            loot.Interact(player);
            Assert.Greater(state.SpareMagazines.Count, before, "magazines taken");
            Assert.LessOrEqual(state.SpareMagazines.Count, rifle.SpareMagazines + 2, "within what can be carried");
            Assert.AreEqual(4 - (state.SpareMagazines.Count - before), loot.Remaining);
        }
    }
}
