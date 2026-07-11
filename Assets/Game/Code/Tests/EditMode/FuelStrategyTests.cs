using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class FuelStrategyTests
    {
        [Test]
        public void NeededScalesWithLapsAndClampsNegative()
        {
            Assert.AreEqual(10f, FuelStrategy.NeededKg(5f, 2f), 0.0001f);
            Assert.AreEqual(0f, FuelStrategy.NeededKg(-3f, 2f), 0.0001f);
            Assert.AreEqual(0f, FuelStrategy.NeededKg(0f, 2f), 0.0001f);
        }

        [Test]
        public void DeltaKgIsSurplusMinusNeed()
        {
            // 20 kg on board, 5 laps at 2 kg/lap needs 10 -> +10 surplus.
            Assert.AreEqual(10f, FuelStrategy.DeltaKg(20f, 5f, 2f), 0.0001f);
            // Short: 6 kg, 5 laps at 2 -> -4.
            Assert.AreEqual(-4f, FuelStrategy.DeltaKg(6f, 5f, 2f), 0.0001f);
        }

        [Test]
        public void DeltaLapsMatchesTheLiveFormulaAndGuards()
        {
            // Same numbers as the live UpdateFuelProjection: delta kg / per-lap.
            Assert.AreEqual(5f, FuelStrategy.DeltaLaps(20f, 5f, 2f), 0.0001f);   // +10 kg / 2
            Assert.AreEqual(-2f, FuelStrategy.DeltaLaps(6f, 5f, 2f), 0.0001f);   // -4 kg / 2

            // Non-meaningful per-lap estimate returns 0, matching the guard.
            Assert.AreEqual(0f, FuelStrategy.DeltaLaps(20f, 5f, 0f), 0.0001f);
            Assert.AreEqual(0f, FuelStrategy.DeltaLaps(20f, 5f, 0.001f), 0.0001f);
        }

        [Test]
        public void SaveTargetIsAffordablePerLapBurn()
        {
            // 12 kg, 4 laps -> can afford 3 kg/lap.
            Assert.AreEqual(3f, FuelStrategy.SaveTargetPerLapKg(12f, 4f), 0.0001f);
            // No laps left, or empty tank: 0.
            Assert.AreEqual(0f, FuelStrategy.SaveTargetPerLapKg(12f, 0f), 0.0001f);
            Assert.AreEqual(0f, FuelStrategy.SaveTargetPerLapKg(-5f, 4f), 0.0001f);
        }

        [Test]
        public void PerLapBurnMatchesTheInlineBandAndFallback()
        {
            // Non-positive length falls back to the ~7266 m reference (the old
            // "track != null && track.length > 1f ? ... : 7266f" guard), which sits
            // ~0.483 through the 5000-9688 m band -> ~1.495 kg/lap before difficulty.
            // At Easy (factor 0) the difficulty nudge is *0.97.
            Assert.AreEqual(1.495008f * 0.97f, FuelStrategy.PerLapBurnKg(0f, 0f), 0.0005f);
            Assert.AreEqual(1.495008f * 0.97f, FuelStrategy.PerLapBurnKg(1f, 0f), 0.0005f);

            // Band ends: 5000 m -> 1.35 floor, 9688 m -> 1.65 ceiling (before nudge).
            Assert.AreEqual(1.35f * 0.97f, FuelStrategy.PerLapBurnKg(5000f, 0f), 0.0005f);
            Assert.AreEqual(1.65f * 1.06f, FuelStrategy.PerLapBurnKg(9688f, 1f), 0.0005f);

            // Out-of-band lengths clamp rather than extrapolate.
            Assert.AreEqual(1.35f * 0.97f, FuelStrategy.PerLapBurnKg(1000f, 0f), 0.0005f);
            Assert.AreEqual(1.65f * 0.97f, FuelStrategy.PerLapBurnKg(100000f, 0f), 0.0005f);

            // Difficulty is monotonic: a sharper field burns more per lap.
            Assert.Greater(FuelStrategy.PerLapBurnKg(7000f, 1f), FuelStrategy.PerLapBurnKg(7000f, 0f));
        }

        [Test]
        public void StartFuelIsTargetPlusChoiceShiftClampedToTank()
        {
            // 2 kg/lap * 10 laps + 1.2 reserve = 21.2, no choice shift.
            Assert.AreEqual(21.2f, FuelStrategy.StartFuelKg(2f, 10, 1.2f, 0f, 4.5f, 220f), 0.0001f);
            // Heavy (+1.5 laps) adds 1.5 * perLap; aggressive (-1.5) subtracts it.
            Assert.AreEqual(21.2f + 1.5f * 2f, FuelStrategy.StartFuelKg(2f, 10, 1.2f, 1.5f, 4.5f, 220f), 0.0001f);
            Assert.AreEqual(21.2f - 1.5f * 2f, FuelStrategy.StartFuelKg(2f, 10, 1.2f, -1.5f, 4.5f, 220f), 0.0001f);

            // Floor and ceiling clamps to the tank window.
            Assert.AreEqual(4.5f, FuelStrategy.StartFuelKg(0.1f, 1, 0f, -10f, 4.5f, 220f), 0.0001f);
            Assert.AreEqual(220f, FuelStrategy.StartFuelKg(100f, 10, 0f, 0f, 4.5f, 220f), 0.0001f);

            // raceLaps is floored at 1 (0 laps still bills one lap of fuel).
            Assert.AreEqual(3.2f, FuelStrategy.StartFuelKg(2f, 0, 1.2f, 0f, 0f, 220f), 0.0001f);
            Assert.AreEqual(3.2f, FuelStrategy.StartFuelKg(2f, 1, 1.2f, 0f, 0f, 220f), 0.0001f);
        }
    }
}
