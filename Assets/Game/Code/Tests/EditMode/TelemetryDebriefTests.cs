using F1Game.Core.Diagnostics;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// The debrief is the in-game consumer of a captured telemetry trace
    /// (RaceManager.BuildTelemetryDebrief), so its aggregate maths - time shares,
    /// top/avg speed, tyre-wear delta, empty-trace handling - are pinned here.
    /// </summary>
    public class TelemetryDebriefTests
    {
        static TelemetryRecorder.Sample S(float time, float distance, float speed, float throttle, float brake, bool drs, float ers, float wear, int gear)
        {
            return new TelemetryRecorder.Sample
            {
                Time = time,
                Distance = distance,
                SpeedKph = speed,
                Throttle = throttle,
                Brake = brake,
                Drs = drs,
                Ers01 = ers,
                TyreWear01 = wear,
                Gear = gear,
            };
        }

        [Test]
        public void EmptyTraceHasNoData()
        {
            TelemetryDebrief.Summary summary = TelemetryDebrief.Build((TelemetryRecorder)null);
            Assert.IsFalse(summary.HasData);
            Assert.AreEqual(0, summary.SampleCount);
        }

        [Test]
        public void AggregatesSpeedGearDurationAndDistance()
        {
            var rec = new TelemetryRecorder(capacity: 16);
            rec.Record(S(10f, 100f, 200f, 1f, 0f, false, 0.5f, 0.0f, 6));
            rec.Record(S(11f, 260f, 300f, 1f, 0f, true, 0.7f, 0.1f, 7));

            TelemetryDebrief.Summary summary = TelemetryDebrief.Build(rec);
            Assert.IsTrue(summary.HasData);
            Assert.AreEqual(2, summary.SampleCount);
            Assert.AreEqual(300f, summary.TopSpeedKph, 0.001f);
            Assert.AreEqual(250f, summary.AverageSpeedKph, 0.001f);
            Assert.AreEqual(7, summary.TopGear);
            Assert.AreEqual(1f, summary.DurationSeconds, 0.001f);
            Assert.AreEqual(160f, summary.DistanceMeters, 0.001f);
        }

        [Test]
        public void ClassifiesThrottleBrakeCoastShares()
        {
            var rec = new TelemetryRecorder(capacity: 16);
            rec.Record(S(0f, 0f, 100f, 1.0f, 0f, false, 0f, 0f, 5));   // full throttle
            rec.Record(S(1f, 0f, 90f, 0.0f, 1f, false, 0f, 0f, 3));    // braking (brake wins over throttle)
            rec.Record(S(2f, 0f, 80f, 0.0f, 0f, false, 0f, 0f, 4));    // coasting
            rec.Record(S(3f, 0f, 80f, 0.5f, 0f, false, 0f, 0f, 4));    // partial (none of the three)

            TelemetryDebrief.Summary summary = TelemetryDebrief.Build(rec);
            Assert.AreEqual(25f, summary.FullThrottlePercent, 0.001f);
            Assert.AreEqual(25f, summary.BrakingPercent, 0.001f);
            Assert.AreEqual(25f, summary.CoastingPercent, 0.001f);
        }

        [Test]
        public void DrsShareAndTyreWearDelta()
        {
            var rec = new TelemetryRecorder(capacity: 16);
            rec.Record(S(0f, 0f, 100f, 1f, 0f, true, 0f, 0.05f, 6));
            rec.Record(S(1f, 0f, 100f, 1f, 0f, false, 0f, 0.20f, 6));

            TelemetryDebrief.Summary summary = TelemetryDebrief.Build(rec);
            Assert.AreEqual(50f, summary.DrsPercent, 0.001f);
            Assert.AreEqual(0.05f, summary.TyreWearStart01, 0.001f);
            Assert.AreEqual(0.20f, summary.TyreWearEnd01, 0.001f);
            Assert.AreEqual(0.15f, summary.TyreWearDelta01, 0.001f);
        }
    }
}
