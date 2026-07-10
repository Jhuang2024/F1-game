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
            }
        }
    }
}
