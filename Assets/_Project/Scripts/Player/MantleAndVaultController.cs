using FPS.Core;
using UnityEngine;

namespace FPS.Locomotion
{
    public enum TraversalKind
    {
        None,
        /// <summary>Over waist-high cover (0.5-1.1 m), one hand planted on top, landing on the far side.</summary>
        Vault,
        /// <summary>Up onto a ledge (to 2.1 m): hand on the edge, pull, step through.</summary>
        Mantle,
    }

    /// <summary>
    /// Context-sensitive traversal on Jump/Climb. Probes the obstacle ahead while the player moves into it (face, top
    /// height, depth, the ground behind it and capsule clearance along the way), classifies a vault or a mantle and
    /// solves the body path (rise before advancing, so the capsule never cuts the edge). It moves nothing itself: the
    /// locomotion controller, the root's only writer, asks <see cref="Step"/> for the body position each frame. Head
    /// motion reads the camera dip, the weapon its one-handed carry and the arms the ledge the support hand plants on.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(PlayerRig), typeof(StanceAndLeanController))]
    public sealed class MantleAndVaultController : MonoBehaviour
    {
        private static readonly float[] ProbeHeights = { 0.35f, 0.6f, 0.85f, 1.1f, 1.35f, 1.6f, 1.85f, 2.05f };

        [Header("Reach (m)")]
        [Tooltip("Lowest obstacle vaulted rather than stepped over.")]
        [SerializeField] private float minVaultHeight = 0.5f;
        [Tooltip("Tallest obstacle vaulted over; taller ones are mantled onto.")]
        [SerializeField] private float maxVaultHeight = 1.1f;
        [Tooltip("Tallest ledge the arms pull the body onto.")]
        [SerializeField] private float maxMantleHeight = 2.1f;
        [Tooltip("How far beyond the capsule an obstacle face is reached.")]
        [SerializeField] private float reach = 0.7f;
        [Tooltip("Thickest obstacle vaulted over in one motion.")]
        [SerializeField] private float maxVaultDepth = 1f;
        [Tooltip("Deepest drop accepted behind a vaulted obstacle.")]
        [SerializeField] private float maxVaultDrop = 1.5f;
        [Tooltip("The body clears the top by this much.")]
        [SerializeField] private float clearance = 0.12f;
        [Tooltip("Forward move input needed toward the obstacle.")]
        [SerializeField, Range(0f, 1f)] private float minForwardInput = 0.3f;

        [Header("Timing (s)")]
        [SerializeField] private float vaultTime = 0.55f;
        [SerializeField] private float mantleBaseTime = 0.45f;
        [Tooltip("Extra mantle time per metre of ledge height.")]
        [SerializeField] private float mantleTimePerMeter = 0.3f;
        [Tooltip("Forward speed carried out of a vault (m/s).")]
        [SerializeField] private float vaultExitSpeed = 2.4f;

        [Header("Presentation (deg)")]
        [Tooltip("Camera pitch dip at the hardest pull of a mantle.")]
        [SerializeField] private float mantleDip = 12f;
        [SerializeField] private float vaultDip = 5f;
        [SerializeField] private float vaultRoll = 7f;

        private readonly Vector3[] path = new Vector3[4];
        private CharacterController controller;
        private StanceAndLeanController stance;
        private InputReader input;
        private float duration, time;
        private bool crouchAtEnd;
        private Vector3 exitDirection;

        public TraversalKind Kind { get; private set; }
        public bool IsActive => Kind != TraversalKind.None;
        public float Progress01 => IsActive ? Mathf.Clamp01(time / duration) : 0f;
        /// <summary>Height of the obstacle top above the feet at the start (m).</summary>
        public float ObstacleHeight { get; private set; }
        /// <summary>Where the support hand plants (world): the top edge of the obstacle, left of centre.</summary>
        public Vector3 HandPoint { get; private set; }
        /// <summary>Support hand weight on <see cref="HandPoint"/>, 0-1.</summary>
        public float HandBlend { get; private set; }
        /// <summary>Weight of the weapon's one-handed side carry, 0-1.</summary>
        public float CarryBlend { get; private set; }
        /// <summary>Camera pitch dip (deg, down) and roll (deg) for head motion.</summary>
        public float CameraPitch { get; private set; }
        public float CameraRoll { get; private set; }
        /// <summary>Planar velocity to carry on once the traversal ends (m/s).</summary>
        public Vector3 ExitVelocity { get; private set; }

        /// <summary>
        /// What an obstacle of <paramref name="height"/> (m above the feet) and <paramref name="depth"/> (m) allows:
        /// low and thin with ground behind is a vault, anything else in reach a mantle, too low or too tall nothing.
        /// </summary>
        public static TraversalKind Classify(float height, float depth, bool landing, float minVault, float maxVault, float maxMantle, float maxDepth)
        {
            if (height < minVault || height > maxMantle) return TraversalKind.None;
            return height <= maxVault && depth <= maxDepth && landing ? TraversalKind.Vault : TraversalKind.Mantle;
        }

        /// <summary>Starts a vault or mantle over what is ahead, if anything fits. The body path is then fed by <see cref="Step"/>.</summary>
        public bool TryBegin()
        {
            if (IsActive || input == null || !enabled) return false;
            if (stance.Current == Stance.Prone || stance.IsProneTransition) return false;
            if (input.Move.y < minForwardInput) return false;

            Vector3 feet = transform.position;
            Vector3 forward = transform.forward;
            float radius = controller.radius;
            int mask = GameLayers.EnvironmentMask;

            // The face: the nearest head-on hit from knee to head height.
            float face = float.MaxValue;
            for (int i = 0; i < ProbeHeights.Length && ProbeHeights[i] <= maxMantleHeight; i++)
            {
                if (Physics.Raycast(feet + Vector3.up * ProbeHeights[i], forward, out RaycastHit hit, radius + reach, mask, QueryTriggerInteraction.Ignore) &&
                    hit.distance < face && Vector3.Dot(hit.normal, -forward) > 0.6f)
                    face = hit.distance;
            }

            if (face == float.MaxValue || !TopAt(feet + forward * (face + 0.15f), feet.y, out float height)) return false;

            // Depth: walk the top until it ends; then look for ground behind it.
            float depth = float.MaxValue;
            bool landing = false;
            float landingY = feet.y;
            if (height <= maxVaultHeight)
            {
                for (float along = 0.35f; along <= maxVaultDepth + 0.35f; along += 0.2f)
                {
                    if (TopAt(feet + forward * (face + along), feet.y, out float top) && Mathf.Abs(top - height) < 0.25f) continue;
                    depth = along - 0.1f;
                    break;
                }

                if (depth <= maxVaultDepth)
                {
                    Vector3 beyond = feet + forward * (face + depth + radius + 0.2f);
                    landing = Physics.Raycast(new Vector3(beyond.x, feet.y + height + 0.3f, beyond.z), Vector3.down, out RaycastHit ground,
                                  height + 0.3f + maxVaultDrop, mask, QueryTriggerInteraction.Ignore) && ground.normal.y > 0.7f &&
                              stance.FitsAt(stance.Current, ground.point);
                    landingY = landing ? ground.point.y : feet.y;
                }
            }

            TraversalKind kind = Classify(height, depth, landing, minVaultHeight, maxVaultHeight, maxMantleHeight, maxVaultDepth);
            if (kind == TraversalKind.None) return false;

            // Headroom above the start: the body has to rise there first.
            Vector3 raised = feet + Vector3.up * (height + clearance);
            if (!stance.FitsAt(Stance.Crouch, raised)) return false;

            float approach = Mathf.Max(0f, face - radius - 0.05f);
            if (kind == TraversalKind.Vault)
            {
                Vector3 overTop = feet + forward * (face + depth * 0.5f) + Vector3.up * (height + clearance);
                if (!stance.FitsAt(Stance.Crouch, overTop)) return false;

                path[0] = feet;
                path[1] = feet + forward * (face - radius * 0.3f) + Vector3.up * (height + clearance);
                path[2] = feet + forward * (face + depth + radius * 0.3f) + Vector3.up * (height + clearance);
                path[3] = new Vector3(feet.x, landingY, feet.z) + forward * (face + depth + radius + 0.2f);
                duration = vaultTime + 0.15f * depth;
                crouchAtEnd = false;
            }
            else
            {
                // Something to stand on beyond the edge: a thin tall wall is not a ledge.
                if (!TopAt(feet + forward * (face + radius + 0.15f), feet.y, out float support) || Mathf.Abs(support - height) > 0.15f) return false;
                Vector3 standPoint = feet + forward * (face + radius + 0.15f) + Vector3.up * support;
                crouchAtEnd = !stance.FitsAt(Stance.Stand, standPoint);
                if (crouchAtEnd && !stance.FitsAt(Stance.Crouch, standPoint)) return false;

                path[0] = feet;
                path[1] = feet + forward * approach + Vector3.up * (height + clearance);
                path[2] = standPoint;
                path[3] = standPoint;
                duration = mantleBaseTime + mantleTimePerMeter * height;
            }

            Kind = kind;
            ObstacleHeight = height;
            HandPoint = feet + forward * (face + 0.08f) + Vector3.up * height - transform.right * 0.22f;
            exitDirection = forward;
            ExitVelocity = Vector3.zero;
            time = 0f;
            return true;
        }

        /// <summary>
        /// Advances the traversal by <paramref name="dt"/> and returns where the feet belong now. Ends itself (and sets
        /// <see cref="ExitVelocity"/>) once the path is done.
        /// </summary>
        public Vector3 Step(float dt)
        {
            if (!IsActive) return transform.position;

            time += dt;
            float t = Mathf.Clamp01(time / duration);
            Vector3 position = Evaluate(t);

            float swing = Mathf.Sin(t * Mathf.PI);
            CameraPitch = (Kind == TraversalKind.Mantle ? mantleDip : vaultDip) * swing;
            CameraRoll = Kind == TraversalKind.Vault ? vaultRoll * swing : 0f;
            CarryBlend = Mathf.Clamp01(Mathf.Min(t / 0.15f, (1f - t) / 0.2f));
            HandBlend = Mathf.Clamp01(Mathf.Min(t / 0.12f, (0.8f - t) / 0.15f)); // plant early, let go before the end

            if (t >= 1f) Finish(true);
            return position;
        }

        /// <summary>Abandons a traversal in progress (death, teleport).</summary>
        public void Cancel()
        {
            if (IsActive) Finish(false);
        }

        private Vector3 Evaluate(float t)
        {
            if (Kind == TraversalKind.Mantle)
            {
                // Pull up in front of the face (up leads, forward lags), then step through onto the top.
                return t < 0.6f ? Segment(path[0], path[1], t / 0.6f) : Vector3.Lerp(path[1], path[2], SmootherStep((t - 0.6f) / 0.4f));
            }

            if (t < 0.3f) return Segment(path[0], path[1], t / 0.3f);
            if (t < 0.7f) return Vector3.Lerp(path[1], path[2], (t - 0.3f) / 0.4f);
            float s = (t - 0.7f) / 0.3f; // drop to the landing: forward leads, down follows
            Vector3 position = Vector3.Lerp(path[2], path[3], s);
            position.y = Mathf.Lerp(path[2].y, path[3].y, s * s);
            return position;
        }

        /// <summary>Rising segment: height eases out (fast first), the horizontal part eases in, so the body clears the edge.</summary>
        private static Vector3 Segment(Vector3 from, Vector3 to, float s)
        {
            float up = 1f - (1f - s) * (1f - s);
            float along = s * s;
            return new Vector3(Mathf.Lerp(from.x, to.x, along), Mathf.Lerp(from.y, to.y, up), Mathf.Lerp(from.z, to.z, along));
        }

        private static float SmootherStep(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x * (x * (6f * x - 15f) + 10f);
        }

        private void Finish(bool completed)
        {
            TraversalKind kind = Kind;
            Kind = TraversalKind.None;
            HandBlend = CarryBlend = CameraPitch = CameraRoll = 0f;
            ExitVelocity = completed && kind == TraversalKind.Vault ? exitDirection * vaultExitSpeed : Vector3.zero;
            if (completed && crouchAtEnd) stance.TrySetStance(Stance.Crouch);
        }

        /// <summary>Height of a walkable top at <paramref name="point"/> above <paramref name="feetY"/> (false if none in reach or roofed over).</summary>
        private bool TopAt(Vector3 point, float feetY, out float height)
        {
            height = 0f;
            const float probeRadius = 0.08f;
            var origin = new Vector3(point.x, feetY + maxMantleHeight + 0.45f, point.z);
            int mask = GameLayers.EnvironmentMask;
            if (Physics.CheckSphere(origin, probeRadius, mask, QueryTriggerInteraction.Ignore)) return false; // taller than any mantle
            if (!Physics.SphereCast(origin, probeRadius, Vector3.down, out RaycastHit hit, maxMantleHeight + 0.45f, mask, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.normal.y < 0.7f) return false;
            height = hit.point.y - feetY;
            return true;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            stance = GetComponent<StanceAndLeanController>();
            input = GetComponent<PlayerRig>().Input;
        }

        private void OnDisable() => Cancel();
    }
}
