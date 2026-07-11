using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// DriverProgressionRules holds the season rating-change curve CareerManager
    /// now delegates to. The near-zero deadband, the potential/veteran/ceiling
    /// modifiers and the +/-5 clamp are pinned here exactly as the pre-extraction
    /// inline ScoreToDelta behaved, plus the 40-99 rating clamp.
    /// </summary>
    public class DriverProgressionRulesTests
    {
        [Test]
        public void ClampRatingHoldsTheFortyToNinetyNineBand()
        {
            Assert.AreEqual(40, DriverProgressionRules.ClampRating(10));
            Assert.AreEqual(99, DriverProgressionRules.ClampRating(120));
            Assert.AreEqual(75, DriverProgressionRules.ClampRating(75));
            Assert.AreEqual(DriverProgressionRules.MinRating, DriverProgressionRules.ClampRating(0));
            Assert.AreEqual(DriverProgressionRules.MaxRating, DriverProgressionRules.ClampRating(999));
        }

        [Test]
        public void NearZeroScoreIsNoChange()
        {
            Assert.AreEqual(0, DriverProgressionRules.RatingDelta(0f, 80, 70, 60, 1f, 5f));
            Assert.AreEqual(0, DriverProgressionRules.RatingDelta(0.019f, 80, 70, 60, 1f, 5f));
            Assert.AreEqual(0, DriverProgressionRules.RatingDelta(-0.019f, 80, 70, 60, 1f, 5f));
        }

        [Test]
        public void PositiveScoresGainAndNegativeScoresLoseWithinTheCap()
        {
            // A solid positive score for a mid-potential driver gains, and stays <= +5.
            int gain = DriverProgressionRules.RatingDelta(0.6f, 80, 70, 60, 1f, 5f);
            Assert.Greater(gain, 0);
            Assert.LessOrEqual(gain, 5);

            // Symmetric negative score loses.
            int loss = DriverProgressionRules.RatingDelta(-0.6f, 80, 70, 60, 1f, 5f);
            Assert.Less(loss, 0);
            Assert.GreaterOrEqual(loss, -5);

            // Huge score clamps to the +/-5 bounds.
            Assert.AreEqual(5, DriverProgressionRules.RatingDelta(5f, 60, 95, 60, 1f, 5f));
            Assert.AreEqual(-5, DriverProgressionRules.RatingDelta(-5f, 60, 95, 60, 1f, 5f));
        }

        [Test]
        public void HigherPotentialDevelopsFasterAndTheCeilingDamps()
        {
            // Same positive score, higher development potential -> at least as big a gain.
            int lowPot = DriverProgressionRules.RatingDelta(0.4f, 70, 45, 60, 1f, 5f);
            int highPot = DriverProgressionRules.RatingDelta(0.4f, 70, 95, 60, 1f, 5f);
            Assert.GreaterOrEqual(highPot, lowPot);

            // Near the 94-99 ceiling, positive gains are damped toward zero.
            int atCeiling = DriverProgressionRules.RatingDelta(0.4f, 99, 95, 60, 1f, 5f);
            int midRange = DriverProgressionRules.RatingDelta(0.4f, 80, 95, 60, 1f, 5f);
            Assert.LessOrEqual(atCeiling, midRange);
        }
    }
}
