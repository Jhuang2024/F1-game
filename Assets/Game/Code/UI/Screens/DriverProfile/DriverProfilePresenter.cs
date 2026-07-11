using System;

namespace F1Game.UI.Screens.DriverProfile
{
    /// <summary>Presenter for the driver profile screen; the bridge supplies the model.</summary>
    public sealed class DriverProfilePresenter
    {
        readonly DriverProfileView view;

        public Action OnBack;

        public DriverProfilePresenter(DriverProfileView view)
        {
            this.view = view;
            view.BackButton.Clicked += () => OnBack?.Invoke();
        }

        public void Present(DriverProfileModel model)
        {
            view.Render(model ?? new DriverProfileModel());
        }
    }
}
