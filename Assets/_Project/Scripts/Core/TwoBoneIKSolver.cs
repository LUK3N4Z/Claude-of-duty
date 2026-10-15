using UnityEngine;

namespace FPS.Core
{
    /// <summary>Analytic two-bone IK (law of cosines) for limbs such as first-person arms.</summary>
    public static class TwoBoneIKSolver
    {
        /// <summary>
        /// Joint position for a chain of <paramref name="upperLength"/> and <paramref name="lowerLength"/> rooted at
        /// <paramref name="root"/> reaching for <paramref name="target"/>, bending toward <paramref name="hint"/>.
        /// <paramref name="end"/> is the reachable end point (the target, or as close as the chain allows).
        /// </summary>
        public static Vector3 SolveJoint(Vector3 root, float upperLength, float lowerLength, Vector3 target, Vector3 hint, out Vector3 end)
        {
            Vector3 toTarget = target - root;
            float distance = toTarget.magnitude;
            Vector3 axis = distance > 1e-5f ? toTarget / distance : Vector3.forward;
            float reach = Mathf.Clamp(distance, Mathf.Abs(upperLength - lowerLength) + 1e-4f, upperLength + lowerLength - 1e-4f);
            end = root + axis * reach;

            Vector3 bend = Vector3.ProjectOnPlane(hint - root, axis);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, axis);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.right, axis);
            bend.Normalize();

            float cosine = Mathf.Clamp((upperLength * upperLength + reach * reach - lowerLength * lowerLength) / (2f * upperLength * reach), -1f, 1f);
            float sine = Mathf.Sqrt(1f - cosine * cosine);
            return root + axis * (upperLength * cosine) + bend * (upperLength * sine);
        }

        /// <summary>
        /// Rotates <paramref name="upper"/> and <paramref name="lower"/> so <paramref name="end"/> reaches the target,
        /// then gives the end bone the target rotation. Bone lengths come from the current pose.
        /// </summary>
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 targetPosition, Quaternion targetRotation, Vector3 hint)
        {
            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, end.position);
            Vector3 joint = SolveJoint(upper.position, upperLength, lowerLength, targetPosition, hint, out Vector3 reached);

            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, joint - upper.position) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, reached - lower.position) * lower.rotation;
            end.rotation = targetRotation;
        }
    }
}
