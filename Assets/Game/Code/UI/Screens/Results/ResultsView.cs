using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.Results
{
    /// <summary>
    /// Post-session classification: title/subtitle over a pooled row list, with
    /// a primary action (continue career / race again) and a return-to-menu
    /// action. Pure presentation - behavior comes from the presenter.
    /// </summary>
    public class ResultsView : ScreenView
    {
        public const string Id = "session-results";

        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] RectTransform rowContainer;
        [SerializeField] TMP_Text rowTemplate;
        [SerializeField] ThemedButton primaryButton;
        [SerializeField] ThemedButton menuButton;

        readonly List<TMP_Text> rows = new List<TMP_Text>();

        public ThemedButton PrimaryButton => primaryButton;
        public ThemedButton MenuButton => menuButton;

        public void Bind(TMP_Text titleText, TMP_Text subtitleText, RectTransform container,
            TMP_Text template, ThemedButton primary, ThemedButton menu)
        {
            title = titleText;
            subtitle = subtitleText;
            rowContainer = container;
            rowTemplate = template;
            primaryButton = primary;
            menuButton = menu;
            rowTemplate.gameObject.SetActive(false);
            SetScreenId(Id);
            SetDefaultSelection(primary != null ? primary.gameObject : (menu != null ? menu.gameObject : null));
        }

        public void Render(ResultsModel model)
        {
            if (title != null) title.text = model.title ?? "RESULT";
            if (subtitle != null) subtitle.text = model.subtitle ?? "";
            if (primaryButton != null) primaryButton.SetText(model.primaryActionLabel ?? "Continue");

            string accent = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.accent);
            string muted = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.textMuted);
            EnsureRowCount(model.rows.Count);
            for (int i = 0; i < model.rows.Count; i++)
            {
                ResultRowModel r = model.rows[i];
                TMP_Text row = rows[i];
                row.gameObject.SetActive(true);
                string open = r.isPlayer ? "<color=#" + accent + ">" : "";
                string close = r.isPlayer ? "</color>" : "";
                string gapColour = r.dnf ? accent : muted;
                string teamCell = string.IsNullOrEmpty(r.team) ? "" : "  <color=#" + muted + ">" + r.team + "</color>";

                if (model.showRaceColumns)
                {
                    row.text = string.Format(
                        "{0}<mspace=0.62em>{1:00}</mspace>  {2}{3}   <color=#{4}>{5}</color>" +
                        "   <color=#{6}>{7} stop{8}</color>   <align=right>{9} pts</align>{10}",
                        open, r.position, r.code, teamCell,
                        gapColour, r.gapText,
                        muted, r.pitStops, r.pitStops == 1 ? "" : "s",
                        r.points, close);
                }
                else
                {
                    // Qualifying: best-lap time and elimination/invalid tag.
                    string tag = string.IsNullOrEmpty(r.penaltyText) || r.penaltyText == "--"
                        ? "" : "   <color=#" + gapColour + ">" + r.penaltyText + "</color>";
                    row.text = string.Format(
                        "{0}<mspace=0.62em>{1:00}</mspace>  {2}{3}   <align=right><mspace=0.62em>{4}</mspace></align>{5}{6}",
                        open, r.position, r.code, teamCell, r.gapText, tag, close);
                }
            }

            for (int i = model.rows.Count; i < rows.Count; i++)
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
