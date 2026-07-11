namespace F1Game.Race.Rules
{
    /// <summary>
    /// Compound classes as the strategy rulebook sees them. Mirrors the
    /// monolith's <c>TyreCompound</c> (mapped at the boundary) the same way
    /// PitRequestRules mirrors the pit-request source.
    /// </summary>
    public enum StintCompound
    {
        Soft,
        Medium,
        Hard,
        Intermediate,
        Wet,
    }

    /// <summary>
    /// Pure AI pit-strategy decisions, extracted from the OR-chain in
    /// AiVehicleController's per-tick decision block. Wear throughout is
    /// REMAINING tyre life (1.0 fresh, 0 gone - see TyreState.Wear).
    /// The race layer supplies live state; thresholds and judgements live here.
    /// </summary>
    public static class AiPitStrategyRules
    {
        // Safety nets: never circulate on tyres that are essentially gone or
        // whose real grip has collapsed, whatever the plan says.
        public const float DestroyedTyreWear = 0.12f;
        public const float CollapsedGripMultiplier = 0.5f;

        // A planned strategy lap may only drag the car in once wear has
        // genuinely arrived near the routine point (a hair before it, so a
        // well-timed plan can fire slightly ahead of pure wear).
        public const float StrategyLapMaxWear = 0.45f;

        /// <summary>
        /// Routine stop threshold: centred on 0.40 remaining (~60% worn) with a
        /// small skill spread, shifted per compound - a Soft falls off a cliff
        /// and comes off earlier, a Hard runs leaner, wet-weather rubber
        /// degrades unpredictably once badly worn.
        /// </summary>
        public static float RoutinePitThreshold(float tyreManagement01, float tyreSavingBias, StintCompound compound)
        {
            float management = Clamp01(tyreManagement01);
            float threshold = 0.42f + (0.38f - 0.42f) * management + tyreSavingBias * 0.03f + CompoundThresholdShift(compound);
            return threshold < 0.2f ? 0.2f : (threshold > 0.8f ? 0.8f : threshold);
        }

        public static float CompoundThresholdShift(StintCompound compound)
        {
            switch (compound)
            {
                case StintCompound.Soft:
                    return 0.06f;
                case StintCompound.Hard:
                    return -0.05f;
                case StintCompound.Intermediate:
                case StintCompound.Wet:
                    return 0.04f;
                default:
                    return 0f;
            }
        }

        public static bool ShouldPitRoutine(float wearRemaining, float threshold)
        {
            return wearRemaining < threshold;
        }

        public static bool TyresEffectivelyGone(float wearRemaining)
        {
            return wearRemaining < DestroyedTyreWear;
        }

        public static bool GripCollapsed(float gripMultiplier)
        {
            return gripMultiplier < CollapsedGripMultiplier;
        }

        public static bool StrategyLapMayFire(float wearRemaining)
        {
            return wearRemaining < StrategyLapMaxWear;
        }

        /// <summary>
        /// Never pit on the final lap: a new stop only throws away track
        /// position the car cannot win back before the flag. Uses the lap being
        /// driven (completed + 1), so it engages the moment the last lap starts.
        /// </summary>
        public static bool FinalLapSuppressesNewRequest(int completedLaps, int raceLaps)
        {
            return raceLaps > 0 && completedLaps + 1 >= raceLaps;
        }

        // ---- Weather crossover -------------------------------------------------

        public static bool IsWetCompound(StintCompound compound)
        {
            return compound == StintCompound.Intermediate || compound == StintCompound.Wet;
        }

        /// <summary>The car is on the wrong class of rubber for the track state.</summary>
        public static bool WantsWeatherCrossover(bool trackWet, bool onWetCompound)
        {
            return trackWet != onWetCompound;
        }

        /// <summary>
        /// How long a driver watches a mismatch before committing to the box.
        /// Crossing TO wet rubber is urgent (the track is only getting worse);
        /// coming back to slicks waits longer for a genuinely dry line. Sharper
        /// awareness reacts sooner.
        /// </summary>
        public static float CrossoverReactionSeconds(bool crossingToWet, float awareness01)
        {
            float awareness = Clamp01(awareness01);
            return crossingToWet
                ? 14f + (6f - 14f) * awareness
                : 22f + (10f - 22f) * awareness;
        }

        /// <summary>Commit once the mismatch has persisted past this driver's reaction window.</summary>
        public static bool ShouldCrossover(bool trackWet, bool onWetCompound, float mismatchSeconds, float reactionSeconds)
        {
            return WantsWeatherCrossover(trackWet, onWetCompound) && mismatchSeconds >= reactionSeconds;
        }

        static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
