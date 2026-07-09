using UnityEngine;

namespace LocalFormulaRacing
{
    public enum DamageImpactType
    {
        None,
        Car,
        Barrier,
        Wall,
        SolidObject
    }

    public class DamageState
    {
        public float frontWing;
        public float floor;
        public float engineWear;
        public float gearboxWear;

        public float AeroMultiplier
        {
            get { return Mathf.Clamp(1f - frontWing * 0.42f - floor * 0.28f, 0.18f, 1f); }
        }

        public float HandlingMultiplier
        {
            get { return Mathf.Clamp(1f - frontWing * 0.38f - floor * 0.34f, 0.2f, 1f); }
        }

        public float PowerMultiplier
        {
            get { return Mathf.Clamp(1f - engineWear * 0.42f - gearboxWear * 0.22f, 0.24f, 1f); }
        }

        public float OverallPercent
        {
            get { return Mathf.Clamp01((frontWing + floor + engineWear + gearboxWear) * 0.25f) * 100f; }
        }

        public bool IsDestroyed
        {
            get { return OverallPercent >= 98f; }
        }

        public float AddImpact(float impactSpeedKph, float normalSpeedKph, Vector3 localPoint, DamageImpactType impactType, bool sustainedScrape)
        {
            if (impactType == DamageImpactType.None)
            {
                ClampAll();
                return 0f;
            }

            // Wheel-to-wheel tuning: car-to-car contact needs a distinctly
            // harder perpendicular hit before it registers at all - ordinary
            // racing contact (a graze, a brief side-by-side rub) routinely
            // produces 25-40kph of normal-direction closing speed on its own
            // without being anything more than normal racing, and used to
            // land well inside the old 24kph/42kph bars.
            float threshold = impactType == DamageImpactType.Car ? 34f : 30f;
            if (sustainedScrape)
            {
                threshold = impactType == DamageImpactType.Car ? 55f : 42f;
            }

            if (impactSpeedKph < 20f || normalSpeedKph < threshold)
            {
                ClampAll();
                return 0f;
            }

            float objectMultiplier = 1f;
            if (impactType == DamageImpactType.Car)
            {
                // Reduced further (was 0.34) - light rubbing and minor taps
                // should read as small balance loss / barely any damage, not
                // a meaningful chunk of the car in one contact. A genuinely
                // hard side-on hit still adds up over the widened
                // normalization window below.
                objectMultiplier = 0.16f;
            }
            else if (impactType == DamageImpactType.Wall)
            {
                objectMultiplier = 1.18f;
            }
            else if (impactType == DamageImpactType.Barrier)
            {
                objectMultiplier = 1f;
            }
            else
            {
                objectMultiplier = 0.82f;
            }

            if (sustainedScrape)
            {
                objectMultiplier *= 0.22f;
            }

            // Car-to-car's normalization window is widened further (was 120)
            // so the curve stays gentle through the "hard racing contact"
            // range and only climbs steeply for a genuinely severe hit - wall
            // contact keeps its original, tighter window since a wall hit
            // should still ramp up to serious damage quickly.
            float normalized = Mathf.Clamp01((normalSpeedKph - threshold) / (impactType == DamageImpactType.Car ? 170f : 96f));
            float energy = Mathf.Pow(normalized, 1.35f) * objectMultiplier;
            float glancingFactor = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(normalSpeedKph / Mathf.Max(1f, impactSpeedKph)));
            energy *= glancingFactor;

            // Collision damage nerf round 2: cut to half of the original baseline
            // (was 0.8x) - every impact type (car/wall/barrier/solid) now registers
            // about 50% less damage overall than before any of this scaling existed.
            const float CollisionDamageScale = 0.5f;
            energy *= CollisionDamageScale;

            float before = OverallPercent;
            if (localPoint.z > 0.1f)
            {
                frontWing += energy * 0.46f;
            }
            else
            {
                floor += energy * 0.28f;
            }

            floor += energy * (sustainedScrape ? 0.08f : 0.05f);
            engineWear += energy * 0.07f;
            gearboxWear += energy * 0.055f;
            ClampAll();
            return Mathf.Max(0f, OverallPercent - before);
        }

        public void RepairPitDamage()
        {
            frontWing = Mathf.Max(0f, frontWing - 0.75f);
            floor = Mathf.Max(0f, floor - 0.25f);
            ClampAll();
        }

        void ClampAll()
        {
            frontWing = Mathf.Clamp01(frontWing);
            floor = Mathf.Clamp01(floor);
            engineWear = Mathf.Clamp01(engineWear);
            gearboxWear = Mathf.Clamp01(gearboxWear);
        }
    }
}
