using System.Collections.Generic;
using FPS.Audio;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// Combines a base weapon with attachments: <see cref="Assemble"/> computes the build's stats into a runtime clone
    /// of the weapon data (mass, ergonomics, barrel length, recoil, muzzle velocity, flash, audio, ADS time);
    /// <see cref="AttachParts"/> fits the part models onto a weapon instance, redirects sockets the parts provide and
    /// measures the real centre of mass and rotational inertia about the stock.
    /// </summary>
    public static class GunsmithAssembler
    {
        /// <summary>Radius of gyration of the bare receiver about its own centre of mass (m).</summary>
        private const float ReceiverGyration = 0.08f;

        /// <summary>
        /// A runtime clone of <paramref name="baseData"/> carrying the build's stats. Assets are never modified;
        /// destroy the clone when the build is replaced.
        /// </summary>
        public static WeaponDataSO Assemble(WeaponDataSO baseData, IReadOnlyList<AttachmentDataSO> parts)
        {
            WeaponDataSO built = Object.Instantiate(baseData);
            built.name = baseData.name;

            float mass = baseData.MassKg;
            float ergonomics = baseData.Ergonomics;
            float barrel = baseData.BarrelLengthInches;
            float vertical = 1f, horizontal = 1f, back = 1f, velocity = 1f, flash = 1f, adsTime = 1f;
            WeaponAudioProfileSO audio = baseData.AudioProfile;

            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    AttachmentDataSO part = parts[i];
                    if (part == null) continue;

                    mass += part.MassKg;
                    ergonomics += part.ErgonomicsDelta;
                    if (part.Slot == AttachmentSlot.Barrel && part.BarrelLengthInches > 0f) barrel = part.BarrelLengthInches;
                    vertical *= part.RecoilVertical;
                    horizontal *= part.RecoilHorizontal;
                    back *= part.RecoilBack;
                    velocity *= part.VelocityMultiplier;
                    flash *= part.FlashMultiplier;
                    adsTime *= part.AdsTimeMultiplier;
                    if (part.AudioOverride != null) audio = part.AudioOverride;
                }
            }

            built.ApplyBuild(mass, ergonomics, barrel, vertical, horizontal, back, velocity, flash, adsTime, audio);
            return built;
        }

        /// <summary>
        /// Instantiates each part at its <c>{Slot}Mount</c> (barrels first, so their MuzzleMount exists for muzzle
        /// devices), lets parts override the Muzzle, LeftHandIKGrip and StockPivot sockets, then stores the measured
        /// mass distribution on <paramref name="data"/> (which must be a build clone). Flip-up irons fold under an optic.
        /// </summary>
        public static void AttachParts(WeaponModelRig weapon, WeaponDataSO data, IReadOnlyList<AttachmentDataSO> parts)
        {
            weapon.SetSightsFolded(HasOptic(parts));
            if (parts == null || parts.Count == 0) return;

            Transform root = weapon.transform;
            float partsMass = 0f;
            Vector3 weightedParts = Vector3.zero;
            int fitted = 0;
            var fittedParts = new AttachmentDataSO[parts.Count];
            var fittedInstances = new Transform[parts.Count];

            for (int slot = (int)AttachmentSlot.Barrel; slot <= (int)AttachmentSlot.Light; slot++)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    AttachmentDataSO part = parts[i];
                    if (part == null || (int)part.Slot != slot || part.Prefab == null) continue;

                    Transform mount = weapon.FindNode(AttachmentDataSO.MountName(part.Slot));
                    if (mount == null)
                    {
                        Debug.LogWarning($"{weapon.name} has no {AttachmentDataSO.MountName(part.Slot)} for {part.DisplayName}.", weapon);
                        continue;
                    }

                    Transform instance = Object.Instantiate(part.Prefab, mount, false).transform;
                    instance.name = part.Prefab.name;
                    instance.gameObject.SetActive(true); // templates may be kept inactive, as with pooled prefabs
                    Redirect(weapon, instance, WeaponModelRig.MuzzleSocketName, WeaponSocket.Muzzle);
                    Redirect(weapon, instance, WeaponModelRig.LeftHandIKGripName, WeaponSocket.LeftHandIKGrip);
                    Redirect(weapon, instance, WeaponModelRig.StockPivotSocketName, WeaponSocket.StockPivot);
                    fittedParts[fitted] = part;
                    fittedInstances[fitted++] = instance;
                }
            }

            // Inertia about the shoulder: the receiver as a body with its own gyration plus each part as a point mass.
            Vector3 stock = weapon.StockPivotSocket != null ? root.InverseTransformPoint(weapon.StockPivotSocket.position) : Vector3.zero;
            float inertia = 0f;
            for (int i = 0; i < fitted; i++)
            {
                Vector3 position = root.InverseTransformPoint(fittedInstances[i].TransformPoint(fittedParts[i].CenterOfMass));
                partsMass += fittedParts[i].MassKg;
                weightedParts += position * fittedParts[i].MassKg;
                inertia += fittedParts[i].MassKg * (position - stock).sqrMagnitude;
            }

            float receiverMass = Mathf.Max(0.1f, data.MassKg - partsMass);
            Vector3 receiverCenter = data.BaseCenterOfMass; // stable when the same build is fitted again
            inertia += receiverMass * ((receiverCenter - stock).sqrMagnitude + ReceiverGyration * ReceiverGyration);
            data.ApplyMassDistribution((receiverCenter * receiverMass + weightedParts) / (receiverMass + partsMass), inertia);
        }

        private static bool HasOptic(IReadOnlyList<AttachmentDataSO> parts)
        {
            if (parts == null) return false;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && parts[i].Slot == AttachmentSlot.Optic && parts[i].Prefab != null) return true;
            }

            return false;
        }

        private static void Redirect(WeaponModelRig weapon, Transform instance, string socketName, WeaponSocket socket)
        {
            Transform provided = WeaponModelRig.FindDeep(instance, socketName);
            if (provided != null) weapon.SetSocket(socket, provided);
        }
    }
}
