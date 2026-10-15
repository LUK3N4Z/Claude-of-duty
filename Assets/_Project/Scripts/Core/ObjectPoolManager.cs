using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace FPS.Core
{
    /// <summary>
    /// Scene-scoped, pre-warmed component pool for casings, projectiles, decals, dropped magazines and audio
    /// emitters. Idle instances are parked under an inactive root, so spawn and release are a single re-parent
    /// and prewarmed instances do not run Awake until first use. Steady-state spawn/release allocates nothing.
    /// Pooled prefabs reset their state through <see cref="IPoolable"/>.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class ObjectPoolManager : MonoBehaviour
    {
        public const int DefaultMaxSize = 64;

        [Serializable]
        private struct PoolConfig
        {
            public Component prefab;
            [Min(0)] public int prewarm;
            [Tooltip("Idle instances kept beyond this count are destroyed on release.")]
            [Min(1)] public int maxSize;
        }

        [Tooltip("Pools created and filled on Awake. Unlisted prefabs get a pool lazily on first Spawn.")]
        [SerializeField] private PoolConfig[] prewarmedPools = Array.Empty<PoolConfig>();

        private static ObjectPoolManager instance;

        private readonly Dictionary<GameObject, Pool> pools = new Dictionary<GameObject, Pool>();
        private readonly Dictionary<GameObject, Handle> handles = new Dictionary<GameObject, Handle>(256);
        private readonly List<TimedRelease> timedReleases = new List<TimedRelease>(128);
        private readonly List<Component> prewarmBuffer = new List<Component>(64);
        private Transform inactiveRoot;

        /// <summary>The scene's pool manager; created on first access if the scene has none. Runtime only.</summary>
        public static ObjectPoolManager Instance
        {
            get
            {
                if (instance == null)
                {
                    if (!Application.isPlaying)
                        throw new InvalidOperationException($"{nameof(ObjectPoolManager)} is only available in play mode.");
                    new GameObject("[" + nameof(ObjectPoolManager) + "]").AddComponent<ObjectPoolManager>();
                }

                return instance;
            }
        }

        /// <summary>
        /// Takes an instance of <paramref name="prefab"/> from its pool (creating the pool if needed), places it and
        /// activates it. A positive <paramref name="lifetime"/> releases it automatically after that many seconds.
        /// </summary>
        public T Spawn<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null, float lifetime = 0f)
            where T : Component
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            Pool pool = GetOrCreatePool(prefab, DefaultMaxSize);
            Component item = pool.Items.Get();
            Handle handle = handles[item.gameObject];

            handle.Transform.SetPositionAndRotation(position, rotation);
            handle.Transform.SetParent(parent, true); // leaving the inactive root activates it: Awake/OnEnable run here
            handle.Active = true;
            handle.SpawnId++;

            IPoolable[] callbacks = handle.Callbacks;
            for (int i = 0; i < callbacks.Length; i++) callbacks[i].OnSpawned();

            if (lifetime > 0f) timedReleases.Add(new TimedRelease(handle, Time.time + lifetime));
            return item as T ?? item.GetComponent<T>();
        }

        public void Release(Component instance)
        {
            if (instance != null) Release(instance.gameObject);
        }

        /// <summary>
        /// Returns a spawned instance to its pool. Releasing twice is a no-op. Objects that did not come from this
        /// manager are destroyed, so callers can release without knowing how an object was created.
        /// </summary>
        public void Release(GameObject instance)
        {
            if (instance == null) return;
            if (!handles.TryGetValue(instance, out Handle handle))
            {
                Destroy(instance);
                return;
            }

            if (!handle.Active) return;
            handle.Active = false;

            IPoolable[] callbacks = handle.Callbacks;
            for (int i = 0; i < callbacks.Length; i++) callbacks[i].OnDespawned();

            handle.Transform.SetParent(inactiveRoot, false);
            handle.Owner.Items.Release(handle.Item);
        }

        /// <summary>Ensures the pool for <paramref name="prefab"/> holds at least <paramref name="count"/> idle instances.</summary>
        public void Prewarm(Component prefab, int count, int maxSize = DefaultMaxSize)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            Pool pool = GetOrCreatePool(prefab, maxSize);
            count = Mathf.Min(count, pool.MaxSize);
            if (pool.Items.CountInactive >= count) return;

            // Draining existing idle items first keeps the final idle count exactly `count`.
            for (int i = 0; i < count; i++) prewarmBuffer.Add(pool.Items.Get());
            for (int i = 0; i < prewarmBuffer.Count; i++) pool.Items.Release(prewarmBuffer[i]);
            prewarmBuffer.Clear();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogError($"Duplicate {nameof(ObjectPoolManager)} on '{name}' removed; use one per scene.", this);
                Destroy(this);
                return;
            }

            instance = this;

            var root = new GameObject("Inactive");
            root.SetActive(false);
            inactiveRoot = root.transform;
            inactiveRoot.SetParent(transform, false);

            foreach (PoolConfig config in prewarmedPools)
            {
                if (config.prefab != null)
                    Prewarm(config.prefab, config.prewarm, config.maxSize > 0 ? config.maxSize : DefaultMaxSize);
            }
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            if (timedReleases.Count == 0) return;

            // ponytail: linear scan of pending timed releases; switch to a min-heap if counts reach the thousands.
            float now = Time.time;
            for (int i = timedReleases.Count - 1; i >= 0; i--)
            {
                TimedRelease entry = timedReleases[i];
                if (now < entry.ReleaseTime) continue;

                int last = timedReleases.Count - 1;
                timedReleases[i] = timedReleases[last];
                timedReleases.RemoveAt(last);

                Handle handle = entry.Handle;
                if (handle.GameObject == null)
                    handles.Remove(handle.GameObject); // destroyed with its parent while spawned; drop bookkeeping
                else if (handle.Active && handle.SpawnId == entry.SpawnId)
                    Release(handle.GameObject); // SpawnId guards against releasing a later reuse of the same instance
            }
        }

        private Pool GetOrCreatePool(Component prefab, int maxSize)
        {
            GameObject key = prefab.gameObject;
            if (pools.TryGetValue(key, out Pool pool)) return pool;

            pool = new Pool(this, prefab, Mathf.Max(1, maxSize));
            pools.Add(key, pool);
            return pool;
        }

        private Component CreateInstance(Pool pool)
        {
            Component item = Instantiate(pool.Prefab, inactiveRoot);
            GameObject go = item.gameObject;
            go.name = pool.Name;
            if (!go.activeSelf) go.SetActive(true); // still inactive in hierarchy under the inactive root

            handles.Add(go, new Handle(pool, item, go.GetComponentsInChildren<IPoolable>(true)));
            return item;
        }

        private void DestroyInstance(Component item)
        {
            if (item == null) return; // already destroyed with its scene
            handles.Remove(item.gameObject);
            Destroy(item.gameObject);
        }

        private sealed class Pool
        {
            public readonly Component Prefab;
            public readonly string Name;
            public readonly int MaxSize;
            public readonly ObjectPool<Component> Items;

            public Pool(ObjectPoolManager owner, Component prefab, int maxSize)
            {
                Prefab = prefab;
                Name = prefab.name;
                MaxSize = maxSize;
                Items = new ObjectPool<Component>(
                    createFunc: () => owner.CreateInstance(this),
                    actionOnDestroy: owner.DestroyInstance,
                    collectionCheck: false, // Handle.Active already guards double release in O(1)
                    defaultCapacity: Mathf.Min(maxSize, 16),
                    maxSize: maxSize);
            }
        }

        private sealed class Handle
        {
            public readonly Pool Owner;
            public readonly Component Item;
            public readonly GameObject GameObject;
            public readonly Transform Transform;
            public readonly IPoolable[] Callbacks;
            public int SpawnId;
            public bool Active;

            public Handle(Pool owner, Component item, IPoolable[] callbacks)
            {
                Owner = owner;
                Item = item;
                GameObject = item.gameObject;
                Transform = item.transform;
                Callbacks = callbacks;
            }
        }

        private readonly struct TimedRelease
        {
            public readonly Handle Handle;
            public readonly int SpawnId;
            public readonly float ReleaseTime;

            public TimedRelease(Handle handle, float releaseTime)
            {
                Handle = handle;
                SpawnId = handle.SpawnId;
                ReleaseTime = releaseTime;
            }
        }
    }
}
