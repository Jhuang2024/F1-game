using System;

namespace F1Game.UI.Screens.MainMenu
{
    /// <summary>
    /// Presenter for the main menu. The bridge supplies the model and the
    /// navigation callbacks; the presenter owns wiring, the view stays passive.
    /// </summary>
    public sealed class MainMenuPresenter
    {
        readonly MainMenuView view;

        public Action OnCareer;
        public Action OnQuickRace;
        public Action OnTimeTrial;
        public Action OnStandings;
        public Action OnSettings;

        public MainMenuPresenter(MainMenuView view)
        {
            this.view = view;
            view.ContinueCareerButton.Clicked += () => OnCareer?.Invoke();
            view.QuickRaceButton.Clicked += () => OnQuickRace?.Invoke();
            view.TimeTrialButton.Clicked += () => OnTimeTrial?.Invoke();
            view.StandingsButton.Clicked += () => OnStandings?.Invoke();
            view.SettingsButton.Clicked += () => OnSettings?.Invoke();
        }

        public void Present(MainMenuModel model)
        {
            view.Render(model);
        }
    }
}
