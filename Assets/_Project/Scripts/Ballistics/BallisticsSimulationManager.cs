using FPS.Core;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS.Ballistics
{
    /// <summary>
    /// Simulates every bullet in flight from its true muzzle position: gravity, G1/G7 drag against the wind, batched
    /// segment raycasts (one RaycastCommand job per frame), then penetration with entry/exit thickness probes, ricochets,
    /// hitbox damage and supersonic near-miss suppression. One per scene; created automatically when a scene has none.
    /// </summary>
    [DefaultExecutionOrder(10)] // after weapons fire, so new rounds fly the frame they are shot
    [DisallowMultipleComponent]
    public sealed class BallisticsSimulationManager : MonoBehaviour
    {
        private struct Projectile
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public CaliberDataSO Caliber;
            public Transform Shooter;
            public float Age;
            public int Interactions;
            public int NotifiedListeners;
        }

        private const int MaxInteractionsPerFrame = 4;
        private const float SurfaceOffset = 0.002f;

        [Header("Surfaces")]
        [Tooltip("Surface for colliders without a SurfaceIdentifier.")]
        [SerializeField] private SurfaceMaterialSO defaultSurface;
        [Tooltip("Surface for hitboxes without their own.")]
        [SerializeField] private SurfaceMaterialSO fleshSurface;

        [Header("Atmosphere")]
        [Tooltip("Wind velocity (m/s).")]
        [SerializeField] private Vector3 wind;
        [SerializeField, Min(0f)] private float airDensity = BallisticsMath.SeaLevelAirDensity;

        [Header("Simulation")]
        [SerializeField, Range(1f / 480f, 1f / 30f)] private float maxSubstep = 1f / 120f;
        [SerializeField, Min(16)] private int initialCapacity = 256;
        [SerializeField] private float maxLifetime = 6f;
        [Tooltip("Rounds slower than this are spent (m/s).")]
        [SerializeField] private float minSpeed = 80f;
        [SerializeField] private int maxInteractions = 8;

        [Header("Near Misses")]
        [Tooltip("Supersonic rounds passing this close to a listener suppress it (m).")]
        [SerializeField] private float nearMissRadius = 2f;

        private static BallisticsSimulationManager instance;

        private Projectile[] projectiles;
        private Vector3[] segmentStarts;
        private NativeArray<RaycastCommand> commands;
        private NativeArray<RaycastHit> results;
        private int count;
        private QueryParameters query;

        /// <summary>The scene's simulation, created on demand.</summary>
        public static BallisticsSimulationManager Instance => EnsureInstance();

        public int ActiveProjectiles => count;

        public Vector3 Wind
        {
            get => wind;
            set => wind = value;
        }

        public SurfaceMaterialSO DefaultSurface
        {
            get => defaultSurface;
            set => defaultSurface = value;
        }

        public SurfaceMaterialSO FleshSurface
        {
            get => fleshSurface;
            set => fleshSurface = value;
        }

        public static BallisticsSimulationManager EnsureInstance()
        {
            if (instance != null) return instance;
            instance = FindAnyObjectByType<BallisticsSimulationManager>();
            if (instance == null) new GameObject("[" + nameof(BallisticsSimulationManager) + "]").AddComponent<BallisticsSimulationManager>();
            return instance;
        }

        /// <summary>Drops every round in flight (round resets, test teardown).</summary>
        public void Clear() => count = 0;

        /// <summary>Adds a round to the simulation (normally via <see cref="ShotFiredEvent"/>).</summary>
        public void Launch(in ShotFiredEvent shot)
        {
            if (shot.Caliber == null || shot.MuzzleVelocity <= 0f) return;
            if (count == projectiles.Length) Grow();

            projectiles[count++] = new Projectile
            {
                Position = shot.Origin,
                Velocity = shot.Direction.normalized * shot.MuzzleVelocity,
                Caliber = shot.Caliber,
                Shooter = shot.Shooter,
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureInstance();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureInstance();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning($"Duplicate {nameof(BallisticsSimulationManager)} on '{name}' removed.", this);
                Destroy(this);
                return;
            }

            instance = this;
            if (defaultSurface == null) defaultSurface = ScriptableObject.CreateInstance<SurfaceMaterialSO>(); // concrete defaults
            if (fleshSurface == null) fleshSurface = defaultSurface;
            query = new QueryParameters(GameLayers.BallisticMask, false, QueryTriggerInteraction.Ignore, false);
            Allocate(initialCapacity);
        }

        private void OnEnable() => EventBus<ShotFiredEvent>.Subscribe(OnShotFired);

        private void OnDisable() => EventBus<ShotFiredEvent>.Unsubscribe(OnShotFired);

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (commands.IsCreated) commands.Dispose();
            if (results.IsCreated) results.Dispose();
        }

        private void OnShotFired(in ShotFiredEvent shot) => Launch(shot);

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || count == 0) return;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / maxSubstep), 1, 16);
            float h = dt / steps;
            for (int i = 0; i < count; i++)
            {
                ref Projectile p = ref projectiles[i];
                Vector3 start = segmentStarts[i] = p.Position;
                for (int s = 0; s < steps; s++) BallisticsMath.Step(ref p.Position, ref p.Velocity, p.Caliber, wind, airDensity, h);
                p.Age += dt;

                Vector3 travel = p.Position - start;
                float distance = travel.magnitude;
                commands[i] = new RaycastCommand(start, distance > 1e-6f ? travel / distance : Vector3.forward, query, distance);
            }

            RaycastCommand.ScheduleBatch(commands.GetSubArray(0, count), results.GetSubArray(0, count), 32, 1).Complete();

            for (int i = count - 1; i >= 0; i--)
            {
                if (!Resolve(i)) Remove(i);
            }
        }

        /// <summary>Applies this frame's hits to projectile <paramref name="index"/>; false when the round is spent.</summary>
        private bool Resolve(int index)
        {
            ref Projectile p = ref projectiles[index];
            Vector3 from = segmentStarts[index];
            Vector3 to = p.Position;
            RaycastHit hit = results[index];

            for (int guard = 0; hit.collider != null && guard < MaxInteractionsPerFrame; guard++)
            {
                CheckNearMisses(ref p, from, hit.point);
                Vector3 direction = (to - from).normalized;
                float speedBefore = p.Velocity.magnitude;
                float remaining = Vector3.Distance(hit.point, to);
                if (!Interact(ref p, hit, direction, out Vector3 resume)) return false;

                // Spend the rest of this frame's travel along the new path, scaled by the speed the hit left.
                Vector3 heading = p.Velocity.normalized;
                remaining *= p.Velocity.magnitude / Mathf.Max(1f, speedBefore);
                from = resume;
                to = resume + heading * remaining;
                p.Position = to;
                if (!Physics.Raycast(from, heading, out hit, remaining, query.layerMask, QueryTriggerInteraction.Ignore)) hit = default;
            }

            if (hit.collider == null) CheckNearMisses(ref p, from, to);
            return p.Age < maxLifetime && p.Velocity.sqrMagnitude > minSpeed * minSpeed && p.Interactions <= maxInteractions && p.Position.y > -1000f;
        }

        private bool Interact(ref Projectile p, RaycastHit hit, Vector3 direction, out Vector3 resume)
        {
            Collider collider = hit.collider;
            Vector3 velocity = p.Velocity;
            float speed = velocity.magnitude;

            if (p.Shooter != null && collider.transform.IsChildOf(p.Shooter))
            {
                resume = hit.point + direction * 0.01f; // the shooter's own body never stops their round
                return true;
            }

            p.Interactions++;
            SurfaceMaterialSO surface;
            if (collider.TryGetComponent(out Hitbox hitbox))
            {
                surface = hitbox.Surface != null ? hitbox.Surface : fleshSurface;
                if (hitbox.Owner != null && hitbox.Owner.ApplyBulletHit(hitbox.Zone, hit.point, direction, p.Caliber, speed))
                {
                    RaiseImpact(hit, velocity, ImpactOutcome.Stopped, surface, p.Caliber);
                    resume = hit.point;
                    return false;
                }
            }
            else
            {
                surface = SurfaceIdentifier.Resolve(collider, defaultSurface);
            }

            // Glancing impacts may skip off: the shallower the angle, the likelier.
            float incidence = 90f - Vector3.Angle(-direction, hit.normal);
            if (surface.RicochetAngle > 0f && incidence < surface.RicochetAngle && Random.value < 1f - incidence / surface.RicochetAngle)
            {
                Vector3 reflected = BallisticsMath.Deflect(Vector3.Reflect(direction, hit.normal), surface.RicochetSpread);
                if (Vector3.Dot(reflected, hit.normal) < 0.02f) reflected = (reflected + hit.normal * 0.05f).normalized;
                p.Velocity = reflected * (speed * surface.RicochetSpeedRetention);
                RaiseImpact(hit, velocity, ImpactOutcome.Ricocheted, surface, p.Caliber);
                resume = hit.point + hit.normal * SurfaceOffset;
                return true;
            }

            // Penetration: probe back from the deepest point the round could reach to measure the real thickness.
            float energy = p.Caliber.KineticEnergy(speed);
            float costPerMeter = BallisticsMath.PenetrationCostPerMeter(surface, p.Caliber);
            float probe = energy / costPerMeter + 0.01f;
            if (collider.Raycast(new Ray(hit.point + direction * probe, -direction), out RaycastHit exit, probe))
            {
                float residual = energy - costPerMeter * (probe - exit.distance);
                if (residual > 0f)
                {
                    float exitSpeed = Mathf.Sqrt(2f * residual / p.Caliber.BulletMassKg);
                    p.Velocity = BallisticsMath.Deflect(direction, surface.ExitDeflection) * exitSpeed;
                    RaiseImpact(hit, velocity, ImpactOutcome.Penetrated, surface, p.Caliber);
                    resume = exit.point + direction * SurfaceOffset;
                    return true;
                }
            }

            RaiseImpact(hit, velocity, ImpactOutcome.Stopped, surface, p.Caliber);
            resume = hit.point;
            return false;
        }

        private void CheckNearMisses(ref Projectile p, Vector3 a, Vector3 b)
        {
            int listeners = Mathf.Min(NearMissListeners.Count, 32);
            if (listeners == 0 || p.Velocity.sqrMagnitude < BallisticsMath.SpeedOfSound * BallisticsMath.SpeedOfSound) return;

            Vector3 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            for (int l = 0; l < listeners; l++)
            {
                int bit = 1 << l;
                if ((p.NotifiedListeners & bit) != 0 || NearMissListeners.GetOwner(l) == p.Shooter) continue;

                Transform listener = NearMissListeners.GetListener(l);
                Vector3 ear = listener.position;
                float t = lengthSquared > 1e-8f ? Mathf.Clamp01(Vector3.Dot(ear - a, segment) / lengthSquared) : 0f;
                if (t > 0.999f) continue; // still closing in: wait for the segment that passes the closest approach
                Vector3 closest = a + segment * t;
                float distance = Vector3.Distance(ear, closest);
                if (distance > nearMissRadius) continue;

                p.NotifiedListeners |= bit;
                EventBus<SuppressionEvent>.Raise(new SuppressionEvent
                {
                    Listener = listener,
                    Intensity = Mathf.Clamp01(1.15f - distance / nearMissRadius),
                    Point = closest,
                    Direction = p.Velocity.normalized,
                });
            }
        }

        private static void RaiseImpact(RaycastHit hit, Vector3 velocity, ImpactOutcome outcome, SurfaceMaterialSO surface, CaliberDataSO caliber)
        {
            EventBus<ProjectileImpactEvent>.Raise(new ProjectileImpactEvent
            {
                Point = hit.point,
                Normal = hit.normal,
                Velocity = velocity,
                Outcome = outcome,
                Surface = surface,
                Collider = hit.collider,
                Caliber = caliber,
            });
        }

        private void Remove(int index)
        {
            int last = --count;
            projectiles[index] = projectiles[last];
            segmentStarts[index] = segmentStarts[last];
            projectiles[last] = default; // release references
        }

        private void Allocate(int capacity)
        {
            projectiles = new Projectile[capacity];
            segmentStarts = new Vector3[capacity];
            commands = new NativeArray<RaycastCommand>(capacity, Allocator.Persistent);
            results = new NativeArray<RaycastHit>(capacity, Allocator.Persistent);
        }

        private void Grow()
        {
            // ponytail: doubles on overflow (one allocation); size initialCapacity to the expected peak to avoid it.
            Projectile[] oldProjectiles = projectiles;
            Vector3[] oldStarts = segmentStarts;
            commands.Dispose();
            results.Dispose();
            Allocate(oldProjectiles.Length * 2);
            System.Array.Copy(oldProjectiles, projectiles, count);
            System.Array.Copy(oldStarts, segmentStarts, count);
        }
    }
}
