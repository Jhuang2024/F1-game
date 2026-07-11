using System;

namespace F1Game.UI.Screens.Settings
{
    /// <summary>
    /// Presenter for the production settings screen. The view stays passive: it
    /// renders whatever rows the bridge supplies. "Classic Settings" hands editing
    /// to the legacy screen; "Back" returns to the previous screen.
    /// </summary>
    public sealed class SettingsPresenter
    {
        readonly SettingsView view;

        public Action OnClassic;
        public Action OnBack;

        public SettingsPresenter(SettingsView view)
        {
            this.view = view;
            view.ClassicButton.Clicked += () => OnClassic?.Invoke();
            view.BackButton.Clicked += () => OnBack?.Invoke();
        }

        public void Present(SettingsModel model)
        {
            view.RenderRows((model ?? new SettingsModel()).rows);
        }
    }
}
