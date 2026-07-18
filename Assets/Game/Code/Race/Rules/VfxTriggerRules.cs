namespace F1Game.Race.Rules
{
    /// <summary>
    /// Engine-free tyre/impact VFX trigger decisions, extracted VERBATIM from
    /// VehicleVfxDriver so the "should this effect fire this frame?" thresholds
    /// (normalized speed, slip, lockup/wheelspin/off-track/kerb gates, fresh-damage
    /// jump) are unit-testable without a live vehicle or particle pool. Pure
    /// predicates with the same thresholds as before (cosmetic VFX, no gameplay feel);
    /// the MonoBehaviour keeps owning cooldown timers, spawn positions and the pool.
    /// </summary>
    public static class VfxTriggerRules
    {
        /// <summary>Normalized speed 0..1 used by every gate (|kph| / 300).</summary>
        public static float Speed01(float speedKph)
        {
            float v = speedKph < 0f ? -speedKph : speedKph;
            return Clamp01(v / 300f);
        }

        /// <summary>Slip signal for wheelspin smoke. [VfxDiag] fix (per report -
        /// the constant "cloud behind the car"): understeer used to count toward
        /// this signal, but understeer is the FRONT washing wide - it produces
        /// no rear wheelspin and no smoke. Cars carry mild understeer through
        /// half of every lap, so the old formula kept the rear smoke spawner
        /// firing continuously. Oversteer only now.</summary>
        public static float Slip(float oversteerAmount, float understeerAmount) =>
            Clamp01(oversteerAmount);

        /// <summary>Lockup smoke: a locked tyre above 0.15 speed with the smoke timer elapsed.</summary>
        public static bool ShouldLockup(bool locked, float speed01, float smokeCooldown) =>
            locked && speed01 > 0.15f && smokeCooldown <= 0f;

        /// <summary>Wheelspin smoke: a genuine slide (slip past 0.55) above 0.05
        /// speed with the smoke timer elapsed - raised from 0.4 so ordinary
        /// corner-exit throttle no longer smokes.</summary>
        public static bool ShouldWheelspin(float slip, float speed01, float smokeCooldown) =>
            slip > 0.55f && speed01 > 0.05f && smokeCooldown <= 0f;

        /// <summary>Off-track gravel/dust: off-track slowdown above 0.08 speed with the dust timer elapsed.</summary>
        public static bool ShouldOffTrackDust(bool offTrackSlowdown, float speed01, float dustCooldown) =>
            offTrackSlowdown && speed01 > 0.08f && dustCooldown <= 0f;

        /// <summary>Kerb sparks: on a kerb above 0.65 speed (195+ kph) with the
        /// dust timer elapsed. [VfxDiag] fix (per report - sparks "arent
        /// supposed to be that common"): the old 0.25 gate (75 kph) fired at
        /// practically every apex, because the racing line legally rides the
        /// kerbs - sparks are now reserved for genuinely fast kerb strikes.</summary>
        public static bool ShouldKerbSparks(bool onKerb, float speed01, float dustCooldown) =>
            onKerb && speed01 > 0.65f && dustCooldown <= 0f;

        /// <summary>Impact burst: overall damage jumped by more than 0.04 since last frame.</summary>
        public static bool IsFreshDamageJump(float damagePercent, float lastDamagePercent) =>
            lastDamagePercent >= 0f && damagePercent > lastDamagePercent + 0.04f;

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
