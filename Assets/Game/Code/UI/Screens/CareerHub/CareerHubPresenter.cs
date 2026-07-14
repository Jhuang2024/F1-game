using System;

namespace F1Game.UI.Screens.CareerHub
{
    /// <summary>Presenter for the career hub; the bridge supplies the callbacks.</summary>
    public sealed class CareerHubPresenter
    {
        readonly CareerHubView view;

        public Action OnContinue;
        public Action OnSimQualifying;
        public Action OnStandings;
        public Action OnProfile;
        public Action OnStats;
        public Action OnRatings;
        public Action OnRnd;
        public Action OnPractice;
        public Action OnLegacyMenu;
        public Action OnBack;

        public CareerHubPresenter(CareerHubView view)
        {
            this.view = view;
            view.ContinueButton.Clicked += () => OnContinue?.Invoke();
            if (view.SimQualifyingButton != null)
            {
                view.SimQualifyingButton.Clicked += () => OnSimQualifying?.Invoke();
            }

            view.StandingsButton.Clicked += () => OnStandings?.Invoke();
            view.ProfileButton.Clicked += () => OnProfile?.Invoke();
            if (view.StatsButton != null)
            {
                view.StatsButton.Clicked += () => OnStats?.Invoke();
            }

            if (view.RatingsButton != null)
            {
                view.RatingsButton.Clicked += () => OnRatings?.Invoke();
            }

            if (view.RndButton != null)
            {
                view.RndButton.Clicked += () => OnRnd?.Invoke();
            }

            if (view.PracticeButton != null)
            {
                view.PracticeButton.Clicked += () => OnPractice?.Invoke();
            }

            view.LegacyMenuButton.Clicked += () => OnLegacyMenu?.Invoke();
            view.BackButton.Clicked += () => OnBack?.Invoke();
        }

        public void Present(CareerHubModel model)
        {
            view.Render(model);
        }
    }
}
