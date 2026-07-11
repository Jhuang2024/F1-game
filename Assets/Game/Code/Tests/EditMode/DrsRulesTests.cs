using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// DrsRules is the live DRS-availability authority (RaceManager.IsDrsAvailable
    /// and EvaluateDrsDetectionGap delegate to it), so the detection-point gap
    /// decision and the availability ordering (wet/cooldown/flag → in-zone →
    /// session → laps → earned) are pinned here. Mirrors the pre-extraction inline
    /// behavior exactly.
    /// </summary>
    public class DrsRulesTests
    {
        [Test]
        public void QualifyingEarnsEveryZoneWithoutGap()
        {
            Assert.IsTrue(DrsRules.EarnsDetectionEligibility(true, 0, 999f));
        }

        [Test]
        public void RaceNeedsTwoLapsAndAOneSecondGap()
        {
            Assert.IsFalse(DrsRules.EarnsDetectionEligibility(false, 1, 0.5f));  // too early
            Assert.IsFalse(DrsRules.EarnsDetectionEligibility(false, 3, 1.5f));  // gap too big
            Assert.IsTrue(DrsRules.EarnsDetectionEligibility(false, 3, 1.0f));   // exactly one second
            Assert.IsTrue(DrsRules.EarnsDetectionEligibility(false, 2, 0.4f));   // within gap
        }

        static bool Available(bool wet = false, bool cooldown = false, bool flagAllows = true,
            int zone = 1, bool qualiOrTt = false, int laps = 5, bool z1 = true, bool z2 = true)
        {
            return DrsRules.IsAvailable(wet, cooldown, flagAllows, zone, qualiOrTt, laps, z1, z2);
        }

        [Test]
        public void HardGatesShutDrsOff()
        {
            Assert.IsFalse(Available(wet: true));
            Assert.IsFalse(Available(cooldown: true));
            Assert.IsFalse(Available(flagAllows: false));
        }

        [Test]
        public void OutsideAZoneIsUnavailable()
        {
            Assert.IsFalse(Available(zone: 0));
        }

        [Test]
        public void QualifyingIsAvailableInAnyZoneOnceGatesPass()
        {
            Assert.IsTrue(Available(qualiOrTt: true, laps: 0, z1: false, z2: false, zone: 2));
        }

        [Test]
        public void RaceRequiresTwoLapsThenEarnedZone()
        {
            Assert.IsFalse(Available(laps: 1));                       // too early
            Assert.IsTrue(Available(zone: 1, z1: true, z2: false));   // earned zone 1
            Assert.IsFalse(Available(zone: 1, z1: false, z2: true));  // not earned in zone 1
            Assert.IsTrue(Available(zone: 2, z1: false, z2: true));   // earned zone 2
            Assert.IsFalse(Available(zone: 2, z1: true, z2: false));  // not earned in zone 2
        }
    }
}
