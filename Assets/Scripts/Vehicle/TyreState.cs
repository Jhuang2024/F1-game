using UnityEngine;
using F1Game.Race.Rules;

namespace LocalFormulaRacing
{
    public class TyreState
    {
        public TyreCompound Compound { get; private set; }
        public float Wear { get; private set; }
        public float Temperature { get; private set; }

        // Real, severity-scaled lockup model (see UpdateLockup): a lockup is a
        // discrete timed event with an intensity, not a single binary flag.
        public float LockupSeverity { get; private set; }
        public float LockupTimer { get; private set; }
        public float LastLockupSeverity { get; private set; }
        public float FlatSpotLevel { get; private set; }
        public int TotalLockups { get; private set; }
        public float RecentLockupWear { get; private set; }

        // Kept as a convenience derived from LockupSeverity so existing callers
        // (VehicleEffects, etc.) that only care about "locked or not" keep working.
        public bool IsLocked { get { return LockupSeverity > 0.05f; } }

        // Part 21: season-regulation hook (CareerManager.GenerateRegulationChanges)
        // for a career-mode "Tyre wear model change" - applied as a flat
        // multiplier on top of the existing wear model rather than touching any
        // of its per-compound tuning, so a season with no such regulation (the
        // default, 1f) drives tyre wear exactly as before, and Quick Race/Time
        // Trial (which never touch career regulation state) are never affected.
        public static float RegulationWearMultiplier = 1f;

        float targetMin;
        float targetMax;
        float baseGrip;
        float baseWear;
        float warmup;
        // Explicit, self-consistent per-compound grip in each rain state, replacing
        // the old single wetPerformance value that got lerped against a shared
        // floor/ceiling and then multiplied by baseGrip - that let a high-baseGrip
        // slick (tuned for dry pace) claw back into the wet-grip ordering even
        // though its wetPerformance was low, which is exactly why Intermediate used
        // to out-pace Wet tyres in heavy rain and Soft used to out-pace Wet tyres in
        // light rain. Each compound's wet-condition grip is now a single number with
        // no dry-grip cross-contamination, so the intended ordering (heavy rain: Wet
        // > Intermediate >> slicks; light rain: Intermediate > Wet >> slicks) always
        // holds regardless of how baseGrip is tuned for the dry.
        float heavyRainGrip;
        float lightRainGrip;
        float lastGripMultiplier = 1f;

        public void Reset(TyreCompound compound)
        {
            Compound = compound;
            Wear = 1f;
            LockupSeverity = 0f;
            LockupTimer = 0f;
            LastLockupSeverity = 0f;
            ResetFlatSpots();

            // Compound-contrast pass (per request - "tires should actually
            // matter"): both the grip ratio (player cornering feel) and the flat
            // CompoundSpeedOffsetKph below are deliberately widened together, and
            // the wear spread is now drastic rather than incremental - a soft is
            // a genuinely fast, genuinely fragile tyre (~2x medium's wear) and a
            // hard is a slow, near-indestructible one (~half of medium's wear),
            // so compound choice and stint timing are real strategic decisions
            // instead of a rounding error.
            // Stint-length calibration (per request - target stint lengths, not
            // just a relative wear ratio): the previous "double every compound"
            // pass overshot badly - a soft didn't even survive a single lap, so
            // cars that started on softs hit a genuine 0%-life puncture (top
            // speed halved) on lap 2 and crawled the rest of the race. baseWear
            // is now calibrated directly to the requested stint lengths: soft
            // ~2 laps, medium ~3, hard ~4, intermediate/wet ~3. The empirical
            // anchor is the reported "soft (baseWear 4.4) lasts ~1 lap", and
            // stint life is very close to inversely proportional to baseWear, so
            // each target-lap figure is 4.4 / targetLaps (the slick compounds);
            // the wet compounds get a small extra allowance because rain adds
            // wear (weatherWear 1.32 vs 1.08 dry) but is largely offset by the
            // lower rain speeds. The 2:1.5:1 soft:medium:hard life ordering is
            // preserved, so compound choice and stint timing stay real strategic
            // decisions - a soft is still the fast, fragile tyre and a hard the
            // slow, durable one - they simply last the intended number of laps
            // now instead of expiring almost immediately.
            // Dry grip spread widened (per request - "softs/mediums/hards feel
            // like the same grip; soft should be incredibly easy to steer,
            // medium less so, hard even less"). baseGrip drives both lateral
            // grip and steering turn-rate (VehicleController), so a wider spread
            // makes the soft feel planted and darty and the hard feel heavy and
            // reluctant - a real, felt handling difference between compounds,
            // not a rounding error. The AI's cornering TARGET-speed model reads
            // GripConditionMultiplier (which neutralises baseGrip) plus the flat
            // CompoundSpeedOffsetKph instead, so this spread changes the felt
            // handling of every compound without re-tuning AI corner pace; the
            // AI's own physics grip still gets its 1.0-1.25 grip assist on top,
            // so a harder tyre's lower base never leaves an AI sliding off.
            if (compound == TyreCompound.Soft)
            {
                baseGrip = 1.28f;
                // ~2 laps (4.4 / 2).
                baseWear = 2.2f;
                targetMin = 82f;
                targetMax = 105f;
                warmup = 1.25f;
                heavyRainGrip = 0.30f;
                lightRainGrip = 0.58f;
                Temperature = 78f;
            }
            else if (compound == TyreCompound.Medium)
            {
                baseGrip = 1f;
                // ~3 laps (4.4 / 3).
                baseWear = 1.47f;
                targetMin = 78f;
                targetMax = 102f;
                warmup = 1f;
                heavyRainGrip = 0.26f;
                lightRainGrip = 0.52f;
                Temperature = 74f;
            }
            else if (compound == TyreCompound.Hard)
            {
                baseGrip = 0.76f;
                // ~4 laps (4.4 / 4) - still clearly the endurance tyre.
                baseWear = 1.1f;
                targetMin = 74f;
                targetMax = 100f;
                warmup = 0.78f;
                heavyRainGrip = 0.22f;
                lightRainGrip = 0.46f;
                Temperature = 68f;
            }
            else if (compound == TyreCompound.Intermediate)
            {
                baseGrip = 0.9f;
                // Mirrors the medium's durability at any temperature (per
                // request) - same baseWear as Medium, and the track-temp wear
                // multiplier in Tick maps Inter/Wet onto the Medium curve.
                baseWear = 1.47f;
                targetMin = 58f;
                targetMax = 82f;
                warmup = 1.05f;
                heavyRainGrip = 0.85f;
                lightRainGrip = 1.02f;
                Temperature = 58f;
            }
            else
            {
                baseGrip = 0.78f;
                // Mirrors the medium's durability at any temperature (per
                // request) - same baseWear as Medium, and the track-temp wear
                // multiplier in Tick maps Inter/Wet onto the Medium curve.
                baseWear = 1.47f;
                targetMin = 45f;
                targetMax = 70f;
                warmup = 1.1f;
                heavyRainGrip = 1.05f;
                lightRainGrip = 0.80f;
                Temperature = 48f;
            }
        }

        // Flat straight-line top-speed penalty (kph) for this compound under the
        // given weather, relative to the fastest compound for those conditions (0).
        // CalculateTargetTopSpeedKph (VehicleController, both player and AI) and
        // EstimateApexSpeedForCornerType's final apex-speed figure (AiVehicleController,
        // AI cornering targets) both subtract this, so a slower compound is
        // consistently that many kph slower everywhere - on a straight or in a
        // corner - rather than a multiplicative grip ratio whose felt kph gap would
        // otherwise vary wildly with corner speed.
        public float CompoundSpeedOffsetKph(WeatherState weather)
        {
            if (weather == WeatherState.HeavyRain)
            {
                if (Compound == TyreCompound.Wet) return 0f;
                if (Compound == TyreCompound.Intermediate) return 15f;
                if (Compound == TyreCompound.Soft) return 80f;
                if (Compound == TyreCompound.Medium) return 87.5f;
                return 95f; // Hard
            }

            if (weather == WeatherState.LightRain)
            {
                if (Compound == TyreCompound.Intermediate) return 0f;
                if (Compound == TyreCompound.Wet) return 10f;
                if (Compound == TyreCompound.Soft) return 40f;
                if (Compound == TyreCompound.Medium) return 47.5f;
                return 55f; // Hard
            }

            // Dry (Clear/Cloudy): only the three slick compounds get a flat offset -
            // Intermediate/Wet in the dry are already handled by their much lower
            // baseGrip alone, no additional flat penalty needed there.
            if (Compound == TyreCompound.Soft) return 0f;
            if (Compound == TyreCompound.Medium) return 15f;
            if (Compound == TyreCompound.Hard) return 30f;
            return 0f;
        }

        // A fresh tyre sheds any accumulated flat-spotting/lockup history from the
        // previous stint. Folded into Reset(), but exposed on its own in case a
        // future caller wants to clear flat spots without a full compound reset.
        // Puncture (per request): a tyre run to 0% remaining life is not just
        // slow, it is destroyed - the carcass lets go. Consumed by
        // VehicleController (top speed halved + HUD reason) and by the AI's
        // destroyed-tyre pit trigger, which fires well before this in normal
        // play; reaching a genuine puncture means someone gambled way past the
        // 80%-worn pit policy.
        public bool Punctured
        {
            get { return Wear <= 0.0001f; }
        }

        public void ResetFlatSpots()
        {
            FlatSpotLevel = 0f;
            TotalLockups = 0;
            RecentLockupWear = 0f;
        }

        // Sets the tyre straight to the centre of its optimal temperature window,
        // as if it came off warmers ready to perform. Used for time trials, where a
        // single flying lap never generates enough heat to reach the window from the
        // cold starting temperature (Soft starts at 78C but its window is 82-105C),
        // so grip felt permanently low. Full grip from the first corner.
        public void WarmToOptimal()
        {
            Temperature = (targetMin + targetMax) * 0.5f;
        }

        public void Tick(float speedKph, float brake, float steer, float throttle, float slipEnergy, WeatherState weather, int tyreManagement, float deltaTime, float trackTemperatureC = TyreStrategyRules.StandardTrackTempC)
        {
            float speedHeat = speedKph / 310f;
            float brakeHeat = brake * brake * Mathf.Lerp(0.72f, 1.45f, Mathf.InverseLerp(80f, 270f, speedKph));
            float steerHeat = Mathf.Abs(steer) * speedHeat * 0.72f;
            float tractionHeat = throttle * slipEnergy * 0.58f;
            float slidingHeat = slipEnergy * speedHeat * 1.05f;
            float ambient = weather == WeatherState.HeavyRain ? 34f : (weather == WeatherState.LightRain ? 44f : 64f);
            float heatGain = (speedHeat + brakeHeat + steerHeat + tractionHeat + slidingHeat) * warmup;
            float targetTemperature = ambient + heatGain * 9.2f;
            float cooling = speedKph < 75f && throttle < 0.25f ? 1.55f : 1f;
            Temperature = Mathf.MoveTowards(Temperature, targetTemperature, deltaTime * (2.45f + heatGain * 3.1f) * cooling);

            UpdateLockup(speedKph, brake, steer, weather, tyreManagement, deltaTime);

            float management = Mathf.Lerp(1.35f, 0.72f, Mathf.Clamp01(tyreManagement / 100f));
            float weatherWear = weather == WeatherState.Clear || weather == WeatherState.Cloudy ? 1.08f : 1.32f;
            if ((weather == WeatherState.Clear || weather == WeatherState.Cloudy) && (Compound == TyreCompound.Intermediate || Compound == TyreCompound.Wet))
            {
                weatherWear *= Compound == TyreCompound.Wet ? 1.9f : 1.55f;
            }
            else if ((weather == WeatherState.LightRain || weather == WeatherState.HeavyRain) && Compound != TyreCompound.Intermediate && Compound != TyreCompound.Wet)
            {
                weatherWear *= 1.3f;
            }

            // Lockups add a little extra wear on top of the baseline model, scaled
            // by how severe the current lockup event is (0 when none active).
            // Consistency fix (per report - "wear can suddenly drop ~8% for no
            // reason"): a locked wheel used to gouge up to ~0.16/s for ~0.6s, i.e.
            // a ~9% instantaneous cliff, which read as random inconsistent
            // degradation (and worsened as a worn tyre locked up more readily).
            // Cut hard so a lockup's real consequence is the FLAT SPOT it leaves
            // (the grip/vibration penalty via FlatSpotLevel, unchanged below), not
            // a sudden chunk of tyre life - degradation now stays smooth.
            float lockupWearRate = LockupSeverity > 0f ? Mathf.Lerp(0.008f, 0.035f, LockupSeverity) : 0f;
            RecentLockupWear += lockupWearRate * deltaTime;

            float overheatWear = Mathf.Lerp(1f, 2.0f, Mathf.InverseLerp(targetMax - 2f, targetMax + 32f, Temperature));
            float wornHeatWear = Mathf.Lerp(1f, 1.3f, Mathf.InverseLerp(0.62f, 0.18f, Wear));
            float slideWear = slipEnergy * 0.0016f;
            float baselineWear = speedHeat * 0.00115f + Mathf.Abs(steer) * 0.0007f + brake * 0.00052f + slideWear;
            baselineWear *= Mathf.Lerp(0.86f, 1.28f, Mathf.InverseLerp(110f, 315f, speedKph));

            // Track-temperature wear gradient (per request - degradation varies
            // with track temp, not a flat per-track rate). The baseWear values
            // above are calibrated to the COOL (15C) stint targets, so the
            // multiplier is exactly 1 there and scales wear UP as the track
            // heats, shrinking stint life toward the hotter targets. It's
            // compound-specific (softs are far more heat-sensitive than hards)
            // and driven off the same ExpectedStintLapsAtTemp curve the AI
            // strategy and pre-race screen read, so life on track matches what
            // they plan for. Inter/Wet mirror the Medium curve.
            int stintCompound = Compound == TyreCompound.Soft ? TyreStrategyRules.Compound.Soft
                : (Compound == TyreCompound.Hard ? TyreStrategyRules.Compound.Hard : TyreStrategyRules.Compound.Medium);
            float lifeAtTemp = TyreStrategyRules.ExpectedStintLapsAtTemp(stintCompound, trackTemperatureC);
            float lifeAtCool = TyreStrategyRules.ExpectedStintLapsAtTemp(stintCompound, TyreStrategyRules.CoolTrackTempC);
            float trackTempWear = lifeAtCool / Mathf.Max(0.1f, lifeAtTemp);

            float wearLoss = (baselineWear * baseWear * management * weatherWear * overheatWear * wornHeatWear * trackTempWear) + lockupWearRate;
            Wear = Mathf.Clamp01(Wear - wearLoss * RegulationWearMultiplier * deltaTime);
        }

        // Severity-scaled lockup model. A lockup is a discrete event: once triggered
        // it runs for LockupTimer seconds at a fixed LockupSeverity (0-1), during
        // which it spikes Temperature, accumulates FlatSpotLevel, and (via
        // RecentLockupWear above) adds extra wear. No new lockup can start while one
        // is already in progress. Probability of triggering scales up with brake
        // input, speed, tyre WEAR (explicitly - a worn tyre locks up far more
        // readily than a fresh one), cold/hot tyres (either end of the temperature
        // window via TemperatureWindowScore), a wet-mismatched compound (slicks in
        // the rain lock up much more easily than wet-weather tyres), steering while
        // braking (trail-braking), and an existing flat spot (a flat-spotted tyre
        // locks up more easily, a small self-reinforcing feedback loop). It scales
        // down with better tyreManagement. ABS/driver-skill/setup effects are not
        // modeled here - they already act upstream by lowering the brake/steer
        // values callers pass in.
        void UpdateLockup(float speedKph, float brake, float steer, WeatherState weather, int tyreManagement, float deltaTime)
        {
            if (LockupTimer > 0f)
            {
                LockupTimer -= deltaTime;
                Temperature += LockupSeverity * deltaTime * 40f;
                FlatSpotLevel = Mathf.Clamp01(FlatSpotLevel + LockupSeverity * deltaTime * 0.4f);
                if (LockupTimer <= 0f)
                {
                    LockupTimer = 0f;
                    LastLockupSeverity = LockupSeverity;
                    LockupSeverity = 0f;
                }

                return;
            }

            if (brake < 0.5f || speedKph < 60f)
            {
                return;
            }

            float brakeFactor = Mathf.InverseLerp(0.5f, 1f, brake);
            float speedFactor = Mathf.InverseLerp(60f, 260f, speedKph);
            float wearNorm = Mathf.Clamp01(1f - Wear);
            float wearFactor = Mathf.Lerp(1f, 2.2f, wearNorm);
            float tempPenalty = Mathf.Clamp01(1f - TemperatureWindowScore);
            float steerFactor = Mathf.Lerp(1f, 1.6f, Mathf.Abs(steer));
            float managementFactor = Mathf.Lerp(1.3f, 0.7f, Mathf.Clamp01(tyreManagement / 100f));
            float flatSpotFactor = Mathf.Lerp(1f, 1.35f, Mathf.Clamp01(FlatSpotLevel));
            float wetMismatchFactor = 1f;
            bool wetWeatherCompound = Compound == TyreCompound.Intermediate || Compound == TyreCompound.Wet;
            if ((weather == WeatherState.LightRain || weather == WeatherState.HeavyRain) && !wetWeatherCompound)
            {
                wetMismatchFactor = weather == WeatherState.HeavyRain ? 2.8f : 1.9f;
            }

            float chance = brakeFactor * speedFactor * Mathf.Lerp(0.6f, 1.4f, tempPenalty) * wearFactor * steerFactor * managementFactor * flatSpotFactor * wetMismatchFactor;
            float probabilityThisTick = deltaTime * chance * 0.85f;
            if (Random.value >= probabilityThisTick)
            {
                return;
            }

            // Severity reflects how far over the edge conditions were, not just a
            // yes/no roll: harder brake at higher speed with worse tyre state and
            // more steering input produces a bigger, longer, more damaging lockup.
            float rawSeverity = brakeFactor * 0.35f + speedFactor * 0.2f + tempPenalty * 0.2f + wearNorm * 0.15f + Mathf.Abs(steer) * 0.1f;
            float severity = Mathf.Lerp(0.15f, 1f, Mathf.Clamp01(rawSeverity));
            LockupSeverity = severity;
            LockupTimer = Mathf.Lerp(0.15f, 0.6f, severity);
            TotalLockups++;
        }

        // trackGripMultiplier is the session-wide "rubbering in" bonus from
        // TrackManager/RaceManager's dynamic track evolution (1 = green track,
        // rising slightly as the session goes on) - optional so every pre-existing
        // caller that only ever cared about tyre-intrinsic grip keeps compiling and
        // behaving exactly as before.
        public float GripMultiplier(WeatherState weather, float trackGripMultiplier = 1f)
        {
            float tempGrip = TemperatureGripMultiplier;
            float wearGrip = Wear > 0.65f ? Mathf.Lerp(0.82f, 1f, (Wear - 0.65f) / 0.35f) :
                             (Wear > 0.35f ? Mathf.Lerp(0.55f, 0.82f, (Wear - 0.35f) / 0.30f) :
                                             Mathf.Lerp(0.12f, 0.55f, Wear / 0.35f));
            // Wet-condition grip fix: heavyRainGrip/lightRainGrip are each compound's
            // own complete, self-consistent grip figure for that rain state - they
            // REPLACE baseGrip for this calculation entirely rather than multiplying
            // on top of it. Multiplying a wet-condition multiplier against baseGrip
            // (the old rainGrip approach) let a high-baseGrip dry-tuned slick claw
            // back into the wet ordering ahead of a genuinely wet-weather compound,
            // which is exactly why Intermediate used to beat Wet in heavy rain and
            // Soft used to beat Wet in light rain.
            float effectiveBaseGrip = baseGrip;
            if (weather == WeatherState.HeavyRain)
            {
                effectiveBaseGrip = heavyRainGrip;
            }
            else if (weather == WeatherState.LightRain)
            {
                effectiveBaseGrip = lightRainGrip;
            }

            float lockupGrip = LockupSeverity > 0f ? Mathf.Lerp(1f, 0.82f, LockupSeverity) : 1f;
            // A flat-spotted tyre vibrates and loses a little contact patch every
            // rotation - a small, persistent handicap, not a game-ruining one.
            float flatSpotGrip = Mathf.Lerp(1f, 0.9f, Mathf.Clamp01(FlatSpotLevel));
            lastGripMultiplier = effectiveBaseGrip * tempGrip * wearGrip * lockupGrip * flatSpotGrip * trackGripMultiplier;
            return lastGripMultiplier;
        }

        // Tyre-difference pass: same formula as GripMultiplier above, but with the
        // compound's own baseline (effectiveBaseGrip - baseGrip in the dry,
        // heavyRainGrip/lightRainGrip in the rain) fixed at a neutral 1x instead of
        // its real compound-specific value. AiVehicleController's cornering-speed
        // model uses this instead of the full GripMultiplier so a compound's speed
        // difference is driven ONLY by TyreState.CompoundSpeedOffsetKph's flat kph
        // figure there - multiplying by the full compound-specific ratio on top of
        // that flat kph subtraction would double-count the same compound gap (and,
        // at genuine 300kph+ corner speeds, the multiplicative ratio alone already
        // works out to far more than the requested "5-10kph slower" by itself).
        // The player's own cornering speed is pure physics (no target-speed model to
        // double-count against), so PlayerVehicleInput/VehicleController still read
        // the full GripMultiplier, compound ratio included, same as before.
        public float GripConditionMultiplier(WeatherState weather, float trackGripMultiplier = 1f)
        {
            float tempGrip = TemperatureGripMultiplier;
            float wearGrip = Wear > 0.65f ? Mathf.Lerp(0.82f, 1f, (Wear - 0.65f) / 0.35f) :
                             (Wear > 0.35f ? Mathf.Lerp(0.55f, 0.82f, (Wear - 0.35f) / 0.30f) :
                                             Mathf.Lerp(0.12f, 0.55f, Wear / 0.35f));
            float lockupGrip = LockupSeverity > 0f ? Mathf.Lerp(1f, 0.82f, LockupSeverity) : 1f;
            float flatSpotGrip = Mathf.Lerp(1f, 0.9f, Mathf.Clamp01(FlatSpotLevel));
            return tempGrip * wearGrip * lockupGrip * flatSpotGrip * trackGripMultiplier;
        }

        public float TemperatureGripMultiplier
        {
            get
            {
                if (Temperature < targetMin)
                {
                    return Mathf.Lerp(0.66f, 1f, Mathf.InverseLerp(35f, targetMin, Temperature));
                }

                if (Temperature > targetMax)
                {
                    if (Temperature <= targetMax + 8f)
                    {
                        return 1f;
                    }

                    return Mathf.Lerp(1f, 0.7f, Mathf.InverseLerp(targetMax + 8f, targetMax + 46f, Temperature));
                }

                float center = (targetMin + targetMax) * 0.5f;
                float halfWindow = Mathf.Max(1f, (targetMax - targetMin) * 0.5f);
                return Mathf.Lerp(1.03f, 1.08f, 1f - Mathf.Clamp01(Mathf.Abs(Temperature - center) / halfWindow));
            }
        }

        public float BrakingMultiplier
        {
            get
            {
                float flatSpotPenalty = Mathf.Lerp(1f, 0.92f, Mathf.Clamp01(FlatSpotLevel));
                return Mathf.Lerp(0.68f, 1.12f, TemperatureWindowScore) * (Wear > 0.5f ? Mathf.Lerp(0.72f, 1f, Wear) : Mathf.Lerp(0.35f, 0.72f, Wear / 0.5f)) * flatSpotPenalty;
            }
        }

        public float TractionMultiplier
        {
            get { return Mathf.Lerp(0.60f, 1.08f, TemperatureWindowScore) * (Wear > 0.5f ? Mathf.Lerp(0.68f, 1f, Wear) : Mathf.Lerp(0.28f, 0.68f, Wear / 0.5f)); }
        }

        public float TemperatureWindowScore
        {
            get
            {
                if (Temperature >= targetMin && Temperature <= targetMax)
                {
                    return 1f;
                }

                if (Temperature < targetMin)
                {
                    return Mathf.InverseLerp(targetMin - 42f, targetMin, Temperature);
                }

                if (Temperature <= targetMax + 8f)
                {
                    return 1f;
                }

                return 1f - Mathf.InverseLerp(targetMax + 8f, targetMax + 50f, Temperature);
            }
        }

        public string TemperatureStatus
        {
            get
            {
                if (Temperature < targetMin - 6f)
                {
                    return "COLD";
                }

                if (Temperature > targetMax + 12f)
                {
                    return "HOT";
                }

                if (Temperature < targetMin || Temperature > targetMax)
                {
                    return "EDGE";
                }

                return "OPT";
            }
        }

        public float LastGripMultiplier
        {
            get { return lastGripMultiplier; }
        }

        public float WearPercent
        {
            get { return (1f - Wear) * 100f; }
        }
    }
}
