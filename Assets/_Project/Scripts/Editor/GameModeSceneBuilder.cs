using System;
using System.Collections.Generic;
using FPS.AI;
using FPS.Ballistics;
using FPS.Core;
using FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static FPS.EditorTools.TestWeaponBuilder;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Builds one tactical arena — a roofed two-room warehouse (both rooms CQB rooms), a walled compound to the west, a
    /// container yard to the east, cover lines across the middle — three times, each with one game mode: Tactical Raid
    /// (two objectives, a garrison of six, extraction in the north-east), Search &amp; Destroy (sites A in the warehouse
    /// and B in the yard, 3v3) and Hardpoint (three rotating zones, 4v4 with respawns). Each scene gets its director,
    /// HUD, spawns and baked NavMesh, and is added to the build settings.
    /// </summary>
    public static class GameModeSceneBuilder
    {
        private const string SceneFolder = "Assets/_Project/Scenes";
        internal const string RaidScene = SceneFolder + "/Mode_Raid.unity";
        internal const string SearchAndDestroyScene = SceneFolder + "/Mode_SearchAndDestroy.unity";
        internal const string HardpointScene = SceneFolder + "/Mode_Hardpoint.unity";
        private const float WallHeight = 3.2f;

        [MenuItem("Tools/FPS Builder/7. Build Game Mode Scenes", priority = 7)]
        public static void BuildGameModeScenes()
        {
            GameObject playerPrefab = PlayerBuilder.CreateOrUpdatePlayerPrefab();
            if (playerPrefab == null) return;
            AIContentBuilder.CreateContent();
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildScene(RaidScene, playerPrefab, BuildRaid);
            BuildScene(SearchAndDestroyScene, playerPrefab, BuildSearchAndDestroy);
            BuildScene(HardpointScene, playerPrefab, BuildHardpoint);
            AddToBuildSettings(RaidScene, SearchAndDestroyScene, HardpointScene);
            Debug.Log("[FPS Builder] Game mode scenes built: Raid, Search & Destroy, Hardpoint.");
        }

        private static void BuildScene(string path, GameObject playerPrefab, Action<Transform, GameObject> setup)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.TryGetComponent(out Camera _)) Object.DestroyImmediate(root); // the player brings its own camera
            }

            Transform level = new GameObject("Arena").transform;
            PlayerBuilder.AtmosphereVolume(level);
            Arena(level);
            WorldLook.Interior(level, "West Room", new Vector3(-4f, WallHeight * 0.5f, 0f), new Vector3(7.6f, WallHeight, 9.6f));
            WorldLook.Interior(level, "East Room", new Vector3(4f, WallHeight * 0.5f, 0f), new Vector3(7.6f, WallHeight, 9.6f));
            EnvironmentMaterials.Dress(level, EnvironmentSurface.Dirt);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, -34f), Quaternion.identity);

            var match = new GameObject("Match");
            setup(level, match);
            match.AddComponent<MatchHUD>();

            AIContentBuilder.BakeNavMesh(level, path.Replace(".unity", "_NavMesh.asset"));
            EditorSceneManager.SaveScene(scene, path);
        }

        // ---- Modes ----

        private static void BuildRaid(Transform level, GameObject match)
        {
            Spawns(level, false);
            // The garrison starts at the objectives.
            foreach (Vector3 post in new[]
                     {
                         new Vector3(3f, 0f, 2f), new Vector3(6.5f, 0f, -2.5f), new Vector3(2f, 0f, -3f),
                         new Vector3(-21f, 0f, 2.5f), new Vector3(-18.5f, 0f, -2f), new Vector3(-16.5f, 0f, 2f),
                     })
                Spawn(level, "Garrison Post", post, 180f, 1, true);

            Block(level, "Intel Desk", new Vector3(6f, 0.4f, 3.4f), new Vector3(1.4f, 0.8f, 0.7f));
            HoldInteraction intel = Terminal(level, "Intel Laptop", new Vector3(6f, 0.95f, 3.1f), "Hold to download intel", 6f);
            Block(level, "Radio Relay", new Vector3(-20f, 0.75f, 0f), new Vector3(0.8f, 1.5f, 0.6f));
            HoldInteraction radio = Terminal(level, "Radio Relay Panel", new Vector3(-20f, 1f, -0.4f), "Hold to plant the tracker", 5f);

            ObjectiveZone lz = Zone(level, "LZ Viper", new Vector3(30f, 0f, 30f), 5f);
            Marker(lz.transform, new Color(1f, 0.45f, 0.1f));

            Director(match, new[] { 0, 6 }, 3);
            ExtractionMode mode = match.AddComponent<ExtractionMode>();
            ModeSettings(mode, "Tactical Raid", 900f, -1f);
            mode.Configure(new[] { intel, radio }, new[] { "Download the intel (warehouse)", "Plant the tracker (west compound)" }, lz);
        }

        private static void BuildSearchAndDestroy(Transform level, GameObject match)
        {
            Spawns(level, true);
            ObjectiveZone siteA = Zone(level, "A", new Vector3(-4f, 0f, 0f), 3.5f);
            ObjectiveZone siteB = Zone(level, "B", new Vector3(21f, 0f, 0f), 4f);
            HoldInteraction plantA = Terminal(level, "Site A Plant Spot", new Vector3(-5.5f, 0.3f, 1.5f), "Hold to plant the charge", 5f);
            HoldInteraction plantB = Terminal(level, "Site B Plant Spot", new Vector3(21f, 0.3f, -0.5f), "Hold to plant the charge", 5f);
            Marker(siteA.transform, new Color(0.9f, 0.2f, 0.15f));
            Marker(siteB.transform, new Color(0.9f, 0.2f, 0.15f));

            Director(match, new[] { 2, 3 }, 2);
            SearchAndDestroyMode mode = match.AddComponent<SearchAndDestroyMode>();
            ModeSettings(mode, "Search & Destroy", 120f, -1f);
            mode.Configure(new[] { siteA, siteB }, new[] { plantA, plantB }, EquipmentContentBuilder.PlantedCharge());
        }

        private static void BuildHardpoint(Transform level, GameObject match)
        {
            Spawns(level, true);
            ObjectiveZone alpha = Zone(level, "Alpha (compound)", new Vector3(-20f, 0f, 0f), 5f);
            ObjectiveZone bravo = Zone(level, "Bravo (warehouse)", new Vector3(4f, 0f, 0f), 3.5f);
            ObjectiveZone charlie = Zone(level, "Charlie (yard)", new Vector3(21f, 0f, 0f), 5f);
            foreach (ObjectiveZone zone in new[] { alpha, bravo, charlie }) Marker(zone.transform, new Color(0.2f, 0.75f, 1f));

            Director(match, new[] { 3, 4 }, 4);
            HardpointMode mode = match.AddComponent<HardpointMode>();
            ModeSettings(mode, "Hardpoint", 600f, 4f);
            mode.Configure(new[] { alpha, bravo, charlie });
        }

        private static MatchDirector Director(GameObject match, int[] soldiers, int squadSize)
        {
            MatchDirector director = match.AddComponent<MatchDirector>();
            director.Configure(new[] { AIContentBuilder.FriendlySoldier(), AIContentBuilder.Soldier() }, soldiers);
            Configure(director, so => Set(so, "squadSize", squadSize));
            return director;
        }

        private static void ModeSettings(GameMode mode, string displayName, float timeLimit, float respawnDelay) =>
            Configure(mode, so =>
            {
                Set(so, "displayName", displayName);
                Set(so, "timeLimit", timeLimit);
                Set(so, "respawnDelay", respawnDelay);
            });

        // ---- Arena ----

        private static void Arena(Transform level)
        {
            Block(level, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(84f, 1f, 84f), "Dirt");
            Block(level, "Perimeter N", new Vector3(0f, 2f, 41.5f), new Vector3(84f, 4f, 0.6f));
            Block(level, "Perimeter S", new Vector3(0f, 2f, -41.5f), new Vector3(84f, 4f, 0.6f));
            Block(level, "Perimeter E", new Vector3(41.5f, 2f, 0f), new Vector3(0.6f, 4f, 84f));
            Block(level, "Perimeter W", new Vector3(-41.5f, 2f, 0f), new Vector3(0.6f, 4f, 84f));

            // Warehouse: two rooms, doors south and north of each, a connecting door in the dividing wall.
            Transform warehouse = Group(level, "Warehouse");
            foreach (float z in new[] { -5f, 5f })
            {
                Block(warehouse, $"Wall {(z < 0f ? "S" : "N")} West", new Vector3(-6.4f, WallHeight * 0.5f, z), new Vector3(3.2f, WallHeight, 0.3f), "Brick");
                Block(warehouse, $"Wall {(z < 0f ? "S" : "N")} Middle", new Vector3(0f, WallHeight * 0.5f, z), new Vector3(6.4f, WallHeight, 0.3f), "Brick");
                Block(warehouse, $"Wall {(z < 0f ? "S" : "N")} East", new Vector3(6.4f, WallHeight * 0.5f, z), new Vector3(3.2f, WallHeight, 0.3f), "Brick");
            }

            Block(warehouse, "Wall W", new Vector3(-8f, WallHeight * 0.5f, 0f), new Vector3(0.3f, WallHeight, 10.3f), "Brick");
            Block(warehouse, "Wall E", new Vector3(8f, WallHeight * 0.5f, 0f), new Vector3(0.3f, WallHeight, 10.3f), "Brick");
            Block(warehouse, "Divider S", new Vector3(0f, WallHeight * 0.5f, -2.9f), new Vector3(0.3f, WallHeight, 4.2f), "Drywall");
            Block(warehouse, "Divider N", new Vector3(0f, WallHeight * 0.5f, 2.9f), new Vector3(0.3f, WallHeight, 4.2f), "Drywall");
            Block(warehouse, "Roof", new Vector3(0f, WallHeight + 0.15f, 0f), new Vector3(16.3f, 0.3f, 10.3f));
            Room(warehouse, "West Room", -4f, Quaternion.Euler(0f, -90f, 0f));
            Room(warehouse, "East Room", 4f, Quaternion.Euler(0f, 90f, 0f));

            // West compound.
            Transform compound = Group(level, "West Compound");
            Block(compound, "Low Wall 1", new Vector3(-22f, 0.625f, -4f), new Vector3(3f, 1.25f, 0.3f));
            Block(compound, "Low Wall 2", new Vector3(-17.5f, 0.625f, 4.5f), new Vector3(3f, 1.25f, 0.3f));
            Block(compound, "Tall Wall 1", new Vector3(-25.5f, 1.2f, 1f), new Vector3(0.3f, 2.4f, 4f));
            Block(compound, "Tall Wall 2", new Vector3(-14.5f, 1.2f, -1.5f), new Vector3(0.3f, 2.4f, 3f));
            Block(compound, "Crate 1", new Vector3(-20f, 0.6f, 7f), new Vector3(1.2f, 1.2f, 1.2f), "Wood");
            Block(compound, "Crate 2", new Vector3(-21.5f, 0.6f, -7.5f), new Vector3(1.2f, 1.2f, 1.2f), "Wood");

            // East container yard.
            Transform yard = Group(level, "Container Yard");
            Block(yard, "Container 1", new Vector3(19f, 1.3f, -5.5f), new Vector3(6f, 2.6f, 2.4f), "Metal");
            Block(yard, "Container 2", new Vector3(25f, 1.3f, 4f), new Vector3(2.4f, 2.6f, 6f), "Metal");
            Block(yard, "Container 3", new Vector3(17f, 1.3f, 7f), new Vector3(6f, 2.6f, 2.4f), "Metal");
            Block(yard, "Crate 1", new Vector3(22.5f, 0.6f, -1.5f), new Vector3(1.2f, 1.2f, 1.2f), "Wood");
            Block(yard, "Crate 2", new Vector3(15.5f, 0.6f, 1f), new Vector3(1.2f, 1.2f, 1.2f), "Wood");

            // Cover lines across the open ground north and south.
            Transform field = Group(level, "Field Cover");
            Block(field, "North Low 1", new Vector3(-10f, 0.625f, 18f), new Vector3(3f, 1.25f, 0.3f));
            Block(field, "North Low 2", new Vector3(1f, 0.625f, 20f), new Vector3(3f, 1.25f, 0.3f));
            Block(field, "North Low 3", new Vector3(11f, 0.625f, 17f), new Vector3(3f, 1.25f, 0.3f));
            Block(field, "North Tall", new Vector3(-17f, 1.2f, 15f), new Vector3(3f, 2.4f, 0.3f));
            Block(field, "South Low 1", new Vector3(-6f, 0.625f, -18f), new Vector3(3f, 1.25f, 0.3f));
            Block(field, "South Low 2", new Vector3(6f, 0.625f, -19f), new Vector3(3f, 1.25f, 0.3f));
            Block(field, "South Low 3", new Vector3(-15f, 0.625f, -20f), new Vector3(3f, 1.25f, 0.3f));
            Block(field, "South Tall", new Vector3(13f, 1.2f, -16f), new Vector3(3f, 2.4f, 0.3f));
        }

        /// <summary>A warehouse room as a CQB room: doors south (enter +Z), north (enter -Z) and the connecting door.</summary>
        private static void Room(Transform warehouse, string roomName, float x, Quaternion fromConnector)
        {
            Transform room = Group(warehouse, roomName);
            room.position = new Vector3(x, WallHeight * 0.5f, 0f);
            Transform south = Point(room, "South Door", new Vector3(x, 0f, -5f), Quaternion.identity);
            Transform north = Point(room, "North Door", new Vector3(x, 0f, 5f), Quaternion.Euler(0f, 180f, 0f));
            Transform connector = Point(room, "Connecting Door", Vector3.zero, fromConnector);
            float side = Mathf.Sign(x);
            room.gameObject.AddComponent<CQBRoom>().Configure(new Vector3(7.6f, WallHeight, 9.6f), new[] { south, north, connector }, new[]
            {
                Point(room, "Domination 1", new Vector3(x + 3.2f * side, 0f, -4.2f), Quaternion.identity),
                Point(room, "Domination 2", new Vector3(x - 3.2f * side, 0f, -4.2f), Quaternion.identity),
                Point(room, "Domination 3", new Vector3(x + 3.2f * side, 0f, 4.2f), Quaternion.identity),
                Point(room, "Domination 4", new Vector3(x - 3.2f * side, 0f, 4.2f), Quaternion.identity),
            });
        }

        private static void Spawns(Transform level, bool northInitial)
        {
            Transform spawns = Group(level, "Spawns");
            foreach (float x in new[] { -12f, -6f, 0f, 6f, 12f })
            {
                Spawn(spawns, "South", new Vector3(x, 0f, -34f), 0f, 0, true);
                Spawn(spawns, "North", new Vector3(x, 0f, 34f), 180f, 1, northInitial);
            }

            // Respawn-only points round the edge, scored by line of sight at the moment of respawn.
            Spawn(spawns, "Edge", new Vector3(-34f, 0f, -20f), 45f, -1, false);
            Spawn(spawns, "Edge", new Vector3(-34f, 0f, 20f), 135f, -1, false);
            Spawn(spawns, "Edge", new Vector3(34f, 0f, -20f), -45f, -1, false);
            Spawn(spawns, "Edge", new Vector3(34f, 0f, 20f), -135f, -1, false);
            Spawn(spawns, "Edge", new Vector3(-36f, 0f, 0f), 90f, -1, false);
            Spawn(spawns, "Edge", new Vector3(36f, 0f, 0f), -90f, -1, false);
            Spawn(spawns, "Edge", new Vector3(0f, 0f, -26f), 0f, -1, false);
            Spawn(spawns, "Edge", new Vector3(0f, 0f, 26f), 180f, -1, false);
        }

        private static void Spawn(Transform parent, string spawnName, Vector3 position, float yaw, int team, bool initial)
        {
            Transform point = Point(parent, $"{spawnName} Spawn ({(team < 0 ? "any" : team == 0 ? "BLUFOR" : "OPFOR")})", position, Quaternion.Euler(0f, yaw, 0f));
            point.gameObject.AddComponent<SpawnPoint>().Configure(team, initial);
        }

        private static ObjectiveZone Zone(Transform level, string zoneName, Vector3 position, float radius)
        {
            Transform zone = Point(level, $"Zone {zoneName}", position, Quaternion.identity);
            ObjectiveZone component = zone.gameObject.AddComponent<ObjectiveZone>();
            component.Configure(zoneName, radius, WallHeight);
            return component;
        }

        /// <summary>A hold-to-use spot on the Interactable layer.</summary>
        private static HoldInteraction Terminal(Transform level, string terminalName, Vector3 position, string prompt, float seconds)
        {
            var terminal = new GameObject(terminalName) { layer = GameLayers.Interactable };
            terminal.transform.SetParent(level, false);
            terminal.transform.position = position;
            var trigger = terminal.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.9f, 0.7f, 0.9f);
            HoldInteraction hold = terminal.AddComponent<HoldInteraction>();
            hold.Configure(prompt, seconds);
            return hold;
        }

        /// <summary>A thin coloured pole marking an objective (no collider).</summary>
        private static void Marker(Transform zone, Color color)
        {
            Part(zone, "Marker", PrimitiveType.Cylinder, new Vector3(0f, 1.5f, 0f), new Vector3(0.08f, 1.5f, 0.08f), Quaternion.identity,
                LitMaterial($"Marker_{ColorUtility.ToHtmlStringRGB(color)}", color, 0f, 0.5f));
        }

        private static Transform Group(Transform parent, string groupName)
        {
            Transform group = new GameObject(groupName).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static Transform Point(Transform parent, string pointName, Vector3 position, Quaternion rotation)
        {
            Transform point = new GameObject(pointName).transform;
            point.SetParent(parent, true);
            point.SetPositionAndRotation(position, rotation);
            return point;
        }

        internal static void Block(Transform parent, string blockName, Vector3 position, Vector3 size, string surface = "Concrete")
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = blockName;
            block.layer = GameLayers.WorldGeometry;
            block.transform.SetParent(parent, true);
            block.transform.position = position;
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = LitMaterial("CoverConcrete", new Color(0.46f, 0.45f, 0.43f), 0f, 0.2f);
            block.AddComponent<SurfaceIdentifier>().Surface = BallisticsContentBuilder.Surface(surface);
            GameObjectUtility.SetStaticEditorFlags(block, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
        }

        private static void AddToBuildSettings(params string[] paths)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (string path in paths)
            {
                if (scenes.Exists(scene => scene.path == path)) continue;
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
