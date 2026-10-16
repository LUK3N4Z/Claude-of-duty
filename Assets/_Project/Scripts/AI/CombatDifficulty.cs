using UnityEngine;

namespace FPS.AI
{
    public enum AIDifficulty
    {
        /// <summary>Slow to notice, slow to react, loose aim.</summary>
        Recruit,
        Regular,
        Hardened,
        /// <summary>Spots you fast and far, reacts at once, groups tightly.</summary>
        Realism,
    }

    /// <summary>How an AI difficulty scales perception and gunnery against the authored (Regular) values.</summary>
    public readonly struct AIDifficultyProfile
    {
        /// <summary>Awareness gain per second of sight.</summary>
        public readonly float Awareness;
        /// <summary>Daylight sight range.</summary>
        public readonly float SightRange;
        /// <summary>Aim spread (smaller = tighter groups).</summary>
        public readonly float AimSpread;
        /// <summary>Reaction time from identifying a target to the first shot.</summary>
        public readonly float Reaction;

        public AIDifficultyProfile(float awareness, float sightRange, float aimSpread, float reaction)
        {
            Awareness = awareness;
            SightRange = sightRange;
            AimSpread = aimSpread;
            Reaction = reaction;
        }
    }

    /// <summary>
    /// The difficulty soldiers fight at, chosen at the deployment table and kept across scene loads. Perception and the
    /// brain multiply their authored values by <see cref="Profile"/> when they use them, so a change applies at once.
    /// </summary>
    public static class CombatDifficulty
    {
        public static AIDifficulty Current { get; set; } = AIDifficulty.Regular;

        public static AIDifficultyProfile Profile => For(Current);

        public static AIDifficultyProfile For(AIDifficulty difficulty)
        {
            switch (difficulty)
            {
                case AIDifficulty.Recruit: return new AIDifficultyProfile(0.6f, 0.8f, 1.8f, 1.6f);
                case AIDifficulty.Hardened: return new AIDifficultyProfile(1.3f, 1.1f, 0.75f, 0.8f);
                case AIDifficulty.Realism: return new AIDifficultyProfile(1.6f, 1.2f, 0.55f, 0.65f);
                default: return new AIDifficultyProfile(1f, 1f, 1f, 1f);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Current = AIDifficulty.Regular;
    }
}
