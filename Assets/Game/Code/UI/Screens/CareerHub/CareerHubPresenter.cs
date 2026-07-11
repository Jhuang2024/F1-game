using System;

namespace F1Game.UI.Screens.CareerHub
{
    /// <summary>Presenter for the career hub; the bridge supplies the callbacks.</summary>
    public sealed class CareerHubPresenter
    {
        readonly CareerHubView view;

        public Action OnContinue;
        public Action OnStandings;
        public Action OnLegacyMenu;
        public Action OnBack;

        public CareerHubPresenter(CareerHubView view)
        {
            this.view = view;
            view.ContinueButton.Clicked += () => OnContinue?.Invoke();
            view.StandingsButton.Clicked += () => OnStandings?.Invoke();
            view.LegacyMenuButton.Clicked += () => OnLegacyMenu?.Invoke();
            view.BackButton.Clicked += () => OnBack?.Invoke();
        }

        public void Present(CareerHubModel model)
        {
            view.Render(model);
        }
    }
}
