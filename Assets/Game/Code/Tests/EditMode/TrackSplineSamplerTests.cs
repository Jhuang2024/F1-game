using System.Collections.Generic;
using F1Game.Track;
using NUnit.Framework;
using UnityEngine;

namespace F1Game.Tests
{
    /// <summary>
    /// TrackSplineSampler underpins the authored-track runtime. Its distance
    /// wrapping/clamping and basic build invariants are pinned here on a simple
    /// square loop; exact interpolation values (spacing-dependent) are not
    /// asserted, only behaviour that must hold for any resampling.
    /// </summary>
    public class TrackSplineSamplerTests
    {
        static List<TrackDefinitionAsset.SplinePoint> Square()
        {
            return new List<TrackDefinitionAsset.SplinePoint>
            {
                new TrackDefinitionAsset.SplinePoint { position = new Vector3(0f, 0f, 0f), width = 12f },
                new TrackDefinitionAsset.SplinePoint { position = new Vector3(100f, 0f, 0f), width = 12f },
                new TrackDefinitionAsset.SplinePoint { position = new Vector3(100f, 0f, 100f), width = 12f },
                new TrackDefinitionAsset.SplinePoint { position = new Vector3(0f, 0f, 100f), width = 12f },
            };
        }

        [Test]
        public void BuildProducesSamplesAndPositiveLength()
        {
            var sampler = new TrackSplineSampler();
            sampler.Build(Square(), closedLoop: true, spacing: 6f);
            Assert.Greater(sampler.Length, 0f);
            Assert.Greater(sampler.Samples.Count, 0);
            Assert.IsTrue(sampler.ClosedLoop);
        }

        [Test]
        public void ClosedLoopWrapsDistanceModulo()
        {
            var sampler = new TrackSplineSampler();
            sampler.Build(Square(), closedLoop: true, spacing: 6f);
            float len = sampler.Length;

            // One lap plus 10 m wraps back to 10 m in; a small negative wraps to
            // near the end.
            Assert.AreEqual(10f, sampler.WrapDistance(len + 10f), 0.001f);
            Assert.AreEqual(len - 10f, sampler.WrapDistance(-10f), 0.001f);
            // Result always sits inside [0, len).
            float wrapped = sampler.WrapDistance(len * 3.5f);
            Assert.GreaterOrEqual(wrapped, 0f);
            Assert.Less(wrapped, len);
        }

        [Test]
        public void OpenSplineClampsDistance()
        {
            var sampler = new TrackSplineSampler();
            sampler.Build(Square(), closedLoop: false, spacing: 6f);
            float len = sampler.Length;

            Assert.AreEqual(len, sampler.WrapDistance(len + 500f), 0.001f);
            Assert.AreEqual(0f, sampler.WrapDistance(-500f), 0.001f);
        }

        [Test]
        public void AtDistanceStartHasZeroCumulativeDistance()
        {
            var sampler = new TrackSplineSampler();
            sampler.Build(Square(), closedLoop: true, spacing: 6f);
            TrackSplineSampler.Sample start = sampler.AtDistance(0f);
            // Cumulative distance is measured from the start/finish line.
            Assert.AreEqual(0f, start.Distance, 0.001f);
        }

        [Test]
        public void ClosedLoopActuallyCloses()
        {
            // The invariant that matters and was never asserted: sampling just
            // before the lap length must land next to sampling at zero. The dense
            // pass emitted t in [0,1) per segment and never measured the closing
            // chord, so Length came out short and the centreline jumped 12-22m at
            // the start/finish seam on the real circuits - a car crossing the line
            // teleported its reference by that much, once per lap, and every
            // distance-based gap glitched with it.
            var sampler = new TrackSplineSampler();
            sampler.Build(Square(), closedLoop: true, spacing: 3f);

            Vector3 atStart = sampler.AtDistance(0f).Position;
            Vector3 justBeforeEnd = sampler.AtDistance(sampler.Length - 1f).Position;
            Assert.Less(Vector3.Distance(atStart, justBeforeEnd), 6f,
                "closed loop does not close - seam gap at start/finish");
        }

        [Test]
        public void ClosedLoopLengthCoversTheWholePerimeter()
        {
            // A 100x100 square routed through Catmull-Rom rounds the corners, so the
            // arc is somewhat under the 400m chord perimeter but must not be far
            // under it. Before the closing chord was measured, Length was short by a
            // whole segment's tail.
            var sampler = new TrackSplineSampler();
            sampler.Build(Square(), closedLoop: true, spacing: 3f);
            Assert.Greater(sampler.Length, 330f, "closed-loop length is missing the closing segment");
            Assert.Less(sampler.Length, 420f);
        }

        [Test]
        public void BuildDoesNotHangOnNonPositiveSpacing()
        {
            // The resample loop advances `target` by `spacing`, so 0 or a negative
            // value never terminated.
            var sampler = new TrackSplineSampler();
            sampler.Build(Square(), closedLoop: true, spacing: 0f);
            Assert.Greater(sampler.Samples.Count, 0);
            Assert.Greater(sampler.Spacing, 0f);
        }
    }
}
