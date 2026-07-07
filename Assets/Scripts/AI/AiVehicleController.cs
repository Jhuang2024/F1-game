using UnityEngine;

namespace LocalFormulaRacing
{
    [RequireComponent(typeof(VehicleController))]
    public class AiVehicleController : MonoBehaviour
    {
        public RaceManager raceManager;
        public RaceParticipant participant;

        VehicleController vehicle;
        TrackRuntime track;
        float mistakeTimer;
        float mistakeSteer;
        float aggressionOffset;
        float damageDecisionTimer;
        float lastProgressDistance;
        bool hasProgressReference;

        // Deterministic side preference so two cars meeting nose-to-tail never
        // both dive the same way; assigned from grid slot at spawn.
        float preferredSide = 1f;

        // Opening-lap fan-out: a per-car lane the AI holds for the first seconds
        // so the pack spreads across the full road instead of forming a train.
        float openingFanOffset;
        const float OpeningFanDuration = 7f;

        // Continuous small line wobble, difficulty-scaled; the seed keeps every
        // car's wobble and apex-miss noise out of phase with the others.
        float noiseSeed;

        // Curvature derivative tracking: comparing this frame's severity against
        // last frame's tells entry (rising), apex (steady/peak) and exit (falling)
        // apart from a single forward-looking sample, without needing lap history.
        float previousSeverityHere;

        // Corner-exit throttle hesitation: a short, skill-scaled beat of reduced
        // commitment right after the car unwinds out of a corner.
        bool corneringActive;
        float throttleDelayTimer;
        float currentThrottle;

        // Race-start confidence, derived once at spawn from difficulty + driver
        // skill. Pure input timing/ramp, never an engine or grip boost.
        float launchConfidence = 1f;
        float launchSettleDuration;

        // Traffic dodge-side memory so a car sitting near local.x==0 ahead of us
        // doesn't make the avoidance steer flicker frame to frame.
        float dodgeMemorySide;
        float dodgeMemoryTimer;

        // DRS commit-once-per-zone so a lower drsUsageQuality AI misses the wing
        // activation as a whole zone decision, not a mid-zone flicker.
        bool drsCommittedThisZone;
        bool wasDrsLegalLastFrame;

        enum OvertakeState { Following, PreparingAttack, AttackingInside, AttackingOutside, SideBySide, CompletingPass, BackingOut }
        OvertakeState overtakeState = OvertakeState.Following;
        float overtakeStateTimer;
        float attackSide = 1f;

        // How long this car has been sitting in Following without forcing an attack -
        // a stuck Expert eventually raises its own attack-attempt probability instead
        // of orbiting the same 0.8-1.2s gap for the rest of the stint.
        float followingTimer;

        // Temporary post-safety-car-restart commitment boost: a lap or two of extra
        // eagerness to attack once the field is unleashed again.
        float postRestartCommitmentBoostTimer;
        bool wasRestartLastFrame;

        // Driver-pressure model (Part 8): 0-1, set by UpdateOvertakeState each frame
        // based on whether this car is actively attacking/defending under close
        // pressure. Consumed once in Update() to feed a slightly more aggressive
        // brake/steer input into this car's own command - which the tyre lockup
        // model then naturally reads as higher lockup risk, with no changes needed
        // to TyreState.cs itself.
        float pressureFactor;

        // Defend cover is capped to one commitment per approaching braking zone
        // so a defending AI covers the line once instead of weaving repeatedly.
        bool hasCoveredThisApex;

        // Corner-type classification (Part 2): a single continuous severity number
        // hides the fact that a flowing high-speed corner and a genuine hairpin need
        // very different confidence curves, not the same eased Lerp toward one floor.
        enum CornerType { HighSpeed, Medium, Slow, Hairpin }

        CornerType ClassifyUpcomingCorner(float apexSeverity)
        {
            if (apexSeverity < 0.25f)
            {
                return CornerType.HighSpeed;
            }

            if (apexSeverity < 0.5f)
            {
                return CornerType.Medium;
            }

            if (apexSeverity < 0.75f)
            {
                return CornerType.Slow;
            }

            return CornerType.Hairpin;
        }

        // Per-tier apex speed curve instead of one flat Pow(severity, 1.4) eased
        // toward the same hairpin floor for every corner. High-speed and medium
        // corners get their own, much higher, floor so a confident (Hard/Expert)
        // driver carries speed close to trueApexSpeed's upper end through a flowing
        // bend instead of being dragged toward hairpin pace the moment severity
        // crosses one broad threshold. apexConfidence (already difficulty+driver
        // derived) blends the floor upward for a sharper driver on the same corner.
        float EstimateApexSpeedForCornerType(CornerType type, float apexSeverity, float straightTargetSpeed, float hairpinSpeedKph, float gripMultiplier, float apexConfidence)
        {
            float floorSpeed;
            float easePower;
            switch (type)
            {
                case CornerType.HighSpeed:
                    floorSpeed = Mathf.Lerp(straightTargetSpeed * 0.84f, straightTargetSpeed * 0.94f, apexConfidence);
                    easePower = 2.6f;
                    break;
                case CornerType.Medium:
                    floorSpeed = Mathf.Lerp(straightTargetSpeed * 0.58f, straightTargetSpeed * 0.72f, apexConfidence);
                    easePower = 1.8f;
                    break;
                case CornerType.Slow:
                    floorSpeed = Mathf.Lerp(hairpinSpeedKph * 1.15f, hairpinSpeedKph * 1.4f, apexConfidence);
                    easePower = 1.4f;
                    break;
                default:
                    floorSpeed = hairpinSpeedKph;
                    easePower = 1.2f;
                    break;
            }

            float eased = Mathf.Pow(Mathf.Clamp01(apexSeverity), easePower);
            return Mathf.Lerp(straightTargetSpeed, floorSpeed, eased) * gripMultiplier;
        }

        public void Initialize(RaceManager manager, RaceParticipant raceParticipant, TrackRuntime raceTrack)
        {
            raceManager = manager;
            participant = raceParticipant;
            track = raceTrack;
            vehicle = GetComponent<VehicleController>();
            noiseSeed = Random.Range(0f, 4096f);
            mistakeTimer = Random.Range(3f, 8f);
            hasProgressReference = false;

            int gridSlot = participant != null ? Mathf.Max(0, participant.gridPosition - 1) : 0;
            preferredSide = gridSlot % 2 == 0 ? -1f : 1f;

            // Spread the field over four lanes at the start; the road is wide
            // enough now for genuine side-by-side into turn one.
            float laneSpread = Mathf.Min(3.4f, raceTrack.roadHalfWidth * 0.24f);
            openingFanOffset = ((gridSlot % 4) - 1.5f) * laneSpread;

            RaceManager.AiDifficultyProfile startupProfile = manager.GetAiDifficultyProfile();
            DriverData startupDriver = participant == null ? null : participant.driverData;
            float launchSkill = startupDriver == null ? 0.5f : Mathf.Clamp01((startupDriver.awareness + startupDriver.consistency) / 200f);
            launchConfidence = Mathf.Clamp01(Mathf.Lerp(0.55f, 0.95f, launchSkill) - startupProfile.reactionTimeSeconds * 0.12f);
            launchSettleDuration = Mathf.Lerp(0.3f, 1.3f, 1f - launchSkill) + startupProfile.reactionTimeSeconds * 0.35f;
        }

        void Update()
        {
            if (vehicle == null || track == null || raceManager == null || raceManager.IsPaused || raceManager.IsRaceFinished)
            {
                return;
            }

            if (participant != null && participant.retired)
            {
                vehicle.SetCommand(new VehicleCommand { brake = 1f });
                return;
            }

            if (!raceManager.CanDrive)
            {
                vehicle.SetCommand(new VehicleCommand { brake = 1f });
                return;
            }

            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.RaceElapsed < participant.startReactionDelay)
            {
                vehicle.SetCommand(new VehicleCommand { brake = 1f });
                return;
            }

            // Continuity-aware progress lookup so the AI never snaps to the wrong part of
            // the track near the start/finish wrap or where sections run close together.
            TrackProgress progress = hasProgressReference
                ? track.GetProgressNear(transform.position, lastProgressDistance)
                : track.GetProgress(transform.position);
            lastProgressDistance = progress.distance;
            hasProgressReference = true;
            float speedKph = Mathf.Abs(vehicle.CurrentSpeedKph);
            DriverData driver = participant == null ? null : participant.driverData;
            int pace = driver == null ? 80 : (raceManager.CurrentSession == RaceWeekendSession.Qualifying ? driver.qualifying : driver.pace);
            int racecraft = driver == null ? 80 : driver.racecraft;
            int consistency = driver == null ? 80 : driver.consistency;
            int aggression = driver == null ? 75 : driver.aggression;
            int tyreManagement = driver == null ? 80 : driver.tyreManagement;
            int defending = driver == null ? 78 : driver.defending;
            int overtaking = driver == null ? 78 : driver.overtaking;
            int experience = driver == null ? 75 : driver.experience;
            int wetSkill = driver == null ? 75 : driver.wetSkill;

            RaceManager.AiDifficultyProfile profile = raceManager.GetAiDifficultyProfile();

            float severityHere = EstimateCornerSeverity(progress.distance);
            // Look further ahead with speed, but shorten in corners so the AI hits apexes
            // instead of cutting across them.
            float lookAhead = Mathf.Lerp(22f, 62f, Mathf.Clamp01(speedKph / 350f)) * Mathf.Lerp(1.12f, 0.62f, severityHere);
            Vector3 targetPoint;
            Vector3 forward;
            Vector3 right;
            track.SampleAtDistance(progress.distance + lookAhead, out targetPoint, out forward, out right);

            float apexDistanceAhead;
            float apexSeverity;
            FindUpcomingApex(progress.distance, out apexDistanceAhead, out apexSeverity);
            float turnSign = EstimateTurnDirection(progress.distance);

            // Real ceiling, not an invented ~330-350kph clamp: the same DRS/ERS-aware
            // number the player's own physics already computes every tick.
            float carTopSpeed = vehicle.CarData == null || vehicle.CarData.topSpeed <= 0 ? 337f : vehicle.CarData.topSpeed;
            // straightSpeedMultiplier is hard-clamped to <= 1.0 here: it may only ever
            // discount how much of the real ceiling this difficulty confidently uses,
            // never inflate a straight-line target past what the car can actually do.
            float straightTargetSpeed = (vehicle.TargetTopSpeedKph > 5f ? vehicle.TargetTopSpeedKph : carTopSpeed) * Mathf.Min(1f, profile.straightSpeedMultiplier);

            bool wet = track.weather == WeatherState.LightRain || track.weather == WeatherState.HeavyRain;
            float gripMultiplier = vehicle.Tyres.GripMultiplier(track.weather);
            float minCornerConfidence = profile.minimumCornerSpeedConfidence;
            if (wet)
            {
                // Low wetSkill drivers lose confidence fastest; Expert's low caution
                // still gets diluted here, it never bypasses the driver's own skill.
                float wetSkillRelief = Mathf.Lerp(0.35f, 0.05f, wetSkill / 100f);
                minCornerConfidence *= 1f - Mathf.Clamp01(profile.wetWeatherCaution * wetSkillRelief);
            }

            float experienceConfidence = Mathf.Lerp(0.85f, 1.05f, consistency / 100f);
            float apexConfidence = Mathf.Clamp01(minCornerConfidence * experienceConfidence);

            // Car-relative hairpin floor instead of one flat number for every car: a
            // stronger braking/cornering car has a genuinely higher minimum apex speed
            // even at a true hairpin.
            float carBrakingStat = vehicle.CarData == null ? 78f : vehicle.CarData.braking;
            float carCorneringStat = vehicle.CarData == null ? 78f : vehicle.CarData.cornering;
            float hairpinSpeedKph = Mathf.Lerp(76f, 108f, Mathf.Clamp01((carBrakingStat + carCorneringStat) / 200f));

            // Classify the upcoming apex by type rather than treating one continuous
            // severity number the same everywhere - a flowing high-speed kink and a
            // genuine hairpin need very different confidence curves (Part 2).
            CornerType upcomingCornerType = ClassifyUpcomingCorner(apexSeverity);
            float trueApexSpeed = EstimateApexSpeedForCornerType(upcomingCornerType, apexSeverity, straightTargetSpeed, hairpinSpeedKph, gripMultiplier, apexConfidence);

            // cornerSpeedMultiplier may exceed 1.0 for Hard/Expert: how much of the
            // tyre's real available grip a confident driver carries through the apex is
            // a driving-skill judgment call, not a hard physics ceiling like top speed.
            float apexTargetSpeed = Mathf.Lerp(trueApexSpeed * 0.5f, trueApexSpeed, apexConfidence) * profile.cornerSpeedMultiplier;

            // Driver-quality variance is the per-driver pace differentiator, independent
            // of difficulty; profile.paceMultiplier is the difficulty-tier pace scaler
            // layered on top so Hard/Expert are meaningfully quicker than Easy/Medium.
            float driverPaceVariance = Mathf.Lerp(0.89f, 1.11f, pace / 100f) * Mathf.Lerp(0.95f, 1.05f, racecraft / 100f);
            float cruiseTargetSpeed = Mathf.Lerp(straightTargetSpeed, apexTargetSpeed, severityHere) * driverPaceVariance * profile.paceMultiplier;
            float brakingApexSpeed = apexTargetSpeed * driverPaceVariance * profile.paceMultiplier;

            float damagePercent = vehicle.Damage == null ? 0f : vehicle.Damage.OverallPercent;
            float damageMultiplier = AiDamagePaceMultiplier(damagePercent);
            cruiseTargetSpeed *= damageMultiplier;
            brakingApexSpeed *= damageMultiplier;

            // Safety car / VSC pace clamp (Part 5): under a full safety car every car
            // targets the same absolute delta speed; under VSC everyone takes a flat
            // percentage off normal pace. straightTargetSpeed is clamped too so the
            // braking-point math downstream (which reasons from it) stays consistent.
            if (raceManager.CurrentRaceControlState == RaceManager.RaceControlState.SafetyCarActive)
            {
                float safetyCarCap = raceManager.SafetyCarTargetSpeedKph;
                straightTargetSpeed = Mathf.Min(straightTargetSpeed, safetyCarCap);
                cruiseTargetSpeed = Mathf.Min(cruiseTargetSpeed, safetyCarCap);
                brakingApexSpeed = Mathf.Min(brakingApexSpeed, safetyCarCap);
            }
            else if (raceManager.CurrentRaceControlState == RaceManager.RaceControlState.VirtualSafetyCar)
            {
                float vscMultiplier = raceManager.VirtualSafetyCarPaceMultiplier;
                straightTargetSpeed *= vscMultiplier;
                cruiseTargetSpeed *= vscMultiplier;
                brakingApexSpeed *= vscMultiplier;
            }

            UpdateMistake(consistency, aggression, profile);
            UpdateOvertakeState(progress, severityHere, apexDistanceAhead, apexSeverity, turnSign, aggression, overtaking, defending, profile);

            // Off-track recovery: drive straight back toward the centerline at reduced pace
            // instead of chasing the racing line offset from the grass.
            bool offTrack = Mathf.Abs(progress.lateralDistance) > track.roadHalfWidth + 0.6f;
            if (offTrack)
            {
                cruiseTargetSpeed = Mathf.Min(cruiseTargetSpeed, 118f);
                brakingApexSpeed = Mathf.Min(brakingApexSpeed, 118f);
                aggressionOffset = 0f;
                mistakeSteer = 0f;
            }

            // Corner-exit hesitation: once curvature unwinds, hold a beat of reduced
            // throttle commitment before ramping in - Expert's throttleDelay is almost
            // nothing, Easy visibly hangs back.
            bool wasCornering = corneringActive;
            corneringActive = severityHere > 0.16f;
            if (wasCornering && !corneringActive)
            {
                throttleDelayTimer = profile.throttleDelay;
            }
            else
            {
                throttleDelayTimer = Mathf.Max(0f, throttleDelayTimer - Time.deltaTime);
            }

            // Outside-inside-outside line: bias toward the outside on entry/exit
            // (curvature rising or falling) and clip toward the apex near the
            // tightest, steady point - diluted by this driver's apex precision.
            float legalLimit = LegalOffsetLimit(severityHere);
            float perCarApexError = profile.apexErrorMeters * Mathf.Lerp(1.4f, 0.6f, consistency / 100f);
            float wobble = (Mathf.PerlinNoise(noiseSeed, Time.time * 0.5f) * 2f - 1f) * profile.lineOffsetNoise;
            float lineBias = 0f;
            if (severityHere > 0.12f)
            {
                bool curvatureRising = severityHere > previousSeverityHere + 0.015f;
                bool curvatureFalling = severityHere < previousSeverityHere - 0.015f;
                float biasMagnitude = Mathf.Lerp(0f, legalLimit * 0.6f, severityHere);
                if (curvatureRising || curvatureFalling)
                {
                    lineBias = -turnSign * biasMagnitude * 0.7f;
                }
                else
                {
                    float apexMissNoise = (Mathf.PerlinNoise(noiseSeed + 37.1f, progress.distance * 0.015f) * 2f - 1f) * perCarApexError;
                    float apexPrecision = Mathf.Clamp01(1f - perCarApexError / 3f);
                    lineBias = turnSign * biasMagnitude * apexPrecision + apexMissNoise;
                }
            }
            previousSeverityHere = severityHere;

            float requestedOffset = wobble + lineBias + aggressionOffset + mistakeSteer;
            // Opening seconds: hold the assigned fan-out lane, blending back to the
            // racing line as the field strings out.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.RaceElapsed < OpeningFanDuration)
            {
                float fanBlend = 1f - Mathf.Clamp01(raceManager.RaceElapsed / OpeningFanDuration);
                requestedOffset = Mathf.Lerp(requestedOffset, openingFanOffset, fanBlend * 0.85f);
            }

            float desiredOffset = offTrack ? 0f : ConstrainLegalLineOffset(progress, requestedOffset, severityHere);
            targetPoint += right * desiredOffset;
            TrackProgress targetProgress = track.GetProgress(targetPoint);
            float legalTargetLimit = LegalOffsetLimit(severityHere);
            if (Mathf.Abs(targetProgress.lateralDistance) > legalTargetLimit)
            {
                track.SampleAtDistance(targetProgress.distance, out targetPoint, out forward, out right);
                targetPoint += right * Mathf.Clamp(targetProgress.lateralDistance, -legalTargetLimit, legalTargetLimit);
            }

            Vector3 toTarget = targetPoint - transform.position;
            float localSteer = Vector3.Dot(toTarget.normalized, transform.right);

            VehicleCommand command = new VehicleCommand();
            float edgeRecovery = Mathf.Abs(progress.lateralDistance) > track.roadHalfWidth - 1.2f ? Mathf.Sign(-progress.lateralDistance) * 0.45f : 0f;
            command.steer = Mathf.Clamp(localSteer * 2.2f + edgeRecovery, -1f, 1f);

            // Real braking point: a kinematic stopping distance from current speed down
            // to the apex speed this driver is actually willing to carry, compared
            // against genuine remaining distance to the upcoming corner - not a single
            // blunt speed-delta formula with a fixed 55kph window.
            float brakingStat = vehicle.CarData == null ? 78f : vehicle.CarData.braking;
            float decelReference = Mathf.Lerp(9.5f, 15.5f, Mathf.Clamp01(brakingStat / 100f));
            // brakeConfidenceMultiplier folds in on top of brakeDistanceMultiplier so
            // Hard/Expert genuinely brake later/shorter, not just via the weaker base
            // multiplier alone: >1 shortens the effective distance (brakes later),
            // <1 lengthens it (brakes earlier), same as the base multiplier's own sense.
            float effectiveBrakeMultiplier = Mathf.Max(0.55f, profile.brakeDistanceMultiplier * Mathf.Lerp(0.92f, 1.05f, experience / 100f) * profile.brakeConfidenceMultiplier);
            float v0 = speedKph / 3.6f;
            float v1 = Mathf.Min(speedKph, brakingApexSpeed) / 3.6f;
            float rawBrakingDistance = Mathf.Max(0f, (v0 * v0 - v1 * v1) / (2f * decelReference));
            float brakingDistance = rawBrakingDistance / effectiveBrakeMultiplier;

            float speedOverApex = speedKph - brakingApexSpeed;
            float brakeDemand = 0f;
            bool nearCorner = apexSeverity > 0.14f && apexDistanceAhead <= Mathf.Max(brakingDistance, 6f);
            if (speedOverApex > 0f && nearCorner)
            {
                float closeness = brakingDistance <= 0.5f ? 1f : Mathf.Clamp01(1f - apexDistanceAhead / brakingDistance);
                brakeDemand = Mathf.Clamp01(speedOverApex / 42f) * Mathf.Lerp(0.35f, 1f, closeness);
            }

            float throttleTarget;
            if (brakeDemand > 0.02f)
            {
                command.brake = brakeDemand;
                throttleTarget = 0f;
            }
            else
            {
                command.brake = 0f;
                float exitConfidence = profile.exitThrottleConfidence;
                if (throttleDelayTimer > 0f)
                {
                    throttleTarget = Mathf.Clamp01(Mathf.Lerp(0.2f, 0.5f, exitConfidence) * profile.throttleAggressionMultiplier);
                }
                else
                {
                    float speedGap = cruiseTargetSpeed - speedKph;
                    // throttleAggressionMultiplier scales the whole exit-throttle target
                    // so Hard/Expert commit to throttle earlier/harder on corner exit,
                    // not just ramp faster once already committed (see the MoveTowards
                    // rate below).
                    throttleTarget = Mathf.Clamp01((speedGap / 60f * Mathf.Lerp(0.7f, 1.1f, exitConfidence) + Mathf.Lerp(0.18f, 0.4f, exitConfidence)) * profile.throttleAggressionMultiplier);
                }

                // Only lift for genuine traction loss, never as a disguised second brake.
                if (vehicle.OversteerAmount > 0.4f)
                {
                    throttleTarget = Mathf.Min(throttleTarget, Mathf.Lerp(1f, 0.5f, Mathf.Clamp01((vehicle.OversteerAmount - 0.4f) / 0.5f)));
                }

                if (vehicle.LastTyreGripMultiplier > 0f && vehicle.LastTyreGripMultiplier < 0.6f)
                {
                    throttleTarget = Mathf.Min(throttleTarget, Mathf.Lerp(0.55f, 1f, vehicle.LastTyreGripMultiplier / 0.6f));
                }
            }

            // Smooth the ramp instead of snapping frame to frame - lift off quickly
            // into a brake, but build throttle back in without chopping.
            currentThrottle = Mathf.MoveTowards(currentThrottle, throttleTarget, Time.deltaTime * (brakeDemand > 0.02f ? 4.5f : 2.6f * profile.throttleAggressionMultiplier));
            command.throttle = currentThrottle;

            // Launch confidence: a brief, skill-scaled settle-in right off the line.
            // Pure input timing/ramp - never an engine or grip boost.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying)
            {
                float sinceLaunch = raceManager.RaceElapsed - participant.startReactionDelay;
                if (sinceLaunch >= 0f && sinceLaunch < launchSettleDuration)
                {
                    command.throttle = Mathf.Min(command.throttle, Mathf.Lerp(launchConfidence, 1f, sinceLaunch / launchSettleDuration));
                }
            }

            // Calmer opening seconds: keep a small throttle cap so the pack fans out into
            // turn one instead of piling into the leaders. Easy/Medium keep the full
            // 3.5s/0.72 pileup-safety cap; Hard/Expert get a much shorter, shallower
            // one - a sharp, confident driver fans out cleanly and shouldn't be held
            // back unless a collision is actually imminent (traffic avoidance still
            // applies on top of this regardless of difficulty).
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying)
            {
                RaceDifficulty difficulty = raceManager.Settings == null ? RaceDifficulty.Medium : raceManager.Settings.Difficulty;
                bool confidentTier = difficulty == RaceDifficulty.Hard || difficulty == RaceDifficulty.Expert;
                float openingCapDuration = confidentTier ? (difficulty == RaceDifficulty.Expert ? 1.1f : 1.8f) : 3.5f;
                float openingCapFloor = confidentTier ? (difficulty == RaceDifficulty.Expert ? 0.92f : 0.85f) : 0.72f;
                if (raceManager.RaceElapsed < openingCapDuration)
                {
                    command.throttle = Mathf.Min(command.throttle, Mathf.Lerp(openingCapFloor, 1f, raceManager.RaceElapsed / openingCapDuration));
                }
            }

            ApplyTrafficAvoidance(ref command, progress, speedKph, profile);

            // Driver-pressure model: a car actively attacking or defending under
            // close pressure pushes slightly harder - the tyre lockup model already
            // reads brake/steer inputs, so this alone raises lockup risk exactly
            // when a real driver would be more likely to lock a wheel.
            if (pressureFactor > 0f)
            {
                command.brake = Mathf.Min(1f, command.brake * (1f + pressureFactor * 0.08f));
                command.steer = Mathf.Clamp(command.steer * (1f + pressureFactor * 0.06f), -1f, 1f);
            }

            float tyrePitThreshold = Mathf.Lerp(0.68f, 0.52f, tyreManagement / 100f) + profile.tyreSavingBias * 0.05f;
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                vehicle.Tyres.Wear < tyrePitThreshold &&
                participant.lapTracker.CompletedLaps > 0)
            {
                command.pitRequest = true;
            }

            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                participant.pitStops == 0 &&
                participant.lapTracker.CompletedLaps >= raceManager.RecommendedPitLap(participant))
            {
                command.pitRequest = true;
            }

            // Safety car pit window (Part 6): an additional OR-condition alongside the
            // normal tyre-wear/mandatory-stop triggers above, flowing through the same
            // command.pitRequest -> BeginPitEntry pipeline (including its existing
            // queueing/staggered release) rather than a parallel path.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.ShouldAiPitUnderSafetyCar(participant))
            {
                command.pitRequest = true;
            }

            ApplyDamageStrategy(ref command, damagePercent);

            command.ers = raceManager.ShouldAiUseErs(participant, severityHere);

            // DRS legality is decided entirely by RaceManager; drsUsageQuality only
            // decides whether this driver reliably remembers to press it, committed
            // once per zone so it never flickers mid-zone.
            bool drsLegal = raceManager.IsDrsAvailable(participant);
            if (drsLegal && !wasDrsLegalLastFrame)
            {
                drsCommittedThisZone = Random.value < profile.drsUsageQuality;
            }
            wasDrsLegalLastFrame = drsLegal;
            command.drs = drsLegal && drsCommittedThisZone;

            vehicle.SetCommand(command);
        }

        float AiDamagePaceMultiplier(float damagePercent)
        {
            if (damagePercent < 8f)
            {
                return 1f;
            }

            if (damagePercent < 28f)
            {
                return Mathf.Lerp(0.99f, 0.94f, Mathf.InverseLerp(8f, 28f, damagePercent));
            }

            if (damagePercent < 58f)
            {
                return Mathf.Lerp(0.94f, 0.82f, Mathf.InverseLerp(28f, 58f, damagePercent));
            }

            return Mathf.Lerp(0.82f, 0.62f, Mathf.InverseLerp(58f, 92f, damagePercent));
        }

        void ApplyDamageStrategy(ref VehicleCommand command, float damagePercent)
        {
            if (raceManager.CurrentSession == RaceWeekendSession.Qualifying || participant == null || participant.lapTracker == null)
            {
                return;
            }

            if (damagePercent >= 42f && participant.pitStops == 0)
            {
                command.pitRequest = true;
            }

            damageDecisionTimer -= Time.deltaTime;
            if (damageDecisionTimer > 0f)
            {
                return;
            }

            damageDecisionTimer = Random.Range(2.5f, 5.5f);
            if (damagePercent >= 92f || (damagePercent >= 78f && Random.value < 0.42f))
            {
                raceManager.RetireParticipant(participant, "Damage");
            }
        }

        void ApplyTrafficAvoidance(ref VehicleCommand command, TrackProgress progress, float speedKph, RaceManager.AiDifficultyProfile profile)
        {
            float brakeDemand = 0f;
            float throttleLimit = 1f;
            float steerAdjust = 0f;
            bool blockedLeft = false;
            bool blockedRight = false;
            bool carDirectlyAhead = false;
            float cautionFactor = Mathf.Clamp(profile.trafficAvoidanceCaution, 0.5f, 1.4f);
            bool legalDrsHere = raceManager.IsDrsAvailable(participant);

            dodgeMemoryTimer = Mathf.Max(0f, dodgeMemoryTimer - Time.deltaTime);

            // The forward window grows with speed so anticipation starts early
            // enough to matter at 300+ km/h instead of five car lengths out.
            float forwardWindow = Mathf.Lerp(18f, 52f, Mathf.Clamp01(speedKph / 320f));

            for (int i = 0; i < raceManager.Participants.Count; i++)
            {
                RaceParticipant other = raceManager.Participants[i];
                if (other == null || other == participant || other.retired || other.vehicle == null || !other.gameObject.activeSelf)
                {
                    continue;
                }

                // Cars on pit guidance rails are non-colliding ghosts; ignore them.
                if (other.vehicle.IsPitGuided)
                {
                    continue;
                }

                Vector3 local = transform.InverseTransformPoint(other.transform.position);
                float absX = Mathf.Abs(local.x);
                if (local.z <= -6f || local.z >= forwardWindow || absX >= 8.5f)
                {
                    continue;
                }

                float overlap = Mathf.Clamp01(1f - absX / 8.5f);
                if (local.z > 0.5f)
                {
                    // Brake proportionally to how fast the gap is actually shrinking,
                    // not just distance: high closing speed means brake much earlier.
                    float otherSpeedKph = Mathf.Abs(other.vehicle.CurrentSpeedKph);
                    float closingKph = Mathf.Max(0f, speedKph - otherSpeedKph);
                    float timeToContact = local.z / Mathf.Max(1.5f, closingKph / 3.6f);
                    if (timeToContact < 2.4f && absX < 3.2f)
                    {
                        float urgency = Mathf.Clamp01(1f - timeToContact / 2.4f);
                        brakeDemand = Mathf.Max(brakeDemand, Mathf.Lerp(0.12f, 0.95f, urgency * urgency) * overlap);
                        throttleLimit = Mathf.Min(throttleLimit, Mathf.Lerp(0.85f, 0.15f, urgency));
                    }

                    // Tighter lane-only overlap for the soft cruising cap so a car with
                    // a real gap sharing the rough forward window (about to lap someone,
                    // or about to be let past) isn't throttle-capped for no real reason.
                    float laneOverlap = Mathf.Clamp01(1f - absX / 5.2f);
                    float proximity = Mathf.Clamp01((forwardWindow - local.z) / forwardWindow) * laneOverlap;
                    float proximityCutback = Mathf.Lerp(1f, 0.42f, proximity * Mathf.Clamp01(closingKph / 40f));

                    // A legitimate DRS tow is not traffic to avoid - lower-caution
                    // (higher-difficulty) followers commit to the draft instead of
                    // backing out of a gap they are supposed to be exploiting.
                    bool legitimateTow = legalDrsHere && absX < 2.4f && closingKph < 15f;
                    if (legitimateTow)
                    {
                        proximityCutback = Mathf.Lerp(proximityCutback, 1f, 1f - Mathf.Clamp01(cautionFactor));
                    }
                    else
                    {
                        proximityCutback = Mathf.Clamp01(1f - (1f - proximityCutback) * cautionFactor);
                    }

                    throttleLimit = Mathf.Min(throttleLimit, proximityCutback);

                    // Car parked in our lane: commit to a stronger lateral move, and
                    // remember the chosen side for a short window so a car sitting near
                    // local.x==0 doesn't make the dodge flicker frame to frame.
                    if (absX < 2.6f && local.z < forwardWindow * 0.7f)
                    {
                        carDirectlyAhead = true;
                        float rawDodgeSide = Mathf.Abs(local.x) < 0.4f ? preferredSide : -Mathf.Sign(local.x);
                        if (dodgeMemoryTimer <= 0f)
                        {
                            dodgeMemorySide = rawDodgeSide;
                        }
                        dodgeMemoryTimer = 1.1f;
                        float dodgeStrength = Mathf.Clamp01(1f - local.z / (forwardWindow * 0.7f));
                        steerAdjust += dodgeMemorySide * Mathf.Lerp(0.08f, 0.4f, dodgeStrength);
                    }
                }

                // Side-by-side: never steer into the car alongside, and remember
                // which flanks are occupied so we don't dodge into a sandwich.
                if (Mathf.Abs(local.z) < 6.5f && absX < 4.2f)
                {
                    if (local.x < 0f)
                    {
                        blockedLeft = true;
                    }
                    else
                    {
                        blockedRight = true;
                    }

                    float sideOverlap = Mathf.Clamp01(1f - absX / 4.2f);
                    steerAdjust += -Mathf.Sign(local.x) * Mathf.Lerp(0.05f, 0.24f, sideOverlap);
                    float sideCutback = Mathf.Clamp01(1f - (1f - Mathf.Lerp(1f, 0.66f, sideOverlap)) * cautionFactor);
                    throttleLimit = Mathf.Min(throttleLimit, sideCutback);
                }
            }

            // Boxed in on both sides with a car ahead: lift cleanly and wait for a
            // gap instead of forcing a three-wide wedge.
            if (blockedLeft && blockedRight)
            {
                steerAdjust = 0f;
                if (carDirectlyAhead)
                {
                    throttleLimit = Mathf.Min(throttleLimit, Mathf.Clamp01(1f - (1f - 0.34f) * cautionFactor));
                    brakeDemand = Mathf.Max(brakeDemand, 0.16f * Mathf.Clamp01(cautionFactor));
                }
            }
            else if (carDirectlyAhead)
            {
                // Don't dodge toward an occupied flank.
                if (steerAdjust < 0f && blockedLeft)
                {
                    steerAdjust = blockedRight ? 0f : Mathf.Abs(steerAdjust);
                }
                else if (steerAdjust > 0f && blockedRight)
                {
                    steerAdjust = blockedLeft ? 0f : -Mathf.Abs(steerAdjust);
                }
            }

            if (brakeDemand > 0f)
            {
                command.brake = Mathf.Max(command.brake, brakeDemand);
            }

            command.throttle = Mathf.Min(command.throttle, throttleLimit);
            command.steer = Mathf.Clamp(command.steer + steerAdjust, -1f, 1f);
        }

        // Curvature sampled across three forward points instead of two, taking the
        // sharper of the two sub-windows so a genuinely tight corner localizes
        // correctly instead of being averaged down by a long single window.
        float EstimateCornerSeverity(float distance)
        {
            Vector3 pointA;
            Vector3 forwardA;
            Vector3 rightA;
            Vector3 pointB;
            Vector3 forwardB;
            Vector3 rightB;
            Vector3 pointC;
            Vector3 forwardC;
            Vector3 rightC;
            track.SampleAtDistance(distance + 14f, out pointA, out forwardA, out rightA);
            track.SampleAtDistance(distance + 46f, out pointB, out forwardB, out rightB);
            track.SampleAtDistance(distance + 82f, out pointC, out forwardC, out rightC);
            float turnNear = Vector3.Angle(forwardA, forwardB);
            float turnFar = Vector3.Angle(forwardB, forwardC);
            return Mathf.Clamp01(Mathf.Max(turnNear, turnFar) / 42f);
        }

        // Walks the corner-severity estimate forward to find the sharpest upcoming
        // point within lookahead range, giving a genuine "distance to the corner"
        // and "how sharp" pair for the braking-point model, instead of only ever
        // reacting to the curvature directly under the car.
        void FindUpcomingApex(float fromDistance, out float apexDistanceAhead, out float apexSeverity)
        {
            apexDistanceAhead = 400f;
            apexSeverity = 0f;
            const float step = 20f;
            const float maxLookahead = 180f;
            for (float d = 0f; d <= maxLookahead; d += step)
            {
                float severity = EstimateCornerSeverity(fromDistance + d);
                if (severity > apexSeverity)
                {
                    apexSeverity = severity;
                    apexDistanceAhead = d;
                }

                // Found a real corner and it is falling away again - that is this
                // corner's peak, no need to keep searching into the next one.
                if (apexSeverity > 0.55f && severity < apexSeverity - 0.12f)
                {
                    break;
                }
            }
        }

        void UpdateMistake(int consistency, int aggression, RaceManager.AiDifficultyProfile profile)
        {
            mistakeTimer -= Time.deltaTime;
            if (mistakeTimer > 0f)
            {
                // Recovery from a small mistake correction scales with difficulty: a
                // sharp, confident Expert gathers the car back up much faster than the
                // flat rate every tier used to share. Derived from mistakeChancePerLap
                // (already the difficulty-quality axis) rather than adding a new field.
                float recoveryBlend = Mathf.Clamp01(1f - profile.mistakeChancePerLap / 0.16f);
                float recoveryRate = Mathf.Lerp(0.6f, 2.2f, recoveryBlend);
                mistakeSteer = Mathf.MoveTowards(mistakeSteer, 0f, Time.deltaTime * recoveryRate);
                return;
            }

            float consistencyPenalty = Mathf.Lerp(1.7f, 0.35f, consistency / 100f);
            float aggressionPenalty = Mathf.Lerp(0.85f, 1.35f, aggression / 100f);
            if (Random.value < profile.mistakeChancePerLap * consistencyPenalty * aggressionPenalty)
            {
                mistakeSteer = Random.Range(-0.9f, 0.9f);
                mistakeTimer = Random.Range(0.5f, 1.2f);
            }
            else
            {
                mistakeTimer = Random.Range(3f, 8f);
            }
        }

        // Explicit overtake/defend state machine. Transitions run on gap, corner
        // context, DRS availability and the driver's commitment/aggression stats;
        // the actual lateral commitment is written into aggressionOffset, which the
        // existing legal-line clamp and traffic-avoidance safety logic still bound.
        void UpdateOvertakeState(TrackProgress progress, float severityHere, float apexDistanceAhead, float apexSeverity, float turnSign, int aggression, int overtaking, int defending, RaceManager.AiDifficultyProfile profile)
        {
            RaceParticipant ahead = raceManager.FindCarAhead(participant, 46f);
            RaceParticipant behind = raceManager.FindCarBehind(participant, 32f);
            float legalLimit = LegalOffsetLimit(severityHere);
            float commitment = Mathf.Clamp01(profile.overtakeCommitment * Mathf.Lerp(0.7f, 1.15f, (aggression + overtaking) / 200f));
            float defendCommitment = Mathf.Clamp01(profile.defendCommitment * Mathf.Lerp(0.7f, 1.15f, defending / 100f));

            // Part 8: a lap or two of extra eagerness right after a safety car
            // restart - detected as a Restart -> Green edge on the race control
            // state machine, cheap to blend in since commitment is already read here
            // every frame.
            RaceManager.RaceControlState rcState = raceManager.CurrentRaceControlState;
            if (wasRestartLastFrame && rcState == RaceManager.RaceControlState.Green)
            {
                postRestartCommitmentBoostTimer = 50f;
            }
            wasRestartLastFrame = rcState == RaceManager.RaceControlState.Restart;
            postRestartCommitmentBoostTimer = Mathf.Max(0f, postRestartCommitmentBoostTimer - Time.deltaTime);
            if (postRestartCommitmentBoostTimer > 0f)
            {
                commitment = Mathf.Clamp01(commitment + 0.12f);
            }

            // No overtaking under a safety car / VSC or in a locally yellow-flagged
            // sector (Part 5 / Part 4). Any attempt already under way is aborted
            // cleanly back to a single lane instead of snapping straight.
            bool overtakingAllowedHere = raceManager.IsOvertakingAllowed && progress.sector != raceManager.YellowFlagSector;
            if (!overtakingAllowedHere && overtakeState != OvertakeState.Following && overtakeState != OvertakeState.BackingOut && overtakeState != OvertakeState.CompletingPass)
            {
                overtakeState = OvertakeState.BackingOut;
                overtakeStateTimer = 0.6f;
            }

            overtakeStateTimer -= Time.deltaTime;
            pressureFactor = 0f;

            switch (overtakeState)
            {
                case OvertakeState.Following:
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, 0f, Time.deltaTime * 4f);
                    if (ahead != null && ahead.vehicle != null && overtakingAllowedHere)
                    {
                        float gapSeconds = raceManager.GetIntervalToAheadSeconds(participant);
                        bool approachingBrakeZone = apexDistanceAhead < 90f && apexSeverity > 0.2f;
                        bool drsHelp = raceManager.IsDrsAvailable(participant);
                        bool hasPace = Mathf.Abs(vehicle.CurrentSpeedKph) >= Mathf.Abs(ahead.vehicle.CurrentSpeedKph) - 4f;

                        // Genuine pace-delta gate: a clearly slower backmarker gets
                        // attacked more readily the higher this driver's commitment is,
                        // not only when the gap happens to cross one fixed threshold.
                        float speedDeltaKph = Mathf.Abs(vehicle.CurrentSpeedKph) - Mathf.Abs(ahead.vehicle.CurrentSpeedKph);
                        bool clearlySlower = speedDeltaKph > Mathf.Lerp(9f, 2.5f, commitment);

                        // Patience timer: stuck following the same 0.8-1.2s gap for a
                        // while raises the attack-attempt probability over time instead
                        // of orbiting it for the rest of the stint. Resets on any state
                        // change or once the gap opens back up.
                        followingTimer = gapSeconds < 1.8f ? followingTimer + Time.deltaTime : 0f;
                        float patienceBonus = Mathf.Clamp01(followingTimer / 10f) * Mathf.Lerp(2f, 9f, commitment);

                        // DRS conversion: a real advantage should turn into real
                        // attacks, scaled by commitment so Expert converts a tow far
                        // more often than a tentative Easy/Medium driver does.
                        float drsBonus = drsHelp ? Mathf.Lerp(1.2f, 2.6f, commitment) : 1f;

                        bool attackTrigger = gapSeconds < 1.1f && (approachingBrakeZone || drsHelp || clearlySlower) && hasPace;
                        if (attackTrigger && Random.value < commitment * Time.deltaTime * (3f + patienceBonus) * drsBonus)
                        {
                            overtakeState = OvertakeState.PreparingAttack;
                            overtakeStateTimer = 2.2f;
                            followingTimer = 0f;
                            attackSide = Mathf.Sign(Vector3.Dot(transform.position - ahead.transform.position, transform.right));
                            if (Mathf.Abs(attackSide) < 0.1f)
                            {
                                attackSide = preferredSide;
                            }
                        }
                    }
                    else
                    {
                        followingTimer = 0f;
                    }
                    break;

                case OvertakeState.PreparingAttack:
                {
                    // attackSide is chosen once on entry above and only ever read from
                    // here on, so it cannot flip mid-attempt.
                    float prepOffset = attackSide * Mathf.Lerp(1.2f, 2.6f, commitment) * 0.6f;
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, Mathf.Clamp(prepOffset, -legalLimit, legalLimit), Time.deltaTime * 5f);
                    bool stillThere = ahead != null && raceManager.GetIntervalToAheadSeconds(participant) < 1.4f;
                    if (!stillThere || overtakeStateTimer <= 0f)
                    {
                        if (stillThere && Random.value < commitment)
                        {
                            overtakeState = attackSide < 0f ? OvertakeState.AttackingOutside : OvertakeState.AttackingInside;
                            overtakeStateTimer = 2.6f;
                        }
                        else
                        {
                            overtakeState = OvertakeState.BackingOut;
                            overtakeStateTimer = 1f;
                        }
                    }
                    break;
                }

                case OvertakeState.AttackingInside:
                case OvertakeState.AttackingOutside:
                {
                    pressureFactor = Mathf.Lerp(0.4f, 1f, commitment);
                    float attackOffset = attackSide * Mathf.Lerp(2f, legalLimit, commitment);
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, Mathf.Clamp(attackOffset, -legalLimit, legalLimit), Time.deltaTime * 6.5f);
                    bool sideBySideNow = ahead != null && Mathf.Abs(transform.InverseTransformPoint(ahead.transform.position).z) < 6f;
                    if (sideBySideNow)
                    {
                        overtakeState = OvertakeState.SideBySide;
                        overtakeStateTimer = 3f;
                    }
                    else if (ahead == null)
                    {
                        overtakeState = OvertakeState.CompletingPass;
                        overtakeStateTimer = 1.4f;
                        raceManager.ReportAiOvertakeCompleted(participant);
                    }
                    else if (raceManager.GetIntervalToAheadSeconds(participant) > 1.8f || overtakeStateTimer <= 0f)
                    {
                        overtakeState = OvertakeState.BackingOut;
                        overtakeStateTimer = 1f;
                    }
                    break;
                }

                case OvertakeState.SideBySide:
                    pressureFactor = Mathf.Lerp(0.4f, 1f, commitment);
                    // Hold the line and ease off the aggression; ApplyTrafficAvoidance's
                    // blockedLeft/blockedRight logic already keeps both cars apart.
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, aggressionOffset * 0.9f, Time.deltaTime * 2f);
                    if (ahead == null || raceManager.FindCarAhead(participant, 12f) == null)
                    {
                        overtakeState = OvertakeState.CompletingPass;
                        overtakeStateTimer = 1.2f;
                        raceManager.ReportAiOvertakeCompleted(participant);
                    }
                    else if (overtakeStateTimer <= 0f)
                    {
                        overtakeState = OvertakeState.BackingOut;
                        overtakeStateTimer = 0.8f;
                    }
                    break;

                case OvertakeState.CompletingPass:
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, 0f, Time.deltaTime * 3f);
                    if (overtakeStateTimer <= 0f)
                    {
                        overtakeState = OvertakeState.Following;
                    }
                    break;

                case OvertakeState.BackingOut:
                    // Higher overtakeCommitment backs out less readily/later.
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, 0f, Time.deltaTime * Mathf.Lerp(3f, 6f, 1f - commitment));
                    if (overtakeStateTimer <= 0f)
                    {
                        overtakeState = OvertakeState.Following;
                    }
                    break;
            }

            // Defend once per approaching braking zone: cover the inside line if a
            // real threat is close behind, then leave it alone until the next corner
            // instead of weaving repeatedly.
            bool approaching = apexDistanceAhead < 70f && apexSeverity > 0.16f;
            if (!approaching)
            {
                hasCoveredThisApex = false;
            }
            else if (overtakeState == OvertakeState.Following && !hasCoveredThisApex && behind != null && behind.vehicle != null)
            {
                float behindGap = raceManager.GetIntervalToAheadSeconds(behind);
                bool behindHasDrs = raceManager.IsDrsAvailable(behind);
                bool threatClose = behindGap > 0f && (behindGap < 1.3f || behindHasDrs);
                if (threatClose && Random.value < defendCommitment)
                {
                    float coverOffset = turnSign * Mathf.Lerp(1f, 2.3f, defendCommitment);
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, Mathf.Clamp(coverOffset, -legalLimit, legalLimit), Time.deltaTime * 5f);
                    hasCoveredThisApex = true;
                    pressureFactor = Mathf.Max(pressureFactor, Mathf.Lerp(0.3f, 0.8f, defendCommitment));
                }
            }
        }

        float ConstrainLegalLineOffset(TrackProgress progress, float requestedOffset, float cornerSeverity)
        {
            float legalLimit = LegalOffsetLimit(cornerSeverity);
            float turnSign = EstimateTurnDirection(progress.distance);
            float desired = Mathf.Clamp(requestedOffset, -legalLimit, legalLimit);
            if (Mathf.Abs(turnSign) > 0.01f && cornerSeverity > 0.18f)
            {
                float insideLimit = Mathf.Lerp(legalLimit, legalLimit * 0.42f, cornerSeverity);
                if (turnSign > 0f)
                {
                    desired = Mathf.Clamp(desired, -legalLimit, insideLimit);
                }
                else
                {
                    desired = Mathf.Clamp(desired, -insideLimit, legalLimit);
                }
            }

            if (Mathf.Abs(progress.lateralDistance) > track.roadHalfWidth - 1.6f)
            {
                desired = Mathf.MoveTowards(desired, 0f, Mathf.Lerp(2.2f, 5.2f, cornerSeverity));
            }

            return desired;
        }

        float LegalOffsetLimit(float cornerSeverity)
        {
            float margin = Mathf.Lerp(1.8f, 3.1f, cornerSeverity);
            float kerbLimit = track.kerbStart > 0f ? track.kerbStart - 0.8f : track.roadHalfWidth - margin;
            return Mathf.Max(0.75f, Mathf.Min(track.roadHalfWidth - margin, kerbLimit));
        }

        float EstimateTurnDirection(float distance)
        {
            Vector3 pointA;
            Vector3 forwardA;
            Vector3 rightA;
            Vector3 pointB;
            Vector3 forwardB;
            Vector3 rightB;
            track.SampleAtDistance(distance + 16f, out pointA, out forwardA, out rightA);
            track.SampleAtDistance(distance + 64f, out pointB, out forwardB, out rightB);
            return Mathf.Sign(Vector3.Cross(forwardA, forwardB).y);
        }
    }
}
