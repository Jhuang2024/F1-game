using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// SafetyCarPacing is the convoy control-law RaceManager.SafetyCar's autopilot
    /// now delegates to. The gap spacing, proportional speed correction, brake/
    /// throttle mapping, steering lookahead and red-flag hold braking are pinned
    /// here exactly as the pre-extraction inline maths behaved.
    /// </summary>
    public class SafetyCarPacingTests
    {
        [Test]
        public void GapPerCarGrowsWithPace()
        {
            Assert.AreEqual(14f, SafetyCarPacing.GapPerCarMeters(0f), 0.0001f);
            Assert.AreEqual(18f, SafetyCarPacing.GapPerCarMeters(80f), 0.0001f);
            Assert.AreEqual(22f, SafetyCarPacing.GapPerCarMeters(160f), 0.0001f);
            Assert.AreEqual(22f, SafetyCarPacing.GapPerCarMeters(250f), 0.0001f); // clamps
        }

        [Test]
        public void SpeedAdjustIsProportionalAndAsymmetricallyCapped()
        {
            Assert.AreEqual(0f, SafetyCarPacing.SpeedAdjustKph(0f), 0.0001f);
            Assert.AreEqual(12f, SafetyCarPacing.SpeedAdjustKph(10f), 0.0001f);      // 10 * 1.2
            Assert.AreEqual(25f, SafetyCarPacing.SpeedAdjustKph(50f), 0.0001f);      // caught up: +25 cap
            Assert.AreEqual(-45f, SafetyCarPacing.SpeedAdjustKph(-50f), 0.0001f);    // overshot: -45 floor
        }

        [Test]
        public void TargetSpeedIsCappedShortOfRacingPace()
        {
            Assert.AreEqual(100f, SafetyCarPacing.TargetSpeedKph(100f, 0f), 0.0001f);
            Assert.AreEqual(125f, SafetyCarPacing.TargetSpeedKph(100f, 25f), 0.0001f);   // sc + 25 ceiling
            Assert.AreEqual(55f, SafetyCarPacing.TargetSpeedKph(100f, -45f), 0.0001f);
            Assert.AreEqual(0f, SafetyCarPacing.TargetSpeedKph(10f, -45f), 0.0001f);     // floored at 0
        }

        [Test]
        public void BrakeThrottleHasADeadbandAndProportionalPedals()
        {
            float brake, throttle;

            // More than 3 km/h over target -> brake, no throttle.
            SafetyCarPacing.BrakeThrottle(100f, 110f, out brake, out throttle);
            Assert.AreEqual(0.25f, brake, 0.0001f);
            Assert.AreEqual(0f, throttle, 0.0001f);

            // Way over -> brake clamps to 1.
            SafetyCarPacing.BrakeThrottle(100f, 200f, out brake, out throttle);
            Assert.AreEqual(1f, brake, 0.0001f);

            // On target -> gentle idle throttle, no brake.
            SafetyCarPacing.BrakeThrottle(100f, 100f, out brake, out throttle);
            Assert.AreEqual(0f, brake, 0.0001f);
            Assert.AreEqual(0.15f, throttle, 0.0001f);

            // Behind target -> more throttle.
            SafetyCarPacing.BrakeThrottle(100f, 90f, out brake, out throttle);
            Assert.AreEqual(0.4f, throttle, 0.0001f);

            // Inside the -3 km/h deadband -> still throttle (slightly reduced), no brake.
            SafetyCarPacing.BrakeThrottle(100f, 102f, out brake, out throttle);
            Assert.AreEqual(0f, brake, 0.0001f);
            Assert.AreEqual(0.1f, throttle, 0.0001f);
        }

        [Test]
        public void LookAheadGrowsWithSpeed()
        {
            Assert.AreEqual(14f, SafetyCarPacing.LookAheadMeters(0f), 0.0001f);
            Assert.AreEqual(26f, SafetyCarPacing.LookAheadMeters(75f), 0.0001f);
            Assert.AreEqual(38f, SafetyCarPacing.LookAheadMeters(150f), 0.0001f);
            Assert.AreEqual(38f, SafetyCarPacing.LookAheadMeters(250f), 0.0001f); // clamps
        }

        [Test]
        public void RedFlagHoldBrakeIsSpeedScaledWithALightHold()
        {
            Assert.AreEqual(0.35f, SafetyCarPacing.RedFlagHoldBrake(0f), 0.0001f);
            Assert.AreEqual(0.35f, SafetyCarPacing.RedFlagHoldBrake(3f), 0.0001f);  // not > 3 -> light hold
            Assert.AreEqual(0.36f, SafetyCarPacing.RedFlagHoldBrake(70f), 0.0001f); // Lerp(0.22,0.5,0.5)
            Assert.AreEqual(0.5f, SafetyCarPacing.RedFlagHoldBrake(140f), 0.0001f);
            Assert.AreEqual(0.5f, SafetyCarPacing.RedFlagHoldBrake(250f), 0.0001f); // clamps
        }
    }
}
