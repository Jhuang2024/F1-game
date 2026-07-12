using System;
using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.TeamRatings
{
    /// <summary>
    /// Production team-ratings screen: a ranked, read-only list of teams with their
    /// car overall, reliability and reputation, the player's team highlighted. A
    /// "Driver Ratings" button cross-navigates to the sibling screen. Pure presentation.
    /// </summary>
    public class TeamRatingsView : ScreenView
    {
        public const string Id = "team-ratings";

        [SerializeField] RectTransform rowContainer;
        [SerializeField] TMP_Text rowTemplate;
        [SerializeField] ThemedButton driversButton;
        [SerializeField] ThemedButton backButton;

        readonly List<TMP_Text> rows = new List<TMP_Text>();

        public event Action DriversClicked;
        public event Action BackClicked;

        public void Bind(RectTransform container, TMP_Text template, ThemedButton drivers, ThemedButton back)
        {
            rowContainer = container;
            rowTemplate = template;
            driversButton = drivers;
            backButton = back;
            if (rowTemplate != null)
            {
                rowTemplate.gameObject.SetActive(false);
            }

            if (driversButton != null)
            {
                driversButton.Clicked += () => DriversClicked?.Invoke();
            }

            if (backButton != null)
            {
                backButton.Clicked += () => BackClicked?.Invoke();
            }

            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        public void Render(TeamRatingsModel model)
        {
            TeamRatingsModel m = model ?? new TeamRatingsModel();
            int count = m.rows.Count;
            while (rows.Count < count)
            {
                rows.Add(Instantiate(rowTemplate, rowContainer));
            }

            for (int i = 0; i < count; i++)
            {
                TeamRatingRow r = m.rows[i];
                TMP_Text row = rows[i];
                row.gameObject.SetActive(true);
                string marker = r.isPlayerTeam ? "<b>[YOUR TEAM] </b>" : "";
                row.text = "P" + (i + 1) + "  " + marker + r.name
                    + "   OVR " + r.carOverall
                    + " · REL " + r.reliability
                    + " · REP " + r.reputation;
            }

            for (int i = count; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }
        }
    }
}
