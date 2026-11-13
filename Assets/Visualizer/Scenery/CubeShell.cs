using System.Collections.Generic;
using UnityEngine;

namespace Visualizer
{
    /// <summary>
    /// Builds a big hollow cube out of many small cubes, with random gaps ("windows") for light to escape.
    /// Change the settings, then right-click the component header and choose "Regenerate".
    /// The same seed always gives the same result.
    /// </summary>
    [AddComponentMenu("Visualizer/Cube Shell")]
    public class CubeShell : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Cubes along each edge of the big cube.")]
        [Range(3, 24)] public int cubesPerEdge = 12;
        [Tooltip("Size of one building-block cube, in metres.")]
        [Min(0.1f)] public float cubeSize = 2f;
        [Tooltip("How many nested walls. A second wall behind the first breaks the light up more.")]
        [Range(1, 3)] public int walls = 2;
        [Tooltip("Gap between neighbouring cubes, as a fraction of the cube size.")]
        [Range(0, 0.3f)] public float gap = 0.04f;

        [Header("Windows")]
        public int seed = 1;
        [Tooltip("Chance that any single cube is missing (outer wall).")]
        [Range(0, 0.9f)] public float windowChance = 0.22f;
        [Tooltip("Chance that a whole 2x2x2 block of cubes is missing, making bigger openings.")]
        [Range(0, 0.9f)] public float bigWindowChance = 0.16f;
        [Tooltip("Extra missing-cube chance added for each wall further in.")]
        [Range(0, 0.5f)] public float innerWallExtraWindows = 0.15f;

        [Header("Chaos")]
        [Tooltip("Chance that a cube is replaced by 8 smaller cubes (some of them missing).")]
        [Range(0, 1)] public float splitChance = 0.2f;
        [Tooltip("Chance that each of those smaller cubes is missing.")]
        [Range(0, 0.9f)] public float splitWindowChance = 0.4f;
        [Tooltip("How much cubes randomly stick out of / sink into the wall, as a fraction of the cube size.")]
        [Range(0, 1)] public float relief = 0.35f;

        [Header("Look")]
        public Material material;

        [ContextMenu("Regenerate")]
        public void Regenerate()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            var mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var rng = new System.Random(seed);
            var bigHoles = new HashSet<Vector3Int>();

            for (int wall = 0; wall < walls; wall++)
            {
                int n = cubesPerEdge - wall * 2;
                if (n < 1) break;
                var parent = new GameObject("Wall " + (wall + 1)).transform;
                parent.SetParent(transform, false);
                float half = (n - 1) * 0.5f;
                float missChance = windowChance + innerWallExtraWindows * wall;

                // Decide the big 2x2x2 openings first so they are independent of iteration order.
                bigHoles.Clear();
                for (int x = 0; x < n; x += 2)
                for (int y = 0; y < n; y += 2)
                for (int z = 0; z < n; z += 2)
                    if (rng.NextDouble() < bigWindowChance) bigHoles.Add(new Vector3Int(x / 2, y / 2, z / 2));

                for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                for (int z = 0; z < n; z++)
                {
                    bool onX = x == 0 || x == n - 1, onY = y == 0 || y == n - 1, onZ = z == 0 || z == n - 1;
                    if (!onX && !onY && !onZ) continue; // hollow inside

                    // Always draw the same number of random values per cell, so tweaking one setting
                    // doesn't reshuffle everything else.
                    double rMiss = rng.NextDouble(), rSplit = rng.NextDouble(), rRelief = rng.NextDouble();
                    int splitSeed = rng.Next();

                    if (bigHoles.Contains(new Vector3Int(x / 2, y / 2, z / 2))) continue;
                    if (rMiss < missChance) continue;

                    var center = new Vector3(x - half, y - half, z - half) * cubeSize;
                    int faces = (onX ? 1 : 0) + (onY ? 1 : 0) + (onZ ? 1 : 0);

                    if (rSplit < splitChance)
                    {
                        var sub = new System.Random(splitSeed);
                        float s = cubeSize * 0.5f;
                        for (int i = 0; i < 8; i++)
                        {
                            bool missing = sub.NextDouble() < splitWindowChance;
                            float jitter = 0.7f + (float)sub.NextDouble() * 0.3f;
                            if (missing) continue;
                            var offset = new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f) * s;
                            Spawn(parent, mesh, center + offset, Vector3.one * (s * (1f - gap * 2f) * jitter));
                        }
                        continue;
                    }

                    var scale = Vector3.one * (cubeSize * (1f - gap));
                    if (faces == 1) // flat part of a face: push in/out along the face normal
                    {
                        float depth = 1f + ((float)rRelief * 2f - 1f) * relief;
                        if (onX) scale.x *= depth; else if (onY) scale.y *= depth; else scale.z *= depth;
                    }
                    Spawn(parent, mesh, center, scale);
                }
            }
        }

        void Spawn(Transform parent, Mesh mesh, Vector3 localPosition, Vector3 scale)
        {
            var go = new GameObject("Cube");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
