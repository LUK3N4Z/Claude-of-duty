using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

namespace FPS.EditorTools
{
    /// <summary>
    /// Tiny offline synthesizer for placeholder sound design: noise bursts, decaying partials, one-pole filters and
    /// echoes, written as 16-bit mono WAV assets. Deterministic per seed so rebuilds are stable.
    /// </summary>
    internal sealed class AudioSynth
    {
        public const int SampleRate = 44100;

        private readonly float[] samples;
        private readonly Random random;

        public AudioSynth(float seconds, int seed)
        {
            samples = new float[Mathf.CeilToInt(seconds * SampleRate)];
            random = new Random(seed);
        }

        public int Length => samples.Length;

        /// <summary>White noise starting at <paramref name="start"/> with attack/decay envelope.</summary>
        public AudioSynth Noise(float gain, float start, float attack, float decay)
        {
            for (int i = Index(start); i < samples.Length; i++)
            {
                float t = (float)i / SampleRate - start;
                samples[i] += Next() * gain * Envelope(t, attack, decay);
            }

            return this;
        }

        /// <summary>A sine whose frequency glides from <paramref name="from"/> to <paramref name="to"/> while decaying.</summary>
        public AudioSynth Tone(float from, float to, float gain, float start, float attack, float decay, float glideTime = 0.05f)
        {
            float phase = (float)random.NextDouble() * Mathf.PI * 2f;
            for (int i = Index(start); i < samples.Length; i++)
            {
                float t = (float)i / SampleRate - start;
                float frequency = Mathf.Lerp(from, to, Mathf.Clamp01(t / glideTime));
                phase += 2f * Mathf.PI * frequency / SampleRate;
                samples[i] += Mathf.Sin(phase) * gain * Envelope(t, attack, decay);
            }

            return this;
        }

        /// <summary>Inharmonic metallic partials (frequency, relative gain) decaying faster as they rise.</summary>
        public AudioSynth Partials(float[] frequencies, float gain, float start, float decay, float pitch = 1f)
        {
            for (int k = 0; k < frequencies.Length; k++)
                Tone(frequencies[k] * pitch, frequencies[k] * pitch, gain / (1f + k * 0.6f), start, 0.0005f, decay * (1f - 0.15f * k), 0f);
            return this;
        }

        public AudioSynth LowPass(float cutoff)
        {
            float alpha = Alpha(cutoff);
            float state = 0f;
            for (int i = 0; i < samples.Length; i++) samples[i] = state += alpha * (samples[i] - state);
            return this;
        }

        public AudioSynth HighPass(float cutoff)
        {
            float alpha = Alpha(cutoff);
            float low = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                low += alpha * (samples[i] - low);
                samples[i] -= low;
            }

            return this;
        }

        /// <summary>Adds delayed, attenuated copies of the current signal (discrete echoes / slapback).</summary>
        public AudioSynth Echo(float delay, float gain)
        {
            int offset = Index(delay);
            for (int i = samples.Length - 1; i >= offset; i--) samples[i] += samples[i - offset] * gain;
            return this;
        }

        /// <summary>Supersonic N-wave: instant overpressure, linear fall through zero, snap back.</summary>
        public AudioSynth NWave(float gain, float start, float width)
        {
            int begin = Index(start);
            int length = Mathf.Max(2, Index(width));
            for (int i = 0; i < length && begin + i < samples.Length; i++) samples[begin + i] += gain * (1f - 2f * i / (length - 1f));
            return this;
        }

        /// <summary>Normalises to 0.9 peak and writes <paramref name="assetPath"/>, returning the imported clip.</summary>
        public AudioClip Save(string assetPath)
        {
            float peak = 1e-6f;
            foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            float gain = 0.9f / peak;

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)1); // mono
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);
                foreach (float sample in samples)
                    writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(sample * gain * short.MaxValue), short.MinValue, short.MaxValue));
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

        private static float Envelope(float t, float attack, float decay) =>
            t < attack ? t / Mathf.Max(attack, 1e-5f) : Mathf.Exp(-(t - attack) / Mathf.Max(decay, 1e-5f));

        private static float Alpha(float cutoff)
        {
            float rc = 1f / (2f * Mathf.PI * cutoff);
            float dt = 1f / SampleRate;
            return dt / (rc + dt);
        }

        private float Next() => (float)random.NextDouble() * 2f - 1f;

        private static int Index(float seconds) => Mathf.Max(0, Mathf.RoundToInt(seconds * SampleRate));
    }
}
