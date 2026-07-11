using F1Game.Race;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// ReplayPlaybackController is the engine-free replay transport (play/pause/
    /// seek/speed over a ReplayRecording) a scrub bar and replay camera drive. Its
    /// playhead advance, clamping, stop-at-end, replay-from-end and seek behaviour
    /// are pinned here.
    /// </summary>
    public class ReplayPlaybackControllerTests
    {
        static ReplayRecording ThreeSecondRecording()
        {
            var rec = new ReplayRecording(1, 8);
            rec.RecordFrame(0f, new[] { new ReplayRecording.CarFrame { PosX = 0f, RotW = 1f } });
            rec.RecordFrame(1f, new[] { new ReplayRecording.CarFrame { PosX = 10f, RotW = 1f } });
            rec.RecordFrame(2f, new[] { new ReplayRecording.CarFrame { PosX = 30f, RotW = 1f } });
            return rec;
        }

        [Test]
        public void SetRecordingStartsAtBeginningPaused()
        {
            var c = new ReplayPlaybackController();
            c.SetRecording(ThreeSecondRecording());
            Assert.IsTrue(c.HasRecording);
            Assert.AreEqual(0f, c.PlayheadSeconds, 0.0001f);
            Assert.IsFalse(c.IsPlaying);
            Assert.AreEqual(2f, c.DurationSeconds, 0.0001f);
        }

        [Test]
        public void AdvanceMovesPlayheadScaledBySpeedOnlyWhilePlaying()
        {
            var c = new ReplayPlaybackController();
            c.SetRecording(ThreeSecondRecording());

            // Paused: no movement.
            c.Advance(0.5f);
            Assert.AreEqual(0f, c.PlayheadSeconds, 0.0001f);

            c.Play();
            c.Advance(0.5f);
            Assert.AreEqual(0.5f, c.PlayheadSeconds, 0.0001f);

            // Double speed advances twice as fast.
            c.SetSpeed(2f);
            c.Advance(0.5f);
            Assert.AreEqual(1.5f, c.PlayheadSeconds, 0.0001f);
        }

        [Test]
        public void PlaybackStopsAndPausesAtTheEnd()
        {
            var c = new ReplayPlaybackController();
            c.SetRecording(ThreeSecondRecording());
            c.Play();
            c.Advance(10f); // overshoot
            Assert.AreEqual(2f, c.PlayheadSeconds, 0.0001f);
            Assert.IsTrue(c.AtEnd);
            Assert.IsFalse(c.IsPlaying);
        }

        [Test]
        public void PlayFromEndRewindsToStart()
        {
            var c = new ReplayPlaybackController();
            c.SetRecording(ThreeSecondRecording());
            c.Play();
            c.Advance(10f);
            Assert.IsTrue(c.AtEnd);

            c.Play(); // "replay again"
            Assert.AreEqual(0f, c.PlayheadSeconds, 0.0001f);
            Assert.IsTrue(c.IsPlaying);
        }

        [Test]
        public void SeekClampsAndNormalizedRoundTrips()
        {
            var c = new ReplayPlaybackController();
            c.SetRecording(ThreeSecondRecording());

            c.Seek(1f);
            Assert.AreEqual(1f, c.PlayheadSeconds, 0.0001f);
            Assert.AreEqual(0.5f, c.NormalizedPlayhead, 0.0001f);

            c.Seek(-5f);
            Assert.AreEqual(0f, c.PlayheadSeconds, 0.0001f);
            c.Seek(99f);
            Assert.AreEqual(2f, c.PlayheadSeconds, 0.0001f);

            c.SeekNormalized(0.25f);
            Assert.AreEqual(0.5f, c.PlayheadSeconds, 0.0001f);
        }

        [Test]
        public void SpeedIsClampedToASaneBand()
        {
            var c = new ReplayPlaybackController();
            c.SetRecording(ThreeSecondRecording());
            c.SetSpeed(1000f);
            Assert.LessOrEqual(c.SpeedMultiplier, 16f);
            c.SetSpeed(-3f);
            Assert.GreaterOrEqual(c.SpeedMultiplier, 0.05f);
        }

        [Test]
        public void SampleCarAtPlayheadInterpolates()
        {
            var c = new ReplayPlaybackController();
            c.SetRecording(ThreeSecondRecording());
            c.Seek(0.5f); // halfway 0..10
            Assert.AreEqual(5f, c.SampleCar(0).PosX, 0.0001f);
        }

        [Test]
        public void EmptyTransportIsInert()
        {
            var c = new ReplayPlaybackController();
            Assert.IsFalse(c.HasRecording);
            c.Play();
            Assert.IsFalse(c.IsPlaying);
            c.Advance(1f);
            Assert.AreEqual(0f, c.PlayheadSeconds, 0.0001f);
        }
    }
}
