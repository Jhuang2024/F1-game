using System;
using F1Game.Race;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// ReplayPlayback samples an interpolated car transform at an arbitrary scrub
    /// time over a ReplayRecording - the pure data-layer foundation a replay
    /// camera / scrub bar will render from. Endpoint exactness, between-frame
    /// interpolation, clamping and the normalized-time conversions are pinned here.
    /// </summary>
    public class ReplayPlaybackTests
    {
        static ReplayRecording BuildStraightLine()
        {
            // One car, frames at t=0/1/2 moving along +X, speeding up, no rotation.
            var rec = new ReplayRecording(carCount: 1, capacityFrames: 8);
            rec.RecordFrame(0f, new[] { Frame(0f, 0f, 1f) });
            rec.RecordFrame(1f, new[] { Frame(10f, 100f, 1f) });
            rec.RecordFrame(2f, new[] { Frame(30f, 200f, 1f) });
            return rec;
        }

        static ReplayRecording.CarFrame Frame(float posX, float speed, float rotW)
        {
            return new ReplayRecording.CarFrame { PosX = posX, PosY = 0f, PosZ = 0f, RotX = 0f, RotY = 0f, RotZ = 0f, RotW = rotW, SpeedKph = speed };
        }

        [Test]
        public void SampleAtFrameTimesIsExact()
        {
            var rec = BuildStraightLine();
            Assert.AreEqual(0f, ReplayPlayback.SampleCar(rec, 0f, 0).PosX, 0.0001f);
            Assert.AreEqual(10f, ReplayPlayback.SampleCar(rec, 1f, 0).PosX, 0.0001f);
            Assert.AreEqual(30f, ReplayPlayback.SampleCar(rec, 2f, 0).PosX, 0.0001f);
        }

        [Test]
        public void SampleBetweenFramesInterpolatesPositionAndSpeed()
        {
            var rec = BuildStraightLine();
            // Halfway through the first segment (0..10 over t=0..1).
            var mid1 = ReplayPlayback.SampleCar(rec, 0.5f, 0);
            Assert.AreEqual(5f, mid1.PosX, 0.0001f);
            Assert.AreEqual(50f, mid1.SpeedKph, 0.0001f);

            // Halfway through the second segment (10..30 over t=1..2).
            var mid2 = ReplayPlayback.SampleCar(rec, 1.5f, 0);
            Assert.AreEqual(20f, mid2.PosX, 0.0001f);
            Assert.AreEqual(150f, mid2.SpeedKph, 0.0001f);
        }

        [Test]
        public void SampleClampsBeforeStartAndAfterEnd()
        {
            var rec = BuildStraightLine();
            Assert.AreEqual(0f, ReplayPlayback.SampleCar(rec, -5f, 0).PosX, 0.0001f);
            Assert.AreEqual(30f, ReplayPlayback.SampleCar(rec, 100f, 0).PosX, 0.0001f);
        }

        [Test]
        public void SampleGuardsEmptyRecordingAndBadCarIndex()
        {
            var empty = new ReplayRecording(1, 4);
            Assert.AreEqual(default(ReplayRecording.CarFrame), ReplayPlayback.SampleCar(empty, 0f, 0));
            Assert.AreEqual(default(ReplayRecording.CarFrame), ReplayPlayback.SampleCar(null, 0f, 0));

            var rec = BuildStraightLine();
            Assert.AreEqual(default(ReplayRecording.CarFrame), ReplayPlayback.SampleCar(rec, 1f, 5));
            Assert.AreEqual(default(ReplayRecording.CarFrame), ReplayPlayback.SampleCar(rec, 1f, -1));
        }

        [Test]
        public void InterpolatedRotationStaysUnitLength()
        {
            // Identity at t=0, a 90-degree yaw at t=1.
            var rec = new ReplayRecording(1, 4);
            rec.RecordFrame(0f, new[] { new ReplayRecording.CarFrame { RotW = 1f } });
            float s = (float)Math.Sqrt(0.5); // sin45 = cos45
            rec.RecordFrame(1f, new[] { new ReplayRecording.CarFrame { RotY = s, RotW = s } });

            var mid = ReplayPlayback.SampleCar(rec, 0.5f, 0);
            float mag = (float)Math.Sqrt(mid.RotX * mid.RotX + mid.RotY * mid.RotY + mid.RotZ * mid.RotZ + mid.RotW * mid.RotW);
            Assert.AreEqual(1f, mag, 0.0005f);
        }

        [Test]
        public void OppositeSignQuaternionsTakeTheShortArc()
        {
            // (0,0,0,1) and (0,0,0,-1) are the SAME orientation (double cover); the
            // shortest-arc nlerp must not cancel to a zero quaternion.
            var rec = new ReplayRecording(1, 4);
            rec.RecordFrame(0f, new[] { new ReplayRecording.CarFrame { RotW = 1f } });
            rec.RecordFrame(1f, new[] { new ReplayRecording.CarFrame { RotW = -1f } });

            var mid = ReplayPlayback.SampleCar(rec, 0.5f, 0);
            float mag = (float)Math.Sqrt(mid.RotX * mid.RotX + mid.RotY * mid.RotY + mid.RotZ * mid.RotZ + mid.RotW * mid.RotW);
            Assert.AreEqual(1f, mag, 0.0005f);
            Assert.AreEqual(1f, Math.Abs(mid.RotW), 0.0005f);
        }

        [Test]
        public void NormalizedTimeConversionsRoundTrip()
        {
            var rec = BuildStraightLine(); // start 0, end 2
            Assert.AreEqual(0f, ReplayPlayback.NormalizedTime(rec, 0f), 0.0001f);
            Assert.AreEqual(0.5f, ReplayPlayback.NormalizedTime(rec, 1f), 0.0001f);
            Assert.AreEqual(1f, ReplayPlayback.NormalizedTime(rec, 2f), 0.0001f);
            // Out of range clamps.
            Assert.AreEqual(0f, ReplayPlayback.NormalizedTime(rec, -3f), 0.0001f);
            Assert.AreEqual(1f, ReplayPlayback.NormalizedTime(rec, 9f), 0.0001f);

            Assert.AreEqual(0f, ReplayPlayback.TimeForNormalized(rec, 0f), 0.0001f);
            Assert.AreEqual(1f, ReplayPlayback.TimeForNormalized(rec, 0.5f), 0.0001f);
            Assert.AreEqual(2f, ReplayPlayback.TimeForNormalized(rec, 1f), 0.0001f);
        }
    }
}
