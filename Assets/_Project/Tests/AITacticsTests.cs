using System.Collections;
using System.Collections.Generic;
using FPS.AI;
using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class AITacticsTests : AITestWorld
    {
        private struct Shot
        {
            public ShotFiredEvent Event;
            public Vector3 Muzzle;
            public Vector3 Bore;
            public float Time;
        }

        private readonly List<Shot> shots = new List<Shot>();
        private readonly List<TacticalAIBrain> soldiers = new List<TacticalAIBrain>();
        private int flashbangs;

        public override void Setup()
        {
            base.Setup();
            shots.Clear();
            soldiers.Clear();
            flashbangs = 0;
            EventBus<ShotFiredEvent>.Subscribe(OnShot);
            EventBus<FlashbangEvent>.Subscribe(OnFlashbang);
        }

        public override void TearDown()
        {
            EventBus<ShotFiredEvent>.Unsubscribe(OnShot);
            EventBus<FlashbangEvent>.Unsubscribe(OnFlashbang);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator Soldier_SpotsThePlayer_AndFiresFromItsMuzzleAlongTheBore()
        {
            BakeNavMesh();
            TacticalAIBrain soldier = Track(Soldier(new Vector3(0f, 0f, 14f), 180f));
            var health = player.GetComponent<TacticalHealthAndArmorSystem>();

            float deadline = Time.time + 8f;
            while (Time.time < deadline && (shots.Count < 3 || health.Health >= health.MaxHealth)) yield return null;

            Assert.GreaterOrEqual(Count(CalloutType.Contact), 1, "contact is called out");
            Assert.GreaterOrEqual(shots.Count, 3, "the soldier opened fire");
            float moa = soldier.WeaponHandler.Data.MoaAccuracy;
            foreach (Shot shot in shots)
            {
                Assert.Less(Vector3.Distance(shot.Event.Origin, shot.Muzzle), 1e-4f, "rounds leave the muzzle socket");
                Assert.LessOrEqual(Vector3.Angle(shot.Event.Direction, shot.Bore), moa / 120f + 0.05f, "along the bore"); // float acos noise ~0.02°
            }

            Vector3 toPlayer = playerTarget.AimPoint - shots[shots.Count - 1].Event.Origin;
            Assert.Less(Vector3.Angle(shots[shots.Count - 1].Event.Direction, toPlayer), 8f, "aimed at the player");
            Assert.Less(health.Health, health.MaxHealth, "AI rounds hit the player's hitboxes");
        }

        [UnityTest]
        public IEnumerator Soldier_MovesToCoverThatHidesIt_AndFightsFromIt()
        {
            Wall(new Vector3(2.5f, 0.65f, 14f), new Vector3(2.6f, 1.3f, 0.3f));
            Wall(new Vector3(-3.5f, 0.65f, 18f), new Vector3(2.6f, 1.3f, 0.3f));
            BakeNavMesh();
            TacticalAIBrain soldier = Track(Soldier(new Vector3(0f, 0f, 16f), 180f));

            bool firedFromCover = false;
            float deadline = Time.time + 12f;
            int shotsAtCover = -1;
            while (Time.time < deadline && !firedFromCover)
            {
                if (soldier.AtCover && shotsAtCover < 0) shotsAtCover = shots.Count;
                firedFromCover = shotsAtCover >= 0 && soldier.AtCover && shots.Count > shotsAtCover;
                yield return null;
            }

            Assert.IsTrue(soldier.HasCover, "found cover");
            Vector3 threatEye = playerTarget.Head.position;
            Assert.IsTrue(CoverFinder.IsConcealed(soldier.Cover.Position, threatEye, !soldier.Cover.Tall), "the cover hides the soldier from the player");
            Assert.AreNotEqual(PeekMode.None, soldier.Cover.Peek, "and can be fought from");
            Assert.IsTrue(firedFromCover, "fired from the cover position");
        }

        [UnityTest]
        public IEnumerator Squad_SuppressesWhereTheTargetWent_CallsItSuppressed_AndFlanks()
        {
            Wall(new Vector3(0f, 0.575f, 1f), new Vector3(4f, 1.15f, 0.3f)); // low wall in front of the player
            BakeNavMesh();
            SquadCoordinator squad = Squad();
            Track(Soldier(new Vector3(-1.5f, 0f, 16f), 180f, squad));
            Track(Soldier(new Vector3(1.5f, 0f, 16f), 180f, squad));

            float deadline = Time.time + 5f;
            while (Time.time < deadline && !(soldiers[0].Perception.TargetIdentifiedNow && soldiers[1].Perception.TargetIdentifiedNow)) yield return null;
            Assert.IsTrue(soldiers[0].Perception.TargetIdentifiedNow && soldiers[1].Perception.TargetIdentifiedNow, "both see the player over the wall");

            Assert.IsTrue(player.GetComponent<StanceAndLeanController>().TrySetStance(Stance.Crouch)); // duck behind it
            Vector3 lastSeen = playerTarget.AimPoint;
            float hiddenAt = Time.time;

            bool suppressed = false, flanked = false;
            int intoTheArea = 0, counted = 0;
            deadline = Time.time + 15f;
            while (Time.time < deadline && (Count(CalloutType.TargetSuppressed) == 0 || !flanked || intoTheArea == 0))
            {
                suppressed |= soldiers[0].State == AIState.Suppress || soldiers[1].State == AIState.Suppress;
                flanked |= squad.Flanker != null && squad.Flanker.State == AIState.Flank;
                for (; counted < shots.Count; counted++)
                {
                    Shot shot = shots[counted];
                    if (shot.Time < hiddenAt + 0.6f) continue;
                    float miss = Vector3.Cross(shot.Event.Direction, lastSeen - shot.Event.Origin).magnitude; // area's distance from the line of fire
                    if (miss < 2.5f) intoTheArea++;
                }

                yield return null;
            }

            Assert.IsTrue(suppressed, "contact lost: the squad lays fire on the last known position");

            Assert.Greater(intoTheArea, 0, "suppressive rounds go into the last known position");
            Assert.GreaterOrEqual(Count(CalloutType.TargetSuppressed), 1, "\"Target suppressed!\" when rounds crack past the target");
            Assert.IsTrue(flanked, "a flanker is sent round");
            Assert.GreaterOrEqual(Count(CalloutType.FlankingLeft) + Count(CalloutType.FlankingRight), 1, "and calls the flank");
        }

        [UnityTest]
        public IEnumerator Squad_ClearsARoom_StackFlashbangThenEntry()
        {
            // 5 x 5 m room north of the squad, one 1.4 m door in its south wall.
            Wall(new Vector3(0f, 1.5f, 12.5f), new Vector3(5.3f, 3f, 0.3f));
            Wall(new Vector3(-2.5f, 1.5f, 10f), new Vector3(0.3f, 3f, 5f));
            Wall(new Vector3(2.5f, 1.5f, 10f), new Vector3(0.3f, 3f, 5f));
            Wall(new Vector3(-1.65f, 1.5f, 7.5f), new Vector3(1.9f, 3f, 0.3f));
            Wall(new Vector3(1.65f, 1.5f, 7.5f), new Vector3(1.9f, 3f, 0.3f));
            BakeNavMesh();

            var roomObject = new GameObject("Room");
            created.Add(roomObject);
            roomObject.transform.position = new Vector3(0f, 1.5f, 10f);
            var door = new GameObject("Door").transform;
            door.SetParent(roomObject.transform, true);
            door.position = new Vector3(0f, 0f, 7.5f);
            CQBRoom room = roomObject.AddComponent<CQBRoom>();
            room.Configure(new Vector3(4.7f, 3f, 4.7f), new[] { door }, new[]
            {
                Point(roomObject.transform, new Vector3(1.6f, 0f, 8.4f)),
                Point(roomObject.transform, new Vector3(-1.6f, 0f, 11.6f)),
            });

            MovePlayer(new Vector3(-1.8f, 0f, 8.3f)); // holed up beside the door, out of its line
            GrenadeDataSO flash = GrenadeData(GrenadeType.Flashbang, 1.5f);
            SquadCoordinator squad = Squad();
            Track(Soldier(new Vector3(-3f, 0f, 1f), 0f, squad, null, flash));
            Track(Soldier(new Vector3(3f, 0f, 1f), 0f, squad, null, flash));
            yield return null;
            foreach (TacticalAIBrain soldier in soldiers)
                soldier.Perception.ShareKnowledge(playerTarget, playerTarget.AimPoint, Time.time, true); // "he ran into that room"

            bool stacked = false;
            float deadline = Time.time + 30f;
            while (Time.time < deadline && !(Inside(room) && Count(CalloutType.Breaching) > 0))
            {
                stacked |= soldiers[0].State == AIState.Stack || soldiers[1].State == AIState.Stack;
                yield return null;
            }

            Assert.IsTrue(stacked, "the squad stacks on the door");
            Assert.GreaterOrEqual(Count(CalloutType.StackUp), 1);
            Assert.GreaterOrEqual(Count(CalloutType.FlashbangOut), 1, "a flashbang goes in first");
            Assert.GreaterOrEqual(flashbangs, 1, "and detonates");
            Assert.GreaterOrEqual(Count(CalloutType.Breaching), 1, "then the entry");
            Assert.IsTrue(Inside(room), $"soldiers are inside the room: {Describe()}");
        }

        [UnityTest]
        public IEnumerator Soldier_FlushesAnEntrenchedTarget_WithAFrag()
        {
            BakeNavMesh();
            GrenadeDataSO frag = GrenadeData(GrenadeType.Fragmentation, 4.5f);
            TacticalAIBrain soldier = Track(Soldier(new Vector3(0f, 0f, 14f), 180f, null, frag));
            var health = player.GetComponent<TacticalHealthAndArmorSystem>();

            float deadline = Time.time + 4f;
            while (Time.time < deadline && !soldier.Perception.TargetIdentifiedNow) yield return null;
            Assert.IsTrue(soldier.Perception.TargetIdentifiedNow);

            Wall(new Vector3(0f, 1.3f, 1.5f), new Vector3(4f, 2.6f, 0.3f)); // the player is now behind solid cover
            Physics.SyncTransforms();
            float hiddenHealth = health.Health;

            deadline = Time.time + 16f;
            while (Time.time < deadline && (soldier.LastGrenade == null || !soldier.LastGrenade.HasDetonated)) yield return null;

            Assert.GreaterOrEqual(Count(CalloutType.FragOut), 1, "\"Frag out!\"");
            Assert.IsNotNull(soldier.LastGrenade, "a frag was thrown at the hidden target");
            Assert.IsTrue(soldier.LastGrenade.HasDetonated);
            Assert.Less(Vector3.Distance(soldier.LastGrenade.DetonationPoint, player.transform.position), 4f, "it landed behind the cover");
            Assert.Less(health.Health, hiddenHealth, "blast and fragments reached the target");
            Assert.AreEqual(0, soldier.FragGrenades);
        }

        [UnityTest]
        public IEnumerator Reload_IsCalledOut_AndCovered_ThenTheMagazineIsFull()
        {
            BakeNavMesh();
            SquadCoordinator squad = Squad();
            TacticalAIBrain shooter = Track(Soldier(new Vector3(-1.5f, 0f, 12f), 180f, squad));
            Track(Soldier(new Vector3(1.5f, 0f, 12f), 180f, squad));
            TestWeapons.SetField(shooter.WeaponHandler, "rounds", 1);

            bool reloading = false;
            float deadline = Time.time + 10f;
            do
            {
                yield return null;
                reloading |= shooter.WeaponHandler.IsReloading;
            } while (Time.time < deadline && !(Count(CalloutType.Reloading) > 0 && Count(CalloutType.Covering) > 0));

            Assert.IsTrue(reloading, "reloads the near-empty magazine");
            Assert.GreaterOrEqual(Count(CalloutType.Reloading), 1, "\"Reloading!\"");
            Assert.GreaterOrEqual(Count(CalloutType.Covering), 1, "\"Covering!\" from the teammate");

            deadline = Time.time + 6f;
            while (Time.time < deadline && shooter.WeaponHandler.IsReloading) yield return null;
            Assert.IsFalse(shooter.WeaponHandler.IsReloading);
            Assert.AreEqual(2, shooter.WeaponHandler.SpareMagazines, "one spare magazine used");
        }

        [UnityTest]
        public IEnumerator Killed_SoldierCollapses_DropsItsRifle_AndTheSquadCallsManDown()
        {
            BakeNavMesh();
            SquadCoordinator squad = Squad();
            TacticalAIBrain victim = Track(Soldier(new Vector3(-1.5f, 0f, 30f), 0f, squad));
            Track(Soldier(new Vector3(1.5f, 0f, 30f), 0f, squad));
            yield return new WaitForSeconds(0.3f);

            victim.GetComponent<TacticalHealthAndArmorSystem>().ApplyDamage(HitZone.Head, 500f, victim.Body.Head.position, Vector3.forward);
            yield return new WaitForSeconds(1f);

            Assert.IsTrue(victim.IsDead);
            Assert.IsFalse(victim.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled);
            Assert.IsNotNull(victim.GetComponent<Rigidbody>(), "the body falls");
            Assert.IsNull(victim.Body.Weapon, "the rifle is dropped");
            Assert.GreaterOrEqual(Count(CalloutType.ManDown), 1);
        }

        private TacticalAIBrain Track(TacticalAIBrain soldier)
        {
            soldiers.Add(soldier);
            return soldier;
        }

        private bool Inside(CQBRoom room)
        {
            foreach (TacticalAIBrain soldier in soldiers)
            {
                if (room.Contains(soldier.transform.position + Vector3.up)) return true;
            }

            return false;
        }

        private string Describe()
        {
            var text = new System.Text.StringBuilder();
            foreach (TacticalAIBrain soldier in soldiers)
                text.Append($"[{soldier.State} at {soldier.transform.position:F1} stunned={soldier.Perception.IsStunned}] ");
            return text.ToString();
        }

        private Transform Point(Transform parent, Vector3 position)
        {
            var point = new GameObject("Domination").transform;
            point.SetParent(parent, true);
            point.position = position;
            return point;
        }

        private void OnShot(in ShotFiredEvent evt)
        {
            foreach (TacticalAIBrain soldier in soldiers)
            {
                if (evt.Shooter != soldier.transform) continue;
                Transform muzzle = soldier.Body.Muzzle;
                shots.Add(new Shot { Event = evt, Muzzle = muzzle.position, Bore = muzzle.forward, Time = Time.time });
                return;
            }
        }

        private void OnFlashbang(in FlashbangEvent evt) => flashbangs++;
    }
}
