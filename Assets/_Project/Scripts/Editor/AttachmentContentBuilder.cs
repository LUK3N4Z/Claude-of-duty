using System;
using FPS.Audio;
using FPS.Core;
using FPS.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using static FPS.EditorTools.TestWeaponBuilder;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Generates the gunsmith parts for the modular weapons: 10.5/14.5/20" barrels, flash hider, compensator and
    /// suppressor, 4x scope and red dot, vertical and angled foregrips, fixed and collapsed stocks, laser and weapon light.
    /// Each part is a prefab (carrying its own sockets where it moves them) plus an <see cref="AttachmentDataSO"/>.
    /// Models are imported Meshy assets when present (sockets bound from their measured end slices), otherwise the
    /// procedural parts from <see cref="ProceduralAttachments"/>.
    /// </summary>
    public static class AttachmentContentBuilder
    {
        private const string ArtFolder = "Assets/_Project/Art/Attachments";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Weapons/Attachments";
        private const string DataFolder = "Assets/_Project/Data/Weapons/Attachments";

        internal static string DataPath(string partName) => $"{DataFolder}/{partName}.asset";

        /// <summary>Creates every part and returns the carbine's default build.</summary>
        internal static AttachmentDataSO[] CreateParts(WeaponAudioProfileSO suppressedAudio)
        {
            ProjectSetupWizard.EnsureFolder(ArtFolder);
            ProjectSetupWizard.EnsureFolder(PrefabFolder);
            ProjectSetupWizard.EnsureFolder(DataFolder);

            AttachmentDataSO carbineBarrel = Barrel("Barrel_14in", "14.5\" Carbine Barrel", 14.5f, 0.6f, 0f, 1f, 1f);
            Barrel("Barrel_10in", "10.5\" CQB Barrel", 10.5f, 0.45f, 6f, 1.5f, 0.94f); // more unburnt powder: bigger flash
            Barrel("Barrel_20in", "20\" Marksman Barrel", 20f, 0.85f, -8f, 0.8f, 1.08f);

            AttachmentDataSO flashHider = Attachment("Muzzle_FlashHider", "Birdcage Flash Hider", AttachmentSlot.Muzzle, 0.08f,
                new Vector3(0f, 0f, 0.0275f), 0f, (root, shape) => Socket(root, WeaponModelRig.MuzzleSocketName, shape.Front, Quaternion.identity),
                so => Set(so, "flashMultiplier", 0.25f));
            Attachment("Muzzle_Compensator", "Three-Port Compensator", AttachmentSlot.Muzzle, 0.12f, new Vector3(0f, 0f, 0.03f), -1f,
                (root, shape) => Socket(root, WeaponModelRig.MuzzleSocketName, shape.Front, Quaternion.identity), so =>
                {
                    Set(so, "flashMultiplier", 1.4f); // ports blast gas up and sideways
                    Set(so, "recoilVertical", 0.8f);
                    Set(so, "recoilHorizontal", 0.85f);
                });
            Attachment("Muzzle_Suppressor", "5.56 Suppressor", AttachmentSlot.Muzzle, 0.55f, new Vector3(0f, 0f, 0.085f), -8f,
                (root, shape) => Socket(root, WeaponModelRig.MuzzleSocketName, shape.Front, Quaternion.identity), so =>
                {
                    Set(so, "flashMultiplier", 0f);
                    Set(so, "audioOverride", suppressedAudio);
                    Set(so, "recoilVertical", 0.9f);
                    Set(so, "recoilBack", 0.85f);
                    Set(so, "velocityMultiplier", 1.01f); // the bore effectively continues through the baffles
                    Set(so, "adsTimeMultiplier", 1.05f);
                });

            Mesh disc = DiscMesh();
            AttachmentDataSO scope = Attachment("Optic_Scope4x", "4x Prism Scope", AttachmentSlot.Optic, 0.5f, new Vector3(0f, -0.005f, 0.08f), -4f, (root, shape) =>
            {
                // Eyepiece at the mount (the sight aligner); the lens fills the measured eyepiece.
                float lensRadius = Mathf.Max(0.008f, Mathf.Min(shape.RearHalfExtents.x, shape.RearHalfExtents.y) * (shape.Imported ? 0.8f : 1f));
                MeshRenderer lens = Disc(root, "Ocular Lens", disc, shape.Rear.z - 0.001f, lensRadius, UnlitMaterial("Scope Lens", Color.white, null, false));
                Disc(root, "Reticle", disc, shape.Rear.z - 0.0015f, lensRadius, UnlitMaterial("Scope Reticle", Color.black, ReticleTexture(), true));
                root.gameObject.AddComponent<WeaponSight>().Configure(4f, 0.07f, 1.25f, lens);
            }, so => Set(so, "adsTimeMultiplier", 1.1f));
            Attachment("Optic_RedDot", "Red Dot Sight", AttachmentSlot.Optic, 0.15f, new Vector3(0f, -0.01f, 0.03f), 0f, (root, shape) =>
            {
                // The dot sits on the sight line near the front of the window, so it overlays the point of aim.
                float z = shape.Imported ? shape.Bounds.max.z - 0.006f : 0.044f;
                Transform dot = DotQuad(root, new Vector3(0f, 0f, z));
                dot.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                root.gameObject.AddComponent<WeaponSight>().Configure(1f, 0.1f, 1.15f, null);
            }, so => Set(so, "adsTimeMultiplier", 0.95f));

            Attachment("Grip_Vertical", "Vertical Foregrip", AttachmentSlot.Underbarrel, 0.12f, new Vector3(0f, -0.045f, 0f), 4f,
                // Fist around the post: fingers point down and wrap it.
                (root, shape) => Socket(root, WeaponModelRig.LeftHandIKGripName, new Vector3(0f, shape.Bounds.center.y, shape.Bounds.center.z), Quaternion.Euler(75f, 0f, 0f)),
                so => Set(so, "recoilHorizontal", 0.85f));
            AttachmentDataSO angledGrip = Attachment("Grip_Angled", "Angled Foregrip", AttachmentSlot.Underbarrel, 0.08f, new Vector3(0f, -0.016f, 0f), 6f,
                // Web of the hand against the ramp, palm under the handguard.
                (root, shape) => Socket(root, WeaponModelRig.LeftHandIKGripName,
                    new Vector3(0f, shape.Bounds.min.y * 0.65f, Mathf.Lerp(shape.Bounds.min.z, shape.Bounds.center.z, 0.5f)), Quaternion.Euler(35f, 0f, 0f)),
                so =>
                {
                    Set(so, "recoilVertical", 0.92f);
                    Set(so, "adsTimeMultiplier", 0.95f);
                });

            AttachmentDataSO fixedStock = Attachment("Stock_Fixed", "Fixed Stock", AttachmentSlot.Stock, 0.4f, new Vector3(0f, 0f, -0.12f), 0f,
                (root, shape) => Socket(root, WeaponModelRig.StockPivotSocketName, new Vector3(0f, shape.Rear.y, shape.Bounds.min.z), Quaternion.identity),
                so => Set(so, "recoilBack", 0.9f));
            Attachment("Stock_Collapsed", "Collapsed Stock", AttachmentSlot.Stock, 0.3f, new Vector3(0f, 0f, -0.11f), 4f,
                (root, shape) => Socket(root, WeaponModelRig.StockPivotSocketName, new Vector3(0f, shape.Rear.y, shape.Bounds.min.z), Quaternion.identity), so =>
                {
                    Set(so, "recoilBack", 1.1f);
                    Set(so, "adsTimeMultiplier", 0.95f);
                });

            AttachmentDataSO laser = Attachment("Rail_Laser", "Visible Laser", AttachmentSlot.Laser, 0.09f, new Vector3(0.01f, 0f, 0f), -1f, (root, shape) =>
            {
                Transform emitter = Socket(root, "Emitter", shape.Front, Quaternion.identity);
                root.gameObject.AddComponent<TacticalDevice>().Configure(TacticalDeviceType.Laser, emitter, LaserMaterial());
            });
            AttachmentDataSO light = Attachment("Rail_Light", "Weapon Light", AttachmentSlot.Light, 0.12f, new Vector3(-0.012f, 0f, 0.01f), -1f, (root, shape) =>
            {
                Transform emitter = Socket(root, "Emitter", shape.Front, Quaternion.identity);
                root.gameObject.AddComponent<TacticalDevice>().Configure(TacticalDeviceType.Flashlight, emitter, null);
            });

            return new[] { carbineBarrel, flashHider, scope, angledGrip, fixedStock, laser, light };
        }

        /// <summary>Barrel from the chamber face forward; provides the MuzzleMount and the bare-muzzle socket.</summary>
        private static AttachmentDataSO Barrel(string partName, string displayName, float inches, float massKg, float ergonomics, float flash, float adsTime)
        {
            float length = inches * 0.0254f;
            return Attachment(partName, displayName, AttachmentSlot.Barrel, massKg, new Vector3(0f, 0f, length * 0.5f), ergonomics, (root, shape) =>
            {
                Socket(root, AttachmentDataSO.MountName(AttachmentSlot.Muzzle), shape.Front, Quaternion.identity);
                Socket(root, WeaponModelRig.MuzzleSocketName, shape.Front, Quaternion.identity);
            }, so =>
            {
                Set(so, "barrelLengthInches", inches);
                Set(so, "flashMultiplier", flash);
                Set(so, "adsTimeMultiplier", adsTime);
            }, inches);
        }

        /// <summary>Builds a part model under a temporary root, saves it as a prefab and writes its attachment data.</summary>
        private static AttachmentDataSO Attachment(string partName, string displayName, AttachmentSlot slot, float massKg, Vector3 centerOfMass,
            float ergonomics, Action<Transform, PartShape> sockets, Action<SerializedObject> tune = null, float barrelInches = 0f)
        {
            var root = new GameObject(partName);
            GameObject prefab;
            try
            {
                PartShape shape = ProceduralAttachments.Build(partName, root.transform, barrelInches);
                sockets(root.transform, shape);
                SetLayerRecursively(root.transform, GameLayers.FirstPersonViewmodel);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{partName}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AttachmentDataSO data = LoadOrCreate<AttachmentDataSO>(DataPath(partName));
            Configure(data, so =>
            {
                Set(so, "displayName", displayName);
                SetEnum(so, "slot", (int)slot);
                Set(so, "prefab", prefab);
                Set(so, "massKg", massKg);
                Set(so, "centerOfMass", centerOfMass);
                Set(so, "ergonomicsDelta", ergonomics);
                tune?.Invoke(so);
            });
            return data;
        }

        /// <summary>Unlit red dot: a small disc facing the eye.</summary>
        private static Transform DotQuad(Transform parent, Vector3 position)
        {
            var dot = new GameObject("Dot");
            dot.transform.SetParent(parent, false);
            dot.transform.localPosition = position;
            dot.transform.localScale = new Vector3(0.0015f, 0.0015f, 1f);
            dot.AddComponent<MeshFilter>().sharedMesh = DiscMesh();
            dot.AddComponent<MeshRenderer>().sharedMaterial = UnlitMaterial("Red Dot", new Color(1f, 0.08f, 0.05f), null, false);
            return dot.transform;
        }

        private static Material LaserMaterial()
        {
            string path = $"{ArtFolder}/LaserBeam.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find("HDRP/Unlit")) { name = "LaserBeam" };
            HDMaterial.SetSurfaceType(material, true);
            material.SetFloat("_BlendMode", 1f); // additive
            material.SetColor("_UnlitColor", new Color(1f, 0.06f, 0.04f, 1f));
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
