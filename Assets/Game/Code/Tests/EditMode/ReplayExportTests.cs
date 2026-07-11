using F1Game.Race;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// ReplayExport turns a captured recording's markers into CSV for offline
    /// review. Header-only on empty input, one row per marker with a
    /// session-relative clock, and comma/quote-safe labels are pinned here.
    /// </summary>
    public class ReplayExportTests
    {
        [Test]
        public void EmptyRecordingYieldsHeaderOnly()
        {
            Assert.AreEqual(ReplayExport.Header + "\n", ReplayExport.MarkersToCsv(null));
            var empty = new ReplayRecording(carCount: 2, capacityFrames: 4);
            Assert.AreEqual(ReplayExport.Header + "\n", ReplayExport.MarkersToCsv(empty));
        }

        [Test]
        public void WritesOneRowPerMarkerWithRelativeClock()
        {
            var rec = new ReplayRecording(carCount: 2, capacityFrames: 8);
            rec.AddMarker(30f, ReplayRecording.MarkerKind.SessionStart, -1, "Session start");
            rec.AddMarker(95f, ReplayRecording.MarkerKind.Overtake, 1, "P4 pass");

            string csv = ReplayExport.MarkersToCsv(rec);
            string[] lines = csv.Split('\n');
            Assert.AreEqual(ReplayExport.Header, lines[0]);
            // Origin is 30, so the overtake at 95 reads 1:05.0 in.
            Assert.AreEqual("30,0:00.0,SessionStart,-1,Session start", lines[1]);
            Assert.AreEqual("95,1:05.0,Overtake,1,P4 pass", lines[2]);
        }

        [Test]
        public void LabelWithCommaIsQuoted()
        {
            var rec = new ReplayRecording(carCount: 1, capacityFrames: 4);
            rec.AddMarker(10f, ReplayRecording.MarkerKind.Incident, 0, "spin, contact");

            string csv = ReplayExport.MarkersToCsv(rec);
            StringAssert.Contains("\"spin, contact\"", csv);
        }
    }
}
