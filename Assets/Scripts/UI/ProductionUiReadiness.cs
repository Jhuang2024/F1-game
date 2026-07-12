using F1Game.UI;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Explicit, versioned capability gate for the production UI. This replaces
    /// the previous bug where <c>UiShell.TextPipelineReady()</c> was treated as
    /// automatic permission to enable the incomplete production UI — importing
    /// TMP Essentials would silently change the active frontend and could leave
    /// the strategy screen up when a race started.
    ///
    /// Rule: TMP availability means only that text CAN render. It is a
    /// prerequisite, never sufficient. The production frontend is now the
    /// production-first default once text can render, because the full single-player
    /// screen matrix is complete (main menu, track select, strategy, standings +
    /// championship graph, career hub + creation + stats + trophy + ratings + R&D +
    /// practice, driver profile, settings editor, results, time trial, and the
    /// production HUD). Every stage falls back to legacy automatically on an
    /// initialization exception (ProductionUiBridge.TryShowMainMenu / .Enabled's
    /// failedThisSession latch, ProductionSessionUi.TryShowRaceHud), and PlayerPrefs=0
    /// is the explicit emergency kill switch back to the legacy frontend + HUD.
    /// Runtime/visual parity validation is pending in-editor; the kill switch and the
    /// automatic fallbacks are the safety net until it is confirmed.
    /// </summary>
    public static class ProductionUiReadiness
    {
        const string PreferenceKey = "f1game_production_ui";

        /// <summary>
        /// Default when the user has expressed no preference. Production-first now the
        /// screen matrix is complete; the PlayerPrefs=0 kill switch and the per-stage
        /// automatic fallbacks keep legacy reachable if production init fails or is
        /// found lacking in-editor.
        /// </summary>
        public const bool DefaultWhenUnset = true;

        /// <summary>User preference: 1 = force on, 0 = force off (kill switch), unset = default.</summary>
        public static int Preference => PlayerPrefs.GetInt(PreferenceKey, -1);

        public static bool ExplicitlyDisabled => Preference == 0;

        public static bool ExplicitlyEnabled => Preference == 1;

        public static void SetEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(PreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Whether the production UI should own the frontend right now. Never true
        /// on TMP readiness alone — requires an explicit decision plus renderable text.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                if (ExplicitlyDisabled)
                {
                    return false;
                }

                bool wanted = ExplicitlyEnabled || DefaultWhenUnset;
                return wanted && UiShell.TextPipelineReady();
            }
        }
    }
}
