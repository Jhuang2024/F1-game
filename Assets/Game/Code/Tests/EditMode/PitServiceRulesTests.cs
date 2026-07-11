using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class PitServiceRulesTests
    {
        [Test]
        public void TyreChangeStaysInsideEachCrewsWindow()
        {
            Assert.AreEqual(PitServiceRules.PlayerMinTyreSeconds, PitServiceRules.TyreChangeSeconds(true, 0f), 0.0001f);
            Assert.AreEqual(PitServiceRules.PlayerMaxTyreSeconds, PitServiceRules.TyreChangeSeconds(true, 1f), 0.0001f);
            Assert.AreEqual(PitServiceRules.AiMinTyreSeconds, PitServiceRules.TyreChangeSeconds(false, 0f), 0.0001f);
            Assert.AreEqual(PitServiceRules.AiMaxTyreSeconds, PitServiceRules.TyreChangeSeconds(false, 1f), 0.0001f);

            // Out-of-range rolls clamp instead of extrapolating.
            Assert.AreEqual(PitServiceRules.PlayerMinTyreSeconds, PitServiceRules.TyreChangeSeconds(true, -2f), 0.0001f);
            Assert.AreEqual(PitServiceRules.AiMaxTyreSeconds, PitServiceRules.TyreChangeSeconds(false, 5f), 0.0001f);
        }

        [Test]
        public void LightDamageIsLeftAloneHeavyDamageHoldsTheCar()
        {
            Assert.AreEqual(0f, PitServiceRules.RepairSeconds(0f, 0.5f));
            Assert.AreEqual(0f, PitServiceRules.RepairSeconds(PitServiceRules.RepairDamageThresholdPercent - 0.1f, 0.5f));

            float lightRepair = PitServiceRules.RepairSeconds(PitServiceRules.RepairDamageThresholdPercent, 0.5f);
            float heavyRepair = PitServiceRules.RepairSeconds(90f, 0.5f);
            Assert.Greater(lightRepair, 0f);
            Assert.Greater(heavyRepair, lightRepair);

            // Even with maximum jitter the hold stays inside a sane pit-stop bound.
            Assert.LessOrEqual(PitServiceRules.RepairSeconds(100f, 1f), PitServiceRules.MaxRepairSeconds * 1.1f + 0.0001f);
        }

        [Test]
        public void CrewErrorIsRareButReal()
        {
            // Roll at/above the chance threshold: clean stop.
            Assert.AreEqual(0f, PitServiceRules.CrewErrorSeconds(PitServiceRules.CrewErrorChance, 0.5f));
            Assert.AreEqual(0f, PitServiceRules.CrewErrorSeconds(1f, 1f));

            // Roll under the threshold: the fumble costs bounded, real time.
            float fumble = PitServiceRules.CrewErrorSeconds(0f, 0f);
            Assert.AreEqual(PitServiceRules.MinCrewErrorSeconds, fumble, 0.0001f);
            Assert.AreEqual(PitServiceRules.MaxCrewErrorSeconds, PitServiceRules.CrewErrorSeconds(0f, 1f), 0.0001f);
        }
    }
}
