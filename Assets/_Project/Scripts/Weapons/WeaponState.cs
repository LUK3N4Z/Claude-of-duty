using System.Collections.Generic;

namespace FPS.Weapons
{
    public enum ReloadStage
    {
        None,
        MagazineRemoved,
        MagazineInserted,
        BoltCycled,
    }

    public enum MagazineEstimate
    {
        Empty,
        NearlyEmpty,
        LessThanHalf,
        AboutHalf,
        MoreThanHalf,
        NearlyFull,
        Full,
    }

    /// <summary>
    /// Mechanical state of one carried weapon. Kept while the weapon is holstered, so an interrupted reload resumes
    /// from its last completed stage and a removed magazine stays removed.
    /// </summary>
    public sealed class WeaponState
    {
        public readonly WeaponDataSO Data;
        /// <summary>Rounds in each spare magazine carried for this weapon.</summary>
        public readonly List<int> SpareMagazines = new List<int>(8);

        public bool HasMagazine;
        public int MagazineRounds;
        /// <summary>Closed bolt and bolt action: a live round is in the chamber. Open bolt: the bolt is cocked.</summary>
        public bool RoundChambered;
        /// <summary>The bolt catch holds the bolt open on an empty magazine.</summary>
        public bool BoltLockedBack;
        public int FireModeIndex;
        /// <summary>Last completed stage of an unfinished reload; None when no reload is pending.</summary>
        public ReloadStage CompletedStage;
        /// <summary>The pending reload drops the magazine instead of retaining it.</summary>
        public bool SpeedReload;

        /// <summary>
        /// A topped-off weapon: round chambered (or bolt cocked), then a full magazine inserted, plus full spares.
        /// Closed-bolt and bolt-action weapons therefore start at capacity + 1.
        /// </summary>
        public WeaponState(WeaponDataSO data)
        {
            Data = data;
            HasMagazine = true;
            MagazineRounds = data.MagazineCapacity;
            RoundChambered = true;
            for (int i = 0; i < data.SpareMagazines; i++) SpareMagazines.Add(data.MagazineCapacity);
        }

        public FireMode FireMode => Data.GetFireMode(FireModeIndex);
        public bool ReloadPending => CompletedStage != ReloadStage.None;

        /// <summary>Rounds that can be fired without reloading (magazine plus chamber).</summary>
        public int LoadedRounds => (HasMagazine ? MagazineRounds : 0) + (RoundChambered && Data.BoltType != BoltType.OpenBolt ? 1 : 0);

        public bool CanFire => Data.BoltType == BoltType.OpenBolt
            ? RoundChambered && HasMagazine && MagazineRounds > 0
            : RoundChambered;

        /// <summary>Index of the fullest non-empty spare magazine, or -1.</summary>
        public int BestSpareIndex()
        {
            int best = -1;
            for (int i = 0; i < SpareMagazines.Count; i++)
            {
                if (SpareMagazines[i] > 0 && (best < 0 || SpareMagazines[i] > SpareMagazines[best])) best = i;
            }

            return best;
        }

        /// <summary>What a shooter can tell by feel and a glance at the witness holes.</summary>
        public static MagazineEstimate Estimate(int rounds, int capacity)
        {
            if (rounds <= 0) return MagazineEstimate.Empty;
            if (rounds >= capacity) return MagazineEstimate.Full;

            float fraction = (float)rounds / capacity;
            if (fraction <= 0.15f) return MagazineEstimate.NearlyEmpty;
            if (fraction < 0.4f) return MagazineEstimate.LessThanHalf;
            if (fraction <= 0.6f) return MagazineEstimate.AboutHalf;
            return fraction < 0.85f ? MagazineEstimate.MoreThanHalf : MagazineEstimate.NearlyFull;
        }
    }
}
