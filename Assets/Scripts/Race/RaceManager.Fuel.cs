using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager fuel subsystem (partial). Distance-scaled start-fuel per session
    /// type (race/qualifying/time trial/practice), the per-lap burn estimate, the
    /// fuel-load choice lap delta and the AI fuel-load choice. Split out of the
    /// RaceManager monolith verbatim - same class, same members, identical fuel
    /// constants, RNG call order and tuned values; VehicleController stays
    /// session-agnostic and burns against whatever it is told here.
    /// </summary>
    public partial class RaceManager
    {
        // ---------- fuel system ----------
        // Distance-scaled fuel replacing the old flat 35kg-every-session load - a
        // 5-lap race used to start with as much fuel as a genuine 60-lap race would
        // need. Each session type gets its own, deliberately simple start-fuel
        // calculation below; VehicleController itself stays session-agnostic (see
        // VehicleController.SetStartFuel) and just burns/reports against whatever
        // it's told here.
        const float MinimumRaceFuelKg = 4.5f;
        const float MaximumRaceFuelKg = 220f;
        // Fuel-strategy contrast pass (per request): the reserve was 1.2 kg
        // (~0.8 laps) on top of a deliberately conservative per-lap estimate,
        // which is why even an underfueled car cruised to the flag with roughly
        // a lap of fuel left - the slack quietly swallowed the entire gamble.
        // A thin 0.4 kg reserve keeps a fumbled last corner from stranding a
        // correctly-fueled car, and no more.
        // Raised 0.4 -> 1.2 (per request - "not enough fuel for all of the
        // fueling types; increase it just a smidge"): the live burn runs above
        // the estimate at a full-throttle duty cycle, so every load choice was
        // arriving at the flag thinner than its label suggested. +0.8 kg
        // (~half a lap) shifts every choice up identically - the underfuel
        // gambles stay gambles, they just aren't rigged anymore.
        const float RaceFuelReserveKg = 1.2f;

        // Widened (was -1.5/-0.7/+0.7/+1.5): an underfuel choice must be a real
        // gamble (aggressive means genuinely managing fuel or running dry) and a
        // heavy load a real insurance premium, not rounding errors around the
        // target. Paired with the absolute-load fuelPenalty in VehicleController
        // so the light car is also VISIBLY faster, not just lighter on paper.
        public static float FuelLoadChoiceLapDelta(FuelLoadChoice choice)
        {
            switch (choice)
            {
                case FuelLoadChoice.AggressiveUnderfuel: return -3f;
                case FuelLoadChoice.LightUnderfuel: return -1.5f;
                case FuelLoadChoice.Safe: return 1.5f;
                case FuelLoadChoice.Heavy: return 3f;
                default: return 0f;
            }
        }

        // Reference track length (this game's own long-standing fallback default
        // elsewhere - TrackManager/EstimateReferenceLapTime) lands right in the
        // middle of the range, at ~1.5kg/lap; genuinely long/short circuits scale
        // a little either side of that. Difficulty nudges it slightly too - a
        // sharper AI field pushes harder and burns a touch more per lap.
        public float EstimateFuelPerLapKg(TrackRuntime track, RaceDifficulty difficulty)
        {
            // Speed-rebalance pass: TrackManager.TargetTrackLength scaled every
            // circuit's target length up ~25% - both the fallback and the
            // short/long InverseLerp band here need to move with it, or every
            // rebalanced track (now mostly 4.9-7km) would sit near/above the old
            // 6200f ceiling and lose the intended short-vs-long fuel variation.
            // Round 2: TrackLengthRebalanceScale stacked another 25% (1.5625x
            // total), so both the fallback and band move with it again.
            //
            // The distance/difficulty band maths live in the engine-free
            // FuelStrategy.PerLapBurnKg (same 1.35-1.65 / 0.97-1.06 constants);
            // this reads the live track length and maps the difficulty tier to the
            // 0-1 factor, then delegates. Passing 0 for a null track lets the
            // rule's own >1 m guard supply the ~7266 m reference, identical to the
            // old inline "track != null && track.length > 1f ? ... : 7266f".
            float trackLength = track != null ? track.length : 0f;
            float difficultyFactor = difficulty == RaceDifficulty.Easy ? 0f : difficulty == RaceDifficulty.Medium ? 0.33f : difficulty == RaceDifficulty.Hard ? 0.66f : 1f;
            return FuelStrategy.PerLapBurnKg(trackLength, difficultyFactor);
        }

        // requiredFuelKg = estimatedFuelPerLapKg * raceLaps + reserveFuelKg, then the
        // chosen fuel-load plan shifts that target by its own lap-count delta
        // (FuelLoadChoiceLapDelta) before the floor/ceiling clamp. raceLaps here
        // should be the real configured lap count (Mathf.Max(3, Settings.Current.laps)),
        // never the RaceLaps property's own 999 stand-in for Practice/Time Trial.
        public float ComputeRaceStartFuelKg(int raceLaps, FuelLoadChoice choice, TrackRuntime track, GameSettingsData settings)
        {
            // GameSettingsData only stores the raw difficultyIndex (0-3) - Difficulty
            // itself is a GameSettingsStore property, not reachable from the plain
            // data object this helper takes - so the same clamp that property
            // applies is repeated here.
            int difficultyIndex = settings == null ? 1 : Mathf.Clamp(settings.difficultyIndex, 0, 3);
            RaceDifficulty difficulty = (RaceDifficulty)difficultyIndex;
            float perLap = EstimateFuelPerLapKg(track, difficulty);
            // Target/choice-shift/clamp maths live in the engine-free
            // FuelStrategy.StartFuelKg; the fuel-load choice is still mapped to its
            // lap delta here (FuelLoadChoiceLapDelta, enum-side) and the tank window
            // consts stay owned by this partial.
            return FuelStrategy.StartFuelKg(perLap, raceLaps, RaceFuelReserveKg,
                FuelLoadChoiceLapDelta(choice), MinimumRaceFuelKg, MaximumRaceFuelKg);
        }

        // AI fuel-starvation fix (per report - "a lot of the AI are going out
        // due to fuel starvation"). Two compounding causes:
        // 1. The live burn (VehicleController.UpdateFuel) is neutral at 0.75
        //    throttle and rises to 1.17x at full throttle - and the AI's own
        //    cornering model keeps it near full throttle for most of a lap, so
        //    AI really burn ~10-15% MORE than the per-lap estimate the Target
        //    load is computed from, against only a 0.4kg reserve.
        // 2. The underfuel lap deltas are ABSOLUTE laps (-1.5/-3), sized as a
        //    player gamble that lift-and-coast can recover - the AI has no
        //    fuel-saving behaviour at all, and on a short race -1.5 laps is a
        //    third of the tank: guaranteed starvation.
        // AI loads therefore carry a 1.12x burn margin on the per-lap estimate,
        // and an AI underfuel choice is capped at 4% of the race distance - the
        // aggressive AI still runs measurably lighter/faster, it just can no
        // longer fuel itself out of the race it cannot manage.
        public float ComputeAiRaceStartFuelKg(int raceLaps, FuelLoadChoice choice, TrackRuntime track, GameSettingsData settings)
        {
            const float AiBurnMargin = 1.12f;
            int difficultyIndex = settings == null ? 1 : Mathf.Clamp(settings.difficultyIndex, 0, 3);
            RaceDifficulty difficulty = (RaceDifficulty)difficultyIndex;
            float perLap = EstimateFuelPerLapKg(track, difficulty) * AiBurnMargin;
            float deltaLaps = FuelLoadChoiceLapDelta(choice);
            if (deltaLaps < 0f)
            {
                deltaLaps = Mathf.Max(deltaLaps, -0.04f * raceLaps);
            }

            return FuelStrategy.StartFuelKg(perLap, raceLaps, RaceFuelReserveKg,
                deltaLaps, MinimumRaceFuelKg, MaximumRaceFuelKg);
        }

        // Enough for an out lap plus a genuine push lap (this game's live qualifying
        // session is a short single/handful-of-attempt format - see
        // QualifyingSessionLapCap - not a long multi-run practice block), with a
        // small margin so a second push attempt after an aborted lap doesn't strand
        // the car mid-lap.
        public float ComputeQualifyingFuelKg(TrackRuntime track)
        {
            float perLap = EstimateFuelPerLapKg(track, RaceDifficulty.Medium);
            return perLap * 2.5f + 1f;
        }

        // Fixed and deliberately low - Time Trial is a pure lap-time comparison
        // tool, not a race, so it should never be distorted by a fuel strategy
        // choice or by fuel burning down over repeated laps.
        public float ComputeTimeTrialFuelKg()
        {
            return 5f;
        }

        // A practice program is a handful of laps at most (see
        // GameBootstrap.StartCareerPractice/EvaluatePracticeSession) - enough fuel
        // for several laps of real running, never a full race load.
        public float ComputePracticeFuelKg(TrackRuntime track)
        {
            float perLap = EstimateFuelPerLapKg(track, RaceDifficulty.Medium);
            return perLap * 6f + 2f;
        }

        // AI fuel-strategy pass: most of the field runs the same Target plan a
        // sensible player would; a genuinely aggressive driver occasionally takes
        // the pace gain of underfuelling, a cautious one occasionally plays it
        // safe. Deliberately conservative odds - "do not make AI fuel-out common"
        // is explicit in the design ask, so AggressiveUnderfuel (the only choice
        // that can plausibly starve a car) is rare and never stacks with an
        // already-aggressive roll.
        public FuelLoadChoice ResolveAiFuelChoice(DriverData driver)
        {
            int aggression = driver == null ? 75 : driver.aggression;
            int consistency = driver == null ? 80 : driver.consistency;
            float roll = Random.value;
            if (aggression >= 90 && roll < 0.10f)
            {
                return FuelLoadChoice.AggressiveUnderfuel;
            }

            if (aggression >= 78 && roll < 0.30f)
            {
                return FuelLoadChoice.LightUnderfuel;
            }

            if (consistency <= 55 && roll < 0.22f)
            {
                return FuelLoadChoice.Safe;
            }

            return FuelLoadChoice.Target;
        }

    }
}
