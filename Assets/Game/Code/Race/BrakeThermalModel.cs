using System;

namespace F1Game.Race.Physics
{
    /// <summary>
    /// Engine-free brake-disc thermal model - the missing evolution piece alongside
    /// <see cref="BrakeModel"/> (which already fades torque by temperature). A disc
    /// heats with braking energy (brake input x speed) and cools toward ambient at a
    /// rate that grows with airflow (speed), so hard braking at high speed heats
    /// fastest and a cooling lap sheds it. Structural + testable; NOT wired into the
    /// live loop, so tuned feel is unchanged - it is the model + seam for a future
    /// default-off brake-thermal pass, and it feeds the existing torque-fade
    /// (BrakeModel.TorqueScale) and the glow ramp (CarVisualCurves.BrakeGlow) via
    /// GlowFromTemp. Constants are placeholders (tuning pending).
    /// </summary>
    public static class BrakeThermalModel
    {
        public const float AmbientC = 80f;
        public const float MaxC = 1000f;

        /// <summary>Peak heating rate (deg C/s) at full brake and reference speed.</summary>
        public const float HeatingRate = 900f;

        /// <summary>Base cooling coefficient (per second) toward ambient.</summary>
        public const float CoolingBase = 0.15f;

        /// <summary>Extra cooling coefficient (per second) at reference airflow (speed).</summary>
        public const float AirflowCooling = 0.6f;

        const float ReferenceSpeedKph = 300f;

        /// <summary>
        /// Advance disc temperature (deg C) over <paramref name="deltaTime"/> seconds:
        /// braking adds heat proportional to brake input x normalized speed; cooling
        /// pulls toward ambient at a rate that rises with airflow. Clamped to
        /// [<see cref="AmbientC"/>, <see cref="MaxC"/>].
        /// </summary>
        public static float Step(float tempC, float brakeInput01, float speedKph, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return Clamp(tempC, AmbientC, MaxC);
            }

            float brake = Clamp01(brakeInput01);
            float speed01 = Clamp01(Abs(speedKph) / ReferenceSpeedKph);

            float heating = brake * speed01 * HeatingRate;
            float cooling = (CoolingBase + speed01 * AirflowCooling) * (tempC - AmbientC);

            float next = tempC + (heating - cooling) * deltaTime;
            return Clamp(next, AmbientC, MaxC);
        }

        /// <summary>
        /// Glow input (0..1) for CarVisualCurves.BrakeGlow: 0 below
        /// <paramref name="glowStartC"/>, ramping to 1 at <see cref="MaxC"/>.
        /// </summary>
        public static float GlowFromTemp(float tempC, float glowStartC = 350f)
        {
            float span = MaxC - glowStartC;
            if (span <= 0f)
            {
                return tempC >= MaxC ? 1f : 0f;
            }

            return Clamp01((tempC - glowStartC) / span);
        }

        static float Abs(float v) => v < 0f ? -v : v;

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
