using System;
using UnityEngine;

namespace F1Game.UI.Screens.PreRaceStrategy
{
    /// <summary>
    /// Presenter owning the strategy-editing state; validates lap choices
    /// against race length and stop ordering before enabling the start action.
    /// </summary>
    public sealed class PreRaceStrategyPresenter
    {
        readonly PreRaceStrategyView view;
        StrategyModel model = new StrategyModel();

        public Action<StrategyChoice> OnStartRace;
        public Action OnBack;

        public PreRaceStrategyPresenter(PreRaceStrategyView view)
        {
            this.view = view;

            for (int i = 0; i < view.CompoundButtons.Count; i++)
            {
                int index = i;
                view.CompoundButtons[i].Clicked += () =>
                {
                    model.selectedCompoundIndex = index;
                    Rerender();
                };
            }

            view.StopCountMinus.Clicked += () => AdjustStops(-1);
            view.StopCountPlus.Clicked += () => AdjustStops(+1);
            view.PitLapOneMinus.Clicked += () => AdjustPitLap(ref model.plannedPitLapOne, -1);
            view.PitLapOnePlus.Clicked += () => AdjustPitLap(ref model.plannedPitLapOne, +1);
            view.PitLapTwoMinus.Clicked += () => AdjustPitLap(ref model.plannedPitLapTwo, -1);
            view.PitLapTwoPlus.Clicked += () => AdjustPitLap(ref model.plannedPitLapTwo, +1);

            view.StartRaceButton.Clicked += () => OnStartRace?.Invoke(new StrategyChoice
            {
                StartCompoundIndex = model.selectedCompoundIndex,
                PlannedStopCount = model.plannedStopCount,
                PlannedPitLapOne = model.plannedPitLapOne,
                PlannedPitLapTwo = model.plannedPitLapTwo,
                StopOneCompoundIndex = model.stopOneCompoundIndex,
                StopTwoCompoundIndex = model.stopTwoCompoundIndex,
            });
            view.BackButton.Clicked += () => OnBack?.Invoke();
        }

        public void Present(StrategyModel strategyModel)
        {
            model = strategyModel;
            ClampPlan();
            Rerender();
        }

        void AdjustStops(int delta)
        {
            model.plannedStopCount = Mathf.Clamp(model.plannedStopCount + delta, 1, 2);
            ClampPlan();
            Rerender();
        }

        void AdjustPitLap(ref int lap, int delta)
        {
            lap += delta;
            ClampPlan();
            Rerender();
        }

        void ClampPlan()
        {
            int lastPlannableLap = Mathf.Max(2, model.raceLaps - 1);
            model.plannedPitLapOne = Mathf.Clamp(
                model.plannedPitLapOne <= 0 ? model.raceLaps / 2 : model.plannedPitLapOne,
                2, lastPlannableLap);

            if (model.plannedStopCount >= 2)
            {
                model.plannedPitLapTwo = Mathf.Clamp(
                    model.plannedPitLapTwo <= 0 ? model.raceLaps * 2 / 3 : model.plannedPitLapTwo,
                    model.plannedPitLapOne + 1, lastPlannableLap);
            }
        }

        void Rerender()
        {
            view.Render(model);
        }
    }
}
