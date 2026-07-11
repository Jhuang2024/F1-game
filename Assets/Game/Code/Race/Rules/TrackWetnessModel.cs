namespace F1Game.Race.Rules
{
    /// <summary>
    /// Engine-free continuous track-wetness model - a structural advance over the
    /// current discrete Clear/Cloudy/LightRain/HeavyRain weather states. Wetness is a
    /// single 0 (bone dry) .. 1 (standing water) value that eases toward the target
    /// the current rain intensity implies: it wets while rain outpaces the surface and
    /// dries when the rain eases, with the racing line drying faster than off-line
    /// (rubbered/driven rubber sheds water sooner). Also maps wetness to a slick-tyre
    /// grip multiplier. Pure and unit-testable; NOT yet wired into the live grip path
    /// (that stays on the tuned discrete model until validated), so nothing about the
    /// current feel changes - this is the model + seam for a future dynamic-wetness
    /// pass behind a default-off switch.
    /// </summary>
    public static class TrackWetnessModel
    {
        /// <summary>Wetting speed (wetness/second) while the surface is drier than the rain implies.</summary>
        public const float WettingRatePerSecond = 0.05f;

        /// <summary>Base drying speed (wetness/second) off the racing line.</summary>
        public const float BaseDryingRatePerSecond = 0.01f;

        /// <summary>Extra drying speed on the racing line (driven rubber sheds water faster).</summary>
        public const float RacingLineDryingBonus = 0.03f;

        /// <summary>Slick-tyre grip lost at full wetness (grip multiplier floor = 1 - this).</summary>
        public const float MaxWetGripPenalty = 0.45f;

        /// <summary>The equilibrium wetness a given rain intensity (0..1) drives the surface toward.</summary>
        public static float TargetWetness(float rainIntensity01) => Clamp01(rainIntensity01);

        /// <summary>
        /// Advance <paramref name="wetness"/> toward the target implied by
        /// <paramref name="rainIntensity01"/> over <paramref name="deltaTime"/> seconds.
        /// Wets at the wetting rate when the target is higher; dries at the base rate
        /// (plus the racing-line bonus when <paramref name="onRacingLine"/>) when it is
        /// lower; never overshoots the target and stays within [0,1].
        /// </summary>
        public static float Step(float wetness, float rainIntensity01, bool onRacingLine, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return Clamp01(wetness);
            }

            float current = Clamp01(wetness);
            float target = TargetWetness(rainIntensity01);

            if (target > current)
            {
                float next = current + WettingRatePerSecond * deltaTime;
                return next > target ? target : next;
            }

            if (target < current)
            {
                float rate = BaseDryingRatePerSecond + (onRacingLine ? RacingLineDryingBonus : 0f);
                float next = current - rate * deltaTime;
                return next < target ? target : next;
            }

            return current;
        }

        /// <summary>
        /// Slick-tyre grip multiplier for a wetness level: 1 on a dry track, falling
        /// linearly to (1 - <see cref="MaxWetGripPenalty"/>) at full wetness.
        /// </summary>
        public static float GripMultiplier(float wetness)
        {
            return 1f - Clamp01(wetness) * MaxWetGripPenalty;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
