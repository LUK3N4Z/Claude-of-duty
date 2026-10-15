using UnityEngine;

namespace FPS.Core
{
    /// <summary>
    /// Something a player can use by looking at it and pressing Interact. Implement on a component whose collider sits on
    /// the Interactable layer.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Short action text for the HUD prompt, e.g. "Resupply ammunition". Return a cached string.</summary>
        string Prompt { get; }

        bool CanInteract(GameObject user);

        void Interact(GameObject user);
    }
}
