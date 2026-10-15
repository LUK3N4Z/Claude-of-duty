using UnityEngine;

namespace FPS.Ballistics
{
    /// <summary>
    /// Point-mass external ballistics: gravity plus aerodynamic drag from the standard G1/G7 reference drag curves,
    /// scaled by the bullet's ballistic coefficient and computed against the air (velocity relative to wind).
    /// </summary>
    public static class BallisticsMath
    {
        public const float SeaLevelAirDensity = 1.225f;
        public const float SpeedOfSound = 343f;
        private const float BallisticCoefficientToSI = 703.0696f; // lb/in² → kg/m²

        // Standard reference drag coefficient (Cd) versus Mach.
        private static readonly float[] G1Mach = { 0f, 0.2f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.85f, 0.9f, 0.95f, 1f, 1.05f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.7f, 2f, 2.5f, 3f, 3.5f, 4f };
        private static readonly float[] G1Cd = { 0.2629f, 0.2344f, 0.2104f, 0.2032f, 0.2034f, 0.2165f, 0.2546f, 0.2901f, 0.3415f, 0.4084f, 0.4805f, 0.5427f, 0.5883f, 0.6393f, 0.6589f, 0.6625f, 0.6573f, 0.6347f, 0.5934f, 0.5397f, 0.5133f, 0.504f, 0.5006f };
        private static readonly float[] G7Mach = { 0f, 0.5f, 0.7f, 0.8f, 0.85f, 0.9f, 0.925f, 0.95f, 0.975f, 1f, 1.025f, 1.05f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.7f, 2f, 2.5f, 3f, 3.5f, 4f };
        private static readonly float[] G7Cd = { 0.1198f, 0.1194f, 0.1202f, 0.1242f, 0.1306f, 0.1464f, 0.166f, 0.2054f, 0.2993f, 0.3803f, 0.4015f, 0.4043f, 0.4014f, 0.3884f, 0.3732f, 0.358f, 0.344f, 0.3209f, 0.298f, 0.2697f, 0.2424f, 0.2154f, 0.1935f };

        public static float DragCoefficient(DragModel model, float mach)
        {
            float[] machs = model == DragModel.G1 ? G1Mach : G7Mach;
            float[] cds = model == DragModel.G1 ? G1Cd : G7Cd;
            if (mach <= machs[0]) return cds[0];

            for (int i = 1; i < machs.Length; i++)
            {
                if (mach <= machs[i]) return Mathf.Lerp(cds[i - 1], cds[i], (mach - machs[i - 1]) / (machs[i] - machs[i - 1]));
            }

            return cds[cds.Length - 1];
        }

        /// <summary>
        /// Drag deceleration (m/s²) at <paramref name="airSpeed"/>: ρ·v²·Cd(M)·π / (8·BC), with BC converted to kg/m².
        /// </summary>
        public static float DragDeceleration(CaliberDataSO caliber, float airSpeed, float airDensity)
        {
            float cd = DragCoefficient(caliber.DragModel, airSpeed / SpeedOfSound);
            return airDensity * airSpeed * airSpeed * cd * Mathf.PI / (8f * caliber.BallisticCoefficient * BallisticCoefficientToSI);
        }

        /// <summary>One semi-implicit Euler step under gravity and drag against the air.</summary>
        public static void Step(ref Vector3 position, ref Vector3 velocity, CaliberDataSO caliber, Vector3 wind, float airDensity, float dt)
        {
            Vector3 air = velocity - wind;
            float airSpeed = air.magnitude;
            Vector3 acceleration = Physics.gravity;
            if (airSpeed > 1e-3f) acceleration -= air * (DragDeceleration(caliber, airSpeed, airDensity) / airSpeed);
            velocity += acceleration * dt;
            position += velocity * dt;
        }

        /// <summary>
        /// Energy needed per metre of a material (J/m): its resistance × the bullet's cross-section, eased for armor-piercing
        /// bullets (penetration power 100 halves it, 0 adds half again).
        /// </summary>
        public static float PenetrationCostPerMeter(SurfaceMaterialSO surface, CaliberDataSO caliber)
        {
            float radiusMm = caliber.BulletDiameterMeters * 500f;
            float crossSectionMm2 = Mathf.PI * radiusMm * radiusMm;
            return surface.Resistance * crossSectionMm2 * Mathf.Lerp(1.5f, 0.5f, caliber.PenetrationPower / 100f);
        }

        /// <summary>Rotates <paramref name="direction"/> by a random angle up to <paramref name="maxAngle"/> degrees.</summary>
        public static Vector3 Deflect(Vector3 direction, float maxAngle)
        {
            if (maxAngle <= 0f) return direction;
            Vector3 axis = Vector3.Cross(direction, Random.onUnitSphere);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(direction, Vector3.up);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;
            return Quaternion.AngleAxis(Random.Range(0f, maxAngle), axis.normalized) * direction;
        }
    }
}
