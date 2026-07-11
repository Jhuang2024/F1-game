using System;

namespace F1Game.UI.Screens.Results
{
    /// <summary>Presenter for the session-results screen; the bridge supplies the callbacks.</summary>
    public sealed class ResultsPresenter
    {
        readonly ResultsView view;

        public Action OnPrimary;   // continue career / race again
        public Action OnMenu;      // return to main menu

        public ResultsPresenter(ResultsView view)
        {
            this.view = view;
            view.PrimaryButton.Clicked += () => OnPrimary?.Invoke();
            view.MenuButton.Clicked += () => OnMenu?.Invoke();
        }

        public void Present(ResultsModel model)
        {
            view.Render(model ?? new ResultsModel());
        }
    }
}
