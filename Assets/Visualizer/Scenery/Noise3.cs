using UnityEngine;

namespace Visualizer
{
    /// <summary>Small repeatable 3D noise used by the procedural rocks and terrain.</summary>
    public static class Noise3
    {
        static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + seed * 974711);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Smooth noise, 0..1.</summary>
        public static float Value(Vector3 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float tx = p.x - x0, ty = p.y - y0, tz = p.z - z0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            tz = tz * tz * (3f - 2f * tz);
            float a = Mathf.Lerp(Hash(x0, y0, z0, seed), Hash(x0 + 1, y0, z0, seed), tx);
            float b = Mathf.Lerp(Hash(x0, y0 + 1, z0, seed), Hash(x0 + 1, y0 + 1, z0, seed), tx);
            float c = Mathf.Lerp(Hash(x0, y0, z0 + 1, seed), Hash(x0 + 1, y0, z0 + 1, seed), tx);
            float d = Mathf.Lerp(Hash(x0, y0 + 1, z0 + 1, seed), Hash(x0 + 1, y0 + 1, z0 + 1, seed), tx);
            return Mathf.Lerp(Mathf.Lerp(a, b, ty), Mathf.Lerp(c, d, ty), tz);
        }

        /// <summary>Layered noise, roughly -1..1.</summary>
        public static float Fbm(Vector3 p, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += (Value(p, seed + o * 31) * 2f - 1f) * amp;
                total += amp;
                amp *= 0.5f;
                p *= 2.03f;
            }
            return sum / total;
        }

        /// <summary>Repeatable 0..1 value for a whole number.</summary>
        public static float Hash01(int n, int seed) => Hash(n, 17, 91, seed);
    }
}
