using UnityEngine;
using UnityEngine.Splines;

namespace Visualizer
{
    /// <summary>
    /// Flies this object (normally the camera) along a spline while looking at a target. Where it is depends
    /// only on the song time, so scrubbing and re-rendering always give the same shot.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("Visualizer/Spline Camera Rig")]
    public class SplineCameraRig : MonoBehaviour
    {
        [Header("Path")]
        [Tooltip("The spline to fly along. Edit its points in the Scene view with the Spline tool.")]
        public SplineContainer path;
        [Tooltip("Seconds for one full trip along the path.")]
        [Min(1)] public float secondsPerLap = 120f;
        [Tooltip("Where on the path the song starts (0 = first point, 0.5 = halfway round).")]
        [Range(0, 1)] public float startPosition = 0f;

        [Header("Aim")]
        public Transform lookAt;
        [Tooltip("Aim this far away from the target's centre, in metres.")]
        public Vector3 lookOffset;

        [Header("Handheld feel")]
        [Tooltip("How far the camera floats off the path, in metres.")]
        [Min(0)] public float sway = 0.4f;
        [Tooltip("How far the aim wanders, in degrees.")]
        [Min(0)] public float aimWander = 1.2f;
        [Tooltip("Sideways tilt, in degrees.")]
        [Min(0)] public float roll = 2f;
        [Tooltip("How quickly all of the above moves.")]
        [Min(0)] public float swaySpeed = 0.12f;

        [Header("Preview")]
        [Tooltip("Song time shown while not playing. Drag to scrub the camera along its path.")]
        public double previewTime = 0;
        [Tooltip("Leave empty to use the SongPlayer in the scene.")]
        public SongPlayer song;

        void Update() => Apply();
        void OnValidate() { if (isActiveAndEnabled) Apply(); }

        void Apply()
        {
            if (path == null || path.Spline == null || path.Spline.Count < 2) return;

            var player = song != null ? song : SongPlayer.Current;
            float t = (float)(Application.isPlaying && player != null ? player.SongTime : previewTime);

            float u = Mathf.Repeat(startPosition + t / secondsPerLap, 1f);
            Vector3 position = path.EvaluatePosition(u);

            float n = t * swaySpeed;
            position += new Vector3(Noise(n, 11.3f), Noise(n, 27.9f), Noise(n, 43.1f)) * sway;
            transform.position = position;

            Vector3 target = lookAt != null ? lookAt.position + lookOffset : (Vector3)path.EvaluatePosition(Mathf.Repeat(u + 0.01f, 1f));
            Vector3 forward = target - position;
            if (forward.sqrMagnitude < 1e-6f) return;

            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            rotation *= Quaternion.Euler(Noise(n, 61.7f) * aimWander, Noise(n, 77.2f) * aimWander, Noise(n * 0.7f, 93.5f) * roll);
            transform.rotation = rotation;
        }

        // Smooth, repeatable -1..1 wobble.
        static float Noise(float x, float row) => Mathf.PerlinNoise(x, row) * 2f - 1f;
    }
}
