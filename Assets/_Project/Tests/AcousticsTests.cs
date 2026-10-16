#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using FPS.Audio;
using FPS.Core;
using FPS.EditorTools;
using FPS.Locomotion;
using FPS.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace FPS.Tests
{
    /// <summary>
    /// M13 acoustics: physical gunshot synthesis (blast, sub-bass, action, spaces, overrides), supersonic cracks from the
    /// Mach cone with Doppler whips, four-space tails, last-rounds ping and first-person gear foley.
    /// </summary>
    public class AcousticsTests : InputTestFixture
    {
        private const float Rate = AudioSynth.SampleRate;

        private readonly List<Object> created = new List<Object>();
        private InputActionAsset actions;
        private InputReader reader;
        private Keyboard keyboard;
        private GameObject player;
        private Transform ear;

        public override void Setup()
        {
            base.Setup();
            TestInput.IgnoreFocus();
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            reader = TestInput.CreateReader(out actions);
            Block(new Vector3(0f, -0.5f, 0f), new Vector3(80f, 1f, 80f));
            player = new GameObject("Player");
            created.Add(player);
            ear = ViewmodelRig.Build(PlayerRig.Build(player, reader)).GetComponent<PlayerRig>().PlayerCamera.transform;
            Physics.SyncTransforms();
        }

        public override void TearDown()
        {
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

        // ---- Synthesis ----

        [Test]
        public void Reports_Are48kHz_TailsStereo()
        {
            Assert.AreEqual(48000, AudioSynth.SampleRate);
            var transient = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Art/Audio/Weapons/TestRifle_Transient_0.wav");
            var tail = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Art/Audio/Weapons/TestRifle_TailWarehouse_0.wav");
            Assert.IsNotNull(transient, "run 3. Create Test Weapons");
            Assert.IsNotNull(tail);
            Assert.AreEqual(48000, transient.frequency);
            Assert.AreEqual(1, transient.channels);
            Assert.AreEqual(2, tail.channels, "tails are stereo");

            var profile = AssetDatabase.LoadAssetAtPath<WeaponAudioProfileSO>("Assets/_Project/Data/Audio/TestRifle_Audio.asset");
            Assert.IsNotNull(profile.SubBass);
            Assert.IsNotNull(profile.WarehouseTail);
            Assert.IsNotNull(profile.LowAmmoPing);
            Assert.AreEqual(3, profile.LowAmmoRounds);
        }

        [Test]
        public void AirOverload_ClipsTheRarefactionHarderThanTheShock()
        {
            float[] blast = new AudioSynth(0.01f, 1).Blast(1f, 0f, 0.001f).Samples(0);
            Assert.AreEqual(1f, Max(blast), 1e-4f, "instant shock front");
            Assert.Greater(-Min(blast), 0.25f, "Friedlander wave with a deep negative phase");

            float[] sine = new AudioSynth(0.02f, 1).Tone(500f, 500f, 1f, 0f, 0f, 100f).Overdrive(3f, 2.2f).Samples(0);
            Assert.Greater(Max(sine), -Min(sine) * 1.5f, "symmetric pressure in, rarefaction clipped harder than compression");
            float[] quiet = new AudioSynth(0.02f, 1).Tone(500f, 500f, 1f, 0f, 0f, 100f).Mix(new AudioSynth(0.02f, 2).Blast(20f, 0.01f, 0.0005f), 1f)
                .Overdrive(3f, 2.2f).Samples(0);
            Assert.That(Peak(quiet, 0, 470), Is.InRange(0.9f, 1.01f), "next to a 20x louder shock, the quiet tone passes almost linearly");
        }

        [Test]
        public void Caliber_SetsTheBlast_SharpCarbine_DeepMagnum()
        {
            float[] carbine = GunshotAudioBuilder.Blast(GunshotAudioBuilder.Carbine, 1).Samples(0);
            float[] magnum = GunshotAudioBuilder.Blast(GunshotAudioBuilder.Marksman, 1).Samples(0);
            Assert.Greater(EnergyTime(magnum, 0.9f), EnergyTime(carbine, 0.9f) * 1.5f, ".300 WM roars longer");
            Assert.Less(CrossingRate(magnum), CrossingRate(carbine), "and lower");

            float[] sub = GunshotAudioBuilder.Sub(GunshotAudioBuilder.Carbine).Samples(0);
            Assert.Greater(sub[48], 0f, "phase-aligned: the thump starts with compression");
            Assert.Greater(Band(sub, 0, sub.Length, 60f), Band(sub, 0, sub.Length, 1000f) * 100f, "chest thump lives below 90 Hz");
        }

        [Test]
        public void ArAction_TwangsItsBufferSpring_PistolDoesNot()
        {
            float[] ar = GunshotAudioBuilder.Action(ActionSound.DirectImpingement, 1).Samples(0);
            float[] pistol = GunshotAudioBuilder.Action(ActionSound.PistolSlide, 1).Samples(0);
            int from = (int)(0.09f * Rate), to = (int)(0.19f * Rate);
            float arSpring = Band(ar, from, to, 1130f) + Band(ar, from, to, 1150f) + Band(ar, from, to, 1170f);
            float pistolSpring = Band(pistol, from, to, 1130f) + Band(pistol, from, to, 1150f) + Band(pistol, from, to, 1170f);
            Assert.Greater(arSpring, pistolSpring * 4f, "buffer spring ring after the carrier slams");
            Assert.Greater(Peak(ar, (int)(0.054f * Rate), (int)(0.06f * Rate)), Peak(ar, (int)(0.04f * Rate), (int)(0.05f * Rate)), "carrier slams home at ~55 ms");
        }

        [Test]
        public void Spaces_DecayAtTheirRates_InDecorrelatedStereo()
        {
            float[] nominal = { 0.4f, 1.4f, 2.2f, 3f };
            float previous = 0f;
            for (int i = 0; i < nominal.Length; i++)
            {
                AudioSynth ir = GunshotAudioBuilder.Impulse((AcousticSpace)i);
                Assert.AreEqual(2, ir.Channels, "stereo");
                float rt60 = DecayTime(ir.Samples(0), ir.Samples(1));
                Assert.That(rt60, Is.InRange(nominal[i] * 0.5f, nominal[i] * 1.6f), $"{(AcousticSpace)i} RT60");
                Assert.Greater(rt60, previous, "small room < warehouse < canyon < field");
                previous = rt60;
            }

            AudioSynth hall = GunshotAudioBuilder.Impulse(AcousticSpace.Warehouse);
            Assert.Less(Mathf.Abs(Correlation(hall.Samples(0), hall.Samples(1), (int)(0.1f * Rate), (int)(1f * Rate))), 0.5f, "wide, decorrelated reverb");

            float[] canyon = GunshotAudioBuilder.Impulse(AcousticSpace.UrbanCanyon).Samples(0);
            int echoes = 0;
            for (int n = 2; n < 8; n++)
            {
                int t = (int)((0.044f + 0.087f * n) * Rate);
                if (Peak(canyon, t - 100, t + 100) > 3f * Rms(canyon, t + 250, t + 1000)) echoes++;
            }

            Assert.GreaterOrEqual(echoes, 3, "flutter echo every 87 ms between the facades");
        }

        [Test]
        public void Overrides_MatchTheirLayer_AndWinOverSynthesis()
        {
            Assert.IsTrue(GunshotAudioBuilder.IsOverrideFor("Transient", "Transient"));
            Assert.IsTrue(GunshotAudioBuilder.IsOverrideFor("transient_take2", "Transient"));
            Assert.IsFalse(GunshotAudioBuilder.IsOverrideFor("TransientLong", "Transient"));
            Assert.IsFalse(GunshotAudioBuilder.IsOverrideFor("SubBass", "Transient"));

            const string owner = "OverrideProbe";
            string folder = $"{GunshotAudioBuilder.OverrideFolder}/{owner}";
            ProjectSetupWizard.EnsureFolder(folder);
            try
            {
                AudioClip recorded = new AudioSynth(0.05f, 3).Tone(440f, 440f, 1f, 0f, 0.001f, 0.02f).Save($"{folder}/Transient_take1.wav");
                AudioClip[] layer = GunshotAudioBuilder.Layer("Weapons", owner, "Transient", 3, i => throw new AssertionException("synthesized despite an override"));
                Assert.AreEqual(1, layer.Length);
                Assert.AreSame(recorded, layer[0]);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // ---- Supersonic cracks ----

        [Test]
        public void CrackSolution_FollowsTheMachCone()
        {
            var closest = new Vector3(0f, 2f, 0f);
            SupersonicCrackEmitter.Solution s = SupersonicCrackEmitter.Solve(Vector3.zero, closest, Vector3.right, 2.5f * 343f, 1f, 2.5f);
            float root = Mathf.Sqrt(2.5f * 2.5f - 1f);
            Assert.IsTrue(s.Supersonic);
            Assert.AreEqual(2.5f, s.Mach, 1e-3f);
            Assert.AreEqual(2f * root / (2.5f * 343f), s.ArrivalDelay, 1e-5f, "the cone sweeps over a few ms after the pass");
            Assert.AreEqual(-2f / root, s.EmissionPoint.x, 1e-3f, "heard from upstream: incoming fire seems to come from ahead");
            Assert.AreEqual(Mathf.Pow(2f, 0.25f), s.DurationRatio, 1e-3f, "N-wave lengthens as b^1/4");
            Assert.AreEqual(Mathf.Pow(2f, -0.75f), s.OverpressureRatio, 1e-3f, "and weakens as b^-3/4");
            Assert.AreEqual(1f / (1f + 2.5f * 0.70710678f), s.DopplerPitch, 1e-4f, "receding whip drops in pitch");

            SupersonicCrackEmitter.Solution faster = SupersonicCrackEmitter.Solve(Vector3.zero, closest, Vector3.right, 3f * 343f, 1f, 2.5f);
            Assert.Greater(faster.EmissionPoint.x, s.EmissionPoint.x, "a narrower cone comes from closer to the pass");
            Assert.Less(faster.DopplerPitch, s.DopplerPitch);
            Assert.IsFalse(SupersonicCrackEmitter.Solve(Vector3.zero, closest, Vector3.right, 300f, 1f, 2.5f).Supersonic, "subsonic: no crack");
        }

        [UnityTest]
        public IEnumerator NearMiss_CracksFromUpstreamThenWhips_BlastsStaySilent()
        {
            var emitter = player.GetComponent<SupersonicCrackEmitter>();
            AudioClip crack = Track(AudioClip.Create("Crack", 480, 1, 48000, false));
            AudioClip whip = Track(AudioClip.Create("Whip", 4800, 1, 48000, false));
            TestWeapons.SetField(emitter, "crackClips", new[] { crack });
            TestWeapons.SetField(emitter, "whipClips", new[] { whip });

            EventBus<SuppressionEvent>.Raise(new SuppressionEvent
            {
                Listener = ear, Intensity = 1f, Point = ear.position + Vector3.up * 1.5f, Direction = Vector3.right, Speed = 880f,
            });
            yield return null;

            AudioEmitter crackSound = Playing(crack), whipSound = Playing(whip);
            Assert.IsNotNull(crackSound);
            Assert.Less(crackSound.transform.position.x, ear.position.x - 0.3f, "the crack comes from upstream");
            Assert.AreEqual(emitter.Last.ArrivalDelay, crackSound.Delay, 1e-4f);
            Assert.IsNotNull(whipSound);
            Assert.Greater(whipSound.transform.position.x, ear.position.x, "the whip recedes downstream");
            Assert.Less(whipSound.GetComponent<AudioSource>().pitch, 0.6f, "Doppler-shifted down");

            int cracks = Count(crack);
            EventBus<SuppressionEvent>.Raise(new SuppressionEvent { Listener = ear, Intensity = 1f, Point = ear.position + Vector3.up, Direction = Vector3.up });
            yield return null;
            Assert.AreEqual(cracks, Count(crack), "a grenade blast is not a bullet");
        }

        // ---- Spaces, last rounds ----

        [Test]
        public void Rooms_SplitIntoSmallRoomsAndHalls_LastRoundsRamp()
        {
            Assert.AreEqual(0f, AcousticPropagationSystem.HallShare(2.8f, 3f, 5f, 12f));
            Assert.AreEqual(1f, AcousticPropagationSystem.HallShare(9f, 20f, 5f, 12f));
            Assert.That(AcousticPropagationSystem.HallShare(4f, 6f, 5f, 12f), Is.InRange(0.1f, 0.4f));

            Assert.AreEqual(0f, AcousticPropagationSystem.LowAmmoShift(5, 3));
            Assert.AreEqual(0f, AcousticPropagationSystem.LowAmmoShift(3, 3));
            Assert.AreEqual(1f / 3f, AcousticPropagationSystem.LowAmmoShift(2, 3), 1e-5f);
            Assert.AreEqual(1f, AcousticPropagationSystem.LowAmmoShift(0, 3));
            Assert.AreEqual(0f, AcousticPropagationSystem.LowAmmoShift(-1, 3), "no magazine, no ping");
        }

        [UnityTest]
        public IEnumerator OwnShot_InAWarehouse_RingsTheHall_AndPingsOnTheLastRound()
        {
            Block(new Vector3(0f, 10.5f, 0f), new Vector3(42f, 1f, 42f)); // roof 10 m up
            Block(new Vector3(20.5f, 5f, 0f), new Vector3(1f, 11f, 42f));
            Block(new Vector3(-20.5f, 5f, 0f), new Vector3(1f, 11f, 42f));
            Block(new Vector3(0f, 5f, 20.5f), new Vector3(42f, 11f, 1f));
            Block(new Vector3(0f, 5f, -20.5f), new Vector3(42f, 11f, 1f));
            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.9f);

            var acoustics = player.GetComponent<AcousticPropagationSystem>();
            Assert.Greater(acoustics.EnvironmentWeights.x, 0.9f, "indoors");
            Assert.Greater(acoustics.WarehouseShare, 0.9f, "a hall, not a small room");

            WeaponAudioProfileSO profile = Profile(out AudioClip room, out AudioClip hall, out AudioClip ping);
            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = ear.position, Shooter = player.transform, Profile = profile, RoundsLeft = 0 });
            AudioSource hallTail = Source(hall), pingSource = Source(ping);
            Assert.IsNotNull(hallTail, "warehouse tail");
            Assert.Greater(hallTail.volume, 0.5f);
            Assert.IsNull(Source(room), "no small-room slapback in a hall");
            Assert.IsNotNull(pingSource, "last round pings");
            Assert.Greater(pingSource.pitch, 1.1f, "the follower is up: highest ping");

            pingSource.clip = null;
            EventBus<GunshotEvent>.Raise(new GunshotEvent { Position = ear.position, Shooter = player.transform, Profile = profile, RoundsLeft = 12 });
            Assert.IsNull(Source(ping), "plenty left: no ping");
        }

        // ---- Gear ----

        [UnityTest]
        public IEnumerator Gear_RustlesWithMotion_AndClinksOnALanding()
        {
            Assert.AreEqual(0.5f, GearFoley.RustleTarget(0f, 0f, 5f, 8f, 220f, 5f), 1e-5f, "steady walking: half");
            Assert.AreEqual(1f, GearFoley.RustleTarget(9f, 0f, 0f, 8f, 220f, 5f), "hard acceleration: full");

            var foley = player.GetComponent<GearFoley>();
            TestWeapons.SetField(foley, "swivelClinks", new[] { Track(AudioClip.Create("Clink", 480, 1, 48000, false)) });
            yield return new WaitForSeconds(0.3f);
            Assert.Less(foley.Intensity, 0.1f, "standing still is quiet");

            Press(keyboard.wKey);
            yield return new WaitForSeconds(0.4f);
            Assert.Greater(foley.Intensity, 0.2f, "moving off rustles the carrier");
            Release(keyboard.wKey);
            yield return new WaitForSeconds(2f);
            Assert.Less(foley.Intensity, 0.1f, "and settles when still");

            int clinks = foley.Clinks;
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position += Vector3.up * 2.5f;
            controller.enabled = true;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(1.2f);
            Assert.Greater(foley.Clinks, clinks, "a hard landing rattles the sling swivels");
        }

        // ---- Helpers ----

        private T Track<T>(T asset) where T : Object
        {
            created.Add(asset);
            return asset;
        }

        private void Block(Vector3 position, Vector3 size)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.layer = GameLayers.WorldGeometry;
            block.transform.position = position;
            block.transform.localScale = size;
            created.Add(block);
        }

        private WeaponAudioProfileSO Profile(out AudioClip room, out AudioClip hall, out AudioClip ping)
        {
            var profile = Track(ScriptableObject.CreateInstance<WeaponAudioProfileSO>());
            room = Track(AudioClip.Create("Room", 480, 2, 48000, false));
            hall = Track(AudioClip.Create("Hall", 480, 2, 48000, false));
            ping = Track(AudioClip.Create("Ping", 480, 1, 48000, false));
            TestWeapons.SetField(profile, "pressureTransients", new[] { Track(AudioClip.Create("Blast", 480, 1, 48000, false)) });
            TestWeapons.SetField(profile, "indoorTail", room);
            TestWeapons.SetField(profile, "warehouseTail", hall);
            TestWeapons.SetField(profile, "lowAmmoPing", ping);
            return profile;
        }

        private static AudioSource Source(AudioClip clip)
        {
            foreach (AudioSource source in Object.FindObjectsByType<AudioSource>())
            {
                if (source.clip == clip) return source;
            }

            return null;
        }

        private static AudioEmitter Playing(AudioClip clip)
        {
            foreach (AudioEmitter emitter in Object.FindObjectsByType<AudioEmitter>())
            {
                if (emitter.gameObject.activeInHierarchy && emitter.Clip == clip) return emitter;
            }

            return null;
        }

        private static int Count(AudioClip clip)
        {
            int count = 0;
            foreach (AudioEmitter emitter in Object.FindObjectsByType<AudioEmitter>()) count += emitter.gameObject.activeInHierarchy && emitter.Clip == clip ? 1 : 0;
            return count;
        }

        private static float Max(float[] x)
        {
            float m = float.MinValue;
            foreach (float v in x) m = Mathf.Max(m, v);
            return m;
        }

        private static float Min(float[] x)
        {
            float m = float.MaxValue;
            foreach (float v in x) m = Mathf.Min(m, v);
            return m;
        }

        private static float Peak(float[] x, int from, int to)
        {
            float m = 0f;
            for (int i = Mathf.Max(0, from); i < Mathf.Min(x.Length, to); i++) m = Mathf.Max(m, Mathf.Abs(x[i]));
            return m;
        }

        private static float Rms(float[] x, int from, int to)
        {
            double sum = 0.0;
            int n = 0;
            for (int i = Mathf.Max(0, from); i < Mathf.Min(x.Length, to); i++, n++) sum += x[i] * x[i];
            return n > 0 ? (float)System.Math.Sqrt(sum / n) : 0f;
        }

        /// <summary>Time by which <paramref name="fraction"/> of the energy has arrived (s).</summary>
        private static float EnergyTime(float[] x, float fraction)
        {
            double total = 0.0;
            foreach (float v in x) total += v * v;
            double sum = 0.0;
            for (int i = 0; i < x.Length; i++)
            {
                sum += x[i] * x[i];
                if (sum >= total * fraction) return i / Rate;
            }

            return x.Length / Rate;
        }

        /// <summary>Zero crossings per second over the part holding 90% of the energy: a brightness measure.</summary>
        private static float CrossingRate(float[] x)
        {
            int end = Mathf.Max(2, (int)(EnergyTime(x, 0.9f) * Rate));
            int crossings = 0;
            for (int i = 1; i < end; i++) crossings += (x[i - 1] < 0f) != (x[i] < 0f) ? 1 : 0;
            return crossings / (end / Rate);
        }

        /// <summary>Goertzel power at <paramref name="frequency"/> over [from, to).</summary>
        private static float Band(float[] x, int from, int to, float frequency)
        {
            float coefficient = 2f * Mathf.Cos(2f * Mathf.PI * frequency / Rate);
            float s1 = 0f, s2 = 0f;
            for (int i = from; i < Mathf.Min(to, x.Length); i++)
            {
                float s = x[i] + coefficient * s1 - s2;
                s2 = s1;
                s1 = s;
            }

            return s1 * s1 + s2 * s2 - coefficient * s1 * s2;
        }

        /// <summary>RT60 from the Schroeder decay curve of both channels: 3 x the time from -5 to -25 dB.</summary>
        private static float DecayTime(float[] left, float[] right)
        {
            var remaining = new double[left.Length];
            double sum = 0.0;
            for (int i = left.Length - 1; i >= 0; i--)
            {
                sum += left[i] * left[i] + right[i] * right[i];
                remaining[i] = sum;
            }

            float t5 = -1f, t25 = -1f;
            for (int i = 0; i < remaining.Length; i++)
            {
                double db = 10.0 * System.Math.Log10(remaining[i] / remaining[0] + 1e-30);
                if (t5 < 0f && db <= -5.0) t5 = i / Rate;
                if (db <= -25.0)
                {
                    t25 = i / Rate;
                    break;
                }
            }

            return t25 > 0f && t5 >= 0f ? 3f * (t25 - t5) : float.PositiveInfinity;
        }

        private static float Correlation(float[] a, float[] b, int from, int to)
        {
            double ab = 0.0, aa = 0.0, bb = 0.0;
            for (int i = from; i < Mathf.Min(to, a.Length); i++)
            {
                ab += a[i] * b[i];
                aa += a[i] * a[i];
                bb += b[i] * b[i];
            }

            return (float)(ab / System.Math.Sqrt(aa * bb + 1e-30));
        }
    }
}
#endif
