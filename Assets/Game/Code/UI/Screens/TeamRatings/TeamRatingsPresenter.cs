using System;

namespace F1Game.UI.Screens.TeamRatings
{
    /// <summary>Presenter for the production team-ratings screen; the bridge supplies data + actions.</summary>
    public sealed class TeamRatingsPresenter
    {
        readonly TeamRatingsView view;

        public Action OnDrivers;
        public Action OnBack;

        public TeamRatingsPresenter(TeamRatingsView view)
        {
            this.view = view;
            view.DriversClicked += () => OnDrivers?.Invoke();
            view.BackClicked += () => OnBack?.Invoke();
        }

        public void Present(TeamRatingsModel model)
        {
            view.Render(model);
        }
    }
}
