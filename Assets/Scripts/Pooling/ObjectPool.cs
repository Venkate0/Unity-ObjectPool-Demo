// ─────────────────────────────────────────────────────────────────────────────
// ObjectPool.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. WHY Stack<T> AND NOT Queue<T> FOR AVAILABLE OBJECTS?
//    A Stack is LIFO: the object we just returned is the first one handed
//    out again. That object's components, transforms, and materials are the
//    most likely to still be warm in the CPU cache, so re-using it is
//    measurably cheaper than cycling through the whole pool the way a FIFO
//    Queue would. LIFO also keeps the "cold" tail of the pool untouched,
//    which makes it obvious in the hierarchy which instances are actually
//    needed (useful when tuning initialSize down). A Queue would only be
//    preferable if we needed fairness/round-robin — pooling does not.
//
// 2. WHY A HashSet<T> FOR ACTIVE OBJECTS?
//    ReturnAll() needs to enumerate active objects, and Return() needs an
//    O(1) "does this pool actually own this active instance?" check to
//    reject double-returns and foreign objects. A HashSet gives both.
//
// 3. WHY A PLAIN C# CLASS AND NOT A MonoBehaviour?
//    The pool has no per-frame behaviour and no scene identity of its own;
//    making it a MonoBehaviour would force a GameObject per pool and invite
//    lifecycle ordering issues. A plain class constructed by PoolManager is
//    simpler to test and reason about. The only Unity thing it needs is a
//    parent Transform for hierarchy tidiness, which is passed in.
//
// 4. WHY PRE-WARM IN THE CONSTRUCTOR?
//    Instantiate() is the expensive call we are hiding. Doing all of them
//    during scene load (constructor time) moves the cost to a moment when a
//    frame hitch is invisible, instead of mid-combat.
//
// 5. WHY DOES Get() RETURN NULL AT THE CAP (INSTEAD OF FORCE-RECYCLING THE
//    OLDEST ACTIVE OBJECT)?
//    Force-recycling silently teleports a live object out from under its
//    systems — an enemy mid-death-animation vanishes, a projectile in flight
//    disappears. Those bugs are brutal to trace. Returning null makes the
//    starvation *visible* (LogWarning) and pushes the decision to the
//    caller, who has the context to decide (skip this spawn, delay it, or
//    raise maxSize). Fail loud beats fail weird.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;

namespace ObjectPoolDemo.Pooling
{
    /// <summary>
    /// A pre-warmed, hard-capped object pool for MonoBehaviours implementing
    /// <see cref="IPoolable"/>. Construct once (typically via
    /// <see cref="PoolManager"/>), then <see cref="Get"/> and
    /// <see cref="Return"/> instead of Instantiate/Destroy.
    /// </summary>
    /// <typeparam name="T">Component type stored in the pool.</typeparam>
    public class ObjectPool<T> : IPoolInfo where T : MonoBehaviour, IPoolable
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly int _maxSize;

        // LIFO storage of deactivated, ready-to-spawn instances. See design
        // decision #1 above for why Stack beats Queue here.
        private readonly Stack<T> _available;

        // Membership set of everything currently checked out. See design
        // decision #2.
        private readonly HashSet<T> _active;

        // Reused by ReturnAll() so wave resets do not allocate a fresh list
        // every time (the whole point of pooling is avoiding garbage).
        private readonly List<T> _returnBuffer;

        /// <inheritdoc />
        public string PoolName => _prefab.name;

        /// <inheritdoc />
        public int ActiveCount => _active.Count;

        /// <inheritdoc />
        public int AvailableCount => _available.Count;

        /// <inheritdoc />
        public int TotalCount => _active.Count + _available.Count;

        /// <inheritdoc />
        public int MaxSize => _maxSize;

        /// <summary>
        /// Creates the pool and pre-warms it by instantiating
        /// <paramref name="initialSize"/> deactivated copies of
        /// <paramref name="prefab"/> immediately.
        /// </summary>
        /// <param name="prefab">Source prefab to clone.</param>
        /// <param name="initialSize">
        /// Instances created up front. Clamped to <paramref name="maxSize"/>.
        /// </param>
        /// <param name="maxSize">
        /// Hard cap on total instances. <see cref="Get"/> returns null (with
        /// a warning) once the cap is reached and nothing is available.
        /// </param>
        /// <param name="parent">
        /// Optional transform the instances are parented under, purely for
        /// hierarchy readability.
        /// </param>
        public ObjectPool(T prefab, int initialSize, int maxSize, Transform parent = null)
        {
            Debug.Assert(prefab != null, "[ObjectPool] prefab must not be null.");
            Debug.Assert(maxSize > 0, "[ObjectPool] maxSize must be at least 1.");

            _prefab = prefab;
            _parent = parent;
            _maxSize = Mathf.Max(1, maxSize);

            if (initialSize > _maxSize)
            {
                Debug.LogWarning(
                    $"[ObjectPool] '{prefab.name}': initialSize ({initialSize}) exceeds maxSize ({_maxSize}). " +
                    $"Clamping to {_maxSize}.");
                initialSize = _maxSize;
            }

            _available = new Stack<T>(_maxSize);
            _active = new HashSet<T>();
            _returnBuffer = new List<T>(_maxSize);

            // Pre-warm: pay the Instantiate cost now, during load, not
            // mid-gameplay. See design decision #4.
            for (int i = 0; i < initialSize; i++)
            {
                _available.Push(CreateInstance());
            }
        }

        /// <summary>
        /// Checks an instance out of the pool, activates it at the given pose,
        /// and calls its <see cref="IPoolable.OnSpawn"/>.
        /// </summary>
        /// <param name="position">World position to spawn at.</param>
        /// <param name="rotation">World rotation to spawn with.</param>
        /// <returns>
        /// The spawned instance, or <c>null</c> if the pool is exhausted and
        /// already at <see cref="MaxSize"/>. Callers must handle null — see
        /// design decision #5 in the header for why we never force-recycle.
        /// </returns>
        public T Get(Vector3 position, Quaternion rotation)
        {
            T instance;

            if (_available.Count > 0)
            {
                instance = _available.Pop();
            }
            else if (TotalCount < _maxSize)
            {
                // Pool under-provisioned but under cap: grow lazily. This is
                // an Instantiate at gameplay time, so the warning nudges the
                // developer to raise initialSize.
                instance = CreateInstance();
                Debug.LogWarning(
                    $"[ObjectPool] '{PoolName}' grew past its pre-warmed size to {TotalCount + 1}. " +
                    "Consider raising initialSize to avoid mid-game Instantiate calls.");
            }
            else
            {
                Debug.LogWarning(
                    $"[ObjectPool] '{PoolName}' is exhausted at its hard cap of {_maxSize} " +
                    "(all instances active). Returning null — the caller should skip or defer this spawn, " +
                    "or maxSize should be raised.");
                return null;
            }

            _active.Add(instance);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.gameObject.SetActive(true);
            instance.OnSpawn();
            return instance;
        }

        /// <summary>
        /// Checks an instance back into the pool: calls its
        /// <see cref="IPoolable.OnDespawn"/>, deactivates it, and stores it
        /// for reuse.
        /// </summary>
        /// <param name="instance">The instance to return.</param>
        /// <returns>
        /// <c>true</c> if the instance was active in this pool and was
        /// returned; <c>false</c> for null, double-returns, or objects this
        /// pool does not own (a warning is logged for the latter).
        /// </returns>
        public bool Return(T instance)
        {
            if (instance == null)
            {
                return false;
            }

            // O(1) ownership + double-return guard. See design decision #2.
            if (!_active.Remove(instance))
            {
                // Not warning on every call: a double-return via
                // PooledObject.ReturnToPool is already suppressed by its
                // IsSpawned guard, so reaching here means direct misuse.
                Debug.LogWarning(
                    $"[ObjectPool] '{instance.name}' was returned to pool '{PoolName}' but is not " +
                    "checked out of it (double return, or wrong pool). Ignoring.", instance);
                return false;
            }

            instance.OnDespawn();
            instance.gameObject.SetActive(false);
            _available.Push(instance);
            return true;
        }

        /// <summary>
        /// Returns every active instance to the pool in one sweep. Ideal for
        /// wave resets, level restarts, and scene transitions.
        /// </summary>
        public void ReturnAll()
        {
            if (_active.Count == 0)
            {
                return;
            }

            // Copy first: Return() mutates _active, and mutating a HashSet
            // while enumerating it throws. The buffer is reused across calls
            // to keep this allocation-free after warm-up.
            _returnBuffer.Clear();
            _returnBuffer.AddRange(_active);

            for (int i = 0; i < _returnBuffer.Count; i++)
            {
                Return(_returnBuffer[i]);
            }

            _returnBuffer.Clear();
        }

        /// <summary>
        /// Instantiates a fresh, deactivated instance and wires its
        /// return-to-pool callback (once, ever — not per spawn).
        /// </summary>
        private T CreateInstance()
        {
            T instance = Object.Instantiate(_prefab, _parent);
            instance.gameObject.SetActive(false);

            // Numbered names make the hierarchy and profiler readable:
            // "Enemy_007" instead of twenty "Enemy(Clone)" entries.
            instance.name = $"{_prefab.name}_{TotalCount + 1:D3}";

            // If the instance uses the PooledObject convenience base class,
            // give it the ability to return itself (e.g. on death). The
            // closure captures 'instance' once at creation time.
            if (instance is PooledObject pooled)
            {
                pooled.SetReturnCallback(() => Return(instance));
            }

            return instance;
        }
    }
}
