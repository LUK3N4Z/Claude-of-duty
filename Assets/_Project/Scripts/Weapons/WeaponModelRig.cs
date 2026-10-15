using System;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// Socket map placed on the root of an imported weapon model. Systems read only these transforms, never mesh
    /// paths, so any FBX/GLTF works once its sockets are placed. Convention: weapon forward is +Z, up is +Y.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponModelRig : MonoBehaviour
    {
        public const string MuzzleSocketName = "MuzzleSocket";
        public const string ChamberEjectionSocketName = "ChamberEjectionSocket";
        public const string MagazineSocketName = "MagazineSocket";
        public const string ChargingHandleSocketName = "ChargingHandleSocket";
        public const string StockPivotSocketName = "StockPivotSocket";
        public const string PrimarySightSocketName = "PrimarySightSocket";
        public const string CantedSightSocketName = "CantedSightSocket";
        public const string LeftHandIKGripName = "LeftHandIKGrip";
        public const string RightHandIKGripName = "RightHandIKGrip";

        /// <summary>Sight slots: 0 = primary, 1 = canted backup.</summary>
        public const int SightSlotCount = 2;
        public const string MagazineRenderersFieldName = nameof(magazineRenderers);

        [Tooltip("Bore exit; +Z is the barrel axis. Bullets spawn here.")]
        [SerializeField] private Transform muzzleSocket;
        [Tooltip("Ejection port; +Z is the ejection direction.")]
        [SerializeField] private Transform chamberEjectionSocket;
        [SerializeField] private Transform magazineSocket;
        [SerializeField] private Transform chargingHandleSocket;
        [Tooltip("Buttstock contact point: recoil and inertia pivot here.")]
        [SerializeField] private Transform stockPivotSocket;
        [Tooltip("Primary sight aligner: +Z is the line of sight, +Y the reticle up. May hold a WeaponSight.")]
        [SerializeField] private Transform primarySightSocket;
        [Tooltip("Canted (45°) backup sight aligner. Optional.")]
        [SerializeField] private Transform cantedSightSocket;
        [Tooltip("Support-hand IK target: +Z along the fingers, +Y back of the hand.")]
        [SerializeField] private Transform leftHandIKGrip;
        [Tooltip("Firing-hand IK target on the pistol grip.")]
        [SerializeField] private Transform rightHandIKGrip;
        [Tooltip("Renderers of the inserted magazine; hidden while the magazine is out of the weapon.")]
        [SerializeField] private Renderer[] magazineRenderers = Array.Empty<Renderer>();

        public Transform MuzzleSocket => muzzleSocket;
        public Transform ChamberEjectionSocket => chamberEjectionSocket;
        public Transform MagazineSocket => magazineSocket;
        public Transform ChargingHandleSocket => chargingHandleSocket;
        public Transform StockPivotSocket => stockPivotSocket;
        public Transform PrimarySightSocket => primarySightSocket;
        public Transform CantedSightSocket => cantedSightSocket;
        public Transform LeftHandIKGrip => leftHandIKGrip;
        public Transform RightHandIKGrip => rightHandIKGrip;

        /// <summary>
        /// The aligner for a sight slot and its optional <see cref="WeaponSight"/> (an optic mounted under the socket
        /// may carry its own aligner). Returns null for an empty slot.
        /// </summary>
        public Transform GetSightAligner(int slot, out WeaponSight sight)
        {
            Transform socket = slot == 0 ? primarySightSocket : slot == 1 ? cantedSightSocket : null;
            sight = socket != null ? socket.GetComponentInChildren<WeaponSight>(true) : null;
            return sight != null ? sight.transform : socket;
        }

        /// <summary>Shows or hides the inserted magazine meshes.</summary>
        public void SetMagazineVisible(bool visible)
        {
            for (int i = 0; i < magazineRenderers.Length; i++)
            {
                if (magazineRenderers[i] != null) magazineRenderers[i].enabled = visible;
            }
        }

        /// <summary>
        /// Binds every unassigned socket to a descendant with its conventional name, creating missing ones at the
        /// model root. Returns the number of sockets created.
        /// </summary>
        public int EnsureSockets()
        {
            int created = 0;
            muzzleSocket = Resolve(muzzleSocket, MuzzleSocketName, ref created);
            chamberEjectionSocket = Resolve(chamberEjectionSocket, ChamberEjectionSocketName, ref created);
            magazineSocket = Resolve(magazineSocket, MagazineSocketName, ref created);
            chargingHandleSocket = Resolve(chargingHandleSocket, ChargingHandleSocketName, ref created);
            stockPivotSocket = Resolve(stockPivotSocket, StockPivotSocketName, ref created);
            primarySightSocket = Resolve(primarySightSocket, PrimarySightSocketName, ref created);
            cantedSightSocket = cantedSightSocket != null ? cantedSightSocket : FindDeep(transform, CantedSightSocketName);
            leftHandIKGrip = Resolve(leftHandIKGrip, LeftHandIKGripName, ref created);
            rightHandIKGrip = Resolve(rightHandIKGrip, RightHandIKGripName, ref created);
            return created;
        }

        private void OnDrawGizmosSelected()
        {
            DrawSocket(muzzleSocket, Color.red, 0.08f);
            DrawSocket(chamberEjectionSocket, new Color(1f, 0.6f, 0f), 0.04f);
            DrawSocket(magazineSocket, Color.yellow, 0.04f);
            DrawSocket(chargingHandleSocket, Color.white, 0.03f);
            DrawSocket(stockPivotSocket, Color.magenta, 0.04f);
            DrawSocket(primarySightSocket, Color.cyan, 0.06f);
            DrawSocket(cantedSightSocket, Color.cyan, 0.06f);
            DrawSocket(leftHandIKGrip, Color.green, 0.04f);
            DrawSocket(rightHandIKGrip, Color.green, 0.04f);
        }

        private static void DrawSocket(Transform socket, Color color, float axisLength)
        {
            if (socket == null) return;
            Gizmos.color = color;
            Gizmos.DrawWireSphere(socket.position, 0.008f);
            Gizmos.DrawLine(socket.position, socket.position + socket.forward * axisLength);
            Gizmos.color = Color.green * 0.8f;
            Gizmos.DrawLine(socket.position, socket.position + socket.up * axisLength * 0.4f);
        }

        private Transform Resolve(Transform current, string socketName, ref int created)
        {
            if (current != null) return current;

            Transform found = FindDeep(transform, socketName);
            if (found != null) return found;

            var socket = new GameObject(socketName).transform;
            socket.SetParent(transform, false);
            created++;
            return socket;
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;
                Transform nested = FindDeep(child, childName);
                if (nested != null) return nested;
            }

            return null;
        }
    }
}
