namespace F1Game.Race.Rules
{
    /// <summary>
    /// Engine-free damage-to-performance maths, extracted VERBATIM from DamageState's
    /// multiplier getters so the aero/handling/power falloff and the overall-damage
    /// percentage are unit-testable without a live DamageState. The tuned coefficients
    /// and floors are identical to the previous inline code (no retuning); DamageState
    /// keeps owning the impact accumulation (the feel-critical part) and delegates
    /// only these pure readouts. Inputs are the four per-component wear values (0..1:
    /// front wing, floor, engine wear, gearbox wear).
    /// </summary>
    public static class DamagePerformance
    {
        /// <summary>Aero effectiveness multiplier (front wing + floor damage), floored at 0.18.</summary>
        public static float AeroMultiplier(float frontWing, float floor) =>
            Clamp(1f - frontWing * 0.42f - floor * 0.28f, 0.18f, 1f);

        /// <summary>Handling multiplier (front wing + floor damage), floored at 0.2.</summary>
        public static float HandlingMultiplier(float frontWing, float floor) =>
            Clamp(1f - frontWing * 0.38f - floor * 0.34f, 0.2f, 1f);

        /// <summary>Power multiplier (engine + gearbox wear), floored at 0.24.</summary>
        public static float PowerMultiplier(float engineWear, float gearboxWear) =>
            Clamp(1f - engineWear * 0.42f - gearboxWear * 0.22f, 0.24f, 1f);

        /// <summary>Overall damage as a 0..100 percentage (mean of the four components, clamped).</summary>
        public static float OverallPercent(float frontWing, float floor, float engineWear, float gearboxWear) =>
            Clamp01((frontWing + floor + engineWear + gearboxWear) * 0.25f) * 100f;

        /// <summary>A car is undriveable at/above 98% overall damage.</summary>
        public static bool IsDestroyed(float overallPercent) => overallPercent >= 98f;

        // Mirror UnityEngine.Mathf.Clamp / Clamp01 exactly so the extracted numbers
        // are byte-for-byte identical to the previous Mathf-based getters.
        static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);

        static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}
