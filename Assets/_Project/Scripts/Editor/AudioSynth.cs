using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace FPS.EditorTools
{
    /// <summary>
    /// Offline synthesizer for the game's sound design, written as 48 kHz 16-bit mono or stereo WAV assets. Besides
    /// noise, tones, partials and one-pole filters it models gunshots physically: a Friedlander blast wave with
    /// asymmetric air-overload clipping, phase-aligned sub-bass sweeps, coil springs with dispersion, and room impulse
    /// responses (discrete reflections, flutter echoes, frequency-dependent diffuse decay, rolling low rumble) applied to
    /// a dry signal by convolution. Deterministic per seed so rebuilds are stable.
    /// </summary>
    internal sealed class AudioSynth
    {
        public const int SampleRate = 48000;

        private readonly float[][] channels;
        private readonly Random random;

        public AudioSynth(float seconds, int seed, int channelCount = 1)
        {
            int length = Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate));
            channels = new float[Mathf.Clamp(channelCount, 1, 2)][];
            for (int c = 0; c < channels.Length; c++) channels[c] = new float[length];
            random = new Random(seed);
        }

        public int Length => channels[0].Length;
        public int Channels => channels.Length;

        /// <summary>The raw samples of one channel (not normalised).</summary>
        public float[] Samples(int channel) => channels[channel];

        // ---- Sources ----

        /// <summary>White noise starting at <paramref name="start"/> with attack/decay envelope, independent per channel.</summary>
        public AudioSynth Noise(float gain, float start, float attack, float decay)
        {
            foreach (float[] s in channels)
            {
                for (int i = Index(start); i < s.Length; i++)
                {
                    float t = (float)i / SampleRate - start;
                    s[i] += Next() * gain * Envelope(t, attack, decay);
                }
            }

            return this;
        }

        /// <summary>A sine whose frequency glides from <paramref name="from"/> to <paramref name="to"/> while decaying.</summary>
        public AudioSynth Tone(float from, float to, float gain, float start, float attack, float decay, float glideTime = 0.05f)
        {
            double phase = random.NextDouble() * Math.PI * 2.0;
            for (int i = Index(start); i < Length; i++)
            {
                float t = (float)i / SampleRate - start;
                float frequency = Mathf.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(glideTime, 1e-5f)));
                phase += 2.0 * Math.PI * frequency / SampleRate;
                Add(i, (float)Math.Sin(phase) * gain * Envelope(t, attack, decay));
            }

            return this;
        }

        /// <summary>Inharmonic metallic partials decaying faster as they rise.</summary>
        public AudioSynth Partials(float[] frequencies, float gain, float start, float decay, float pitch = 1f)
        {
            for (int k = 0; k < frequencies.Length; k++)
                Tone(frequencies[k] * pitch, frequencies[k] * pitch, gain / (1f + k * 0.6f), start, 0.0005f, decay * (1f - 0.15f * k), 0f);
            return this;
        }

        /// <summary>Supersonic N-wave: instant overpressure, linear fall through zero, snap back.</summary>
        public AudioSynth NWave(float gain, float start, float width)
        {
            int begin = Index(start);
            int length = Mathf.Max(2, Index(width));
            for (int i = 0; i < length && begin + i < Length; i++) Add(begin + i, gain * (1f - 2f * i / (length - 1f)));
            return this;
        }

        /// <summary>
        /// Friedlander blast wave: an instant shock front to <paramref name="peak"/>, the positive phase falling through
        /// zero at <paramref name="positiveDuration"/> s, then a longer rarefaction recovering to ambient:
        /// p(t) = P (1 - t/T) e^(-b t/T). A small <paramref name="decayShape"/> b gives a deep negative phase (N-wave like).
        /// </summary>
        public AudioSynth Blast(float peak, float start, float positiveDuration, float decayShape = 0.6f)
        {
            int begin = Index(start);
            float samplesPerPhase = Mathf.Max(1f, positiveDuration * SampleRate);
            int length = Mathf.Min(Length - begin, Mathf.CeilToInt(samplesPerPhase * 10f));
            for (int i = 0; i < length; i++)
            {
                float x = i / samplesPerPhase;
                Add(begin + i, peak * (1f - x) * Mathf.Exp(-decayShape * x));
            }

            return this;
        }

        /// <summary>
        /// Phase-aligned sub-bass chest thump: an exponential sine sweep from <paramref name="from"/> down to
        /// <paramref name="to"/> Hz that starts at zero phase with the blast (compression first) and dies away over
        /// <paramref name="decay"/> s.
        /// </summary>
        public AudioSynth SubThump(float from, float to, float gain, float start, float decay)
        {
            int begin = Index(start);
            double phase = 0.0;
            float sweep = decay * 1.5f;
            for (int i = begin; i < Length; i++)
            {
                float t = (float)(i - begin) / SampleRate;
                float frequency = from * Mathf.Pow(to / from, Mathf.Clamp01(t / sweep));
                float envelope = Mathf.Min(1f, t / 0.0015f) * Mathf.Exp(-t / decay);
                if (t > decay && envelope < 1e-5f) break;
                Add(i, (float)Math.Sin(phase) * gain * envelope);
                phase += 2.0 * Math.PI * frequency / SampleRate;
            }

            return this;
        }

        /// <summary>
        /// A coil spring's twang (an AR-15 buffer spring): inharmonic modes that start sharp and sag in pitch (dispersion
        /// along the coil) with a slow wobble from the coils rubbing their tube.
        /// </summary>
        public AudioSynth Spring(float fundamental, float gain, float start, float decay, float wobbleHz = 7f, float wobbleDepth = 0.012f)
        {
            float[] ratios = { 1f, 2.71f, 5.18f };
            float[] weights = { 1f, 0.55f, 0.3f };
            int begin = Index(start);
            for (int k = 0; k < ratios.Length; k++)
            {
                double phase = random.NextDouble() * Math.PI * 2.0;
                float modeDecay = decay / (1f + 0.5f * k);
                for (int i = begin; i < Length; i++)
                {
                    float t = (float)(i - begin) / SampleRate;
                    float sag = 1f + 0.06f * Mathf.Exp(-t / 0.02f);
                    float wobble = 1f + wobbleDepth * Mathf.Sin(2f * Mathf.PI * wobbleHz * t);
                    phase += 2.0 * Math.PI * fundamental * ratios[k] * sag * wobble / SampleRate;
                    float envelope = Mathf.Min(1f, t / 0.0004f) * Mathf.Exp(-t / modeDecay);
                    if (t > modeDecay && envelope < 1e-5f) break;
                    Add(i, (float)Math.Sin(phase) * gain * weights[k] * envelope);
                }
            }

            return this;
        }

        /// <summary>A small hard contact (trigger reset, extractor snap): a sub-millisecond burst plus short partials.</summary>
        public AudioSynth Click(float gain, float start, float brightness)
        {
            int begin = Index(start);
            int length = Mathf.Min(Length - begin, Index(0.002f));
            for (int i = 0; i < length; i++) Add(begin + i, Next() * gain * Mathf.Exp(-i / (0.0003f * SampleRate)));
            return Partials(new[] { brightness, brightness * 1.63f, brightness * 2.41f }, gain * 0.6f, start, 0.004f);
        }

        // ---- Impulse responses ----

        /// <summary>A discrete reflection: an impulse at <paramref name="time"/>, panned equal-power from -1 (left) to 1 (right).</summary>
        public AudioSynth Reflection(float time, float gain, float pan)
        {
            int i = Index(time);
            if (i >= Length) return this;
            if (channels.Length == 1)
            {
                channels[0][i] += gain;
                return this;
            }

            float angle = (Mathf.Clamp(pan, -1f, 1f) + 1f) * Mathf.PI * 0.25f;
            channels[0][i] += gain * Mathf.Cos(angle);
            channels[1][i] += gain * Mathf.Sin(angle);
            return this;
        }

        /// <summary>
        /// Flutter echo between two parallel facades: reflections every <paramref name="period"/> s, alternating sides by
        /// <paramref name="width"/>, keeping <paramref name="retention"/> of the level per bounce.
        /// </summary>
        public AudioSynth Flutter(float start, float period, int count, float gain, float retention, float width)
        {
            for (int n = 0; n < count; n++)
            {
                float jitter = (float)(random.NextDouble() - 0.5) * period * 0.04f;
                Reflection(start + n * period + jitter, gain * Mathf.Pow(retention, n), n % 2 == 0 ? -width : width);
            }

            return this;
        }

        /// <summary>
        /// Diffuse late reverberation: decorrelated noise per channel decaying 60 dB over <paramref name="rt60"/> s, its
        /// brightness falling from <paramref name="brightCutoff"/> to <paramref name="darkCutoff"/> Hz (air and walls
        /// absorb the highs first). RMS-compensated so the cutoff does not change the level.
        /// </summary>
        public AudioSynth Diffuse(float gain, float start, float rt60, float brightCutoff, float darkCutoff, float build = 0.01f)
        {
            int begin = Index(start);
            foreach (float[] s in channels)
            {
                float state = 0f;
                for (int i = begin; i < s.Length; i++)
                {
                    float t = (float)(i - begin) / SampleRate;
                    float envelope = Mathf.Min(1f, t / build) * Mathf.Pow(10f, -3f * t / rt60);
                    float alpha = Alpha(Mathf.Lerp(brightCutoff, darkCutoff, Mathf.Clamp01(t / rt60)));
                    state += alpha * (Next() - state);
                    s[i] += state * Mathf.Sqrt((2f - alpha) / alpha) * gain * envelope;
                }
            }

            return this;
        }

        /// <summary>
        /// Rolling thunder over open ground: low-passed noise whose level swells and fades every ~1/<paramref name="rollRate"/>
        /// s as reflections from distant hills and tree lines arrive, from <paramref name="start"/> until it has died
        /// away (-60 dB) at <paramref name="end"/>.
        /// </summary>
        public AudioSynth Rumble(float gain, float start, float end, float cutoff, float rollRate)
        {
            int begin = Index(start);
            float span = Mathf.Max(0.05f, end - start);
            float alpha = Alpha(cutoff);
            float compensation = (2f - alpha) / alpha; // two cascaded poles: one RMS factor each
            foreach (float[] s in channels)
            {
                float a = 0f, b = 0f, from = (float)random.NextDouble(), to = (float)random.NextDouble(), phase = 0f;
                for (int i = begin; i < s.Length; i++)
                {
                    float t = (float)(i - begin) / SampleRate;
                    phase += rollRate / SampleRate;
                    if (phase >= 1f)
                    {
                        phase -= 1f;
                        from = to;
                        to = (float)random.NextDouble();
                    }

                    float roll = Mathf.Lerp(from, to, 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI));
                    float envelope = Mathf.Min(1f, t / 0.15f) * Mathf.Pow(10f, -3f * t / span) * (0.25f + roll);
                    a += alpha * (Next() - a);
                    b += alpha * (a - b);
                    s[i] += b * compensation * gain * envelope;
                }
            }

            return this;
        }

        /// <summary>
        /// Convolves a mono <paramref name="dry"/> signal with this impulse response (each channel) into a new synth:
        /// the dry sound as heard through the space.
        /// </summary>
        public AudioSynth Convolve(float[] dry, int seed)
        {
            int dryLength = dry.Length;
            while (dryLength > 1 && Mathf.Abs(dry[dryLength - 1]) < 1e-4f) dryLength--;

            var output = new AudioSynth((float)(Length + dryLength) / SampleRate, seed, channels.Length);
            for (int c = 0; c < channels.Length; c++)
            {
                float[] ir = channels[c], wet = output.channels[c];
                for (int i = 0; i < ir.Length; i++)
                {
                    float h = ir[i];
                    if (h == 0f) continue;
                    for (int j = 0; j < dryLength; j++) wet[i + j] += h * dry[j];
                }
            }

            return output;
        }

        // ---- Processing ----

        public AudioSynth LowPass(float cutoff)
        {
            float alpha = Alpha(cutoff);
            foreach (float[] s in channels)
            {
                float state = 0f;
                for (int i = 0; i < s.Length; i++) s[i] = state += alpha * (s[i] - state);
            }

            return this;
        }

        public AudioSynth HighPass(float cutoff)
        {
            float alpha = Alpha(cutoff);
            foreach (float[] s in channels)
            {
                float low = 0f;
                for (int i = 0; i < s.Length; i++)
                {
                    low += alpha * (s[i] - low);
                    s[i] -= low;
                }
            }

            return this;
        }

        public AudioSynth BandPass(float low, float high) => HighPass(low).LowPass(high);

        /// <summary>Adds delayed, attenuated copies of the current signal (discrete echoes / slapback).</summary>
        public AudioSynth Echo(float delay, float gain)
        {
            int offset = Index(delay);
            foreach (float[] s in channels)
            {
                for (int i = s.Length - 1; i >= offset; i--) s[i] += s[i - offset] * gain;
            }

            return this;
        }

        /// <summary>
        /// Air overload: at 165+ dB SPL the medium itself is nonlinear. Compression peaks saturate softly
        /// (tanh(drive x)); the rarefaction side, bounded by ambient pressure, clips harder by <paramref name="asymmetry"/>
        /// (tanh(drive a x) / a). Small signals pass at unity gain.
        /// </summary>
        public AudioSynth Overdrive(float drive, float asymmetry)
        {
            float peak = Peak();
            if (peak <= 1e-9f) return this;
            float a = Mathf.Max(1f, asymmetry);
            foreach (float[] s in channels)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    float x = s[i] / peak;
                    float y = x >= 0f ? (float)Math.Tanh(drive * x) : (float)Math.Tanh(drive * a * x) / a;
                    s[i] = y * peak / drive;
                }
            }

            return this;
        }

        public AudioSynth Gain(float gain)
        {
            foreach (float[] s in channels)
            {
                for (int i = 0; i < s.Length; i++) s[i] *= gain;
            }

            return this;
        }

        /// <summary>Mixes another synth (from the start, mono into every channel or channel by channel) at <paramref name="gain"/>.</summary>
        public AudioSynth Mix(AudioSynth other, float gain)
        {
            for (int c = 0; c < channels.Length; c++)
            {
                float[] source = other.channels[Mathf.Min(c, other.channels.Length - 1)], target = channels[c];
                int length = Mathf.Min(source.Length, target.Length);
                for (int i = 0; i < length; i++) target[i] += source[i] * gain;
            }

            return this;
        }

        /// <summary>
        /// Makes the clip loop seamlessly: its last <paramref name="crossfade"/> s are faded into the start (equal power)
        /// and cut off, so the end flows into the beginning.
        /// </summary>
        public AudioSynth Loopable(float crossfade)
        {
            int fade = Mathf.Clamp(Index(crossfade), 1, Length / 2);
            int length = Length - fade;
            for (int c = 0; c < channels.Length; c++)
            {
                float[] s = channels[c];
                var looped = new float[length];
                for (int i = 0; i < length; i++)
                {
                    float sample = s[i];
                    if (i < fade)
                    {
                        float x = (float)i / fade;
                        sample = s[i] * Mathf.Sin(x * Mathf.PI * 0.5f) + s[length + i] * Mathf.Cos(x * Mathf.PI * 0.5f);
                    }

                    looped[i] = sample;
                }

                channels[c] = looped;
            }

            return this;
        }

        public float Peak()
        {
            float peak = 0f;
            foreach (float[] s in channels)
            {
                foreach (float sample in s) peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            return peak;
        }

        // ---- Output ----

        /// <summary>Normalises to 0.9 peak and writes <paramref name="assetPath"/> (interleaved PCM), returning the imported clip.</summary>
        public AudioClip Save(string assetPath)
        {
            float gain = 0.9f / Mathf.Max(1e-6f, Peak());
            int frames = Length, count = channels.Length;

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                int dataBytes = frames * count * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)count);
                writer.Write(SampleRate);
                writer.Write(SampleRate * count * 2); // byte rate
                writer.Write((short)(count * 2)); // block align
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);
                for (int i = 0; i < frames; i++)
                {
                    for (int c = 0; c < count; c++)
                        writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(channels[c][i] * gain * short.MaxValue), short.MinValue, short.MaxValue));
                }
            }

            AssetDatabase.ImportAsset(assetPath);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        }

        /// <summary>The clip at <paramref name="assetPath"/>, synthesizing it first if missing.</summary>
        public static AudioClip LoadOrCreate(string assetPath, Func<AudioSynth> build)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            return existing != null ? existing : build().Save(assetPath);
        }

        private void Add(int index, float value)
        {
            foreach (float[] s in channels) s[index] += value;
        }

        private static float Envelope(float t, float attack, float decay) =>
            t < attack ? t / Mathf.Max(attack, 1e-5f) : Mathf.Exp(-(t - attack) / Mathf.Max(decay, 1e-5f));

        private static float Alpha(float cutoff)
        {
            float rc = 1f / (2f * Mathf.PI * Mathf.Max(1f, cutoff));
            float dt = 1f / SampleRate;
            return dt / (rc + dt);
        }

        private float Next() => (float)random.NextDouble() * 2f - 1f;

        private static int Index(float seconds) => Mathf.Max(0, Mathf.RoundToInt(seconds * SampleRate));
    }
}
