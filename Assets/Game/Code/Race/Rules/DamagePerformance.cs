namespace F1Game.Race.Rules
{
    /// <summary>
    /// Engine-free damage-to-performance maths. DamageState keeps owning the impact
    /// accumulation (the feel-critical part) and delegates these pure readouts.
    ///
    /// The car used to have four damageable components: front wing, floor, engine and
    /// gearbox. Two of the things that most often end a real grand prix were missing
    /// entirely, so the game could not represent them at all:
    ///
    /// - REAR WING. A rear-end hit or a barrier brush at the back of the car is one of
    ///   the most common pieces of race-ending damage in F1, and it is the component
    ///   that costs straight-line speed. Everything that struck the back of the car
    ///   used to be filed under "floor".
    /// - SUSPENSION. A kerb strike, a wheel-to-wheel launch or a heavy compression
    ///   breaks suspension, and it is the damage that is NOT repairable in a pit stop -
    ///   in reality it is a retirement. Modelling it separately is what lets a
    ///   black-and-orange flag mean something (see RaceFlag.MechanicalBlackOrange).
    /// </summary>
    public static class DamagePerformance
    {
        /// <summary>
        /// Aero effectiveness multiplier, floored at 0.18. The rear wing bites hardest
        /// of the three - it is the single biggest aerodynamic device on the car.
        /// </summary>
        public static float AeroMultiplier(float frontWing, float rearWing, float floor) =>
            Clamp(1f - frontWing * 0.32f - rearWing * 0.38f - floor * 0.22f, 0.18f, 1f);

        /// <summary>
        /// Handling multiplier, floored at 0.2. Broken suspension is the most
        /// destructive thing that can happen to how a car handles - far worse than
        /// bodywork, because the contact patch itself stops working.
        /// </summary>
        public static float HandlingMultiplier(float frontWing, float rearWing, float floor, float suspension) =>
            Clamp(1f - frontWing * 0.26f - rearWing * 0.16f - floor * 0.26f - suspension * 0.58f, 0.2f, 1f);

        /// <summary>Power multiplier (engine + gearbox wear), floored at 0.24.</summary>
        public static float PowerMultiplier(float engineWear, float gearboxWear) =>
            Clamp(1f - engineWear * 0.42f - gearboxWear * 0.22f, 0.24f, 1f);

        /// <summary>
        /// Overall damage as a 0..100 percentage - the mean of the six components,
        /// clamped.
        /// </summary>
        public static float OverallPercent(
            float frontWing, float rearWing, float floor, float suspension, float engineWear, float gearboxWear) =>
            Clamp01((frontWing + rearWing + floor + suspension + engineWear + gearboxWear) / 6f) * 100f;

        /// <summary>A car is undriveable at/above 98% overall damage.</summary>
        public static bool IsDestroyed(float overallPercent) => overallPercent >= 98f;

        /// <summary>
        /// Suspension damage past this point ends the car's race. There is deliberately
        /// no black-and-orange threshold for suspension: the flag means "come in and
        /// have it put right", and a pit stop cannot put suspension right. Flagging it
        /// would order a car in for a repair that does not exist, leave the flag
        /// permanently unclearable, and black-flag the whole field two laps later.
        /// </summary>
        public const float SuspensionTerminalThreshold = 0.82f;

        /// <summary>
        /// Bodywork hanging off the car - a loose front or rear wing endplate. THIS is
        /// the black-and-orange case, and the only one: the car is not slow enough to
        /// retire, it is dangerous to everyone behind it, and a stop genuinely fixes it.
        /// </summary>
        public const float BodyworkBlackOrangeThreshold = 0.7f;

        /// <summary>
        /// Whether the car must be shown the black-and-orange flag: report to the pits
        /// to have the loose bodywork put right. Repairable damage only, by design -
        /// see SuspensionTerminalThreshold.
        /// </summary>
        public static bool RequiresMechanicalBlackOrange(float frontWing, float rearWing) =>
            frontWing >= BodyworkBlackOrangeThreshold ||
            rearWing >= BodyworkBlackOrangeThreshold;

        /// <summary>
        /// Whether suspension damage has passed the point where the car cannot
        /// continue at all. A broken wishbone is not something a pit stop fixes.
        /// </summary>
        public static bool SuspensionIsTerminal(float suspension) => suspension >= SuspensionTerminalThreshold;

        // Mirror UnityEngine.Mathf.Clamp / Clamp01 exactly.
        static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);

        static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}
