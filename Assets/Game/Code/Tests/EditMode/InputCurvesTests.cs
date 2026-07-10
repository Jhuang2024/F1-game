using F1Game.Input;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class InputCurvesTests
    {
        static InputCurves.AxisTuning Tuning(float deadZone = 0.1f, float saturation = 0.9f, float sensitivity = 1f, bool invert = false)
        {
            return new InputCurves.AxisTuning
            {
                deadZone = deadZone,
                saturation = saturation,
                sensitivity = sensitivity,
                invert = invert,
            };
        }

        [Test]
        public void DeadZoneSilencesSmallInput()
        {
            Assert.AreEqual(0f, InputCurves.Shape(0.05f, Tuning(deadZone: 0.1f)));
            Assert.AreEqual(0f, InputCurves.Shape(-0.09f, Tuning(deadZone: 0.1f)));
        }

        [Test]
        public void SaturationReachesFullDeflectionEarly()
        {
            Assert.AreEqual(1f, InputCurves.Shape(0.95f, Tuning(saturation: 0.9f)), 0.0001f);
            Assert.AreEqual(-1f, InputCurves.Shape(-1f, Tuning(saturation: 0.9f)), 0.0001f);
        }

        [Test]
        public void LinearSensitivityIsProportionalInsideLiveRange()
        {
            // Midpoint of the 0.1..0.9 live range should map to 0.5 at linearity 1.
            float mid = InputCurves.Shape(0.5f, Tuning(deadZone: 0.1f, saturation: 0.9f, sensitivity: 1f));
            Assert.AreEqual(0.5f, mid, 0.0001f);
        }

        [Test]
        public void HigherSensitivitySoftensTheCentre()
        {
            float linear = InputCurves.Shape(0.5f, Tuning(sensitivity: 1f));
            float soft = InputCurves.Shape(0.5f, Tuning(sensitivity: 2f));
            Assert.Less(linear, soft, "sensitivity > 1 should raise mid-range response");
        }

        [Test]
        public void InversionFlipsSign()
        {
            float normal = InputCurves.Shape(0.6f, Tuning());
            float inverted = InputCurves.Shape(0.6f, Tuning(invert: true));
            Assert.AreEqual(-normal, inverted, 0.0001f);
        }

        [Test]
        public void DegenerateTuningIsClampedSafely()
        {
            // Saturation below dead zone and zero sensitivity must not divide by
            // zero or NaN.
            float shaped = InputCurves.Shape(0.7f, Tuning(deadZone: 0.4f, saturation: 0.1f, sensitivity: 0f));
            Assert.IsFalse(float.IsNaN(shaped));
            Assert.GreaterOrEqual(shaped, 0f);
            Assert.LessOrEqual(shaped, 1f);
        }
    }
}
