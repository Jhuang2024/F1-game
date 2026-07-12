using System;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Widgets
{
    /// <summary>
    /// A career-list row: a label, a detail line, and up to two action buttons
    /// (primary + secondary). It is display only - pressing a button raises
    /// <see cref="Clicked"/> with the row's id and which button (0 primary, 1
    /// secondary); the presenter maps that to an authoritative CareerManager command.
    /// Buttons can be hidden or disabled per row, so an ineligible action is a
    /// visible no-op. Pooled from a bound template.
    /// </summary>
    public class CareerActionRow : MonoBehaviour
    {
        [SerializeField] TMP_Text labelText;
        [SerializeField] TMP_Text detailText;
        [SerializeField] ThemedButton primaryButton;
        [SerializeField] ThemedButton secondaryButton;

        string rowId;

        /// <summary>(row id, button index: 0 primary / 1 secondary).</summary>
        public event Action<string, int> Clicked;

        public void Bind(TMP_Text label, TMP_Text detail, ThemedButton primary, ThemedButton secondary)
        {
            labelText = label;
            detailText = detail;
            primaryButton = primary;
            secondaryButton = secondary;

            if (primaryButton != null)
            {
                primaryButton.Clicked += () => Clicked?.Invoke(rowId, 0);
            }

            if (secondaryButton != null)
            {
                secondaryButton.Clicked += () => Clicked?.Invoke(rowId, 1);
            }
        }

        public void Set(string id, string label, string detail,
            string primaryLabel, bool primaryShown, bool primaryEnabled,
            string secondaryLabel = null, bool secondaryShown = false, bool secondaryEnabled = false)
        {
            rowId = id;
            if (labelText != null)
            {
                labelText.text = label ?? string.Empty;
            }

            if (detailText != null)
            {
                detailText.text = detail ?? string.Empty;
            }

            Apply(primaryButton, primaryLabel, primaryShown, primaryEnabled);
            Apply(secondaryButton, secondaryLabel, secondaryShown, secondaryEnabled);
        }

        static void Apply(ThemedButton button, string label, bool shown, bool enabled)
        {
            if (button == null)
            {
                return;
            }

            button.gameObject.SetActive(shown);
            if (shown)
            {
                button.SetText(label ?? string.Empty);
                button.interactable = enabled;
            }
        }
    }
}
