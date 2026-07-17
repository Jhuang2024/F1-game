using F1Game.UI.Theme;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Services
{
    /// <summary>
    /// Single shared tooltip on the top UI layer. Widgets request/release it
    /// (pointer hover or controller focus dwell); it follows the anchor rect
    /// and clamps to the screen.
    /// </summary>
    public sealed class TooltipService
    {
        const float EdgeMargin = 8f;

        readonly RectTransform tooltipRoot;
        readonly TMP_Text label;
        readonly CanvasGroup group;

        object owner;
        float showAtTime;
        bool pending;

        public TooltipService(RectTransform tooltipRoot, TMP_Text label, CanvasGroup group)
        {
            this.tooltipRoot = tooltipRoot;
            this.label = label;
            this.group = group;
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
        }

        public void Request(object requester, string text, Vector2 screenPosition)
        {
            owner = requester;
            pending = true;
            showAtTime = Time.unscaledTime + UiTheme.Active.components.tooltipDelaySeconds;
            if (label != null)
            {
                label.text = text;
            }

            if (tooltipRoot != null)
            {
                tooltipRoot.position = screenPosition;
                ClampToScreen();
            }
        }

        // Keep the tooltip fully on-screen: a request near the right/top edge
        // used to place it partly (or wholly) outside the display.
        void ClampToScreen()
        {
            if (tooltipRoot == null)
            {
                return;
            }

            Vector3 scale = tooltipRoot.lossyScale;
            float width = tooltipRoot.rect.width * Mathf.Abs(scale.x);
            float height = tooltipRoot.rect.height * Mathf.Abs(scale.y);
            Vector2 pivot = tooltipRoot.pivot;
            Vector3 position = tooltipRoot.position;
            position.x = Mathf.Clamp(position.x,
                EdgeMargin + width * pivot.x,
                Mathf.Max(EdgeMargin + width * pivot.x, Screen.width - EdgeMargin - width * (1f - pivot.x)));
            position.y = Mathf.Clamp(position.y,
                EdgeMargin + height * pivot.y,
                Mathf.Max(EdgeMargin + height * pivot.y, Screen.height - EdgeMargin - height * (1f - pivot.y)));
            tooltipRoot.position = position;
        }

        public void Release(object requester)
        {
            if (!ReferenceEquals(owner, requester))
            {
                return;
            }

            owner = null;
            pending = false;
            if (group != null)
            {
                group.alpha = 0f;
            }
        }

        /// <summary>Ticked by UiShell.Update to honour the show delay.</summary>
        public void Tick()
        {
            if (pending && Time.unscaledTime >= showAtTime && group != null)
            {
                // Re-clamp at reveal time: the label's layout has settled by
                // now, so the rect size used for clamping is the real one.
                ClampToScreen();
                group.alpha = 1f;
                pending = false;
            }
        }
    }
}
