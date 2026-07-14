using System;

namespace F1Game.Race.Physics
{
    /// <summary>
    /// Engine-free simcade physics models as pure functions over explicit data,
    /// so tyre / aero / brake / powertrain behavior is documented and testable in
    /// one place instead of as constants inside VehicleController. The live
    /// VehicleController consumes the aero (drag/downforce/slipstream) and ERS
    /// (boost/drain) authorities directly; the slip-curve, brake and torque
    /// functions remain the extraction target for the deeper physics migration.
    /// </summary>
    public static class TyreModel
    {
        /// <summary>Peak grip as a function of slip angle (deg) — Pacejka-like curve.</summary>
        public static float LateralGrip(float slipAngleDeg, float peak, float shape = 1.6f, float stiffness = 0.28f)
        {
            // Simplified magic-formula: F = peak * sin(shape * atan(stiffness * slip)).
            return peak * (float)Math.Sin(shape * Math.Atan(stiffness * slipAngleDeg));
        }

        /// <summary>Longitudinal grip as a function of slip ratio (-1..1).</summary>
        public static float LongitudinalGrip(float slipRatio, float peak, float shape = 1.5f, float stiffness = 12f)
        {
            return peak * (float)Math.Sin(shape * Math.Atan(stiffness * slipRatio));
        }

        /// <summary>Combined-slip grip circle: available lateral shrinks as longitudinal is used.</summary>
        public static float CombinedLateral(float lateral, float longitudinalUsed01)
        {
            float u = Clamp01(longitudinalUsed01);
            return lateral * (float)Math.Sqrt(Math.Max(0f, 1f - u * u));
        }

        /// <summary>Grip scalar from temperature window (0..1, 1 at optimum).</summary>
        public static float TemperatureGrip(float tempC, float optimumC, float windowC)
        {
            float d = (tempC - optimumC) / Math.Max(1f, windowC);
            return Clamp01(1f - d * d);
        }

        /// <summary>Load sensitivity: grip coefficient falls slightly as vertical load rises.</summary>
        public static float LoadSensitivity(float loadN, float referenceN)
        {
            float ratio = loadN / Math.Max(1f, referenceN);
            return 1f / (float)Math.Pow(Math.Max(0.01f, ratio), 0.15f);
        }

        /// <summary>Per-lap wear increment (0..1) scaled by aggression and compound softness.</summary>
        public static float WearPerLap(float aggression01, float compoundSoftness01, float baseWear = 0.02f)
        {
            return baseWear * (1f + aggression01) * (1f + compoundSoftness01);
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    public static class AeroModel
    {
        // Live tuning authority (consumed by VehicleController.ApplyForces).
        // These are the shipped arcade-model coefficients; the car's stats,
        // gear and damage scale them at the boundary.
        public const float DrsClosedDragCoefficient = 0.00054f;
        public const float DrsOpenDragCoefficient = 0.00025f;
        public const float DrsDragReductionFraction = 1f - DrsOpenDragCoefficient / DrsClosedDragCoefficient;
        public const float DownforceCoefficient = 0.0022f;

        // Slipstream tow: deliberately much smaller than DRS's own drag cut -
        // DRS is the big reduction from physically opening the rear wing,
        // slipstream is only the benefit of running in another car's wake.
        public const float SlipstreamDragReduction = 0.08f;

        public static float SlipstreamDragFactor(float strength01)
        {
            float s = strength01 < 0f ? 0f : (strength01 > 1f ? 1f : strength01);
            return 1f - SlipstreamDragReduction * s;
        }

        /// <summary>Downforce (N) ∝ speed² × coefficient; ride-height and damage scale it.</summary>
        public static float Downforce(float speedMs, float coefficient, float rideHeightScale = 1f, float damageScale = 1f)
        {
            return speedMs * speedMs * coefficient * rideHeightScale * damageScale;
        }

        /// <summary>Drag (N) ∝ speed² × coefficient; DRS reduces the coefficient.</summary>
        public static float Drag(float speedMs, float coefficient, bool drsOpen, float drsDragReduction = 0.25f)
        {
            float c = drsOpen ? coefficient * (1f - drsDragReduction) : coefficient;
            return speedMs * speedMs * c;
        }

        /// <summary>Dirty-air downforce loss when following (gap in car lengths).</summary>
        public static float DirtyAirLoss(float gapCarLengths)
        {
            if (gapCarLengths <= 0f)
            {
                return 0.35f;
            }

            // Loss decays with gap; negligible beyond ~3 car lengths.
            return 0.35f * (float)Math.Exp(-gapCarLengths / 1.5f);
        }

        /// <summary>
        /// The follower's tow strength (0-1) from the gap behind and the lateral
        /// offset, extracted verbatim from RaceManager.ComputeSlipstreamStrength:
        /// peak tow at <paramref name="peakDistance"/>, fading to zero by
        /// <paramref name="maxDistance"/>, and full-width within
        /// <paramref name="fullLateralWidth"/> fading to zero by
        /// <paramref name="maxLateralWidth"/>. The distance/lateral cutoffs and every
        /// track read stay in the caller; this is only the strength product it
        /// applies once a pair is already in range.
        /// </summary>
        public static float SlipstreamTowStrength(float aheadDistance, float lateralDiff,
            float maxDistance, float peakDistance, float fullLateralWidth, float maxLateralWidth)
        {
            float distanceStrength = InverseLerp(maxDistance, peakDistance, aheadDistance);
            float lateralStrength = InverseLerp(maxLateralWidth, fullLateralWidth, lateralDiff);
            return Clamp01(distanceStrength * lateralStrength);
        }

        /// <summary>
        /// The straight-section scale (0-1) applied to the tow, extracted verbatim
        /// from RaceManager.SlipstreamStraightSectionStrength: full tow at/below
        /// <paramref name="fullAtDeg"/> of heading change across the sampled span,
        /// fading to none at/above <paramref name="noneAtDeg"/>. The track sampling
        /// that produces the angle stays in the caller.
        /// </summary>
        public static float SlipstreamStraightFactor(float headingAngleDeg, float fullAtDeg, float noneAtDeg)
        {
            return Clamp01(InverseLerp(noneAtDeg, fullAtDeg, headingAngleDeg));
        }

        // Engine-free equivalents of the UnityEngine.Mathf helpers the inline
        // slipstream maths used, so the extracted curves stay byte-identical
        // (Mathf.InverseLerp clamps and returns 0 when the endpoints coincide).
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        static float InverseLerp(float a, float b, float v) => a == b ? 0f : Clamp01((v - a) / (b - a));
    }

    public static class BrakeModel
    {
        /// <summary>Effective brake torque scaled by temperature (fade when too hot/cold).</summary>
        public static float TorqueScale(float tempC, float optimumC = 450f, float windowC = 250f)
        {
            float d = (tempC - optimumC) / Math.Max(1f, windowC);
            return Math.Max(0.4f, 1f - 0.6f * d * d);
        }

        /// <summary>Front/rear split of a total braking force given a 0..1 bias (1 = all front).</summary>
        public static void Split(float total, float frontBias01, out float front, out float rear)
        {
            float b = frontBias01 < 0f ? 0f : (frontBias01 > 1f ? 1f : frontBias01);
            front = total * b;
            rear = total * (1f - b);
        }
    }

    public static class PowertrainModel
    {
        /// <summary>Engine torque (Nm) from a normalized RPM position on a simple curve.</summary>
        public static float Torque(float rpm01, float peakTorque)
        {
            // Rises to a plateau then tapers toward the limiter.
            float r = rpm01 < 0f ? 0f : (rpm01 > 1f ? 1f : rpm01);
            float curve = (float)Math.Sin(r * Math.PI * 0.85f);
            return peakTorque * Math.Max(0.15f, curve);
        }

        /// <summary>Mass added by fuel (kg) → lap-time sensitivity is handled by the caller.</summary>
        public static float FuelMass(float litres, float densityKgPerL = 0.75f)
        {
            return litres * densityKgPerL;
        }

        /// <summary>ERS deployment power (kW) clamped by battery state and mode cap.</summary>
        public static float ErsDeployPower(float battery01, float modeCapKw)
        {
            return battery01 <= 0f ? 0f : modeCapKw * Math.Min(1f, battery01 * 2f);
        }

        // ---- Live ERS tuning (consumed by VehicleController) --------------------
        // Boost force range across car ERS efficiency, tuned for a 15-20 km/h
        // felt gain on a typical straight; drain range across throttle position
        // (see the long drain-rate history in VehicleController for how these
        // numbers were arrived at).
        public const float MinErsBoostForce = 19f;
        public const float MaxErsBoostForce = 30f;
        // Battery-cycle rebalance round 1: raised ~45% (was 0.0805-0.1140) to
        // stop the battery pinning at 100% once the harvest side was cut down
        // to match (see VehicleController's braking-harvest history). That
        // landed too far the other way - a full deploy drained in ~6-8s, far
        // faster than the tank actually feels like it should empty.
        // Round 2 (per request): cut 75% off round 1's rate. Harvest is
        // untouched, so the battery still doesn't pin at 100% (round 1's fix
        // for that stands), but a full-throttle deploy now takes ~24-32s to
        // empty a full battery instead of ~6-8s.
        // Round 3 (per request): raised 50% off round 2's rate - a full
        // deploy now takes ~16-21s to empty a full battery.
        // Round 4 (per request): raised a further 20%; full-throttle deployment
        // now drains a full battery in roughly 13.5s instead of 16.2s.
        // Round 5 (per request): raised a further 20% (was 0.05175-0.07425);
        // full-throttle deployment now drains a full battery in roughly 11.2s.
        public const float MinErsDrainPerSecond = 0.0621f;
        public const float MaxErsDrainPerSecond = 0.0891f;

        /// <summary>Deploy force for a car's ERS efficiency (0-1) in the current deploy mode.</summary>
        public static float ErsBoostForce(float ersEfficiency01, float deployModeMultiplier)
        {
            float e = ersEfficiency01 < 0f ? 0f : (ersEfficiency01 > 1f ? 1f : ersEfficiency01);
            return (MinErsBoostForce + (MaxErsBoostForce - MinErsBoostForce) * e) * deployModeMultiplier;
        }

        /// <summary>Battery drain per second while deploying, rising with throttle.</summary>
        public static float ErsDrainPerSecond(float throttle01)
        {
            float t = throttle01 < 0f ? 0f : (throttle01 > 1f ? 1f : throttle01);
            return MinErsDrainPerSecond + (MaxErsDrainPerSecond - MinErsDrainPerSecond) * t;
        }
    }
}
