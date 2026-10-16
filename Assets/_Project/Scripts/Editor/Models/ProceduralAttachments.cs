using System.Collections.Generic;
using FPS.Core;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>Where a built part's sockets belong, measured from its geometry (part frame).</summary>
    internal struct PartShape
    {
        public bool Imported;
        public Bounds Bounds;
        /// <summary>Centre of the front (+Z) end: muzzle crown, emitter, objective.</summary>
        public Vector3 Front;
        /// <summary>Centre of the rear (-Z) end: thread, eyepiece, butt pad.</summary>
        public Vector3 Rear;
        /// <summary>Half extents of the rear end slice (eyepiece size for the PiP lens).</summary>
        public Vector2 RearHalfExtents;
    }

    /// <summary>
    /// Gunsmith part models in their part frames (origin at the mount): an imported Meshy model when present, with
    /// sockets measured from its end slices, otherwise procedural parts with machined detail (barrel profiles and gas
    /// block, birdcage slots, compensator ports, knurled suppressor, ACOG-pattern prism scope, tube red dot, ribbed and
    /// wedge foregrips, A2 and CTR stocks, PEQ-pattern laser, weapon light with bezel and lens).
    /// </summary>
    internal static class ProceduralAttachments
    {
        public static PartShape Build(string id, Transform root, float barrelInches = 0f)
        {
            MeshyAssetSpec spec = MeshyAssetManifest.Find(id);
            Transform imported = ImportedModels.Instantiate(spec, root, out List<Vector3> points, out Bounds bounds);
            if (imported != null)
            {
                ModelAssembly.SetLayer(imported, GameLayers.FirstPersonViewmodel);
                Vector3 front = ModelFitting.EndSliceCenter(points, 2, true, out _);
                Vector3 rear = ModelFitting.EndSliceCenter(points, 2, false, out Vector2 rearHalf);
                return new PartShape { Imported = true, Bounds = bounds, Front = front, Rear = rear, RearHalfExtents = rearHalf };
            }

            var model = new ModelAssembly(id, root, GameLayers.FirstPersonViewmodel);
            PartShape shape;
            switch (id)
            {
                case "Barrel_10in":
                case "Barrel_14in":
                case "Barrel_20in":
                    shape = Barrel(model, barrelInches * 0.0254f);
                    break;
                case "Muzzle_FlashHider": shape = FlashHider(model); break;
                case "Muzzle_Compensator": shape = Compensator(model); break;
                case "Muzzle_Suppressor": shape = Suppressor(model); break;
                case "Optic_Scope4x": shape = PrismScope(model); break;
                case "Optic_RedDot": shape = RedDot(model); break;
                case "Grip_Vertical": shape = VerticalGrip(model); break;
                case "Grip_Angled": shape = AngledGrip(model); break;
                case "Stock_Fixed": shape = FixedStock(model); break;
                case "Stock_Collapsed": shape = CollapsedStock(model); break;
                case "Rail_Laser": shape = Laser(model); break;
                case "Rail_Light": shape = Light(model); break;
                default: throw new System.ArgumentException($"No procedural model for attachment '{id}'.", nameof(id));
            }

            model.Finish();
            return shape;
        }

        private static PartShape Shape(Vector3 front, Vector3 rear, Vector2 rearHalf, Vector3 center, Vector3 size) =>
            new PartShape { Front = front, Rear = rear, RearHalfExtents = rearHalf, Bounds = new Bounds(center, size) };

        private static PartShape Barrel(ModelAssembly model, float length)
        {
            MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
            float gas = Mathf.Min(length * 0.48f, 0.19f);
            steel.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.0105f, 0f), new Vector2(0.0105f, 0.028f), new Vector2(0.0098f, 0.032f), new Vector2(0.0094f, gas - 0.01f),
                new Vector2(0.0084f, gas + 0.03f), new Vector2(0.0084f, length - 0.014f), new Vector2(0.0072f, length - 0.012f), new Vector2(0.0072f, length),
                new Vector2(0.0029f, length), new Vector2(0.0029f, length - 0.002f), new Vector2(0f, length - 0.002f),
            }, 22);
            MeshKit block = model.Static(SurfaceFinish.AnodizedBlack);
            block.Push(new Vector3(0f, 0f, gas), Quaternion.identity);
            block.Cylinder(0.0145f, -0.01f, 0.012f, 20, 0.0015f);
            block.Pop();
            block.Box(new Vector3(0f, 0.012f, gas + 0.001f), new Vector3(0.016f, 0.012f, 0.018f), 0.002f);
            block.Push(new Vector3(0f, 0.013f, 0.03f), Quaternion.identity);
            block.Cylinder(0.0024f, 0f, gas - 0.03f, 10); // gas tube
            block.Pop();
            return Shape(new Vector3(0f, 0f, length), Vector3.zero, new Vector2(0.0105f, 0.0105f), new Vector3(0f, 0.003f, length * 0.5f), new Vector3(0.03f, 0.036f, length));
        }

        private static PartShape FlashHider(ModelAssembly model)
        {
            MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
            steel.Lathe(new[]
            {
                new Vector2(0.0042f, 0f), new Vector2(0.0098f, 0f), new Vector2(0.0108f, 0.004f), new Vector2(0.0108f, 0.052f), new Vector2(0.0098f, 0.055f),
                new Vector2(0.0068f, 0.055f), new Vector2(0.0068f, 0.02f), new Vector2(0.0042f, 0.02f),
            }, 24);
            MeshKit hole = model.Static(SurfaceFinish.Rubber);
            for (int i = 0; i < 5; i++)
            {
                hole.Push(Vector3.zero, Quaternion.Euler(0f, 0f, 90f + i * 72f));
                hole.Box(new Vector3(0f, 0.0106f, 0.037f), new Vector3(0.0034f, 0.0012f, 0.03f), 0.0005f);
                hole.Pop();
            }

            return Shape(new Vector3(0f, 0f, 0.055f), Vector3.zero, new Vector2(0.01f, 0.01f), new Vector3(0f, 0f, 0.0275f), new Vector3(0.022f, 0.022f, 0.055f));
        }

        private static PartShape Compensator(ModelAssembly model)
        {
            MeshKit steel = model.Static(SurfaceFinish.BluedSteel);
            steel.Lathe(new[]
            {
                new Vector2(0.0042f, 0f), new Vector2(0.012f, 0f), new Vector2(0.0128f, 0.003f), new Vector2(0.0128f, 0.057f), new Vector2(0.0118f, 0.06f),
                new Vector2(0.0045f, 0.06f), new Vector2(0.0045f, 0.055f), new Vector2(0.0042f, 0.055f),
            }, 24);
            MeshKit hole = model.Static(SurfaceFinish.Rubber);
            for (int i = 0; i < 3; i++)
            {
                float z = 0.016f + i * 0.015f;
                hole.Box(new Vector3(0f, 0.0125f, z), new Vector3(0.016f, 0.0012f, 0.008f), 0.0006f);
                hole.Box(new Vector3(0.0125f, 0.003f, z), new Vector3(0.0012f, 0.01f, 0.007f), 0.0006f);
                hole.Box(new Vector3(-0.0125f, 0.003f, z), new Vector3(0.0012f, 0.01f, 0.007f), 0.0006f);
            }

            return Shape(new Vector3(0f, 0f, 0.06f), Vector3.zero, new Vector2(0.012f, 0.012f), new Vector3(0f, 0f, 0.03f), new Vector3(0.026f, 0.026f, 0.06f));
        }

        private static PartShape Suppressor(ModelAssembly model)
        {
            MeshKit can = model.Static(SurfaceFinish.CerakoteFde);
            can.Lathe(new[]
            {
                new Vector2(0.0158f, 0.03f), new Vector2(0.019f, 0.033f), new Vector2(0.019f, 0.164f), new Vector2(0.0165f, 0.17f),
                new Vector2(0.0042f, 0.17f), new Vector2(0.0042f, 0.165f), new Vector2(0f, 0.165f),
            }, 32, true);
            MeshKit mount = model.Static(SurfaceFinish.AnodizedBlack);
            mount.Lathe(new[] { new Vector2(0.0045f, 0f), new Vector2(0.0158f, 0f), new Vector2(0.0158f, 0.031f), new Vector2(0.0045f, 0.031f) }, 28);
            for (int i = 0; i < 12; i++)
            {
                mount.Push(Vector3.zero, Quaternion.Euler(0f, 0f, i * 30f));
                mount.Box(new Vector3(0f, 0.0162f, 0.015f), new Vector3(0.0028f, 0.0012f, 0.022f), 0.0004f); // knurling
                mount.Pop();
            }

            return Shape(new Vector3(0f, 0f, 0.17f), Vector3.zero, new Vector2(0.016f, 0.016f), new Vector3(0f, 0f, 0.085f), new Vector3(0.038f, 0.038f, 0.17f));
        }

        /// <summary>ACOG-pattern prism scope; the eyepiece (sight aligner) is the origin, the body extends forward.</summary>
        private static PartShape PrismScope(ModelAssembly model)
        {
            MeshKit body = model.Static(SurfaceFinish.AnodizedBlack);
            body.Lathe(new[]
            {
                new Vector2(0.0168f, 0f), new Vector2(0.0185f, 0.001f), new Vector2(0.0185f, 0.022f), new Vector2(0.014f, 0.032f),
            }, 28, true);
            body.Box(new Vector3(0f, 0.001f, 0.07f), new Vector3(0.034f, 0.036f, 0.08f), 0.006f); // prism housing
            body.Lathe(new[]
            {
                new Vector2(0.016f, 0.104f), new Vector2(0.0205f, 0.12f), new Vector2(0.0205f, 0.16f), new Vector2(0.0175f, 0.16f), new Vector2(0.0175f, 0.156f), new Vector2(0f, 0.156f),
            }, 28, true);
            body.Box(new Vector3(0f, 0.025f, 0.07f), new Vector3(0.012f, 0.012f, 0.07f), 0.003f); // fiber housing
            body.Box(new Vector3(0f, -0.025f, 0.08f), new Vector3(0.02f, 0.03f, 0.06f), 0.003f); // mount
            body.Box(new Vector3(0f, -0.036f, 0.08f), new Vector3(0.028f, 0.008f, 0.062f), 0.002f); // rail clamp
            MeshKit knob = model.Static(SurfaceFinish.ParkerizedSteel);
            knob.Push(new Vector3(0.016f, -0.034f, 0.08f), Quaternion.LookRotation(Vector3.right));
            knob.Cylinder(0.0065f, 0f, 0.008f, 14, 0.001f); // throw-lever nut
            knob.Pop();
            return Shape(new Vector3(0f, 0f, 0.16f), Vector3.zero, new Vector2(0.0168f, 0.0168f), new Vector3(0f, -0.005f, 0.08f), new Vector3(0.041f, 0.08f, 0.16f));
        }

        /// <summary>Tube red dot: the sight line runs through the open tube at y = 0.</summary>
        private static PartShape RedDot(ModelAssembly model)
        {
            MeshKit body = model.Static(SurfaceFinish.AnodizedBlack);
            body.Push(new Vector3(0f, 0f, 0f), Quaternion.identity);
            body.Tube(0.0155f, 0.0128f, 0.012f, 0.05f, 28);
            body.Pop();
            body.Box(new Vector3(0f, -0.022f, 0.031f), new Vector3(0.022f, 0.014f, 0.036f), 0.002f); // base
            body.Box(new Vector3(0f, -0.031f, 0.031f), new Vector3(0.028f, 0.006f, 0.04f), 0.0015f); // clamp
            body.Push(new Vector3(0.0155f, 0f, 0.028f), Quaternion.LookRotation(Vector3.right));
            body.Cylinder(0.0075f, 0f, 0.008f, 18, 0.0012f); // brightness dial
            body.Pop();
            body.Push(new Vector3(0f, 0.0155f, 0.028f), Quaternion.LookRotation(Vector3.up));
            body.Cylinder(0.006f, 0f, 0.005f, 16, 0.001f); // elevation cap
            body.Pop();
            return Shape(new Vector3(0f, 0f, 0.05f), new Vector3(0f, 0f, 0.012f), new Vector2(0.0128f, 0.0128f), new Vector3(0f, -0.008f, 0.031f), new Vector3(0.039f, 0.047f, 0.04f));
        }

        private static PartShape VerticalGrip(ModelAssembly model)
        {
            MeshKit polymer = model.Static(SurfaceFinish.PolymerBlack);
            polymer.Push(Vector3.zero, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            polymer.Lathe(new[]
            {
                new Vector2(0f, 0.004f), new Vector2(0.0125f, 0.004f), new Vector2(0.0135f, 0.03f), new Vector2(0.0125f, 0.06f), new Vector2(0.014f, 0.085f),
                new Vector2(0.0145f, 0.09f), new Vector2(0f, 0.092f),
            }, 24, true);
            for (int i = 0; i < 6; i++)
            {
                polymer.Push(Vector3.zero, Quaternion.Euler(0f, 0f, i * 60f));
                polymer.Box(new Vector3(0f, 0.0132f, 0.045f), new Vector3(0.004f, 0.0016f, 0.06f), 0.0006f); // finger ribs
                polymer.Pop();
            }

            polymer.Pop();
            polymer.Box(new Vector3(0f, -0.003f, 0f), new Vector3(0.026f, 0.007f, 0.04f), 0.002f); // rail clamp
            return Shape(new Vector3(0f, -0.045f, 0.0145f), new Vector3(0f, -0.045f, -0.0145f), new Vector2(0.0145f, 0.045f),
                new Vector3(0f, -0.046f, 0f), new Vector3(0.029f, 0.092f, 0.04f));
        }

        private static PartShape AngledGrip(ModelAssembly model)
        {
            MeshKit polymer = model.Static(SurfaceFinish.PolymerBlack);
            polymer.Profile(new[]
            {
                new Vector2(-0.038f, 0f), new Vector2(0.038f, 0f), new Vector2(0.038f, -0.006f), new Vector2(0.03f, -0.03f),
                new Vector2(0.018f, -0.033f), new Vector2(-0.012f, -0.017f), new Vector2(-0.034f, -0.008f),
            }, 0.011f);
            polymer.Box(new Vector3(0f, -0.0025f, 0f), new Vector3(0.026f, 0.005f, 0.074f), 0.0015f);
            return Shape(new Vector3(0f, -0.015f, 0.038f), new Vector3(0f, -0.008f, -0.038f), new Vector2(0.011f, 0.008f),
                new Vector3(0f, -0.016f, 0f), new Vector3(0.026f, 0.033f, 0.076f));
        }

        private static PartShape FixedStock(ModelAssembly model)
        {
            MeshKit polymer = model.Static(SurfaceFinish.PolymerBlack);
            polymer.Profile(new[]
            {
                new Vector2(0f, 0.018f), new Vector2(0f, -0.026f), new Vector2(-0.06f, -0.036f), new Vector2(-0.234f, -0.056f),
                new Vector2(-0.234f, 0.046f), new Vector2(-0.07f, 0.026f),
            }, 0.019f);
            MeshKit rubber = model.Static(SurfaceFinish.Rubber);
            rubber.Box(new Vector3(0f, -0.005f, -0.239f), new Vector3(0.04f, 0.104f, 0.01f), 0.003f); // butt plate
            MeshKit steel = model.Static(SurfaceFinish.ParkerizedSteel);
            steel.Push(new Vector3(0f, -0.06f, -0.2f), Quaternion.LookRotation(Vector3.right));
            steel.Torus(0.008f, 0.0015f, 14, 6); // sling swivel
            steel.Pop();
            return Shape(new Vector3(0f, -0.004f, 0f), new Vector3(0f, -0.005f, -0.244f), new Vector2(0.02f, 0.052f),
                new Vector3(0f, -0.005f, -0.122f), new Vector3(0.04f, 0.104f, 0.244f));
        }

        private static PartShape CollapsedStock(ModelAssembly model)
        {
            MeshKit tube = model.Static(SurfaceFinish.AnodizedBlack);
            tube.Push(new Vector3(0f, 0.01f, 0f), Quaternion.identity);
            tube.Cylinder(0.0145f, -0.125f, 0f, 24, 0.0015f);
            tube.Pop();
            MeshKit polymer = model.Static(SurfaceFinish.PolymerBlack);
            polymer.Profile(new[]
            {
                new Vector2(-0.095f, 0.03f), new Vector2(-0.095f, -0.005f), new Vector2(-0.112f, -0.045f), new Vector2(-0.165f, -0.047f),
                new Vector2(-0.165f, 0.036f), new Vector2(-0.12f, 0.034f),
            }, 0.02f);
            polymer.Box(new Vector3(0f, -0.002f, -0.08f), new Vector3(0.012f, 0.012f, 0.03f), 0.002f); // lock lever
            MeshKit rubber = model.Static(SurfaceFinish.Rubber);
            rubber.Box(new Vector3(0f, -0.005f, -0.168f), new Vector3(0.042f, 0.086f, 0.008f), 0.003f);
            return Shape(new Vector3(0f, 0.01f, 0f), new Vector3(0f, -0.005f, -0.172f), new Vector2(0.021f, 0.043f),
                new Vector3(0f, -0.005f, -0.086f), new Vector3(0.042f, 0.086f, 0.172f));
        }

        /// <summary>PEQ-pattern laser box on the right rail; its left face sits against the rail.</summary>
        private static PartShape Laser(ModelAssembly model)
        {
            MeshKit housing = model.Static(SurfaceFinish.PolymerFde);
            housing.Box(new Vector3(0.01f, 0f, 0f), new Vector3(0.018f, 0.02f, 0.05f), 0.0025f);
            housing.Push(new Vector3(0.016f, 0.006f, -0.025f), Quaternion.LookRotation(Vector3.back));
            housing.Cylinder(0.005f, 0f, 0.008f, 16, 0.001f); // battery cap
            housing.Pop();
            MeshKit glass = model.Static(SurfaceFinish.Rubber);
            glass.Box(new Vector3(0.006f, 0.004f, 0.0252f), new Vector3(0.006f, 0.006f, 0.0008f), 0.0005f); // visible emitter
            glass.Box(new Vector3(0.014f, 0.004f, 0.0252f), new Vector3(0.006f, 0.006f, 0.0008f), 0.0005f); // IR emitter
            glass.Box(new Vector3(0.0192f, -0.003f, 0.004f), new Vector3(0.0008f, 0.006f, 0.012f), 0.0004f); // buttons
            MeshKit clamp = model.Static(SurfaceFinish.AnodizedBlack);
            clamp.Box(new Vector3(0.0005f, 0f, 0f), new Vector3(0.003f, 0.016f, 0.03f), 0.001f);
            return Shape(new Vector3(0.006f, 0.004f, 0.026f), new Vector3(0.01f, 0f, -0.025f), new Vector2(0.009f, 0.01f),
                new Vector3(0.01f, 0f, 0f), new Vector3(0.019f, 0.02f, 0.05f));
        }

        /// <summary>Weapon light on the left rail: body, wider bezel with lens, tail switch, rail clamp.</summary>
        private static PartShape Light(ModelAssembly model)
        {
            MeshKit body = model.Static(SurfaceFinish.AnodizedBlack);
            body.Push(new Vector3(-0.012f, 0f, 0f), Quaternion.identity);
            body.Lathe(new[]
            {
                new Vector2(0f, -0.04f), new Vector2(0.008f, -0.04f), new Vector2(0.011f, -0.036f), new Vector2(0.0118f, 0.04f), new Vector2(0.015f, 0.05f),
                new Vector2(0.015f, 0.065f), new Vector2(0.0128f, 0.065f), new Vector2(0.0128f, 0.062f),
            }, 28);
            for (int i = 0; i < 8; i++)
            {
                body.Push(Vector3.zero, Quaternion.Euler(0f, 0f, i * 45f));
                body.Box(new Vector3(0f, 0.0119f, 0.0f), new Vector3(0.0025f, 0.0008f, 0.05f), 0.0003f); // grip flats
                body.Pop();
            }

            body.Pop();
            MeshKit glass = model.Static(SurfaceFinish.BluedSteel);
            glass.Push(new Vector3(-0.012f, 0f, 0f), Quaternion.identity);
            glass.Lathe(new[] { new Vector2(0.0128f, 0.062f), new Vector2(0.006f, 0.0605f), new Vector2(0f, 0.06f) }, 28, true); // reflector and lens
            glass.Pop();
            MeshKit clamp = model.Static(SurfaceFinish.ParkerizedSteel);
            clamp.Box(new Vector3(-0.0005f, 0f, 0.01f), new Vector3(0.004f, 0.016f, 0.03f), 0.001f);
            MeshKit rubber = model.Static(SurfaceFinish.Rubber);
            rubber.Push(new Vector3(-0.012f, 0f, -0.042f), Quaternion.identity);
            rubber.Cylinder(0.006f, -0.004f, 0.002f, 16, 0.0015f); // tail switch
            rubber.Pop();
            return Shape(new Vector3(-0.012f, 0f, 0.067f), new Vector3(-0.012f, 0f, -0.046f), new Vector2(0.008f, 0.008f),
                new Vector3(-0.012f, 0f, 0.012f), new Vector3(0.03f, 0.03f, 0.11f));
        }
    }
}
