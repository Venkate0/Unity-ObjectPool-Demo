// ─────────────────────────────────────────────────────────────────────────────
// EnemySpawner.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. COROUTINE WAVE LOOP INSTEAD OF Update + TIMER FIELDS.
//    The wave sequence is inherently sequential state — "spawn N, wait until
//    cleared, breathe, escalate, repeat". As a coroutine that reads top to
//    bottom; as an Update state machine it becomes four timer floats and an
//    enum. Coroutines are the right tool for scripted sequences.
//
// 2. WAVE-CLEARED DETECTION VIA pool.ActiveCount.
//    The pool already knows exactly how many enemies are alive — no separate
//    "aliveEnemies" counter to keep in sync, no per-enemy death events
//    required for the demo. This doubles as a showcase of why pool stats
//    properties are useful beyond debug UI.
//
// 3. CACHED WaitForSeconds INSTANCES.
//    "new WaitForSeconds(...)" allocates. Inside a loop that runs for the
//    whole session, caching the two wait objects keeps the spawner
//    allocation-free — consistent with the pooling theme of this project.
//
// 4. SPAWN ON THE CIRCLE'S EDGE, NOT INSIDE IT.
//    insideUnitCircle.normalized puts enemies on the ring at spawnRadius,
//    so waves visibly close in from the perimeter instead of popping into
//    existence next to the player. The Gizmo draws that same radius so the
//    designer sees exactly where enemies will appear.
//
// 5. NULL FROM Spawn() IS TOLERATED, NOT TREATED AS AN ERROR.
//    When the enemy pool hits its hard cap, Spawn returns null and the pool
//    logs a warning. The spawner simply skips that enemy — the wave is a
//    little smaller, the game keeps running. Graceful degradation over
//    crash.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections;
using ObjectPoolDemo.Pooling;
using UnityEngine;

namespace ObjectPoolDemo.Gameplay
{
    /// <summary>
    /// Spawns escalating waves of pooled enemies on a ring around itself.
    /// Wave N spawns <c>base + (N-1) * increment</c> enemies; the next wave
    /// starts once every enemy from the current one has despawned.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [Header("Enemy Pool")]
        [Tooltip("Enemy prefab this spawner draws from the pool.")]
        [SerializeField] private EnemyController _enemyPrefab;

        [Tooltip("Enemies pre-warmed at startup. Size this to the largest wave you expect early on.")]
        [SerializeField] private int _initialPoolSize = 20;

        [Tooltip("Hard cap on total enemy instances. Spawns beyond this are skipped with a warning.")]
        [SerializeField] private int _maxPoolSize = 60;

        [Header("Waves")]
        [Tooltip("Enemies in wave 1.")]
        [SerializeField] private int _baseEnemiesPerWave = 5;

        [Tooltip("Extra enemies added to each subsequent wave.")]
        [SerializeField] private int _enemiesAddedPerWave = 3;

        [Tooltip("Breather in seconds after a wave is cleared before the next begins.")]
        [SerializeField] private float _timeBetweenWaves = 4f;

        [Tooltip("Stagger in seconds between individual enemy spawns within a wave.")]
        [SerializeField] private float _timeBetweenSpawns = 0.25f;

        [Header("Spawn Area")]
        [Tooltip("Enemies spawn on the edge of this ring, centered on the spawner.")]
        [SerializeField] private float _spawnRadius = 15f;

        [Header("Gizmo")]
        [Tooltip("Color of the spawn-radius ring drawn in the Scene view.")]
        [SerializeField] private Color _gizmoColor = new Color(1f, 0.6f, 0f, 1f);

        /// <summary>The wave currently spawning or being fought (1-based). Zero before the first wave.</summary>
        public int CurrentWave { get; private set; }

        private ObjectPool<PooledObject> _enemyPool;
        private Coroutine _waveRoutine;
        private WaitForSeconds _spawnStaggerWait;
        private WaitForSeconds _betweenWavesWait;

        private void Start()
        {
            if (_enemyPrefab == null)
            {
                Debug.LogError("[EnemySpawner] No enemy prefab assigned — spawner disabled.", this);
                enabled = false;
                return;
            }

            // Cached waits — design decision #3.
            _spawnStaggerWait = new WaitForSeconds(_timeBetweenSpawns);
            _betweenWavesWait = new WaitForSeconds(_timeBetweenWaves);

            _enemyPool = PoolManager.Instance.GetOrCreatePool(_enemyPrefab, _initialPoolSize, _maxPoolSize);

            BeginWaves();
        }

        /// <summary>
        /// Stops the current wave sequence, returns every enemy to the pool,
        /// and restarts from wave 1. Called by the player's reset key.
        /// </summary>
        public void ResetWaves()
        {
            if (_waveRoutine != null)
            {
                StopCoroutine(_waveRoutine);
                _waveRoutine = null;
            }

            // ReturnAll is idempotent: enemies already returned by
            // PoolManager.ReturnAllPools() are simply skipped.
            _enemyPool.ReturnAll();

            BeginWaves();
        }

        private void BeginWaves()
        {
            CurrentWave = 0;
            _waveRoutine = StartCoroutine(WaveLoop());
        }

        /// <summary>
        /// The full wave sequence: spawn the wave with staggered timing, wait
        /// until the player clears it, breathe, escalate. Runs forever.
        /// </summary>
        private IEnumerator WaveLoop()
        {
            while (true)
            {
                CurrentWave++;
                int enemyCount = _baseEnemiesPerWave + (CurrentWave - 1) * _enemiesAddedPerWave;

                for (int i = 0; i < enemyCount; i++)
                {
                    SpawnEnemy();
                    yield return _spawnStaggerWait;
                }

                // Wave-cleared detection via pool stats — design decision #2.
                while (_enemyPool.ActiveCount > 0)
                {
                    yield return null;
                }

                yield return _betweenWavesWait;
            }
        }

        /// <summary>
        /// Spawns one enemy on the ring's edge, facing the spawner's center.
        /// Silently skips the spawn if the pool is at its hard cap (the pool
        /// logs the warning).
        /// </summary>
        private void SpawnEnemy()
        {
            Vector2 onCircle = Random.insideUnitCircle.normalized;
            if (onCircle.sqrMagnitude < Mathf.Epsilon)
            {
                // insideUnitCircle can (rarely) return exactly zero, which
                // normalizes to zero — fall back to a fixed direction.
                onCircle = Vector2.right;
            }

            Vector3 spawnPosition = transform.position + new Vector3(onCircle.x, 0f, onCircle.y) * _spawnRadius;

            // Face inward so enemies immediately look like they're attacking.
            Vector3 inward = transform.position - spawnPosition;
            inward.y = 0f;
            Quaternion spawnRotation = Quaternion.LookRotation(inward.normalized, Vector3.up);

            PoolManager.Instance.Spawn(_enemyPrefab, spawnPosition, spawnRotation);
        }

        /// <summary>
        /// Draws the spawn ring in the Scene view so the play area is visible
        /// while level-designing, even with the spawner not selected.
        /// </summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = _gizmoColor;
            Gizmos.DrawWireSphere(transform.position, _spawnRadius);
        }
    }
}
