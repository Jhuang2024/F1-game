using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// Deterministic procedural-pattern maths behind the project-owned surface
    /// textures: reproducibility (same input -> same output), range bounds ([0,1]),
    /// and the shape invariants each pattern promises. Engine-light and texture-free,
    /// so the generated content is verifiable without the graphics device.
    /// </summary>
    public class ProceduralPatternsTests
    {
        [Test]
        public void Hash01IsDeterministicAndInRange()
        {
            for (int i = 0; i < 200; i++)
            {
                int x = (i * 37) - 100;
                int y = (i * 53) - 250;
                float a = ProceduralPatterns.Hash01(x, y, 7);
                float b = ProceduralPatterns.Hash01(x, y, 7);
                Assert.AreEqual(a, b, "hash must be reproducible");
                Assert.GreaterOrEqual(a, 0f);
                Assert.Less(a, 1f);
            }
        }

        [Test]
        public void Hash01SeedSeparatesStreams()
        {
            // Different seeds should not collapse to identical fields everywhere.
            int diffs = 0;
            for (int i = 0; i < 64; i++)
            {
                if (ProceduralPatterns.Hash01(i, i, 1) != ProceduralPatterns.Hash01(i, i, 2))
                {
                    diffs++;
                }
            }

            Assert.Greater(diffs, 50);
        }

        [Test]
        public void ValueNoiseInRangeAndSmoothAtLattice()
        {
            for (float x = -3f; x < 3f; x += 0.31f)
            {
                for (float y = -3f; y < 3f; y += 0.29f)
                {
                    float n = ProceduralPatterns.ValueNoise(x, y, 3);
                    Assert.GreaterOrEqual(n, 0f);
                    Assert.LessOrEqual(n, 1f);
                }
            }

            // At integer coordinates value noise equals the lattice hash.
            Assert.AreEqual(ProceduralPatterns.Hash01(2, 5, 3), ProceduralPatterns.ValueNoise(2f, 5f, 3), 1e-5f);
        }

        [Test]
        public void FbmInRangeAndOctaveClamped()
        {
            for (float x = 0f; x < 4f; x += 0.5f)
            {
                float n = ProceduralPatterns.Fbm(x, x * 0.5f, 11, 5);
                Assert.GreaterOrEqual(n, 0f);
                Assert.LessOrEqual(n, 1f);
            }

            // octaves < 1 is clamped to 1 rather than dividing by zero.
            float single = ProceduralPatterns.Fbm(0.5f, 0.5f, 11, 0);
            Assert.GreaterOrEqual(single, 0f);
            Assert.LessOrEqual(single, 1f);
        }

        [Test]
        public void StripeIndexWrapsAndBounds()
        {
            Assert.AreEqual(0, ProceduralPatterns.StripeIndex(0f, 6));
            Assert.AreEqual(3, ProceduralPatterns.StripeIndex(0.5f, 6));
            // Periodicity: t and t+1 land in the same band.
            Assert.AreEqual(ProceduralPatterns.StripeIndex(0.2f, 6), ProceduralPatterns.StripeIndex(1.2f, 6));
            // Degenerate count is safe.
            Assert.AreEqual(0, ProceduralPatterns.StripeIndex(0.7f, 0));
        }

        [Test]
        public void WeaveInRangeAndReproducible()
        {
            for (float x = 0f; x < 1f; x += 0.13f)
            {
                for (float y = 0f; y < 1f; y += 0.17f)
                {
                    float w = ProceduralPatterns.Weave(x, y, 8);
                    Assert.GreaterOrEqual(w, 0f);
                    Assert.LessOrEqual(w, 1f);
                    Assert.AreEqual(w, ProceduralPatterns.Weave(x, y, 8), 1e-6f);
                }
            }
        }

        [Test]
        public void PanelSeamIsDarkAtGridLinesAndBrightInField()
        {
            // Seam sits at the panel boundary (u = 0), field at the panel centre.
            float atSeam = ProceduralPatterns.PanelSeam(0f, 0.5f, 4, 0.1f);
            float inField = ProceduralPatterns.PanelSeam(0.125f, 0.125f, 4, 0.1f);
            Assert.Less(atSeam, inField);
            Assert.GreaterOrEqual(atSeam, 0f);
            Assert.LessOrEqual(inField, 1f);
        }

        [Test]
        public void ChainLinkPeaksOnWireInRange()
        {
            for (float x = 0f; x < 1f; x += 0.19f)
            {
                for (float y = 0f; y < 1f; y += 0.23f)
                {
                    float c = ProceduralPatterns.ChainLink(x, y, 6, 0.16f);
                    Assert.GreaterOrEqual(c, 0f);
                    Assert.LessOrEqual(c, 1f);
                }
            }
        }
    }
}
