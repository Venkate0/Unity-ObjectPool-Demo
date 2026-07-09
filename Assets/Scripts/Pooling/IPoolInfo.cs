// ─────────────────────────────────────────────────────────────────────────────
// IPoolInfo.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. WHY A NON-GENERIC "STATS" INTERFACE?
//    ObjectPool<T> is generic, so two pools of different T have no common
//    base type. The PoolManager and the debug overlay (PoolDebugUI) need to
//    iterate over *all* pools regardless of T to display stats and to call
//    ReturnAll() on a wave reset. Extracting the type-agnostic surface into
//    IPoolInfo lets heterogeneous pools live in one List<IPoolInfo> without
//    boxing, reflection, or casting.
//
// 2. WHY EXPOSE READ-ONLY STATS INSTEAD OF THE COLLECTIONS?
//    Handing out the underlying Stack/HashSet would let callers corrupt pool
//    invariants (e.g. pushing an active object). Counts are all the outside
//    world legitimately needs.
// ─────────────────────────────────────────────────────────────────────────────

namespace ObjectPoolDemo.Pooling
{
    /// <summary>
    /// Type-agnostic view of a pool: identity, live statistics, and bulk
    /// return. Used by <see cref="PoolManager"/> and the debug overlay to
    /// treat pools of different element types uniformly.
    /// </summary>
    public interface IPoolInfo
    {
        /// <summary>Display name of the pool (the source prefab's name).</summary>
        string PoolName { get; }

        /// <summary>Number of objects currently spawned and in use in the scene.</summary>
        int ActiveCount { get; }

        /// <summary>Number of objects sitting deactivated in the pool, ready to spawn.</summary>
        int AvailableCount { get; }

        /// <summary>Total objects this pool has ever created (Active + Available).</summary>
        int TotalCount { get; }

        /// <summary>The hard cap this pool will never grow beyond.</summary>
        int MaxSize { get; }

        /// <summary>
        /// Despawns every active object back into the pool in one call.
        /// Used for wave resets and scene teardown.
        /// </summary>
        void ReturnAll();
    }
}
