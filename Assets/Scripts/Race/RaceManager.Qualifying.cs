using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager qualifying-simulation entry points (partial). The shared
    /// best-of-two attempt orchestration and the tyre/weather penalty, split
    /// out of the monolith verbatim (RNG order and all values unchanged). The
    /// deeper lap-time model (SimulateQualifyingRunDetailed and the field-average
    /// helpers) remains in the main file for now.
    /// </summary>
    public partial class RaceManager
    {
        // Qualifying rework: AI and player used to run two independently-written
        // "best of two laps" implementations with two different, uncalibrated
        // second-run-improvement models (the AI version could hand up to ~0.46s for
        // no reason beyond a coin flip; the player version up to ~0.34s). Both now
        // share this one helper - the second-run gain is a small, explicit,
        // reasoned term (see below) instead of raw per-path randomness, so AI and
        // player results are internally consistent with each other.
        QualifyingLapBreakdown SimulateBestOfTwoQualifyingAttempt(QualifyingSimEntry entry, int phase, TyreCompound? tyreChoice)
        {
            QualifyingLapBreakdown first = SimulateQualifyingRunDetailed(entry, phase, false);
            QualifyingLapBreakdown second = SimulateQualifyingRunDetailed(entry, phase, true);

            // Second-run improvement: a small baseline gain from track evolution,
            // better tyre prep and driver adaptation on a repeated lap - NOT a
            // random half-second lottery. A second run only gains meaningfully more
            // than that when the first lap was genuinely compromised by a mistake
            // (see QualifyingMistakePenalty) - recovering from a bad lap, not luck.
            float secondRunGain = Random.Range(0.03f, 0.10f);
            if (first.mistakePenalty > 0.05f)
            {
                secondRunGain += Mathf.Min(first.mistakePenalty * 0.5f, 0.5f);
            }

            second.variance -= secondRunGain;
            second.finalTime -= secondRunGain;

            if (tyreChoice.HasValue)
            {
                float tyrePenalty = PlayerQualifyingTyreWeatherPenalty(tyreChoice.Value);
                first.tyreChoicePenalty = tyrePenalty;
                first.finalTime += tyrePenalty;
                second.tyreChoicePenalty = tyrePenalty;
                second.finalTime += tyrePenalty;
                // Part of the [QualiSim] diagnostic set: this penalty lands
                // AFTER the per-run logs above, so it was invisible in the
                // console while being fully capable of burying a front-row
                // time (inters in the dry cost +1.7s, wets +3.1s - and the
                // selected compound PERSISTS from previous sessions).
                Debug.Log("[QualiSim] tyre choice " + tyreChoice.Value + " -> penalty " +
                          (tyrePenalty >= 0f ? "+" : "") + tyrePenalty.ToString("0.000") +
                          "s, best run " + Mathf.Min(first.finalTime, second.finalTime).ToString("0.000"));
            }

            return first.finalTime <= second.finalTime ? first : second;
        }

        float SimulateAiQualifyingTime(QualifyingSimEntry entry, int phase)
        {
            return SimulateBestOfTwoQualifyingAttempt(entry, phase, null).finalTime;
        }

        float SimulatePlayerQualifyingTime(QualifyingSimEntry entry, int phase)
        {
            TyreCompound compound = Settings == null ? TyreCompound.Medium : Settings.SelectedTyreCompound;
            QualifyingLapBreakdown best = SimulateBestOfTwoQualifyingAttempt(entry, phase, compound);
            best.finalTime = Mathf.Max(20f, best.finalTime);
            if (phase >= 1 && phase <= 3)
            {
                playerSimBreakdowns[phase - 1] = best;
            }

            return best.finalTime;
        }

        float PlayerQualifyingTyreWeatherPenalty(TyreCompound compound)
        {
            // Live weather read here; the weather x compound penalty table is the
            // engine-free QualifyingModel.TyreWeatherPenalty (WeatherState and
            // TyreCompound orderings match its codes).
            WeatherState weather = Track == null ? WeatherState.Clear : Track.weather;
            return QualifyingModel.TyreWeatherPenalty((int)weather, (int)compound);
        }

        // ---------- Qualifying model rework ----------
        // Full rework: the old model derived the ENTIRE base lap from a single
        // car's own top speed (treating top speed as if it were the car's average
        // speed around the whole circuit), so a few km/h of difference between two
        // cars swung the WHOLE lap by whole seconds. Car performance was then also
        // applied a second time via a separate, much smaller carEffect term using
        // different stats - two inconsistent mechanisms for the same thing. Every
        // driver now starts from the exact same neutral circuit reference lap
        // (CircuitReferenceLapTime, track-only, no car parameter at all); the ONE
        // place car performance enters the model is the composite, track-weighted
        // carEffect term below.
        // Driver-separation pass (per report - the grid was almost pure car, so a
        // top driver in a midfield car qualified midfield and, in a short race,
        // finished there): the driver coefficients are raised so qualifying ability
        // and pace carry real weight, and the car's maximum swing is trimmed a
        // little (2.0 -> 1.7s) so an elite driver can drag a merely-decent car up
        // the order rather than being locked to the machinery. Elite-vs-weak driver
        // delta is now roughly 0.6-0.9s instead of ~0.3s.
        const float DriverQualifyingCoefficient = 0.024f;
        const float DriverPaceCoefficient = 0.007f;
        const float DriverConfidenceCoefficient = 0.002f;
        const float CarEffectCoefficientPerPoint = 0.08f;
        const float CarEffectCapSeconds = 1.7f;

        QualifyingLapBreakdown SimulateQualifyingRunDetailed(QualifyingSimEntry entry, int phase, bool secondRun)
        {
            QualifyingLapBreakdown breakdown = new QualifyingLapBreakdown { phase = phase };
            DriverData driver = entry.driverData;
            CarPerformanceData car = entry.carData;

            // Neutral circuit reference lap: identical for every driver in the
            // field this session - car/driver/tyre/weather/mistake effects are
            // layered on top of this SAME starting point below, never baked into
            // it.
            breakdown.baseLap = CircuitReferenceLapTime(Track);

            float consistency = driver == null ? 80f : driver.consistency;
            float qualifying = driver == null ? 82f : driver.qualifying;
            float pace = driver == null ? 82f : driver.pace;
            float confidence = driver == null ? 80f : driver.experience;
            float tyreManagement = driver == null ? 80f : driver.tyreManagement;

            // Driver effect: qualifying ability is the PRIMARY term; pace and
            // confidence (experience) are smaller secondary flavors, per the
            // calibration targets (elite vs good ~0.05-0.25s, elite vs weak
            // ~0.25-0.55s in comparable machinery). Centered on the FIELD's own
            // average of each stat, not a fixed baseline number, so the whole grid
            // isn't shifted just because a given season's roster skews stronger or
            // weaker than some hardcoded expectation.
            float avgQualifying;
            float avgPace;
            float avgConfidence;
            FieldAverageDriverStats(out avgQualifying, out avgPace, out avgConfidence);
            // The stat-gap-to-time-delta mapping (field-centered, coefficient-weighted)
            // is the engine-free QualifyingModel.DriverEffect; the live driver/field
            // reads and the tuned coefficients stay owned here.
            breakdown.driverEffect = QualifyingModel.DriverEffect(
                qualifying, pace, confidence, avgQualifying, avgPace, avgConfidence,
                DriverQualifyingCoefficient, DriverPaceCoefficient, DriverConfidenceCoefficient);

            // Car effect: ONE composite, track-weighted rating (see
            // CarQualifyingPerformanceRating/CarPerformanceWeights) covering every
            // relevant car stat, including top speed - now normalized onto the
            // same 45-125 scale every other stat already uses, instead of driving
            // the entire base lap on its own. Centered on the field's own average
            // composite rating (same reasoning as driverEffect above) and clamped
            // so an extreme outlier car can never dominate the result by itself.
            // This is the ONLY place car performance affects the result - it is
            // never applied a second time anywhere else in this model.
            float carRating = CarQualifyingPerformanceRating(car, Track);
            float fieldAverageCarRating = FieldAverageCarRating(Track);
            // Composite-rating gap -> clamped time delta is the engine-free
            // QualifyingModel.CarEffect; the live car/field ratings and the tuned
            // coefficient/cap stay owned here.
            breakdown.carEffect = QualifyingModel.CarEffect(carRating, fieldAverageCarRating, CarEffectCoefficientPerPoint, CarEffectCapSeconds);

            // Percentage of baseLap rather than a flat constant, so difficulty stays
            // meaningful regardless of track length: Easy is clearly the slowest,
            // Expert clearly the fastest/most aggressive, Medium close to neutral.
            //
            // Quali/race coherence fix: this is now an AI-ONLY term, and Easy/
            // Medium are pushed meaningfully slower. It used to be added to every
            // entry alike - including the player's own simulated lap - so it never
            // separated the player from the AI at all, and it was an order of
            // magnitude smaller than the difficulty handicap the same AI actually
            // races with (the -92/-55/-15/-5 kph straight-line discount in
            // AiVehicleController): on Easy the AI qualified at near-competitive
            // formula times and then raced 20%+ slower, so the player started
            // P15 and drove through the whole field in a lap or two, and grid
            // position predicted nothing. The AI quali handicap now points the
            // same direction, at a magnitude that keeps the grid a rough preview
            // of race pace while staying comfortably faster than the same AI's
            // race laps (so pole laps still beat race fastest laps).
            // Difficulty round 4: re-anchored to the much faster race pace so
            // the grid keeps predicting the race (Easy 8->5%, Medium 1.5->0.5%,
            // Hard -3->-4%, Expert -6->-7%).
            // Difficulty round 5 (per report - "my qualifying sims keep putting
            // me P22"): the -4%/-7% AI bonus was anchored to an AI race
            // handicap model (-92/-55/-15/-5 kph) that no longer exists - the
            // AI's actual race advantage today is a +10/+13 kph straight-line
            // bonus and a modest grip assist, roughly 1-2% of lap time. At
            // -4%/-7% (2.8-4.9s on a typical lap) the ENTIRE AI field
            // out-qualified the player by seconds regardless of car or driver
            // (car effect caps at 1.7s, driver at ~0.5s), so the player was
            // pinned to P22 on Hard/Expert no matter what they drove.
            // Re-anchored to the CURRENT AI advantage so an upgraded car and a
            // good driver genuinely move the player up the grid.
            // Round 6 (per report - P22 again on a healthy driver/car): with the
            // difficulty rework, AI machinery is IDENTICAL across difficulties
            // (no kph bonus, no grip assist, no straight-line staircase) and
            // difficulty differentiates racecraft - which does not apply to a
            // solo qualifying lap. Even the previous -2% Expert bonus (-1.6s on
            // a typical lap) buried the player's CAPPED car advantage (1.7s
            // max) under the entire AI field: a backmarker with -1.6s of free
            // difficulty time still out-qualified a 110-rated car. Hard/Expert
            // now get NO artificial qualifying bonus, matching the race model;
            // Easy/Medium keep their positive (slower) offsets, mirroring their
            // genuinely slower race behaviour profiles.
            float difficultyPercent = Settings.Difficulty == RaceDifficulty.Easy ? 0.050f : Settings.Difficulty == RaceDifficulty.Medium ? 0.005f : 0f;
            breakdown.difficultyEffect = entry.isPlayer ? 0f : breakdown.baseLap * difficultyPercent;

            // Track evolution: small and gradual, shared identically by every
            // driver in the same session phase - a later session can be marginally
            // faster (more rubber down) without artificially exaggerating the
            // P1-P20 spread the way large fixed per-phase bonuses (previously
            // Q1 +0.08 / Q2 -0.18 / Q3 -0.36, a 0.44s swing across sessions on its
            // own) used to.
            breakdown.phaseEffect = phase == 1 ? 0.02f : (phase == 2 ? -0.02f : -0.05f);

            breakdown.tyrePrep = Mathf.Lerp(0.14f, 0.0f, tyreManagement / 100f) + Random.Range(0f, 0.04f);
            breakdown.weatherPenalty = WeatherQualifyingPenalty(driver);
            breakdown.mistakePenalty = QualifyingMistakePenalty(driver, phase, out breakdown.mistakeType);

            // Normal clean-lap variance only, narrowed to the calibration target
            // (was up to 0.03s at worst, now a proper 0.03-0.12s band scaled by
            // consistency). The extra second-run-specific noise this used to add
            // here is gone - second-run improvement is now its own explicit,
            // reasoned term (see SimulateBestOfTwoQualifyingAttempt).
            float variance = Mathf.Lerp(0.12f, 0.03f, consistency / 100f);
            breakdown.variance = Random.Range(-variance, variance);

            breakdown.finalTime = breakdown.baseLap + breakdown.driverEffect + breakdown.carEffect +
                                  breakdown.difficultyEffect + breakdown.phaseEffect + breakdown.tyrePrep +
                                  breakdown.weatherPenalty + breakdown.mistakePenalty + breakdown.variance;
            // P19/P22-diagnosis log: the whole per-term breakdown for the
            // player's OWN simulated lap, every phase, unconditionally to the
            // console (Debug.Log, not GameLog.Info - GameLog.Info is silently
            // dropped unless verbose/F3 logging is on, which is exactly why an
            // earlier version of this same diagnostic never actually reached
            // the console on a real play session). Placed here rather than at
            // field-build time so avgQualifying/fieldAverageCarRating are the
            // REAL field averages (qualifyingEntries is empty earlier in
            // BuildSimulatedQualifyingField, before the AI grid is added) - a
            // "qualifying at the back despite good stats" report can now be
            // pinned to a specific term (driver stats vs. field average, car
            // rating vs. field average, a harsh mistake penalty, unlucky
            // variance) instead of guessing.
            if (entry.isPlayer)
            {
                Debug.Log("[QualiSim] phase " + phase + " driver(qualifying=" + qualifying + " pace=" + pace +
                          " consistency=" + consistency + " experience=" + confidence + ") vs field avg(qualifying=" +
                          avgQualifying.ToString("0.0") + " pace=" + avgPace.ToString("0.0") + " confidence=" +
                          avgConfidence.ToString("0.0") + ") -> driverEffect=" + breakdown.driverEffect.ToString("0.000") +
                          " || car=" + (car == null ? "null" : car.id) + " carRating=" + carRating.ToString("0.0") +
                          " vs fieldAvgCarRating=" + fieldAverageCarRating.ToString("0.0") + " -> carEffect=" +
                          breakdown.carEffect.ToString("0.000") + " || baseLap=" + breakdown.baseLap.ToString("0.000") +
                          " difficultyEffect=" + breakdown.difficultyEffect.ToString("0.000") + " phaseEffect=" +
                          breakdown.phaseEffect.ToString("0.000") + " tyrePrep=" + breakdown.tyrePrep.ToString("0.000") +
                          " weatherPenalty=" + breakdown.weatherPenalty.ToString("0.000") + " mistakePenalty=" +
                          breakdown.mistakePenalty.ToString("0.000") + " (" + breakdown.mistakeType + ") variance=" +
                          breakdown.variance.ToString("0.000") + " -> finalTime=" + breakdown.finalTime.ToString("0.000"));
            }

            return breakdown;
        }

        // Neutral circuit reference lap: track length and character only, no car
        // parameter at all - every driver's simulated lap begins from this exact
        // same number. The "neutral expected average speed" is the CURRENT
        // field's own average top speed (a genuine km/h value, unlike every other
        // car stat which is a 0-125ish rating) rather than any one competitor's -
        // shared by the whole field, so it can never by itself favor one driver
        // over another the way the old per-car version did.
        float CircuitReferenceLapTime(TrackRuntime track)
        {
            // Field-average top speed + circuit style factor are the live reads;
            // the length/speed lap-time formula is the engine-free
            // QualifyingModel.ReferenceLapTime.
            float neutralTopSpeedKph = FieldAverageTopSpeedKph();
            float styleFactor = TrackAverageSpeedFactor(track);
            float trackLength = track == null ? 7266f : track.length;
            return QualifyingModel.ReferenceLapTime(neutralTopSpeedKph, styleFactor, trackLength);
        }

        float FieldAverageTopSpeedKph()
        {
            if (qualifyingEntries.Count == 0)
            {
                return 337f;
            }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < qualifyingEntries.Count; i++)
            {
                CarPerformanceData car = qualifyingEntries[i].carData;
                sum += car == null || car.topSpeed <= 0 ? 337f : car.topSpeed;
                count++;
            }

            return count > 0 ? sum / count : 337f;
        }

        void FieldAverageDriverStats(out float avgQualifying, out float avgPace, out float avgConfidence)
        {
            avgQualifying = 82f;
            avgPace = 82f;
            avgConfidence = 80f;
            if (qualifyingEntries.Count == 0)
            {
                return;
            }

            float sumQualifying = 0f;
            float sumPace = 0f;
            float sumConfidence = 0f;
            int count = 0;
            for (int i = 0; i < qualifyingEntries.Count; i++)
            {
                DriverData d = qualifyingEntries[i].driverData;
                sumQualifying += d == null ? 82f : d.qualifying;
                sumPace += d == null ? 82f : d.pace;
                sumConfidence += d == null ? 80f : d.experience;
                count++;
            }

            if (count > 0)
            {
                avgQualifying = sumQualifying / count;
                avgPace = sumPace / count;
                avgConfidence = sumConfidence / count;
            }
        }

        float FieldAverageCarRating(TrackRuntime track)
        {
            if (qualifyingEntries.Count == 0)
            {
                return 84f;
            }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < qualifyingEntries.Count; i++)
            {
                sum += CarQualifyingPerformanceRating(qualifyingEntries[i].carData, track);
                count++;
            }

            return count > 0 ? sum / count : 84f;
        }

        // Composite qualifying car-performance rating: every relevant car stat,
        // weighted by what actually matters on THIS circuit (see
        // CarPerformanceWeights) - a power-sensitive circuit weights top
        // speed/acceleration/engine power heavily, a high-downforce circuit
        // weights aero/cornering, a technical circuit weights cornering/braking/
        // chassis, and a balanced circuit spreads the weight evenly. topSpeed is
        // stored as a literal km/h value (roughly 310-362), not a 0-125 rating
        // like every other stat - normalized onto that same scale first
        // (NormalizeTopSpeedToRating) so it contributes proportionally instead of
        // dominating or being dwarfed by unit mismatch.
        float CarQualifyingPerformanceRating(CarPerformanceData car, TrackRuntime track)
        {
            if (car == null)
            {
                return 84f;
            }

            float wTopSpeed;
            float wAcceleration;
            float wCornering;
            float wBraking;
            float wAero;
            float wChassis;
            float wEngine;
            CarPerformanceWeights(track, out wTopSpeed, out wAcceleration, out wCornering, out wBraking, out wAero, out wChassis, out wEngine);

            float topSpeedRating = NormalizeTopSpeedToRating(car.topSpeed);
            return topSpeedRating * wTopSpeed + car.acceleration * wAcceleration + car.cornering * wCornering +
                   car.braking * wBraking + car.aeroEfficiency * wAero + car.chassisBalance * wChassis +
                   car.enginePower * wEngine;
        }

        // Same 45-125 scale every other car stat uses (CareerManager's own clamp
        // range for cornering/braking/aero/etc.) so topSpeed can be weighted-
        // averaged alongside them without a unit mismatch - this is exactly what
        // let a few km/h of difference swing the entire old lap-time formula by
        // whole seconds (topSpeed used to be a literal speed divisor for the
        // WHOLE lap, see CircuitReferenceLapTime's own history above).
        float NormalizeTopSpeedToRating(float topSpeedKph)
        {
            return QualifyingModel.TopSpeedRating(topSpeedKph);
        }

        // Per-circuit car-stat weighting (must each sum to 1.0). Named-circuit
        // checks mirror TrackAverageSpeedFactor's own bucketing style.
        void CarPerformanceWeights(TrackRuntime track, out float wTopSpeed, out float wAcceleration, out float wCornering, out float wBraking, out float wAero, out float wChassis, out float wEngine)
        {
            // Per-circuit stat weighting lives in the engine-free
            // QualifyingModel.CarPerformanceWeights. A null track maps to empty
            // descriptors and a wide road half-width (999) so the tight-circuit test
            // is false, exactly matching the old inline "track != null && ..."
            // short-circuit.
            QualifyingModel.CarPerformanceWeights(
                track == null ? "" : track.trackId,
                track == null ? "" : track.styleName,
                track == null ? 999f : track.roadHalfWidth,
                out wTopSpeed, out wAcceleration, out wCornering, out wBraking, out wAero, out wChassis, out wEngine);
        }

        // Fraction of top speed a well-driven qualifying lap averages, by track
        // character. Tight/low-speed circuits (Monaco, street layouts) average much
        // lower than top speed; flowing high-speed circuits average much closer to
        // it. Named-circuit checks run before the generic street check since several
        // real high-speed circuits (Jeddah, Baku, Las Vegas) are technically street
        // layouts but should not be bucketed with tight street pace.
        //
        // Qualifying-vs-race calibration fix round 2: this is the ONLY place AI
        // qualifying times come from - RecordQualifyingPhase falls back to
        // SimulateAiQualifyingTime for every AI entry even in the live/driven
        // qualifying session, so AI never actually banks a real physics-driven lap
        // time for qualifying purposes, live or simulated. Round 1's ~1.2x bump
        // still undershot actual race pace: AiVehicleController's corner-speed
        // model now keeps HighSpeed/Medium corners near 97-100% of straight-line
        // pace and Slow (tight) corners at a flat ~300-310kph (itself often
        // 90%+ of a car's real top speed) - only the narrow VeryTight/Hairpin
        // bands meaningfully cut into a lap's average anymore, so a track's real
        // achieved average speed fraction is far closer to its top speed than
        // these factors assumed even after round 1. Pushed further here so the
        // simulated qualifying baseline is genuinely faster than what the
        // buffed race physics produce, matching a real low-fuel flying lap.
        //
        // Round 3: the race-fastest-lap-beats-qualifying bug came back after this
        // session's slipstream buff (SlipstreamTopSpeedBonusKph/slipstreamBoost in
        // VehicleController doubled) - a genuine tow gives race laps a real speed
        // source this formula never modeled (no drafting in a simulated/solo
        // flying lap), so real race pace on drafting-heavy circuits pulled back
        // ahead of the quali baseline even with the earlier AI straight-line-speed
        // nerfs. Every factor nudged up again to buy back that headroom.
        float TrackAverageSpeedFactor(TrackRuntime track)
        {
            // Null-track baseline stays here (returned before any descriptor read);
            // the circuit-character lookup is the engine-free QualifyingModel.
            if (track == null)
            {
                return 0.83f;
            }

            return QualifyingModel.TrackSpeedFactor(track.trackId, track.styleName, track.roadHalfWidth);
        }

        // All drivers under the same conditions share the same weather baseline
        // (Track.weather, clear/cloudy penalty) - wetSkill only creates a
        // controlled difference AROUND that shared baseline, narrowed here so wet
        // conditions can't stack into an extreme, barely-explainable gap on their
        // own (was 1.18-0.42, roughly a 2s spread between best/worst wet driver in
        // heavy rain).
        float WeatherQualifyingPenalty(DriverData driver)
        {
            // Live weather state + the driver's wet skill read here; the shared
            // per-condition baseline and the wetSkill spread are the engine-free
            // QualifyingModel.WeatherPenalty (WeatherState ordering matches its codes).
            float wetSkill = driver == null ? 80f : driver.wetSkill;
            return QualifyingModel.WeatherPenalty((int)Track.weather, wetSkill);
        }

        // Mistakes are modeled explicitly and kept separate from ordinary
        // clean-lap variance (see SimulateQualifyingRunDetailed's own variance
        // term) - minor mistakes cost roughly 0.1-0.4s, a rare major mistake adds
        // substantially more on top. mistakeType names what happened so it's
        // visible in the qualifying breakdown instead of hidden inside a single
        // opaque number.
        float QualifyingMistakePenalty(DriverData driver, int phase, out string mistakeType)
        {
            mistakeType = "";
            float consistency = driver == null ? 80f : driver.consistency;
            float awareness = driver == null ? 80f : driver.awareness;
            // Chance build-up (consistency base rate + rain + Q3 nudge) is the
            // engine-free QualifyingModel.MistakeChance; every Random roll below -
            // the trigger, the type pick and the magnitude - stays here so the RNG
            // call order is unchanged.
            float chance = QualifyingModel.MistakeChance(consistency, (int)Track.weather, phase);
            if (Random.value > chance)
            {
                return 0f;
            }

            string[] minorMistakeTypes = { "small lock-up", "poor corner exit", "traffic", "track limits" };
            mistakeType = minorMistakeTypes[Random.Range(0, minorMistakeTypes.Length)];
            float penalty = Random.Range(0.1f, 0.35f) * Mathf.Lerp(1.25f, 0.75f, awareness / 100f);
            // Grid-sanity fix (per report - a nonsensical order with weak
            // drivers up front): the old major-mistake tail (10% chance,
            // +0.8-2.0s) could drop a genuine front-runner two seconds down the
            // grid, and in a short race there's no time to recover - so the
            // result read as random. The tail is now both rarer and much
            // smaller, so car+driver quality, not luck, sets the grid.
            if (Random.value < 0.04f)
            {
                mistakeType = "major mistake";
                penalty += Random.Range(0.4f, 1.0f);
            }

            return penalty;
        }

        float InvalidQualifyingTime(int phase)
        {
            return QualifyingModel.InvalidTime(phase);
        }

        float GetQualifyingPhaseTime(QualifyingSimEntry entry, int phase)
        {
            return phase == 1 ? entry.q1 : (phase == 2 ? entry.q2 : entry.q3);
        }

        void SetQualifyingPhaseTime(QualifyingSimEntry entry, int phase, float time)
        {
            if (phase == 1)
            {
                entry.q1 = time;
            }
            else if (phase == 2)
            {
                entry.q2 = time;
            }
            else
            {
                entry.q3 = time;
            }

            entry.session = "Q" + phase;
            entry.finalTime = time;
        }

        void SetAiQualifyingPhaseTime(QualifyingSimEntry entry, int phase, float time)
        {
            SetQualifyingPhaseTime(entry, phase, time);
            float s1;
            float s2;
            float s3;
            SimulateQualifyingSectors(entry, phase, time, out s1, out s2, out s3);
            SetQualifyingPhaseSectors(entry, phase, s1, s2, s3);
            if (State != null && entry.participant != null)
            {
                State.OnSectorComplete(entry.participant, 1, s1, false);
                State.OnSectorComplete(entry.participant, 2, s2, false);
                State.OnSectorComplete(entry.participant, 3, s3, false);
            }
        }

        void SetSimulatedPlayerQualifyingPhaseTime(QualifyingSimEntry entry, int phase, float time)
        {
            SetQualifyingPhaseTime(entry, phase, time);
            entry.invalidated = false;
            float s1;
            float s2;
            float s3;
            SimulateQualifyingSectors(entry, phase, time, out s1, out s2, out s3);
            int phaseIndex = Mathf.Clamp(phase, 1, 3) - 1;
            playerQualifyingBestTimes[phaseIndex] = time;
            playerQualifyingBestSectors[phaseIndex, 0] = s1;
            playerQualifyingBestSectors[phaseIndex, 1] = s2;
            playerQualifyingBestSectors[phaseIndex, 2] = s3;
            SetQualifyingPhaseSectors(entry, phase, s1, s2, s3);
            if (State != null && entry.participant != null)
            {
                State.OnSectorComplete(entry.participant, 1, s1, false);
                State.OnSectorComplete(entry.participant, 2, s2, false);
                State.OnSectorComplete(entry.participant, 3, s3, false);
            }
        }

        void SetPlayerQualifyingSectors(QualifyingSimEntry entry, int phase, float lapTime, bool invalidated)
        {
            LapTracker lap = PlayerParticipant == null ? null : PlayerParticipant.lapTracker;
            int phaseIndex = Mathf.Clamp(phase, 1, 3) - 1;
            float s1 = invalidated ? 0f : playerQualifyingBestSectors[phaseIndex, 0];
            float s2 = invalidated ? 0f : playerQualifyingBestSectors[phaseIndex, 1];
            float s3 = invalidated ? 0f : playerQualifyingBestSectors[phaseIndex, 2];
            if (s1 <= 0f || s2 <= 0f || s3 <= 0f)
            {
                s1 = lap == null ? 0f : lap.LastSector1Time;
                s2 = lap == null ? 0f : lap.LastSector2Time;
                s3 = lap == null ? 0f : lap.LastSector3Time;
            }
            if (s1 <= 0f || s2 <= 0f || s3 <= 0f)
            {
                s1 = lapTime * 0.333f;
                s2 = lapTime * 0.334f;
                s3 = Mathf.Max(0.001f, lapTime - s1 - s2);
            }

            SetQualifyingPhaseSectors(entry, phase, s1, s2, s3);
            if (!invalidated && State != null && entry.participant != null)
            {
                State.OnSectorComplete(entry.participant, 1, s1, false);
                State.OnSectorComplete(entry.participant, 2, s2, false);
                State.OnSectorComplete(entry.participant, 3, s3, false);
            }
        }

        void SimulateQualifyingSectors(QualifyingSimEntry entry, int phase, float lapTime, out float s1, out float s2, out float s3)
        {
            float consistency = entry.driverData == null ? 80f : entry.driverData.consistency;
            float spread = Mathf.Lerp(0.028f, 0.008f, consistency / 100f);
            float w1 = 0.334f + Random.Range(-spread, spread);
            float w2 = 0.332f + Random.Range(-spread, spread);
            float w3 = Mathf.Max(0.25f, 1f - w1 - w2);
            float total = w1 + w2 + w3;
            s1 = lapTime * w1 / total;
            s2 = lapTime * w2 / total;
            s3 = Mathf.Max(0.001f, lapTime - s1 - s2);
        }

        void SetQualifyingPhaseSectors(QualifyingSimEntry entry, int phase, float s1, float s2, float s3)
        {
            if (phase == 1)
            {
                entry.q1s1 = s1;
                entry.q1s2 = s2;
                entry.q1s3 = s3;
            }
            else if (phase == 2)
            {
                entry.q2s1 = s1;
                entry.q2s2 = s2;
                entry.q2s3 = s3;
            }
            else
            {
                entry.q3s1 = s1;
                entry.q3s2 = s2;
                entry.q3s3 = s3;
            }
        }
    }
}
