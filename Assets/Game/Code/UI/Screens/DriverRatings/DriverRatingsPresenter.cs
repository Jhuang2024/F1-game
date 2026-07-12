using System;

namespace F1Game.UI.Screens.DriverRatings
{
    /// <summary>Presenter for the production driver-ratings screen; the bridge supplies data + actions.</summary>
    public sealed class DriverRatingsPresenter
    {
        readonly DriverRatingsView view;

        public Action<string> OnSort;
        public Action OnTeams;
        public Action OnBack;

        public DriverRatingsPresenter(DriverRatingsView view)
        {
            this.view = view;
            view.SortSelected += key => OnSort?.Invoke(key);
            view.TeamsClicked += () => OnTeams?.Invoke();
            view.BackClicked += () => OnBack?.Invoke();
        }

        public void Present(DriverRatingsModel model)
        {
            view.Render(model);
        }
    }
}
