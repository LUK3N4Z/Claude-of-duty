using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Tactical operator visuals hung on the procedural soldier's bones so they bend with the body and IK: tapered
    /// uniform segments, knee pads and boots on the legs, a plate carrier with magazine and radio pouches on the chest,
    /// a high-cut ballistic helmet with rails, NVG shroud and ear protection, sleeves and gloves on the arms. The
    /// helmet and carrier come from Meshy when imported (Operator_Helmet_*/Operator_PlateCarrier_*). Friendly operators
    /// wear coyote and tan, hostiles ranger green and olive. Visuals carry no colliders: the hitboxes stay separate.
    /// </summary>
    internal static class ProceduralOperators
    {
        internal struct Kit
        {
            public string Name;
            public SurfaceFinish Uniform;
            public SurfaceFinish Gear;
            public SurfaceFinish Helmet;
            public string ImportSuffix;

            public static Kit Friendly => new Kit
            {
                Name = "Operator_Friendly", Uniform = SurfaceFinish.ClothTan, Gear = SurfaceFinish.WebbingCoyote, Helmet = SurfaceFinish.HelmetTan, ImportSuffix = "Tan",
            };

            public static Kit Hostile => new Kit
            {
                Name = "Operator_Hostile", Uniform = SurfaceFinish.ClothOlive, Gear = SurfaceFinish.WebbingRanger, Helmet = SurfaceFinish.HelmetOlive, ImportSuffix = "Olive",
            };
        }

        public static void Legs(Kit kit, Transform legs, int layer)
        {
            var model = new ModelAssembly($"{kit.Name}_Legs", legs, layer);
            MeshKit cloth = model.Static(kit.Uniform);
            foreach (float x in new[] { -0.1f, 0.1f })
            {
                cloth.Push(new Vector3(x, 0f, 0f), Quaternion.LookRotation(Vector3.up, Vector3.forward));
                cloth.Lathe(new[]
                {
                    new Vector2(0.058f, 0.1f), new Vector2(0.068f, 0.32f), new Vector2(0.07f, 0.48f), new Vector2(0.082f, 0.6f),
                    new Vector2(0.098f, 0.84f), new Vector2(0.104f, 1.0f), new Vector2(0f, 1.0f),
                }, 16, true);
                cloth.Pop();
            }

            MeshKit gear = model.Static(kit.Gear);
            gear.Box(new Vector3(0f, 0.96f, 0f), new Vector3(0.4f, 0.06f, 0.24f), 0.02f); // belt
            MeshKit pads = model.Static(SurfaceFinish.Rubber);
            MeshKit boots = model.Static(SurfaceFinish.GloveLeather);
            foreach (float x in new[] { -0.1f, 0.1f })
            {
                pads.Box(new Vector3(x, 0.5f, 0.068f), new Vector3(0.11f, 0.12f, 0.03f), 0.012f);
                boots.Box(new Vector3(x, 0.06f, 0.035f), new Vector3(0.11f, 0.12f, 0.28f), 0.025f);
                boots.Box(new Vector3(x, 0.012f, 0.035f), new Vector3(0.115f, 0.024f, 0.29f), 0.006f); // sole
            }

            model.Finish();
        }

        public static void Torso(Kit kit, Transform torso, int layer)
        {
            var model = new ModelAssembly($"{kit.Name}_Torso", torso, layer);
            MeshKit cloth = model.Static(kit.Uniform);
            cloth.Box(new Vector3(0f, 0.13f, 0f), new Vector3(0.33f, 0.28f, 0.2f), 0.06f);
            model.Finish();
        }

        public static void Chest(Kit kit, Transform chest, int layer)
        {
            var model = new ModelAssembly($"{kit.Name}_Chest", chest, layer);
            MeshKit cloth = model.Static(kit.Uniform);
            cloth.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.39f, 0.32f, 0.22f), 0.07f);
            cloth.Box(new Vector3(0f, 0.2f, 0f), new Vector3(0.13f, 0.08f, 0.13f), 0.03f); // neck

            if (ImportedModels.Instantiate(MeshyAssetManifest.Find($"Operator_PlateCarrier_{kit.ImportSuffix}"), model.Root, out _, out _) == null)
            {
                MeshKit gear = model.Static(kit.Gear);
                gear.Box(new Vector3(0f, 0f, 0.125f), new Vector3(0.31f, 0.31f, 0.035f), 0.01f); // front plate bag
                gear.Box(new Vector3(0f, 0.01f, -0.125f), new Vector3(0.31f, 0.33f, 0.035f), 0.01f); // back plate bag
                gear.Box(new Vector3(0.2f, -0.06f, 0f), new Vector3(0.025f, 0.16f, 0.24f), 0.008f); // cummerbund
                gear.Box(new Vector3(-0.2f, -0.06f, 0f), new Vector3(0.025f, 0.16f, 0.24f), 0.008f);
                foreach (float x in new[] { -0.11f, 0.11f }) gear.Box(new Vector3(x, 0.16f, 0f), new Vector3(0.06f, 0.025f, 0.27f), 0.008f); // shoulder straps
                for (int i = 0; i < 3; i++)
                {
                    float x = -0.08f + i * 0.08f;
                    gear.Box(new Vector3(x, -0.07f, 0.162f), new Vector3(0.07f, 0.13f, 0.045f), 0.008f); // magazine pouches
                    gear.Box(new Vector3(x, 0.0f, 0.168f), new Vector3(0.072f, 0.025f, 0.05f), 0.006f); // flaps
                }

                gear.Box(new Vector3(-0.21f, 0.0f, -0.06f), new Vector3(0.05f, 0.14f, 0.07f), 0.008f); // radio pouch
                MeshKit black = model.Static(SurfaceFinish.Rubber);
                black.Push(new Vector3(-0.21f, 0.07f, -0.07f), Quaternion.LookRotation(Vector3.up));
                black.Cylinder(0.005f, 0f, 0.16f, 8); // antenna
                black.Pop();
                for (int row = 0; row < 3; row++)
                    black.Box(new Vector3(0f, 0.1f - row * 0.04f, -0.1435f), new Vector3(0.26f, 0.012f, 0.002f), 0.001f); // MOLLE rows (back)
            }

            model.Finish();
        }

        public static void Head(Kit kit, Transform head, int layer)
        {
            var model = new ModelAssembly($"{kit.Name}_Head", head, layer);
            MeshKit skin = model.Static(SurfaceFinish.Skin);
            skin.Ellipsoid(new Vector3(0f, -0.005f, 0f), new Vector3(0.095f, 0.118f, 0.108f), 24, 16);
            skin.Box(new Vector3(0f, -0.06f, 0.07f), new Vector3(0.07f, 0.06f, 0.06f), 0.025f); // jaw
            MeshKit glasses = model.Static(SurfaceFinish.BluedSteel);
            glasses.Box(new Vector3(0f, 0.012f, 0.1f), new Vector3(0.15f, 0.035f, 0.02f), 0.008f); // ballistic glasses

            if (ImportedModels.Instantiate(MeshyAssetManifest.Find($"Operator_Helmet_{kit.ImportSuffix}"), model.Root, out _, out _) == null)
            {
                MeshKit shell = model.Static(kit.Helmet);
                shell.Ellipsoid(new Vector3(0f, 0.035f, -0.01f), new Vector3(0.128f, 0.11f, 0.14f), 28, 12, 8f, 90f);
                shell.Ellipsoid(new Vector3(0f, 0.035f, -0.01f), new Vector3(0.12f, 0.102f, 0.132f), 28, 12, 8f, 90f, true);
                MeshKit black = model.Static(SurfaceFinish.PolymerBlack);
                foreach (float x in new[] { -0.124f, 0.124f })
                {
                    black.Box(new Vector3(x, 0.045f, 0f), new Vector3(0.012f, 0.025f, 0.11f), 0.004f); // accessory rails
                    black.Push(new Vector3(x * 0.88f, -0.02f, -0.005f), Quaternion.LookRotation(new Vector3(Mathf.Sign(x), 0f, 0f)));
                    black.Cylinder(0.042f, 0f, 0.028f, 20, 0.006f); // ear protection
                    black.Pop();
                }

                black.Box(new Vector3(0f, 0.09f, 0.115f), new Vector3(0.05f, 0.035f, 0.025f), 0.006f); // NVG shroud
                MeshKit patch = model.Static(kit.Gear);
                patch.Box(new Vector3(0f, 0.09f, -0.135f), new Vector3(0.09f, 0.05f, 0.012f), 0.004f);
            }

            model.Finish();
        }

        /// <summary>Sleeves along the bones' +Z and a gloved hand; shared with the player's first-person arms.</summary>
        public static void Arm(string modelName, SurfaceFinish sleeve, Transform upper, Transform lower, Transform hand, float upperLength, float lowerLength, int layer)
        {
            var upperModel = new ModelAssembly($"{modelName}_Upper", upper, layer);
            upperModel.Static(sleeve).Lathe(new[]
            {
                new Vector2(0f, -0.04f), new Vector2(0.048f, -0.03f), new Vector2(0.054f, 0.02f), new Vector2(0.047f, upperLength * 0.7f),
                new Vector2(0.044f, upperLength), new Vector2(0f, upperLength + 0.01f),
            }, 16, true);
            upperModel.Finish();

            var lowerModel = new ModelAssembly($"{modelName}_Lower", lower, layer);
            lowerModel.Static(sleeve).Lathe(new[]
            {
                new Vector2(0f, -0.03f), new Vector2(0.044f, -0.02f), new Vector2(0.046f, 0.06f), new Vector2(0.04f, lowerLength * 0.55f),
                new Vector2(0.034f, lowerLength * 0.8f), new Vector2(0.031f, lowerLength), new Vector2(0f, lowerLength + 0.005f),
            }, 16, true);
            lowerModel.Finish();

            var handModel = new ModelAssembly($"{modelName}_Hand", hand, layer);
            MeshKit glove = handModel.Static(SurfaceFinish.GloveLeather);
            glove.Box(new Vector3(0f, 0f, 0.035f), new Vector3(0.078f, 0.03f, 0.085f), 0.012f); // palm
            glove.Box(new Vector3(0f, -0.012f, 0.088f), new Vector3(0.074f, 0.022f, 0.035f), 0.009f); // curled fingers
            glove.Box(new Vector3(0f, -0.03f, 0.08f), new Vector3(0.07f, 0.02f, 0.03f), 0.008f);
            glove.Push(new Vector3(-0.038f, -0.008f, 0.03f), Quaternion.Euler(0f, -35f, 0f));
            glove.Box(new Vector3(0f, 0f, 0.025f), new Vector3(0.022f, 0.022f, 0.055f), 0.008f); // thumb
            glove.Pop();
            MeshKit knuckles = handModel.Static(SurfaceFinish.Rubber);
            knuckles.Box(new Vector3(0f, 0.016f, 0.06f), new Vector3(0.06f, 0.006f, 0.02f), 0.003f);
            handModel.Finish();
        }
    }
}
