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
            // Recalibrated anchors (matching observed live wear): 15C 4/5/6,
            // 22.5C 3/4/6, 30C 2/3/4.
            Assert.AreEqual(4, TyreStrategyRules.StintLapsForPlanning(Soft, Cool));
            Assert.AreEqual(5, TyreStrategyRules.StintLapsForPlanning(Medium, Cool));
            Assert.AreEqual(6, TyreStrategyRules.StintLapsForPlanning(Hard, Cool));

            Assert.AreEqual(3, TyreStrategyRules.StintLapsForPlanning(Soft, Standard));
            Assert.AreEqual(4, TyreStrategyRules.StintLapsForPlanning(Medium, Standard));
            Assert.AreEqual(6, TyreStrategyRules.StintLapsForPlanning(Hard, Standard));

            Assert.AreEqual(2, TyreStrategyRules.StintLapsForPlanning(Soft, Hot));
            Assert.AreEqual(3, TyreStrategyRules.StintLapsForPlanning(Medium, Hot));
            Assert.AreEqual(4, TyreStrategyRules.StintLapsForPlanning(Hard, Hot));
        }

        [Test]
        public void StintLifeInterpolatesBetweenAnchorsAndClampsTheEnds()
        {
            // Halfway between cool and standard: soft halfway between 4 and 3.
            Assert.AreEqual(3.5f, TyreStrategyRules.ExpectedStintLapsAtTemp(Soft, (Cool + Standard) * 0.5f), 0.01f);
            // Below/above the defined range holds flat at the end anchors.
            Assert.AreEqual(4f, TyreStrategyRules.ExpectedStintLapsAtTemp(Soft, 5f), 0.01f);
            Assert.AreEqual(2f, TyreStrategyRules.ExpectedStintLapsAtTemp(Soft, 45f), 0.01f);
        }

        [Test]
        public void PlanningStintIsAboutOneLapShorterThanRawLife()
        {
            // A car pits once a lap, so usable planning laps trail the raw life by
            // ~1 (raw soft 3/med 4/hard 6 at 22.5C -> usable 2/3/5).
            Assert.AreEqual(2, TyreStrategyRules.PlanningStintLaps(Soft, Standard));
            Assert.AreEqual(3, TyreStrategyRules.PlanningStintLaps(Medium, Standard));
            Assert.AreEqual(5, TyreStrategyRules.PlanningStintLaps(Hard, Standard));
            // Cool: raw 4/5/6 -> usable 3/4/5.
            Assert.AreEqual(3, TyreStrategyRules.PlanningStintLaps(Soft, Cool));
            Assert.AreEqual(5, TyreStrategyRules.PlanningStintLaps(Hard, Cool));
        }

        [Test]
        public void PicksSoftestCompoundThatReachesTheFlagAtStandardTemp()
        {
            // At 22.5C usable planning stint is soft 2 / medium 3 / hard 5.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(1, Standard));
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(2, Standard));
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(3, Standard));
            // 4+ laps left: soft and medium both fall short, so the hard.
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(4, Standard));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(5, Standard));
        }

        [Test]
        public void HotTrackPushesOntoHarderCompoundsSooner()
        {
            // At 30C usable planning stint collapses to soft 1 / medium 2 / hard 3.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(1, Hot));
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(2, Hot));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(3, Hot));

            // The same 2 laps on a cool track (usable soft 3) can still take a soft.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(2, Cool));
        }

        [Test]
        public void FastestStrategyGetsMoreStopsAsTheTrackHeats()
        {
            int startCompound;
            int stops;
            // A hot track forces short stints, so the fastest plan takes more stops
            // than the same race on a cool track.
            TyreStrategyRules.FastestDryStrategy(8, Cool, out startCompound, out stops);
            int coolStops = stops;
            TyreStrategyRules.FastestDryStrategy(8, Hot, out startCompound, out stops);
            int hotStops = stops;
            Assert.Greater(hotStops, coolStops);

            // A 4+ lap race always carries at least the mandatory stop.
            TyreStrategyRules.FastestDryStrategy(5, Cool, out startCompound, out stops);
            Assert.GreaterOrEqual(stops, 1);
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
            // plain clear day in the middle. The per-circuit offset is a real climate
            // bias now, not a hash, so it is asserted against the true track ids.
            Assert.That(TyreStrategyRules.TrackTemperatureFor("clear_hot", "bahrain_desert"), Is.EqualTo(Hot));
            Assert.That(TyreStrategyRules.TrackTemperatureFor("wet", "spa_flowing"), Is.EqualTo(Cool));
            Assert.That(TyreStrategyRules.TrackTemperatureFor("clear", "monza_low_downforce"), Is.EqualTo(Standard));
        }

        [Test]
        public void SameProfileDifferentTrackWearsDifferently()
        {
            // The per-circuit offset means two clear-weather circuits don't land on an
            // identical temperature, and the ORDER is meaningful: Monza in the Italian
            // late summer runs hotter than Silverstone.
            float monza = TyreStrategyRules.TrackTemperatureFor("clear", "monza_low_downforce");
            float silverstone = TyreStrategyRules.TrackTemperatureFor("clear", "silverstone_high_speed");
            Assert.Greater(monza, silverstone);
        }

        [Test]
        public void ColdestAndHottestCircuitsAreTheRealOnes()
        {
            // Las Vegas is a November night race in a desert: the coldest surface on
            // the calendar. Bahrain and Qatar are the hottest. A hashed offset had no
            // way to express either.
            Assert.Less(TyreStrategyRules.CircuitTemperatureOffsetC("las_vegas_street"),
                        TyreStrategyRules.CircuitTemperatureOffsetC("spa_flowing"));
            Assert.Greater(TyreStrategyRules.CircuitTemperatureOffsetC("bahrain_desert"),
                           TyreStrategyRules.CircuitTemperatureOffsetC("monza_low_downforce"));
            Assert.AreEqual(0f, TyreStrategyRules.CircuitTemperatureOffsetC("not_a_real_circuit"));
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
