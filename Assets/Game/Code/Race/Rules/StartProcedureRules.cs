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
    /// Pure start-procedure rules: light-sequence timing, jump/false-start
    /// judgement and start-type resolution. The race layer drives the clock
    /// (RaceManager.StartCountdown); this class owns the numbers and the
    /// outcomes so they are testable and stated in exactly one place.
    /// </summary>
    public static class StartProcedureRules
    {
        public const int LightCount = 5;

        // Race build-up: first light after a short beat, then one per step.
        // The total duration includes a randomized hold after all five lights
        // are lit, so a launch cannot be timed by rhythm alone.
        public const float FirstLightDelaySeconds = 0.55f;
        public const float LightStepSeconds = 0.48f;
        public const float MinRaceSequenceSeconds = 5.4f;
        public const float MaxRaceSequenceSeconds = 6.8f;

        // Qualifying / time trial: a short fixed hold, no light sequence.
        public const float NonRaceSequenceSeconds = 1.5f;

        // A "reaction" faster than humanly plausible reads as anticipation:
        // the driver was already committed before the lights went out.
        public const float AnticipationThresholdSeconds = 0.1f;

        /// <summary>Total build-up duration for a race start. unitRandom in [0,1] supplies the hold randomness.</summary>
        public static float RaceSequenceDuration(float unitRandom)
        {
            float t = unitRandom < 0f ? 0f : (unitRandom > 1f ? 1f : unitRandom);
            return MinRaceSequenceSeconds + (MaxRaceSequenceSeconds - MinRaceSequenceSeconds) * t;
        }

        /// <summary>Lit lights for a given time into the build-up sequence.</summary>
        public static int LitLightCount(float elapsedSeconds)
        {
            if (elapsedSeconds < FirstLightDelaySeconds)
            {
                return 0;
            }

            int lit = (int)((elapsedSeconds - FirstLightDelaySeconds) / LightStepSeconds) + 1;
            return lit > LightCount ? LightCount : lit;
        }

        /// <summary>A movement before lights-out is a jump start; a sub-threshold reaction is anticipation.</summary>
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

            if (reactionSeconds >= 0f && reactionSeconds < AnticipationThresholdSeconds)
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
