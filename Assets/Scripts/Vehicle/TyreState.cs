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

        // Live track wetness (0 = bone dry, 1 = fully soaked), ramped by
        // RaceManager.UpdateTrackWetness over ~90s when rain arrives and ~150s
        // as the track dries (per report - "everything goes to shit when the
        // weather goes from dry to wet"). Every weather-driven term in this
        // class blends by it instead of hard-switching on the WeatherState
        // enum, so a weather flip no longer teleports the field from full dry
        // grip to full rain grip between two frames. Static ambient state
        // shared by all cars, same pattern as RegulationWearMultiplier.
        public static float TrackWetness01 = 0f;

        // Lap length of the current circuit, set once at session start by
        // RaceManager (same shared-static pattern as TrackWetness01). The
        // distance-normalized wear model below divides distance travelled by
        // this to convert "metres driven" into "laps of tyre life consumed".
        public static float TrackLengthMeters = 5000f;

        float targetMin;
        float targetMax;
        float baseGrip;
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
            // not a rounding error.
            // CORRECTION, and it matters: this used to claim the AI's cornering
            // target model read the compound-neutral GripConditionMultiplier, so
            // that widening this spread "changes felt handling without re-tuning AI
            // corner pace". That was never true, and it is the reason the spread was
            // allowed to grow until hards were unraceable. Every AI corner target is
            // a fraction of VehicleController.MaxCorneringSpeedKph, which solves
            // against MaxYawRateDegPerSec, which multiplies by the FULL
            // GripMultiplier - baseGrip included. baseGrip therefore sets AI race
            // pace directly and proportionally. Treat any change here as a change to
            // the entire field's lap time.
            // Compound-contrast round 3 (per report - "softs are now way too
            // grippy; make the soft the 1.00 benchmark and rescale the rest"):
            // the whole grip scale is rebased so SOFT = 1.00 is the ceiling
            // (round 2 had pushed it to 1.36, which over-gripped the car) and
            // every other compound sits proportionally below it. The rain-state
            // grips are rescaled by the same factor - they replace baseGrip
            // outright in the wet, so leaving them untouched would have made a
            // wet tyre in heavy rain nearly as fast as a dry soft. The felt
            // compound spread from round 2 (via VehicleController's
            // lateral-force weighting) is preserved by the ratios, not the
            // absolute numbers.
            // The 1.00 / 0.82 / 0.66 dry spread is INTENTIONAL and is not to be
            // compressed (it was briefly cut to 1.00/0.95/0.90 to chase a "the AI
            // have no grip, they're so slow on hards" report, and that was the wrong
            // diagnosis - the spread is the design, the AI's inability to drive to it
            // was the bug). A hard tyre really is meant to give up a third of the
            // car's cornering ability here. What must hold instead is that the AI
            // drives a low-grip car COMPETENTLY: see SteerAuthorityCompensation in
            // AiVehicleController (which used to saturate its own steering the moment
            // grip dropped) and MaxCorneringSpeedKph below.
            // OPERATING WINDOWS: harder compounds work HOTTER. This was inverted -
            // the soft was given the highest window (82-105) and the hard the lowest
            // (74-100), so the hard came up to temperature soonest and easiest. In
            // reality it is the opposite and it is the defining characteristic of the
            // hard tyre: C1/C2 rubber has the highest working range and is notoriously
            // hard to "switch on", which is why drivers complain about no grip on the
            // hards for the first two laps of a stint and on a cold out-lap. Combined
            // with the hard's already-slowest `warmup`, the hard is now genuinely
            // difficult to bring in, and the soft lights up almost immediately.
            if (compound == TyreCompound.Soft)
            {
                baseGrip = 1f;
                targetMin = 85f;
                targetMax = 105f;
                warmup = 1.25f;
                heavyRainGrip = 0.19f;
                lightRainGrip = 0.38f;
                Temperature = 78f;
            }
            else if (compound == TyreCompound.Medium)
            {
                baseGrip = 0.82f;
                targetMin = 90f;
                targetMax = 110f;
                warmup = 1f;
                heavyRainGrip = 0.16f;
                lightRainGrip = 0.34f;
                Temperature = 74f;
            }
            else if (compound == TyreCompound.Hard)
            {
                baseGrip = 0.66f;
                targetMin = 95f;
                targetMax = 115f;
                warmup = 0.78f;
                heavyRainGrip = 0.13f;
                lightRainGrip = 0.29f;
                Temperature = 68f;
            }
            else if (compound == TyreCompound.Intermediate)
            {
                baseGrip = 0.74f;
                // Durability mirrors the Medium at any temperature (per
                // request): the distance-normalized wear model in Tick maps
                // Inter/Wet onto the Medium life curve directly.
                targetMin = 58f;
                targetMax = 82f;
                warmup = 1.05f;
                // Clearly best in light rain, clearly second-best in heavy -
                // the gap to the full wet (and to slicks) widened so the
                // right-tyre-for-the-conditions choice is felt in the wheel.
                heavyRainGrip = 0.57f;
                lightRainGrip = 0.78f;
                Temperature = 58f;
            }
            else
            {
                baseGrip = 0.64f;
                // Durability mirrors the Medium at any temperature (per
                // request): the distance-normalized wear model in Tick maps
                // Inter/Wet onto the Medium life curve directly.
                targetMin = 45f;
                targetMax = 70f;
                warmup = 1.1f;
                // Dominant in heavy rain, visibly clumsy in light rain (where
                // the inter is the right call) - both gaps widened alongside
                // the inter's so the two wet-weather compounds feel distinct.
                heavyRainGrip = 0.79f;
                lightRainGrip = 0.53f;
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
            // Dry (Clear/Cloudy): only the three slick compounds get a flat offset -
            // Intermediate/Wet in the dry are already handled by their much lower
            // baseGrip alone, no additional flat penalty needed there.
            // Dry offsets cut in half (per report - "360-stat car only pulls
            // ~345"): the [PlayerCar] log proved the car data was right, and the
            // remaining gap was exactly this offset - a hard tyre erased -30 kph
            // of top speed, swallowing the entire car-stat advantage the player
            // had earned (the original ask was a 5-10 kph compound difference,
            // and the corner-speed side of a harder compound already comes from
            // its lower grip). Soft stays the reference at 0; medium/hard now
            // cost a real but sane straight-line penalty. Shared by AI targets,
            // so field-wide consistency is preserved.
            float dryOffset = Compound == TyreCompound.Medium ? 7f
                : (Compound == TyreCompound.Hard ? 14f : 0f);

            float heavyOffset;
            float lightOffset;
            if (Compound == TyreCompound.Wet)
            {
                heavyOffset = 0f;
                lightOffset = 10f;
            }
            else if (Compound == TyreCompound.Intermediate)
            {
                heavyOffset = 15f;
                lightOffset = 0f;
            }
            else if (Compound == TyreCompound.Soft)
            {
                heavyOffset = 80f;
                lightOffset = 40f;
            }
            else if (Compound == TyreCompound.Medium)
            {
                heavyOffset = 87.5f;
                lightOffset = 47.5f;
            }
            else
            {
                heavyOffset = 95f;
                lightOffset = 55f;
            }

            // Soak blend (per report - the dry-to-wet flip): the rain offsets
            // fade in with track wetness rather than landing whole the frame
            // the weather enum flips - AI target speeds (which subtract this
            // figure) now fall in step with the grip actually draining away,
            // instead of the whole field either overdriving a soaked track or
            // crawling a still-dry one.
            float rainOffset = weather == WeatherState.HeavyRain ? heavyOffset : lightOffset;
            return Mathf.Lerp(dryOffset, rainOffset, Mathf.Clamp01(TrackWetness01));
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
        /// <summary>
        /// Brings the tyre to TYRE-BLANKET temperature, not to its operating window.
        ///
        /// Blankets are regulated to 70 C, which is well BELOW the ~85-115 C working
        /// range - a car leaves the grid warm but not switched on, and that is
        /// precisely why the formation lap exists and why lap-1 grip is a real
        /// variable. This used to snap the tyre to the exact centre of its window,
        /// which handed every car perfect grip on the opening lap and deleted the
        /// warm-up phase from the race start entirely.
        /// </summary>
        public void WarmToBlanketTemperature()
        {
            Temperature = Mathf.Min(BlanketTemperatureC, (targetMin + targetMax) * 0.5f);
        }

        /// <summary>Regulated tyre-blanket temperature, degrees C.</summary>
        public const float BlanketTemperatureC = 70f;

        /// <summary>
        /// Converts normalised per-lap heat input into degrees above ambient. Tuned
        /// so a normal racing lap settles near 95-100 C - i.e. inside the real
        /// operating windows - rather than the ~75 C the old coefficient produced.
        /// </summary>
        const float TyreHeatCoefficient = 32f;

        /// <summary>
        /// Snaps the tyre to the middle of its operating window. Only appropriate
        /// where there is no out-lap to speak of (time trial).
        /// </summary>
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
            // Ambient carcass baseline follows the TRACK temperature rather than a
            // per-weather literal, so a hot Bahrain afternoon and a cold Spa morning
            // genuinely differ. Rain pulls it down hard.
            float ambient = trackTemperatureC + (weather == WeatherState.HeavyRain ? 10f
                : (weather == WeatherState.LightRain ? 20f : 40f));

            // `warmup` drives the RATE a tyre comes up to temperature, NOT its
            // equilibrium. This is the important distinction: all four tyres on a
            // car take broadly the same energy from the same lap, so the steady-state
            // temperature is compound-independent - what differs is how fast each
            // compound gets there and, crucially, where its WORKING WINDOW sits.
            //
            // It used to multiply the heat GAIN, which meant a harder compound both
            // warmed slower AND settled cooler. Combined with the (now corrected)
            // window ordering that was survivable; with realistic windows it is not.
            // Worked through: the old model's equilibrium spanned only ~71-80 C
            // against windows of 85-115, so EVERY compound sat permanently below its
            // window and ran at a flat grip penalty for the whole race.
            //
            // The coefficient is scaled so a normal racing lap settles near 95-100 C,
            // a slow//cold lap near 80 C and a hard push near 110 C. Against the real
            // windows that gives exactly the right behaviour: the soft is in its
            // window immediately and overheats when pushed, the medium is happy
            // across the range, and the hard only reaches its window when the driver
            // genuinely leans on it - the "I can't get the hards switched on"
            // phenomenon, which is now a real consequence rather than a comment.
            float heatGainRaw = speedHeat + brakeHeat + steerHeat + tractionHeat + slidingHeat;
            float targetTemperature = ambient + heatGainRaw * TyreHeatCoefficient;
            float cooling = speedKph < 75f && throttle < 0.25f ? 1.55f : 1f;
            Temperature = Mathf.MoveTowards(
                Temperature,
                targetTemperature,
                deltaTime * (2.45f + heatGainRaw * 3.1f) * warmup * cooling);

            UpdateLockup(speedKph, brake, steer, weather, tyreManagement, deltaTime);

            float weatherWear = weather == WeatherState.Clear || weather == WeatherState.Cloudy ? 1.08f : 1.32f;
            bool wetWeatherCompound = Compound == TyreCompound.Intermediate || Compound == TyreCompound.Wet;
            if ((weather == WeatherState.Clear || weather == WeatherState.Cloudy) && wetWeatherCompound)
            {
                weatherWear *= Compound == TyreCompound.Wet ? 1.9f : 1.55f;
            }
            else if ((weather == WeatherState.LightRain || weather == WeatherState.HeavyRain) && !wetWeatherCompound)
            {
                weatherWear *= 1.3f;
            }
            else if ((weather == WeatherState.LightRain || weather == WeatherState.HeavyRain) && wetWeatherCompound)
            {
                // Rain-service relief (per report - "inters/wets barely last 2
                // laps where the medium curve promises 4"): the inter/wet were
                // documented as mirroring the MEDIUM's durability, but that only
                // covered baseWear and the temp curve - in actual rain they
                // still paid the full 1.32 rain-wear penalty (vs the dry 1.08 a
                // medium is judged at), AND the low rain grip makes the car
                // slide more, which the wear model charges directly through
                // slipEnergy. Together the correct rain tyre wore ~2x its
                // promised medium curve. A rain tyre is built for rain: no rain
                // penalty, plus relief sized to cancel the extra slip wear -
                // 0.66 is calibrated to the observed 2-laps-vs-4 gap so a
                // correctly-fitted inter/wet genuinely lives the medium curve.
                weatherWear = 0.66f;
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

            // DISTANCE-NORMALIZED wear (round 6, the structural fix). Five
            // rounds of baseWear recalibration all failed the same way: life
            // was fully EMERGENT from driving-intensity inputs (speed, steer,
            // brake, slip, heat), so every physics/AI behaviour change moved
            // real stint length and invalidated the previous calibration -
            // round 5 raised baseWear ~30% in the same commit that stopped
            // the field-wide weaving, the weave turned out to be a bigger
            // wear input than the raise, and tyres came out lasting LONGER
            // (the reported "WOW youve made the tyres last even longer").
            //
            // The anchor is now structural: distance travelled divided by
            // (displayed life x lap length) IS the baseline life consumption,
            // so a stint lasts what the tyre screen says at this track temp
            // BY CONSTRUCTION - the same ExpectedStintLapsAtTemp curve the
            // strategy AI and the UI read. Driving intensity (braking,
            // steering, sliding, overheating) modulates around 1.0 in a
            // bounded band instead of defining the scale: pushing hard costs
            // real life, cruising saves some, and no future handling change
            // can silently double stint length again. tyreManagement and the
            // compound-mismatch weather penalties keep their existing roles.
            int stintCompound = Compound == TyreCompound.Soft ? TyreStrategyRules.Compound.Soft
                : (Compound == TyreCompound.Hard ? TyreStrategyRules.Compound.Hard : TyreStrategyRules.Compound.Medium);
            float lifeLapsAtTemp = Mathf.Max(0.5f, TyreStrategyRules.ExpectedStintLapsAtTemp(stintCompound, trackTemperatureC));
            float lapsPerSecond = (speedKph / 3.6f) / Mathf.Max(500f, TrackLengthMeters);
            float baseLifeFraction = lapsPerSecond / lifeLapsAtTemp;

            // Round 7 (per report - "you clearly havent updated tire wear or
            // you havent updated the recommendation screen"): round 6 anchored
            // the BASELINE to the displayed laps but then multiplied by the
            // legacy tyreManagement factor at its historical +-35% swing
            // (Lerp(1.35, 0.72)) - so a top car (management ~90) still quietly
            // stretched a "3 laps here" soft to ~4+ laps and the screen read
            // as a lie all over again. The display is the CONTRACT now, so
            // every modifier is disciplined around it:
            // - The 1.0 base means flat-out cruising on the straights alone
            //   consumes exactly the displayed life rate. Corners, braking,
            //   sliding and overheating only ever ADD on top, so a driven lap
            //   comes out at ~1.05-1.15x - the tyre dies ON or slightly
            //   BEFORE the displayed lap, never a lap after.
            // - tyreManagement is re-ranged from a hidden +-35% life swing to
            //   an honest +-8% (a stat, not a second wear model); its bigger
            //   gameplay lever remains the lockup/temperature behaviour it
            //   also feeds.
            // Clamp01 made every point of tyreManagement above 100 inert, exactly as
            // Mathf.Lerp's own clamp did for the car stats in VehicleController.
            // Career development builds this stat to 125; extrapolate to the same cap.
            float managementRelief = Mathf.LerpUnclamped(1.08f, 0.92f, Mathf.Clamp(tyreManagement / 100f, 0f, 1.25f));
            // Round 8 (per report - "the recommendations screen is lying about
            // how long the tires last"): the 0.9 intensity floor times the 0.92
            // management relief let a smooth, well-managed car consume as
            // little as ~0.83x the displayed life rate - a "5 laps here" tyre
            // quietly lasted ~6. The display is the CONTRACT: the combined
            // multiplier is now hard-floored at 1.0, so no car can ever burn
            // life SLOWER than the screen says. Pushing (braking, sliding,
            // overheating) still costs extra on top; management/smoothness now
            // only ever claws back that extra, never stretches the baseline.
            // Round 9 (per report - "the recommendations screen pre-race's
            // tire wear and how long a tire lasts is still not accurate", plus
            // the AI 2-stopping "for no reason"): the round-8 coefficients
            // summed to ~1.3-1.5x under genuine race driving (braking zones,
            // loaded steering, slip at racing pace, temps riding at/over
            // target), so a "20 laps here" tyre really died around lap 14 -
            // and the AI's second stops were then MODEL-JUSTIFIED, because the
            // tyre genuinely could not reach the flag at that burn rate. The
            // additive terms are recalibrated so a hard-driven lap lands at
            // ~1.05-1.12x: the displayed number is now what actually happens
            // on track, dying only slightly early when genuinely abused. The
            // >= 1.0 floor (the contract: never LONGER than displayed) stays.
            // Round 11 (per report - "the amount of time a tyre lasts on the
            // pre-race screen is still not right" AND "still so many 2 stops"):
            // rounds 6-10 deliberately set the CRUISE lap to 1.0x and let a
            // driven lap ride at 1.05-1.15x, i.e. the design intent was for a
            // tyre to die 5-15% BEFORE the displayed number. On the short
            // sprints here that 5-15% is the whole margin - a "5-lap" tyre
            // lasting ~4.5 means a second stint can't reach the flag, which
            // both reads as the screen lying AND makes the AI's tyres-gone /
            // short-stint triggers fire a model-justified second stop. The
            // contract is now the honest one the player actually asked for:
            // a NORMAL RACING lap consumes exactly the displayed life (base
            // dropped 1.0 -> 0.92 so the ~0.08 of additive racing load lands on
            // 1.0), genuine abuse (heavy braking, sliding, overheating) still
            // shortens it, and smooth management can stretch it only a hair
            // (floor 0.95, so never more than ~5% past the screen). Displayed
            // life is now what a raced stint actually gets.
            float intensity = Mathf.Max(0.95f, Mathf.Clamp(
                0.92f
                + brake * 0.08f
                + Mathf.Abs(steer) * 0.06f
                + slipEnergy * 0.18f
                + (overheatWear - 1f) * 0.22f
                + (wornHeatWear - 1f) * 0.2f,
                0.86f, 2.0f) * managementRelief);

            float wearLoss = baseLifeFraction * intensity * weatherWear + lockupWearRate;
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
            float managementFactor = Mathf.LerpUnclamped(1.3f, 0.7f, Mathf.Clamp(tyreManagement / 100f, 0f, 1.25f));
            float flatSpotFactor = Mathf.Lerp(1f, 1.35f, Mathf.Clamp01(FlatSpotLevel));
            // Soak blend (per report - the dry-to-wet flip): a slick's extra
            // lockup risk fades in with track wetness instead of arriving whole
            // the frame the weather flips - the same-frame field-wide lockup
            // storm was a big part of the chaos.
            float wetMismatchFactor = 1f;
            bool wetWeatherCompound = Compound == TyreCompound.Intermediate || Compound == TyreCompound.Wet;
            if (!wetWeatherCompound)
            {
                float mismatchReference = weather == WeatherState.HeavyRain ? 2.8f : 1.9f;
                wetMismatchFactor = Mathf.Lerp(1f, mismatchReference, Mathf.Clamp01(TrackWetness01));
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
            // Soak blend (per report): the rain-state grip fades in with track
            // wetness rather than switching the moment the weather enum flips.
            // While the track dries under a dry sky, the light-rain figure is
            // the damp-track reference the remaining wetness blends toward.
            float rainReferenceGrip = weather == WeatherState.HeavyRain ? heavyRainGrip : lightRainGrip;
            float effectiveBaseGrip = Mathf.Lerp(baseGrip, rainReferenceGrip, Mathf.Clamp01(TrackWetness01));

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
