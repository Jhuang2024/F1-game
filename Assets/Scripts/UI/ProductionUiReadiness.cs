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
    /// prerequisite, never sufficient. During migration the stable legacy
    /// frontend is the default; the production UI is used only when explicitly
    /// enabled (PlayerPrefs) AND text can render. When the full frontend + race
    /// flow is complete, flip <see cref="DefaultWhenUnset"/> to true and keep the
    /// PlayerPrefs=0 emergency kill switch.
    /// </summary>
    public static class ProductionUiReadiness
    {
        const string PreferenceKey = "f1game_production_ui";

        /// <summary>
        /// Default when the user has expressed no preference. False during
        /// migration (legacy is the stable default); becomes true only when the
        /// production frontend/race flow reaches parity.
        /// </summary>
        public const bool DefaultWhenUnset = false;

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
