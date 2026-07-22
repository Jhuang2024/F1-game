using System.Collections.Generic;
using F1Game.UI.Navigation;
using F1Game.UI.Widgets;
using TMPro;
using UnityEngine;

namespace F1Game.UI.Screens.PreRaceStrategy
{
    /// <summary>
    /// Pre-race strategy screen: starting compound, planned stop count and
    /// planned pit laps, race context (track, laps, forecast), start action.
    /// </summary>
    public class PreRaceStrategyView : ScreenView
    {
        public const string Id = "pre-race-strategy";

        [SerializeField] TMP_Text header;
        [SerializeField] TMP_Text contextLine;
        [SerializeField] List<ThemedButton> compoundButtons = new List<ThemedButton>();
        [SerializeField] ThemedButton stopCountMinus;
        [SerializeField] ThemedButton stopCountPlus;
        [SerializeField] TMP_Text stopCountValue;
        [SerializeField] ThemedButton pitLapOneMinus;
        [SerializeField] ThemedButton pitLapOnePlus;
        [SerializeField] TMP_Text pitLapOneValue;
        [SerializeField] ThemedButton pitLapTwoMinus;
        [SerializeField] ThemedButton pitLapTwoPlus;
        [SerializeField] TMP_Text pitLapTwoValue;
        [SerializeField] GameObject pitLapTwoGroup;
        [SerializeField] ThemedButton startRaceButton;
        [SerializeField] ThemedButton backButton;
        [SerializeField] TyreLifeChartView plannedChart;
        [SerializeField] TyreLifeChartView optimalChart;
        [SerializeField] TMP_Text trackTempCell;
        [SerializeField] TMP_Text fuelWeightCell;
        [SerializeField] TMP_Text fuelLapsCell;

        public IReadOnlyList<ThemedButton> CompoundButtons => compoundButtons;
        public ThemedButton StopCountMinus => stopCountMinus;
        public ThemedButton StopCountPlus => stopCountPlus;
        public ThemedButton PitLapOneMinus => pitLapOneMinus;
        public ThemedButton PitLapOnePlus => pitLapOnePlus;
        public ThemedButton PitLapTwoMinus => pitLapTwoMinus;
        public ThemedButton PitLapTwoPlus => pitLapTwoPlus;
        public ThemedButton StartRaceButton => startRaceButton;
        public ThemedButton BackButton => backButton;

        public void Bind(
            TMP_Text headerText, TMP_Text context,
            List<ThemedButton> compounds,
            ThemedButton stopsMinus, ThemedButton stopsPlus, TMP_Text stopsValue,
            ThemedButton lapOneMinus, ThemedButton lapOnePlus, TMP_Text lapOneValue,
            ThemedButton lapTwoMinus, ThemedButton lapTwoPlus, TMP_Text lapTwoValue, GameObject lapTwoGroup,
            ThemedButton startRace, ThemedButton back)
        {
            header = headerText;
            contextLine = context;
            compoundButtons = compounds;
            stopCountMinus = stopsMinus;
            stopCountPlus = stopsPlus;
            stopCountValue = stopsValue;
            pitLapOneMinus = lapOneMinus;
            pitLapOnePlus = lapOnePlus;
            pitLapOneValue = lapOneValue;
            pitLapTwoMinus = lapTwoMinus;
            pitLapTwoPlus = lapTwoPlus;
            pitLapTwoValue = lapTwoValue;
            pitLapTwoGroup = lapTwoGroup;
            startRaceButton = startRace;
            backButton = back;
            SetScreenId(Id);
            SetDefaultSelection(startRace != null ? startRace.gameObject : null);
        }

        /// <summary>Binds the tyre-life comparison charts and the context bar cells.</summary>
        public void BindCharts(
            TyreLifeChartView planned, TyreLifeChartView optimal,
            TMP_Text trackTemp, TMP_Text fuelWeight, TMP_Text fuelLaps)
        {
            plannedChart = planned;
            optimalChart = optimal;
            trackTempCell = trackTemp;
            fuelWeightCell = fuelWeight;
            fuelLapsCell = fuelLaps;
        }

        public void Render(StrategyModel model)
        {
            if (contextLine != null)
            {
                contextLine.text = string.Format(
                    "{0} · {1} laps · {2}", model.trackName, model.raceLaps, model.weatherForecast);
            }

            for (int i = 0; i < compoundButtons.Count; i++)
            {
                compoundButtons[i].SetText(i < model.compoundNames.Length ? model.compoundNames[i] : "?");
                compoundButtons[i].SetSelectedInGroup(i == model.selectedCompoundIndex);
            }

            if (stopCountValue != null)
            {
                stopCountValue.text = model.plannedStopCount.ToString();
            }

            if (pitLapOneValue != null)
            {
                pitLapOneValue.text = "Lap " + model.plannedPitLapOne;
            }

            if (pitLapTwoValue != null)
            {
                pitLapTwoValue.text = "Lap " + model.plannedPitLapTwo;
            }

            if (pitLapTwoGroup != null)
            {
                pitLapTwoGroup.SetActive(model.plannedStopCount >= 2);
            }

            if (plannedChart != null)
            {
                plannedChart.Render(StrategyChartBuilder.BuildPlanned(model));
            }

            if (optimalChart != null)
            {
                optimalChart.Render(StrategyChartBuilder.BuildOptimal(model));
            }

            RenderContextBar(model);
        }

        // ~1.18 kg of fuel per race lap, loaded with a small margin - a plausible
        // display figure so the context bar reads like the reference screen.
        const float FuelKgPerLap = 1.18f;
        const float FuelLapMargin = 0.7f;

        void RenderContextBar(StrategyModel model)
        {
            if (trackTempCell != null)
            {
                trackTempCell.text = string.Format("TRACK {0:0.0}°C", model.trackTempC);
            }

            float fuelLaps = model.raceLaps + FuelLapMargin;
            if (fuelWeightCell != null)
            {
                fuelWeightCell.text = string.Format("FUEL {0:0.0} kg", fuelLaps * FuelKgPerLap);
            }

            if (fuelLapsCell != null)
            {
                fuelLapsCell.text = string.Format("{0:0.0} laps", fuelLaps);
            }
        }
    }
}
