// ─────────────────────────────────────────────────────────────────────────────
// PlayerController.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. WHY CharacterController INSTEAD OF Rigidbody?
//    Top-down arcade movement wants direct, deterministic control with
//    built-in collision sliding and no physics tuning (drag, friction,
//    freeze-rotation). CharacterController.Move gives exactly that.
//
// 2. WHY A static Instance ON THE PLAYER?
//    Enemies need the player's transform every spawn. FindObjectOfType per
//    spawn is a scene scan; a serialized reference on a pooled *prefab*
//    cannot point at a scene object. A scene-scoped static reference set in
//    Awake is the cheapest correct lookup. It is intentionally NOT a full
//    singleton pattern (no lazy creation, no DontDestroyOnLoad) — just a
//    published reference.
//
// 3. WHY RAYCAST AGAINST A "Ground" LAYER FOR AIMING?
//    Aiming needs "where on the play plane is the mouse?". Raycasting the
//    camera ray against ONLY the Ground layer means enemies, projectiles and
//    props can never hijack the aim point, and the mask check happens inside
//    the physics engine instead of in C#.
//
// 4. WHY IS THE AIM POINT FLATTENED TO THE PLAYER'S HEIGHT?
//    Without flattening, aiming at a point below the capsule's center would
//    pitch the player (and therefore the muzzle) into the floor. Top-down
//    shooters aim in the XZ plane only.
//
// 5. WHY Camera.main CACHED IN Awake?
//    Camera.main performs a tag lookup. Once per session is fine; once per
//    frame in Update is a habit worth showing you don't have.
// ─────────────────────────────────────────────────────────────────────────────

using ObjectPoolDemo.Pooling;
using UnityEngine;

namespace ObjectPoolDemo.Gameplay
{
    /// <summary>
    /// Top-down player: WASD movement via CharacterController, mouse aim via
    /// a raycast against the Ground layer, left-click shooting from a pooled
    /// projectile supply, and a full game reset on the reset key.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        /// <summary>
        /// Scene-scoped reference to the player, published in Awake so pooled
        /// enemies can acquire their chase target without scene scans.
        /// </summary>
        public static PlayerController Instance { get; private set; }

        [Header("Movement")]
        [Tooltip("Horizontal move speed in units per second.")]
        [SerializeField] private float _moveSpeed = 6f;

        [Tooltip("Constant downward velocity that keeps the controller grounded on slopes and edges.")]
        [SerializeField] private float _groundStickForce = 4f;

        [Header("Aiming")]
        [Tooltip("Layers the aim raycast may hit. Set this to ONLY the Ground layer.")]
        [SerializeField] private LayerMask _groundLayer;

        [Tooltip("Maximum distance of the camera-to-ground aim raycast.")]
        [SerializeField] private float _maxAimDistance = 200f;

        [Tooltip("Ignore aim points closer than this to the player, to avoid rotation jitter when the mouse is on top of the capsule.")]
        [SerializeField] private float _minAimDistance = 0.1f;

        [Header("Shooting")]
        [Tooltip("Projectile prefab spawned from the pool on left-click.")]
        [SerializeField] private ProjectileController _projectilePrefab;

        [Tooltip("Child transform the projectiles spawn from (tip of the 'gun'). Falls back to the player position if unset.")]
        [SerializeField] private Transform _muzzle;

        [Tooltip("Shots per second while the left mouse button is held.")]
        [SerializeField] private float _shotsPerSecond = 8f;

        [Tooltip("Distance in front of the player used when no Muzzle transform is assigned.")]
        [SerializeField] private float _muzzleFallbackOffset = 1f;

        [Header("Projectile Pool Sizing")]
        [Tooltip("Projectiles pre-warmed at startup.")]
        [SerializeField] private int _projectilePoolInitialSize = 30;

        [Tooltip("Hard cap on live + pooled projectiles.")]
        [SerializeField] private int _projectilePoolMaxSize = 100;

        [Header("Reset")]
        [Tooltip("Key that returns every pooled object and restarts the waves.")]
        [SerializeField] private KeyCode _resetKey = KeyCode.R;

        [Tooltip("Spawner whose waves restart on reset.")]
        [SerializeField] private EnemySpawner _spawner;

        private CharacterController _controller;
        private Camera _camera;
        private float _nextFireTime;
        private Vector3 _startPosition;
        private Quaternion _startRotation;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    $"[PlayerController] A second player ('{name}') exists in the scene. " +
                    $"Enemies will target '{Instance.name}'.", this);
            }
            else
            {
                Instance = this;
            }

            _controller = GetComponent<CharacterController>();
            _camera = Camera.main; // Cached — see design decision #5.
            _startPosition = transform.position;
            _startRotation = transform.rotation;
        }

        private void Start()
        {
            // Pre-warm the projectile pool during load so the first click
            // never triggers an Instantiate.
            if (_projectilePrefab != null)
            {
                PoolManager.Instance.GetOrCreatePool(
                    _projectilePrefab, _projectilePoolInitialSize, _projectilePoolMaxSize);
            }
            else
            {
                Debug.LogError("[PlayerController] No projectile prefab assigned — shooting is disabled.", this);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            HandleMovement();
            HandleAiming();
            HandleShooting();
            HandleReset();
        }

        /// <summary>
        /// WASD movement on the world XZ plane. GetAxisRaw for snappy arcade
        /// response (no input smoothing), normalized so diagonals are not
        /// faster than cardinals.
        /// </summary>
        private void HandleMovement()
        {
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            Vector3 motion = input * _moveSpeed + Vector3.down * _groundStickForce;
            _controller.Move(motion * Time.deltaTime);
        }

        /// <summary>
        /// Rotates the player to face the mouse cursor's position on the
        /// ground plane. See design decisions #3 and #4.
        /// </summary>
        private void HandleAiming()
        {
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);

            if (!Physics.Raycast(ray, out RaycastHit hit, _maxAimDistance, _groundLayer))
            {
                return; // Mouse is off the ground plane — keep current facing.
            }

            Vector3 aimPoint = hit.point;
            aimPoint.y = transform.position.y; // Flatten — decision #4.

            Vector3 toAim = aimPoint - transform.position;
            if (toAim.sqrMagnitude < _minAimDistance * _minAimDistance)
            {
                return; // Cursor is on top of the player — avoid jitter.
            }

            transform.rotation = Quaternion.LookRotation(toAim.normalized, Vector3.up);
        }

        /// <summary>
        /// Fires pooled projectiles while the left mouse button is held,
        /// rate-limited to <see cref="_shotsPerSecond"/>.
        /// </summary>
        private void HandleShooting()
        {
            if (_projectilePrefab == null || !Input.GetMouseButton(0) || Time.time < _nextFireTime)
            {
                return;
            }

            _nextFireTime = Time.time + 1f / _shotsPerSecond;

            Vector3 spawnPosition = _muzzle != null
                ? _muzzle.position
                : transform.position + transform.forward * _muzzleFallbackOffset;

            // May return null if the projectile pool is capped — that simply
            // drops this shot, which is the intended graceful degradation.
            PoolManager.Instance.Spawn(_projectilePrefab, spawnPosition, transform.rotation);
        }

        /// <summary>
        /// Full game reset: despawn everything in every pool, restart the
        /// wave sequence, and teleport the player back to its start pose.
        /// </summary>
        private void HandleReset()
        {
            if (!Input.GetKeyDown(_resetKey))
            {
                return;
            }

            PoolManager.Instance.ReturnAllPools();

            if (_spawner != null)
            {
                _spawner.ResetWaves();
            }

            // CharacterController caches its position internally and ignores
            // direct transform writes while enabled — toggle it around the
            // teleport so the reset actually takes effect.
            _controller.enabled = false;
            transform.SetPositionAndRotation(_startPosition, _startRotation);
            _controller.enabled = true;
        }
    }
}
