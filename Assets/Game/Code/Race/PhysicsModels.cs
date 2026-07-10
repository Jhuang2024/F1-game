using System;

namespace F1Game.Race.Physics
{
    /// <summary>
    /// Engine-free simcade physics models as pure functions over explicit data,
    /// so tyre / aero / brake / powertrain behavior is documented and testable in
    /// one place instead of as constants inside VehicleController. These are the
    /// extraction target for the physics migration; the live VehicleController is
    /// unchanged until it is wired onto these (a later, validated step).
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
    }
}
