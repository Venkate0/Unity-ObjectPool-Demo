// ─────────────────────────────────────────────────────────────────────────────
// EnemyController.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. STATE RESET LIVES IN OnSpawn, NOT Awake/OnEnable.
//    A pooled enemy is re-used dozens of times. Health, lifetime timer, and
//    chase target must reset on every checkout — Awake fires once per
//    instance and OnEnable fires for non-pooling reasons too, so OnSpawn is
//    the single reliable reset point.
//
// 2. TARGET ACQUISITION VIA PlayerController.Instance, ONCE PER SPAWN.
//    FindObjectOfType in Update is a per-frame scene scan and the #1 thing
//    reviewers grep for. The player publishes itself as a static reference
//    in Awake; enemies read it once in OnSpawn and cache the transform.
//
// 3. HIT FLASH USES MaterialPropertyBlock, NOT renderer.material.
//    Accessing renderer.material silently clones the material (one extra
//    material per enemy, breaking batching and leaking until scene unload).
//    A MaterialPropertyBlock overrides the color per-renderer with zero
//    material instances. The shader color property name is serialized
//    ("_Color" for Built-in/Standard, "_BaseColor" for URP Lit) so the same
//    script works in both pipelines.
//
// 4. LIFETIME EXPIRY AS A SAFETY NET.
//    If an enemy somehow gets stuck (pathing bug, player quits moving), it
//    despawns itself after a max lifetime instead of leaking out of the pool
//    forever. Pools amplify leaks: one stuck object is one permanently lost
//    pool slot.
//
// 5. WHY TRANSFORM-BASED CHASE MOVEMENT?
//    The demo isolates the pooling system; physics-driven enemy locomotion
//    would add tuning noise without demonstrating anything about pooling.
//    A collider (for projectile hits) plus direct transform motion keeps
//    the enemy cheap and deterministic.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections;
using ObjectPoolDemo.Pooling;
using UnityEngine;

namespace ObjectPoolDemo.Gameplay
{
    /// <summary>
    /// Pooled chaser enemy: pursues the player on the XZ plane, flashes red
    /// when hit, and returns itself to the pool on death or when its maximum
    /// lifetime expires.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class EnemyController : PooledObject
    {
        [Header("Movement")]
        [Tooltip("Chase speed in units per second.")]
        [SerializeField] private float _moveSpeed = 3.5f;

        [Tooltip("Turn rate in degrees per second while tracking the player.")]
        [SerializeField] private float _turnSpeed = 720f;

        [Tooltip("Enemies stop advancing when closer than this, so they don't clip inside the player.")]
        [SerializeField] private float _stopDistance = 1.2f;

        [Header("Health")]
        [Tooltip("Hits-to-kill at 1 damage per projectile (see ProjectileController damage).")]
        [SerializeField] private float _maxHealth = 3f;

        [Header("Lifetime")]
        [Tooltip("Failsafe: seconds after spawn before the enemy despawns itself even if alive. Prevents pool-slot leaks.")]
        [SerializeField] private float _maxLifetime = 30f;

        [Header("Hit Flash")]
        [Tooltip("Color the enemy flashes when damaged.")]
        [SerializeField] private Color _flashColor = Color.red;

        [Tooltip("Seconds the flash color stays before reverting.")]
        [SerializeField] private float _flashDuration = 0.1f;

        [Tooltip("Shader color property: \"_Color\" for Built-in/Standard, \"_BaseColor\" for URP Lit.")]
        [SerializeField] private string _colorPropertyName = "_Color";

        private float _currentHealth;
        private float _spawnTime;
        private Transform _target;
        private Renderer _renderer;
        private MaterialPropertyBlock _propertyBlock;
        private int _colorPropertyId;
        private Color _baseColor;
        private Coroutine _flashRoutine;
        private WaitForSeconds _flashWait; // Cached: WaitForSeconds allocates, and enemies flash a lot.

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
            _colorPropertyId = Shader.PropertyToID(_colorPropertyName);
            _flashWait = new WaitForSeconds(_flashDuration);

            if (_renderer != null && _renderer.sharedMaterial != null &&
                _renderer.sharedMaterial.HasProperty(_colorPropertyId))
            {
                _baseColor = _renderer.sharedMaterial.GetColor(_colorPropertyId);
            }
            else
            {
                _baseColor = Color.white;
                Debug.LogWarning(
                    $"[EnemyController] '{name}' has no material with property '{_colorPropertyName}'. " +
                    "Hit flash will assume a white base color.", this);
            }
        }

        /// <summary>
        /// Pool checkout: resets health, lifetime timer, visuals, and
        /// re-acquires the chase target. See design decision #1.
        /// </summary>
        public override void OnSpawn()
        {
            base.OnSpawn();

            _currentHealth = _maxHealth;
            _spawnTime = Time.time;
            _target = PlayerController.Instance != null ? PlayerController.Instance.transform : null;
            SetColor(_baseColor);

            if (_target == null)
            {
                Debug.LogWarning("[EnemyController] No PlayerController in scene — enemy will idle in place.", this);
            }
        }

        /// <summary>
        /// Pool check-in: stops the flash coroutine and restores the base
        /// color so the next checkout starts visually clean.
        /// </summary>
        public override void OnDespawn()
        {
            base.OnDespawn();

            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }

            SetColor(_baseColor);
            _target = null; // Never hold scene references while parked in the pool.
        }

        private void Update()
        {
            // Failsafe despawn — design decision #4.
            if (Time.time - _spawnTime >= _maxLifetime)
            {
                ReturnToPool();
                return;
            }

            if (_target == null)
            {
                return;
            }

            ChaseTarget();
        }

        /// <summary>
        /// Moves and turns toward the player on the XZ plane, holding
        /// position inside the stop distance.
        /// </summary>
        private void ChaseTarget()
        {
            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f; // Ground-plane pursuit only.

            float sqrDistance = toTarget.sqrMagnitude;
            if (sqrDistance < Mathf.Epsilon)
            {
                return; // Exactly on top of the target — nothing sensible to do.
            }

            Vector3 direction = toTarget.normalized;

            // Turn smoothly rather than snapping, so a strafing player sees
            // the enemy arc around instead of teleport-rotating.
            Quaternion desired = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, _turnSpeed * Time.deltaTime);

            if (sqrDistance > _stopDistance * _stopDistance)
            {
                transform.position += direction * (_moveSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// Applies damage to this enemy, triggering the hit flash. Kills the
        /// enemy (returns it to the pool) when health reaches zero.
        /// </summary>
        /// <param name="amount">Damage to subtract from current health.</param>
        public void TakeDamage(float amount)
        {
            if (!IsSpawned)
            {
                return; // Stale hit against an already-despawned instance.
            }

            _currentHealth -= amount;

            if (_currentHealth <= 0f)
            {
                ReturnToPool(); // "Death" — the pooled equivalent of Destroy.
                return;
            }

            // Restart the flash from full if we're hit mid-flash.
            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
            }

            _flashRoutine = StartCoroutine(FlashRoutine());
        }

        /// <summary>
        /// Briefly overrides the renderer color with the flash color, then
        /// restores the material's base color.
        /// </summary>
        private IEnumerator FlashRoutine()
        {
            SetColor(_flashColor);
            yield return _flashWait;
            SetColor(_baseColor);
            _flashRoutine = null;
        }

        /// <summary>
        /// Per-renderer color override via MaterialPropertyBlock — no
        /// material instances are created. See design decision #3.
        /// </summary>
        private void SetColor(Color color)
        {
            if (_renderer == null)
            {
                return;
            }

            _propertyBlock.SetColor(_colorPropertyId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
