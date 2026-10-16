#if UNITY_EDITOR
using System.Collections;
using FPS.Gameplay;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    /// <summary>
    /// Playtest polish: weapon racks show their weapons, wall tucks and the vault carry keep part of the weapon in frame,
    /// the magazine check shows the magazine, a bolt stroke comes off the scope, and the PiP scope is exposed by the main
    /// camera (radiance in, exposure-weighted emission out).
    /// </summary>
    public class ViewmodelPolishTests : AITestWorld
    {
        private Mouse mouse;
        private RealisticWeaponController weapons;
        private ViewmodelRig viewmodel;
        private Camera view;

        private static WeaponModelRig Model(string weapon) => AssetDatabase.LoadAssetAtPath<WeaponModelRig>($"Assets/_Project/Prefabs/Weapons/{weapon}.prefab");
        private static WeaponDataSO Data(string weapon) => AssetDatabase.LoadAssetAtPath<WeaponDataSO>($"Assets/_Project/Data/Weapons/{weapon}_Data.asset");

        public override void Setup()
        {
            base.Setup();
            mouse = InputSystem.AddDevice<Mouse>();
            weapons = player.GetComponent<RealisticWeaponController>();
            viewmodel = player.GetComponent<ViewmodelRig>();
            view = player.GetComponent<PlayerRig>().PlayerCamera;
        }

        private IEnumerator Carry(string weapon)
        {
            weapons.SetLoadout(0, Model(weapon), Data(weapon));
            yield return new WaitForSeconds(Data(weapon).DrawTime + 0.4f);
        }

        /// <summary>
        /// Headless runs have no GPU: the first PiP scope camera to switch on makes HDRP log while it builds its pipeline.
        /// </summary>
        private static void ToleratePipelineErrorsWithoutGpu()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>Share of <paramref name="samples"/> points from <paramref name="from"/> to <paramref name="to"/> on screen and nearer than <paramref name="depth"/>.</summary>
        private float InView(Vector3 from, Vector3 to, float depth = float.PositiveInfinity, float margin = 0f, int samples = 40)
        {
            int seen = 0;
            for (int i = 0; i < samples; i++)
            {
                Vector3 v = view.WorldToViewportPoint(Vector3.Lerp(from, to, i / (samples - 1f)));
                if (v.z > view.nearClipPlane && v.z < depth && v.x > margin && v.x < 1f - margin && v.y > margin && v.y < 1f - margin) seen++;
            }

            return seen / (float)samples;
        }

        /// <summary>Share of the bore (stock to muzzle) on screen in front of a wall <paramref name="depth"/> m ahead.</summary>
        private float BoreInView(float depth = float.PositiveInfinity) =>
            InView(viewmodel.Weapon.StockPivotSocket.position, viewmodel.Weapon.MuzzleSocket.position, depth);

        /// <summary>Share of the bore inside the central fifth of the screen.</summary>
        private float BoreAtCentre() =>
            InView(viewmodel.Weapon.StockPivotSocket.position, viewmodel.Weapon.MuzzleSocket.position, float.PositiveInfinity, 0.4f);

        // ---- Weapon racks ----

        [UnityTest]
        public IEnumerator WeaponRack_ShowsItsWeaponsOnThePegs_AndHidesTheCarriedOne()
        {
            yield return Carry("TestRifle");
            var rackObject = new GameObject("Rack");
            created.Add(rackObject);
            rackObject.transform.position = new Vector3(0f, 0.2f, 3f);
            Transform pegA = Peg(rackObject.transform, -0.3f), pegB = Peg(rackObject.transform, 0.3f);
            var rack = rackObject.AddComponent<WeaponRack>();
            rack.Configure(0, new[]
            {
                new WeaponRack.Option { model = Model("TestRifle"), data = Data("TestRifle"), peg = pegA },
                new WeaponRack.Option { model = Model("BattleRifle"), data = Data("BattleRifle"), peg = pegB },
            });
            yield return null;
            yield return null;

            GameObject carbine = rack.Display(0), battleRifle = rack.Display(1);
            Assert.IsNotNull(carbine);
            Assert.IsNotNull(battleRifle);
            Assert.IsFalse(carbine.activeSelf, "the carried carbine is not on the rack");
            Assert.IsTrue(battleRifle.activeSelf);
            Assert.Less(Vector3.Angle(battleRifle.transform.forward, pegB.up), 1f, "stands muzzle up along the peg");
            Assert.Less(Vector3.Angle(battleRifle.transform.right, pegB.forward), 1f, "right side toward the room");

            float lowest = float.PositiveInfinity;
            int renderers = 0;
            foreach (MeshRenderer meshRenderer in battleRifle.GetComponentsInChildren<MeshRenderer>())
            {
                Assert.AreEqual(0, meshRenderer.gameObject.layer, "a world prop, not the viewmodel layer");
                lowest = Mathf.Min(lowest, meshRenderer.bounds.min.y);
                renderers++;
            }

            Assert.Greater(renderers, 3, "the default build with its parts");
            Assert.AreEqual(pegB.position.y, lowest, 0.03f, "the butt rests on the peg");
            Assert.IsEmpty(battleRifle.GetComponentsInChildren<Collider>(), "nothing to bump into or shoot");

            rack.Interact(player);
            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(carbine.activeSelf, "the carbine goes back on its peg");
            Assert.IsFalse(battleRifle.activeSelf, "the battle rifle left with the player");
        }

        private static Transform Peg(Transform rack, float x)
        {
            var peg = new GameObject("Peg").transform;
            peg.SetParent(rack, false);
            peg.localPosition = new Vector3(x, 0f, 0f);
            return peg;
        }

        // ---- Tucks and the vault carry ----

        [UnityTest]
        public IEnumerator WallTuck_HighAndLowReady_KeepPartOfTheWeaponInFrame()
        {
            var inertia = player.GetComponent<RealisticWeaponInertia>();
            foreach (string weapon in new[] { "TestRifle", "BattleRifle", "TestMarksman" })
            {
                yield return Carry(weapon);
                foreach (float distance in new[] { 0.3f, 0.4f })
                {
                    Transform wall = Wall(new Vector3(0f, 1.5f, distance + 0.05f), new Vector3(3f, 3f, 0.1f));
                    Physics.SyncTransforms();
                    yield return new WaitForSeconds(0.8f);
                    Assert.IsTrue(inertia.IsTucked, $"{weapon} at {distance} m: tucked");
                    float seen = BoreInView(distance);
                    Assert.Greater(seen, 0.08f, $"{weapon} at {distance} m: part of the weapon stays in frame");
                    Assert.AreEqual(0f, BoreAtCentre(), $"{weapon} at {distance} m: the centre of the view stays clear");
                    Object.DestroyImmediate(wall.gameObject);
                    Physics.SyncTransforms();
                    yield return new WaitForSeconds(0.8f);
                }
            }
        }

        [UnityTest]
        public IEnumerator VaultCarry_KeepsPartOfTheWeaponInFrame()
        {
            yield return Carry("TestRifle");
            var traversal = player.GetComponent<MantleAndVaultController>();
            Wall(new Vector3(0f, 0.45f, 1f), new Vector3(3f, 0.9f, 0.35f));
            Physics.SyncTransforms();
            Press(keyboard.wKey);
            yield return null;
            PressAndRelease(keyboard.spaceKey);
            yield return null;
            Assert.AreEqual(TraversalKind.Vault, traversal.Kind);

            float carried = 0f, seenAtCarry = 0f;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                if (traversal.CarryBlend > carried)
                {
                    carried = traversal.CarryBlend;
                    seenAtCarry = BoreInView();
                }

                yield return null;
            }

            Release(keyboard.wKey);
            Assert.Greater(carried, 0.8f, "one-handed carry over the wall");
            Assert.Greater(seenAtCarry, 0.08f, "the carried rifle stays at the edge of the view");
        }

        // ---- Magazine check ----

        [UnityTest]
        public IEnumerator MagazineCheck_BringsTheMagazineIntoView()
        {
            foreach (string weapon in new[] { "TestRifle", "BattleRifle", "TestMarksman", "Sidearm" })
            {
                yield return Carry(weapon);
                weapons.SetMagazineCheck(true);
                yield return new WaitForSeconds(0.7f);
                Assert.AreEqual(WeaponAction.MagazineCheck, weapons.CurrentAction);
                Transform magazine = viewmodel.Weapon.MagazineSocket;
                float depth = Data(weapon).BarrelLengthInches < 8f ? 0.08f : 0.15f; // a pistol's magazine sits in the grip
                Assert.AreEqual(1f, InView(magazine.position, magazine.position - magazine.up * depth, float.PositiveInfinity, 0.03f),
                    $"{weapon}: the whole magazine is on screen");
                weapons.SetMagazineCheck(false);
                yield return new WaitForSeconds(0.5f);
            }
        }

        // ---- Bolt action ----

        [UnityTest]
        public IEnumerator BoltStroke_ComesOffTheScope_AndBackOnAfter()
        {
            ToleratePipelineErrorsWithoutGpu();
            yield return Carry("TestMarksman");
            var ads = player.GetComponent<ProceduralADSAndOpticsController>();
            Press(mouse.rightButton);
            yield return new WaitForSeconds(weapons.ActiveState.Data.AdsTime + 0.3f);
            Assert.Greater(ads.AdsBlend, 0.95f, "scoped in");

            weapons.PullTrigger();
            yield return null;
            weapons.ReleaseTrigger();
            float deadline = Time.time + 3f;
            while (weapons.CurrentAction != WeaponAction.BoltCycle && Time.time < deadline) yield return null;
            Assert.AreEqual(WeaponAction.BoltCycle, weapons.CurrentAction);

            float lowestBlend = 1f, nearestHand = float.PositiveInfinity;
            while (weapons.CurrentAction == WeaponAction.BoltCycle)
            {
                lowestBlend = Mathf.Min(lowestBlend, ads.AdsBlend);
                Vector3 hand = view.WorldToViewportPoint(weapons.LeftHandTarget.position);
                if (hand.z > view.nearClipPlane) nearestHand = Mathf.Min(nearestHand, Vector2.Distance(hand, new Vector2(0.5f, 0.5f)));
                yield return null;
            }

            Assert.Less(lowestBlend, 0.1f, "off the scope while the bolt runs");
            Assert.Greater(nearestHand, 0.15f, "the hand on the bolt stays clear of the view centre");
            yield return new WaitForSeconds(weapons.ActiveState.Data.AdsTime + 0.3f);
            Assert.Greater(ads.AdsBlend, 0.95f, "back on the scope, still aiming");
            Release(mouse.rightButton);
        }

        // ---- PiP scope exposure ----

        [UnityTest]
        public IEnumerator Scope_RendersRadiance_AndTheLensEmitsItThroughTheMainExposure()
        {
            ToleratePipelineErrorsWithoutGpu();
            yield return Carry("TestMarksman");
            var ads = player.GetComponent<ProceduralADSAndOpticsController>();
            var scopeData = ads.ScopeCamera.GetComponent<HDAdditionalCameraData>();
            Assert.IsTrue(scopeData.customRenderingSettings);
            foreach (FrameSettingsField field in ProceduralADSAndOpticsController.RadianceOnlyFields)
            {
                Assert.IsTrue(scopeData.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field], $"{field} overridden");
                Assert.IsFalse(scopeData.renderingPathCustomFrameSettings.IsEnabled(field), $"{field} is the main camera's job");
            }

            Assert.Less(player.GetComponent<RealisticLocomotionController>().Velocity.magnitude, 0.01f,
                "standing still: the stick-to-ground push is not body motion (it lifted the weapon off the eye)");
            Press(mouse.rightButton);
            yield return new WaitForSeconds(weapons.ActiveState.Data.AdsTime + 0.3f);
            Vector3 eye = ads.ActiveAligner.InverseTransformPoint(view.transform.position);
            Assert.Less(new Vector2(eye.x, eye.y).magnitude, ads.ActiveSight.EyeBoxRadius * 0.5f, "the eye sits in the exit pupil: no shadow crescent");
            Renderer lens = ads.ActiveSight.LensRenderer;
            var block = new MaterialPropertyBlock();
            lens.GetPropertyBlock(block);
            Assert.AreEqual(ads.LensTexture, block.GetTexture("_EmissiveColorMap"), "the lens emits the scope picture");
            Material material = lens.sharedMaterial;
            Assert.AreEqual("HDRP/Unlit", material.shader.name);
            Assert.IsTrue(material.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"));
            Assert.AreEqual(1f, material.GetFloat("_EmissiveExposureWeight"), "exposed like the world");
            Assert.AreEqual(Color.black, material.GetColor("_UnlitColor"), "no unexposed colour on top");
            Release(mouse.rightButton);
        }
    }
}
#endif
