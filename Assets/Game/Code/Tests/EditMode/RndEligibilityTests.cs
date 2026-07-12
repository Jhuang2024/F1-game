using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// RndEligibility must mirror the CareerManager R&D mutation preconditions exactly
    /// (department max/affordability, project tier/slot/ownership/prerequisite/cost,
    /// rework state/slot/cost), so the production UI's button state never lets a
    /// submission through that the authoritative mutation would reject. Pinned here.
    /// </summary>
    public class RndEligibilityTests
    {
        [Test]
        public void DepartmentUpgradeRespectsMaxAndAffordability()
        {
            // Cost = currentLevel * 400 (CarDevelopmentRules). Level 2 -> 800 RP.
            Assert.IsTrue(RndEligibility.CanUpgradeDepartment(2, 5, 800));
            Assert.IsFalse(RndEligibility.CanUpgradeDepartment(2, 5, 799)); // can't afford
            Assert.IsFalse(RndEligibility.CanUpgradeDepartment(5, 5, 100000)); // maxed
        }

        [Test]
        public void MeetsTierTreatsTierBelowOneAsOne()
        {
            Assert.IsTrue(RndEligibility.MeetsTier(1, 0));   // tier<1 clamps to 1
            Assert.IsTrue(RndEligibility.MeetsTier(3, 3));
            Assert.IsFalse(RndEligibility.MeetsTier(2, 3));
        }

        [Test]
        public void HasFreeSlotIsStrictlyLessThanMax()
        {
            Assert.IsTrue(RndEligibility.HasFreeSlot(1, 2));
            Assert.IsFalse(RndEligibility.HasFreeSlot(2, 2));
        }

        [Test]
        public void CanStartProjectChecksEveryPrecondition()
        {
            // Baseline eligible: 1000 RP, cost 600, dept 3 >= tier 3, 1/2 slots, fresh, prereq met.
            Assert.IsTrue(RndEligibility.CanStartProject(1000, 600, 3, 3, 1, 2, alreadyOwnedOrActive: false, prerequisiteMet: true));
            // Already owned/active.
            Assert.IsFalse(RndEligibility.CanStartProject(1000, 600, 3, 3, 1, 2, true, true));
            // Prerequisite not met.
            Assert.IsFalse(RndEligibility.CanStartProject(1000, 600, 3, 3, 1, 2, false, false));
            // Department too low for tier.
            Assert.IsFalse(RndEligibility.CanStartProject(1000, 600, 2, 3, 1, 2, false, true));
            // No free slot.
            Assert.IsFalse(RndEligibility.CanStartProject(1000, 600, 3, 3, 2, 2, false, true));
            // Can't afford.
            Assert.IsFalse(RndEligibility.CanStartProject(500, 600, 3, 3, 1, 2, false, true));
        }

        [Test]
        public void CanReworkChecksStateSlotAndCost()
        {
            // ReworkCost = round(40% of original). Original 1000 -> 400.
            Assert.IsTrue(RndEligibility.CanRework(400, 1000, 1, 2, isReworkable: true));
            Assert.IsFalse(RndEligibility.CanRework(399, 1000, 1, 2, true)); // can't afford
            Assert.IsFalse(RndEligibility.CanRework(400, 1000, 2, 2, true)); // no slot
            Assert.IsFalse(RndEligibility.CanRework(400, 1000, 1, 2, false)); // not reworkable
        }
    }
}
