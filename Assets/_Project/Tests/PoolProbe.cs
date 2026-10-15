using FPS.Core;
using UnityEngine;

namespace FPS.Tests
{
    /// <summary>Test double that counts pool lifecycle callbacks.</summary>
    public sealed class PoolProbe : MonoBehaviour, IPoolable
    {
        public int Spawned { get; private set; }
        public int Despawned { get; private set; }

        public void OnSpawned() => Spawned++;
        public void OnDespawned() => Despawned++;
    }
}
