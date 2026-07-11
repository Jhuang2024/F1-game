using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// AiErsRules holds the awareness-modulated ERS decision-quality maths
    /// RaceManager.ShouldAiUseErs delegates to. Values mirror the pre-extraction
    /// inline behaviour exactly - the caller still applies the Random roll and the
    /// Expert deterministic bypass on top of these probabilities.
    /// </summary>
    public class AiErsRulesTests
    {
        [Test]
        public void RacecraftQualityScalesWithAwarenessAndClamps()
        {
            // Awareness 0 -> 0.8x the base, 100 -> 1.08x.
            Assert.AreEqual(0.5f * 0.8f, AiErsRules.RacecraftDeployQuality(0.5f, 0f), 0.0001f);
            Assert.AreEqual(0.5f * 1.08f, AiErsRules.RacecraftDeployQuality(0.5f, 100f), 0.0001f);
            // Awareness 50 -> midpoint of 0.8..1.08 = 0.94x.
            Assert.AreEqual(0.5f * 0.94f, AiErsRules.RacecraftDeployQuality(0.5f, 50f), 0.0001f);
            // A high base quality with high awareness clamps to 1.
            Assert.AreEqual(1f, AiErsRules.RacecraftDeployQuality(1f, 100f), 0.0001f);
            // Better awareness never reduces the quality.
            Assert.Greater(AiErsRules.RacecraftDeployQuality(0.6f, 90f),
                           AiErsRules.RacecraftDeployQuality(0.6f, 20f));
        }

        [Test]
        public void PushLapChanceIsHalfTheBaseQuality()
        {
            Assert.AreEqual(0.25f, AiErsRules.PushLapDeployChance(0.5f), 0.0001f);
            Assert.AreEqual(0f, AiErsRules.PushLapDeployChance(0f), 0.0001f);
            Assert.AreEqual(0.45f, AiErsRules.PushLapDeployChance(0.9f), 0.0001f);
        }
    }
}
