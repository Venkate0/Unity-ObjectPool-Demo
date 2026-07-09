// ─────────────────────────────────────────────────────────────────────────────
// PooledObject.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. WHY A CALLBACK INSTEAD OF A DIRECT POOL REFERENCE?
//    A pooled instance needs a way to say "return me" (e.g. an enemy dies in
//    its own Update). Storing a typed ObjectPool<T> reference here is
//    impossible without making PooledObject generic, which would break Unity
//    serialization and prefab workflows. Instead the pool injects a single
//    System.Action at *creation* time (one closure allocation per instance,
//    ever — not per spawn), and the instance invokes it blindly. The object
//    knows nothing about the pool's type; the pool keeps full control over
//    bookkeeping.
//
// 2. WHY THE IsSpawned GUARD?
//    Double-return is the most common pooling bug: a projectile hits two
//    colliders in the same physics step, both handlers call ReturnToPool(),
//    and the object ends up in the available stack twice — later it gets
//    handed to two owners simultaneously. IsSpawned makes the second call a
//    harmless no-op. The pool ALSO guards with its active-set membership
//    check (defense in depth).
//
// 3. WHY virtual OnSpawn/OnDespawn WITH base-CALL CONTRACT?
//    Subclasses reset their own state but must keep the IsSpawned flag
//    coherent, so overrides are required to call base.OnSpawn()/OnDespawn().
//    This is documented on the methods below.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using UnityEngine;

namespace ObjectPoolDemo.Pooling
{
    /// <summary>
    /// Convenient base class for pooled MonoBehaviours. Handles the
    /// return-to-pool plumbing so subclasses only implement gameplay logic
    /// and state resets in <see cref="OnSpawn"/> / <see cref="OnDespawn"/>.
    /// </summary>
    public abstract class PooledObject : MonoBehaviour, IPoolable
    {
        private Action _returnToPool;

        /// <summary>
        /// True while this instance is spawned (checked out of the pool).
        /// Use this to ignore stale physics callbacks that arrive after the
        /// object has already been returned in the same frame.
        /// </summary>
        public bool IsSpawned { get; private set; }

        /// <summary>
        /// Injected once by <see cref="ObjectPool{T}"/> when the instance is
        /// first created. Internal so gameplay code cannot rewire it.
        /// </summary>
        internal void SetReturnCallback(Action returnToPool)
        {
            _returnToPool = returnToPool;
        }

        /// <summary>
        /// Returns this instance to the pool that owns it. Safe to call more
        /// than once per spawn — subsequent calls are no-ops. If the instance
        /// was placed in the scene by hand (never owned by a pool), it is
        /// destroyed instead, with a warning, so it does not linger.
        /// </summary>
        public void ReturnToPool()
        {
            if (!IsSpawned)
            {
                return; // Already returned this spawn cycle — ignore.
            }

            if (_returnToPool != null)
            {
                _returnToPool();
            }
            else
            {
                Debug.LogWarning(
                    $"[PooledObject] '{name}' has no owning pool (was it placed in the scene manually?). " +
                    "Destroying it instead of pooling.", this);
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Pool lifecycle hook — the object just left the pool.
        /// Overrides MUST call <c>base.OnSpawn()</c> first so
        /// <see cref="IsSpawned"/> stays correct.
        /// </summary>
        public virtual void OnSpawn()
        {
            IsSpawned = true;
        }

        /// <summary>
        /// Pool lifecycle hook — the object is about to re-enter the pool.
        /// Overrides MUST call <c>base.OnDespawn()</c> so
        /// <see cref="IsSpawned"/> stays correct.
        /// </summary>
        public virtual void OnDespawn()
        {
            IsSpawned = false;
        }
    }
}
