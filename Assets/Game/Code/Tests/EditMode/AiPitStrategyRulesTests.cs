using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class AiPitStrategyRulesTests
    {
        [Test]
        public void RoutineThresholdCentresOnFortyPercentRemaining()
        {
            // Mid-skill, no bias, Medium: right at the 0.40 centre.
            Assert.AreEqual(0.40f, AiPitStrategyRules.RoutinePitThreshold(0.5f, 0f, StintCompound.Medium), 0.0001f);

            // Better tyre management runs longer (lower threshold).
            Assert.Greater(
                AiPitStrategyRules.RoutinePitThreshold(0f, 0f, StintCompound.Medium),
                AiPitStrategyRules.RoutinePitThreshold(1f, 0f, StintCompound.Medium));

            // Softs come off earlier, Hards run leaner, wet rubber nudges early.
            float medium = AiPitStrategyRules.RoutinePitThreshold(0.5f, 0f, StintCompound.Medium);
            Assert.Greater(AiPitStrategyRules.RoutinePitThreshold(0.5f, 0f, StintCompound.Soft), medium);
            Assert.Less(AiPitStrategyRules.RoutinePitThreshold(0.5f, 0f, StintCompound.Hard), medium);
            Assert.Greater(AiPitStrategyRules.RoutinePitThreshold(0.5f, 0f, StintCompound.Wet), medium);

            // Whatever the inputs, the threshold stays inside the sane band.
            Assert.GreaterOrEqual(AiPitStrategyRules.RoutinePitThreshold(5f, -9f, StintCompound.Hard), 0.2f);
            Assert.LessOrEqual(AiPitStrategyRules.RoutinePitThreshold(-5f, 9f, StintCompound.Soft), 0.8f);
        }

        [Test]
        public void SafetyNetsForceTheStop()
        {
            Assert.IsTrue(AiPitStrategyRules.TyresEffectivelyGone(0.11f));
            Assert.IsFalse(AiPitStrategyRules.TyresEffectivelyGone(0.12f));

            Assert.IsTrue(AiPitStrategyRules.GripCollapsed(0.49f));
            Assert.IsFalse(AiPitStrategyRules.GripCollapsed(0.5f));
        }

        [Test]
        public void StrategyLapOnlyFiresNearTheRoutinePoint()
        {
            Assert.IsFalse(AiPitStrategyRules.StrategyLapMayFire(0.6f));
            Assert.IsTrue(AiPitStrategyRules.StrategyLapMayFire(0.44f));
        }

        [Test]
        public void FinalLapNeverTakesANewStop()
        {
            // Engages the moment the last lap starts (completed + 1 == raceLaps).
            Assert.IsTrue(AiPitStrategyRules.FinalLapSuppressesNewRequest(9, 10));
            Assert.IsFalse(AiPitStrategyRules.FinalLapSuppressesNewRequest(8, 10));

            // Sessions without a lap count (0) never suppress.
            Assert.IsFalse(AiPitStrategyRules.FinalLapSuppressesNewRequest(50, 0));
        }

        [Test]
        public void WeatherCrossoverNeedsPersistentMismatch()
        {
            Assert.IsTrue(AiPitStrategyRules.WantsWeatherCrossover(trackWet: true, onWetCompound: false));
            Assert.IsTrue(AiPitStrategyRules.WantsWeatherCrossover(trackWet: false, onWetCompound: true));
            Assert.IsFalse(AiPitStrategyRules.WantsWeatherCrossover(true, true));
            Assert.IsFalse(AiPitStrategyRules.WantsWeatherCrossover(false, false));

            float reaction = AiPitStrategyRules.CrossoverReactionSeconds(crossingToWet: true, awareness01: 0.5f);
            Assert.IsFalse(AiPitStrategyRules.ShouldCrossover(true, false, reaction - 0.01f, reaction));
            Assert.IsTrue(AiPitStrategyRules.ShouldCrossover(true, false, reaction, reaction));

            // A matched car never crosses over however long the timer says.
            Assert.IsFalse(AiPitStrategyRules.ShouldCrossover(true, true, 999f, reaction));
        }

        [Test]
        public void CrossoverUrgencyOrdering()
        {
            // Crossing TO wets is more urgent than coming back to slicks.
            Assert.Less(
                AiPitStrategyRules.CrossoverReactionSeconds(true, 0.5f),
                AiPitStrategyRules.CrossoverReactionSeconds(false, 0.5f));

            // Sharper awareness reacts sooner.
            Assert.Less(
                AiPitStrategyRules.CrossoverReactionSeconds(true, 1f),
                AiPitStrategyRules.CrossoverReactionSeconds(true, 0f));
        }

        [Test]
        public void CompoundClassing()
        {
            Assert.IsTrue(AiPitStrategyRules.IsWetCompound(StintCompound.Wet));
            Assert.IsTrue(AiPitStrategyRules.IsWetCompound(StintCompound.Intermediate));
            Assert.IsFalse(AiPitStrategyRules.IsWetCompound(StintCompound.Soft));
            Assert.IsFalse(AiPitStrategyRules.IsWetCompound(StintCompound.Hard));
        }
    }
}
