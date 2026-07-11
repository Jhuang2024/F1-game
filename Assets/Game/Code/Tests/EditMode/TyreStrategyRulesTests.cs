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
        public void ShortStintReachesForAFasterCompound()
        {
            // Aggressive (>=65) inside the 8-lap window pushes all the way to Soft.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(6, 70, Hard));
            // A very short stint (<=4 laps) pushes to Soft even for a cautious driver.
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(4, 40, Medium));
            Assert.AreEqual(Soft, TyreStrategyRules.NextDryCompound(1, 50, Soft));
            // Cautious driver, 5-8 laps left: reaches only to Medium, not Soft.
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(6, 50, Hard));
            // Boundary: 8 laps is still "short"; 9 is not.
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(8, 50, Hard));
        }

        [Test]
        public void LongStintFollowsTheLadder()
        {
            // Out of the short window (or 0 remaining) -> Soft->Medium->Hard ladder,
            // with Hard and any unexpected compound settling back to Medium.
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(20, 90, Soft));
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(20, 90, Medium));
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(20, 90, Hard));
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(20, 90, Intermediate));
            // 9 laps: out of the reach window, so aggression is ignored.
            Assert.AreEqual(Hard, TyreStrategyRules.NextDryCompound(9, 90, Medium));
            // 0 laps remaining is not "> 0", so the ladder runs, not the reach.
            Assert.AreEqual(Medium, TyreStrategyRules.NextDryCompound(0, 90, Soft));
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
