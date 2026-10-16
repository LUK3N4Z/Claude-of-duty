using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// An interactable that takes the scroll wheel while Interact is held on it (cracking a door ajar). While it does, the
    /// scroll does not change the walking pace.
    /// </summary>
    public interface IScrollAdjustable
    {
        /// <summary>One scroll step (+1 / -1) from <paramref name="user"/> holding Interact on it.</summary>
        void Scroll(GameObject user, int direction);
    }
}
