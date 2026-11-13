using UnityEngine;
using UnityEngine.Rendering;

namespace Visualizer
{
    /// <summary>
    /// A cloud of floating objects (dust, debris, asteroids) drawn very cheaply. Every object's position is
    /// worked out from the song time, so the cloud looks identical every time you render the same moment.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Visualizer/Drift Field")]
    public class DriftField : MonoBehaviour
    {
        [Header("What to draw")]
        public Mesh mesh;
        [Tooltip("Must have 'Enable GPU Instancing' ticked.")]
        public Material material;
        [Range(1, 20000)] public int count = 3000;
        public int seed = 1;

        [Header("Where")]
        [Tooltip("Size of the box the objects fill, in metres.")]
        public Vector3 volumeSize = new Vector3(40, 40, 40);
        [Tooltip("Keep the box centred on the camera, so there is always dust nearby wherever it flies.")]
        public bool followCamera = false;
        [Tooltip("Camera to follow. Empty = the main camera.")]
        public Transform followTarget;
        [Tooltip("Nothing appears closer than this to the centre of this object (keeps debris out of the monolith).")]
        [Min(0)] public float clearRadius = 0f;

        [Header("Size")]
        [Tooltip("Smallest and largest object size, in metres.")]
        public Vector2 sizeRange = new Vector2(0.02f, 0.06f);
        [Tooltip("0 = perfect cubes, 1 = very uneven slabs and shards.")]
        [Range(0, 1)] public float irregularity = 0.3f;

        [Header("Motion")]
        [Tooltip("Steady wind, metres per second.")]
        public Vector3 drift = new Vector3(0.05f, 0.02f, 0f);
        [Tooltip("How far each object meanders around its path, in metres.")]
        [Min(0)] public float wander = 0.3f;
        [Tooltip("How quickly it meanders.")]
        [Min(0)] public float wanderSpeed = 0.2f;
        [Tooltip("Fastest tumbling, degrees per second.")]
        [Min(0)] public float tumble = 20f;
        [Tooltip("Slowly rotates the whole field around this object's up axis, degrees per second. Use with Follow Camera off.")]
        public float orbitSpeed = 0f;

        [Header("Rendering")]
        public bool castShadows = false;

        [Header("Preview")]
        [Tooltip("Song time shown while not playing.")]
        public double previewTime = 0;
        [Tooltip("Leave empty to use the SongPlayer in the scene.")]
        public SongPlayer song;

        struct Item
        {
            public Vector3 basePosition; // 0..1 inside the volume
            public Vector3 scale;
            public Vector3 phase;
            public Vector3 wanderRate;
            public Vector3 axis;
            public float spin;
            public float startAngle;
        }

        const int BatchSize = 1023;
        Item[] _items;
        Matrix4x4[] _matrices;

        void OnEnable() => Build();
        void OnValidate() => _items = null;

        void Build()
        {
            var rng = new System.Random(seed);
            _items = new Item[count];
            _matrices = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                float size = Mathf.Lerp(sizeRange.x, sizeRange.y, Mathf.Pow(R(rng), 2f)); // mostly small, a few big
                var item = new Item
                {
                    basePosition = new Vector3(R(rng), R(rng), R(rng)),
                    scale = new Vector3(
                        size * (1f + (R(rng) * 2f - 1f) * irregularity * 0.8f),
                        size * (1f + (R(rng) * 2f - 1f) * irregularity * 0.8f),
                        size * (1f + (R(rng) * 2f - 1f) * irregularity * 0.8f)),
                    phase = new Vector3(R(rng), R(rng), R(rng)) * (Mathf.PI * 2f),
                    wanderRate = new Vector3(0.5f + R(rng), 0.5f + R(rng), 0.5f + R(rng)),
                    spin = (R(rng) * 2f - 1f),
                    startAngle = R(rng) * 360f
                };
                var axis = new Vector3(R(rng) * 2f - 1f, R(rng) * 2f - 1f, R(rng) * 2f - 1f);
                item.axis = axis.sqrMagnitude > 1e-4f ? axis.normalized : Vector3.up;
                _items[i] = item;
            }
        }

        static float R(System.Random rng) => (float)rng.NextDouble();

        Transform _sceneCamera;

        // The camera in this object's own scene (several worlds can be loaded at once), else the main camera.
        Transform SceneCamera()
        {
            if (_sceneCamera != null) return _sceneCamera;
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                var cam = root.GetComponentInChildren<Camera>(true);
                if (cam != null) { _sceneCamera = cam.transform; break; }
            }
            if (_sceneCamera == null && Camera.main != null) _sceneCamera = Camera.main.transform;
            return _sceneCamera;
        }

        void LateUpdate()
        {
            if (mesh == null || material == null) return;
            if (_items == null || _items.Length != count) Build();

            var player = song != null ? song : SongPlayer.Current;
            float t = (float)(Application.isPlaying && player != null ? player.SongTime : previewTime);

            Vector3 origin = transform.position;
            Vector3 center = origin;
            if (followCamera)
            {
                var target = followTarget != null ? followTarget : SceneCamera();
                if (target != null) center = target.position;
            }

            Vector3 size = new Vector3(Mathf.Max(0.01f, volumeSize.x), Mathf.Max(0.01f, volumeSize.y), Mathf.Max(0.01f, volumeSize.z));
            Quaternion orbit = Quaternion.AngleAxis(orbitSpeed * t, transform.up);
            float clearSqr = clearRadius * clearRadius;
            Vector3 wind = drift * t;

            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                Vector3 p = Vector3.Scale(it.basePosition, size) + wind;
                // Wrap into a box around the centre so objects that drift out one side come back in the other.
                Vector3 rel = p - (followCamera ? center : Vector3.zero);
                rel.x -= size.x * Mathf.Round(rel.x / size.x);
                rel.y -= size.y * Mathf.Round(rel.y / size.y);
                rel.z -= size.z * Mathf.Round(rel.z / size.z);

                Vector3 world;
                if (followCamera) world = center + rel;
                else world = origin + orbit * rel;

                bool hidden = clearSqr > 0f && (world - origin).sqrMagnitude < clearSqr;

                float w = t * wanderSpeed;
                world += new Vector3(
                    Mathf.Sin(w * it.wanderRate.x + it.phase.x),
                    Mathf.Sin(w * it.wanderRate.y + it.phase.y),
                    Mathf.Sin(w * it.wanderRate.z + it.phase.z)) * wander;

                var rotation = Quaternion.AngleAxis(it.startAngle + it.spin * tumble * t, it.axis);
                _matrices[i] = Matrix4x4.TRS(world, rotation, hidden ? Vector3.zero : it.scale);
            }

            var rp = new RenderParams(material)
            {
                worldBounds = new Bounds(center, size + Vector3.one * (wander * 2f + sizeRange.y * 2f)),
                shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                layer = gameObject.layer
            };
            for (int start = 0; start < _matrices.Length; start += BatchSize)
                Graphics.RenderMeshInstanced(rp, mesh, 0, _matrices, Mathf.Min(BatchSize, _matrices.Length - start), start);
        }
    }
}
