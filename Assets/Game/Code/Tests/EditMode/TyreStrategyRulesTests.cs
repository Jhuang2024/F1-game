using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// TyreStrategyRules is the dry-compound decision RaceManager.NextPitCompound /
    /// StartingTyreForParticipant delegate to, plus the temperature->stint-length
    /// gradient the wear model and the pre-race screen all read. Compound codes
    /// match the live TyreCompound enum ordering (Soft 0, Medium 1, Hard 2,
    /// Intermediate 3, Wet 4).
    /// </summary>
    public class TyreStrategyRulesTests
    {
        const int Soft = 0, Medium = 1, Hard = 2;

        const float Cool = TyreStrategyRules.CoolTrackTempC;         // 15C
        const float Standard = TyreStrategyRules.StandardTrackTempC; // 22.5C
        const float Hot = TyreStrategyRules.HotTrackTempC;           // 30C

        [Test]
        public void StintLifeMatchesTheTemperatureGradient()
        {
            // The three requested anchors: 15C 3/4/5, 22.5C 2/3/5, 30C 1/2/3.
            Assert.AreEqual(3, TyreStrategyRules.StintLapsForPlanning(Soft, Cool));
            Assert.AreEqual(4, TyreStrategyRules.StintLapsForPlanning(Medium, Cool));
            Assert.AreEqual(5, TyreStrategyRules.StintLapsForPlanning(Hard, Cool));

            Assert.AreEqual(2, TyreStrategyRules.StintLapsForPlanning(Soft, Standard));
            Assert.AreEqual(3, TyreStrategyRules.StintLapsForPlanning(Medium, Standard));
            Assert.AreEqual(5, TyreStrategyRules.StintLapsForPlanning(Hard, Standard));

            Assert.AreEqual(1, TyreStrategyRules.StintLapsForPlanning(Soft, Hot));
            Assert.AreEqual(2, TyreStrategyRules.StintLapsForPlanning(Medium, Hot));
            Assert.AreEqual(3, TyreStrategyRules.StintLapsForPlanning(Hard, Hot));
        }

        [Test]
        public void StintLifeInterpolatesBetweenAnchorsAndClampsTheEnds()
        {
            // Halfway between cool and standard: soft halfway between 3 and 2.
            Assert.AreEqual(2.5f, TyreStrategyRules.ExpectedStintLapsAtTemp(Soft, (Cool + Standard) * 0.5f), 0.01f);
            // Below/above the defined range holds flat at the end anchors.
            Assert.AreEqual(3f, TyreStrategyRules.ExpectedStintLapsAtTemp(Soft, 5f), 0.01f);
            Assert.AreEqual(1f, TyreStrategyRules.ExpectedStintLapsAtTemp(Soft, 45f), 0.01f);
        }

        [Test]
        public void PicksSoftestCompoundThatReachesTheFlagAtStandardTemp()
        {
            // At 22.5C stint life is soft 2 / medium 3 / hard 5.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(1, Standard));
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(2, Standard));
            // 3 laps left: a soft would fall short, reach to Medium.
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(3, Standard));
            // 4 laps left: a medium falls short too - the hard is the one-stint call.
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(4, Standard));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(5, Standard));
        }

        [Test]
        public void HotTrackPushesOntoHarderCompoundsSooner()
        {
            // At 30C life collapses to soft 1 / medium 2 / hard 3, so the same laps-
            // remaining reaches for a harder tyre than it would on a cool track.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(1, Hot));
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(2, Hot));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(3, Hot));

            // The same 3 laps on a cool track (soft lasts 3) can still take a soft.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(3, Cool));
        }

        [Test]
        public void LongRunFitsTheMostDurableTyre()
        {
            // More laps left than even a hard covers: still the hard, to minimise
            // how many further stops remain.
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(8, Standard));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(20, Cool));
        }

        [Test]
        public void TrackTemperatureBandsFromWeatherProfile()
        {
            // Hot events sit at the top of the gradient, wet/cool at the bottom, a
            // plain clear day in the middle - within the +/-2.5C per-track spread.
            Assert.That(TyreStrategyRules.TrackTemperatureFor("clear_hot", "bahrain"), Is.InRange(Hot - 2.5f, Hot));
            Assert.That(TyreStrategyRules.TrackTemperatureFor("wet", "spa"), Is.InRange(Cool, Cool + 2.5f));
            Assert.That(TyreStrategyRules.TrackTemperatureFor("clear", "monza"), Is.InRange(Standard - 2.5f, Standard + 2.5f));
        }

        [Test]
        public void SameProfileDifferentTrackWearsDifferently()
        {
            // The per-track offset means two clear-weather circuits don't land on
            // an identical temperature (so degradation varies track to track).
            float a = TyreStrategyRules.TrackTemperatureFor("clear", "monza");
            float b = TyreStrategyRules.TrackTemperatureFor("clear", "silverstone");
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void DryStartPickMatchesTheRoll()
        {
            Assert.AreEqual(Soft, TyreStrategyRules.DryStartCompoundFromRoll(0));
            Assert.AreEqual(Medium, TyreStrategyRules.DryStartCompoundFromRoll(1));
            Assert.AreEqual(Hard, TyreStrategyRules.DryStartCompoundFromRoll(2));
        }
    }
}
