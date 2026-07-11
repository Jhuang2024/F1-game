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
    }
}
