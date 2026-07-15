using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// TyreStrategyRules is the dry-compound decision RaceManager.NextPitCompound /
    /// StartingTyreForParticipant now delegate to, so the short-stint reach, the
    /// Soft->Medium->Hard ladder and the roll->compound start pick are pinned here.
    /// Compound codes match the live TyreCompound enum ordering
    /// (Soft 0, Medium 1, Hard 2, Intermediate 3, Wet 4).
    /// </summary>
    public class TyreStrategyRulesTests
    {
        const int Soft = 0, Medium = 1, Hard = 2, Intermediate = 3;

        [Test]
        public void PicksSoftestCompoundThatReachesTheFlag()
        {
            // Softest compound whose stint life (Soft 2, Medium 3, Hard 4) still
            // reaches the flag, so this is the LAST stop - never a compound that
            // guarantees another one.
            // 1-2 laps left: a soft covers it and is fastest.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(1, 50, Soft));
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(2, 50, Medium));
            // 3 laps left: a soft would fall a lap short, so reach only to Medium.
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(3, 90, Hard));
            // 4 laps left: only the hard reaches the flag in one stint - fitting a
            // soft here is exactly the old soft->soft two-stopper.
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(4, 90, Medium));
        }

        [Test]
        public void LongRunFitsTheMostDurableTyre()
        {
            // More laps left than even a hard covers: still the hard, to minimise
            // how many further stops remain. Aggression and current compound no
            // longer change the pick - stint length alone drives it.
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(8, 50, Hard));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(20, 90, Soft));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(20, 40, Medium));
        }

        [Test]
        public void StintLengthsMatchTheWearCalibration()
        {
            Assert.AreEqual(2, TyreStrategyRules.DryStintLaps(Soft));
            Assert.AreEqual(3, TyreStrategyRules.DryStintLaps(Medium));
            Assert.AreEqual(4, TyreStrategyRules.DryStintLaps(Hard));
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
