using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>What the mechanism needs from its owner each frame.</summary>
    public struct MechanismState
    {
        public bool TriggerPressed;
        public int FireMode;
        /// <summary>Seconds since the last round fired.</summary>
        public float SinceShot;
        /// <summary>Cyclic period (s): 60 / RPM.</summary>
        public float CycleTime;
        /// <summary>Bolt or slide held open on an empty magazine.</summary>
        public bool LockedBack;
        /// <summary>Charging progress 0-1 (0 when not charging).</summary>
        public float Charge;
        /// <summary>Bolt-action manipulation progress 0-1 (0 when idle).</summary>
        public float BoltWork;
    }

    /// <summary>
    /// Animates a weapon model's moving parts: trigger pull, fire-selector position, the bolt carrier or slide snapping
    /// back after each shot (and staying back when locked open), the charging handle during a charge (or riding with the
    /// carrier on SCAR-style actions), and a bolt-action handle that lifts, draws back, returns and drops. Writes only
    /// these part transforms, as offsets from their authored rest poses. Any part may be left unassigned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponMechanism : MonoBehaviour
    {
        [SerializeField] private Transform trigger;
        [Tooltip("Trigger rotation about its local X at full pull (deg).")]
        [SerializeField] private float triggerPull = -14f;
        [SerializeField] private Transform selector;
        [Tooltip("Selector rotation about its local X for each fire-mode index (deg).")]
        [SerializeField] private float[] selectorAngles = { 90f, 180f };
        [SerializeField] private Transform reciprocating;
        [Tooltip("Bolt carrier or slide travel at full recoil (m, local).")]
        [SerializeField] private Vector3 reciprocatingTravel = new Vector3(0f, 0f, -0.03f);
        [Tooltip("Fraction of the cyclic period the carrier spends travelling back and returning.")]
        [SerializeField, Range(0.1f, 1f)] private float cycleFraction = 0.7f;
        [SerializeField] private Transform chargingHandle;
        [Tooltip("Charging handle travel at full draw (m, local).")]
        [SerializeField] private Vector3 chargingTravel = new Vector3(0f, 0f, -0.06f);
        [Tooltip("The handle is fixed to the carrier and cycles with every shot (SCAR-pattern).")]
        [SerializeField] private bool handleReciprocates;
        [SerializeField] private Transform boltHandle;
        [Tooltip("Bolt handle lift about its local Z (deg) and draw (m, local).")]
        [SerializeField] private float boltLift = 65f;
        [SerializeField] private Vector3 boltTravel = new Vector3(0f, 0f, -0.08f);
        [Tooltip("Trigger and selector response (1/s).")]
        [SerializeField, Min(1f)] private float response = 30f;

        private Quaternion triggerRest = Quaternion.identity, selectorRest = Quaternion.identity, boltRest = Quaternion.identity;
        private Vector3 reciprocatingRest, chargingRest, boltPositionRest;
        private float triggerBlend, selectorAngle;
        private bool captured;

        public Transform Reciprocating => reciprocating;
        public Transform Trigger => trigger;
        /// <summary>Current carrier/slide travel 0 (forward) to 1 (fully back).</summary>
        public float CarrierTravel { get; private set; }

        public void Configure(Transform triggerPart, Transform selectorPart, Transform reciprocatingPart, Vector3 travel, Transform chargingPart,
            Vector3 chargeTravel, bool chargeReciprocates, Transform boltPart)
        {
            trigger = triggerPart;
            selector = selectorPart;
            reciprocating = reciprocatingPart;
            reciprocatingTravel = travel;
            chargingHandle = chargingPart;
            chargingTravel = chargeTravel;
            handleReciprocates = chargeReciprocates;
            boltHandle = boltPart;
            captured = false;
        }

        public void Drive(in MechanismState state, float deltaTime)
        {
            if (!captured) Capture();
            float blend = 1f - Mathf.Exp(-response * Mathf.Max(deltaTime, 0f));

            if (trigger != null)
            {
                triggerBlend = Mathf.Lerp(triggerBlend, state.TriggerPressed ? 1f : 0f, blend);
                trigger.localRotation = triggerRest * Quaternion.Euler(triggerPull * triggerBlend, 0f, 0f);
            }

            if (selector != null && selectorAngles.Length > 0)
            {
                float target = selectorAngles[Mathf.Clamp(state.FireMode, 0, selectorAngles.Length - 1)];
                selectorAngle = Mathf.Lerp(selectorAngle, target, blend);
                selector.localRotation = selectorRest * Quaternion.Euler(selectorAngle, 0f, 0f);
            }

            float charge = state.Charge > 0f ? Mathf.Sin(Mathf.Clamp01(state.Charge) * Mathf.PI) : 0f;
            float carrier = state.LockedBack ? 1f : Mathf.Max(CycleTravel(state.SinceShot, state.CycleTime), charge);
            CarrierTravel = carrier;
            if (reciprocating != null) reciprocating.localPosition = reciprocatingRest + reciprocatingTravel * carrier;
            if (chargingHandle != null)
                chargingHandle.localPosition = chargingRest + chargingTravel * (handleReciprocates ? Mathf.Max(carrier, charge) : charge);

            if (boltHandle != null)
            {
                float work = Mathf.Clamp01(state.BoltWork);
                // Lift (0-0.25), draw back (0.25-0.5), drive forward (0.5-0.75), lock down (0.75-1).
                float lift = work <= 0f ? 0f : work < 0.25f ? work / 0.25f : work < 0.75f ? 1f : 1f - (work - 0.75f) / 0.25f;
                float draw = work < 0.25f ? 0f : work < 0.5f ? (work - 0.25f) / 0.25f : work < 0.75f ? 1f - (work - 0.5f) / 0.25f : 0f;
                boltHandle.localRotation = boltRest * Quaternion.Euler(0f, 0f, boltLift * lift);
                boltHandle.localPosition = boltPositionRest + boltTravel * draw;
            }
        }

        /// <summary>Carrier travel after a shot: a fast run to the buffer, then a slower spring return.</summary>
        public float CycleTravel(float sinceShot, float cycleTime)
        {
            float duration = Mathf.Max(cycleTime, 0.02f) * cycleFraction;
            if (sinceShot < 0f || sinceShot >= duration) return 0f;
            float phase = sinceShot / duration;
            return phase < 0.35f ? phase / 0.35f : 1f - (phase - 0.35f) / 0.65f;
        }

        private void Awake() => Capture();

        private void Capture()
        {
            captured = true;
            if (trigger != null) triggerRest = trigger.localRotation;
            if (selector != null) selectorRest = selector.localRotation;
            if (reciprocating != null) reciprocatingRest = reciprocating.localPosition;
            if (chargingHandle != null) chargingRest = chargingHandle.localPosition;
            if (boltHandle != null)
            {
                boltRest = boltHandle.localRotation;
                boltPositionRest = boltHandle.localPosition;
            }

            selectorAngle = selectorAngles.Length > 0 ? selectorAngles[0] : 0f;
        }
    }
}
