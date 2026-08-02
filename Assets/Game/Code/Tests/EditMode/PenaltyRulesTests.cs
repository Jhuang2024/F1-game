using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class PenaltyRulesTests
    {
        [Test]
        public void TwoCompoundRuleDisqualifiesOnlyASingleDryCompoundInADryRace()
        {
            // The real dry-race rule: at least two different dry specifications, on
            // pain of disqualification. Replaces the old "mandatory pit stop + 30s"
            // rule, which is not an F1 regulation.
            Assert.IsTrue(PenaltyRules.ShouldDisqualifyForTwoCompoundRule(
                false, false, false, false, 20, 1, false), "one dry compound in a dry race is a DSQ");
            Assert.IsFalse(PenaltyRules.ShouldDisqualifyForTwoCompoundRule(
                false, false, false, false, 20, 2, false), "two dry compounds complies");

            // Pitting is irrelevant - a soft->soft stop is still one specification.
            // A wet race, or any wet-weather tyre used, voids the requirement.
            Assert.IsFalse(PenaltyRules.ShouldDisqualifyForTwoCompoundRule(
                false, false, true, false, 20, 1, false), "wet race voids the rule");
            Assert.IsFalse(PenaltyRules.ShouldDisqualifyForTwoCompoundRule(
                false, false, false, true, 20, 1, false), "running inters/wets voids the rule");

            // Never applies to qualifying, time trial, a retirement, or a race
            // shorter than the arcade floor.
            Assert.IsFalse(PenaltyRules.ShouldDisqualifyForTwoCompoundRule(true, false, false, false, 20, 1, false));
            Assert.IsFalse(PenaltyRules.ShouldDisqualifyForTwoCompoundRule(false, true, false, false, 20, 1, false));
            Assert.IsFalse(PenaltyRules.ShouldDisqualifyForTwoCompoundRule(false, false, false, false, 20, 1, true));
            Assert.IsFalse(PenaltyRules.ShouldDisqualifyForTwoCompoundRule(
                false, false, false, false, PenaltyRules.TwoCompoundMinimumRaceLaps - 1, 1, false));
        }

        [Test]
        public void IgnoredBlueFlagPenalisedOncePerEpisode()
        {
            Assert.IsFalse(PenaltyRules.ShouldPenaliseIgnoredBlueFlag(PenaltyRules.BlueFlagComplianceSeconds - 0.1f, false));
            Assert.IsTrue(PenaltyRules.ShouldPenaliseIgnoredBlueFlag(PenaltyRules.BlueFlagComplianceSeconds, false));
            Assert.IsFalse(PenaltyRules.ShouldPenaliseIgnoredBlueFlag(999f, true));
        }

        [Test]
        public void TrackLimitThresholdsMatchDetection()
        {
            float halfWidth = 6f;

            Assert.IsFalse(PenaltyRules.IsOutsideTrackLimits(6.4f, halfWidth));
            Assert.IsTrue(PenaltyRules.IsOutsideTrackLimits(6.6f, halfWidth));

            // Gaining time needs both the wider excursion and real speed.
            Assert.IsFalse(PenaltyRules.IsGainingTime(6.8f, halfWidth, 120f));
            Assert.IsTrue(PenaltyRules.IsGainingTime(7.1f, halfWidth, 120f));
            Assert.IsFalse(PenaltyRules.IsGainingTime(7.1f, halfWidth, 50f));
        }

        [Test]
        public void ThreeWarningsTriggerThePenalty()
        {
            Assert.IsFalse(PenaltyRules.ShouldPenaliseTrackLimits(2));
            Assert.IsTrue(PenaltyRules.ShouldPenaliseTrackLimits(3));
            Assert.IsTrue(PenaltyRules.ShouldPenaliseTrackLimits(4));
        }

        [Test]
        public void PenaltyReasonsAccumulateWithDedup()
        {
            string reason = PenaltyRules.AppendPenaltyReason(null, "Track limits");
            Assert.AreEqual("Track limits", reason);

            reason = PenaltyRules.AppendPenaltyReason(reason, "No mandatory stop");
            Assert.AreEqual("Track limits, No mandatory stop", reason);

            // Repeats are dropped, matching the monolith's Contains check.
            reason = PenaltyRules.AppendPenaltyReason(reason, "Track limits");
            Assert.AreEqual("Track limits, No mandatory stop", reason);
        }
    }
}
