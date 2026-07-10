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
            }
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
                group.alpha = 1f;
                pending = false;
            }
        }
    }
}
