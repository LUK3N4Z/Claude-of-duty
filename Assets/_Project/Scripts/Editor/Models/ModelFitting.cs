using System.Collections.Generic;
using UnityEngine;

namespace FPS.EditorTools
{
    internal enum AxisAnchor
    {
        Min,
        Center,
        Max,
    }

    internal enum ModelOrientation
    {
        /// <summary>Keep the model's own axes (props: Y up as authored), fit inside a box.</summary>
        Keep,
        /// <summary>Longest axis becomes +Z with the slimmer end forward (barrels, muzzle devices, rifles, stocks).</summary>
        ForwardThinFront,
        /// <summary>Longest axis becomes +Z with the bulkier end forward (scopes, lights: objective bell, bezel).</summary>
        ForwardThickFront,
        /// <summary>Longest axis becomes Y with the bulkier end up (vertical foregrips: mount on top).</summary>
        VerticalThickTop,
    }

    internal enum SliceAnchor
    {
        /// <summary>X and Y anchors use the bounds.</summary>
        None,
        /// <summary>X and Y follow the centre of the rear (-Z) end slice: a bore, an eyepiece, a thread.</summary>
        Rear,
        /// <summary>X and Y follow the centre of the front (+Z) end slice: a muzzle crown, a stock's tube end.</summary>
        Front,
    }

    /// <summary>How an imported model is posed inside its part: orientation rule, size and where its bounds sit.</summary>
    internal struct ModelFit
    {
        public ModelOrientation Orientation;
        /// <summary>Overrides the X/Y anchors with an end slice's centre (forward-oriented models only).</summary>
        public SliceAnchor Slice;
        /// <summary>Target length of the long axis (m); for <see cref="ModelOrientation.Keep"/> the box to fit inside.</summary>
        public Vector3 Size;
        public AxisAnchor AnchorX, AnchorY, AnchorZ;
        /// <summary>Where the anchored bounds point lands, in the part's local space.</summary>
        public Vector3 Target;

        public static ModelFit Forward(float length, bool thickFront, AxisAnchor x, AxisAnchor y, AxisAnchor z, Vector3 target) => new ModelFit
        {
            Orientation = thickFront ? ModelOrientation.ForwardThickFront : ModelOrientation.ForwardThinFront,
            Size = new Vector3(0f, 0f, length), AnchorX = x, AnchorY = y, AnchorZ = z, Target = target,
        };

        public static ModelFit Sliced(float length, bool thickFront, SliceAnchor slice, AxisAnchor z, Vector3 target) => new ModelFit
        {
            Orientation = thickFront ? ModelOrientation.ForwardThickFront : ModelOrientation.ForwardThinFront,
            Size = new Vector3(0f, 0f, length), Slice = slice, AnchorX = AxisAnchor.Center, AnchorY = AxisAnchor.Center, AnchorZ = z, Target = target,
        };

        public static ModelFit Vertical(float height, AxisAnchor x, AxisAnchor y, AxisAnchor z, Vector3 target) => new ModelFit
        {
            Orientation = ModelOrientation.VerticalThickTop, Size = new Vector3(0f, height, 0f), AnchorX = x, AnchorY = y, AnchorZ = z, Target = target,
        };

        public static ModelFit Box(Vector3 size, Vector3 target) => new ModelFit
        {
            Orientation = ModelOrientation.Keep, Size = size, AnchorX = AxisAnchor.Center, AnchorY = AxisAnchor.Min, AnchorZ = AxisAnchor.Center, Target = target,
        };
    }

    /// <summary>
    /// Bounding-box heuristics that pose an arbitrary imported mesh (Meshy output has no fixed orientation or scale) in a
    /// part's socket frame: principal axis by extent, front/back by end cross-section, up by where the mass sits
    /// relative to the box centre, uniform scale to a target length, and anchoring of a bounds point. Also measures
    /// end slices so sockets (muzzle, sight aligner, hand grips) land on the real geometry.
    /// </summary>
    internal static class ModelFitting
    {
        private const float SliceFraction = 0.1f;

        /// <summary>Rotation that maps the model's axes onto the part frame for <paramref name="orientation"/>.</summary>
        public static Quaternion SolveOrientation(IReadOnlyList<Vector3> points, ModelOrientation orientation)
        {
            if (orientation == ModelOrientation.Keep || points.Count == 0) return Quaternion.identity;

            Bounds bounds = BoundsOf(points, Matrix4x4.identity);
            Vector3 extents = bounds.size;
            int longAxis = extents.x >= extents.y && extents.x >= extents.z ? 0 : extents.y >= extents.z ? 1 : 2;
            int a = (longAxis + 1) % 3, b = (longAxis + 2) % 3;

            // Front: compare the cross-section of the two end slices.
            float min = bounds.min[longAxis], max = bounds.max[longAxis];
            float slice = (max - min) * SliceFraction;
            float areaMin = SliceArea(points, longAxis, min, min + slice, a, b);
            float areaMax = SliceArea(points, longAxis, max - slice, max, a, b);
            bool thickAtMax = areaMax > areaMin;
            bool wantThick = orientation != ModelOrientation.ForwardThinFront;
            float sign = thickAtMax == wantThick ? 1f : -1f;
            Vector3 along = Vector3.zero;
            along[longAxis] = sign;

            // Up: of the cross axes, the larger extent; its sign follows the mass (centroid above the box centre).
            int upAxis = extents[a] >= extents[b] ? a : b;
            Vector3 centroid = Centroid(points);
            float upSign = centroid[upAxis] >= bounds.center[upAxis] ? 1f : -1f;
            Vector3 up = Vector3.zero;
            up[upAxis] = upSign;

            if (orientation == ModelOrientation.VerticalThickTop)
            {
                // The long axis becomes up; the larger cross axis becomes forward.
                Vector3 forward = Vector3.zero;
                forward[upAxis] = 1f;
                return Quaternion.Inverse(Quaternion.LookRotation(forward, along));
            }

            return Quaternion.Inverse(Quaternion.LookRotation(along, up));
        }

        /// <summary>
        /// The model-to-part transform for <paramref name="fit"/>: orientation, uniform scale, then translation of the
        /// anchored bounds point onto the target. <paramref name="fitted"/> is the resulting bounds in part space.
        /// </summary>
        public static Matrix4x4 Solve(IReadOnlyList<Vector3> points, ModelFit fit, Quaternion? orientationOverride, out Bounds fitted)
        {
            Quaternion rotation = orientationOverride ?? SolveOrientation(points, fit.Orientation);
            Bounds rotated = BoundsOf(points, Matrix4x4.Rotate(rotation));
            Vector3 size = rotated.size;
            float scale;
            switch (fit.Orientation)
            {
                case ModelOrientation.Keep:
                    scale = Mathf.Min(Ratio(fit.Size.x, size.x), Mathf.Min(Ratio(fit.Size.y, size.y), Ratio(fit.Size.z, size.z)));
                    break;
                case ModelOrientation.VerticalThickTop:
                    scale = Ratio(fit.Size.y, size.y);
                    break;
                default:
                    scale = Ratio(fit.Size.z, size.z);
                    break;
            }

            if (float.IsInfinity(scale) || scale <= 0f) scale = 1f;
            var scaled = new Bounds(rotated.center * scale, rotated.size * scale);
            Vector3 anchor = new Vector3(Pick(scaled, 0, fit.AnchorX), Pick(scaled, 1, fit.AnchorY), Pick(scaled, 2, fit.AnchorZ));
            if (fit.Slice != SliceAnchor.None && fit.Orientation != ModelOrientation.Keep)
            {
                List<Vector3> posed = Transform(points, Matrix4x4.TRS(Vector3.zero, rotation, Vector3.one * scale));
                Vector3 end = EndSliceCenter(posed, 2, fit.Slice == SliceAnchor.Front, out _);
                anchor.x = end.x;
                anchor.y = end.y;
            }

            Vector3 translation = fit.Target - anchor;
            fitted = new Bounds(scaled.center + translation, scaled.size);
            return Matrix4x4.TRS(translation, rotation, Vector3.one * scale);
        }

        public static Bounds BoundsOf(IReadOnlyList<Vector3> points, Matrix4x4 transform)
        {
            if (points.Count == 0) return new Bounds();
            var bounds = new Bounds(transform.MultiplyPoint3x4(points[0]), Vector3.zero);
            for (int i = 1; i < points.Count; i++) bounds.Encapsulate(transform.MultiplyPoint3x4(points[i]));
            return bounds;
        }

        /// <summary>
        /// Centre (cross-axis bounds centre) and half extents of the end slice of <paramref name="points"/> along
        /// <paramref name="axis"/>; <paramref name="atMax"/> picks the far end. The axial coordinate is the end itself.
        /// </summary>
        public static Vector3 EndSliceCenter(IReadOnlyList<Vector3> points, int axis, bool atMax, out Vector2 halfExtents, float fraction = SliceFraction)
        {
            Bounds bounds = BoundsOf(points, Matrix4x4.identity);
            float min = bounds.min[axis], max = bounds.max[axis];
            float depth = (max - min) * fraction;
            float from = atMax ? max - depth : min, to = atMax ? max : min + depth;
            int a = (axis + 1) % 3, b = (axis + 2) % 3;
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                if (p[axis] < from || p[axis] > to) continue;
                minA = Mathf.Min(minA, p[a]);
                maxA = Mathf.Max(maxA, p[a]);
                minB = Mathf.Min(minB, p[b]);
                maxB = Mathf.Max(maxB, p[b]);
            }

            if (minA > maxA)
            {
                halfExtents = Vector2.zero;
                Vector3 fallback = bounds.center;
                fallback[axis] = atMax ? max : min;
                return fallback;
            }

            halfExtents = new Vector2((maxA - minA) * 0.5f, (maxB - minB) * 0.5f);
            Vector3 center = Vector3.zero;
            center[axis] = atMax ? max : min;
            center[a] = (minA + maxA) * 0.5f;
            center[b] = (minB + maxB) * 0.5f;
            return center;
        }

        public static Vector3 Centroid(IReadOnlyList<Vector3> points)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++) sum += points[i];
            return points.Count > 0 ? sum / points.Count : Vector3.zero;
        }

        public static List<Vector3> Transform(IReadOnlyList<Vector3> points, Matrix4x4 transform)
        {
            var result = new List<Vector3>(points.Count);
            for (int i = 0; i < points.Count; i++) result.Add(transform.MultiplyPoint3x4(points[i]));
            return result;
        }

        private static float SliceArea(IReadOnlyList<Vector3> points, int axis, float from, float to, int a, int b)
        {
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                if (p[axis] < from || p[axis] > to) continue;
                minA = Mathf.Min(minA, p[a]);
                maxA = Mathf.Max(maxA, p[a]);
                minB = Mathf.Min(minB, p[b]);
                maxB = Mathf.Max(maxB, p[b]);
            }

            return minA > maxA ? 0f : (maxA - minA) * (maxB - minB);
        }

        private static float Ratio(float target, float actual) => target > 0f && actual > 1e-6f ? target / actual : float.PositiveInfinity;

        private static float Pick(Bounds bounds, int axis, AxisAnchor anchor) =>
            anchor == AxisAnchor.Min ? bounds.min[axis] : anchor == AxisAnchor.Max ? bounds.max[axis] : bounds.center[axis];
    }
}
