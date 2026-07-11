namespace F1Game.Race.Rules
{
    /// <summary>
    /// Pure safety-car / red-flag convoy control-law, extracted verbatim from
    /// RaceManager.SafetyCar's autopilot so the following-distance, proportional
    /// speed correction, brake/throttle mapping, steering lookahead and red-flag
    /// hold braking are stated and tested in one place. No engine dependency: the
    /// caller keeps every live state read (queue index, slot distance via
    /// Track.WrapDistance, own speed/position, the Track sampling for steering) and
    /// the command mutation; only these numeric control formulas live here. All
    /// speeds are km/h, distances metres, outputs 0-1 pedal fractions. Feel-sensitive
    /// numbers are unchanged - this is a relocation, not a retune.
    /// </summary>
    public static class SafetyCarPacing
    {
        /// <summary>
        /// Following distance per car in the queue, scaling slightly with pace - a
        /// faster-moving queue needs a little more room per car than a crawling one.
        /// </summary>
        public static float GapPerCarMeters(float scSpeedKph)
        {
            return Lerp(14f, 22f, Clamp01(scSpeedKph / 160f));
        }

        /// <summary>
        /// Proportional speed correction (km/h) toward a car's queue slot: positive
        /// signedSlotError (slot still ahead) closes in gently, negative (overshot)
        /// brakes back, capped well short of racing pace so a car that fell back
        /// during the deployment can't storm up on the queue.
        /// </summary>
        public static float SpeedAdjustKph(float signedSlotError)
        {
            return Clamp(signedSlotError * 1.2f, -45f, 25f);
        }

        /// <summary>
        /// The convoy speed target (km/h) around the safety car's own pace, clamped
        /// to [0, scSpeed + 25] so it never targets a genuine racing overspeed.
        /// </summary>
        public static float TargetSpeedKph(float scSpeedKph, float speedAdjustKph)
        {
            return Clamp(scSpeedKph + speedAdjustKph, 0f, scSpeedKph + 25f);
        }

        /// <summary>
        /// Proportional brake/throttle toward the target speed, with a -3 km/h
        /// deadband: more than 3 km/h over target brakes (throttle off), otherwise
        /// a gentle throttle that idles at 0.15 and rises as the car falls behind.
        /// </summary>
        public static void BrakeThrottle(float targetSpeedKph, float currentSpeedKph, out float brake, out float throttle)
        {
            float speedGapKph = targetSpeedKph - currentSpeedKph;
            if (speedGapKph < -3f)
            {
                brake = Clamp01(-speedGapKph / 40f);
                throttle = 0f;
            }
            else
            {
                brake = 0f;
                throttle = Clamp01(0.15f + speedGapKph / 40f);
            }
        }

        /// <summary>Steering lookahead distance (m), growing with speed.</summary>
        public static float LookAheadMeters(float currentSpeedKph)
        {
            return Lerp(14f, 38f, Clamp01(currentSpeedKph / 150f));
        }

        /// <summary>
        /// Red-flag hold braking (0-1): speed-scaled and capped so a queue slowing
        /// together doesn't brake-check itself, with a light hold once nearly stopped.
        /// </summary>
        public static float RedFlagHoldBrake(float currentSpeedKph)
        {
            return currentSpeedKph > 3f ? Lerp(0.22f, 0.5f, Clamp01(currentSpeedKph / 140f)) : 0.35f;
        }

        // Engine-free UnityEngine.Mathf equivalents (Lerp clamps t; Clamp/Clamp01
        // match Mathf exactly) so the control-law stays byte-identical.
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        static float Lerp(float a, float b, float t)
        {
            float clamped = t < 0f ? 0f : (t > 1f ? 1f : t);
            return a + (b - a) * clamped;
        }
    }
}
