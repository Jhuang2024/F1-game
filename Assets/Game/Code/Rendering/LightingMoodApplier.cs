using UnityEngine;

namespace F1Game.Rendering
{
    /// <summary>
    /// Applies a <see cref="LightingMoodProfile"/> to the scene environment:
    /// sun (main directional) intensity/tint, ambient colour, fog and reflection
    /// intensity. Complements <see cref="RaceVolumeService"/> (which owns the URP
    /// post-processing/grade side) so the whole lighting mood — post and
    /// environment — comes from one authored profile rather than scattered
    /// constants.
    /// </summary>
    public static class LightingMoodApplier
    {
        public static void ApplyToScene(LightingMoodProfile mood, Light sun)
        {
            if (mood == null)
            {
                return;
            }

            if (sun != null)
            {
                sun.intensity = Mathf.Max(0f, sun.intensity * mood.sunIntensityMultiplier);
                sun.color = Color.Lerp(sun.color, mood.ambientTint, 0.35f);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientIntensity = Mathf.Clamp(mood.sunIntensityMultiplier, 0.1f, 1.5f);
            RenderSettings.ambientLight = mood.ambientTint;

            RenderSettings.fog = mood.fogEnabled;
            if (mood.fogEnabled)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = mood.fogColor;
                // Scenery-visibility clamp: mood assets carrying the old 0.004
                // default were ~500m visibility soup - every building/mountain
                // beyond the fence fogged out entirely.
                RenderSettings.fogDensity = Mathf.Min(mood.fogDensity, 0.0007f);
            }

            RenderSettings.reflectionIntensity = mood.fogEnabled ? 0.7f : 0.85f;
        }
    }
}
