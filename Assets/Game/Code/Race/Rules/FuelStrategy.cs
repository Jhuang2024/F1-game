namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure fuel-projection maths, extracted verbatim from
    /// <c>VehicleController.UpdateFuelProjection</c> so the "will this load make
    /// the finish" reasoning is stated and tested in one place. All masses in kg,
    /// laps include the fractional lap in progress. No engine dependency.
    /// </summary>
    public static class FuelStrategy
    {
        /// <summary>Fuel needed to cover the remaining laps at the current per-lap burn.</summary>
        public static float NeededKg(float remainingLaps, float perLapKg)
        {
            float laps = remainingLaps < 0f ? 0f : remainingLaps;
            return laps * perLapKg;
        }

        /// <summary>Surplus (+) or shortfall (-) in kg against what the finish needs.</summary>
        public static float DeltaKg(float fuelKg, float remainingLaps, float perLapKg)
        {
            return fuelKg - NeededKg(remainingLaps, perLapKg);
        }

        /// <summary>
        /// Surplus/shortfall expressed in laps (the HUD's fuel delta). 0 when the
        /// per-lap estimate is not yet meaningful, matching the live guard.
        /// </summary>
        public static float DeltaLaps(float fuelKg, float remainingLaps, float perLapKg)
        {
            if (perLapKg <= 0.001f)
            {
                return 0f;
            }

            return DeltaKg(fuelKg, remainingLaps, perLapKg) / perLapKg;
        }

        /// <summary>
        /// The per-lap burn a driver can afford and still make the finish -
        /// the lift-and-coast target. 0 when no laps remain.
        /// </summary>
        public static float SaveTargetPerLapKg(float fuelKg, float remainingLaps)
        {
            if (remainingLaps <= 0.001f)
            {
                return 0f;
            }

            float fuel = fuelKg < 0f ? 0f : fuelKg;
            return fuel / remainingLaps;
        }

        /// <summary>
        /// Distance-scaled per-lap fuel burn, extracted verbatim from
        /// <c>RaceManager.EstimateFuelPerLapKg</c>. <paramref name="trackLengthMeters"/>
        /// falls back to the game's ~7266 m reference when non-positive (matching the
        /// live &gt;1 m guard); <paramref name="difficultyFactor01"/> is the 0-1 tier
        /// factor (Easy 0, Medium 0.33, Hard 0.66, Expert 1) the caller maps from the
        /// difficulty enum. The 1.35-1.65 short/long band and the 0.97-1.06 difficulty
        /// nudge mirror the pre-extraction inline maths exactly.
        /// </summary>
        public static float PerLapBurnKg(float trackLengthMeters, float difficultyFactor01)
        {
            float trackLength = trackLengthMeters > 1f ? trackLengthMeters : 7266f;
            float perLap = Lerp(1.35f, 1.65f, Clamp01(InverseLerp(5000f, 9688f, trackLength)));
            perLap *= Lerp(0.97f, 1.06f, difficultyFactor01);
            return perLap;
        }

        /// <summary>
        /// Race start-fuel load, extracted verbatim from
        /// <c>RaceManager.ComputeRaceStartFuelKg</c>: the finish target
        /// (perLap * laps + reserve) shifted by the chosen fuel-load plan's lap delta
        /// (<paramref name="loadDeltaLaps"/>, already in laps) then clamped to the
        /// [min,max] tank window. <paramref name="raceLaps"/> is floored at 1.
        /// </summary>
        public static float StartFuelKg(float perLapKg, int raceLaps, float reserveKg,
            float loadDeltaLaps, float minKg, float maxKg)
        {
            int laps = raceLaps < 1 ? 1 : raceLaps;
            float targetFuelKg = perLapKg * laps + reserveKg;
            float choiceFuelKg = targetFuelKg + loadDeltaLaps * perLapKg;
            return Clamp(choiceFuelKg, minKg, maxKg);
        }

        // Engine-free equivalents of the UnityEngine.Mathf helpers the inline maths
        // used, so the extracted formulas stay byte-identical without a UnityEngine
        // reference (this assembly is noEngineReferences).
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        static float InverseLerp(float a, float b, float v) => a == b ? 0f : Clamp01((v - a) / (b - a));
    }
}
