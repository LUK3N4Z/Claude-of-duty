using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FPS.AI;
using FPS.Ballistics;
using FPS.Gameplay;
using FPS.Locomotion;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests
{
    /// <summary>
    /// The built Tactical Raid scene opens with the player unseen: standing still at the infiltration spawn, nobody in
    /// the garrison identifies them through the pre-round countdown and the first seconds of the live round.
    /// </summary>
    public class RaidOpeningTests : InputTestFixture
    {
        private const string RaidScene = "Assets/_Project/Scenes/Mode_Raid.unity";
        private Scene previousActive;
        private string profileFolder;

        public override void Setup()
        {
            base.Setup();
            profileFolder = Path.Combine(Application.temporaryCachePath, "raid_tests_" + Guid.NewGuid().ToString("N"));
            SaveProfileManager.DirectoryOverride = profileFolder; // the scene's player loads (and applies) a fresh profile
        }

        public override void TearDown()
        {
            SaveProfileManager.DirectoryOverride = null;
            if (Directory.Exists(profileFolder)) Directory.Delete(profileFolder, true);
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator UnloadRaid()
        {
            CombatDifficulty.Current = AIDifficulty.Regular;
            Scene raid = SceneManager.GetSceneByPath(RaidScene);
            if (!raid.isLoaded) yield break;
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            yield return SceneManager.UnloadSceneAsync(raid);
        }

        /// <summary>Every player start in the Raid: the match picks one of them.</summary>
        private static readonly int[] InfiltrationSpawns = { 0, 1, 2, 3, 4 };

        [UnityTest]
        public IEnumerator GarrisonDoesNotSpotThePlayerStandingAtSpawn([ValueSource(nameof(InfiltrationSpawns))] int spawnIndex)
        {
            previousActive = SceneManager.GetActiveScene();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(RaidScene, new LoadSceneParameters(LoadSceneMode.Additive));
            Scene raid = SceneManager.GetSceneByPath(RaidScene);
            SceneManager.SetActiveScene(raid);
            foreach (GameObject root in raid.GetRootGameObjects())
            {
                foreach (Light light in root.GetComponentsInChildren<Light>())
                {
                    if (light.type == LightType.Directional) AIIllumination.Sun = light; // not a stale sun from earlier tests
                }
            }

            // Leave only the start under test, before the director's first frame places everyone.
            var director = Object.FindAnyObjectByType<MatchDirector>();
            Assert.IsNotNull(director, "Raid scene has no match director");
            Assert.AreEqual(MatchPhase.Waiting, director.Phase, "the round began before the start could be chosen");
            var starts = new List<SpawnPoint>();
            foreach (SpawnPoint point in SpawnPoint.All)
            {
                if (point.Team == MatchDirector.PlayerTeam && point.Initial) starts.Add(point);
            }

            Assert.AreEqual(InfiltrationSpawns.Length, starts.Count, "player starts in the Raid");
            starts.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            for (int i = 0; i < starts.Count; i++) starts[i].enabled = i == spawnIndex;
            yield return null;
            CombatDifficulty.Current = AIDifficulty.Realism; // after the profile's settings: the sharpest eyes

            var rig = Object.FindAnyObjectByType<PlayerRig>();
            Assert.IsNotNull(rig, "Raid scene has no player");
            var health = rig.GetComponent<TacticalHealthAndArmorSystem>();
            TestWeapons.SetField(health, "maxHealth", 100000f); // keep watching even if someone opens fire
            health.ResetState();
            PerceivableTarget target = rig.GetComponent<PerceivableTarget>();
            Vector3 spawn = rig.transform.position;

            var report = new StringBuilder();
            var peak = new float[director.Combatants.Count];
            float deadline = Time.time + 10f;
            while (director.Phase != MatchPhase.Live && Time.time < deadline)
            {
                Watch(director, target, report, peak);
                yield return null;
            }

            Assert.AreEqual(MatchPhase.Live, director.Phase, "Raid never went live");
            float end = Time.time + 8f;
            while (Time.time < end)
            {
                Watch(director, target, report, peak);
                yield return null;
            }

            Debug.Log($"[Raid opening] player at {spawn}, peak garrison awareness: {string.Join(", ", peak)}");
            Assert.Less(Vector3.Distance(rig.transform.position, spawn), 0.5f, "the player moved off the spawn");
            Assert.AreEqual(0, report.Length, $"Player at the spawn {spawn} was identified:\n{report}");
        }

        /// <summary>Notes each soldier that identifies <paramref name="target"/> (once) and the highest awareness each reached.</summary>
        private static void Watch(MatchDirector director, PerceivableTarget target, StringBuilder report, float[] peak)
        {
            var combatants = director.Combatants;
            for (int i = 0; i < combatants.Count; i++)
            {
                Combatant soldier = combatants[i];
                if (soldier.Brain == null || !soldier.Alive) continue;
                if (!soldier.Brain.Perception.TryGetKnowledge(target, out AIPerceptionSensor.TargetKnowledge knowledge)) continue;
                bool first = knowledge.Identified && peak[i] < 1f;
                peak[i] = Mathf.Max(peak[i], knowledge.Awareness);
                if (!first) continue;
                Transform body = soldier.Root.transform;
                report.AppendLine($"soldier {i} at {body.position} yaw {body.eulerAngles.y:F0}, " +
                                  $"{Vector3.Distance(body.position, target.transform.position):F1} m, phase {director.Phase}, " +
                                  $"state {soldier.Brain.State}, t {Time.timeSinceLevelLoad:F1} s");
            }
        }
    }
}
