using FPS.Core;
using UnityEngine;

namespace FPS.Weapons
{
    /// <summary>Interactable locker that swaps the user's tactical grenades between two types (stun and smoke).</summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentLocker : MonoBehaviour, IInteractable
    {
        [SerializeField] private string prompt = "Swap tactical grenades";
        [SerializeField] private GrenadeDataSO first;
        [SerializeField] private GrenadeDataSO second;
        [SerializeField, Min(1)] private int count = 2;

        public string Prompt => prompt;

        public void Configure(GrenadeDataSO firstType, GrenadeDataSO secondType, int carried)
        {
            first = firstType;
            second = secondType;
            count = carried;
        }

        public bool CanInteract(GameObject user) =>
            user.TryGetComponent(out TacticalEquipmentSystem equipment) && !equipment.IsBusy && first != null && second != null;

        public void Interact(GameObject user)
        {
            if (!CanInteract(user)) return;
            var equipment = user.GetComponent<TacticalEquipmentSystem>();
            GrenadeDataSO next = equipment.Grenade(EquipmentSlot.Tactical) == first ? second : first;
            equipment.SetLoadout(EquipmentSlot.Tactical, next, count);
        }
    }
}
