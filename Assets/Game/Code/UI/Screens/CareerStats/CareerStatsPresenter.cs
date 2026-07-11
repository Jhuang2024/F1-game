using System;

namespace F1Game.UI.Screens.CareerStats
{
    /// <summary>Presenter for the production career-stats screen; the bridge supplies the data + Back.</summary>
    public sealed class CareerStatsPresenter
    {
        readonly CareerStatsView view;

        public Action OnBack;

        public CareerStatsPresenter(CareerStatsView view)
        {
            this.view = view;
            view.BackClicked += () => OnBack?.Invoke();
        }

        public void Present(CareerStatsModel model)
        {
            view.Render(model);
        }
    }
}
