using System.Collections.Generic;
using F1Game.Race.Rules;
using UnityEngine;

namespace F1Game.UI.Screens.PreRaceStrategy
{
    /// <summary>
    /// Turns a <see cref="StrategyModel"/> into the two <see cref="StrategyChartModel"/>s
    /// the pre-race screen compares: the player's live plan and the fastest plan
    /// the optimiser finds for this race length and track temperature. All stint
    /// lengths, expected tyre life and the estimated total race time come from the
    /// SAME <see cref="TyreStrategyRules"/> the wear model and the AI use, so the
    /// chart, the recommendation and what happens on track all agree.
    /// </summary>
    public static class StrategyChartBuilder
    {
        // Relative per-lap pace penalty by dry compound (seconds/lap slower than
        // the soft), mirroring TyreStrategyRules' private table so the estimated
        // times rank strategies the same way the optimiser does.
        static float CompoundPaceSeconds(int dryCompound)
        {
            switch (dryCompound)
            {
                case TyreStrategyRules.Compound.Soft: return 0f;
                case TyreStrategyRules.Compound.Medium: return 0.45f;
                default: return 0.9f; // Hard
            }
        }

        // A UI compound index (0 Soft .. 4 Wet) collapsed onto the dry curve the
        // wear model uses: inters and wets mirror the medium's durability.
        static int DryCompound(int uiCompoundIndex)
        {
            switch (uiCompoundIndex)
            {
                case 0: return TyreStrategyRules.Compound.Soft;
                case 2: return TyreStrategyRules.Compound.Hard;
                default: return TyreStrategyRules.Compound.Medium;
            }
        }

        static string ShortName(int uiCompoundIndex)
        {
            switch (uiCompoundIndex)
            {
                case 0: return "S";
                case 1: return "M";
                case 2: return "H";
                case 3: return "I";
                default: return "W";
            }
        }

        static string LongName(int uiCompoundIndex)
        {
            switch (uiCompoundIndex)
            {
                case 0: return "Soft";
                case 1: return "Medium";
                case 2: return "Hard";
                case 3: return "Inter";
                default: return "Wet";
            }
        }

        // Baseline dry lap time from track length, assuming an ~200 km/h race
        // average; clamped so a very short synthetic circuit still reads as a
        // plausible lap. Only the relative ranking of strategies matters here.
        static float BaseLapSeconds(float trackLengthKm)
        {
            if (trackLengthKm <= 0.1f)
            {
                return 90f;
            }

            float lap = trackLengthKm / 200f * 3600f;
            return Mathf.Clamp(lap, 45f, 130f);
        }

        static string FormatTime(float totalSeconds)
        {
            if (totalSeconds < 0f)
            {
                totalSeconds = 0f;
            }

            int minutes = (int)(totalSeconds / 60f);
            float seconds = totalSeconds - minutes * 60f;
            return string.Format("{0}:{1:00.0}", minutes, seconds);
        }

        static float EstimateTotalSeconds(List<StintPlan> stints, int stopCount, float baseLap)
        {
            float total = 0f;
            for (int i = 0; i < stints.Count; i++)
            {
                StintPlan s = stints[i];
                int dry = DryCompound(s.compoundIndex);
                total += s.laps * (baseLap + CompoundPaceSeconds(dry));
                // Within-stint degradation grows with stint length (same shape the
                // optimiser uses); an over-long stint is visibly penalised.
                total += TyreStrategyRules.StintDegPenaltyCoeffSeconds * s.laps * s.laps;
            }

            total += stopCount * TyreStrategyRules.PitStopLossSeconds;
            return total;
        }

        static StintPlan MakeStint(int uiCompoundIndex, int startLap, int laps, float trackTempC)
        {
            return new StintPlan
            {
                compoundIndex = uiCompoundIndex,
                startLap = startLap,
                laps = laps < 1 ? 1 : laps,
                expectedLife = TyreStrategyRules.ExpectedStintLapsAtTemp(DryCompound(uiCompoundIndex), trackTempC),
                compoundShort = ShortName(uiCompoundIndex),
            };
        }

        /// <summary>The chart for the player's currently-edited plan.</summary>
        public static StrategyChartModel BuildPlanned(StrategyModel m)
        {
            int laps = m.raceLaps > 0 ? m.raceLaps : 1;
            int stops = Mathf.Clamp(m.plannedStopCount, 1, 2);
            var stints = new List<StintPlan>();

            int pit1 = Mathf.Clamp(m.plannedPitLapOne, 1, laps - 1);
            if (stops >= 2)
            {
                int pit2 = Mathf.Clamp(m.plannedPitLapTwo, pit1 + 1, laps - 1);
                stints.Add(MakeStint(m.selectedCompoundIndex, 1, pit1, m.trackTempC));
                stints.Add(MakeStint(m.stopOneCompoundIndex, pit1 + 1, pit2 - pit1, m.trackTempC));
                stints.Add(MakeStint(m.stopTwoCompoundIndex, pit2 + 1, laps - pit2, m.trackTempC));
            }
            else
            {
                stints.Add(MakeStint(m.selectedCompoundIndex, 1, pit1, m.trackTempC));
                stints.Add(MakeStint(m.stopOneCompoundIndex, pit1 + 1, laps - pit1, m.trackTempC));
            }

            float baseLap = BaseLapSeconds(m.trackLengthKm);
            var subtitle = new System.Text.StringBuilder();
            for (int i = 0; i < stints.Count; i++)
            {
                if (i > 0)
                {
                    subtitle.Append('-');
                }

                subtitle.Append(LongName(stints[i].compoundIndex));
            }

            return new StrategyChartModel
            {
                title = "YOUR PLAN",
                subtitle = stops + (stops == 1 ? " stop · " : " stops · ") + subtitle,
                totalTime = FormatTime(EstimateTotalSeconds(stints, stops, baseLap)),
                isRecommended = false,
                raceLaps = laps,
                stints = stints,
            };
        }

        /// <summary>The chart for the fastest plan the optimiser finds.</summary>
        public static StrategyChartModel BuildOptimal(StrategyModel m)
        {
            int laps = m.raceLaps > 0 ? m.raceLaps : 1;
            TyreStrategyRules.FastestDryStrategy(laps, m.trackTempC, out int startDry, out int stops);
            stops = Mathf.Clamp(stops, 0, 2);
            int stints = stops + 1;

            // Even stint split, remainder pushed onto the earlier stints.
            var plan = new List<StintPlan>();
            int used = 0;
            int uiCompound = startDry; // dry codes 0/1/2 line up with the UI Soft/Med/Hard indices.
            for (int i = 0; i < stints; i++)
            {
                int remainingStints = stints - i;
                int lapsLeft = laps - used;
                int stintLen = Mathf.CeilToInt((float)lapsLeft / remainingStints);
                if (stintLen < 1)
                {
                    stintLen = 1;
                }

                plan.Add(MakeStint(uiCompound, used + 1, stintLen, m.trackTempC));
                used += stintLen;
            }

            float baseLap = BaseLapSeconds(m.trackLengthKm);
            return new StrategyChartModel
            {
                title = "FASTEST",
                subtitle = stops + (stops == 1 ? " stop · " : " stops · ") + LongName(uiCompound),
                totalTime = FormatTime(EstimateTotalSeconds(plan, stops, baseLap)),
                isRecommended = true,
                raceLaps = laps,
                stints = plan,
            };
        }
    }
}
