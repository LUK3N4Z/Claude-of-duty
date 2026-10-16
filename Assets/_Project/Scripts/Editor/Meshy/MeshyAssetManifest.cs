using System.Collections.Generic;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>One model the game wants from Meshy: where it lives, what to ask for, and how it is posed in its part.</summary>
    internal sealed class MeshyAssetSpec
    {
        public string Id;
        public string Category;
        public string Prompt;
        public string TexturePrompt;
        public int Polycount = 15000;
        public ModelFit Fit;

        public string Folder => $"{ImportedModels.Root}/{Category}/{Id}";
    }

    /// <summary>
    /// Every Meshy-generated asset with its text-to-3D prompt (used verbatim by the API client and the prompt sheet for
    /// meshy.ai) and its fit in the part frame the builders expect. Generate on meshy.ai with Realistic style and PBR
    /// on, download FBX with textures, and drop the files into the asset's folder.
    /// </summary>
    internal static class MeshyAssetManifest
    {
        internal const string Weapons = "Weapons";
        internal const string Attachments = "Attachments";
        internal const string Equipment = "Equipment";
        internal const string Props = "Props";
        internal const string Operators = "Operators";

        private const string Style = " Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.";

        private static readonly MeshyAssetSpec[] All =
        {
            // ---- Weapons ----
            new MeshyAssetSpec
            {
                Id = "Carbine_Receiver", Category = Weapons,
                Prompt = "M4A1 / URG-I style 5.56 carbine upper and lower receiver with free-float M-LOK handguard and full-length top picatinny rail, pistol grip, trigger guard, magazine well, ejection port with dust cover and forward assist; NO stock, NO barrel past the handguard, NO magazine, NO optic. Side profile, muzzle pointing forward." + Style,
                TexturePrompt = "Hard-anodized matte black aluminum with subtle edge wear to bare silver aluminum, black polymer pistol grip, faint laser-engraved markings.",
                Fit = ModelFit.Forward(0.45f, false, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Min, new Vector3(0f, 0.04f, -0.15f)),
            },
            new MeshyAssetSpec
            {
                Id = "Carbine_Magazine", Category = Weapons,
                Prompt = "30-round 5.56 NATO polymer rifle magazine (PMAG style), curved body, ribbed grip texture, removable baseplate, feed lips at the top." + Style,
                TexturePrompt = "Matte black textured polymer with light scuffing.",
                Fit = ModelFit.Vertical(0.13f, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Center, new Vector3(0f, 0.065f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Marksman_Rifle", Category = Weapons,
                Prompt = "Bolt-action .300 Winchester Magnum precision rifle in a tactical chassis (Accuracy International / M2010 style): skeletonized folding stock with adjustable cheek riser, long fluted heavy barrel with muzzle brake, fluted bolt with tactical knob, picatinny scope rail; NO scope, NO magazine, NO bipod. Side profile, muzzle forward." + Style,
                TexturePrompt = "Dark earth Cerakote chassis with worn edges, blued steel fluted barrel and bolt, black rubber recoil pad.",
                Fit = ModelFit.Forward(1.32f, false, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Min, new Vector3(0f, 0.035f, -0.43f)),
            },
            new MeshyAssetSpec
            {
                Id = "Marksman_Scope", Category = Weapons,
                Prompt = "Long-range tactical rifle scope 5-25x56 with 34 mm tube, large objective bell, elevation and windage turrets, parallax side focus knob, picatinny ring mount with two rings." + Style,
                TexturePrompt = "Matte black anodized aluminum, laser-etched white turret markings, glass lenses.",
                Fit = ModelFit.Sliced(0.3f, true, SliceAnchor.Rear, AxisAnchor.Min, Vector3.zero),
            },
            new MeshyAssetSpec
            {
                Id = "Marksman_Magazine", Category = Weapons,
                Prompt = "5-round detachable box magazine for a .300 Winchester Magnum bolt-action rifle, steel body, short and wide." + Style,
                TexturePrompt = "Black parkerized steel with worn edges.",
                Fit = ModelFit.Vertical(0.075f, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Center, new Vector3(0f, 0.035f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "BattleRifle", Category = Weapons,
                Prompt = "7.62x51 battle rifle (FN SCAR-H / MK17 style): monolithic upper receiver with long top picatinny rail, side rails, reciprocating charging handle on the left, polymer lower with pistol grip, side-folding adjustable stock extended, 16 inch barrel with flash hider; NO optic, NO magazine. Side profile, muzzle forward." + Style,
                TexturePrompt = "Flat dark earth (FDE) polymer and anodized aluminum, black steel barrel and flash hider, light edge wear.",
                Fit = ModelFit.Forward(0.88f, false, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Min, new Vector3(0f, 0.04f, -0.42f)),
            },
            new MeshyAssetSpec
            {
                Id = "BattleRifle_Magazine", Category = Weapons,
                Prompt = "20-round 7.62x51 steel rifle magazine for a SCAR-H, straight box body with stiffening ribs and baseplate." + Style,
                TexturePrompt = "Black phosphate steel with worn edges.",
                Fit = ModelFit.Vertical(0.13f, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Center, new Vector3(0f, 0.065f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Sidearm_Frame", Category = Weapons,
                Prompt = "Compact 9mm striker-fired pistol frame only (Glock 19 / SIG M17 style): polymer grip frame with textured grip, trigger guard, trigger, accessory rail; WITHOUT the slide, WITHOUT magazine. Side profile, muzzle forward." + Style,
                TexturePrompt = "Black textured polymer frame, black steel trigger parts.",
                Fit = ModelFit.Forward(0.17f, false, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Max, new Vector3(0f, -0.006f, 0.095f)),
            },
            new MeshyAssetSpec
            {
                Id = "Sidearm_Slide", Category = Weapons,
                Prompt = "Pistol slide only for a compact 9mm striker-fired handgun: rectangular slide with front and rear cocking serrations, ejection port, rear notch sight and front post sight, barrel crown visible at the muzzle." + Style,
                TexturePrompt = "Black nitrided steel with sharp worn edges showing grey metal.",
                Fit = ModelFit.Forward(0.185f, false, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Max, new Vector3(0f, 0.022f, 0.1f)),
            },
            new MeshyAssetSpec
            {
                Id = "Sidearm_Magazine", Category = Weapons,
                Prompt = "15-round 9mm double-stack pistol magazine with polymer-coated steel body and baseplate." + Style,
                TexturePrompt = "Black polymer-coated steel, bright brass rounds visible at the top.",
                Fit = ModelFit.Vertical(0.105f, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Center, new Vector3(0f, 0.05f, 0f)),
            },

            // ---- Attachments (part frames match the procedural parts) ----
            Barrel("Barrel_10in", 10.5f, "10.5 inch"),
            Barrel("Barrel_14in", 14.5f, "14.5 inch"),
            Barrel("Barrel_20in", 20f, "20 inch"),
            new MeshyAssetSpec
            {
                Id = "Muzzle_FlashHider", Category = Attachments,
                Prompt = "A2 birdcage flash hider for a 5.56 rifle: short steel cylinder with five long slots around the front, threaded rear." + Style,
                TexturePrompt = "Black parkerized steel.",
                Fit = ModelFit.Sliced(0.055f, false, SliceAnchor.Rear, AxisAnchor.Min, Vector3.zero),
            },
            new MeshyAssetSpec
            {
                Id = "Muzzle_Compensator", Category = Attachments,
                Prompt = "Three-port rifle muzzle compensator / brake with three large top and side ports, threaded rear, flat front face." + Style,
                TexturePrompt = "Black nitride steel with machined edges.",
                Fit = ModelFit.Sliced(0.06f, false, SliceAnchor.Rear, AxisAnchor.Min, Vector3.zero),
            },
            new MeshyAssetSpec
            {
                Id = "Muzzle_Suppressor", Category = Attachments,
                Prompt = "5.56 rifle sound suppressor (silencer): 17 cm long 38 mm diameter cylinder with knurled rear mount, flat front cap with bore hole." + Style,
                TexturePrompt = "Flat dark earth Cerakote with heat discoloration near the front, black knurled mount.",
                Fit = ModelFit.Sliced(0.17f, false, SliceAnchor.Rear, AxisAnchor.Min, Vector3.zero),
            },
            new MeshyAssetSpec
            {
                Id = "Optic_Scope4x", Category = Attachments,
                Prompt = "Compact 4x prism rifle scope (ACOG / Trijicon style) with integrated picatinny mount, objective lens forward, ocular eyepiece at the rear, top fiber-optic housing." + Style,
                TexturePrompt = "Matte black anodized aluminum housing, glass lenses with green coating.",
                Fit = ModelFit.Sliced(0.16f, true, SliceAnchor.Rear, AxisAnchor.Min, Vector3.zero),
            },
            new MeshyAssetSpec
            {
                Id = "Optic_RedDot", Category = Attachments,
                Prompt = "Micro red dot sight (Aimpoint T2 style) with tubular body, flip-up lens covers removed, brightness knob on the right, low picatinny mount." + Style,
                TexturePrompt = "Matte black anodized aluminum, red-tinted front lens coating.",
                Fit = ModelFit.Forward(0.06f, false, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Center, new Vector3(0f, 0.018f, 0.03f)),
            },
            new MeshyAssetSpec
            {
                Id = "Grip_Vertical", Category = Attachments,
                Prompt = "Vertical rifle foregrip with picatinny rail clamp on top, ribbed cylindrical grip body." + Style,
                TexturePrompt = "Black textured polymer.",
                Fit = ModelFit.Vertical(0.095f, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Center, Vector3.zero),
            },
            new MeshyAssetSpec
            {
                Id = "Grip_Angled", Category = Attachments,
                Prompt = "Angled foregrip (Magpul AFG style) wedge-shaped ergonomic hand stop for a rifle handguard rail." + Style,
                TexturePrompt = "Black textured polymer.",
                Fit = ModelFit.Forward(0.08f, false, AxisAnchor.Center, AxisAnchor.Max, AxisAnchor.Center, Vector3.zero),
            },
            new MeshyAssetSpec
            {
                Id = "Stock_Fixed", Category = Attachments,
                Prompt = "A2 style fixed rifle buttstock with trapdoor buttplate and sling swivel, attaches to the receiver at the front." + Style,
                TexturePrompt = "Black textured polymer, steel sling swivel.",
                Fit = ModelFit.Sliced(0.25f, false, SliceAnchor.Front, AxisAnchor.Max, Vector3.zero),
            },
            new MeshyAssetSpec
            {
                Id = "Stock_Collapsed", Category = Attachments,
                Prompt = "Collapsible carbine stock (Magpul CTR style) fully collapsed on a short mil-spec buffer tube, rubber butt pad." + Style,
                TexturePrompt = "Black polymer stock, black anodized aluminum buffer tube.",
                Fit = ModelFit.Sliced(0.17f, false, SliceAnchor.Front, AxisAnchor.Max, Vector3.zero),
            },
            new MeshyAssetSpec
            {
                Id = "Rail_Laser", Category = Attachments,
                Prompt = "Rifle aiming laser box (AN/PEQ-15 style): rectangular housing with two emitter apertures at the front, battery cap, side picatinny clamp." + Style,
                TexturePrompt = "Tan/FDE polymer housing, black clamp, glass emitter windows.",
                Fit = ModelFit.Forward(0.05f, false, AxisAnchor.Min, AxisAnchor.Center, AxisAnchor.Center, new Vector3(0.001f, 0f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Rail_Light", Category = Attachments,
                Prompt = "Tactical weapon light (SureFire Scout style): cylindrical aluminum body with wider front bezel and glass lens, tail cap switch, side picatinny mount." + Style,
                TexturePrompt = "Matte black anodized aluminum, clear glass lens with reflector.",
                Fit = ModelFit.Forward(0.07f, true, AxisAnchor.Max, AxisAnchor.Center, AxisAnchor.Center, new Vector3(0.0005f, 0f, 0.01f)),
            },

            // ---- Equipment ----
            new MeshyAssetSpec
            {
                Id = "Grenade_M67", Category = Equipment,
                Prompt = "M67 fragmentation hand grenade: smooth spherical steel body, fuse assembly on top with spoon (safety lever) and pull ring with safety pin." + Style,
                TexturePrompt = "Olive drab painted steel with yellow marking band, bare steel spoon and pin.",
                Fit = ModelFit.Box(new Vector3(0.064f, 0.095f, 0.064f), new Vector3(0f, -0.032f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Grenade_M84", Category = Equipment,
                Prompt = "M84 stun grenade (flashbang): perforated steel cylinder with many round holes, fuse on top with spoon and pull ring." + Style,
                TexturePrompt = "Black painted steel with white markings, bare steel spoon and ring.",
                Fit = ModelFit.Box(new Vector3(0.045f, 0.15f, 0.045f), new Vector3(0f, -0.065f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Grenade_M18", Category = Equipment,
                Prompt = "M18 colored smoke grenade: sheet-metal cylinder canister with emission holes on top, fuse with spoon and pull ring." + Style,
                TexturePrompt = "Olive drab paint with green top band and stenciled yellow text 'SMOKE GREEN'.",
                Fit = ModelFit.Box(new Vector3(0.062f, 0.17f, 0.062f), new Vector3(0f, -0.075f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Charge_C4", Category = Equipment,
                Prompt = "M112 C4 demolition charge block wrapped in olive film with blasting cap inserted and detonation cord coiled on top." + Style,
                TexturePrompt = "Olive drab plastic film wrapper with yellow stencil text, off-white explosive visible at the cut, red and black wires.",
                Fit = ModelFit.Box(new Vector3(0.28f, 0.08f, 0.18f), new Vector3(0f, -0.04f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Clacker_M57", Category = Equipment,
                Prompt = "M57 firing device 'clacker': handheld squeeze-handle detonator with safety bail and cable connector." + Style,
                TexturePrompt = "Olive drab painted aluminum with black rubber cable connector.",
                Fit = ModelFit.Box(new Vector3(0.11f, 0.045f, 0.04f), Vector3.zero),
            },

            // ---- Props ----
            new MeshyAssetSpec
            {
                Id = "Prop_AmmoCrate", Category = Props,
                Prompt = "Military wooden ammunition crate with rope handles, metal corner brackets and hinged lid, stenciled markings." + Style,
                TexturePrompt = "Olive painted wood planks with wear to raw wood, black steel brackets, yellow stencils.",
                Fit = ModelFit.Box(new Vector3(0.8f, 0.6f, 0.5f), new Vector3(0f, -0.3f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Prop_Workbench", Category = Props,
                Prompt = "Gunsmith workbench: heavy steel frame table with thick butcher-block wooden top, mounted bench vise, lower shelf with tool boxes." + Style,
                TexturePrompt = "Grey powder-coated steel frame, oiled hardwood top with scratches and oil stains, red tool boxes.",
                Fit = ModelFit.Box(new Vector3(2.6f, 0.9f, 1.1f), new Vector3(0f, -0.45f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Prop_Locker", Category = Props,
                Prompt = "Tall military equipment locker: steel cabinet with two doors, ventilation slots and padlock hasp." + Style,
                TexturePrompt = "Olive green powder-coated steel with chipped paint and dents.",
                Fit = ModelFit.Box(new Vector3(0.9f, 1.8f, 0.5f), new Vector3(0f, -0.9f, 0f)),
            },
            new MeshyAssetSpec
            {
                Id = "Prop_WeaponRack", Category = Props,
                Prompt = "Wall-standing armory weapon rack: steel frame with padded cradles for four rifles, empty." + Style,
                TexturePrompt = "Black powder-coated steel, grey padded cradles.",
                Fit = ModelFit.Box(new Vector3(1.2f, 1.6f, 0.4f), new Vector3(0f, -0.8f, 0f)),
            },

            // ---- Operators (gear pieces; limbs stay procedural so they bend with the IK) ----
            Helmet("Operator_Helmet_Tan", "coyote tan"),
            Helmet("Operator_Helmet_Olive", "ranger green"),
            Carrier("Operator_PlateCarrier_Tan", "coyote tan"),
            Carrier("Operator_PlateCarrier_Olive", "ranger green"),
        };

        public static IReadOnlyList<MeshyAssetSpec> Specs => All;

        public static MeshyAssetSpec Find(string id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id) return All[i];
            }

            return null;
        }

        private static MeshyAssetSpec Barrel(string id, float inches, string label) => new MeshyAssetSpec
        {
            Id = id, Category = Attachments,
            Prompt = $"{label} 5.56 AR-15 rifle barrel alone: chamber extension at the rear, low-profile gas block, threaded muzzle with no muzzle device, straight profile." + Style,
            TexturePrompt = "Black nitride steel with machining marks.",
            Fit = ModelFit.Sliced(inches * 0.0254f, false, SliceAnchor.Front, AxisAnchor.Min, Vector3.zero),
        };

        private static MeshyAssetSpec Helmet(string id, string color) => new MeshyAssetSpec
        {
            Id = id, Category = Operators,
            Prompt = $"Modern ballistic high-cut combat helmet (Ops-Core FAST style) in {color}, side accessory rails, front NVG shroud, rear velcro patch panel, chin strap tucked, no person." + Style,
            TexturePrompt = $"{color} rough textured paint, black rails and shroud, scuffed edges.",
            Fit = ModelFit.Box(new Vector3(0.27f, 0.2f, 0.3f), new Vector3(0f, -0.04f, -0.02f)),
        };

        private static MeshyAssetSpec Carrier(string id, string color) => new MeshyAssetSpec
        {
            Id = id, Category = Operators,
            Prompt = $"Modern tactical plate carrier vest in {color} with front and back plates, cummerbund, three rifle magazine pouches on the front, radio pouch, shoulder pads, no person inside." + Style,
            TexturePrompt = $"{color} 500D nylon with MOLLE webbing, black buckles and elastic.",
            Fit = ModelFit.Box(new Vector3(0.44f, 0.4f, 0.32f), new Vector3(0f, -0.2f, 0f)),
        };
    }
}
