using TMPro;
using UnityEngine;

namespace F1Game.UI.Widgets
{
    /// <summary>Labelled value tile (grid stats, career numbers, telemetry summaries).</summary>
    public class StatTile : MonoBehaviour
    {
        [SerializeField] TMP_Text label;
        [SerializeField] TMP_Text value;
        [SerializeField] TMP_Text caption;

        public void Bind(TMP_Text labelText, TMP_Text valueText, TMP_Text captionText)
        {
            label = labelText;
            value = valueText;
            caption = captionText;
        }

        public void Set(string labelString, string valueString, string captionString = null)
        {
            if (label != null)
            {
                label.text = labelString;
            }

            if (value != null)
            {
                value.text = valueString;
            }

            if (caption != null)
            {
                caption.text = captionString ?? string.Empty;
                caption.gameObject.SetActive(!string.IsNullOrEmpty(captionString));
            }
        }
    }
}
