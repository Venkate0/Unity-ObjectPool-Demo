// ─────────────────────────────────────────────────────────────────────────────
// ProjectileController.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. KINEMATIC RIGIDBODY + TRIGGER COLLIDER.
//    For OnTriggerEnter to fire, at least one participant needs a Rigidbody.
//    Putting a *kinematic* one on the projectile means: no gravity, no
//    physics forces, we keep exact control of the trajectory via transform
//    movement, and enemies stay rigidbody-free (cheaper — there are far more
//    enemies than the physics engine would like rigidbodies).
//
// 2. LIFETIME EXPIRY IS MANDATORY, NOT OPTIONAL.
//    A projectile that misses everything flies forever. Without expiry it
//    never returns, and the pool drains one slot per miss until Get()
//    starts returning null. Every pooled projectile system needs a max
//    lifetime — this is the pooling equivalent of a memory leak.
//
// 3. THE IsSpawned CHECK IN OnTriggerEnter.
//    If two enemy colliders overlap the projectile in the same physics step,
//    Unity delivers two OnTriggerEnter calls. After the first one returns
//    the projectile to the pool, the second must be ignored — otherwise one
//    bullet damages two enemies and double-returns itself.
//
// 4. TRANSFORM MOVEMENT IN Update, NOT Rigidbody.MovePosition IN FixedUpdate.
//    The projectile is kinematic and purely visual-trajectory; frame-rate
//    smooth motion (Update + deltaTime) looks better for fast bullets, and
//    trigger overlap tests still run in the physics step regardless. At the
//    demo's speeds (20 u/s, ~0.3 u/frame at 60 fps) tunneling is not a
//    concern; a shipping game with faster bullets would raycast instead.
// ─────────────────────────────────────────────────────────────────────────────

using ObjectPoolDemo.Pooling;
using UnityEngine;

namespace ObjectPoolDemo.Gameplay
{
    /// <summary>
    /// Pooled projectile: flies along its forward axis, damages the first
    /// enemy it touches, and returns to the pool on hit or lifetime expiry.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class ProjectileController : PooledObject
    {
        [Header("Motion")]
        [Tooltip("Forward speed in units per second.")]
        [SerializeField] private float _speed = 20f;

        [Header("Lifetime")]
        [Tooltip("Seconds before a projectile that hits nothing despawns itself. Prevents pool drain — see header comment.")]
        [SerializeField] private float _maxLifetime = 3f;

        [Header("Damage")]
        [Tooltip("Damage dealt to an enemy on contact.")]
        [SerializeField] private float _damage = 1f;

        private float _spawnTime;

        private void Awake()
        {
            // Validate the physics setup once, loudly, instead of failing
            // silently at runtime (a non-trigger collider would shove enemies
            // around; a non-kinematic rigidbody would fall under gravity).
            var body = GetComponent<Rigidbody>();
            if (!body.isKinematic)
            {
                Debug.LogWarning(
                    $"[ProjectileController] '{name}' has a non-kinematic Rigidbody. " +
                    "Set it to Is Kinematic — the projectile moves itself.", this);
            }

            var trigger = GetComponent<Collider>();
            if (!trigger.isTrigger)
            {
                Debug.LogWarning(
                    $"[ProjectileController] '{name}' collider is not a trigger. " +
                    "Enable Is Trigger so hits register without physical pushes.", this);
            }
        }

        /// <summary>
        /// Pool checkout: stamps the spawn time so lifetime expiry is
        /// measured per flight, not per instance.
        /// </summary>
        public override void OnSpawn()
        {
            base.OnSpawn();
            _spawnTime = Time.time;
        }

        private void Update()
        {
            // Straight-line flight along the spawn rotation's forward axis.
            transform.position += transform.forward * (_speed * Time.deltaTime);

            // Miss failsafe — design decision #2.
            if (Time.time - _spawnTime >= _maxLifetime)
            {
                ReturnToPool();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Ignore stale contacts after this projectile already returned
            // this frame — design decision #3.
            if (!IsSpawned)
            {
                return;
            }

            // TryGetComponent avoids the GetComponent null-return allocation
            // in the editor and reads cleaner than tag comparison.
            if (!other.TryGetComponent(out EnemyController enemy) || !enemy.IsSpawned)
            {
                return;
            }

            enemy.TakeDamage(_damage);
            ReturnToPool();
        }
    }
}
