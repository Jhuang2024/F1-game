namespace F1Game.Core
{
    /// <summary>
    /// Engine-free colour-vision-accessibility palette. Maps the game's semantic
    /// status roles (positive/negative/warning/info/neutral - used for flags, tyre
    /// state, delta signs, warnings) to colours that stay distinguishable under the
    /// common colour-vision deficiencies. The default palette is the game's normal
    /// scheme; each deficiency mode swaps the confusable pair (red/green for
    /// prot-/deuteranopia, blue/yellow for tritanopia) for a pair that separates on
    /// an axis the viewer can still see. Also provides WCAG relative-luminance and
    /// contrast-ratio maths for verifying text legibility. Pure and unit-testable;
    /// the UI layer converts <see cref="Rgb"/> to its own colour type and applies it.
    /// </summary>
    public static class AccessibilityPalette
    {
        public enum ColorVisionMode
        {
            None = 0,
            Protanopia = 1,   // red-weak/blind
            Deuteranopia = 2, // green-weak/blind
            Tritanopia = 3,   // blue-weak/blind
        }

        public enum StatusRole
        {
            Positive = 0, // gain, DRS available, fresh tyre, green flag
            Negative = 1, // loss, penalty, danger, red flag
            Warning = 2,  // caution, worn tyre, yellow flag
            Info = 3,     // neutral information highlight
            Neutral = 4,  // idle / disabled
        }

        public readonly struct Rgb
        {
            public readonly float R;
            public readonly float G;
            public readonly float B;

            public Rgb(float r, float g, float b)
            {
                R = r;
                G = g;
                B = b;
            }
        }

        /// <summary>
        /// The colour to use for a status role under a colour-vision mode.
        /// <see cref="ColorVisionMode.None"/> returns the game's default palette;
        /// each deficiency mode returns a distinguishable substitute set.
        /// </summary>
        public static Rgb StatusColor(StatusRole role, ColorVisionMode mode)
        {
            switch (mode)
            {
                case ColorVisionMode.Protanopia:
                case ColorVisionMode.Deuteranopia:
                    // Red-green confusable: separate on blue vs orange, which both
                    // red-blind and green-blind viewers still tell apart.
                    switch (role)
                    {
                        case StatusRole.Positive: return new Rgb(0.20f, 0.55f, 0.95f); // blue
                        case StatusRole.Negative: return new Rgb(0.95f, 0.50f, 0.10f); // orange
                        case StatusRole.Warning: return new Rgb(0.95f, 0.85f, 0.15f);  // yellow
                        case StatusRole.Info: return new Rgb(0.85f, 0.90f, 0.96f);     // near-white
                        default: return new Rgb(0.60f, 0.64f, 0.70f);                  // grey
                    }

                case ColorVisionMode.Tritanopia:
                    // Blue-yellow confusable: separate on red vs teal/magenta.
                    switch (role)
                    {
                        case StatusRole.Positive: return new Rgb(0.10f, 0.75f, 0.70f); // teal
                        case StatusRole.Negative: return new Rgb(0.90f, 0.15f, 0.20f); // red
                        case StatusRole.Warning: return new Rgb(0.90f, 0.30f, 0.70f);  // magenta
                        case StatusRole.Info: return new Rgb(0.55f, 0.57f, 0.62f);     // grey-blue
                        default: return new Rgb(0.60f, 0.64f, 0.70f);                  // grey
                    }

                default:
                    // Default scheme (normal colour vision).
                    switch (role)
                    {
                        case StatusRole.Positive: return new Rgb(0.20f, 0.80f, 0.35f); // green
                        case StatusRole.Negative: return new Rgb(0.90f, 0.15f, 0.12f); // red
                        case StatusRole.Warning: return new Rgb(1.00f, 0.75f, 0.15f);  // amber
                        case StatusRole.Info: return new Rgb(0.20f, 0.70f, 1.00f);     // cyan
                        default: return new Rgb(0.60f, 0.65f, 0.70f);                  // grey
                    }
            }
        }

        /// <summary>Clamp a raw settings integer to a valid <see cref="ColorVisionMode"/>.</summary>
        public static ColorVisionMode ModeFromIndex(int index)
        {
            if (index < 0 || index > (int)ColorVisionMode.Tritanopia)
            {
                return ColorVisionMode.None;
            }

            return (ColorVisionMode)index;
        }

        /// <summary>
        /// WCAG relative luminance of a colour (sRGB, channels 0..1), used for
        /// contrast checks. Channels are linearized then weighted 0.2126/0.7152/0.0722.
        /// </summary>
        public static float RelativeLuminance(Rgb c)
        {
            return 0.2126f * Linearize(c.R) + 0.7152f * Linearize(c.G) + 0.0722f * Linearize(c.B);
        }

        /// <summary>
        /// WCAG contrast ratio between two colours, from 1 (identical) to 21
        /// (black/white). Text needs at least 4.5 for normal size, 3 for large.
        /// </summary>
        public static float ContrastRatio(Rgb a, Rgb b)
        {
            float la = RelativeLuminance(a);
            float lb = RelativeLuminance(b);
            float lighter = la > lb ? la : lb;
            float darker = la > lb ? lb : la;
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        static float Linearize(float channel)
        {
            float c = channel < 0f ? 0f : (channel > 1f ? 1f : channel);
            return c <= 0.03928f ? c / 12.92f : (float)System.Math.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
    }
}
