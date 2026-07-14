using System;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Theme
{
    /// <summary>
    /// The single design-token asset for the production UI: typography scale,
    /// spacing grid, colour system, interaction-state colours, radii, opacity
    /// and motion durations. Widgets and screen prefabs read everything from
    /// here — no per-screen magic numbers.
    ///
    /// Visual identity (see Docs/UI_DESIGN_SYSTEM.md): dark neutral base,
    /// high-contrast white type, a single warm accent, team colours reserved
    /// for data identity, tabular numerals for all timing/telemetry.
    /// </summary>
    [CreateAssetMenu(menuName = "F1 Game/UI/Theme", fileName = "UiTheme_Default")]
    public class UiTheme : ScriptableObject
    {
        [Serializable]
        public class Typography
        {
            [Tooltip("TMP font for headings and body. Created from Rajdhani by the UI setup tool.")]
            public TMP_FontAsset regular;
            public TMP_FontAsset semiBold;
            [Tooltip("Font used for lap times, deltas, telemetry and standings. Must render tabular (fixed-width) numerals.")]
            public TMP_FontAsset tabularNumeric;

            public float display = 64f;
            public float h1 = 40f;
            public float h2 = 28f;
            public float h3 = 22f;
            public float body = 18f;
            public float bodySmall = 15f;
            public float label = 13f;
            public float button = 18f;
            public float numeric = 20f;
            public float timingCompact = 16f;
            public float caption = 12f;
        }

        [Serializable]
        public class Spacing
        {
            // 8-point grid
            public float micro = 4f;
            public float small = 8f;
            public float normal = 16f;
            public float section = 24f;
            public float large = 32f;
            public float major = 48f;
            public float hero = 64f;
        }

        [Serializable]
        public class Palette
        {
            public Color background = new Color(0.055f, 0.06f, 0.07f);
            public Color surface = new Color(0.09f, 0.095f, 0.11f);
            public Color surfaceRaised = new Color(0.125f, 0.13f, 0.15f);
            public Color outline = new Color(1f, 1f, 1f, 0.08f);
            public Color textPrimary = new Color(0.96f, 0.96f, 0.97f);
            public Color textMuted = new Color(0.62f, 0.64f, 0.68f);
            [Tooltip("The one strong accent. Everything else stays neutral.")]
            public Color accent = new Color(0.90f, 0.17f, 0.10f);
            public Color positive = new Color(0.24f, 0.78f, 0.42f);
            public Color warning = new Color(0.95f, 0.72f, 0.16f);
            public Color danger = new Color(0.92f, 0.26f, 0.21f);
            [Tooltip("Cool data colour for healthy telemetry meters (ERS charge, RPM). The warm accent is too close to danger red to double as a meter fill.")]
            public Color energy = new Color(0.22f, 0.62f, 0.94f);
            public Color focusOutline = new Color(1f, 1f, 1f, 0.9f);
        }

        [Serializable]
        public class InteractionStates
        {
            public Color normal = new Color(0.125f, 0.13f, 0.15f);
            public Color hover = new Color(0.17f, 0.175f, 0.20f);
            public Color pressed = new Color(0.10f, 0.105f, 0.12f);
            public Color selected = new Color(0.24f, 0.12f, 0.10f);
            public Color disabledSurface = new Color(0.09f, 0.095f, 0.11f, 0.5f);
            public Color disabledText = new Color(0.45f, 0.46f, 0.49f);
            [Tooltip("Focus is drawn as an outline, never only a colour shift, so controller focus is always visible.")]
            public float focusOutlineWidth = 2f;
        }

        [Serializable]
        public class Motion
        {
            [Tooltip("Micro interactions (hover, press) — 150–250 ms band.")]
            public float micro = 0.18f;
            [Tooltip("Screen transitions — 250–450 ms band.")]
            public float screen = 0.32f;
            public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            [Tooltip("Honoured by TransitionService when reduced motion is enabled in settings.")]
            public bool respectReducedMotion = true;
        }

        [Serializable]
        public class Components
        {
            public float buttonHeight = 56f;
            public float buttonHeightCompact = 44f;
            public float cornerRadius = 6f;
            public float cardPadding = 16f;
            public float modalBackdropOpacity = 0.68f;
            public float toastSeconds = 3.5f;
            public float tooltipDelaySeconds = 0.45f;
        }

        public Typography typography = new Typography();
        public Spacing spacing = new Spacing();
        public Palette palette = new Palette();
        public InteractionStates states = new InteractionStates();
        public Motion motion = new Motion();
        public Components components = new Components();

        static UiTheme active;

        /// <summary>Active theme; loads UiTheme_Default from Resources on first use.</summary>
        public static UiTheme Active
        {
            get
            {
                if (active == null)
                {
                    active = Resources.Load<UiTheme>("UI/UiTheme_Default");
                    if (active == null)
                    {
                        Debug.LogWarning("[UI] UiTheme_Default missing from Resources/UI - using built-in defaults.");
                        active = CreateInstance<UiTheme>();
                    }
                }

                return active;
            }
            set => active = value;
        }
    }
}
