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
        /// <summary>
        /// The pit lane speed limit, in km/h. One number, applying from the pit
        /// entry speed-limit line to the pit exit line, for the whole pit lane and
        /// every session - which is how the real regulation reads.
        ///
        /// The game previously ran FOUR different pit speeds, none of them the real
        /// one: 105 on the entry leg, 75 past the boxes, 106 on exit and a 108 cap,
        /// while the painted sign on track said "80" and the engineer radio said
        /// "limiter is 105 km/h". The real limit is 80 km/h at almost every circuit.
        ///
        /// A few venues run 60 km/h instead (Monaco and Singapore are the ones I am
        /// confident about; the FIA sets it per event, so this should become circuit
        /// data rather than a constant if per-track limits are wanted). Left as a
        /// single constant here rather than guessing at an incomplete circuit list.
        ///
        /// This is the input to pit-lane time loss, and therefore to every undercut,
        /// overcut and safety-car stop decision in the game.
        /// </summary>
        public const float PitLaneSpeedLimitKph = 80f;

        /// <summary>
        /// How far over the limit a car may run before it counts as speeding. Real
        /// timing measures an average and allows only a whisker; 1 km/h is a fair
        /// game-side tolerance.
        /// </summary>
        public const float PitLaneSpeedToleranceKph = 1f;

        /// <summary>Time penalty for speeding in the pit lane during a race.</summary>
        public const float PitLaneSpeedingPenaltySeconds = 5f;

        // Tyre change: the visible wheel-off/wheel-on window. One shared range
        // for every crew (per request - "AI should have the same range of pit
        // stop times as me, 1.8-3.0 for everyone"): the player's crew is no
        // longer sharper than the AI teams', the roll alone decides.
        public const float PlayerMinTyreSeconds = 1.8f;
        public const float PlayerMaxTyreSeconds = 3.0f;
        public const float AiMinTyreSeconds = 1.8f;
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
