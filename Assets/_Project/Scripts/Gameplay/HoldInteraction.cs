using System;
using FPS.Core;
using FPS.Locomotion;
using UnityEngine;

namespace FPS.Gameplay
{
    /// <summary>
    /// A hold-to-use objective (plant, defuse, download): the player presses Interact on it and keeps holding while
    /// looking at it within reach; letting go or looking away loses the progress. Soldiers progress it through
    /// <see cref="Work"/> while their brain reports they are working the objective. Needs a collider on the
    /// Interactable layer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoldInteraction : MonoBehaviour, IInteractable
    {
        [SerializeField] private string prompt = "Hold to use";
        [SerializeField, Min(0.1f)] private float duration = 5f;

        private GameObject user;
        private Interactor interactor;
        private InputReader input;
        private float progress, lastWork;

        /// <summary>Who may use it right now (the mode's rules); null allows everyone.</summary>
        public Func<GameObject, bool> Allowed { get; set; }

        /// <summary>Raised with the user when the hold completes.</summary>
        public event Action<GameObject> Completed;

        public string Prompt => prompt;
        public float Duration => duration;
        public float Progress01 => progress / duration;
        public GameObject User => user;

        public void Configure(string promptText, float seconds)
        {
            prompt = promptText;
            duration = Mathf.Max(0.1f, seconds);
        }

        public bool CanInteract(GameObject candidate) => isActiveAndEnabled && (Allowed == null || Allowed(candidate));

        public void Interact(GameObject candidate)
        {
            if (!CanInteract(candidate) || !candidate.TryGetComponent(out PlayerRig rig) || !candidate.TryGetComponent(out Interactor eyes)) return;
            if (user != candidate) progress = 0f;
            user = candidate;
            interactor = eyes;
            input = rig.Input;
        }

        /// <summary>A soldier (or script) works it for <paramref name="dt"/> seconds. True on the frame it completes.</summary>
        public bool Work(GameObject worker, float dt)
        {
            if (!CanInteract(worker))
            {
                if (user == worker) Cancel();
                return false;
            }

            if (user != worker)
            {
                user = worker;
                interactor = null;
                input = null;
                progress = 0f;
            }

            lastWork = Time.time;
            return Advance(dt);
        }

        public void Cancel()
        {
            user = null;
            interactor = null;
            input = null;
            progress = 0f;
        }

        private void Update()
        {
            if (user == null) return;
            if (input == null)
            {
                if (Time.time - lastWork > 0.25f) Cancel(); // the soldier stopped working it
                return;
            }

            bool holding = input.InteractHeld && ReferenceEquals(interactor.Focused, this) && CanInteract(user);
            if (!holding)
            {
                Cancel();
                return;
            }

            Advance(Time.deltaTime);
        }

        private bool Advance(float dt)
        {
            progress += dt;
            if (progress < duration) return false;

            GameObject finisher = user;
            Cancel();
            Completed?.Invoke(finisher);
            return true;
        }

        private void OnDisable() => Cancel();
    }
}
