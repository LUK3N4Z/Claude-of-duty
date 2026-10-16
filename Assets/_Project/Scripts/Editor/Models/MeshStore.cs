using System;
using System.Collections.Generic;
using FPS.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Saves procedural meshes as one asset per piece (Art/Models/Procedural/&lt;Model&gt;/&lt;Piece&gt;.asset). An existing
    /// asset is overwritten in place, so prefabs and scenes that reference it keep working across rebuilds.
    /// </summary>
    internal static class MeshStore
    {
        internal const string Folder = "Assets/_Project/Art/Models/Procedural";

        public static Mesh Save(string model, Mesh built)
        {
            string folder = $"{Folder}/{model}";
            ProjectSetupWizard.EnsureFolder(folder);
            string path = $"{folder}/{built.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(built, path);
                return built;
            }

            existing.Clear();
            existing.indexFormat = built.indexFormat;
            existing.SetVertices(built.vertices);
            existing.SetNormals(built.normals);
            existing.SetTangents(built.tangents);
            existing.SetUVs(0, built.uv);
            existing.subMeshCount = built.subMeshCount;
            for (int i = 0; i < built.subMeshCount; i++) existing.SetTriangles(built.GetTriangles(i), i);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(built);
            return existing;
        }
    }

    /// <summary>
    /// Builds one procedural model: static geometry is merged per surface finish (one renderer per material), moving
    /// parts get their own child transforms and meshes. Every renderer is collider-free and on the given layer.
    /// </summary>
    internal sealed class ModelAssembly
    {
        private readonly string model;
        private readonly Transform root;
        private readonly int layer;
        private readonly Dictionary<SurfaceFinish, MeshKit> statics = new Dictionary<SurfaceFinish, MeshKit>();

        public ModelAssembly(string modelName, Transform parent, int renderLayer)
        {
            model = modelName;
            layer = renderLayer;
            root = new GameObject("Model").transform;
            root.SetParent(parent, false);
            root.gameObject.layer = layer;
        }

        public Transform Root => root;

        /// <summary>The merged static kit for a finish (UVs scaled for it).</summary>
        public MeshKit Static(SurfaceFinish finish)
        {
            if (statics.TryGetValue(finish, out MeshKit kit)) return kit;
            kit = new MeshKit { UvScale = SurfaceFinishes.UvScaleFor(finish) };
            statics[finish] = kit;
            return kit;
        }

        /// <summary>A separately moving part under <paramref name="parent"/> (the model root when null) with its own mesh.</summary>
        public Transform Moving(string partName, Vector3 position, Quaternion rotation, SurfaceFinish finish, Action<MeshKit> build, Transform parent = null)
        {
            var kit = new MeshKit { UvScale = SurfaceFinishes.UvScaleFor(finish) };
            build(kit);
            Transform part = Renderer(partName, parent != null ? parent : root, kit, finish);
            part.SetLocalPositionAndRotation(position, rotation);
            return part;
        }

        /// <summary>A moving part made of several finishes; <paramref name="build"/> receives a kit per finish requested.</summary>
        public Transform MovingGroup(string partName, Vector3 position, Quaternion rotation, Action<Func<SurfaceFinish, MeshKit>> build, Transform parent = null)
        {
            var kits = new Dictionary<SurfaceFinish, MeshKit>();
            build(finish =>
            {
                if (!kits.TryGetValue(finish, out MeshKit kit)) kits[finish] = kit = new MeshKit { UvScale = SurfaceFinishes.UvScaleFor(finish) };
                return kit;
            });

            var group = new GameObject(partName).transform;
            group.SetParent(parent != null ? parent : root, false);
            group.SetLocalPositionAndRotation(position, rotation);
            group.gameObject.layer = layer;
            foreach (KeyValuePair<SurfaceFinish, MeshKit> pair in kits) Renderer($"{partName}_{pair.Key}", group, pair.Value, pair.Key);
            return group;
        }

        /// <summary>Writes the merged static meshes. Call once after all parts are added.</summary>
        public Transform Finish()
        {
            foreach (KeyValuePair<SurfaceFinish, MeshKit> pair in statics)
            {
                if (pair.Value.TriangleCount > 0) Renderer($"Static_{pair.Key}", root, pair.Value, pair.Key);
            }

            return root;
        }

        public int TriangleCount
        {
            get
            {
                int count = 0;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != null) count += (int)filter.sharedMesh.GetIndexCount(0) / 3;
                }

                return count;
            }
        }

        private Transform Renderer(string partName, Transform parent, MeshKit kit, SurfaceFinish finish)
        {
            var part = new GameObject(partName);
            part.layer = layer;
            part.transform.SetParent(parent, false);
            Mesh mesh = MeshStore.Save(model, kit.ToMesh($"{model}_{partName}"));
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = SurfaceFinishes.Get(finish);
            return part.transform;
        }

        /// <summary>Moves every renderer under <paramref name="root"/> onto <paramref name="layer"/> (used after nesting).</summary>
        public static void SetLayer(Transform transform, int layer)
        {
            transform.gameObject.layer = layer;
            for (int i = 0; i < transform.childCount; i++) SetLayer(transform.GetChild(i), layer);
        }

        public static int ViewmodelLayer => GameLayers.FirstPersonViewmodel;
    }
}
