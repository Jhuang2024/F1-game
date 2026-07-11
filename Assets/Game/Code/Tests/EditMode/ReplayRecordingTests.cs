using F1Game.Race;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// The replay recording ring buffer is now the live capture target
    /// (RaceManager.ReplayCaptureService), so its bounded storage, seek and
    /// marker behavior are worth pinning down.
    /// </summary>
    public class ReplayRecordingTests
    {
        static ReplayRecording.CarFrame[] Frame(int cars, float baseValue)
        {
            var f = new ReplayRecording.CarFrame[cars];
            for (int i = 0; i < cars; i++)
            {
                f[i] = new ReplayRecording.CarFrame { PosX = baseValue + i, SpeedKph = baseValue };
            }

            return f;
        }

        [Test]
        public void RecordsFramesAndReadsThemBack()
        {
            var rec = new ReplayRecording(carCount: 3, capacityFrames: 8);
            rec.RecordFrame(0f, Frame(3, 10f));
            rec.RecordFrame(1f, Frame(3, 20f));

            Assert.AreEqual(2, rec.FrameCount);
            Assert.AreEqual(3, rec.CarCount);
            Assert.IsTrue(rec.TryGetFrame(1, out ReplayRecording.FrameHeader h));
            Assert.AreEqual(1f, h.Time, 0.0001f);
            Assert.AreEqual(22f, rec.GetCar(h, 2).PosX, 0.0001f); // 20 base + car index 2
            Assert.AreEqual(20f, rec.GetCar(h, 0).SpeedKph, 0.0001f);
        }

        [Test]
        public void BoundedRingOverwritesOldestOnceFull()
        {
            var rec = new ReplayRecording(carCount: 1, capacityFrames: 3);
            for (int i = 0; i < 5; i++)
            {
                rec.RecordFrame(i, Frame(1, i));
            }

            // Only the last three frames survive; logical index 0 is the oldest kept.
            Assert.AreEqual(3, rec.FrameCount);
            Assert.AreEqual(2f, rec.StartTime, 0.0001f);
            Assert.AreEqual(4f, rec.EndTime, 0.0001f);
            Assert.IsTrue(rec.TryGetFrame(0, out ReplayRecording.FrameHeader oldest));
            Assert.AreEqual(2f, oldest.Time, 0.0001f);
        }

        [Test]
        public void SeekReturnsNearestFrameAtOrBeforeTime()
        {
            var rec = new ReplayRecording(1, 16);
            for (int i = 0; i < 10; i++)
            {
                rec.RecordFrame(i * 0.5f, Frame(1, i)); // times 0, 0.5, 1.0, ...
            }

            Assert.AreEqual(0, rec.SeekToTime(-1f));     // before start clamps to first
            Assert.AreEqual(4, rec.SeekToTime(2.0f));    // exact hit
            Assert.AreEqual(4, rec.SeekToTime(2.4f));    // between -> earlier frame
            Assert.AreEqual(9, rec.SeekToTime(999f));    // after end clamps to last
        }

        [Test]
        public void MarkersAreRetainedInOrder()
        {
            var rec = new ReplayRecording(1, 8);
            rec.AddMarker(0f, ReplayRecording.MarkerKind.SessionStart, -1, "start");
            rec.AddMarker(5f, ReplayRecording.MarkerKind.Flag, -1, "YELLOW");
            rec.AddMarker(9f, ReplayRecording.MarkerKind.SessionEnd, -1, "end");

            Assert.AreEqual(3, rec.Markers.Count);
            Assert.AreEqual(ReplayRecording.MarkerKind.Flag, rec.Markers[1].Kind);
            Assert.AreEqual("YELLOW", rec.Markers[1].Label);
        }

        [Test]
        public void RejectsUndersizedFrames()
        {
            var rec = new ReplayRecording(carCount: 4, capacityFrames: 4);
            rec.RecordFrame(0f, Frame(2, 0f)); // too few cars -> ignored
            Assert.AreEqual(0, rec.FrameCount);
        }
    }
}
