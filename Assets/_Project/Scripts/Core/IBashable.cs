using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// Something a body can smash open (a door): the player's context melee (V facing it) or sprinting into it. Implement
    /// on a component on or above the collider that is hit.
    /// </summary>
    public interface IBashable
    {
        /// <summary>
        /// Bash from <paramref name="user"/> along <paramref name="direction"/> with <paramref name="strength"/> (0-1, 1 = a
        /// full sprint). False when it cannot be bashed right now (already swinging, out of reach).
        /// </summary>
        bool Bash(GameObject user, Vector3 direction, float strength);
    }
}
