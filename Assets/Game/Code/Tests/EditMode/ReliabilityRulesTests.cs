using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// ReliabilityRules is the live mechanical-failure authority (RaceManager's
    /// race-control tick delegates to it), so its mode gating, reliability→chance
    /// curve and per-window roll are pinned here. Values mirror the pre-extraction
    /// inline tuning exactly.
    /// </summary>
    public class ReliabilityRulesTests
    {
        [Test]
        public void ModeOffIsNeverEligible()
        {
            Assert.IsFalse(ReliabilityRules.IsEligible(0, false, false, false));
        }

        [Test]
        public void ModeAiOnlyExemptsThePlayer()
        {
            Assert.IsFalse(ReliabilityRules.IsEligible(1, true, false, false));  // player
            Assert.IsTrue(ReliabilityRules.IsEligible(1, false, false, false));  // AI
        }

        [Test]
        public void ModeEveryoneIncludesThePlayer()
        {
            Assert.IsTrue(ReliabilityRules.IsEligible(2, true, false, false));
        }

        [Test]
        public void PreRaceAndPaceLimitedAreExcluded()
        {
            Assert.IsFalse(ReliabilityRules.IsEligible(2, false, true, false));  // pre-race
            Assert.IsFalse(ReliabilityRules.IsEligible(2, false, false, true));  // under SC/VSC
        }

        [Test]
        public void FailureChanceInterpolatesByReliability()
        {
            Assert.AreEqual(ReliabilityRules.MaxPerSecondChance,
                ReliabilityRules.PerSecondFailureChance(0f), 1e-9f);
            Assert.AreEqual(ReliabilityRules.MinPerSecondChance,
                ReliabilityRules.PerSecondFailureChance(100f), 1e-9f);

            float mid = ReliabilityRules.PerSecondFailureChance(50f);
            float expectedMid = (ReliabilityRules.MaxPerSecondChance + ReliabilityRules.MinPerSecondChance) * 0.5f;
            Assert.AreEqual(expectedMid, mid, 1e-9f);
        }

        [Test]
        public void FailureChanceClampsOutOfRangeReliability()
        {
            Assert.AreEqual(ReliabilityRules.MaxPerSecondChance,
                ReliabilityRules.PerSecondFailureChance(-20f), 1e-9f);
            Assert.AreEqual(ReliabilityRules.MinPerSecondChance,
                ReliabilityRules.PerSecondFailureChance(150f), 1e-9f);
        }

        [Test]
        public void FailsThisCheckComparesRollAgainstScaledChance()
        {
            // Chance for a 50-rated car over a 0.25s window.
            float chance = ReliabilityRules.PerSecondFailureChance(50f) * 0.25f;
            Assert.IsTrue(ReliabilityRules.FailsThisCheck(50f, 0.25f, chance * 0.5f));  // roll below -> fails
            Assert.IsFalse(ReliabilityRules.FailsThisCheck(50f, 0.25f, chance * 2f));   // roll above -> survives
        }
    }
}
