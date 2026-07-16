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
            Assert.AreEqual(0.034155f, PowertrainModel.MinErsDrainPerSecond, 0.000001f);
            Assert.AreEqual(0.049005f, PowertrainModel.MaxErsDrainPerSecond, 0.000001f);

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
        public void TyreModelSecondaryCurves()
        {
            // Longitudinal grip is zero at no slip and rises with slip.
            Assert.AreEqual(0f, TyreModel.LongitudinalGrip(0f, 1f), 0.0001f);
            Assert.Greater(TyreModel.LongitudinalGrip(0.1f, 1f), 0f);

            // Load sensitivity: unity at the reference load, falls under more load.
            Assert.AreEqual(1f, TyreModel.LoadSensitivity(100f, 100f), 0.0001f);
            Assert.Less(TyreModel.LoadSensitivity(200f, 100f), 1f);

            // Wear rises with aggression and softer compounds.
            Assert.AreEqual(0.02f, TyreModel.WearPerLap(0f, 0f), 0.0001f);
            Assert.AreEqual(0.08f, TyreModel.WearPerLap(1f, 1f), 0.0001f);
        }

        [Test]
        public void AeroModelDownforceDragAndSlipstream()
        {
            // Downforce ∝ speed²; quadruples when speed doubles; zero at rest.
            Assert.AreEqual(0.22f, AeroModel.Downforce(10f, 0.0022f), 0.0001f);
            Assert.AreEqual(0.88f, AeroModel.Downforce(20f, 0.0022f), 0.0001f);
            Assert.AreEqual(0f, AeroModel.Downforce(0f, 0.0022f), 0.0001f);

            // DRS open cuts drag; slipstream shaves a smaller amount, clamped.
            Assert.Less(AeroModel.Drag(10f, 0.001f, true, 0.25f), AeroModel.Drag(10f, 0.001f, false));
            Assert.AreEqual(1f, AeroModel.SlipstreamDragFactor(0f), 0.0001f);
            Assert.AreEqual(1f - AeroModel.SlipstreamDragReduction, AeroModel.SlipstreamDragFactor(2f), 0.0001f); // clamps
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

        [Test]
        public void SlipstreamTowStrengthPeaksCloseAndOnLine()
        {
            // Live RaceManager tuning: max 150 m, peak 18 m, full lateral 3.5 m,
            // fade to none by 7.5 m.
            const float maxD = 150f, peakD = 18f, fullLat = 3.5f, maxLat = 7.5f;

            // Peak distance, dead on line -> full tow; at the max distance -> none.
            Assert.AreEqual(1f, AeroModel.SlipstreamTowStrength(18f, 0f, maxD, peakD, fullLat, maxLat), 0.0001f);
            Assert.AreEqual(0f, AeroModel.SlipstreamTowStrength(150f, 0f, maxD, peakD, fullLat, maxLat), 0.0001f);
            // Halfway through the distance band (84 m ~ the 0.5 point of 18..150).
            Assert.AreEqual(0.5f, AeroModel.SlipstreamTowStrength(84f, 0f, maxD, peakD, fullLat, maxLat), 0.0001f);

            // Lateral: full within 3.5 m, half at 5.5 m, none by 7.5 m.
            Assert.AreEqual(1f, AeroModel.SlipstreamTowStrength(18f, 3.5f, maxD, peakD, fullLat, maxLat), 0.0001f);
            Assert.AreEqual(0.5f, AeroModel.SlipstreamTowStrength(18f, 5.5f, maxD, peakD, fullLat, maxLat), 0.0001f);
            Assert.AreEqual(0f, AeroModel.SlipstreamTowStrength(18f, 7.5f, maxD, peakD, fullLat, maxLat), 0.0001f);

            // Closer is stronger at equal offset.
            Assert.Greater(AeroModel.SlipstreamTowStrength(30f, 0f, maxD, peakD, fullLat, maxLat),
                           AeroModel.SlipstreamTowStrength(100f, 0f, maxD, peakD, fullLat, maxLat));
        }

        [Test]
        public void SlipstreamStraightFactorFadesThroughBends()
        {
            // Full tow at/below 9 deg of heading change over the sampled span,
            // none at/above 22 deg, linear between.
            Assert.AreEqual(1f, AeroModel.SlipstreamStraightFactor(9f, 9f, 22f), 0.0001f);
            Assert.AreEqual(1f, AeroModel.SlipstreamStraightFactor(5f, 9f, 22f), 0.0001f);   // below full -> clamps to 1
            Assert.AreEqual(0f, AeroModel.SlipstreamStraightFactor(22f, 9f, 22f), 0.0001f);
            Assert.AreEqual(0f, AeroModel.SlipstreamStraightFactor(30f, 9f, 22f), 0.0001f);  // above none -> clamps to 0
            Assert.AreEqual(0.5f, AeroModel.SlipstreamStraightFactor(15.5f, 9f, 22f), 0.0001f);
        }
    }
}
