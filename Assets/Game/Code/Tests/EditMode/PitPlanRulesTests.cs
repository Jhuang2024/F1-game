using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class PitPlanRulesTests
    {
        [Test]
        public void FirstStopPendingBeforeAnyStop()
        {
            Assert.AreEqual(1, PitPlanRules.NextPlannedStopIndex(pitStopsMade: 0, plannedStopCount: 1));
            Assert.AreEqual(1, PitPlanRules.NextPlannedStopIndex(0, 2));
            // Guards negative just in case.
            Assert.AreEqual(1, PitPlanRules.NextPlannedStopIndex(-1, 1));
        }

        [Test]
        public void SecondStopOnlyWhenPlanCallsForIt()
        {
            // One stop made, two-stop plan -> stop 2 pending.
            Assert.AreEqual(2, PitPlanRules.NextPlannedStopIndex(1, 2));
            Assert.AreEqual(2, PitPlanRules.NextPlannedStopIndex(1, 3));
            // One stop made on a one-stop plan -> done.
            Assert.AreEqual(0, PitPlanRules.NextPlannedStopIndex(1, 1));
        }

        [Test]
        public void PlanCompleteAfterFinalStop()
        {
            Assert.AreEqual(0, PitPlanRules.NextPlannedStopIndex(2, 2));
            Assert.AreEqual(0, PitPlanRules.NextPlannedStopIndex(3, 2));
        }

        [Test]
        public void HasPendingMirrorsTheIndex()
        {
            Assert.IsTrue(PitPlanRules.HasPendingPlannedStop(0, 1));
            Assert.IsTrue(PitPlanRules.HasPendingPlannedStop(1, 2));
            Assert.IsFalse(PitPlanRules.HasPendingPlannedStop(1, 1));
            Assert.IsFalse(PitPlanRules.HasPendingPlannedStop(2, 2));
        }
    }
}
