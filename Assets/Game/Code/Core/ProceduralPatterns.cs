using UnityEngine;

namespace F1Game.Core
{
    /// <summary>
    /// Deterministic, engine-light procedural pattern math for project-owned source
    /// textures (see F1Game.Rendering.ProceduralSurfaceTextures, which turns these
    /// into <see cref="Texture2D"/> maps for the MaterialLibrary slots). Everything
    /// here is a pure function of its inputs - no <c>UnityEngine.Random</c>, no time,
    /// no state - so the same coordinates always yield the same value. That makes the
    /// generated surfaces reproducible build-to-build and lets the pattern maths be
    /// unit-tested without a texture, a material, or the graphics device.
    ///
    /// Only <see cref="Mathf"/> is used from the engine (F1Game.Core permits it); no
    /// GPU, no Texture2D, no allocation. All sample functions return a value in the
    /// closed range [0,1] unless documented otherwise.
    /// </summary>
    public static class ProceduralPatterns
    {
        /// <summary>Integer hash of a lattice cell to a stable value in [0,1).</summary>
        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000; // 24-bit mantissa -> [0,1)
            }
        }

        static float Fade(float t) => t * t * (3f - 2f * t); // smoothstep

        /// <summary>Smooth bilinear value noise in [0,1]. Integer coords map to lattice cells.</summary>
        public static float ValueNoise(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = Fade(x - x0);
            float fy = Fade(y - y0);

            float v00 = Hash01(x0, y0, seed);
            float v10 = Hash01(x0 + 1, y0, seed);
            float v01 = Hash01(x0, y0 + 1, seed);
            float v11 = Hash01(x0 + 1, y0 + 1, seed);

            float a = Mathf.Lerp(v00, v10, fx);
            float b = Mathf.Lerp(v01, v11, fx);
            return Mathf.Lerp(a, b, fy);
        }

        /// <summary>Fractal (multi-octave) value noise in [0,1]. Higher octaves add fine detail.</summary>
        public static float Fbm(float x, float y, int seed, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            if (octaves < 1)
            {
                octaves = 1;
            }

            float sum = 0f;
            float amp = 1f;
            float norm = 0f;
            float fx = x;
            float fy = y;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise(fx, fy, seed + i * 1013);
                norm += amp;
                amp *= gain;
                fx *= lacunarity;
                fy *= lacunarity;
            }

            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Position within the current stripe band, in [0,1) (sawtooth of t*count).</summary>
        public static float StripePhase(float t, int count) => Mathf.Repeat(t * count, 1f);

        /// <summary>Which stripe band position t falls in, 0..count-1 (count &gt; 0).</summary>
        public static int StripeIndex(float t, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int i = Mathf.FloorToInt(Mathf.Repeat(t * count, count));
            return i < 0 ? 0 : (i >= count ? count - 1 : i);
        }

        /// <summary>
        /// Carbon-fibre twill weave brightness in [0,1]: a 2x2 over/under lattice where
        /// alternating cells run the ridge along x or y, giving the woven sheen look.
        /// </summary>
        public static float Weave(float x, float y, int cells)
        {
            if (cells < 1)
            {
                cells = 1;
            }

            float cx = x * cells;
            float cy = y * cells;
            int px = Mathf.FloorToInt(cx);
            int py = Mathf.FloorToInt(cy);
            bool warpOver = (((px + py) & 1) == 0);
            float fx = cx - px;
            float fy = cy - py;
            float ridge = warpOver ? Mathf.Sin(fy * Mathf.PI) : Mathf.Sin(fx * Mathf.PI);
            return Mathf.Clamp01(0.35f + 0.65f * ridge);
        }

        /// <summary>
        /// Panel field mask in [0,1]: 1 across a panel, falling to 0 in a seam of
        /// width <paramref name="seamWidth"/> (in panel-fraction units) at each grid
        /// line. Used to darken concrete/garage panel joints.
        /// </summary>
        public static float PanelSeam(float x, float y, int panels, float seamWidth)
        {
            if (panels < 1)
            {
                panels = 1;
            }

            float u = Mathf.Repeat(x * panels, 1f);
            float v = Mathf.Repeat(y * panels, 1f);
            float du = Mathf.Min(u, 1f - u);
            float dv = Mathf.Min(v, 1f - v);
            float d = Mathf.Min(du, dv);
            float w = Mathf.Max(1e-4f, seamWidth);
            return Mathf.SmoothStep(0f, w, d);
        }

        /// <summary>
        /// Diagonal chain-link lattice coverage in [0,1]: ~1 on a wire, ~0 in the gap.
        /// Two crossed diagonal bands approximate the mesh for a fence surface.
        /// </summary>
        public static float ChainLink(float x, float y, int cells, float wire)
        {
            if (cells < 1)
            {
                cells = 1;
            }

            float a = Mathf.Repeat((x + y) * cells, 1f);
            float b = Mathf.Repeat((x - y) * cells, 1f);
            float da = Mathf.Min(a, 1f - a);
            float db = Mathf.Min(b, 1f - b);
            float d = Mathf.Min(da, db);
            float w = Mathf.Clamp(wire, 1e-4f, 0.5f);
            return 1f - Mathf.SmoothStep(0f, w, d);
        }
    }
}
