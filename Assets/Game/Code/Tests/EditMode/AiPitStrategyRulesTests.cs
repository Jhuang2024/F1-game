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
            Assert.AreEqual(0.18f, AiPitStrategyRules.RoutinePitThreshold(0.5f, 0f, StintCompound.Medium), 0.0001f);

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
            Assert.GreaterOrEqual(AiPitStrategyRules.RoutinePitThreshold(5f, -9f, StintCompound.Hard), 0.12f);
            Assert.LessOrEqual(AiPitStrategyRules.RoutinePitThreshold(-5f, 9f, StintCompound.Soft), 0.2f);
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
            Assert.IsFalse(AiPitStrategyRules.StrategyLapMayFire(0.25f));
            Assert.IsTrue(AiPitStrategyRules.StrategyLapMayFire(0.19f));
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
        public void RecommendedPitLapWindowing()
        {
            // Sprint-length races pit immediately if at all.
            Assert.AreEqual(1, AiPitStrategyRules.RecommendedPitLap(3, false, 0.5f, 0.5f));

            // Dry mid-skill no-jitter: 55% distance. 20 laps -> lap 11.
            Assert.AreEqual(11, AiPitStrategyRules.RecommendedPitLap(20, false, 0.5f, 0.5f));

            // Wet pulls the window earlier.
            Assert.Less(
                AiPitStrategyRules.RecommendedPitLap(20, true, 0.5f, 0.5f),
                AiPitStrategyRules.RecommendedPitLap(20, false, 0.5f, 0.5f));

            // Better management stretches the stint; jitter spreads drivers.
            Assert.GreaterOrEqual(
                AiPitStrategyRules.RecommendedPitLap(30, false, 1f, 0.5f),
                AiPitStrategyRules.RecommendedPitLap(30, false, 0f, 0.5f));
            Assert.GreaterOrEqual(
                AiPitStrategyRules.RecommendedPitLap(30, false, 0.5f, 1f),
                AiPitStrategyRules.RecommendedPitLap(30, false, 0.5f, 0f));

            // Never later than the penultimate lap.
            Assert.LessOrEqual(AiPitStrategyRules.RecommendedPitLap(5, false, 1f, 1f), 4);
        }

        [Test]
        public void UndercutOnlyInsideOwnWindowAgainstALiveRival()
        {
            // In-window, worn tyres, rival unstopped, close: go.
            Assert.IsTrue(AiPitStrategyRules.ShouldPitForUndercut(9, 10, 0.18f, true, 1.5f));

            // Before the window opens, or at/after the recommended lap: never.
            Assert.IsFalse(AiPitStrategyRules.ShouldPitForUndercut(7, 10, 0.18f, true, 1.5f));
            Assert.IsFalse(AiPitStrategyRules.ShouldPitForUndercut(10, 10, 0.18f, true, 1.5f));

            // Fresh tyres, no rival, or the gap too big: never.
            Assert.IsFalse(AiPitStrategyRules.ShouldPitForUndercut(9, 10, 0.4f, true, 1.5f));
            Assert.IsFalse(AiPitStrategyRules.ShouldPitForUndercut(9, 10, 0.18f, false, 1.5f));
            Assert.IsFalse(AiPitStrategyRules.ShouldPitForUndercut(9, 10, 0.18f, true, 2.3f));
        }

        [Test]
        public void SafetyCarWindowDecision()
        {
            // Owing the mandatory stop: any real excuse takes the cheap stop.
            Assert.IsTrue(AiPitStrategyRules.ShouldPitUnderSafetyCar(true, true, false, false));
            Assert.IsTrue(AiPitStrategyRules.ShouldPitUnderSafetyCar(true, false, true, false));
            Assert.IsTrue(AiPitStrategyRules.ShouldPitUnderSafetyCar(true, false, false, true));
            Assert.IsFalse(AiPitStrategyRules.ShouldPitUnderSafetyCar(true, false, false, false));

            // Already stopped: only weather, or genuinely worn inside the window.
            Assert.IsTrue(AiPitStrategyRules.ShouldPitUnderSafetyCar(false, false, false, true));
            Assert.IsTrue(AiPitStrategyRules.ShouldPitUnderSafetyCar(false, true, true, false));
            Assert.IsFalse(AiPitStrategyRules.ShouldPitUnderSafetyCar(false, true, false, false));
            Assert.IsFalse(AiPitStrategyRules.ShouldPitUnderSafetyCar(false, false, true, false));
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
