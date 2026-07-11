using F1Game.Race;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// ReplayDirector picks which car a replay/broadcast camera focuses from the
    /// marker track and playhead. Priority (incident &gt; overtake &gt; pit &gt;
    /// lap/flag), nearest-in-time tie-break, the focus window and the default
    /// fallback are pinned here.
    /// </summary>
    public class ReplayDirectorTests
    {
        static ReplayRecording WithMarkers(int carCount)
        {
            // One frame so CarCount/bounds are real; markers drive the director.
            var rec = new ReplayRecording(carCount, 8);
            var frame = new ReplayRecording.CarFrame[carCount];
            rec.RecordFrame(0f, frame);
            rec.RecordFrame(20f, frame);
            return rec;
        }

        [Test]
        public void NoMarkersFallsBackToDefault()
        {
            var rec = WithMarkers(4);
            Assert.AreEqual(2, ReplayDirector.FocusCarIndex(rec, 5f, defaultCarIndex: 2));
            Assert.AreEqual(0, ReplayDirector.FocusCarIndex(null, 5f, defaultCarIndex: 0));
        }

        [Test]
        public void ANearbyCarMarkerPullsFocus()
        {
            var rec = WithMarkers(4);
            rec.AddMarker(5f, ReplayRecording.MarkerKind.Overtake, 3, "P4 passes P3");
            // Playhead near the overtake -> focus car 3, not the default.
            Assert.AreEqual(3, ReplayDirector.FocusCarIndex(rec, 5.5f, defaultCarIndex: 0));
            // Playhead far from any marker -> default.
            Assert.AreEqual(0, ReplayDirector.FocusCarIndex(rec, 15f, defaultCarIndex: 0));
        }

        [Test]
        public void HigherPriorityMarkerWinsWithinTheWindow()
        {
            var rec = WithMarkers(6);
            rec.AddMarker(5.0f, ReplayRecording.MarkerKind.Overtake, 2, "overtake");
            rec.AddMarker(5.2f, ReplayRecording.MarkerKind.Incident, 5, "crash");
            // Incident (priority 4) beats the slightly-closer overtake (priority 3).
            Assert.AreEqual(5, ReplayDirector.FocusCarIndex(rec, 5.1f, defaultCarIndex: 0));
        }

        [Test]
        public void SamePriorityPicksTheNearerMarker()
        {
            var rec = WithMarkers(6);
            rec.AddMarker(4.0f, ReplayRecording.MarkerKind.Overtake, 1, "far");
            rec.AddMarker(5.0f, ReplayRecording.MarkerKind.Overtake, 4, "near");
            // Playhead 5.1 -> car 4 (0.1 away) over car 1 (1.1 away).
            Assert.AreEqual(4, ReplayDirector.FocusCarIndex(rec, 5.1f, defaultCarIndex: 0));
        }

        [Test]
        public void FieldWideAndOutOfRangeMarkersAreIgnored()
        {
            var rec = WithMarkers(4);
            // A field-wide flag marker (carIndex -1) cannot pull focus to a car.
            rec.AddMarker(5f, ReplayRecording.MarkerKind.Flag, -1, "YELLOW");
            Assert.AreEqual(1, ReplayDirector.FocusCarIndex(rec, 5f, defaultCarIndex: 1));

            // A marker whose carIndex is out of range is ignored too.
            rec.AddMarker(6f, ReplayRecording.MarkerKind.Overtake, 99, "bad index");
            Assert.AreEqual(1, ReplayDirector.FocusCarIndex(rec, 6f, defaultCarIndex: 1));
        }

        [Test]
        public void MarkerOutsideTheWindowDoesNotPullFocus()
        {
            var rec = WithMarkers(4);
            rec.AddMarker(5f, ReplayRecording.MarkerKind.Incident, 3, "crash");
            // Default 2.5s window: a playhead 3s away is out of range.
            Assert.AreEqual(0, ReplayDirector.FocusCarIndex(rec, 8f, defaultCarIndex: 0));
            // A wider window brings it back in.
            Assert.AreEqual(3, ReplayDirector.FocusCarIndex(rec, 8f, defaultCarIndex: 0, windowSeconds: 5f));
        }
    }
}
