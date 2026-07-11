using F1Game.Core.Diagnostics;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// The telemetry recorder is the live player-trace target
    /// (RaceManager.TelemetryCaptureService), so its append/cap/clear behavior is
    /// worth pinning down. CSV export touches persistentDataPath and is exercised
    /// at runtime, not here.
    /// </summary>
    public class TelemetryRecorderTests
    {
        static TelemetryRecorder.Sample Sample(float time, float speed)
        {
            return new TelemetryRecorder.Sample
            {
                Time = time,
                SpeedKph = speed,
                Throttle = 1f,
                Gear = 4,
            };
        }

        [Test]
        public void RecordsSamplesAndCounts()
        {
            var rec = new TelemetryRecorder(capacity: 8);
            rec.Record(Sample(0f, 100f));
            rec.Record(Sample(0.05f, 120f));

            Assert.AreEqual(2, rec.Count);
        }

        [Test]
        public void StopsAppendingOnceCapReached()
        {
            var rec = new TelemetryRecorder(capacity: 3);
            for (int i = 0; i < 10; i++)
            {
                rec.Record(Sample(i, i * 10f));
            }

            // The recorder does not wrap; it caps to keep a clean trace from start.
            Assert.AreEqual(3, rec.Count);
        }

        [Test]
        public void ClampsNonPositiveCapacityToOne()
        {
            var rec = new TelemetryRecorder(capacity: 0);
            rec.Record(Sample(0f, 50f));
            rec.Record(Sample(1f, 60f));

            Assert.AreEqual(1, rec.Count);
        }

        [Test]
        public void ClearResetsCount()
        {
            var rec = new TelemetryRecorder(capacity: 4);
            rec.Record(Sample(0f, 100f));
            rec.Record(Sample(1f, 110f));
            rec.Clear();

            Assert.AreEqual(0, rec.Count);
        }
    }
}
