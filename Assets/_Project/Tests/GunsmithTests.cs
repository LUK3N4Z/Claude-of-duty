using System.Collections;
using System.Collections.Generic;
using FPS.Audio;
using FPS.Ballistics;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class GunsmithTests : InputTestFixture
    {
        private readonly List<Object> created = new List<Object>();
        private InputActionAsset actions;
        private InputReader reader;
        private ViewmodelRig viewmodel;
        private RealisticWeaponController controller;
        private ShotFiredEvent lastShot;
        private GunshotEvent lastGunshot;
        private int shots;

        public override void Setup()
        {
            base.Setup();
            TestInput.IgnoreFocus();
            reader = TestInput.CreateReader(out actions);

            Box(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            Box(new Vector3(0f, 2f, 5f), new Vector3(20f, 6f, 1f)); // wall, front face at z = 4.5

            var player = new GameObject("Player");
            created.Add(player);
            viewmodel = ViewmodelRig.Build(PlayerRig.Build(player, reader));
            controller = player.GetComponent<RealisticWeaponController>();
            Physics.SyncTransforms();

            shots = 0;
            EventBus<ShotFiredEvent>.Subscribe(OnShot);
            EventBus<GunshotEvent>.Subscribe(OnGunshot);
        }

        public override void TearDown()
        {
            EventBus<ShotFiredEvent>.Unsubscribe(OnShot);
            EventBus<GunshotEvent>.Unsubscribe(OnGunshot);
            foreach (ObjectPoolManager pool in Object.FindObjectsByType<ObjectPoolManager>())
                Object.DestroyImmediate(pool.gameObject);
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            Object.DestroyImmediate(reader);
            Object.DestroyImmediate(actions);
            base.TearDown();
        }

        [Test]
        public void Assemble_SumsMassAndErgonomics_MultipliesModifiers_AndLeavesTheAssetAlone()
        {
            WeaponDataSO baseData = Data();
            var suppressed = ScriptableObject.CreateInstance<WeaponAudioProfileSO>();
            created.Add(suppressed);
            AttachmentDataSO barrel = Part(AttachmentSlot.Barrel, 0.45f, null);
            Set(barrel, "barrelLengthInches", 10.5f);
            Set(barrel, "ergonomicsDelta", 6f);
            Set(barrel, "flashMultiplier", 1.5f);
            AttachmentDataSO can = Part(AttachmentSlot.Muzzle, 0.55f, null);
            Set(can, "ergonomicsDelta", -8f);
            Set(can, "flashMultiplier", 0f);
            Set(can, "recoilVertical", 0.9f);
            Set(can, "recoilBack", 0.85f);
            Set(can, "velocityMultiplier", 1.01f);
            Set(can, "audioOverride", suppressed);
            AttachmentDataSO grip = Part(AttachmentSlot.Underbarrel, 0.08f, null);
            Set(grip, "ergonomicsDelta", 6f);
            Set(grip, "recoilVertical", 0.92f);
            Set(grip, "adsTimeMultiplier", 0.95f);

            WeaponDataSO built = GunsmithAssembler.Assemble(baseData, new[] { barrel, null, can, grip });
            created.Add(built);

            Assert.AreNotSame(baseData, built);
            Assert.AreEqual(1.6f + 0.45f + 0.55f + 0.08f, built.MassKg, 1e-4f);
            Assert.AreEqual(50f + 6f - 8f + 6f, built.Ergonomics, 1e-4f);
            Assert.AreEqual(10.5f, built.BarrelLengthInches);
            Assert.AreEqual(0.9f * 0.92f, built.RecoilVerticalMultiplier, 1e-4f);
            Assert.AreEqual(0.85f, built.RecoilBackMultiplier, 1e-4f);
            Assert.AreEqual(1.01f, built.MuzzleVelocityMultiplier, 1e-4f);
            Assert.AreEqual(0f, built.MuzzleFlashMultiplier);
            Assert.AreSame(suppressed, built.AudioProfile);
            Assert.AreEqual(baseData.AdsTime * 0.95f * Mathf.Lerp(1.3f, 0.75f, 0.54f) / Mathf.Lerp(1.3f, 0.75f, 0.5f), built.AdsTime, 1e-4f);

            Assert.AreEqual(1.6f, baseData.MassKg, "the asset is never modified");
            Assert.AreEqual(14.5f, baseData.BarrelLengthInches);
            Assert.AreEqual(1f, baseData.MuzzleFlashMultiplier);
        }

        [UnityTest]
        public IEnumerator FittedParts_ProvideSockets_AndAFrontHeavyBuildSwingsSlower()
        {
            AttachmentDataSO barrel = BarrelPart(0.6f, 0.37f);
            AttachmentDataSO can = Part(AttachmentSlot.Muzzle, 0.55f, Model("Can", r => Socket(r, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.17f))));
            Set(can, "centerOfMass", new Vector3(0f, 0f, 0.085f));
            AttachmentDataSO grip = Part(AttachmentSlot.Underbarrel, 0.12f,
                Model("Grip", r => Socket(r, WeaponModelRig.LeftHandIKGripName, new Vector3(0f, -0.045f, 0f)).localRotation = Quaternion.Euler(75f, 0f, 0f)));
            AttachmentDataSO stock = Part(AttachmentSlot.Stock, 0.3f, Model("Stock", r => Socket(r, WeaponModelRig.StockPivotSocketName, new Vector3(0f, 0f, -0.17f))));

            yield return Equip(barrel, grip, stock);
            WeaponModelRig weapon = viewmodel.Weapon;
            float bareInertia = controller.ActiveState.Data.MomentOfInertia;
            float bareCenter = controller.ActiveState.Data.CenterOfMass.z;

            AssertAt(weapon, weapon.MuzzleSocket, new Vector3(0f, 0f, 0.05f + 0.37f), "muzzle at the barrel's end");
            AssertAt(weapon, weapon.LeftHandIKGrip, new Vector3(0f, -0.03f - 0.045f, 0.25f), "support hand on the grip");
            Assert.Less(Quaternion.Angle(weapon.LeftHandIKGrip.rotation, weapon.transform.rotation * Quaternion.Euler(75f, 0f, 0f)), 0.01f);
            AssertAt(weapon, weapon.StockPivotSocket, new Vector3(0f, -0.015f, -0.15f - 0.17f), "shoulder at the collapsed stock's butt");

            controller.SetAttachment(0, AttachmentSlot.Muzzle, can);
            yield return null;
            weapon = viewmodel.Weapon;
            WeaponDataSO built = controller.ActiveState.Data;
            AssertAt(weapon, weapon.MuzzleSocket, new Vector3(0f, 0f, 0.05f + 0.37f + 0.17f), "muzzle moved to the suppressor's end");
            Assert.Greater(built.CenterOfMass.z, bareCenter + 0.05f, "centre of mass moves forward");
            // 0.55 kg at ~0.83 m from the shoulder adds ~0.37 kg·m².
            Assert.AreEqual(0.374f, built.MomentOfInertia - bareInertia, 0.01f);

            controller.SetLoadout(1, TestWeapons.CreateTemplate(created), Data());
            controller.SelectSlot(1);
            yield return new WaitForSeconds(0.3f);
            controller.SelectSlot(0);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0, controller.ActiveSlot);
            Assert.Less(Vector3.Distance(built.CenterOfMass, controller.ActiveState.Data.CenterOfMass), 1e-5f, "re-fitting the same build measures the same");
            Assert.AreEqual(built.MomentOfInertia, controller.ActiveState.Data.MomentOfInertia, 1e-5f);
        }

        [UnityTest]
        public IEnumerator OpticSwap_IsLive_KeepsAmmo_AndAdsUsesTheNewSight()
        {
            AttachmentDataSO dot = OpticPart("RedDot", 0.1f);
            AttachmentDataSO prism = OpticPart("Prism", 0.06f);
            Set(prism, "massKg", 0.5f);

            yield return Equip(dot);
            var ads = controller.GetComponent<ProceduralADSAndOpticsController>();
            Assert.AreEqual(0.1f, ads.ActiveSight.EyeRelief, 1e-5f);

            WeaponState state = controller.ActiveState;
            state.MagazineRounds = 7;
            float mass = state.Data.MassKg;

            controller.SetAttachment(0, AttachmentSlot.Optic, prism);
            yield return null;

            Assert.AreSame(state, controller.ActiveState);
            Assert.AreEqual(7, state.MagazineRounds);
            Assert.IsTrue(state.RoundChambered);
            Assert.AreEqual(mass + 0.5f - 0.1f, state.Data.MassKg, 1e-4f);
            Assert.AreEqual(0.06f, ads.ActiveSight.EyeRelief, 1e-5f, "ADS aligns to the newly fitted optic");
            Transform mount = viewmodel.Weapon.FindNode(AttachmentDataSO.MountName(AttachmentSlot.Optic));
            Assert.AreEqual(1, mount.GetComponentsInChildren<WeaponSight>().Length, "the old optic is gone");
            CollectionAssert.Contains(controller.GetAttachments(0), prism);
            CollectionAssert.DoesNotContain(controller.GetAttachments(0), dot);
        }

        [UnityTest]
        public IEnumerator SuppressedShortBarrel_HidesFlash_SwapsAudio_AndFiresFromTheCan()
        {
            var suppressedAudio = ScriptableObject.CreateInstance<WeaponAudioProfileSO>();
            created.Add(suppressedAudio);
            AttachmentDataSO barrel = BarrelPart(0.45f, 10.5f * 0.0254f);
            Set(barrel, "barrelLengthInches", 10.5f);
            AttachmentDataSO can = Part(AttachmentSlot.Muzzle, 0.55f, Model("Can", r => Socket(r, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.17f))));
            Set(can, "flashMultiplier", 0f);
            Set(can, "velocityMultiplier", 1.01f);
            Set(can, "audioOverride", suppressedAudio);

            WeaponDataSO data = Data();
            yield return Equip(data, barrel, can);
            var vfx = controller.GetComponent<MuzzleAndSmokeVFXController>();

            controller.PullTrigger();
            controller.ReleaseTrigger();
            yield return null;

            Assert.AreEqual(1, shots);
            Assert.IsFalse(vfx.FlashVisible, "a suppressor shows no flash");
            Assert.AreSame(suppressedAudio, lastGunshot.Profile);
            Assert.AreEqual(data.Caliber.MuzzleVelocity(10.5f) * 1.01f, lastShot.MuzzleVelocity, 1e-2f);
            Assert.Less(data.Caliber.MuzzleVelocity(10.5f), data.Caliber.MuzzleVelocity(data.BarrelLengthInches), "short barrel is slower");
            Assert.AreEqual(viewmodel.Weapon.MuzzleSocket.name, WeaponModelRig.MuzzleSocketName);
            Assert.AreEqual("Can", viewmodel.Weapon.MuzzleSocket.parent.name, "bullets leave the suppressor, not the barrel");
        }

        [UnityTest]
        public IEnumerator Devices_ToggleTheVolumetricLight_AndTheLaserLandsOnTheWall()
        {
            AttachmentDataSO light = Part(AttachmentSlot.Light, 0.12f, Model("Light", r =>
                r.gameObject.AddComponent<TacticalDevice>().Configure(TacticalDeviceType.Flashlight, Socket(r, "Emitter", new Vector3(0f, 0f, 0.06f)), null)));
            var beam = new Material(Shader.Find("HDRP/Unlit"));
            created.Add(beam);
            AttachmentDataSO laser = Part(AttachmentSlot.Laser, 0.09f, Model("Laser", r =>
                r.gameObject.AddComponent<TacticalDevice>().Configure(TacticalDeviceType.Laser, Socket(r, "Emitter", new Vector3(0f, 0f, 0.03f)), beam)));

            yield return Equip(light, laser);
            TacticalDevice[] devices = viewmodel.Weapon.GetComponentsInChildren<TacticalDevice>();
            Assert.AreEqual(2, devices.Length);
            TacticalDevice flashlight = devices[0].Type == TacticalDeviceType.Flashlight ? devices[0] : devices[1];
            TacticalDevice pointer = devices[0].Type == TacticalDeviceType.Laser ? devices[0] : devices[1];
            Assert.IsFalse(flashlight.Flashlight.enabled, "devices start off");

            controller.ToggleDevices();
            yield return null;
            yield return null;

            Assert.IsTrue(flashlight.IsOn && pointer.IsOn);
            Assert.IsTrue(flashlight.Flashlight.enabled);
            Assert.AreEqual(LightType.Spot, flashlight.Flashlight.type);
            Assert.IsTrue(flashlight.Flashlight.GetComponent<HDAdditionalLightData>().affectsVolumetric, "the beam lights the fog");
            Assert.AreEqual(4.5f, pointer.LaserEnd.z, 0.01f, "laser stops on the wall");

            controller.ToggleDevices();
            Assert.IsFalse(flashlight.Flashlight.enabled);
            Assert.IsFalse(pointer.IsOn);
        }

        private IEnumerator Equip(params AttachmentDataSO[] parts) => Equip(Data(), parts);

        private IEnumerator Equip(WeaponDataSO data, params AttachmentDataSO[] parts)
        {
            controller.SetLoadout(0, Template(), data, parts);
            yield return new WaitForSeconds(0.12f); // draw
        }

        /// <summary>The shared test weapon with gunsmith mounts and no sight of its own on the primary socket.</summary>
        private WeaponModelRig Template()
        {
            WeaponModelRig rig = TestWeapons.CreateTemplate(created);
            Transform root = rig.transform;
            Object.DestroyImmediate(rig.GetSightAligner(0, out _).GetComponent<WeaponSight>());
            Socket(rig.GetSightAligner(0, out _), AttachmentDataSO.MountName(AttachmentSlot.Optic), Vector3.zero);
            Socket(root, AttachmentDataSO.MountName(AttachmentSlot.Barrel), new Vector3(0f, 0f, 0.05f));
            Socket(root, AttachmentDataSO.MountName(AttachmentSlot.Underbarrel), new Vector3(0f, -0.03f, 0.25f));
            Socket(root, AttachmentDataSO.MountName(AttachmentSlot.Stock), new Vector3(0f, -0.015f, -0.15f));
            Socket(root, AttachmentDataSO.MountName(AttachmentSlot.Laser), new Vector3(0.03f, 0f, 0.25f));
            Socket(root, AttachmentDataSO.MountName(AttachmentSlot.Light), new Vector3(-0.03f, 0f, 0.25f));
            return rig;
        }

        private WeaponDataSO Data()
        {
            var data = ScriptableObject.CreateInstance<WeaponDataSO>();
            var caliber = ScriptableObject.CreateInstance<CaliberDataSO>();
            created.Add(data);
            created.Add(caliber);
            TestWeapons.SetField(data, "caliber", caliber);
            TestWeapons.SetField(data, "fireModes", new[] { FireMode.Semi });
            TestWeapons.SetField(data, "massKg", 1.6f);
            TestWeapons.SetField(data, "ergonomics", 50f);
            TestWeapons.SetField(data, "barrelLengthInches", 14.5f);
            TestWeapons.SetField(data, "magazineCapacity", 10);
            TestWeapons.SetField(data, "sprintToFireTime", 0f);
            TestWeapons.SetField(data, "drawTime", 0.05f);
            TestWeapons.SetField(data, "holsterTime", 0.05f);
            return data;
        }

        private AttachmentDataSO BarrelPart(float massKg, float length)
        {
            AttachmentDataSO barrel = Part(AttachmentSlot.Barrel, massKg, Model("Barrel", r =>
            {
                Socket(r, AttachmentDataSO.MountName(AttachmentSlot.Muzzle), new Vector3(0f, 0f, length));
                Socket(r, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, length));
            }));
            Set(barrel, "centerOfMass", new Vector3(0f, 0f, length * 0.5f));
            return barrel;
        }

        private AttachmentDataSO OpticPart(string partName, float eyeRelief) =>
            Part(AttachmentSlot.Optic, 0.1f, Model(partName, r => r.gameObject.AddComponent<WeaponSight>().Configure(1f, eyeRelief, 1.1f, null)));

        private AttachmentDataSO Part(AttachmentSlot slot, float massKg, GameObject prefab)
        {
            var part = ScriptableObject.CreateInstance<AttachmentDataSO>();
            created.Add(part);
            Set(part, "slot", slot);
            Set(part, "massKg", massKg);
            Set(part, "prefab", prefab);
            return part;
        }

        /// <summary>An inactive part model standing in for a prefab: only fitted copies wake up.</summary>
        private GameObject Model(string partName, System.Action<Transform> build)
        {
            var root = new GameObject(partName);
            root.SetActive(false);
            created.Add(root);
            build(root.transform);
            return root;
        }

        private static void Set(Object target, string field, object value) => TestWeapons.SetField(target, field, value);

        private static Transform Socket(Transform parent, string socketName, Vector3 position)
        {
            var socket = new GameObject(socketName).transform;
            socket.SetParent(parent, false);
            socket.localPosition = position;
            return socket;
        }

        private static void AssertAt(WeaponModelRig weapon, Transform socket, Vector3 expected, string message)
        {
            Vector3 local = weapon.transform.InverseTransformPoint(socket.position);
            Assert.Less(Vector3.Distance(expected, local), 1e-4f, $"{message}: {local:F4}");
        }

        private void Box(Vector3 position, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.layer = GameLayers.WorldGeometry;
            box.transform.position = position;
            box.transform.localScale = size;
            created.Add(box);
        }

        private void OnShot(in ShotFiredEvent evt)
        {
            shots++;
            lastShot = evt;
        }

        private void OnGunshot(in GunshotEvent evt) => lastGunshot = evt;
    }
}
