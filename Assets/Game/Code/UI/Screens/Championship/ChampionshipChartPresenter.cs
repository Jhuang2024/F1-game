using System;

namespace F1Game.UI.Screens.Championship
{
    /// <summary>Presenter for the production championship-graph screen; the bridge supplies data + actions.</summary>
    public sealed class ChampionshipChartPresenter
    {
        readonly ChampionshipChartView view;

        public Action<bool> OnModeSelected; // true = constructors
        public Action OnBack;

        public ChampionshipChartPresenter(ChampionshipChartView view)
        {
            this.view = view;
            view.ModeSelected += constructors => OnModeSelected?.Invoke(constructors);
            view.BackClicked += () => OnBack?.Invoke();
        }

        public void Present(ChampionshipChartModel model)
        {
            view.Render(model);
        }
    }
}
