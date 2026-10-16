using System.Collections.Generic;
using FPS.Ballistics;
using FPS.Locomotion;
using FPS.Weapons;
using UnityEngine;
using UnityEngine.AI;

namespace FPS.AI
{
    /// <summary>
    /// Anything AI can see and hear: the player or a soldier. Exposes the points sight is tested against, how
    /// conspicuous the body is (stance, movement), how much noise it makes moving, and whether it carries a lit light.
    /// Reads the player rig or an <see cref="AIBody"/> on the same object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerceivableTarget : MonoBehaviour
    {
        private static readonly List<PerceivableTarget> all = new List<PerceivableTarget>(16);

        [Tooltip("Targets only perceive members of other teams as threats. Player = 0, opposing force = 1.")]
        [SerializeField] private int team;

        [Header("Noise")]
        [Tooltip("Footstep audibility radius per m/s of speed when standing (m).")]
        [SerializeField, Min(0f)] private float noisePerSpeed = 5f;
        [SerializeField, Range(0f, 1f)] private float crouchNoise = 0.5f;
        [SerializeField, Range(0f, 1f)] private float proneNoise = 0.3f;
        [SerializeField, Min(1f)] private float sprintNoise = 1.4f;

        [Header("Conspicuity")]
        [SerializeField, Range(0f, 1f)] private float crouchConspicuity = 0.65f;
        [SerializeField, Range(0f, 1f)] private float proneConspicuity = 0.35f;
        [Tooltip("Extra conspicuity per m/s: motion catches the eye.")]
        [SerializeField, Min(0f)] private float motionConspicuity = 0.12f;

        private PlayerRig rig;
        private StanceAndLeanController stance;
        private RealisticLocomotionController locomotion;
        private AIBody body;
        private NavMeshAgent agent;
        private TacticalHealthAndArmorSystem health;

        public static IReadOnlyList<PerceivableTarget> All => all;

        public int Team => team;
        public Transform Head { get; private set; }
        public Transform Chest { get; private set; }
        public Transform Pelvis { get; private set; }
        public bool IsAlive => health == null || !health.IsDead;
        public TacticalHealthAndArmorSystem Health => health;

        /// <summary>The ear/eye that hears near misses (player camera, soldier head).</summary>
        public Transform Listener => rig != null ? rig.PlayerCamera.transform : Head;

        public Vector3 Velocity
        {
            get
            {
                if (locomotion != null) return locomotion.Velocity;
                return agent != null && agent.enabled ? agent.velocity : Vector3.zero;
            }
        }

        /// <summary>Chest height reference point for aiming and last-known-position bookkeeping.</summary>
        public Vector3 AimPoint => Chest.position;

        /// <summary>How readily the body is noticed: stance, plus motion.</summary>
        public float Conspicuity
        {
            get
            {
                float shape = 1f;
                if (stance != null)
                {
                    if (stance.Current == Stance.Crouch) shape = crouchConspicuity;
                    else if (stance.Current == Stance.Prone) shape = proneConspicuity;
                }
                else if (body != null && body.IsCrouched)
                {
                    shape = crouchConspicuity;
                }

                return shape + motionConspicuity * new Vector2(Velocity.x, Velocity.z).magnitude;
            }
        }

        /// <summary>Radius within which footsteps are heard right now (m); 0 when still or airborne.</summary>
        public float NoiseRadius
        {
            get
            {
                float speed = new Vector2(Velocity.x, Velocity.z).magnitude;
                if (speed < 0.2f) return 0f;
                if (locomotion != null && !locomotion.IsGrounded) return 0f;

                float radius = speed * noisePerSpeed;
                if (stance != null)
                {
                    if (stance.Current == Stance.Crouch) radius *= crouchNoise;
                    else if (stance.Current == Stance.Prone) radius *= proneNoise;
                }
                else if (body != null && body.IsCrouched)
                {
                    radius *= crouchNoise;
                }

                if (locomotion != null && locomotion.IsSprinting) radius *= sprintNoise;
                return radius;
            }
        }

        /// <summary>A lit flashlight carried by this target, or null.</summary>
        public TacticalDevice LitLight
        {
            get
            {
                IReadOnlyList<TacticalDevice> lights = TacticalDevice.LitFlashlights;
                for (int i = 0; i < lights.Count; i++)
                {
                    if (lights[i].transform.IsChildOf(transform)) return lights[i];
                }

                return null;
            }
        }

        /// <summary>The registered target owning <paramref name="part"/> (a shooter root or any child), or null.</summary>
        public static PerceivableTarget Find(Transform part)
        {
            if (part == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (part == all[i].transform || part.IsChildOf(all[i].transform)) return all[i];
            }

            return null;
        }

        public void SetTeam(int value) => team = value;

        private void Awake()
        {
            TryGetComponent(out health);
            TryGetComponent(out agent);
            if (TryGetComponent(out rig))
            {
                TryGetComponent(out stance);
                TryGetComponent(out locomotion);
                Head = rig.HeadPivot;
                Chest = PlayerRig.EnsureChild(rig.LeanPivot, "Perception Chest");
                Chest.localPosition = new Vector3(0f, 0.36f, 0f);
                Pelvis = rig.BodyPivot;
            }
            else if (TryGetComponent(out body))
            {
                Head = body.Head;
                Chest = body.Chest;
                Pelvis = body.Pelvis;
            }
            else
            {
                Head = Chest = Pelvis = transform;
            }
        }

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable() => all.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => all.Clear();
    }
}
