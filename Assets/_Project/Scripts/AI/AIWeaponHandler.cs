using System;
using System.Collections.Generic;
using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Random = UnityEngine.Random;

namespace FPS.AI
{
    /// <summary>
    /// A soldier's rifle: the same weapon data, gunsmith build, ballistics and audio as the player's. Rounds leave the
    /// muzzle socket along the bore with the weapon's MoA dispersion; magazines and the chamber are counted; reloads
    /// take the weapon's handling times and raise the usual foley events; unsuppressed shots flash.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AIBody))]
    public sealed class AIWeaponHandler : MonoBehaviour
    {
        [SerializeField] private WeaponModelRig weaponPrefab;
        [SerializeField] private WeaponDataSO weaponData;
        [Tooltip("Fitted parts; empty uses the weapon's default build.")]
        [SerializeField] private AttachmentDataSO[] attachments = new AttachmentDataSO[0];
        [SerializeField, Min(0)] private int spareMagazines = 3;
        [Tooltip("Reload time relative to the weapon's handling times.")]
        [SerializeField, Min(0.1f)] private float reloadTimeScale = 1.1f;
        [Tooltip("Shortest interval between aimed semi-automatic shots (s).")]
        [SerializeField, Min(0.05f)] private float semiInterval = 0.22f;

        [Header("Presentation")]
        [SerializeField, Min(0f)] private float flashLumen = 12000f;
        [SerializeField, Min(0.005f)] private float flashDuration = 0.03f;
        [Tooltip("Muzzle rise per shot before the weapon's recoil multipliers (deg).")]
        [SerializeField] private float recoilPitch = 1.4f;
        [SerializeField] private float recoilYaw = 0.5f;

        private AIBody body;
        private WeaponDataSO data;
        private WeaponModelRig weapon;
        private WeaponMechanism mechanism;
        private Light flash;
        private bool automatic, chambered;
        private int rounds, spares, burstLeft;
        private float nextShotTime, flashOffTime;
        private float reloadStart, reloadEnd;
        private bool reloadInserted, looted;

        /// <summary>Raised when a reload starts (for the "Reloading!" callout).</summary>
        public event Action ReloadStarted;
        /// <summary>Raised after every round fired.</summary>
        public event Action Fired;

        public WeaponModelRig Weapon => weapon;
        public WeaponDataSO Data => data;
        public int MagazineRounds => rounds;
        public int Capacity => data != null ? data.MagazineCapacity : 0;
        public int SpareMagazines => spares;
        public bool RoundChambered => chambered;
        public int LoadedRounds => rounds + (chambered ? 1 : 0);
        public bool IsReloading { get; private set; }
        public bool IsFiring => burstLeft > 0;
        public bool IsEmpty => !chambered;
        public int ShotsFired { get; private set; }
        public float LastShotTime { get; private set; } = float.NegativeInfinity;

        public void Configure(WeaponModelRig prefab, WeaponDataSO baseData, AttachmentDataSO[] parts, int spareCount)
        {
            weaponPrefab = prefab;
            weaponData = baseData;
            attachments = parts ?? new AttachmentDataSO[0];
            spareMagazines = spareCount;
        }

        /// <summary>Fires up to <paramref name="count"/> rounds at the weapon's cadence (automatic) or aimed semi pace.</summary>
        public void Fire(int count)
        {
            if (IsReloading || count <= 0) return;
            burstLeft = Mathf.Max(burstLeft, count);
        }

        public void CeaseFire() => burstLeft = 0;

        /// <summary>Starts a reload when there is a spare magazine and anything to gain. False otherwise.</summary>
        public bool StartReload()
        {
            if (IsReloading || data == null || spares <= 0 || rounds >= data.MagazineCapacity) return false;

            CeaseFire();
            IsReloading = true;
            reloadInserted = false;
            reloadStart = Time.time;
            float duration = data.MagazineRemoveTime + data.MagazineInsertTime + (chambered ? 0f : data.BoltCycleTime);
            reloadEnd = reloadStart + duration * reloadTimeScale;
            RaiseAction(WeaponActionType.MagazineRemoved);
            ReloadStarted?.Invoke();
            return true;
        }

        private void Awake()
        {
            body = GetComponent<AIBody>();
            if (weaponPrefab != null && weaponData != null) Equip();
        }

        private void OnDestroy()
        {
            if (data != null) Destroy(data);
        }

        private void Equip()
        {
            IReadOnlyList<AttachmentDataSO> parts = attachments.Length > 0 ? attachments : weaponData.DefaultAttachments;
            data = GunsmithAssembler.Assemble(weaponData, parts);
            weapon = Instantiate(weaponPrefab);
            weapon.name = weaponPrefab.name;
            GunsmithAssembler.AttachParts(weapon, data, parts);
            mechanism = weapon.GetComponent<WeaponMechanism>();
            SetLayer(weapon.transform, 0); // third-person weapon: seen by every camera, no colliders
            body.HoldWeapon(weapon);

            for (int i = 0; i < data.FireModeCount; i++) automatic |= data.GetFireMode(i) == FireMode.Auto;
            rounds = data.MagazineCapacity;
            chambered = true;
            spares = spareMagazines;

            var flashObject = new GameObject("Muzzle Flash");
            flashObject.transform.SetParent(body.Muzzle, false);
            flash = flashObject.AddComponent<Light>();
            flashObject.AddComponent<HDAdditionalLightData>();
            flash.type = LightType.Point;
            flash.lightUnit = LightUnit.Lumen;
            flash.range = 12f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
        }

        private void Update()
        {
            if (flash != null && flash.enabled && Time.time >= flashOffTime) flash.enabled = false;
            if (data == null) return;
            if (body.IsDead)
            {
                if (!looted) LeaveMagazines();
                return;
            }

            if (IsReloading)
            {
                UpdateReload();
                return;
            }

            if (burstLeft <= 0 || Time.time < nextShotTime) return;
            if (!chambered)
            {
                burstLeft = 0;
                RaiseAction(WeaponActionType.DryFire);
                return;
            }

            Discharge();
            burstLeft--;
            nextShotTime = Time.time + (automatic ? data.CycleTime : Mathf.Max(data.CycleTime, semiInterval));
        }

        private void LateUpdate()
        {
            if (mechanism == null || data == null || body.IsDead) return;
            float boltStage = Mathf.Max(data.BoltCycleTime * reloadTimeScale, 0.01f);
            float charge = IsReloading && !chambered ? Mathf.Clamp01((Time.time - (reloadEnd - boltStage)) / boltStage) : 0f;
            mechanism.Drive(new MechanismState
            {
                TriggerPressed = burstLeft > 0,
                FireMode = automatic ? 1 : 0,
                SinceShot = Time.time - LastShotTime,
                CycleTime = data.CycleTime,
                LockedBack = !chambered && rounds <= 0 && !IsReloading,
                Charge = charge,
            }, Time.deltaTime);
        }

        private void UpdateReload()
        {
            float now = Time.time;
            float insertAt = reloadStart + data.MagazineRemoveTime * reloadTimeScale;
            if (!reloadInserted && now >= insertAt + data.MagazineInsertTime * reloadTimeScale)
            {
                reloadInserted = true;
                spares--;
                rounds = data.MagazineCapacity;
                RaiseAction(WeaponActionType.MagazineInserted);
            }

            if (now < reloadEnd) return;
            if (!chambered)
            {
                rounds--;
                chambered = true;
                RaiseAction(WeaponActionType.BoltCycled);
            }

            IsReloading = false;
        }

        private void Discharge()
        {
            Transform muzzle = body.Muzzle;
            CaliberDataSO caliber = data.Caliber;
            EventBus<ShotFiredEvent>.Raise(new ShotFiredEvent
            {
                Origin = muzzle.position,
                Direction = RealisticWeaponController.ApplyDispersion(muzzle.forward, muzzle.right, muzzle.up, data.MoaAccuracy, Random.insideUnitCircle),
                MuzzleVelocity = caliber != null ? caliber.MuzzleVelocity(data.BarrelLengthInches) * data.MuzzleVelocityMultiplier : 0f,
                Caliber = caliber,
                Shooter = transform,
            });
            EventBus<GunshotEvent>.Raise(new GunshotEvent
            {
                Position = muzzle.position,
                Direction = muzzle.forward,
                Shooter = transform,
                Profile = data.AudioProfile,
                MuzzleFlash = data.MuzzleFlashMultiplier,
            });

            if (rounds > 0) rounds--;
            else chambered = false;

            if (data.MuzzleFlashMultiplier > 0.05f)
            {
                flash.intensity = flashLumen * data.MuzzleFlashMultiplier;
                flash.enabled = true;
                flashOffTime = Time.time + flashDuration;
            }

            body.AddRecoil(recoilPitch * data.RecoilVerticalMultiplier, Random.Range(-recoilYaw, recoilYaw) * data.RecoilHorizontalMultiplier);
            ShotsFired++;
            LastShotTime = Time.time;
            Fired?.Invoke();
        }

        /// <summary>The fallen rifle keeps its magazines: the player can strip them (Interact).</summary>
        private void LeaveMagazines()
        {
            looted = true;
            if (weapon == null) return;
            var magazines = new int[spares + 1];
            magazines[0] = rounds;
            for (int i = 1; i < magazines.Length; i++) magazines[i] = data.MagazineCapacity;

            var loot = new GameObject("Loot (interact)") { layer = GameLayers.Interactable };
            loot.transform.SetParent(weapon.transform, false);
            var trigger = loot.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0f, 0.1f);
            trigger.size = new Vector3(0.5f, 0.4f, 1f);
            loot.AddComponent<DroppedWeapon>().Configure(data.Caliber, magazines);
        }

        private void RaiseAction(WeaponActionType type)
        {
            EventBus<WeaponActionEvent>.Raise(new WeaponActionEvent
            {
                Type = type,
                Position = weapon != null ? weapon.transform.position : transform.position,
                Owner = transform,
                Value = rounds,
            });
        }

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
        }
    }
}
