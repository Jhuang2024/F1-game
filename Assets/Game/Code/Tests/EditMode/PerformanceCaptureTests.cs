using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// PerformanceCapture's percentile statistic is the core of its frame-time
    /// report, so its nearest-rank behaviour and edge cases are pinned here.
    /// </summary>
    public class PerformanceCaptureTests
    {
        [Test]
        public void EmptyOrNullSampleReturnsZero()
        {
            Assert.AreEqual(0f, PerformanceCapture.Percentile(null, 0.95f));
            Assert.AreEqual(0f, PerformanceCapture.Percentile(new List<float>(), 0.95f));
        }

        [Test]
        public void NearestRankPicksExpectedSample()
        {
            // Ten ascending samples 1..10. p95 -> ceil(0.95*10)-1 = 9 -> 10.
            var sorted = new List<float> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            Assert.AreEqual(10f, PerformanceCapture.Percentile(sorted, 0.95f));
            Assert.AreEqual(10f, PerformanceCapture.Percentile(sorted, 0.99f));
            // p50 -> ceil(5)-1 = 4 -> 5.
            Assert.AreEqual(5f, PerformanceCapture.Percentile(sorted, 0.5f));
        }

        [Test]
        public void ClampsBelowAndAboveRange()
        {
            var sorted = new List<float> { 3, 7, 11 };
            Assert.AreEqual(3f, PerformanceCapture.Percentile(sorted, 0f));   // clamps to first
            Assert.AreEqual(11f, PerformanceCapture.Percentile(sorted, 1f));  // last
        }
    }
}
