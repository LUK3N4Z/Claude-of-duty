using FPS.Ballistics;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;

namespace FPS.AI
{
    /// <summary>A fallen soldier's rifle: Interact strips its loaded magazines into the user's matching weapons.</summary>
    [DisallowMultipleComponent]
    public sealed class DroppedWeapon : MonoBehaviour, IInteractable
    {
        [SerializeField] private string prompt = "Take magazines";

        private CaliberDataSO caliber;
        private int[] magazines = new int[0];

        public string Prompt => prompt;

        /// <summary>Loaded magazines still on the rifle.</summary>
        public int Remaining
        {
            get
            {
                int count = 0;
                for (int i = 0; i < magazines.Length; i++) count += magazines[i] > 0 ? 1 : 0;
                return count;
            }
        }

        public void Configure(CaliberDataSO magazineCaliber, int[] loadedMagazines)
        {
            caliber = magazineCaliber;
            magazines = loadedMagazines;
        }

        public bool CanInteract(GameObject user) => Remaining > 0 && user.TryGetComponent(out RealisticWeaponController _);

        public void Interact(GameObject user)
        {
            if (CanInteract(user)) user.GetComponent<RealisticWeaponController>().TakeMagazines(caliber, magazines);
        }
    }
}
