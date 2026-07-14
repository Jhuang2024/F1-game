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

        // Image.OnPopulateMesh takes the Filled path ONLY when a sprite is
        // assigned; a sprite-less Image falls back to the plain full-rect quad
        // and silently ignores fillAmount - every runtime-built meter rendered
        // permanently full. One shared 4x4 white sprite (tinted per-bar by
        // Image.color) makes the Filled geometry actually apply.
        static Sprite solidSprite;

        static Sprite SolidSprite
        {
            get
            {
                if (solidSprite == null)
                {
                    Texture2D tex = Texture2D.whiteTexture;
                    solidSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    solidSprite.name = "UiProgressBar solid";
                }

                return solidSprite;
            }
        }

        public void Bind(Image fillImage, Image trackImage)
        {
            fill = fillImage;
            track = trackImage;

            // fillAmount only works on Filled images WITH a sprite; a Simple-type
            // or sprite-less image silently ignores it (meter stuck at 100%).
            if (fill != null)
            {
                if (fill.sprite == null)
                {
                    fill.sprite = SolidSprite;
                }

                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
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
