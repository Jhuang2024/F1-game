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
        // Rear wing and suspension were missing entirely: a rear-end hit was filed
        // under "floor", and suspension - the damage that most often actually ends a
        // real grand prix, and the one a pit stop cannot fix - had nowhere to go at
        // all. See DamagePerformance for what each one costs.
        public float rearWing;
        public float floor;
        public float suspension;
        public float engineWear;
        public float gearboxWear;

        // Pure damage->performance maths live in the engine-free DamagePerformance
        // (same tuned coefficients/floors, now unit-tested); this class keeps owning
        // the impact accumulation and just reads through.
        public float AeroMultiplier
        {
            get { return F1Game.Race.Rules.DamagePerformance.AeroMultiplier(frontWing, rearWing, floor); }
        }

        public float HandlingMultiplier
        {
            get { return F1Game.Race.Rules.DamagePerformance.HandlingMultiplier(frontWing, rearWing, floor, suspension); }
        }

        public float PowerMultiplier
        {
            get { return F1Game.Race.Rules.DamagePerformance.PowerMultiplier(engineWear, gearboxWear); }
        }

        public float OverallPercent
        {
            get { return F1Game.Race.Rules.DamagePerformance.OverallPercent(frontWing, rearWing, floor, suspension, engineWear, gearboxWear); }
        }

        public bool IsDestroyed
        {
            get { return F1Game.Race.Rules.DamagePerformance.IsDestroyed(OverallPercent); }
        }

        // Normalized impact severity below which nothing at all reaches the
        // suspension. See the accumulation below.
        const float SuspensionImpactThreshold = 0.35f;

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
            // Where the car was hit decides what broke. A rear-end impact used to be
            // credited to the floor, so being rear-ended cost cornering grip and no
            // straight-line speed - the wrong way round.
            if (localPoint.z > 0.1f)
            {
                frontWing += energy * 0.46f;
            }
            else if (localPoint.z < -0.1f)
            {
                rearWing += energy * 0.42f;
            }
            else
            {
                floor += energy * 0.28f;
            }

            floor += energy * (sustainedScrape ? 0.08f : 0.05f);
            engineWear += energy * 0.07f;
            gearboxWear += energy * 0.055f;

            // Suspension takes load from a genuinely SHARP, HARD impact - a kerb
            // strike, a wheel over a wheel, a square hit on a wall. It is explicitly
            // not loaded by scraping along a barrier or by routine wheel-to-wheel
            // rubbing, both of which load bodywork instead: a real suspension failure
            // is one big hit, not the sum of twenty small ones. The ramp starts well
            // up the impact curve for exactly that reason - below it, contact costs
            // bodywork and nothing else.
            if (!sustainedScrape && normalized > SuspensionImpactThreshold)
            {
                float suspensionShare = impactType == DamageImpactType.Car ? 0.30f : 0.20f;
                suspension += energy * suspensionShare *
                    Mathf.InverseLerp(SuspensionImpactThreshold, 1f, normalized);
            }

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
            get { return Mathf.Clamp01((frontWing + rearWing + floor) / 3f) * 100f; }
        }

        /// <summary>
        /// Whether race control must show this car the black-and-orange flag: a
        /// mechanical problem or hanging bodywork that has to be put right before it
        /// may continue. See RaceManager.UpdateMechanicalFlags.
        /// </summary>
        public bool RequiresMechanicalFlag
        {
            get { return F1Game.Race.Rules.DamagePerformance.RequiresMechanicalBlackOrange(frontWing, rearWing); }
        }

        /// <summary>Broken suspension - not something a pit stop fixes.</summary>
        public bool SuspensionIsTerminal
        {
            get { return F1Game.Race.Rules.DamagePerformance.SuspensionIsTerminal(suspension); }
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

            // A nose change is quick and near-total; a rear wing change is slower and
            // rarer, so it is repaired less completely. Suspension is deliberately NOT
            // repaired - a broken wishbone is a retirement, not a pit stop, which is
            // what makes it worth modelling separately at all.
            frontWing = Mathf.Max(0f, frontWing - 0.75f);
            rearWing = Mathf.Max(0f, rearWing - 0.55f);
            floor = Mathf.Max(0f, floor - 0.25f);
            ClampAll();
        }

        void ClampAll()
        {
            frontWing = Mathf.Clamp01(frontWing);
            rearWing = Mathf.Clamp01(rearWing);
            floor = Mathf.Clamp01(floor);
            suspension = Mathf.Clamp01(suspension);
            engineWear = Mathf.Clamp01(engineWear);
            gearboxWear = Mathf.Clamp01(gearboxWear);
        }
    }
}
