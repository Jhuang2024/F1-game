using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.CareerStandings
{
    /// <summary>
    /// Championship standings: drivers/teams tab bar over a pooled row list.
    /// Rows are plain TMP lines re-bound on data changes, never destroyed per
    /// refresh. Pure presentation - behavior comes from the presenter.
    /// </summary>
    public class CareerStandingsView : ScreenView
    {
        public const string Id = "career-standings";

        [SerializeField] TMP_Text header;
        [SerializeField] TMP_Text seasonLabel;
        [SerializeField] TabBar tabs;
        [SerializeField] RectTransform rowContainer;
        [SerializeField] TMP_Text rowTemplate;
        [SerializeField] ThemedButton backButton;

        readonly List<TMP_Text> rows = new List<TMP_Text>();

        public TabBar Tabs => tabs;
        public ThemedButton BackButton => backButton;

        public void Bind(TMP_Text headerText, TMP_Text season, TabBar tabBar,
            RectTransform container, TMP_Text template, ThemedButton back)
        {
            header = headerText;
            seasonLabel = season;
            tabs = tabBar;
            rowContainer = container;
            rowTemplate = template;
            backButton = back;
            rowTemplate.gameObject.SetActive(false);
            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        public void RenderSeason(string label)
        {
            if (seasonLabel != null)
            {
                seasonLabel.text = label ?? "";
            }
        }

        public void RenderRows(IReadOnlyList<StandingsRowModel> models)
        {
            EnsureRowCount(models.Count);
            string accent = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.accent);
            string muted = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.textMuted);
            for (int i = 0; i < models.Count; i++)
            {
                StandingsRowModel model = models[i];
                TMP_Text row = rows[i];
                row.gameObject.SetActive(true);
                string line = string.Format(
                    "<mspace=0.62em>{0:00}</mspace>  {1}{2}   <color=#{3}>{4}</color>   <align=right><mspace=0.62em>{5} pts</mspace></align>",
                    model.position,
                    model.isPlayer ? "<color=#" + accent + ">" : "",
                    model.name + (model.isPlayer ? "</color>" : ""),
                    muted,
                    string.IsNullOrEmpty(model.detail) ? (model.wins > 0 ? model.wins + " wins" : "") : model.detail,
                    model.points);
                row.text = line;
            }

            for (int i = models.Count; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }
        }

        void EnsureRowCount(int count)
        {
            while (rows.Count < count)
            {
                TMP_Text row = Instantiate(rowTemplate, rowContainer);
                rows.Add(row);
            }
        }
    }
}
