using System.Collections;
using FPS.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    public class EventBusAndPoolTests
    {
        private struct Ping
        {
            public int Value;
        }

        private PoolProbe prefab;
        private ObjectPoolManager pool;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("ProbePrefab").AddComponent<PoolProbe>();
            prefab.gameObject.SetActive(false); // template only; the pool must activate its clones
            pool = ObjectPoolManager.Instance;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(prefab.gameObject);
            Object.DestroyImmediate(pool.gameObject);
        }

        [Test]
        public void EventBus_DeliversUntilUnsubscribed_AndClearAllDropsHandlers()
        {
            int sum = 0;
            BusHandler<Ping> handler = (in Ping e) => sum += e.Value;

            EventBus<Ping>.Subscribe(handler);
            EventBus<Ping>.Raise(new Ping { Value = 2 });
            EventBus<Ping>.Unsubscribe(handler);
            EventBus<Ping>.Raise(new Ping { Value = 5 });
            Assert.AreEqual(2, sum);

            EventBus<Ping>.Subscribe(handler);
            EventBus.ClearAll();
            Assert.IsFalse(EventBus<Ping>.HasSubscribers);
        }

        [Test]
        public void Pool_ReusesInstances_RunsCallbacks_IgnoresDoubleRelease()
        {
            pool.Prewarm(prefab, 3, 8);

            PoolProbe a = pool.Spawn(prefab, Vector3.one, Quaternion.identity);
            Assert.IsTrue(a.gameObject.activeInHierarchy);
            Assert.AreEqual(Vector3.one, a.transform.position);
            Assert.AreEqual(1, a.Spawned);

            pool.Release(a);
            pool.Release(a);
            Assert.IsFalse(a.gameObject.activeInHierarchy);
            Assert.AreEqual(1, a.Despawned);

            PoolProbe b = pool.Spawn(prefab, Vector3.zero, Quaternion.identity);
            Assert.AreSame(a, b);
            Assert.AreEqual(2, b.Spawned);
            pool.Release(b);
        }

        [UnityTest]
        public IEnumerator Pool_TimedRelease_SkipsInstanceReusedAfterManualRelease()
        {
            PoolProbe first = pool.Spawn(prefab, Vector3.zero, Quaternion.identity, lifetime: 0.05f);
            pool.Release(first);
            PoolProbe reused = pool.Spawn(prefab, Vector3.zero, Quaternion.identity);
            Assert.AreSame(first, reused);
            PoolProbe timed = pool.Spawn(prefab, Vector3.zero, Quaternion.identity, lifetime: 0.05f);

            yield return new WaitForSeconds(0.2f);

            Assert.IsTrue(reused.gameObject.activeInHierarchy, "stale timer released a reused instance");
            Assert.IsFalse(timed.gameObject.activeInHierarchy, "timed instance was not released");
            pool.Release(reused);
        }
    }

    public class InputReaderTests : InputTestFixture
    {
        private Keyboard keyboard;
        private Mouse mouse;
        private InputActionAsset actions;
        private InputReader reader;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            reader = TestInput.CreateReader(out actions);
            reader.EnablePlayerInput();
        }

        public override void TearDown()
        {
            Object.DestroyImmediate(reader);
            Object.DestroyImmediate(actions);
            base.TearDown();
        }

        [Test]
        public void ReloadTap_SecondTapInWindow_UpgradesToFastReload()
        {
            int reloads = 0, fast = 0, checks = 0;
            reader.ReloadPressed += () => reloads++;
            reader.FastReloadPressed += () => fast++;
            reader.CheckMagazineChanged += _ => checks++;

            Tap(keyboard.rKey, 1.0);
            Assert.AreEqual((1, 0), (reloads, fast));

            Tap(keyboard.rKey, 1.2);
            Assert.AreEqual((1, 1, 0), (reloads, fast, checks));

            Tap(keyboard.rKey, 3.0);
            Assert.AreEqual((2, 1), (reloads, fast), "a tap outside the window starts a new reload");
        }

        [Test]
        public void ReloadHold_ChecksMagazine_WithoutReloading()
        {
            int reloads = 0, changes = 0;
            reader.ReloadPressed += () => reloads++;
            reader.CheckMagazineChanged += _ => changes++;

            currentTime = 1.0;
            Press(keyboard.rKey);
            currentTime = 1.5;
            InputSystem.Update();
            Assert.IsTrue(reader.IsCheckingMagazine);

            currentTime = 1.8;
            Release(keyboard.rKey);
            Assert.IsFalse(reader.IsCheckingMagazine);
            Assert.AreEqual((0, 2), (reloads, changes));
        }

        [Test]
        public void StanceKey_TapCrouches_HoldOrCtrlGoesProne()
        {
            int crouches = 0, prones = 0;
            reader.CrouchPressed += () => crouches++;
            reader.PronePressed += () => prones++;

            Tap(keyboard.cKey, 1.0);
            Assert.AreEqual((1, 0), (crouches, prones));

            currentTime = 2.0;
            Press(keyboard.cKey);
            currentTime = 2.5;
            InputSystem.Update();
            currentTime = 2.6;
            Release(keyboard.cKey);
            Assert.AreEqual((1, 1), (crouches, prones));

            Tap(keyboard.leftCtrlKey, 3.0);
            Assert.AreEqual((1, 2), (crouches, prones));
        }

        [Test]
        public void SprintDoubleTap_RequestsTacticalSprint()
        {
            int sprints = 0, tactical = 0;
            reader.SprintPressed += () => sprints++;
            reader.TacticalSprintPressed += () => tactical++;

            Tap(keyboard.leftShiftKey, 1.0);
            Tap(keyboard.leftShiftKey, 1.2);
            Assert.AreEqual((2, 1), (sprints, tactical));
        }

        [Test]
        public void DisablingInputMidPress_StillReleasesTrigger()
        {
            int pressed = 0, released = 0;
            reader.FirePressed += () => pressed++;
            reader.FireReleased += () => released++;

            Press(mouse.leftButton);
            Assert.IsTrue(reader.FireHeld);
            reader.DisablePlayerInput();
            Assert.AreEqual((1, 1), (pressed, released));
        }

        private void Tap(UnityEngine.InputSystem.Controls.ButtonControl key, double time)
        {
            currentTime = time;
            Press(key);
            currentTime = time + 0.1;
            Release(key);
        }
    }
}
