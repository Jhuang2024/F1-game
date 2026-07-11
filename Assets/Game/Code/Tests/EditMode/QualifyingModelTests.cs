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

        [Test]
        public void TopSpeedRatingMapsKphOntoThe45To125Scale()
        {
            // 300 km/h -> bottom of the scale, 365 -> top, midpoint linear.
            Assert.AreEqual(45f, QualifyingModel.TopSpeedRating(300f), 0.0001f);
            Assert.AreEqual(125f, QualifyingModel.TopSpeedRating(365f), 0.0001f);
            Assert.AreEqual(85f, QualifyingModel.TopSpeedRating(332.5f), 0.0001f);
            // Out of band clamps rather than extrapolating.
            Assert.AreEqual(45f, QualifyingModel.TopSpeedRating(250f), 0.0001f);
            Assert.AreEqual(125f, QualifyingModel.TopSpeedRating(400f), 0.0001f);
        }

        [Test]
        public void ReferenceLapTimeIsLengthOverExpectedSpeedFloored()
        {
            // 337 km/h field avg * 0.92 style -> ~86.12 m/s; 5000 m -> ~58.06 s.
            Assert.AreEqual(5000f / ((337f / 3.6f) * 0.92f), QualifyingModel.ReferenceLapTime(337f, 0.92f, 5000f), 0.001f);
            // Longer track -> longer lap.
            Assert.Greater(QualifyingModel.ReferenceLapTime(337f, 0.92f, 7000f),
                           QualifyingModel.ReferenceLapTime(337f, 0.92f, 5000f));
            // A very short/fast circuit is floored at 45 s.
            Assert.AreEqual(45f, QualifyingModel.ReferenceLapTime(400f, 1f, 1000f), 0.0001f);
        }

        [Test]
        public void CarPerformanceWeightsPickTheRightBucketAndSumToOne()
        {
            AssertWeightsSumToOne("monza", "permanent", 15f, 0.28f);        // power
            AssertWeightsSumToOne("spa", "permanent", 15f, 0.10f);         // high-downforce
            AssertWeightsSumToOne("monaco", "street", 8f, 0.04f);          // technical
            AssertWeightsSumToOne("generic", "permanent", 15f, 0.14f);     // balanced
            // Street style OR narrow road both route to the technical bucket.
            AssertWeightsSumToOne("generic", "Street Circuit", 15f, 0.04f);
            AssertWeightsSumToOne("generic", "permanent", 11.9f, 0.04f);
            // Null-ish descriptors (as the caller passes for a null track) -> balanced.
            AssertWeightsSumToOne("", "", 999f, 0.14f);
        }

        [Test]
        public void DriverEffectIsFieldCenteredAndCoefficientWeighted()
        {
            // Live coefficients: qualifying 0.012, pace 0.003, confidence 0.001.
            const float qC = 0.012f, pC = 0.003f, cC = 0.001f;

            // Exactly at the field average -> no effect.
            Assert.AreEqual(0f, QualifyingModel.DriverEffect(82f, 82f, 80f, 82f, 82f, 80f, qC, pC, cC), 0.0001f);

            // A driver 10 qualifying points ABOVE the field average is faster
            // (negative time): (82-92)*0.012 = -0.12 s.
            Assert.AreEqual(-0.12f, QualifyingModel.DriverEffect(92f, 82f, 80f, 82f, 82f, 80f, qC, pC, cC), 0.0001f);
            // 10 points below -> slower (+0.12 s).
            Assert.AreEqual(0.12f, QualifyingModel.DriverEffect(72f, 82f, 80f, 82f, 82f, 80f, qC, pC, cC), 0.0001f);

            // Pace and confidence are smaller secondary terms and add up.
            // qualifying +5 (-0.06), pace +10 (-0.03), confidence +10 (-0.01) = -0.10.
            Assert.AreEqual(-0.10f, QualifyingModel.DriverEffect(87f, 92f, 90f, 82f, 82f, 80f, qC, pC, cC), 0.0001f);
        }

        [Test]
        public void TyreWeatherPenaltyRewardsTheRightRubber()
        {
            // Heavy rain: wets are a small bonus, inters a moderate penalty, slicks huge.
            Assert.AreEqual(-0.12f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.HeavyRain, QualifyingModel.Compound.Wet), 0.0001f);
            Assert.AreEqual(1.45f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.HeavyRain, QualifyingModel.Compound.Intermediate), 0.0001f);
            Assert.AreEqual(5.4f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.HeavyRain, QualifyingModel.Compound.Soft), 0.0001f);

            // Light rain: inters best, wets a small penalty, slicks a big one.
            Assert.AreEqual(-0.12f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.LightRain, QualifyingModel.Compound.Intermediate), 0.0001f);
            Assert.AreEqual(0.74f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.LightRain, QualifyingModel.Compound.Wet), 0.0001f);
            Assert.AreEqual(2.75f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.LightRain, QualifyingModel.Compound.Medium), 0.0001f);

            // Dry: soft fastest, then medium, then hard; rain tyres are penalised.
            Assert.AreEqual(-0.18f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.Clear, QualifyingModel.Compound.Soft), 0.0001f);
            Assert.AreEqual(0.08f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.Clear, QualifyingModel.Compound.Medium), 0.0001f);
            Assert.AreEqual(0.34f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.Clear, QualifyingModel.Compound.Hard), 0.0001f);
            Assert.AreEqual(1.7f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.Clear, QualifyingModel.Compound.Intermediate), 0.0001f);
            Assert.AreEqual(3.1f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.Clear, QualifyingModel.Compound.Wet), 0.0001f);
            // Cloudy behaves like dry here (only Clear is checked implicitly by the else path).
            Assert.AreEqual(-0.18f, QualifyingModel.TyreWeatherPenalty(QualifyingModel.Weather.Cloudy, QualifyingModel.Compound.Soft), 0.0001f);
        }

        [Test]
        public void CarEffectIsClampedRatingGap()
        {
            // Live coefficient 0.08/point, cap 2.0 s.
            const float coeff = 0.08f, cap = 2.0f;

            // At the field average -> no effect.
            Assert.AreEqual(0f, QualifyingModel.CarEffect(84f, 84f, coeff, cap), 0.0001f);
            // 10 rating points above average -> faster: (84-94)*0.08 = -0.8 s.
            Assert.AreEqual(-0.8f, QualifyingModel.CarEffect(94f, 84f, coeff, cap), 0.0001f);
            // 10 below -> slower: +0.8 s.
            Assert.AreEqual(0.8f, QualifyingModel.CarEffect(74f, 84f, coeff, cap), 0.0001f);
            // An extreme outlier clamps to +/- the cap.
            Assert.AreEqual(-2.0f, QualifyingModel.CarEffect(200f, 84f, coeff, cap), 0.0001f);
            Assert.AreEqual(2.0f, QualifyingModel.CarEffect(0f, 84f, coeff, cap), 0.0001f);
        }

        static void AssertWeightsSumToOne(string id, string style, float roadHalfWidth, float expectedTopSpeed)
        {
            QualifyingModel.CarPerformanceWeights(id, style, roadHalfWidth,
                out float wTop, out float wAcc, out float wCor, out float wBrk, out float wAero, out float wCha, out float wEng);
            Assert.AreEqual(expectedTopSpeed, wTop, 0.0001f, "top-speed weight for " + id + "/" + style);
            Assert.AreEqual(1f, wTop + wAcc + wCor + wBrk + wAero + wCha + wEng, 0.0001f, "weights must sum to 1 for " + id + "/" + style);
        }
    }
}
