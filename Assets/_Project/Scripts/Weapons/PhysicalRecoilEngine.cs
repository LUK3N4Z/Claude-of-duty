using FPS.Core;
using FPS.Locomotion;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// Per-shot recoil from <see cref="RecoilProfileSO"/>: muzzle rise, sideways torque and roll, rearward shoulder
    /// impulse, a delayed bolt-carrier return jolt, permanent view climb the shooter must pull down, and a damped camera
    /// concussion without shake. The weapon pivots about its stock contact point. Owns RecoilPivot and
    /// <see cref="PlayerRig.CameraOffset"/>. The firing logic calls <see cref="Kick"/> once per round.
    /// </summary>
    [DefaultExecutionOrder(-15)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ProceduralADSAndOpticsController))]
    public sealed class PhysicalRecoilEngine : MonoBehaviour
    {
        private readonly ProceduralSpringSolver weaponSpring = new ProceduralSpringSolver();
        private readonly ProceduralSpringSolver cameraSpring = new ProceduralSpringSolver();
        private ViewmodelRig viewmodel;
        private RealisticLocomotionController locomotion;
        private StanceAndLeanController stance;
        private ProceduralADSAndOpticsController ads;
        private int consecutiveShots;
        private float lastShotTime = float.NegativeInfinity;
        private float boltCarrierDue = -1f;
        private float boltCarrierScale;

        /// <summary>Rounds fired without a pause longer than the profile's ramp reset time.</summary>
        public int ConsecutiveShots => consecutiveShots;

        /// <summary>Applies one round's recoil. Ignored when the held weapon has no recoil profile.</summary>
        public void Kick()
        {
            WeaponDataSO data = viewmodel.Data;
            if (data == null || data.Recoil == null) return;

            RecoilProfileSO profile = data.Recoil;
            float now = Time.time;
            if (now - lastShotTime > profile.RampResetTime) consecutiveShots = 0;
            float ramp = Mathf.Min(profile.MaxKickRamp, 1f + consecutiveShots * profile.KickRampPerShot);
            consecutiveShots++;
            lastShotTime = now;

            float scale = ramp *
                          Mathf.Lerp(profile.HipMultiplier, profile.AdsMultiplier, ads.AdsBlend) *
                          stance.RecoilMultiplier *
                          profile.ReferenceMassKg / data.MassKg; // same impulse, heavier gun, less velocity
            float verticalFactor = Random.Range(1f - profile.VerticalSpread, 1f + profile.VerticalSpread);
            float vertical = profile.VerticalKick * verticalFactor * scale;
            float horizontal = (profile.HorizontalBias + Random.Range(-profile.HorizontalSpread, profile.HorizontalSpread)) * scale;
            float roll = Random.Range(-profile.RollKick, profile.RollKick) * scale;

            weaponSpring.AddImpulse(new Vector3(0f, profile.UpKick * scale, -profile.BackKick * scale), new Vector3(-vertical, horizontal, roll));
            cameraSpring.AddImpulse(new Vector3(0f, 0f, -profile.CameraPush * scale), new Vector3(-profile.CameraKick * scale, 0f, 0f));

            float climb = profile.ViewClimb * verticalFactor * scale;
            locomotion.AddLookOffset(-climb, climb * profile.ViewHorizontalFactor * horizontal / Mathf.Max(1f, vertical));

            boltCarrierDue = now + profile.BoltCarrierDelay;
            boltCarrierScale = scale;
        }

        private void Awake()
        {
            viewmodel = GetComponent<ViewmodelRig>();
            locomotion = GetComponent<RealisticLocomotionController>();
            stance = GetComponent<StanceAndLeanController>();
            ads = GetComponent<ProceduralADSAndOpticsController>();
        }

        private void OnEnable()
        {
            viewmodel.WeaponChanged += OnWeaponChanged;
            OnWeaponChanged();
        }

        private void OnDisable() => viewmodel.WeaponChanged -= OnWeaponChanged;

        private void OnWeaponChanged()
        {
            weaponSpring.Reset();
            cameraSpring.Reset();
            consecutiveShots = 0;
            boltCarrierDue = -1f;

            RecoilProfileSO profile = viewmodel.Data != null ? viewmodel.Data.Recoil : null;
            if (profile == null) return;
            weaponSpring.PositionTuning = profile.WeaponPositionSpring;
            weaponSpring.RotationTuning = profile.WeaponRotationSpring;
            cameraSpring.PositionTuning = profile.CameraPositionSpring;
            cameraSpring.RotationTuning = profile.CameraRotationSpring;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            RecoilProfileSO profile = viewmodel.Data != null ? viewmodel.Data.Recoil : null;
            if (boltCarrierDue >= 0f && Time.time >= boltCarrierDue)
            {
                boltCarrierDue = -1f;
                if (profile != null)
                    weaponSpring.AddImpulse(new Vector3(0f, 0f, profile.BoltCarrierImpulse * boltCarrierScale),
                        new Vector3(profile.BoltCarrierPitch * boltCarrierScale, 0f, 0f));
            }

            weaponSpring.Step(dt);
            cameraSpring.Step(dt);

            Transform pivot = viewmodel.RecoilPivot;
            WeaponModelRig weapon = viewmodel.Weapon;
            Quaternion rotation = Quaternion.Euler(weaponSpring.Rotation);
            Vector3 stock = weapon != null && weapon.StockPivotSocket != null
                ? pivot.InverseTransformPoint(weapon.StockPivotSocket.position)
                : Vector3.zero;
            pivot.SetLocalPositionAndRotation(stock - rotation * stock + weaponSpring.Position, rotation);

            viewmodel.PlayerRig.CameraOffset.SetLocalPositionAndRotation(cameraSpring.Position, Quaternion.Euler(cameraSpring.Rotation));
        }
    }
}
