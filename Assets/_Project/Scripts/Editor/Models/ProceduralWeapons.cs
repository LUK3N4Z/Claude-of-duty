using System.Collections.Generic;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>Renderers and moving parts of a built weapon model, for the rig and the <c>WeaponMechanism</c>.</summary>
    internal struct WeaponVisual
    {
        public Transform Root;
        public Transform Magazine;
        public Renderer[] MagazineRenderers;
        public Transform Trigger;
        public Transform Selector;
        public Transform Reciprocating;
        public Transform ChargingHandle;
        public Transform BoltHandle;
        public Transform[] FoldingSights;
    }

    /// <summary>
    /// High-detail weapon models: an imported Meshy model when its folder holds one, otherwise procedural geometry with
    /// real proportions (M4-pattern carbine receiver, chassis bolt rifle, SCAR-pattern battle rifle, striker pistol).
    /// Moving parts (trigger, selector, bolt carrier or slide, charging handle, bolt handle) are always separate
    /// procedural pieces so the mechanism can animate them; magazines are separate so reloads can hide them.
    /// All coordinates are in the weapon frame: bore on the Z axis at y = 0, +Z forward, +Y up.
    /// </summary>
    internal static class ProceduralWeapons
    {
        // ---- Carbine (modular receiver) ----

        public static WeaponVisual Carbine(Transform weapon, Transform magazineSocket)
        {
            var model = new ModelAssembly(TestWeaponBuilder.CarbineName, weapon, ModelAssembly.ViewmodelLayer);
            bool imported = ImportedModels.Instantiate(MeshyAssetManifest.Find("Carbine_Receiver"), model.Root, out _, out _) != null;
            if (!imported) CarbineReceiver(model);

            var visual = new WeaponVisual { Root = model.Root };
            CarbineMoving(model, ref visual);
            AttachMagazine(TestWeaponBuilder.CarbineName, magazineSocket, ref visual);
            model.Finish();
            return visual;
        }

        private static void CarbineReceiver(ModelAssembly model)
        {
            MeshKit metal = model.Static(SurfaceFinish.AnodizedBlack);
            MeshKit polymer = model.Static(SurfaceFinish.PolymerBlack);
            MeshKit dark = model.Static(SurfaceFinish.ParkerizedSteel);
            MeshKit hole = model.Static(SurfaceFinish.Rubber);

            // Upper receiver with the rail on top.
            metal.Profile(new[]
            {
                new Vector2(-0.15f, -0.008f), new Vector2(0.15f, -0.008f), new Vector2(0.15f, 0.0308f), new Vector2(-0.13f, 0.0308f), new Vector2(-0.15f, 0.022f),
            }, 0.021f);
            metal.Rail(-0.145f, 0.44f, 0.04f);
            hole.Box(new Vector3(0.0212f, 0.008f, 0f), new Vector3(0.0016f, 0.016f, 0.07f), 0.0005f); // ejection port
            metal.Box(new Vector3(0.0225f, 0.02f, -0.042f), new Vector3(0.006f, 0.012f, 0.02f), 0.002f); // brass deflector
            metal.Push(new Vector3(0.016f, 0.014f, -0.072f), Quaternion.LookRotation(new Vector3(0.35f, 0f, -1f)));
            metal.Cylinder(0.0065f, 0f, 0.028f, 16, 0.0015f); // forward assist
            metal.Pop();

            // Lower receiver: magwell, trigger housing, buffer tower.
            metal.Profile(new[]
            {
                new Vector2(-0.15f, -0.008f), new Vector2(0.108f, -0.008f), new Vector2(0.112f, -0.06f), new Vector2(0.026f, -0.06f),
                new Vector2(0.02f, -0.036f), new Vector2(-0.058f, -0.036f), new Vector2(-0.09f, -0.042f), new Vector2(-0.122f, -0.038f), new Vector2(-0.15f, -0.03f),
            }, 0.019f);
            metal.Box(new Vector3(0f, -0.058f, -0.015f), new Vector3(0.012f, 0.004f, 0.062f), 0.0012f); // trigger guard
            metal.Box(new Vector3(0f, -0.047f, 0.014f), new Vector3(0.012f, 0.022f, 0.004f), 0.0012f);
            dark.Push(new Vector3(0.0195f, -0.028f, 0.02f), Quaternion.LookRotation(Vector3.right));
            dark.Cylinder(0.0045f, 0f, 0.003f, 12, 0.001f); // magazine release
            dark.Pop();
            dark.Box(new Vector3(-0.0198f, -0.016f, 0.012f), new Vector3(0.003f, 0.01f, 0.022f), 0.001f); // bolt catch
            metal.Push(new Vector3(0f, -0.015f, -0.15f), Quaternion.identity);
            metal.Lathe(new[] { new Vector2(0f, -0.006f), new Vector2(0.016f, -0.006f), new Vector2(0.017f, -0.003f), new Vector2(0.017f, 0f), new Vector2(0f, 0f) }, 8); // castle nut
            metal.Pop();

            // Pistol grip, raked 15°.
            polymer.Push(new Vector3(0f, -0.036f, -0.066f), Quaternion.Euler(15f, 0f, 0f));
            polymer.Profile(new[]
            {
                new Vector2(0.013f, 0f), new Vector2(0.017f, -0.018f), new Vector2(0.012f, -0.026f), new Vector2(0.017f, -0.036f), new Vector2(0.015f, -0.076f),
                new Vector2(-0.012f, -0.082f), new Vector2(-0.02f, -0.07f), new Vector2(-0.023f, -0.03f), new Vector2(-0.02f, 0f),
            }, 0.0135f);
            polymer.Pop();

            // Free-float handguard: octagonal tube with M-LOK slots, side and bottom rail sections.
            var octagon = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
                octagon[i] = new Vector2(Mathf.Cos(a) * 0.0281f, Mathf.Sin(a) * 0.0281f + 0.006f);
            }

            metal.Prism(octagon, 0.15f, 0.3f);
            for (int i = 0; i < 3; i++)
            {
                float z = 0.18f + i * 0.045f;
                hole.Box(new Vector3(0.0262f, 0.006f, z), new Vector3(0.001f, 0.007f, 0.032f), 0.0006f);
                hole.Box(new Vector3(-0.0262f, 0.006f, z), new Vector3(0.001f, 0.007f, 0.032f), 0.0006f);
            }

            metal.Push(new Vector3(0f, -0.018f, 0f), Quaternion.Euler(0f, 0f, 180f));
            metal.Rail(0.235f, 0.065f, 0.0094f, 0.019f);
            metal.Pop();
            metal.Push(new Vector3(0.024f, 0.006f, 0f), Quaternion.Euler(0f, 0f, -90f));
            metal.Rail(0.225f, 0.07f, 0.0094f, 0.019f);
            metal.Pop();
            metal.Push(new Vector3(-0.024f, 0.006f, 0f), Quaternion.Euler(0f, 0f, 90f));
            metal.Rail(0.225f, 0.07f, 0.0094f, 0.019f);
            metal.Pop();
            metal.Push(new Vector3(0f, 0.006f, 0.15f), Quaternion.identity);
            metal.Tube(0.03f, 0.012f, -0.006f, 0f, 24); // barrel nut
            metal.Pop();
            metal.Push(new Vector3(0f, 0.006f, 0.3f), Quaternion.identity);
            metal.Tube(0.0285f, 0.012f, -0.004f, 0f, 8); // end cap
            metal.Pop();
        }

        private static void CarbineMoving(ModelAssembly model, ref WeaponVisual visual)
        {
            visual.Trigger = model.Moving("Trigger", new Vector3(0f, -0.036f, -0.022f), Quaternion.identity, SurfaceFinish.BluedSteel, kit => kit.Profile(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.004f, -0.004f), new Vector2(0.0065f, -0.012f), new Vector2(0.0045f, -0.019f),
                new Vector2(0.001f, -0.021f), new Vector2(-0.0015f, -0.013f), new Vector2(-0.002f, -0.004f),
            }, 0.003f));
            visual.Selector = model.Moving("Selector", new Vector3(-0.0195f, -0.02f, -0.06f), Quaternion.identity, SurfaceFinish.ParkerizedSteel, kit =>
            {
                kit.Push(Vector3.zero, Quaternion.LookRotation(Vector3.left));
                kit.Cylinder(0.0055f, 0f, 0.003f, 14, 0.0008f);
                kit.Pop();
                kit.Box(new Vector3(-0.0025f, 0f, 0.008f), new Vector3(0.002f, 0.004f, 0.016f), 0.0008f);
            });
            visual.Reciprocating = model.Moving("BoltCarrier", new Vector3(0.0222f, 0.009f, 0.004f), Quaternion.identity, SurfaceFinish.BluedSteel, kit =>
            {
                kit.Box(Vector3.zero, new Vector3(0.0014f, 0.0095f, 0.04f), 0.0006f);
                kit.Box(new Vector3(0f, 0f, 0.022f), new Vector3(0.0016f, 0.006f, 0.006f), 0.0008f); // bolt face
            });
            visual.ChargingHandle = model.Moving("ChargingHandle", new Vector3(0f, 0.025f, -0.15f), Quaternion.identity, SurfaceFinish.AnodizedBlack, kit =>
            {
                kit.Box(new Vector3(0f, 0f, 0.02f), new Vector3(0.008f, 0.006f, 0.04f), 0.001f);
                kit.Box(new Vector3(0f, 0f, -0.004f), new Vector3(0.042f, 0.008f, 0.01f), 0.002f);
                kit.Box(new Vector3(-0.016f, 0.004f, -0.002f), new Vector3(0.01f, 0.004f, 0.012f), 0.001f); // latch
            });
        }

        // ---- Marksman (fixed build: chassis, barrel, 8x scope) ----

        public static WeaponVisual Marksman(Transform weapon, Transform sightSocket, Transform magazineSocket, out Transform scopeRoot)
        {
            var model = new ModelAssembly(TestWeaponBuilder.MarksmanName, weapon, ModelAssembly.ViewmodelLayer);
            bool imported = ImportedModels.Instantiate(MeshyAssetManifest.Find("Marksman_Rifle"), model.Root, out _, out _) != null;
            if (!imported) MarksmanChassis(model);

            var visual = new WeaponVisual { Root = model.Root };
            visual.Trigger = model.Moving("Trigger", new Vector3(0f, -0.036f, -0.022f), Quaternion.identity, SurfaceFinish.BluedSteel, kit => kit.Profile(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.003f, -0.006f), new Vector2(0.005f, -0.014f), new Vector2(0.002f, -0.02f), new Vector2(-0.002f, -0.012f),
            }, 0.0028f));
            visual.BoltHandle = model.MovingGroup("Bolt", new Vector3(0.012f, 0.01f, -0.09f), Quaternion.identity, kit =>
            {
                MeshKit steel = kit(SurfaceFinish.BluedSteel);
                steel.Push(new Vector3(-0.012f, 0f, -0.03f), Quaternion.identity);
                steel.Cylinder(0.0095f, 0f, 0.07f, 18, 0.001f); // bolt body (fluted look via the blued finish)
                steel.Pop();
                steel.Push(Vector3.zero, Quaternion.LookRotation(new Vector3(1f, -0.35f, -0.25f)));
                steel.Cylinder(0.0035f, 0f, 0.045f, 12, 0.0008f); // handle
                steel.Pop();
                MeshKit knob = kit(SurfaceFinish.Rubber);
                knob.Ellipsoid(new Vector3(0.04f, -0.014f, -0.011f), new Vector3(0.009f, 0.009f, 0.009f), 16, 10);
            });

            scopeRoot = new GameObject("Scope").transform;
            scopeRoot.SetParent(sightSocket, false);
            if (ImportedModels.Instantiate(MeshyAssetManifest.Find("Marksman_Scope"), scopeRoot, out _, out _) == null) LongScope(model, scopeRoot, 0.3f, 0.025f, 0.03f);
            AttachMagazine(TestWeaponBuilder.MarksmanName, magazineSocket, ref visual);
            model.Finish();
            return visual;
        }

        private static void MarksmanChassis(ModelAssembly model)
        {
            MeshKit chassis = model.Static(SurfaceFinish.CerakoteFde);
            MeshKit steel = model.Static(SurfaceFinish.BluedSteel);
            MeshKit rubber = model.Static(SurfaceFinish.Rubber);
            MeshKit black = model.Static(SurfaceFinish.AnodizedBlack);

            // Action and chassis body with magwell and forend.
            steel.Push(new Vector3(0f, 0.01f, -0.16f), Quaternion.identity);
            steel.Cylinder(0.017f, 0f, 0.24f, 24, 0.002f); // round receiver
            steel.Pop();
            chassis.Profile(new[]
            {
                new Vector2(-0.16f, -0.006f), new Vector2(0.43f, -0.006f), new Vector2(0.43f, -0.038f), new Vector2(0.11f, -0.04f), new Vector2(0.105f, -0.052f),
                new Vector2(0.015f, -0.052f), new Vector2(0.01f, -0.038f), new Vector2(-0.055f, -0.038f), new Vector2(-0.16f, -0.03f),
            }, 0.026f);
            black.Rail(-0.15f, 0.42f, 0.04f, 0.021f);
            black.Box(new Vector3(0f, 0.028f, 0.06f), new Vector3(0.014f, 0.006f, 0.4f), 0.001f);

            // Barrel: heavy fluted profile and muzzle brake.
            steel.Push(new Vector3(0f, 0f, 0.43f), Quaternion.identity);
            steel.Lathe(new[] { new Vector2(0f, -0.35f), new Vector2(0.013f, -0.35f), new Vector2(0.012f, 0.3f), new Vector2(0.0105f, 0.4f), new Vector2(0f, 0.4f) }, 20);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                rubber.Push(new Vector3(0f, 0f, 0.43f), Quaternion.Euler(0f, 0f, a));
                rubber.Box(new Vector3(0f, 0.0122f, 0.12f), new Vector3(0.0035f, 0.0012f, 0.2f), 0.0004f); // flutes
                rubber.Pop();
            }

            steel.Lathe(new[] { new Vector2(0f, 0.4f), new Vector2(0.015f, 0.4f), new Vector2(0.015f, 0.46f), new Vector2(0f, 0.46f) }, 16); // brake
            steel.Pop();
            rubber.Push(new Vector3(0f, 0f, 0.43f), Quaternion.identity);
            for (int i = 0; i < 3; i++) rubber.Box(new Vector3(0f, 0.0105f, 0.412f + i * 0.016f), new Vector3(0.02f, 0.01f, 0.007f), 0.001f);
            rubber.Pop();

            // Skeleton stock: top tube, lower strut, cheek riser, butt pad.
            black.Push(new Vector3(0f, 0f, -0.16f), Quaternion.identity);
            black.Cylinder(0.011f, -0.27f, 0f, 16, 0.001f);
            black.Pop();
            chassis.Profile(new[]
            {
                new Vector2(-0.16f, -0.03f), new Vector2(-0.43f, -0.07f), new Vector2(-0.43f, -0.058f), new Vector2(-0.18f, -0.02f),
            }, 0.009f);
            chassis.Profile(new[]
            {
                new Vector2(-0.36f, 0.01f), new Vector2(-0.24f, 0.01f), new Vector2(-0.24f, 0.03f), new Vector2(-0.36f, 0.032f),
            }, 0.013f);
            rubber.Box(new Vector3(0f, -0.022f, -0.425f), new Vector3(0.036f, 0.13f, 0.012f), 0.004f);
            rubber.Push(new Vector3(0f, -0.036f, -0.066f), Quaternion.Euler(15f, 0f, 0f));
            rubber.Profile(new[]
            {
                new Vector2(0.013f, 0f), new Vector2(0.016f, -0.03f), new Vector2(0.014f, -0.075f), new Vector2(-0.012f, -0.082f),
                new Vector2(-0.022f, -0.06f), new Vector2(-0.022f, 0f),
            }, 0.0145f);
            rubber.Pop();
            chassis.Box(new Vector3(0f, -0.058f, -0.01f), new Vector3(0.012f, 0.004f, 0.05f), 0.0012f); // trigger guard
        }

        /// <summary>Tube scope built forward from its eyepiece (the sight aligner at the origin).</summary>
        private static void LongScope(ModelAssembly model, Transform scope, float length, float tubeRadius, float objectiveRadius)
        {
            model.MovingGroup("ScopeBody", Vector3.zero, Quaternion.identity, kit =>
            {
                MeshKit body = kit(SurfaceFinish.AnodizedBlack);
                body.Lathe(new[]
                {
                    new Vector2(tubeRadius * 0.9f, 0f), new Vector2(tubeRadius * 1.25f, 0.002f), new Vector2(tubeRadius * 1.25f, 0.05f), new Vector2(tubeRadius * 0.85f, 0.075f),
                    new Vector2(tubeRadius * 0.6f, 0.08f), new Vector2(tubeRadius * 0.6f, length - 0.08f), new Vector2(objectiveRadius, length - 0.045f),
                    new Vector2(objectiveRadius, length), new Vector2(objectiveRadius * 0.85f, length), new Vector2(objectiveRadius * 0.85f, length - 0.004f),
                }, 32, true);
                MeshKit glass = kit(SurfaceFinish.Rubber);
                glass.Lathe(new[] { new Vector2(objectiveRadius * 0.86f, length - 0.004f), new Vector2(0f, length - 0.004f) }, 32); // objective glass
                body.Push(new Vector3(0f, tubeRadius * 0.6f, length * 0.42f), Quaternion.LookRotation(Vector3.up));
                body.Cylinder(0.014f, 0f, 0.022f, 20, 0.0015f); // elevation turret
                body.Pop();
                body.Push(new Vector3(tubeRadius * 0.6f, 0f, length * 0.42f), Quaternion.LookRotation(Vector3.right));
                body.Cylinder(0.013f, 0f, 0.02f, 20, 0.0015f); // windage turret
                body.Pop();
                body.Push(new Vector3(-tubeRadius * 0.6f, 0f, length * 0.42f), Quaternion.LookRotation(Vector3.left));
                body.Cylinder(0.015f, 0f, 0.012f, 20, 0.0015f); // parallax
                body.Pop();
                MeshKit rings = kit(SurfaceFinish.AnodizedBlack);
                foreach (float z in new[] { 0.11f, 0.21f })
                {
                    rings.Push(new Vector3(0f, 0f, z), Quaternion.identity);
                    rings.Tube(tubeRadius * 0.6f + 0.004f, tubeRadius * 0.6f, -0.009f, 0.009f, 24);
                    rings.Pop();
                    rings.Box(new Vector3(0f, -tubeRadius * 0.6f - 0.012f, z), new Vector3(0.022f, 0.022f, 0.018f), 0.002f);
                }
            }, scope);
        }

        // ---- Battle rifle (SCAR-H pattern; optic, underbarrel and rail devices are attachments) ----

        public static WeaponVisual BattleRifle(Transform weapon, Transform magazineSocket)
        {
            var model = new ModelAssembly(TestWeaponBuilder.BattleRifleName, weapon, ModelAssembly.ViewmodelLayer);
            bool imported = ImportedModels.Instantiate(MeshyAssetManifest.Find("BattleRifle"), model.Root, out _, out _) != null;
            if (!imported) BattleRifleBody(model);

            var visual = new WeaponVisual { Root = model.Root };
            if (!imported) visual.FoldingSights = FlipUpIrons(model);
            visual.Trigger = model.Moving("Trigger", new Vector3(0f, -0.038f, -0.03f), Quaternion.identity, SurfaceFinish.BluedSteel, kit => kit.Profile(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.004f, -0.004f), new Vector2(0.006f, -0.012f), new Vector2(0.004f, -0.019f), new Vector2(-0.002f, -0.012f),
            }, 0.003f));
            visual.Selector = model.Moving("Selector", new Vector3(-0.0215f, -0.022f, -0.065f), Quaternion.identity, SurfaceFinish.AnodizedBlack, kit =>
            {
                kit.Push(Vector3.zero, Quaternion.LookRotation(Vector3.left));
                kit.Cylinder(0.006f, 0f, 0.003f, 14, 0.0008f);
                kit.Pop();
                kit.Box(new Vector3(-0.0025f, 0f, 0.009f), new Vector3(0.002f, 0.005f, 0.018f), 0.0008f);
            });
            visual.Reciprocating = model.Moving("BoltCarrier", new Vector3(0.0242f, 0.012f, 0.0f), Quaternion.identity, SurfaceFinish.BluedSteel,
                kit => kit.Box(Vector3.zero, new Vector3(0.0014f, 0.01f, 0.05f), 0.0006f));
            // Reciprocating side charging handle on the left, travelling with the carrier.
            visual.ChargingHandle = model.Moving("ChargingHandle", new Vector3(-0.026f, 0.018f, 0.06f), Quaternion.identity, SurfaceFinish.PolymerBlack, kit =>
            {
                kit.Box(new Vector3(-0.004f, 0f, 0f), new Vector3(0.008f, 0.008f, 0.012f), 0.002f);
                kit.Push(new Vector3(-0.008f, 0f, 0f), Quaternion.LookRotation(Vector3.left));
                kit.Cylinder(0.0055f, 0f, 0.014f, 12, 0.0015f);
                kit.Pop();
            });
            AttachMagazine(TestWeaponBuilder.BattleRifleName, magazineSocket, ref visual);
            model.Finish();
            return visual;
        }

        private static void BattleRifleBody(ModelAssembly model)
        {
            MeshKit fde = model.Static(SurfaceFinish.CerakoteFde);
            MeshKit polymer = model.Static(SurfaceFinish.PolymerFde);
            MeshKit black = model.Static(SurfaceFinish.AnodizedBlack);
            MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
            MeshKit hole = model.Static(SurfaceFinish.Rubber);

            // Monolithic upper, long top rail, side rails.
            fde.Profile(new[]
            {
                new Vector2(-0.16f, -0.006f), new Vector2(0.32f, -0.006f), new Vector2(0.32f, 0.0306f), new Vector2(-0.14f, 0.0306f), new Vector2(-0.16f, 0.02f),
            }, 0.0235f);
            black.Rail(-0.155f, 0.47f, 0.04f);
            fde.Push(new Vector3(0.024f, 0.006f, 0f), Quaternion.Euler(0f, 0f, -90f));
            fde.Rail(0.2f, 0.11f, 0.0094f, 0.019f);
            fde.Pop();
            fde.Push(new Vector3(-0.024f, 0.006f, 0f), Quaternion.Euler(0f, 0f, 90f));
            fde.Rail(0.2f, 0.11f, 0.0094f, 0.019f);
            fde.Pop();
            fde.Push(new Vector3(0f, -0.006f, 0f), Quaternion.Euler(0f, 0f, 180f));
            fde.Rail(0.22f, 0.095f, 0.0094f, 0.019f);
            fde.Pop();
            hole.Box(new Vector3(0.0238f, 0.012f, 0f), new Vector3(0.0012f, 0.016f, 0.08f), 0.0005f); // ejection port
            hole.Box(new Vector3(-0.0238f, 0.018f, 0.0f), new Vector3(0.0012f, 0.006f, 0.24f), 0.0005f); // charging slot

            // Lower: magwell, grip, trigger guard.
            polymer.Profile(new[]
            {
                new Vector2(-0.16f, -0.006f), new Vector2(0.12f, -0.006f), new Vector2(0.125f, -0.062f), new Vector2(0.03f, -0.062f), new Vector2(0.024f, -0.038f),
                new Vector2(-0.07f, -0.038f), new Vector2(-0.16f, -0.03f),
            }, 0.021f);
            polymer.Box(new Vector3(0f, -0.06f, -0.025f), new Vector3(0.013f, 0.004f, 0.07f), 0.0012f);
            polymer.Push(new Vector3(0f, -0.038f, -0.075f), Quaternion.Euler(15f, 0f, 0f));
            polymer.Profile(new[]
            {
                new Vector2(0.013f, 0f), new Vector2(0.017f, -0.03f), new Vector2(0.015f, -0.078f), new Vector2(-0.013f, -0.084f),
                new Vector2(-0.022f, -0.06f), new Vector2(-0.022f, 0f),
            }, 0.0145f);
            polymer.Pop();

            // Barrel, gas block and flash hider (16").
            steel.Push(new Vector3(0f, 0f, 0f), Quaternion.identity);
            steel.Lathe(new[] { new Vector2(0f, 0.0f), new Vector2(0.0115f, 0f), new Vector2(0.0105f, 0.36f), new Vector2(0.0095f, 0.406f), new Vector2(0f, 0.406f) }, 18);
            steel.Lathe(new[] { new Vector2(0f, 0.406f), new Vector2(0.0115f, 0.406f), new Vector2(0.0115f, 0.456f), new Vector2(0f, 0.456f) }, 12);
            steel.Pop();
            hole.Push(new Vector3(0f, 0f, 0.43f), Quaternion.identity);
            for (int i = 0; i < 4; i++)
            {
                hole.Push(Vector3.zero, Quaternion.Euler(0f, 0f, 45f + i * 90f));
                hole.Box(new Vector3(0f, 0.0113f, 0f), new Vector3(0.003f, 0.0012f, 0.03f), 0.0004f);
                hole.Pop();
            }

            hole.Pop();
            fde.Box(new Vector3(0f, 0.008f, 0.33f), new Vector3(0.03f, 0.03f, 0.03f), 0.004f); // gas block

            // Side-folding stock, extended.
            polymer.Profile(new[]
            {
                new Vector2(-0.16f, 0.012f), new Vector2(-0.4f, 0.016f), new Vector2(-0.42f, 0.012f), new Vector2(-0.42f, -0.082f), new Vector2(-0.39f, -0.078f),
                new Vector2(-0.33f, -0.04f), new Vector2(-0.2f, -0.022f), new Vector2(-0.16f, -0.026f),
            }, 0.016f);
            black.Box(new Vector3(0f, -0.032f, -0.415f), new Vector3(0.036f, 0.11f, 0.012f), 0.004f);
            black.Box(new Vector3(0f, 0.021f, -0.3f), new Vector3(0.028f, 0.012f, 0.1f), 0.003f); // cheek riser
        }

        /// <summary>
        /// Flip-up irons on the top rail, each a separate part pivoting at its base (y = 0.04): a rear ghost ring and a
        /// front post on the sight line (y = 0.075). <c>WeaponModelRig.SetSightsFolded</c> lays them flat under an optic.
        /// </summary>
        private static Transform[] FlipUpIrons(ModelAssembly model)
        {
            Transform rear = model.Moving("Rear Iron", new Vector3(0f, 0.04f, -0.12f), Quaternion.identity, SurfaceFinish.AnodizedBlack, kit =>
            {
                kit.Box(new Vector3(0f, 0.015f, 0f), new Vector3(0.022f, 0.03f, 0.012f), 0.002f);
                kit.Push(new Vector3(0f, 0.035f, -0.003f), Quaternion.identity);
                kit.Tube(0.0075f, 0.0028f, 0f, 0.006f, 20); // open aperture centred on the sight line
                kit.Pop();
            });
            Transform front = model.Moving("Front Iron", new Vector3(0f, 0.04f, 0.3f), Quaternion.identity, SurfaceFinish.AnodizedBlack, kit =>
            {
                kit.Box(new Vector3(0f, 0.015f, 0f), new Vector3(0.012f, 0.03f, 0.012f), 0.002f);
                kit.Box(new Vector3(0f, 0.035f, 0f), new Vector3(0.0025f, 0.012f, 0.003f), 0.0005f); // post
            });
            return new[] { rear, front };
        }

        // ---- Sidearm (striker pistol, reciprocating slide) ----

        public static WeaponVisual Sidearm(Transform weapon, Transform magazineSocket)
        {
            var model = new ModelAssembly(TestWeaponBuilder.SidearmName, weapon, ModelAssembly.ViewmodelLayer);
            bool importedFrame = ImportedModels.Instantiate(MeshyAssetManifest.Find("Sidearm_Frame"), model.Root, out _, out _) != null;
            if (!importedFrame) PistolFrame(model);

            var visual = new WeaponVisual { Root = model.Root };
            visual.Trigger = model.Moving("Trigger", new Vector3(0f, -0.016f, 0.008f), Quaternion.identity, SurfaceFinish.PolymerBlack, kit => kit.Profile(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.004f, -0.006f), new Vector2(0.004f, -0.016f), new Vector2(0f, -0.019f), new Vector2(-0.002f, -0.01f),
            }, 0.0035f));
            var slide = new GameObject("Slide").transform;
            slide.SetParent(model.Root, false);
            visual.Reciprocating = slide;
            if (ImportedModels.Instantiate(MeshyAssetManifest.Find("Sidearm_Slide"), slide, out _, out _) == null)
            {
                model.MovingGroup("SlideBody", Vector3.zero, Quaternion.identity, kit =>
                {
                    MeshKit steel = kit(SurfaceFinish.BluedSteel);
                    steel.Profile(new[]
                    {
                        new Vector2(-0.085f, -0.008f), new Vector2(0.1f, -0.008f), new Vector2(0.1f, 0.014f), new Vector2(0.092f, 0.019f),
                        new Vector2(-0.08f, 0.019f), new Vector2(-0.085f, 0.014f),
                    }, 0.0128f);
                    MeshKit black = kit(SurfaceFinish.Rubber);
                    for (int i = 0; i < 6; i++)
                    {
                        black.Box(new Vector3(0.0129f, 0.005f, -0.075f + i * 0.0045f), new Vector3(0.0008f, 0.016f, 0.0015f), 0.0003f); // rear serrations
                        black.Box(new Vector3(-0.0129f, 0.005f, -0.075f + i * 0.0045f), new Vector3(0.0008f, 0.016f, 0.0015f), 0.0003f);
                    }

                    black.Box(new Vector3(0.0129f, 0.01f, 0.0f), new Vector3(0.0008f, 0.01f, 0.03f), 0.0005f); // ejection port
                    black.Box(new Vector3(-0.004f, 0.022f, -0.078f), new Vector3(0.004f, 0.006f, 0.006f), 0.0008f); // rear sight, notch between
                    black.Box(new Vector3(0.004f, 0.022f, -0.078f), new Vector3(0.004f, 0.006f, 0.006f), 0.0008f);
                    black.Box(new Vector3(0f, 0.0215f, 0.088f), new Vector3(0.0035f, 0.005f, 0.004f), 0.0006f); // front sight
                    MeshKit barrel = kit(SurfaceFinish.ParkerizedSteel);
                    barrel.Push(new Vector3(0f, 0f, 0.1f), Quaternion.identity);
                    barrel.Tube(0.0068f, 0.0047f, -0.002f, 0.0005f, 18); // crown in the slide face
                    barrel.Pop();
                }, slide);
            }

            AttachMagazine(TestWeaponBuilder.SidearmName, magazineSocket, ref visual);
            model.Finish();
            return visual;
        }

        private static void PistolFrame(ModelAssembly model)
        {
            MeshKit polymer = model.Static(SurfaceFinish.PolymerBlack);
            polymer.Profile(new[]
            {
                new Vector2(-0.07f, -0.008f), new Vector2(0.095f, -0.008f), new Vector2(0.095f, -0.016f), new Vector2(0.03f, -0.018f),
                new Vector2(0.028f, -0.03f), new Vector2(0.0f, -0.034f), new Vector2(-0.012f, -0.028f), new Vector2(-0.028f, -0.03f),
                new Vector2(-0.04f, -0.118f), new Vector2(-0.074f, -0.118f), new Vector2(-0.082f, -0.03f), new Vector2(-0.082f, -0.012f),
            }, 0.0118f);
            polymer.Box(new Vector3(0f, -0.034f, 0.012f), new Vector3(0.01f, 0.004f, 0.044f), 0.0012f); // trigger guard bottom
            polymer.Push(new Vector3(0f, -0.016f, 0f), Quaternion.Euler(0f, 0f, 180f));
            polymer.Rail(0.055f, 0.035f, 0.004f, 0.018f); // accessory rail
            polymer.Pop();
            MeshKit grip = model.Static(SurfaceFinish.Rubber);
            grip.Box(new Vector3(0.0121f, -0.072f, -0.058f), new Vector3(0.0008f, 0.07f, 0.026f), 0.0004f); // stippled panels
            grip.Box(new Vector3(-0.0121f, -0.072f, -0.058f), new Vector3(0.0008f, 0.07f, 0.026f), 0.0004f);
        }

        // ---- Shared ----

        private static void AttachMagazine(string weapon, Transform socket, ref WeaponVisual visual)
        {
            visual.Magazine = MagazineModel(weapon, socket, ModelAssembly.ViewmodelLayer);
            visual.MagazineRenderers = visual.Magazine.GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>
        /// A weapon's magazine model under a new "Magazine" child of <paramref name="parent"/>: the imported
        /// &lt;Weapon&gt;_Magazine when present, else procedural. Shared by the inserted and the dropped magazine.
        /// </summary>
        internal static Transform MagazineModel(string weapon, Transform parent, int layer)
        {
            var magazine = new GameObject("Magazine").transform;
            magazine.SetParent(parent, false);
            string id = weapon == TestWeaponBuilder.CarbineName ? "Carbine_Magazine" : $"{weapon}_Magazine";
            if (ImportedModels.Instantiate(MeshyAssetManifest.Find(id), magazine, out _, out _) == null)
            {
                var model = new ModelAssembly($"{weapon}_Magazine", magazine, layer);
                if (weapon == TestWeaponBuilder.CarbineName)
                {
                    RifleMagazine(model.Static(SurfaceFinish.PolymerBlack), 0.12f, 0.06f, 0.0125f, 8f);
                    model.Static(SurfaceFinish.Brass).Box(new Vector3(0f, 0.0625f, 0.004f), new Vector3(0.0057f, 0.005f, 0.05f), 0.0025f); // top round
                }
                else if (weapon == TestWeaponBuilder.MarksmanName)
                {
                    MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
                    steel.Box(Vector3.zero, new Vector3(0.03f, 0.07f, 0.09f), 0.003f);
                    steel.Box(new Vector3(0f, -0.037f, 0f), new Vector3(0.034f, 0.006f, 0.094f), 0.002f);
                }
                else if (weapon == TestWeaponBuilder.BattleRifleName)
                {
                    MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
                    steel.Box(Vector3.zero, new Vector3(0.028f, 0.13f, 0.072f), 0.002f);
                    for (int i = 0; i < 4; i++) steel.Box(new Vector3(0f, 0.04f - i * 0.026f, 0f), new Vector3(0.0295f, 0.003f, 0.06f), 0.0008f);
                    steel.Box(new Vector3(0f, -0.068f, 0f), new Vector3(0.032f, 0.006f, 0.078f), 0.002f);
                }
                else
                {
                    MeshKit polymer = model.Static(SurfaceFinish.PolymerBlack);
                    polymer.Box(Vector3.zero, new Vector3(0.022f, 0.1f, 0.03f), 0.002f);
                    polymer.Box(new Vector3(0f, -0.051f, 0f), new Vector3(0.025f, 0.005f, 0.034f), 0.0015f);
                    model.Static(SurfaceFinish.Brass).Box(new Vector3(0f, 0.051f, 0.002f), new Vector3(0.009f, 0.004f, 0.02f), 0.002f);
                }

                model.Finish();
            }

            ModelAssembly.SetLayer(magazine, layer);
            return magazine;
        }

        /// <summary>Curved polymer rifle magazine, feed lips at the top (+Y), ribbed sides, baseplate.</summary>
        internal static void RifleMagazine(MeshKit kit, float height, float depth, float halfWidth, float curveDegrees)
        {
            var outline = new List<Vector2>();
            int steps = 8;
            float bend = curveDegrees * Mathf.Deg2Rad;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float y = height * 0.5f - t * height;
                float z = depth * 0.5f + Mathf.Sin(t * bend) * height * 0.35f;
                outline.Add(new Vector2(z, y));
            }

            for (int i = steps; i >= 0; i--)
            {
                float t = i / (float)steps;
                float y = height * 0.5f - t * height;
                float z = -depth * 0.5f + Mathf.Sin(t * bend) * height * 0.3f;
                outline.Add(new Vector2(z, y));
            }

            kit.Profile(outline, halfWidth);
            for (int i = 0; i < 5; i++)
            {
                float t = 0.25f + i * 0.12f;
                float y = height * 0.5f - t * height;
                float z = Mathf.Sin(t * bend) * height * 0.33f;
                kit.Box(new Vector3(0f, y, z), new Vector3(halfWidth * 2f + 0.0012f, 0.0025f, depth * 0.7f), 0.0006f);
            }

            float bottom = -height * 0.5f;
            kit.Box(new Vector3(0f, bottom - 0.003f, Mathf.Sin(bend) * height * 0.33f), new Vector3(halfWidth * 2f + 0.003f, 0.006f, depth + 0.006f), 0.0018f);
        }
    }
}
