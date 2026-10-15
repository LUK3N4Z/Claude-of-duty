using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class SpringTests
    {
        [Test]
        public void ImpulseResponse_MatchesClosedForm_AtAnyFrameRate()
        {
            var critical = new ProceduralSpringSolver.Tuning(2f, 1f);
            var oneFrame = new ProceduralSpringSolver(critical, critical);
            var tenFrames = new ProceduralSpringSolver(critical, critical);
            oneFrame.AddImpulse(Vector3.right, Vector3.up * 10f);
            tenFrames.AddImpulse(Vector3.right, Vector3.up * 10f);

            oneFrame.Step(0.1f);
            for (int i = 0; i < 10; i++) tenFrames.Step(0.01f);

            float omega = 2f * Mathf.PI * 2f;
            float expected = 0.1f * Mathf.Exp(-omega * 0.1f); // x(t) = v0·t·e^(-ωt) for a kick from rest
            Assert.AreEqual(expected, oneFrame.Position.x, 1e-5f);
            Assert.AreEqual(10f * expected, oneFrame.Rotation.y, 1e-4f);
            Assert.AreEqual(oneFrame.Position.x, tenFrames.Position.x, 1e-5f);
        }

        [Test]
        public void UnderdampedSpring_Settles()
        {
            var bouncy = new ProceduralSpringSolver.Tuning(5f, 0.3f);
            var spring = new ProceduralSpringSolver(bouncy, bouncy);
            spring.AddImpulse(Vector3.one, Vector3.one * 100f);
            for (int i = 0; i < 180; i++) spring.Step(1f / 60f);
            Assert.Less(spring.Position.magnitude, 1e-3f);
            Assert.Less(spring.Rotation.magnitude, 1e-2f);
        }
    }

    public class ViewmodelTests : InputTestFixture
    {
        private const float PrimaryEyeRelief = 0.07f;

        private readonly List<Object> created = new List<Object>();
        private Keyboard keyboard;
        private Mouse mouse;
        private InputActionAsset actions;
        private InputReader reader;
        private RealisticLocomotionController locomotion;
        private ViewmodelRig viewmodel;
        private ProceduralADSAndOpticsController ads;
        private RealisticWeaponInertia inertia;
        private PhysicalRecoilEngine recoil;
        private Camera camera;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            reader = TestInput.CreateReader(out actions);

            Block(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            var player = new GameObject("Player");
            created.Add(player);
            PlayerRig rig = PlayerRig.Build(player, reader);
            viewmodel = ViewmodelRig.Build(rig, null, null);
            viewmodel.Equip(TestWeaponTemplate(), TestWeaponData());

            locomotion = player.GetComponent<RealisticLocomotionController>();
            ads = player.GetComponent<ProceduralADSAndOpticsController>();
            inertia = player.GetComponent<RealisticWeaponInertia>();
            recoil = player.GetComponent<PhysicalRecoilEngine>();
            camera = rig.PlayerCamera;
            Physics.SyncTransforms();
        }

        public override void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            Object.DestroyImmediate(reader);
            Object.DestroyImmediate(actions);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator Ads_AlignsPrimaryThenCantedSightWithTheEye()
        {
            Press(mouse.rightButton);
            yield return new WaitForSeconds(0.8f);

            Assert.Greater(ads.AdsBlend, 0.99f);
            Transform eye = camera.transform;
            Assert.Less(Vector3.Distance(ads.ActiveAligner.position, eye.position + eye.forward * PrimaryEyeRelief), 0.006f);
            Assert.Less(Vector3.Angle(ads.ActiveAligner.forward, eye.forward), 1.5f);

            PressAndRelease(keyboard.xKey);
            yield return new WaitForSeconds(0.8f);

            Assert.AreEqual(1, ads.ActiveSightSlot);
            Assert.Less(Vector3.Angle(ads.ActiveAligner.up, eye.up), 1.5f, "canted sight not brought upright");
            Assert.That(Vector3.Angle(viewmodel.Weapon.transform.up, eye.up), Is.InRange(40f, 50f), "weapon not rolled onto its cant");
        }

        [UnityTest]
        public IEnumerator BarrelCollision_TucksAwayFromWall_AndBlocksAiming()
        {
            Block(new Vector3(0f, 1.5f, 0.45f), new Vector3(2f, 3f, 0.1f)); // wall face at z = 0.4
            Physics.SyncTransforms();
            Press(mouse.rightButton);
            yield return new WaitForSeconds(0.6f);

            Assert.IsTrue(inertia.IsTucked);
            Assert.Less(ads.AdsBlend, 0.05f, "aimed through a wall");
            Assert.Less(viewmodel.Weapon.MuzzleSocket.position.z, 0.45f, "muzzle inside the wall");
        }

        [UnityTest]
        public IEnumerator Recoil_KicksMuzzleUpAboutStock_ClimbsView_AndRecovers()
        {
            yield return new WaitForSeconds(0.4f); // settle into the hip pose
            Transform space = viewmodel.ViewmodelRoot;
            WeaponModelRig weapon = viewmodel.Weapon;
            Vector3 muzzleRest = space.InverseTransformPoint(weapon.MuzzleSocket.position);
            Vector3 stockRest = space.InverseTransformPoint(weapon.StockPivotSocket.position);
            float pitchBefore = locomotion.Pitch;

            recoil.Kick();
            yield return new WaitForSeconds(0.04f);

            float muzzleRise = space.InverseTransformPoint(weapon.MuzzleSocket.position).y - muzzleRest.y;
            float stockShift = Vector3.Distance(space.InverseTransformPoint(weapon.StockPivotSocket.position), stockRest);
            Assert.Greater(muzzleRise, 0.01f);
            Assert.Less(stockShift, muzzleRise, "recoil should pivot about the stock");
            Assert.Less(locomotion.Pitch, pitchBefore - 0.1f, "view did not climb");

            yield return new WaitForSeconds(1.5f);
            Assert.Less(Vector3.Distance(space.InverseTransformPoint(weapon.MuzzleSocket.position), muzzleRest), 0.006f);
        }

        private void PressAndRelease(UnityEngine.InputSystem.Controls.ButtonControl key)
        {
            Press(key);
            Release(key);
        }

        private WeaponModelRig TestWeaponTemplate()
        {
            var root = new GameObject("TestWeapon");
            created.Add(root);
            Socket(root.transform, WeaponModelRig.MuzzleSocketName, new Vector3(0f, 0f, 0.6f), Quaternion.identity);
            Socket(root.transform, WeaponModelRig.StockPivotSocketName, new Vector3(0f, -0.02f, -0.4f), Quaternion.identity);
            Socket(root.transform, WeaponModelRig.PrimarySightSocketName, new Vector3(0f, 0.075f, -0.06f), Quaternion.identity)
                .gameObject.AddComponent<WeaponSight>().Configure(1f, PrimaryEyeRelief, 1.25f, null);
            Socket(root.transform, WeaponModelRig.CantedSightSocketName, new Vector3(0.042f, 0.042f, -0.09f), Quaternion.Euler(0f, 0f, -45f))
                .gameObject.AddComponent<WeaponSight>().Configure(1f, 0.09f, 1.1f, null);
            var rig = root.AddComponent<WeaponModelRig>();
            rig.EnsureSockets();
            return rig;
        }

        private WeaponDataSO TestWeaponData()
        {
            var data = ScriptableObject.CreateInstance<WeaponDataSO>();
            var profile = ScriptableObject.CreateInstance<RecoilProfileSO>();
            created.Add(data);
            created.Add(profile);
            typeof(WeaponDataSO)
                .GetField(WeaponDataSO.RecoilFieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(data, profile);
            return data;
        }

        private static Transform Socket(Transform parent, string socketName, Vector3 position, Quaternion rotation)
        {
            var socket = new GameObject(socketName).transform;
            socket.SetParent(parent, false);
            socket.SetLocalPositionAndRotation(position, rotation);
            return socket;
        }

        private void Block(Vector3 position, Vector3 size)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.layer = GameLayers.WorldGeometry;
            block.transform.position = position;
            block.transform.localScale = size;
            created.Add(block);
        }
    }
}
