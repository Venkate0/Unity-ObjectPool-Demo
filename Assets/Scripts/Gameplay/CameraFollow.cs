// ─────────────────────────────────────────────────────────────────────────────
// CameraFollow.cs
//
// DESIGN DECISIONS
// ────────────────
// 1. WHY LateUpdate?
//    The camera must sample the player's position AFTER all movement for the
//    frame is finished. In Update, script execution order decides whether
//    the camera sees this frame's player position or last frame's — which
//    manifests as intermittent jitter that "sometimes happens on some
//    machines". LateUpdate runs after every Update, removing the race.
//
// 2. WHY SmoothDamp INSTEAD OF Lerp(current, target, speed * deltaTime)?
//    The classic exponential Lerp never quite arrives and its feel changes
//    with frame rate. SmoothDamp is critically damped: no overshoot, frame
//    rate independent, and tuned with one intuitive number (time to reach
//    the target). It is what Unity's own examples use for cameras.
//
// 3. WHY NOT PARENT THE CAMERA TO THE PLAYER?
//    Parenting gives zero lag (robotic feel), inherits the player's rotation
//    (nauseating for top-down), and couples the camera rig to the player
//    hierarchy. A follow script keeps the camera independent and tunable.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace ObjectPoolDemo.Gameplay
{
    /// <summary>
    /// Smooth top-down follow camera. Trails the target at a fixed offset
    /// using critically damped motion, updated in LateUpdate so it always
    /// sees the target's final position for the frame.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Transform to follow — the Player.")]
        [SerializeField] private Transform _target;

        [Header("Follow")]
        [Tooltip("World-space offset from the target. Default gives a high, slightly pulled-back top-down view.")]
        [SerializeField] private Vector3 _offset = new Vector3(0f, 16f, -8f);

        [Tooltip("Approximate seconds the camera takes to catch up to the target. Smaller = tighter.")]
        [SerializeField] private float _smoothTime = 0.15f;

        [Tooltip("If enabled, the camera also pitches to keep the target centered in view.")]
        [SerializeField] private bool _lookAtTarget = true;

        private Vector3 _velocity; // SmoothDamp's internal state — must persist between frames.

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            Vector3 desiredPosition = _target.position + _offset;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _velocity, _smoothTime);

            if (_lookAtTarget)
            {
                transform.LookAt(_target.position);
            }
        }
    }
}
