namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure weather- and track-condition rules for a race session: when a
    /// mixed-forecast weather swing fires, what it changes to, and how the racing
    /// line rubbers in (or is washed off by rain) over green-flag running. The
    /// race layer owns the live weather/track state and the engine calls; it asks
    /// these rules for the decisions so they stay deterministic and testable.
    /// Engine-free (F1Game.Race has no UnityEngine reference), so grip ramps and
    /// clamps are implemented here directly.
    /// </summary>
    public static class WeatherRules
    {
        /// <summary>Which scheduled weather swing (if any) should fire this tick.</summary>
        public enum TransitionPhase { None, First, Second }

        // Track evolution: the maximum multiplicative grip bonus a fully
        // rubbered-in line gives (applied identically to every car). Mirrors the
        // old RaceManager.TrackEvolutionMaxGripBonus.
        public const float TrackEvolutionMaxGripBonus = 0.05f;

        // Rubber ramps in slowly over green running and is scrubbed off fast when
        // the track is soaked (heavy rain keeps none of the built-up line).
        public const float RubberBuildRampSpeed = 0.012f;
        public const float RubberWashRampSpeed = 0.3f;

        /// <summary>
        /// A mixed-forecast race gets one swing at half distance, and (only at the
        /// High variability setting) a second at three-quarter distance. Returns
        /// the phase that should fire now given how much of the race is done and
        /// which swings have already happened.
        /// </summary>
        public static TransitionPhase EvaluateTransition(
            int variability, int completedLaps, int raceLaps, bool firstDone, bool secondDone)
        {
            if (variability <= 0)
            {
                return TransitionPhase.None;
            }

            if (!firstDone)
            {
                int halfway = Max(1, raceLaps / 2);
                return completedLaps >= halfway ? TransitionPhase.First : TransitionPhase.None;
            }

            bool wantSecondSwing = variability >= 3 && !secondDone;
            if (wantSecondSwing)
            {
                int threeQuarter = Max(1, (raceLaps * 3) / 4);
                return completedLaps >= threeQuarter ? TransitionPhase.Second : TransitionPhase.None;
            }

            return TransitionPhase.None;
        }

        /// <summary>
        /// The swing simply toggles wet↔dry: a raining track clears (to cloudy),
        /// a dry track picks up light rain. Returns whether the new state rains.
        /// </summary>
        public static bool NextIsRaining(bool currentlyRaining)
        {
            return !currentlyRaining;
        }

        /// <summary>
        /// Advances the track's rubber level toward fully rubbered (1) over green
        /// running, or toward bare (0) fast when washed by heavy rain.
        /// </summary>
        public static float RampRubberLevel(float currentRubber, bool washedByRain, float deltaTime)
        {
            float target = washedByRain ? 0f : 1f;
            float rampSpeed = washedByRain ? RubberWashRampSpeed : RubberBuildRampSpeed;
            return MoveTowards(currentRubber, target, deltaTime * rampSpeed);
        }

        /// <summary>The per-car grip multiplier for a given rubber level (1 at bare track).</summary>
        public static float GripMultiplier(float rubberLevel)
        {
            return 1f + rubberLevel * TrackEvolutionMaxGripBonus;
        }

        static int Max(int a, int b)
        {
            return a > b ? a : b;
        }

        static float MoveTowards(float current, float target, float maxDelta)
        {
            if (maxDelta < 0f)
            {
                maxDelta = 0f;
            }

            float diff = target - current;
            float magnitude = diff < 0f ? -diff : diff;
            if (magnitude <= maxDelta)
            {
                return target;
            }

            return current + (diff < 0f ? -maxDelta : maxDelta);
        }
    }
}
