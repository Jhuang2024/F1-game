using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// Pins the car visual colour curves (brake-disc glow ramp and per-compound tyre
    /// look) exactly as the rendering call sites produced them, so a refactor can't
    /// silently change how brakes glow or how a compound reads.
    /// </summary>
    public class CarVisualCurvesTests
    {
        [Test]
        public void BrakeGlowIsBlackWhenCold()
        {
            CarVisualCurves.BrakeGlow(0f, out float r, out float g, out float b);
            Assert.AreEqual(0f, r, 1e-6f);
            Assert.AreEqual(0f, g, 1e-6f);
            Assert.AreEqual(0f, b, 1e-6f);
        }

        [Test]
        public void BrakeGlowReachesHotOrangeAtFullTemperature()
        {
            CarVisualCurves.BrakeGlow(1f, out float r, out float g, out float b);
            Assert.AreEqual(1.4f, r, 1e-6f);
            Assert.AreEqual(0.25f, g, 1e-6f);
            Assert.AreEqual(0.05f, b, 1e-6f);
        }

        [Test]
        public void BrakeGlowRampsWithTemperatureSquared()
        {
            // At t=0.5 the glow is 0.25 of the way (0.5^2), and clamps above 1.
            CarVisualCurves.BrakeGlow(0.5f, out float r, out _, out _);
            Assert.AreEqual(1.4f * 0.25f, r, 1e-6f);
            CarVisualCurves.BrakeGlow(2f, out float rc, out _, out _);
            Assert.AreEqual(1.4f, rc, 1e-6f);
        }

        [Test]
        public void SoftTyreLookMatchesTheAuthoredValues()
        {
            CarVisualCurves.TyreLook(0, out float r, out float g, out float b, out float metallic, out float smoothness);
            Assert.AreEqual(0.05f, r, 1e-6f);
            Assert.AreEqual(0.014f, g, 1e-6f);
            Assert.AreEqual(0.013f, b, 1e-6f);
            Assert.AreEqual(0.02f, metallic, 1e-6f);
            Assert.AreEqual(0.34f, smoothness, 1e-6f);
        }

        [Test]
        public void UnknownCompoundUsesTheMediumDefault()
        {
            CarVisualCurves.TyreLook(1, out float rM, out _, out _, out _, out float sM); // Medium
            CarVisualCurves.TyreLook(99, out float rU, out _, out _, out _, out float sU); // unknown
            Assert.AreEqual(rM, rU, 1e-6f);
            Assert.AreEqual(sM, sU, 1e-6f);
            Assert.AreEqual(0.26f, sM, 1e-6f);
        }

        [Test]
        public void EachCompoundHasADistinctSmoothness()
        {
            CarVisualCurves.TyreLook(0, out _, out _, out _, out _, out float soft);
            CarVisualCurves.TyreLook(2, out _, out _, out _, out _, out float hard);
            CarVisualCurves.TyreLook(3, out _, out _, out _, out _, out float inter);
            CarVisualCurves.TyreLook(4, out _, out _, out _, out _, out float wet);
            Assert.AreEqual(0.34f, soft, 1e-6f);
            Assert.AreEqual(0.18f, hard, 1e-6f);
            Assert.AreEqual(0.24f, inter, 1e-6f);
            Assert.AreEqual(0.3f, wet, 1e-6f);
        }
    }
}
