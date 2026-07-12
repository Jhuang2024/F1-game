using System;

namespace F1Game.UI.Screens.PracticePrograms
{
    /// <summary>Presenter for the production practice-programs screen; the bridge supplies data + actions.</summary>
    public sealed class PracticeProgramsPresenter
    {
        readonly PracticeProgramsView view;

        public Action<string> OnRun;
        public Action OnBack;

        public PracticeProgramsPresenter(PracticeProgramsView view)
        {
            this.view = view;
            view.RunProgram += id => OnRun?.Invoke(id);
            view.BackClicked += () => OnBack?.Invoke();
        }

        public void Present(PracticeProgramsModel model)
        {
            view.Render(model);
        }
    }
}
