using System.Collections.Generic;
using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// Championship chart geometry: x/y normalization, the "nice" axis maximum, tick
    /// spacing, whole-series mapping, and the degenerate cases (no rounds, one round,
    /// all-zero points) that must not divide by zero. Pinned here.
    /// </summary>
    public class ChampionshipChartTests
    {
        [Test]
        public void NormalizeXSpansZeroToOne()
        {
            Assert.AreEqual(0f, ChampionshipChart.NormalizeX(0, 5), 1e-6f);
            Assert.AreEqual(1f, ChampionshipChart.NormalizeX(4, 5), 1e-6f);
            Assert.AreEqual(0.5f, ChampionshipChart.NormalizeX(2, 5), 1e-6f);
            // A single round can't span, so it pins to the left.
            Assert.AreEqual(0f, ChampionshipChart.NormalizeX(0, 1), 1e-6f);
            // Out-of-range indices clamp.
            Assert.AreEqual(1f, ChampionshipChart.NormalizeX(9, 5), 1e-6f);
        }

        [Test]
        public void NormalizeYClampsAgainstAxisMax()
        {
            Assert.AreEqual(0.5f, ChampionshipChart.NormalizeY(50, 100), 1e-6f);
            Assert.AreEqual(0f, ChampionshipChart.NormalizeY(0, 100), 1e-6f);
            Assert.AreEqual(1f, ChampionshipChart.NormalizeY(200, 100), 1e-6f); // clamped
            // Zero axis (empty board) never divides by zero.
            Assert.AreEqual(0f, ChampionshipChart.NormalizeY(5, 0), 1e-6f);
        }

        [Test]
        public void AxisMaxRoundsUpToANiceStep()
        {
            Assert.AreEqual(1, ChampionshipChart.AxisMax(0));    // empty board
            Assert.AreEqual(1, ChampionshipChart.AxisMax(-5));   // guarded
            Assert.AreEqual(50, ChampionshipChart.AxisMax(50));  // exact (step 10)
            Assert.AreEqual(50, ChampionshipChart.AxisMax(45));  // rounds up
            Assert.AreEqual(150, ChampionshipChart.AxisMax(150)); // exact (step 25)
            Assert.AreEqual(200, ChampionshipChart.AxisMax(151)); // step 50 -> 200
        }

        [Test]
        public void AxisTicksAreEvenlySpaced()
        {
            CollectionAssert.AreEqual(new List<int> { 0, 25, 50, 75, 100 },
                ChampionshipChart.AxisTicks(100, 4));
            // Degenerate tick count falls back to endpoints only.
            CollectionAssert.AreEqual(new List<int> { 0, 80 }, ChampionshipChart.AxisTicks(80, 0));
        }

        [Test]
        public void NormalizeSeriesMapsEachRound()
        {
            var pts = ChampionshipChart.NormalizeSeries(new List<int> { 0, 25, 50 }, 3, 50);
            Assert.AreEqual(3, pts.Count);
            Assert.AreEqual(0f, pts[0].X, 1e-6f);
            Assert.AreEqual(0f, pts[0].Y, 1e-6f);
            Assert.AreEqual(0.5f, pts[1].X, 1e-6f);
            Assert.AreEqual(0.5f, pts[1].Y, 1e-6f);
            Assert.AreEqual(1f, pts[2].X, 1e-6f);
            Assert.AreEqual(1f, pts[2].Y, 1e-6f);
        }

        [Test]
        public void EmptyOrNullSeriesYieldsNoPoints()
        {
            Assert.AreEqual(0, ChampionshipChart.NormalizeSeries(null, 5, 100).Count);
            Assert.AreEqual(0, ChampionshipChart.NormalizeSeries(new List<int>(), 5, 100).Count);
        }
    }
}
