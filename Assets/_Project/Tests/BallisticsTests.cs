using System.Collections;
using System.Collections.Generic;
using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class BallisticsTests
    {
        private readonly List<Object> created = new List<Object>();
        private readonly List<ImpactOutcome> outcomes = new List<ImpactOutcome>();
        private readonly List<SuppressionEvent> suppressions = new List<SuppressionEvent>();
        private BallisticsSimulationManager simulation;
        private SurfaceMaterialSO previousDefault, previousFlesh;
        private CaliberDataSO rifle;

        [SetUp]
        public void SetUp()
        {
            simulation = BallisticsSimulationManager.EnsureInstance();
            previousDefault = simulation.DefaultSurface;
            previousFlesh = simulation.FleshSurface;
            simulation.Wind = Vector3.zero;
            rifle = Make<CaliberDataSO>(); // 5.56 defaults: 4 g, 940 m/s, G7 0.151, penetration 40
            outcomes.Clear();
            suppressions.Clear();
            EventBus<ProjectileImpactEvent>.Subscribe(OnImpact);
            EventBus<SuppressionEvent>.Subscribe(OnSuppressed);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<ProjectileImpactEvent>.Unsubscribe(OnImpact);
            EventBus<SuppressionEvent>.Unsubscribe(OnSuppressed);
            simulation.DefaultSurface = previousDefault;
            simulation.FleshSurface = previousFlesh;
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
        }

        [Test]
        public void Trajectory_DropVelocityAndWindDrift_ArePlausibleFor556()
        {
            Assert.AreEqual(0.4043f, BallisticsMath.DragCoefficient(DragModel.G7, 1.05f), 1e-4f);
            Assert.AreEqual(0.6625f, BallisticsMath.DragCoefficient(DragModel.G1, 1.4f), 1e-4f);

            Vector3 position = Vector3.zero, velocity = new Vector3(0f, 0f, 940f);
            Vector3 crosswind = new Vector3(5f, 0f, 0f);
            while (position.z < 300f) BallisticsMath.Step(ref position, ref velocity, rifle, crosswind, BallisticsMath.SeaLevelAirDensity, 1f / 240f);

            Assert.That(-position.y, Is.InRange(0.55f, 0.95f), "drop at 300 m");
            Assert.That(velocity.magnitude, Is.InRange(620f, 790f), "remaining velocity at 300 m");
            Assert.That(position.x, Is.InRange(0.05f, 1.5f), "drifts downwind");
        }

        [Test]
        public void PenetrationBudget_MatchesMaterialIntuition()
        {
            float energy = rifle.KineticEnergy(900f);
            float concrete = energy / BallisticsMath.PenetrationCostPerMeter(Surface(2400f, 60f, 0f), rifle);
            float steel = energy / BallisticsMath.PenetrationCostPerMeter(Surface(7850f, 100f, 0f), rifle);
            float drywall = energy / BallisticsMath.PenetrationCostPerMeter(Surface(700f, 3f, 0f), rifle);
            Assert.That(concrete, Is.InRange(0.03f, 0.1f));
            Assert.That(steel, Is.InRange(0.005f, 0.02f));
            Assert.Greater(drywall, 1f);
        }

        [UnityTest]
        public IEnumerator Drywall_IsPenetrated_ThickConcrete_StopsTheRound()
        {
            Wall(new Vector3(0f, 1f, 5f), 0.03f, Surface(700f, 3f, 0f));
            Wall(new Vector3(0f, 1f, 10f), 0.5f, Surface(2400f, 60f, 0f));
            Physics.SyncTransforms();

            Fire(new Vector3(0f, 1f, 0f), Vector3.forward, 900f, rifle);
            yield return new WaitForSeconds(0.1f);

            CollectionAssert.AreEqual(new[] { ImpactOutcome.Penetrated, ImpactOutcome.Stopped }, outcomes);
            Assert.AreEqual(0, simulation.ActiveProjectiles);
        }

        [UnityTest]
        public IEnumerator GrazingImpactOnSteel_Ricochets()
        {
            Random.InitState(1234);
            Wall(new Vector3(0f, 0f, 20f), 0.05f, Surface(7850f, 100f, 60f), new Vector3(10f, 0.05f, 40f));
            Physics.SyncTransforms();

            Fire(new Vector3(0f, 0.5f, 0f), Quaternion.Euler(1f, 0f, 0f) * Vector3.forward, 900f, rifle); // 1° down
            yield return new WaitForSeconds(0.1f);

            Assert.AreEqual(ImpactOutcome.Ricocheted, outcomes[0]);
        }

        [UnityTest]
        public IEnumerator FrontPlate_StopsTheRound_UnarmoredBack_TakesFullDamage()
        {
            var dummy = new GameObject("Dummy");
            created.Add(dummy);
            var health = dummy.AddComponent<TacticalHealthAndArmorSystem>();
            ArmorPieceSO plate = Make<ArmorPieceSO>(); // front plate by default
            TestWeapons.SetField(plate, "protectionRating", 100f);
            TestWeapons.SetField(health, TacticalHealthAndArmorSystem.ArmorFieldName,
                new[] { new TacticalHealthAndArmorSystem.ArmorSlot { piece = plate, durability = plate.MaxDurability } });

            GameObject thorax = GameObject.CreatePrimitive(PrimitiveType.Cube);
            thorax.layer = GameLayers.Hitbox;
            thorax.transform.SetParent(dummy.transform, false);
            thorax.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            thorax.transform.localScale = new Vector3(0.38f, 0.32f, 0.22f);
            thorax.AddComponent<Hitbox>().Configure(HitZone.Thorax, Surface(1050f, 15f, 0f));
            Physics.SyncTransforms();

            Fire(new Vector3(0f, 1.3f, 5f), Vector3.back, 900f, rifle); // from the front
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(ImpactOutcome.Stopped, outcomes[0]);
            float blunt = 100f - health.Health;
            Assert.That(blunt, Is.InRange(1f, 12f), "only blunt trauma behind the plate");

            Fire(new Vector3(0f, 1.3f, -5f), Vector3.forward, 900f, rifle); // from behind: no back plate
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(ImpactOutcome.Penetrated, outcomes[1], "rifle round passes through the torso");
            Assert.Greater(100f - blunt - health.Health, 35f);
        }

        [UnityTest]
        public IEnumerator SupersonicNearMiss_Suppresses_SubsonicAndDistantDoNot()
        {
            var listener = new GameObject("Listener").transform;
            var owner = new GameObject("Owner").transform;
            created.Add(listener.gameObject);
            created.Add(owner.gameObject);
            listener.position = new Vector3(1f, 1f, 20f);
            NearMissListeners.Register(listener, owner);
            try
            {
                Fire(new Vector3(0f, 1f, 0f), Vector3.forward, 900f, rifle);
                Fire(new Vector3(6f, 1f, 0f), Vector3.forward, 900f, rifle);
                Fire(new Vector3(0.5f, 1f, 0f), Vector3.forward, 300f, rifle);
                yield return new WaitForSeconds(0.2f);

                Assert.AreEqual(1, suppressions.Count, "only the close supersonic round cracks");
                Assert.AreSame(listener, suppressions[0].Listener);
                Assert.That(suppressions[0].Intensity, Is.InRange(0.4f, 1f));
            }
            finally
            {
                NearMissListeners.Unregister(listener);
            }
        }

        [Test]
        public void LegHits_SlowSprinting_ArmHits_RegisterInjury()
        {
            var root = new GameObject("Player");
            created.Add(root);
            var health = root.AddComponent<TacticalHealthAndArmorSystem>();
            InputReader reader = TestInput.CreateReader(out InputActionAsset actions);
            created.Add(reader);
            created.Add(actions);
            PlayerRig.Build(root, reader);
            var locomotion = root.GetComponent<RealisticLocomotionController>();

            Assert.AreEqual(1f, locomotion.InjurySprintMultiplier);
            health.ApplyBulletHit(HitZone.LeftLeg, Vector3.zero, Vector3.forward, rifle, rifle.ReferenceVelocity);
            Assert.Less(locomotion.InjurySprintMultiplier, 0.8f);

            health.ApplyBulletHit(HitZone.RightArm, Vector3.zero, Vector3.forward, rifle, rifle.ReferenceVelocity);
            Assert.Greater(health.ArmInjury01, 0.4f);
        }

        private void Fire(Vector3 origin, Vector3 direction, float speed, CaliberDataSO caliber)
        {
            EventBus<ShotFiredEvent>.Raise(new ShotFiredEvent { Origin = origin, Direction = direction, MuzzleVelocity = speed, Caliber = caliber });
        }

        private void Wall(Vector3 position, float thickness, SurfaceMaterialSO surface, Vector3? size = null)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.layer = GameLayers.WorldGeometry;
            wall.transform.position = position;
            wall.transform.localScale = size ?? new Vector3(3f, 3f, thickness);
            wall.AddComponent<SurfaceIdentifier>().Surface = surface;
            created.Add(wall);
        }

        private SurfaceMaterialSO Surface(float density, float hardness, float ricochetAngle)
        {
            var surface = Make<SurfaceMaterialSO>();
            TestWeapons.SetField(surface, "densityKgM3", density);
            TestWeapons.SetField(surface, "hardness", hardness);
            TestWeapons.SetField(surface, "ricochetAngle", ricochetAngle);
            TestWeapons.SetField(surface, "exitDeflection", 0f);
            return surface;
        }

        private T Make<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            created.Add(asset);
            return asset;
        }

        private void OnImpact(in ProjectileImpactEvent evt) => outcomes.Add(evt.Outcome);

        private void OnSuppressed(in SuppressionEvent evt) => suppressions.Add(evt);
    }
}
