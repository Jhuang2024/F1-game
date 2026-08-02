namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure planned-pit-window logic, extracted from
    /// <c>RaceManager.NextPlannedPitLapFor</c>: given how many stops a car has
    /// already made and how many the plan calls for, which planned stop (if any)
    /// is next. The race layer maps the returned index to an actual lap.
    /// </summary>
    public static class PitPlanRules
    {
        /// <summary>
        /// 1-based index of the next pending planned stop, or 0 when none
        /// remain. Mirrors the monolith exactly: a car yet to stop is on stop 1;
        /// a car that has made its first stop is on stop 2 only when the plan
        /// calls for two or more; otherwise the plan is complete.
        /// </summary>
        public static int NextPlannedStopIndex(int pitStopsMade, int plannedStopCount)
        {
            // Two defects in the previous hard-coded form: a ZERO-stop plan
            // reported a pending first stop (pitStopsMade <= 0 returned 1 before
            // plannedStopCount was ever consulted), and any plan of three or more
            // silently dropped every stop past the second. Both were masked only
            // by the [1,2] clamp the settings layer happens to apply today.
            if (plannedStopCount <= 0)
            {
                return 0;
            }

            if (pitStopsMade < 0)
            {
                pitStopsMade = 0;
            }

            return pitStopsMade >= plannedStopCount ? 0 : pitStopsMade + 1;
        }

        /// <summary>Whether any planned stop is still outstanding.</summary>
        public static bool HasPendingPlannedStop(int pitStopsMade, int plannedStopCount)
        {
            return NextPlannedStopIndex(pitStopsMade, plannedStopCount) != 0;
        }
    }
}
