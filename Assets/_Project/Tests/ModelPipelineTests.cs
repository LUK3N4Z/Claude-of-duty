#if UNITY_EDITOR
using System.Collections.Generic;
using FPS.Ballistics;
using FPS.EditorTools;
using FPS.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FPS.Tests
{
    /// <summary>M11 asset pipeline: procedural mesh kit, mask-map packing, fitting heuristics, Meshy API payloads and the built content.</summary>
    public class ModelPipelineTests
    {
        private static readonly string[] PrimitiveMeshes = { "Cube", "Cylinder", "Sphere", "Capsule", "Quad", "Plane" };

        // ---- Mesh kit ----

        [Test]
        public void ChamferedBox_HasEveryFaceFacingOutward()
        {
            var kit = new MeshKit();
            kit.Box(new Vector3(0.1f, 0.2f, 0.3f), new Vector3(0.2f, 0.1f, 0.4f), 0.01f);
            Mesh mesh = kit.ToMesh("box");
            AssertOutward(mesh, triangleCenter => triangleCenter - new Vector3(0.1f, 0.2f, 0.3f));
            Assert.AreEqual(26 * 2 - 8, kit.TriangleCount, "6 face quads + 12 edge quads (2 tris each) + 8 corner triangles");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Lathe_ClosedCylinder_FacesOutwardAndHasExpectedBounds()
        {
            var kit = new MeshKit();
            kit.Cylinder(0.02f, -0.05f, 0.1f, 24, 0.002f);
            Mesh mesh = kit.ToMesh("cylinder");
            AssertOutward(mesh, c => new Vector3(c.x, c.y, c.z - 0.025f));
            Assert.AreEqual(0.04f, mesh.bounds.size.x, 0.001f);
            Assert.AreEqual(0.15f, mesh.bounds.size.z, 0.0001f);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ConcaveOutline_TriangulatesToItsArea()
        {
            var l = new[] { new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(3f, 1f), new Vector2(1f, 1f), new Vector2(1f, 3f), new Vector2(0f, 3f) };
            List<Vector3Int> triangles = MeshKit.Triangulate(l);
            Assert.AreEqual(l.Length - 2, triangles.Count);
            float area = 0f;
            foreach (Vector3Int t in triangles)
                area += Mathf.Abs((l[t.y].x - l[t.x].x) * (l[t.z].y - l[t.x].y) - (l[t.z].x - l[t.x].x) * (l[t.y].y - l[t.x].y)) * 0.5f;
            Assert.AreEqual(5f, area, 1e-4f, "L-shape area");

            var kit = new MeshKit();
            kit.Profile(l, 0.5f);
            Mesh mesh = kit.ToMesh("profile");
            Assert.AreEqual(1f, mesh.bounds.size.x, 1e-4f, "extruded across X by twice the half width");
            Assert.AreEqual(3f, mesh.bounds.size.z, 1e-4f);
            Object.DestroyImmediate(mesh);
        }

        // ---- Mask map ----

        [Test]
        public void MaskMap_PacksMetallicOcclusionDetailAndInvertedRoughness()
        {
            Color32 pixel = MaskMapPacker.Pack(1f, 0.5f, 1f, 0.2f);
            Assert.AreEqual(255, pixel.r, "R metallic");
            Assert.AreEqual(128, pixel.g, "G occlusion");
            Assert.AreEqual(255, pixel.b, "B detail mask");
            Assert.AreEqual(204, pixel.a, "A smoothness = 1 - roughness");

            Color32[] packed = MaskMapPacker.Pack(new[] { new Color32(0, 0, 0, 255) }, new[] { new Color32(255, 255, 255, 255) }, null, 1);
            Assert.AreEqual(0, packed[0].r);
            Assert.AreEqual(255, packed[0].g, "missing occlusion reads as unoccluded");
            Assert.AreEqual(0, packed[0].a, "fully rough is fully matte");

            Color32 orm = MaskMapPacker.FromOrm(new[] { new Color32(200, 51, 255, 255) })[0];
            Assert.AreEqual(255, orm.r, "ORM blue is metallic");
            Assert.AreEqual(200, orm.g, "ORM red is occlusion");
            Assert.AreEqual(204, orm.a, "ORM green is roughness");
        }

        [Test]
        public void TextureRoles_AreClassifiedFromMeshyFileNames()
        {
            Assert.AreEqual(TextureRole.Normal, ImportedModels.Classify("texture_0_normal.png"));
            Assert.AreEqual(TextureRole.Metallic, ImportedModels.Classify("Carbine_Metallic.png"));
            Assert.AreEqual(TextureRole.Roughness, ImportedModels.Classify("texture_0_roughness.png"));
            Assert.AreEqual(TextureRole.Orm, ImportedModels.Classify("model_metallicRoughness.png"));
            Assert.AreEqual(TextureRole.Occlusion, ImportedModels.Classify("rifle_ao.png"));
            Assert.AreEqual(TextureRole.PackedMask, ImportedModels.Classify("Carbine_Receiver_MaskMap.png"));
            Assert.AreEqual(TextureRole.BaseColor, ImportedModels.Classify("texture_0.png"));
        }

        // ---- Fitting heuristics ----

        [Test]
        public void Orientation_PutsTheSlimEndForwardAndTheMassUp_FromAnyPose()
        {
            List<Vector3> rifle = RiflePoints();
            foreach (Quaternion pose in new[] { Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(0f, -90f, 180f), Quaternion.Euler(90f, 0f, 0f) })
            {
                List<Vector3> posed = ModelFitting.Transform(rifle, Matrix4x4.Rotate(pose));
                Quaternion fix = ModelFitting.SolveOrientation(posed, ModelOrientation.ForwardThinFront);
                List<Vector3> fixedPoints = ModelFitting.Transform(posed, Matrix4x4.Rotate(fix));
                Vector3 muzzle = fixedPoints[0], butt = fixedPoints[1];
                Assert.Greater(muzzle.z, butt.z + 0.5f, $"muzzle forward for pose {pose.eulerAngles}");
                Bounds bounds = ModelFitting.BoundsOf(fixedPoints, Matrix4x4.identity);
                Assert.Greater(bounds.size.z, bounds.size.y, "long axis along Z");
                Vector3 magazine = fixedPoints[2];
                Assert.Less(magazine.y, bounds.center.y, $"magazine hangs below for pose {pose.eulerAngles}");
            }
        }

        [Test]
        public void Solve_ScalesToLength_AnchorsBounds_AndCentresEndSlices()
        {
            List<Vector3> rifle = ModelFitting.Transform(RiflePoints(), Matrix4x4.TRS(new Vector3(3f, 1f, -2f), Quaternion.Euler(0f, 90f, 0f), Vector3.one * 100f));
            ModelFit fit = ModelFit.Forward(0.5f, false, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Min, new Vector3(0f, 0.04f, -0.15f));
            ModelFitting.Solve(rifle, fit, null, out Bounds fitted);
            Assert.AreEqual(0.5f, fitted.size.z, 1e-4f, "scaled to the target length");
            Assert.AreEqual(-0.15f, fitted.min.z, 1e-4f, "rear anchored");
            Assert.AreEqual(0.04f, fitted.max.y, 1e-4f, "top anchored");
            Assert.AreEqual(0f, fitted.center.x, 1e-4f);

            var barrel = new MeshKit();
            barrel.Cylinder(0.01f, 0f, 0.3f, 16);
            barrel.Box(new Vector3(0f, 0.02f, 0.1f), new Vector3(0.02f, 0.02f, 0.03f)); // gas block off-axis
            Mesh mesh = barrel.ToMesh("barrel");
            var points = new List<Vector3>(mesh.vertices);
            Object.DestroyImmediate(mesh);
            Matrix4x4 pose = ModelFitting.Solve(ModelFitting.Transform(points, Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0.5f))),
                ModelFit.Sliced(0.3f, false, SliceAnchor.Front, AxisAnchor.Min, Vector3.zero), null, out _);
            List<Vector3> posed = ModelFitting.Transform(ModelFitting.Transform(points, Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0.5f))), pose);
            Vector3 crown = ModelFitting.EndSliceCenter(posed, 2, true, out _);
            Assert.AreEqual(0f, crown.x, 1e-3f, "bore on the axis despite the gas block");
            Assert.AreEqual(0f, crown.y, 1e-3f);
            Assert.AreEqual(0.3f, crown.z, 1e-3f);
        }

        // ---- Meshy API ----

        [Test]
        public void MeshyPayloads_MatchTheTextTo3DApi()
        {
            MeshyAssetSpec spec = MeshyAssetManifest.Find("Optic_RedDot");
            string preview = MeshyApiClient.PreviewJson(spec);
            StringAssert.Contains("\"mode\":\"preview\"", preview);
            StringAssert.Contains("\"art_style\":\"realistic\"", preview);
            StringAssert.Contains("\"target_polycount\":15000", preview);
            StringAssert.Contains("\"should_remesh\":true", preview);
            StringAssert.Contains("Micro red dot", preview);

            string refine = MeshyApiClient.RefineJson("task-123", spec);
            StringAssert.Contains("\"mode\":\"refine\"", refine);
            StringAssert.Contains("\"preview_task_id\":\"task-123\"", refine);
            StringAssert.Contains("\"enable_pbr\":true", refine);

            MeshyTask task = MeshyApiClient.ParseTask(
                "{\"id\":\"t1\",\"status\":\"SUCCEEDED\",\"progress\":100,\"model_urls\":{\"fbx\":\"https://x/model.fbx\",\"glb\":\"https://x/m.glb\"}," +
                "\"texture_urls\":[{\"base_color\":\"https://x/b.png\",\"metallic\":\"https://x/m.png\",\"roughness\":\"https://x/r.png\",\"normal\":\"https://x/n.png\"}],\"consumed_credits\":10}");
            Assert.IsTrue(task.Succeeded && task.Finished);
            Assert.AreEqual("https://x/model.fbx", task.model_urls.fbx);
            Assert.AreEqual("https://x/r.png", task.texture_urls[0].roughness);
            Assert.AreEqual("t9", MeshyApiClient.ParseCreated("{\"result\":\"t9\"}"));
        }

        [Test]
        public void Manifest_CoversEveryPartWithUniqueIdsAndValidPrompts()
        {
            var ids = new HashSet<string>();
            foreach (MeshyAssetSpec spec in MeshyAssetManifest.Specs)
            {
                Assert.IsTrue(ids.Add(spec.Id), $"duplicate id {spec.Id}");
                Assert.LessOrEqual(spec.Prompt.Length, 800, $"{spec.Id} prompt over the API limit");
                Assert.LessOrEqual(spec.TexturePrompt.Length, 800, $"{spec.Id} texture prompt over the API limit");
            }

            foreach (string guid in AssetDatabase.FindAssets("t:AttachmentDataSO", new[] { "Assets/_Project/Data/Weapons/Attachments" }))
            {
                string part = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                Assert.IsTrue(ids.Contains(part), $"attachment {part} has no Meshy spec");
            }

            Assert.GreaterOrEqual(ids.Count, 36);
        }

        // ---- Built content ----

        [Test]
        public void Weapons_AreDetailedModels_WithMechanismsAndMagazines()
        {
            foreach (string weapon in new[] { "TestRifle", "TestMarksman", "BattleRifle", "Sidearm" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<WeaponModelRig>($"Assets/_Project/Prefabs/Weapons/{weapon}.prefab");
                Assert.IsNotNull(prefab, weapon);
                AssertNoPrimitives(prefab.gameObject);
                Assert.Greater(Triangles(prefab.gameObject), weapon == "Sidearm" ? 500 : 1500, $"{weapon} geometry detail");
                var mechanism = prefab.GetComponent<WeaponMechanism>();
                Assert.IsNotNull(mechanism, $"{weapon} mechanism");
                Assert.IsNotNull(mechanism.Trigger, $"{weapon} trigger part");
                if (weapon != "TestMarksman") Assert.IsNotNull(mechanism.Reciprocating, $"{weapon} carrier or slide");
                Assert.IsNotNull(prefab.MuzzleSocket);
                var magazines = new SerializedObject(prefab).FindProperty(WeaponModelRig.MagazineRenderersFieldName);
                Assert.Greater(magazines.arraySize, 0, $"{weapon} magazine renderers");
                Assert.AreEqual(0, prefab.GetComponentsInChildren<Collider>(true).Length, $"{weapon} viewmodel has no colliders");
            }

            foreach (string guid in AssetDatabase.FindAssets("t:AttachmentDataSO", new[] { "Assets/_Project/Data/Weapons/Attachments" }))
            {
                var part = AssetDatabase.LoadAssetAtPath<AttachmentDataSO>(AssetDatabase.GUIDToAssetPath(guid));
                AssertNoPrimitives(part.Prefab);
                Assert.Greater(Triangles(part.Prefab), 30, part.name);
            }
        }

        [Test]
        public void Soldier_HasInvisibleHitboxes_UnderColliderFreeOperatorVisuals()
        {
            foreach (string path in new[] { "Assets/_Project/Prefabs/AI/AI_Soldier.prefab", "Assets/_Project/Prefabs/AI/AI_Soldier_Friendly.prefab" })
            {
                var soldier = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Hitbox[] hitboxes = soldier.GetComponentsInChildren<Hitbox>(true);
                Assert.GreaterOrEqual(hitboxes.Length, 9, "legs, stomach, thorax, head and four arm zones");
                foreach (Hitbox hitbox in hitboxes)
                {
                    Assert.IsNotNull(hitbox.GetComponent<Collider>(), $"{hitbox.name} collider");
                    Assert.IsNull(hitbox.GetComponent<Renderer>(), $"{hitbox.name} is invisible");
                }

                int visuals = 0;
                foreach (Renderer renderer in soldier.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.GetComponentInParent<WeaponModelRig>() != null) continue;
                    Assert.IsNull(renderer.GetComponent<Collider>(), $"{renderer.name} visual must not collide");
                    visuals++;
                }

                Assert.Greater(visuals, 10, "operator visuals on every bone");
                AssertNoPrimitives(soldier);
            }
        }

        [Test]
        public void EquipmentAndProps_AreDetailedModels()
        {
            foreach (string grenade in new[] { "Grenade_M67", "Grenade_M84", "Grenade_M18", "Charge_C4" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Equipment/{grenade}.prefab");
                Assert.IsNotNull(prefab, grenade);
                AssertNoPrimitives(prefab);
                Assert.IsNotNull(prefab.GetComponent<Collider>(), $"{grenade} keeps its physics collider");
            }

            foreach (string prop in new[] { "Prop_AmmoCrate", "Prop_Locker", "Prop_Workbench", "Prop_WeaponRack" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Prefabs/Props/{prop}.prefab");
                Assert.IsNotNull(prefab, prop);
                Assert.Greater(Triangles(prefab), 50, prop);
                Assert.AreEqual(0, prefab.GetComponentsInChildren<Collider>(true).Length, $"{prop} is visual only");
            }
        }

        // ---- Helpers ----

        /// <summary>Muzzle tip, butt end and magazine bottom first, then a dense receiver: a rifle-like point cloud along +X.</summary>
        private static List<Vector3> RiflePoints()
        {
            var points = new List<Vector3> { new Vector3(1f, 0f, 0f), new Vector3(-0.4f, -0.02f, 0f), new Vector3(0.1f, -0.2f, 0f) };
            for (int i = 0; i <= 40; i++)
            {
                float x = Mathf.Lerp(-0.4f, 1f, i / 40f);
                bool stock = x < -0.1f;
                float halfHeight = stock ? 0.06f : x > 0.45f ? 0.01f : 0.035f;
                float halfWidth = stock ? 0.02f : x > 0.45f ? 0.01f : 0.022f;
                foreach (float y in new[] { -halfHeight, halfHeight })
                foreach (float z in new[] { -halfWidth, halfWidth })
                    points.Add(new Vector3(x, y + 0.02f, z));
            }

            for (int i = 0; i < 6; i++) points.Add(new Vector3(0.1f, -0.03f - i * 0.03f, 0f)); // magazine
            return points;
        }

        private static void AssertOutward(Mesh mesh, System.Func<Vector3, Vector3> outwardAt)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Vector3 outward = outwardAt((a + b + c) / 3f);
                Assert.Greater(Vector3.Dot(normal, outward), 0f, $"triangle {i / 3} faces inward");
            }
        }

        private static void AssertNoPrimitives(GameObject root)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                CollectionAssert.DoesNotContain(PrimitiveMeshes, filter.sharedMesh.name, $"{root.name}/{filter.name} still uses a primitive mesh");
            }
        }

        private static int Triangles(GameObject root)
        {
            int count = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) count += (int)(filter.sharedMesh.GetIndexCount(0) / 3);
            }

            return count;
        }
    }
}
#endif
