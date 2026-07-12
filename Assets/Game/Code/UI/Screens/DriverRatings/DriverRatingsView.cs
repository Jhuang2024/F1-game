using System;
using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Theme;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.DriverRatings
{
    /// <summary>
    /// Production driver-ratings screen: a row of sort tabs and a ranked list of
    /// drivers with their key ratings, each row highlighted when it is the player or
    /// the player's team-mate. Read-only; sorting re-requests the model from the
    /// bridge. Pure presentation - the presenter owns Sort/Back.
    /// </summary>
    public class DriverRatingsView : ScreenView
    {
        public const string Id = "driver-ratings";

        [SerializeField] RectTransform rowContainer;
        [SerializeField] TMP_Text rowTemplate;
        [SerializeField] ThemedButton teamsButton;
        [SerializeField] ThemedButton backButton;

        readonly List<TMP_Text> rows = new List<TMP_Text>();
        readonly List<ThemedButton> sortTabs = new List<ThemedButton>();
        readonly List<string> sortKeys = new List<string>();

        public event Action<string> SortSelected;
        public event Action TeamsClicked;
        public event Action BackClicked;

        public void Bind(RectTransform container, TMP_Text template, ThemedButton teams, ThemedButton back)
        {
            rowContainer = container;
            rowTemplate = template;
            teamsButton = teams;
            backButton = back;
            if (rowTemplate != null)
            {
                rowTemplate.gameObject.SetActive(false);
            }

            if (teamsButton != null)
            {
                teamsButton.Clicked += () => TeamsClicked?.Invoke();
            }

            if (backButton != null)
            {
                backButton.Clicked += () => BackClicked?.Invoke();
            }

            SetScreenId(Id);
            SetDefaultSelection(back != null ? back.gameObject : null);
        }

        public void AddSortTab(ThemedButton tab, string key)
        {
            if (tab == null)
            {
                return;
            }

            sortTabs.Add(tab);
            sortKeys.Add(key);
            tab.Clicked += () => SortSelected?.Invoke(key);
        }

        public void Render(DriverRatingsModel model)
        {
            DriverRatingsModel m = model ?? new DriverRatingsModel();

            for (int i = 0; i < sortTabs.Count; i++)
            {
                sortTabs[i].SetSelectedInGroup(sortKeys[i] == m.sortKey);
            }

            int count = m.rows.Count;
            while (rows.Count < count)
            {
                rows.Add(Instantiate(rowTemplate, rowContainer));
            }

            for (int i = 0; i < count; i++)
            {
                DriverRatingRow r = m.rows[i];
                TMP_Text row = rows[i];
                row.gameObject.SetActive(true);
                row.text = FormatRow(i + 1, r);
            }

            for (int i = count; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }
        }

        static string FormatRow(int rank, DriverRatingRow r)
        {
            string marker = r.highlight == 1 ? "<b>[YOU] </b>" : (r.highlight == 2 ? "[TEAM] " : "");
            return "P" + rank + "  " + marker + r.name
                + "   <color=#8aa>" + r.team + "</color>"
                + "   OVR " + r.overall
                + " · PAC " + r.pace
                + " · QUA " + r.qualifying
                + " · RAC " + r.racecraft
                + " · POT " + r.potential;
        }
    }
}
