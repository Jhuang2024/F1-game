using System;
using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.CareerCreation
{
    /// <summary>
    /// Production new-career creation screen: pick a driver name and a team, then
    /// start. Name and team are chosen with two interactive stepper rows (the same
    /// EditableSettingRow used by the settings editor), so the screen needs no text
    /// input widget. It is display only - the presenter owns the choices and the
    /// StartClicked/BackClicked actions. Pure presentation.
    /// </summary>
    public class CareerCreationView : ScreenView
    {
        public const string Id = "career-creation";

        [SerializeField] TMP_Text summaryLabel;
        [SerializeField] EditableSettingRow nameRow;
        [SerializeField] EditableSettingRow teamRow;
        [SerializeField] ThemedButton startButton;
        [SerializeField] ThemedButton backButton;

        public event Action<int> NameStep;   // -1 / +1
        public event Action<int> TeamStep;    // -1 / +1
        public event Action StartClicked;
        public event Action BackClicked;

        public void Bind(TMP_Text summary, EditableSettingRow name, EditableSettingRow team,
            ThemedButton start, ThemedButton back)
        {
            summaryLabel = summary;
            nameRow = name;
            teamRow = team;
            startButton = start;
            backButton = back;

            if (nameRow != null)
            {
                nameRow.Adjusted += (id, dir) => NameStep?.Invoke(dir);
            }

            if (teamRow != null)
            {
                teamRow.Adjusted += (id, dir) => TeamStep?.Invoke(dir);
            }

            if (startButton != null)
            {
                startButton.Clicked += () => StartClicked?.Invoke();
            }

            if (backButton != null)
            {
                backButton.Clicked += () => BackClicked?.Invoke();
            }

            SetScreenId(Id);
            SetDefaultSelection(start != null ? start.gameObject : (back != null ? back.gameObject : null));
        }

        /// <summary>Render the current driver name, team, and a one-line summary.</summary>
        public void Render(string driverName, string teamName, string teamDetail)
        {
            if (nameRow != null)
            {
                nameRow.Set("name", "Driver", driverName, true, true);
            }

            if (teamRow != null)
            {
                teamRow.Set("team", "Team", teamName, true, true);
            }

            if (summaryLabel != null)
            {
                summaryLabel.text = string.IsNullOrEmpty(teamDetail)
                    ? (driverName + " · " + teamName)
                    : (driverName + " · " + teamName + " · " + teamDetail);
            }
        }
    }
}
