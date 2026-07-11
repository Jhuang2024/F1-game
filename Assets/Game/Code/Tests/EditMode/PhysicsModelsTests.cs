using F1Game.Race.Physics;
using NUnit.Framework;

namespace F1Game.Tests
{
    public class PhysicsModelsTests
    {
        [Test]
        public void DragMatchesTheLiveCoefficients()
        {
            // Closed wing: plain v²c.
            Assert.AreEqual(50f * 50f * AeroModel.DrsClosedDragCoefficient,
                AeroModel.Drag(50f, AeroModel.DrsClosedDragCoefficient, false, AeroModel.DrsDragReductionFraction), 0.0005f);

            // Open wing reproduces the live DRS coefficient from the reduction fraction.
            Assert.AreEqual(50f * 50f * AeroModel.DrsOpenDragCoefficient,
                AeroModel.Drag(50f, AeroModel.DrsClosedDragCoefficient, true, AeroModel.DrsDragReductionFraction), 0.0005f);
        }

        [Test]
        public void SlipstreamTowIsSmallerThanDrs()
        {
            Assert.AreEqual(1f, AeroModel.SlipstreamDragFactor(0f), 0.0001f);
            Assert.AreEqual(1f - AeroModel.SlipstreamDragReduction, AeroModel.SlipstreamDragFactor(1f), 0.0001f);
            Assert.Less(AeroModel.SlipstreamDragReduction, AeroModel.DrsDragReductionFraction);
        }

        [Test]
        public void DownforceScalesWithSpeedSquaredAndDamage()
        {
            float baseline = AeroModel.Downforce(50f, AeroModel.DownforceCoefficient);
            Assert.AreEqual(4f * baseline, AeroModel.Downforce(100f, AeroModel.DownforceCoefficient), 0.01f);
            Assert.AreEqual(0.5f * baseline, AeroModel.Downforce(50f, AeroModel.DownforceCoefficient, 1f, 0.5f), 0.01f);
        }

        [Test]
        public void ErsBoostAndDrainMatchTheLiveRanges()
        {
            Assert.AreEqual(PowertrainModel.MinErsBoostForce, PowertrainModel.ErsBoostForce(0f, 1f), 0.0001f);
            Assert.AreEqual(PowertrainModel.MaxErsBoostForce, PowertrainModel.ErsBoostForce(1f, 1f), 0.0001f);
            Assert.AreEqual(PowertrainModel.MaxErsBoostForce * 2f, PowertrainModel.ErsBoostForce(1f, 2f), 0.0001f);

            Assert.AreEqual(PowertrainModel.MinErsDrainPerSecond, PowertrainModel.ErsDrainPerSecond(0f), 0.0001f);
            Assert.AreEqual(PowertrainModel.MaxErsDrainPerSecond, PowertrainModel.ErsDrainPerSecond(1f), 0.0001f);

            // Out-of-range inputs clamp rather than extrapolate.
            Assert.AreEqual(PowertrainModel.MinErsBoostForce, PowertrainModel.ErsBoostForce(-1f, 1f), 0.0001f);
            Assert.AreEqual(PowertrainModel.MaxErsDrainPerSecond, PowertrainModel.ErsDrainPerSecond(2f), 0.0001f);
        }

        [Test]
        public void TyreCurvesArePhysicallySane()
        {
            // Lateral grip rises from zero slip and stays bounded by peak.
            Assert.AreEqual(0f, TyreModel.LateralGrip(0f, 1f), 0.0001f);
            Assert.Greater(TyreModel.LateralGrip(4f, 1f), TyreModel.LateralGrip(1f, 1f));
            Assert.LessOrEqual(TyreModel.LateralGrip(30f, 1f), 1f);

            // Combined slip: full longitudinal use leaves no lateral.
            Assert.AreEqual(0f, TyreModel.CombinedLateral(1f, 1f), 0.0001f);
            Assert.AreEqual(1f, TyreModel.CombinedLateral(1f, 0f), 0.0001f);

            // Temperature window peaks at optimum and falls off both sides.
            Assert.AreEqual(1f, TyreModel.TemperatureGrip(90f, 90f, 40f), 0.0001f);
            Assert.Greater(TyreModel.TemperatureGrip(90f, 90f, 40f), TyreModel.TemperatureGrip(60f, 90f, 40f));
            Assert.Greater(TyreModel.TemperatureGrip(90f, 90f, 40f), TyreModel.TemperatureGrip(120f, 90f, 40f));
        }

        [Test]
        public void BrakeAndTorqueModels()
        {
            // Brake split respects the bias and conserves the total.
            float front, rear;
            BrakeModel.Split(100f, 0.6f, out front, out rear);
            Assert.AreEqual(60f, front, 0.0001f);
            Assert.AreEqual(40f, rear, 0.0001f);

            // Fade: torque is strongest at optimum temperature.
            Assert.Greater(BrakeModel.TorqueScale(450f), BrakeModel.TorqueScale(50f));
            Assert.GreaterOrEqual(BrakeModel.TorqueScale(2000f), 0.4f);

            // Torque curve is positive everywhere and tapers at the limiter.
            Assert.Greater(PowertrainModel.Torque(0.5f, 100f), PowertrainModel.Torque(1f, 100f));
            Assert.GreaterOrEqual(PowertrainModel.Torque(0f, 100f), 100f * 0.15f - 0.0001f);
        }

        [Test]
        public void DirtyAirLossDecaysWithGap()
        {
            // Maximum loss right on the gearbox of the car ahead, decaying to
            // negligible by a few car-lengths back (consumed live by
            // VehicleController's dirty-air cornering penalty).
            Assert.AreEqual(0.35f, AeroModel.DirtyAirLoss(0f), 0.0001f);
            Assert.Greater(AeroModel.DirtyAirLoss(1f), AeroModel.DirtyAirLoss(3f));
            Assert.Less(AeroModel.DirtyAirLoss(5f), 0.02f);
        }
    }
}
