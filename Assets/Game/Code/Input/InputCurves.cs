using System;

namespace F1Game.Input
{
    /// <summary>
    /// Pure input-shaping math shared by every analogue axis: dead zone,
    /// saturation, sensitivity (response curve), linearity and inversion.
    /// Values map from the existing GameSettingsData fields
    /// (controllerDeadzone, steering/throttle/brakeSensitivity).
    /// </summary>
    public static class InputCurves
    {
        [Serializable]
        public struct AxisTuning
        {
            public float deadZone;      // 0..0.5, input below this is zero
            public float saturation;    // input at/above this maps to 1 (e.g. 0.95)
            public float sensitivity;   // response curve exponent scale, 1 = linear
            public bool invert;

            public static AxisTuning Default => new AxisTuning
            {
                deadZone = 0.08f,
                saturation = 0.98f,
                sensitivity = 1f,
                invert = false,
            };
        }

        /// <summary>Shapes a raw [-1,1] axis through the tuning parameters.</summary>
        public static float Shape(float raw, in AxisTuning tuning)
        {
            float sign = Math.Sign(raw);
            float magnitude = Math.Abs(raw);

            float deadZone = Clamp(tuning.deadZone, 0f, 0.5f);
            float saturation = Clamp(tuning.saturation <= deadZone ? 1f : tuning.saturation, deadZone + 0.01f, 1f);

            // Re-normalise the live range between dead zone and saturation.
            magnitude = Clamp((magnitude - deadZone) / (saturation - deadZone), 0f, 1f);

            // Sensitivity as a response exponent: >1 = softer centre, <1 = sharper.
            float exponent = Clamp(tuning.sensitivity <= 0f ? 1f : 1f / tuning.sensitivity, 0.25f, 4f);
            magnitude = (float)Math.Pow(magnitude, exponent);

            float shaped = sign * magnitude;
            return tuning.invert ? -shaped : shaped;
        }

        static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
