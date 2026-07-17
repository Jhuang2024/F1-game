using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class PenaltyRulesTests
    {
        [Test]
        public void MandatoryPitPenaltyAppliesOnlyInEligibleRaces()
        {
            // Standard race, no stop made: penalty applies.
            Assert.IsTrue(PenaltyRules.ShouldApplyMandatoryPitPenalty(
                isQualifying: false, isTimeTrial: false, raceLaps: 20, pitStops: 0, alreadyApplied: false));

            // Any completed stop clears it.
            Assert.IsFalse(PenaltyRules.ShouldApplyMandatoryPitPenalty(false, false, 20, 1, false));

            // Qualifying and time trial never require a stop.
            Assert.IsFalse(PenaltyRules.ShouldApplyMandatoryPitPenalty(true, false, 20, 0, false));
            Assert.IsFalse(PenaltyRules.ShouldApplyMandatoryPitPenalty(false, true, 20, 0, false));

            // Sprint-length races are exempt (forcing 22 stops through one pit
            // lane in a handful of laps jams the pit entry); the constant is
            // the single source of truth for the boundary.
            Assert.IsFalse(PenaltyRules.ShouldApplyMandatoryPitPenalty(
                false, false, PenaltyRules.MandatoryPitMinimumRaceLaps - 1, 0, false));
            Assert.IsTrue(PenaltyRules.ShouldApplyMandatoryPitPenalty(
                false, false, PenaltyRules.MandatoryPitMinimumRaceLaps, 0, false));

            // Never applied twice.
            Assert.IsFalse(PenaltyRules.ShouldApplyMandatoryPitPenalty(false, false, 20, 0, true));
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
