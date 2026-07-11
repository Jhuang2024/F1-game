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
    }
}
