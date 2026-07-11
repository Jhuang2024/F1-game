using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// QualifyingModel holds the RNG-free pieces of the qualifying lap-time model
    /// that RaceManager.Qualifying now delegates to (track-speed character, wet
    /// penalty, mistake probability, invalid-time sentinel). Values mirror the
    /// pre-extraction inline behaviour exactly - this pins them so a later change is
    /// deliberate. Weather codes: Clear 0, Cloudy 1, LightRain 2, HeavyRain 3.
    /// </summary>
    public class QualifyingModelTests
    {
        [Test]
        public void TrackSpeedFactorMatchesTheCircuitTable()
        {
            // trackId is matched case-sensitively (as the inline code did).
            Assert.AreEqual(0.65f, QualifyingModel.TrackSpeedFactor("monaco", "street", 8f), 0.0001f);
            Assert.AreEqual(1.02f, QualifyingModel.TrackSpeedFactor("monza", "permanent", 15f), 0.0001f);
            Assert.AreEqual(1.02f, QualifyingModel.TrackSpeedFactor("las_vegas", "street", 15f), 0.0001f);
            Assert.AreEqual(0.71f, QualifyingModel.TrackSpeedFactor("hungary", "permanent", 15f), 0.0001f);
            // Street style OR a narrow road -> 0.76.
            Assert.AreEqual(0.76f, QualifyingModel.TrackSpeedFactor("generic", "Street Circuit", 15f), 0.0001f);
            Assert.AreEqual(0.76f, QualifyingModel.TrackSpeedFactor("generic", "permanent", 11.9f), 0.0001f);
            // Ordinary permanent circuit -> 0.92; null descriptors are safe.
            Assert.AreEqual(0.92f, QualifyingModel.TrackSpeedFactor("generic", "permanent", 15f), 0.0001f);
            Assert.AreEqual(0.92f, QualifyingModel.TrackSpeedFactor(null, null, 15f), 0.0001f);
        }

        [Test]
        public void WeatherPenaltyIsSharedBaselinePlusWetSkillSpread()
        {
            Assert.AreEqual(0f, QualifyingModel.WeatherPenalty(QualifyingModel.Weather.Clear, 80f), 0.0001f);
            Assert.AreEqual(0.04f, QualifyingModel.WeatherPenalty(QualifyingModel.Weather.Cloudy, 80f), 0.0001f);
            // Light rain baseline 1.25, wetSkill 80 -> *Lerp(1.1,0.6,0.8)=0.7.
            Assert.AreEqual(1.25f * 0.7f, QualifyingModel.WeatherPenalty(QualifyingModel.Weather.LightRain, 80f), 0.0001f);
            // Heavy rain baseline 2.65; a wet specialist (100) gets the 0.6 floor.
            Assert.AreEqual(2.65f * 0.6f, QualifyingModel.WeatherPenalty(QualifyingModel.Weather.HeavyRain, 100f), 0.0001f);
            // A poor wet driver (0) gets the 1.1 ceiling; and better wetSkill helps.
            Assert.AreEqual(2.65f * 1.1f, QualifyingModel.WeatherPenalty(QualifyingModel.Weather.HeavyRain, 0f), 0.0001f);
            Assert.Less(QualifyingModel.WeatherPenalty(QualifyingModel.Weather.HeavyRain, 90f),
                        QualifyingModel.WeatherPenalty(QualifyingModel.Weather.HeavyRain, 40f));
        }

        [Test]
        public void MistakeChanceRisesWithRainAndInconsistencyAndQ3()
        {
            // Base rate: consistency 80 -> Lerp(0.075,0.012,0.8) = 0.0246, dry, Q1.
            Assert.AreEqual(0.0246f, QualifyingModel.MistakeChance(80f, QualifyingModel.Weather.Clear, 1), 0.0001f);
            // Rain adds a fixed increment; heavy > light.
            Assert.AreEqual(0.0246f + 0.025f, QualifyingModel.MistakeChance(80f, QualifyingModel.Weather.LightRain, 1), 0.0001f);
            Assert.AreEqual(0.0246f + 0.045f, QualifyingModel.MistakeChance(80f, QualifyingModel.Weather.HeavyRain, 1), 0.0001f);
            // Q3 nudges it up by 0.008.
            Assert.AreEqual(0.0246f + 0.008f, QualifyingModel.MistakeChance(80f, QualifyingModel.Weather.Clear, 3), 0.0001f);
            // Lower consistency -> higher base chance.
            Assert.Greater(QualifyingModel.MistakeChance(40f, QualifyingModel.Weather.Clear, 1),
                           QualifyingModel.MistakeChance(90f, QualifyingModel.Weather.Clear, 1));
        }

        [Test]
        public void InvalidTimeIsLargeAndPhaseStableAndClamped()
        {
            Assert.AreEqual(9998.1f, QualifyingModel.InvalidTime(1), 0.0001f);
            Assert.AreEqual(9998.2f, QualifyingModel.InvalidTime(2), 0.0001f);
            Assert.AreEqual(9998.3f, QualifyingModel.InvalidTime(3), 0.0001f);
            // Out-of-range phases clamp to 1..3.
            Assert.AreEqual(9998.1f, QualifyingModel.InvalidTime(0), 0.0001f);
            Assert.AreEqual(9998.3f, QualifyingModel.InvalidTime(9), 0.0001f);
        }
    }
}
