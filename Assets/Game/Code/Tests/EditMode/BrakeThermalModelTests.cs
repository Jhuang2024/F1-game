using F1Game.Race.Physics;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// The brake-disc thermal model: heating under braking, airflow-scaled cooling
    /// toward ambient, the temperature clamp, and the glow mapping. Pinned here (the
    /// model is structural / not yet live, but its curve is fixed for wiring later).
    /// </summary>
    public class BrakeThermalModelTests
    {
        [Test]
        public void HardBrakingAtSpeedHeatsTheDisc()
        {
            // From ambient, full brake at reference speed for 1s: heat 900, no cooling
            // yet (temp == ambient) -> 980 C.
            float t = BrakeThermalModel.Step(BrakeThermalModel.AmbientC, 1f, 300f, 1f);
            Assert.AreEqual(980f, t, 0.5f);
        }

        [Test]
        public void CoastingCoolsTowardAmbient()
        {
            // Hot disc, no braking, moderate airflow: cools by ~147 C in 1s.
            float t = BrakeThermalModel.Step(500f, 0f, 100f, 1f);
            Assert.AreEqual(353f, t, 0.5f);
            Assert.Less(t, 500f);
        }

        [Test]
        public void CoolingNeverFallsBelowAmbient()
        {
            float t = BrakeThermalModel.Step(85f, 0f, 0f, 1000f);
            Assert.AreEqual(BrakeThermalModel.AmbientC, t, 1e-4f);
        }

        [Test]
        public void TemperatureClampsAtMax()
        {
            float t = BrakeThermalModel.Step(999f, 1f, 300f, 10f);
            Assert.AreEqual(BrakeThermalModel.MaxC, t, 1e-4f);
        }

        [Test]
        public void ZeroDeltaTimeIsAClampedNoOp()
        {
            Assert.AreEqual(500f, BrakeThermalModel.Step(500f, 1f, 300f, 0f), 1e-4f);
            Assert.AreEqual(BrakeThermalModel.MaxC, BrakeThermalModel.Step(5000f, 1f, 300f, 0f), 1e-4f);
        }

        [Test]
        public void GlowRampsFromTheGlowStartToMax()
        {
            Assert.AreEqual(0f, BrakeThermalModel.GlowFromTemp(BrakeThermalModel.AmbientC), 1e-4f);
            Assert.AreEqual(1f, BrakeThermalModel.GlowFromTemp(BrakeThermalModel.MaxC), 1e-4f);
            Assert.AreEqual(0.5f, BrakeThermalModel.GlowFromTemp(675f), 1e-4f); // (675-350)/650
        }
    }
}
