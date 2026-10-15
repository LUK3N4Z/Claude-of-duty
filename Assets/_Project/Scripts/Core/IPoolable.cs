namespace FPS.Core
{
    /// <summary>
    /// Pool lifecycle callbacks for <see cref="ObjectPoolManager"/>. Implement on any component of a pooled
    /// prefab (root or children) to reset state instead of relying on Awake/Start, which run only once.
    /// </summary>
    public interface IPoolable
    {
        /// <summary>Called after the instance is positioned, parented and active.</summary>
        void OnSpawned();

        /// <summary>Called while the instance is still active, right before it returns to the pool.</summary>
        void OnDespawned();
    }
}
