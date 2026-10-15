using FPS.Core;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>Interactable resupply: refills the user's spare magazines for every carried weapon.</summary>
    [DisallowMultipleComponent]
    public sealed class AmmoCrate : MonoBehaviour, IInteractable
    {
        [SerializeField] private string prompt = "Resupply ammunition";
        [SerializeField, Min(0f)] private float cooldown = 1f;

        private float readyTime;

        public string Prompt => prompt;

        public bool CanInteract(GameObject user) => Time.time >= readyTime && user.TryGetComponent(out RealisticWeaponController _);

        public void Interact(GameObject user)
        {
            if (!CanInteract(user)) return;
            user.GetComponent<RealisticWeaponController>().RefillAmmunition();
            readyTime = Time.time + cooldown;
        }
    }
}
