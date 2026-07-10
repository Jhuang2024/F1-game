namespace F1Game.Race.Rules
{
    public enum StartType
    {
        Standing,
        Rolling,
        SafetyCarStart,   // wet / low-visibility
        PitLaneStart,
    }

    public enum StartInfraction
    {
        None,
        JumpStart,        // moved before lights out
        FalseStart,       // anticipation within reaction window
        OutOfPosition,    // not within the grid box
    }

    /// <summary>
    /// Pure start-procedure rules: formation, light sequence and jump/false-start
    /// judgement. The race layer drives the timing; this decides the outcome.
    /// </summary>
    public static class StartProcedureRules
    {
        /// <summary>Standard 5-light sequence: lights come on at 1 s intervals, then a random hold before out.</summary>
        public const int LightCount = 5;
        public const float LightIntervalSeconds = 1f;
        public const float MinHoldSeconds = 0.2f;
        public const float MaxHoldSeconds = 3f;

        /// <summary>A movement before lights-out is a jump start.</summary>
        public static StartInfraction Judge(bool movedBeforeLightsOut, float reactionSeconds, bool inGridBox)
        {
            if (movedBeforeLightsOut)
            {
                return StartInfraction.JumpStart;
            }

            if (!inGridBox)
            {
                return StartInfraction.OutOfPosition;
            }

            // A "reaction" faster than humanly plausible reads as anticipation.
            if (reactionSeconds >= 0f && reactionSeconds < 0.1f)
            {
                return StartInfraction.FalseStart;
            }

            return StartInfraction.None;
        }

        /// <summary>Penalty seconds for a start infraction (0 = none / grid-based handled elsewhere).</summary>
        public static float PenaltySeconds(StartInfraction infraction)
        {
            switch (infraction)
            {
                case StartInfraction.JumpStart:
                case StartInfraction.FalseStart:
                    return 5f;
                default:
                    return 0f;
            }
        }

        /// <summary>A safety-car start is forced when conditions are too wet for a standing start.</summary>
        public static StartType ResolveStartType(bool heavyRain, bool lowVisibility, bool pitLaneStartElected)
        {
            if (pitLaneStartElected)
            {
                return StartType.PitLaneStart;
            }

            if (heavyRain || lowVisibility)
            {
                return StartType.SafetyCarStart;
            }

            return StartType.Standing;
        }
    }
}
