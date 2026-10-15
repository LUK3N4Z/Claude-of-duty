using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.Core
{
    /// <summary>Handler for <see cref="EventBus{T}"/>. Events arrive by readonly reference, so large structs never copy.</summary>
    public delegate void BusHandler<T>(in T evt) where T : struct;

    /// <summary>
    /// Typed, static, main-thread event bus. Raising never allocates; subscribing allocates one delegate
    /// combine, so subscribe in OnEnable and unsubscribe in OnDisable, never per frame. Handlers added or
    /// removed during a raise take effect from the next raise.
    /// </summary>
    public static class EventBus<T> where T : struct
    {
        private static BusHandler<T> handlers;

        static EventBus() => EventBus.RegisterReset(() => handlers = null);

        public static bool HasSubscribers => handlers != null;

        public static void Subscribe(BusHandler<T> handler) => handlers += handler;

        public static void Unsubscribe(BusHandler<T> handler) => handlers -= handler;

        // ponytail: a throwing handler stops later handlers for this raise (multicast semantics); isolate per
        // handler with try/catch only if a subsystem ever needs to survive another's exception.
        public static void Raise(in T evt) => handlers?.Invoke(in evt);
    }

    /// <summary>Global control over every <see cref="EventBus{T}"/> instantiation.</summary>
    public static class EventBus
    {
        private static readonly List<Action> resets = new List<Action>(64);

        internal static void RegisterReset(Action reset) => resets.Add(reset);

        /// <summary>
        /// Drops all subscribers on every bus. Runs automatically before each play session so stale handlers
        /// cannot survive when domain reload is disabled.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearAll()
        {
            for (int i = 0; i < resets.Count; i++) resets[i]();
        }
    }
}
