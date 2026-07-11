namespace F1Game.Core
{
    /// <summary>
    /// Engine-free car visual colour curves, extracted VERBATIM from the rendering
    /// call sites so the brake-disc glow ramp and the per-compound tyre look are
    /// unit-testable without a Renderer or UnityEngine.Color. Outputs are plain float
    /// channels (0..1) the Unity layer wraps in a Color; the numbers are identical to
    /// the previous inline code (no retuning). Compound codes follow the project
    /// convention: 0 Soft, 1 Medium, 2 Hard, 3 Intermediate, 4 Wet.
    /// </summary>
    public static class CarVisualCurves
    {
        /// <summary>
        /// Brake-disc emissive glow for a disc temperature (0..1): black when cold,
        /// ramping to a hot orange (1.4, 0.25, 0.05) as temperature squared, matching
        /// Color.Lerp(black, hot, t*t).
        /// </summary>
        public static void BrakeGlow(float temperature01, out float r, out float g, out float b)
        {
            float t = Clamp01(temperature01);
            float k = t * t;
            r = 1.4f * k;
            g = 0.25f * k;
            b = 0.05f * k;
        }

        /// <summary>
        /// Tyre sidewall look for a compound code: base colour channels, metallic and
        /// smoothness. Unknown codes (incl. Medium) use the neutral default.
        /// </summary>
        public static void TyreLook(int compoundCode, out float r, out float g, out float b,
            out float metallic, out float smoothness)
        {
            metallic = 0.02f;
            switch (compoundCode)
            {
                case 0: // Soft
                    r = 0.05f; g = 0.014f; b = 0.013f; smoothness = 0.34f;
                    break;
                case 2: // Hard
                    r = 0.05f; g = 0.05f; b = 0.054f; smoothness = 0.18f;
                    break;
                case 3: // Intermediate
                    r = 0.018f; g = 0.032f; b = 0.02f; smoothness = 0.24f;
                    break;
                case 4: // Wet
                    r = 0.014f; g = 0.02f; b = 0.045f; smoothness = 0.3f;
                    break;
                default: // Medium / unknown
                    r = 0.028f; g = 0.028f; b = 0.03f; smoothness = 0.26f;
                    break;
            }
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
