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
            Assert.AreEqual(1f, DamagePerformance.AeroMultiplier(0f, 0f, 0f), 1e-6f);
            Assert.AreEqual(1f, DamagePerformance.HandlingMultiplier(0f, 0f, 0f, 0f), 1e-6f);
            Assert.AreEqual(1f, DamagePerformance.PowerMultiplier(0f, 0f), 1e-6f);
            Assert.AreEqual(0f, DamagePerformance.OverallPercent(0f, 0f, 0f, 0f, 0f, 0f), 1e-6f);
            Assert.IsFalse(DamagePerformance.IsDestroyed(0f));
        }

        [Test]
        public void AeroAndHandlingFallWithBodyworkAndSuspension()
        {
            Assert.AreEqual(1f - 0.32f * 0.5f - 0.38f * 0.5f - 0.22f * 0.5f,
                DamagePerformance.AeroMultiplier(0.5f, 0.5f, 0.5f), 1e-6f);
            Assert.AreEqual(1f - 0.26f * 0.5f - 0.16f * 0.5f - 0.26f * 0.5f - 0.58f * 0.5f,
                DamagePerformance.HandlingMultiplier(0.5f, 0.5f, 0.5f, 0.5f), 1e-6f);
        }

        [Test]
        public void RearWingCostsMoreAeroThanTheFloor()
        {
            // The rear wing is the biggest aerodynamic device on the car, so losing it
            // must hurt more than losing the same fraction of floor.
            Assert.Less(DamagePerformance.AeroMultiplier(0f, 0.6f, 0f),
                        DamagePerformance.AeroMultiplier(0f, 0f, 0.6f));
        }

        [Test]
        public void SuspensionDominatesHandling()
        {
            // Broken suspension must be worse for handling than any single piece of
            // damaged bodywork - the contact patch itself stops working.
            Assert.Less(DamagePerformance.HandlingMultiplier(0f, 0f, 0f, 0.5f),
                        DamagePerformance.HandlingMultiplier(0.5f, 0.5f, 0.5f, 0f));
        }

        [Test]
        public void MechanicalFlagAndTerminalSuspensionThresholds()
        {
            Assert.IsFalse(DamagePerformance.RequiresMechanicalBlackOrange(0f, 0f));
            Assert.IsTrue(DamagePerformance.RequiresMechanicalBlackOrange(0.75f, 0f));  // hanging front wing
            Assert.IsTrue(DamagePerformance.RequiresMechanicalBlackOrange(0f, 0.75f));  // hanging rear wing

            Assert.IsFalse(DamagePerformance.SuspensionIsTerminal(0.5f));
            Assert.IsTrue(DamagePerformance.SuspensionIsTerminal(0.9f));
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
            Assert.AreEqual(0.18f, DamagePerformance.AeroMultiplier(10f, 10f, 10f), 1e-6f);
            Assert.AreEqual(0.2f, DamagePerformance.HandlingMultiplier(10f, 10f, 10f, 10f), 1e-6f);
            Assert.AreEqual(0.24f, DamagePerformance.PowerMultiplier(10f, 10f), 1e-6f);
        }

        [Test]
        public void OverallPercentAveragesTheSixComponents()
        {
            // All six at 0.5 -> mean 0.5 -> 50%.
            Assert.AreEqual(50f, DamagePerformance.OverallPercent(0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f), 1e-6f);
            // Clamped at 100%.
            Assert.AreEqual(100f, DamagePerformance.OverallPercent(1f, 1f, 1f, 1f, 1f, 1f), 1e-6f);
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
