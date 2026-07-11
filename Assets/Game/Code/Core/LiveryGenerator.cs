using System.Collections.Generic;

namespace F1Game.Core
{
    /// <summary>
    /// Engine-free procedural livery generator + grid validator. Where authored team
    /// liveries are missing or would clash, it fills the grid with clearly-placeholder
    /// liveries spread evenly around the colour wheel so every car is visually distinct
    /// (a grid of same-looking cars is unreadable). Pure HSV maths + distinctness
    /// resolution, so it is unit-testable; the Unity layer paints the result.
    /// </summary>
    public static class LiveryGenerator
    {
        /// <summary>
        /// A distinct placeholder livery for car <paramref name="index"/> of
        /// <paramref name="total"/>: a saturated primary at an evenly-spaced hue, a
        /// dark secondary and a light accent of the same hue.
        /// </summary>
        public static CarLivery Generate(int index, int total)
        {
            int count = total < 1 ? 1 : total;
            float hue = ((index % count) + count) % count / (float)count;
            LiveryColor primary = FromHsv(hue, 0.85f, 0.90f);
            LiveryColor secondary = FromHsv(hue, 0.35f, 0.12f);
            LiveryColor accent = FromHsv(hue, 0.15f, 0.96f);
            return new CarLivery(primary, secondary, accent);
        }

        /// <summary>
        /// Return a livery for every grid slot: keep each requested livery when it is
        /// present and stays distinct from the ones already accepted, otherwise fill
        /// with a generated placeholder. Guarantees no two returned liveries share a
        /// too-similar primary (by <paramref name="minChannelDelta"/>).
        /// </summary>
        public static List<CarLivery> AssignDistinctGrid(IReadOnlyList<CarLivery> requested, int total,
            int minChannelDelta = 40)
        {
            int count = total < 0 ? 0 : total;
            var result = new List<CarLivery>(count);
            for (int i = 0; i < count; i++)
            {
                CarLivery desired = requested != null && i < requested.Count ? requested[i] : null;
                if (desired != null && IsDistinctFromAll(desired, result, minChannelDelta))
                {
                    result.Add(desired);
                    continue;
                }

                // Collision or missing: walk generated hues until one is distinct.
                CarLivery replacement = Generate(i, count);
                int guard = 0;
                while (!IsDistinctFromAll(replacement, result, minChannelDelta) && guard < count * 2 + 4)
                {
                    guard++;
                    replacement = Generate(i + guard, count * 2 + 4);
                }

                result.Add(replacement);
            }

            return result;
        }

        static bool IsDistinctFromAll(CarLivery candidate, List<CarLivery> accepted, int minChannelDelta)
        {
            for (int i = 0; i < accepted.Count; i++)
            {
                if (!CarLivery.AreDistinct(candidate, accepted[i], minChannelDelta))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>HSV (all 0..1) to a 24-bit <see cref="LiveryColor"/>. Deterministic and engine-free.</summary>
        public static LiveryColor FromHsv(float h, float s, float v)
        {
            h = Frac(h) * 6f;
            int sector = (int)h;
            float f = h - sector;
            float p = v * (1f - s);
            float q = v * (1f - f * s);
            float t = v * (1f - (1f - f) * s);

            float r, g, b;
            switch (sector % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return new LiveryColor(ToByte(r), ToByte(g), ToByte(b));
        }

        static float Frac(float value)
        {
            float f = value - (int)value;
            return f < 0f ? f + 1f : f;
        }

        static byte ToByte(float channel01)
        {
            float c = channel01 < 0f ? 0f : (channel01 > 1f ? 1f : channel01);
            int v = (int)(c * 255f + 0.5f);
            return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));
        }
    }
}
