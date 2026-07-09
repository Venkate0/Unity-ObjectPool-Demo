// ─────────────────────────────────────────────────────────────────────────────
// PoolManager.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. WHY A SINGLETON?
//    Every spawner and shooter in the scene needs the *same* pool for a
//    given prefab — two pools for one prefab defeats the purpose. A single
//    scene-level owner keyed by prefab guarantees that. The singleton is
//    scene-scoped (no DontDestroyOnLoad) because pooled instances are scene
//    objects; carrying the manager across scenes while its contents are
//    destroyed would leave it holding dead references.
//
// 2. WHY ARE POOLS STORED AS ObjectPool<PooledObject>?
//    C# generics are invariant: ObjectPool<EnemyController> and
//    ObjectPool<ProjectileController> share no common base except via
//    IPoolInfo. Storing pools at the PooledObject level lets one dictionary
//    hold every pool while Spawn<T> gives callers back their concrete type
//    with a single safe downcast (the pool for prefab X only ever contains
//    clones of X).
//
// 3. WHY KEY BY PREFAB REFERENCE?
//    Keying by System.Type breaks the moment you have two enemy prefabs
//    using the same EnemyController script. The prefab asset reference is
//    the true identity of "what gets cloned".
//
// 4. WHY PER-POOL CONTAINER TRANSFORMS?
//    Purely hierarchy hygiene: 60 pooled objects under "Pool_Enemy" and
//    "Pool_Projectile" nodes instead of loose at scene root. Zero gameplay
//    effect, big readability win when debugging in the editor.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ObjectPoolDemo.Pooling
{
    /// <summary>
    /// Scene-scoped singleton that owns every <see cref="ObjectPool{T}"/>.
    /// Create pools explicitly with <see cref="GetOrCreatePool"/> (preferred,
    /// so sizes are deliberate) or implicitly via <see cref="Spawn{T}"/>.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class PoolManager : MonoBehaviour
    {
        // Runs before default-time scripts so pools configured in the
        // Inspector exist by the time other Start() methods ask for them.
        private const int ExecutionOrder = -100;

        // Fallback sizes used only when Spawn() is called for a prefab no one
        // pre-configured. Named constants rather than magic numbers; the
        // warning in GetOrCreatePool nudges toward explicit configuration.
        private const int DefaultInitialSize = 8;
        private const int DefaultMaxSize = 64;

        /// <summary>The active scene's pool manager, set in Awake.</summary>
        public static PoolManager Instance { get; private set; }

        [Serializable]
        private class PoolConfig
        {
            [Tooltip("Prefab this pool clones. Must derive from PooledObject.")]
            public PooledObject prefab;

            [Tooltip("Instances created up front during Awake (pre-warm).")]
            [Min(0)] public int initialSize = 10;

            [Tooltip("Hard cap. Spawn returns null with a warning past this.")]
            [Min(1)] public int maxSize = 50;
        }

        [Header("Pre-configured Pools")]
        [Tooltip("Pools built during Awake. Spawners may also create pools at runtime.")]
        [SerializeField] private PoolConfig[] _preconfiguredPools = Array.Empty<PoolConfig>();

        private readonly Dictionary<PooledObject, ObjectPool<PooledObject>> _pools =
            new Dictionary<PooledObject, ObjectPool<PooledObject>>();

        private readonly List<IPoolInfo> _poolInfos = new List<IPoolInfo>();

        /// <summary>
        /// Read-only view of every pool's stats, in creation order. Consumed
        /// by <c>PoolDebugUI</c>.
        /// </summary>
        public IReadOnlyList<IPoolInfo> AllPools => _poolInfos;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    $"[PoolManager] Duplicate PoolManager on '{name}' — destroying it. " +
                    $"The scene should contain exactly one (found on '{Instance.name}').", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            foreach (PoolConfig config in _preconfiguredPools)
            {
                if (config.prefab == null)
                {
                    Debug.LogWarning("[PoolManager] A pre-configured pool has no prefab assigned. Skipping.", this);
                    continue;
                }

                GetOrCreatePool(config.prefab, config.initialSize, config.maxSize);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Returns the pool for <paramref name="prefab"/>, creating and
        /// pre-warming it if it does not exist yet. If the pool already
        /// exists, the size arguments are ignored (first configuration wins).
        /// </summary>
        /// <param name="prefab">Prefab identity of the pool.</param>
        /// <param name="initialSize">Pre-warm count for a newly created pool.</param>
        /// <param name="maxSize">Hard cap for a newly created pool.</param>
        /// <returns>The (possibly pre-existing) pool for the prefab.</returns>
        public ObjectPool<PooledObject> GetOrCreatePool(PooledObject prefab, int initialSize, int maxSize)
        {
            if (prefab == null)
            {
                Debug.LogError("[PoolManager] GetOrCreatePool called with a null prefab.", this);
                return null;
            }

            if (_pools.TryGetValue(prefab, out ObjectPool<PooledObject> existing))
            {
                return existing;
            }

            // Container node purely for hierarchy readability (decision #4).
            var container = new GameObject($"Pool_{prefab.name}").transform;
            container.SetParent(transform, false);

            var pool = new ObjectPool<PooledObject>(prefab, initialSize, maxSize, container);
            _pools.Add(prefab, pool);
            _poolInfos.Add(pool);
            return pool;
        }

        /// <summary>
        /// Spawns an instance of <paramref name="prefab"/> from its pool at
        /// the given pose. Creates the pool with default sizes (and a
        /// warning) if nothing configured it first.
        /// </summary>
        /// <typeparam name="T">Concrete PooledObject type of the prefab.</typeparam>
        /// <param name="prefab">Prefab to spawn a pooled clone of.</param>
        /// <param name="position">World spawn position.</param>
        /// <param name="rotation">World spawn rotation.</param>
        /// <returns>
        /// The spawned instance, or <c>null</c> if the pool is at its hard
        /// cap (a warning is logged by the pool).
        /// </returns>
        public T Spawn<T>(T prefab, Vector3 position, Quaternion rotation) where T : PooledObject
        {
            if (prefab == null)
            {
                Debug.LogError("[PoolManager] Spawn called with a null prefab.", this);
                return null;
            }

            if (!_pools.ContainsKey(prefab))
            {
                Debug.LogWarning(
                    $"[PoolManager] No pool was configured for '{prefab.name}'. Creating one with default " +
                    $"sizes (initial {DefaultInitialSize}, max {DefaultMaxSize}). Prefer configuring it " +
                    "explicitly on the PoolManager or via GetOrCreatePool.", this);
            }

            ObjectPool<PooledObject> pool = GetOrCreatePool(prefab, DefaultInitialSize, DefaultMaxSize);
            PooledObject instance = pool.Get(position, rotation);

            // Safe: the pool keyed by this prefab only ever contains clones
            // of it, so every instance is a T. Null (cap reached) passes
            // through the cast unchanged.
            return (T)instance;
        }

        /// <summary>
        /// Returns a spawned instance to its owning pool. Equivalent to
        /// calling <see cref="PooledObject.ReturnToPool"/> on it directly.
        /// </summary>
        /// <param name="instance">The active pooled instance to despawn.</param>
        public void Despawn(PooledObject instance)
        {
            if (instance == null)
            {
                return;
            }

            instance.ReturnToPool();
        }

        /// <summary>
        /// Despawns every active object in every pool. Used for full game
        /// resets (the R key in the demo).
        /// </summary>
        public void ReturnAllPools()
        {
            for (int i = 0; i < _poolInfos.Count; i++)
            {
                _poolInfos[i].ReturnAll();
            }
        }
    }
}
