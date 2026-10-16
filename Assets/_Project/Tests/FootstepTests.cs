using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FPS.AI;
using FPS.Audio;
using FPS.Core;
using FPS.Locomotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    /// <summary>Footsteps: strikes at the gait rate, loudness from pace and fall speed, remote steps within their radius.</summary>
    public class FootstepTests : InputTestFixture
    {
        private readonly List<Object> created = new List<Object>();
        private readonly List<FootstepEvent> steps = new List<FootstepEvent>();
        private Keyboard keyboard;
        private InputActionAsset actions;
        private InputReader reader;
        private GameObject player;
        private RealisticLocomotionController locomotion;
        private float measuredRate, measuredLoudness;

        public override void Setup()
        {
            base.Setup();
            TestInput.IgnoreFocus();
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            reader = TestInput.CreateReader(out actions);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.layer = GameLayers.WorldGeometry;
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(120f, 1f, 120f);
            created.Add(ground);
            player = new GameObject("Player");
            created.Add(player);
            PlayerRig.Build(player, reader);
            player.AddComponent<FootstepEmitter>();
            locomotion = player.GetComponent<RealisticLocomotionController>();
            Physics.SyncTransforms();
            EventBus<FootstepEvent>.Subscribe(OnStep);
        }

        public override void TearDown()
        {
            EventBus<FootstepEvent>.Unsubscribe(OnStep);
            steps.Clear();
            foreach (ObjectPoolManager pool in Object.FindObjectsByType<ObjectPoolManager>()) Object.DestroyImmediate(pool.gameObject);
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
        public IEnumerator MovingPlayer_StepsAtGaitRate_LouderWhenSprinting()
        {
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(0, steps.Count, "standing still makes no steps");

            Press(keyboard.wKey);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(LocomotionPace.CombatPace, locomotion.Pace);
            yield return Measure(2f, Stride("combatStride"));
            float walkRate = measuredRate, walkLoudness = measuredLoudness;

            Press(keyboard.leftShiftKey);
            yield return new WaitForSeconds(0.1f);
            Release(keyboard.leftShiftKey);
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(locomotion.IsSprinting, $"pace {locomotion.Pace}, {locomotion.PlanarSpeed:F1} m/s");
            yield return Measure(1.5f, Stride("sprintStride"));
            Release(keyboard.wKey);

            Assert.Greater(measuredRate, walkRate * 1.1f, "sprinting quickens the cadence");
            Assert.Greater(measuredLoudness, walkLoudness * 1.5f, "sprint steps are louder");
            Assert.AreEqual(1f, measuredLoudness, 0.05f, "a sprint is full loudness");
        }

        [UnityTest]
        public IEnumerator Landing_ThudScalesWithFallSpeed()
        {
            yield return new WaitForSeconds(0.3f);
            yield return Drop(0.5f);
            float low = measuredLoudness;
            yield return Drop(2.5f);
            float high = measuredLoudness;

            Assert.Greater(low, 0.3f, "a 0.5 m hop down is heard");
            Assert.Greater(high, low * 1.5f, "a 2.5 m drop thuds louder");
            Assert.AreEqual(1f, high, 0.01f);
        }

        [UnityTest]
        public IEnumerator RemoteSteps_ArriveAtSoundSpeed_OnlyWithinTheirRadius()
        {
            var stranger = new GameObject("Soldier");
            created.Add(stranger);
            AudioClip near = AudioClip.Create("Step Near", 4800, 1, 48000, false);
            AudioClip far = AudioClip.Create("Step Far", 4800, 1, 48000, false);
            created.Add(near);
            created.Add(far);
            Vector3 ear = player.GetComponentInChildren<AudioListener>().transform.position;

            Raise(stranger.transform, ear + Vector3.forward * 10f, near, 20f);
            Raise(stranger.transform, ear + Vector3.forward * 10f, far, 5f);
            yield return null;

            AudioEmitter heard = null;
            int beyond = 0;
            foreach (AudioEmitter emitter in Object.FindObjectsByType<AudioEmitter>())
            {
                if (!emitter.gameObject.activeInHierarchy) continue;
                if (emitter.Clip == near) heard = emitter;
                if (emitter.Clip == far) beyond++;
            }

            Assert.IsNotNull(heard, "a step 10 m away inside its 20 m radius is heard");
            Assert.AreEqual(10f / 343f, heard.Delay, 0.002f, "delayed by the sound's travel");
            Assert.AreEqual(0, beyond, "a step beyond its radius is not");
        }

        private static void Raise(Transform source, Vector3 position, AudioClip clip, float radius) =>
            EventBus<FootstepEvent>.Raise(new FootstepEvent { Source = source, Position = position, Clip = clip, Loudness = 0.5f, Radius = radius });

        /// <summary>Counts own steps over <paramref name="seconds"/> against the strikes the gait predicts (two per stride).</summary>
        private IEnumerator Measure(float seconds, float stride)
        {
            steps.Clear();
            float expected = 0f, elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
                expected += 2f * locomotion.PlanarSpeed * Time.deltaTime / stride;
            }

            Assert.Greater(expected, 5f);
            Assert.AreEqual(expected, steps.Count, 1.5f, "one step per half stride");
            float sum = 0f;
            foreach (FootstepEvent step in steps) sum += step.Loudness;
            measuredRate = steps.Count / elapsed;
            measuredLoudness = sum / steps.Count;
        }

        private IEnumerator Drop(float height)
        {
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position += Vector3.up * height;
            controller.enabled = true;
            Physics.SyncTransforms();
            steps.Clear();
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(1, steps.Count, $"one landing after a {height} m drop");
            measuredLoudness = steps[0].Loudness;
        }

        private float Stride(string field) =>
            (float)typeof(RealisticLocomotionController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(locomotion);

        private void OnStep(in FootstepEvent step)
        {
            if (step.Source == player.transform) steps.Add(step);
        }
    }
}
