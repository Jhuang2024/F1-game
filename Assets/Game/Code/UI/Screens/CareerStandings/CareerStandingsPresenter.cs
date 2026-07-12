using System;
using System.Collections.Generic;
using F1Game.UI.Theme;
using UnityEngine;

namespace F1Game.UI.Screens.CareerStandings
{
    /// <summary>
    /// Presenter for the championship standings screen: owns the tab switch
    /// between drivers and teams; the view stays passive.
    /// </summary>
    public sealed class CareerStandingsPresenter
    {
        readonly CareerStandingsView view;
        CareerStandingsModel model = new CareerStandingsModel();

        public Action OnBack;
        public Action OnGraph;

        public CareerStandingsPresenter(CareerStandingsView view)
        {
            this.view = view;
            view.BackButton.Clicked += () => OnBack?.Invoke();
            view.GraphClicked += () => OnGraph?.Invoke();
            view.Tabs.SelectionChanged += RenderTab;
        }

        public void Present(CareerStandingsModel standings)
        {
            model = standings ?? new CareerStandingsModel();
            view.RenderSeason(model.seasonLabel);
            RenderTab(view.Tabs.SelectedIndex);
        }

        void RenderTab(int index)
        {
            if (index == 2)
            {
                view.RenderLines(BuildCalendarLines());
                return;
            }

            view.RenderRows(index == 1 ? model.teams : model.drivers);
        }

        List<string> BuildCalendarLines()
        {
            var lines = new List<string>(model.calendar.Count);
            string accent = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.accent);
            string muted = ColorUtility.ToHtmlStringRGB(UiTheme.Active.palette.textMuted);
            for (int i = 0; i < model.calendar.Count; i++)
            {
                CalendarRowModel row = model.calendar[i];
                string status = row.isNext ? "<color=#" + accent + ">NEXT</color>"
                    : (row.isDone ? "<color=#" + muted + ">DONE</color>" : "");
                lines.Add(string.Format(
                    "<mspace=0.62em>R{0:00}</mspace>  {1}   <color=#{2}>{3} · {4} laps</color>   {5}",
                    row.round, row.trackName, muted, row.country, row.laps, status));
            }

            return lines;
        }
    }
}
