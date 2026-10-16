using FPS.Weapons;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>A clickable marker on an attachment mount of the weapon laid out on the workbench.</summary>
    [DisallowMultipleComponent]
    public sealed class WorkbenchNode : MonoBehaviour
    {
        public AttachmentSlot Slot { get; private set; }
        public Renderer Marker { get; private set; }

        public void Configure(AttachmentSlot slot, Renderer marker)
        {
            Slot = slot;
            Marker = marker;
        }
    }
}
