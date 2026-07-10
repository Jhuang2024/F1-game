using F1Game.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI.Widgets
{
    /// <summary>Horizontal progress/meter bar (fuel, tyre life, R&D progress, loading).</summary>
    public class UiProgressBar : MonoBehaviour
    {
        [SerializeField] Image fill;
        [SerializeField] Image track;

        public void Bind(Image fillImage, Image trackImage)
        {
            fill = fillImage;
            track = trackImage;
        }

        public void SetValue(float normalized)
        {
            if (fill != null)
            {
                fill.fillAmount = Mathf.Clamp01(normalized);
            }
        }

        public void SetValue(float normalized, Color color)
        {
            SetValue(normalized);
            if (fill != null)
            {
                fill.color = color;
            }
        }

        /// <summary>Green→amber→red mapping for depletion-style meters.</summary>
        public void SetDepletion(float normalized)
        {
            UiTheme theme = UiTheme.Active;
            Color color = normalized > 0.5f
                ? theme.palette.positive
                : (normalized > 0.25f ? theme.palette.warning : theme.palette.danger);
            SetValue(normalized, color);
        }
    }
}
