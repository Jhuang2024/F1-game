using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// Pins the damage-to-performance curve (aero/handling/power falloff, overall
    /// percentage, destroyed threshold) exactly as DamageState exposed it, so a
    /// refactor can't silently change how damage bites into car performance.
    /// </summary>
    public class DamagePerformanceTests
    {
        [Test]
        public void UndamagedCarHasFullPerformance()
        {
            Assert.AreEqual(1f, DamagePerformance.AeroMultiplier(0f, 0f), 1e-6f);
            Assert.AreEqual(1f, DamagePerformance.HandlingMultiplier(0f, 0f), 1e-6f);
            Assert.AreEqual(1f, DamagePerformance.PowerMultiplier(0f, 0f), 1e-6f);
            Assert.AreEqual(0f, DamagePerformance.OverallPercent(0f, 0f, 0f, 0f), 1e-6f);
            Assert.IsFalse(DamagePerformance.IsDestroyed(0f));
        }

        [Test]
        public void AeroAndHandlingFallWithFrontWingAndFloor()
        {
            Assert.AreEqual(1f - 0.42f * 0.5f - 0.28f * 0.5f, DamagePerformance.AeroMultiplier(0.5f, 0.5f), 1e-6f);
            Assert.AreEqual(1f - 0.38f * 0.5f - 0.34f * 0.5f, DamagePerformance.HandlingMultiplier(0.5f, 0.5f), 1e-6f);
        }

        [Test]
        public void PowerFallsWithEngineAndGearboxWear()
        {
            Assert.AreEqual(1f - 0.42f * 0.6f - 0.22f * 0.4f, DamagePerformance.PowerMultiplier(0.6f, 0.4f), 1e-6f);
        }

        [Test]
        public void MultipliersRespectTheirFloors()
        {
            // Values beyond the normalised live range still cannot cross the
            // tuned defensive floors.
            Assert.AreEqual(0.18f, DamagePerformance.AeroMultiplier(10f, 10f), 1e-6f);
            Assert.AreEqual(0.2f, DamagePerformance.HandlingMultiplier(10f, 10f), 1e-6f);
            Assert.AreEqual(0.24f, DamagePerformance.PowerMultiplier(10f, 10f), 1e-6f);
        }

        [Test]
        public void OverallPercentAveragesTheFourComponents()
        {
            // All four at 0.5 -> mean 0.5 -> 50%.
            Assert.AreEqual(50f, DamagePerformance.OverallPercent(0.5f, 0.5f, 0.5f, 0.5f), 1e-6f);
            // Clamped at 100%.
            Assert.AreEqual(100f, DamagePerformance.OverallPercent(1f, 1f, 1f, 1f), 1e-6f);
        }

        [Test]
        public void DestroyedAtNinetyEightPercent()
        {
            Assert.IsFalse(DamagePerformance.IsDestroyed(97.9f));
            Assert.IsTrue(DamagePerformance.IsDestroyed(98f));
            Assert.IsTrue(DamagePerformance.IsDestroyed(100f));
        }
    }
}
