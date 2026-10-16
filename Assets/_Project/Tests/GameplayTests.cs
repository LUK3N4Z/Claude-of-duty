using System.Collections;
using FPS.AI;
using FPS.Ballistics;
using FPS.Core;
using FPS.Gameplay;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class GameplayTests : AITestWorld
    {
        [UnityTest]
        public IEnumerator SpawnSelection_AvoidsEnemyLineOfSight()
        {
            var enemyRoot = new GameObject("Enemy");
            created.Add(enemyRoot);
            enemyRoot.transform.position = new Vector3(0f, 0f, 40f);
            var enemy = new Combatant { Root = enemyRoot, Team = 1, Health = enemyRoot.AddComponent<TacticalHealthAndArmorSystem>() };
            Wall(new Vector3(6f, 1.5f, 34f), new Vector3(0.3f, 3f, 8f));
            Physics.SyncTransforms();

            SpawnPoint open = SpawnAt(new Vector3(0f, 0f, 10f), 0, true);
            SpawnPoint hidden = SpawnAt(new Vector3(12f, 0f, 30f), 0, true);
            yield return null;
            var combatants = new[] { enemy };

            Assert.Less(SpawnSelector.Score(open, 0, combatants), SpawnSelector.Score(hidden, 0, combatants));
            Assert.AreSame(hidden, SpawnSelector.Best(0, combatants, true), "the spawn the enemy cannot see wins, though it is closer");
            hidden.LastUsed = Time.time;
            Assert.AreSame(open, SpawnSelector.Best(0, combatants, true), "a spawn just used is avoided");
        }

        [UnityTest]
        public IEnumerator Hardpoint_ScoresTheHolder_NotAContest_AndRotates()
        {
            Wall(new Vector3(0f, 1.5f, 26f), new Vector3(10f, 3f, 0.3f)); // keeps the enemy's spawn out of the player's sight
            BakeNavMesh();
            SpawnAt(Vector3.zero, 0, true);
            SpawnAt(new Vector3(0f, 0f, 30f), 1, true);
            ObjectiveZone first = ZoneAt("First", new Vector3(0f, 0f, 10f), 3f);
            ObjectiveZone second = ZoneAt("Second", new Vector3(-15f, 0f, 0f), 3f);
            TacticalAIBrain template = Soldier(Vector3.down * 50f, 0f, null, null, null, null, false);

            HardpointMode mode = null;
            MatchDirector director = Director(new[] { 0, 1 }, template.gameObject, go =>
            {
                mode = go.AddComponent<HardpointMode>();
                mode.Configure(new[] { first, second });
                TestWeapons.SetField(mode, "zoneDuration", 5f);
                TestWeapons.SetField(mode, "respawnDelay", 2f);
            });
            yield return WaitFor(() => director.Phase == MatchPhase.Live, 3f);
            playerTarget.enabled = false; // the enemy should hold, not hunt

            director.TeleportPlayer(new Vector3(0f, 0f, 10f), Quaternion.identity);
            yield return new WaitForSeconds(2.2f);
            Assert.AreEqual(MatchDirector.PlayerTeam, mode.Controller, "alone in the zone");
            Assert.GreaterOrEqual(director.Score(0), 2);

            Combatant enemy = director.Combatants[1];
            enemy.Brain.Body.Agent.Warp(new Vector3(1.5f, 0f, 10f));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(-2, mode.Controller, "both teams inside: contested");
            int frozen = director.Score(0);
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(frozen, director.Score(0), "nobody scores a contested zone");
            Assert.AreEqual(0, director.Score(1));

            yield return WaitFor(() => mode.ActiveZoneIndex == 1, 4f);
            Assert.AreSame(second, mode.ActiveZone, "the hardpoint moves on");
            Assert.AreEqual(ObjectiveKind.Hold, enemy.Brain.Objective);
            Assert.Less(Vector3.Distance(enemy.Brain.ObjectivePoint, second.Center), second.Radius, "squads are sent to the new zone");
        }

        [UnityTest]
        public IEnumerator SearchAndDestroy_Detonation_Defuse_Elimination()
        {
            BakeNavMesh();
            SpawnAt(Vector3.zero, 0, true);
            SpawnAt(new Vector3(0f, 0f, 24f), 1, true);
            ObjectiveZone site = ZoneAt("A", new Vector3(8f, 0f, 12f), 3f);
            HoldInteraction plant = Hold("Plant", new Vector3(8f, 0.3f, 12f));
            PlantedBomb bomb = BombTemplate();
            TacticalAIBrain template = Soldier(Vector3.down * 50f, 0f, null, null, null, null, false);

            SearchAndDestroyMode mode = null;
            MatchDirector director = Director(new[] { 0, 1 }, template.gameObject, go =>
            {
                mode = go.AddComponent<SearchAndDestroyMode>();
                mode.Configure(new[] { site }, new[] { plant }, bomb);
                TestWeapons.SetField(mode, "roundsToWin", 3);
                TestWeapons.SetField(mode, "swapAfter", 1);
                TestWeapons.SetField(mode, "bombFuse", 2f);
                TestWeapons.SetField(mode, "plantTime", 1f);
                TestWeapons.SetField(mode, "defuseTime", 1f);
                TestWeapons.SetField(mode, "timeLimit", 60f);
            });

            // Round 1: the player's team attacks; the charge goes off.
            yield return WaitFor(() => director.Phase == MatchPhase.Live, 3f);
            Assert.AreEqual(MatchDirector.PlayerTeam, mode.AttackingTeam);
            mode.Plant(0);
            Assert.IsNotNull(mode.Planted);
            Assert.IsTrue(mode.ClockStopped, "a planted charge stops the clock");
            yield return WaitFor(() => director.Phase == MatchPhase.RoundOver, 4f);
            Assert.AreEqual(1, director.RoundWins(0), "detonation: attackers win");

            // Round 2: sides swap. The soldier walks to the site and plants; the player defuses.
            yield return WaitFor(() => director.Phase == MatchPhase.Live && director.Round == 2, 3f);
            playerTarget.enabled = false; // unseen, so the soldier goes about the objective
            Assert.AreEqual(1, mode.AttackingTeam);
            yield return WaitFor(() => mode.Planted != null, 25f);
            Assert.IsNotNull(mode.Planted, "the soldier planted the charge");
            Assert.AreEqual(ObjectiveKind.Hold, director.Combatants[1].Brain.Objective, "and now guards it");
            float defused = 0f;
            while (director.Phase == MatchPhase.Live && defused < 10f)
            {
                mode.Planted.Defuse.Work(player, Time.deltaTime);
                defused += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(2, director.RoundWins(0), "defused: defenders win");

            // Round 3: the attackers are wiped out before they plant.
            yield return WaitFor(() => director.Phase == MatchPhase.Live && director.Round == 3, 3f);
            Combatant attacker = director.Combatants[1];
            attacker.Health.ApplyDamage(HitZone.Head, 1000f, attacker.Eye, Vector3.forward);
            yield return WaitFor(() => director.Phase != MatchPhase.Live, 1f);
            Assert.AreEqual(3, director.RoundWins(0), "elimination: defenders win");
            yield return WaitFor(() => director.Phase == MatchPhase.MatchOver, 2f);
            Assert.AreEqual(MatchDirector.PlayerTeam, director.Winner, "three rounds take the match");
        }

        [UnityTest]
        public IEnumerator Raid_ObjectivesOpenTheExtraction_HoldingOutWins()
        {
            SpawnAt(Vector3.zero, 0, true);
            HoldInteraction terminal = Hold("Intel", new Vector3(0f, 1.6f, 1.8f));
            terminal.Configure("Hold to download intel", 1f);
            ObjectiveZone lz = ZoneAt("LZ", new Vector3(10f, 0f, 10f), 3f);

            ExtractionMode mode = null;
            MatchDirector director = Director(new[] { 0, 0 }, null, go =>
            {
                mode = go.AddComponent<ExtractionMode>();
                mode.Configure(new[] { terminal }, new[] { "Download the intel" }, lz);
                TestWeapons.SetField(mode, "extractTime", 1f);
            });
            yield return WaitFor(() => director.Phase == MatchPhase.Live, 3f);
            Assert.IsFalse(mode.ExtractionOpen);

            director.TeleportPlayer(new Vector3(10f, 0f, 10f), Quaternion.identity);
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(MatchPhase.Live, director.Phase, "no extraction before the objectives");

            director.TeleportPlayer(Vector3.zero, Quaternion.identity);
            yield return null;
            Press(keyboard.fKey);
            yield return new WaitForSeconds(1.4f);
            Release(keyboard.fKey);
            Assert.AreEqual(1, mode.CompletedObjectives, "held Interact long enough on the terminal");
            Assert.IsTrue(mode.ExtractionOpen);

            director.TeleportPlayer(new Vector3(10f, 0f, 10f), Quaternion.identity);
            yield return WaitFor(() => director.Phase == MatchPhase.MatchOver, 2f);
            Assert.AreEqual(MatchDirector.PlayerTeam, director.Winner, "extracted");
        }

        [UnityTest]
        public IEnumerator Raid_PlayerKilled_FailsTheMission_AndLosesControl()
        {
            SpawnAt(Vector3.zero, 0, true);
            HoldInteraction terminal = Hold("Intel", new Vector3(0f, 1.6f, 1.8f));
            ObjectiveZone lz = ZoneAt("LZ", new Vector3(10f, 0f, 10f), 3f);
            ExtractionMode mode = null;
            MatchDirector director = Director(new[] { 0, 0 }, null, go =>
            {
                mode = go.AddComponent<ExtractionMode>();
                mode.Configure(new[] { terminal }, new[] { "Download the intel" }, lz);
            });
            yield return WaitFor(() => director.Phase == MatchPhase.Live, 3f);

            player.GetComponent<TacticalHealthAndArmorSystem>().ApplyDamage(HitZone.Head, 1e7f, Vector3.zero, Vector3.forward);
            yield return null;
            Assert.AreEqual(MatchPhase.MatchOver, director.Phase);
            Assert.AreEqual(1, director.Winner, "KIA: mission failed");
            Assert.IsFalse(player.GetComponent<RealisticWeaponController>().enabled, "the dead do not shoot");
        }

        [UnityTest]
        public IEnumerator PlayerDeath_ViewFallsToTheFloor_AndARespawnRestoresIt()
        {
            SpawnAt(Vector3.zero, 0, true);
            ObjectiveZone zone = ZoneAt("Far", new Vector3(0f, 0f, 40f), 3f);
            MatchDirector director = Director(new[] { 0, 0 }, null, go =>
            {
                var mode = go.AddComponent<HardpointMode>();
                mode.Configure(new[] { zone });
                TestWeapons.SetField(mode, "respawnDelay", 1.5f);
            });
            yield return WaitFor(() => director.Phase == MatchPhase.Live, 3f);

            Camera view = player.GetComponent<PlayerRig>().PlayerCamera;
            Transform viewmodelRoot = player.GetComponent<ViewmodelRig>().ViewmodelRoot;
            var death = player.GetComponent<DeathCamera>();
            float eyeHeight = view.transform.position.y;
            float fieldOfView = view.fieldOfView;
            Assert.Greater(eyeHeight, 1.2f, "standing");

            player.GetComponent<TacticalHealthAndArmorSystem>().ApplyDamage(HitZone.Head, 1e7f, view.transform.position, Vector3.back);
            Assert.IsTrue(death.IsActive, "the fall starts with the death");
            Assert.IsFalse(viewmodelRoot.gameObject.activeSelf, "weapon and arms hidden");
            yield return new WaitForSeconds(0.3f);
            float partway = view.transform.position.y;
            Assert.Less(partway, eyeHeight, "going down");
            Assert.Greater(partway, 0.5f, "not in one frame");

            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(1f, death.Progress);
            Assert.AreEqual(0.22f, view.transform.position.y, 0.05f, "on the floor");
            float roll = Mathf.Abs(Mathf.DeltaAngle(0f, view.transform.eulerAngles.z));
            Assert.That(roll, Is.InRange(15f, 45f), "rolled onto a side");
            Assert.AreEqual(fieldOfView, view.fieldOfView, 0.01f);
            Vector3 lying = view.transform.position;
            yield return new WaitForSeconds(0.2f);
            Assert.Less(Vector3.Distance(lying, view.transform.position), 1e-3f, "and stays down");
            Assert.IsFalse(player.GetComponent<RealisticWeaponController>().enabled);

            yield return WaitFor(() => !death.IsActive, 2f);
            Assert.IsFalse(player.GetComponent<TacticalHealthAndArmorSystem>().IsDead, "respawned");
            Assert.AreEqual(Vector3.zero, view.transform.localPosition);
            Assert.Less(Quaternion.Angle(Quaternion.identity, view.transform.localRotation), 0.01f, "view back on the rig");
            Assert.IsTrue(viewmodelRoot.gameObject.activeSelf, "weapon back");
            Assert.IsTrue(player.GetComponent<ProceduralADSAndOpticsController>().enabled);
            Assert.IsTrue(player.GetComponent<RealisticWeaponController>().enabled, "control back");
            yield return null;
            Assert.AreEqual(eyeHeight, view.transform.position.y, 0.1f, "standing at the spawn");
        }

        [UnityTest]
        public IEnumerator Squad_HoldsAnObjective_SpreadRoundIt()
        {
            BakeNavMesh();
            playerTarget.enabled = false;
            SquadCoordinator squad = Squad();
            TacticalAIBrain first = Soldier(new Vector3(-2f, 0f, 0f), 0f, squad);
            TacticalAIBrain second = Soldier(new Vector3(2f, 0f, 0f), 0f, squad);
            Vector3 point = new Vector3(6f, 0f, 14f);
            squad.SetObjective(ObjectiveKind.Hold, point, 4f, Vector3.forward);

            yield return WaitFor(() => first.AtObjective && second.AtObjective, 12f);
            Assert.AreEqual(AIState.Objective, first.State);
            Assert.Less(Vector3.Distance(first.transform.position, point), 4f);
            Assert.Less(Vector3.Distance(second.transform.position, point), 4f);
            Assert.Greater(Vector3.Distance(first.transform.position, second.transform.position), 1f, "spread out, not stacked");
        }

        private MatchDirector Director(int[] soldiers, GameObject opfor, System.Action<GameObject> addMode)
        {
            var match = new GameObject("Match");
            match.SetActive(false);
            created.Add(match);
            addMode(match);
            MatchDirector director = match.AddComponent<MatchDirector>();
            director.Configure(new[] { opfor, opfor }, soldiers);
            TestWeapons.SetField(director, "preRoundTime", 0.2f);
            TestWeapons.SetField(director, "roundOverTime", 0.4f);
            match.SetActive(true);
            return director;
        }

        private SpawnPoint SpawnAt(Vector3 position, int team, bool initial)
        {
            var spawn = new GameObject("Spawn");
            created.Add(spawn);
            spawn.transform.position = position;
            SpawnPoint point = spawn.AddComponent<SpawnPoint>();
            point.Configure(team, initial);
            return point;
        }

        private ObjectiveZone ZoneAt(string zoneName, Vector3 position, float radius)
        {
            var zone = new GameObject(zoneName);
            created.Add(zone);
            zone.transform.position = position;
            ObjectiveZone component = zone.AddComponent<ObjectiveZone>();
            component.Configure(zoneName, radius, 3f);
            return component;
        }

        private HoldInteraction Hold(string holdName, Vector3 position)
        {
            var spot = new GameObject(holdName) { layer = GameLayers.Interactable };
            created.Add(spot);
            spot.transform.position = position;
            var trigger = spot.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.9f, 0.7f, 0.9f);
            return spot.AddComponent<HoldInteraction>();
        }

        private PlantedBomb BombTemplate()
        {
            var bomb = new GameObject("Charge Template") { layer = GameLayers.Interactable };
            created.Add(bomb);
            bomb.transform.position = Vector3.down * 60f;
            bomb.AddComponent<BoxCollider>().isTrigger = true;
            bomb.AddComponent<HoldInteraction>();
            bomb.AddComponent<AudioSource>();
            PlantedBomb planted = bomb.AddComponent<PlantedBomb>();
            planted.Configure(GrenadeData(GrenadeType.Fragmentation, 0.1f), null);
            return planted;
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, float timeout)
        {
            float deadline = Time.time + timeout;
            while (!condition() && Time.time < deadline) yield return null;
        }
    }
}
