using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using FPS.AI;
using FPS.Ballistics;
using FPS.Core;
using FPS.Weapons;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using static FPS.EditorTools.TestWeaponBuilder;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Generates the opposing force: callout voices (Windows speech synthesis, three voices), a soldier prefab dressed as
    /// a tactical operator (NavMesh agent, IK arms on a gunsmith-built carbine, invisible hitbox colliders under
    /// <see cref="ProceduralOperators"/> visuals, helmet armour, perception, brain, two grenades)
    /// and, for the test range, a cover yard held by a three-man squad, the shoot house as a clearable CQB room, and
    /// the baked NavMesh.
    /// </summary>
    public static class AIContentBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/AI";
        private const string VoiceDataFolder = "Assets/_Project/Data/AI";
        private const string VoiceAudioFolder = "Assets/_Project/Art/Audio/Voice";
        private const string SoldierPath = PrefabFolder + "/AI_Soldier.prefab";
        private const string FriendlySoldierPath = PrefabFolder + "/AI_Soldier_Friendly.prefab";
        private const string NavMeshPath = "Assets/_Project/Scenes/LocomotionTestRange_NavMesh.asset";

        private struct VoiceSpec
        {
            public string Name;
            public int VoiceIndex;
            public int Rate;
            public string Pitch;
        }

        private static readonly VoiceSpec[] Voices =
        {
            new VoiceSpec { Name = "Alpha", VoiceIndex = 0, Rate = 2, Pitch = "-10%" },
            new VoiceSpec { Name = "Bravo", VoiceIndex = 1, Rate = 3, Pitch = "+5%" },
            new VoiceSpec { Name = "Charlie", VoiceIndex = 0, Rate = 1, Pitch = "-25%" },
        };

        internal static AIVoiceProfileSO Voice(string voiceName) =>
            AssetDatabase.LoadAssetAtPath<AIVoiceProfileSO>($"{VoiceDataFolder}/Voice_{voiceName}.asset");

        internal static GameObject Soldier() => AssetDatabase.LoadAssetAtPath<GameObject>(SoldierPath);
        /// <summary>The same soldier on the player's side (team 0, tan uniform).</summary>
        internal static GameObject FriendlySoldier() => AssetDatabase.LoadAssetAtPath<GameObject>(FriendlySoldierPath);

        public static void CreateContent()
        {
            EquipmentContentBuilder.CreateContent();
            ProjectSetupWizard.EnsureFolder(PrefabFolder);
            ProjectSetupWizard.EnsureFolder(VoiceDataFolder);
            CreateVoices();
            CreateSoldierPrefab(SoldierPath, 1, ProceduralOperators.Kit.Hostile, "Alpha");
            CreateSoldierPrefab(FriendlySoldierPath, 0, ProceduralOperators.Kit.Friendly, "Bravo");
            AssetDatabase.SaveAssets();
            Debug.Log("[FPS Builder] AI soldier, voices and grenades ready.");
        }

        // ---- Voices ----

        private static void CreateVoices()
        {
            Array callouts = Enum.GetValues(typeof(CalloutType));
            GenerateSpeech(callouts);

            foreach (VoiceSpec spec in Voices)
            {
                AIVoiceProfileSO profile = LoadOrCreate<AIVoiceProfileSO>($"{VoiceDataFolder}/Voice_{spec.Name}.asset");
                Configure(profile, so =>
                {
                    SerializedProperty lines = Property(so, "lines");
                    lines.arraySize = callouts.Length;
                    for (int i = 0; i < callouts.Length; i++)
                    {
                        var callout = (CalloutType)callouts.GetValue(i);
                        SerializedProperty line = lines.GetArrayElementAtIndex(i);
                        line.FindPropertyRelative("callout").enumValueIndex = (int)callout;
                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SpeechPath(spec, callout));
                        SerializedProperty clips = line.FindPropertyRelative("clips");
                        clips.arraySize = clip != null ? 1 : 0;
                        if (clip != null) clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
                    }
                });
            }
        }

        private static string SpeechPath(VoiceSpec spec, CalloutType callout) => $"{VoiceAudioFolder}/{spec.Name}/{callout}.wav";

        /// <summary>
        /// Placeholder voice acting through the Windows speech synthesizer (one PowerShell run for every missing line).
        /// Elsewhere the callouts stay silent until recorded clips are assigned to the voice profiles.
        /// </summary>
        private static void GenerateSpeech(Array callouts)
        {
            var script = new StringBuilder();
            script.AppendLine("Add-Type -AssemblyName System.Speech");
            script.AppendLine("$s = New-Object System.Speech.Synthesis.SpeechSynthesizer");
            script.AppendLine("$male = @($s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Gender -eq 'Male' } | ForEach-Object { $_.VoiceInfo.Name })");
            script.AppendLine("$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)");
            script.AppendLine("function Say($index, $rate, $pitch, $text, $path) {");
            script.AppendLine("  if ($male.Count -gt 0) { $s.SelectVoice($male[$index % $male.Count]) }");
            script.AppendLine("  $s.Rate = $rate");
            script.AppendLine("  $s.SetOutputToWaveFile($path, $fmt)");
            script.AppendLine("  $s.SpeakSsml(\"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><prosody pitch='$pitch' volume='x-loud'>$text</prosody></speak>\")");
            script.AppendLine("  $s.SetOutputToNull()");
            script.AppendLine("}");

            int missing = 0;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            foreach (VoiceSpec spec in Voices)
            {
                ProjectSetupWizard.EnsureFolder($"{VoiceAudioFolder}/{spec.Name}");
                foreach (CalloutType callout in callouts)
                {
                    string assetPath = SpeechPath(spec, callout);
                    if (File.Exists(Path.Combine(projectRoot, assetPath))) continue;
                    string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath)).Replace("'", "''");
                    string text = AIVoiceProfileSO.Text(callout).Replace("'", "''");
                    script.AppendLine($"Say {spec.VoiceIndex} {spec.Rate} '{spec.Pitch}' '{text}' '{fullPath}'");
                    missing++;
                }
            }

            if (missing == 0) return;
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                Debug.LogWarning("[FPS Builder] Voice lines need the Windows speech synthesizer; callouts stay silent until clips are assigned.");
                return;
            }

            string scriptPath = Path.Combine(Path.GetTempPath(), "fps_ai_voices.ps1");
            File.WriteAllText(scriptPath, script.ToString(), new UTF8Encoding(true));
            try
            {
                using (Process process = Process.Start(new ProcessStartInfo("powershell.exe",
                           $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"")
                       {
                           UseShellExecute = false,
                           CreateNoWindow = true,
                           RedirectStandardError = true,
                       }))
                {
                    string errors = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(120000)) process.Kill();
                    if (process.ExitCode != 0 || errors.Length > 0) Debug.LogWarning($"[FPS Builder] Voice synthesis reported: {errors}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[FPS Builder] Voice synthesis unavailable ({exception.Message}); callouts stay silent.");
                return;
            }
            finally
            {
                File.Delete(scriptPath);
            }

            AssetDatabase.Refresh();
        }

        // ---- Soldier ----

        private static void CreateSoldierPrefab(string path, int team, ProceduralOperators.Kit kit, string voiceName)
        {

            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path)) { layer = GameLayers.Player };
            try
            {
                Transform t = root.transform;
                var agent = root.AddComponent<NavMeshAgent>();
                agent.radius = 0.3f;
                agent.height = 1.8f;
                agent.speed = 3.5f;
                agent.angularSpeed = 720f;
                agent.acceleration = 10f;
                agent.stoppingDistance = 0.1f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.9f, 0f);
                capsule.radius = 0.28f;
                capsule.height = 1.8f;

                TacticalHealthAndArmorSystem health = root.AddComponent<TacticalHealthAndArmorSystem>();
                Configure(health, so =>
                {
                    SerializedProperty armor = Property(so, TacticalHealthAndArmorSystem.ArmorFieldName);
                    armor.arraySize = 1;
                    armor.GetArrayElementAtIndex(0).FindPropertyRelative("piece").objectReferenceValue = BallisticsContentBuilder.Armor("Helmet_IIIA");
                });

                Transform legs = Bone(t, "Legs", Vector3.zero);
                Zone(legs, "Left Leg", HitZone.LeftLeg, PrimitiveType.Cube, new Vector3(-0.1f, 0.5f, 0f), new Vector3(0.15f, 1f, 0.17f), Quaternion.identity);
                Zone(legs, "Right Leg", HitZone.RightLeg, PrimitiveType.Cube, new Vector3(0.1f, 0.5f, 0f), new Vector3(0.15f, 1f, 0.17f), Quaternion.identity);
                ProceduralOperators.Legs(kit, legs, GameLayers.Hitbox);

                Transform pelvis = Bone(t, "Pelvis", new Vector3(0f, 1f, 0f));
                Transform torso = Bone(pelvis, "Torso", Vector3.zero);
                Zone(torso, "Stomach", HitZone.Stomach, PrimitiveType.Cube, new Vector3(0f, 0.11f, 0f), new Vector3(0.32f, 0.22f, 0.2f), Quaternion.identity);
                ProceduralOperators.Torso(kit, torso, GameLayers.Hitbox);
                Transform chest = Bone(torso, "Chest", new Vector3(0f, 0.35f, 0f));
                Zone(chest, "Thorax", HitZone.Thorax, PrimitiveType.Cube, Vector3.zero, new Vector3(0.38f, 0.3f, 0.22f), Quaternion.identity);
                ProceduralOperators.Chest(kit, chest, GameLayers.Hitbox);
                Transform head = Bone(torso, "Head", new Vector3(0f, 0.65f, 0f));
                Zone(head, "Head", HitZone.Head, PrimitiveType.Sphere, new Vector3(0f, 0f, -0.02f), new Vector3(0.22f, 0.24f, 0.22f), Quaternion.identity);
                ProceduralOperators.Head(kit, head, GameLayers.Hitbox);
                var voice = head.gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                Transform aimPivot = Bone(torso, "AimPivot", new Vector3(0.16f, 0.47f, 0.06f));

                AIBody.Arm left = BuildArm(torso, "Left", new Vector3(-0.21f, 0.48f, 0f), new Vector3(-0.4f, 0.15f, 0.25f), kit, HitZone.LeftArm);
                AIBody.Arm right = BuildArm(torso, "Right", new Vector3(0.21f, 0.48f, 0f), new Vector3(0.45f, 0.2f, -0.05f), kit, HitZone.RightArm);

                AIBody body = root.AddComponent<AIBody>();
                body.Configure(pelvis, torso, chest, head, aimPivot, legs, left, right);
                root.AddComponent<PerceivableTarget>().SetTeam(team);
                root.AddComponent<AIPerceptionSensor>().Configure(head, team);

                AIWeaponHandler weapon = root.AddComponent<AIWeaponHandler>();
                weapon.Configure(AssetDatabase.LoadAssetAtPath<WeaponModelRig>(PrefabPath(CarbineName)),
                    AssetDatabase.LoadAssetAtPath<WeaponDataSO>(DataPath(CarbineName)),
                    new[]
                    {
                        Attachment("Barrel_14in"), Attachment("Muzzle_FlashHider"), Attachment("Optic_RedDot"),
                        Attachment("Grip_Angled"), Attachment("Stock_Fixed"), Attachment("Rail_Light"),
                    }, 3);
                root.AddComponent<TacticalAIBrain>().Configure(Voice(voiceName), EquipmentContentBuilder.Grenade(EquipmentContentBuilder.FragName), 1,
                    EquipmentContentBuilder.Grenade(EquipmentContentBuilder.FlashName), 1);

                // Every serialized change above must reach the prefab file.
                foreach (Component component in root.GetComponents<Component>()) EditorUtility.SetDirty(component);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static AttachmentDataSO Attachment(string partName) =>
            AssetDatabase.LoadAssetAtPath<AttachmentDataSO>(AttachmentContentBuilder.DataPath(partName));

        private static AIBody.Arm BuildArm(Transform torso, string side, Vector3 shoulder, Vector3 elbowHint, ProceduralOperators.Kit kit, HitZone zone)
        {
            const float upperLength = 0.31f;
            const float lowerLength = 0.33f;
            Transform upper = Bone(torso, $"{side} Upper Arm", shoulder);
            Transform lower = Bone(upper, $"{side} Forearm", new Vector3(0f, 0f, upperLength));
            Transform hand = Bone(lower, $"{side} Hand", new Vector3(0f, 0f, lowerLength));
            Zone(upper, $"{side} Upper Arm Zone", zone, PrimitiveType.Capsule, new Vector3(0f, 0f, upperLength * 0.5f), new Vector3(0.09f, upperLength * 0.5f, 0.09f),
                Quaternion.Euler(90f, 0f, 0f));
            Zone(lower, $"{side} Forearm Zone", zone, PrimitiveType.Capsule, new Vector3(0f, 0f, lowerLength * 0.5f), new Vector3(0.08f, lowerLength * 0.5f, 0.08f),
                Quaternion.Euler(90f, 0f, 0f));
            ProceduralOperators.Arm($"{kit.Name}_{side}Arm", kit.Uniform, upper, lower, hand, upperLength, lowerLength, GameLayers.Hitbox);
            return new AIBody.Arm { upper = upper, lower = lower, hand = hand, elbowHint = elbowHint };
        }

        private static Transform Bone(Transform parent, string boneName, Vector3 position)
        {
            Transform bone = new GameObject(boneName).transform;
            bone.SetParent(parent, false);
            bone.localPosition = position;
            bone.gameObject.layer = GameLayers.Player;
            return bone;
        }

        /// <summary>An invisible damage zone: a primitive collider on the Hitbox layer (the operator visuals are separate).</summary>
        private static void Zone(Transform parent, string zoneName, HitZone zone, PrimitiveType shape, Vector3 position, Vector3 size, Quaternion rotation)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = zoneName;
            part.layer = GameLayers.Hitbox;
            Object.DestroyImmediate(part.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(part.GetComponent<MeshFilter>());
            part.transform.SetParent(parent, false);
            part.transform.SetLocalPositionAndRotation(position, rotation);
            part.transform.localScale = size;
            part.AddComponent<Hitbox>().Configure(zone, null);
        }

        // ---- Test range ----

        /// <summary>
        /// Adds the cover yard and its squad, makes the shoot house a CQB room, then bakes the NavMesh over everything
        /// under <paramref name="level"/>. Call after all level geometry exists.
        /// </summary>
        internal static void BuildEncounters(Transform level)
        {
            Transform yard = new GameObject("Firefight Yard").transform;
            yard.SetParent(level, false);
            Cover(yard, "Low Wall A", new Vector3(-12f, 0.55f, 23.5f), new Vector3(2.6f, 1.1f, 0.3f));
            Cover(yard, "Low Wall B", new Vector3(-17.5f, 0.55f, 21.5f), new Vector3(2.6f, 1.1f, 0.3f));
            Cover(yard, "Low Wall C", new Vector3(-7f, 0.55f, 30f), new Vector3(2.6f, 1.1f, 0.3f));
            Cover(yard, "Tall Wall A", new Vector3(-9f, 1.2f, 24.5f), new Vector3(0.3f, 2.4f, 2.6f));
            Cover(yard, "Tall Wall B", new Vector3(-15f, 1.2f, 27.5f), new Vector3(2.6f, 2.4f, 0.3f));
            Cover(yard, "Tall Wall C", new Vector3(-20f, 1.2f, 25f), new Vector3(0.3f, 2.4f, 2.6f));
            Cover(yard, "Crates", new Vector3(-12.5f, 0.6f, 29f), new Vector3(1.2f, 1.2f, 1.2f));

            // The shoot house (built by the range at -26, 0, 6) as a clearable room: door on the south wall.
            Transform roomTransform = new GameObject("Shoot House (CQB room)").transform;
            roomTransform.SetParent(level, false);
            roomTransform.localPosition = new Vector3(-26f, 1.5f, 6f);
            Transform door = Marker(level, "Shoot House Door", new Vector3(-26f, 0f, 3f), Quaternion.identity);
            CQBRoom cqb = roomTransform.gameObject.AddComponent<CQBRoom>();
            cqb.Configure(new Vector3(5.7f, 3f, 5.7f), new[] { door }, new[]
            {
                Marker(roomTransform, "Domination 1", new Vector3(2.2f, -1.5f, -2.2f), Quaternion.identity),
                Marker(roomTransform, "Domination 2", new Vector3(-2.2f, -1.5f, -2.2f), Quaternion.identity),
                Marker(roomTransform, "Domination 3", new Vector3(2.2f, -1.5f, 2.2f), Quaternion.identity),
                Marker(roomTransform, "Domination 4", new Vector3(-2.2f, -1.5f, 2.2f), Quaternion.identity),
            });

            // Squad Alpha guards the yard facing north, away from the spawn: gunfire on the range brings them in.
            Transform squad = new GameObject("Squad Alpha").transform;
            squad.SetParent(level, false);
            squad.gameObject.AddComponent<SquadCoordinator>();
            GameObject soldier = Soldier();
            Place(soldier, squad, "Rifleman 1", new Vector3(-12f, 0f, 25.5f), "Alpha");
            Place(soldier, squad, "Rifleman 2", new Vector3(-16.5f, 0f, 24.5f), "Bravo");
            Place(soldier, squad, "Rifleman 3", new Vector3(-8f, 0f, 27f), "Charlie");

            BakeNavMesh(level, NavMeshPath);
        }

        private static void Place(GameObject prefab, Transform squad, string soldierName, Vector3 position, string voiceName)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, squad);
            instance.name = soldierName;
            instance.transform.SetLocalPositionAndRotation(position, Quaternion.identity);
            Configure(instance.GetComponent<TacticalAIBrain>(), so => Set(so, "voice", Voice(voiceName)));
        }

        private static void Cover(Transform parent, string coverName, Vector3 position, Vector3 size)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = coverName;
            block.layer = GameLayers.WorldGeometry;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = LitMaterial("CoverConcrete", new Color(0.46f, 0.45f, 0.43f), 0f, 0.2f);
            block.AddComponent<SurfaceIdentifier>().Surface = BallisticsContentBuilder.Surface("Concrete");
            GameObjectUtility.SetStaticEditorFlags(block, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        private static Transform Marker(Transform parent, string markerName, Vector3 position, Quaternion rotation)
        {
            Transform marker = new GameObject(markerName).transform;
            marker.SetParent(parent, false);
            marker.SetLocalPositionAndRotation(position, rotation);
            return marker;
        }

        /// <summary>Bakes walkable space over every collider under <paramref name="level"/> and stores it at <paramref name="assetPath"/>.</summary>
        internal static void BakeNavMesh(Transform level, string assetPath)
        {
            if (!level.TryGetComponent(out NavMeshSurface surface)) surface = level.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = GameLayers.EnvironmentMask;
            surface.BuildNavMesh();
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
        }
    }
}
