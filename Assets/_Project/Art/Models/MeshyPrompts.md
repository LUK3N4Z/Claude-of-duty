# Meshy asset prompts

Generate each asset on meshy.ai (Text to 3D, Realistic, PBR on), then download **FBX with textures** and drop the
files into the folder shown. The importer packs metallic/roughness/AO into an HDRP mask map, builds the material, poses
the model in its part and binds sockets. Rebuild with `Tools/FPS Builder/3`, `4`, `5`, `7`, `8` afterwards.
If a model comes in rotated wrong, add `fit.json` next to it: `{"euler": {"x": 0, "y": 90, "z": 0}}`.

## Weapons

- [ ] **Carbine_Receiver** → `Assets/_Project/Art/Models/Weapons/Carbine_Receiver/`
  - Prompt: M4A1 / URG-I style 5.56 carbine upper and lower receiver with free-float M-LOK handguard and full-length top picatinny rail, pistol grip, trigger guard, magazine well, ejection port with dust cover and forward assist; NO stock, NO barrel past the handguard, NO magazine, NO optic. Side profile, muzzle pointing forward. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Hard-anodized matte black aluminum with subtle edge wear to bare silver aluminum, black polymer pistol grip, faint laser-engraved markings.
  - Target polycount: 15000

- [ ] **Carbine_Magazine** → `Assets/_Project/Art/Models/Weapons/Carbine_Magazine/`
  - Prompt: 30-round 5.56 NATO polymer rifle magazine (PMAG style), curved body, ribbed grip texture, removable baseplate, feed lips at the top. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Matte black textured polymer with light scuffing.
  - Target polycount: 15000

- [ ] **Marksman_Rifle** → `Assets/_Project/Art/Models/Weapons/Marksman_Rifle/`
  - Prompt: Bolt-action .300 Winchester Magnum precision rifle in a tactical chassis (Accuracy International / M2010 style): skeletonized folding stock with adjustable cheek riser, long fluted heavy barrel with muzzle brake, fluted bolt with tactical knob, picatinny scope rail; NO scope, NO magazine, NO bipod. Side profile, muzzle forward. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Dark earth Cerakote chassis with worn edges, blued steel fluted barrel and bolt, black rubber recoil pad.
  - Target polycount: 15000

- [ ] **Marksman_Scope** → `Assets/_Project/Art/Models/Weapons/Marksman_Scope/`
  - Prompt: Long-range tactical rifle scope 5-25x56 with 34 mm tube, large objective bell, elevation and windage turrets, parallax side focus knob, picatinny ring mount with two rings. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Matte black anodized aluminum, laser-etched white turret markings, glass lenses.
  - Target polycount: 15000

- [ ] **Marksman_Magazine** → `Assets/_Project/Art/Models/Weapons/Marksman_Magazine/`
  - Prompt: 5-round detachable box magazine for a .300 Winchester Magnum bolt-action rifle, steel body, short and wide. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black parkerized steel with worn edges.
  - Target polycount: 15000

- [ ] **BattleRifle** → `Assets/_Project/Art/Models/Weapons/BattleRifle/`
  - Prompt: 7.62x51 battle rifle (FN SCAR-H / MK17 style): monolithic upper receiver with long top picatinny rail, side rails, reciprocating charging handle on the left, polymer lower with pistol grip, side-folding adjustable stock extended, 16 inch barrel with flash hider; NO optic, NO magazine. Side profile, muzzle forward. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Flat dark earth (FDE) polymer and anodized aluminum, black steel barrel and flash hider, light edge wear.
  - Target polycount: 15000

- [ ] **BattleRifle_Magazine** → `Assets/_Project/Art/Models/Weapons/BattleRifle_Magazine/`
  - Prompt: 20-round 7.62x51 steel rifle magazine for a SCAR-H, straight box body with stiffening ribs and baseplate. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black phosphate steel with worn edges.
  - Target polycount: 15000

- [ ] **Sidearm_Frame** → `Assets/_Project/Art/Models/Weapons/Sidearm_Frame/`
  - Prompt: Compact 9mm striker-fired pistol frame only (Glock 19 / SIG M17 style): polymer grip frame with textured grip, trigger guard, trigger, accessory rail; WITHOUT the slide, WITHOUT magazine. Side profile, muzzle forward. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black textured polymer frame, black steel trigger parts.
  - Target polycount: 15000

- [ ] **Sidearm_Slide** → `Assets/_Project/Art/Models/Weapons/Sidearm_Slide/`
  - Prompt: Pistol slide only for a compact 9mm striker-fired handgun: rectangular slide with front and rear cocking serrations, ejection port, rear notch sight and front post sight, barrel crown visible at the muzzle. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black nitrided steel with sharp worn edges showing grey metal.
  - Target polycount: 15000

- [ ] **Sidearm_Magazine** → `Assets/_Project/Art/Models/Weapons/Sidearm_Magazine/`
  - Prompt: 15-round 9mm double-stack pistol magazine with polymer-coated steel body and baseplate. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black polymer-coated steel, bright brass rounds visible at the top.
  - Target polycount: 15000

## Attachments

- [ ] **Barrel_10in** → `Assets/_Project/Art/Models/Attachments/Barrel_10in/`
  - Prompt: 10.5 inch 5.56 AR-15 rifle barrel alone: chamber extension at the rear, low-profile gas block, threaded muzzle with no muzzle device, straight profile. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black nitride steel with machining marks.
  - Target polycount: 15000

- [ ] **Barrel_14in** → `Assets/_Project/Art/Models/Attachments/Barrel_14in/`
  - Prompt: 14.5 inch 5.56 AR-15 rifle barrel alone: chamber extension at the rear, low-profile gas block, threaded muzzle with no muzzle device, straight profile. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black nitride steel with machining marks.
  - Target polycount: 15000

- [ ] **Barrel_20in** → `Assets/_Project/Art/Models/Attachments/Barrel_20in/`
  - Prompt: 20 inch 5.56 AR-15 rifle barrel alone: chamber extension at the rear, low-profile gas block, threaded muzzle with no muzzle device, straight profile. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black nitride steel with machining marks.
  - Target polycount: 15000

- [ ] **Muzzle_FlashHider** → `Assets/_Project/Art/Models/Attachments/Muzzle_FlashHider/`
  - Prompt: A2 birdcage flash hider for a 5.56 rifle: short steel cylinder with five long slots around the front, threaded rear. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black parkerized steel.
  - Target polycount: 15000

- [ ] **Muzzle_Compensator** → `Assets/_Project/Art/Models/Attachments/Muzzle_Compensator/`
  - Prompt: Three-port rifle muzzle compensator / brake with three large top and side ports, threaded rear, flat front face. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black nitride steel with machined edges.
  - Target polycount: 15000

- [ ] **Muzzle_Suppressor** → `Assets/_Project/Art/Models/Attachments/Muzzle_Suppressor/`
  - Prompt: 5.56 rifle sound suppressor (silencer): 17 cm long 38 mm diameter cylinder with knurled rear mount, flat front cap with bore hole. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Flat dark earth Cerakote with heat discoloration near the front, black knurled mount.
  - Target polycount: 15000

- [ ] **Optic_Scope4x** → `Assets/_Project/Art/Models/Attachments/Optic_Scope4x/`
  - Prompt: Compact 4x prism rifle scope (ACOG / Trijicon style) with integrated picatinny mount, objective lens forward, ocular eyepiece at the rear, top fiber-optic housing. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Matte black anodized aluminum housing, glass lenses with green coating.
  - Target polycount: 15000

- [ ] **Optic_RedDot** → `Assets/_Project/Art/Models/Attachments/Optic_RedDot/`
  - Prompt: Micro red dot sight (Aimpoint T2 style) with tubular body, flip-up lens covers removed, brightness knob on the right, low picatinny mount. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Matte black anodized aluminum, red-tinted front lens coating.
  - Target polycount: 15000

- [ ] **Grip_Vertical** → `Assets/_Project/Art/Models/Attachments/Grip_Vertical/`
  - Prompt: Vertical rifle foregrip with picatinny rail clamp on top, ribbed cylindrical grip body. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black textured polymer.
  - Target polycount: 15000

- [ ] **Grip_Angled** → `Assets/_Project/Art/Models/Attachments/Grip_Angled/`
  - Prompt: Angled foregrip (Magpul AFG style) wedge-shaped ergonomic hand stop for a rifle handguard rail. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black textured polymer.
  - Target polycount: 15000

- [ ] **Stock_Fixed** → `Assets/_Project/Art/Models/Attachments/Stock_Fixed/`
  - Prompt: A2 style fixed rifle buttstock with trapdoor buttplate and sling swivel, attaches to the receiver at the front. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black textured polymer, steel sling swivel.
  - Target polycount: 15000

- [ ] **Stock_Collapsed** → `Assets/_Project/Art/Models/Attachments/Stock_Collapsed/`
  - Prompt: Collapsible carbine stock (Magpul CTR style) fully collapsed on a short mil-spec buffer tube, rubber butt pad. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black polymer stock, black anodized aluminum buffer tube.
  - Target polycount: 15000

- [ ] **Rail_Laser** → `Assets/_Project/Art/Models/Attachments/Rail_Laser/`
  - Prompt: Rifle aiming laser box (AN/PEQ-15 style): rectangular housing with two emitter apertures at the front, battery cap, side picatinny clamp. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Tan/FDE polymer housing, black clamp, glass emitter windows.
  - Target polycount: 15000

- [ ] **Rail_Light** → `Assets/_Project/Art/Models/Attachments/Rail_Light/`
  - Prompt: Tactical weapon light (SureFire Scout style): cylindrical aluminum body with wider front bezel and glass lens, tail cap switch, side picatinny mount. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Matte black anodized aluminum, clear glass lens with reflector.
  - Target polycount: 15000

## Equipment

- [ ] **Grenade_M67** → `Assets/_Project/Art/Models/Equipment/Grenade_M67/`
  - Prompt: M67 fragmentation hand grenade: smooth spherical steel body, fuse assembly on top with spoon (safety lever) and pull ring with safety pin. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Olive drab painted steel with yellow marking band, bare steel spoon and pin.
  - Target polycount: 15000

- [ ] **Grenade_M84** → `Assets/_Project/Art/Models/Equipment/Grenade_M84/`
  - Prompt: M84 stun grenade (flashbang): perforated steel cylinder with many round holes, fuse on top with spoon and pull ring. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black painted steel with white markings, bare steel spoon and ring.
  - Target polycount: 15000

- [ ] **Grenade_M18** → `Assets/_Project/Art/Models/Equipment/Grenade_M18/`
  - Prompt: M18 colored smoke grenade: sheet-metal cylinder canister with emission holes on top, fuse with spoon and pull ring. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Olive drab paint with green top band and stenciled yellow text 'SMOKE GREEN'.
  - Target polycount: 15000

- [ ] **Charge_C4** → `Assets/_Project/Art/Models/Equipment/Charge_C4/`
  - Prompt: M112 C4 demolition charge block wrapped in olive film with blasting cap inserted and detonation cord coiled on top. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Olive drab plastic film wrapper with yellow stencil text, off-white explosive visible at the cut, red and black wires.
  - Target polycount: 15000

- [ ] **Clacker_M57** → `Assets/_Project/Art/Models/Equipment/Clacker_M57/`
  - Prompt: M57 firing device 'clacker': handheld squeeze-handle detonator with safety bail and cable connector. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Olive drab painted aluminum with black rubber cable connector.
  - Target polycount: 15000

## Props

- [ ] **Prop_AmmoCrate** → `Assets/_Project/Art/Models/Props/Prop_AmmoCrate/`
  - Prompt: Military wooden ammunition crate with rope handles, metal corner brackets and hinged lid, stenciled markings. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Olive painted wood planks with wear to raw wood, black steel brackets, yellow stencils.
  - Target polycount: 15000

- [ ] **Prop_Workbench** → `Assets/_Project/Art/Models/Props/Prop_Workbench/`
  - Prompt: Gunsmith workbench: heavy steel frame table with thick butcher-block wooden top, mounted bench vise, lower shelf with tool boxes. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Grey powder-coated steel frame, oiled hardwood top with scratches and oil stains, red tool boxes.
  - Target polycount: 15000

- [ ] **Prop_Locker** → `Assets/_Project/Art/Models/Props/Prop_Locker/`
  - Prompt: Tall military equipment locker: steel cabinet with two doors, ventilation slots and padlock hasp. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Olive green powder-coated steel with chipped paint and dents.
  - Target polycount: 15000

- [ ] **Prop_WeaponRack** → `Assets/_Project/Art/Models/Props/Prop_WeaponRack/`
  - Prompt: Wall-standing armory weapon rack: steel frame with padded cradles for four rifles, empty. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: Black powder-coated steel, grey padded cradles.
  - Target polycount: 15000

## Operators

- [ ] **Operator_Helmet_Tan** → `Assets/_Project/Art/Models/Operators/Operator_Helmet_Tan/`
  - Prompt: Modern ballistic high-cut combat helmet (Ops-Core FAST style) in coyote tan, side accessory rails, front NVG shroud, rear velcro patch panel, chin strap tucked, no person. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: coyote tan rough textured paint, black rails and shroud, scuffed edges.
  - Target polycount: 15000

- [ ] **Operator_Helmet_Olive** → `Assets/_Project/Art/Models/Operators/Operator_Helmet_Olive/`
  - Prompt: Modern ballistic high-cut combat helmet (Ops-Core FAST style) in ranger green, side accessory rails, front NVG shroud, rear velcro patch panel, chin strap tucked, no person. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: ranger green rough textured paint, black rails and shroud, scuffed edges.
  - Target polycount: 15000

- [ ] **Operator_PlateCarrier_Tan** → `Assets/_Project/Art/Models/Operators/Operator_PlateCarrier_Tan/`
  - Prompt: Modern tactical plate carrier vest in coyote tan with front and back plates, cummerbund, three rifle magazine pouches on the front, radio pouch, shoulder pads, no person inside. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: coyote tan 500D nylon with MOLLE webbing, black buckles and elastic.
  - Target polycount: 15000

- [ ] **Operator_PlateCarrier_Olive** → `Assets/_Project/Art/Models/Operators/Operator_PlateCarrier_Olive/`
  - Prompt: Modern tactical plate carrier vest in ranger green with front and back plates, cummerbund, three rifle magazine pouches on the front, radio pouch, shoulder pads, no person inside. Photorealistic game-ready asset, real-world scale, clean topology, single object, no base, no stand, no background, neutral lighting.
  - Texture prompt: ranger green 500D nylon with MOLLE webbing, black buckles and elastic.
  - Target polycount: 15000
