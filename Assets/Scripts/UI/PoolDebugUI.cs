// ─────────────────────────────────────────────────────────────────────────────
// PoolDebugUI.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. WHY OnGUI (IMGUI) FOR A DEBUG OVERLAY?
//    IMGUI needs zero scene setup — no Canvas, no EventSystem, no prefab
//    wiring — which is exactly right for a tool that must drop into any
//    scene with one component. Yes, OnGUI allocates a little per frame;
//    that is acceptable for an editor/dev overlay that a shipping build
//    would strip or hide. Player-facing UI would use UGUI/UI Toolkit.
//
// 2. WHY unscaledDeltaTime FOR FPS?
//    Time.deltaTime is affected by Time.timeScale (pause, slow-mo). An FPS
//    counter must measure real wall-clock frame time or it lies whenever
//    the game manipulates time.
//
// 3. WHY AN EXPONENTIAL MOVING AVERAGE?
//    Raw per-frame FPS flickers unreadably. An EMA smooths it with a single
//    tunable weight and no ring-buffer allocation.
//
// 4. HOW THIS PROVES THE POOL WORKS.
//    Watch "Total": after the opening seconds it stops growing, no matter
//    how long you play. Every spawn after that point is a recycled object —
//    zero Instantiate, zero Destroy, zero garbage from spawning.
// ─────────────────────────────────────────────────────────────────────────────

using ObjectPoolDemo.Gameplay;
using ObjectPoolDemo.Pooling;
using UnityEngine;

namespace ObjectPoolDemo.UI
{
    /// <summary>
    /// Toggleable IMGUI overlay showing live active/available/total counts
    /// for every pool, the current wave, and a smoothed FPS counter.
    /// </summary>
    public class PoolDebugUI : MonoBehaviour
    {
        [Header("Toggle")]
        [Tooltip("Key that shows/hides the overlay.")]
        [SerializeField] private KeyCode _toggleKey = KeyCode.F1;

        [Tooltip("Whether the overlay is visible when the scene starts.")]
        [SerializeField] private bool _startVisible = true;

        [Header("FPS Smoothing")]
        [Tooltip("EMA weight for the FPS counter. Higher = reacts faster but flickers more.")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _fpsSmoothing = 0.1f;

        [Header("Layout")]
        [Tooltip("Overlay panel width in pixels.")]
        [SerializeField] private float _panelWidth = 340f;

        [Tooltip("Distance from the top-left screen corner in pixels.")]
        [SerializeField] private float _screenPadding = 10f;

        [Header("Optional References")]
        [Tooltip("If assigned, the overlay also shows the current wave number.")]
        [SerializeField] private EnemySpawner _spawner;

        private bool _visible;
        private float _smoothedDeltaTime;

        private void Awake()
        {
            _visible = _startVisible;
            _smoothedDeltaTime = Time.unscaledDeltaTime;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                _visible = !_visible;
            }

            // Real frame time, EMA-smoothed — design decisions #2 and #3.
            _smoothedDeltaTime = Mathf.Lerp(_smoothedDeltaTime, Time.unscaledDeltaTime, _fpsSmoothing);
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(_screenPadding, _screenPadding, _panelWidth, Screen.height - _screenPadding * 2f));
            GUILayout.BeginVertical(GUI.skin.box);

            float fps = _smoothedDeltaTime > 0f ? 1f / _smoothedDeltaTime : 0f;
            GUILayout.Label($"FPS: {fps:F0}  ({_smoothedDeltaTime * 1000f:F1} ms)");

            if (_spawner != null)
            {
                GUILayout.Label($"Wave: {_spawner.CurrentWave}");
            }

            GUILayout.Space(6f);
            GUILayout.Label("POOLS  (active / available / total, cap)");

            if (PoolManager.Instance == null)
            {
                GUILayout.Label("No PoolManager in scene.");
            }
            else
            {
                var pools = PoolManager.Instance.AllPools;
                if (pools.Count == 0)
                {
                    GUILayout.Label("No pools created yet.");
                }

                for (int i = 0; i < pools.Count; i++)
                {
                    IPoolInfo pool = pools[i];
                    GUILayout.Label(
                        $"{pool.PoolName}:  {pool.ActiveCount} / {pool.AvailableCount} / {pool.TotalCount}   (cap {pool.MaxSize})");
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label($"[{_toggleKey}] toggle overlay   [R] reset waves");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
