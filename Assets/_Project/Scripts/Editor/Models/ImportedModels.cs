using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    internal enum TextureRole
    {
        BaseColor,
        Normal,
        Metallic,
        Roughness,
        Occlusion,
        /// <summary>glTF-style packed occlusion (R), roughness (G), metallic (B).</summary>
        Orm,
        Emission,
        /// <summary>A mask map this pipeline generated.</summary>
        PackedMask,
    }

    /// <summary>
    /// Meshy (or any) models dropped into Art/Models/&lt;Category&gt;/&lt;AssetId&gt;/: finds the FBX/OBJ, builds an
    /// HDRP/Lit material from its PBR textures (metallic, roughness and occlusion packed into a Mask Map), and
    /// instantiates the model posed in its part frame by <see cref="ModelFitting"/>. A <c>fit.json</c> next to the model
    /// ({"euler": {"x":0,"y":90,"z":0}}) overrides the automatic orientation when the heuristic guesses wrong.
    /// </summary>
    internal static class ImportedModels
    {
        internal const string Root = "Assets/_Project/Art/Models";
        private const string OverrideFile = "fit.json";

        [Serializable]
        private sealed class FitOverride
        {
            public Vector3 euler;
        }

        public static bool TryGetModel(MeshyAssetSpec spec, out GameObject model)
        {
            model = null;
            if (spec == null || !AssetDatabase.IsValidFolder(spec.Folder)) return false;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { spec.Folder }))
            {
                model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (model != null) return true;
            }

            return false;
        }

        public static bool Has(string id) => TryGetModel(MeshyAssetManifest.Find(id), out _);

        public static TextureRole Classify(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            if (name.EndsWith("_maskmap")) return TextureRole.PackedMask;
            if (name.Contains("normal") || name.EndsWith("_nrm") || name.EndsWith("_n")) return TextureRole.Normal;
            if (name.Contains("metallicroughness") || name.Contains("occlusionroughnessmetallic") || name.EndsWith("_orm")) return TextureRole.Orm;
            if (name.Contains("metal")) return TextureRole.Metallic;
            if (name.Contains("rough")) return TextureRole.Roughness;
            if (name.Contains("occlusion") || name.EndsWith("_ao") || name.Contains("ambient")) return TextureRole.Occlusion;
            if (name.Contains("emiss")) return TextureRole.Emission;
            return TextureRole.BaseColor;
        }

        /// <summary>Creates or refreshes <c>&lt;Id&gt;_HDRP.mat</c> in the asset folder; null when the folder has no textures.</summary>
        public static Material EnsureMaterial(MeshyAssetSpec spec) => spec != null ? MaterialFromFolder(spec.Folder, spec.Id) : null;

        /// <summary>
        /// HDRP/Lit material <c>&lt;name&gt;_HDRP.mat</c> built from the PBR textures in <paramref name="folder"/> (base
        /// colour, normal, and metallic/roughness/occlusion or ORM packed into a Mask Map); null without a base colour map.
        /// </summary>
        public static Material MaterialFromFolder(string folder, string name)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return null;
            var byRole = new Dictionary<TextureRole, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureRole role = Classify(path);
                if (!byRole.TryGetValue(role, out string existing) || Preferred(role, path, existing)) byRole[role] = path;
            }

            if (!byRole.ContainsKey(TextureRole.BaseColor)) return null;

            string materialPath = $"{folder}/{name}_HDRP.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            bool created = material == null;
            if (created) material = new Material(Shader.Find("HDRP/Lit")) { name = $"{name}_HDRP" };

            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(byRole[TextureRole.BaseColor]));
            material.SetTexture("_NormalMap", byRole.TryGetValue(TextureRole.Normal, out string normal) ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null);

            byRole.TryGetValue(TextureRole.Metallic, out string metallic);
            byRole.TryGetValue(TextureRole.Roughness, out string roughness);
            byRole.TryGetValue(TextureRole.Occlusion, out string occlusion);
            byRole.TryGetValue(TextureRole.Orm, out string orm);
            Texture2D mask = metallic != null || roughness != null || occlusion != null || orm != null
                ? MaskMapPacker.BuildFromFiles(metallic, roughness, occlusion, orm, $"{folder}/{name}_MaskMap.png")
                : null;
            material.SetTexture("_MaskMap", mask);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.4f);
            material.SetFloat("_SmoothnessRemapMin", 0f);
            material.SetFloat("_SmoothnessRemapMax", 1f);
            material.SetFloat("_MetallicRemapMin", 0f);
            material.SetFloat("_MetallicRemapMax", 1f);
            material.SetFloat("_AORemapMin", 0f);
            material.SetFloat("_AORemapMax", 1f);
            if (byRole.TryGetValue(TextureRole.Emission, out string emission))
            {
                material.SetTexture("_EmissiveColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emission));
                material.SetColor("_EmissiveColor", Color.white);
            }

            HDMaterial.ValidateMaterial(material);
            if (created) AssetDatabase.CreateAsset(material, materialPath);
            else EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Instantiates the imported model under a new "Model" child of <paramref name="parent"/>, posed by the spec's
        /// fit. <paramref name="points"/> receives every vertex in the parent's space (for socket placement).
        /// Returns null when the asset folder holds no model.
        /// </summary>
        public static Transform Instantiate(MeshyAssetSpec spec, Transform parent, out List<Vector3> points, out Bounds bounds)
        {
            points = null;
            bounds = default;
            if (!TryGetModel(spec, out GameObject model)) return null;

            Material material = EnsureMaterial(spec);
            var instance = (GameObject)Object.Instantiate(model);
            instance.name = spec.Id;
            instance.transform.SetPositionAndRotation(Vector3.zero, instance.transform.rotation);
            foreach (Component component in instance.GetComponentsInChildren<Component>(true))
            {
                if (component is Collider || component is Animator || component is Animation || component is Camera || component is Light)
                    Object.DestroyImmediate(component);
            }

            List<Vector3> raw = Vertices(instance.transform);
            Quaternion? orientation = ReadOverride(spec);
            Matrix4x4 pose = ModelFitting.Solve(raw, spec.Fit, orientation, out bounds);

            var container = new GameObject("Model").transform;
            container.SetParent(parent, false);
            container.SetLocalPositionAndRotation(pose.GetPosition(), pose.rotation);
            container.localScale = pose.lossyScale;
            instance.transform.SetParent(container, false);

            if (material != null)
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                }
            }

            points = ModelFitting.Transform(raw, pose);
            return container;
        }

        internal static List<Vector3> Vertices(Transform root)
        {
            // The unparented instance sits at the origin, so world space is the frame the container will pose.
            var points = new List<Vector3>(4096);
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                Append(points, filter.sharedMesh, filter.transform.localToWorldMatrix);
            foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Append(points, skinned.sharedMesh, skinned.transform.localToWorldMatrix);
            return points;
        }

        private static void Append(List<Vector3> points, Mesh mesh, Matrix4x4 transform)
        {
            if (mesh == null) return;
            if (!mesh.isReadable)
            {
                // Without Read/Write the vertex data is off limits: fall back to the bounds corners.
                Bounds b = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                    points.Add(transform.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f))));
                return;
            }

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) points.Add(transform.MultiplyPoint3x4(vertices[i]));
        }

        private static Quaternion? ReadOverride(MeshyAssetSpec spec)
        {
            string path = MaskMapPacker.FullPath($"{spec.Folder}/{OverrideFile}");
            if (!File.Exists(path)) return null;
            FitOverride fit = JsonUtility.FromJson<FitOverride>(File.ReadAllText(path));
            return fit != null ? Quaternion.Euler(fit.euler) : (Quaternion?)null;
        }

        private static bool Preferred(TextureRole role, string candidate, string current)
        {
            if (role != TextureRole.BaseColor) return string.CompareOrdinal(candidate, current) < 0;
            bool Named(string path)
            {
                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                return name.Contains("base") || name.Contains("albedo") || name.Contains("diffuse") || name.Contains("color");
            }

            return Named(candidate) && !Named(current);
        }
    }

    /// <summary>Import settings for everything under Art/Models, and material refresh when an asset folder changes.</summary>
    internal sealed class ImportedModelPostprocessor : AssetPostprocessor
    {
        private bool InModelFolders =>
            assetPath.StartsWith(ImportedModels.Root + "/") &&
            !assetPath.StartsWith(SurfaceFinishes.Folder + "/") &&
            !assetPath.StartsWith(MeshStore.Folder + "/");

        private void OnPreprocessModel()
        {
            if (!InModelFolders) return;
            var importer = (ModelImporter)assetImporter;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
        }

        private void OnPreprocessTexture()
        {
            if (!InModelFolders) return;
            var importer = (TextureImporter)assetImporter;
            TextureRole role = ImportedModels.Classify(assetPath);
            importer.textureType = role == TextureRole.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = role == TextureRole.BaseColor || role == TextureRole.Emission;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var touched = new List<MeshyAssetSpec>();
            foreach (string path in imported)
            {
                if (!path.StartsWith(ImportedModels.Root + "/") || path.EndsWith("_MaskMap.png") || path.EndsWith("_HDRP.mat")) continue;
                foreach (MeshyAssetSpec spec in MeshyAssetManifest.Specs)
                {
                    if (path.StartsWith(spec.Folder + "/") && !touched.Contains(spec)) touched.Add(spec);
                }
            }

            if (touched.Count == 0) return;
            EditorApplication.delayCall += () =>
            {
                foreach (MeshyAssetSpec spec in touched) ImportedModels.EnsureMaterial(spec);
                AssetDatabase.SaveAssets();
            };
        }
    }
}
