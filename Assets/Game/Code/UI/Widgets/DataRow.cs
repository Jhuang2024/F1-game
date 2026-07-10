using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace F1Game.UI.Widgets
{
    /// <summary>
    /// Generic table/standings row: position badge, identity colour bar, name,
    /// and a set of value columns rendered with the tabular-numeral font so
    /// times and gaps align. Used by track lists, timing towers and results.
    /// </summary>
    public class DataRow : MonoBehaviour
    {
        [SerializeField] TMP_Text positionText;
        [SerializeField] Image identityBar;
        [SerializeField] TMP_Text nameText;
        [SerializeField] List<TMP_Text> valueColumns = new List<TMP_Text>();
        [SerializeField] Image background;

        public Image Background => background;

        public void Bind(TMP_Text position, Image identity, TMP_Text name, List<TMP_Text> columns, Image backgroundImage)
        {
            positionText = position;
            identityBar = identity;
            nameText = name;
            valueColumns = columns;
            background = backgroundImage;
        }

        public void Set(int position, Color identityColor, string name, params string[] values)
        {
            if (positionText != null)
            {
                positionText.text = position > 0 ? position.ToString() : string.Empty;
            }

            if (identityBar != null)
            {
                identityBar.color = identityColor;
            }

            if (nameText != null)
            {
                nameText.text = name;
            }

            for (int i = 0; i < valueColumns.Count; i++)
            {
                if (valueColumns[i] == null)
                {
                    continue;
                }

                valueColumns[i].text = i < values.Length ? values[i] : string.Empty;
            }
        }
    }
}
