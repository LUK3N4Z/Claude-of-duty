using System;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// An armed demolition charge: a beep that quickens as the timer runs down, a <see cref="HoldInteraction"/> to
    /// defuse it, and a detonation through the grenade machinery (blast, flash, report, suppression) when it runs out.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HoldInteraction), typeof(AudioSource))]
    public sealed class PlantedBomb : MonoBehaviour
    {
        [Tooltip("Blast definition: radius, damage, report (its prefab is spawned and set off).")]
        [SerializeField] private GrenadeDataSO charge;
        [SerializeField] private AudioClip beep;
        [SerializeField] private Vector2 beepInterval = new Vector2(1f, 0.12f);

        private AudioSource speaker;
        private float fuse, armedAt, nextBeep;

        public event Action Exploded;
        public event Action Defused;

        public HoldInteraction Defuse { get; private set; }
        public bool IsArmed { get; private set; }
        public float TimeLeft => IsArmed ? Mathf.Max(0f, armedAt + fuse - Time.time) : 0f;

        public void Configure(GrenadeDataSO chargeData, AudioClip beepClip)
        {
            charge = chargeData;
            beep = beepClip;
        }

        /// <summary>Starts the countdown; <paramref name="defusers"/> decides who may defuse.</summary>
        public void Arm(float fuseTime, float defuseTime, Func<GameObject, bool> defusers)
        {
            fuse = fuseTime;
            armedAt = Time.time;
            nextBeep = Time.time;
            IsArmed = true;
            Defuse.Configure("Hold to defuse the charge", defuseTime);
            Defuse.Allowed = defusers;
        }

        private void Awake()
        {
            speaker = GetComponent<AudioSource>();
            speaker.spatialBlend = 1f;
            speaker.maxDistance = 40f;
            speaker.rolloffMode = AudioRolloffMode.Linear;
            Defuse = GetComponent<HoldInteraction>();
            Defuse.Completed += OnDefused;
        }

        private void OnDestroy() => Defuse.Completed -= OnDefused;

        private void Update()
        {
            if (!IsArmed) return;
            float now = Time.time;
            if (now >= nextBeep)
            {
                if (beep != null) speaker.PlayOneShot(beep);
                nextBeep = now + Mathf.Lerp(beepInterval.x, beepInterval.y, 1f - TimeLeft / fuse);
            }

            if (TimeLeft > 0f) return;
            IsArmed = false;
            Defuse.Cancel();
            Defuse.enabled = false;
            if (charge != null && charge.Prefab != null)
            {
                ThrownGrenade blast = ObjectPoolManager.Instance.Spawn(charge.Prefab, transform.position + Vector3.up * 0.2f, Quaternion.identity);
                blast.Throw(charge, transform, Vector3.zero, charge.FuseTime); // fuse fully burnt: goes off at once
            }

            Exploded?.Invoke();
        }

        private void OnDefused(GameObject defuser)
        {
            if (!IsArmed) return;
            IsArmed = false;
            Defuse.enabled = false;
            Defused?.Invoke();
        }
    }
}
