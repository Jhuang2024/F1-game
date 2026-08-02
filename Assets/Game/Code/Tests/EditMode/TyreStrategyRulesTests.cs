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
        const float Standard = TyreStrategyRules.StandardTrackTempC; // 35C
        const float Hot = TyreStrategyRules.HotTrackTempC;           // 55C

        // Every lap-based assertion below pins an explicit lap length, because stint
        // life is a DISTANCE and the lap count depends on the circuit. 5 km is the
        // reference, so soft/medium/hard are 15/25/35 laps at standard temperature.
        const float Ref = TyreStrategyRules.ReferenceLapLengthMeters;

        [Test]
        public void StintLifeMatchesTheTemperatureGradient()
        {
            // Real Pirelli stint DISTANCES: soft 95/75/50 km, medium 155/125/88,
            // hard 215/175/125 across cool/standard/hot. At the 5 km reference lap
            // that is 19/31/43 laps cool, 15/25/35 standard, 10/17/25 hot - a grand
            // prix is the one-to-two stop race it should be.
            Assert.AreEqual(19, TyreStrategyRules.StintLapsForPlanning(Soft, Cool, Ref));
            Assert.AreEqual(31, TyreStrategyRules.StintLapsForPlanning(Medium, Cool, Ref));
            Assert.AreEqual(43, TyreStrategyRules.StintLapsForPlanning(Hard, Cool, Ref));

            Assert.AreEqual(15, TyreStrategyRules.StintLapsForPlanning(Soft, Standard, Ref));
            Assert.AreEqual(25, TyreStrategyRules.StintLapsForPlanning(Medium, Standard, Ref));
            Assert.AreEqual(35, TyreStrategyRules.StintLapsForPlanning(Hard, Standard, Ref));

            Assert.AreEqual(10, TyreStrategyRules.StintLapsForPlanning(Soft, Hot, Ref));
            Assert.AreEqual(17, TyreStrategyRules.StintLapsForPlanning(Medium, Hot, Ref));
            Assert.AreEqual(25, TyreStrategyRules.StintLapsForPlanning(Hard, Hot, Ref));
        }

        [Test]
        public void StintLifeInterpolatesBetweenAnchorsAndClampsTheEnds()
        {
            // Halfway between cool and standard: soft halfway between 95 and 75 km.
            Assert.AreEqual(85f, TyreStrategyRules.ExpectedStintKmAtTemp(Soft, (Cool + Standard) * 0.5f), 0.01f);
            // Below/above the defined range holds flat at the end anchors.
            Assert.AreEqual(95f, TyreStrategyRules.ExpectedStintKmAtTemp(Soft, 5f), 0.01f);
            Assert.AreEqual(50f, TyreStrategyRules.ExpectedStintKmAtTemp(Soft, 75f), 0.01f);
        }

        [Test]
        public void PlanningStintIsAboutOneLapShorterThanRawLife()
        {
            // A car can only pit once a lap, so usable planning laps trail the raw
            // life. Raw soft 15 / medium 25 / hard 35 at standard temp on a 5 km lap.
            Assert.Less(TyreStrategyRules.PlanningStintLaps(Soft, Standard, Ref),
                        TyreStrategyRules.StintLapsForPlanning(Soft, Standard, Ref));
            Assert.AreEqual(12, TyreStrategyRules.PlanningStintLaps(Soft, Standard, Ref));
            Assert.AreEqual(21, TyreStrategyRules.PlanningStintLaps(Medium, Standard, Ref));
            Assert.AreEqual(29, TyreStrategyRules.PlanningStintLaps(Hard, Standard, Ref));
        }

        [Test]
        public void PicksSoftestCompoundThatReachesTheFlagAtStandardTemp()
        {
            // At standard temp on a 5 km lap the usable stint is soft 12 / medium 21
            // / hard 29.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(10, Standard, Ref));
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(12, Standard, Ref));
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(18, Standard, Ref));
            // Past the medium's reach: the hard.
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(25, Standard, Ref));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(40, Standard, Ref));
        }

        [Test]
        public void HotTrackPushesOntoHarderCompoundsSooner()
        {
            // At the top of the gradient the usable stint collapses to soft 8 /
            // medium 15 / hard 21 on a 5 km lap.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(8, Hot, Ref));
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(12, Hot, Ref));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(18, Hot, Ref));

            // The same 12 laps on a cool track can still take a soft.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(12, Cool, Ref));
        }

        [Test]
        public void FastestStrategyGetsMoreStopsAsTheTrackHeats()
        {
            int startCompound;
            int stops;
            // A hot track forces short stints, so the fastest plan takes more stops
            // than the same race on a cool track.
            TyreStrategyRules.FastestDryStrategy(57, Cool, out startCompound, out stops, Ref);
            int coolStops = stops;
            TyreStrategyRules.FastestDryStrategy(57, Hot, out startCompound, out stops, Ref);
            int hotStops = stops;
            Assert.Greater(hotStops, coolStops);

            // A real grand prix distance is a one-to-two stop race on a cool track,
            // not the five-stopper a 3-lap tyre model produced.
            TyreStrategyRules.FastestDryStrategy(57, Cool, out startCompound, out stops, Ref);
            Assert.GreaterOrEqual(stops, 1);
            Assert.LessOrEqual(stops, 2);

            // A 4+ lap race always carries at least the mandatory stop.
            TyreStrategyRules.FastestDryStrategy(5, Cool, out startCompound, out stops, Ref);
            Assert.GreaterOrEqual(stops, 1);
        }

        [Test]
        public void LongRunFitsTheMostDurableTyre()
        {
            // More laps left than even a hard covers: still the hard, to minimise
            // how many further stops remain.
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(40, Standard, Ref));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(60, Cool, Ref));
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
