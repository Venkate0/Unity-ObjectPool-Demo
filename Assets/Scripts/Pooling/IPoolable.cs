// ─────────────────────────────────────────────────────────────────────────────
// IPoolable.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. WHY AN INTERFACE INSTEAD OF ONLY A BASE CLASS?
//    The generic constraint on ObjectPool<T> is "T : MonoBehaviour, IPoolable".
//    Constraining against an *interface* (rather than the PooledObject base
//    class) keeps the pool usable by teams that already have their own
//    MonoBehaviour hierarchy and cannot re-parent everything under
//    PooledObject. The PooledObject base class (see PooledObject.cs) is the
//    convenient default implementation, not a hard requirement.
//
// 2. WHY OnSpawn/OnDespawn INSTEAD OF OnEnable/OnDisable?
//    Unity's OnEnable/OnDisable fire for reasons unrelated to pooling
//    (scene load, editor toggling, parent deactivation). A pooled object
//    needs a signal that means exactly "you just left the pool" /
//    "you are about to re-enter the pool" so it can reset health, timers,
//    velocities, and visual state deterministically. Mixing that logic into
//    OnEnable is a classic source of pooling bugs.
// ─────────────────────────────────────────────────────────────────────────────

namespace ObjectPoolDemo.Pooling
{
    /// <summary>
    /// Contract for any component that can live inside an <see cref="ObjectPool{T}"/>.
    /// Implementors reset their state in <see cref="OnSpawn"/> so a recycled
    /// instance is indistinguishable from a freshly instantiated one.
    /// </summary>
    public interface IPoolable
    {
        /// <summary>
        /// Called by the pool immediately after the object is activated and
        /// positioned. Reset all runtime state (health, timers, velocity,
        /// visual effects) here.
        /// </summary>
        void OnSpawn();

        /// <summary>
        /// Called by the pool just before the object is deactivated and
        /// stored. Stop coroutines, clear references to scene objects, and
        /// restore any modified visuals here.
        /// </summary>
        void OnDespawn();
    }
}
