namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure pit-service timing rules: how long a stop holds the car in its box.
    /// A stop is the tyre change, plus repair time when the car arrives damaged
    /// (the stop is also what repairs it - see VehicleController.CompletePitStop),
    /// plus an occasional crew fumble. The race layer supplies the random rolls
    /// (each in [0,1]) so outcomes stay deterministic and testable here.
    /// </summary>
    public static class PitServiceRules
    {
        // Tyre change: the visible wheel-off/wheel-on window. The player's crew
        // is slightly sharper than the AI teams' spread, matching the original
        // live tuning.
        public const float PlayerMinTyreSeconds = 1.8f;
        public const float PlayerMaxTyreSeconds = 2.6f;
        public const float AiMinTyreSeconds = 2.0f;
        public const float AiMaxTyreSeconds = 3.0f;

        // Repair work (front wing / bodywork) holds the car beyond the tyre
        // change once damage is worth fixing; below the threshold the crew
        // leaves it (a scuff costs nothing).
        public const float RepairDamageThresholdPercent = 12f;
        public const float MinRepairSeconds = 3f;
        public const float MaxRepairSeconds = 7.5f;

        // Crew error: a cross-threaded nut or slow jack. Rare, costs real time.
        public const float CrewErrorChance = 0.04f;
        public const float MinCrewErrorSeconds = 1.5f;
        public const float MaxCrewErrorSeconds = 4f;

        public static float TyreChangeSeconds(bool playerCrew, float unitRandom)
        {
            float t = Clamp01(unitRandom);
            return playerCrew
                ? PlayerMinTyreSeconds + (PlayerMaxTyreSeconds - PlayerMinTyreSeconds) * t
                : AiMinTyreSeconds + (AiMaxTyreSeconds - AiMinTyreSeconds) * t;
        }

        /// <summary>Extra hold for repairing damagePercent (0-100) of overall damage; 0 below the threshold.</summary>
        public static float RepairSeconds(float damagePercent, float unitRandom)
        {
            if (damagePercent < RepairDamageThresholdPercent)
            {
                return 0f;
            }

            float severity = (damagePercent - RepairDamageThresholdPercent) /
                             (100f - RepairDamageThresholdPercent);
            severity = Clamp01(severity);
            float baseSeconds = MinRepairSeconds + (MaxRepairSeconds - MinRepairSeconds) * severity;
            // Small crew-to-crew jitter so identical damage doesn't produce
            // identical stops.
            return baseSeconds * (0.9f + 0.2f * Clamp01(unitRandom));
        }

        /// <summary>Fumble time; chanceRoll decides IF it happens, severityRoll how bad it is.</summary>
        public static float CrewErrorSeconds(float chanceRoll, float severityRoll)
        {
            if (Clamp01(chanceRoll) >= CrewErrorChance)
            {
                return 0f;
            }

            return MinCrewErrorSeconds + (MaxCrewErrorSeconds - MinCrewErrorSeconds) * Clamp01(severityRoll);
        }

        static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
