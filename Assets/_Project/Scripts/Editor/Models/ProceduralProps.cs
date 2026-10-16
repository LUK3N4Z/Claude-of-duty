using FPS.Core;
using UnityEditor;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Equipment and prop models: an imported Meshy model when its folder holds one, otherwise procedural (M67 with
    /// fuse, spoon and pin ring; perforated M84; M18 canister; wrapped C4 with blasting cap; M57 clacker; wooden ammo
    /// crate; steel locker; gunsmith bench with vise; weapon rack). Props become prefabs under Prefabs/Props so scenes
    /// reference them by stable prefab, and every prop prefab is visual only (scene objects carry the colliders).
    /// </summary>
    internal static class ProceduralProps
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/Props";
        private const int RackCradles = 4;
        /// <summary>Weapons with shorter barrels hang from the rack's top bar instead of standing in a cradle (in).</summary>
        private const float HangingBarrelInches = 8f;

        public static void Equipment(string id, Transform root, int layer)
        {
            if (ImportedModels.Instantiate(MeshyAssetManifest.Find(id), root, out _, out _) is Transform imported)
            {
                ModelAssembly.SetLayer(imported, layer);
                return;
            }

            var model = new ModelAssembly(id, root, layer);
            switch (id)
            {
                case "Grenade_M67": Frag(model); break;
                case "Grenade_M84": Stun(model); break;
                case "Grenade_M18": Smoke(model); break;
                case "Charge_C4": Charge(model); break;
                case "Clacker_M57": Clacker(model); break;
                default: throw new System.ArgumentException($"No procedural model for equipment '{id}'.", nameof(id));
            }

            model.Finish();
        }

        /// <summary>The prop's prefab (built on first use, rebuilt by <paramref name="rebuild"/>); visual only, origin at its centre.</summary>
        public static GameObject Prop(string id, bool rebuild = false)
        {
            string path = $"{PrefabFolder}/{id}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !rebuild) return existing;

            ProjectSetupWizard.EnsureFolder(PrefabFolder);
            var root = new GameObject(id);
            try
            {
                if (ImportedModels.Instantiate(MeshyAssetManifest.Find(id), root.transform, out _, out _) == null)
                {
                    var model = new ModelAssembly(id, root.transform, 0);
                    switch (id)
                    {
                        case "Prop_AmmoCrate": AmmoCrate(model); break;
                        case "Prop_Locker": Locker(model); break;
                        case "Prop_Workbench": Workbench(model); break;
                        case "Prop_WeaponRack": WeaponRack(model); break;
                        default: throw new System.ArgumentException($"No procedural model for prop '{id}'.", nameof(id));
                    }

                    model.Finish();
                }

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Places a prop prefab instance under <paramref name="parent"/> at a local position.</summary>
        public static Transform PlaceProp(string id, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Prop(id), parent);
            instance.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            return instance.transform;
        }

        /// <summary>Hides a placeholder block's cube (its collider and children stay) and puts the prop model in its place.</summary>
        public static Transform Dress(Component block, string id)
        {
            Transform t = block.transform;
            if (t.TryGetComponent(out MeshRenderer meshRenderer)) Object.DestroyImmediate(meshRenderer);
            if (t.TryGetComponent(out MeshFilter meshFilter)) Object.DestroyImmediate(meshFilter);
            return PlaceProp(id, t.parent, t.localPosition, t.localRotation);
        }

        /// <summary>
        /// A weapon rack against a wall (+Z faces the room): solid collider, rack model, and an interaction volume whose
        /// <see cref="FPS.Gameplay.WeaponRack"/> cycles loadout <paramref name="slot"/> through <paramref name="weaponNames"/>.
        /// Each weapon gets a peg: long guns stand in the butt cradles from the right, short ones hang from the top bar.
        /// </summary>
        public static FPS.Gameplay.WeaponRack PlaceWeaponRack(Transform parent, string rackName, Vector3 position, Quaternion rotation, int slot, params string[] weaponNames)
        {
            var rack = new GameObject(rackName) { layer = GameLayers.WorldGeometry };
            rack.transform.SetParent(parent, false);
            rack.transform.SetLocalPositionAndRotation(position, rotation);
            rack.AddComponent<BoxCollider>().size = new Vector3(1.2f, 1.6f, 0.4f);
            GameObjectUtility.SetStaticEditorFlags(rack, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            PlaceProp("Prop_WeaponRack", rack.transform, Vector3.zero, Quaternion.identity);

            var interact = new GameObject("Take weapon (interact)") { layer = GameLayers.Interactable };
            interact.transform.SetParent(rack.transform, false);
            var trigger = interact.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0f, 0.35f);
            trigger.size = new Vector3(1.3f, 1.6f, 0.7f);

            var options = new FPS.Gameplay.WeaponRack.Option[weaponNames.Length];
            int spacing = weaponNames.Length <= 2 ? 2 : 1; // cradles 0.28 m apart: two weapons skip one
            for (int i = 0; i < weaponNames.Length; i++)
            {
                var data = AssetDatabase.LoadAssetAtPath<FPS.Weapons.WeaponDataSO>(TestWeaponBuilder.DataPath(weaponNames[i]));
                int cradle = Mathf.Max(0, RackCradles - 1 - spacing * (weaponNames.Length - 1 - i));
                var peg = new GameObject($"Peg {weaponNames[i]}").transform;
                peg.SetParent(rack.transform, false);
                float x = RackCradleX(cradle);
                if (data != null && data.BarrelLengthInches < HangingBarrelInches)
                    peg.SetLocalPositionAndRotation(new Vector3(x, 0.66f, 0f), Quaternion.Euler(0f, 0f, 180f)); // hangs from the top bar
                else
                    peg.SetLocalPositionAndRotation(new Vector3(x, -0.68f, 0.05f), Quaternion.Euler(-4f, 0f, 0f)); // butt cradle, leaning back

                options[i] = new FPS.Gameplay.WeaponRack.Option
                {
                    model = AssetDatabase.LoadAssetAtPath<FPS.Weapons.WeaponModelRig>(TestWeaponBuilder.PrefabPath(weaponNames[i])),
                    data = data,
                    peg = peg,
                };
            }

            var component = interact.AddComponent<FPS.Gameplay.WeaponRack>();
            component.Configure(slot, options);
            return component;
        }

        // ---- Grenades ----

        private static void Fuse(ModelAssembly model, float top, float bodyRadius)
        {
            MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
            steel.Push(new Vector3(0f, top, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
            steel.Lathe(new[] { new Vector2(0f, -0.004f), new Vector2(0.0095f, -0.004f), new Vector2(0.0095f, 0.012f), new Vector2(0.007f, 0.019f), new Vector2(0f, 0.02f) }, 18);
            steel.Pop();
            // Spoon: down the +X side, hugging the body.
            steel.Box(new Vector3(0.0095f, top + 0.012f, 0f), new Vector3(0.006f, 0.0025f, 0.012f), 0.0008f);
            steel.Profile(new[]
            {
                new Vector2(-0.006f, top + 0.012f), new Vector2(0.006f, top + 0.012f), new Vector2(0.005f, top - bodyRadius * 1.1f), new Vector2(-0.005f, top - bodyRadius * 1.1f),
            }, 0.0012f, bodyRadius * 0.55f + 0.004f);
            steel.Push(new Vector3(-0.012f, top + 0.008f, 0f), Quaternion.LookRotation(Vector3.forward));
            steel.Torus(0.011f, 0.0013f, 20, 6); // pull ring
            steel.Pop();
        }

        private static void Frag(ModelAssembly model)
        {
            MeshKit body = model.Static(SurfaceFinish.GrenadeOliveDrab);
            body.Ellipsoid(Vector3.zero, new Vector3(0.032f, 0.032f, 0.032f), 28, 18);
            MeshKit band = model.Static(SurfaceFinish.PolymerFde);
            band.Ellipsoid(Vector3.zero, new Vector3(0.0322f, 0.0322f, 0.0322f), 28, 2, 6f, 12f); // yellow marking band (read as tan)
            Fuse(model, 0.028f, 0.032f);
        }

        private static void Stun(ModelAssembly model)
        {
            MeshKit body = model.Static(SurfaceFinish.StunBlack);
            body.Push(new Vector3(0f, -0.065f, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
            body.Cylinder(0.0225f, 0f, 0.115f, 28, 0.002f);
            body.Pop();
            MeshKit holes = model.Static(SurfaceFinish.Rubber);
            for (int ring = 0; ring < 4; ring++)
            {
                for (int i = 0; i < 8; i++)
                {
                    float a = (i * 45f + ring * 22.5f) * Mathf.Deg2Rad;
                    holes.Push(new Vector3(Mathf.Cos(a) * 0.0227f, -0.045f + ring * 0.025f, Mathf.Sin(a) * 0.0227f), Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))));
                    holes.Cylinder(0.0035f, -0.0005f, 0.0003f, 10);
                    holes.Pop();
                }
            }

            Fuse(model, 0.05f, 0.0225f);
        }

        private static void Smoke(ModelAssembly model)
        {
            MeshKit body = model.Static(SurfaceFinish.SmokeCanister);
            body.Push(new Vector3(0f, -0.075f, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
            body.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.029f, 0f), new Vector2(0.031f, 0.003f), new Vector2(0.031f, 0.134f), new Vector2(0.029f, 0.137f),
                new Vector2(0.029f, 0.14f), new Vector2(0f, 0.14f),
            }, 30);
            body.Pop();
            MeshKit band = model.Static(SurfaceFinish.ClothOlive);
            band.Push(new Vector3(0f, 0.045f, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
            band.Cylinder(0.0312f, 0f, 0.02f, 30);
            band.Pop();
            MeshKit holes = model.Static(SurfaceFinish.Rubber);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                holes.Push(new Vector3(Mathf.Cos(a) * 0.018f, 0.0655f, Mathf.Sin(a) * 0.018f), Quaternion.LookRotation(Vector3.up));
                holes.Cylinder(0.004f, -0.0005f, 0.0003f, 10);
                holes.Pop();
            }

            Fuse(model, 0.065f, 0.031f);
        }

        private static void Charge(ModelAssembly model)
        {
            MeshKit block = model.Static(SurfaceFinish.GrenadeOliveDrab);
            block.Box(Vector3.zero, new Vector3(0.28f, 0.08f, 0.18f), 0.008f);
            MeshKit label = model.Static(SurfaceFinish.PolymerFde);
            label.Box(new Vector3(0f, 0.0405f, 0f), new Vector3(0.16f, 0.001f, 0.1f), 0.0004f);
            MeshKit putty = model.Static(SurfaceFinish.C4Putty);
            putty.Box(new Vector3(0.1405f, 0f, 0f), new Vector3(0.001f, 0.07f, 0.17f), 0.0004f); // cut end shows the explosive
            MeshKit cap = model.Static(SurfaceFinish.Brass);
            cap.Push(new Vector3(0.06f, 0.04f, 0.03f), Quaternion.LookRotation(Vector3.up));
            cap.Cylinder(0.0035f, -0.02f, 0.012f, 12, 0.0006f); // blasting cap
            cap.Pop();
            MeshKit wire = model.Static(SurfaceFinish.Rubber);
            wire.Push(new Vector3(0.06f, 0.058f, 0.03f), Quaternion.LookRotation(Vector3.up));
            wire.Torus(0.02f, 0.0014f, 24, 6);
            wire.Pop();
            wire.Push(new Vector3(0.06f, 0.056f, 0.03f), Quaternion.LookRotation(Vector3.up));
            wire.Torus(0.016f, 0.0014f, 24, 6);
            wire.Pop();
        }

        private static void Clacker(ModelAssembly model)
        {
            MeshKit body = model.Static(SurfaceFinish.GrenadeOliveDrab);
            body.Profile(new[] { new Vector2(-0.055f, -0.01f), new Vector2(0.055f, -0.01f), new Vector2(0.05f, 0.008f), new Vector2(-0.05f, 0.008f) }, 0.018f);
            body.Profile(new[] { new Vector2(-0.05f, 0.01f), new Vector2(0.045f, 0.012f), new Vector2(0.04f, 0.022f), new Vector2(-0.045f, 0.018f) }, 0.016f); // squeeze handle
            MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
            steel.Box(new Vector3(0f, 0.012f, -0.035f), new Vector3(0.03f, 0.0025f, 0.03f), 0.0006f); // safety bail
            MeshKit rubber = model.Static(SurfaceFinish.Rubber);
            rubber.Push(new Vector3(0f, 0f, -0.055f), Quaternion.LookRotation(Vector3.back));
            rubber.Cylinder(0.008f, 0f, 0.014f, 16, 0.0015f);
            rubber.Pop();
        }

        // ---- Props ----

        private static void AmmoCrate(ModelAssembly model)
        {
            MeshKit wood = model.Static(SurfaceFinish.CrateWood);
            for (int i = 0; i < 4; i++) wood.Box(new Vector3(0f, -0.225f + i * 0.15f, -0.24f), new Vector3(0.78f, 0.14f, 0.02f), 0.004f); // planks
            for (int i = 0; i < 4; i++) wood.Box(new Vector3(0f, -0.225f + i * 0.15f, 0.24f), new Vector3(0.78f, 0.14f, 0.02f), 0.004f);
            wood.Box(new Vector3(-0.39f, 0f, 0f), new Vector3(0.02f, 0.6f, 0.46f), 0.004f);
            wood.Box(new Vector3(0.39f, 0f, 0f), new Vector3(0.02f, 0.6f, 0.46f), 0.004f);
            wood.Box(new Vector3(0f, -0.29f, 0f), new Vector3(0.78f, 0.02f, 0.46f), 0.004f);
            wood.Box(new Vector3(0f, 0.29f, 0f), new Vector3(0.8f, 0.03f, 0.5f), 0.006f); // lid
            wood.Box(new Vector3(0f, 0.31f, 0f), new Vector3(0.7f, 0.012f, 0.06f), 0.003f); // lid batten
            MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
            foreach (float x in new[] { -0.4f, 0.4f })
            foreach (float y in new[] { -0.3f, 0.3f })
            foreach (float z in new[] { -0.25f, 0.25f })
                steel.Box(new Vector3(x, y, z), new Vector3(0.05f, 0.05f, 0.05f), 0.004f); // corner brackets
            MeshKit rope = model.Static(SurfaceFinish.WebbingCoyote);
            foreach (float x in new[] { -0.405f, 0.405f })
            {
                rope.Push(new Vector3(x, 0.1f, 0f), Quaternion.LookRotation(Vector3.right));
                rope.Torus(0.05f, 0.008f, 20, 8);
                rope.Pop();
            }
        }

        private static void Locker(ModelAssembly model)
        {
            MeshKit paint = model.Static(SurfaceFinish.LockerPaint);
            paint.Box(new Vector3(0f, 0f, -0.01f), new Vector3(0.9f, 1.8f, 0.48f), 0.01f);
            paint.Box(new Vector3(-0.222f, 0f, 0.235f), new Vector3(0.44f, 1.74f, 0.012f), 0.004f); // doors
            paint.Box(new Vector3(0.222f, 0f, 0.235f), new Vector3(0.44f, 1.74f, 0.012f), 0.004f);
            MeshKit dark = model.Static(SurfaceFinish.Rubber);
            for (int door = -1; door <= 1; door += 2)
            {
                for (int i = 0; i < 6; i++)
                {
                    dark.Box(new Vector3(door * 0.222f, 0.7f - i * 0.03f, 0.2415f), new Vector3(0.24f, 0.008f, 0.002f), 0.001f); // top vents
                    dark.Box(new Vector3(door * 0.222f, -0.6f - i * 0.03f, 0.2415f), new Vector3(0.24f, 0.008f, 0.002f), 0.001f); // bottom vents
                }
            }

            MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
            steel.Box(new Vector3(-0.02f, 0.05f, 0.25f), new Vector3(0.012f, 0.18f, 0.02f), 0.004f); // handles
            steel.Box(new Vector3(0.02f, 0.05f, 0.25f), new Vector3(0.012f, 0.18f, 0.02f), 0.004f);
            steel.Box(new Vector3(0f, 0.2f, 0.252f), new Vector3(0.05f, 0.03f, 0.008f), 0.002f); // hasp
        }

        private static void Workbench(ModelAssembly model)
        {
            MeshKit top = model.Static(SurfaceFinish.BenchWood);
            top.Box(new Vector3(0f, 0.41f, 0f), new Vector3(2.6f, 0.08f, 1.1f), 0.01f);
            MeshKit steel = model.Static(SurfaceFinish.BenchSteel);
            foreach (float x in new[] { -1.24f, 1.24f })
            foreach (float z in new[] { -0.49f, 0.49f })
                steel.Box(new Vector3(x, -0.04f, z), new Vector3(0.06f, 0.82f, 0.06f), 0.004f); // legs
            steel.Box(new Vector3(0f, -0.33f, 0f), new Vector3(2.48f, 0.03f, 1.0f), 0.004f); // lower shelf
            steel.Box(new Vector3(0f, 0.34f, 0.52f), new Vector3(2.5f, 0.06f, 0.03f), 0.004f); // apron
            steel.Box(new Vector3(0f, 0.34f, -0.52f), new Vector3(2.5f, 0.06f, 0.03f), 0.004f);
            // Vice at the right corner.
            steel.Box(new Vector3(1.05f, 0.5f, -0.42f), new Vector3(0.16f, 0.1f, 0.14f), 0.008f);
            steel.Box(new Vector3(1.05f, 0.57f, -0.36f), new Vector3(0.16f, 0.06f, 0.03f), 0.006f);
            steel.Push(new Vector3(1.05f, 0.53f, -0.52f), Quaternion.LookRotation(Vector3.back));
            steel.Cylinder(0.008f, 0f, 0.08f, 12, 0.001f);
            steel.Pop();
            MeshKit red = model.Static(SurfaceFinish.CerakoteFde);
            red.Box(new Vector3(-0.8f, -0.25f, 0.1f), new Vector3(0.5f, 0.14f, 0.3f), 0.01f); // tool boxes
            red.Box(new Vector3(0.6f, -0.25f, -0.1f), new Vector3(0.4f, 0.14f, 0.28f), 0.01f);
        }

        private static float RackCradleX(int cradle) => -0.42f + cradle * 0.28f;

        private static void WeaponRack(ModelAssembly model)
        {
            MeshKit steel = model.Static(SurfaceFinish.BenchSteel);
            steel.Box(new Vector3(0f, -0.78f, 0f), new Vector3(1.2f, 0.04f, 0.4f), 0.006f);
            foreach (float x in new[] { -0.58f, 0.58f }) steel.Box(new Vector3(x, 0f, -0.15f), new Vector3(0.04f, 1.6f, 0.06f), 0.006f);
            steel.Box(new Vector3(0f, 0.7f, -0.15f), new Vector3(1.2f, 0.04f, 0.06f), 0.006f);
            MeshKit pad = model.Static(SurfaceFinish.Rubber);
            for (int i = 0; i < RackCradles; i++)
            {
                float x = RackCradleX(i);
                pad.Box(new Vector3(x, -0.72f, 0.05f), new Vector3(0.08f, 0.08f, 0.12f), 0.01f); // butt cradles
                pad.Box(new Vector3(x, 0.55f, -0.08f), new Vector3(0.06f, 0.05f, 0.08f), 0.01f); // barrel cradles
            }
        }

        public static int DebrisLayer => GameLayers.Debris;
    }
}
