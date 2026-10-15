using UnityEngine;

namespace FPS.Core
{
    public enum WeaponActionType
    {
        DryFire,
        FireModeChanged,
        MagazineRemoved,
        MagazineDropped,
        MagazineInserted,
        BoltCycled,
        BoltLockedBack,
        MagazineChecked,
        Holstered,
        Drawn,
    }

    /// <summary>
    /// Raised on <see cref="EventBus{T}"/> for every mechanical weapon action, for audio, HUD and AI hearing.
    /// </summary>
    public struct WeaponActionEvent
    {
        public WeaponActionType Type;
        public Vector3 Position;
        /// <summary>Root of the weapon's owner.</summary>
        public Transform Owner;
        /// <summary>Type-specific payload: new fire-mode index, magazine estimate, rounds in the inserted magazine.</summary>
        public int Value;
    }
}
