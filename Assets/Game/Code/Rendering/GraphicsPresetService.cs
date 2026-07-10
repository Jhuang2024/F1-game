using F1Game.Core.Events;
using UnityEngine;

namespace F1Game.Rendering
{
    /// <summary>
    /// Maps the game's 0–3 graphics-quality setting onto Unity quality levels,
    /// which under URP is what actually switches the pipeline tier assets
    /// (Low/Medium/High). Replaces the legacy direct writes to
    /// QualitySettings.antiAliasing/shadow* which are inert under URP —
    /// MSAA, shadow distance/resolution and cascades now come from the tier
    /// asset selected here.
    /// </summary>
    public static class GraphicsPresetService
    {
        // Project quality levels: 0 Very Low, 1 Low, 2 Medium, 3 High,
        // 4 Very High, 5 Ultra. Pipeline tiers: 0-1 -> URP-Low, 2 -> URP-Medium,
        // 3-5 -> URP-High (ProjectSettings/QualitySettings.asset).
        static readonly int[] GameQualityToUnityLevel = { 1, 2, 3, 5 };

        public static void Apply(int gameQuality)
        {
            int clamped = Mathf.Clamp(gameQuality, 0, GameQualityToUnityLevel.Length - 1);
            int unityLevel = Mathf.Clamp(GameQualityToUnityLevel[clamped], 0, QualitySettings.names.Length - 1);

            if (QualitySettings.GetQualityLevel() != unityLevel)
            {
                // applyExpensiveChanges: true — this is a menu/session-start
                // operation, not a per-frame one.
                QualitySettings.SetQualityLevel(unityLevel, true);
            }

            GameEvents.Publish(new GraphicsPresetChangedEvent(clamped));
        }
    }
}
