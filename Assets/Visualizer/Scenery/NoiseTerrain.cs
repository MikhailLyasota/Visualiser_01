using UnityEngine;

namespace Visualizer
{
    /// <summary>
    /// A procedural ground: rolling hills plus wind-blown dune ridges. Rebuilt from these settings whenever the
    /// scene loads. Flip it upside down and raise it to use it as a cave ceiling.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Visualizer/Noise Terrain")]
    public class NoiseTerrain : MonoBehaviour
    {
        [Header("Size")]
        [Tooltip("Width and length, in metres.")]
        [Min(1)] public float size = 400f;
        [Tooltip("Mesh detail: squares along each side.")]
        [Range(8, 250)] public int resolution = 180;

        [Header("Hills")]
        [Min(0)] public float hillHeight = 5f;
        [Tooltip("Width of a typical hill, in metres.")]
        [Min(1)] public float hillSize = 70f;

        [Header("Dunes")]
        [Min(0)] public float duneHeight = 2.5f;
        [Tooltip("Distance between dune crests, in metres.")]
        [Min(1)] public float duneSpacing = 26f;
        [Tooltip("Compass direction the dunes march in, degrees.")]
        public float duneDirection = 30f;
        [Tooltip("How wavy the dune crests are.")]
        [Range(0, 3)] public float duneWobble = 1.4f;

        public int seed = 1;

        Mesh _mesh;
        bool _dirty = true;

        void OnEnable() => Rebuild();
        void OnValidate() => _dirty = true;
        void Update() { if (_dirty) Rebuild(); }

        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
        }

        /// <summary>Height of the surface at a local x/z position.</summary>
        public float HeightAt(float x, float z)
        {
            var p = new Vector3(x, 0f, z);
            float h = hillHeight * Noise3.Fbm(p / hillSize + Vector3.one * seed, 4, seed);
            if (duneHeight > 0f)
            {
                float rad = duneDirection * Mathf.Deg2Rad;
                float u = (x * Mathf.Cos(rad) + z * Mathf.Sin(rad)) / duneSpacing
                          + Noise3.Fbm(p / (duneSpacing * 3f), 2, seed + 9) * duneWobble;
                float s = u - Mathf.Floor(u);
                float ridge = s < 0.72f ? s / 0.72f : (1f - s) / 0.28f; // gentle windward slope, steep lee side
                h += duneHeight * Mathf.SmoothStep(0f, 1f, ridge);
            }
            return h;
        }

        public void Rebuild()
        {
            _dirty = false;
            if (_mesh == null) _mesh = new Mesh { name = "Noise Terrain", hideFlags = HideFlags.DontSave };
            _mesh.Clear();

            int n = resolution, row = n + 1;
            var vertices = new Vector3[row * row];
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                {
                    float px = ((float)x / n - 0.5f) * size, pz = ((float)z / n - 0.5f) * size;
                    vertices[z * row + x] = new Vector3(px, HeightAt(px, pz), pz);
                }

            var triangles = new int[n * n * 6];
            int t = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int a = z * row + x;
                    triangles[t++] = a; triangles[t++] = a + row; triangles[t++] = a + 1;
                    triangles[t++] = a + 1; triangles[t++] = a + row; triangles[t++] = a + row + 1;
                }

            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }
    }
}
