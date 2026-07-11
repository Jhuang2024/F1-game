namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure mechanical-reliability rules: whether a car is eligible for a random
    /// race-ending failure this session, and how likely one is per second given
    /// the car's reliability stat. Deliberately rare - over a full race a failure
    /// is a talking point, not a routine event. The race layer supplies the
    /// random roll and makes the engine calls, so this stays deterministic and
    /// testable. Engine-free (F1Game.Race has no UnityEngine reference).
    /// </summary>
    public static class ReliabilityRules
    {
        // Per-second failure chance is interpolated by reliability: a 0-rated car
        // sits at the max, a 100-rated car at the min. Values mirror the
        // pre-extraction inline tuning (already halved in Part 2).
        public const float MaxPerSecondChance = 0.000015f;
        public const float MinPerSecondChance = 0.000001f;

        // Reliability assumed when a car carries no car data.
        public const float DefaultReliability = 88f;

        /// <summary>
        /// Mechanical failures are off (mode 0), AI-only (mode 1, the player is
        /// exempt) or everyone (mode 2). A car is never eligible before the race
        /// has gone green or while it is pace-limited under SC/VSC (its pace is
        /// race control's own doing, not a fault).
        /// </summary>
        public static bool IsEligible(int mechanicalMode, bool isPlayer, bool preRace, bool paceLimited)
        {
            return mechanicalMode != 0
                && !(mechanicalMode == 1 && isPlayer)
                && !preRace
                && !paceLimited;
        }

        /// <summary>Per-second failure probability for a given reliability (0-100).</summary>
        public static float PerSecondFailureChance(float reliability)
        {
            float t = Clamp01(reliability / 100f);
            return MaxPerSecondChance + (MinPerSecondChance - MaxPerSecondChance) * t;
        }

        /// <summary>
        /// Whether a failure fires in one race-control check window, given the
        /// window length and a caller-supplied random roll in [0,1).
        /// </summary>
        public static bool FailsThisCheck(float reliability, float checkIntervalSeconds, float roll)
        {
            return roll < PerSecondFailureChance(reliability) * checkIntervalSeconds;
        }

        static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
