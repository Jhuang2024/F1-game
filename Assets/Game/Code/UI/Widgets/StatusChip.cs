using F1Game.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI.Widgets
{
    /// <summary>Small status pill (flag state, session phase, tyre compound, DRS...).</summary>
    public class StatusChip : MonoBehaviour
    {
        public enum Tone { Neutral, Accent, Positive, Warning, Danger }

        [SerializeField] TMP_Text label;
        [SerializeField] Image background;

        public void Bind(TMP_Text labelText, Image backgroundImage)
        {
            label = labelText;
            background = backgroundImage;
        }

        public void Set(string text, Tone tone)
        {
            if (label != null)
            {
                label.text = text;
            }

            if (background != null)
            {
                UiTheme theme = UiTheme.Active;
                background.color = tone switch
                {
                    Tone.Accent => theme.palette.accent,
                    Tone.Positive => theme.palette.positive,
                    Tone.Warning => theme.palette.warning,
                    Tone.Danger => theme.palette.danger,
                    _ => theme.palette.surfaceRaised,
                };
                ApplyLabelContrast(background.color);
            }
        }

        /// <summary>Data-identity colouring (team colours are allowed here by the design system).</summary>
        public void SetCustom(string text, Color color)
        {
            if (label != null)
            {
                label.text = text;
            }

            if (background != null)
            {
                background.color = color;
                ApplyLabelContrast(color);
            }
        }

        // Bright backgrounds (hard-compound white, medium yellow...) need a dark
        // label; the fixed light text was invisible on them.
        void ApplyLabelContrast(Color bg)
        {
            if (label == null)
            {
                return;
            }

            float luminance = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            label.color = luminance > 0.6f
                ? new Color(0.09f, 0.10f, 0.12f)
                : UiTheme.Active.palette.textPrimary;
        }
    }
}
