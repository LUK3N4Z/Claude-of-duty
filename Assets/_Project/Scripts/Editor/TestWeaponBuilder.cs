using System;
using FPS.Ballistics;
using FPS.Core;
using FPS.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Generates the reference calibers and the weapons with every socket: a modular 5.56 closed-bolt carbine (45° canted
    /// irons), a .300 Win Mag bolt-action marksman rifle (8x PiP scope), a 7.62 battle rifle (optic, grip and rail
    /// mounts) and a 9 mm striker sidearm with a reciprocating slide, each with weapon data, recoil profile, audio,
    /// a droppable magazine and a <see cref="WeaponMechanism"/> over its moving parts. Models are imported Meshy assets
    /// when present (Art/Models), otherwise high-detail procedural geometry.
    /// </summary>
    public static class TestWeaponBuilder
    {
        internal const string CarbineName = "TestRifle";
        internal const string MarksmanName = "TestMarksman";
        internal const string BattleRifleName = "BattleRifle";
        internal const string SidearmName = "Sidearm";

        private const string ArtFolder = "Assets/_Project/Art/TestRifle";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Weapons";
        private const string DataFolder = "Assets/_Project/Data/Weapons";
        private const string CaliberFolder = "Assets/_Project/Data/Ballistics";

        private struct RifleSpec
        {
            public string Name;
            public float BarrelExposed;
            public float StockLength;
            public float ScopeLength;
            public float ScopeDiameter;
            public float Magnification;
            public float EyeRelief;
            public float AdsZoom;
            public bool CantedIrons;
            public Vector3 MagazineSize;
            public float MagazineAngle;
            /// <summary>Receiver only: barrel, muzzle, optic, grip, stock and rail devices come from attachments.</summary>
            public bool Modular;
        }

        internal static string PrefabPath(string weaponName) => $"{PrefabFolder}/{weaponName}.prefab";
        internal static string DataPath(string weaponName) => $"{DataFolder}/{weaponName}_Data.asset";

        [MenuItem("Tools/FPS Builder/3. Create Test Weapons", priority = 3)]
        public static void CreateTestWeaponsMenu()
        {
            CreateTestWeapons();
            PlayerBuilder.CreateOrUpdatePlayerPrefab();
        }

        public static void CreateTestWeapons()
        {
            BallisticsContentBuilder.CreateContent();
            EffectsContentBuilder.CreateContent();
            ProjectSetupWizard.EnsureFolder(ArtFolder);
            ProjectSetupWizard.EnsureFolder(PrefabFolder);
            ProjectSetupWizard.EnsureFolder(DataFolder);
            ProjectSetupWizard.EnsureFolder(CaliberFolder);

            CaliberDataSO nato556 = Caliber("Caliber_556x45", "5.56x45mm NATO (M855)", 4.0f, 5.7f, 940f, 20f, 7.5f, DragModel.G7, 0.151f, 40f, 35f, "Casing_556x45");
            CaliberDataSO nato762 = Caliber("Caliber_762x51", "7.62x51mm NATO (M80)", 9.5f, 7.82f, 840f, 22f, 5.5f, DragModel.G7, 0.2f, 55f, 55f, "Casing_762x51");
            CaliberDataSO parabellum = Caliber("Caliber_9x19", "9x19mm Parabellum (124gr FMJ)", 8.0f, 9.01f, 360f, 4f, 4.5f, DragModel.G1, 0.15f, 15f, 30f, "Casing_9x19");
            CaliberDataSO winMag = Caliber("Caliber_300WM", ".300 Winchester Magnum (190gr)", 12.3f, 7.82f, 880f, 24f, 7.6f, DragModel.G7, 0.29f, 70f, 90f, "Casing_300WM");

            var carbine = new RifleSpec
            {
                Name = CarbineName, BarrelExposed = 0.18f, StockLength = 0.24f, ScopeLength = 0.16f, ScopeDiameter = 0.04f,
                Magnification = 4f, EyeRelief = 0.07f, AdsZoom = 1.25f, CantedIrons = true,
                MagazineSize = new Vector3(0.025f, 0.12f, 0.06f), MagazineAngle = 8f, Modular = true,
            };
            AttachmentDataSO[] carbineBuild = AttachmentContentBuilder.CreateParts(GunshotAudioBuilder.Suppressed(GunshotAudioBuilder.Carbine));
            BuildRifle(carbine);
            Rigidbody carbineMagazine = DroppedMagazine(CarbineName, carbine.MagazineSize, 0.45f);
            RecoilProfileSO carbineRecoil = LoadOrCreate<RecoilProfileSO>($"{DataFolder}/{CarbineName}_Recoil.asset");
            Configure(LoadOrCreate<WeaponDataSO>(DataPath(CarbineName)), so =>
            {
                Set(so, "displayName", "Test Carbine 5.56");
                Set(so, "massKg", 1.6f); // bare receiver; the default build brings it to about 3.5 kg
                Set(so, "centerOfMass", new Vector3(0f, -0.02f, 0.02f));
                SerializedProperty defaults = Property(so, "defaultAttachments");
                defaults.arraySize = carbineBuild.Length;
                for (int i = 0; i < carbineBuild.Length; i++) defaults.GetArrayElementAtIndex(i).objectReferenceValue = carbineBuild[i];
                Set(so, "ergonomics", 55f);
                Set(so, "barrelLengthInches", 14.5f);
                Set(so, "caliber", nato556);
                SetEnum(so, "boltType", (int)BoltType.ClosedBolt);
                SetFireModes(so, FireMode.Semi, FireMode.Auto);
                Set(so, "roundsPerMinute", 800f);
                Set(so, "moaAccuracy", 2.5f);
                Set(so, "magazineCapacity", 30);
                Set(so, "boltHoldOpen", true);
                Set(so, "spareMagazines", 4);
                Set(so, "droppedMagazinePrefab", carbineMagazine);
                Set(so, "hipStockOffset", new Vector3(0.14f, -0.2f, -0.16f));
                Set(so, WeaponDataSO.RecoilFieldName, carbineRecoil);
                Set(so, "audioProfile", EffectsContentBuilder.WeaponAudio(CarbineName));
            });

            var marksman = new RifleSpec
            {
                Name = MarksmanName, BarrelExposed = 0.46f, StockLength = 0.28f, ScopeLength = 0.3f, ScopeDiameter = 0.05f,
                Magnification = 8f, EyeRelief = 0.09f, AdsZoom = 1.3f, CantedIrons = false,
                MagazineSize = new Vector3(0.03f, 0.07f, 0.09f), MagazineAngle = 0f,
            };
            BuildRifle(marksman);
            Rigidbody marksmanMagazine = DroppedMagazine(MarksmanName, marksman.MagazineSize, 0.3f);
            RecoilProfileSO marksmanRecoil = LoadOrCreate<RecoilProfileSO>($"{DataFolder}/{MarksmanName}_Recoil.asset");
            Configure(marksmanRecoil, so =>
            {
                Set(so, "referenceMassKg", 6.2f);
                Set(so, "verticalKick", 330f);
                Set(so, "verticalSpread", 0.1f);
                Set(so, "horizontalBias", 5f);
                Set(so, "horizontalSpread", 30f);
                Set(so, "rollKick", 15f);
                Set(so, "backKick", 1.9f);
                Set(so, "upKick", 0.2f);
                Set(so, "boltCarrierImpulse", 0f); // no carrier on a bolt action
                Set(so, "boltCarrierPitch", 0f);
                Set(so, "viewClimb", 1.1f);
                Set(so, "cameraKick", 55f);
                Set(so, "cameraPush", 0.09f);
                Set(so, "kickRampPerShot", 0f);
                Set(so, "maxKickRamp", 1f);
            });
            Configure(LoadOrCreate<WeaponDataSO>(DataPath(MarksmanName)), so =>
            {
                Set(so, "displayName", "Test Marksman .300 WM");
                Set(so, "massKg", 6.2f);
                Set(so, "ergonomics", 32f);
                Set(so, "barrelLengthInches", 24f);
                Set(so, "caliber", winMag);
                SetEnum(so, "boltType", (int)BoltType.ManualBoltAction);
                SetFireModes(so, FireMode.Semi);
                Set(so, "roundsPerMinute", 60f);
                Set(so, "moaAccuracy", 0.6f);
                Set(so, "magazineCapacity", 5);
                Set(so, "boltHoldOpen", false);
                Set(so, "spareMagazines", 3);
                Set(so, "aimDownSightsTime", 0.42f);
                Set(so, "manualCycleTime", 1f);
                Set(so, "droppedMagazinePrefab", marksmanMagazine);
                Set(so, "hipStockOffset", new Vector3(0.14f, -0.2f, -0.18f));
                Set(so, WeaponDataSO.RecoilFieldName, marksmanRecoil);
                Set(so, "audioProfile", EffectsContentBuilder.WeaponAudio(MarksmanName));
            });

            CreateBattleRifle(nato762);
            CreateSidearm(parabellum);

            AssetDatabase.SaveAssets();
            Debug.Log($"[FPS Builder] Weapons {CarbineName}, {MarksmanName}, {BattleRifleName}, {SidearmName} and 4 calibers ready.");
        }

        internal static Material LitMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            string path = $"{ArtFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            ProjectSetupWizard.EnsureFolder(ArtFolder);
            material = new Material(Shader.Find("HDRP/Lit")) { name = materialName };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static WeaponModelRig BuildRifle(RifleSpec spec)
        {
            var root = new GameObject(spec.Name);
            try
            {
                Transform t = root.transform;
                float handguardEnd = spec.Modular ? 0.3f : 0.43f;
                float stockEnd = spec.Modular ? -0.15f : -0.15f - spec.StockLength;
                float muzzle = spec.Modular ? handguardEnd : handguardEnd + spec.BarrelExposed;

                // Sockets first: the models are built around them.
                Transform primary = Socket(t, WeaponModelRig.PrimarySightSocketName, new Vector3(0f, 0.075f, -0.06f), Quaternion.identity);
                if (spec.Modular)
                {
                    Socket(primary, AttachmentDataSO.MountName(AttachmentSlot.Optic), Vector3.zero, Quaternion.identity);
                    Socket(t, AttachmentDataSO.MountName(AttachmentSlot.Barrel), new Vector3(0f, 0f, 0.05f), Quaternion.identity);
                    Socket(t, AttachmentDataSO.MountName(AttachmentSlot.Stock), new Vector3(0f, -0.015f, -0.15f), Quaternion.identity);
                    Socket(t, AttachmentDataSO.MountName(AttachmentSlot.Underbarrel), new Vector3(0f, -0.028f, 0.27f), Quaternion.identity);
                    Socket(t, AttachmentDataSO.MountName(AttachmentSlot.Laser), new Vector3(0.026f, 0.005f, 0.26f), Quaternion.identity);
                    Socket(t, AttachmentDataSO.MountName(AttachmentSlot.Light), new Vector3(-0.028f, 0.005f, 0.26f), Quaternion.identity);
                }

                Socket(t, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, muzzle), Quaternion.identity);
                Socket(t, WeaponModelRig.StockPivotSocketName, new Vector3(0f, -0.02f, stockEnd), Quaternion.identity);
                Socket(t, WeaponModelRig.ChamberEjectionSocketName, new Vector3(0.026f, 0.01f, 0f), Quaternion.Euler(0f, 90f, 0f));
                Transform magazineSocket = Socket(t, WeaponModelRig.MagazineSocketName, new Vector3(0f, -0.04f - spec.MagazineSize.y * 0.5f, 0.06f),
                    Quaternion.Euler(spec.MagazineAngle, 0f, 0f));
                Socket(t, WeaponModelRig.ChargingHandleSocketName, new Vector3(0f, 0.03f, -0.15f), Quaternion.identity);
                Socket(t, WeaponModelRig.LeftHandIKGripName, new Vector3(0f, -0.035f, spec.Modular ? 0.25f : 0.3f), Quaternion.identity);
                Socket(t, WeaponModelRig.RightHandIKGripName, new Vector3(0f, -0.075f, -0.07f), Quaternion.Euler(15f, 0f, 0f));

                WeaponVisual visual;
                if (spec.Modular)
                {
                    visual = ProceduralWeapons.Carbine(t, magazineSocket);
                }
                else
                {
                    visual = ProceduralWeapons.Marksman(t, primary, magazineSocket, out _);
                    float lensRadius = spec.ScopeDiameter * 0.4125f;
                    MeshRenderer lensRenderer = Disc(primary, "Ocular Lens", DiscMesh(), -0.001f, lensRadius, UnlitMaterial("Scope Lens", Color.white, null, false));
                    var sight = primary.gameObject.AddComponent<WeaponSight>();
                    sight.Configure(spec.Magnification, spec.EyeRelief, spec.AdsZoom, lensRenderer);
                    sight.ConfigureReticle(ReticleStyle.DuplexDot, new Color(5f, 0.3f, 0.15f), 1f); // etched duplex, illuminated red dot
                }

                if (spec.CantedIrons) CantedIrons(t, spec.Modular ? 0.38f : 0.48f);

                var rig = root.AddComponent<WeaponModelRig>();
                rig.EnsureSockets();
                SetMagazineRenderers(rig, visual);
                AddMechanism(root, visual, new Vector3(0f, 0f, -0.026f), new Vector3(0f, 0f, -0.06f), false);

                SetLayerRecursively(t, GameLayers.FirstPersonViewmodel);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(spec.Name)).GetComponent<WeaponModelRig>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>45° canted irons on the right: aperture ring at the aligner, post <paramref name="post"/> ahead, both on the sight line.</summary>
        private static void CantedIrons(Transform weapon, float post)
        {
            Transform canted = Socket(weapon, WeaponModelRig.CantedSightSocketName, new Vector3(0.042f, 0.042f, -0.09f), Quaternion.Euler(0f, 0f, -45f));
            var model = new ModelAssembly("CantedIrons", canted, GameLayers.FirstPersonViewmodel);
            MeshKit steel = model.Static(SurfaceFinish.AnodizedBlack);
            steel.Push(new Vector3(0f, 0f, 0.005f), Quaternion.identity);
            steel.Tube(0.0085f, 0.0042f, -0.002f, 0.002f, 24); // aperture ring
            steel.Pop();
            steel.Box(new Vector3(0f, -0.0125f, 0.005f), new Vector3(0.016f, 0.008f, 0.012f), 0.0015f); // rear base
            steel.Box(new Vector3(0f, -0.009f, 0.005f), new Vector3(0.006f, 0.004f, 0.004f), 0.001f);
            steel.Box(new Vector3(0f, -0.004f, post), new Vector3(0.0025f, 0.008f, 0.003f), 0.0005f); // front post
            steel.Box(new Vector3(-0.004f, -0.004f, post), new Vector3(0.0015f, 0.01f, 0.006f), 0.0004f); // post ears
            steel.Box(new Vector3(0.004f, -0.004f, post), new Vector3(0.0015f, 0.01f, 0.006f), 0.0004f);
            steel.Box(new Vector3(0f, -0.012f, post), new Vector3(0.014f, 0.01f, 0.01f), 0.0015f); // front base
            // 45° offset mounts bridging each sight down to the rail's upper right edge (rail edge at (-0.017, -0.027) here).
            foreach (float z in new[] { 0.005f, post })
            {
                steel.Box(new Vector3(-0.0085f, -0.0215f, z), new Vector3(0.024f, 0.011f, 0.014f), 0.0015f);
                steel.Box(new Vector3(-0.0175f, -0.0285f, z), new Vector3(0.008f, 0.006f, 0.02f), 0.001f); // rail clamp
            }

            model.Finish();
            canted.gameObject.AddComponent<WeaponSight>().Configure(1f, 0.09f, 1.1f, null);
        }

        private static void SetMagazineRenderers(WeaponModelRig rig, WeaponVisual visual)
        {
            Configure(rig, so =>
            {
                SerializedProperty list = Property(so, WeaponModelRig.MagazineRenderersFieldName);
                list.arraySize = visual.MagazineRenderers.Length;
                for (int i = 0; i < visual.MagazineRenderers.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = visual.MagazineRenderers[i];
            });
        }

        private static void AddMechanism(GameObject root, WeaponVisual visual, Vector3 carrierTravel, Vector3 chargeTravel, bool handleReciprocates)
        {
            root.AddComponent<WeaponMechanism>().Configure(visual.Trigger, visual.Selector, visual.Reciprocating, carrierTravel, visual.ChargingHandle,
                chargeTravel, handleReciprocates, visual.BoltHandle);
        }

        // ---- Battle rifle ----

        private static void CreateBattleRifle(CaliberDataSO caliber)
        {
            var root = new GameObject(BattleRifleName);
            try
            {
                Transform t = root.transform;
                Transform primary = Socket(t, WeaponModelRig.PrimarySightSocketName, new Vector3(0f, 0.075f, -0.06f), Quaternion.identity);
                Socket(primary, AttachmentDataSO.MountName(AttachmentSlot.Optic), Vector3.zero, Quaternion.identity);
                Socket(t, AttachmentDataSO.MountName(AttachmentSlot.Underbarrel), new Vector3(0f, -0.016f, 0.27f), Quaternion.identity);
                Socket(t, AttachmentDataSO.MountName(AttachmentSlot.Laser), new Vector3(0.0334f, 0.006f, 0.255f), Quaternion.identity);
                Socket(t, AttachmentDataSO.MountName(AttachmentSlot.Light), new Vector3(-0.0334f, 0.006f, 0.255f), Quaternion.identity);
                Socket(t, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.456f), Quaternion.identity);
                Socket(t, WeaponModelRig.StockPivotSocketName, new Vector3(0f, -0.03f, -0.42f), Quaternion.identity);
                Socket(t, WeaponModelRig.ChamberEjectionSocketName, new Vector3(0.026f, 0.012f, 0f), Quaternion.Euler(0f, 90f, 0f));
                Transform magazineSocket = Socket(t, WeaponModelRig.MagazineSocketName, new Vector3(0f, -0.11f, 0.077f), Quaternion.identity);
                Socket(t, WeaponModelRig.ChargingHandleSocketName, new Vector3(-0.034f, 0.018f, 0.06f), Quaternion.identity);
                Socket(t, WeaponModelRig.LeftHandIKGripName, new Vector3(0f, -0.03f, 0.25f), Quaternion.identity);
                Socket(t, WeaponModelRig.RightHandIKGripName, new Vector3(0f, -0.078f, -0.08f), Quaternion.Euler(15f, 0f, 0f));

                WeaponVisual visual = ProceduralWeapons.BattleRifle(t, magazineSocket);
                var rig = root.AddComponent<WeaponModelRig>();
                rig.EnsureSockets();
                SetMagazineRenderers(rig, visual);
                AddMechanism(root, visual, new Vector3(0f, 0f, -0.03f), new Vector3(0f, 0f, -0.1f), true);
                SetLayerRecursively(t, GameLayers.FirstPersonViewmodel);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(BattleRifleName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Rigidbody magazine = DroppedMagazine(BattleRifleName, new Vector3(0.028f, 0.13f, 0.072f), 0.55f);
            RecoilProfileSO recoil = LoadOrCreate<RecoilProfileSO>($"{DataFolder}/{BattleRifleName}_Recoil.asset");
            Configure(recoil, so =>
            {
                Set(so, "referenceMassKg", 4.1f);
                Set(so, "verticalKick", 230f);
                Set(so, "verticalSpread", 0.18f);
                Set(so, "horizontalBias", 18f);
                Set(so, "horizontalSpread", 60f);
                Set(so, "rollKick", 30f);
                Set(so, "backKick", 1.35f);
                Set(so, "upKick", 0.16f);
                Set(so, "boltCarrierImpulse", 0.35f);
                Set(so, "boltCarrierPitch", 24f);
                Set(so, "viewClimb", 0.6f);
                Set(so, "cameraKick", 42f);
                Set(so, "cameraPush", 0.07f);
                Set(so, "kickRampPerShot", 0.07f);
                Set(so, "maxKickRamp", 1.5f);
            });

            AttachmentDataSO[] build =
            {
                AssetDatabase.LoadAssetAtPath<AttachmentDataSO>(AttachmentContentBuilder.DataPath("Optic_RedDot")),
                AssetDatabase.LoadAssetAtPath<AttachmentDataSO>(AttachmentContentBuilder.DataPath("Grip_Vertical")),
                AssetDatabase.LoadAssetAtPath<AttachmentDataSO>(AttachmentContentBuilder.DataPath("Rail_Light")),
            };
            Configure(LoadOrCreate<WeaponDataSO>(DataPath(BattleRifleName)), so =>
            {
                Set(so, "displayName", "Battle Rifle 7.62");
                Set(so, "massKg", 3.45f); // receiver, barrel and stock; optic, grip and light add the rest
                Set(so, "centerOfMass", new Vector3(0f, -0.01f, 0.02f));
                SerializedProperty defaults = Property(so, "defaultAttachments");
                defaults.arraySize = build.Length;
                for (int i = 0; i < build.Length; i++) defaults.GetArrayElementAtIndex(i).objectReferenceValue = build[i];
                Set(so, "ergonomics", 42f);
                Set(so, "barrelLengthInches", 16f);
                Set(so, "caliber", caliber);
                SetEnum(so, "boltType", (int)BoltType.ClosedBolt);
                SetFireModes(so, FireMode.Semi, FireMode.Auto);
                Set(so, "roundsPerMinute", 600f);
                Set(so, "moaAccuracy", 1.5f);
                Set(so, "magazineCapacity", 20);
                Set(so, "boltHoldOpen", true);
                Set(so, "spareMagazines", 4);
                Set(so, "aimDownSightsTime", 0.36f);
                Set(so, "magazineInsertTime", 1.0f);
                Set(so, "droppedMagazinePrefab", magazine);
                Set(so, "hipStockOffset", new Vector3(0.14f, -0.2f, -0.17f));
                Set(so, WeaponDataSO.RecoilFieldName, recoil);
                Set(so, "audioProfile", EffectsContentBuilder.WeaponAudio(BattleRifleName));
            });
        }

        // ---- Sidearm ----

        private static void CreateSidearm(CaliberDataSO caliber)
        {
            var root = new GameObject(SidearmName);
            try
            {
                Transform t = root.transform;
                Transform primary = Socket(t, WeaponModelRig.PrimarySightSocketName, new Vector3(0f, 0.0235f, -0.078f), Quaternion.identity);
                Socket(t, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.1f), Quaternion.identity);
                Socket(t, WeaponModelRig.StockPivotSocketName, new Vector3(0f, -0.05f, -0.085f), Quaternion.identity);
                Socket(t, WeaponModelRig.ChamberEjectionSocketName, new Vector3(0.014f, 0.01f, 0f), Quaternion.Euler(-20f, 90f, 0f));
                Transform magazineSocket = Socket(t, WeaponModelRig.MagazineSocketName, new Vector3(0f, -0.072f, -0.056f), Quaternion.Euler(8f, 0f, 0f));
                Socket(t, WeaponModelRig.ChargingHandleSocketName, new Vector3(0f, 0.006f, -0.075f), Quaternion.identity);
                Socket(t, WeaponModelRig.LeftHandIKGripName, new Vector3(-0.016f, -0.064f, -0.05f), Quaternion.Euler(8f, 20f, -30f));
                Socket(t, WeaponModelRig.RightHandIKGripName, new Vector3(0f, -0.056f, -0.058f), Quaternion.Euler(8f, 0f, 0f));

                WeaponVisual visual = ProceduralWeapons.Sidearm(t, magazineSocket);
                primary.gameObject.AddComponent<WeaponSight>().Configure(1f, 0.36f, 1.05f, null);
                var rig = root.AddComponent<WeaponModelRig>();
                rig.EnsureSockets();
                SetMagazineRenderers(rig, visual);
                // The slide is the reciprocating part; a charge racks it through the carrier travel.
                AddMechanism(root, visual, new Vector3(0f, 0f, -0.032f), Vector3.zero, false);
                SetLayerRecursively(t, GameLayers.FirstPersonViewmodel);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(SidearmName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Rigidbody magazine = DroppedMagazine(SidearmName, new Vector3(0.022f, 0.1f, 0.03f), 0.2f);
            RecoilProfileSO recoil = LoadOrCreate<RecoilProfileSO>($"{DataFolder}/{SidearmName}_Recoil.asset");
            Configure(recoil, so =>
            {
                Set(so, "referenceMassKg", 0.85f);
                Set(so, "verticalKick", 120f);
                Set(so, "verticalSpread", 0.2f);
                Set(so, "horizontalBias", 4f);
                Set(so, "horizontalSpread", 40f);
                Set(so, "rollKick", 18f);
                Set(so, "backKick", 0.35f);
                Set(so, "upKick", 0.25f);
                Set(so, "boltCarrierImpulse", 0.1f);
                Set(so, "boltCarrierPitch", 10f);
                Set(so, "viewClimb", 0.5f);
                Set(so, "cameraKick", 14f);
                Set(so, "cameraPush", 0.02f);
                Set(so, "kickRampPerShot", 0.03f);
                Set(so, "maxKickRamp", 1.2f);
            });

            Configure(LoadOrCreate<WeaponDataSO>(DataPath(SidearmName)), so =>
            {
                Set(so, "displayName", "Sidearm 9mm");
                Set(so, "massKg", 0.85f);
                Set(so, "centerOfMass", new Vector3(0f, -0.03f, -0.01f));
                Set(so, "ergonomics", 80f);
                Set(so, "barrelLengthInches", 4.02f);
                Set(so, "caliber", caliber);
                SetEnum(so, "boltType", (int)BoltType.ClosedBolt);
                SetFireModes(so, FireMode.Semi);
                Set(so, "roundsPerMinute", 900f);
                Set(so, "moaAccuracy", 4.5f);
                Set(so, "magazineCapacity", 15);
                Set(so, "boltHoldOpen", true);
                Set(so, "spareMagazines", 3);
                Set(so, "aimDownSightsTime", 0.18f);
                Set(so, "sprintToFireTime", 0.1f);
                Set(so, "holsterTime", 0.3f);
                Set(so, "drawTime", 0.35f);
                Set(so, "magazineRemoveTime", 0.45f);
                Set(so, "speedMagazineRemoveTime", 0.18f);
                Set(so, "magazineInsertTime", 0.6f);
                Set(so, "boltCycleTime", 0.4f);
                Set(so, "droppedMagazinePrefab", magazine);
                Set(so, "hipStockOffset", new Vector3(0.12f, -0.17f, 0.24f));
                Set(so, "hipEuler", new Vector3(2f, -4f, -2f));
                Set(so, WeaponDataSO.RecoilFieldName, recoil);
                Set(so, "audioProfile", EffectsContentBuilder.WeaponAudio(SidearmName));
            });
        }

        /// <summary>Pooled physical magazine: the weapon's magazine model with a box collider, on the Debris layer.</summary>
        private static Rigidbody DroppedMagazine(string weaponName, Vector3 size, float massKg)
        {
            var magazine = new GameObject($"{weaponName}_DroppedMagazine");
            try
            {
                ProceduralWeapons.MagazineModel(weaponName, magazine.transform, GameLayers.Debris);
                magazine.layer = GameLayers.Debris;
                magazine.AddComponent<BoxCollider>().size = size;
                var body = magazine.AddComponent<Rigidbody>();
                body.mass = massKg;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                return PrefabUtility.SaveAsPrefabAsset(magazine, $"{PrefabFolder}/{magazine.name}.prefab").GetComponent<Rigidbody>();
            }
            finally
            {
                Object.DestroyImmediate(magazine);
            }
        }

        private static CaliberDataSO Caliber(string assetName, string displayName, float grams, float diameterMm, float velocity,
            float referenceBarrel, float velocityPerInch, DragModel drag, float ballisticCoefficient, float penetration, float damage,
            string casingName)
        {
            CaliberDataSO caliber = LoadOrCreate<CaliberDataSO>($"{CaliberFolder}/{assetName}.asset");
            Configure(caliber, so =>
            {
                Set(so, "displayName", displayName);
                Set(so, "bulletMassGrams", grams);
                Set(so, "bulletDiameterMm", diameterMm);
                Set(so, "referenceVelocity", velocity);
                Set(so, "referenceBarrelInches", referenceBarrel);
                Set(so, "velocityPerInch", velocityPerInch);
                SetEnum(so, "dragModel", (int)drag);
                Set(so, "ballisticCoefficient", ballisticCoefficient);
                Set(so, "penetrationPower", penetration);
                Set(so, "damage", damage);
                Set(so, "casingPrefab", BallisticsContentBuilder.Casing(casingName));
            });
            return caliber;
        }

        internal static Transform Part(Transform parent, string partName, PrimitiveType shape, Vector3 position, Vector3 size,
            Quaternion rotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = partName;
            Object.DestroyImmediate(part.GetComponent<Collider>()); // the viewmodel never collides; barrel tucking casts instead
            part.transform.SetParent(parent, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part.transform;
        }

        internal static Transform Socket(Transform parent, string socketName, Vector3 position, Quaternion rotation)
        {
            var socket = new GameObject(socketName).transform;
            socket.SetParent(parent, false);
            socket.SetLocalPositionAndRotation(position, rotation);
            return socket;
        }

        internal static MeshRenderer Disc(Transform parent, string discName, Mesh mesh, float depth, float radius, Material material)
        {
            var disc = new GameObject(discName);
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = new Vector3(0f, 0f, depth);
            disc.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            disc.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = disc.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return meshRenderer;
        }

        internal static Material UnlitMaterial(string materialName, Color color, Texture texture, bool alphaClip)
        {
            string path = $"{ArtFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find("HDRP/Unlit")) { name = materialName };
            material.SetColor("_UnlitColor", color);
            if (texture != null) material.SetTexture("_UnlitColorMap", texture);
            if (alphaClip)
            {
                HDMaterial.SetAlphaClipping(material, true);
                material.SetFloat("_AlphaCutoff", 0.5f);
            }

            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Unit-diameter disc in the XY plane facing -Z (toward the eye); UVs map the full 0-1 square.</summary>
        internal static Mesh DiscMesh()
        {
            string path = $"{ArtFolder}/LensDisc.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;

            const int segments = 48;
            var vertices = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var normals = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            uvs[0] = new Vector2(0.5f, 0.5f);
            normals[0] = Vector3.back;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.5f;
                vertices[i + 1] = point;
                uvs[i + 1] = point + new Vector2(0.5f, 0.5f);
                normals[i + 1] = Vector3.back;
                // Clockwise as seen from -Z, so the face points at the eye.
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % segments + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            mesh = new Mesh { name = "LensDisc", vertices = vertices, uv = uvs, normals = normals, triangles = triangles };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        internal static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        internal static void Configure(Object asset, Action<SerializedObject> apply)
        {
            var serialized = new SerializedObject(asset);
            apply(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        internal static SerializedProperty Property(SerializedObject so, string field) =>
            so.FindProperty(field) ?? throw new InvalidOperationException($"{so.targetObject.GetType().Name} has no serialized field '{field}'.");

        internal static void Set(SerializedObject so, string field, float value) => Property(so, field).floatValue = value;
        internal static void Set(SerializedObject so, string field, int value) => Property(so, field).intValue = value;
        internal static void Set(SerializedObject so, string field, bool value) => Property(so, field).boolValue = value;
        internal static void Set(SerializedObject so, string field, string value) => Property(so, field).stringValue = value;
        internal static void Set(SerializedObject so, string field, Object value) => Property(so, field).objectReferenceValue = value;
        internal static void Set(SerializedObject so, string field, Vector3 value) => Property(so, field).vector3Value = value;
        internal static void Set(SerializedObject so, string field, Color value) => Property(so, field).colorValue = value;
        internal static void SetEnum(SerializedObject so, string field, int index) => Property(so, field).enumValueIndex = index;

        private static void SetFireModes(SerializedObject so, params FireMode[] modes)
        {
            SerializedProperty list = Property(so, "fireModes");
            list.arraySize = modes.Length;
            for (int i = 0; i < modes.Length; i++) list.GetArrayElementAtIndex(i).enumValueIndex = (int)modes[i];
        }

        internal static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
