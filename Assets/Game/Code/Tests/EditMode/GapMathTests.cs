using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// GapMath is the lapped-gap detection/count RaceManager.GapToLeaderText and
    /// IntervalAheadText now share. The threshold (92% of a lap) and the lap count
    /// (rounded, floored at 1, banker's rounding to match Mathf.RoundToInt) are
    /// pinned here exactly as the pre-extraction inline blocks behaved.
    /// </summary>
    public class GapMathTests
    {
        [Test]
        public void IsLapDownGapTriggersAtNinetyTwoPercentOfALap()
        {
            // 5000 m track: a 4600 m (0.92) gap reads as lapped; 4599 does not.
            Assert.IsTrue(GapMath.IsLapDownGap(4600f, 5000f));
            Assert.IsTrue(GapMath.IsLapDownGap(5000f, 5000f));
            Assert.IsFalse(GapMath.IsLapDownGap(4599f, 5000f));
            Assert.IsFalse(GapMath.IsLapDownGap(0f, 5000f));
        }

        [Test]
        public void LapsDownRoundsAndFloorsAtOne()
        {
            // Whole and near-whole laps.
            Assert.AreEqual(1, GapMath.LapsDown(5000f, 5000f));
            Assert.AreEqual(1, GapMath.LapsDown(4600f, 5000f));   // 0.92 rounds to 1
            Assert.AreEqual(2, GapMath.LapsDown(10000f, 5000f));
            // Floored at 1 even when the rounded value would be 0.
            Assert.AreEqual(1, GapMath.LapsDown(100f, 5000f));
            // Degenerate track length uses a 1 m floor rather than dividing by zero.
            Assert.AreEqual(3, GapMath.LapsDown(3f, 0f));
        }

        [Test]
        public void LapsDownUsesBankersRoundingLikeMathfRoundToInt()
        {
            // Mathf.RoundToInt rounds halves to even: 2.5 -> 2, 3.5 -> 4.
            Assert.AreEqual(2, GapMath.LapsDown(12500f, 5000f)); // 2.5 laps -> 2 (even)
            Assert.AreEqual(4, GapMath.LapsDown(17500f, 5000f)); // 3.5 laps -> 4 (even)
        }
    }
}
