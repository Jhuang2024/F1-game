using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// WeatherRules is the live weather-swing and track-evolution authority for a
    /// race (RaceManager.UpdateWeatherTransition/UpdateTrackEvolution delegate to
    /// it), so its transition gating, wet↔dry toggle and rubber ramp/grip maths
    /// are pinned here. Values mirror the pre-extraction inline behavior exactly.
    /// </summary>
    public class WeatherRulesTests
    {
        [Test]
        public void VariabilityOffNeverTransitions()
        {
            Assert.AreEqual(WeatherRules.TransitionPhase.None,
                WeatherRules.EvaluateTransition(0, 40, 50, false, false));
        }

        [Test]
        public void FirstSwingFiresAtHalfDistance()
        {
            // 50-lap race -> halfway at lap 25.
            Assert.AreEqual(WeatherRules.TransitionPhase.None,
                WeatherRules.EvaluateTransition(2, 24, 50, false, false));
            Assert.AreEqual(WeatherRules.TransitionPhase.First,
                WeatherRules.EvaluateTransition(2, 25, 50, false, false));
        }

        [Test]
        public void SecondSwingOnlyAtHighVariabilityAndThreeQuarters()
        {
            // Standard variability (2): no second swing once the first is done.
            Assert.AreEqual(WeatherRules.TransitionPhase.None,
                WeatherRules.EvaluateTransition(2, 45, 50, true, false));

            // High variability (3): second swing at three-quarter distance (lap 37).
            Assert.AreEqual(WeatherRules.TransitionPhase.None,
                WeatherRules.EvaluateTransition(3, 36, 50, true, false));
            Assert.AreEqual(WeatherRules.TransitionPhase.Second,
                WeatherRules.EvaluateTransition(3, 37, 50, true, false));

            // Once both swings are done, nothing more fires.
            Assert.AreEqual(WeatherRules.TransitionPhase.None,
                WeatherRules.EvaluateTransition(3, 49, 50, true, true));
        }

        [Test]
        public void ShortRaceClampsThresholdsToAtLeastOneLap()
        {
            // 1-lap race: halfway math (0) is clamped to lap 1.
            Assert.AreEqual(WeatherRules.TransitionPhase.None,
                WeatherRules.EvaluateTransition(2, 0, 1, false, false));
            Assert.AreEqual(WeatherRules.TransitionPhase.First,
                WeatherRules.EvaluateTransition(2, 1, 1, false, false));
        }

        [Test]
        public void NextIsRainingTogglesWetAndDry()
        {
            Assert.IsTrue(WeatherRules.NextIsRaining(false));  // dry -> rain
            Assert.IsFalse(WeatherRules.NextIsRaining(true));  // rain -> dry
        }

        [Test]
        public void RubberBuildsInSlowlyAndWashesOffFast()
        {
            // Dry: builds toward 1 at the slow ramp.
            float built = WeatherRules.RampRubberLevel(0.5f, false, 1f);
            Assert.AreEqual(0.5f + WeatherRules.RubberBuildRampSpeed, built, 0.0001f);

            // Heavy rain: scrubs toward 0 at the fast ramp.
            float washed = WeatherRules.RampRubberLevel(0.5f, true, 1f);
            Assert.AreEqual(0.5f - WeatherRules.RubberWashRampSpeed, washed, 0.0001f);
        }

        [Test]
        public void RampReachesTargetWithoutOvershoot()
        {
            Assert.AreEqual(1f, WeatherRules.RampRubberLevel(0.999f, false, 1f), 0.0001f);
            Assert.AreEqual(0f, WeatherRules.RampRubberLevel(0.1f, true, 1f), 0.0001f);
        }

        [Test]
        public void GripMultiplierScalesWithRubber()
        {
            Assert.AreEqual(1f, WeatherRules.GripMultiplier(0f), 0.0001f);
            Assert.AreEqual(1f + WeatherRules.TrackEvolutionMaxGripBonus,
                WeatherRules.GripMultiplier(1f), 0.0001f);
        }
    }
}
