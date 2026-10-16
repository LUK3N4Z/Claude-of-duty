using FPS.AI;
using FPS.Ballistics;
using FPS.Core;
using Unity.AI.Navigation;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Hangs <see cref="TacticalDoor"/>s in doorways: narrows an opening to a 0.95 m frame with jambs and a header in the
    /// wall's material, fills the frame with an Interactable-layer trigger (the door stays in reach open or closed), then
    /// adds the hinge (kinematic body), the wooden leaf (solid world geometry, penetrable) with handles, and the door
    /// sounds. The swinging part is left out of NavMesh bakes, so soldiers path through and push it open.
    /// </summary>
    internal static class DoorBuilder
    {
        internal const float DoorWidth = 0.95f;
        internal const float DoorHeight = 2.05f;
        internal const float LeafThickness = 0.045f;
        private const float FrameHeight = 2.1f;

        /// <summary>
        /// A door in an opening <paramref name="openingWidth"/> wide in a wall <paramref name="wallHeight"/> high and
        /// <paramref name="wallThickness"/> thick, centred on <paramref name="floorCenter"/> (world) with its facing
        /// (+Z through the wall) <paramref name="facing"/>.
        /// </summary>
        public static TacticalDoor Doorway(Transform parent, string doorName, Vector3 floorCenter, Quaternion facing, float openingWidth,
            float wallHeight, float wallThickness, string wallSurface)
        {
            var root = new GameObject(doorName) { layer = GameLayers.Interactable }.transform;
            root.SetParent(parent, true);
            root.SetPositionAndRotation(floorCenter, facing);
            var reachZone = root.gameObject.AddComponent<BoxCollider>();
            reachZone.isTrigger = true;
            reachZone.center = new Vector3(0f, DoorHeight * 0.5f, 0f);
            reachZone.size = new Vector3(DoorWidth - 0.04f, DoorHeight - 0.04f, wallThickness + 0.1f);

            float filler = (openingWidth - DoorWidth) * 0.5f;
            if (filler > 0.01f)
            {
                Piece(root, "Jamb Left", new Vector3(-(DoorWidth + filler) * 0.5f, wallHeight * 0.5f, 0f), new Vector3(filler, wallHeight, wallThickness), wallSurface);
                Piece(root, "Jamb Right", new Vector3((DoorWidth + filler) * 0.5f, wallHeight * 0.5f, 0f), new Vector3(filler, wallHeight, wallThickness), wallSurface);
            }

            if (wallHeight > FrameHeight + 0.05f)
                Piece(root, "Header", new Vector3(0f, (FrameHeight + wallHeight) * 0.5f, 0f), new Vector3(DoorWidth, wallHeight - FrameHeight, wallThickness), wallSurface);

            var hinge = new GameObject("Door Hinge") { layer = GameLayers.WorldGeometry };
            hinge.transform.SetParent(root, false);
            hinge.transform.localPosition = new Vector3(-DoorWidth * 0.5f + 0.01f, 0.02f, 0f);
            var body = hinge.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var unbaked = hinge.AddComponent<NavMeshModifier>();
            unbaked.ignoreFromBuild = true;

            GameObject leaf = Piece(hinge.transform, "Door Leaf", new Vector3(DoorWidth * 0.5f, DoorHeight * 0.5f, 0f),
                new Vector3(DoorWidth - 0.02f, DoorHeight, LeafThickness), "Wood");
            foreach (float side in new[] { 1f, -1f })
            {
                GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                handle.name = side > 0f ? "Handle Front" : "Handle Back";
                Object.DestroyImmediate(handle.GetComponent<Collider>());
                handle.transform.SetParent(hinge.transform, false);
                handle.transform.localPosition = new Vector3(DoorWidth - 0.1f, 1.02f, side * (LeafThickness * 0.5f + 0.03f));
                handle.transform.localScale = new Vector3(0.13f, 0.022f, 0.022f);
                handle.GetComponent<MeshRenderer>().sharedMaterial = EnvironmentMaterials.Get(EnvironmentSurface.PaintedSteel);
            }

            var door = root.gameObject.AddComponent<TacticalDoor>();
            door.Configure(hinge.transform, leaf.transform.localScale, OpenSound(), CloseSound(), BashSound());
            return door;
        }

        /// <summary>Latch click, then the hinge creaking in stick-slip pulses.</summary>
        internal static AudioClip OpenSound() => GunshotAudioBuilder.Layer("Foley", "Door", "Open", 1, i =>
        {
            AudioSynth creak = new AudioSynth(0.7f, 81).Click(0.5f, 0f, 3200f);
            var random = new System.Random(82);
            float t = 0.06f;
            while (t < 0.55f)
            {
                creak.Click(0.35f * (1f - t / 0.6f), t, 900f + (float)random.NextDouble() * 500f);
                t += 0.018f + (float)random.NextDouble() * 0.014f;
            }

            return creak.Tone(180f, 150f, 0.25f, 0.02f, 0.01f, 0.2f).LowPass(6000f);
        })[0];

        /// <summary>The leaf meeting the frame and the latch snapping in.</summary>
        internal static AudioClip CloseSound() => GunshotAudioBuilder.Layer("Foley", "Door", "Close", 1, i => new AudioSynth(0.4f, 83)
            .Tone(140f, 100f, 0.8f, 0f, 0.002f, 0.06f).Partials(new[] { 620f, 1150f }, 0.4f, 0f, 0.04f).Click(0.6f, 0.006f, 2800f)
            .Noise(0.3f, 0f, 0.0005f, 0.02f).LowPass(5000f))[0];

        /// <summary>A shoulder bash: the leaf bursting open, its panel ringing, then slamming into the stop.</summary>
        internal static AudioClip BashSound() => GunshotAudioBuilder.Layer("Foley", "Door", "Bash", 1, i => new AudioSynth(0.8f, 84)
            .Noise(1f, 0f, 0.0005f, 0.05f).Tone(110f, 70f, 1f, 0f, 0.002f, 0.15f).Partials(new[] { 380f, 760f, 1340f }, 0.6f, 0f, 0.1f)
            .Click(0.8f, 0.004f, 2400f).Noise(0.6f, 0.26f, 0.0005f, 0.04f).Tone(160f, 110f, 0.6f, 0.26f, 0.002f, 0.08f).LowPass(4500f))[0];

        private static GameObject Piece(Transform parent, string pieceName, Vector3 localPosition, Vector3 size, string surface)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = pieceName;
            piece.layer = GameLayers.WorldGeometry;
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = size;
            piece.AddComponent<SurfaceIdentifier>().Surface = BallisticsContentBuilder.Surface(surface);
            piece.GetComponent<MeshRenderer>().sharedMaterial = EnvironmentMaterials.Get(EnvironmentMaterials.ForBallisticSurface(surface));
            return piece;
        }
    }
}
