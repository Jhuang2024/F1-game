using UnityEngine.InputSystem;

namespace F1Game.Input
{
    /// <summary>
    /// Real gamepad vibration adapter implementing the force-feedback contract,
    /// replacing the permanent no-op stub. Wheel-class force feedback still
    /// falls back to <see cref="NoForceFeedback"/> until wheel hardware APIs are
    /// wired; this drives the low/high-frequency motors of the current gamepad
    /// scaled by the user's vibration setting.
    /// </summary>
    public sealed class GamepadRumbleFeedback : IForceFeedback
    {
        float vibrationScale = 1f;
        float lowConstant;
        float pulseUntilRealtime;
        float pulseLow;
        float pulseHigh;

        /// <summary>0..1 user vibration strength (from settings).</summary>
        public float VibrationScale
        {
            get => vibrationScale;
            set => vibrationScale = value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        public bool IsConnected => Gamepad.current != null;

        public float SteeringRangeDegrees { get; set; } = 540f;

        public void SetAligningTorque(float torque)
        {
            // Gamepads have no wheel torque; map |torque| onto a gentle constant
            // low-frequency bias so heavy load is felt as a low rumble.
            float t = torque < 0f ? -torque : torque;
            lowConstant = t > 1f ? 1f : t;
        }

        public void PulseImpulse(float magnitude, float durationSeconds)
        {
            float m = magnitude < 0f ? 0f : (magnitude > 1f ? 1f : magnitude);
            pulseLow = m;
            pulseHigh = m;
            pulseUntilRealtime = UnityEngine.Time.unscaledTime + (durationSeconds <= 0f ? 0.15f : durationSeconds);
            Apply();
        }

        public void SetTextureRumble(float magnitude, float frequencyHz)
        {
            float m = magnitude < 0f ? 0f : (magnitude > 1f ? 1f : magnitude);
            // Texture rides the high-frequency motor; store as the steady term.
            pulseHigh = m > pulseHigh ? m : pulseHigh;
            Apply();
        }

        public void StopAll()
        {
            lowConstant = 0f;
            pulseLow = 0f;
            pulseHigh = 0f;
            Gamepad.current?.ResetHaptics();
        }

        /// <summary>Ticked once per frame by the driving adapter.</summary>
        public void Tick()
        {
            if (UnityEngine.Time.unscaledTime > pulseUntilRealtime)
            {
                pulseLow = 0f;
                pulseHigh = 0f;
            }

            Apply();
        }

        void Apply()
        {
            Gamepad pad = Gamepad.current;
            if (pad == null)
            {
                return;
            }

            float low = (lowConstant + pulseLow) * vibrationScale;
            float high = pulseHigh * vibrationScale;
            pad.SetMotorSpeeds(Clamp01(low), Clamp01(high));
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
