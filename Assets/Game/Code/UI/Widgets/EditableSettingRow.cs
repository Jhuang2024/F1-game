using System;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Widgets
{
    /// <summary>
    /// One interactive row of the production settings editor: a label, the current
    /// value, and a pair of adjust buttons (decrement / increment). It is display
    /// only - it never mutates a setting itself. When a button is pressed it raises
    /// <see cref="Adjusted"/> with this row's field id and a direction (-1 / +1); the
    /// presenter forwards that to the bridge, which mutates + persists the setting and
    /// re-presents. Pooled by SettingsView from a bound template.
    /// </summary>
    public class EditableSettingRow : MonoBehaviour
    {
        [SerializeField] TMP_Text labelText;
        [SerializeField] TMP_Text valueText;
        [SerializeField] ThemedButton decrementButton;
        [SerializeField] ThemedButton incrementButton;

        string fieldId;

        /// <summary>Raised on a control press: (field id, direction -1 or +1).</summary>
        public event Action<string, int> Adjusted;

        public void Bind(TMP_Text label, TMP_Text value, ThemedButton decrement, ThemedButton increment)
        {
            labelText = label;
            valueText = value;
            decrementButton = decrement;
            incrementButton = increment;

            if (decrementButton != null)
            {
                decrementButton.SetText("<");
                decrementButton.Clicked += () => Adjusted?.Invoke(fieldId, -1);
            }

            if (incrementButton != null)
            {
                incrementButton.SetText(">");
                incrementButton.Clicked += () => Adjusted?.Invoke(fieldId, +1);
            }
        }

        /// <summary>Show a field. Bounds control whether each adjust button is interactable.</summary>
        public void Set(string id, string label, string value, bool canDecrement, bool canIncrement)
        {
            fieldId = id;
            if (labelText != null)
            {
                labelText.text = label ?? string.Empty;
            }

            if (valueText != null)
            {
                valueText.text = value ?? string.Empty;
            }

            if (decrementButton != null)
            {
                decrementButton.interactable = canDecrement;
            }

            if (incrementButton != null)
            {
                incrementButton.interactable = canIncrement;
            }
        }
    }
}
