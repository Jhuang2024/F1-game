using F1Game.Core;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Unity-facing bridge to the engine-free <see cref="AccessibilityPalette"/>. Holds
    /// the active colour-vision mode (driven from the player's setting) and converts a
    /// semantic status role into a <see cref="Color"/> for UI/HUD widgets. UI code adopts
    /// this incrementally by asking for <c>Status(role)</c> instead of a hard-coded colour;
    /// with the default mode it returns the game's normal scheme, so nothing changes until
    /// a colour-vision mode is selected.
    /// </summary>
    public static class AccessibilityColors
    {
        public static AccessibilityPalette.ColorVisionMode Mode { get; private set; }
            = AccessibilityPalette.ColorVisionMode.None;

        /// <summary>Set the active mode from the persisted settings index (clamped).</summary>
        public static void SetMode(int settingsIndex)
        {
            Mode = AccessibilityPalette.ModeFromIndex(settingsIndex);
        }

        /// <summary>The colour for a status role under the active colour-vision mode.</summary>
        public static Color Status(AccessibilityPalette.StatusRole role)
        {
            AccessibilityPalette.Rgb c = AccessibilityPalette.StatusColor(role, Mode);
            return new Color(c.R, c.G, c.B, 1f);
        }
    }
}
