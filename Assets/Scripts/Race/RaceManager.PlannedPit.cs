using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager planned-pit-schedule subsystem (partial). Read-side accessors
    /// over the player's strategy-screen pit plan - stop count, the planned lap and
    /// compound per stop, the next planned lap/compound, the planned-stop prompt
    /// gate, the recommendation reason clause, and the planned-or-fallback pit lap.
    /// Split out of the RaceManager monolith verbatim - same class, same members,
    /// identical clamping and fallback behaviour; the cached-plan state stays
    /// main-nested and resolves in-class, and the public entry points stay public.
    /// </summary>
    public partial class RaceManager
    {
        // Number of stops the player planned on the strategy screen, defensively
        // clamped to the 1-2 range GameSettingsStore already enforces on load.
        public int GetPlannedStopCount()
        {
            return Mathf.Clamp(Settings == null ? 1 : Settings.Current.plannedStopCount, 1, 2);
        }

        // stopIndex is 1 or 2. Falls back to a RecommendedPitLap-style window when the
        // player left the lap at 0 (engineer's recommendation). Stop 2, when it has no
        // explicit lap, targets roughly two-thirds of the way through what remains
        // after stop 1 and is always strictly later than the resolved stop-1 lap.
        public int GetPlannedPitLapForStop(int stopIndex)
        {
            int maxPitLap = Mathf.Max(1, RaceLaps - 1);
            if (stopIndex <= 1)
            {
                int plannedLapOne = Settings == null ? 0 : Settings.Current.plannedPitLapOne;
                if (plannedLapOne > 0)
                {
                    return Mathf.Clamp(plannedLapOne, 1, maxPitLap);
                }

                // Auto: resolve once and cache for the rest of the race - see the
                // cachedPlannedPitLapStopOne field comment for why this must not
                // be recomputed on every call.
                if (cachedPlannedPitLapStopOne < 0)
                {
                    cachedPlannedPitLapStopOne = Mathf.Clamp(RecommendedPitLap(PlayerParticipant), 1, maxPitLap);
                }

                return cachedPlannedPitLapStopOne;
            }

            int stopOneLap = GetPlannedPitLapForStop(1);
            int minStopTwoLap = Mathf.Min(maxPitLap, stopOneLap + 1);
            int plannedLapTwo = Settings == null ? 0 : Settings.Current.plannedPitLapTwo;
            if (plannedLapTwo > 0)
            {
                return Mathf.Clamp(plannedLapTwo, minStopTwoLap, maxPitLap);
            }

            if (cachedPlannedPitLapStopTwo < 0)
            {
                int remaining = Mathf.Max(1, RaceLaps - stopOneLap);
                int recommended = stopOneLap + Mathf.RoundToInt(remaining * 0.66f);
                cachedPlannedPitLapStopTwo = Mathf.Clamp(recommended, minStopTwoLap, maxPitLap);
            }

            return cachedPlannedPitLapStopTwo;
        }

        // stopIndex is 1 or 2; returns the planned compound name for that stop.
        public string GetPlannedCompoundForStop(int stopIndex)
        {
            if (Settings == null)
            {
                return stopIndex <= 1 ? "Hard" : "Medium";
            }

            return stopIndex <= 1 ? Settings.Current.plannedStopOneCompound : Settings.Current.plannedStopTwoCompound;
        }

        // Which lap the NEXT still-pending planned stop should happen on. Returns -1
        // when there is no more planned stop (1-stop plan already taken, or both
        // stops of a 2-stop plan already taken) so callers know not to prompt.
        // Non-player participants have no strategy plan and just use the generic
        // engineer recommendation, same as before.
        public int NextPlannedPitLapFor(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer)
            {
                return RecommendedPitLap(participant);
            }

            // Sprint races carry no mandatory stop (see PenaltyRules) and the
            // AI no longer take one - the player's auto plan must not schedule
            // one either, or the PreRacePlan assist would pit the player into
            // a stop nobody else makes. Manual stops (P key) and the
            // wear/weather auto triggers still work when the rubber demands.
            if (RaceLaps < PenaltyRules.MandatoryPitMinimumRaceLaps)
            {
                return -1;
            }

            // Which planned stop is next is the rulebook's call; the lap it maps
            // to stays here. Behavior-identical to the prior inline branches.
            int stopIndex = PitPlanRules.NextPlannedStopIndex(participant.pitStops, GetPlannedStopCount());
            return stopIndex == 0 ? -1 : GetPlannedPitLapForStop(stopIndex);
        }

        // Compound for the player's next pending planned stop, parsed from the
        // strategy screen's stored string the same way Settings.SelectedTyreCompound
        // parses the qualifying/race tyre choice. Falls back to the automatic
        // weather/degradation-based NextPitCompound when there is no plan to read
        // (parse failure, no pending planned stop, or a non-player participant) so
        // AI behaviour is unchanged.
        public TyreCompound NextPlannedPitCompoundFor(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer)
            {
                return NextPitCompound(participant);
            }

            int stopIndex = participant.pitStops + 1;
            if (stopIndex > GetPlannedStopCount())
            {
                return NextPitCompound(participant);
            }

            TyreCompound parsed;
            string planned = GetPlannedCompoundForStop(stopIndex);
            if (!string.IsNullOrEmpty(planned) && System.Enum.TryParse(planned, true, out parsed))
            {
                return parsed;
            }

            return NextPitCompound(participant);
        }

        // Gate for the engineer's pit prompts: true while a planned stop is still
        // owed for this participant.
        public bool ShouldPromptPlannedStop(RaceParticipant participant)
        {
            return NextPlannedPitLapFor(participant) > 0;
        }

        // Recommendation reason (#67): short, additive clause naming whichever of
        // tyre wear / rain crossover is genuinely true right now, appended onto
        // the "Box this lap" call alongside the mandatory-rule/undercut reasons
        // already built into that message. Returns "" when neither applies -
        // the base message already stands on its own without this.
        string PitRecommendationReasonClause(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.vehicle.Tyres == null)
            {
                return "";
            }

            bool tyresWorn = participant.vehicle.Tyres.WearPercent > 65f;
            WeatherState currentWeather = Track == null ? WeatherState.Clear : Track.weather;
            bool wetNow = currentWeather == WeatherState.LightRain || currentWeather == WeatherState.HeavyRain;
            TyreCompound currentCompound = participant.vehicle.Tyres.Compound;
            bool onWetTyre = currentCompound == TyreCompound.Intermediate || currentCompound == TyreCompound.Wet;
            bool weatherMismatch = wetNow != onWetTyre;

            if (weatherMismatch)
            {
                return wetNow ? " Track's too wet for these tyres." : " Track's drying out - these won't last.";
            }

            if (tyresWorn)
            {
                return " Tyre wear is high.";
            }

            return "";
        }

        // Player pit plan: kept for any other/legacy callers. Now stop-aware -
        // resolves to whichever stop is currently pending (stop 1 if none taken yet,
        // stop 2 if the first is done and a 2-stop plan is selected), falling back to
        // the generic recommendation once there is no more planned stop.
        public int PlannedPitLapFor(RaceParticipant participant)
        {
            int next = NextPlannedPitLapFor(participant);
            return next > 0 ? next : RecommendedPitLap(participant);
        }

    }
}
