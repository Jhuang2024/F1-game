using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.DriverProfile
{
    /// <summary>
    /// Driver profile / career records: identity header over a pooled list of
    /// label-value stat rows. Pure presentation.
    /// </summary>
    public class DriverProfileView : ScreenView
    {
        public const string Id = "driver-profile";

        [SerializeField] TMP_Text driverName;
        [SerializeField] TMP_Text teamLine;
        [SerializeField] RectTransform rowContainer;
        [SerializeField] TMP_Text rowTemplate;
        [SerializeField] ThemedButton backButton;

        readonly List<TMP_Text> rows = new List<TMP_Text>();

        public ThemedButton BackButton => backButton;

        public void Bind(TMP_Text name, TMP_Text team, RectTransform container, TMP_Text template, ThemedButton back)
        {
            driverName = name;
            teamLine = team;
            rowContainer = container;
            rowTemplate = template;
            backButton = back;
            rowTemplate.gameObject.SetActive(false);
            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        public void Render(DriverProfileModel model)
        {
            if (driverName != null)
            {
                driverName.text = model.driverName ?? "";
            }

            if (teamLine != null)
            {
                teamLine.text = model.teamLine ?? "";
            }

            string muted = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.textMuted);
            EnsureRowCount(model.stats.Count);
            for (int i = 0; i < model.stats.Count; i++)
            {
                rows[i].gameObject.SetActive(true);
                rows[i].text = string.Format("<color=#{0}>{1}</color>   <mspace=0.62em>{2}</mspace>",
                    muted, model.stats[i].label, model.stats[i].value);
            }

            for (int i = model.stats.Count; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }
        }

        void EnsureRowCount(int count)
        {
            while (rows.Count < count)
            {
                rows.Add(Instantiate(rowTemplate, rowContainer));
            }
        }
    }
}
