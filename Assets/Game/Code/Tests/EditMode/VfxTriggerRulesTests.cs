using F1Game.Race.Rules;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// Pins the tyre/impact VFX trigger thresholds (normalized speed, slip, lockup/
    /// wheelspin/off-track/kerb gates, fresh-damage jump) exactly as VehicleVfxDriver
    /// applied them, so a refactor can't silently change when effects fire.
    /// </summary>
    public class VfxTriggerRulesTests
    {
        [Test]
        public void Speed01NormalizesAndClampsMagnitude()
        {
            Assert.AreEqual(0.5f, VfxTriggerRules.Speed01(150f), 1e-6f);
            Assert.AreEqual(1f, VfxTriggerRules.Speed01(400f), 1e-6f);
            // Uses magnitude (reverse still registers).
            Assert.AreEqual(0.5f, VfxTriggerRules.Speed01(-150f), 1e-6f);
        }

        [Test]
        public void SlipCombinesOversteerAndHalfUndersteer()
        {
            Assert.AreEqual(0.5f, VfxTriggerRules.Slip(0.4f, 0.2f), 1e-6f);
            Assert.AreEqual(1f, VfxTriggerRules.Slip(0.9f, 0.9f), 1e-6f); // clamped
        }

        [Test]
        public void LockupNeedsLockSpeedAndCooldownElapsed()
        {
            Assert.IsTrue(VfxTriggerRules.ShouldLockup(true, 0.2f, 0f));
            Assert.IsFalse(VfxTriggerRules.ShouldLockup(false, 0.2f, 0f));   // not locked
            Assert.IsFalse(VfxTriggerRules.ShouldLockup(true, 0.15f, 0f));   // exactly at gate, not above
            Assert.IsFalse(VfxTriggerRules.ShouldLockup(true, 0.2f, 0.05f)); // cooldown not elapsed
        }

        [Test]
        public void WheelspinNeedsSlipSpeedAndCooldownElapsed()
        {
            Assert.IsTrue(VfxTriggerRules.ShouldWheelspin(0.41f, 0.06f, 0f));
            Assert.IsFalse(VfxTriggerRules.ShouldWheelspin(0.4f, 0.06f, 0f));   // not above slip gate
            Assert.IsFalse(VfxTriggerRules.ShouldWheelspin(0.41f, 0.05f, 0f));  // not above speed gate
            Assert.IsFalse(VfxTriggerRules.ShouldWheelspin(0.41f, 0.06f, 0.1f));// cooldown
        }

        [Test]
        public void OffTrackAndKerbGatesRespectTheirSpeedThresholds()
        {
            Assert.IsTrue(VfxTriggerRules.ShouldOffTrackDust(true, 0.09f, 0f));
            Assert.IsFalse(VfxTriggerRules.ShouldOffTrackDust(true, 0.08f, 0f));
            Assert.IsTrue(VfxTriggerRules.ShouldKerbSparks(true, 0.26f, 0f));
            Assert.IsFalse(VfxTriggerRules.ShouldKerbSparks(true, 0.25f, 0f));
            Assert.IsFalse(VfxTriggerRules.ShouldKerbSparks(false, 0.26f, 0f));
        }

        [Test]
        public void FreshDamageJumpRequiresAPriorReadingAndAJumpOverThreshold()
        {
            // First frame (lastDamagePercent -1) never fires.
            Assert.IsFalse(VfxTriggerRules.IsFreshDamageJump(0.5f, -1f));
            Assert.IsTrue(VfxTriggerRules.IsFreshDamageJump(0.5f, 0.45f));   // +0.05 > 0.04
            Assert.IsFalse(VfxTriggerRules.IsFreshDamageJump(0.48f, 0.45f)); // +0.03 <= 0.04
        }
    }
}
