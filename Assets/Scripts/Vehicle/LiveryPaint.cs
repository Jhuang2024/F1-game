using F1Game.Core;
using F1Game.Rendering;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Unity-facing bridge from the engine-free <see cref="CarLivery"/> model to the
    /// material painting layer. Converts livery channels to <see cref="Color"/> and
    /// applies a livery to a spawned car through the existing
    /// <see cref="MaterialInstanceService"/> (per-instance property blocks, no shared
    /// material mutation). Primary paint reuses the proven single-colour path; the
    /// secondary/accent per-part re-skin needs the car parts tagged in
    /// CarVisualFactory and is editor-gated, so it is left for that pass.
    /// </summary>
    public static class LiveryPaint
    {
        public static Color ToColor(LiveryColor c) => new Color(c.R01, c.G01, c.B01, 1f);

        /// <summary>Paint a spawned car's body with the livery's primary colour.</summary>
        public static void Apply(GameObject carRoot, CarLivery livery)
        {
            if (carRoot == null || livery == null)
            {
                return;
            }

            MaterialInstanceService.ApplyLivery(carRoot, ToColor(livery.Primary));
        }
    }
}
