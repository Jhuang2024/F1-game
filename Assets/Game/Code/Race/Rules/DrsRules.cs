namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure DRS availability rules. Two decisions live here: whether a car earns
    /// a zone's eligibility at its detection point (the one-second gap check), and
    /// whether DRS is available to open right now given that earned eligibility
    /// plus the live gates (weather, restart cooldown, flag permission, which
    /// zone, session type, laps completed). The race layer resolves the live state
    /// and hands in primitives, so the ordering of the rule is stated once here
    /// and stays testable. Engine-free (F1Game.Race has no UnityEngine reference).
    /// </summary>
    public static class DrsRules
    {
        // A car must be within one second of the car ahead at the detection point,
        // and DRS arms from the second lap on (one completed). The real regulation
        // waits two racing laps, but with this game's much shorter race lengths a
        // two-lap lockout left DRS unavailable for most of the overtaking a race
        // actually contains - it read as simply broken. Qualifying and time trial
        // ignore the gap entirely.
        public const float ActivationGapSeconds = 1f;
        public const int MinCompletedLapsForGap = 1;

        /// <summary>
        /// The detection-point decision, made once as the car crosses the point.
        /// In qualifying/time trial every zone is earned with no car-ahead
        /// requirement; in a race the car needs two completed laps and a gap to the
        /// car ahead within one second.
        /// </summary>
        public static bool EarnsDetectionEligibility(bool isQualifyingOrTimeTrial, int completedLaps, float intervalToAheadSeconds)
        {
            if (isQualifyingOrTimeTrial)
            {
                return true;
            }

            if (completedLaps < MinCompletedLapsForGap)
            {
                return false;
            }

            return intervalToAheadSeconds <= ActivationGapSeconds;
        }

        /// <summary>
        /// Whether DRS is available to open this frame. Wet weather, an active
        /// post-restart cooldown or a flag that forbids DRS shut it off outright;
        /// otherwise the car must be inside a DRS zone (index 1 or 2) that it has
        /// earned. Qualifying/time trial need no earned gap; a race needs two
        /// completed laps and the zone's detection-point eligibility (which is NOT
        /// re-checked against the live gap - once earned at the line it holds for
        /// the whole zone).
        /// </summary>
        public static bool IsAvailable(
            bool isWet,
            bool restartCooldownActive,
            bool flagAllowsDrs,
            int zoneIndex,
            bool isQualifyingOrTimeTrial,
            int completedLaps,
            bool zoneOneEligible,
            bool zoneTwoEligible)
        {
            if (isWet || restartCooldownActive || !flagAllowsDrs)
            {
                return false;
            }

            if (zoneIndex == 0)
            {
                return false;
            }

            if (isQualifyingOrTimeTrial)
            {
                return true;
            }

            if (completedLaps < MinCompletedLapsForGap)
            {
                return false;
            }

            return zoneIndex == 1 ? zoneOneEligible : zoneTwoEligible;
        }
    }
}
