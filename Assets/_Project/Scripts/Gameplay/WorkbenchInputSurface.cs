using UnityEngine;
using UnityEngine.EventSystems;

namespace FPS.Gameplay
{
    /// <summary>The transparent full-screen layer behind the workbench panels: drag turns the weapon, click picks a mount.</summary>
    [DisallowMultipleComponent]
    public sealed class WorkbenchInputSurface : MonoBehaviour, IDragHandler, IPointerClickHandler
    {
        private GunsmithWorkbenchController workbench;

        public void Bind(GunsmithWorkbenchController owner) => workbench = owner;

        public void OnDrag(PointerEventData eventData) => workbench.Rotate(eventData.delta);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!eventData.dragging) workbench.Pick(eventData.position);
        }
    }
}
