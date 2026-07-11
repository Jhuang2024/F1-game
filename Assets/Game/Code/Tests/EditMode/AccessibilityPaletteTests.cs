using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// AccessibilityPalette must keep the positive/negative status pair visually
    /// separable under each colour-vision mode, expose the default scheme unchanged
    /// for normal vision, and compute WCAG luminance/contrast correctly. Pinned here.
    /// </summary>
    public class AccessibilityPaletteTests
    {
        static float MaxChannelDiff(AccessibilityPalette.StatusRole a, AccessibilityPalette.StatusRole b,
            AccessibilityPalette.ColorVisionMode mode)
        {
            AccessibilityPalette.Rgb x = AccessibilityPalette.StatusColor(a, mode);
            AccessibilityPalette.Rgb y = AccessibilityPalette.StatusColor(b, mode);
            float dr = System.Math.Abs(x.R - y.R);
            float dg = System.Math.Abs(x.G - y.G);
            float db = System.Math.Abs(x.B - y.B);
            return System.Math.Max(dr, System.Math.Max(dg, db));
        }

        [Test]
        public void NoneReturnsTheDefaultGreenPositive()
        {
            AccessibilityPalette.Rgb pos = AccessibilityPalette.StatusColor(
                AccessibilityPalette.StatusRole.Positive, AccessibilityPalette.ColorVisionMode.None);
            // Default positive is green: green channel dominates red.
            Assert.Greater(pos.G, pos.R);
            Assert.Greater(pos.G, pos.B);
        }

        [Test]
        public void DeficiencyModesChangeThePositiveNegativePair()
        {
            AccessibilityPalette.Rgb defaultPos = AccessibilityPalette.StatusColor(
                AccessibilityPalette.StatusRole.Positive, AccessibilityPalette.ColorVisionMode.None);
            foreach (var mode in new[]
            {
                AccessibilityPalette.ColorVisionMode.Protanopia,
                AccessibilityPalette.ColorVisionMode.Deuteranopia,
                AccessibilityPalette.ColorVisionMode.Tritanopia,
            })
            {
                AccessibilityPalette.Rgb pos = AccessibilityPalette.StatusColor(
                    AccessibilityPalette.StatusRole.Positive, mode);
                Assert.AreNotEqual(defaultPos.R + defaultPos.G + defaultPos.B, pos.R + pos.G + pos.B,
                    $"{mode} should remap the positive colour");
            }
        }

        [Test]
        public void PositiveAndNegativeStaySeparableInEveryMode()
        {
            foreach (AccessibilityPalette.ColorVisionMode mode in
                System.Enum.GetValues(typeof(AccessibilityPalette.ColorVisionMode)))
            {
                float diff = MaxChannelDiff(
                    AccessibilityPalette.StatusRole.Positive,
                    AccessibilityPalette.StatusRole.Negative, mode);
                // The two most important states differ strongly in at least one
                // channel, so they never read as the same colour.
                Assert.Greater(diff, 0.3f, $"{mode} positive/negative too close");
            }
        }

        [Test]
        public void ModeFromIndexClampsOutOfRange()
        {
            Assert.AreEqual(AccessibilityPalette.ColorVisionMode.None, AccessibilityPalette.ModeFromIndex(-1));
            Assert.AreEqual(AccessibilityPalette.ColorVisionMode.None, AccessibilityPalette.ModeFromIndex(99));
            Assert.AreEqual(AccessibilityPalette.ColorVisionMode.Deuteranopia, AccessibilityPalette.ModeFromIndex(2));
        }

        [Test]
        public void LuminanceOfBlackAndWhiteAreZeroAndOne()
        {
            Assert.AreEqual(0f, AccessibilityPalette.RelativeLuminance(new AccessibilityPalette.Rgb(0f, 0f, 0f)), 1e-4f);
            Assert.AreEqual(1f, AccessibilityPalette.RelativeLuminance(new AccessibilityPalette.Rgb(1f, 1f, 1f)), 1e-4f);
        }

        [Test]
        public void ContrastOfBlackOnWhiteIsTwentyOne()
        {
            float ratio = AccessibilityPalette.ContrastRatio(
                new AccessibilityPalette.Rgb(0f, 0f, 0f),
                new AccessibilityPalette.Rgb(1f, 1f, 1f));
            Assert.AreEqual(21f, ratio, 0.01f);
            // Order-independent.
            float flipped = AccessibilityPalette.ContrastRatio(
                new AccessibilityPalette.Rgb(1f, 1f, 1f),
                new AccessibilityPalette.Rgb(0f, 0f, 0f));
            Assert.AreEqual(ratio, flipped, 1e-4f);
        }
    }
}
