using System;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>
    /// A viewmodel posture fixed by where one weapon socket lands in view: the weapon turns by <see cref="euler"/> about
    /// its stock, then shifts so the socket sits at <see cref="target"/> (the pivot parent's space: the eye at the origin
    /// looking down +Z). The same posture keeps every weapon in frame, whatever its length or stock geometry.
    /// </summary>
    [Serializable]
    public struct ViewPose
    {
        [Tooltip("Where the socket is brought, in eye space (m).")]
        public Vector3 target;
        [Tooltip("Weapon rotation about its stock (deg).")]
        public Vector3 euler;

        public ViewPose(Vector3 target, Vector3 euler)
        {
            this.target = target;
            this.euler = euler;
        }

        /// <summary>
        /// Translation that brings <paramref name="socket"/> to <paramref name="target"/> once <paramref name="pivot"/> is
        /// posed by <paramref name="rotation"/> about <paramref name="stock"/> (pivot-local) plus <paramref name="translation"/>.
        /// </summary>
        public static Vector3 Placement(Transform pivot, Transform socket, Vector3 stock, Quaternion rotation, Vector3 translation, Vector3 target)
        {
            Vector3 local = pivot.InverseTransformPoint(socket.position); // independent of the pivot's own pose
            return target - (stock + rotation * (local - stock) + translation);
        }
    }
}
