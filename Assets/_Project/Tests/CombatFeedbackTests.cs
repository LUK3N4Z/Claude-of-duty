using System.Collections;
using FPS.Ballistics;
using FPS.Core;
using FPS.Gameplay;
using FPS.Locomotion;
using FPS.UI;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    /// <summary>
    /// M16 hit confirmation: hits credited to their shooter through the ballistics, classified and confirmed by marker and
    /// tick; incoming damage pointed out by direction arcs.
    /// </summary>
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

        [Test]
        public void DamageDirection_BearingIsClockwiseFromTheBodyYaw_AndFadesOutOverASecondAndAHalf()
        {
            Vector3 eye = new Vector3(0f, 1.6f, 0f);
            Assert.AreEqual(0f, DamageDirectionIndicator.Bearing(eye, Vector3.forward, new Vector3(0f, 9f, 20f)), 1e-3f, "ahead, height ignored");
            Assert.AreEqual(90f, DamageDirectionIndicator.Bearing(eye, Vector3.forward, new Vector3(8f, 0f, 0f)), 1e-3f, "right");
            Assert.AreEqual(-90f, DamageDirectionIndicator.Bearing(eye, Vector3.forward, new Vector3(-8f, 0f, 0f)), 1e-3f, "left");
            Assert.AreEqual(180f, Mathf.Abs(DamageDirectionIndicator.Bearing(eye, Vector3.forward, new Vector3(0f, 0f, -8f))), 1e-3f, "behind");
            Assert.AreEqual(-45f, DamageDirectionIndicator.Bearing(eye, Vector3.right, new Vector3(5f, 0f, 5f)), 1e-3f, "turned right: ahead-left");
            Assert.AreEqual(0f, DamageDirectionIndicator.Bearing(eye, Vector3.forward, new Vector3(0f, 0.1f, 0f)), "underfoot: no bearing");

            Assert.AreEqual(1f, DamageDirectionIndicator.Fade(0f));
            Assert.AreEqual(1f, DamageDirectionIndicator.Fade(0.15f), "holds a moment");
            float previous = 1f;
            for (float age = 0.25f; age < DamageDirectionIndicator.Duration; age += 0.25f)
            {
                float alpha = DamageDirectionIndicator.Fade(age);
                Assert.Less(alpha, previous, $"fading at {age} s");
                previous = alpha;
            }

            Assert.AreEqual(0f, DamageDirectionIndicator.Fade(DamageDirectionIndicator.Duration));
            Assert.AreEqual(0f, DamageDirectionIndicator.Fade(5f));
        }

        [UnityTest]
        public IEnumerator DamageDirection_PointsAtTheShooter_AGrenadeAtItsBlast_AndTheSettingTurnsItOff()
        {
            var indicator = player.AddComponent<DamageDirectionIndicator>();
            var locomotion = player.GetComponent<RealisticLocomotionController>();
            var health = player.GetComponent<TacticalHealthAndArmorSystem>();
            Transform thorax = null;
            foreach (Hitbox hitbox in player.GetComponentsInChildren<Hitbox>())
            {
                if (hitbox.Zone == HitZone.Thorax) thorax = hitbox.transform;
            }

            yield return null;
            var rifleman = new GameObject("Rifleman").transform;
            created.Add(rifleman.gameObject);
            rifleman.position = thorax.position + Vector3.right * 8f;
            Fire(rifleman.position, thorax.position, rifleman, null);
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(1, indicator.VisibleCount, "hit from the right");
            Assert.AreEqual(90f, indicator.LastBearing, 1f);
            Assert.Greater(indicator.LastAlpha, 0.4f);

            locomotion.SetLook(90f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(0f, indicator.LastBearing, 1f, "turned to face him: the arc swings ahead");
            yield return new WaitForSeconds(DamageDirectionIndicator.Duration);
            Assert.AreEqual(0, indicator.VisibleCount, "faded out");

            locomotion.SetLook(0f, 0f);
            var thrower = new GameObject("Thrower").transform;
            created.Add(thrower.gameObject);
            thrower.position = new Vector3(12f, 0f, 0f);
            GrenadeDataSO frag = GrenadeData(GrenadeType.Fragmentation, 0.1f);
            ThrownGrenade grenade = ObjectPoolManager.Instance.Spawn(frag.Prefab, new Vector3(-2.5f, 0.1f, 0f), Quaternion.identity);
            grenade.Throw(frag, thrower, Vector3.zero);
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(grenade.HasDetonated);
            Assert.AreEqual(1, indicator.VisibleCount, "blast and fragments: one thrower, one arc");
            Assert.AreEqual(-90f, indicator.LastBearing, 5f, "toward the blast on the left, not the thrower on the right");

            indicator.Shown = false;
            Assert.AreEqual(0, indicator.VisibleCount, "off clears it");
            health.ApplyDamage(HitZone.Thorax, 5f, thorax.position, Vector3.left, thrower);
            Assert.AreEqual(0, indicator.VisibleCount, "and ignores hits");

            var menu = player.AddComponent<HDRPSettingsMenu>();
            yield return null;
            menu.Settings.damageIndicator = true;
            menu.Apply(menu.Settings);
            Assert.IsTrue(indicator.Shown, "the setting drives it");
            health.ApplyDamage(HitZone.Thorax, 5f, thorax.position, Vector3.left, thrower, thrower.position);
            Assert.AreEqual(1, indicator.VisibleCount);
            Assert.AreEqual(90f, indicator.LastBearing, 1f);
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
