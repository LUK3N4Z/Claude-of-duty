using System.Collections;
using FPS.Ballistics;
using FPS.Core;
using FPS.Gameplay;
using FPS.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    /// <summary>M16 hit confirmation: hits credited to their shooter through the ballistics, classified and confirmed by marker and tick.</summary>
    public class CombatFeedbackTests : AITestWorld
    {
        private HitMarkerFeedback feedback;
        private AudioClip flesh, armor, kill, headshot;
        private CaliberDataSO rifle;

        public override void Setup()
        {
            base.Setup();
            feedback = player.AddComponent<HitMarkerFeedback>();
            flesh = Clip("Flesh");
            armor = Clip("Armor");
            kill = Clip("Kill");
            headshot = Clip("Headshot");
            feedback.Configure(flesh, armor, kill, headshot);
            rifle = ScriptableObject.CreateInstance<CaliberDataSO>(); // 5.56 defaults
            created.Add(rifle);
        }

        [Test]
        public void Hits_ClassifyAsFleshArmorKillOrHeadshot()
        {
            var plate = ScriptableObject.CreateInstance<ArmorPieceSO>();
            created.Add(plate);
            Assert.AreEqual(HitMarkerKind.Flesh, HitMarkerFeedback.Classify(new DamageEvent { Zone = HitZone.Thorax }));
            Assert.AreEqual(HitMarkerKind.Armor, HitMarkerFeedback.Classify(new DamageEvent { Zone = HitZone.Thorax, Armor = plate }), "the plate stopped it");
            Assert.AreEqual(HitMarkerKind.Flesh, HitMarkerFeedback.Classify(new DamageEvent { Zone = HitZone.Thorax, Armor = plate, ArmorPenetrated = true }), "through the plate");
            Assert.AreEqual(HitMarkerKind.Kill, HitMarkerFeedback.Classify(new DamageEvent { Zone = HitZone.Stomach, Fatal = true }));
            Assert.AreEqual(HitMarkerKind.Headshot, HitMarkerFeedback.Classify(new DamageEvent { Zone = HitZone.Head, Fatal = true }));
        }

        [UnityTest]
        public IEnumerator OwnRounds_ConfirmFleshThenAHeadshotKill()
        {
            Dummy(new Vector3(0f, 0f, 6f), null);
            yield return null;

            Fire(new Vector3(0f, 1.3f, 1f), new Vector3(0f, 1.3f, 6f), player.transform, null);
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(1, feedback.Confirmations, "the hit is credited to the player");
            Assert.AreEqual(HitMarkerKind.Flesh, feedback.LastKind);
            Assert.AreSame(flesh, feedback.LastClip);
            Assert.Greater(feedback.MarkerAlpha, 0.5f, "marker up");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(0f, feedback.MarkerAlpha, "and gone");

            Fire(new Vector3(0f, 1.72f, 1f), new Vector3(0f, 1.72f, 6f), player.transform, null);
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(HitMarkerKind.Headshot, feedback.LastKind, "a head shot downs him");
            Assert.AreSame(headshot, feedback.LastClip);
        }

        [UnityTest]
        public IEnumerator APlateStoppingTheRound_Cracks_AndFragmentsCreditTheThrower()
        {
            var plate = ScriptableObject.CreateInstance<ArmorPieceSO>(); // front plate
            created.Add(plate);
            TestWeapons.SetField(plate, "protectionRating", 200f);
            Dummy(new Vector3(0f, 0f, 6f), plate);
            yield return null;

            Fire(new Vector3(0f, 1.3f, 1f), new Vector3(0f, 1.3f, 6f), player.transform, null);
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(HitMarkerKind.Armor, feedback.LastKind, "plate crack");
            Assert.AreSame(armor, feedback.LastClip);

            Fire(new Vector3(0f, 0.9f, 1f), new Vector3(0f, 0.9f, 6f), null, player.transform); // a fragment: no shooter, credited
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(2, feedback.Confirmations, "grenade fragments confirm for the thrower");
        }

        [UnityTest]
        public IEnumerator OthersHits_AndTheModes_DecideWhatShows()
        {
            var target = new GameObject("Target").AddComponent<TacticalHealthAndArmorSystem>();
            created.Add(target.gameObject);
            yield return null;

            target.ApplyDamage(HitZone.Thorax, 10f, Vector3.zero, Vector3.forward, sun.transform);
            Assert.AreEqual(0, feedback.Confirmations, "someone else's hit");

            feedback.Mode = HitMarkerMode.AudioOnly;
            target.ApplyDamage(HitZone.Thorax, 10f, Vector3.zero, Vector3.forward, player.transform);
            Assert.AreSame(flesh, feedback.LastClip);
            Assert.AreEqual(0f, feedback.MarkerAlpha, "ticks only");

            feedback.Mode = HitMarkerMode.VisualOnly;
            target.ApplyDamage(HitZone.Thorax, 10f, Vector3.zero, Vector3.forward, player.transform);
            Assert.IsNull(feedback.LastClip, "markers only");
            Assert.Greater(feedback.MarkerAlpha, 0.5f);

            feedback.Mode = HitMarkerMode.Off;
            int before = feedback.Confirmations;
            target.ApplyDamage(HitZone.Thorax, 10f, Vector3.zero, Vector3.forward, player.transform);
            Assert.AreEqual(before, feedback.Confirmations, "off is off");

            var menu = player.AddComponent<HDRPSettingsMenu>();
            yield return null;
            menu.Settings.hitMarkers = HitMarkerMode.AudioAndVisual;
            menu.Apply(menu.Settings);
            Assert.AreEqual(HitMarkerMode.AudioAndVisual, feedback.Mode, "the setting drives it");
        }

        private AudioClip Clip(string clipName)
        {
            AudioClip clip = AudioClip.Create(clipName, 480, 1, 48000, false);
            created.Add(clip);
            return clip;
        }

        /// <summary>A body with a thorax and a head hitbox (and <paramref name="frontPlate"/> on the chest), facing -Z.</summary>
        private void Dummy(Vector3 position, ArmorPieceSO frontPlate)
        {
            var dummy = new GameObject("Dummy");
            created.Add(dummy);
            dummy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));
            var health = dummy.AddComponent<TacticalHealthAndArmorSystem>();
            if (frontPlate != null)
                TestWeapons.SetField(health, TacticalHealthAndArmorSystem.ArmorFieldName,
                    new[] { new TacticalHealthAndArmorSystem.ArmorSlot { piece = frontPlate, durability = frontPlate.MaxDurability } });
            var tissue = ScriptableObject.CreateInstance<SurfaceMaterialSO>();
            created.Add(tissue);
            Box(dummy.transform, HitZone.Thorax, new Vector3(0f, 1.3f, 0f), new Vector3(0.38f, 0.32f, 0.22f), tissue);
            Box(dummy.transform, HitZone.Stomach, new Vector3(0f, 0.95f, 0f), new Vector3(0.34f, 0.3f, 0.22f), tissue);
            Box(dummy.transform, HitZone.Head, new Vector3(0f, 1.72f, 0f), new Vector3(0.2f, 0.24f, 0.22f), tissue);
            Physics.SyncTransforms();
        }

        private static void Box(Transform parent, HitZone zone, Vector3 position, Vector3 size, SurfaceMaterialSO tissue)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.layer = GameLayers.Hitbox;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            box.AddComponent<Hitbox>().Configure(zone, tissue);
        }

        private void Fire(Vector3 from, Vector3 to, Transform shooter, Transform attacker) =>
            EventBus<ShotFiredEvent>.Raise(new ShotFiredEvent
            {
                Origin = from,
                Direction = (to - from).normalized,
                MuzzleVelocity = 900f,
                Caliber = rifle,
                Shooter = shooter,
                Attacker = attacker,
            });
    }
}
