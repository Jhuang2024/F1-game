using System.Collections.Generic;
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

        // Stuck-recovery maneuver (Part 2/3): only engages while RaceManager's own
        // recovery-state classification says this car is Recovering or already
        // ActuallyStranded - never while merely Queued/PitSequence/RaceControlPacing,
        // which are legitimate reasons to be slow that need no intervention at all.
        enum RecoveryManeuver { None, ReverseAway, ReorientWrongWay }
        RecoveryManeuver activeManeuver = RecoveryManeuver.None;
        float maneuverTimer;
        float maneuverTurnSide = 1f;
        float stuckDetectTimer;
        const float StuckManeuverTriggerSeconds = 2.5f;
        const float ReverseAwayDuration = 1.1f;
        const float ReorientDuration = 1.6f;

        // How long this car has been sitting in Following without forcing an attack -
        // a stuck Expert eventually raises its own attack-attempt probability instead
        // of orbiting the same 0.8-1.2s gap for the rest of the stint.
        float followingTimer;

        // Temporary post-safety-car-restart commitment boost: a lap or two of extra
        // eagerness to attack once the field is unleashed again.
        float postRestartCommitmentBoostTimer;
        bool wasRestartLastFrame;

        // Race-control autopilot handback (safety car restart bug fix). While
        // participant.isRaceControlAutopilot is true, Update() returns early
        // every frame (see the autopilot branch below) and never touches
        // lastProgressDistance/hasProgressReference - so by the time control
        // comes back, hasProgressReference could be seeded from a lap or more
        // ago. wasRaceControlAutopilotLastFrame catches the exact frame that
        // flag drops back to false (RaceManager now holds it true through the
        // whole Restart + green-flag ramp, not just the physical SC period) so
        // this controller can resync from its real current position and clear
        // every piece of transient attack/defend/avoidance state before normal
        // driving resumes, instead of steering off a stale reference.
        bool wasRaceControlAutopilotLastFrame;
        float handbackRampTimer;
        const float HandbackRampDuration = 2f;

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
        enum CornerType { HighSpeed, Medium, Slow, VeryTight, Hairpin }

        // Corner-speed fix: Hard used to get zero benefit from every isExpert-only
        // branch in this classifier and in EstimateApexSpeedForCornerType below -
        // only the literal Expert tier (gated further by ExpertIsRuthless) ever
        // widened the HighSpeed/Medium buckets or raised their floors, so "Hard"
        // cornered identically to Medium apart from the separate confidence/
        // multiplier stats. skillTier replaces the flat isExpert bool with a
        // continuous 0-1 blend (Easy/Medium=0, Hard=0.6, Expert=1) so Hard is
        // genuinely, meaningfully faster through corners than Medium - not just
        // "the same corner behaviour with a higher confidence number feeding it" -
        // while staying clearly a notch behind Expert.
        float CorneringSkillTier()
        {
            if (raceManager.IsExpertDifficulty)
            {
                return 1f;
            }

            RaceDifficulty difficulty = raceManager.Settings == null ? RaceDifficulty.Medium : raceManager.Settings.Difficulty;
            if (difficulty == RaceDifficulty.Expert)
            {
                // Expert difficulty selected but ExpertIsRuthless is off - still
                // meaningfully sharper than Hard, just not the absolute ceiling.
                // Cornering buff round 5 (was 0.85).
                return 0.92f;
            }

            // Cornering buff round 5: Hard pulled closer to Expert's cornering
            // commitment (was 0.6) - Hard should be meaningfully competitive through
            // corners, not just "clearly better than Medium".
            if (difficulty == RaceDifficulty.Hard)
            {
                return 0.72f;
            }

            // Cornering buff round 9: Easy and Medium used to share this tier at a
            // flat 0 - "tune down by little increments for easier difficulty
            // levels" needs an actual step between them, not just whatever
            // apexConfidence alone provided. Medium now gets a real, meaningfully
            // smaller share than Hard so the four tiers form a genuine staircase
            // (Easy 0 -> Medium 0.32 -> Hard 0.72 -> Expert 0.92-1.0) instead of
            // Easy and Medium reading identically through corners.
            return difficulty == RaceDifficulty.Medium ? 0.32f : 0f;
        }

        // EstimateCornerSeverity measures heading change over a short, fixed ~32-36m
        // window and clamps at 1.0 for anything tighter than roughly a 44m-radius
        // turn - a genuinely tight corner and an actual near-180-degree hairpin both
        // saturate that metric identically, so it alone can't tell them apart. This
        // instead compares forward direction well before and well after the apex
        // (a much wider baseline) to measure how much of a real U-turn the corner
        // actually is - a track like Japan/Suzuka, whose tightest corners are real
        // but well short of a full U-turn, saturates EstimateCornerSeverity the same
        // as a genuine hairpin but should never read as one here.
        float MeasureHairpinTurnAngle(float apexDistance)
        {
            Vector3 pointBefore, forwardBefore, rightBefore;
            Vector3 pointAfter, forwardAfter, rightAfter;
            track.SampleAtDistance(apexDistance - 55f, out pointBefore, out forwardBefore, out rightBefore);
            track.SampleAtDistance(apexDistance + 55f, out pointAfter, out forwardAfter, out rightAfter);
            return Vector3.Angle(forwardBefore, forwardAfter);
        }

        // Part A.5: higher-skill tiers get wider HighSpeed/Medium buckets so they
        // stop treating flowing bends as real corners the way lower tiers
        // correctly still do.
        CornerType ClassifyUpcomingCorner(float apexSeverity, float skillTier, float hairpinTurnAngleDegrees)
        {
            // Corner-speed pass: the old bands (0.25-0.30 / 0.5-0.55) were
            // systematically mis-bucketing genuinely fast, flowing corners
            // (a Copse/Maggotts-style sweep) into the Medium tier, which has
            // a much lower confidence floor - the AI read as "scared of every
            // corner" even after the HighSpeed bucket itself was tuned to be
            // fast, because too few real corners ever landed in that bucket.
            // Widened further for higher-skill tiers so Hard/Expert commit a
            // meaningfully larger share of the curve to the confident bands.
            // Cornering buff round 7: redefined what "tight" and "fast" even mean
            // here - after six rounds of raising the FLOOR speed inside each
            // bucket, feedback was still "fast corners need to be a lot faster",
            // which means too many genuinely fast corners were still landing in
            // Medium/Slow rather than HighSpeed at all. Bands widened
            // dramatically: HighSpeed/Medium together now cover roughly 80-90%
            // of the severity range (was ~52-67%), and Hairpin is reserved for
            // only the most extreme ~10-12% instead of ~25-26%. Slow (a real but
            // non-hairpin tight corner) now sits in that narrow remaining band
            // between Medium and Hairpin.
            // Three-tier tight-corner split: Slow ("tight corners"), VeryTight ("very
            // very tight corners") and Hairpin now cover three genuinely distinct
            // corner feels instead of Slow/Hairpin alone. Hairpin's band is narrowed
            // to sit right at the top of the severity range so it's reserved for
            // corners that are actually close to a 180-degree turn, not merely tight.
            // Band rebalance: Slow expanded (0.88-0.90 -> 0.93-0.95 ceiling) and
            // VeryTight tightened into the narrow remaining gap before Hairpin
            // (was a 0.07-wide band, now ~0.02-0.04) - most corners that used to read
            // as "very very tight" now read as an ordinary tight corner instead, and
            // VeryTight is reserved for only the small severity range genuinely
            // sharper than that.
            float highSpeedCeiling = Mathf.Lerp(0.42f, 0.60f, skillTier);
            float mediumCeiling = Mathf.Lerp(0.66f, 0.80f, skillTier);
            float slowCeiling = Mathf.Lerp(0.93f, 0.95f, skillTier);
            float veryTightCeiling = Mathf.Lerp(0.95f, 0.97f, skillTier);

            if (apexSeverity < highSpeedCeiling)
            {
                return CornerType.HighSpeed;
            }

            if (apexSeverity < mediumCeiling)
            {
                return CornerType.Medium;
            }

            if (apexSeverity < slowCeiling)
            {
                return CornerType.Slow;
            }

            if (apexSeverity < veryTightCeiling)
            {
                return CornerType.VeryTight;
            }

            // Genuine hairpin reservation: apexSeverity has saturated at 1.0, but that
            // alone just means "tighter than ~44m radius" - it says nothing about
            // whether the corner is actually close to a full 180-degree turn. Only
            // promote to Hairpin when the wide-baseline turn angle backs that up;
            // otherwise this is a very tight corner, not a hairpin (e.g. Japan/Suzuka's
            // tightest corners, which saturate apexSeverity without ever approaching a
            // true U-turn), and stays classified as VeryTight instead.
            return hairpinTurnAngleDegrees >= 150f ? CornerType.Hairpin : CornerType.VeryTight;
        }

        // Per-tier apex speed curve instead of one flat Pow(severity, 1.4) eased
        // toward the same hairpin floor for every corner. High-speed and medium
        // corners get their own, much higher, floor so a confident driver carries
        // speed close to trueApexSpeed's upper end through a flowing bend instead
        // of being dragged toward hairpin pace the moment severity crosses one
        // broad threshold. apexConfidence (already difficulty+driver derived)
        // blends the floor upward for a sharper driver on the same corner.
        //
        // Corner-speed fix: the low-confidence base floors (0.84/0.58/1.15) are
        // raised across the board (0.88/0.64/1.25) - AI was reading as too slow
        // even on Easy/Medium, and "the issue is corner speed" was a general
        // complaint, not only a difficulty-scaling one. skillTier (0-1, see
        // CorneringSkillTier) replaces the flat isExpert bool so Hard now gets a
        // genuine, partial share of the high-confidence ceiling/ease-power
        // instead of none at all, while Easy/Medium are untouched at skillTier=0.
        float EstimateApexSpeedForCornerType(CornerType type, float apexSeverity, float straightTargetSpeed, float hairpinSpeedKph, float gripMultiplier, float apexConfidence, float skillTier, float compoundSpeedOffsetKph)
        {
            float floorSpeed;
            float easePower;
            switch (type)
            {
                case CornerType.HighSpeed:
                    // Cornering buff round 8: round 7 pushed this ceiling PAST 100% of
                    // straightTargetSpeed (up to 1.22x) and then apexTargetSpeed below
                    // multiplied the result by profile.cornerSpeedMultiplier (up to
                    // 1.85x for Expert) on top of that - two independent
                    // difficulty-scaled multipliers stacking on the same number. The
                    // AI ended up targeting speeds meaningfully ABOVE its own
                    // straight-line top speed through a corner with real curvature,
                    // which is not achievable by any amount of steering authority -
                    // it ran wide and hit the wall exactly as reported. A corner can
                    // at best approach straight-line speed, never exceed it, so this
                    // is now hard-capped at 1.0x and cornerSpeedMultiplier no longer
                    // applies to any corner type (see apexTargetSpeed below) - skillTier
                    // alone drives how close to that 1.0x ceiling a sharper difficulty
                    // gets, with no second multiplier stacked on top.
                    // Cornering buff round 9: pushed to the practical ceiling - Expert
                    // (skillTier=1) now reaches essentially the full 1.0x cap with
                    // barely any confidence-blend discount, and the new smoother
                    // skillTier staircase (see CorneringSkillTier) means Easy/Medium/
                    // Hard now step down from that in genuinely smaller increments
                    // instead of Easy and Medium sharing the same low ceiling.
                    // Tyre-difference pass: HighSpeed/Medium floors are proportional to
                    // straightTargetSpeed, which already carries TyreState's flat
                    // compound penalty (see VehicleController.CalculateTargetTopSpeedKph,
                    // which straightTargetSpeed is read from) - no separate subtraction
                    // needed here, or a slower compound would be double-penalized in
                    // these two bucket types relative to a genuine straight.
                    floorSpeed = Mathf.Lerp(straightTargetSpeed * 0.94f, straightTargetSpeed * Mathf.Lerp(0.97f, 1.0f, skillTier), apexConfidence);
                    easePower = Mathf.Lerp(6f, 10f, skillTier);
                    break;
                case CornerType.Medium:
                    // Cornering buff round 9: pushed to the practical ceiling
                    // alongside HighSpeed above - a "medium" corner still has real
                    // curvature so it keeps a hair more margin under 1.0x than
                    // HighSpeed, but Expert now gets essentially all of it.
                    floorSpeed = Mathf.Lerp(straightTargetSpeed * 0.72f, straightTargetSpeed * Mathf.Lerp(0.87f, 0.99f, skillTier), apexConfidence);
                    easePower = Mathf.Lerp(3.6f, 5.4f, skillTier);
                    break;
                case CornerType.Slow:
                    // Tight-corner speed calibration round 3: raised again from
                    // ~250-300kph to ~300-310kph (apexConfidence still blends toward the
                    // low end, skillTier still lifts the ceiling within that band) -
                    // Mathf.Min against straightTargetSpeed keeps the same
                    // overspeed/wall-crash guard for the rare case a car's own
                    // straight-line pace is below this (e.g. under a safety car).
                    // Tyre-difference pass: unlike HighSpeed/Medium above, this floor is
                    // a fixed absolute kph target rather than a straightTargetSpeed
                    // fraction, so it does NOT automatically inherit the compound
                    // penalty from straightTargetSpeed - subtracted explicitly here
                    // instead (and clamped so it can never go negative).
                    floorSpeed = Mathf.Min(straightTargetSpeed, Mathf.Max(15f, Mathf.Lerp(300f, Mathf.Lerp(305f, 310f, skillTier), apexConfidence) - compoundSpeedOffsetKph));
                    easePower = Mathf.Lerp(3.4f, 4.6f, skillTier);
                    break;
                case CornerType.VeryTight:
                    // "Very very tight" corners: a distinct tier between Slow's
                    // ~300-310kph tight corner and Hairpin's ~50-75kph crawl - pinned to
                    // an explicit ~150kph target (130 low-confidence base, 150-165
                    // skill-scaled ceiling) rather than a range, since this tier is
                    // meant to read as one consistent speed rather than a wide band.
                    // Tyre-difference pass: explicit compound-penalty subtraction, same
                    // reasoning as the Slow bucket above.
                    floorSpeed = Mathf.Min(straightTargetSpeed, Mathf.Max(15f, Mathf.Lerp(130f, Mathf.Lerp(150f, 165f, skillTier), apexConfidence) - compoundSpeedOffsetKph));
                    easePower = Mathf.Lerp(2.8f, 3.8f, skillTier);
                    break;
                default:
                    // Hairpin floor: an explicit ~50-75kph target (a real hairpin
                    // crawl) - this bucket's severity band is now narrowed (see
                    // ClassifyUpcomingCorner) so it's only reached by corners that are
                    // actually close to a 180-degree turn, not merely tight ones.
                    // Mathf.Min against straightTargetSpeed keeps the same overspeed
                    // guard as the tiers above.
                    // Tight-corner fix round 2: easePower dropped from 1.2 to 0.45 - at
                    // 1.2, only a corner at the literal severity ceiling (~1.0) actually
                    // reached floorSpeed; anything else in the Hairpin bucket (severity
                    // as low as ~0.75) still blended in enough straightTargetSpeed to
                    // land well above the floor regardless of how low the floor itself
                    // was set. A much smaller exponent pulls the whole Hairpin severity
                    // band toward floorSpeed instead of only its very top.
                    // Tyre-difference pass: explicit compound-penalty subtraction, same
                    // reasoning as the Slow/VeryTight buckets above - in heavy rain this
                    // can take a slick's hairpin floor down close to walking pace, which
                    // is exactly the "incredibly slow" the tyre-difference request asked
                    // for.
                    floorSpeed = Mathf.Min(straightTargetSpeed, Mathf.Max(15f, hairpinSpeedKph - compoundSpeedOffsetKph));
                    easePower = 0.45f;
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
            // Part A.7: the 0.95 confidence ceiling and 0.3s settle floor below were
            // diluting Expert's near-zero reactionTimeSeconds - a perfect-skill driver
            // on Expert should reach genuinely near-instant full confidence, not cap
            // out at the same ceiling a merely-good Hard-tier driver would.
            bool isExpertStart = manager.IsExpertDifficulty;
            float launchSkillCeiling = isExpertStart ? 0.99f : 0.95f;
            float settleFloor = isExpertStart ? 0.05f : 0.3f;
            launchConfidence = Mathf.Clamp01(Mathf.Lerp(0.55f, launchSkillCeiling, launchSkill) - startupProfile.reactionTimeSeconds * 0.12f);
            launchSettleDuration = Mathf.Lerp(settleFloor, 1.3f, 1f - launchSkill) + startupProfile.reactionTimeSeconds * 0.35f;
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

            // Race-control autopilot handback resync: fires on the exact frame
            // participant.isRaceControlAutopilot drops from true to false (now
            // only after RaceManager's Restart + green-flag ramp hold, not the
            // instant the physical safety car period ends) - resets progress
            // tracking and every piece of transient driving state before normal
            // driving resumes below.
            bool isAutopilotNow = participant != null && participant.isRaceControlAutopilot;
            if (wasRaceControlAutopilotLastFrame && !isAutopilotNow)
            {
                HandleRaceControlAutopilotReleased();
            }
            wasRaceControlAutopilotLastFrame = isAutopilotNow;

            // Full safety car convoy autopilot: race control drives the car
            // directly for the duration of the full SC period (now extended
            // through the Restart hold and green-flag ramp - see
            // RaceManager.IsRaceControlAutopilotHoldPeriod), skipping the
            // entire overtake/defend/attack state machine and ERS/DRS usage
            // below entirely - a pitting car (isPitting/pitPhase != None, or
            // still limited on pit exit) falls out of this and keeps its normal
            // driving/pit-guided handling instead, then rejoins the convoy on
            // its own once it's back out and clear of the pit limiter.
            if (isAutopilotNow && !participant.isPitting &&
                participant.pitPhase == PitPhase.None && !participant.pitLimiterUntilExit)
            {
                vehicle.SetCommand(raceManager.BuildRaceControlAutopilotCommand(participant));
                return;
            }

            // Continuity-aware progress lookup so the AI never snaps to the wrong part of
            // the track near the start/finish wrap or where sections run close together.
            // hasProgressReference is deliberately false for at least one frame right
            // after a race-control autopilot handback (see above), which forces a full
            // GetProgress position search instead of trusting a lastProgressDistance
            // that could be a lap or more stale.
            TrackProgress progress = hasProgressReference
                ? track.GetProgressNear(transform.position, lastProgressDistance)
                : track.GetProgress(transform.position);
            lastProgressDistance = progress.distance;
            hasProgressReference = true;
            float speedKph = Mathf.Abs(vehicle.CurrentSpeedKph);

            // Stuck-recovery maneuver (Part 2/3): runs to completion once triggered,
            // fully overriding normal driving for its short duration, then hands
            // straight back to the regular off-track recovery steering below (which
            // already drives back toward the centerline on its own).
            if (activeManeuver != RecoveryManeuver.None)
            {
                maneuverTimer -= Time.deltaTime;
                vehicle.SetCommand(new VehicleCommand { reverseAssist = true, steer = maneuverTurnSide });
                if (maneuverTimer <= 0f)
                {
                    activeManeuver = RecoveryManeuver.None;
                    stuckDetectTimer = 0f;
                    if (participant != null)
                    {
                        participant.recoveryAttemptCount++;
                        GameLog.Info("[RaceControl] " + participant.driverName + " completed recovery maneuver attempt #" + participant.recoveryAttemptCount + ".");
                    }
                }

                return;
            }

            bool eligibleForRecoveryManeuver = participant != null &&
                (participant.recoveryState == CarRecoveryState.Recovering || participant.recoveryState == CarRecoveryState.ActuallyStranded);
            if (eligibleForRecoveryManeuver && speedKph < 4f)
            {
                stuckDetectTimer += Time.deltaTime;
                if (stuckDetectTimer > StuckManeuverTriggerSeconds)
                {
                    bool facingWrongWay = Vector3.Dot(transform.forward, progress.forward) < -0.4f;
                    float recoverySteerSign = Mathf.Sign(Vector3.Cross(transform.forward, progress.forward).y);
                    float preferredSteerSide = recoverySteerSign == 0f ? preferredSide : recoverySteerSign;

                    // Barrier-awareness fix: the maneuver used to pick its turn
                    // side purely from heading alignment (cross product above),
                    // with no idea which side of the track it's actually stuck
                    // near - a car wedged against the right-hand barrier could
                    // still choose to reverse/turn further into that same
                    // barrier if heading alignment happened to favor that side.
                    // When genuinely close to an edge (using the same widened,
                    // hairpin-aware HalfWidthAt every other edge check in this
                    // file already uses), steer away from that edge specifically
                    // overrides the heading-based preference; a car stuck well
                    // clear of either edge (e.g. wedged against another car)
                    // keeps the original heading-based choice.
                    float localHalfWidth = track.HalfWidthAt(progress.distance);
                    if (localHalfWidth > 0.1f)
                    {
                        float edgeFraction = Mathf.Clamp01(Mathf.Abs(progress.lateralDistance) / localHalfWidth);
                        if (edgeFraction > 0.55f)
                        {
                            preferredSteerSide = -Mathf.Sign(progress.lateralDistance);
                        }
                    }

                    // Escalating recovery (stuck-recovery fix): a car genuinely
                    // wedged against a wall/kerb at a bad angle will often fail
                    // the same fixed-direction maneuver the same way every
                    // time - alternate the turn side on repeated attempts
                    // instead of repeating a losing move, and commit to a
                    // longer, more decisive maneuver each time rather than the
                    // same brief one that already didn't work. RaceManager's
                    // HandleStuckEscalation is still the final backstop if
                    // several varied attempts all fail.
                    bool alternateSide = participant.recoveryAttemptCount % 2 == 1;
                    maneuverTurnSide = alternateSide ? -preferredSteerSide : preferredSteerSide;
                    activeManeuver = facingWrongWay ? RecoveryManeuver.ReorientWrongWay : RecoveryManeuver.ReverseAway;
                    float attemptScale = 1f + Mathf.Min(participant.recoveryAttemptCount, 3) * 0.4f;
                    maneuverTimer = (facingWrongWay ? ReorientDuration : ReverseAwayDuration) * attemptScale;
                    GameLog.Info("[RaceControl] " + participant.driverName + " attempting " + activeManeuver + " recovery maneuver (stuck " + stuckDetectTimer.ToString("0.0") + "s, attempt #" + (participant.recoveryAttemptCount + 1) + ").");
                    vehicle.SetCommand(new VehicleCommand { reverseAssist = true, steer = maneuverTurnSide });
                    return;
                }
            }
            else
            {
                stuckDetectTimer = Mathf.Max(0f, stuckDetectTimer - Time.deltaTime * 2f);
            }

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

            // Part 8: small, bounded personality nudges layered on top of the raw
            // stats above - traits are derived from these same stats, so this
            // only sharpens an already-existing tendency rather than inventing a
            // new one. Never enough to override the underlying stat spread.
            if (driver != null)
            {
                List<string> traits = DriverTraits.Compute(driver);
                if (traits.Contains("Error-Prone")) consistency = Mathf.Max(5, consistency - 6);
                if (traits.Contains("Consistent Finisher")) consistency = Mathf.Min(99, consistency + 4);
                if (traits.Contains("Aggressive Overtaker")) overtaking = Mathf.Min(99, overtaking + 4);
                if (traits.Contains("Defensive Wall")) defending = Mathf.Min(99, defending + 4);
                if (traits.Contains("Tyre Saver")) tyreManagement = Mathf.Min(99, tyreManagement + 4);
                if (traits.Contains("Wet Specialist") && (raceManager.Track != null && (raceManager.Track.weather == WeatherState.LightRain || raceManager.Track.weather == WeatherState.HeavyRain)))
                {
                    wetSkill = Mathf.Min(99, wetSkill + 8);
                }
            }

            RaceManager.AiDifficultyProfile profile = raceManager.GetAiDifficultyProfile();
            // Part A: the single source of truth for every Expert-only branch below -
            // corner classification, corner-speed ceilings, traffic caution floor,
            // DRS commit and the overtake/defend RNG bypasses all read this once.
            bool isExpert = raceManager.IsExpertDifficulty;
            // Continuous 0-1 skill blend for corner-speed tuning specifically (see
            // CorneringSkillTier) - unlike isExpert above, Hard gets a genuine
            // partial share of the higher-confidence corner-speed curves instead
            // of none.
            float skillTier = CorneringSkillTier();

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
            FindUpcomingApex(progress.distance, speedKph, skillTier, out apexDistanceAhead, out apexSeverity);
            float turnSign = EstimateTurnDirection(progress.distance);

            // Real ceiling, not an invented ~330-350kph clamp: the same DRS/ERS-aware
            // number the player's own physics already computes every tick.
            float carTopSpeed = vehicle.CarData == null || vehicle.CarData.topSpeed <= 0 ? 337f : vehicle.CarData.topSpeed;
            // straightSpeedMultiplier is hard-clamped to <= 1.0 here: it may only ever
            // discount how much of the real ceiling this difficulty confidently uses,
            // never inflate a straight-line target past what the car can actually do.
            float straightTargetSpeed = (vehicle.TargetTopSpeedKph > 5f ? vehicle.TargetTopSpeedKph : carTopSpeed) * Mathf.Min(1f, profile.straightSpeedMultiplier);

            bool wet = track.weather == WeatherState.LightRain || track.weather == WeatherState.HeavyRain;
            // Tyre-difference pass: uses the compound-neutral condition multiplier
            // (temperature/wear/lockup only) instead of the full GripMultiplier - the
            // compound's own speed difference is applied separately and precisely via
            // CompoundSpeedOffsetKph below, so folding the compound-specific ratio in
            // here too would double-count the same tyre gap.
            float gripMultiplier = vehicle.Tyres.GripConditionMultiplier(track.weather);
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
            // Explicit ~50-75kph target for a genuine hairpin (a corner that's actually
            // close to a 180-degree turn - see the narrowed Hairpin band in
            // ClassifyUpcomingCorner). A merely-tight or very-tight corner is the
            // separate Slow/VeryTight buckets below and no longer derives from this
            // number at all.
            float carBrakingStat = vehicle.CarData == null ? 78f : vehicle.CarData.braking;
            float carCorneringStat = vehicle.CarData == null ? 78f : vehicle.CarData.cornering;
            float hairpinSpeedKph = Mathf.Lerp(50f, 75f, Mathf.Clamp01((carBrakingStat + carCorneringStat) / 200f));

            // Classify the upcoming apex by type rather than treating one continuous
            // severity number the same everywhere - a flowing high-speed kink and a
            // genuine hairpin need very different confidence curves (Part 2).
            float hairpinTurnAngle = MeasureHairpinTurnAngle(progress.distance + apexDistanceAhead);
            CornerType upcomingCornerType = ClassifyUpcomingCorner(apexSeverity, skillTier, hairpinTurnAngle);
            float compoundSpeedOffsetKph = vehicle.Tyres.CompoundSpeedOffsetKph(track.weather);
            float trueApexSpeed = EstimateApexSpeedForCornerType(upcomingCornerType, apexSeverity, straightTargetSpeed, hairpinSpeedKph, gripMultiplier, apexConfidence, skillTier, compoundSpeedOffsetKph);

            // Cornering buff round 8: profile.cornerSpeedMultiplier is no longer
            // applied here at all, for any corner type. It used to stack on top of
            // EstimateApexSpeedForCornerType's own skillTier-scaled floor/ceiling -
            // two independently difficulty-scaled multipliers compounding on the
            // same number, which is exactly what pushed apexTargetSpeed past the
            // car's own straight-line top speed and sent the AI straight into the
            // wall trying to carry an unachievable speed through a corner with real
            // curvature (see the HighSpeed/Medium cap in EstimateApexSpeedForCornerType).
            // skillTier alone now drives corner-speed difficulty scaling, in one
            // place, with a hard ceiling that can never exceed straightTargetSpeed.
            float apexTargetSpeed = Mathf.Lerp(trueApexSpeed * 0.5f, trueApexSpeed, apexConfidence);

            // Driver-quality variance is the per-driver pace differentiator, independent
            // of difficulty; profile.paceMultiplier is the difficulty-tier pace scaler
            // layered on top so Hard/Expert are meaningfully quicker than Easy/Medium.
            // Part A.8: racecraft's spread widened slightly (was 0.95-1.05) - the
            // thinnest driver-stat blend found in the verification pass.
            float driverPaceVariance = Mathf.Lerp(0.89f, 1.11f, pace / 100f) * Mathf.Lerp(0.92f, 1.08f, racecraft / 100f);
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
            UpdateOvertakeState(progress, severityHere, apexDistanceAhead, apexSeverity, turnSign, aggression, overtaking, defending, profile, isExpert, driver);

            // Off-track recovery: drive straight back toward the centerline at reduced pace
            // instead of chasing the racing line offset from the grass.
            // Uses the actual (possibly hairpin-widened) drivable half-width at this
            // point on track, not the flat field - otherwise AI would treat the extra
            // tarmac at a widened hairpin as off-track and brake/recover exactly where
            // the widening was meant to give it more room to work with.
            bool offTrack = Mathf.Abs(progress.lateralDistance) > track.HalfWidthAt(progress.distance) + 0.6f;
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
            float legalLimit = LegalOffsetLimit(severityHere, progress.distance);
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

            // Pit-entry fix: steer visibly toward the pit side under completely
            // normal driving well before the guided/kinematic entry phase can
            // ever trigger (RaceManager.BeginPitEntry, gated on track distance
            // alone) - without this the car drove the ordinary racing line
            // right up to that distance threshold and the guided system then
            // had to silently snap it sideways, reading exactly like "the pit
            // animation starts before the car is anywhere near pit entry".
            // Blended in only across the approach window and only while a pit
            // stop is actually requested and not yet underway, so a car simply
            // passing the pit entry on a normal lap never gets pulled off-line.
            if (participant.pitPhase == PitPhase.None && vehicle.PitRequested && track.IsInPitApproach(progress.normalized))
            {
                float approachBlend = Mathf.Clamp01((progress.normalized - 0.78f) / (0.955f - 0.78f));
                float pitApproachTargetLateral = track.HalfWidthAt(progress.distance) * 0.82f;
                requestedOffset = Mathf.Lerp(requestedOffset, pitApproachTargetLateral, approachBlend * approachBlend);
            }

            // Pit-exit early-turn fix: the mirror image of the pit-entry fix above.
            // A just-released car's normal line-following logic doesn't know it
            // just merged out of the pit lane - it targets the racing line/next
            // apex immediately, which read as "turns onto the track way too early"
            // even though the physical pit-exit ramp/barrier is still narrowing
            // back to the track edge. Holds the line near the pit-exit side
            // through the real ramp geometry (track.PitExitMergeBlend), fading out
            // smoothly as the car actually clears the merge instead of snapping
            // straight to the racing line. Gated on pitExitMergeActive (set at
            // release) rather than participant.pitLimiterUntilExit - that flag
            // clears on the shorter speed-limiter window, well before the physical
            // ramp actually finishes narrowing (see NotifyPitExitReleased for the
            // round-2 fix history).
            //
            // Round 3: PitExitMergeBlend's own window is a FIXED normalized
            // distance - it has no idea where the final corner's own curvature
            // actually ends, which varies per track/apex. On a corner whose real
            // barrier-hugging footprint runs longer than that fixed window, the
            // hold used to let go (blend reaching exactly 0) while the corner - and
            // its wall - was still very much in progress, and the sudden handoff to
            // normal apex-seeking right there is exactly what put cars into the
            // barrier "as soon as the limiter turned off".
            //
            // Round 4: round 3's extension used severityHere (EstimateCornerSeverity,
            // a short-window heading-change heuristic built for braking/apex
            // targeting) as a proxy for "is the barrier still there" - but that's a
            // different algorithm, with different thresholds/radius, than what
            // TrackManager's ComputeBarrierPlan actually uses to decide whether the
            // main edge barrier is still in tight-corner containment mode. The two
            // could (and did) disagree, so the hold could still release while a real
            // wall was still tight. Now queries track.IsNearTightFenceCorner, which
            // is baked directly from the exact same corner detection + radius/span
            // math the barrier builder itself uses - "the barrier has ended" now
            // means the literal same thing to both systems.
            if (pitExitMergeActive)
            {
                float exitBlend = track.PitExitMergeBlend(progress.normalized);
                bool stillInTightCorner = track.IsNearTightFenceCorner(progress.distance);
                if (exitBlend > 0f || stillInTightCorner)
                {
                    float pitExitHoldLateral = track.HalfWidthAt(progress.distance) * 0.82f;
                    float holdBlend = Mathf.Max(exitBlend, stillInTightCorner ? 0.75f : 0f);
                    requestedOffset = Mathf.Lerp(requestedOffset, pitExitHoldLateral, holdBlend * holdBlend);
                }
                else
                {
                    pitExitMergeActive = false;
                }
            }

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
            float legalTargetLimit = LegalOffsetLimit(severityHere, progress.distance);
            if (Mathf.Abs(targetProgress.lateralDistance) > legalTargetLimit)
            {
                track.SampleAtDistance(targetProgress.distance, out targetPoint, out forward, out right);
                targetPoint += right * Mathf.Clamp(targetProgress.lateralDistance, -legalTargetLimit, legalTargetLimit);
            }

            Vector3 toTarget = targetPoint - transform.position;
            float localSteer = Vector3.Dot(toTarget.normalized, transform.right);

            VehicleCommand command = new VehicleCommand();
            // Barrier-avoidance fix: this used to be a flat 0.45 correction
            // that only switched on in the last 1.2m before the true edge -
            // fine at low speed, but at real corner speed a car can cross
            // that whole 1.2m band in a couple of frames, leaving no time to
            // actually turn back before hitting whatever's just beyond it.
            // Now ramps in progressively from a wider margin (2.4m out) so
            // the correction is already building well before the car is
            // actually at risk, reaching a much stronger pull only right at
            // the limit.
            // Uses the local (possibly hairpin-widened) half-width so this correction
            // still keys off "how close to the true edge", not a stale flat distance
            // that would fire early and fight the extra room a widened hairpin exists
            // to give the AI in the first place.
            // Corner-speed pass: margin now widens a little with speed - raising
            // apex confidence through fast/medium corners means the car can be
            // carrying meaningfully more speed at the same point on track than
            // before, so the recovery pull needs to start building earlier to
            // still catch it in time.
            // Barrier-avoidance fix round 2: widened further (was 2.4-3.6) - the AI
            // now legitimately carries speed close to straight-line pace through
            // genuine fast corners, closing the reaction window this correction has
            // to actually catch a line error before the barrier. Starting further
            // out and reaching full strength sooner gives it more real time/distance
            // to correct at the speeds it's now carrying.
            // Barrier-avoidance fix round 3: widened again (was 3.2-5.2) and paired
            // with an actual emergency brake (edgeEmergencyBrake below, folded into
            // brakeDemand near the corner-braking block) - steering correction alone
            // has a hard physical ceiling (the shared turnRate/radius limit every
            // car has), so a car already carrying too much speed for the geometry
            // could not out-steer its way clear no matter how strong the correction
            // got. Speed is the one thing that always helps regardless of how tight
            // the turn radius needs to be, so this now genuinely brakes near the
            // edge too, not just steers.
            // Barrier-avoidance fix round 4: ceiling raised again and the scaling
            // window extended out to 340kph (was 6.8m ceiling reached by 280kph) -
            // the Slow corner-speed bucket now targets ~300-310kph, so the old curve
            // was already maxed out well before cars reached their actual tight-corner
            // speed, leaving no extra margin exactly when it was needed most.
            float edgeMarginDistance = Mathf.Lerp(4.2f, 9f, Mathf.Clamp01(speedKph / 340f));
            float edgeMargin = track.HalfWidthAt(progress.distance) - edgeMarginDistance;
            float edgeOvershoot = Mathf.Abs(progress.lateralDistance) - edgeMargin;
            float edgeProximity = Mathf.Clamp01(edgeOvershoot / edgeMarginDistance);
            float edgeRecovery = edgeOvershoot > 0f
                ? Mathf.Sign(-progress.lateralDistance) * Mathf.Lerp(0.3f, 1f, edgeProximity)
                : 0f;
            float edgeEmergencyBrake = edgeOvershoot > 0f ? Mathf.Lerp(0.1f, 0.9f, edgeProximity * edgeProximity) : 0f;
            command.steer = Mathf.Clamp(localSteer * 2.2f + edgeRecovery, -1f, 1f);

            // Real braking point: a kinematic stopping distance from current speed down
            // to the apex speed this driver is actually willing to carry, compared
            // against genuine remaining distance to the upcoming corner - not a single
            // blunt speed-delta formula with a fixed 55kph window.
            // Corner-speed fix: this deceleration assumption used to sit well below
            // what a confident driver would actually trust the brakes for, forcing
            // an early, gentle-looking brake zone regardless of how high the apex
            // target itself was - "brake less excessively" was as much about when/
            // how hard braking starts as it was about the apex number. Raised
            // across the board (later, more decisive braking for every difficulty)
            // and scaled further by skillTier so Hard/Expert genuinely trail the
            // brakes deeper into the zone instead of only reaching a higher target
            // speed at the same conservative brake point.
            // Entry-speed fix: pushed again (was 11.5-18 base / 1.0-1.15 skill-scaled)
            // so the brake zone itself starts later for the same apex target,
            // carrying more speed deeper into corner entry - this is a genuinely
            // separate lever from the apex target speed above (which is already
            // capped at straight-line pace), so it doesn't reopen the overspeed/
            // wall-crash bug that cap fixed.
            float brakingStat = vehicle.CarData == null ? 78f : vehicle.CarData.braking;
            float decelReference = Mathf.Lerp(13f, 21f, Mathf.Clamp01(brakingStat / 100f)) * Mathf.Lerp(1f, 1.22f, skillTier);
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

            // Barrier-avoidance fix round 3: the edge-proximity emergency brake
            // (computed alongside edgeRecovery above) always wins over whatever the
            // normal corner-braking model alone decided - it exists specifically for
            // the case where that model still let the car carry too much speed for
            // the actual geometry, so it must never be capped by it.
            brakeDemand = Mathf.Max(brakeDemand, edgeEmergencyBrake);

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

                // Fuel system pass: AI lift-and-coast - once the projected fuel
                // delta goes negative, back off the throttle target somewhat right
                // where it actually matters (nearCorner, the same braking-zone-
                // approach gate the brake-demand model above uses), mirroring the
                // player's own ApproachingBrakingZone-gated saving in
                // VehicleController.UpdateFuel. Severity scales with how far
                // negative the delta is, so a car only barely short barely lifts,
                // while a genuinely negative delta (e.g. an aggressive underfuel
                // plan that isn't paying off) lifts meaningfully harder.
                if (nearCorner && vehicle.ProjectedFuelDeltaLaps < -0.25f)
                {
                    float fuelSaveSeverity = Mathf.Clamp01((-vehicle.ProjectedFuelDeltaLaps - 0.25f) / 1f);
                    throttleTarget = Mathf.Min(throttleTarget, Mathf.Lerp(1f, 0.55f, fuelSaveSeverity));
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
                // Part A.7: Expert's pileup-safety cap shrunk further still (was
                // 1.1s/0.92) - just enough margin to not cause a first-corner pileup
                // by itself, no more.
                float openingCapDuration = confidentTier ? (isExpert ? 0.6f : 1.8f) : 3.5f;
                float openingCapFloor = confidentTier ? (isExpert ? 0.95f : 0.85f) : 0.72f;
                if (raceManager.RaceElapsed < openingCapDuration)
                {
                    command.throttle = Mathf.Min(command.throttle, Mathf.Lerp(openingCapFloor, 1f, raceManager.RaceElapsed / openingCapDuration));
                }
            }

            ApplyTrafficAvoidance(ref command, progress, speedKph, profile, isExpert);

            // Driver-pressure model: a car actively attacking or defending under
            // close pressure pushes slightly harder - the tyre lockup model already
            // reads brake/steer inputs, so this alone raises lockup risk exactly
            // when a real driver would be more likely to lock a wheel.
            if (pressureFactor > 0f)
            {
                command.brake = Mathf.Min(1f, command.brake * (1f + pressureFactor * 0.08f));
                command.steer = Mathf.Clamp(command.steer * (1f + pressureFactor * 0.06f), -1f, 1f);
            }

            // Part A.9: reacts a bit earlier than before (was Lerp(0.68, 0.52, ...)) -
            // the old threshold let strategy hang on for slightly too long before
            // requesting a stop.
            float tyrePitThreshold = Mathf.Lerp(0.72f, 0.58f, tyreManagement / 100f) + profile.tyreSavingBias * 0.05f;
            // Tyre-overextension fix (compound life): the threshold above was a flat
            // wear NUMBER applied identically to every compound. Wear itself already
            // decays faster on a Soft than a Hard (TyreState.baseWear), so a flat
            // number does track compound life somewhat - but a Soft genuinely falls
            // off a cliff once it crosses this zone and needs to come off sooner in
            // wear-number terms too, while a Hard can be run leaner without the same
            // cliff risk. Wets/Intermediates get the same early-side nudge as Softs -
            // they degrade unpredictably once badly worn and a mismatch (dried track,
            // rain fading) compounds fast.
            TyreCompound currentCompound = vehicle.Tyres.Compound;
            float compoundThresholdShift = currentCompound == TyreCompound.Soft ? 0.06f
                : currentCompound == TyreCompound.Hard ? -0.05f
                : (currentCompound == TyreCompound.Wet || currentCompound == TyreCompound.Intermediate) ? 0.04f
                : 0f;
            tyrePitThreshold = Mathf.Clamp(tyrePitThreshold + compoundThresholdShift, 0.2f, 0.8f);
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                vehicle.Tyres.Wear < tyrePitThreshold &&
                participant.lapTracker.CompletedLaps > 0)
            {
                command.pitRequest = true;
            }

            // Part A.9: never stay out on destroyed/near-destroyed tyres regardless of
            // planned lap or the threshold above - strategy timing should never keep a
            // car circulating on tyres that are essentially gone.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && vehicle.Tyres.Wear < 0.12f)
            {
                command.pitRequest = true;
            }

            // Tyre-overextension fix (pace-loss awareness): independent of the wear-
            // number threshold above, which a high-tyre-management driver can push
            // quite low - if the tyre's own real grip multiplier has genuinely
            // collapsed, force the stop regardless of plan. A car losing this much
            // grip is several seconds a lap off the pace and becomes a rolling
            // roadblock no matter what the pre-race strategy said.
            WeatherState currentWeather = raceManager.Track == null ? WeatherState.Clear : raceManager.Track.weather;
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                vehicle.Tyres.GripMultiplier(currentWeather) < 0.5f &&
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

            // Smarter AI strategy: jump a closely-followed rival that hasn't
            // stopped yet by taking this car's own pit window a lap or two early.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.ShouldAiPitForUndercut(participant))
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
                // Part A.2: Expert-only, deterministic - if DRS is legal, use it.
                drsCommittedThisZone = isExpert || Random.value < profile.drsUsageQuality;
            }
            wasDrsLegalLastFrame = drsLegal;
            command.drs = drsLegal && drsCommittedThisZone;

            ApplySafetyCarFollowing(ref command);

            // Green-flag handback ramp: for a couple of seconds right after
            // race-control autopilot lets go (see HandleRaceControlAutopilotReleased),
            // cap throttle and steering authority so the car eases back into full
            // racing behaviour instead of instantly snapping to whatever the
            // overtake/defend/traffic logic above just decided - even with a
            // freshly-resynced progress reference, a hard instant commit right at
            // a restart is exactly the kind of snap that reads as unnatural.
            if (handbackRampTimer > 0f)
            {
                handbackRampTimer -= Time.deltaTime;
                float rampBlend = 1f - Mathf.Clamp01(handbackRampTimer / HandbackRampDuration);
                command.throttle = Mathf.Min(command.throttle, Mathf.Lerp(0.55f, 1f, rampBlend));
                float steerCap = Mathf.Lerp(0.7f, 1f, rampBlend);
                command.steer = Mathf.Clamp(command.steer, -steerCap, steerCap);
            }

            vehicle.SetCommand(command);
        }

        // Race-control autopilot has just handed control back - see the call site
        // in Update() for why this specific transition (isRaceControlAutopilot
        // true -> false) is the one moment this controller can't trust its own
        // cached driving state. Resyncs the track-progress reference from the
        // car's real current position and clears every piece of transient
        // attack/defend/avoidance state so the car resumes normal driving
        // cleanly on the racing line instead of lunging off stale data.
        void HandleRaceControlAutopilotReleased()
        {
            hasProgressReference = false;
            overtakeState = OvertakeState.Following;
            overtakeStateTimer = 0f;
            attackSide = preferredSide;
            aggressionOffset = 0f;
            mistakeSteer = 0f;
            mistakeTimer = Random.Range(3f, 8f);
            hasCoveredThisApex = false;
            pressureFactor = 0f;
            followingTimer = 0f;
            dodgeMemoryTimer = 0f;
            dodgeMemorySide = 0f;
            drsCommittedThisZone = false;
            wasDrsLegalLastFrame = false;
            currentThrottle = 0f;
            previousSeverityHere = 0f;
            handbackRampTimer = HandbackRampDuration;
            // Any forced reposition (safety car handback, stuck-recovery teleport)
            // invalidates whatever pit-exit merge was in progress just as much as
            // it invalidates the rest of this transient state - clear it here so a
            // stale pitExitMergeActive can't linger and pull the car sideways well
            // after it's actually relevant. The real pit-release path re-arms it
            // via NotifyPitExitReleased right after calling this.
            pitExitMergeActive = false;
        }

        // Stuck-recovery escalation fix: called by RaceManager right after it
        // force-repositions a genuinely stuck car (see
        // RaceManager.HandleStuckEscalation) - without this, the exact same
        // stale-progress-reference bug the safety-car restart fix addressed
        // above would recur here too, since a hard teleport invalidates
        // lastProgressDistance just as much as a long SC period does. Shares
        // the same reset plus a short handback-style ramp so the car eases
        // back into racing instead of instantly committing to whatever the
        // overtake/defend state machine last decided before it got stuck.
        public void ResyncAfterForcedReposition()
        {
            HandleRaceControlAutopilotReleased();
            activeManeuver = RecoveryManeuver.None;
            stuckDetectTimer = 0f;
        }

        // Pit-exit early-turn fix round 2: participant.pitLimiterUntilExit (the
        // speed-limiter flag) used to double as the steering-line-hold gate too,
        // but it deliberately clears early (IsInPitExitLimiterZone's short
        // speed-cap window) - well before the physical pit-exit ramp geometry
        // actually finishes narrowing back to the track edge
        // (TrackRuntime.PitExitRampEndNormalized). Reusing it meant the AI's line
        // hold let go and handed back to completely normal racing-line targeting
        // while the ramp was still narrowing, which is exactly the "turns onto
        // the track too early" bug. This flag is set once, right at release (see
        // RaceManager.UpdatePitRelease), and clears itself in Update() only once
        // track.PitExitMergeBlend actually reaches 0 - i.e. once the car has
        // genuinely cleared the real ramp - instead of on any fixed timer or the
        // shorter speed-limiter window.
        bool pitExitMergeActive;

        public void NotifyPitExitReleased()
        {
            pitExitMergeActive = true;
        }

        // Part 1: the real safety car isn't a RaceParticipant, so it never shows
        // up in ApplyTrafficAvoidance's loop over raceManager.Participants above -
        // this gives it the same "brake and back off the throttle as it gets
        // close" treatment as a real car directly ahead, so the queue actually
        // forms behind it instead of AI cars only being pace-capped in the
        // abstract while treating the visible car itself as empty air.
        // Queue shape: hold a ~14m gap to the safety car, matching its speed
        // once settled, with controlled braking (not a stab) when closing fast.
        void ApplySafetyCarFollowing(ref VehicleCommand command)
        {
            Transform safetyCar = raceManager.SafetyCarTransform;
            if (safetyCar == null)
            {
                return;
            }

            Vector3 local = transform.InverseTransformPoint(safetyCar.position);
            if (local.z <= 0f || local.z > 90f || Mathf.Abs(local.x) > 7f)
            {
                return;
            }

            const float targetGap = 14f;
            float mySpeedKph = Mathf.Abs(vehicle.CurrentSpeedKph);
            float scSpeedKph = raceManager.SafetyCarCurrentSpeedKph;
            float closingKph = Mathf.Max(0f, mySpeedKph - scSpeedKph);

            // Brake earlier the faster the gap is actually shrinking, so a car
            // arriving at 250kph starts shedding speed well before the queue.
            float timeToContact = (local.z - targetGap) / Mathf.Max(1.5f, closingKph / 3.6f);
            if (timeToContact < 3f && closingKph > 5f)
            {
                float urgency = Mathf.Clamp01(1f - timeToContact / 3f);
                command.brake = Mathf.Max(command.brake, Mathf.Lerp(0.1f, 0.9f, urgency * urgency));
            }

            if (local.z < targetGap)
            {
                command.brake = Mathf.Max(command.brake, 0.6f);
                command.throttle = 0f;
            }
            else if (local.z < targetGap * 2.5f)
            {
                // Settled in the queue: hold station by matching the safety
                // car's speed instead of oscillating between full throttle and
                // panic braking.
                if (mySpeedKph > scSpeedKph + 2f)
                {
                    command.throttle = 0f;
                }
                else
                {
                    command.throttle = Mathf.Min(command.throttle, 0.45f);
                }
            }
            else
            {
                float closeness = Mathf.Clamp01(1f - local.z / 90f);
                command.throttle = Mathf.Min(command.throttle, Mathf.Lerp(1f, 0.3f, closeness));
            }
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

        void ApplyTrafficAvoidance(ref VehicleCommand command, TrackProgress progress, float speedKph, RaceManager.AiDifficultyProfile profile, bool isExpert)
        {
            float brakeDemand = 0f;
            float throttleLimit = 1f;
            float steerAdjust = 0f;
            bool blockedLeft = false;
            bool blockedRight = false;
            bool carDirectlyAhead = false;
            // Bug fix (Part A.6): the old fixed 0.5 floor here silently clamped
            // Expert's much lower trafficAvoidanceCaution (0.22) right back up to 0.5,
            // neutering the difficulty profile's own number. Expert gets its own,
            // much lower, floor plus an additional reduction on top so a confident
            // Expert actually commits to a real gap instead of lifting for traffic it
            // can beat.
            // Collision-reduction pass: Expert's caution discount was strong enough
            // that it committed to gaps other tiers correctly judged too tight,
            // which read as constant contact rather than clean hard racing. Pulled
            // back from *0.7 to *0.8 - still clearly the most committed tier, just
            // with a genuine defensive margin left over instead of none.
            // Car-avoidance fix: Expert's floor/discount pulled back further (was
            // 0.18 floor / *0.8 discount) - "committing to a real gap" was still
            // reading as running into the player/other cars, not clean hard racing.
            // Car-avoidance fix round 2: floors raised again (was 0.26/0.5) across
            // every difficulty - cars were still making contact more than clean
            // racing should allow, so the baseline caution every tier starts from
            // is genuinely higher, not just Expert's discount being smaller.
            float cautionFloor = isExpert ? 0.34f : 0.6f;
            float cautionFactor = Mathf.Clamp(profile.trafficAvoidanceCaution, cautionFloor, 1.4f);
            if (isExpert)
            {
                cautionFactor *= 0.9f;
            }
            bool legalDrsHere = raceManager.IsDrsAvailable(participant);

            dodgeMemoryTimer = Mathf.Max(0f, dodgeMemoryTimer - Time.deltaTime);

            // Car-avoidance fix: widened (was 18-52) so anticipation starts earlier
            // at every speed, giving more real time to react before a car ahead
            // becomes an actual collision instead of a late dodge/brake.
            // Car-avoidance fix round 2: widened again (was 24-64) - the higher
            // corner-exit and straight-line speeds cars now carry closed the
            // reaction window this detection range gives before contact.
            float forwardWindow = Mathf.Lerp(30f, 76f, Mathf.Clamp01(speedKph / 320f));

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
                    // Car-avoidance fix: threshold/gate widened (was 2.4s / 3.2m) and
                    // the brake/throttle response curves strengthened (was 0.12-0.95 /
                    // 0.85-0.15) - closing cars were still reaching contact before this
                    // reacted hard enough.
                    // Car-avoidance fix round 2: threshold/gate widened again (was
                    // 3.0s / 3.8m) and the brake/throttle response curves strengthened
                    // further (was 0.2-1 / 0.8-0.05) - reacts earlier and harder to a
                    // genuinely closing gap.
                    float timeToContact = local.z / Mathf.Max(1.5f, closingKph / 3.6f);
                    if (timeToContact < 3.6f && absX < 4.2f)
                    {
                        float urgency = Mathf.Clamp01(1f - timeToContact / 3.6f);
                        brakeDemand = Mathf.Max(brakeDemand, Mathf.Lerp(0.3f, 1f, urgency * urgency) * overlap);
                        throttleLimit = Mathf.Min(throttleLimit, Mathf.Lerp(0.72f, 0.05f, urgency));
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
                    if (absX < 3.0f && local.z < forwardWindow * 0.7f)
                    {
                        carDirectlyAhead = true;
                        float rawDodgeSide = Mathf.Abs(local.x) < 0.4f ? preferredSide : -Mathf.Sign(local.x);
                        if (dodgeMemoryTimer <= 0f)
                        {
                            dodgeMemorySide = rawDodgeSide;
                        }
                        // Part A.6: Expert resolves a dodge decision faster instead of
                        // lingering on a stale side-choice that's no longer relevant.
                        dodgeMemoryTimer = isExpert ? 0.6f : 1.1f;
                        float dodgeStrength = Mathf.Clamp01(1f - local.z / (forwardWindow * 0.7f));
                        // Car-avoidance fix: dodge commitment strengthened (was 0.08-0.4).
                        // Car-avoidance fix round 2: strengthened again (was 0.12-0.5).
                        steerAdjust += dodgeMemorySide * Mathf.Lerp(0.18f, 0.66f, dodgeStrength);
                    }
                }

                // Side-by-side: never steer into the car alongside, and remember
                // which flanks are occupied so we don't dodge into a sandwich.
                // Car-avoidance fix: detection window widened again (was 6.5/4.2)
                // and the push-away response strengthened further (was 0.06-0.34 /
                // 4.8 divisor / 1-0.6 cutback) - side-by-side pairs were still
                // converging into contact before this resolved them.
                // Car-avoidance fix round 2: detection window widened again (was
                // 7.5/5.4) and the push-away response strengthened further (was
                // 0.1-0.48 / 5.4 divisor) - catches a converging side-by-side pair
                // earlier and pushes apart harder once detected.
                if (Mathf.Abs(local.z) < 8.5f && absX < 6.2f)
                {
                    if (local.x < 0f)
                    {
                        blockedLeft = true;
                    }
                    else
                    {
                        blockedRight = true;
                    }

                    float sideOverlap = Mathf.Clamp01(1f - absX / 6.2f);
                    steerAdjust += -Mathf.Sign(local.x) * Mathf.Lerp(0.14f, 0.6f, sideOverlap);
                    float sideCutback = Mathf.Clamp01(1f - (1f - Mathf.Lerp(1f, 0.5f, sideOverlap)) * cautionFactor);
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
        void FindUpcomingApex(float fromDistance, float speedKph, float skillTier, out float apexDistanceAhead, out float apexSeverity)
        {
            apexDistanceAhead = 400f;
            apexSeverity = 0f;
            const float step = 20f;
            // Corner-speed fix: 180m was shorter than the kinematic braking distance
            // a fast car can need for a slow corner (well over 200m from high top
            // speed down to a hairpin), so the corner sometimes wasn't detected
            // until already inside its own required braking zone - the AI then had
            // to stab the brakes hard the instant it appeared, which reads as
            // panicked/overcautious even though the apex speed target itself was
            // fine. A longer lookahead lets the braking-point model commit to a
            // later, smoother, more confident brake instead of reacting last-second.
            // Corner-speed pass 3: now also scales with the car's OWN current
            // speed - a car doing 320 km/h needs to "see" a corner much further
            // out than one doing 140 km/h for the braking-point model to ever
            // have a chance at a late, confident brake instead of a panicked
            // one, and a fixed 260m regardless of speed under-served exactly
            // the fastest, most important corners.
            // Corner-speed pass 4: ceiling widened further still and scaled up
            // for higher skill tiers - a sharper driver reads the track further
            // ahead (better anticipation, not better eyesight), which is what
            // lets Hard/Expert commit to the later, flatter braking points their
            // raised apex-speed floors above now expect without ever having to
            // react to a corner that "appeared" late and panic-brake for it.
            // Cornering buff round 5: lookahead ceiling raised again (was 340/400) -
            // Hard/Expert's later braking points and higher apex floors below need
            // correspondingly earlier, more confident corner detection to never read
            // as a late "surprise" panic-brake.
            float maxLookahead = Mathf.Lerp(220f, Mathf.Lerp(365f, 440f, skillTier), Mathf.Clamp01(speedKph / 320f));
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
        void UpdateOvertakeState(TrackProgress progress, float severityHere, float apexDistanceAhead, float apexSeverity, float turnSign, int aggression, int overtaking, int defending, RaceManager.AiDifficultyProfile profile, bool isExpert, DriverData driver)
        {
            RaceParticipant ahead = raceManager.FindCarAhead(participant, 46f);
            RaceParticipant behind = raceManager.FindCarBehind(participant, 32f);
            float legalLimit = LegalOffsetLimit(severityHere, progress.distance);
            float commitment = Mathf.Clamp01(profile.overtakeCommitment * Mathf.Lerp(0.7f, 1.15f, (aggression + overtaking) / 200f));
            float defendCommitment = Mathf.Clamp01(profile.defendCommitment * Mathf.Lerp(0.7f, 1.15f, defending / 100f));

            // Part A.3/A.4: extended state timers so Expert doesn't bail out of an
            // attack or a defend cover early. Everything else keeps the previous
            // fixed durations.
            float preparingAttackTimer = isExpert ? 3.2f : 2.2f;
            float attackingTimer = isExpert ? 4.2f : 2.6f;
            float sideBySideTimer = isExpert ? 4.5f : 3f;
            float backingOutTimer = isExpert ? 0.4f : 1f;

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

            // No overtaking under a safety car / VSC / restart hold or in a locally
            // yellow-flagged sector - decided by RaceManager's single legality
            // helper so the AI's own restraint, the player's enforcement and the
            // penalty detector can never disagree about what was allowed. Any
            // attempt already under way is aborted cleanly back to a single lane
            // instead of snapping straight. Order correction (a retired/pitting/
            // recovering/crawling car ahead) is the one exception, checked
            // per-target below.
            bool overtakingAllowedHere = !raceManager.IsOvertakingRestrictedForParticipant(participant);
            if (!overtakingAllowedHere && overtakeState != OvertakeState.Following && overtakeState != OvertakeState.BackingOut && overtakeState != OvertakeState.CompletingPass)
            {
                overtakeState = OvertakeState.BackingOut;
                overtakeStateTimer = 0.6f;
            }

            // Pit entry/exit + pit-limiter fix: no attack attempt should ever be
            // initiated or continued while either car is on the physical pit
            // entry/exit stretch (the road splits/narrows there) or under a pit
            // limiter speed cap (a limiter car has no business defending its
            // position, and attacking one is a free, riskless pass rather than a
            // real overtake). Mirrors the yellow-flag abort immediately above -
            // any live attempt is aborted cleanly back to a single lane.
            bool pitZoneNearby = track.IsInPitApproach(progress.normalized) || track.IsInPitExitLimiterZone(progress.normalized);
            bool eitherUnderPitLimiter = vehicle.PitLimiterActive || (ahead != null && ahead.vehicle != null && ahead.vehicle.PitLimiterActive);
            bool suppressAttackManeuvers = pitZoneNearby || eitherUnderPitLimiter;
            if (suppressAttackManeuvers && overtakeState != OvertakeState.Following && overtakeState != OvertakeState.BackingOut && overtakeState != OvertakeState.CompletingPass)
            {
                overtakeState = OvertakeState.BackingOut;
                overtakeStateTimer = 0.6f;
            }

            // Commitment zones: once genuinely close to an upcoming corner's
            // braking point, no NEW attack or defensive cover maneuver may be
            // initiated - the line is locked for the corner rather than the AI
            // swerving to start (or abandon-and-restart) a pass while it should
            // be focused on the braking/turn-in itself. Attempts already under
            // way before the zone was entered are left alone (their own timers/
            // MoveTowards easing already hold a stable line); this only gates
            // the decision points that pick a brand new line.
            bool inCornerCommitmentZone = apexDistanceAhead < 35f && apexSeverity > 0.18f;

            overtakeStateTimer -= Time.deltaTime;
            pressureFactor = 0f;

            switch (overtakeState)
            {
                case OvertakeState.Following:
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, 0f, Time.deltaTime * 4f);
                    if (ahead != null && ahead.vehicle != null && raceManager.CanParticipantOvertake(participant, ahead))
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

                        // Part A.3: Expert alone can trigger an attack on a genuine
                        // positive speed delta by itself, without needing DRS, a
                        // braking zone or the wider "clearly slower" margin above.
                        // Slight nerf pass: needs a bit more than a token 0.5kph
                        // edge before committing off this alone.
                        bool positiveSpeedDeltaExpert = isExpert && speedDeltaKph > 1.4f;

                        // Part A.3: a clearly-slower backmarker (large pace gap) gets
                        // attacked almost immediately on Expert - widen the gap
                        // threshold below rather than waiting for the normal window.
                        DriverData aheadDriver = ahead.driverData;
                        bool aheadIsBackmarker = isExpert && driver != null && aheadDriver != null && (driver.pace - aheadDriver.pace) > 8;

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

                        // Part A.3: Expert's attack-trigger gap threshold is far wider
                        // than the other tiers, which keep the original threshold
                        // untouched. Slight nerf pass: trimmed back a bit from
                        // 1.8s/3.0s so Expert doesn't launch attacks from quite as
                        // far back as before.
                        float attackGapThreshold = isExpert ? (aheadIsBackmarker ? 2.6f : 1.6f) : 1.1f;
                        bool attackTrigger = gapSeconds < attackGapThreshold && (approachingBrakeZone || drsHelp || clearlySlower || positiveSpeedDeltaExpert) && hasPace;

                        // Part A.2: Expert is fully deterministic once attackTrigger is
                        // true - no dice roll for permission to attack.
                        if (attackTrigger && !suppressAttackManeuvers && !inCornerCommitmentZone && (isExpert || Random.value < commitment * Time.deltaTime * (3f + patienceBonus) * drsBonus))
                        {
                            overtakeState = OvertakeState.PreparingAttack;
                            overtakeStateTimer = preparingAttackTimer;
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
                        // Part A.2: Expert commits to the attack whenever the target is
                        // still there, no roll.
                        if (stillThere && (isExpert || Random.value < commitment))
                        {
                            overtakeState = attackSide < 0f ? OvertakeState.AttackingOutside : OvertakeState.AttackingInside;
                            overtakeStateTimer = attackingTimer;
                        }
                        else
                        {
                            overtakeState = OvertakeState.BackingOut;
                            overtakeStateTimer = backingOutTimer;
                        }
                    }
                    break;
                }

                case OvertakeState.AttackingInside:
                case OvertakeState.AttackingOutside:
                {
                    pressureFactor = Mathf.Lerp(0.4f, 1f, commitment);
                    float attackOffset = attackSide * Mathf.Lerp(2f, legalLimit, commitment);
                    // Collision-reduction pass: don't keep committing further toward
                    // attackOffset once genuinely close alongside the car being
                    // attacked - ApplyTrafficAvoidance's own steer nudge is a small
                    // additive correction, not strong enough by itself to stop a car
                    // that's still actively steering toward its full attack offset
                    // from closing the last half-metre into contact. Capping the
                    // target at the current aggressionOffset once the real lateral
                    // gap is dangerously tight is what actually backs the car out of
                    // a bad situation instead of only reacting after the gap (in
                    // seconds, not metres) has already closed.
                    if (ahead != null)
                    {
                        Vector3 aheadLocal = transform.InverseTransformPoint(ahead.transform.position);
                        bool genuinelyAlongside = Mathf.Abs(aheadLocal.z) < 9f;
                        bool dangerouslyClose = Mathf.Abs(aheadLocal.x) < 1.7f;
                        if (genuinelyAlongside && dangerouslyClose)
                        {
                            attackOffset = Mathf.Clamp(aggressionOffset, -legalLimit, legalLimit);
                        }
                    }

                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, Mathf.Clamp(attackOffset, -legalLimit, legalLimit), Time.deltaTime * 6.5f);
                    bool sideBySideNow = ahead != null && Mathf.Abs(transform.InverseTransformPoint(ahead.transform.position).z) < 6f;
                    if (sideBySideNow)
                    {
                        overtakeState = OvertakeState.SideBySide;
                        overtakeStateTimer = sideBySideTimer;
                    }
                    else if (ahead == null)
                    {
                        overtakeState = OvertakeState.CompletingPass;
                        overtakeStateTimer = 1.4f;
                        raceManager.ReportAiOvertakeCompleted(participant);
                    }
                    else
                    {
                        // Part A.3: Expert only bails into BackingOut when the gap has
                        // genuinely opened back up (a wider threshold than the other
                        // tiers); if only the attack-state timer expired and the gap
                        // hasn't opened, Expert refreshes the timer and keeps pressing
                        // instead of giving up a still-live attack. This is not
                        // repeated weaving - aggressionOffset above is unchanged, this
                        // only decides whether to keep holding the current line.
                        float currentGap = raceManager.GetIntervalToAheadSeconds(participant);
                        float abortGapThreshold = isExpert ? 2.6f : 1.8f;
                        bool gapOpening = currentGap > abortGapThreshold;
                        if (gapOpening || overtakeStateTimer <= 0f)
                        {
                            if (isExpert && !gapOpening && overtakeStateTimer <= 0f)
                            {
                                overtakeStateTimer = attackingTimer;
                            }
                            else
                            {
                                overtakeState = OvertakeState.BackingOut;
                                overtakeStateTimer = backingOutTimer;
                            }
                        }
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
                        overtakeStateTimer = backingOutTimer * 0.8f;
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
                    // Higher overtakeCommitment backs out less readily/later; Expert's
                    // own backingOutTimer above is also much shorter to begin with.
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, 0f, Time.deltaTime * Mathf.Lerp(3f, 6f, 1f - commitment));
                    if (overtakeStateTimer <= 0f)
                    {
                        overtakeState = OvertakeState.Following;
                    }
                    break;
            }

            // Defend once per approaching braking zone: cover the inside line if a
            // real threat is close behind, then leave it alone until the next corner
            // instead of weaving repeatedly. Part A.4: Expert covers from much
            // further out than the other tiers - earlier inside-cover before the
            // braking zone - while still only ever committing once per zone.
            // Slight nerf pass: pulled in from 110m to 95m.
            float approachTriggerDistance = isExpert ? 95f : 70f;
            bool approaching = apexDistanceAhead < approachTriggerDistance && apexSeverity > 0.16f;
            if (!approaching)
            {
                hasCoveredThisApex = false;
            }
            else if (overtakeState == OvertakeState.Following && !hasCoveredThisApex && !suppressAttackManeuvers && !inCornerCommitmentZone && behind != null && behind.vehicle != null)
            {
                float behindGap = raceManager.GetIntervalToAheadSeconds(behind);
                bool behindHasDrs = raceManager.IsDrsAvailable(behind);
                bool threatClose = behindGap > 0f && (behindGap < 1.3f || behindHasDrs);
                // Part A.2: Expert commits to the cover whenever a real threat is
                // close, no roll.
                if (threatClose && (isExpert || Random.value < defendCommitment))
                {
                    // Part A.4: a stronger cover offset ceiling for Expert specifically
                    // (clamped by legalLimit like every other tier, same as before).
                    float coverCeiling = isExpert ? 2.7f : 2.3f;
                    float coverOffset = turnSign * Mathf.Lerp(1f, coverCeiling, defendCommitment);
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, Mathf.Clamp(coverOffset, -legalLimit, legalLimit), Time.deltaTime * 5f);
                    hasCoveredThisApex = true;
                    pressureFactor = Mathf.Max(pressureFactor, Mathf.Lerp(0.3f, 0.8f, defendCommitment));
                }
            }
        }

        float ConstrainLegalLineOffset(TrackProgress progress, float requestedOffset, float cornerSeverity)
        {
            float legalLimit = LegalOffsetLimit(cornerSeverity, progress.distance);
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

            if (Mathf.Abs(progress.lateralDistance) > track.HalfWidthAt(progress.distance) - 1.6f)
            {
                desired = Mathf.MoveTowards(desired, 0f, Mathf.Lerp(2.2f, 5.2f, cornerSeverity));
            }

            return desired;
        }

        float LegalOffsetLimit(float cornerSeverity, float distance)
        {
            float margin = Mathf.Lerp(1.8f, 3.1f, cornerSeverity);
            float localHalfWidth = track.HalfWidthAt(distance);
            float kerbLimit = track.kerbStart > 0f ? track.kerbStart - 0.8f : localHalfWidth - margin;
            return Mathf.Max(0.75f, Mathf.Min(localHalfWidth - margin, kerbLimit));
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
