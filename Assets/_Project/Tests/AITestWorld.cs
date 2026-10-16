using System.Collections.Generic;
using FPS.AI;
using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace FPS.Tests
{
    /// <summary>
    /// A sunlit floor, the player (team 0) at the origin facing +Z, and builders for walls, a dark alcove, bare
    /// observers, full soldiers, squads, grenades and a baked NavMesh.
    /// </summary>
    public abstract class AITestWorld : InputTestFixture
    {
        protected readonly List<Object> created = new List<Object>();
        protected readonly List<CalloutType> callouts = new List<CalloutType>();
        protected InputActionAsset actions;
        protected InputReader reader;
        protected Keyboard keyboard;
        protected GameObject player;
        protected PerceivableTarget playerTarget;
        protected Transform level;
        protected Light sun;

        public override void Setup()
        {
            base.Setup();
            TestInput.IgnoreFocus();
            keyboard = InputSystem.AddDevice<Keyboard>();
            reader = TestInput.CreateReader(out actions);

            level = new GameObject("Level").transform;
            created.Add(level.gameObject);
            Wall(new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f));

            var sunObject = new GameObject("Sun");
            created.Add(sunObject);
            sunObject.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            AIIllumination.Sun = sun;

            player = new GameObject("Player");
            created.Add(player);
            ViewmodelRig.Build(PlayerRig.Build(player, reader));
            playerTarget = player.AddComponent<PerceivableTarget>();
            var health = player.GetComponent<TacticalHealthAndArmorSystem>();
            TestWeapons.SetField(health, "maxHealth", 100000f); // the test target survives being shot at
            health.ResetState();
            Physics.SyncTransforms();

            callouts.Clear();
            EventBus<SquadCalloutEvent>.Subscribe(OnCallout);
        }

        public override void TearDown()
        {
            EventBus<SquadCalloutEvent>.Unsubscribe(OnCallout);
            AIIllumination.Sun = null;
            var simulation = Object.FindAnyObjectByType<BallisticsSimulationManager>();
            if (simulation != null) simulation.Clear(); // no stray rounds into the next test
            foreach (ObjectPoolManager pool in Object.FindObjectsByType<ObjectPoolManager>()) Object.DestroyImmediate(pool.gameObject);
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            Object.DestroyImmediate(reader);
            Object.DestroyImmediate(actions);
            base.TearDown();
        }

        protected Transform Wall(Vector3 position, Vector3 size)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.layer = GameLayers.WorldGeometry;
            wall.transform.SetParent(level, false);
            wall.transform.position = position;
            wall.transform.localScale = size;
            return wall.transform;
        }

        /// <summary>A roofed alcove open toward -Z around <paramref name="center"/>: sun and sky cannot reach inside.</summary>
        protected void DarkAlcove(Vector3 center)
        {
            Wall(center + new Vector3(0f, 2.3f, 0.3f), new Vector3(3.2f, 0.2f, 3.2f));
            Wall(center + new Vector3(0f, 1.2f, 1.5f), new Vector3(3f, 2.4f, 0.2f));
            Wall(center + new Vector3(-1.5f, 1.2f, 0.3f), new Vector3(0.2f, 2.4f, 2.6f));
            Wall(center + new Vector3(1.5f, 1.2f, 0.3f), new Vector3(0.2f, 2.4f, 2.6f));
        }

        protected void MovePlayer(Vector3 position, float yaw = 0f)
        {
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        protected void BakeNavMesh()
        {
            var surface = level.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = GameLayers.EnvironmentMask;
            surface.BuildNavMesh();
        }

        /// <summary>An eye with nothing but a perception sensor (team 1) at <paramref name="eyePosition"/>.</summary>
        protected AIPerceptionSensor Observer(Vector3 eyePosition, Vector3 forward)
        {
            var observer = new GameObject("Observer");
            observer.SetActive(false);
            created.Add(observer);
            observer.transform.SetPositionAndRotation(eyePosition, Quaternion.LookRotation(forward));
            AIPerceptionSensor sensor = observer.AddComponent<AIPerceptionSensor>();
            sensor.Configure(observer.transform, 1);
            observer.SetActive(true);
            return sensor;
        }

        protected SquadCoordinator Squad()
        {
            var squad = new GameObject("Squad");
            created.Add(squad);
            return squad.AddComponent<SquadCoordinator>();
        }

        /// <summary>A full soldier (body, perception, rifle, brain) standing at <paramref name="position"/> facing <paramref name="yaw"/>.</summary>
        protected TacticalAIBrain Soldier(Vector3 position, float yaw, SquadCoordinator squad = null, GrenadeDataSO frag = null, GrenadeDataSO flash = null,
            WeaponDataSO rifle = null, bool activate = true, int team = 1)
        {
            var root = new GameObject("Soldier") { layer = GameLayers.Player };
            root.SetActive(false);
            created.Add(root);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (squad != null) root.transform.SetParent(squad.transform, true);

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 1.8f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.1f;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.radius = 0.28f;
            capsule.height = 1.8f;
            root.AddComponent<TacticalHealthAndArmorSystem>();

            Transform legs = Bone(root.transform, "Legs", Vector3.zero);
            Transform pelvis = Bone(root.transform, "Pelvis", new Vector3(0f, 1f, 0f));
            Transform torso = Bone(pelvis, "Torso", Vector3.zero);
            Transform chest = Bone(torso, "Chest", new Vector3(0f, 0.35f, 0f));
            Transform head = Bone(torso, "Head", new Vector3(0f, 0.65f, 0f));
            Transform aim = Bone(torso, "AimPivot", new Vector3(0.16f, 0.47f, 0.06f));
            HitboxBox(chest, HitZone.Thorax, new Vector3(0.38f, 0.3f, 0.22f));
            HitboxBox(head, HitZone.Head, new Vector3(0.2f, 0.24f, 0.22f));

            root.AddComponent<AIBody>().Configure(pelvis, torso, chest, head, aim, legs, default, default);
            root.AddComponent<PerceivableTarget>().SetTeam(team);
            root.AddComponent<AIPerceptionSensor>().Configure(head, team);
            root.AddComponent<AIWeaponHandler>().Configure(TestWeapons.CreateTemplate(created), rifle != null ? rifle : RifleData(), new AttachmentDataSO[0], 3);
            TacticalAIBrain brain = root.AddComponent<TacticalAIBrain>();
            brain.Configure(null, frag, frag != null ? 1 : 0, flash, flash != null ? 1 : 0);
            if (!activate) return brain; // an inactive template: copies wake up when spawned
            root.SetActive(true);
            squad?.Register(brain);
            return brain;
        }

        protected WeaponDataSO RifleData()
        {
            var data = ScriptableObject.CreateInstance<WeaponDataSO>();
            var caliber = ScriptableObject.CreateInstance<CaliberDataSO>();
            created.Add(data);
            created.Add(caliber);
            TestWeapons.SetField(data, "caliber", caliber);
            TestWeapons.SetField(data, "fireModes", new[] { FireMode.Semi, FireMode.Auto });
            TestWeapons.SetField(data, "roundsPerMinute", 700f);
            TestWeapons.SetField(data, "magazineCapacity", 30);
            TestWeapons.SetField(data, "moaAccuracy", 2f);
            return data;
        }

        /// <summary>A grenade type whose pooled body is an inactive sphere template.</summary>
        protected GrenadeDataSO GrenadeData(GrenadeType type, float fuse)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = type + " Template";
            body.layer = GameLayers.Debris;
            body.transform.localScale = Vector3.one * 0.07f;
            body.SetActive(false);
            created.Add(body);
            body.AddComponent<Rigidbody>().isKinematic = true;
            ThrownGrenade grenade = body.AddComponent<ThrownGrenade>();

            var data = ScriptableObject.CreateInstance<GrenadeDataSO>();
            created.Add(data);
            TestWeapons.SetField(data, "type", type);
            TestWeapons.SetField(data, "prefab", grenade);
            TestWeapons.SetField(data, "fuseTime", fuse);
            if (type == GrenadeType.Fragmentation)
            {
                var fragment = ScriptableObject.CreateInstance<CaliberDataSO>();
                created.Add(fragment);
                TestWeapons.SetField(data, "fragmentCaliber", fragment);
                TestWeapons.SetField(data, "fragmentCount", 16);
            }

            return data;
        }

        protected int Count(CalloutType type)
        {
            int count = 0;
            foreach (CalloutType callout in callouts) count += callout == type ? 1 : 0;
            return count;
        }

        private void OnCallout(in SquadCalloutEvent evt) => callouts.Add(evt.Type);

        private static Transform Bone(Transform parent, string boneName, Vector3 position)
        {
            Transform bone = new GameObject(boneName).transform;
            bone.SetParent(parent, false);
            bone.localPosition = position;
            return bone;
        }

        private static void HitboxBox(Transform parent, HitZone zone, Vector3 size)
        {
            var box = new GameObject(zone.ToString()) { layer = GameLayers.Hitbox };
            box.transform.SetParent(parent, false);
            box.AddComponent<BoxCollider>().size = size;
            box.AddComponent<Hitbox>().Configure(zone, null);
        }
    }
}
