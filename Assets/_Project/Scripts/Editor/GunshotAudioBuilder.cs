using System;
using System.Collections.Generic;
using System.IO;
using FPS.Audio;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace FPS.EditorTools
{
    /// <summary>How a weapon's action cycles, for its mechanical layer.</summary>
    internal enum ActionSound
    {
        /// <summary>AR-15: carrier slams back into the buffer tube; the buffer spring twangs.</summary>
        DirectImpingement,
        /// <summary>Short-stroke piston (SCAR): op-rod clunk, heavier carrier, softer recoil spring.</summary>
        Piston,
        /// <summary>Locked-breech pistol: fast slide clack, striker reset.</summary>
        PistolSlide,
        /// <summary>Manual bolt: only the firing pin falls.</summary>
        BoltAction,
    }

    /// <summary>The four acoustic spaces a report is heard in.</summary>
    internal enum AcousticSpace
    {
        SmallRoom,
        Warehouse,
        UrbanCanyon,
        OpenField,
    }

    /// <summary>The magazine's last-rounds ping.</summary>
    internal enum MagazineSound
    {
        Rifle,
        Pistol,
        Marksman,
    }

    /// <summary>Acoustic parameters of one weapon's report (SI units).</summary>
    internal readonly struct GunshotSpec
    {
        public readonly string Weapon;
        /// <summary>Positive phase of the muzzle blast shock at the shooter's ear (s): short is a sharp crack.</summary>
        public readonly float BlastDuration;
        /// <summary>Decay of the turbulent propellant gas jet (s): long is a deep roar.</summary>
        public readonly float JetDecay;
        /// <summary>Upper edge of the blast band (Hz).</summary>
        public readonly float Brightness;
        public readonly float SubFrom, SubTo, SubDecay;
        /// <summary>Relative report energy (5.56 carbine = 1).</summary>
        public readonly float Power;
        public readonly float BodyPitch;
        public readonly ActionSound Action;
        public readonly MagazineSound Magazine;

        public GunshotSpec(string weapon, float blastDuration, float jetDecay, float brightness, float subFrom, float subTo, float subDecay,
            float power, float bodyPitch, ActionSound action, MagazineSound magazine)
        {
            Weapon = weapon;
            BlastDuration = blastDuration;
            JetDecay = jetDecay;
            Brightness = brightness;
            SubFrom = subFrom;
            SubTo = subTo;
            SubDecay = subDecay;
            Power = power;
            BodyPitch = bodyPitch;
            Action = action;
            Magazine = magazine;
        }
    }

    /// <summary>
    /// Physically modelled weapon and explosive audio. Each report is layered: a sub-bass chest thump (phase-aligned
    /// 90 → 30 Hz sweep), the muzzle blast (Friedlander shock with asymmetric air-overload clipping plus the turbulent gas
    /// jet, 200 Hz – 8 kHz, its duration set by the cartridge), the mechanical action (extractor snap, carrier and buffer
    /// impacts, the AR buffer spring's twang, carrier slam, trigger reset), body resonance, a distant report, the
    /// magazine's last-rounds ping, and four stereo tails made by convolving the dry blast with synthesized impulse
    /// responses of a small room, a warehouse, an urban canyon and open ground. Any layer can be replaced by recorded
    /// audio: WAVs in <c>Art/Audio/Overrides/&lt;Owner&gt;/</c> named <c>&lt;Layer&gt;.wav</c> or <c>&lt;Layer&gt;_*.wav</c> win
    /// over the synthesis.
    /// </summary>
    internal static class GunshotAudioBuilder
    {
        internal const string AudioFolder = "Assets/_Project/Art/Audio";
        internal const string OverrideFolder = AudioFolder + "/Overrides";
        private const string DataFolder = "Assets/_Project/Data/Audio";
        private const float DryLength = 0.06f; // the part of a report that excites a space

        internal static readonly GunshotSpec Carbine = new GunshotSpec(TestWeaponBuilder.CarbineName,
            0.0006f, 0.025f, 8000f, 95f, 40f, 0.07f, 1f, 1f, ActionSound.DirectImpingement, MagazineSound.Rifle);
        internal static readonly GunshotSpec Marksman = new GunshotSpec(TestWeaponBuilder.MarksmanName,
            0.0014f, 0.07f, 5000f, 80f, 30f, 0.14f, 1.45f, 0.8f, ActionSound.BoltAction, MagazineSound.Marksman);
        internal static readonly GunshotSpec BattleRifle = new GunshotSpec(TestWeaponBuilder.BattleRifleName,
            0.0009f, 0.04f, 6500f, 85f, 35f, 0.1f, 1.2f, 0.88f, ActionSound.Piston, MagazineSound.Rifle);
        internal static readonly GunshotSpec Sidearm = new GunshotSpec(TestWeaponBuilder.SidearmName,
            0.00035f, 0.012f, 8000f, 110f, 55f, 0.045f, 0.6f, 1.35f, ActionSound.PistolSlide, MagazineSound.Pistol);

        private static readonly Dictionary<AcousticSpace, AudioSynth> Impulses = new Dictionary<AcousticSpace, AudioSynth>();

        // ---- Profiles ----

        /// <summary>Synthesizes (or picks up overrides for) every layer of <paramref name="spec"/> and writes its profile.</summary>
        public static WeaponAudioProfileSO Weapon(GunshotSpec spec)
        {
            string owner = spec.Weapon;
            int seed = StableSeed(owner);
            AudioClip[] blasts = Layer("Weapons", owner, "Transient", 3, i => Blast(spec, seed + i));
            AudioClip[] subs = Layer("Weapons", owner, "SubBass", 1, i => Sub(spec));
            AudioClip[] actions = Layer("Weapons", owner, "Mechanism", 2, i => Action(spec.Action, seed + 10 + i));
            AudioClip[] bodies = Layer("Weapons", owner, "Body", 1, i => Body(spec, seed + 20));
            AudioClip[] distant = Layer("Weapons", owner, "Distant", 1, i => Distant(spec, seed + 30));
            AudioClip[] tails = Tails("Weapons", owner, () => Dry(Blast(spec, seed), Sub(spec)), seed + 40);
            AudioClip[] ping = Layer("Weapons", owner, "LowAmmoPing", 1, i => Ping(spec.Magazine, seed + 50));

            return Profile(owner, blasts, subs, actions, bodies, distant, tails, ping[0], 120f * spec.Power, 1500f * spec.Power, Mathf.Min(1f, 0.8f * spec.Power));
        }

        /// <summary>
        /// The weapon behind a suppressor: no shock front, a short dull pop and a small thump, its own (quieter, duller)
        /// tails, the open weapon's action and body clearly audible; heard far less far.
        /// </summary>
        public static WeaponAudioProfileSO Suppressed(GunshotSpec spec)
        {
            string owner = spec.Weapon + "_Suppressed";
            int seed = StableSeed(owner), openSeed = StableSeed(spec.Weapon);
            AudioClip[] actions = Layer("Weapons", spec.Weapon, "Mechanism", 2, i => Action(spec.Action, openSeed + 10 + i));
            AudioClip[] bodies = Layer("Weapons", spec.Weapon, "Body", 1, i => Body(spec, openSeed + 20));
            AudioClip[] ping = Layer("Weapons", spec.Weapon, "LowAmmoPing", 1, i => Ping(spec.Magazine, openSeed + 50));
            AudioClip[] pops = Layer("Weapons", owner, "Transient", 3, i => SuppressedPop(spec, seed + i));
            AudioClip[] subs = Layer("Weapons", owner, "SubBass", 1, i => new AudioSynth(0.25f, seed + 5).SubThump(80f, 45f, 1f, 0f, 0.04f).LowPass(200f));
            AudioClip[] distant = Layer("Weapons", owner, "Distant", 1, i => new AudioSynth(0.6f, seed + 30)
                .Noise(0.6f, 0f, 0.008f, 0.12f).LowPass(500f).LowPass(500f));
            AudioClip[] tails = Tails("Weapons", owner, () => Dry(SuppressedPop(spec, seed), null), seed + 40);

            return Profile(owner, pops, subs, actions, bodies, distant, tails, ping[0], 40f, 300f, 0.35f);
        }

        /// <summary>
        /// A grenade or charge: <paramref name="power"/> scales the blast, <paramref name="crack"/> its sharp snap (a stun
        /// grenade is mostly crack, a frag mostly thump). Blast, thump, debris, distant boom and four tails.
        /// </summary>
        public static WeaponAudioProfileSO Explosion(string owner, float power, float crack)
        {
            int seed = StableSeed(owner);
            Func<int, AudioSynth> blast = s => new AudioSynth(1.2f * power, s)
                .Blast(1f, 0f, 0.0025f * power, 0.5f).NWave(crack * 0.6f, 0f, 0.0008f)
                .Noise(0.8f, 0f, 0.001f, 0.12f * power).Overdrive(3f, 2.4f).LowPass(6000f)
                .Noise(0.35f, 0.02f, 0.02f, 0.5f * power).LowPass(2500f);
            Func<AudioSynth> thump = () => new AudioSynth(2.5f * power, seed + 5).SubThump(60f, 25f, 1f, 0f, 0.35f * power).LowPass(160f);

            AudioClip[] blasts = Layer("Explosives", owner, "Blast", 2, i => blast(seed + i));
            AudioClip[] subs = Layer("Explosives", owner, "SubBass", 1, i => thump());
            AudioClip[] debris = Layer("Explosives", owner, "Debris", 1, i => new AudioSynth(1f, seed + 10)
                .Noise(0.25f, 0.15f, 0.05f, 0.4f).HighPass(1500f).Partials(new[] { 2400f, 4100f }, 0.15f, 0.3f, 0.05f));
            AudioClip[] distant = Layer("Explosives", owner, "Distant", 1, i => new AudioSynth(2.5f * power, seed + 30)
                .Blast(1f, 0f, 0.02f * power, 0.5f).Noise(1f, 0f, 0.03f, 0.7f * power).LowPass(300f).LowPass(300f)
                .SubThump(45f, 28f, 0.8f, 0f, 0.6f * power).Rumble(0.3f, 0.2f, 2.4f * power, 160f, 1.5f));
            AudioClip[] tails = Tails("Explosives", owner, () => Dry(blast(seed), thump()), seed + 40, 0.12f);

            return Profile(owner, blasts, subs, debris, new AudioClip[0], distant, tails, null, 150f, 2500f, 1f);
        }

        // ---- Bullets and gear ----

        /// <summary>Supersonic cracks for a 1 m miss at Mach 2.5: N-wave, ground reflection, a short bright ring.</summary>
        public static AudioClip[] Cracks() => Layer("Foley", "Bullet", "SupersonicCrack", 3, i => new AudioSynth(0.08f, 20 + i)
            .NWave(1f, 0f, 0.0004f).Echo(0.0028f, 0.45f).Noise(0.2f, 0.0004f, 0.0002f, 0.004f).HighPass(1500f));

        /// <summary>The round's flight noise (whip/zip), played Doppler-shifted as it recedes.</summary>
        public static AudioClip[] Whips() => Layer("Foley", "Bullet", "Whip", 2, i => new AudioSynth(0.3f, 30 + i)
            .Noise(0.6f, 0f, 0.006f, 0.05f).BandPass(1500f, 7000f).Tone(2600f, 900f, 0.35f, 0f, 0.003f, 0.06f, 0.12f));

        /// <summary>Plate carrier nylon friction: band-limited hiss with crackling fibre contacts, looped seamlessly.</summary>
        public static AudioClip GearRustle() => Layer("Foley", "Gear", "Rustle", 1, i =>
        {
            AudioSynth rustle = new AudioSynth(2.4f, 61).Noise(0.3f, 0f, 0.0001f, 1000f).BandPass(1400f, 6000f);
            var random = new Random(62);
            for (int n = 0; n < 90; n++)
                rustle.Click((float)(0.04 + random.NextDouble() * 0.14), (float)(random.NextDouble() * 2.35), (float)(2500 + random.NextDouble() * 5000));
            return rustle.Loopable(0.4f);
        })[0];

        /// <summary>Sling swivel and buckle clinks: small steel loops knocking, with a short rattle after.</summary>
        public static AudioClip[] SlingClinks() => Layer("Foley", "Gear", "SlingClink", 3, i =>
        {
            float pitch = 0.9f + 0.1f * i;
            return new AudioSynth(0.16f, 70 + i).Click(0.5f, 0f, 5200f * pitch).Partials(new[] { 3300f, 5900f, 8200f }, 0.8f, 0.001f, 0.03f, pitch)
                .Partials(new[] { 3500f, 6100f }, 0.35f, 0.05f, 0.02f, pitch);
        });

        // ---- Layers ----

        /// <summary>
        /// Muzzle blast: Friedlander shock (positive phase <see cref="GunshotSpec.BlastDuration"/>, deep negative phase),
        /// the turbulent gas jet and afterburning muzzle gases, overdriven asymmetrically and band-limited to 200 Hz –
        /// <see cref="GunshotSpec.Brightness"/>.
        /// </summary>
        internal static AudioSynth Blast(GunshotSpec spec, int seed) => new AudioSynth(0.06f + spec.JetDecay * 6f, seed)
            .Blast(1f, 0f, spec.BlastDuration)
            .Noise(0.55f, 0f, 0.0002f, spec.JetDecay)
            .Noise(0.25f, 0.0005f, 0.003f, spec.JetDecay * 2.5f)
            .Overdrive(3f, 2.2f)
            .BandPass(200f, spec.Brightness);

        internal static AudioSynth Sub(GunshotSpec spec) => new AudioSynth(spec.SubDecay * 6f, StableSeed(spec.Weapon) + 5)
            .SubThump(spec.SubFrom, spec.SubTo, 1f, 0f, spec.SubDecay).LowPass(200f);

        /// <summary>The action's sequence of contacts, timed for its cyclic rate.</summary>
        internal static AudioSynth Action(ActionSound action, int seed)
        {
            switch (action)
            {
                case ActionSound.DirectImpingement: // ~800 rpm: 75 ms cycle
                    return new AudioSynth(0.35f, seed)
                        .Click(0.4f, 0f, 3800f) // hammer and firing pin
                        .Click(0.7f, 0.004f, 6200f) // bolt unlocks, extractor claw snaps the case out
                        .Noise(0.12f, 0.006f, 0.002f, 0.012f) // carrier sliding back
                        .Tone(240f, 180f, 0.6f, 0.018f, 0.001f, 0.02f) // buffer bottoms in the tube
                        .Partials(new[] { 900f, 1700f, 2600f }, 0.5f, 0.018f, 0.02f)
                        .Spring(1150f, 0.45f, 0.012f, 0.22f) // the buffer spring's twang
                        .Partials(new[] { 2300f, 4100f, 6200f, 8300f }, 0.9f, 0.055f, 0.03f) // carrier slams into battery
                        .Noise(0.5f, 0.055f, 0.0003f, 0.006f)
                        .Click(0.35f, 0.062f, 5200f) // cam pin rotates the bolt home
                        .Click(0.3f, 0.085f, 7000f) // trigger reset
                        .HighPass(100f);
                case ActionSound.Piston: // ~600 rpm: 100 ms cycle
                    return new AudioSynth(0.3f, seed)
                        .Click(0.4f, 0f, 3400f)
                        .Tone(170f, 120f, 0.7f, 0.003f, 0.001f, 0.03f) // op-rod and piston clunk
                        .Noise(0.25f, 0.003f, 0.001f, 0.01f)
                        .Click(0.6f, 0.006f, 5600f) // extractor
                        .Partials(new[] { 700f, 1400f, 2300f }, 0.6f, 0.03f, 0.03f) // carrier hits the rear
                        .Spring(640f, 0.25f, 0.028f, 0.12f, 5f, 0.008f) // recoil spring under the rail
                        .Partials(new[] { 1600f, 2900f, 4400f, 6600f }, 0.9f, 0.075f, 0.035f) // heavy carrier slams home
                        .Noise(0.5f, 0.075f, 0.0003f, 0.008f)
                        .Click(0.3f, 0.11f, 6500f) // trigger reset
                        .HighPass(90f);
                case ActionSound.PistolSlide:
                    return new AudioSynth(0.2f, seed)
                        .Click(0.4f, 0f, 4200f) // striker
                        .Partials(new[] { 2700f, 4600f, 6900f }, 0.7f, 0.003f, 0.015f) // slide unlocks and runs back
                        .Noise(0.3f, 0.003f, 0.0005f, 0.006f)
                        .Click(0.5f, 0.004f, 7200f) // extractor
                        .Partials(new[] { 1900f, 3500f }, 0.5f, 0.018f, 0.012f) // slide stops on the frame
                        .Partials(new[] { 3100f, 5300f, 7600f }, 0.9f, 0.034f, 0.02f) // slide slams into battery
                        .Noise(0.4f, 0.034f, 0.0003f, 0.005f)
                        .Click(0.35f, 0.05f, 7500f) // trigger reset
                        .HighPass(150f);
                default:
                    return new AudioSynth(0.1f, seed).Click(0.6f, 0f, 3600f).Partials(new[] { 3000f, 5200f }, 0.4f, 0f, 0.008f).HighPass(150f);
            }
        }

        /// <summary>Receiver and handguard ring.</summary>
        internal static AudioSynth Body(GunshotSpec spec, int seed) => new AudioSynth(0.4f * spec.Power, seed)
            .Tone(190f * spec.BodyPitch, 190f * spec.BodyPitch, 0.6f, 0f, 0.002f, 0.12f * spec.Power)
            .Tone(410f * spec.BodyPitch, 400f * spec.BodyPitch, 0.35f, 0f, 0.002f, 0.08f)
            .Tone(620f * spec.BodyPitch, 610f * spec.BodyPitch, 0.2f, 0f, 0.002f, 0.05f)
            .Noise(0.1f, 0f, 0.002f, 0.05f).LowPass(3000f);

        /// <summary>Far away: the shock has spread into a long low pop, with thunder rolling after it.</summary>
        internal static AudioSynth Distant(GunshotSpec spec, int seed) => new AudioSynth(1.8f * spec.Power, seed)
            .Blast(1f, 0f, spec.BlastDuration * 12f).Noise(0.5f, 0f, 0.01f, 0.2f * spec.Power).LowPass(900f).LowPass(900f)
            .SubThump(70f, 35f, 0.6f, 0f, 0.2f).Rumble(0.25f, 0.1f, 1.6f * spec.Power, 250f, 2.5f);

        /// <summary>The magazine spring and follower ringing on the last rounds.</summary>
        internal static AudioSynth Ping(MagazineSound magazine, int seed)
        {
            var ping = new AudioSynth(0.18f, seed);
            switch (magazine)
            {
                case MagazineSound.Pistol: return ping.Partials(new[] { 4100f, 6350f, 9100f }, 0.8f, 0f, 0.035f);
                case MagazineSound.Marksman: return ping.Partials(new[] { 2700f, 4300f, 6500f }, 0.8f, 0f, 0.06f);
                default: return ping.Partials(new[] { 3150f, 4870f, 7330f }, 0.8f, 0f, 0.05f).Spring(2250f, 0.12f, 0.001f, 0.04f, 11f, 0.004f);
            }
        }

        private static AudioSynth SuppressedPop(GunshotSpec spec, int seed) => new AudioSynth(0.25f, seed)
            .Blast(0.5f, 0f, spec.BlastDuration * 3f, 1.5f).Noise(0.6f, 0f, 0.001f, 0.025f).LowPass(1800f)
            .Tone(120f, 70f, 0.7f, 0f, 0.002f, 0.05f, 0.03f).Noise(0.12f, 0.002f, 0.004f, 0.04f).HighPass(60f);

        // ---- Spaces ----

        /// <summary>
        /// The stereo impulse response of a space: small room (slapback off near walls, 0.4 s), warehouse (late booming
        /// reflections, ringing metal, 1.4 s), urban canyon (flutter echo between facades 15 m apart, 2.2 s) or open
        /// field (sparse distant reflections and low rolling thunder, 3 s). No direct sound: tails carry only the room.
        /// </summary>
        internal static AudioSynth Impulse(AcousticSpace space)
        {
            if (Impulses.TryGetValue(space, out AudioSynth cached)) return cached;

            AudioSynth ir;
            switch (space)
            {
                case AcousticSpace.SmallRoom:
                    ir = new AudioSynth(0.45f, 701, 2)
                        .Reflection(0.0042f, 0.75f, -0.6f).Reflection(0.0061f, 0.7f, 0.7f).Reflection(0.0093f, 0.6f, -0.2f)
                        .Reflection(0.0118f, 0.85f, 0.4f).Reflection(0.0155f, 0.5f, -0.8f).Reflection(0.0189f, 0.55f, 0.9f)
                        .Flutter(0.012f, 0.0117f, 14, 0.45f, 0.72f, 0.6f) // walls 2 m apart
                        .Diffuse(0.35f, 0.008f, 0.4f, 7000f, 2500f);
                    break;
                case AcousticSpace.Warehouse:
                    ir = new AudioSynth(1.5f, 702, 2)
                        .Reflection(0.024f, 0.6f, -0.5f).Reflection(0.031f, 0.55f, 0.6f).Reflection(0.047f, 0.5f, -0.1f)
                        .Reflection(0.058f, 0.45f, 0.8f).Reflection(0.072f, 0.4f, -0.9f)
                        .Flutter(0.06f, 0.058f, 16, 0.3f, 0.75f, 0.5f) // floor to roof, 10 m: -43 dB/s
                        .Diffuse(0.4f, 0.02f, 1.4f, 5000f, 1100f, 0.04f)
                        .Partials(new[] { 173f, 412f, 761f, 1190f }, 0.05f, 0.03f, 0.2f); // corrugated cladding ringing (tau 0.2 s: RT60 1.4 s)
                    break;
                case AcousticSpace.UrbanCanyon:
                    ir = new AudioSynth(2.4f, 703, 2)
                        .Reflection(0.044f, 0.55f, -0.7f).Reflection(0.051f, 0.5f, 0.7f)
                        .Flutter(0.044f, 0.087f, 24, 0.5f, 0.76f, 0.75f) // facades 15 m apart: 87 ms round trips, -27 dB/s
                        .Diffuse(0.08f, 0.05f, 2.2f, 3500f, 700f, 0.15f) // open above: mostly discrete echoes
                        .LowPass(6000f);
                    break;
                default:
                    ir = new AudioSynth(3.1f, 704, 2)
                        .Reflection(0.32f, 0.25f, -0.6f).Reflection(0.58f, 0.2f, 0.5f).Reflection(0.91f, 0.16f, -0.2f)
                        .Reflection(1.37f, 0.12f, 0.8f) // tree lines and hills
                        .Rumble(0.5f, 0.05f, 3f, 260f, 1.7f)
                        .LowPass(1800f);
                    break;
            }

            Impulses[space] = ir;
            return ir;
        }

        /// <summary>Tail clips in <see cref="AcousticSpace"/> order: the dry report convolved with each space.</summary>
        private static AudioClip[] Tails(string folder, string owner, Func<float[]> dry, int seed, float dryLength = DryLength)
        {
            float[] excitation = null;
            var tails = new AudioClip[4];
            for (int i = 0; i < tails.Length; i++)
            {
                var space = (AcousticSpace)i;
                tails[i] = Layer(folder, owner, "Tail" + space, 1, v =>
                {
                    if (excitation == null) excitation = Trim(dry(), dryLength);
                    AudioSynth wet = Impulse(space).Convolve(excitation, seed + i);
                    return space == AcousticSpace.OpenField ? wet.LowPass(900f) : wet; // only the lows roll across open ground
                })[0];
            }

            return tails;
        }

        private static float[] Dry(AudioSynth blast, AudioSynth sub)
        {
            var dry = new AudioSynth(0.5f, 1).Mix(blast, 1f);
            if (sub != null) dry.Mix(sub, 0.6f);
            return dry.Samples(0);
        }

        private static float[] Trim(float[] samples, float seconds)
        {
            var trimmed = new float[Mathf.Min(samples.Length, Mathf.CeilToInt(seconds * AudioSynth.SampleRate))];
            Array.Copy(samples, trimmed, trimmed.Length);
            return trimmed;
        }

        private static WeaponAudioProfileSO Profile(string owner, AudioClip[] blasts, AudioClip[] subs, AudioClip[] actions, AudioClip[] bodies,
            AudioClip[] distant, AudioClip[] tails, AudioClip ping, float distantThreshold, float maxAudibleDistance, float loudness)
        {
            ProjectSetupWizard.EnsureFolder(DataFolder);
            WeaponAudioProfileSO profile = TestWeaponBuilder.LoadOrCreate<WeaponAudioProfileSO>($"{DataFolder}/{owner}_Audio.asset");
            TestWeaponBuilder.Configure(profile, so =>
            {
                EffectsContentBuilder.SetClips(TestWeaponBuilder.Property(so, "pressureTransients"), blasts);
                EffectsContentBuilder.SetClips(TestWeaponBuilder.Property(so, "subBassThumps"), subs);
                EffectsContentBuilder.SetClips(TestWeaponBuilder.Property(so, "mechanicalCycles"), actions);
                EffectsContentBuilder.SetClips(TestWeaponBuilder.Property(so, "bodyResonances"), bodies);
                EffectsContentBuilder.SetClips(TestWeaponBuilder.Property(so, "distantReports"), distant);
                TestWeaponBuilder.Set(so, "indoorTail", tails[(int)AcousticSpace.SmallRoom]);
                TestWeaponBuilder.Set(so, "warehouseTail", tails[(int)AcousticSpace.Warehouse]);
                TestWeaponBuilder.Set(so, "urbanTail", tails[(int)AcousticSpace.UrbanCanyon]);
                TestWeaponBuilder.Set(so, "openTail", tails[(int)AcousticSpace.OpenField]);
                TestWeaponBuilder.Set(so, "lowAmmoPing", ping);
                TestWeaponBuilder.Set(so, "distantThreshold", distantThreshold);
                TestWeaponBuilder.Set(so, "maxAudibleDistance", maxAudibleDistance);
                TestWeaponBuilder.Set(so, "loudness", loudness);
            });
            return profile;
        }

        // ---- Clips and overrides ----

        /// <summary>
        /// The clips of one layer: recorded overrides from <c>Overrides/&lt;owner&gt;/</c> when present, otherwise
        /// <paramref name="variants"/> synthesized clips at <c>&lt;folder&gt;/&lt;owner&gt;_&lt;layer&gt;_&lt;i&gt;.wav</c>
        /// (made once, kept across rebuilds).
        /// </summary>
        internal static AudioClip[] Layer(string folder, string owner, string layer, int variants, Func<int, AudioSynth> synth)
        {
            AudioClip[] overrides = Overrides(owner, layer);
            if (overrides.Length > 0) return overrides;

            ProjectSetupWizard.EnsureFolder($"{AudioFolder}/{folder}");
            var clips = new AudioClip[variants];
            for (int i = 0; i < variants; i++)
            {
                int variant = i;
                clips[i] = AudioSynth.LoadOrCreate($"{AudioFolder}/{folder}/{owner}_{layer}_{i}.wav", () => synth(variant));
            }

            return clips;
        }

        /// <summary>Recorded clips for a layer, sorted by path: <c>Overrides/&lt;owner&gt;/&lt;layer&gt;.wav</c> or <c>&lt;layer&gt;_*.wav</c>.</summary>
        internal static AudioClip[] Overrides(string owner, string layer)
        {
            string folder = $"{OverrideFolder}/{owner}";
            if (!AssetDatabase.IsValidFolder(folder)) return new AudioClip[0];

            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsOverrideFor(Path.GetFileNameWithoutExtension(path), layer)) paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);
            var clips = new AudioClip[paths.Count];
            for (int i = 0; i < clips.Length; i++) clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
            return clips;
        }

        /// <summary>Whether a file named <paramref name="fileName"/> overrides <paramref name="layer"/> ("Transient", "Transient_02").</summary>
        internal static bool IsOverrideFor(string fileName, string layer) =>
            string.Equals(fileName, layer, StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(layer + "_", StringComparison.OrdinalIgnoreCase);

        private static int StableSeed(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text) hash = hash * 31 + c;
                return hash & 0x7FFF;
            }
        }
    }
}
