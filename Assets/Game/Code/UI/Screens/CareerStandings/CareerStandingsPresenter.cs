using System;

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

        public CareerStandingsPresenter(CareerStandingsView view)
        {
            this.view = view;
            view.BackButton.Clicked += () => OnBack?.Invoke();
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
            view.RenderRows(index == 1 ? model.teams : model.drivers);
        }
    }
}
