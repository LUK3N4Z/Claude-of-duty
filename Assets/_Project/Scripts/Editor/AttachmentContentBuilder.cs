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
    /// Generates the gunsmith parts for the modular test carbine: 10.5/14.5/20" barrels, flash hider, compensator and
    /// suppressor, 4x scope and red dot, vertical and angled foregrips, fixed and collapsed stocks, laser and weapon light.
    /// Each part is a prefab (carrying its own sockets where it moves them) plus an <see cref="AttachmentDataSO"/>.
    /// </summary>
    public static class AttachmentContentBuilder
    {
        private const string ArtFolder = "Assets/_Project/Art/Attachments";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Weapons/Attachments";
        private const string DataFolder = "Assets/_Project/Data/Weapons/Attachments";

        internal static string DataPath(string partName) => $"{DataFolder}/{partName}.asset";

        /// <summary>Creates every part and returns the carbine's default build.</summary>
        internal static AttachmentDataSO[] CreateParts(Material metal, Material polymer, WeaponAudioProfileSO suppressedAudio)
        {
            ProjectSetupWizard.EnsureFolder(ArtFolder);
            ProjectSetupWizard.EnsureFolder(PrefabFolder);
            ProjectSetupWizard.EnsureFolder(DataFolder);

            AttachmentDataSO carbineBarrel = Barrel("Barrel_14in", "14.5\" Carbine Barrel", 14.5f, 0.6f, 0f, 1f, 1f, metal);
            Barrel("Barrel_10in", "10.5\" CQB Barrel", 10.5f, 0.45f, 6f, 1.5f, 0.94f, metal); // more unburnt powder: bigger flash
            Barrel("Barrel_20in", "20\" Marksman Barrel", 20f, 0.85f, -8f, 0.8f, 1.08f, metal);

            AttachmentDataSO flashHider = Attachment("Muzzle_FlashHider", "Birdcage Flash Hider", AttachmentSlot.Muzzle, 0.08f,
                new Vector3(0f, 0f, 0.0275f), 0f, root =>
                {
                    Part(root, "Body", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.0275f), new Vector3(0.022f, 0.0275f, 0.022f), Quaternion.Euler(90f, 0f, 0f), metal);
                    for (int i = 0; i < 4; i++)
                    {
                        Quaternion around = Quaternion.Euler(0f, 0f, 45f + i * 90f);
                        Part(root, $"Slot {i}", PrimitiveType.Cube, around * new Vector3(0f, 0.0105f, 0f) + new Vector3(0f, 0f, 0.035f),
                            new Vector3(0.004f, 0.002f, 0.03f), around, polymer);
                    }

                    Socket(root, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.055f), Quaternion.identity);
                }, so => Set(so, "flashMultiplier", 0.25f));
            Attachment("Muzzle_Compensator", "Three-Port Compensator", AttachmentSlot.Muzzle, 0.12f, new Vector3(0f, 0f, 0.03f), -1f, root =>
            {
                Part(root, "Body", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.03f), new Vector3(0.026f, 0.03f, 0.026f), Quaternion.Euler(90f, 0f, 0f), metal);
                for (int i = 0; i < 3; i++)
                    Part(root, $"Port {i}", PrimitiveType.Cube, new Vector3(0f, 0.0125f, 0.014f + i * 0.016f), new Vector3(0.018f, 0.002f, 0.008f),
                        Quaternion.identity, polymer);
                Socket(root, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.06f), Quaternion.identity);
            }, so =>
            {
                Set(so, "flashMultiplier", 1.4f); // ports blast gas up and sideways
                Set(so, "recoilVertical", 0.8f);
                Set(so, "recoilHorizontal", 0.85f);
            });
            Attachment("Muzzle_Suppressor", "5.56 Suppressor", AttachmentSlot.Muzzle, 0.55f, new Vector3(0f, 0f, 0.085f), -8f, root =>
            {
                Part(root, "Can", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.085f), new Vector3(0.038f, 0.085f, 0.038f), Quaternion.Euler(90f, 0f, 0f), metal);
                Part(root, "End Cap", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.169f), new Vector3(0.012f, 0.0015f, 0.012f), Quaternion.Euler(90f, 0f, 0f), polymer);
                Socket(root, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.17f), Quaternion.identity);
            }, so =>
            {
                Set(so, "flashMultiplier", 0f);
                Set(so, "audioOverride", suppressedAudio);
                Set(so, "recoilVertical", 0.9f);
                Set(so, "recoilBack", 0.85f);
                Set(so, "velocityMultiplier", 1.01f); // the bore effectively continues through the baffles
                Set(so, "adsTimeMultiplier", 1.05f);
            });

            Mesh disc = DiscMesh();
            AttachmentDataSO scope = Attachment("Optic_Scope4x", "4x Prism Scope", AttachmentSlot.Optic, 0.5f, new Vector3(0f, -0.005f, 0.08f), -4f, root =>
            {
                // Eyepiece at the mount (the sight aligner); the tube extends forward.
                Part(root, "Mount", PrimitiveType.Cube, new Vector3(0f, -0.025f, 0.08f), new Vector3(0.02f, 0.03f, 0.06f), Quaternion.identity, metal);
                Part(root, "Body", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.08f), new Vector3(0.04f, 0.08f, 0.04f), Quaternion.Euler(90f, 0f, 0f), metal);
                const float lensRadius = 0.04f * 0.4125f;
                MeshRenderer lens = Disc(root, "Ocular Lens", disc, -0.001f, lensRadius, UnlitMaterial("Scope Lens", Color.white, null, false));
                Disc(root, "Reticle", disc, -0.0015f, lensRadius, UnlitMaterial("Scope Reticle", Color.black, ReticleTexture(), true));
                root.gameObject.AddComponent<WeaponSight>().Configure(4f, 0.07f, 1.25f, lens);
            }, so => Set(so, "adsTimeMultiplier", 1.1f));
            Attachment("Optic_RedDot", "Red Dot Sight", AttachmentSlot.Optic, 0.15f, new Vector3(0f, -0.01f, 0.03f), 0f, root =>
            {
                Part(root, "Base", PrimitiveType.Cube, new Vector3(0f, -0.024f, 0.03f), new Vector3(0.022f, 0.016f, 0.04f), Quaternion.identity, metal);
                Part(root, "Hood Top", PrimitiveType.Cube, new Vector3(0f, 0.014f, 0.03f), new Vector3(0.03f, 0.004f, 0.03f), Quaternion.identity, metal);
                Part(root, "Hood Bottom", PrimitiveType.Cube, new Vector3(0f, -0.014f, 0.03f), new Vector3(0.03f, 0.004f, 0.03f), Quaternion.identity, metal);
                Part(root, "Hood Left", PrimitiveType.Cube, new Vector3(-0.014f, 0f, 0.03f), new Vector3(0.004f, 0.03f, 0.03f), Quaternion.identity, metal);
                Part(root, "Hood Right", PrimitiveType.Cube, new Vector3(0.014f, 0f, 0.03f), new Vector3(0.004f, 0.03f, 0.03f), Quaternion.identity, metal);
                // The dot sits on the sight line, so it overlays the point of aim from any eye position behind it.
                Transform dot = Part(root, "Dot", PrimitiveType.Quad, new Vector3(0f, 0f, 0.044f), Vector3.one * 0.0015f, Quaternion.identity,
                    UnlitMaterial("Red Dot", new Color(1f, 0.08f, 0.05f), null, false));
                dot.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                root.gameObject.AddComponent<WeaponSight>().Configure(1f, 0.1f, 1.15f, null);
            }, so => Set(so, "adsTimeMultiplier", 0.95f));

            Attachment("Grip_Vertical", "Vertical Foregrip", AttachmentSlot.Underbarrel, 0.12f, new Vector3(0f, -0.045f, 0f), 4f, root =>
            {
                Part(root, "Grip", PrimitiveType.Cylinder, new Vector3(0f, -0.045f, 0f), new Vector3(0.026f, 0.045f, 0.026f), Quaternion.identity, polymer);
                // Fist around the post: fingers point down and wrap it.
                Socket(root, WeaponModelRig.LeftHandIKGripName, new Vector3(0f, -0.045f, 0f), Quaternion.Euler(75f, 0f, 0f));
            }, so => Set(so, "recoilHorizontal", 0.85f));
            AttachmentDataSO angledGrip = Attachment("Grip_Angled", "Angled Foregrip", AttachmentSlot.Underbarrel, 0.08f,
                new Vector3(0f, -0.016f, 0f), 6f, root =>
                {
                    Part(root, "Ramp", PrimitiveType.Cube, new Vector3(0f, -0.014f, 0f), new Vector3(0.022f, 0.02f, 0.075f), Quaternion.Euler(18f, 0f, 0f), polymer);
                    // Web of the hand against the ramp, palm under the handguard.
                    Socket(root, WeaponModelRig.LeftHandIKGripName, new Vector3(0f, -0.022f, -0.02f), Quaternion.Euler(35f, 0f, 0f));
                }, so =>
                {
                    Set(so, "recoilVertical", 0.92f);
                    Set(so, "adsTimeMultiplier", 0.95f);
                });

            AttachmentDataSO fixedStock = Attachment("Stock_Fixed", "Fixed Stock", AttachmentSlot.Stock, 0.4f, new Vector3(0f, 0f, -0.12f), 0f, root =>
            {
                Part(root, "Stock", PrimitiveType.Cube, new Vector3(0f, 0f, -0.12f), new Vector3(0.04f, 0.09f, 0.24f), Quaternion.identity, polymer);
                Part(root, "Butt Pad", PrimitiveType.Cube, new Vector3(0f, 0f, -0.236f), new Vector3(0.042f, 0.1f, 0.01f), Quaternion.identity, polymer);
                Socket(root, WeaponModelRig.StockPivotSocketName, new Vector3(0f, -0.005f, -0.24f), Quaternion.identity);
            }, so => Set(so, "recoilBack", 0.9f));
            Attachment("Stock_Collapsed", "Collapsed Stock", AttachmentSlot.Stock, 0.3f, new Vector3(0f, 0f, -0.11f), 4f, root =>
            {
                Part(root, "Buffer Tube", PrimitiveType.Cylinder, new Vector3(0f, 0.01f, -0.06f), new Vector3(0.028f, 0.06f, 0.028f), Quaternion.Euler(90f, 0f, 0f), metal);
                Part(root, "Stock", PrimitiveType.Cube, new Vector3(0f, -0.005f, -0.135f), new Vector3(0.04f, 0.08f, 0.07f), Quaternion.identity, polymer);
                Socket(root, WeaponModelRig.StockPivotSocketName, new Vector3(0f, -0.005f, -0.17f), Quaternion.identity);
            }, so =>
            {
                Set(so, "recoilBack", 1.1f);
                Set(so, "adsTimeMultiplier", 0.95f);
            });

            AttachmentDataSO laser = Attachment("Rail_Laser", "Visible Laser", AttachmentSlot.Laser, 0.09f, new Vector3(0.01f, 0f, 0f), -1f, root =>
            {
                Part(root, "Housing", PrimitiveType.Cube, new Vector3(0.01f, 0f, 0f), new Vector3(0.018f, 0.02f, 0.05f), Quaternion.identity, polymer);
                Transform emitter = Socket(root, "Emitter", new Vector3(0.01f, 0.004f, 0.026f), Quaternion.identity);
                root.gameObject.AddComponent<TacticalDevice>().Configure(TacticalDeviceType.Laser, emitter, LaserMaterial());
            });
            AttachmentDataSO light = Attachment("Rail_Light", "Weapon Light", AttachmentSlot.Light, 0.12f, new Vector3(-0.012f, 0f, 0.01f), -1f, root =>
            {
                Part(root, "Body", PrimitiveType.Cylinder, new Vector3(-0.012f, 0f, 0.01f), new Vector3(0.025f, 0.05f, 0.025f), Quaternion.Euler(90f, 0f, 0f), metal);
                Part(root, "Bezel", PrimitiveType.Cylinder, new Vector3(-0.012f, 0f, 0.062f), new Vector3(0.03f, 0.004f, 0.03f), Quaternion.Euler(90f, 0f, 0f), metal);
                Transform emitter = Socket(root, "Emitter", new Vector3(-0.012f, 0f, 0.067f), Quaternion.identity);
                root.gameObject.AddComponent<TacticalDevice>().Configure(TacticalDeviceType.Flashlight, emitter, null);
            });

            return new[] { carbineBarrel, flashHider, scope, angledGrip, fixedStock, laser, light };
        }

        /// <summary>Barrel from the chamber face forward; provides the MuzzleMount and the bare-muzzle socket.</summary>
        private static AttachmentDataSO Barrel(string partName, string displayName, float inches, float massKg, float ergonomics,
            float flash, float adsTime, Material metal)
        {
            float length = inches * 0.0254f;
            return Attachment(partName, displayName, AttachmentSlot.Barrel, massKg, new Vector3(0f, 0f, length * 0.5f), ergonomics, root =>
            {
                Part(root, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0f, length * 0.5f), new Vector3(0.02f, length * 0.5f, 0.02f),
                    Quaternion.Euler(90f, 0f, 0f), metal);
                Socket(root, AttachmentDataSO.MountName(AttachmentSlot.Muzzle), new Vector3(0f, 0f, length), Quaternion.identity);
                Socket(root, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, length), Quaternion.identity);
            }, so =>
            {
                Set(so, "barrelLengthInches", inches);
                Set(so, "flashMultiplier", flash);
                Set(so, "adsTimeMultiplier", adsTime);
            });
        }

        /// <summary>Builds a part model under a temporary root, saves it as a prefab and writes its attachment data.</summary>
        private static AttachmentDataSO Attachment(string partName, string displayName, AttachmentSlot slot, float massKg, Vector3 centerOfMass,
            float ergonomics, Action<Transform> build, Action<SerializedObject> tune = null)
        {
            var root = new GameObject(partName);
            GameObject prefab;
            try
            {
                build(root.transform);
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
