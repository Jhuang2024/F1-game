using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// CarDevelopmentRules is the live R&D project-maths authority
    /// (CareerManager delegates its success-chance / weeks / cost computations to
    /// it), so its risk-mode and department-level effects are pinned here. Values
    /// mirror the pre-extraction inline behaviour exactly.
    /// </summary>
    public class CarDevelopmentRulesTests
    {
        [Test]
        public void ConservativeRaisesChanceRushAndExperimentalLowerIt()
        {
            float bas = CarDevelopmentRules.SuccessChance(0.6f, 1, CarDevelopmentRules.RiskStandard);
            Assert.AreEqual(0.6f, bas, 0.0001f);

            Assert.Greater(CarDevelopmentRules.SuccessChance(0.6f, 1, CarDevelopmentRules.RiskConservative), bas);
            Assert.Less(CarDevelopmentRules.SuccessChance(0.6f, 1, CarDevelopmentRules.RiskRush), bas);
            Assert.Less(CarDevelopmentRules.SuccessChance(0.6f, 1, CarDevelopmentRules.RiskExperimental), bas);
        }

        [Test]
        public void HigherDepartmentLevelRaisesChance()
        {
            float l1 = CarDevelopmentRules.SuccessChance(0.5f, 1, CarDevelopmentRules.RiskStandard);
            float l5 = CarDevelopmentRules.SuccessChance(0.5f, 5, CarDevelopmentRules.RiskStandard);
            Assert.AreEqual(0.5f + 0.04f * 4f, l5, 0.0001f);
            Assert.Greater(l5, l1);
        }

        [Test]
        public void ChanceIsClampedToSaneBand()
        {
            Assert.AreEqual(0.05f, CarDevelopmentRules.SuccessChance(0.01f, 1, CarDevelopmentRules.RiskRush), 0.0001f);
            Assert.AreEqual(0.97f, CarDevelopmentRules.SuccessChance(1.5f, 1, CarDevelopmentRules.RiskConservative), 0.0001f);
            Assert.AreEqual(0.95f, CarDevelopmentRules.SuccessChance(1.5f, 1, CarDevelopmentRules.RiskStandard), 0.0001f);
        }

        [Test]
        public void WeeksStretchConservativeCompressRushAndClampToOne()
        {
            int standard = CarDevelopmentRules.DevelopmentWeeks(100, 1, CarDevelopmentRules.RiskStandard); // ceil(10)=10
            Assert.AreEqual(10, standard);
            Assert.Greater(CarDevelopmentRules.DevelopmentWeeks(100, 1, CarDevelopmentRules.RiskConservative), standard);
            Assert.Less(CarDevelopmentRules.DevelopmentWeeks(100, 1, CarDevelopmentRules.RiskRush), standard);

            // A tiny budget never drops below one week.
            Assert.AreEqual(1, CarDevelopmentRules.DevelopmentWeeks(1, 1, CarDevelopmentRules.RiskRush));
        }

        [Test]
        public void ConservativeCostCarriesPremium()
        {
            Assert.AreEqual(1000, CarDevelopmentRules.Cost(1000, CarDevelopmentRules.RiskStandard));
            Assert.AreEqual(1150, CarDevelopmentRules.Cost(1000, CarDevelopmentRules.RiskConservative));
        }

        [Test]
        public void DepartmentUpgradeCostScalesWithLevel()
        {
            Assert.AreEqual(400, CarDevelopmentRules.DepartmentUpgradeCost(1));
            Assert.AreEqual(1600, CarDevelopmentRules.DepartmentUpgradeCost(4));
        }
    }
}
