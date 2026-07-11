using F1Game.Race;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// ReplaySerialization round-trips a ReplayRecording through its text format so
    /// a session's replay can be saved and reloaded. Frame transforms, speeds and
    /// markers (including spaced labels) must survive exactly; malformed input must
    /// not throw.
    /// </summary>
    public class ReplaySerializationTests
    {
        [Test]
        public void RoundTripsFramesAndMarkersExactly()
        {
            var rec = new ReplayRecording(carCount: 2, capacityFrames: 8);
            rec.RecordFrame(0f, new[]
            {
                new ReplayRecording.CarFrame { PosX = 1.5f, PosY = 2.25f, PosZ = -3.75f, RotX = 0f, RotY = 0f, RotZ = 0f, RotW = 1f, SpeedKph = 42.5f },
                new ReplayRecording.CarFrame { PosX = -10f, PosY = 0f, PosZ = 5f, RotX = 0.1f, RotY = 0.2f, RotZ = 0.3f, RotW = 0.9f, SpeedKph = 100f },
            });
            rec.RecordFrame(0.5f, new[]
            {
                new ReplayRecording.CarFrame { PosX = 12.5f, PosY = 2.25f, PosZ = -3.75f, RotW = 1f, SpeedKph = 55f },
                new ReplayRecording.CarFrame { PosX = -8f, PosZ = 6f, RotW = 1f, SpeedKph = 110f },
            });
            rec.AddMarker(0.2f, ReplayRecording.MarkerKind.Overtake, 1, "P2 into P1 at Turn 4");
            rec.AddMarker(0.4f, ReplayRecording.MarkerKind.Flag, -1, "YELLOW SECTOR 2");

            string text = ReplaySerialization.ToText(rec);
            ReplayRecording back = ReplaySerialization.FromText(text);

            Assert.IsNotNull(back);
            Assert.AreEqual(2, back.CarCount);
            Assert.AreEqual(2, back.FrameCount);
            Assert.AreEqual(2, back.Markers.Count);

            for (int f = 0; f < rec.FrameCount; f++)
            {
                Assert.IsTrue(rec.TryGetFrame(f, out var origHeader));
                Assert.IsTrue(back.TryGetFrame(f, out var backHeader));
                Assert.AreEqual(origHeader.Time, backHeader.Time, 0.0f, "frame " + f + " time");
                for (int c = 0; c < rec.CarCount; c++)
                {
                    var o = rec.GetCar(origHeader, c);
                    var b = back.GetCar(backHeader, c);
                    Assert.AreEqual(o.PosX, b.PosX, 0f); Assert.AreEqual(o.PosY, b.PosY, 0f); Assert.AreEqual(o.PosZ, b.PosZ, 0f);
                    Assert.AreEqual(o.RotX, b.RotX, 0f); Assert.AreEqual(o.RotY, b.RotY, 0f); Assert.AreEqual(o.RotZ, b.RotZ, 0f); Assert.AreEqual(o.RotW, b.RotW, 0f);
                    Assert.AreEqual(o.SpeedKph, b.SpeedKph, 0f);
                }
            }

            // Markers survive, including a label with spaces.
            Assert.AreEqual(0.2f, back.Markers[0].Time, 0f);
            Assert.AreEqual(ReplayRecording.MarkerKind.Overtake, back.Markers[0].Kind);
            Assert.AreEqual(1, back.Markers[0].CarIndex);
            Assert.AreEqual("P2 into P1 at Turn 4", back.Markers[0].Label);
            Assert.AreEqual(ReplayRecording.MarkerKind.Flag, back.Markers[1].Kind);
            Assert.AreEqual("YELLOW SECTOR 2", back.Markers[1].Label);
        }

        [Test]
        public void MalformedOrEmptyInputReturnsNullNotThrow()
        {
            Assert.IsNull(ReplaySerialization.FromText(null));
            Assert.IsNull(ReplaySerialization.FromText(""));
            Assert.IsNull(ReplaySerialization.FromText("not a replay file"));
            Assert.IsNull(ReplaySerialization.FromText("REPLAYv1 1"));   // truncated header
        }

        [Test]
        public void NullRecordingSerializesToAnEmptyButValidReplay()
        {
            string text = ReplaySerialization.ToText(null);
            ReplayRecording back = ReplaySerialization.FromText(text);
            Assert.IsNotNull(back);
            Assert.AreEqual(0, back.FrameCount);
            Assert.AreEqual(0, back.Markers.Count);
        }

        [Test]
        public void CorruptFrameLinesAreSkippedRatherThanThrowing()
        {
            // Header says 1 car / 2 frames but one frame line is short.
            string text = "REPLAYv1 1 1 2 0\nF 0 0 0 0 0 0 0 1 50\nF 1 garbage\n";
            ReplayRecording back = ReplaySerialization.FromText(text);
            Assert.IsNotNull(back);
            // Only the well-formed frame is kept.
            Assert.AreEqual(1, back.FrameCount);
            Assert.IsTrue(back.TryGetFrame(0, out var h));
            Assert.AreEqual(50f, back.GetCar(h, 0).SpeedKph, 0f);
        }
    }
}
