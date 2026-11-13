using UnityEngine;

namespace Visualizer
{
    /// <summary>
    /// Keeps this object (normally the camera) hovering at one spot while it slowly turns on all three axes.
    /// Where it points depends only on the song time, so scrubbing and re-rendering always give the same shot.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("Visualizer/Tumble Camera Rig")]
    public class TumbleCameraRig : MonoBehaviour
    {
        [Header("Rotation")]
        [Tooltip("Turning speed in degrees per second: X = nodding, Y = panning, Z = rolling.")]
        public Vector3 degreesPerSecond = new Vector3(2.5f, 5f, 1.5f);
        [Tooltip("Which way the camera faces at the start of the song.")]
        public Vector3 startAngles;
        [Tooltip("Makes the turning speed breathe faster and slower instead of staying perfectly even. 0 = constant.")]
        [Range(0, 1)] public float unevenness = 0.35f;

        [Header("Position")]
        [Tooltip("Where the camera hovers. Empty = wherever this object is when the scene starts.")]
        public Transform anchor;
        [Tooltip("How far the camera drifts around that point, in metres.")]
        [Min(0)] public float drift = 0.8f;
        [Tooltip("How quickly it drifts.")]
        [Min(0)] public float driftSpeed = 0.08f;

        [Header("Preview")]
        [Tooltip("Song time shown while not playing. Drag to scrub the rotation.")]
        public double previewTime = 0;
        [Tooltip("Leave empty to use the SongPlayer in the scene.")]
        public SongPlayer song;

        Vector3 _home;
        bool _hasHome;

        void OnEnable()
        {
            if (!_hasHome) { _home = transform.position; _hasHome = true; }
        }

        void Update() => Apply();
        void OnValidate() { if (isActiveAndEnabled && _hasHome) Apply(); }

        void Apply()
        {
            var player = song != null ? song : SongPlayer.Current;
            float t = (float)(Application.isPlaying && player != null ? player.SongTime : previewTime);

            // A slow wave added to the clock makes each axis speed up and ease off without ever jumping.
            float tx = t + Mathf.Sin(t * 0.11f) * unevenness / 0.11f * 0.5f;
            float ty = t + Mathf.Sin(t * 0.07f + 1.3f) * unevenness / 0.07f * 0.5f;
            float tz = t + Mathf.Sin(t * 0.05f + 2.9f) * unevenness / 0.05f * 0.5f;

            transform.rotation = Quaternion.Euler(startAngles)
                                 * Quaternion.AngleAxis(degreesPerSecond.y * ty, Vector3.up)
                                 * Quaternion.AngleAxis(degreesPerSecond.x * tx, Vector3.right)
                                 * Quaternion.AngleAxis(degreesPerSecond.z * tz, Vector3.forward);

            Vector3 centre = anchor != null ? anchor.position : _home;
            float n = t * driftSpeed;
            transform.position = centre + new Vector3(Noise(n, 13.1f), Noise(n, 29.7f), Noise(n, 47.3f)) * drift;
        }

        // Smooth, repeatable -1..1 wobble.
        static float Noise(float x, float row) => Mathf.PerlinNoise(x, row) * 2f - 1f;
    }
}
