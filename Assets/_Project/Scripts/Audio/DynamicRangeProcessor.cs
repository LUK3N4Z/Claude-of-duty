using UnityEngine;

namespace FPS.Audio
{
    public enum AudioDynamicRange
    {
        /// <summary>Loud and wide: makeup gain into a soft saturating limiter, gunfire slams.</summary>
        WarTapes,
        /// <summary>Transparent: the full dynamic range, a safety limiter only.</summary>
        StudioReference,
        /// <summary>Heavy compression: quiet sounds lifted, gunfire tamed, for low listening levels.</summary>
        NightMode,
    }

    /// <summary>
    /// Master-bus dynamics on the AudioListener's GameObject (OnAudioFilterRead sees the whole mix): a feed-forward
    /// compressor (peak follower in dB with attack/release, threshold, ratio, makeup) followed by a limiter at the
    /// ceiling — soft (tanh) for War Tapes, hard otherwise. Presets set the parameters; the audio thread only reads them.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioListener))]
    public sealed class DynamicRangeProcessor : MonoBehaviour
    {
        public struct Curve
        {
            public float ThresholdDb;
            public float Ratio;
            public float MakeupDb;
            public float CeilingDb;
            public float AttackSeconds;
            public float ReleaseSeconds;
            public bool SoftLimit;
        }

        [SerializeField] private AudioDynamicRange preset = AudioDynamicRange.StudioReference;

        private volatile float thresholdDb, ratio, makeupDb, ceiling, attackCoefficient, releaseCoefficient;
        private volatile bool softLimit;
        private float envelopeDb = -120f;
        private int sampleRate;

        public AudioDynamicRange Preset
        {
            get => preset;
            set
            {
                preset = value;
                Configure();
            }
        }

        /// <summary>Gain reduction applied to the last processed block (dB, positive).</summary>
        public float GainReductionDb { get; private set; }

        public static Curve CurveFor(AudioDynamicRange range)
        {
            switch (range)
            {
                case AudioDynamicRange.WarTapes:
                    return new Curve { ThresholdDb = -10f, Ratio = 1.5f, MakeupDb = 4f, CeilingDb = -0.5f, AttackSeconds = 0.002f, ReleaseSeconds = 0.25f, SoftLimit = true };
                case AudioDynamicRange.NightMode:
                    return new Curve { ThresholdDb = -30f, Ratio = 5f, MakeupDb = 12f, CeilingDb = -6f, AttackSeconds = 0.001f, ReleaseSeconds = 0.4f, SoftLimit = false };
                default:
                    return new Curve { ThresholdDb = 0f, Ratio = 1f, MakeupDb = 0f, CeilingDb = -0.3f, AttackSeconds = 0.001f, ReleaseSeconds = 0.1f, SoftLimit = false };
            }
        }

        /// <summary>Steady-state output level for a constant input level (dB), before the limiter.</summary>
        public static float OutputDb(AudioDynamicRange range, float inputDb)
        {
            Curve curve = CurveFor(range);
            float over = inputDb - curve.ThresholdDb;
            float reduction = over > 0f ? over * (1f - 1f / curve.Ratio) : 0f;
            return Mathf.Min(inputDb - reduction + curve.MakeupDb, curve.CeilingDb);
        }

        /// <summary>Processes interleaved samples in place (the audio thread's filter callback).</summary>
        public void Process(float[] data, int channels)
        {
            float threshold = thresholdDb, slope = 1f - 1f / ratio, makeup = makeupDb, limit = ceiling;
            float attack = attackCoefficient, release = releaseCoefficient;
            bool soft = softLimit;
            float maxReduction = 0f;
            for (int frame = 0; frame < data.Length; frame += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++) peak = Mathf.Max(peak, Mathf.Abs(data[frame + c]));
                float levelDb = 20f * Mathf.Log10(peak + 1e-6f);
                float coefficient = levelDb > envelopeDb ? attack : release;
                envelopeDb = coefficient * envelopeDb + (1f - coefficient) * levelDb;

                float over = envelopeDb - threshold;
                float reduction = over > 0f ? over * slope : 0f;
                maxReduction = Mathf.Max(maxReduction, reduction);
                float gain = Mathf.Pow(10f, (makeup - reduction) / 20f);
                for (int c = 0; c < channels; c++)
                {
                    float sample = data[frame + c] * gain;
                    data[frame + c] = soft ? limit * Tanh(sample / limit) : Mathf.Clamp(sample, -limit, limit);
                }
            }

            GainReductionDb = maxReduction;
        }

        private static float Tanh(float x)
        {
            float e = Mathf.Exp(2f * Mathf.Clamp(x, -10f, 10f));
            return (e - 1f) / (e + 1f);
        }

        private void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            Configure();
        }

        private void OnValidate()
        {
            if (sampleRate > 0) Configure();
        }

        private void Configure()
        {
            if (sampleRate <= 0) sampleRate = AudioSettings.outputSampleRate;
            Curve curve = CurveFor(preset);
            thresholdDb = curve.ThresholdDb;
            ratio = Mathf.Max(1f, curve.Ratio);
            makeupDb = curve.MakeupDb;
            ceiling = Mathf.Pow(10f, curve.CeilingDb / 20f);
            attackCoefficient = Mathf.Exp(-1f / (curve.AttackSeconds * sampleRate));
            releaseCoefficient = Mathf.Exp(-1f / (curve.ReleaseSeconds * sampleRate));
            softLimit = curve.SoftLimit;
        }

        private void OnAudioFilterRead(float[] data, int channels) => Process(data, channels);
    }
}
