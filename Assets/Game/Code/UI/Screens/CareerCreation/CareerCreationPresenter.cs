using System;

namespace F1Game.UI.Screens.CareerCreation
{
    /// <summary>
    /// Presenter for the production career-creation screen. Holds the offered driver
    /// names and teams and the current selection, steps them on the view's row
    /// controls, and hands the confirmed (name, teamId) back to the bridge on Start.
    /// </summary>
    public sealed class CareerCreationPresenter
    {
        readonly CareerCreationView view;
        CareerCreationModel model = new CareerCreationModel();

        /// <summary>Confirmed choice: (driver name, team id).</summary>
        public Action<string, string> OnStart;
        public Action OnBack;

        public CareerCreationPresenter(CareerCreationView view)
        {
            this.view = view;
            view.NameStep += StepName;
            view.TeamStep += StepTeam;
            view.StartClicked += Confirm;
            view.BackClicked += () => OnBack?.Invoke();
        }

        public void Present(CareerCreationModel value)
        {
            model = value ?? new CareerCreationModel();
            model.selectedNameIndex = Clamp(model.selectedNameIndex, model.driverNames.Count);
            model.selectedTeamIndex = Clamp(model.selectedTeamIndex, model.teams.Count);
            Render();
        }

        void StepName(int direction)
        {
            if (model.driverNames.Count == 0)
            {
                return;
            }

            model.selectedNameIndex = Wrap(model.selectedNameIndex + direction, model.driverNames.Count);
            Render();
        }

        void StepTeam(int direction)
        {
            if (model.teams.Count == 0)
            {
                return;
            }

            model.selectedTeamIndex = Wrap(model.selectedTeamIndex + direction, model.teams.Count);
            Render();
        }

        void Confirm()
        {
            string name = CurrentName();
            CareerTeamOption team = CurrentTeam();
            if (string.IsNullOrEmpty(name) || team == null)
            {
                return;
            }

            OnStart?.Invoke(name, team.teamId);
        }

        void Render()
        {
            CareerTeamOption team = CurrentTeam();
            view.Render(CurrentName(), team != null ? team.teamName : "-", team != null ? team.detail : "");
        }

        string CurrentName()
        {
            return model.driverNames.Count > 0 ? model.driverNames[model.selectedNameIndex] : "";
        }

        CareerTeamOption CurrentTeam()
        {
            return model.teams.Count > 0 ? model.teams[model.selectedTeamIndex] : null;
        }

        static int Clamp(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return index < 0 ? 0 : (index >= count ? count - 1 : index);
        }

        static int Wrap(int v, int count) => count <= 0 ? 0 : ((v % count) + count) % count;
    }
}
