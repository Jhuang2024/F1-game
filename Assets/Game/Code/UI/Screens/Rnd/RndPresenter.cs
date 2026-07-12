using System;

namespace F1Game.UI.Screens.Rnd
{
    /// <summary>
    /// Presenter for the production R&D centre. It forwards row actions to the bridge
    /// as commands (which invoke the authoritative CareerManager mutation methods and
    /// then re-present from saved state). The presenter holds no career state itself.
    /// </summary>
    public sealed class RndPresenter
    {
        readonly RndView view;

        public Action<string> OnUpgradeDepartment;
        public Action<string> OnStartUpgrade;
        public Action<string> OnReworkProject;
        public Action<string> OnAbandonProject;
        public Action OnBack;

        public RndPresenter(RndView view)
        {
            this.view = view;
            view.UpgradeDepartment += id => OnUpgradeDepartment?.Invoke(id);
            view.StartUpgrade += id => OnStartUpgrade?.Invoke(id);
            view.ReworkProject += id => OnReworkProject?.Invoke(id);
            view.AbandonProject += id => OnAbandonProject?.Invoke(id);
            view.BackClicked += () => OnBack?.Invoke();
        }

        public void Present(RndModel model)
        {
            view.Render(model);
        }
    }
}
