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

            float threshold = impactType == DamageImpactType.Car ? 24f : 30f;
            if (sustainedScrape)
            {
                threshold = 42f;
            }

            if (impactSpeedKph < 20f || normalSpeedKph < threshold)
            {
                ClampAll();
                return 0f;
            }

            float objectMultiplier = 1f;
            if (impactType == DamageImpactType.Car)
            {
                objectMultiplier = 0.34f;
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

            float normalized = Mathf.Clamp01((normalSpeedKph - threshold) / (impactType == DamageImpactType.Car ? 120f : 96f));
            float energy = Mathf.Pow(normalized, 1.35f) * objectMultiplier;
            float glancingFactor = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(normalSpeedKph / Mathf.Max(1f, impactSpeedKph)));
            energy *= glancingFactor;

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
