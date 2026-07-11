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
        // Inline gameplay quick-settings: cycle the value / flip the toggle.
        public Action OnCycleDifficulty;
        public Action OnCycleErs;
        public Action OnToggleManualGears;
        // Inline accessibility quick-toggles: the bridge flips the setting,
        // persists it, and re-presents so the labels + summary refresh.
        public Action OnToggleUnits;
        public Action OnToggleCameraShake;
        public Action OnToggleCompactHud;
        public Action OnToggleUiAnimations;

        public SettingsPresenter(SettingsView view)
        {
            this.view = view;
            view.ClassicButton.Clicked += () => OnClassic?.Invoke();
            view.BackButton.Clicked += () => OnBack?.Invoke();
            view.DifficultyButton.Clicked += () => OnCycleDifficulty?.Invoke();
            view.ErsButton.Clicked += () => OnCycleErs?.Invoke();
            view.ManualGearsButton.Clicked += () => OnToggleManualGears?.Invoke();
            view.UnitsButton.Clicked += () => OnToggleUnits?.Invoke();
            view.CameraShakeButton.Clicked += () => OnToggleCameraShake?.Invoke();
            view.CompactHudButton.Clicked += () => OnToggleCompactHud?.Invoke();
            view.UiAnimationsButton.Clicked += () => OnToggleUiAnimations?.Invoke();
        }

        public void Present(SettingsModel model)
        {
            SettingsModel m = model ?? new SettingsModel();
            view.RenderRows(m.rows);
            view.RenderGameplay(m.difficultyToggleLabel, m.ersToggleLabel, m.manualGearsToggleLabel);
            view.RenderToggles(m.unitsToggleLabel, m.cameraShakeToggleLabel, m.compactHudToggleLabel, m.uiAnimationsToggleLabel);
        }
    }
}
