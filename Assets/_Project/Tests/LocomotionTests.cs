using System.Collections;
using System.Collections.Generic;
using FPS.Core;
using FPS.Locomotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class LocomotionTests : InputTestFixture
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private Keyboard keyboard;
        private InputActionAsset actions;
        private InputReader reader;
        private PlayerRig rig;
        private StanceAndLeanController stance;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            reader = TestInput.CreateReader(out actions);

            Block(new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f));
            var player = new GameObject("Player");
            created.Add(player);
            rig = PlayerRig.Build(player, reader);
            stance = rig.GetComponent<StanceAndLeanController>();
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

        [Test]
        public void Stance_CannotStandUpUnderLowCeiling()
        {
            Assert.IsTrue(stance.TrySetStance(Stance.Crouch));
            Assert.AreEqual(1.15f, rig.GetComponent<CharacterController>().height, 1e-4f);

            GameObject ceiling = Block(new Vector3(0f, 1.45f, 0f), new Vector3(3f, 0.3f, 3f));
            Physics.SyncTransforms();
            Assert.IsFalse(stance.TrySetStance(Stance.Stand), "stood up into a 1.3 m ceiling");
            Assert.AreEqual(Stance.Crouch, stance.Current);

            Object.DestroyImmediate(ceiling);
            Physics.SyncTransforms();
            Assert.IsTrue(stance.TrySetStance(Stance.Stand));
        }

        [Test]
        public void Prone_BodyBlockedByWallAhead_FitsAlongIt()
        {
            Block(new Vector3(0f, 0.5f, 0.6f), new Vector3(0.6f, 1f, 0.2f));
            Physics.SyncTransforms();

            Assert.IsFalse(stance.IsProneBodyClear(Vector3.zero, 0f));
            Assert.IsTrue(stance.IsProneBodyClear(Vector3.zero, 90f));
            Assert.IsFalse(stance.TrySetStance(Stance.Prone), "went prone with the head inside a wall");
        }

        [UnityTest]
        public IEnumerator Lean_IsLimitedByWall_FullOnOpenSide()
        {
            rig.GetComponent<RealisticLocomotionController>().enabled = false; // drive the lean directly
            Block(new Vector3(0.35f, 1.5f, 0f), new Vector3(0.1f, 3f, 2f));
            Physics.SyncTransforms();

            stance.SetLeanInput(1);
            yield return new WaitForSeconds(0.8f);
            Assert.That(stance.LeanAngle, Is.InRange(4f, 16.5f), "lean into the wall");

            stance.SetLeanInput(-1);
            yield return new WaitForSeconds(0.8f);
            Assert.That(stance.LeanAngle, Is.LessThan(-17f), "lean away from the wall");
        }

        [Test]
        public void Mount_FindsLedgeAndCorner_NotOpenSpace()
        {
            Assert.IsFalse(stance.TryMount(0f, 0f), "mounted on nothing");

            GameObject ledge = Block(new Vector3(0f, 0.7f, 0.55f), new Vector3(2f, 1.4f, 0.3f));
            Physics.SyncTransforms();
            Assert.IsTrue(stance.TryMount(0f, 0f));
            Assert.AreEqual(MountType.Ledge, stance.Mount);
            Assert.AreEqual(0.2f, stance.SwayMultiplier, 1e-4f);
            stance.Unmount();
            Object.DestroyImmediate(ledge);

            Block(new Vector3(-1f, 1.5f, 0.5f), new Vector3(2.1f, 3f, 0.2f)); // vertical edge at x = 0.05, open to the right
            Physics.SyncTransforms();
            Assert.IsTrue(stance.TryMount(0f, 0f));
            Assert.AreEqual(MountType.Corner, stance.Mount);
        }

        [Test]
        public void CrouchKeyTap_ChangesStanceThroughInputReader()
        {
            currentTime = 1.0;
            Press(keyboard.cKey);
            currentTime = 1.1;
            Release(keyboard.cKey);
            Assert.AreEqual(Stance.Crouch, stance.Current);
        }

        [Test]
        public void Momentum_ReversingBrakesBeforeAccelerating()
        {
            Vector3 v = RealisticLocomotionController.AccelerateTowards(
                new Vector3(0f, 0f, 3f), new Vector3(0f, 0f, -3f), 8f, 10f, 0.1f);
            Assert.AreEqual(2f, v.z, 1e-4f, "still moving forward after 0.1 s of braking");
        }

        [Test]
        public void Slide_FrictionSlowsOnFlat_GravityWinsOnSteepSlope()
        {
            Vector3 flat = RealisticLocomotionController.IntegrateSlide(new Vector3(0f, 0f, 6f), Vector3.up, 0.42f, 9.81f, 0.1f);
            Assert.AreEqual(6f - 0.42f * 9.81f * 0.1f, flat.z, 1e-4f);

            Vector3 descending35 = Quaternion.Euler(35f, 0f, 0f) * Vector3.up; // ground falls away toward +z
            Vector3 downhill = RealisticLocomotionController.IntegrateSlide(new Vector3(0f, 0f, 6f), descending35, 0.42f, 9.81f, 0.1f);
            Assert.Greater(downhill.z, 6f);
        }

        private GameObject Block(Vector3 position, Vector3 size)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.layer = GameLayers.WorldGeometry;
            block.transform.position = position;
            block.transform.localScale = size;
            created.Add(block);
            return block;
        }
    }
}
