using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS.EditorTools
{
    /// <summary>
    /// Procedural mesh builder for the high-detail fallback models: chamfered boxes, lathed (revolved) profiles,
    /// polygon prisms and side-profile extrusions, picatinny rails, tori and ellipsoids, under a transform stack.
    /// Triangles are wound from their intended normal, so every face points outward whatever order points arrive in.
    /// UVs are planar or cylindrical in metres times <see cref="UvScale"/>, so tiling PBR materials keep a constant
    /// texel density across parts of any size.
    /// </summary>
    internal sealed class MeshKit
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
        private Matrix4x4 matrix = Matrix4x4.identity;

        /// <summary>Texture repeats per metre.</summary>
        public float UvScale { get; set; } = 8f;
        public int VertexCount => vertices.Count;
        public int TriangleCount => triangles.Count / 3;

        public MeshKit Push(Vector3 position, Quaternion rotation)
        {
            stack.Push(matrix);
            matrix *= Matrix4x4.TRS(position, rotation, Vector3.one);
            return this;
        }

        public MeshKit Push(Vector3 position) => Push(position, Quaternion.identity);

        public MeshKit Pop()
        {
            matrix = stack.Pop();
            return this;
        }

        // ---- Primitives ----

        /// <summary>Box with its edges chamfered by <paramref name="bevel"/> (0 = sharp): 6 faces, 12 edge strips, 8 corners.</summary>
        public void Box(Vector3 center, Vector3 size, float bevel = 0f)
        {
            Vector3 h = size * 0.5f;
            float b = Mathf.Clamp(bevel, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
            Vector3 V(int sx, int sy, int sz, int axis)
            {
                var p = new Vector3(sx * (h.x - b), sy * (h.y - b), sz * (h.z - b));
                p[axis] = (axis == 0 ? sx : axis == 1 ? sy : sz) * h[axis];
                return center + p;
            }

            for (int axis = 0; axis < 3; axis++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                    var corners = new Vector3[4];
                    int[] u = { -1, 1, 1, -1 }, v = { -1, -1, 1, 1 };
                    for (int i = 0; i < 4; i++)
                    {
                        var sign = new int[3];
                        sign[axis] = s;
                        sign[a1] = u[i];
                        sign[a2] = v[i];
                        corners[i] = V(sign[0], sign[1], sign[2], axis);
                    }

                    Vector3 outward = Vector3.zero;
                    outward[axis] = s;
                    Polygon(corners, outward);
                }
            }

            if (b <= 0f) return;

            for (int a = 0; a < 3; a++)
            {
                for (int c = a + 1; c < 3; c++)
                {
                    int k = 3 - a - c;
                    for (int sa = -1; sa <= 1; sa += 2)
                    {
                        for (int sc = -1; sc <= 1; sc += 2)
                        {
                            var sign0 = new int[3];
                            var sign1 = new int[3];
                            sign0[a] = sign1[a] = sa;
                            sign0[c] = sign1[c] = sc;
                            sign0[k] = -1;
                            sign1[k] = 1;
                            Vector3 outward = Vector3.zero;
                            outward[a] = sa;
                            outward[c] = sc;
                            Polygon(new[]
                            {
                                V(sign0[0], sign0[1], sign0[2], a), V(sign1[0], sign1[1], sign1[2], a),
                                V(sign1[0], sign1[1], sign1[2], c), V(sign0[0], sign0[1], sign0[2], c),
                            }, outward);
                        }
                    }
                }
            }

            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Polygon(new[] { V(sx, sy, sz, 0), V(sx, sy, sz, 1), V(sx, sy, sz, 2) }, new Vector3(sx, sy, sz));
        }

        /// <summary>
        /// Surface of revolution about local +Z. <paramref name="profile"/> holds (radius, z) points traced from the back
        /// to the front; start and end at radius 0 to close the ends. <paramref name="smooth"/> shares normals between
        /// profile segments (curved silhouettes); otherwise each segment is a hard-edged band.
        /// </summary>
        public void Lathe(IReadOnlyList<Vector2> profile, int segments, bool smooth = false, float startDegrees = 0f, float sweepDegrees = 360f)
        {
            if (profile.Count < 2) throw new ArgumentException("A lathe profile needs at least two points.", nameof(profile));
            bool closed = Mathf.Approximately(sweepDegrees, 360f);
            int ringCount = closed ? segments : segments + 1;
            var segmentNormals = new Vector2[profile.Count - 1];
            for (int i = 0; i < segmentNormals.Length; i++)
            {
                Vector2 d = profile[i + 1] - profile[i];
                segmentNormals[i] = new Vector2(d.y, -d.x).normalized;
            }

            float circumferenceScale = Mathf.Deg2Rad * sweepDegrees / segments;
            for (int s = 0; s < segmentNormals.Length; s++)
            {
                Vector2 a = profile[s], b = profile[s + 1];
                if ((b - a).sqrMagnitude < 1e-12f) continue;
                Vector2 na = smooth && s > 0 ? (segmentNormals[s - 1] + segmentNormals[s]).normalized : segmentNormals[s];
                Vector2 nb = smooth && s < segmentNormals.Length - 1 ? (segmentNormals[s] + segmentNormals[s + 1]).normalized : segmentNormals[s];
                int baseIndex = vertices.Count;
                float radiusForUv = (a.x + b.x) * 0.5f;
                for (int r = 0; r < ringCount; r++)
                {
                    float angle = (startDegrees + sweepDegrees * r / segments) * Mathf.Deg2Rad;
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    float u = r * circumferenceScale * Mathf.Max(radiusForUv, 0.002f) * UvScale;
                    AddVertex(new Vector3(cos * a.x, sin * a.x, a.y), new Vector3(cos * na.x, sin * na.x, na.y), new Vector2(u, a.y * UvScale));
                    AddVertex(new Vector3(cos * b.x, sin * b.x, b.y), new Vector3(cos * nb.x, sin * nb.x, nb.y), new Vector2(u, b.y * UvScale));
                }

                Vector2 faceNormal = segmentNormals[s];
                for (int r = 0; r < segments; r++)
                {
                    int r0 = baseIndex + r * 2;
                    int r1 = baseIndex + ((r + 1) % ringCount) * 2;
                    float angle = (startDegrees + sweepDegrees * (r + 0.5f) / segments) * Mathf.Deg2Rad;
                    Vector3 expected = new Vector3(Mathf.Cos(angle) * faceNormal.x, Mathf.Sin(angle) * faceNormal.x, faceNormal.y);
                    if (a.x > 1e-6f) Triangle(r0, r1, r0 + 1, expected);
                    if (b.x > 1e-6f) Triangle(r0 + 1, r1, r1 + 1, expected);
                }
            }
        }

        /// <summary>Closed cylinder along local +Z from <paramref name="z0"/> to <paramref name="z1"/>, ends chamfered by <paramref name="chamfer"/>.</summary>
        public void Cylinder(float radius, float z0, float z1, int segments, float chamfer = 0f)
        {
            float c = Mathf.Min(chamfer, Mathf.Min(radius * 0.5f, (z1 - z0) * 0.25f));
            Lathe(c > 0f
                ? new[] { new Vector2(0f, z0), new Vector2(radius - c, z0), new Vector2(radius, z0 + c), new Vector2(radius, z1 - c), new Vector2(radius - c, z1), new Vector2(0f, z1) }
                : new[] { new Vector2(0f, z0), new Vector2(radius, z0), new Vector2(radius, z1), new Vector2(0f, z1) }, segments);
        }

        /// <summary>Thick-walled tube along local +Z with flat annular ends.</summary>
        public void Tube(float outerRadius, float innerRadius, float z0, float z1, int segments)
        {
            Lathe(new[]
            {
                new Vector2(innerRadius, z0), new Vector2(outerRadius, z0), new Vector2(outerRadius, z1),
                new Vector2(innerRadius, z1), new Vector2(innerRadius, z0),
            }, segments);
        }

        /// <summary>Torus around local +Z.</summary>
        public void Torus(float majorRadius, float minorRadius, int majorSegments, int minorSegments)
        {
            int baseIndex = vertices.Count;
            for (int i = 0; i <= majorSegments; i++)
            {
                float u = i * Mathf.PI * 2f / majorSegments;
                var ring = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                for (int j = 0; j <= minorSegments; j++)
                {
                    float v = j * Mathf.PI * 2f / minorSegments;
                    Vector3 normal = ring * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v);
                    AddVertex(ring * majorRadius + normal * minorRadius, normal,
                        new Vector2(u * majorRadius * UvScale, v * minorRadius * UvScale));
                }
            }

            int stride = minorSegments + 1;
            for (int i = 0; i < majorSegments; i++)
            {
                for (int j = 0; j < minorSegments; j++)
                {
                    int a = baseIndex + i * stride + j, b = a + stride, c = a + 1, d = b + 1;
                    Vector3 expected = normals[a] + normals[d];
                    Triangle(a, b, c, expected);
                    Triangle(c, b, d, expected);
                }
            }
        }

        /// <summary>
        /// Ellipsoid (or a latitude band of one) centred at <paramref name="center"/>, poles on local ±Y. Latitudes in
        /// degrees from -90 (bottom) to 90 (top). <paramref name="inside"/> flips the faces to line a shell.
        /// </summary>
        public void Ellipsoid(Vector3 center, Vector3 radii, int segments, int rings, float latitudeMin = -90f, float latitudeMax = 90f, bool inside = false)
        {
            int baseIndex = vertices.Count;
            var inverseSquare = new Vector3(1f / (radii.x * radii.x), 1f / (radii.y * radii.y), 1f / (radii.z * radii.z));
            for (int r = 0; r <= rings; r++)
            {
                float latitude = Mathf.Lerp(latitudeMin, latitudeMax, r / (float)rings) * Mathf.Deg2Rad;
                for (int s = 0; s <= segments; s++)
                {
                    float longitude = s * Mathf.PI * 2f / segments;
                    var unit = new Vector3(Mathf.Cos(latitude) * Mathf.Cos(longitude), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Sin(longitude));
                    Vector3 point = Vector3.Scale(unit, radii);
                    Vector3 normal = Vector3.Scale(point, inverseSquare).normalized * (inside ? -1f : 1f);
                    AddVertex(center + point, normal, new Vector2(longitude * radii.x * UvScale, latitude * radii.y * UvScale));
                }
            }

            int stride = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = baseIndex + r * stride + s, b = a + stride, c = a + 1, d = b + 1;
                    Vector3 expected = normals[a] + normals[d];
                    if (expected.sqrMagnitude < 1e-10f) expected = normals[a] + normals[c] + normals[b];
                    Triangle(a, b, c, expected);
                    Triangle(c, b, d, expected);
                }
            }
        }

        /// <summary>Prism: <paramref name="section"/> (XY, any simple polygon) extruded along local +Z from z0 to z1, capped.</summary>
        public void Prism(IReadOnlyList<Vector2> section, float z0, float z1) =>
            ExtrudeWith(section, z0, z1, (p, depth) => new Vector3(p.x, p.y, depth), Vector3.forward);

        /// <summary>
        /// Side-profile extrusion: <paramref name="outline"/> holds (z, y) points of a part seen from the side, extruded
        /// across the weapon (local X) from -halfWidth to +halfWidth. Concave outlines are fine.
        /// </summary>
        public void Profile(IReadOnlyList<Vector2> outline, float halfWidth, float centerX = 0f) =>
            ExtrudeWith(outline, centerX - halfWidth, centerX + halfWidth, (p, depth) => new Vector3(depth, p.y, p.x), Vector3.right);

        /// <summary>
        /// MIL-STD-1913 rail along local +Z from <paramref name="z0"/>: a base strip with a recoil slot every 10 mm.
        /// Its top face sits at <paramref name="top"/>.
        /// </summary>
        public void Rail(float z0, float length, float top, float width = 0.021f)
        {
            const float pitch = 0.01001f, toothLength = 0.0048f, baseHeight = 0.0042f, toothHeight = 0.0052f;
            Box(new Vector3(0f, top - toothHeight - baseHeight * 0.5f, z0 + length * 0.5f), new Vector3(width * 0.72f, baseHeight, length), 0.0006f);
            int teeth = Mathf.Max(1, Mathf.FloorToInt(length / pitch));
            float start = z0 + (length - (teeth - 1) * pitch) * 0.5f;
            for (int i = 0; i < teeth; i++)
                Box(new Vector3(0f, top - toothHeight * 0.5f, start + i * pitch), new Vector3(width, toothHeight, toothLength), 0.0008f);
        }

        /// <summary>Flat convex or concave polygon, wound to face <paramref name="outward"/>.</summary>
        public void Polygon(IReadOnlyList<Vector3> points, Vector3 outward)
        {
            Vector3 normal = NewellNormal(points);
            if (Vector3.Dot(normal, outward) < 0f) normal = -normal;
            int baseIndex = vertices.Count;
            for (int i = 0; i < points.Count; i++) AddVertex(points[i], normal, PlanarUv(points[i], normal));

            if (points.Count == 3)
            {
                Triangle(baseIndex, baseIndex + 1, baseIndex + 2, normal);
                return;
            }

            // Project onto the dominant plane and ear-clip, so concave outlines triangulate correctly.
            var flat = new Vector2[points.Count];
            int drop = Dominant(normal);
            for (int i = 0; i < points.Count; i++)
                flat[i] = drop == 0 ? new Vector2(points[i].y, points[i].z) : drop == 1 ? new Vector2(points[i].x, points[i].z) : new Vector2(points[i].x, points[i].y);
            foreach (Vector3Int tri in Triangulate(flat)) Triangle(baseIndex + tri.x, baseIndex + tri.y, baseIndex + tri.z, normal);
        }

        public Mesh ToMesh(string meshName)
        {
            var mesh = new Mesh { name = meshName, indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- Internals ----

        private void ExtrudeWith(IReadOnlyList<Vector2> polygon, float d0, float d1, Func<Vector2, float, Vector3> map, Vector3 axis)
        {
            int n = polygon.Count;
            float area = SignedArea(polygon);
            // Caps.
            var front = new Vector3[n];
            var back = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                front[i] = map(polygon[i], d1);
                back[i] = map(polygon[i], d0);
            }

            Polygon(front, axis);
            Polygon(back, -axis);

            // Walls: one hard-edged quad per outline edge, facing away from the interior.
            for (int i = 0; i < n; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % n];
                Vector2 edge = b - a;
                if (edge.sqrMagnitude < 1e-12f) continue;
                Vector2 outward2 = area > 0f ? new Vector2(edge.y, -edge.x) : new Vector2(-edge.y, edge.x);
                Vector3 outward = map(outward2, 0f) - map(Vector2.zero, 0f);
                Polygon(new[] { map(a, d0), map(b, d0), map(b, d1), map(a, d1) }, outward);
            }
        }

        private int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            vertices.Add(matrix.MultiplyPoint3x4(position));
            normals.Add(matrix.MultiplyVector(normal).normalized);
            uvs.Add(uv);
            return vertices.Count - 1;
        }

        /// <summary>Adds a triangle facing <paramref name="localNormal"/> (pre-transform), whatever the index order.</summary>
        private void Triangle(int a, int b, int c, Vector3 localNormal)
        {
            Vector3 expected = matrix.MultiplyVector(localNormal);
            Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (cross.sqrMagnitude < 1e-16f) return; // degenerate
            if (Vector3.Dot(cross, expected) < 0f)
            {
                (b, c) = (c, b);
            }

            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private Vector2 PlanarUv(Vector3 p, Vector3 normal)
        {
            switch (Dominant(normal))
            {
                case 0: return new Vector2(p.z, p.y) * UvScale;
                case 1: return new Vector2(p.x, p.z) * UvScale;
                default: return new Vector2(p.x, p.y) * UvScale;
            }
        }

        private static int Dominant(Vector3 n)
        {
            float x = Mathf.Abs(n.x), y = Mathf.Abs(n.y), z = Mathf.Abs(n.z);
            return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        }

        private static Vector3 NewellNormal(IReadOnlyList<Vector3> points)
        {
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 current = points[i], next = points[(i + 1) % points.Count];
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }

            return normal.sqrMagnitude > 1e-20f ? normal.normalized : Vector3.up;
        }

        internal static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }

            return area * 0.5f;
        }

        /// <summary>Ear clipping of a simple polygon (either winding). Returns index triples into <paramref name="polygon"/>.</summary>
        internal static List<Vector3Int> Triangulate(IReadOnlyList<Vector2> polygon)
        {
            var result = new List<Vector3Int>(polygon.Count);
            var remaining = new List<int>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++) remaining.Add(i);
            bool counterClockwise = SignedArea(polygon) > 0f;

            int guard = polygon.Count * polygon.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int prev = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int curr = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];
                    Vector2 a = polygon[prev], b = polygon[curr], c = polygon[next];
                    float turn = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (counterClockwise ? turn <= 0f : turn >= 0f) continue; // reflex vertex

                    bool contains = false;
                    for (int j = 0; j < remaining.Count && !contains; j++)
                    {
                        int other = remaining[j];
                        if (other == prev || other == curr || other == next) continue;
                        contains = InTriangle(polygon[other], a, b, c);
                    }

                    if (contains) continue;
                    result.Add(new Vector3Int(prev, curr, next));
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped) break; // numerically degenerate outline: fan the rest
            }

            for (int i = 1; i + 1 < remaining.Count; i++) result.Add(new Vector3Int(remaining[0], remaining[i], remaining[i + 1]));
            return result;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }
    }
}
