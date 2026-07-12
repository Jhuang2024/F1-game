using F1Game.Race.Physics;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// Audio-only RPM reconstruction + rev-limiter gate. Pins the per-gear band mapping
    /// (0 at the bottom of a gear, 1 at the top), the top-gear extrapolation, degenerate
    /// safety, and the hysteresis/dwell/edge behaviour that stops the limiter cue from
    /// spamming on every upshift. Engine-free - no vehicle, no audio, no graphics device.
    /// </summary>
    public class AudioRpmModelTests
    {
        // The authoritative live schedule (VehicleController.AutoShiftUpKph): index g is
        // the speed at which gear g upshifts; gear g spans [schedule[g-1], schedule[g]].
        static readonly float[] Schedule = { 0f, 62f, 102f, 142f, 186f, 232f, 282f, 322f };

        [Test]
        public void RpmIsZeroAtGearBottomAndOneAtGearTop()
        {
            // Gear 2 spans 62..102.
            Assert.AreEqual(0f, AudioRpmModel.NormalizedRpm(62f, 2, Schedule), 1e-4f);
            Assert.AreEqual(1f, AudioRpmModel.NormalizedRpm(102f, 2, Schedule), 1e-4f);
            Assert.AreEqual(0.5f, AudioRpmModel.NormalizedRpm(82f, 2, Schedule), 1e-4f);
            // Gear 1 spans 0..62.
            Assert.AreEqual(0f, AudioRpmModel.NormalizedRpm(0f, 1, Schedule), 1e-4f);
            Assert.AreEqual(1f, AudioRpmModel.NormalizedRpm(62f, 1, Schedule), 1e-4f);
        }

        [Test]
        public void RpmClampsOutsideTheGearBand()
        {
            // Below the gear's band -> 0, above -> 1 (never negative / >1).
            Assert.AreEqual(0f, AudioRpmModel.NormalizedRpm(50f, 3, Schedule), 1e-4f);
            Assert.AreEqual(1f, AudioRpmModel.NormalizedRpm(500f, 3, Schedule), 1e-4f);
        }

        [Test]
        public void TopGearExtrapolatesOneBand()
        {
            // Gear 8 has no next threshold; last band is 322-282 = 40, so 322..362.
            Assert.AreEqual(0f, AudioRpmModel.NormalizedRpm(322f, 8, Schedule), 1e-4f);
            Assert.AreEqual(1f, AudioRpmModel.NormalizedRpm(362f, 8, Schedule), 1e-4f);
            Assert.AreEqual(0.5f, AudioRpmModel.NormalizedRpm(342f, 8, Schedule), 1e-4f);
        }

        [Test]
        public void DegenerateScheduleIsSafe()
        {
            Assert.AreEqual(0f, AudioRpmModel.NormalizedRpm(120f, 3, null), 1e-4f);
            Assert.AreEqual(0f, AudioRpmModel.NormalizedRpm(120f, 3, new float[] { 0f }), 1e-4f);
        }

        [Test]
        public void GearBelowOneClampsToFirstGear()
        {
            Assert.AreEqual(AudioRpmModel.NormalizedRpm(30f, 1, Schedule),
                            AudioRpmModel.NormalizedRpm(30f, 0, Schedule), 1e-4f);
        }

        [Test]
        public void LimiterFiresOnceAfterSustainedDwell()
        {
            var gate = new RevLimiterGate();
            int fires = 0;
            // Hold at the ceiling for ~0.5s in 0.05s steps (> 0.35 dwell).
            for (int i = 0; i < 10; i++)
            {
                if (gate.Update(1f, 0.05f))
                {
                    fires++;
                }
            }

            Assert.AreEqual(1, fires, "exactly one cue per sustained engagement");
            Assert.IsTrue(gate.Engaged);
        }

        [Test]
        public void MomentaryUpshiftTouchDoesNotFire()
        {
            var gate = new RevLimiterGate();
            // One frame at the ceiling (an upshift instant), then RPM drops as the next
            // gear engages. Dwell never reaches the threshold.
            bool a = gate.Update(1f, 0.016f);
            bool b = gate.Update(0.4f, 0.016f); // gear changed, rpm fell
            Assert.IsFalse(a);
            Assert.IsFalse(b);
            Assert.IsFalse(gate.Engaged);
        }

        [Test]
        public void BriefDipResetsDwellSoNoFire()
        {
            var gate = new RevLimiterGate();
            // Above enter for 0.3s (< 0.35 dwell)...
            gate.Update(0.98f, 0.3f);
            // ...then a dip below enter (but above exit) resets the dwell...
            gate.Update(0.93f, 0.05f);
            // ...so another 0.3s above enter still hasn't accumulated 0.35s continuously.
            bool fired = gate.Update(0.98f, 0.3f);
            Assert.IsFalse(fired);
        }

        [Test]
        public void ReArmsOnlyAfterDroppingBelowExit()
        {
            var gate = new RevLimiterGate();
            // Engage.
            gate.Update(1f, 0.4f);
            Assert.IsTrue(gate.Engaged);
            // Stay high in the hysteresis band: no re-fire, still engaged.
            Assert.IsFalse(gate.Update(0.95f, 0.4f));
            Assert.IsTrue(gate.Engaged);
            // Drop below exit -> re-arm.
            gate.Update(0.5f, 0.1f);
            Assert.IsFalse(gate.Engaged);
            // Sustained ceiling again fires a second cue.
            int fires = 0;
            for (int i = 0; i < 10; i++)
            {
                if (gate.Update(1f, 0.05f))
                {
                    fires++;
                }
            }

            Assert.AreEqual(1, fires);
        }
    }
}
