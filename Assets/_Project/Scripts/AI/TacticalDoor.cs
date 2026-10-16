using FPS.Core;
using FPS.Locomotion;
using UnityEngine;
using UnityEngine.AI;

namespace FPS.AI
{
    /// <summary>
    /// A hinged CQB door that swings both ways, always away from whoever moves it. Tap Interact to open or close it; hold
    /// Interact and scroll to crack it ajar a few degrees at a time and peek; bash it (V facing it, or sprint into it) and
    /// it slams open, stunning soldiers on the far side in its swing. Soldiers walking up to a closed door push it open.
    /// The leaf is solid world geometry (blocks bodies and rounds, penetrable wood); a gentle swing stops against a body.
    /// Lives on the doorway (its Interactable trigger fills the frame, so an open door is still in reach; +Z is the door's
    /// facing) and is the only writer of the hinge (local +X along the leaf).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TacticalDoor : MonoBehaviour, IInteractable, IBashable, IScrollAdjustable
    {
        private const string OpenText = "Open door", CloseText = "Close door";
        private static readonly Collider[] Overlaps = new Collider[16];

        [Tooltip("The swinging transform: leaf along its +X, rotated about its +Y.")]
        [SerializeField] private Transform hinge;
        [Tooltip("Leaf size: width along +X from the hinge, height, thickness (m).")]
        [SerializeField] private Vector3 leafSize = new Vector3(0.95f, 2.05f, 0.045f);
        [Tooltip("Fully open, either way (deg).")]
        [SerializeField] private float openAngle = 100f;
        [Tooltip("Widest crack while peeking (deg).")]
        [SerializeField] private float maxCrack = 35f;
        [Tooltip("Crack per scroll step (deg).")]
        [SerializeField] private float crackStep = 6f;
        [Tooltip("Swing spring of a hand-moved door (Hz) and its damping ratio.")]
        [SerializeField] private float swingFrequency = 1.6f;
        [SerializeField, Range(0.1f, 2f)] private float swingDamping = 0.9f;
        [Tooltip("Angular speed of a full-strength bash (deg/s).")]
        [SerializeField] private float bashSpeed = 720f;
        [Tooltip("Hinge friction slowing a bashed leaf (1/s).")]
        [SerializeField] private float bashFriction = 2.5f;
        [Tooltip("How long a soldier hit by a bashed door is stunned (s).")]
        [SerializeField] private float bashStun = 2.5f;
        [Tooltip("Soldiers this close to the closed leaf walk it open (m).")]
        [SerializeField] private float autoOpenRadius = 1.3f;
        [SerializeField] private AudioClip openClip;
        [SerializeField] private AudioClip closeClip;
        [SerializeField] private AudioClip bashClip;

        private Quaternion closedRotation;
        private AudioSource audioSource;
        private float angle, velocity, target, nextAutoCheck;
        private bool bashing, pendingToggle, cracking;
        private InputReader pendingInput;
        private Transform pendingUser;

        /// <summary>Signed opening angle (deg): positive swings the leaf toward -Z.</summary>
        public float Angle => angle;
        public float TargetAngle => target;
        public bool IsOpen => Mathf.Abs(angle) > 5f;
        public bool IsBashing => bashing;
        public int Bashes { get; private set; }
        public string Prompt => Mathf.Abs(target) > 5f ? CloseText : OpenText;

        /// <summary>Opening angle of <paramref name="magnitude"/> that swings away from someone at <paramref name="position"/>.</summary>
        public float AwayFrom(Vector3 position, float magnitude) =>
            Vector3.Dot(position - transform.position, transform.forward) >= 0f ? magnitude : -magnitude;

        /// <summary>Builder setup: the hinge, leaf dimensions and sounds.</summary>
        public void Configure(Transform hingeTransform, Vector3 size, AudioClip open, AudioClip close, AudioClip bash)
        {
            hinge = hingeTransform;
            leafSize = size;
            openClip = open;
            closeClip = close;
            bashClip = bash;
        }

        public bool CanInteract(GameObject user) => !bashing;

        /// <summary>
        /// A player's press waits for the release: a tap toggles the door, scrolling while held cracks it instead. Anyone
        /// without player input (scripts, soldiers) toggles at once.
        /// </summary>
        public void Interact(GameObject user)
        {
            pendingUser = user.transform;
            pendingInput = user.TryGetComponent(out PlayerRig rig) ? rig.Input : null;
            cracking = false;
            pendingToggle = pendingInput != null;
            if (!pendingToggle) Toggle(pendingUser.position);
        }

        public void Scroll(GameObject user, int direction)
        {
            cracking = true;
            float side = Mathf.Abs(target) > 0.5f ? Mathf.Sign(target) : Mathf.Sign(AwayFrom(user.transform.position, 1f));
            target = side * Mathf.Clamp(Mathf.Abs(target) + direction * crackStep, 0f, maxCrack);
            bashing = false;
        }

        public bool Bash(GameObject user, Vector3 direction, float strength)
        {
            if (bashing) return false;
            float side = Mathf.Sign(AwayFrom(user.transform.position, 1f));
            target = side * openAngle;
            velocity = side * bashSpeed * Mathf.Clamp(strength, 0.4f, 1f);
            bashing = true;
            pendingToggle = false;
            Bashes++;
            StunBehind(side);
            Play(bashClip, 1f);
            return true;
        }

        private void Awake()
        {
            if (hinge == null) hinge = transform;
            closedRotation = hinge.localRotation;
            audioSource = hinge.gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.dopplerLevel = 0f;
            audioSource.maxDistance = 40f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (pendingToggle && (pendingInput == null || !pendingInput.InteractHeld))
            {
                pendingToggle = false;
                if (!cracking && pendingUser != null) Toggle(pendingUser.position);
            }

            if (Time.time >= nextAutoCheck)
            {
                nextAutoCheck = Time.time + 0.2f;
                OpenForApproachingSoldier();
            }

            Swing(dt);
        }

        private void Toggle(Vector3 from)
        {
            bool close = Mathf.Abs(target) > 5f;
            target = close ? 0f : AwayFrom(from, openAngle);
            bashing = false;
            Play(close ? closeClip : openClip, 0.7f);
        }

        private void Swing(float dt)
        {
            float next;
            if (bashing)
            {
                velocity *= Mathf.Exp(-bashFriction * dt);
                next = angle + velocity * dt;
                if (Mathf.Abs(velocity) < 30f || Mathf.Abs(next) >= openAngle) bashing = false;
            }
            else
            {
                float w = 2f * Mathf.PI * swingFrequency;
                velocity += (w * w * (target - angle) - 2f * swingDamping * w * velocity) * dt;
                next = angle + velocity * dt;
            }

            if (Mathf.Abs(next) > openAngle)
            {
                next = Mathf.Sign(next) * openAngle; // door stop: a bashed leaf bounces off it
                velocity *= -0.25f;
            }

            if (Mathf.Abs(next - angle) < 1e-4f) return;
            if (!bashing && Blocked(next))
            {
                velocity = 0f; // a hand-moved door stops against a body
                return;
            }

            angle = next;
            hinge.localRotation = closedRotation * Quaternion.Euler(0f, angle, 0f);
        }

        private bool Blocked(float candidate)
        {
            Quaternion rotation = Parent() * closedRotation * Quaternion.Euler(0f, candidate, 0f);
            Vector3 center = hinge.position + rotation * new Vector3(leafSize.x * 0.5f, leafSize.y * 0.5f, 0f);
            Vector3 extents = new Vector3(leafSize.x * 0.45f, leafSize.y * 0.45f, leafSize.z * 0.5f + 0.02f);
            return Physics.OverlapBoxNonAlloc(center, extents, Overlaps, rotation, 1 << GameLayers.Player, QueryTriggerInteraction.Ignore) > 0;
        }

        /// <summary>Stuns soldiers in the bashed leaf's sweep on the <paramref name="side"/> it swings to.</summary>
        private void StunBehind(float side)
        {
            Quaternion swung = Parent() * closedRotation * Quaternion.Euler(0f, side * 50f, 0f);
            Vector3 sweep = hinge.position + swung * new Vector3(leafSize.x * 0.55f, 1f, 0f);
            int count = Physics.OverlapSphereNonAlloc(sweep, leafSize.x * 0.75f, Overlaps, 1 << GameLayers.Player, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                AIPerceptionSensor soldier = Overlaps[i].GetComponentInParent<AIPerceptionSensor>();
                if (soldier != null) soldier.Stun(bashStun);
            }
        }

        private void OpenForApproachingSoldier()
        {
            if (Mathf.Abs(target) > 0.5f || bashing) return;
            Vector3 leafCenter = hinge.position + Parent() * closedRotation * new Vector3(leafSize.x * 0.5f, 1f, 0f);
            int count = Physics.OverlapSphereNonAlloc(leafCenter, autoOpenRadius, Overlaps, 1 << GameLayers.Player, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                NavMeshAgent agent = Overlaps[i].GetComponentInParent<NavMeshAgent>();
                if (agent == null || !agent.enabled || agent.velocity.sqrMagnitude < 0.04f) continue;
                if (Vector3.Dot(agent.velocity, leafCenter - agent.transform.position) <= 0f) continue; // walking away from it
                target = AwayFrom(agent.transform.position, openAngle);
                Play(openClip, 0.6f);
                return;
            }
        }

        private Quaternion Parent() => hinge.parent != null ? hinge.parent.rotation : Quaternion.identity;

        private void Play(AudioClip clip, float volume)
        {
            if (clip != null) audioSource.PlayOneShot(clip, volume);
        }
    }
}
