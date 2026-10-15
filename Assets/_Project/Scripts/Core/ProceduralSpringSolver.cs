using System;
using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// Analytical 6-DOF damped harmonic oscillator: three positional axes (m) and three rotational axes (Euler degrees,
    /// small-angle) chasing their targets. Each sub-step uses the exact closed-form solution, so the result is stable at
    /// any frame rate; sub-steps exist so a target that moved during a long frame is followed along its path. The owner
    /// calls <see cref="Step"/> once per LateUpdate and applies <see cref="Position"/>/<see cref="Rotation"/>.
    /// </summary>
    [Serializable]
    public sealed class ProceduralSpringSolver
    {
        [Serializable]
        public struct Tuning
        {
            [Tooltip("Natural frequency (Hz).")] [Min(0f)] public float frequency;
            [Tooltip("Damping ratio: < 1 overshoots, 1 critical, > 1 sluggish.")] [Min(0f)] public float dampingRatio;

            public Tuning(float frequency, float dampingRatio)
            {
                this.frequency = frequency;
                this.dampingRatio = dampingRatio;
            }
        }

        /// <summary>Closed-form step: x' = pp·x + pv·v, v' = vp·x + vv·v (x relative to the target).</summary>
        public readonly struct StepCoefficients
        {
            public readonly float PosPos, PosVel, VelPos, VelVel;

            public StepCoefficients(float posPos, float posVel, float velPos, float velVel)
            {
                PosPos = posPos;
                PosVel = posVel;
                VelPos = velPos;
                VelVel = velVel;
            }
        }

        private const int MaxSubsteps = 16;
        private const float CriticalBand = 1e-4f;

        [SerializeField] private Tuning position = new Tuning(8f, 0.7f);
        [SerializeField] private Tuning rotation = new Tuning(8f, 0.7f);
        [Tooltip("Longest integration sub-step (s).")]
        [SerializeField, Range(1f / 480f, 1f / 30f)] private float maxSubstep = 1f / 240f;

        [NonSerialized] private Vector3 pos, posVel, rot, rotVel;
        [NonSerialized] private Vector3 posTarget, rotTarget, lastPosTarget, lastRotTarget;

        public ProceduralSpringSolver()
        {
        }

        public ProceduralSpringSolver(Tuning position, Tuning rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public Tuning PositionTuning { get => position; set => position = value; }
        public Tuning RotationTuning { get => rotation; set => rotation = value; }

        /// <summary>Current positional offset (m).</summary>
        public Vector3 Position => pos;
        /// <summary>Current rotational offset (Euler degrees).</summary>
        public Vector3 Rotation => rot;
        public Vector3 PositionVelocity => posVel;
        public Vector3 RotationVelocity => rotVel;
        public Vector3 PositionTarget { get => posTarget; set => posTarget = value; }
        public Vector3 RotationTarget { get => rotTarget; set => rotTarget = value; }

        /// <summary>Instant velocity change: linear (m/s) and angular (deg/s). Recoil and landings use this.</summary>
        public void AddImpulse(Vector3 linear, Vector3 angular)
        {
            posVel += linear;
            rotVel += angular;
        }

        /// <summary>Zeroes state and targets.</summary>
        public void Reset() => Teleport(Vector3.zero, Vector3.zero);

        /// <summary>Places the spring at rest on a new pose: current values and targets set, velocities cleared.</summary>
        public void Teleport(Vector3 position, Vector3 rotation)
        {
            pos = posTarget = lastPosTarget = position;
            rot = rotTarget = lastRotTarget = rotation;
            posVel = rotVel = Vector3.zero;
        }

        public void Step(float dt)
        {
            if (dt <= 0f) return;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / maxSubstep), 1, MaxSubsteps);
            float h = dt / steps;
            StepCoefficients p = Coefficients(position.frequency, position.dampingRatio, h);
            StepCoefficients r = Coefficients(rotation.frequency, rotation.dampingRatio, h);

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Integrate(ref pos, ref posVel, Vector3.LerpUnclamped(lastPosTarget, posTarget, t), p);
                Integrate(ref rot, ref rotVel, Vector3.LerpUnclamped(lastRotTarget, rotTarget, t), r);
            }

            lastPosTarget = posTarget;
            lastRotTarget = rotTarget;
        }

        public static void Integrate(ref Vector3 x, ref Vector3 v, Vector3 target, in StepCoefficients c)
        {
            Vector3 offset = x - target;
            x = target + offset * c.PosPos + v * c.PosVel;
            v = offset * c.VelPos + v * c.VelVel;
        }

        /// <summary>Exact one-step coefficients of x'' = -ω²x - 2ζωx' over <paramref name="dt"/> (Juckett's derivation).</summary>
        public static StepCoefficients Coefficients(float frequency, float dampingRatio, float dt)
        {
            float omega = frequency * 2f * Mathf.PI;
            if (omega < 1e-4f) return new StepCoefficients(1f, dt, 0f, 1f); // no spring: free drift

            float zeta = Mathf.Max(0f, dampingRatio);
            if (zeta > 1f + CriticalBand)
            {
                float za = -omega * zeta;
                float zb = omega * Mathf.Sqrt(zeta * zeta - 1f);
                float z1 = za - zb;
                float z2 = za + zb;
                float e1 = Mathf.Exp(z1 * dt);
                float e2 = Mathf.Exp(z2 * dt);
                float invTwoZb = 1f / (2f * zb);
                float e1OverTwoZb = e1 * invTwoZb;
                float e2OverTwoZb = e2 * invTwoZb;
                float z1e1OverTwoZb = z1 * e1OverTwoZb;
                float z2e2OverTwoZb = z2 * e2OverTwoZb;
                return new StepCoefficients(
                    e1OverTwoZb * z2 - z2e2OverTwoZb + e2,
                    -e1OverTwoZb + e2OverTwoZb,
                    (z1e1OverTwoZb - z2e2OverTwoZb + e2) * z2,
                    -z1e1OverTwoZb + z2e2OverTwoZb);
            }

            if (zeta < 1f - CriticalBand)
            {
                float omegaZeta = omega * zeta;
                float alpha = omega * Mathf.Sqrt(1f - zeta * zeta);
                float expTerm = Mathf.Exp(-omegaZeta * dt);
                float cosTerm = Mathf.Cos(alpha * dt);
                float sinTerm = Mathf.Sin(alpha * dt);
                float invAlpha = 1f / alpha;
                float expSin = expTerm * sinTerm;
                float expCos = expTerm * cosTerm;
                float expOmegaZetaSinOverAlpha = expTerm * omegaZeta * sinTerm * invAlpha;
                return new StepCoefficients(
                    expCos + expOmegaZetaSinOverAlpha,
                    expSin * invAlpha,
                    -expSin * alpha - omegaZeta * expOmegaZetaSinOverAlpha,
                    expCos - expOmegaZetaSinOverAlpha);
            }

            float exp = Mathf.Exp(-omega * dt);
            float timeExp = dt * exp;
            float timeExpFreq = timeExp * omega;
            return new StepCoefficients(timeExpFreq + exp, timeExp, -omega * timeExpFreq, -timeExpFreq + exp);
        }
    }
}
