namespace F1Game.Input
{
    /// <summary>
    /// Steering-wheel force-feedback contract. Full hardware support is a later
    /// milestone; the interface exists now so vehicle/physics code can publish
    /// FFB terms without knowing whether real hardware is attached.
    /// </summary>
    public interface IForceFeedback
    {
        bool IsConnected { get; }

        /// <summary>Steering rotation range in degrees the device is set to (e.g. 360–1080).</summary>
        float SteeringRangeDegrees { get; set; }

        /// <summary>Constant aligning torque, -1..1 (self-centering / mechanical trail).</summary>
        void SetAligningTorque(float torque);

        /// <summary>Short additive effects: kerb strike, collision, gear shift.</summary>
        void PulseImpulse(float magnitude, float durationSeconds);

        /// <summary>Continuous texture (surface rumble / lockup judder), 0 disables.</summary>
        void SetTextureRumble(float magnitude, float frequencyHz);

        void StopAll();
    }

    /// <summary>Default no-hardware implementation; also used when FFB is disabled.</summary>
    public sealed class NoForceFeedback : IForceFeedback
    {
        public bool IsConnected => false;
        public float SteeringRangeDegrees { get; set; } = 540f;
        public void SetAligningTorque(float torque) { }
        public void PulseImpulse(float magnitude, float durationSeconds) { }
        public void SetTextureRumble(float magnitude, float frequencyHz) { }
        public void StopAll() { }
    }

    /// <summary>Gamepad rumble adapter (vibration setting), stub until wired to settings.</summary>
    public static class ForceFeedbackHub
    {
        public static IForceFeedback Active { get; set; } = new NoForceFeedback();
    }
}
