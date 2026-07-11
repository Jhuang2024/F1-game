using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// The continuous track-wetness model: target from rain intensity, wetting/drying
    /// toward that target without overshoot, the racing line drying faster, and the
    /// slick-tyre grip falloff. Pinned here (the model is not live yet, but its curve
    /// is fixed so wiring it later is a known quantity).
    /// </summary>
    public class TrackWetnessModelTests
    {
        [Test]
        public void TargetWetnessClampsRainIntensity()
        {
            Assert.AreEqual(0f, TrackWetnessModel.TargetWetness(-0.5f), 1e-6f);
            Assert.AreEqual(1f, TrackWetnessModel.TargetWetness(2f), 1e-6f);
            Assert.AreEqual(0.6f, TrackWetnessModel.TargetWetness(0.6f), 1e-6f);
        }

        [Test]
        public void WetsTowardTargetUnderRainWithoutOvershoot()
        {
            // Dry track, full rain: wets by the wetting rate per second.
            float w = TrackWetnessModel.Step(0f, 1f, onRacingLine: false, deltaTime: 1f);
            Assert.AreEqual(TrackWetnessModel.WettingRatePerSecond, w, 1e-6f);
            // A huge step cannot exceed the target.
            float capped = TrackWetnessModel.Step(0.9f, 1f, false, 100f);
            Assert.AreEqual(1f, capped, 1e-6f);
        }

        [Test]
        public void DriesWhenRainEasesAndTheLineDriesFaster()
        {
            // Fully wet, rain stopped: dries down. On-line dries more than off-line.
            float offLine = TrackWetnessModel.Step(1f, 0f, onRacingLine: false, deltaTime: 1f);
            float onLine = TrackWetnessModel.Step(1f, 0f, onRacingLine: true, deltaTime: 1f);
            Assert.Less(onLine, offLine);
            Assert.AreEqual(1f - TrackWetnessModel.BaseDryingRatePerSecond, offLine, 1e-6f);
            Assert.AreEqual(1f - (TrackWetnessModel.BaseDryingRatePerSecond + TrackWetnessModel.RacingLineDryingBonus), onLine, 1e-6f);
        }

        [Test]
        public void DryingCannotUndershootTheTarget()
        {
            // Nearly dry, no rain, big step: settles exactly at the dry target, not below.
            float w = TrackWetnessModel.Step(0.005f, 0f, onRacingLine: true, deltaTime: 100f);
            Assert.AreEqual(0f, w, 1e-6f);
        }

        [Test]
        public void AtEquilibriumWetnessHolds()
        {
            // Wetness already equals the rain target -> unchanged.
            Assert.AreEqual(0.6f, TrackWetnessModel.Step(0.6f, 0.6f, false, 5f), 1e-6f);
        }

        [Test]
        public void GripFallsMonotonicallyWithWetness()
        {
            Assert.AreEqual(1f, TrackWetnessModel.GripMultiplier(0f), 1e-6f);
            Assert.AreEqual(1f - TrackWetnessModel.MaxWetGripPenalty, TrackWetnessModel.GripMultiplier(1f), 1e-6f);
            Assert.Less(TrackWetnessModel.GripMultiplier(0.5f), TrackWetnessModel.GripMultiplier(0.2f));
        }
    }
}
