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
    }
}
