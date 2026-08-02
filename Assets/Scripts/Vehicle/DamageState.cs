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

        // Pure damage->performance maths live in the engine-free DamagePerformance
        // (same tuned coefficients/floors, now unit-tested); this class keeps owning
        // the impact accumulation and just reads through.
        public float AeroMultiplier
        {
            get { return F1Game.Race.Rules.DamagePerformance.AeroMultiplier(frontWing, floor); }
        }

        public float HandlingMultiplier
        {
            get { return F1Game.Race.Rules.DamagePerformance.HandlingMultiplier(frontWing, floor); }
        }

        public float PowerMultiplier
        {
            get { return F1Game.Race.Rules.DamagePerformance.PowerMultiplier(engineWear, gearboxWear); }
        }

        public float OverallPercent
        {
            get { return F1Game.Race.Rules.DamagePerformance.OverallPercent(frontWing, floor, engineWear, gearboxWear); }
        }

        public bool IsDestroyed
        {
            get { return F1Game.Race.Rules.DamagePerformance.IsDestroyed(OverallPercent); }
        }

        public float AddImpact(float impactSpeedKph, float normalSpeedKph, Vector3 localPoint, DamageImpactType impactType, bool sustainedScrape, float externalScale = 1f)
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

            // Collision damage nerf round 3: another 0.5x on top of round 2's 0.5x -
            // every impact type (car/wall/barrier/solid) now registers about 25% of
            // the damage it did before any of this scaling existed.
            const float CollisionDamageScale = 0.25f;
            // externalScale lets the caller reduce damage intake per car - used
            // to give AI cars a damage resistance so the maxed-aggression
            // contact storm doesn't accumulate enough damage to slow them
            // (see VehicleController.ProcessDamageCollision).
            energy *= CollisionDamageScale * Mathf.Max(0f, externalScale);

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

        /// <summary>
        /// The share of damage a pit stop can actually do something about, as a
        /// 0..100 percentage: bodywork only.
        ///
        /// Repair TIME used to be priced off OverallPercent, which is the mean of
        /// all four components - but RepairPitDamage only touches frontWing and
        /// floor; engineWear and gearboxWear are never repaired. So a car whose
        /// damage was mostly engine/gearbox paid the full 3-7.5s repair hold on
        /// every single stop, came out with essentially the same damage percentage
        /// on the HUD, and was charged again at the next stop - while the radio
        /// announced "we're repairing that damage too - longer stop".
        /// </summary>
        public float RepairablePercent
        {
            get { return Mathf.Clamp01((frontWing + floor) * 0.5f) * 100f; }
        }

        public void RepairPitDamage()
        {
            // Below the threshold no repair time is charged
            // (PitServiceRules.RepairSeconds returns 0), so no repair may be
            // performed either - this used to silently wipe 75% of front-wing and
            // 25% of floor damage for free on any stop under 12%.
            if (RepairablePercent < F1Game.Race.Rules.PitServiceRules.RepairDamageThresholdPercent)
            {
                return;
            }

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
