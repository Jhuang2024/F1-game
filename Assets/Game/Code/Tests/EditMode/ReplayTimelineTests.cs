using F1Game.Race;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// The replay timeline is the in-game consumer of the captured markers
    /// (RaceManager.BuildReplayTimeline), so its per-kind counts, session-relative
    /// clock, start/end framing and empty handling are pinned here.
    /// </summary>
    public class ReplayTimelineTests
    {
        static ReplayRecording WithMarkers()
        {
            // Capture began at race-clock 30s, so the origin is 30 and the clock
            // must read from zero.
            var rec = new ReplayRecording(carCount: 2, capacityFrames: 8);
            rec.AddMarker(30f, ReplayRecording.MarkerKind.SessionStart, -1, "Session start");
            rec.AddMarker(35f, ReplayRecording.MarkerKind.Flag, -1, "YELLOW S2");
            rec.AddMarker(67.3f, ReplayRecording.MarkerKind.Overtake, 1, "P4 pass");
            rec.AddMarker(90f, ReplayRecording.MarkerKind.PitStop, 0, "pit stop");
            rec.AddMarker(120f, ReplayRecording.MarkerKind.Incident, 1, "spin");
            rec.AddMarker(150f, ReplayRecording.MarkerKind.SessionEnd, -1, "Session end");
            return rec;
        }

        [Test]
        public void NullRecordingHasNoData()
        {
            ReplayTimeline.Summary summary = ReplayTimeline.Build(null);
            Assert.IsFalse(summary.HasData);
            Assert.IsNotNull(summary.Highlights);
            Assert.AreEqual(0, summary.Highlights.Count);
        }

        [Test]
        public void CountsMarkersPerKind()
        {
            ReplayTimeline.Summary summary = ReplayTimeline.Build(WithMarkers());
            Assert.AreEqual(1, summary.FlagCount);
            Assert.AreEqual(1, summary.OvertakeCount);
            Assert.AreEqual(1, summary.PitStopCount);
            Assert.AreEqual(1, summary.IncidentCount);
            Assert.AreEqual(0, summary.LapCount);
            Assert.AreEqual(2, summary.CarCount);
        }

        [Test]
        public void HighlightsExcludeSessionFramingAndKeepOrder()
        {
            ReplayTimeline.Summary summary = ReplayTimeline.Build(WithMarkers());
            Assert.IsTrue(summary.HasData);
            // 6 markers minus SessionStart/SessionEnd -> 4 highlight rows.
            Assert.AreEqual(4, summary.Highlights.Count);
            Assert.AreEqual(ReplayRecording.MarkerKind.Flag, summary.Highlights[0].Kind);
            Assert.AreEqual(ReplayRecording.MarkerKind.Incident, summary.Highlights[3].Kind);
        }

        [Test]
        public void ClockIsRelativeToSessionStart()
        {
            ReplayTimeline.Summary summary = ReplayTimeline.Build(WithMarkers());
            // First flag at 35s with a 30s origin -> 5.0s in -> "0:05.0".
            Assert.AreEqual("0:05.0", summary.Highlights[0].Clock);
            // Duration spans origin (30) to last marker (150) = 120s.
            Assert.AreEqual(120f, summary.DurationSeconds, 0.001f);
        }

        [Test]
        public void FormatClockRendersMinutesAndTenths()
        {
            Assert.AreEqual("0:05.0", ReplayTimeline.FormatClock(5f));
            Assert.AreEqual("1:07.3", ReplayTimeline.FormatClock(67.3f));
            Assert.AreEqual("0:00.0", ReplayTimeline.FormatClock(-4f));
        }
    }
}
