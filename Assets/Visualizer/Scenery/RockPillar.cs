using UnityEngine;
using UnityEngine.Rendering;

namespace Visualizer
{
    /// <summary>
    /// A procedural rock pillar (stalagmite / hoodoo). The shape is rebuilt from these settings whenever the
    /// scene loads, so nothing heavy is stored in the scene. Turn it upside down for a stalactite.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Visualizer/Rock Pillar")]
    public class RockPillar : MonoBehaviour
    {
        public enum NormalStyle
        {
            [Tooltip("Lit the way the shape really is.")] Natural,
            [Tooltip("Every triangle flat, like cut crystal.")] Faceted,
            [Tooltip("Lighting snaps into a few hard bands, ignoring the real surface.")] Stepped,
            [Tooltip("Lighting spirals around the pillar as it goes up, as if the light source kept moving.")] Twisted
        }

        [Header("Shape")]
        [Min(1)] public float height = 40f;
        [Min(0.2f)] public float radius = 5f;
        [Tooltip("Width at the top compared to the bottom. 0 = sharp point.")]
        [Range(0, 1.5f)] public float topScale = 0.35f;
        [Tooltip("How much the foot spreads out.")]
        [Range(0, 2)] public float baseFlare = 0.5f;
        [Tooltip("How lumpy the surface is.")]
        [Range(0, 1)] public float roughness = 0.35f;
        [Tooltip("Size of the lumps: lower = bigger, smoother bulges.")]
        [Range(0.02f, 1)] public float detail = 0.12f;
        [Tooltip("Number of horizontal rock layers. 0 = none.")]
        [Range(0, 60)] public int strata = 14;
        [Tooltip("How strongly the layers step in and out.")]
        [Range(0, 0.6f)] public float strataDepth = 0.18f;
        [Tooltip("How far the pillar wanders sideways as it rises, in metres.")]
        [Min(0)] public float lean = 3f;
        public int seed = 1;

        [Header("Mesh detail")]
        [Range(6, 64)] public int aroundSegments = 28;
        [Range(4, 128)] public int heightSegments = 48;

        [Header("Normal tricks (how light reads the surface)")]
        public NormalStyle normalStyle = NormalStyle.Natural;
        [Tooltip("0 = natural lighting, 1 = full effect (Stepped and Twisted).")]
        [Range(0, 1)] public float normalAmount = 1f;
        [Tooltip("Stepped: fewer steps = bigger, harder bands.")]
        [Range(1, 8)] public int steps = 2;
        [Tooltip("Twisted: degrees of spiral per metre of height.")]
        public float twist = 12f;

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

        public void Rebuild()
        {
            _dirty = false;
            if (_mesh == null) _mesh = new Mesh { name = "Rock Pillar", hideFlags = HideFlags.DontSave };
            _mesh.Clear();

            int R = aroundSegments, H = heightSegments;
            var positions = new Vector3[(H + 1) * R + 1];
            var offset = new Vector3(seed * 13.7f, seed * 7.3f, seed * 3.1f);

            for (int j = 0; j <= H; j++)
            {
                float v = (float)j / H;
                float y = v * height;
                float profile = Mathf.Lerp(1f, topScale, Mathf.Pow(v, 1.6f)) + baseFlare * Mathf.Exp(-v * 9f);
                float layer = 1f;
                if (strata > 0)
                {
                    float f = v * strata;
                    int band = Mathf.FloorToInt(f);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f - band) * 6f));
                    float prev = Noise3.Hash01(band - 1, seed), cur = Noise3.Hash01(band, seed);
                    layer = 1f + (Mathf.Lerp(prev, cur, edge) * 2f - 1f) * strataDepth;
                }
                var centre = new Vector3(
                    Noise3.Fbm(new Vector3(y * 0.03f, 0.5f, 0f) + offset, 2, seed),
                    0f,
                    Noise3.Fbm(new Vector3(0f, 0.5f, y * 0.03f) + offset, 2, seed + 5)) * lean;

                for (int i = 0; i < R; i++)
                {
                    float a = (float)i / R * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float n = Noise3.Fbm((dir * radius + new Vector3(0f, y * 0.6f, 0f)) * detail + offset, 4, seed);
                    float r = radius * profile * layer * (1f + n * roughness * 1.6f);
                    positions[j * R + i] = centre + dir * Mathf.Max(0.02f, r) + new Vector3(0f, y, 0f);
                }
                if (j == H) positions[(H + 1) * R] = centre + new Vector3(0f, height + radius * topScale * 0.4f, 0f);
            }

            var triangles = new int[H * R * 6 + R * 3];
            int t = 0;
            for (int j = 0; j < H; j++)
                for (int i = 0; i < R; i++)
                {
                    int a = j * R + i, b = j * R + (i + 1) % R, c = a + R, d = b + R;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            int cap = (H + 1) * R;
            for (int i = 0; i < R; i++)
            {
                triangles[t++] = H * R + i; triangles[t++] = cap; triangles[t++] = H * R + (i + 1) % R;
            }

            if (normalStyle == NormalStyle.Faceted)
            {
                // Give every triangle its own corners so nothing is smoothed.
                var flat = new Vector3[triangles.Length];
                var flatTriangles = new int[triangles.Length];
                for (int i = 0; i < triangles.Length; i++) { flat[i] = positions[triangles[i]]; flatTriangles[i] = i; }
                _mesh.indexFormat = flat.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                _mesh.vertices = flat;
                _mesh.triangles = flatTriangles;
                _mesh.RecalculateNormals();
            }
            else
            {
                _mesh.indexFormat = IndexFormat.UInt16;
                _mesh.vertices = positions;
                _mesh.triangles = triangles;
                _mesh.RecalculateNormals();

                if (normalStyle != NormalStyle.Natural && normalAmount > 0f)
                {
                    var normals = _mesh.normals;
                    for (int i = 0; i < normals.Length; i++)
                    {
                        Vector3 n = normals[i], tricked = n;
                        if (normalStyle == NormalStyle.Stepped)
                        {
                            tricked = new Vector3(Mathf.Round(n.x * steps), Mathf.Round(n.y * steps), Mathf.Round(n.z * steps));
                            tricked = tricked.sqrMagnitude > 0.001f ? tricked.normalized : n;
                        }
                        else if (normalStyle == NormalStyle.Twisted)
                        {
                            tricked = Quaternion.AngleAxis(twist * positions[i].y, Vector3.up) * n;
                        }
                        normals[i] = Vector3.Slerp(n, tricked, normalAmount).normalized;
                    }
                    _mesh.normals = normals;
                }
            }

            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }
    }
}
