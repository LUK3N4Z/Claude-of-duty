using FPS.Core;
using UnityEngine;

namespace FPS.Locomotion
{
    /// <summary>Finds the <see cref="IInteractable"/> the player is looking at and uses it on Interact.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerRig))]
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField] private float reach = 2.2f;

        private InputReader input;
        private Transform eye;
        private Component focusedComponent;

        /// <summary>The interactable in reach and in view, or null.</summary>
        public IInteractable Focused { get; private set; }

        private void Awake()
        {
            var rig = GetComponent<PlayerRig>();
            input = rig.Input;
            eye = rig.PlayerCamera.transform;
        }

        private void OnEnable()
        {
            if (input != null) input.InteractPressed += Interact;
        }

        private void OnDisable()
        {
            if (input != null) input.InteractPressed -= Interact;
            Focused = null;
        }

        private void Update()
        {
            Focused = null;
            focusedComponent = null;
            int mask = GameLayers.EnvironmentMask | (1 << GameLayers.Interactable);
            if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Collide)) return;
            if (hit.collider.gameObject.layer != GameLayers.Interactable) return; // a wall is in the way

            if (hit.collider.TryGetComponent(out IInteractable interactable) && interactable.CanInteract(gameObject))
            {
                Focused = interactable;
                focusedComponent = interactable as Component;
            }
        }

        /// <summary>Uses the focused interactable, if any.</summary>
        public void Interact()
        {
            if (Focused != null && focusedComponent != null) Focused.Interact(gameObject);
        }
    }
}
