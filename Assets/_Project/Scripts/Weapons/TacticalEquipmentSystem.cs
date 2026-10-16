using System;
using FPS.Core;
using FPS.Locomotion;
using UnityEngine;

namespace FPS.Weapons
{
    public enum EquipmentSlot
    {
        /// <summary>Fragmentation grenades (G / RB).</summary>
        Lethal,
        /// <summary>Stun or smoke grenades (H / LB).</summary>
        Tactical,
    }

    /// <summary>
    /// The player's throwables. Pressing a throw key lowers the rifle and pulls the pin; once the pin is out the spoon
    /// flies and the fuse burns while the key is held (cooking); releasing throws along the line of sight with a little
    /// loft plus the player's own velocity, the fuse already burnt carried over. Hold too long and it goes off in hand;
    /// a cooking grenade is dropped if the player is killed or disabled.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig), typeof(RealisticWeaponController))]
    public sealed class TacticalEquipmentSystem : MonoBehaviour
    {
        [Serializable]
        public struct Loadout
        {
            public GrenadeDataSO grenade;
            [Min(0)] public int count;
        }

        private enum Phase
        {
            Idle,
            PinPull,
            Cooking,
            Throwing,
            Recovering,
        }

        public const string LethalFieldName = nameof(lethal);
        public const string TacticalFieldName = nameof(tactical);

        [SerializeField] private Loadout lethal;
        [SerializeField] private Loadout tactical;
        [Tooltip("Pulling the pin before the spoon can fly (s).")]
        [SerializeField, Min(0f)] private float pinTime = 0.35f;
        [Tooltip("Arm swing from release to the grenade leaving the hand (s).")]
        [SerializeField, Min(0f)] private float releaseTime = 0.12f;
        [Tooltip("Time before the rifle comes back up after a throw (s).")]
        [SerializeField, Min(0f)] private float recoverTime = 0.35f;
        [Tooltip("Throw elevation above the line of sight (deg).")]
        [SerializeField, Range(0f, 45f)] private float throwElevation = 10f;
        [Tooltip("Release point relative to the camera (m).")]
        [SerializeField] private Vector3 releaseOffset = new Vector3(0.2f, -0.1f, 0.35f);

        private readonly int[] counts = new int[2];
        private PlayerRig rig;
        private RealisticWeaponController weapons;
        private RealisticLocomotionController locomotion;
        private InputReader input;
        private Phase phase;
        private float phaseStart, spoonTime;
        private bool releaseQueued;

        /// <summary>Raised when a grenade leaves the hand (thrown, dropped or cooked off).</summary>
        public event Action<EquipmentSlot, ThrownGrenade> Thrown;

        public bool IsBusy => phase != Phase.Idle;
        public bool IsCooking => phase == Phase.Cooking;
        public EquipmentSlot ActiveSlot { get; private set; }
        /// <summary>Fuse burnt so far by the grenade in hand (s).</summary>
        public float CookTime => phase == Phase.Cooking || phase == Phase.Throwing ? Time.time - spoonTime : 0f;
        public ThrownGrenade LastThrown { get; private set; }

        public int Count(EquipmentSlot slot) => counts[(int)slot];
        public GrenadeDataSO Grenade(EquipmentSlot slot) => slot == EquipmentSlot.Lethal ? lethal.grenade : tactical.grenade;

        public void SetLoadout(EquipmentSlot slot, GrenadeDataSO grenade, int count)
        {
            var loadout = new Loadout { grenade = grenade, count = Mathf.Max(0, count) };
            if (slot == EquipmentSlot.Lethal) lethal = loadout;
            else tactical = loadout;
            counts[(int)slot] = loadout.count;
        }

        /// <summary>Restocks both slots to their loadout counts.</summary>
        public void Refill()
        {
            counts[(int)EquipmentSlot.Lethal] = lethal.count;
            counts[(int)EquipmentSlot.Tactical] = tactical.count;
        }

        /// <summary>Lowers the rifle and pulls the pin. False when busy, out of that grenade or mid weapon swap.</summary>
        public bool BeginThrow(EquipmentSlot slot)
        {
            if (phase != Phase.Idle || Grenade(slot) == null || counts[(int)slot] <= 0 || !weapons.LowerForEquipment()) return false;
            ActiveSlot = slot;
            phase = Phase.PinPull;
            phaseStart = Time.time;
            releaseQueued = false;
            return true;
        }

        /// <summary>Lets go: throws once the pin is out (immediately if it already is).</summary>
        public void ReleaseThrow(EquipmentSlot slot)
        {
            if (slot != ActiveSlot) return;
            if (phase == Phase.PinPull) releaseQueued = true;
            else if (phase == Phase.Cooking) StartRelease();
        }

        private void Awake()
        {
            rig = GetComponent<PlayerRig>();
            weapons = GetComponent<RealisticWeaponController>();
            locomotion = GetComponent<RealisticLocomotionController>();
            input = rig.Input;
            Refill();
        }

        private void OnEnable()
        {
            if (input == null) return;
            input.LethalPressed += OnLethalPressed;
            input.LethalReleased += OnLethalReleased;
            input.TacticalPressed += OnTacticalPressed;
            input.TacticalReleased += OnTacticalReleased;
        }

        private void OnDisable()
        {
            if (input != null)
            {
                input.LethalPressed -= OnLethalPressed;
                input.LethalReleased -= OnLethalReleased;
                input.TacticalPressed -= OnTacticalPressed;
                input.TacticalReleased -= OnTacticalReleased;
            }

            // Cut down mid-throw: a live grenade falls where you stand; one with the pin still in stays in the pouch.
            if (phase == Phase.Cooking || phase == Phase.Throwing) Launch(Vector3.zero, Time.time - spoonTime);
            phase = Phase.Idle;
        }

        private void Update()
        {
            float now = Time.time;
            switch (phase)
            {
                case Phase.PinPull:
                    if (now < phaseStart + pinTime) return;
                    phase = Phase.Cooking;
                    spoonTime = now; // the spoon flies: the fuse is burning
                    if (releaseQueued) StartRelease();
                    return;

                case Phase.Cooking:
                    if (now - spoonTime >= Grenade(ActiveSlot).FuseTime) Launch(Vector3.zero, now - spoonTime); // held too long
                    return;

                case Phase.Throwing:
                    if (now < phaseStart + releaseTime) return;
                    Transform view = rig.PlayerCamera.transform;
                    Vector3 direction = Quaternion.AngleAxis(-throwElevation, view.right) * view.forward;
                    Launch(direction * Grenade(ActiveSlot).ThrowSpeed, now - spoonTime);
                    return;

                case Phase.Recovering:
                    if (now < phaseStart + recoverTime) return;
                    weapons.RaiseFromEquipment();
                    phase = Phase.Idle;
                    return;
            }
        }

        private void StartRelease()
        {
            phase = Phase.Throwing;
            phaseStart = Time.time;
        }

        private void Launch(Vector3 throwVelocity, float cooked)
        {
            GrenadeDataSO grenade = Grenade(ActiveSlot);
            Transform view = rig.PlayerCamera.transform;
            Vector3 origin = view.TransformPoint(releaseOffset);
            if (Physics.Linecast(view.position, origin, out RaycastHit wall, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore))
                origin = wall.point - (origin - view.position).normalized * 0.08f; // never through the wall we stand against

            Vector3 velocity = throwVelocity + (locomotion != null ? locomotion.Velocity : Vector3.zero);
            ThrownGrenade live = ObjectPoolManager.Instance.Spawn(grenade.Prefab, origin,
                velocity.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(velocity) : view.rotation);
            live.Throw(grenade, transform, velocity, cooked);
            counts[(int)ActiveSlot]--;
            LastThrown = live;
            phase = Phase.Recovering;
            phaseStart = Time.time;
            Thrown?.Invoke(ActiveSlot, live);
        }

        private void OnLethalPressed() => BeginThrow(EquipmentSlot.Lethal);
        private void OnLethalReleased() => ReleaseThrow(EquipmentSlot.Lethal);
        private void OnTacticalPressed() => BeginThrow(EquipmentSlot.Tactical);
        private void OnTacticalReleased() => ReleaseThrow(EquipmentSlot.Tactical);
    }
}
