using System.Collections.Generic;
using F1Game.Race.Rules;
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
        // A "real" mistake window: while > 0 the driver has misjudged the next
        // braking zone (see UpdateMistake / the brakingApexSpeed consumer) and
        // will arrive at the corner genuinely hot instead of just wobbling the
        // line inside the legal-offset corridor.
        float mistakeBrakeErrorTimer;
        float mistakeBrakeErrorSeverity;
        // Continuous seconds spent at walking pace under green racing
        // conditions - the wedged-in-traffic detector for the forced unstick
        // crawl in ApplyTrafficAvoidance.
        float stuckInTrafficSeconds;
        // Written by ApplyTrafficAvoidance each tick (read one frame stale by
        // the line-pursuit code, which runs earlier in the frame): is there a
        // car close ahead or alongside? Racing-line convergence is suspended
        // in traffic - see the lineTrafficBlend comment at the pursuit site.
        bool lineTrafficNearby;
        // 0 = clear air (pursue the optimal line), 1 = in traffic (hold the
        // current lane). Smoothed so a neighbour blinking in and out of the
        // detection windows can never snap the steering target.
        float lineTrafficBlend;
        // Latched holding lane for the in-traffic case ([SwerveDiag amplitude]:
        // with the drawn line calmed, the residual straight-line swerve was the
        // traffic hold itself - laneHold used to be the LIVE lateral position, so
        // the target was always exactly where the car already was, i.e. zero
        // centring force. Any drift then persisted and grew, bounded only by
        // wall-aversion bouncing the car off each edge - a pack-wide wall-to-wall
        // free-drift on the straights). Latch a STABLE lane on entering traffic
        // (clamped so it can never latch onto the wall) and hold that, so pursuit
        // actually centres the car on a fixed lane; each car keeps its own lane, so
        // the field separation this hold exists to provide is preserved.
        float heldLaneOffset;
        bool hasHeldLane;
        // Rate-limited NET lateral target for the unified straight-line governor
        // (see where requestedOffset is finalised). Kept as controller state so a
        // car eases across the road once rather than snapping between whatever the
        // line/lane/overtake inputs happen to sum to frame-to-frame.
        float smoothedNetOffset;
        // Sustained clear-air time, so the held lane is STICKY: it is only dropped
        // (and thus re-latched to a fresh live position next time traffic appears)
        // after the car has been in genuine clear air for a while. Without this the
        // lane re-latched every time the traffic blend briefly dipped in a dense
        // pack, capturing wherever the car happened to be - a wall-ward, mid-swing
        // position - which flipped the target side to side and drove the big
        // straight-line lineBias weave the [SwerveDiag amplitude] pass reported.
        float laneClearAirTimer;
        // [WallDiag] black box (per report - "write code to diagnose it
        // properly"): the complete lateral-control state, captured every frame
        // at command-dispatch time, so a wall impact can print exactly what
        // the controller was commanding at the moment of the crash instead of
        // anyone inferring it after the fact.
        float bbFinalSteer;
        float bbEdgeRecovery;
        float bbAuthorityComp;
        float bbLineBias;
        float bbSeverityHere;

        public string DescribeLateralState()
        {
            return "state=" + overtakeState +
                   " steer=" + bbFinalSteer.ToString("0.00") +
                   " comp=" + bbAuthorityComp.ToString("0.0") +
                   " edgeRec=" + bbEdgeRecovery.ToString("0.00") +
                   " lineBias=" + bbLineBias.ToString("0.00") +
                   " agg=" + aggressionOffset.ToString("0.00") +
                   " adjust=" + smoothedSteerAdjust.ToString("0.00") +
                   " heldLane=" + (hasHeldLane ? heldLaneOffset.ToString("0.0") : "-") +
                   " blend=" + lineTrafficBlend.ToString("0.00") +
                   " netOff=" + smoothedNetOffset.ToString("0.0") +
                   " sev=" + bbSeverityHere.ToString("0.00");
        }

        // Hysteresis for the traffic gate itself (see the lineTrafficNearby
        // assignment): holds the gate engaged until the road has been
        // continuously clear for a full second, so a neighbour flickering
        // across the detection edge can't re-blend the lateral target every
        // few frames.
        float lineTrafficReleaseTimer;
        // Previous-frame lateral offset, for the wall-aversion system's
        // trajectory (time-to-edge) computation.
        float previousEdgeLateral;
        bool hasEdgeLateralRef;
        // Low-passed lateral drift rate for the same system ([WallDiag] black
        // box round: edgeRec sat at max urgency even 1-2m inside the edge at
        // sev=0.00 - the raw per-frame derivative spikes on contact jolts and
        // on progress-reference snaps between overlapping crossing legs, and
        // one spiked frame read as "wall in <0.2s" = instant full-urgency
        // save. The filtered rate only reads urgent for SUSTAINED drift).
        float smoothedEdgeLateralRate;
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
        // Extended per request ("AI shouldn't immediately look for the line once
        // it starts, to avoid accidents") - was 7s with an 85%-strength blend that
        // was already mostly back on the racing line (and its tight apex swings)
        // within a couple of seconds. Now matches the traffic-caution race-start
        // window (12s) so the two systems agree on how long "just after the
        // start" lasts, and the hold is full-strength for the first stretch
        // before easing off - see the fan-blend curve below.
        const float OpeningFanDuration = 12f;

        // Pit-entry target look-ahead, kept short and dedicated - the normal
        // racing-line lookahead (22-62m, speed/severity scaled) is tuned for
        // reading corners far down the track, not for tracking a ~210m-long ramp
        // whose lateral envelope changes meaningfully over a much shorter span.
        const float PitEntryLookAheadMeters = 18f;

        // Pit-entry look-ahead fix: builds a dedicated world-space pit-entry
        // target - position AND lateral sampled together at the SAME distance,
        // using the canonical ramp/track geometry - instead of grafting a
        // lateral computed at the car's current distance onto the ordinary
        // racing-line lookahead point (sampled further down the track). Never
        // looks past the real physical opening (PitCorridorStartNormalized)
        // while the car is still in PitPhase.None - once it has genuinely
        // entered PitPhase.Entry, RaceManager's own guided kinematic sequence
        // (UpdatePitEntry) takes over movement entirely and this is no longer
        // consulted. Delegates to TrackRuntime.ComputePitEntryTargetPoint, the
        // single shared implementation RaceManager's player pit-entry assist
        // now also uses, so the two can never diverge.
        Vector3 ComputePitEntryTargetPoint(TrackProgress fromProgress)
        {
            Vector3 pitTargetPoint;
            Quaternion pitTargetRotation;
            // Speed-scaled lookahead (per report - "I'm no longer crashing into
            // the barriers while pitting but the AI are"): the exact bug the
            // player assist just had, inherited. The pre-window braking
            // envelope means an AI can now legitimately be at racing speed
            // during the early approach while committingToPit steering is
            // already active - and aiming at a point a fixed 18m ahead at
            // 300+ kph makes the pursuit steering violently under-damped: a
            // small lateral error becomes a huge target angle, the wheel
            // chases it, overshoots, and the oscillation ends on the wall.
            // Mirrors RaceManager.BuildPitEntryAssistCommand's fix: 18m at
            // ramp-tracking speeds, growing to 55m at racing speed.
            float lookAhead = Mathf.Lerp(PitEntryLookAheadMeters, 55f, Mathf.Clamp01(Mathf.Abs(vehicle.CurrentSpeedKph) / 300f));
            // Speed-conditioned pre-positioning, identical to the player assist
            // (see ComputePitEntryTargetPoint's blend param): hold the safe
            // centre while still at racing pace, slot into the edge lane as
            // the envelope brings the speed down.
            // Low-grip pit lockout fix (mirrors the player assist - see
            // RaceManager.BuildPitEntryAssistCommand): the speed-only blend is
            // zero above 280kph, so a car that cannot brake below 280 by the
            // ramp (cold/wet slicks, 0% wear) holds the road centre, never
            // reaches the pit-side edge, and never satisfies IsOnPitEntryRamp -
            // locked out of its stop. Once physically at the ramp mouth, commit
            // to the edge lane regardless of speed.
            float speedPre = Mathf.InverseLerp(280f, 160f, Mathf.Abs(vehicle.CurrentSpeedKph));
            float atRampPre = Mathf.InverseLerp(40f, 0f, (track.PitEntryRampStartNormalized - fromProgress.normalized) * track.length);
            float prePositionBlend = Mathf.Max(speedPre, atRampPre);
            track.ComputePitEntryTargetPoint(fromProgress.distance, lookAhead, prePositionBlend, out pitTargetPoint, out pitTargetRotation);
            return pitTargetPoint;
        }

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

        // Restart/caution-recovery throttle boost: counts UP to
        // PaceCapRecoveryBoostSeconds every frame the car is genuinely
        // pace-capped (VSC/SC/local yellow), then counts back DOWN once the
        // cap clears - so the boosted throttle ramp (see AccelerationBoostMultiplier)
        // covers both the run back up to speed while still technically capped
        // and, more importantly, the first few seconds after the cap actually
        // lifts, which is when a real driver floors it hardest.
        float paceCapRecoveryBoostTimer;
        const float PaceCapRecoveryBoostSeconds = 4f;
        // Pure throttle-ramp response, never an engine/grip boost (same
        // convention as launchConfidence above) - how fast currentThrottle
        // catches up to its target during a race start or a VSC/SC/yellow
        // recovery. Raised to 2x per request.
        const float AccelerationBoostMultiplier = 2f;
        // How long the dedicated physics-level launch boost (VehicleCommand.launchBoost)
        // stays armed from lights-out. Deliberately covers the whole first-corner
        // sprint, not just the sub-second launch-confidence settle, so the AI
        // actually drags off the line instead of the boost expiring before the
        // pack has moved.
        const float RaceStartBoostSeconds = 4.5f;

        // Race-start confidence, derived once at spawn from difficulty + driver
        // skill. Pure input timing/ramp, never an engine or grip boost.
        float launchConfidence = 1f;
        float launchSettleDuration;
        // Rate-limited racing-line offset (see the racing-line block) - persists
        // between frames so the outside->apex->outside line slews smoothly instead
        // of snapping, which was reading as the cars visibly swerving/weaving.
        float smoothedLineBias;
        // Denoised distance-to-apex. FindUpcomingApex resolves in 20 m steps, so
        // the raw value can jump a full step between frames and wobble the racing
        // line's apex-proximity term; this low-passes it so the line phase changes
        // smoothly.
        float smoothedApexDistance = 400f;
        // Rate-limited traffic-avoidance steer nudge (dodge/side-by-side pushes in
        // ApplyTrafficAvoidance). Unlike lineBias above, this was applied RAW every
        // frame straight from that frame's proximity geometry - on a dense grid
        // launch, many neighbour relationships sit right at a detection threshold
        // and flicker in/out frame to frame, so the raw nudge could jump hard
        // between +-0.3 (or +-0.78 outside the launch window) tick to tick. That
        // is the "AI swerving erratically at the start" - smoothing it the same
        // way lineBias is smoothed fixes it without weakening the underlying
        // collision-avoidance response, just its frame-to-frame snappiness.
        float smoothedSteerAdjust;

        // Traffic dodge-side memory so a car sitting near local.x==0 ahead of us
        // doesn't make the avoidance steer flicker frame to frame.
        float dodgeMemorySide;
        float dodgeMemoryTimer;

        // DRS commit-once-per-zone so a lower drsUsageQuality AI misses the wing
        // activation as a whole zone decision, not a mid-zone flicker.
        bool drsCommittedThisZone;
        bool wasDrsLegalLastFrame;
        // How long DRS has read illegal, continuously. The commitment roll below
        // only re-rolls on a legality rising edge after a REAL gap (a genuine new
        // zone), so a one-frame availability blip mid-zone can't make a committed
        // driver suddenly "forget" DRS (or an uncommitted one remember it).
        float drsIllegalSeconds = 10f;
        // Post-zone hold-open grace, mirroring PlayerVehicleInput.drsAutoHoldTimer.
        float drsAutoHoldTimer;

        // The rival this car is currently attacking. Captured when an attack state
        // is entered so a "pass completed" transition can be VERIFIED against the
        // actual target rather than inferred from a proximity query returning null.
        RaceParticipant overtakeTarget;

        /// <summary>
        /// True only if the car we were attacking is genuinely behind us now.
        ///
        /// The pass state machine used to declare a completed overtake whenever
        /// FindCarAhead returned null. That query means "no car within N metres of
        /// progress AHEAD" - which is equally true when the rival PULLS AWAY. From
        /// SideBySide, a rival edging 13m clear (i.e. this car losing the fight)
        /// tripped the 12m test and was recorded as a completed pass; the
        /// gap-opening abort check sits in the later else and was never reached. That
        /// inflated every AI driver's "overtakes made" with passes they actually
        /// lost, and armed a 2.5s re-attack cooldown on a car that had just been
        /// shuffled backwards, suppressing the genuine re-attack.
        /// </summary>
        bool OvertakeTargetIsBehind()
        {
            if (overtakeTarget == null || overtakeTarget.lapTracker == null ||
                participant == null || participant.lapTracker == null)
            {
                return false;
            }

            // Driving past a car that retired or took the flag is not an overtake.
            if (overtakeTarget.retired || overtakeTarget.finished)
            {
                return false;
            }

            return overtakeTarget.lapTracker.TotalProgressDistance < participant.lapTracker.TotalProgressDistance;
        }

        // The most restrictive SAFETY throttle cap applied this frame - traffic
        // avoidance (hold-station, proximity, side-by-side, boxed-in) and the blue
        // flag concession. The ERS "genuine straight" override forces throttle to
        // 1.0, and because it runs AFTER both of those it used to silently cancel
        // them: its only guard is `command.brake > 0.05f`, so every throttle-ONLY
        // safety response - which is all of them, by design - was unprotected. An AI
        // holding station a couple of car lengths behind another on a straight had
        // its cap cancelled and closed onto the car ahead, and a lapped AI under a
        // blue flag went to full throttle on exactly the straights where it is meant
        // to concede, then collected the 5s "ignoring blue flags" penalty for it.
        float safetyThrottleCeiling = 1f;

        // Difficulty-tier pace multipliers, as authored in RaceManager.AiProfiles.
        // Used only to normalise a profile onto a 0..1 tier axis so difficulty can
        // reach corner speed through a bounded term - see feasibilityCap.
        const float EasyPaceMultiplier = 1.77f;
        const float ExpertPaceMultiplier = 2.05f;

        // Weather-crossover watch: how long this car has been on the wrong
        // class of rubber for the track state (AiPitStrategyRules decides when
        // that persistence commits the car to the box).
        float weatherMismatchSeconds;

        // Track-query seam (Phase C): drivable half-width reads the active
        // ITrackQuery so an authored backend drops in per circuit; identical
        // to the direct hairpin-aware TrackRuntime.HalfWidthAt today, with a
        // null-safe fallback before the provider is selected.
        float LocalHalfWidthAt(float distance)
        {
            F1Game.Track.ITrackQuery query = TrackQueryProvider.Active;
            return query != null ? query.WidthAt(distance) * 0.5f : track.HalfWidthAt(distance);
        }

        // Boundary mapping into the strategy rulebook's compound mirror, the
        // same pattern RaceManager uses for PitRequestRules' origin enum.
        static StintCompound MapStintCompound(TyreCompound compound)
        {
            switch (compound)
            {
                case TyreCompound.Soft:
                    return StintCompound.Soft;
                case TyreCompound.Hard:
                    return StintCompound.Hard;
                case TyreCompound.Intermediate:
                    return StintCompound.Intermediate;
                case TyreCompound.Wet:
                    return StintCompound.Wet;
                default:
                    return StintCompound.Medium;
            }
        }

        enum OvertakeState { Following, PreparingAttack, AttackingInside, AttackingOutside, SideBySide, CompletingPass, BackingOut }
        OvertakeState overtakeState = OvertakeState.Following;
        float overtakeStateTimer;
        float attackSide = 1f;

        // [SwerveDiag] state - detects the reported AI "swerving"/shimmy by
        // counting steering/offset sign reversals while on a STRAIGHT (where a
        // reversal cannot be justified by the corner, so it is unambiguously a
        // weave). When a car crosses the threshold it logs which component is
        // oscillating (the racing-line bias, the overtake offset, the traffic
        // nudge, or the raw steer) plus the overtake-state churn, so the cause
        // is named rather than guessed. Per-instance, so each AI tracks itself.
        int swerveSteerSign, swerveAggSign, swerveAdjustSign, swerveLineSign;
        int swerveSteerRev, swerveAggRev, swerveAdjustRev, swerveLineRev;
        int swerveStateChanges;
        OvertakeState swervePrevState = OvertakeState.Following;
        float swerveWindowStart;
        float swerveLastLogTime;
        // Previous applied steer, for the steering slew-rate limiter (the swerve
        // fix): the final command.steer is rate-limited toward its new target so a
        // fast oscillation can never physically materialise.
        float steerSlewPrev;
        // Previous frame's forward heading, for straight-line yaw-rate damping.
        // The pursuit loop (steer -> yaw -> transform.right -> localSteer -> steer)
        // gains loop authority with speed, so a maxed 360kph car crosses the
        // oscillation threshold the normal ~300kph field stayed under - the weave
        // that got worse the instant Legends (maxed cars) was switched on. Damping
        // the measured yaw rate on a STRAIGHT (where any rotation is unwanted) with
        // an UN-smoothed term removes the loop's zero-crossing rate, not just its
        // amplitude - the thing a low-pass or slew limiter alone can't touch.
        Vector3 swerveHeadingPrev;
        bool swerveHeadingHasRef;
        // Low-passed yaw rate for the straight-line damper. Differentiating the
        // heading frame-to-frame (SignedAngle/dt) is inherently noisy - the
        // rigidbody heading jitters a fraction of a degree every physics step,
        // and feeding that raw derivative straight into an un-rate-limited steer
        // (which is where the previous "yaw damp after the slew limiter" version
        // put it) injects that jitter as high-frequency steer CHATTER - the
        // steer=30-56 reversals/2s the diagnostic kept reporting. Smoothing the
        // yaw rate first keeps the genuine low-frequency damping while throwing
        // the per-step noise away, so the damper stops being an oscillation source.
        float smoothedYawRateDeg;
        // [SwerveDiag amplitude] The reversal counter above catches high-frequency
        // steer CHATTER, but the swerve that crashes the player into a wall is a
        // large, slow lateral EXCURSION (the car drifting most of the road width and
        // back) whose period exceeds the 2s reversal window, so it barely registers
        // there. These track the peak-to-peak swing over a longer window of the car's
        // actual lateral position and of each thing that could be commanding it, so a
        // single log names the dominant driver of the visible weave: the raw drawn
        // racing line, the blended line target, or the overtake offset.
        float swerveAmpWindowStart;
        float swerveAmpLatMin, swerveAmpLatMax;      // car's actual lateralDistance
        float swerveAmpDrawnMin, swerveAmpDrawnMax;  // raw racing-line offset (pre-blend/trim)
        float swerveAmpLineMin, swerveAmpLineMax;    // final lineBias (post blend + slew)
        float swerveAmpAggMin, swerveAmpAggMax;      // overtake aggressionOffset
        bool swerveAmpHasSample;
        float swerveAmpLastLog;
        // Previous frame's measured lateral, for the teleport guard (see below).
        float swerveAmpPrevLat;
        bool swerveAmpHasPrevLat;
        // Raw racing-line offset at the steering target, captured from the line block
        // so the amplitude diagnostic can compare it against the car's real motion.
        float lastDrawnOffset;
        bool hasLastDrawnOffset;

        // [SwerveTape] full-pipeline flight recorder (per request - "write a
        // better diagnostic"). Every frame, EVERY term that feeds the final
        // steering command is captured into a ring buffer; when either swerve
        // detector fires, the whole window is dumped as a time-series table
        // PLUS an automatic verdict that attributes each final-steer reversal
        // to whichever term actually moved the wheel that frame. No inference,
        // no guessing - the dump names the oscillating term measurably.
        struct SwerveTapeFrame
        {
            public float time, speedKph, lat, halfW, sev;
            public float drawn, lineBias, agg, traffic;
            public float pursuit, comp, edge, yawDamp, final;
            public float urgT, urgP, towardRate, yawMeas;
            public bool refSnap;
            public int state;
        }
        readonly SwerveTapeFrame[] swerveTape = new SwerveTapeFrame[512];
        int swerveTapeCount;
        float swerveTapeLastDump = -999f;

        // [PitStopDiag] (per report - "the 2 stop problem still exists; if you
        // dont know write code to diagnose it"): every voluntary pit trigger
        // that fires for a car that has ALREADY stopped once logs, once per
        // lap, exactly which trigger(s) fired and the full projection state
        // (wear, laps left vs laps to flag, veto result) that let it through -
        // so the next report names the guilty trigger instead of another
        // round of guessing.
        int pitDiagLastLoggedLap = -1;

        static void AddPitTag(ref string tags, string tag)
        {
            tags = tags == null ? tag : tags + "+" + tag;
        }

        // [SwerveTape] round 3: continuous seconds the FINAL steering command
        // has been pinned at (near) full lock at speed - the measured
        // precursor of nearly every remaining wall hit (sustained understeer:
        // the car cannot produce the yaw the line needs, drifts wide for
        // seconds, and never slows down). Drives the understeer speed-shed
        // reflex below.
        float steerSaturationTimer;

        // Overtake-thrash guards ([SwerveDiag] - during passes the state machine
        // cycled Attacking->SideBySide->CompletingPass->Following->Attacking
        // several times a second, swinging aggressionOffset +-7m). enteredTime
        // gives every state a minimum dwell before it can hand off; reattack
        // cooldown stops a just-"completed" pass from instantly re-triggering
        // when two cars run level and FindCarAhead flickers null.
        float overtakeStateEnteredTime;
        float overtakeReattackCooldown;
        const float OvertakeMinStateDwell = 0.45f;
        const float OvertakeReattackCooldownSeconds = 2.5f;

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
        // VSC/SC restart acceleration fix: shortened from 2f - this ramp
        // covers every handback (pit-exit merge, forced reposition, and the
        // SC/VSC/yellow-flag restart alike), and the restart case in
        // particular was reading as sluggish: race control's own convoy ramp
        // (RaceManager.UpdateSafetyCarConvoy) has already brought the field
        // up near green-flag pace by the time this handback fires, so a full
        // 2 further seconds of capped throttle on top of that read as a slow,
        // hesitant restart rather than a smooth one.
        const float HandbackRampDuration = 1.1f;

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
            // Measure the turn angle symmetrically about THIS corner's true apex.
            //
            // EstimateCornerSeverity samples forward only (+14/+46/+82m), so
            // FindUpcomingApex's argmax sits up to ~80m SHORT of a long-arc hairpin's
            // tip. Probing the +/-55m baseline about that short point put both samples
            // on the entry leg, so the Italy/Monza 177-degree switchback read ~0
            // degrees, failed the >=150 hairpin test, and was taken flat out into the
            // wall (VeryTight speed floor). An earlier fix took the max angle over a
            // forward window, but that ~170m span accumulated heading change across
            // ADJACENT corners on twisty tracks and falsely promoted ordinary tight
            // corners to the hairpin crawl speed - the AI read as far too slow.
            //
            // Instead, walk forward to this corner's tightest point (peak local
            // heading change) and stop the moment that curvature falls back, so the
            // search reaches a real hairpin's tip but never crosses into the next
            // corner. Then measure the full +/-55m angle about that apex. Ordinary and
            // back-to-back corners are measured at their own true angle (still < 150 =
            // VeryTight, unchanged speed); only a genuine single >=150-degree turn is
            // promoted to Hairpin.
            float apex = apexDistance;
            float bestLocal = LocalHeadingChange(apexDistance);
            for (float ahead = 14f; ahead <= 90f; ahead += 14f)
            {
                float local = LocalHeadingChange(apexDistance + ahead);
                if (local > bestLocal)
                {
                    bestLocal = local;
                    apex = apexDistance + ahead;
                }
                else if (local < bestLocal - 8f)
                {
                    break; // past this corner's tightest point; don't cross into the next
                }
            }

            Vector3 pointBefore, forwardBefore, rightBefore;
            Vector3 pointAfter, forwardAfter, rightAfter;
            track.SampleAtDistance(apex - 55f, out pointBefore, out forwardBefore, out rightBefore);
            track.SampleAtDistance(apex + 55f, out pointAfter, out forwardAfter, out rightAfter);
            return Vector3.Angle(forwardBefore, forwardAfter);
        }

        // Local heading change over a short symmetric baseline - a curvature proxy
        // used to locate a corner's tightest point.
        float LocalHeadingChange(float distance)
        {
            Vector3 point, forwardBefore, right, forwardAfter;
            track.SampleAtDistance(distance - 16f, out point, out forwardBefore, out right);
            track.SampleAtDistance(distance + 16f, out point, out forwardAfter, out right);
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
            //
            // Threshold raised 150 -> 168 degrees. Now that the turn angle is measured
            // about the real apex, an early tight corner on Italy was measuring in the
            // 150s and getting the hairpin crawl speed even though it is just a tight
            // turn, not a U-turn. A genuine hairpin (the Italy switchback measures
            // ~176) clears 168 comfortably, so only near-180 corners are treated as
            // hairpins; everything from a merely-tight corner up to ~168 stays
            // VeryTight and keeps its faster speed.
            //
            // Wall-crash fix: brought back down 168 -> 140 on the theory that the
            // +/-55m wide-baseline probe underreads a genuine switchback on tight
            // street layouts. Play-testing showed this wasn't the actual
            // corner-crash cause, and VeryTight is back to a full straight-line-
            // speed tier (see EstimateApexSpeedForCornerType's round 21/15
            // reverts), so the original reasoning for 168 (only near-180 corners
            // get the hairpin crawl; everything else keeps VeryTight's faster
            // speed) applies again. Reverted to 168.
            return hairpinTurnAngleDegrees >= 168f ? CornerType.Hairpin : CornerType.VeryTight;
        }

        // Envelope-derived apex speeds (corner-speed realism pass, per report -
        // "the tracks are way too easy... a lot of turns where i can go flat
        // out, i rarely have to switch to anything but 8th gear"). The old
        // version was a hand-tuned kph floor table per corner class, raised
        // and lowered across 20+ rounds while ApplySteering's rotation
        // authority was retuned separately - every wall-crash and every
        // crawling regression in this area came from those two hand-matched
        // numbers drifting apart. The floor table is gone: the caller now
        // measures the apex's true geometric radius (TrackRuntime.
        // CurvatureRadiusAt) and asks the shared physics envelope
        // (VehicleController.MaxCorneringSpeedKph) for the fastest speed the
        // car can actually rotate through it - weather, tyre grip, damage and
        // chassis stats all flow in through that one function. What remains
        // here is pure DRIVER JUDGMENT: what fraction of the physical maximum
        // this tier/driver commits to, slightly more conservative in tighter
        // classes where the penalty for misjudging is a wall rather than a
        // wide exit. The severity-eased blend toward straightTargetSpeed is
        // gone too - the envelope already returns ~straight-line speed for
        // gentle radii, so blending was redundant on kinks and dangerously
        // optimistic on real corners. The AI-only rotation margin
        // (AiCorneringRotationBoost, 1.1x) plus the understeer feedback in
        // ApplySteering cover line imperfection on top of the <1.0 fractions.
        float EstimateApexSpeedForCornerType(CornerType type, float straightTargetSpeed, float hairpinSpeedKph, float apexConfidence, float skillTier, float compoundSpeedOffsetKph, float corneringEnvelopeKph)
        {
            // Slow-corner pace round (per report - "ai go into slow corners
            // way too slow still"): the low-confidence floors sat at 0.80-0.83
            // of the envelope, and mid-field drivers live near those floors -
            // a fifth of the corner speed given away before the geometric caps
            // even spoke. Floors raised hardest on the slow/tight/hairpin
            // tiers where the drag was reported; the skill ceilings barely
            // move, so driver ordering is preserved.
            float envelopeFraction;
            switch (type)
            {
                case CornerType.HighSpeed:
                    envelopeFraction = Mathf.Lerp(0.90f, Mathf.Lerp(0.94f, 0.99f, skillTier), apexConfidence);
                    break;
                case CornerType.Medium:
                    envelopeFraction = Mathf.Lerp(0.88f, Mathf.Lerp(0.93f, 0.985f, skillTier), apexConfidence);
                    break;
                case CornerType.Slow:
                    envelopeFraction = Mathf.Lerp(0.88f, Mathf.Lerp(0.94f, 0.99f, skillTier), apexConfidence);
                    break;
                case CornerType.VeryTight:
                    envelopeFraction = Mathf.Lerp(0.86f, Mathf.Lerp(0.93f, 0.985f, skillTier), apexConfidence);
                    break;
                default:
                    // Hairpin: same judgment-fraction idea, additionally capped
                    // by the car-stat hairpin band so a hairpin always reads as
                    // the deliberate 2nd-gear corner it is even if the smoothed
                    // radius sample runs a little generous.
                    envelopeFraction = Mathf.Lerp(0.85f, Mathf.Lerp(0.92f, 0.98f, skillTier), apexConfidence);
                    break;
            }

            float apexSpeed = corneringEnvelopeKph * envelopeFraction;
            if (type == CornerType.Hairpin)
            {
                apexSpeed = Mathf.Min(apexSpeed, hairpinSpeedKph);
            }

            // Compound speed offset stays an explicit absolute subtraction so
            // compounds still differentiate corner pace (the envelope's full
            // grip read already carries some of this, so the double count is a
            // few kph of extra caution on slower rubber - the safe direction);
            // the straight-line Min keeps the "a corner can at best approach
            // straight-line speed" invariant closed.
            return Mathf.Min(straightTargetSpeed, Mathf.Max(15f, apexSpeed - compoundSpeedOffsetKph));
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

            // Starting-mess fix ("people all over the place"): the old 4-lane
            // scheme picked a fan-out target from gridSlot % 4, which does NOT
            // correlate with which side of the grid a car actually spawned on
            // (that's gridSlot % 2, matching preferredSide/GetGridSlot's real
            // leftSlot). For half the grid (every slot where gridSlot % 4 was 1
            // or 2) the two disagreed - e.g. slot 1 spawns on the RIGHT column
            // but %4 assigned it a lane on the LEFT - so those cars launched by
            // immediately cutting diagonally across the track to a lane on the
            // OPPOSITE side from where they started, right as the fan-out hold
            // (now full-strength for several seconds) locked them onto that
            // wrong-side target. That cross-traffic every single race is the
            // "starting procedure is a mess". Fan lane is now always on the same
            // side the car actually started (preferredSide), with a near/far
            // depth alternation within that side to keep the side-by-side
            // spread effect without ever crossing the track's centreline.
            float laneSpread = Mathf.Min(3.4f, raceTrack.roadHalfWidth * 0.24f);
            int depthIndex = (gridSlot / 2) % 2;
            openingFanOffset = preferredSide * laneSpread * (depthIndex == 0 ? 0.6f : 1.4f);

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
            // Launch acceleration fix: raised the base confidence floor (was
            // 0.55) and softened how much reaction time drags it down (was
            // *0.12) - even a low-skill/slow-reacting AI used to leave the
            // line noticeably softer than it needed to, on top of the
            // separate opening pileup-safety cap just below (which already
            // does the actual first-corner fan-out job). Settle duration is
            // also shortened (was 1.3f ceiling / *0.35f reaction weight) so
            // whatever gap remains between initial confidence and full
            // throttle closes faster.
            // Start acceleration (per request - player was making up 10-12
            // places from P22): the AI now launches essentially flat out. The
            // confidence floor is raised near full (was 0.68) so even a
            // low-skill / slow-reacting driver leaves the line hard, and the
            // settle window is much shorter so any remaining gap to full
            // throttle closes almost immediately. ApplyTrafficAvoidance is
            // still the actual first-corner anti-pileup guard.
            launchConfidence = Mathf.Clamp01(Mathf.Lerp(0.92f, launchSkillCeiling, launchSkill) - startupProfile.reactionTimeSeconds * 0.04f);
            launchSettleDuration = Mathf.Lerp(settleFloor, 0.45f, 1f - launchSkill) + startupProfile.reactionTimeSeconds * 0.12f;
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
                    float localHalfWidth = LocalHalfWidthAt(progress.distance);
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
            // Raw driver stats, deliberately unscaled: the old +69% pre-clamp
            // multiply pushed every driver on the grid (77-94 in drivers.json)
            // past the 0-100 clamp, so all 22 drivers defended and attacked as
            // identical 100-rated clones and the per-driver spread downstream
            // (commit/lunge/hold decisions, defensive-line logic) was erased.
            // Racecraft sharpness is tuned where it belongs - on the commitment
            // blend in UpdateOvertakeState - not by flattening the stat model.
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

            // Difficulty differentiates RACECRAFT, not machinery (per request):
            // the per-tier kph/grip assists are gone (RaceManager.Grid now
            // hands every AI identical machinery to the player) and difficulty
            // instead shifts how well the field attacks and defends - Expert AI
            // commit to overtakes and shut doors like elites, Easy AI are
            // passive and easy to pass.
            RaceDifficulty racecraftTier = raceManager.Settings != null ? raceManager.Settings.Difficulty : RaceDifficulty.Medium;
            // Round 5: whole ladder shifted up (+6) - every tier attacks and
            // defends harder while the tier ordering stays intact.
            // Round 6: ladder shifted up another +6 (per request).
            // Round 7: another +6 (per request) - every tier now attacks and
            // defends above its raw stats; the 99 clamp bounds the top end.
            // Rounds 9-10 REWORK (per question - "do these upgrades completely
            // get rid of the AI driver's skill? don't make it like that"): the
            // flat +delta ladder WAS saturating - by round 8's +26/+32 nearly
            // every driver clamped at 99 overtaking/defending, erasing the
            // skill differences between drivers exactly as feared. Difficulty
            // now COMPRESSES each driver's gap to 99 instead of adding a flat
            // amount: every tier makes the whole field better, but an elite
            // keeps a real margin over a journeyman at every tier and nobody
            // saturates. (Average 80-rated driver: Easy ~85, Expert ~93;
            // elite 92: Easy ~94, Expert ~97 - ordering always preserved.)
            // Round 11: compression deepened another notch at every tier.
            // Rounds 12-16 (per request - "buff the AI again 5 times the same
            // way"): compression deepened five more notches. Still a gap-KEEP
            // fraction, never a flat delta, so an elite always stays measurably
            // ahead of a journeyman and nobody saturates at the 98 cap.
            // Rounds 17-21 (per request): five more notches (was
            // 0.42/0.30/0.19/0.11).
            // Rounds 22-26 (per request): five more (was 0.26/0.17/0.10/0.05) -
            // effectively at the floor now; racecraft is next to saturated at
            // the 98 clamp on Hard/Expert and further rounds express through
            // the pace knobs instead.
            // Rounds 27-31 (per request): the last meaningful notches left
            // before the fraction is zero (was 0.16/0.10/0.06/0.03).
            // Skill-differentiation fix (per report - "the leaderboard does not
            // make sense"): at 0.02-0.10 gap-keep the whole field landed within
            // a point of the 98 clamp and integer rounding made every driver's
            // overtaking/defending IDENTICAL - which is exactly the "skill
            // erased" failure the compression design was built to avoid.
            // Backed off to fractions that keep the field elite (an average
            // driver still racecrafts in the mid-90s on Expert) but leave a
            // real, visible several-point gap between an elite and a
            // journeyman at every tier.
            float racecraftGapKeep = racecraftTier == RaceDifficulty.Easy ? 0.30f
                : racecraftTier == RaceDifficulty.Medium ? 0.22f
                : racecraftTier == RaceDifficulty.Hard ? 0.16f : 0.12f;
            overtaking = Mathf.Clamp(Mathf.RoundToInt(99f - (99f - overtaking) * racecraftGapKeep), 30, 98);
            defending = Mathf.Clamp(Mathf.RoundToInt(99f - (99f - defending) * racecraftGapKeep), 30, 98);

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
            // Low-pass the 20 m-quantised apex distance so the racing line's
            // apex-proximity term doesn't wobble on single-frame steps - but
            // ASYMMETRICALLY: passing an apex makes the raw value jump hundreds of
            // metres up to the next corner in one frame, and slewing through that
            // jump kept the car in "at apex" line-bias for ~2s after the corner
            // while turnSign already pointed at the NEXT corner - steering the car
            // to the wrong side between corners (line never settled, read as both
            // "not on the optimal line" and residual weaving). Big upward jumps now
            // snap immediately; only the small step noise is smoothed.
            if (apexDistanceAhead > smoothedApexDistance + 45f)
            {
                smoothedApexDistance = apexDistanceAhead;
            }
            else
            {
                smoothedApexDistance = Mathf.MoveTowards(smoothedApexDistance, apexDistanceAhead, Time.deltaTime * 140f);
            }
            float turnSign = EstimateTurnDirection(progress.distance);

            // Real ceiling, not an invented ~330-350kph clamp: the same DRS/ERS-aware
            // number the player's own physics already computes every tick.
            float carTopSpeed = vehicle.CarData == null || vehicle.CarData.topSpeed <= 0 ? 337f : vehicle.CarData.topSpeed;
            // straightSpeedMultiplier is hard-clamped to <= 1.0 here: it may only ever
            // discount how much of the real ceiling this difficulty confidently uses,
            // never inflate a straight-line target past what the car can actually do.
            float straightTargetSpeed = (vehicle.TargetTopSpeedKph > 5f ? vehicle.TargetTopSpeedKph : carTopSpeed) * Mathf.Min(1f, profile.straightSpeedMultiplier);
            // Straight-line speed pass: AI top speed was reading as insane - flat
            // -15kph off the straight-line target, applied AFTER the per-difficulty
            // multiplier above so every difficulty (Easy through Expert) loses the
            // same absolute amount instead of the cut shrinking on an already-lower
            // difficulty ceiling.
            // Round 2: still too fast - an additional flat -7.5kph on top of the
            // round-1 cut (now -22.5kph total off the raw ceiling), same reasoning,
            // applied uniformly across every difficulty.
            // Round 3: another flat -10kph on top of that (now -32.5kph total),
            // same reasoning, applied uniformly across every difficulty.
            // Round 4: another flat -10kph on top of that (now -42.5kph total),
            // same reasoning, applied uniformly across every difficulty.
            // Round 5: another flat -7.5kph on top of that (now -50kph total),
            // same reasoning, applied uniformly across every difficulty.
            // Round 6: another flat -15kph on top of that (now -65kph total),
            // same reasoning, applied uniformly across every difficulty.
            // Round 7: another flat -10kph on top of that (now -75kph total),
            // same reasoning, applied uniformly across every difficulty.
            // Round 8: eased back up 5kph (now -70kph total), same reasoning,
            // applied uniformly across every difficulty.
            // Round 9: another flat -20kph on top of that (now -90kph total),
            // same reasoning, applied uniformly across every difficulty.
            // Round 10: another flat -5kph on top of that (now -95kph total),
            // same reasoning, applied uniformly across every difficulty.
            // Round 11: another flat -5kph on top of that (now -100kph total),
            // same reasoning, applied uniformly across every difficulty.
            // Round 12: eased back up 3kph (now -97kph total), same reasoning,
            // applied uniformly across every difficulty.
            // Round 13: eased back up 2kph (now -95kph total), same reasoning,
            // applied uniformly across every difficulty.
            // Round 14: eased back up 2.5kph (now -92.5kph total), same reasoning,
            // applied uniformly across every difficulty.
            // Round 15: eased back up 0.5kph (now -92kph total), same reasoning,
            // applied uniformly across every difficulty.
            // Round 16 (difficulty staircase - "way too easy, P22 to P1 in one
            // lap on Hard"): the 15 rounds above landed on a flat -92kph applied
            // uniformly to EVERY difficulty, leaving even Hard/Expert cruising
            // ~90kph under the car's real physics ceiling while the player runs
            // right at it - a straight-line gap of several seconds per lap that
            // no decision-quality model could hide, and exactly why the whole
            // field could be driven through in a single lap. The discount is now
            // per-difficulty: Easy keeps the full historical -92 feel, Medium
            // gives a chunk back, and Hard/Expert run close to the real envelope
            // (their paceMultiplier and per-driver variance still spread the
            // field on top, and the braking model below is kinematic - braking
            // distance scales with the square of real speed - so the higher
            // entry speeds still get correct brake points).
            // Difficulty rework (per request - "AI straight-line speed should
            // be the same across ALL difficulties"): the per-tier straight
            // discount staircase (Easy -30 / Medium -10 / Hard/Expert 0) is
            // gone - every tier now runs the identical straight-line envelope,
            // and difficulty differentiates through racecraft (the per-tier
            // overtaking/defending delta above) and the per-tier behaviour
            // profiles (corner commitment, reaction, mistakes) instead.

            bool wet = track.weather == WeatherState.LightRain || track.weather == WeatherState.HeavyRain;
            // (A compound-neutral GripConditionMultiplier used to be read here, from
            // an era when this method scaled its own corner-speed table by grip. It
            // had become dead - every corner target below is now a confidence
            // fraction of MaxCorneringSpeedKph, which derives from the shared yaw
            // envelope and therefore already carries the car's REAL tyre grip. Kept
            // as a note because the comment it carried claimed the opposite and cost
            // real time to disprove while chasing the hard-tyre pace report.)
            float minCornerConfidence = profile.minimumCornerSpeedConfidence;
            if (wet)
            {
                // Low wetSkill drivers lose confidence fastest; Expert's low caution
                // still gets diluted here, it never bypasses the driver's own skill.
                float wetSkillRelief = Mathf.Lerp(0.35f, 0.05f, wetSkill / 100f);
                minCornerConfidence *= 1f - Mathf.Clamp01(profile.wetWeatherCaution * wetSkillRelief);
            }

            // Floor 0.85 -> 0.93, same calibration argument as gripUtilization in
            // RaceManager.Grid, and for a reason visible only when you put the two
            // side by side: they are two independent models of the SAME thing, and
            // they multiply. A midfield driver was losing 16% of the car's grip to
            // one and another 15% of its corner-speed target to the other, compounding
            // to ~0.80 of the machinery - and then apexConfidence, the sweeper margin
            // and tightCornerJudgment stacked on that. Driver quality should be
            // expressed once, at a realistic spread, not accumulated across every
            // system that has an opinion about it.
            float experienceConfidence = Mathf.Lerp(0.93f, 1.05f, consistency / 100f);
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
            // Hairpin apex speed cut ~15% (per request, "maybe reduce hairpin
            // speed" - the Italy hairpin barrier-gap report) as extra margin on
            // top of the corner-detection/barrier-radius fixes: 50-75 -> 42-64.
            // Difficulty pass ("still too easy" round 2): the flat 42-64kph crawl
            // was identical on every difficulty and far below what the player
            // carries through the same hairpin - each hairpin handed the player
            // seconds for free. Skill-scaled up to ~30% for the top tiers
            // (Easy/Medium keep most of the original crawl); the kinematic
            // braking model plus the wall-aversion multiplier (raised again in
            // the same pass) keep the approach safe.
            // Raised alongside the round-2 rotation-authority boost (the yaw
            // model now rotates a hairpin's ~8-15m radius at 90-120 kph, so the
            // old 42-83 crawl was far under the car's real capability).
            // Difficulty round 4: hairpin band up again (75-100 -> 85-110,
            // skill mult 1.3 -> 1.35).
            // Corner-speed realism pass: re-based to the new ~4-5.5g yaw
            // envelope (was 85-110 x up to 1.35 skill - tuned for the old
            // arcade authority). A real hairpin (8-15m radius) supports
            // ~60-80 kph under MaxYawRateDegPerSec, so the band is now 62-80
            // with a modest skill lift; the envelope-derived floor in
            // EstimateApexSpeedForCornerType Min()s against this anyway, so
            // this acts as a cap/character number rather than a physics claim.
            // Slow-corner pace round ("ai go into slow corners way too slow
            // still"): 62-80 -> 70-88. A real 8-15m hairpin supports ~94kph
            // under the current envelope, so the old band bound BELOW the
            // physics for everyone but the elite; the envelope Min() in
            // EstimateApexSpeedForCornerType still caps whatever this allows.
            // Round 2 ("still WAY too slow going into hairpins"): 70-88 ->
            // 76-94 - the band now brackets the ~94kph the physics envelope
            // actually supports, acting as a car-stat/skill spread within it
            // rather than a blanket crawl below it.
            float hairpinSpeedKph = Mathf.Lerp(76f, 94f, Mathf.Clamp01((carBrakingStat + carCorneringStat) / 200f)) * Mathf.Lerp(1f, 1.15f, skillTier);

            // Classify the upcoming apex by type rather than treating one continuous
            // severity number the same everywhere - a flowing high-speed kink and a
            // genuine hairpin need very different confidence curves (Part 2).
            float hairpinTurnAngle = MeasureHairpinTurnAngle(progress.distance + apexDistanceAhead);
            CornerType upcomingCornerType = ClassifyUpcomingCorner(apexSeverity, skillTier, hairpinTurnAngle);
            float compoundSpeedOffsetKph = vehicle.Tyres.CompoundSpeedOffsetKph(track.weather);
            // Physics-derived cornering ceiling: the fastest speed the shared
            // yaw envelope can actually rotate through the apex's measured
            // geometric radius. Every class floor in
            // EstimateApexSpeedForCornerType is now a skill/confidence
            // fraction of THIS number instead of a hand-tuned kph table, so
            // the AI's target and the car's achievable rotation come from one
            // function and cannot drift apart again.
            float apexRadius = track.CurvatureRadiusAt(progress.distance + apexDistanceAhead);
            float corneringEnvelopeKph = vehicle.MaxCorneringSpeedKph(apexRadius);
            float trueApexSpeed = EstimateApexSpeedForCornerType(upcomingCornerType, straightTargetSpeed, hairpinSpeedKph, apexConfidence, skillTier, compoundSpeedOffsetKph, corneringEnvelopeKph);

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
            // Corner-pace fix (per report - AI still corners too slowly): the
            // 0.5 floor here was a SECOND confidence penalty (apexConfidence
            // already shapes floorSpeed inside EstimateApexSpeedForCornerType),
            // so a low-confidence tier targeted as little as half the corner
            // speed the car could actually hold while the player drives near
            // the real limit. Raised to 0.9 - the AI now targets 90-100% of the
            // achievable corner speed. This can never exceed the geometry: the
            // floors are the geometric max the steering can rotate, trueApexSpeed
            // <= that, and the brakingApexSpeed feasibility cap (x1.12) still
            // guards entry, so cars carry the speed without overshooting.
            float apexTargetSpeed = Mathf.Lerp(trueApexSpeed * 0.9f, trueApexSpeed, apexConfidence);

            // Driver-quality variance is the per-driver pace differentiator, independent
            // of difficulty; profile.paceMultiplier is the difficulty-tier pace scaler
            // layered on top so Hard/Expert are meaningfully quicker than Easy/Medium.
            // Driver-separation fix (per report - "you can't tell Stroll from
            // Verstappen; weak drivers finish among the strong"): real driver
            // pace/qualifying stats only span roughly 74-97, so the old pace/100
            // normalisation crushed the whole grid into a ~6% band where a pace-90
            // and a pace-96 driver were barely 1% apart - well inside the race's
            // own noise, so the field never sorted by skill. Both axes are now
            // normalised against the ACTUAL stat band (InverseLerp), so the grid
            // spreads across the full multiplier range: a genuine top driver is
            // meaningfully quicker every lap than a midfielder, who is clearly
            // quicker than a backmarker. Pace is the dominant race-pace axis;
            // racecraft adds a smaller edge on top.
            // The endpoints are deliberately chosen so the FIELD AVERAGE driver
            // (pace/racecraft ~86) lands at ~1.14 - the same overall pace the old
            // pace/100 mapping produced - so this only spreads the field, it does
            // NOT change the average AI difficulty. A top driver comes out ~1.21,
            // a backmarker ~1.08 (was ~1.18 vs ~1.11): roughly double the old
            // top-to-bottom spread, so skill genuinely sorts the order.
            float paceNorm = Mathf.InverseLerp(74f, 97f, pace);
            float racecraftNorm = Mathf.InverseLerp(76f, 96f, racecraft);
            // CAR-pace variance (per report - "how is Bottas in a Cadillac top
            // 5?"): driver skill spread the field, but the CAR barely touched
            // AI corner pace - its stats only nudge shared physics (grip stat,
            // small top-speed differences), so a backmarker chassis with a
            // decent variance roll could genuinely run top 5. The car's overall
            // performance level now scales the corner-pace target directly,
            // centred on ~1.0 for a midfield car: a top car gains ~2.5%, a
            // backmarker loses ~3.5% - about 1.5-2.5s/lap across the field,
            // which stacks with (rather than replaces) the driver spread, so a
            // great driver in a bad car can salvage midfield but never podium.
            float carScore = vehicle.CarData == null ? 90f
                : (vehicle.CarData.cornering + vehicle.CarData.enginePower + vehicle.CarData.aeroEfficiency + vehicle.CarData.acceleration) * 0.25f;
            // Live data spans 80.2 (Cadillac) .. 93.5 (McLaren), so this band
            // uses the field's real spread end to end.
            float carNorm = Mathf.InverseLerp(80f, 94f, carScore);
            // Widened (per request - "a Red Bull should be MUCH faster than a
            // Cadillac"): the machinery gap is the car's own stats, not the
            // difficulty setting - top-to-bottom corner-pace spread is now ~8%
            // (~2.5-3s/lap between the best and worst chassis), stacked on the
            // driver spread.
            // Car-weight pass (per request - "the same Verstappen that can go
            // P22 to P1 will likely only manage P22 to P15 in a Cadillac,
            // while Stroll in a Red Bull/McLaren should still fall from P1 to
            // ~P10"): the car band nearly doubles (8% -> 14% top-to-bottom),
            // sized at roughly 60% of the driver band below - machinery gates
            // the ceiling hard, but a driver's own ability remains the single
            // biggest factor. Top speed stays the car's own absolute kph stat
            // and is untouched here.
            float carPaceVariance = Mathf.Lerp(0.93f, 1.07f, carNorm);
            // Difficulty raise round 5 (per request - "the AI still aren't good
            // enough; realistic, no straight-line/unfair advantage"): the whole
            // field's corner commitment band lifted +3% (1.06-1.16 -> 1.09-1.19)
            // - still bounded by the physical feasibility cap below and by the
            // shared grip/turn-rate physics, so this is drivers using more of
            // the car, not a faster car.
            // Round 6 (per request - "buff the AI the same way again"): another
            // +3% on the commitment band (1.09-1.19 -> 1.12-1.22).
            // Round 7 (per request): +3% again (1.12-1.22 -> 1.15-1.25).
            // Round 8 (per request): +3% again (1.15-1.25 -> 1.18-1.28).
            // Rounds 9-10 (per request): +3% twice more (-> 1.24-1.34). The
            // band's WIDTH is untouched every round, so the driver-skill
            // spread (paceNorm) is fully preserved - the whole field is
            // braver, the pecking order unchanged.
            // Round 11 (per request): +3% again (-> 1.27-1.37).
            // Rounds 12-16 (per request - "buff the AI again 5 times the same
            // way"): +3% five more times (-> 1.42-1.52). Width still untouched
            // every round - the whole field is braver, the pecking order
            // unchanged, and the feasibility cap below still bounds it all.
            // Rounds 17-21 (per request): +3% five more times (-> 1.57-1.67).
            // Rounds 22-26 (per request): +3% five more times (-> 1.72-1.82).
            // Rounds 27-31 (per request): +3% five more times (-> 1.87-1.97).
            // Skill-differentiation fix (per report - "the leaderboard does not
            // make sense"): thirty rounds of buffs grew the band's MEAN from
            // ~1.11 to ~1.92 while its width stayed at 0.10 - so the relative
            // pace gap between an elite and a backmarker quietly halved from
            // ~9% to ~5%, and driver skill stopped sorting the field. Width
            // restored to the original ~12% relative spread around the SAME
            // mean (1.92), so the field's average difficulty is unchanged -
            // the fast drivers are faster and the slow ones slower, no
            // artificial anything.
            // Rounds 32-36 (per request - "buff the AI another 5 rounds while
            // keeping Max Verstappen level drivers a lot better than Lance
            // Stroll level drivers"): +3% per round on the MEAN as usual, and
            // the band is widened further still (0.24 -> 0.36) rather than
            // shifted rigidly - the whole field gets faster AND the elite pull
            // further clear of the backmarkers at the same time.
            // Rounds 37-41 + skill-decides-races pass (per request - "an AI
            // with Verstappen-level skill should be able to go P22 to P1 in
            // any difficulty; Stroll-level should be able to fall P1 to P22"):
            // mean +3% per round as usual (2.10 -> 2.25), and the width jumps
            // 0.36 -> 0.54 (~24% top-to-bottom) so driver ability is the
            // single biggest separator on track. Difficulty still only moves
            // the MEAN (via the per-tier paceMultiplier), never this spread -
            // the elite-vs-backmarker gap is identical on Easy and Expert.
            float driverPaceVariance = Mathf.Lerp(1.98f, 2.52f, paceNorm) * Mathf.Lerp(1.0f, 1.06f, racecraftNorm) * carPaceVariance;
            float cruiseTargetSpeed = Mathf.Lerp(straightTargetSpeed, apexTargetSpeed, severityHere) * driverPaceVariance * profile.paceMultiplier;
            // Feasibility cap on the braking target: the driver/tier pace
            // multipliers used to inflate the corner-entry target past what the
            // steering can physically rotate through. The cap is now DRIVER-
            // DEPENDENT (per report - "you can't tell the drivers apart"): a top
            // driver is allowed to carry meaningfully more corner-entry speed
            // (up to ~1.17x the geometric apex) than a backmarker (~1.07x), so
            // skill shows through the corners instead of everyone being flattened
            // to the same flat 1.12 ceiling. It still never runs away from the
            // geometry the way the uncapped target could.
            // Round 5: entry-speed judgment window widened slightly with the
            // commitment lift above (1.07-1.17 -> 1.08-1.19) - still a
            // corner-entry judgment bound, never past what steering authority
            // can physically rotate.
            // Round 6: widened again with the commitment lift (1.08-1.19 ->
            // 1.09-1.21).
            // Round 7: widened again (1.09-1.21 -> 1.10-1.23).
            // Round 8: widened again (1.10-1.23 -> 1.11-1.25).
            // Rounds 9-10: widened twice more (-> 1.13-1.29); driver-skill
            // spread preserved (same width, same paceNorm mapping).
            // Round 11: widened again (-> 1.14-1.31).
            // Rounds 12-16: widened five more times (-> 1.19-1.41); same width
            // discipline as every prior round, so paceNorm (driver skill) still
            // decides who gets to use the extra entry-speed judgment.
            // Rounds 17-21: widened five more times (-> 1.24-1.51).
            // Rounds 22-26: widened five more times (-> 1.29-1.61).
            // Rounds 27-31: widened five more times (-> 1.34-1.71).
            // Rounds 32-36: widened five more times (-> 1.39-1.81), bottom
            // moving less than the top so driver skill decides more of it.
            // Rounds 37-41 + skill-decides-races pass: raised with the rounds
            // and widened further (1.39-1.81 -> 1.42-1.98) so the corner-entry
            // judgment ceiling separates by driver skill as hard as the
            // commitment band does.
            // Envelope rebase: 41 rounds widened this entry-speed judgment
            // window to 1.42-1.98x - which was survivable only because the old
            // apex targets were themselves saturated near straight-line speed
            // (the extra never physically materialized). With apex targets now
            // derived from the real ~4-5.5g yaw envelope, braking toward
            // 1.4-2x the geometric apex means arriving 40-100% too hot at a
            // corner the steering genuinely cannot rotate through - the exact
            // recipe for the historical hairpin wall-crashes. Entry overshoot
            // is now a trail-braking-sized judgment call: an elite driver
            // carries ~12% over the apex number deep into the zone and washes
            // the rest off inside the corner, a backmarker ~4%. Driver-skill
            // ordering preserved, physics honored.
            // Corner-entry pace round 2: nudged 1.04-1.12 -> 1.06-1.15. The
            // closed-loop brake demand and the full-lock understeer reflex
            // both exist specifically to convert an over-ambitious entry into
            // time loss instead of a wall, so entry judgment can afford a
            // touch more ambition than the immediate post-crash tuning.
            // True-ceiling round: 1.06-1.15 -> 1.07-1.18. Elite entry
            // judgment carries more speed over the apex number and trail-
            // brakes it off inside the corner - time loss on a misjudgment,
            // never a wall (brake saturation + speed-shed catch it).
            // The Mathf.Min that used to be here was dead code, and it hid a real
            // gameplay defect. Its first argument was
            // apexTargetSpeed * driverPaceVariance * profile.paceMultiplier, whose
            // smallest possible multiplier is 1.98 * 1.00 * 0.93 * 1.77 = 3.26,
            // against a feasibilityCap of at most 1.18 - so the cap ALWAYS won and
            // driverPaceVariance, carPaceVariance and profile.paceMultiplier were
            // completely inert in the corner-entry target. That is why round after
            // round of "+2% paceMultiplier" tuning changed nothing, and why picking
            // a higher difficulty did not make the field corner any faster.
            //
            // Difficulty now reaches corner speed through the feasibility cap, which
            // is the bounded, physically-meaningful lever (a better driver carries
            // more entry speed over the geometric apex and trail-brakes it off).
            // Normalised so Expert is exactly 1.0 - i.e. the top tier's corner speed
            // is unchanged from the tuned behaviour - and only the lower tiers slow
            // down. Driver-to-driver spread still comes from paceNorm, as before.
            float tierNorm = Mathf.InverseLerp(EasyPaceMultiplier, ExpertPaceMultiplier, profile.paceMultiplier);
            float difficultyCornerScale = Mathf.Lerp(0.93f, 1f, tierNorm);
            float feasibilityCap = Mathf.Lerp(1.07f, 1.18f, paceNorm) * difficultyCornerScale;
            float brakingApexSpeed = apexTargetSpeed * feasibilityCap;

            float damagePercent = vehicle.Damage == null ? 0f : vehicle.Damage.OverallPercent;
            float damageMultiplier = AiDamagePaceMultiplier(damagePercent);
            cruiseTargetSpeed *= damageMultiplier;
            brakingApexSpeed *= damageMultiplier;

            // Safety car / VSC pace clamp (Part 5): under a full safety car every car
            // targets the same absolute delta speed; under VSC every car is clamped to
            // the same absolute legal limit too. straightTargetSpeed is clamped too so
            // the braking-point math downstream (which reasons from it) stays
            // consistent.
            //
            // VSC pace-cap fix: this used to multiply already-scaled normal-racing
            // pace by a flat 0.62 - a proportional CUT with no relationship to the
            // actual legal VSC limit (RaceManager.VirtualSafetyCarSpeedCapKph, the
            // same 190 kph the player's and every AI car's shared physical hard
            // limiter already enforces in VehicleController.ApplyForces). On a fast
            // straight that put AI well below the real limit (far too slow); in a
            // slow corner it barely reduced anything (barely slowed at all). Clamping
            // directly to the absolute cap - exactly like the full-SC branch above -
            // means AI pace now agrees with the actual posted limit everywhere on
            // track, never drifting arbitrarily far under it.
            //
            // Round 2 - still too slow under caution: a pure Mathf.Min only ever
            // LOWERS the target if it happened to be above the cap - it never
            // raises the car up to the cap when its own normal-pace target was
            // already below it, which is the common case: straightTargetSpeed
            // has a large flat historical reduction baked in a few lines above
            // (calibrated for normal green-flag racing pace, nothing to do with
            // the posted SC/VSC limit), and driverPaceVariance/paceMultiplier/
            // damageMultiplier can each pull the number down further. The net
            // result was AI routinely cruising well under the actual limit on
            // a straight, not right up against it the way a real SC/VSC convoy
            // does. On a straight (or any section not meaningfully cornering),
            // pace now targets the cap directly instead of whatever the
            // (possibly much lower) normal-racing number happened to be; a
            // genuine corner's apex speed is still only ever capped DOWN to the
            // limit, never forced up to it - taking a real corner at the SC/VSC
            // delta would be unrealistic and unsafe. driverPaceVariance/
            // paceMultiplier/damageMultiplier are still applied on top so the
            // field doesn't all bunch at one identical number and a genuinely
            // damaged car can still legitimately fall short of the cap.
            // Round 3 - still crawling everywhere but the dead-straights: the
            // Round 2 fix above only ever capped apexTargetSpeed DOWN toward
            // paceCap via Mathf.Min, but apexTargetSpeed itself was derived
            // (several lines up, through EstimateApexSpeedForCornerType) from
            // straightTargetSpeed - which by this point already has the large
            // flat "Round 1-15" historical discount baked in (~92kph, tuned
            // only for unrestricted green-flag pace, with no relationship to
            // the posted SC/VSC/yellow limit). So Min(alreadyCrushedNumber,
            // paceCap) just kept returning the crushed number almost
            // everywhere a corner existed, i.e. everywhere except a pure
            // straight - exactly the reported "crawling in every corner"
            // behaviour. The apex reference now gets rebuilt from the real
            // legal cap instead of the discounted one, so cappedApexTargetSpeed
            // is genuinely close to the posted limit, only pulled down by
            // actual corner severity - never by an unrelated straight-line
            // pace discount. This also now covers a local yellow sector, not
            // only full/virtual safety car, via the same canonical cap
            // (RaceControlSpeedCapKphFor) the physical hard limiter already
            // uses for every car.
            float raceControlCap = raceManager.RaceControlSpeedCapKphFor(participant);
            if (raceControlCap < 9000f)
            {
                float paceCap = raceControlCap;

                float trueApexSpeedUnderCap = EstimateApexSpeedForCornerType(upcomingCornerType, paceCap, hairpinSpeedKph, apexConfidence, skillTier, compoundSpeedOffsetKph, corneringEnvelopeKph);
                float apexTargetSpeedUnderCap = Mathf.Lerp(trueApexSpeedUnderCap * 0.9f, trueApexSpeedUnderCap, apexConfidence);

                straightTargetSpeed = paceCap;
                float cappedApexTargetSpeed = Mathf.Min(apexTargetSpeedUnderCap, paceCap);
                // Under a race-control cap the target is the CAP - it must never be
                // scaled back up by the pace multipliers.
                //
                // This used to multiply by driverPaceVariance * profile.paceMultiplier,
                // whose minimum product is ~3.26, so a 190 kph VSC cap became a ~620
                // kph target. speedGap never approached zero, throttle stayed pinned
                // at 1.0, and enforcement fell entirely to the physical limiter -
                // which is deliberately soft under a race-control cap (<=5.5 m/s^2),
                // taking ~7 seconds to coast down from 330. Meanwhile the player's
                // throttle is cut to zero and brake applied immediately, and only the
                // player can collect the "Ignored safety car pace" penalty. Every
                // caution therefore handed the entire AI field free time over the
                // player. Note the unlike-for-like with the green-flag branch above:
                // that one has a feasibility Min, this one had no clamp at all.
                //
                // Damage still slows a hurt car, but nothing may exceed the cap.
                cruiseTargetSpeed = Mathf.Min(
                    Mathf.Lerp(straightTargetSpeed, cappedApexTargetSpeed, severityHere) * damageMultiplier,
                    paceCap);
                brakingApexSpeed = Mathf.Min(cappedApexTargetSpeed * damageMultiplier, paceCap);
                paceCapRecoveryBoostTimer = PaceCapRecoveryBoostSeconds;
            }
            else if (paceCapRecoveryBoostTimer > 0f)
            {
                paceCapRecoveryBoostTimer -= Time.deltaTime;
            }

            // Tight-corner physics ceiling (per report - "cars cant turn
            // enough on the final turn of belgium which sends them into the
            // wall"): the corner-speed buckets are severity-classified, and a
            // near-hairpin that measures just under the 168-degree Hairpin
            // threshold lands in VeryTight - a near-straight-line-speed tier.
            // But speed through a bend is bounded by GEOMETRY: v = sqrt(a*r).
            // The relaxed final corner has a ~26-30m radius; no classification
            // bucket may target a speed the measured radius cannot physically
            // support, or the car simply understeers into the outside wall
            // exactly as reported. Applied ONLY to genuinely tight geometry
            // (radius under ~61m, i.e. >30 degrees of heading change across
            // the 32m sampling baseline) with a generous 30 m/s^2 lateral
            // budget, so flowing/medium corners and their tuned bucket speeds
            // are completely untouched. Difficulty-independent, machinery-fair
            // (it is the corner's radius, identical for everyone).
            // Skill-scaled lateral budget (season-standings report - the flat
            // 30 m/s^2 budget was a hidden EQUALIZER: at every tight corner
            // all 22 drivers were capped to the identical geometric speed, so
            // exactly the corners where skill should separate the field
            // compressed it instead). The ceiling is a driver's JUDGEMENT of
            // how much of the physical budget to use - a backmarker leaves
            // real margin, an elite driver runs the geometry's edge. The top
            // of the band (30) is unchanged, so nobody exceeds what the
            // radius physically supports; difficulty-independent as always.
            // Envelope rebase: the flat 24-30 m/s^2 lateral budget predates the
            // shared yaw envelope and was BOTH the wrong shape and the wrong
            // size once that landed - it capped the AI to ~2.4-3g in exactly
            // the tight corners where the player (on the same physics) can
            // hold ~4-5g, quietly handing the player a free gap at every tight
            // corner. The safety-net role stays (it reacts to LOCAL heading
            // and so catches geometry the apex classifier misreads), but the
            // number now comes from the same MaxCorneringSpeedKph the apex
            // targets use, with the same judgment-fraction idea: a backmarker
            // leaves margin, an elite driver runs the geometry's edge.
            // Corner-EXIT push fix (per report - "the AI really find the wall
            // after basically every turn"): both geometric caps were gated
            // behind LocalHeadingChange > 30 degrees, i.e. hairpin-tight
            // geometry only. On every ordinary corner the moment the apex
            // passed, apexDistanceAhead snapped to the NEXT corner, the
            // cruise target snapped to straight-line speed, and the car
            // floored the throttle while still mid-arc - the old ~13-16g
            // authority forgave that, the realistic envelope does not, so
            // cars pushed wide into the wall on the way out of nearly every
            // turn. The caps are now ALWAYS-ON geometric governors built on
            // the measured centreline radius: on a straight the radius reads
            // ~9999 and the caps are far above any speed (a no-op), and in or
            // exiting a corner they bind exactly as hard as the remaining arc
            // demands. The cruise cap samples the tightest radius the car is
            // about to occupy (here / +18m / +36m) so exit throttle ramps as
            // the road genuinely opens instead of the instant the apex tag
            // moves on. Judgment band trimmed (0.88-0.99 -> 0.85-0.94): at
            // 0.99 the elite tier had no margin left for the understeer
            // feedback that trims yaw authority under full steering load,
            // which is exactly where "finds the wall" lives.
            // Trimmed again 0.85-0.94 -> 0.82-0.90 (barrier-retirement round):
            // at 0.94 the top tier's corner-exit margin against the understeer
            // feedback (which trims yaw authority ~8-10% under combined
            // steering + exit throttle) was ~zero, so elite cars still drifted
            // to the wall on exits. 0.90 leaves a real reserve at every tier.
            // Restored 0.82-0.90 -> 0.87-0.95 (per report - "AI still way too
            // slow on entry and overall"): both trims above predate the
            // layered protections that now exist downstream - the closed-loop
            // brake-demand controller, the full-lock understeer speed-shed
            // reflex, the corner-keyed sweeper margin, the reworked edge
            // recovery, and wall contact being survivable (glance physics +
            // rescue instead of retirement). With those nets in place this
            // flat 10-18% everywhere-cut was the main thing dragging corner
            // pace; the band comes most of the way back while staying under
            // the pre-trim 0.88-0.99 that genuinely crashed cars.
            // Round 2 ("the AI are WAY too easy - they simply dont have the
            // pace"): 0.87-0.95 -> 0.91-0.98. This is the geometric cap that
            // binds nearly every corner (the pace-multiplier targets sit far
            // above it), so it is the honest lap-time lever. The elite tier
            // now runs at 98% of the physics envelope with the reflex/edge/
            // rescue nets as the backstop; the crash-prone pre-rework 0.99
            // ceiling stays untouched.
            // Floor 0.91 -> 0.94 ("make them faster naturally"): the spread
            // was double-counted - gripUtilization already scales the physics
            // envelope by skill, and this judgment band then took a second
            // skill-scaled bite out of the already-reduced envelope. The
            // ceiling stays 0.98; the floor compresses so the field's corner
            // pace is respectable at every tier.
            // Ceiling 0.98 -> 0.995 (per report - the player was TOYING with
            // the elite tier; the "matching" laps were the player's cruise
            // pace, not their limit): the best drivers now run at 99.5% of
            // the physics envelope. The reflex/edge/rescue nets carry the
            // residual risk; this is driving commitment, not extra machinery.
            float tightCornerJudgment = Mathf.Lerp(0.94f, 0.995f, paceNorm);
            // [SwerveTape] round 3 (the fast-sweeper crash signature): every
            // wall-hit tape now shows the same thing - sev 0.1-0.35 corners
            // taken at 350-390kph with the final steer PINNED at full lock for
            // seconds (|steer|>0.98 up to 37% of the window) and no braking:
            // the geometric caps below said the speed was fine, but they
            // measure the CENTRELINE radius while the car actually follows
            // the racing line, whose entry/exit sweeps are meaningfully
            // tighter, and at 350+ the envelope leaves zero reserve for that
            // difference. Above ~260kph the caps now demand a genuine margin
            // (up to 14% by 380kph) - which also means these sweepers become
            // real braking zones instead of flat-out zero-margin passes.
            // Round 4: 0.86 -> 0.78 and engages from 240kph (13/22 OUT - the
            // tapes still showed cruise sitting at 345-355 straight through
            // sev 0.15-0.3 sweepers; the previous margin never actually bound).
            // Round 5 (per report - "AI extremely slow going into corners"):
            // rounds 3/4 keyed the margin off the CAR'S CURRENT speed, so a
            // hairpin approached at 340kph had its apex target cut by the full
            // sweeper margin too - every braking zone entered from speed ended
            // in a crawl. The margin's entire justification is fast sweepers
            // whose racing line runs tighter than the measured centreline
            // radius; it is now keyed off the CORNER'S own uncapped speed
            // instead, so slow and medium corners are untouched while genuine
            // 260+ sweepers keep the reserve that ended the wall crashes.
            float apexGeoRadius = track.CurvatureRadiusAt(progress.distance + apexDistanceAhead);
            float rawApexCap = vehicle.MaxCorneringSpeedKph(apexGeoRadius) * tightCornerJudgment;
            brakingApexSpeed = Mathf.Min(brakingApexSpeed, rawApexCap * SweeperCorneringMargin(rawApexCap));

            // Hairpin-entry crawl fix (per report - "the ai are WAY too slow
            // going into hairpins"): this used to take the TIGHTEST radius of
            // here/+18m/+36m and clamp cruise to that corner's final speed
            // outright - so the instant a hairpin entered the 36m look-ahead,
            // the car was commanded to already BE at hairpin apex speed,
            // finishing its braking ~40m early and crawling the entire
            // approach. A corner N metres ahead only justifies the speed you
            // can no longer brake down from: each look-ahead sample now gets a
            // kinematic braking allowance (v^2 = vCap^2 + 2*a*s at a
            // conservative 24 m/s^2 - well under the planning decel, so the
            // real braking model stays the binding authority in the zone).
            // The here-sample keeps its exact cap, so mid-corner speed is
            // unchanged; only the premature clamp on approach is gone.
            float cruiseGeoCapKph = float.MaxValue;
            for (int cruiseSample = 0; cruiseSample < 3; cruiseSample++)
            {
                float sampleAhead = cruiseSample * 18f;
                float sampleCapKph = vehicle.MaxCorneringSpeedKph(track.CurvatureRadiusAt(progress.distance + sampleAhead)) * tightCornerJudgment;
                sampleCapKph *= SweeperCorneringMargin(sampleCapKph);
                float sampleCapMs = sampleCapKph / 3.6f;
                float allowedNowKph = Mathf.Sqrt(sampleCapMs * sampleCapMs + 2f * 24f * sampleAhead) * 3.6f;
                cruiseGeoCapKph = Mathf.Min(cruiseGeoCapKph, allowedNowKph);
            }

            cruiseTargetSpeed = Mathf.Min(cruiseTargetSpeed, cruiseGeoCapKph);

            UpdateMistake(consistency, aggression, profile);
            UpdateOvertakeState(progress, severityHere, apexDistanceAhead, apexSeverity, turnSign, aggression, overtaking, defending, profile, isExpert, driver);

            // AI pit-entry bugfix: pit-entry intent has to be known BEFORE the
            // off-track recovery decision below, not after it (as it used to be,
            // computed much further down at the old `committingToPit` site) -
            // otherwise the moment a car actually starts crossing the track edge
            // toward the pit ramp, the off-track check below fires first and wins,
            // steering it straight back toward the centerline and defeating the
            // whole pit-entry approach. The pit-entry ramp is intentionally outside
            // the normal racing surface, so a car genuinely committing to it must
            // never be classified as needing normal off-track recovery. Also covers
            // the moment it's physically on the built ramp itself
            // (Track.IsOnPitEntryRamp), which can briefly still read pitPhase ==
            // None right before RaceManager's own tick promotes it to Entry.
            //
            // Deterministic-deadlock fix: this used to stay true across the whole
            // broad IsInPitApproach range (0.78-0.955) and had no missedPitEntryThisLap
            // gate at all - a car that ran out of the real 0.850-0.885 opening got
            // its request re-armed by any of the automatic pit triggers below (still
            // gated per-trigger further down), stayed inside this same broad window,
            // and resumed steering toward a pit lane target through a divider wall
            // that had already physically closed. committingToPit can now only be
            // true up to the REAL physical opening (PitCorridorStartNormalized), and
            // never at all while missedPitEntryThisLap is set for this lap.
            bool committingToPit = participant.pitPhase == PitPhase.None && vehicle.PitRequested &&
                                    !participant.missedPitEntryThisLap &&
                                    progress.normalized > TrackRuntime.PitApproachStartNormalized &&
                                    progress.normalized <= track.PitCorridorStartNormalized;
            bool onPitEntryRamp = track.IsOnPitEntryRamp(progress);
            bool suppressOffTrackRecovery = committingToPit || onPitEntryRamp;

            // Off-track recovery: drive straight back toward the centerline at reduced pace
            // instead of chasing the racing line offset from the grass.
            // Uses the actual (possibly hairpin-widened) drivable half-width at this
            // point on track, not the flat field - otherwise AI would treat the extra
            // tarmac at a widened hairpin as off-track and brake/recover exactly where
            // the widening was meant to give it more room to work with.
            bool offTrack = !suppressOffTrackRecovery && Mathf.Abs(progress.lateralDistance) > LocalHalfWidthAt(progress.distance) + 0.6f;
            if (offTrack)
            {
                cruiseTargetSpeed = Mathf.Min(cruiseTargetSpeed, 118f);
                brakingApexSpeed = Mathf.Min(brakingApexSpeed, 118f);
                aggressionOffset = 0f;
                mistakeSteer = 0f;
            }

            // Pit-entry speed target: while genuinely committing to the box, aim for
            // something close to the real pit-limiter speed (VehicleController's own
            // 80kph hard cap applies once RaceManager's HandlePitService puts the
            // limiter on) instead of whatever the corner/straight model alone would
            // ask for - without this an AI approaching the ramp on a fast straight
            // still targets full straight-line pace right up until the hard limiter
            // physically clamps it, leaving no room to actually turn in before the
            // ramp runs out.
            //
            // Limiter-consistency note: this is intentionally NORMAL approach
            // braking, not an active limiter - committingToPit (and this target)
            // starts at TrackRuntime.PitApproachStartNormalized (0.78), well
            // before the single shared hard-limiter boundary
            // (Track.HasCrossedPitEntryLimiterLine / PitEntryLimiterLineNormalized,
            // ~0.85, the same one RaceManager.HandlePitService uses for both the
            // player and the AI). This never sets PitLimiterActive or shows "PIT
            // LIMITER" itself - it just means a well-driven AI already arrives at
            // that line under 95 km/h instead of getting hard-clamped into it.
            // Pit-approach braking envelope (per report - "why are some AI
            // allowed to enter the pits later than I am? They're at full racing
            // speed for a solid one more corner"): the old flat 90 km/h target
            // engaged the moment committingToPit did - which for an AI whose
            // pit request latched EARLY meant crawling the entire 0.78 -> ramp
            // stretch, while an AI whose wear/strategy trigger happened to fire
            // MID-approach kept racing until it latched. The player assist had
            // the same flat-target shape, so who got slowed where was down to
            // request timing, not driving. Both sides now use the same rule a
            // real driver uses: race at full pace until the kinematic braking
            // point for the ramp, then brake once at a normal rate. The
            // envelope reaches the entry target 80 m before the physical ramp
            // start (positioning buffer), and the player's assist mirrors this
            // exact same envelope (RaceManager.BuildPitEntryAssistCommand) so
            // the two brake at the same point by construction. Corner targets
            // still win via the Min below, and the shared hard limiter on the
            // ramp itself is untouched.
            const float PitApproachTargetSpeedKph = F1Game.Race.Rules.PitServiceRules.PitLaneSpeedLimitKph;
            const float PitApproachBrakeDecelMs2 = 10f;
            // Buffer raised 80 -> 140m (matching the player assist): lateral
            // positioning happens entirely at the settled target speed.
            const float PitApproachRampBufferMetres = 140f;
            if (committingToPit)
            {
                float metresToRamp = (track.PitEntryRampStartNormalized - progress.normalized) * track.length;

                // Late-decision abort (per report - "the AI cars are still able
                // to dive into the pits a lot faster than I am"): a request
                // that genuinely latched too late to make the ramp is treated
                // as missed - the AI stays at race pace and pits next lap
                // (missedPitEntryThisLap suppresses the request until the lap
                // completes, and clears automatically).
                // Only reconsider while there is still real distance to the
                // ramp. Once the car is essentially at the mouth (<= 25m), a low-
                // grip car that could not slow in time must ENTER hot (the pit
                // limiter then clamps it on the ramp) rather than abort and take
                // a 0-stop penalty - being locked out of the pits is never the
                // right answer once you have physically arrived at the opening.
                if (metresToRamp > 25f)
                {
                    // Abort criterion rebuilt (per the Britain [PitDiag] wall of
                    // "aborted pit entry" lines at 130-380 kph and the 0-stop
                    // field it produced): the old test demanded reaching the
                    // 90 kph target BY the ramp start at <= 13 m/s^2 - but as
                    // metresToRamp shrinks, that ratio blows up on ANY excess
                    // speed, so a car at 130 kph 4 m from the mouth (trivially
                    // makeable - the ramp itself is 70m+ of braking room at the
                    // limiter) was scored "too late" and looped another lap.
                    // Worn/wet tyres made it worse: the envelope assumed a decel
                    // the physics couldn't deliver, cars converged on the target
                    // a beat late every lap, and the whole field ground its
                    // tyres to 0.00 aborting forever. The honest question is
                    // feasibility: braking at the decel THIS car can actually
                    // pull right now, how fast is it still going when the ramp
                    // arrives? Only a genuinely unmakeable entry (arrival above
                    // the speed the ramp turn-in can physically accept) aborts.
                    float speedMs = speedKph / 3.6f;
                    float panicDecel = 26f * Mathf.Clamp(vehicle.LiveBrakingGripFactor, 0.35f, 1.05f);
                    float arrivalSq = speedMs * speedMs - 2f * panicDecel * metresToRamp;
                    float arrivalKph = arrivalSq > 0f ? Mathf.Sqrt(arrivalSq) * 3.6f : 0f;
                    const float MaxMakeableRampArrivalKph = 170f;
                    if (arrivalKph > MaxMakeableRampArrivalKph)
                    {
                        // Mirror RaceManager.HandlePitService's miss bookkeeping:
                        // record the lap so UpdateMissedPitEntryReset only clears
                        // the flag once a genuinely new lap has started, and drop
                        // the latched request so nothing reads it as still live.
                        participant.missedPitEntryThisLap = true;
                        participant.missedPitEntryCompletedLap = participant.lapTracker != null ? participant.lapTracker.CompletedLaps : 0;
                        vehicle.ClearPitRequest();
                        committingToPit = false;
                        // Was completely silent - a whole field of cars can 0-stop
                        // through this exact branch (see the pre-window envelope
                        // comment below) without a single line of evidence.
                        // Debug.LogWarning, not GameLog.Warn - GameLog is gated
                        // behind the Verbose flag and this log exists precisely
                        // to be visible when the field 0-stops.
                        Debug.LogWarning("[PitDiag] " + participant.driverName + " aborted pit entry (unmakeable): speed=" +
                                         speedKph.ToString("0") + "kph metresToRamp=" + metresToRamp.ToString("0") +
                                         "m projectedArrival=" + arrivalKph.ToString("0") + "kph brakeGrip=" +
                                         vehicle.LiveBrakingGripFactor.ToString("0.00"));
                    }
                }
            }

            // 0-stop root fix (per report - "a lot of cars seemed like they
            // were diving into the pits and just swerved off last second"):
            // this envelope used to be gated on committingToPit, which only
            // becomes true at the approach window (0.78) - the AI was FORBIDDEN
            // from starting its pit braking any earlier. After the pace-buff
            // rounds pushed straight-line speeds to 350-365 kph, the window
            // itself became kinematically too short: from 360 kph, making the
            // ramp needs ~13.4 m/s^2 at the very moment committingToPit first
            // turns true, which is already OVER the 13 m/s^2 late-decision
            // abort threshold below - so every fast car silently aborted the
            // instant it committed, retried next lap at the same speed,
            // aborted again, and finished on 0 stops with a penalty. The
            // envelope now engages from the moment a pit request is LIVE,
            // keyed purely on wrapped distance-to-ramp: far from the ramp it
            // allows any speed (sqrt grows unbounded), so this changes nothing
            // for most of the lap - it just means a car that already knows it
            // is pitting begins its one normal 10 m/s^2 braking zone at the
            // physically correct point, exactly like a real driver, instead of
            // holding 360 into an approach it can no longer make. The abort
            // check stays: it now only catches requests genuinely latched too
            // late (mid-approach wear/strategy triggers), its original purpose.
            bool pitEntryPlanned = participant.pitPhase == PitPhase.None && vehicle.PitRequested &&
                                    !participant.missedPitEntryThisLap;
            float pitApproachBrakeDemand = 0f;
            if (pitEntryPlanned)
            {
                float rampStartDistance = track.PitEntryRampStartNormalized * track.length;
                float rampWindowMetres = Mathf.Max(0f, (track.PitCorridorStartNormalized - track.PitEntryRampStartNormalized) * track.length);
                float metresToRampWrapped = rampStartDistance - progress.distance;
                if (metresToRampWrapped < 0f && metresToRampWrapped >= -(rampWindowMetres + 30f))
                {
                    // Physically at/inside the ramp opening while still lining up
                    // the commit - hold the entry target speed (the old in-window
                    // code's behaviour), don't wrap to "next lap's ramp" and
                    // release the cap right at the ramp mouth.
                    metresToRampWrapped = 0f;
                }
                else if (metresToRampWrapped < 0f)
                {
                    metresToRampWrapped += track.length;
                }

                float envelopeDistance = Mathf.Max(0f, metresToRampWrapped - PitApproachRampBufferMetres);
                float pitTargetMs = PitApproachTargetSpeedKph / 3.6f;
                // Grip-scaled envelope (the other half of the Britain 0-stop
                // loop): the 10 m/s^2 plan assumed healthy tyres, but a car on
                // worn wets on a drying track brakes at a fraction of that -
                // it followed the envelope's line perfectly and still arrived
                // hot every lap. The braking point now moves earlier in exact
                // proportion to what the tyres can actually deliver, same as
                // the corner-braking planner already does via decelReference.
                float envelopeDecel = PitApproachBrakeDecelMs2 * Mathf.Clamp(vehicle.LiveBrakingGripFactor, 0.4f, 1f);
                float pitEnvelopeKph = Mathf.Sqrt(pitTargetMs * pitTargetMs + 2f * envelopeDecel * envelopeDistance) * 3.6f;
                // Queue behind any pit-bound car ahead (per report - cars
                // "overtake each other while being in animation"): the shared
                // headway cap the player assist also obeys, so an AI arriving
                // at racing pace follows the queue instead of driving past or
                // through a slower car already committed ahead of it. Only
                // applied near the approach itself - a pit-bound car 80m ahead
                // on the back straight is just racing traffic, not a pit queue.
                if (metresToRampWrapped < 480f)
                {
                    pitEnvelopeKph = Mathf.Min(pitEnvelopeKph, raceManager.PitApproachHeadwayCapKph(participant));
                }

                // Parity with the player assist (per report - "they are clearly
                // faster than me"): the player's approach obeys a corner-speed
                // cap through the pit window; the AI's own corner model targets
                // much higher speeds through the same bends, so the AI both
                // out-paced the player on the approach and carried corner speed
                // into the pre-position line (the same corner-at-speed wall
                // risk the assist had). Identical rule, identical window.
                if (track.IsInPitApproach(progress.normalized))
                {
                    pitEnvelopeKph = Mathf.Min(pitEnvelopeKph, raceManager.PitApproachCornerCapKph(progress.distance));
                }
                cruiseTargetSpeed = Mathf.Min(cruiseTargetSpeed, pitEnvelopeKph);
                brakingApexSpeed = Mathf.Min(brakingApexSpeed, pitEnvelopeKph);

                // THE actual fix behind the [PitDiag] mass-abort report (every
                // abort at exactly 13.0-13.1 m/s^2, still at 353-377 kph): the
                // two target caps above only LIFT THE THROTTLE. The AI's brake
                // pedal is driven by the corner-braking model, which fires only
                // when a genuine corner apex is within braking distance - on a
                // straight pit approach there is no apex, so the car COASTED
                // toward the ramp on drag alone, requiredDecel crept up through
                // the abort threshold, and the whole field bailed lap after
                // lap. The envelope now produces a direct brake demand of its
                // own: proportional to how far the car is above the envelope
                // speed, folded into brakeDemand below alongside the corner
                // model and the edge emergency brake.
                pitApproachBrakeDemand = Mathf.Clamp01((speedKph - pitEnvelopeKph) / 40f);
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
            // Wobble halved (per "not fully following the optimal line"): the
            // per-driver line noise is a human-imperfection wander, but at full
            // strength it kept the cars visibly off the ideal line even on a clean
            // lap. Cut to 0.5x so they sit much closer to the optimal line while
            // still not being robotically perfect.
            float wobble = (Mathf.PerlinNoise(noiseSeed, Time.time * 0.5f) * 2f - 1f) * profile.lineOffsetNoise * 0.5f;
            float lineBias = 0f;
            // Optimal-line pass v2: pursue the PRECOMPUTED racing line
            // (TrackRuntime.ComputeRacingLine - the deterministic out-in-out
            // construction with apexes pinned to the wall/kerb-safe corridor,
            // validated offline). Both previous approaches were structurally
            // wrong:
            // - The reactive severity heuristic computed its offset from
            //   conditions AT THE CAR but applied it at the 22-62m lookahead
            //   target point, so the whole line ran ~1-2s late; and its
            //   entry-setup phase keyed off FindUpcomingApex's SHARPEST corner
            //   ahead, so through chicanes/S-complexes it set up wide for the
            //   second element while driving straight past the first - visibly
            //   swinging AWAY from the apex the car was actually entering.
            // - The original pure-pursuit of the drawn line was reverted
            //   because the line's full-outside gates sit at halfWidth-1.8m,
            //   INSIDE the edge-emergency-brake's 2.4m ramp band, so cars
            //   edge-rode under constant brake trims and crawled.
            // This version samples the drawn line AT the same lookahead
            // distance the steering target uses (no phase lag, chicanes come
            // from real geometry, not a heuristic) and caps the offset at
            // halfWidth-2.6m - outside the emergency-brake ramp band by
            // construction - with a small scale-down for permanent air to the
            // kerb at apexes. The heuristic remains only as a fallback for
            // degenerate layouts where no line was computed.
            float apexMissNoise = (Mathf.PerlinNoise(noiseSeed + 37.1f, progress.distance * 0.015f) * 2f - 1f) * perCarApexError;
            bool hasDrawnLine = track.racingLineOffsets != null && track.racingLineOffsets.Length > 0;
            if (hasDrawnLine)
            {
                float drawnOffset = track.RacingLineOffsetAt(progress.distance + lookAhead);
                lastDrawnOffset = drawnOffset;
                hasLastDrawnOffset = true;
                float edgeSafeBound = LocalHalfWidthAt(progress.distance + lookAhead) - 2.6f;
                float bound = Mathf.Max(0.75f, Mathf.Min(edgeSafeBound, legalLimit));
                // THE START-PACK COLLISION FIX (root cause of the bunching):
                // the racing line is a single one-car-wide path, and pursuing
                // it unconditionally ordered every car in a dense pack to merge
                // laterally onto the same ribbon - on the opening straight,
                // as the grid fan-out decayed, a two-wide field of 22 was
                // forced to become single file at pack density. Cars converged
                // INTO each other, spun (the backwards-facing cars in the
                // reports), and the wreck then jammed into the stopped bunch.
                // No amount of braking/creep tuning could fix that, because
                // braking was never the cause - the lateral merge order was.
                // Real drivers only take the racing line in CLEAR AIR; in
                // traffic they hold their lane and position tactically. So:
                // blend between pursuing the optimal line (clear air) and
                // holding the car's current lateral lane (traffic - the
                // overtake state machine's aggressionOffset still layers its
                // own tactical moves on top). The blend is smoothed both ways
                // so a neighbour flickering at a window edge never snaps the
                // target, and eases in traffic->clear slowly so the field
                // strings out before anyone cuts across to the ideal line.
                // Clear-air release 0.5 -> 1.2/s: at 0.5 a car needed 2 full
                // seconds of clear air to re-earn its racing line - in a pack
                // that never happens, so trafficBlend rode 1.00 permanently
                // (every [SwerveDiag] line confirms it) and the whole field
                // stayed off-line all race. Engagement stays fast (2.5/s) so
                // collision safety in a closing pack is unchanged; only the
                // return to pace when a gap opens is quicker (~0.8s).
                lineTrafficBlend = Mathf.MoveTowards(
                    lineTrafficBlend,
                    lineTrafficNearby ? 1f : 0f,
                    Time.deltaTime * (lineTrafficNearby ? 2.5f : 1.2f));
                // Straight-line weave trim ([SwerveDiag]: with the steering
                // controller settled, the remaining weave was the LINE itself -
                // on the long straights the drawn line still ordered cars to ride
                // the edge at ~+/-9.9m and drift back, a big slow lateral weave).
                // Ease the drawn offset toward centre ONLY where the road is
                // genuinely straight (severity ~0); it ramps back to the full
                // authored offset as a corner approaches, so corner entry / apex
                // lines are unchanged.
                // Floor dropped 0.55 -> 0.35 (per report - the swerve still crashes
                // the player into a wall). On a dead-straight the authored out-in-out
                // line has no reason to sit at +/-9.9m; that edge-riding IS the slow
                // wall-to-wall excursion the new [SwerveDiag amplitude] pass is built
                // to confirm. Pulling the straight-line target to a third of the
                // authored offset can only reduce lateral motion where severity ~ 0,
                // and the Lerp still restores the full line before any corner.
                // Floor 0.35 -> 0.22 and range widened /0.09 -> /0.12 ([SwerveDiag
                // amplitude] still flagged RACING LINE swings of 4-7m on sections it
                // classifies as straight, i.e. severity < 0.12). Pulling the drawn
                // offset to ~a fifth of its authored value across the whole straight
                // band keeps cars near centre where there is no corner to shape; the
                // Lerp still restores the full authored line by severity 0.12, before
                // any real corner turn-in.
                // Band widened /0.12 -> /0.2 ([SwerveDiag amplitude]: RACING LINE
                // verdicts persisted at sections with severity just under the old
                // 0.12 edge - there the ramp already restored ~80% of an 11m
                // edge-to-edge line, so lineBias still swung ~8m on road that looks
                // dead straight on screen. Full authority now returns by severity
                // 0.2, matching the lateral governor's own straightness band; real
                // corner turn-in (severity 0.18-0.34+) is past the ramp regardless.
                float straightTrim = Mathf.Lerp(0.22f, 1f, Mathf.Clamp01(severityHere / 0.2f));
                float optimalPursuit = drawnOffset * 0.94f * straightTrim + apexMissNoise * 0.4f;
                // Latched holding lane (see heldLaneOffset): the target the car
                // holds in traffic is a STABLE captured lane, not its live position.
                // A fixed lateral target gives pursuit a real centring pull (the car
                // returns to its lane instead of free-drifting), while each car
                // latching its own lane keeps the field spread out. Latch once when
                // traffic first appears and drop it in clear air; clamp so it can
                // never hold at the wall (a lane, not the barrier). The overtake
                // state machine's aggressionOffset still layers tactical moves on top.
                // Lane cap tightened 5.5 -> 2.5m ([SwerveDiag amplitude]: the held
                // lane latching near +/-5.5m and flipping sign was the dominant
                // straight-line weave, swinging lineBias ~11m peak-to-peak and
                // shoving cars toward the walls). A +/-2.5m spread still de-stacks
                // the field off the single racing line, but keeps the hold well
                // clear of the barriers; the overtake state machine's aggression
                // offset still layers real tactical moves on top for actual passes.
                float laneCap = Mathf.Min(bound, 2.5f);
                if (lineTrafficBlend > 0.05f)
                {
                    if (!hasHeldLane)
                    {
                        heldLaneOffset = Mathf.Clamp(progress.lateralDistance, -laneCap, laneCap);
                        hasHeldLane = true;
                    }

                    laneClearAirTimer = 0f;
                }
                else
                {
                    // Sticky release: keep the captured lane through brief traffic-
                    // blend dips (constant in a dense pack) and only drop it after
                    // sustained clear air, so the car does not re-latch a new,
                    // possibly wall-ward lane every time a gap flickers open.
                    laneClearAirTimer += Time.deltaTime;
                    if (laneClearAirTimer > 3f)
                    {
                        hasHeldLane = false;
                    }
                }

                float laneHold = hasHeldLane ? heldLaneOffset : progress.lateralDistance;
                // THE PACK-PACE FIX ([PaceDiag]: equal-pace AI ~4s/lap slower than
                // the player, whole field collapsing to ~2:00 laps with walls of
                // track-limit invalids; trafficBlend pinned at 1.00 in every log
                // line). Lane-hold applied at FULL strength in corners too, so a
                // car in traffic took every corner from its held lane instead of
                // the racing line - seconds per lap slower. And it self-reinforces:
                // 21 equally-slowed cars never string out, so nobody ever leaves
                // anyone's traffic window and the whole field stays off-line for
                // the entire race. Real racing is single file ON THE LINE through
                // corners; lane discipline matters on straights where cars run
                // side by side. So corner severity now bleeds authority back to
                // the optimal line even in traffic (down to ~35% lane-hold at
                // full corner severity) - cars carry proper corner speed, string
                // out, and traffic windows actually clear.
                float cornerLineAuthority = Mathf.Clamp01(severityHere / 0.2f);
                float effectiveTrafficBlend = lineTrafficBlend * Mathf.Lerp(1f, 0.35f, cornerLineAuthority);
                lineBias = Mathf.Clamp(Mathf.Lerp(optimalPursuit, laneHold, effectiveTrafficBlend), -bound, bound);
            }
            else if (severityHere > 0.05f)
            {
                float biasMagnitude = Mathf.Lerp(legalLimit * 0.6f, legalLimit, severityHere);
                float apexProximity = Mathf.Clamp01(1f - apexDistanceAhead / 50f);
                float lineShape = apexProximity * 2f - 1f;
                float apexPrecision = Mathf.Clamp01(1f - perCarApexError / 9f);
                float shapedSide = lineShape > 0f ? lineShape * apexPrecision * 0.88f : lineShape;
                lineBias = turnSign * biasMagnitude * shapedSide + (lineShape > 0f ? apexMissNoise : 0f);
            }

            // Slew proportional to FORWARD speed (~0.12m sideways per metre
            // travelled, floored so a slow car still converges): a time-based
            // slew let a slow car chase the drawn line's road-crossing moves
            // almost perpendicularly - at race-start speeds the whole pack
            // weaved hard across the road after the line's grid-area kink,
            // colliding and bunching. Distance-proportional tracking keeps the
            // crossing angle shallow at every speed (at 300 kph this is the
            // same ~10 m/s lateral pace as before; at 60 kph it is a calm 2).
            // Straight-line line-chase rate ([SwerveDiag]: confirmed on-screen weave
            // is on the LONG STRAIGHTS). Every prior fix clamped the target offset's
            // MAGNITUDE (straightTrim, laneCap) but left this tracking RATE hot, so
            // the car still chased the authored line's small along-track lateral
            // variation at up to ~8 m/s sideways - and because the line is sampled
            // 55m ahead, that chase is phase-led, i.e. a self-sustaining weave down
            // an otherwise dead-straight road. Ease the rate to a third where the
            // road is genuinely straight (severity ~0) so the car HOLDS a steady
            // line instead of sawing after every metre of line wiggle; corner
            // turn-in keeps the full rate so real lines are still taken. Monotone-
            // safe: a slower lateral rate can only reduce straight-line motion.
            float lineStraightness = 1f - Mathf.Clamp01(severityHere / 0.14f);
            float lineSlewRate = Mathf.Max(2f, speedKph / 3.6f * 0.10f) * Mathf.Lerp(1f, 0.33f, lineStraightness);
            smoothedLineBias = Mathf.MoveTowards(smoothedLineBias, lineBias, Time.deltaTime * lineSlewRate);
            lineBias = smoothedLineBias;
            previousSeverityHere = severityHere;

            float requestedOffset = wobble + lineBias + aggressionOffset + mistakeSteer;

            // Pit-entry fix: steer visibly toward the pit side under completely
            // normal driving well before the guided/kinematic entry phase can
            // ever trigger (RaceManager.BeginPitEntry now requires the car to be
            // physically on the built pit-entry ramp - see Track.IsOnPitEntryRamp)
            // - without this the car drove the ordinary racing line right up to
            // that distance threshold and the guided system then had to silently
            // snap it sideways, reading exactly like "the pit animation starts
            // before the car is anywhere near pit entry". Blended in only across
            // the approach window and only while a pit stop is actually requested
            // and not yet underway, so a car simply passing the pit entry on a
            // normal lap never gets pulled off-line.
            //
            // Pit-lane architecture fix: the target used to be HalfWidthAt * 0.82,
            // which is still INSIDE the racing surface - the AI never actually,
            // visibly left the track before the guided system took over. Now aims
            // just past the true track edge (Track.PitEntryApproachLateral, the
            // same envelope BuildPitRampSurface paves), and once deep enough into
            // the entry zone to physically commit, blends further in toward the
            // real ramp's own centerline (Track.PitEntryPathLateral) instead of a
            // fixed approach target - so the car visibly drives onto the actual
            // ramp, not just "somewhere off to the right".
            // Item 11: single flag for "actively steering off the racing surface
            // toward the pit entry right now" - reused below to (a) bypass the
            // normal legal-line/edge-recovery logic, which exists to keep the car
            // ON the racing surface and would otherwise fight this steering the
            // instant it crosses the true track edge, and (b) suppress
            // overtake/defend/mistake-steer commitment while committing to the box.
            // (committingToPit itself is now computed earlier, before the off-track
            // recovery decision above - see the comment there for why.)
            //
            // Pit-entry architecture fix: this used to weakly LERP requestedOffset
            // toward the pit target using blends keyed off 0.955 (approachBlend) and
            // 0.865 (lateEntryBlend) - neither matched the real physical ramp, which
            // only spans PitEntryRampStartNormalized (0.85) to
            // PitCorridorStartNormalized (0.885).
            //
            // Look-ahead fix: even after that rewrite, the LATERAL value was still
            // computed at the car's CURRENT distance (progress.distance) while the
            // POINT it was added to came from the normal racing-line lookahead
            // sample further down the track (progress.distance + lookAhead). Over
            // the real ramp's own short ~0.035-normalized span that mismatch could
            // be tens of metres of disagreement between the target point and the
            // lateral it was nudged by - exactly the kind of error that points a
            // car at the divider instead of the true ramp line. ComputePitEntryTargetPoint
            // now samples position AND lateral together, at the SAME distance,
            // using the canonical ramp envelope/pose helpers, and this replaces
            // targetPoint directly (a real world-space target, not an offset
            // grafted onto the ordinary racing-line point) further below.
            bool preEntryRampStage = committingToPit && progress.normalized < track.PitEntryRampStartNormalized;
            bool onEntryRampStage = committingToPit && !preEntryRampStage;
            Vector3 pitEntryTargetPoint = committingToPit ? ComputePitEntryTargetPoint(progress) : Vector3.zero;

            // Pit-exit early-turn fix: the guided PitPhase.ExitMerge itself
            // (RaceManager.UpdatePitRelease/UpdatePitExitMerge) carries the car all
            // the way through the real pit-exit merge geometry under kinematic
            // guidance. But normal racing-line/overtake/defend logic resuming the
            // instant guided control hands back can still dive for the next apex
            // more aggressively than is safe right after a merge - this short
            // post-merge hold (armed by RaceManager once ExitMerge completes) keeps
            // the car on the outer lane a little longer and decays smoothly, rather
            // than snapping straight back to full racing-line targeting.
            // (Guarded against committingToPit so a fresh pit-entry target can never
            // be overridden by a stale post-merge hold from a previous stop -
            // committing to a NEW entry always wins.)
            if (!committingToPit && (participant.pitExitLaneHoldTimer > 0f || participant.pitExitLaneHoldDistanceRemaining > 0f))
            {
                participant.pitExitLaneHoldTimer = Mathf.Max(0f, participant.pitExitLaneHoldTimer - Time.deltaTime);
                float distanceThisFrame = Mathf.Max(0f, vehicle.CurrentSpeedKph) / 3.6f * Time.deltaTime;
                participant.pitExitLaneHoldDistanceRemaining = Mathf.Max(0f, participant.pitExitLaneHoldDistanceRemaining - distanceThisFrame);

                float postMergeHalfWidth = LocalHalfWidthAt(progress.distance);
                float safeExitOffset = postMergeHalfWidth - 1.4f;
                requestedOffset = Mathf.Lerp(requestedOffset, safeExitOffset, 0.85f);
            }

            // Opening seconds: hold the assigned fan-out lane, blending back to the
            // racing line as the field strings out. (Guarded against committingToPit
            // for the same reason as the post-merge hold above.)
            //
            // Delayed line-seeking (per request): the old linear ramp started
            // easing back toward the racing line - including its tight apex
            // swings right against the wall - from the very first frame, so the
            // field was substantially back on the aggressive line within a
            // couple of seconds of a compressed, still-settling launch. Now holds
            // FULL fan-out strength for the first third of the window (a flat
            // plateau, not an immediate ramp-down) and only eases toward the
            // racing line over the remaining two-thirds, by which point the pack
            // has actually strung out.
            if (!committingToPit && raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.RaceElapsed < OpeningFanDuration)
            {
                float holdPhase = OpeningFanDuration * 0.35f;
                float fanBlend = raceManager.RaceElapsed < holdPhase
                    ? 1f
                    : 1f - Mathf.Clamp01((raceManager.RaceElapsed - holdPhase) / (OpeningFanDuration - holdPhase));
                // "AI taking longer to turn" fix: the fan-out hold had no idea
                // whether the car was actually approaching a real corner - on a
                // track whose first corner arrives inside the opening window
                // (common on the shorter/technical layouts), it kept forcing the
                // car toward its flat grid-lane offset right through the corner
                // instead of letting it turn in, reading as sluggish/late
                // steering. Fades the hold out with corner severity, so a genuine
                // corner always wins the fan-out lane hold, straights don't.
                // Bunching fix: this used to cancel the fan-out ENTIRELY in a
                // corner (x1-severity*2.5 -> 0), which snapped all 21 cars onto
                // the identical drawn racing line at turn 1 while the pack was
                // still dense - the funnel that produced the stopped bunch. 65%
                // of the hold still releases so cars genuinely turn in (the
                // original sluggish-steering fix is preserved), but a third of
                // the lane spread survives through the opening corners until
                // the window expires and the field has strung out.
                fanBlend *= 1f - Mathf.Clamp01(severityHere * 2.5f) * 0.65f;
                requestedOffset = Mathf.Lerp(requestedOffset, openingFanOffset, fanBlend);
            }

            // Pit-lane architecture fix: the legal-line clamp exists to keep normal
            // racing-line targeting on the drivable track surface - applying it here
            // while committingToPit would clamp the new off-track pit-entry target
            // straight back inside the racing surface, silently undoing the steering
            // above and leaving the car never actually, visibly leaving the track.
            //
            // Look-ahead fix: committingToPit now uses the dedicated
            // pitEntryTargetPoint (see ComputePitEntryTargetPoint above) directly as
            // the steering target, replacing targetPoint outright rather than
            // adding a lateral offset onto the ordinary racing-line lookahead point
            // - see the comment above pitEntryTargetPoint for why the old
            // offset-based approach could disagree with the real ramp geometry by
            // tens of metres.
            // ===== Unified straight-line lateral governor =====
            // ONE place that bounds every source of lateral motion on a straight -
            // racing line, traffic lane-hold, overtake offset, wobble and mistake
            // steer all funnel through requestedOffset, so governing it here catches
            // all of them at once instead of tuning each contributor separately.
            // Two guarantees on a genuine straight (severity ~0):
            //   1. CLAMP the net target to a safe band well inside the walls, so no
            //      combination of inputs can ever aim a car at a barrier.
            //   2. RATE-LIMIT how fast that target moves, so a car eases to one side
            //      once instead of weaving back and forth (the wall-to-wall swing
            //      the [SwerveDiag amplitude] pass kept reporting).
            // Both fade out as a corner approaches (straightness -> 0), so corner
            // turn-in and apex lines keep full authority. The rate limit acts on the
            // TARGET offset (kinematic), never inside the steering feedback loop, so
            // unlike a steer-side slew limiter it can never induce its own
            // oscillation. A car actively committed to a pass keeps a wider band so
            // genuine overtakes still have room to go side-by-side.
            if (!committingToPit)
            {
                bool committedPass = overtakeState == OvertakeState.AttackingInside
                    || overtakeState == OvertakeState.AttackingOutside
                    || overtakeState == OvertakeState.SideBySide
                    || overtakeState == OvertakeState.CompletingPass;
                // FIRM cap on all low/medium-curvature road, opening to the full
                // legal width only as a GENUINE corner arrives (severity 0.18 ->
                // 0.34). The previous version lerped the band down from the full
                // legal width starting at severity 0, so a gentle sweeper (severity
                // ~0.10) still allowed ~8m and cars traced the racing line
                // edge-to-edge - which, across a 20-car field in clear air, is the
                // "swerving really badly" the report describes. Now anything short
                // of a real corner is held to a lane a few metres off centre; the
                // apex line for actual corners is untouched. A car committed to a
                // pass keeps a wider band so it can still go side-by-side.
                // Never let the band exceed the wall-safety corridor: with the
                // barriers flush to the paved edge, legalLimit IS the last safe
                // lateral metre - a committed pass on a narrow track must not be
                // allowed to command a target beyond it.
                // Pass band trimmed 5.5 -> 4.2 (amplitude logs: overtakeOff
                // swings of 6.3-8.6m were the other big visible-swerve source;
                // a pass needs about a car width plus margin of lateral
                // clearance from the target's line, not five and a half
                // metres of dart).
                float baseBand = Mathf.Min(committedPass ? 4.2f : 3.5f, legalLimit);
                float cornerOpen = Mathf.InverseLerp(0.18f, 0.34f, severityHere);
                float safeBand = Mathf.Lerp(baseBand, Mathf.Max(baseBand, legalLimit), cornerOpen);
                requestedOffset = Mathf.Clamp(requestedOffset, -safeBand, safeBand);
                // Rate-limit the net target so a car eases across once instead of
                // hunting; kinematic (acts on the target, never inside the steering
                // loop) and faded in on straighter road so corner turn-in is crisp.
                float straightness = 1f - Mathf.Clamp01(severityHere / 0.2f);
                // Slew trimmed 0.07 -> 0.055 (exit-wall round): a full-speed
                // lane change now takes ~1.3s instead of ~1s - passes still
                // work, but an overtake target flip no longer darts the car
                // sideways faster than the realistic yaw envelope can
                // comfortably track, which was reading as traffic swerve.
                float netSlew = Mathf.Max(1.2f, speedKph / 3.6f * 0.055f);
                smoothedNetOffset = Mathf.MoveTowards(smoothedNetOffset, requestedOffset, Time.deltaTime * netSlew);
                requestedOffset = Mathf.Lerp(requestedOffset, smoothedNetOffset, straightness);
            }
            else
            {
                // Keep the smoothed state tracking the live target through the pit
                // sequence so it re-engages seamlessly afterwards.
                smoothedNetOffset = requestedOffset;
            }

            if (committingToPit)
            {
                targetPoint = pitEntryTargetPoint;
            }
            else
            {
                float desiredOffset = offTrack ? 0f : ConstrainLegalLineOffset(progress, requestedOffset, severityHere);
                targetPoint += right * desiredOffset;
                TrackProgress targetProgress = track.GetProgress(targetPoint);
                float legalTargetLimit = LegalOffsetLimit(severityHere, progress.distance);
                if (Mathf.Abs(targetProgress.lateralDistance) > legalTargetLimit)
                {
                    track.SampleAtDistance(targetProgress.distance, out targetPoint, out forward, out right);
                    targetPoint += right * Mathf.Clamp(targetProgress.lateralDistance, -legalTargetLimit, legalTargetLimit);
                }
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
            // Barrier-avoidance fix round 5: margin/response widened again (was
            // 4.2-9m / 0.3-1 recovery / 0.1-0.9 brake) - cars were still catching
            // barriers before the correction had built up enough authority to pull
            // them back, so both the distance this starts reacting at and how hard
            // it reacts once triggered are raised across the board.
            // Pit-lane architecture fix: this correction exists to pull a car back
            // from the true track edge toward the racing surface - it must be off
            // while committingToPit, or it would fight the pit-entry steering above
            // the moment the car's actual position crosses the edge (which is the
            // whole point of physically driving onto the entry ramp). Also off while
            // onPitEntryRamp (see suppressOffTrackRecovery above) for the same reason.
            // Track-width fix: edgeMarginDistance is a flat metre value, not scaled
            // to roadHalfWidth - after the earlier 45% track-width cut it could
            // approach or exceed half the actual track width on the tighter/street
            // circuits, so edgeMargin went to ~0 or negative and this correction
            // fired almost everywhere on track (not just genuinely near the edge),
            // reading as constant AI swerving/twitching rather than real barrier
            // avoidance. Track width has since been raised back up 20%, giving real
            // headroom back, so the margin ceiling and response strength are only
            // trimmed slightly here (not redesigned) to stop it overcorrecting now
            // that there's more room to work with.
            // Wall-aversion boost (per request): margin band widened well past
            // the earlier tuning (was 5.5-9.5m) so the reactive recovery/brake
            // system below starts reacting much further from the true edge,
            // buying real distance to correct before the barrier regardless of
            // what's actually causing cars to arrive there in the first place.
            // Round 2 (per request, small trim): eased back ~10% (was 8-14m).
            // Round 3 (per request): increase the complete wall-aversion response
            // by 5% without compounding separate sub-terms.
            // Round 4 (per request): a further 10% on top (1.05 -> 1.155), same
            // single-multiplier approach so the sub-terms never compound.
            // Round 5 (per request - "increase AI wall aversion now that tracks
            // have narrower sections"): the procedural width profile now pinches
            // to ~7m half-width in its narrow sections, so a wall arrives sooner
            // than the earlier tuning assumed. Bumped ~10% (1.155 -> 1.27) to
            // strengthen the steering correction and emergency brake near a wall.
            // Round 6 (per request - "buff ai wall aversion even more"): a
            // further ~12% (1.27 -> 1.42), still a single non-compounding
            // multiplier, so the reactive steer-away and brake fire harder and
            // earlier in the pinched sections.
            // Round 7 (per request - "up the wall aversion without breaking
            // the game"): ~13% more (1.42 -> 1.6). The "without breaking"
            // part is carried by the existing gates, all untouched: the
            // corridor gate (a car on its legal line reads ZERO urgency, so
            // no phantom braking returns), the launch gate (nothing fires
            // below 15kph) and the narrow-track width clamp.
            const float wallAversionMultiplier = 1.6f;
            float edgeMarginDistance = Mathf.Lerp(7.2f, 12.6f, Mathf.Clamp01(speedKph / 340f)) * wallAversionMultiplier;
            // Wall-crash defence-in-depth: near a known tight-fence corner (the
            // same containment data the barrier builder uses) the reactive edge
            // band starts wider still, so the emergency response begins while a
            // hot car still has room to shed speed - close street barriers leave
            // no run-off for the standard band at speed.
            if (track != null && track.IsNearTightFenceCorner(progress.distance))
            {
                edgeMarginDistance *= 1.45f;
            }

            // Track-width safety clamp: on the narrowest circuits (roadHalfWidth
            // can go down to ~7m) the boosted distance above could exceed the
            // track's own half-width, driving edgeMargin to zero or negative -
            // exactly the earlier "Track-width fix" regression, where the
            // correction fired almost everywhere on track instead of only near
            // a genuine edge. Capped to a fraction of the real half-width here
            // so the boost can never eat the whole safe corridor.
            // Narrow-section fix: on the pinched procedural sections the 0.6
            // cap nullified the boost above (band-start clamped well inside
            // the corridor edge), so the reactive zone shrank exactly where
            // the wall is closest. Raised to 0.68 so the band reaches
            // proportionally further on narrow track while still leaving the
            // outer racing corridor (halfWidth-1.8m) unblocked.
            // Round 6 (per request - "buff even more"): 0.68 -> 0.72 so the
            // reactive band reaches further still on narrow track while the
            // outer racing corridor (halfWidth-1.8m) stays just clear.
            // Round 7: 0.72 -> 0.75, same reasoning and same corridor bound.
            float edgeHalfWidth = LocalHalfWidthAt(progress.distance);
            edgeMarginDistance = Mathf.Min(edgeMarginDistance, edgeHalfWidth * 0.75f);

            float edgeMargin = edgeHalfWidth - edgeMarginDistance;
            float edgeOvershoot = !suppressOffTrackRecovery ? Mathf.Abs(progress.lateralDistance) - edgeMargin : -1f;
            float edgeProximity = Mathf.Clamp01(edgeOvershoot / edgeMarginDistance);

            // TRAJECTORY-BASED urgency (the "brake wall" fix): the band above is
            // enormous - 7.2-12.6m (x1.45 at tight fences), capped only at 60%
            // of half-width, so on ordinary 6-7m half-width roads it starts
            // ~2.4-2.8m from the CENTERLINE while the legal racing corridor
            // runs out to halfWidth-1.8m. Every apex, entry sweep and
            // overtaking swing therefore lived inside the band, and the brake
            // below had a 0.36+ floor at band entry - a hard brake for simply
            // BEING at a normal lateral position under full control. That was a
            // dominant hidden braking source everywhere on track. Position
            // says nothing about danger; TRAJECTORY does. The response is now
            // driven by time-to-edge (how soon the car actually reaches the
            // wall at its current lateral drift rate), with a pure-position
            // term only inside the final ~1.2m. A controlled car on a
            // legitimate line reads zero urgency however wide it runs; a car
            // genuinely sliding at the wall reads urgent long before contact.
            // [WallDiag] black-box fix (the "edgeRec=2.30 in every record" log):
            // 1) A reference-snap guard - near a flyover crossing the progress
            //    reference can jump between the two overlapping legs, which
            //    teleports lateralDistance by metres in one frame. That is a
            //    RESOLUTION glitch, not car motion; deriving a drift rate from
            //    it produced a fake +-20 m/s "sliding at the wall" reading and
            //    a full-urgency full-lock save on a car driving straight.
            // 2) A low-pass (tau ~0.12s) so only sustained drift, not one
            //    jolted frame, can push time-to-edge into the urgent range.
            float rawEdgeDelta = progress.lateralDistance - previousEdgeLateral;
            bool edgeReferenceSnap = hasEdgeLateralRef && Mathf.Abs(rawEdgeDelta) > 2.5f;
            float edgeLateralRate = hasEdgeLateralRef && !edgeReferenceSnap
                ? Mathf.Clamp(rawEdgeDelta / Mathf.Max(Time.deltaTime, 0.0001f), -20f, 20f)
                : 0f;
            if (edgeReferenceSnap)
            {
                smoothedEdgeLateralRate = 0f;
            }
            else
            {
                float rateSmooth = 1f - Mathf.Exp(-Time.deltaTime / 0.12f);
                smoothedEdgeLateralRate = Mathf.Lerp(smoothedEdgeLateralRate, edgeLateralRate, rateSmooth);
            }

            previousEdgeLateral = progress.lateralDistance;
            hasEdgeLateralRef = true;
            float towardEdgeRate = Mathf.Sign(progress.lateralDistance == 0f ? 1f : progress.lateralDistance) * smoothedEdgeLateralRate;
            float distanceToHardEdge = Mathf.Max(0.05f, edgeHalfWidth - 0.5f - Mathf.Abs(progress.lateralDistance));
            float timeToEdge = towardEdgeRate > 0.15f ? distanceToHardEdge / towardEdgeRate : 99f;
            // Corridor gate (the "extremely slow cornering" fix): the racing
            // line's own entry/exit sweeps cross the road diagonally at up to
            // ~7 m/s of perfectly intentional lateral drift, and a ballistic
            // time-to-edge extrapolation read every one of those sweeps as
            // "wall in <1s" and near-full-braked every corner entry and exit.
            // A car still INSIDE its legal corridor is inside its commanded
            // envelope - the line follower arrests the sweep at the corridor
            // bound by construction, so trajectory urgency only counts once
            // the car is actually BEYOND the corridor (a genuine runaway the
            // steering did not arrest). The pure-position term for the final
            // ~1.2m before the hard edge is unaffected.
            bool beyondCorridor = Mathf.Abs(progress.lateralDistance) > legalLimit + 0.3f;
            // Round 8 (per request - "buff the wall and car aversion of ai without
            // recreating the swerving issue"). The buff is bought entirely by seeing
            // the wall EARLIER, never by pulling harder at the same moment, because
            // every previous swerve regression in this file came from raising a gain
            // at a fixed trigger point. A correction that starts sooner needs less
            // amplitude to achieve the same avoidance, so this direction of change
            // reduces peak steering demand rather than raising it - it takes pressure
            // off the [-1,1] clamp instead of adding to it.
            //
            // Trajectory horizon 2.0s -> 2.8s (already corridor-gated), plus a WIDER
            // position band that is itself corridor-gated: 2.6m of run-up instead of
            // 1.5m, but only for a car that is already outside its legal corridor.
            //
            // The gate on the widened band is not optional. positionUrgency is a pure
            // position term with no gate of its own, and the road's outer 2.6m is
            // exactly where a correct racing line puts the car at every apex and every
            // exit - so widening it ungated would have had edgeEmergencyBrake firing
            // on healthy corner entries, which is the "AI extremely slow going into
            // corners" regression this file has already been through once. The
            // original 1.5m last-resort band therefore stays ungated and completely
            // unchanged; the extra reach is layered on top for runaways only.
            //
            // The RESPONSE curves are deliberately untouched.
            float trajectoryUrgency = beyondCorridor ? Mathf.Clamp01(1f - timeToEdge / 2.8f) : 0f;
            float lastResortUrgency = Mathf.Clamp01((Mathf.Abs(progress.lateralDistance) - (edgeHalfWidth - 1.5f)) / 1.5f);
            float earlyPositionUrgency = beyondCorridor
                ? Mathf.Clamp01((Mathf.Abs(progress.lateralDistance) - (edgeHalfWidth - 2.6f)) / 2.6f)
                : 0f;
            float positionUrgency = Mathf.Max(lastResortUrgency, earlyPositionUrgency);
            float edgeUrgency = suppressOffTrackRecovery ? 0f : Mathf.Max(trajectoryUrgency, positionUrgency);
            // Grid-start fix: the wall-aversion boost below removed the old
            // emergency brake's built-in low-speed discount (0.7x floor ->
            // 1.0x), so a stationary/launching car sitting in a grid box even
            // slightly toward the track edge (normal with 22 staggered boxes,
            // especially combined with the wider margin band above) now got a
            // genuine brake demand at 0 kph - cars never left the grid. This
            // system exists to shed speed before a wall, which is meaningless
            // for a car that has none: fully off below 15kph, ramped in by
            // 40kph (comfortably clear of the launch phase, still well under
            // even the hairpin crawl speed) so it's at full strength for every
            // situation it's actually meant to catch.
            float wallAversionLaunchGate = Mathf.Clamp01((speedKph - 15f) / 25f);
            // Wall-aversion boost (per request): steering-correction strength
            // raised across the board (was 0.38-1.03) - the pull back toward
            // the racing surface is stronger both as soon as the band is
            // entered and at the true edge, so it can dominate over whatever
            // line-following steering put the car there.
            // Round 2 (per request, small trim): eased back ~10% (was 0.65-1.6).
            // Recovery steering keyed off the same trajectory urgency, but with
            // a gentle position-based pre-nudge across the outer half of the
            // old band so a drifting car is shepherded back long before the
            // urgent response is needed.
            // The gentle pre-nudge also only engages beyond the corridor - the
            // old band covers most of the road width, and a constant pull
            // toward the centreline was fighting every apex the line
            // legitimately commanded.
            float edgeSteerNudge = beyondCorridor && edgeOvershoot > 0f ? Mathf.Lerp(0.1f, 0.35f, edgeProximity) : 0f;
            // [WallDiag] black-box fix (the smoking gun - "edgeRec=+-2.25-2.30,
            // comp=4.0" in nearly every wall-contact record): the old form
            // Lerp(0.58,1.44,urgency) * 1.6 commanded up to 2.3x FULL LOCK raw,
            // and even at urgency ~0 the floor was 0.58*1.6 = 0.93 - a near-
            // full-lock slam the instant the band flickered on, then off, then
            // on: that on/off full-lock chatter IS the visible swerve. Now
            // proportional (0.35 at band entry -> 1.0 at true emergency),
            // hard-capped at full lock, and - critically - NO LONGER multiplied
            // by SteerAuthorityCompensation downstream (see the final compose):
            // a save is already expressed in real steering units, and scaling
            // it by the saturated x4 authority ratio just pinned the command at
            // the clamp on every frame the band was live.
            float edgeRecovery = (edgeUrgency > 0f || edgeOvershoot > 0f)
                ? Mathf.Clamp(Mathf.Sign(-progress.lateralDistance) * Mathf.Max(edgeSteerNudge, Mathf.Lerp(0.35f, 1f, edgeUrgency) * (edgeUrgency > 0f ? 1f : 0f)) * wallAversionLaunchGate, -1f, 1f)
                : 0f;
            // Barrier-avoidance round 6 ("AI sending it into the final corner and
            // hitting the barriers"): the emergency brake's quadratic ramp
            // (edgeProximity^2) kept the response tiny through the first half of
            // the margin band - a car arriving AT SPEED near the edge got almost
            // no braking until it was nearly on the barrier, and by then no
            // steering authority could save it. Linear ramp with a higher floor
            // and full-brake ceiling, additionally scaled up with how much faster
            // than a safe edge speed the car is travelling, so a genuinely
            // overspeeding car gets braked hard while it still has room.
            // Overspeed ceiling raised further (Italy hairpin/chicane fix,
            // secondary safety net alongside the FindUpcomingApex chicane
            // blind-spot fix above) - a car that still arrives at the edge
            // carrying real speed (the exact failure mode reported) now gets
            // braked harder still while there's room, rather than relying only
            // on the corner-detection fix to prevent it arriving hot in the
            // first place.
            // Wall-aversion boost (per request): overspeed ramp starts much
            // earlier (was 130-250kph) and the brake-demand floor/ceiling are
            // both raised (was 0.22-1 x 0.7-1.55) so the emergency brake
            // reaches full strength (the product is Clamp01'd) at meaningfully
            // lower proximity/speed than before - it engages harder and sooner.
            // Round 2 (per request, small trim): eased back ~10% (was 0.45-1.3
            // x 1-2).
            float edgeOverspeed = Mathf.Clamp01((speedKph - 70f) / 90f);
            // Brake keyed off trajectory urgency, ramping from ZERO: no more
            // 0.36+ floor for merely entering the (road-wide) band. A car
            // genuinely running out of room still reaches the same full-force
            // ceiling, scaled up by real speed exactly as before.
            // Round 8 wall-aversion buff, the half that CANNOT swerve. A brake demand
            // is unsigned - there is no left or right to flap between - so unlike
            // steering it can be strengthened freely without any oscillation risk,
            // and shedding speed is strictly better wall avoidance than steering
            // anyway: it buys the car more time AND, because the yaw envelope rises
            // as speed falls, it hands back the steering authority needed to make the
            // save. This is where the real aversion increase goes (1.17 -> 1.55).
            float edgeEmergencyBrake = edgeUrgency > 0f
                ? Mathf.Clamp01(Mathf.Lerp(0f, 1.55f, edgeUrgency) * Mathf.Lerp(0.9f, 1.8f, edgeOverspeed) * wallAversionMultiplier) * wallAversionLaunchGate
                : 0f;
            // Pure-pursuit gain is scheduled DOWN on straights ([SwerveDiag] root
            // cause - 70% of logged weave was steer=3, the detector's floor, in every
            // state with the offsets steady). This steering is pure-proportional:
            // steer = gain * (error at the lookahead point), with no derivative term.
            // The 2.2 gain is tuned for corner turn-in, but on a straight the lookahead
            // point sits dead ahead and the error is ~0, so that hot gain drives the
            // loop into a self-sustaining limit cycle - the pervasive weave. (The
            // earlier low-pass could not remove it: a low-pass shrinks a limit cycle's
            // amplitude but not its zero-crossing rate, which is exactly what the
            // reversal count measures.) A straight needs almost no gain, so it eases to
            // ~1.1 as the road straightens - well clear of the oscillation threshold -
            // and returns to the full 2.2 for genuine corner turn-in.
            float pursuitStraightness = 1f - Mathf.Clamp01(severityHere / 0.14f);
            // Speed-scaled gain cut ([SwerveDiag] round: the weave got visibly worse
            // the moment Legends/maxed cars were enabled). The pursuit loop's
            // authority rises with speed, so above ~260kph the 1.1 straight-line
            // gain self-oscillates - and a maxed 360kph car lives there. Ease the
            // gain to 0.6x by 360kph. Monotone: a lower gain can only REDUCE
            // steering activity, never introduce oscillation, so this is safe even
            // untuned, and it leaves low-speed hairpin turn-in at full strength.
            float highSpeedGainScale = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01((speedKph - 260f) / 100f));
            float pursuitGain = Mathf.Lerp(2.2f, 1.1f, pursuitStraightness) * highSpeedGainScale;
            float pursuitSteer = localSteer * pursuitGain;
            // Straight-line steer DEADBAND - the actual cure for the "no target
            // moved - pursuit/slew/yaw limit cycle" the amplitude diagnostic kept
            // pinning. Every prior attempt (slew limiter, gain schedule, yaw damp
            // before AND after the slew limiter) tried to DAMP the hunt after it
            // had already started; none removed its SOURCE. On a dead straight the
            // pursuit error is ~0, so the loop hunts back and forth across zero
            // steer forever - a classic proportional-controller limit cycle. A
            // deadband removes the error signal itself below a small threshold:
            // with no command to overshoot, there is nothing to reverse, so the
            // cycle cannot sustain. It is sized to a few metres of lateral slack
            // (steer ~= gain * lateralError / lookahead), scales to zero the
            // instant the road curves (pursuitStraightness -> 0) so genuine corner
            // turn-in keeps full authority, and is applied only to the pursuit
            // term - the wall-aversion edgeRecovery below is deliberately outside
            // it so a real save is never deadbanded. Monotone-safe: a deadband can
            // only ever REDUCE steering activity, never introduce it.
            // Pure soft deadband: sign(x) * max(0, |x| - db). Continuous
            // everywhere (no snap at the band edge), it just shifts the response
            // so tiny straight-line commands collapse to zero while genuine
            // steering past the band keeps (magnitude - db) of its authority.
            float steerDeadband = Mathf.Lerp(0f, 0.06f, pursuitStraightness);
            if (steerDeadband > 0.0001f)
            {
                pursuitSteer = Mathf.Sign(pursuitSteer) * Mathf.Max(0f, Mathf.Abs(pursuitSteer) - steerDeadband);
            }
            // Pursuit only here - the wall-aversion edgeRecovery is composed in
            // AFTER the authority compensation below, so the compensation never
            // amplifies a save (it exists to restore small-correction scale for
            // the pursuit loop, not to multiply an already-full-lock emergency).
            command.steer = Mathf.Clamp(pursuitSteer, -1f, 1f);

            // Straight-line yaw-rate damping (see swerveHeadingPrev): oppose the
            // measured rotation on a straight, where any yaw IS the shimmy. Scaled
            // by pursuitStraightness so it fades to zero for genuine corner yaw, and
            // gated off during edge recovery so it never fights a wall save.
            // [SwerveDiag amplitude] CONFIRMED this is the last cause: on dead
            // straights (drawnLine 0, lineBias 0, no overtake) cars still swing
            // 8-15m with steer reversing 10-22x/2s - a pure pursuit/slew/yaw limit
            // cycle. The damper is the phase-lead term that kills it, but it was
            // being APPLIED here, BEFORE the steering slew-limiter far below, which
            // then rate-limited its fast correction away (a slew limiter is a
            // low-pass; it filters out exactly the derivative term meant to break
            // the cycle). So only COMPUTE it here; it is applied AFTER the slew
            // limiter (see below), un-rate-limited, where its phase lead survives.
            // Gain also raised (0.0022 -> 0.006) since the filtered version clearly
            // wasn't enough. Monotone-safe: it only ever opposes measured yaw, so a
            // higher gain can only reduce rotation, never add it.
            float yawDampSteer = 0f;
            Vector3 flatForward = transform.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude > 0.0001f)
            {
                flatForward.Normalize();
                if (swerveHeadingHasRef && Time.deltaTime > 0.0001f)
                {
                    float rawYawRateDeg = Vector3.SignedAngle(swerveHeadingPrev, flatForward, Vector3.up) / Time.deltaTime;
                    // Low-pass the yaw rate before it drives the steer. The raw
                    // per-frame derivative carries the rigidbody's heading jitter,
                    // and feeding that straight into an un-rate-limited steer was
                    // itself a chatter source; smoothing keeps the genuine
                    // low-frequency yaw (the weave) and discards the per-step noise.
                    float yawSmooth = 1f - Mathf.Exp(-Time.deltaTime / 0.08f);
                    smoothedYawRateDeg = Mathf.Lerp(smoothedYawRateDeg, rawYawRateDeg, yawSmooth);
                    // Scaled by the same authority compensation as the final
                    // steer command (see SteerAuthorityCompensation) so the
                    // damping RATIO survives the envelope change - measured
                    // yaw shrank with the new authority, and an uncompensated
                    // damp term would be proportionally too weak to break the
                    // pursuit/slew limit cycle it exists to kill.
                    // Edge-recovery damping (per the steer=32-reversals/2s log
                    // lines): the damper used to switch OFF entirely during a
                    // wall save (edgeUrgency > 0, "never fight a save") -
                    // which left the one regime that combines the HIGHEST
                    // steering gain (edgeRecovery), the FASTEST slew (18/s)
                    // and the full authority compensation with NO damping at
                    // all. That is a textbook limit-cycle recipe, and it is
                    // exactly where the 8-16Hz saw in the logs lives: cars
                    // banging full-lock alternately while grinding a wall.
                    // Damping that opposes MEASURED yaw can never fight a
                    // save's direction - it only stops the save overshooting
                    // into the opposite swerve - so it now stays on during
                    // recovery at 60% gain.
                    float recoveryDampScale = edgeUrgency > 0f ? 0.6f : 1f;
                    yawDampSteer = smoothedYawRateDeg * 0.006f * Mathf.Max(pursuitStraightness, edgeUrgency > 0f ? 0.75f : 0f)
                        * recoveryDampScale * SteerAuthorityCompensation(speedKph);
                }

                swerveHeadingPrev = flatForward;
                swerveHeadingHasRef = true;
            }

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
            // Difficulty pass ("still too easy" round 2): skill ceiling raised
            // (was 1.22) - braking zones were one of the last places the player
            // gained hand over fist on Hard/Expert, because the AI's trusted
            // deceleration sat well below what the car can actually do.
            // Difficulty round 4 (per request): trusted deceleration ceiling
            // raised again (1.35 -> 1.55 skill scale) - higher tiers brake
            // later and harder than any assist-aided player line.
            // Entry-overshoot fix (per report - the AI "keep finding the
            // barriers", half the field OUT): the physics delivers ~60-90
            // m/s^2 at full pedal (ApplyForces' brakeStat), so 13-32 here was
            // never a capacity number - it was the PLANNING decel, and the
            // old demand ramp below never even delivered it. Planning decel
            // raised to a realistic-but-conservative band (Expert ~50, still
            // ~25%+ under what the car can physically do, so the closed-loop
            // demand below always has reserve to correct with).
            // Corner-entry pace round 2 (per report - "still way too slow on
            // entry"): the band above still left a 35-65% reserve against the
            // availableDecel the closed-loop demand actually trusts (34-59
            // scaled up to 1.4x with speed), so zones opened far earlier than
            // the controller needed. Planning decel raised toward - never past
            // - that envelope; the closed-loop demand still self-corrects hot
            // arrivals every frame and keeps ~12% headroom on top.
            // Overall-pace round: 24-38 -> 26-40, closing more of the gap to
            // the availableDecel envelope the closed-loop demand trusts.
            // Low-grip round (per report - "the AI are still extremely
            // terrible at handling the low grip especially when it starts
            // raining on a dry track"): both this planning decel and the
            // availableDecel below were FIXED dry-baseline numbers, while the
            // physical brake force scales with Tyres.BrakingMultiplier - cold
            // slicks at rain onset cut real capability ~30%, so the AI kept
            // braking at dry distances and arrived hot at every single zone.
            // Both now scale by the live factor, so brake points move out
            // exactly as far as the car's real braking has shrunk.
            // True-ceiling round: 26-40 -> 28-42. Elite braking now plans at
            // ~76% of the physical envelope on the base band (the big-stop
            // lift and closed-loop demand still cover the rest).
            float liveBrakeGrip = vehicle.LiveBrakingGripFactor;
            float decelReference = Mathf.Lerp(28f, 42f, Mathf.Clamp01(brakingStat / 100f)) * Mathf.Lerp(1.05f, 1.5f, skillTier) * liveBrakeGrip;
            // brakeConfidenceMultiplier folds in on top of brakeDistanceMultiplier so
            // Hard/Expert genuinely brake later/shorter, not just via the weaker base
            // multiplier alone: >1 shortens the effective distance (brakes later),
            // <1 lengthens it (brakes earlier), same as the base multiplier's own sense.
            // Hairpin-crash fix (per report - "the AI crash mostly at hairpin
            // or hairpin-like turns"): this multiplier lets confident tiers
            // brake far LATER than the kinematic distance (Expert ~2x shorter).
            // That swagger is survivable scrubbing 40 kph for a sweeper, but a
            // hairpin is a 250+ kph delta - halving the required distance there
            // guarantees arriving unsaveably hot, which is exactly why crashes
            // clustered at hairpin-scale stops only. Real drivers do the
            // opposite: the bigger the stop, the more conservatively they hit
            // the number. Brake-later confidence now fades with stop size -
            // full effect for small scrubs, near-kinematic braking once the
            // required delta approaches hairpin scale.
            float stopSizeKph = Mathf.Max(0f, speedKph - brakingApexSpeed);
            float bigStopBlend = Mathf.Clamp01((stopSizeKph - 120f) / 130f);
            // Tight-corner braking round (per report - "they still brake SO
            // EARLY especially into tight corners"): the big-stop path kept
            // BOTH conservatisms at once - near-kinematic distance (the 1.02
            // confidence cap below) AND the ordinary planning decel. Real
            // drivers do the opposite on a hairpin-sized stop: they commit to
            // near-maximum braking and hit the number. Planning decel now
            // ramps up to +25% as the stop approaches hairpin scale, which
            // pulls the brake point ~20% closer to the corner exactly where
            // the early braking was most visible; the closed-loop demand
            // below still measures the remaining distance every frame and
            // saturates to full pedal (with the edge emergency brake and the
            // speed-shed reflex behind it) if the arrival runs hot.
            decelReference *= Mathf.Lerp(1f, 1.25f, bigStopBlend);
            // Entry-overshoot fix: this multiplier DIVIDES the kinematic
            // braking distance, and the per-tier confidence product ran to
            // ~1.4-1.7 - i.e. Hard/Expert began braking at ~60% of the
            // distance their own planning decel needs, relying on the old
            // ~13-16g rotation to absorb the guaranteed hot arrival. With the
            // realistic envelope, that was a wall at nearly every real
            // braking zone. Brake-later skill is now a few percent, not tens:
            // capped at 1.03-1.08 by tier.
            float confidentMultiplier = Mathf.Min(
                profile.brakeDistanceMultiplier * Mathf.Lerp(0.92f, 1.05f, experience / 100f) * profile.brakeConfidenceMultiplier,
                Mathf.Lerp(1.03f, 1.08f, skillTier));
            // 1.02 -> 1.04 with the big-stop planning-decel lift above: the
            // stop-size fade still strips most of the brake-later swagger on
            // hairpin-scale stops, just no longer all of it.
            float effectiveBrakeMultiplier = Mathf.Max(0.55f, Mathf.Lerp(confidentMultiplier, Mathf.Min(confidentMultiplier, 1.04f), bigStopBlend));
            // Active braking-point mistake (see UpdateMistake): the driver is
            // momentarily willing to carry more entry speed than the corner can
            // take. The edge emergency brake below still catches the car at the
            // limit, so this converts into a visible run-deep/run-wide moment
            // and real time loss rather than a scripted crash. Never applied on
            // a pit approach, during off-track recovery, or under a race-control
            // pace cap - those targets are safety/legality clamps, not corner
            // judgement, and inflating them would break pit entry or SC pacing.
            if (mistakeBrakeErrorTimer > 0f && !committingToPit && !offTrack && raceControlCap >= 9000f)
            {
                brakingApexSpeed *= 1f + mistakeBrakeErrorSeverity;
            }

            float v0 = speedKph / 3.6f;
            float v1 = Mathf.Min(speedKph, brakingApexSpeed) / 3.6f;
            float rawBrakingDistance = Mathf.Max(0f, (v0 * v0 - v1 * v1) / (2f * decelReference));
            float brakingDistance = rawBrakingDistance / effectiveBrakeMultiplier;

            float speedOverApex = speedKph - brakingApexSpeed;
            float brakeDemand = 0f;
            bool nearCorner = apexSeverity > 0.14f && apexDistanceAhead <= Mathf.Max(brakingDistance, 6f);
            if (speedOverApex > 0f && nearCorner)
            {
                // Entry-overshoot fix, part 2 - the old demand curve was
                // Clamp01(over/42) * Lerp(0.35, 1, closeness): it entered
                // every braking zone at 35% pedal and only reached full brake
                // deep into the zone, while the kinematic distance above
                // assumed FULL planning decel from the very first metre. The
                // arrival was therefore hot at every corner BY CONSTRUCTION -
                // survivable under the old ~13-16g rotation, a barrier under
                // the realistic envelope (the report's "they keep finding the
                // barriers", 12 of 22 OUT). The demand is now a closed-loop
                // controller: each frame it computes the deceleration the
                // REMAINING distance actually requires and commands exactly
                // that fraction of the car's real braking capacity (~12%
                // headroom). Entering on plan holds ~60-80% pedal; arriving
                // hot (traffic, tow, mistake) saturates to full brake
                // immediately, and the ~25%+ physical reserve above the
                // planning decel absorbs it. No fixed ramp, self-correcting
                // every frame.
                float remainingMeters = Mathf.Max(1.5f, apexDistanceAhead);
                float requiredDecel = Mathf.Max(0f, v0 * v0 - v1 * v1) / (2f * remainingMeters);
                float availableDecel = Mathf.Lerp(34f, 59f, Mathf.Clamp01(brakingStat / 100f))
                    * Mathf.Lerp(1.05f, 1.4f, Mathf.InverseLerp(80f, 330f, speedKph))
                    * liveBrakeGrip;
                brakeDemand = Mathf.Clamp01(requiredDecel * 1.12f / Mathf.Max(8f, availableDecel));
            }

            // Barrier-avoidance fix round 3: the edge-proximity emergency brake
            // (computed alongside edgeRecovery above) always wins over whatever the
            // normal corner-braking model alone decided - it exists specifically for
            // the case where that model still let the car carry too much speed for
            // the actual geometry, so it must never be capped by it.
            brakeDemand = Mathf.Max(brakeDemand, edgeEmergencyBrake);

            // Pit-approach envelope braking (see the pitEntryPlanned block):
            // the corner model above only brakes for apexes, and a pit
            // approach on a straight has none.
            brakeDemand = Mathf.Max(brakeDemand, pitApproachBrakeDemand);

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

                // Low-grip traction management, and an AI-ONLY one - the player has no
                // equivalent throttle cap. Its 0.6 threshold was tripping on DRY
                // HARDS: baseGrip 0.66 x an in-window ~1.05 puts a fresh hard at 0.69,
                // and the first bit of wear (wearGrip 0.82 by 65% life) drops it to
                // 0.57, so from mid-stint onward every hard-shod AI car drove the rest
                // of its stint with the throttle capped below full while the player,
                // on the same tyre, did not. The hard tyre's grip deficit is deliberate
                // (see TyreState) and stays; an asymmetric penalty for the AI on top of
                // it is not the same thing. Threshold moved to 0.45, where only real
                // rain (slicks in the wet sit at 0.13-0.38) or a tyre genuinely off the
                // cliff can reach it.
                if (vehicle.LastTyreGripMultiplier > 0f && vehicle.LastTyreGripMultiplier < 0.45f)
                {
                    throttleTarget = Mathf.Min(throttleTarget, Mathf.Lerp(0.55f, 1f, vehicle.LastTyreGripMultiplier / 0.45f));
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
            //
            // Acceleration buff (race start / VSC-SC-yellow recovery, +50%):
            // pure throttle-ramp response, never an engine/grip boost, matching
            // launchConfidence's own "input timing only" convention. Applies
            // during the opening seconds of the race (RaceElapsed - the same
            // window the launch-confidence/pileup-safety cap just below already
            // covers) and for PaceCapRecoveryBoostSeconds after a VSC/SC/local
            // yellow pace cap actually clears (paceCapRecoveryBoostTimer, set
            // above whenever raceControlCap is active) - the two moments a real
            // driver floors it hardest.
            bool inLaunchWindow = raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                                   raceManager.RaceElapsed >= 0f && raceManager.RaceElapsed < launchSettleDuration + 1.5f;
            float accelerationBoost = (inLaunchWindow || paceCapRecoveryBoostTimer > 0f) ? AccelerationBoostMultiplier : 1f;
            // AI acceleration 2x (per request): the BASE on-power throttle ramp
            // (previously 2.6 * throttleAggression) is doubled so AI cars get
            // back to full throttle roughly twice as fast on every corner exit
            // and rolling start, everywhere on track - not just off cautions.
            // This is throttle INPUT ramp only; the vehicle's own grip/power
            // model still bounds actual acceleration, so it sharpens corner
            // exits and traffic response without becoming unphysical. The
            // launch/recovery boost above still stacks on top of it.
            float baseRamp = brakeDemand > 0.02f ? 4.5f : 5.2f * profile.throttleAggressionMultiplier;
            currentThrottle = Mathf.MoveTowards(currentThrottle, throttleTarget, Time.deltaTime * baseRamp * accelerationBoost);
            command.throttle = currentThrottle;

            // Physics-level launch boost off a standing start / VSC-SC-yellow
            // restart. The throttle-input ramp above is already saturated within a
            // fraction of a second, so buffing it further did nothing the player
            // could feel - both cars then share identical physics and the AI could
            // never out-drag the player off the line. This flag hands
            // VehicleController a real additive low-speed forward push (AI-only) so
            // the AI genuinely launches as hard as - now harder than - the player.
            // Driven by a dedicated, robust race-start window (the first
            // RaceStartBoostSeconds of the race, independent of the fragile
            // launchSettleDuration window above, which was so short the boost was
            // gone before the pack had really moved) plus any pace-cap recovery.
            // Only set while braking-clear and below race pace so it can't shove a
            // car trying to slow for turn one and can't add straight-line top speed.
            bool raceStartBoostWindow = raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                                        raceManager.RaceElapsed >= 0f && raceManager.RaceElapsed < RaceStartBoostSeconds;
            if ((raceStartBoostWindow || paceCapRecoveryBoostTimer > 0f) && brakeDemand < 0.05f && speedKph < 210f)
            {
                command.launchBoost = Mathf.Clamp01(command.throttle);
            }

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
                // Launch acceleration fix: shortened every tier's duration and
                // raised every tier's floor a further step - ApplyTrafficAvoidance
                // (called right below, every difficulty, every frame) is still
                // the actual thing preventing a real collision; this cap only
                // ever existed to keep the pack from bunching too tightly into
                // turn one, and was holding every difficulty back for longer
                // than that job needs.
                // Start-acceleration buff (per request, 2x off the line): the
                // opening pileup-safety throttle cap is shortened and its floor
                // raised close to full so AI barely lift off the line - the
                // launch throttle-ramp boost (AccelerationBoostMultiplier during
                // the launch window) still stacks on top. ApplyTrafficAvoidance
                // remains the actual anti-collision guard, so this only removes
                // the artificial early-race hesitation, it doesn't cause pileups.
                float openingCapDuration = confidentTier ? (isExpert ? 0.2f : 0.6f) : 1.1f;
                float openingCapFloor = confidentTier ? (isExpert ? 0.99f : 0.96f) : 0.92f;
                if (raceManager.RaceElapsed < openingCapDuration)
                {
                    command.throttle = Mathf.Min(command.throttle, Mathf.Lerp(openingCapFloor, 1f, raceManager.RaceElapsed / openingCapDuration));
                }
            }

            float preTrafficSteer = command.steer;
            ApplyTrafficAvoidance(ref command, progress, speedKph, profile, isExpert, committingToPit, nearCorner);

            if (committingToPit && !participant.isPlayer)
            {
                // Pit-entry debug logging (verbose-gated, matches the existing
                // GameLog.Verbose convention): makes it obvious whether the AI
                // failed to pre-position, traffic steering overrode the target, it
                // reached the ramp but failed the physical test, or RaceManager
                // failed to begin Entry (logged separately in
                // RaceManager.HandlePitService's own "[PitEntry]" line).
                GameLog.Info("[PitEntrySteer] " + participant.driverName +
                             " normalized=" + progress.normalized.ToString("0.000") +
                             " lateral=" + progress.lateralDistance.ToString("0.00") +
                             " halfWidth=" + LocalHalfWidthAt(progress.distance).ToString("0.00") +
                             " preEntryStage=" + preEntryRampStage +
                             " onRampStage=" + onEntryRampStage +
                             " onPitEntryRamp=" + onPitEntryRamp +
                             " pitTarget=" + pitEntryTargetPoint.ToString("F1") +
                             " trafficSteerAdjust=" + (command.steer - preTrafficSteer).ToString("0.00") +
                             " command.steer=" + command.steer.ToString("0.00"));
            }

            // Driver-pressure model: a car actively attacking or defending under
            // close pressure pushes slightly harder - the tyre lockup model already
            // reads brake/steer inputs, so this alone raises lockup risk exactly
            // when a real driver would be more likely to lock a wheel.
            if (pressureFactor > 0f)
            {
                command.brake = Mathf.Min(1f, command.brake * (1f + pressureFactor * 0.08f));
                command.steer = Mathf.Clamp(command.steer * (1f + pressureFactor * 0.06f), -1f, 1f);
            }

            // Pit-timing rule (per request): "only pit if tyre WEAR exceeds 60%".
            // Tyres.Wear here is REMAINING life (1.0 fresh, 0.12 = destroyed), so
            // 60% wear == 40% remaining == a 0.40 threshold. Centred tightly on
            // 0.40 with only a small skill spread so every AI genuinely runs its
            // tyres to ~60% wear before the routine stop, instead of pitting while
            // there's still plenty of life left. The destroyed-tyre safety nets
            // below (0.12 wear, 0.5 grip) still force a stop before tyres are gone.
            // Threshold ownership moved to AiPitStrategyRules.RoutinePitThreshold:
            // centred on 0.40 remaining with a small skill spread, shifted per
            // compound (a Soft falls off a cliff and comes off earlier, a Hard
            // runs leaner, wet rubber degrades unpredictably once badly worn).
            TyreCompound currentCompound = vehicle.Tyres.Compound;
            float tyrePitThreshold = AiPitStrategyRules.RoutinePitThreshold(
                tyreManagement / 100f, profile.tyreSavingBias, MapStintCompound(currentCompound));

            // Round 8 (per report - "the AI are going for 2 stops again even
            // when its not a viable strategy"): the reach-the-flag projection
            // (already used below to ADD an emergency short-stint stop) now
            // also VETOES pointless extra stops. Once a car has made its stop,
            // a further voluntary stop only ever happens if the current tyre
            // genuinely cannot finish the race - a worn tyre that CAN reach
            // the flag is always faster than a ~22s stop with a handful of
            // laps left. The destroyed-tyre (0.12) and grip-collapse safety
            // nets below stay unconditional.
            float pitTrackTempC = raceManager.Track != null ? raceManager.Track.trackTemperatureC : TyreStrategyRules.StandardTrackTempC;
            int pitStintCode = currentCompound == TyreCompound.Soft ? TyreStrategyRules.Compound.Soft
                : (currentCompound == TyreCompound.Hard ? TyreStrategyRules.Compound.Hard : TyreStrategyRules.Compound.Medium);
            float expectedStintLaps = Mathf.Max(0.6f, TyreStrategyRules.ExpectedStintLapsAtTemp(pitStintCode, pitTrackTempC));
            float lapsToFlag = raceManager.RaceLaps - participant.lapTracker.CompletedLaps - progress.normalized;
            float tyreLapsLeft = vehicle.Tyres.Wear * expectedStintLaps;
            bool tyreReachesFlag = tyreLapsLeft >= lapsToFlag - 0.1f;
            // A wrong-compound-for-the-weather car is never "fine to the flag"
            // whatever the wear projection says - the crossover paths stay open.
            WeatherState currentWeather = raceManager.Track == null ? WeatherState.Clear : raceManager.Track.weather;
            bool trackWetNow = currentWeather == WeatherState.LightRain || currentWeather == WeatherState.HeavyRain;
            bool onWetCompoundNow = AiPitStrategyRules.IsWetCompound(MapStintCompound(currentCompound));
            bool weatherMismatchNow = trackWetNow != onWetCompoundNow;
            bool extraStopPointless = participant.pitStops >= 1 && tyreReachesFlag && !weatherMismatchNow;
            string pitDiagTriggers = null;

            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                AiPitStrategyRules.ShouldPitRoutine(vehicle.Tyres.Wear, tyrePitThreshold) &&
                participant.lapTracker.CompletedLaps > 0 &&
                !raceManager.AiVoluntaryStopsExhausted(participant) &&
                !extraStopPointless)
            {
                command.pitRequest = true;
                AddPitTag(ref pitDiagTriggers, "routine-wear");
            }

            // Part A.9: never stay out on destroyed/near-destroyed tyres regardless of
            // planned lap or the threshold above - strategy timing should never keep a
            // car circulating on tyres that are essentially gone.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                AiPitStrategyRules.TyresEffectivelyGone(vehicle.Tyres.Wear))
            {
                command.pitRequest = true;
                AddPitTag(ref pitDiagTriggers, "tyres-gone");
            }

            // Short-stint predictive pit (per request - dynamic track-temperature
            // wear can cut a stint to a lap or two, and the AI must PLAN for that
            // instead of circulating on a dead tyre). From the tyre's expected
            // life AT THIS track temperature, if it won't survive roughly another
            // full lap - its projected wear at the next pit pass would be into
            // destroyed territory - box at the next entry. Because a car can only
            // physically pit once per lap (~85% of the way round), the safe call
            // for a short stint is to box the moment it can't reach the FOLLOWING
            // pass, which for a one/two-lap stint means lap 1. Deliberately NOT
            // gated on CompletedLaps > 0 so that lap-1 box can happen rather than
            // running a lap punctured; the pit-entry logic still owns the actual
            // box lap, and the final-lap suppression below still applies.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying)
            {
                float wearPerLap = 1f / expectedStintLaps;
                // Only box if the tyre genuinely CAN'T reach the flag on its
                // remaining life - a tyre that can finish, even worn, is never
                // worth a ~22s stop it can't win back. lapsToFlag is fractional
                // (counts the part-lap already driven), so a two-lap tyre fitted
                // with two laps to go runs to the end instead of pitting early.
                // (Projection inputs hoisted above the routine trigger, which
                // now shares them for the extra-stop veto.)
                if (!tyreReachesFlag && vehicle.Tyres.Wear < wearPerLap * 1.15f + 0.04f)
                {
                    command.pitRequest = true;
                    AddPitTag(ref pitDiagTriggers, "short-stint");
                }
            }

            // Tyre-overextension fix (pace-loss awareness): independent of the wear-
            // number threshold above, which a high-tyre-management driver can push
            // quite low - if the tyre's own real grip multiplier has genuinely
            // collapsed, force the stop regardless of plan. A car losing this much
            // grip is several seconds a lap off the pace and becomes a rolling
            // roadblock no matter what the pre-race strategy said.
            // currentWeather declared alongside the reach-the-flag projection above.
            // Round 10 (per report + [PitStopDiag] capture - "so many 2 stops
            // still", race started wet then dried): the smoking-gun lines
            // were "requests stop #2 ... compound=Medium wear=1.00
            // triggers=[grip-collapse] vetoActive=True" - cars that had just
            // fitted FRESH weather-correct slicks read as grip-collapsed
            // anyway, because an out-lap tyre is stone cold on a 10-12C track
            // still damp from the rain that just ended. Those are CONDITIONS,
            // and a pit stop cannot fix conditions: the next set comes out
            // just as cold onto the same damp track, so the unconditional
            // collapse trigger sent the entire field straight back in for an
            // identical tyre. Grip collapse is now only a pit trigger when a
            // stop can actually CURE it: the tyre is meaningfully worn, or it
            // is the wrong compound for the weather. A fresh, weather-correct
            // tyre reading collapsed is something you drive through while it
            // comes up to temperature and the track dries.
            // Grip collapse only triggers a stop a fresh tyre can actually CURE:
            // Wear is REMAINING tread (1 = fresh, 0 = gone), so Wear < 0.55 means
            // meaningfully worn, or it is the wrong compound for the weather. A
            // fresh, weather-correct set reading collapsed (stone cold on a
            // cooling track after wet->dry) is driven through, not pitted - that
            // was the fresh-tyre 2-stop loop. Also honour the extra-stop veto: a
            // worn-but-collapsed tyre that will still reach the flag in a short
            // sprint can never win back a ~22s stop, so an already-stopped car
            // does not throw away the race on a second stop it cannot amortise.
            bool gripCollapseCurableByStop = vehicle.Tyres.Wear < 0.55f || weatherMismatchNow;
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                AiPitStrategyRules.GripCollapsed(vehicle.Tyres.GripMultiplier(currentWeather)) &&
                gripCollapseCurableByStop &&
                !extraStopPointless &&
                participant.lapTracker.CompletedLaps > 0)
            {
                command.pitRequest = true;
                AddPitTag(ref pitDiagTriggers, "grip-collapse");
            }

            // Off-by-one fix: used to compare raw CompletedLaps against
            // RecommendedPitLap directly - CompletedLaps only reaches a given
            // number once that lap is already fully finished, so a target of lap 3
            // fired the request only after lap 3 was done (i.e. already on lap 4).
            // ShouldAiPitByStrategyLap does the same display-lap (+1) comparison
            // the player's own auto-pit path already used, in one shared place.
            // Gated behind the same 60%-wear rule as the routine trigger: a planned
            // strategy lap should not drag a car in while its tyres are still fresh
            // (the recurring "AI pits too early" complaint). Only let the strategy
            // lap actually trigger the stop once wear has reached ~55%+ (0.45
            // remaining, a hair before the 0.40 routine point so a well-timed plan
            // can still fire slightly ahead of pure wear). Destroyed-tyre/grip
            // safety nets above are unaffected and still force a stop when needed.
            if (raceManager.ShouldAiPitByStrategyLap(participant) && AiPitStrategyRules.StrategyLapMayFire(vehicle.Tyres.Wear) &&
                !raceManager.AiVoluntaryStopsExhausted(participant) &&
                !extraStopPointless)
            {
                command.pitRequest = true;
                participant.pitRequestLapNumber = participant.lapTracker.CompletedLaps + 1;
                AddPitTag(ref pitDiagTriggers, "strategy-lap");
            }

            // Safety car pit window (Part 6): an additional OR-condition alongside the
            // normal tyre-wear/mandatory-stop triggers above, flowing through the same
            // command.pitRequest -> BeginPitEntry pipeline (including its existing
            // queueing/staggered release) rather than a parallel path.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.ShouldAiPitUnderSafetyCar(participant) &&
                !extraStopPointless)
            {
                command.pitRequest = true;
                AddPitTag(ref pitDiagTriggers, "safety-car-window");
            }

            // Smarter AI strategy: jump a closely-followed rival that hasn't
            // stopped yet by taking this car's own pit window a lap or two early.
            // Round 9 ("theyre starting to do 2 stops for no reason again"):
            // this was the one voluntary-stop path NOT gated by the
            // reach-the-flag veto - an already-stopped car whose tyre finishes
            // the race would still burn ~22s on an "undercut" that can never
            // pay for itself (the rival it jumps still has a free stop in
            // hand). Same gate as every other voluntary trigger now.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.ShouldAiPitForUndercut(participant) &&
                !raceManager.AiVoluntaryStopsExhausted(participant) &&
                !extraStopPointless)
            {
                command.pitRequest = true;
                AddPitTag(ref pitDiagTriggers, "undercut");
            }

            // Weather crossover under green: the safety-car window already reacts
            // to a wet/dry mismatch, but with no caution out a mismatched car used
            // to stay on the wrong rubber until wear or outright grip collapse
            // forced the call. Watch the mismatch and commit once it has persisted
            // past this driver's reaction window (sharper awareness reacts sooner;
            // crossing TO wets is more urgent than coming back to slicks). The
            // stop itself picks the weather-correct compound, same as any stop.
            bool trackWet = currentWeather == WeatherState.LightRain || currentWeather == WeatherState.HeavyRain;
            bool onWetCompound = AiPitStrategyRules.IsWetCompound(MapStintCompound(currentCompound));
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                participant.lapTracker.CompletedLaps > 0 &&
                AiPitStrategyRules.WantsWeatherCrossover(trackWet, onWetCompound))
            {
                weatherMismatchSeconds += Time.deltaTime;
                float awareness01 = participant.driverData == null ? 0.78f : participant.driverData.awareness / 100f;
                float reaction = AiPitStrategyRules.CrossoverReactionSeconds(trackWet, awareness01);
                if (AiPitStrategyRules.ShouldCrossover(trackWet, onWetCompound, weatherMismatchSeconds, reaction))
                {
                    command.pitRequest = true;
                    AddPitTag(ref pitDiagTriggers, "weather-crossover");
                }
            }
            else
            {
                weatherMismatchSeconds = 0f;
            }

            // Mandatory-stop backstop (per report - "a lot of AI are just doing
            // 0 stops which gives them a penalty... they shouldn't be doing 0
            // stops at all"): every trigger above is gated on wear, grip, plan
            // window or opportunity - on a short race (or with durable rubber)
            // none of them ever fire, and the AI simply ate the +10s no-stop
            // penalty. A car still owing a second dry compound now boxes
            // unconditionally once ~62% of the race is done (same lap gate as
            // PenaltyRules.ShouldDisqualifyForTwoCompoundRule; a 5-lap race boxes
            // at the end of lap 4 - normally the wear triggers have fired long
            // before this).
            // Backstop is now keyed to the REAL rule: has this car actually run two
            // different DRY specifications? Checking pitStops == 0 was the old
            // "mandatory stop" test and would let a car that pitted soft->soft sit
            // out, which is now a disqualification. A wet race voids the rule.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && !raceManager.IsTimeTrial &&
                !raceManager.RaceDeclaredWet &&
                raceManager.RaceLaps >= PenaltyRules.TwoCompoundMinimumRaceLaps &&
                participant.lapTracker.CompletedLaps + 1 >= Mathf.Max(2, Mathf.CeilToInt(raceManager.RaceLaps * 0.62f)))
            {
                int distinctDry;
                bool usedWet;
                participant.CountDryCompoundsUsed(out distinctDry, out usedWet);
                if (!usedWet && distinctDry < 2)
                {
                    command.pitRequest = true;
                }
            }

            // Never pit on the final lap (per request), whatever the tyre wear:
            // a stop here only throws away track position the car can't win
            // back before the flag, and the mandatory stop is always taken well
            // before this. Overrides every trigger above; a car already on the
            // pit rail (pitPhase != None) is mid-stop and unaffected - this only
            // suppresses a NEW request. Uses CompletedLaps + 1 (the lap being
            // driven) vs RaceLaps so it engages the moment the last lap starts.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                participant.lapTracker != null &&
                AiPitStrategyRules.FinalLapSuppressesNewRequest(participant.lapTracker.CompletedLaps, raceManager.RaceLaps))
            {
                command.pitRequest = false;
            }

            // [PitStopDiag] flight recorder: any surviving voluntary request
            // logs which trigger(s) fired and every number the veto weighed,
            // once per car per lap. Round 2: extended from second-stop-only to
            // ALL stops - the pasted [PaceDiag] showed the entire field taking
            // ~30s laps (lap time + a stop) on lap 2, and FIRST stops left no
            // trace in the recorder, so the mass-stop trigger was invisible.
            if (command.pitRequest && pitDiagTriggers != null &&
                participant.lapTracker.CompletedLaps != pitDiagLastLoggedLap)
            {
                pitDiagLastLoggedLap = participant.lapTracker.CompletedLaps;
                Debug.LogWarning("[PitStopDiag] " + participant.driverName + " requests stop #" + (participant.pitStops + 1) +
                    " lap " + (participant.lapTracker.CompletedLaps + 1) + "/" + raceManager.RaceLaps +
                    " triggers=[" + pitDiagTriggers + "]" +
                    " compound=" + currentCompound +
                    " wear=" + vehicle.Tyres.Wear.ToString("0.00") +
                    " tyreLapsLeft=" + tyreLapsLeft.ToString("0.0") +
                    " lapsToFlag=" + lapsToFlag.ToString("0.0") +
                    " reachesFlag=" + tyreReachesFlag +
                    " expectedStint=" + expectedStintLaps.ToString("0.0") + "@" + pitTrackTempC.ToString("0") + "C" +
                    " weather=" + currentWeather +
                    " mismatch=" + weatherMismatchNow +
                    " vetoActive=" + extraStopPointless);
            }

            ApplyDamageStrategy(ref command, damagePercent);

            // Deterministic-deadlock fix: once a real physical pit-entry opening
            // has been missed this lap, no automatic trigger above (tyre wear,
            // grip collapse, strategy lap, damage, undercut, VSC/SC) is allowed
            // to silently re-arm PitRequested while the car is still inside the
            // broad approach zone - that re-armed request is what previously sent
            // committingToPit steering back toward an opening that had already
            // closed. The request stays suppressed until UpdateMissedPitEntryReset
            // clears missedPitEntryThisLap on the next completed lap.
            if (participant.missedPitEntryThisLap)
            {
                command.pitRequest = false;
            }

            // Late-latch filter (per [PitDiag] - 'MISSED pit entry' logged at
            // norm 0.984-0.998 at full racing speed): a request whose FIRST
            // latch happens past the pit corridor start cannot possibly be
            // serviced this lap - RaceManager immediately scores it a miss,
            // with the full miss bookkeeping and the visible last-second bail
            // swerve. Hold the trigger instead: it re-fires right after the
            // start/finish line with the entire lap to plan a real approach.
            // An ALREADY-latched request (vehicle.PitRequested) is untouched.
            if (command.pitRequest && !vehicle.PitRequested &&
                progress.normalized > track.PitCorridorStartNormalized)
            {
                command.pitRequest = false;
            }

            // Blue flag: concede pace on the straights while being lapped so the
            // leader gets by quickly - a small lift, never a mid-corner one (a
            // lift at the apex is how accidents happen), and never below the
            // race-control caps already applied above.
            if (raceManager.IsShownBlueFlag(participant) && severityHere < 0.12f)
            {
                command.throttle = Mathf.Min(command.throttle, 0.88f);
                safetyThrottleCeiling = Mathf.Min(safetyThrottleCeiling, 0.88f);
            }

            // ERS is a POWER boost, so it helps wherever the car is POWER-limited
            // rather than GRIP-limited - and that is emphatically NOT just dead
            // straights (per report - "there are SO many corners including
            // high-speed and most medium-speed ones which you can literally take
            // flat out"). A corner taken flat is power-limited too: the downforce
            // already supplies the grip to hold it, so extra power = more speed
            // through it, exactly like the player deploying flat through a fast
            // sweeper. The car is only truly GRIP-limited where it must brake,
            // run a genuinely tight speed-capped corner (the same >30deg
            // heading-change / v=sqrt(a*r) ceiling the cornering model uses
            // above), or is already sliding. That - not a blanket "is this a
            // corner" severity cut - is the honest "can ERS help here" test.
            // Deploy where the car is power-limited: not braking and not in a
            // genuinely tight, speed-capped corner (>30deg heading change). This
            // deliberately does NOT include the live slide state - keying the
            // deploy on oversteer/understeer made it binary-flip frame to frame
            // (deploy -> slide crosses the threshold -> stop -> slide recovers ->
            // deploy...), and combined with the throttle floor below that
            // flip-flop surged the car and made the steering controller chase the
            // induced line changes = the reported swerving. In a corner the boost
            // is instead scaled smoothly by the AI's own grip-managed throttle
            // (which already lifts on oversteer - see the exit-throttle block), so
            // a slide bleeds the boost off gradually instead of chopping it.
            float hereHeadingForErs = LocalHeadingChange(progress.distance);
            bool gripLimitedForErs = command.brake > 0.05f || hereHeadingForErs > 30f;
            command.ers = !gripLimitedForErs && raceManager.ShouldAiUseErs(participant, severityHere);

            // Force full throttle ONLY on a genuine straight. This is what stops
            // the throttle controller from lifting once ERS pushes the car past
            // its cruise target (the self-cancelling deploy), but forcing 1.0
            // inside a corner - even a flat one - is exactly what induced the
            // slide/flip-flop swerve above. In corners the deploy still happens;
            // the boost just rides the AI's normal grip-managed throttle, which
            // is smooth. Straights have no lateral load, so full throttle there
            // can't provoke a slide.
            bool genuineStraightForErs = hereHeadingForErs < 8f && severityHere < 0.1f &&
                                         vehicle.OversteerAmount < 0.4f && vehicle.UndersteerAmount < 0.4f;
            if (command.ers && genuineStraightForErs && !vehicle.PitRequested && !participant.missedPitEntryThisLap)
            {
                // Never above the safety ceiling set by traffic avoidance / blue
                // flags this frame - see safetyThrottleCeiling.
                command.throttle = Mathf.Min(1f, safetyThrottleCeiling);
                currentThrottle = command.throttle;
            }

            // DRS legality is decided entirely by RaceManager; drsUsageQuality only
            // decides whether this driver reliably remembers to press it, committed
            // once per zone so it never flickers mid-zone. The re-roll requires the
            // preceding illegal spell to have lasted >= 0.5s (a genuine between-
            // zones gap; blips are single frames) - see drsIllegalSeconds.
            bool drsLegal = raceManager.IsDrsAvailable(participant);
            if (drsLegal && !wasDrsLegalLastFrame && drsIllegalSeconds >= 0.5f)
            {
                // Part A.2: Expert-only, deterministic - if DRS is legal, use it.
                drsCommittedThisZone = isExpert || Random.value < profile.drsUsageQuality;
            }
            drsIllegalSeconds = drsLegal ? 0f : drsIllegalSeconds + Time.deltaTime;
            wasDrsLegalLastFrame = drsLegal;

            // Hold-open grace, matching the player exactly. PlayerVehicleInput keeps
            // the wing open for DrsAutoHoldSeconds past the end of a zone (closing
            // early on braking), which carries the full DRS package with it: the
            // +kph ceiling, the boost force and the ~54% drag cut. The AI had no
            // equivalent, so the player alone got that package on the run out of
            // every zone - a straight, invisible, one-sided advantage on every lap.
            // Same duration, same brake-closes-it rule, same source constant.
            if (drsLegal)
            {
                drsAutoHoldTimer = VehicleController.DrsAutoHoldSeconds;
            }
            else
            {
                drsAutoHoldTimer = Mathf.Max(0f, drsAutoHoldTimer - Time.deltaTime);
            }

            bool drsHeldOpen = (drsLegal || drsAutoHoldTimer > 0f) && command.brake <= 0.2f;
            command.drs = drsHeldOpen && drsCommittedThisZone;

            ApplySafetyCarFollowing(ref command);

            // Steering-authority compensation (per report after the corner-
            // speed realism pass - the AI "are swerving more than ever and
            // cant seem to go straight"): the physics envelope cut the yaw
            // produced per unit of steer by up to ~3.5x at speed. That is
            // correct for sustained cornering, but every gain, deadband and
            // slew constant in this controller was tuned in the OLD steer
            // units - small straight-line corrections became ~3x too weak to
            // hold the line, so lateral errors grew until the car finally
            // lurched across, over and over. Multiplying the composed command
            // by the old-to-new authority ratio restores the tuned
            // command-to-response scale for small corrections, while the
            // [-1,1] clamp plus the physics envelope still bound what the car
            // can actually do - a saturated command is simply full lock, so
            // sustained corner speed stays exactly as slow as the realism
            // pass demands.
            // [WallDiag] black-box fix: compensation applies to the PURSUIT
            // command only; the wall-aversion save (already capped at full
            // lock, already in real steering units) is added AFTER it. The old
            // order multiplied edgeRecovery (raw up to 2.3) by the saturated
            // x4 ratio - the command lived pinned at the clamp with the slew
            // limiter forced wide open (18/s), i.e. full-lock sawing: exactly
            // the swerve every log round kept showing near walls.
            // trafficNudge composed here too ([SwerveTape] verdict fix): like the
            // wall save, the traffic separation nudge is a direct steering
            // intention in real units - multiplying it by the saturated ratio was
            // measurably the top wheel-mover in every dump.
            command.steer = Mathf.Clamp(command.steer * SteerAuthorityCompensation(speedKph) + edgeRecovery + smoothedSteerAdjust, -1f, 1f);

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
                // Restart acceleration (per request - player was gaining ~2s on
                // every restart): the throttle is NO LONGER capped on the
                // green-flag handback - the AI floors it the instant control
                // returns, exactly like a player mashing the throttle at the
                // line. Only the STEERING is still eased for a beat so the car
                // doesn't snap sideways into whatever the overtake/defend logic
                // last decided.
                float steerCap = Mathf.Lerp(0.7f, 1f, rampBlend);
                command.steer = Mathf.Clamp(command.steer, -steerCap, steerCap);
            }

            // Steering slew-rate limiter - the actual swerve cure. Every prior
            // attempt (low-pass, pursuit-gain schedule above, smoothed yaw-damp)
            // failed to cut the REVERSAL COUNT, and the reason is now clear from the
            // telemetry (shimmy up to ~16 Hz, steer=30+ reversals/2s): an amplitude
            // shrink (low-pass / lower gain) leaves a limit cycle's zero-crossing
            // rate unchanged, and a smoothed derivative is itself filtered out at
            // those frequencies, so it did nothing. A slew-rate limiter is the
            // standard actuator-chatter cure and, unlike those, works at ALL
            // frequencies: it caps how fast the steer may change per second, so a
            // fast oscillation is physically flattened - exactly like a real steering
            // rack that cannot reverse instantly. The rate is tight on a genuine
            // straight (where quick steering is never needed, so this is free) and
            // opens right up as the corner severity rises; it is also forced wide
            // open whenever the wall-aversion recovery is active, so it can never
            // slow a genuine save. This subsumes and replaces the gain schedule's
            // job on the oscillation itself (the schedule is kept only for the mild
            // amplitude help it still gives).
            float steerStraightness = 1f - Mathf.Clamp01(severityHere / 0.12f);
            // THE SWERVE REGRESSION, and the reason the last aversion pass made things
            // worse rather than better. This was a BINARY disable: any edgeRecovery at
            // all above 0.05 threw the rate limiter wide open at 18/s, on the sound
            // principle that the limiter must never slow a genuine save. But widening
            // the wall-aversion bands to see walls earlier also made edgeRecovery
            // non-zero far more of the time - so the widened bands did not just add
            // avoidance, they switched OFF the single most effective anti-chatter
            // mechanism in this controller across a much larger share of the lap. More
            // aversion and more swerve, from one line.
            //
            // Now proportional: the limiter opens with urgency instead of flipping.
            // A faint early nudge at the edge of the band keeps nearly all of the
            // limiting (it has seconds of room - it does not need a fast wheel), and a
            // genuine emergency still reaches the full 18/s exactly as before, so the
            // "never slow a save" guarantee is intact where it actually matters.
            float steerSlewRate = Mathf.Max(
                Mathf.Lerp(18f, 4.5f, steerStraightness),
                Mathf.Lerp(4.5f, 18f, edgeUrgency));
            command.steer = Mathf.MoveTowards(steerSlewPrev, command.steer, steerSlewRate * Time.deltaTime);
            // slewPrev tracks the slew-limited PURSUIT command only (pre-yaw-damp),
            // so the yaw damper's fast correction below never feeds back into the
            // rate limiter and get smeared out next frame.
            steerSlewPrev = command.steer;

            // Yaw-rate damping applied HERE, after the slew limiter (see the compute
            // site far above): un-rate-limited so its phase lead survives to break
            // the pursuit/slew limit cycle the amplitude diagnostic pinned as the
            // last remaining straight-line swerve. Layered on top of, not fed into,
            // the slew state.
            command.steer = Mathf.Clamp(command.steer - yawDampSteer, -1f, 1f);

            // Understeer speed-shed reflex ([SwerveTape] round 3 - THE current
            // wall-hit signature): tapes show cars holding FIN=+-1.00 for
            // literal seconds at 300-390kph in sev 0.1-0.35 sweepers, yaw far
            // below demand, drifting metres wide - and never braking, because
            // the corner model believed the speed was legal. A real driver at
            // full lock who is STILL running wide lifts and brakes until the
            // car answers the wheel again; that reflex now exists. Gated well
            // above hairpin speeds (full lock at 60-120kph is normal rotation,
            // not understeer) and on a sustained 0.45s of saturation so a
            // momentary correction spike never triggers it. Braking directly
            // helps twice: it sheds the speed AND the yaw envelope rises as
            // speed falls, so steering authority returns and the timer clears
            // itself.
            // Round 4 widening (13/22 OUT report): most crash tapes showed the
            // wheel held at 0.75-0.95 - just UNDER the old 0.96-only trigger -
            // while the car visibly drifted outward for seconds with no brake.
            // The reflex now also fires on "steering hard AND still sliding
            // toward the edge" (towardEdgeRate is the same filtered drift the
            // wall-aversion band uses), starts sooner, and brakes harder.
            // Round 5 retune (per report - "AI extremely slow going into
            // corners"): the round-4 widening (0.82 + drift 1.2) fired during
            // ORDINARY hard turn-in - a committed corner entry routinely holds
            // 0.85+ steer while the car still carries outward drift from its
            // entry positioning - so the reflex brake-stomped healthy corner
            // entries. The secondary trigger now demands both harder lock and
            // a genuinely runaway drift, and the reflex arms a beat later. The
            // primary full-lock trigger (the verified crash signature:
            // seconds of pinned steering, no yaw response) is unchanged.
            bool steerAtLimit = Mathf.Abs(command.steer) > 0.96f ||
                                 (Mathf.Abs(command.steer) > 0.9f && towardEdgeRate > 1.8f);
            if (steerAtLimit && speedKph > 150f)
            {
                steerSaturationTimer += Time.deltaTime;
            }
            else if (Mathf.Abs(command.steer) < 0.7f || speedKph < 130f)
            {
                steerSaturationTimer = 0f;
            }

            if (steerSaturationTimer > 0.45f)
            {
                float shed = Mathf.Clamp01((steerSaturationTimer - 0.45f) / 0.5f);
                command.brake = Mathf.Max(command.brake, Mathf.Lerp(0.35f, 0.8f, shed));
                command.throttle = Mathf.Min(command.throttle, 0.05f);
            }

            // [WallDiag] black-box capture (see DescribeLateralState).
            bbFinalSteer = command.steer;
            bbEdgeRecovery = edgeRecovery;
            bbAuthorityComp = SteerAuthorityCompensation(speedKph);
            bbLineBias = lineBias;
            bbSeverityHere = severityHere;

            // [SwerveTape] frame capture - the full steering pipeline, every frame.
            {
                SwerveTapeFrame tapeFrame;
                tapeFrame.time = Time.time;
                tapeFrame.speedKph = speedKph;
                tapeFrame.lat = progress.lateralDistance;
                tapeFrame.halfW = edgeHalfWidth;
                tapeFrame.sev = severityHere;
                tapeFrame.drawn = hasLastDrawnOffset ? lastDrawnOffset : 0f;
                tapeFrame.lineBias = lineBias;
                tapeFrame.agg = aggressionOffset;
                tapeFrame.traffic = smoothedSteerAdjust;
                tapeFrame.pursuit = pursuitSteer;
                tapeFrame.comp = bbAuthorityComp;
                tapeFrame.edge = edgeRecovery;
                tapeFrame.yawDamp = yawDampSteer;
                tapeFrame.final = command.steer;
                tapeFrame.urgT = trajectoryUrgency;
                tapeFrame.urgP = positionUrgency;
                tapeFrame.towardRate = towardEdgeRate;
                tapeFrame.yawMeas = smoothedYawRateDeg;
                tapeFrame.refSnap = edgeReferenceSnap;
                tapeFrame.state = (int)overtakeState;
                swerveTape[swerveTapeCount % swerveTape.Length] = tapeFrame;
                swerveTapeCount++;
            }

            DiagnoseSwerve(progress, severityHere, lineBias, command.steer);
            DiagnoseSwerveAmplitude(progress, severityHere, lineBias);
            vehicle.SetCommand(command);
        }

        // Margin the geometric corner caps demand of genuinely fast corners
        // (see the round-3/4/5 history at the call sites): a corner whose own
        // uncapped speed is 270kph or below is untouched; above that the
        // racing line runs meaningfully tighter than the measured centreline
        // radius the caps are built on, so the reserve ramps to 15% by 390.
        // Keyed to the corner's speed, never the car's - approaching a slow
        // corner fast must not shrink its apex target.
        // Softened 0.8@380 -> 0.85@390 with the overall-pace round ("they
        // simply dont have the pace") - the reflex and edge nets carry more
        // of the fast-sweeper protection now.
        // Softened again with the true-ceiling round (player was sandbagging;
        // the whole field needs real pace): reserve tops out at 11% from
        // 280kph corners up - the speed-shed reflex and edge nets are the
        // fast-sweeper protection now, the blanket margin only takes the edge
        // off the very fastest sweeps.
        static float SweeperCorneringMargin(float cornerCapKph)
        {
            return Mathf.Lerp(1f, 0.89f, Mathf.Clamp01((cornerCapKph - 280f) / 120f));
        }

        // Controller-side compensation for the corner-speed realism pass. The
        // physics envelope (VehicleController.MaxYawRateDegPerSec) cut the yaw
        // produced per unit of steer by up to ~3.5x at speed - correct for
        // SUSTAINED cornering, but this controller's constants were all tuned
        // against the old response.
        //
        // Round 2 made this GRIP-AWARE, dividing the tuned reference by the car's
        // live MaxYawRateDegPerSec, on the reasoning that low grip shrinks the live
        // envelope further and a static ratio would under-compensate exactly when
        // grip was lowest. The loop-gain argument is sound and the intent was right,
        // but it is the direct cause of the report it was meant to fix, and the
        // mechanism runs straight through the understeer speed-shed reflex ~100 lines
        // above:
        //
        //   hard tyres -> grip ~0.69 in window -> the ratio wants 4.4 -> it hits its
        //   own x4 clamp and sticks there ([SwerveTape]: comp@max 100%) -> an ordinary
        //   mid-corner pursuit error x4 saturates the command far below the car's real
        //   limit (|steer|>0.98 on 60% of pre-crash frames) -> steerAtLimit latches ->
        //   past 0.45s the reflex applies 0.35-0.8 brake and cuts throttle to 0.05.
        //
        // So on hards the field brake-stomped its way around the lap, and the reflex
        // was doing precisely what it was designed to do - it had simply been fed a
        // saturation signal that no longer meant "at the limit". That is the whole of
        // "the AI genuinely just can't handle the low grip".
        //
        // The compensation is therefore grip-INVARIANT again (see
        // VehicleController.YawEnvelopeTuningRatio, which cancels every state term by
        // construction and lives beside the envelope so it cannot drift out of date
        // the way this hand-copied curve did). Round 2's objection stands but is
        // answered elsewhere and better: less grip genuinely does mean less yaw per
        // unit of steer, and the correct response to that is to CORNER SLOWER, which
        // MaxCorneringSpeedKph already delivers because it reads real grip. Restoring
        // loop gain by 1/grip cannot conjure rotation the tyres will not produce - it
        // only pushes the command into the clamp. What the car does instead now is run
        // a slightly wider line on hards, which is what a real driver does, rather
        // than braking mid-corner.
        float SteerAuthorityCompensation(float speedKph)
        {
            return Mathf.Clamp(VehicleController.YawEnvelopeTuningRatio(speedKph), 1f, 4f);
        }

        // Shared by every "something moved this car's transform out from under
        // its own cached driving state" path below - clears every piece of
        // transient attack/defend/avoidance/mistake state so the car resumes
        // normal driving cleanly instead of lunging off stale data. Does NOT
        // touch the track-progress reference itself (hasProgressReference/
        // lastProgressDistance) - callers decide separately whether they have
        // a trustworthy known distance to resync to, or need a fresh search.
        void ClearTransientDrivingState()
        {
            overtakeState = OvertakeState.Following;
            overtakeStateTimer = 0f;
            attackSide = preferredSide;
            aggressionOffset = 0f;
            hasHeldLane = false;
            mistakeSteer = 0f;
            mistakeTimer = Random.Range(3f, 8f);
            mistakeBrakeErrorTimer = 0f;
            hasCoveredThisApex = false;
            pressureFactor = 0f;
            followingTimer = 0f;
            dodgeMemoryTimer = 0f;
            dodgeMemorySide = 0f;
            drsCommittedThisZone = false;
            wasDrsLegalLastFrame = false;
            drsIllegalSeconds = 10f;
            currentThrottle = 0f;
            previousSeverityHere = 0f;
            smoothedLineBias = 0f;
            smoothedApexDistance = 400f;
            smoothedSteerAdjust = 0f;
        }

        // Race-control autopilot has just handed control back - see the call site
        // in Update() for why this specific transition (isRaceControlAutopilot
        // true -> false) is the one moment this controller can't trust its own
        // cached driving state. No trustworthy known distance exists here (the
        // car may have sat under autopilot for a lap or more), so this forces a
        // fresh global search next frame instead of trusting a stale
        // lastProgressDistance.
        void HandleRaceControlAutopilotReleased()
        {
            hasProgressReference = false;
            ClearTransientDrivingState();
            handbackRampTimer = HandbackRampDuration;
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
        // Use this ONLY when no trustworthy distance is known for where the
        // car actually landed - if RaceManager already knows the exact correct
        // distance (e.g. a normal pit-exit handoff), call
        // ResyncToKnownTrackDistance instead so the very next frame doesn't
        // have to guess via an unrestricted global search.
        public void ResyncAfterForcedReposition()
        {
            HandleRaceControlAutopilotReleased();
            activeManeuver = RecoveryManeuver.None;
            stuckDetectTimer = 0f;
        }

        // Pit-exit handoff fix (root cause 4): RaceManager.CompletePitExitMerge
        // already knows the exact correct track distance the car was just
        // guided to (finalDistance) - calling ResyncAfterForcedReposition here
        // instead threw that away by setting hasProgressReference = false,
        // forcing the very next AI frame to run an UNRESTRICTED
        // Track.GetProgress(transform.position) global nearest-centreline
        // search. Right at a pit exit - where the exit lane can run close to
        // another, unrelated part of the circuit - that search can resolve
        // onto the wrong nearby segment, and the AI then steers sideways
        // trying to reach a target on a completely different piece of track
        // (matching cars turning sideways into traffic right after release).
        // This preserves the known-correct segment across the handoff instead
        // of ever performing that search.
        public void ResyncToKnownTrackDistance(float distance)
        {
            lastProgressDistance = track.WrapDistance(distance);
            hasProgressReference = true;
            ClearTransientDrivingState();
            activeManeuver = RecoveryManeuver.None;
            stuckDetectTimer = 0f;
            handbackRampTimer = HandbackRampDuration;
            GameLog.Info("[PitExit] " + (participant != null ? participant.driverName : name) +
                         " AI resynced to exact known distance=" + lastProgressDistance.ToString("0.0") + " after pit-exit handoff.");
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

        // Double-count fix (per report - "AI going slowly, ~150 kph, not
        // crashed"): damage is ALREADY penalised in the shared physics
        // (Damage.PowerMultiplier cuts drive force, Damage.HandlingMultiplier
        // cuts grip/turn - for every car, the player included). This function
        // used to ALSO cap the AI's target speed on top, down to 0.62 (~186
        // kph, worse with worn grip) - a second penalty the player never gets,
        // so a contact-damaged AI (routine with aggression maxed) crawled while
        // an equally-damaged player would only feel the physics. It is now just
        // a small extra-caution nudge for a genuinely wrecked car (harder to
        // place accurately), never a second pace cap - the physics carries the
        // real damage slowdown.
        float AiDamagePaceMultiplier(float damagePercent)
        {
            if (damagePercent < 45f)
            {
                return 1f;
            }

            return Mathf.Lerp(1f, 0.93f, Mathf.InverseLerp(45f, 92f, damagePercent));
        }

        void ApplyDamageStrategy(ref VehicleCommand command, float damagePercent)
        {
            if (raceManager.CurrentSession == RaceWeekendSession.Qualifying || participant == null || participant.lapTracker == null)
            {
                return;
            }

            // The final-lap suppression in UpdatePitStrategy claims to override every
            // trigger, but this method is called AFTER it, so the damage trigger
            // simply re-armed the request it had just cleared - putting a damaged AI
            // that still owed its mandatory stop into the pits on the last lap,
            // throwing away multiple positions for a stop it can never recover.
            // Re-check the same rule here.
            if (damagePercent >= 42f && participant.pitStops == 0 &&
                !AiPitStrategyRules.FinalLapSuppressesNewRequest(participant.lapTracker.CompletedLaps, raceManager.RaceLaps))
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

        void ApplyTrafficAvoidance(ref VehicleCommand command, TrackProgress progress, float speedKph, RaceManager.AiDifficultyProfile profile, bool isExpert, bool committingToPit, bool nearCorner)
        {
            float brakeDemand = 0f;
            float throttleLimit = 1f;
            safetyThrottleCeiling = 1f;
            float steerAdjust = 0f;
            bool blockedLeft = false;
            bool blockedRight = false;
            // Genuinely door-to-door occupancy (fore-aft overlap, tight lateral
            // window) - the only thing that can justify the boxed-in lift below.
            bool blockedLeftAlongside = false;
            bool blockedRightAlongside = false;
            bool carDirectlyAhead = false;
            float nearestInLaneAheadZ = float.MaxValue;
            // Wider than the creep tracker below (which deliberately only counts
            // true same-lane blockers): anything loosely ahead that makes this
            // "traffic" for racing-line purposes.
            float nearestAnyAheadZ = float.MaxValue;
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
            // Car-avoidance fix round 3: floors raised again (was 0.34/0.6) - cars
            // (AI-vs-AI and AI-vs-player alike) were still making contact more than
            // clean racing should allow, so the baseline caution every tier starts
            // from is genuinely higher again, not just Expert's discount being
            // smaller.
            // Aggression pass (per request - "prioritize overtaking rather than
            // avoiding contact"): floors lowered (was 0.4/0.68) so the reduced
            // per-tier trafficAvoidanceCaution values in the difficulty profiles
            // actually take effect instead of being clamped back up.
            float cautionFloor = isExpert ? 0.3f : 0.52f;
            float cautionFactor = Mathf.Clamp(profile.trafficAvoidanceCaution, cautionFloor, 1.4f);
            if (isExpert)
            {
                cautionFactor *= 0.9f;
            }

            // Aggression pass: a car mid-move fights for the position instead of
            // yielding to its own caution - while genuinely attacking/side-by-side/
            // completing a pass, the throttle-lift and brake responses below are
            // scaled down so the avoidance layer stops cancelling the overtake the
            // state machine just committed to. Steering separation and the genuine
            // collision urgency brake still apply at the reduced factor - this
            // biases contests toward contact-tolerant hard racing, it doesn't
            // disable protection outright.
            // PreparingAttack included ("they just brake" fix, part 2): the prep
            // phase lasts 2.2-3.2s and used to run at FULL traffic caution - the
            // avoidance braking re-opened the gap the driver was trying to close,
            // PreparingAttack's stillThere check (<1.4s) then failed, and the move
            // aborted to BackingOut in a loop. A driver lining up a pass is
            // already committed to running close; the reduced factor still keeps
            // steering separation and genuine collision braking active.
            bool activelyOvertaking = overtakeState == OvertakeState.PreparingAttack ||
                                      overtakeState == OvertakeState.AttackingInside ||
                                      overtakeState == OvertakeState.AttackingOutside ||
                                      overtakeState == OvertakeState.SideBySide ||
                                      overtakeState == OvertakeState.CompletingPass;
            if (activelyOvertaking)
            {
                // Tuned WAY up (per request): a driver mid-attack backs itself.
                // Steering separation and the genuine collision brake remain.
                cautionFactor *= 0.3f;
            }

            // Race-start pack window (THE "AI slow off the line" cause, found at
            // last): on a two-column grid EVERY mid-pack car has a neighbour on
            // both flanks within the side-by-side detection window and a
            // same-column car ahead within the carDirectlyAhead window - so the
            // "boxed in on both sides, lift and wait for a gap" branch below
            // clamped every mid-pack AI to ~0.31-0.46 throttle WITH 0.16 brake for
            // the whole run to turn one, while the player launched at 100% and
            // drove straight through for 10+ places. That lift is correct racing
            // behaviour mid-race, but a real F1 start holds full throttle in a
            // dense pack and lifts only for genuine closure - which the
            // closing-speed urgency brake above already handles. During this
            // window the boxed-in lift and the side-by-side/soft throttle
            // cutbacks are suppressed entirely (steering separation and genuine
            // collision braking stay fully active).
            bool raceStartPackWindow = raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                                       raceManager.RaceElapsed < 12f;
            if (raceStartPackWindow)
            {
                cautionFactor *= 0.3f;
            }
            bool legalDrsHere = raceManager.IsDrsAvailable(participant);

            dodgeMemoryTimer = Mathf.Max(0f, dodgeMemoryTimer - Time.deltaTime);

            // Pit-entry vs stranded-car fix (per request): a car committed to the
            // pit entry (or already physically on the ramp) leaves the racing
            // surface, so a car declared ActuallyStranded on or near the track
            // must never pull it off its deterministic entry trajectory - the
            // brake/throttle-cap/dodge responses below used to fire for a
            // stationary wreck parked near the entrance exactly as if it were a
            // live queue, making pit-bound cars lift, swerve and miss the opening.
            // Only the sustained ActuallyStranded classification is skipped (a
            // Recovering car is still moving and stays avoided like any other),
            // and only while entering the pits - normal traffic keeps full
            // avoidance around stranded cars.
            bool enteringPit = committingToPit || track.IsOnPitEntryRamp(progress);

            // Car-avoidance fix round 3: widened again (was 30-76) - anticipation
            // now starts earlier at every speed than before, giving more real time
            // to react to a car ahead (AI or player) before it becomes an actual
            // collision instead of a late dodge/brake.
            float forwardWindow = Mathf.Lerp(36f, 86f, Mathf.Clamp01(speedKph / 320f));

            for (int i = 0; i < raceManager.Participants.Count; i++)
            {
                RaceParticipant other = raceManager.Participants[i];
                if (other == null || other == participant || other.retired || other.vehicle == null || !other.gameObject.activeSelf)
                {
                    continue;
                }

                // Cars deep in the pit lane (Entry/Service/Release) are
                // non-colliding ghosts on a physically separate corridor;
                // ignore them. A car in ExitMerge is DIFFERENT: it is rolling
                // down the exit lane about to join the live track, and the
                // pit-exit rework makes live traffic responsible for avoiding
                // it (the merging car never yields/stops for live traffic any
                // more - that inverted responsibility is what used to freeze
                // cars at the pit exit forever). Treat it exactly like any
                // other slow car ahead so this car brakes/steers around it.
                if (other.vehicle.IsPitGuided && other.pitPhase != PitPhase.ExitMerge)
                {
                    continue;
                }

                // See enteringPit above - stranded cars never gate a pit entry.
                if (enteringPit && other.recoveryState == CarRecoveryState.ActuallyStranded)
                {
                    continue;
                }

                // FPS fix: this loop runs every physics tick for every one of the
                // ~20 AI cars against every OTHER participant (~400+ pairs/tick,
                // ~20k+/second at a 50Hz fixed timestep), and used to run a full
                // InverseTransformPoint (matrix multiply) on every single pair
                // before checking whether the other car was even remotely close.
                // On a full grid, the overwhelming majority of pairs are cars on
                // opposite sides of the track - a cheap squared-distance check in
                // world space (no transform needed) filters those out first, so
                // the actual per-pair local-space math below only ever runs for
                // cars that could plausibly interact. Radius covers the widest
                // forward window (86m) plus the behind/lateral checks further
                // down with margin.
                float sqrDistance = (other.transform.position - transform.position).sqrMagnitude;
                if (sqrDistance > 100f * 100f)
                {
                    continue;
                }

                Vector3 local = transform.InverseTransformPoint(other.transform.position);
                float absX = Mathf.Abs(local.x);
                if (local.z <= -6f || local.z >= forwardWindow || absX >= 8.5f)
                {
                    continue;
                }

                if (local.z > 0.5f)
                {
                    // Feeds the low-speed anti-deadlock creep below: the nearest
                    // car actually sharing this lane ahead, regardless of the
                    // various response windows that follow. Narrowed from 3.0m
                    // (stopped-bunch fix round 2): at a standstill a car 2.5m to
                    // the SIDE does not physically block forward motion, but the
                    // old width counted every diagonal neighbour in a clump as
                    // an in-lane blocker, so nobody in a 2-3 wide bunch ever
                    // qualified to creep and the bunch froze. 1.6m is a genuine
                    // same-lane car (about a car's width).
                    if (absX < 1.6f)
                    {
                        nearestInLaneAheadZ = Mathf.Min(nearestInLaneAheadZ, local.z);
                    }

                    // 4.5 -> 2.6m ([PaceDiag]: the field never re-earned its
                    // racing line - a car ahead but a FULL LANE AND A HALF to the
                    // side still counted as "traffic ahead" and suspended line
                    // pursuit, so in any loose pack every car stayed off-line
                    // (trafficBlend 1.00 in every log) and seconds-per-lap slower
                    // than the player. Only a genuinely same-lane car ahead
                    // suspends the line now; diagonal/alongside cars are still
                    // covered by blockedLeft/blockedRight and avoidance braking.
                    if (absX < 2.6f)
                    {
                        nearestAnyAheadZ = Mathf.Min(nearestAnyAheadZ, local.z);
                    }

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
                    // Car-avoidance fix round 3: threshold/gate widened again (was
                    // 3.6s / 4.2m) and the brake/throttle response curves
                    // strengthened further (was 0.3-1 / 0.72-0.05) - reacts to a
                    // genuinely closing gap earlier and harder still.
                    // Launch/pack-compression fix (THE "AI slow off the line" cause):
                    // the old timeToContact divisor had a 1.5 m/s FLOOR - so a car
                    // ahead travelling at the SAME speed still read as "contact in
                    // gap/1.5 seconds", and any same-speed gap under ~6.3m tripped
                    // the urgency brake/throttle-cap. Off a standing start the pack
                    // compresses immediately, so every AI behind row one spent the
                    // whole launch throttle-capped to ~0.6 with brake taps, pacing
                    // the entire field to the front row's launch while the player
                    // freely drove around the outside for 10+ places. Urgency now
                    // only fires when GENUINELY closing (>0.8 m/s); a same-speed
                    // near-gap instead gets a gentle non-braking throttle trim to
                    // hold station. This also stops the pack yo-yoing behind a
                    // slightly slower car mid-race.
                    float closingMps = closingKph / 3.6f;
                    if (closingMps > 0.8f)
                    {
                        float timeToContact = local.z / closingMps;
                        if (timeToContact < 4.2f && absX < 4.6f)
                        {
                            float urgency = Mathf.Clamp01(1f - timeToContact / 4.2f);
                            // Launch concertina fix (diagnosed from the FIRING logs:
                            // the boost lands on every car, so the remaining launch
                            // killer is this brake pulsing through the compressing
                            // pack - brake force ~40 m/s2 dwarfs the 30 boost, and
                            // each pulse re-opens a gap the car behind then closes,
                            // re-triggering the pulse behind IT: the accordion the
                            // player drives around for 10+ places). During the
                            // race-start window braking is reserved for genuinely
                            // imminent contact (0.8s, per request); ordinary
                            // closing is managed by the throttle cut alone, which
                            // also scales the launch boost down (see
                            // VehicleController), so the pack stays dense AND
                            // fast like a real F1 start.
                            //
                            // Barrier-pileup fix: this suppression was scoped to
                            // "the launch straight" but had no idea whether a real
                            // corner was actually approaching - on a track whose
                            // first corner arrives within the pack window (a short
                            // start straight into a tight braking zone, e.g. a
                            // chicane right after the grid), it silently held the
                            // full collision brake off a tailgating car queued up
                            // for the SAME braking zone too, right up until 0.8s
                            // from impact - far too late to shed race-start speed
                            // for a tight corner, so cars piled through each other
                            // into the barrier. nearCorner (this driver's own
                            // upcoming-apex detection, identical to the braking-
                            // point model further up) always overrides the
                            // suppression, so the launch-pack easing only ever
                            // applies to genuine straight-line closing.
                            // Aversion fix (per report - "braking when they
                            // clearly have to swerve and overtake"): the urgency
                            // brake and hard throttle cut were the ONE pair of
                            // responses never scaled by caution or by a genuine
                            // lane check - they fired at full force for any car
                            // within 4.6m laterally, so a follower that had
                            // already swung 2.5-3m off line to pass STILL got
                            // braked to the leader's pace with a clear lane
                            // alongside. Both now scale by real same-lane
                            // overlap (zero from ~2.6m offset - a car you are
                            // passing is not a wall) and committed overtakers
                            // brake at half force. Genuinely imminent contact
                            // (<0.8s) keeps the full response regardless.
                            float laneBrakeOverlap = Mathf.Clamp01(1f - absX / 2.6f);
                            float attackBrakeScale = activelyOvertaking && timeToContact >= 0.8f ? 0.5f : 1f;
                            if (!raceStartPackWindow || timeToContact < 0.8f || nearCorner)
                            {
                                brakeDemand = Mathf.Max(brakeDemand, Mathf.Lerp(0.38f, 1f, urgency * urgency) * laneBrakeOverlap * attackBrakeScale);
                            }
                            if (laneBrakeOverlap > 0f)
                            {
                                throttleLimit = Mathf.Min(throttleLimit, Mathf.Lerp(0.65f, 0.02f, urgency * laneBrakeOverlap * attackBrakeScale));
                            }
                        }
                    }
                    else if (local.z < 5.5f && absX < 3.2f)
                    {
                        throttleLimit = Mathf.Min(throttleLimit, 0.85f);
                    }

                    // Tighter lane-only overlap for the soft cruising cap so a car with
                    // a real gap sharing the rough forward window (about to lap someone,
                    // or about to be let past) isn't throttle-capped for no real reason.
                    float laneOverlap = Mathf.Clamp01(1f - absX / 5.2f);
                    float proximity = Mathf.Clamp01((forwardWindow - local.z) / forwardWindow) * laneOverlap;
                    // Aggression pass: cruise cutback softened (was 0.42 at full
                    // proximity) so a chasing car sits in the gearbox of the car
                    // ahead and sets up a move instead of hanging back.
                    // Follower-aggression pass: the cruise cutback floor raised
                    // 0.55 -> 0.75 so a closing car keeps genuine pace in the
                    // leader's gearbox instead of being throttled into orbit.
                    float proximityCutback = Mathf.Lerp(1f, 0.75f, proximity * Mathf.Clamp01(closingKph / 40f));

                    // A legitimate DRS or slipstream tow is not traffic to avoid -
                    // lower-caution (higher-difficulty) followers commit to the draft
                    // instead of backing out of a gap they are supposed to be
                    // exploiting. Slightly wider gates than DRS-only, since a real
                    // slipstream tow tolerates a bit more lateral offset/closing
                    // speed than sitting dead in someone's DRS zone.
                    bool legitimateTow = (legalDrsHere || vehicle.SlipstreamActive) && absX < 4.5f && closingKph < 18f;
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
                    // THE SQUARE WAVE, source #1. This was a hard "absX < 3.0" gate on
                    // a term worth up to 0.85 of full lock, so a neighbour hovering
                    // near three metres of lateral offset - completely normal in a
                    // pack - toggled a near-half-lock push on and off frame to frame.
                    // Every previous round tried to smooth that downstream (a slew
                    // limiter, then a lower ceiling, then a sign latch), each of which
                    // cost real avoidance because the signal being smoothed was still
                    // a square wave. The window now FADES: full strength inside the
                    // original 3.0m, tapering to nothing by 4.2m. A car crossing the
                    // boundary produces a continuous ramp instead of a step, so there
                    // is nothing left to filter - and the wider reach means the dodge
                    // begins earlier, which is the buff.
                    if (absX < 4.2f && local.z < forwardWindow * 0.7f)
                    {
                        float laneFade = Mathf.Clamp01(Mathf.InverseLerp(4.2f, 3.0f, absX));
                        // The flag itself keeps its original footprint - it gates
                        // braking and the don't-dodge-into-an-occupied-flank logic
                        // elsewhere, which are decisions, not blendable pushes.
                        if (absX < 3.0f)
                        {
                            carDirectlyAhead = true;
                        }
                        // Pile-up fix ("one AI hits the barrier, the rest hit it
                        // too"): when the blocking car is dead-ahead (|local.x| <
                        // 0.4, e.g. exactly where a crash tends to leave a car
                        // sitting - ON the tight racing line), this used to fall
                        // back to preferredSide, a FIXED per-driver constant
                        // decided once at the grid (which side of the grid they
                        // started on) with no idea where the wall actually is.
                        // On the half of the field whose preferredSide happened to
                        // point at the wall, every following car dodged INTO the
                        // wall side instead of away from it - the exact pile-up
                        // mechanism reported. Now picks whichever side has more
                        // real room between the car and the track edge.
                        float roomRight = LocalHalfWidthAt(progress.distance) - progress.lateralDistance;
                        float roomLeft = LocalHalfWidthAt(progress.distance) + progress.lateralDistance;
                        float roomBasedSide = roomRight > roomLeft ? 1f : -1f;
                        float rawDodgeSide = Mathf.Abs(local.x) < 0.4f ? roomBasedSide : -Mathf.Sign(local.x);
                        if (dodgeMemoryTimer <= 0f)
                        {
                            dodgeMemorySide = rawDodgeSide;
                        }
                        // Part A.6: Expert resolves a dodge decision faster instead of
                        // lingering on a stale side-choice that's no longer relevant.
                        dodgeMemoryTimer = isExpert ? 0.6f : 1.1f;
                        float dodgeStrength = Mathf.Clamp01(1f - local.z / (forwardWindow * 0.7f));
                        // Dodge authority trimmed back a bit (per request - "AI
                        // swerve out of others' way too readily, hurts
                        // wheel-to-wheel racing"): this was how eagerly the AI
                        // steers CLEAR of a car ahead sharing its lane, distinct
                        // from the genuine collision brake/throttle cut above
                        // (untouched, still a real safety response). Cut from
                        // 0.24-0.78 to 0.18-0.6 so the AI holds its line and
                        // fights for the corner more instead of backing out of
                        // contested space at the first overlap.
                        // Swerve authority raised (0.18-0.6 -> 0.3-0.85, per
                        // report): when a car IS parked in this lane, the
                        // response the situation calls for is a decisive lateral
                        // move, not a brake-and-wait.
                        steerAdjust += dodgeMemorySide * Mathf.Lerp(0.3f, 0.85f, dodgeStrength) * laneFade;
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
                // Car-avoidance fix round 3: detection window widened again (was
                // 8.5/6.2) and the push-away response strengthened further (was
                // 0.14-0.6) - catches a converging side-by-side pair (AI or player)
                // earlier and pushes apart harder once detected.
                // THE SQUARE WAVE, source #2. absX already tapers to zero at 7m, but
                // the FORE-AFT edge did not: alongsideFactor below only fades a car
                // that is behind, so a car ahead sat at full strength right up to
                // local.z = 9.5 and then vanished. At racing closing speeds a
                // neighbour crosses that boundary several times a second. The window
                // now reaches 13m and fades in across the last 4 of them, so a car
                // arriving alongside ramps up smoothly - earlier detection AND no
                // step, from the same change.
                if (Mathf.Abs(local.z) < 13f && absX < 7f)
                {
                    // The blocked flags keep their original 9.5m footprint: they are a
                    // binary "is this flank occupied" decision feeding the dodge logic,
                    // not a blendable push, and widening them would make cars refuse
                    // dodges for neighbours that are not really alongside yet.
                    if (Mathf.Abs(local.z) < 9.5f)
                    {
                        if (local.x < 0f)
                        {
                            blockedLeft = true;
                        }
                        else
                        {
                            blockedRight = true;
                        }
                    }

                    // Right-of-way fix (per report - "the AI ahead ALSO brakes
                    // when someone is behind them on the line"): this window
                    // spans 9.5m BEHIND the car too, so a follower tucked in the
                    // leader's wake set the leader's own flank flags and made the
                    // LEADER lift and swerve for the car chasing it. A car
                    // clearly behind is the FOLLOWER's problem (its own
                    // ahead-car logic above manages the gap); the leader only
                    // responds to cars genuinely alongside. alongsideFactor
                    // fades the leader's responses to zero once the other car
                    // sits more than ~4.5m back; the raw blocked flags stay set
                    // (never dodge INTO an occupied flank, wherever the occupant
                    // sits fore-aft).
                    float alongsideFactor = Mathf.InverseLerp(-4.5f, -1f, local.z);
                    // Matching fade at the FRONT of the window (see above): full
                    // strength once a car is within 9m ahead, ramping from zero at 13m.
                    float approachFactor = Mathf.Clamp01(Mathf.InverseLerp(13f, 9f, local.z));
                    float sideOverlap = Mathf.Clamp01(1f - absX / 7f) * alongsideFactor * approachFactor;
                    steerAdjust += -Mathf.Sign(local.x) * Mathf.Lerp(0f, 0.52f, sideOverlap);
                    // Side-by-side throttle cutback suppressed during the race-start
                    // pack window - see raceStartPackWindow above. Every grid car
                    // has flank neighbours by definition; lifting for them is what
                    // killed the AI launch. Steering separation above stays.
                    // nearCorner always overrides (barrier-pileup fix, same
                    // reasoning as the urgency brake above).
                    if (sideOverlap > 0f && (!raceStartPackWindow || nearCorner))
                    {
                        // Aggression pass: softened (was 0.5 at full overlap) -
                        // holding station alongside is racing, lifting out of a
                        // fight the driver could win is not.
                        float sideCutback = Mathf.Clamp01(1f - (1f - Mathf.Lerp(1f, 0.62f, sideOverlap)) * cautionFactor);
                        throttleLimit = Mathf.Min(throttleLimit, sideCutback);
                    }

                    if (alongsideFactor > 0.4f && absX < 5f)
                    {
                        if (local.x < 0f)
                        {
                            blockedLeftAlongside = true;
                        }
                        else
                        {
                            blockedRightAlongside = true;
                        }
                    }
                }
            }

            // Boxed in on both sides with a car ahead: lift cleanly and wait for a
            // gap instead of forcing a three-wide wedge. Suppressed during the
            // race-start pack window (see raceStartPackWindow above) - EVERY
            // mid-pack grid car is "boxed in" by definition off a standing start,
            // and this clamp was the single thing killing the AI launch; the
            // genuine-closing urgency brake still protects against real contact.
            // nearCorner always overrides (barrier-pileup fix): a boxed-in car
            // arriving at the very first braking zone still needs to be able to
            // lift/brake, not just hold flat because the launch window happens
            // to still be open.
            if (blockedLeft && blockedRight)
            {
                steerAdjust = 0f;
                // Boxed-in fix (per report): the flags above can be raised by
                // cars BEHIND, and carDirectlyAhead counts a car up to ~60m
                // down the road - so a mid-train leader with two followers
                // tucked diagonally behind it was reading "boxed in" and
                // braking for the cars chasing it. The lift now needs genuine
                // door-to-door occupancy on BOTH flanks and a real car inside
                // 15m ahead - the actual three-wide-wedge situation it exists
                // for.
                if (blockedLeftAlongside && blockedRightAlongside && nearestInLaneAheadZ < 15f && (!raceStartPackWindow || nearCorner))
                {
                    // Aggression pass: lift softened (was 0.34 cap / 0.16 brake) -
                    // boxed-in should mean "hold position and wait for a gap", not
                    // "give up the run entirely".
                    throttleLimit = Mathf.Min(throttleLimit, Mathf.Clamp01(1f - (1f - 0.5f) * cautionFactor));
                    brakeDemand = Mathf.Max(brakeDemand, 0.12f * Mathf.Clamp01(cautionFactor));
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

            // Turn-one standstill fix ("cars mysteriously pause on track during
            // the first couple of corners"): when the field funnels into the
            // opening corners, mid-pack cars slow each other into a standing
            // queue - and the boxed-in brake plus the stacked throttle cutbacks
            // above then HELD them there, every car waiting on a neighbour that
            // was itself waiting, a mutual-yield deadlock that read as cars
            // pausing on track for no reason. A car that is already near-stopped
            // with genuine free space ahead in its own lane (the nearest in-lane
            // car more than a car length plus margin away, or no car at all)
            // never needs an avoidance brake and always gets enough throttle
            // authority to creep forward - the queue now unwinds front-to-back
            // instead of gridlocking. Cars genuinely nose-to-tail (gap under
            // 4.5m) still hold their brake until the car ahead moves off.
            // Stopped-bunch deadlock fix: this creep used to require a 4.5m
            // in-lane gap, but a pack that has braked to a stop (e.g. funnelled
            // onto the same line into the first corners) settles at 2-3m
            // spacing - UNDER the old threshold, so no car in the bunch could
            // ever creep and the whole queue just sat there. The urgency brake
            // above only fires on genuine closing speed (>0.8 m/s), so letting
            // a near-stationary car creep from 2.2m is safe: it inches forward,
            // the closing-speed logic resumes control, and the queue unwinds
            // instead of freezing. Pit-committed cars keep their queueing
            // behaviour unchanged.
            if (speedKph < 30f && !committingToPit && nearestInLaneAheadZ > 2.2f)
            {
                brakeDemand = 0f;
                throttleLimit = Mathf.Max(throttleLimit, 0.4f);
            }

            // Stopped-bunch fix round 2, the guarantee: whatever the pair
            // geometry above concluded, a car that has been at walking pace for
            // several seconds under green racing conditions is wedged in
            // traffic, and staying wedged is the one unacceptable outcome. Force
            // a crawl (throttle floor, traffic brake released, steer toward
            // whichever side of the road has more room) until real speed
            // returns; contact risk at <8 kph is trivial next to a race
            // permanently stalled. Never fires while pitting, pace-limited
            // (SC/VSC/yellow hold slow speeds legitimately), pre-race, or in the
            // first seconds after lights-out (launch spacing is handled by the
            // pack-window logic above).
            // Deadlock-loop fix (per report - cars still stopping on track):
            // the unstick used to be disabled whenever a race-control speed cap
            // was active. But a stopped clump IS an incident - it raises a
            // local yellow, the yellow's cap disabled the unstick, the cars
            // stayed stopped, and the yellow stayed out: a self-sustaining
            // freeze. Being pace-CAPPED never legitimately means being
            // STOPPED, so the unstick now runs under caution too (the physical
            // limiter still holds the crawl under the posted cap); only the
            // pre-race/launch window and pit entries stay excluded.
            bool raceRunning = raceManager.CanDrive &&
                               raceManager.CurrentSession != RaceWeekendSession.Qualifying &&
                               raceManager.RaceElapsed > 6f;
            // Threshold aligned to the crawl rule's <=10 (was <8): the crawl
            // rule that teleports a car fires at <=10 kph, so the unstick that
            // is supposed to prevent it must engage across the SAME band - a
            // car steadily at 8-10 kph used to trip the crawl rule while never
            // accumulating any unstick time at all.
            if (raceRunning && !committingToPit && speedKph <= 10f)
            {
                stuckInTrafficSeconds += Time.deltaTime;
            }
            else
            {
                stuckInTrafficSeconds = 0f;
            }

            // WALL-PIN ESCAPE ([WallStickDiag]: "George Russell pinned on 'Bridge
            // concrete wall' for 5.3s speed=20kph throttle=1.00 brake=0.00", and a
            // dozen more between 1.2s and 5.3s at 7-20 kph). The unstick above could
            // never catch these: its gate is speedKph <= 10, and a car GRINDING along
            // a barrier is still rolling at 15-20 kph - moving, going nowhere, and
            // accumulating no unstick time whatsoever. Meanwhile it is flooring the
            // throttle with the wheel turned into the wall, which is precisely what
            // cancels the physics peel-off in VehicleController.DampenWallContactResponse
            // (the guaranteed separation velocity is real, but it is re-pinned every
            // tick by the drive force behind it).
            //
            // Sustained wall contact is therefore its own trigger, independent of the
            // speed gate: point the nose along the escape normal, and - the part that
            // actually matters - stop driving into the barrier so the peel-off wins.
            // Deliberately NOT a reverse: at 15-20 kph the car still has rolling
            // momentum to carry it out, and reversing into the pack behind is worse
            // than the scrape. Under half a second of contact is an ordinary racing
            // graze and is left completely alone.
            // committingToPit excluded for the same reason the unstick above excludes
            // it: a car on its pit-entry line is steering a deterministic trajectory
            // that this override would drag it off, and its own guidance owns that.
            float wallPinSeconds = vehicle != null ? vehicle.SustainedWallContactSeconds : 0f;
            if (raceRunning && !committingToPit && wallPinSeconds > 0.5f && speedKph < 45f)
            {
                Vector3 escape = vehicle.SustainedWallEscapeNormal;
                if (escape.sqrMagnitude > 0.01f)
                {
                    // Positive when the way out is to the car's right.
                    float escapeSide = Vector3.Dot(escape, vehicle.transform.right);
                    // Ramps in over the first half-second of the pin so a brief scrape
                    // gets a hint of correction and a genuine 5-second wedge gets the
                    // full commitment.
                    float pinCommit = Mathf.Clamp01((wallPinSeconds - 0.5f) / 0.5f);
                    command.steer = Mathf.Clamp(Mathf.Lerp(command.steer, Mathf.Sign(escapeSide), pinCommit), -1f, 1f);
                    // Throttle eased rather than cut: enough drive to keep rolling out
                    // along the wall, never enough to hold the car against it.
                    command.throttle = Mathf.Min(command.throttle, Mathf.Lerp(1f, 0.3f, pinCommit));
                    command.brake = 0f;
                    brakeDemand = 0f;
                    // The dodge/side-by-side nudge merged in at the end of this method
                    // must not fight the way out - a barrier is not a car to dodge.
                    steerAdjust = 0f;
                }
            }

            if (stuckInTrafficSeconds > 0.8f)
            {
                // Root-cause fix (why cars crawl for 3s at all): flooring the
                // throttle FORWARD drives a car straight into whatever stopped
                // it - the back of a dead car, or a barrier - so it never
                // escaped and only the crawl rule saved it. The unstick now
                // reads what is actually ahead: with a car within ~6m directly
                // ahead (nearestInLaneAheadZ) or the car pinned hard against a
                // track edge, it REVERSES and steers toward the open side (the
                // same reverseAssist the dedicated recovery maneuver uses);
                // otherwise it floors forward as before. Either way it is a
                // genuine override - any upstream brake (corner model, edge
                // brake, handback ramp) is cleared, since the final merge is
                // Max(command.brake, brakeDemand) / Min(command.throttle,
                // throttleLimit) and an uncleared upstream brake was exactly
                // what kept the "unstuck" car braked at walking pace.
                brakeDemand = 0f;
                float unstickRoomRight = LocalHalfWidthAt(progress.distance) - progress.lateralDistance;
                float unstickRoomLeft = LocalHalfWidthAt(progress.distance) + progress.lateralDistance;
                float openSide = unstickRoomRight > unstickRoomLeft ? 1f : -1f;

                float localHalf = LocalHalfWidthAt(progress.distance);
                bool pinnedToEdge = localHalf > 0.1f && Mathf.Abs(progress.lateralDistance) > localHalf - 1.2f;
                // Reverse only for a car that is genuinely STOPPED (<=3 kph) and
                // blocked - a car merely crawling at 4-10 kph in slow traffic
                // must keep trying forward, never throw it into reverse.
                bool blockedAhead = speedKph <= 3f && (nearestInLaneAheadZ < 6f || pinnedToEdge);

                if (blockedAhead)
                {
                    // Back out of the obstacle, nose swinging toward open road.
                    // steerAdjust is zeroed so the leftover dodge/side-by-side
                    // steering (merged in at the end of this method) can't fight
                    // the reverse-out line.
                    command.throttle = 0f;
                    command.brake = 0f;
                    command.reverseAssist = true;
                    command.steer = Mathf.Clamp(-openSide, -1f, 1f);
                    steerAdjust = 0f;
                }
                else
                {
                    command.brake = 0f;
                    command.throttle = Mathf.Max(command.throttle, 0.4f);
                    steerAdjust += openSide * 0.35f;
                }
            }

            if (brakeDemand > 0f)
            {
                command.brake = Mathf.Max(command.brake, brakeDemand);
            }

            command.throttle = Mathf.Min(command.throttle, throttleLimit);
            safetyThrottleCeiling = Mathf.Min(safetyThrottleCeiling, throttleLimit);

            // Racing-line traffic gate (see lineTrafficNearby field): a car
            // within ~18m ahead or anyone alongside means this car is racing in
            // traffic and must hold its lane rather than converge on the shared
            // optimal line.
            // Hysteresis (per report - "theyre swerving on straights", with
            // the logs showing trafficBlend snapping 0.00 <-> 1.00 between
            // samples of the same car): the raw gate flickered every time a
            // neighbour crossed the 18m/alongside detection edge, and each
            // flicker re-blended the lateral target between the racing line
            // and the held lane - a multi-metre zigzag per flicker, in packs,
            // on straights. Engagement stays instant (collision safety), but
            // release now requires a full second of continuously clear road
            // before the car starts drifting back to the racing line.
            bool trafficNearNow = nearestAnyAheadZ < 18f || blockedLeft || blockedRight;
            if (trafficNearNow)
            {
                lineTrafficReleaseTimer = 1.0f;
            }
            else
            {
                lineTrafficReleaseTimer = Mathf.Max(0f, lineTrafficReleaseTimer - Time.deltaTime);
            }

            lineTrafficNearby = trafficNearNow || lineTrafficReleaseTimer > 0f;

            // SEPARATION-NUDGE BUDGET ([SwerveTape] VERDICT, latest dump: trafficNudge
            // is the #1 wheel-mover at final-steer reversals - 11 of 15 - and carries
            // a MEAN magnitude of 0.48, i.e. half the car's entire steering authority
            // permanently spent on dodging). Two problems, one clamp:
            //
            // 1) The dodge push (up to 0.85) and the side-by-side push (up to 0.52)
            //    are ADDED, so the raw term could demand +-1.37 - 137% of full lock -
            //    from separation alone, before the racing line has asked for anything.
            //    Composed with a pursuit command that is itself authority-compensated,
            //    that is guaranteed saturation, and the tapes show it: |steer|>0.98 on
            //    60% of the frames leading into a wall hit. A car pinned at its
            //    actuator limit is a bang-bang controller, not a driver - it cannot
            //    proportionally correct anything, so it saws lock-to-lock and ends up
            //    in the barrier. A nudge is by definition the SMALL correction layered
            //    on top of the line; it now gets a bounded slice of the wheel and the
            //    line keeps the rest.
            // 2) The detection windows (9.5m fore/aft, 7m lateral) are crossed in a
            //    blink at racing speed, so neighbours flicker in and out of them - the
            //    tapes show the raw term square-waving 0.00 <-> 0.30 between adjacent
            //    rows, and at 300kph each flicker is metres of lateral movement.
            //
            // ROUND 3. Round 2 committed the dodge SIDE and suppressed any opposing
            // demand for 0.35s, which is why aversion got worse rather than better: a
            // suppressed demand is not a calmer dodge, it is NO dodge. In a pack the
            // threat genuinely does switch sides, and the latch spent a third of a
            // second refusing to react each time it did - trading a cosmetic problem
            // for a contact. Removed entirely, along with its state.
            //
            // The flicker is killed at its SOURCE instead, in the detection windows
            // themselves (see the dodge and side-by-side terms above, both now faded
            // in over a band instead of switching on at a hard threshold). A term that
            // ramps up from zero as a neighbour approaches cannot square-wave however
            // marginal that neighbour is, so no downstream suppression is needed and
            // no avoidance is ever withheld. That also makes the windows genuinely
            // WIDER, which is the aversion buff: threats are seen further out and
            // answered earlier, so a smaller push does the same job.
            //
            // Ceiling stays raised past the pre-swerve-fix authority (0.9/0.45), still
            // easing with speed because at 300kph even 0.45 moves the car several
            // metres and the racing line needs headroom under the shared [-1,1] clamp.
            float trafficNudgeCeiling = Mathf.Lerp(0.9f, 0.45f, Mathf.Clamp01((speedKph - 120f) / 180f));
            steerAdjust = Mathf.Clamp(steerAdjust, -trafficNudgeCeiling, trafficNudgeCeiling);

            // Pit-entry queueing fix: while committing to a pit stop, braking/
            // throttle reduction for a car ahead (e.g. another car already queued
            // into the same entry) is kept exactly as-is - cars should still slow
            // and queue behind one another. But the ordinary dodge/side-by-side
            // steerAdjust above can swing up to +-0.78, which was easily strong
            // enough to cancel or reverse the deterministic pit-entry line computed
            // above, reading as the AI dodging sideways and missing the entrance
            // entirely in a dense pack. Constrained to a small emergency-separation
            // nudge only while committing - enough to avoid clipping another car,
            // never enough to pull the car off the entry trajectory and into a
            // three-wide scatter across the straight.
            if (committingToPit)
            {
                steerAdjust = Mathf.Clamp(steerAdjust, -0.12f, 0.12f);
            }

            // Launch-window steering: the dodge/side-by-side adjustments can swing
            // to +-0.78, and on a dense grid every car has triggers on both flanks
            // - full-authority dodging off the line reads as the pack zigzagging
            // and scrubs launch speed through lateral grip. The opening fan-out
            // lane offsets already separate the field; traffic steering is capped
            // to a separation nudge for those first seconds.
            if (raceStartPackWindow)
            {
                steerAdjust = Mathf.Clamp(steerAdjust, -0.3f, 0.3f);
            }

            // Erratic-swerve fix: rate-limit the nudge itself (see
            // smoothedSteerAdjust) instead of applying this frame's raw value
            // straight to the wheel. ~4 units/second of adjustment change is
            // still fast enough to react to a genuinely closing car within a
            // fraction of a second, but can no longer jump instantly between
            // opposite pushes just because a marginal neighbour flickered in or
            // out of a detection window.
            // [SwerveTape] VERDICT fix - the tape named trafficNudge the #1
            // wheel-mover at final-steer reversals (28/35, 25/47, 17/27...):
            // 1) This nudge was added HERE, before the authority compensation,
            //    so its tuned +-0.3-0.85 got multiplied by the x2-4 ratio at
            //    speed - tape rows verify it exactly: pur=0.00 traf=0.30
            //    comp=4.0 yawD=0.26 -> FIN=0.74 = clamp(0.3*4)-0.26. A 30%
            //    separation nudge became a near-full-lock slam at 390kph.
            //    It is now only COMPUTED here and composed into the command
            //    AFTER compensation (with edgeRecovery), in real steer units,
            //    where the final slew limiter also rate-limits its flapping.
            // 2) The 4/s slew followed every flicker of the detection windows
            //    (tape: 0.00 <-> 0.30 between adjacent rows). The rate now
            //    eases down with speed - a dodge at 60kph still snaps in fast,
            //    while at 350kph (where 0.3 steer moves the car metres in a
            //    blink and windows flicker most) the target is followed
            //    smoothly instead of square-wave.
            // Round 2: rate raised (4/1.6 -> 6/3) alongside the sign latch above. The
            // slow rate existed to smear out the square wave; with the sign committed
            // there is no square wave left to smear, and a slow rate on a
            // single-direction dodge is just a late reaction. This is the other half
            // of the car-aversion buff - the response now arrives promptly, which
            // again means less of it is needed.
            float steerAdjustSlewRate = Mathf.Lerp(6f, 3f, Mathf.Clamp01((speedKph - 140f) / 220f));
            smoothedSteerAdjust = Mathf.MoveTowards(smoothedSteerAdjust, steerAdjust, Time.deltaTime * steerAdjustSlewRate);
        }

        // [SwerveDiag] (per report - AI "swerving", predates and is unrelated to
        // the ERS work). Counts sign reversals of the steering and each lateral
        // component while the car is on a STRAIGHT (severity < 0.15) - on a
        // straight there is no corner to justify a reversal, so a run of them IS
        // the weave. When the raw steer reverses 3+ times inside a 2s window it
        // logs the per-component reversal counts and the overtake-state churn, so
        // the paste names which input is oscillating: lineBias (racing line),
        // aggressionOffset (overtake positioning), smoothedSteerAdjust (traffic
        // nudge), or the base steer alone (pursuit controller). Debug.LogWarning
        // (not GameLog, which is verbosity-gated).
        void DiagnoseSwerve(TrackProgress progress, float severityHere, float lineBias, float finalSteer)
        {
            if (raceManager == null || raceManager.CurrentSession == RaceWeekendSession.Qualifying)
            {
                return;
            }

            // Reset the 2s counting window.
            if (Time.time - swerveWindowStart > 2f)
            {
                swerveWindowStart = Time.time;
                swerveSteerRev = 0;
                swerveAggRev = 0;
                swerveAdjustRev = 0;
                swerveLineRev = 0;
                swerveStateChanges = 0;
            }

            if (overtakeState != swervePrevState)
            {
                swerveStateChanges++;
                swervePrevState = overtakeState;
            }

            // Only score reversals on a straight - a corner legitimately reverses
            // steering/line, and that is not the swerve being reported.
            if (severityHere < 0.15f)
            {
                // Steer deadband raised 0.045 -> 0.12: at 0.045 the alarm fired on
                // +-5-10%-of-lock micro-corrections - the normal breathing of any
                // proportional controller, invisible on screen - and papered the
                // console with steer=3 floor entries that drowned the real events.
                // Only sawing past ~12% of lock (genuinely visible movement) counts.
                swerveSteerRev += CountReversal(finalSteer, 0.12f, ref swerveSteerSign);
                swerveAggRev += CountReversal(aggressionOffset, 0.35f, ref swerveAggSign);
                swerveAdjustRev += CountReversal(smoothedSteerAdjust, 0.05f, ref swerveAdjustSign);
                swerveLineRev += CountReversal(lineBias, 0.6f, ref swerveLineSign);
            }

            // Alert bar raised 3 -> 7 reversals/2s (per report - "at this point
            // i should have 0 swerve alerts"): at 3, the log fired on the
            // detector's own floor - three ~12%-of-lock micro-corrections in
            // two seconds, invisible on screen at realistic yaw rates - so the
            // console read as "still swerving everywhere" no matter how much
            // the real weave improved, and genuine events drowned in floor
            // entries. Seven reversals in two seconds is an actual visible
            // saw; anything below that is a controller breathing normally.
            if (swerveSteerRev >= 7 && Time.time - swerveLastLogTime > 3f)
            {
                swerveLastLogTime = Time.time;
                string who = participant != null && participant.driverData != null ? participant.driverData.displayName : "AI";
                Debug.LogWarning("[SwerveDiag] " + who + " weaving on a straight (norm " + progress.normalized.ToString("0.00") +
                    ") - reversals/2s: steer=" + swerveSteerRev + " lineBias=" + swerveLineRev +
                    " overtakeOffset=" + swerveAggRev + " trafficNudge=" + swerveAdjustRev +
                    " | overtakeStateChanges=" + swerveStateChanges + " state=" + overtakeState +
                    " | now steer=" + finalSteer.ToString("0.00") + " lineBias=" + lineBias.ToString("0.00") +
                    " agg=" + aggressionOffset.ToString("0.00") + " adjust=" + smoothedSteerAdjust.ToString("0.00") +
                    " trafficBlend=" + lineTrafficBlend.ToString("0.00") +
                    " (whichever reversal count is high is the oscillating input).");
                DumpSwerveTape(who, "steer-reversals(" + swerveSteerRev + "/2s)", progress.normalized);
            }
        }

        // [SwerveDiag amplitude] Names the driver of the VISIBLE, wall-crashing
        // weave, which the reversal counter above misses. Over a 4s window on a
        // sustained straight it records the peak-to-peak swing of the car's real
        // lateral position and of each thing that could command it. If the car
        // actually swung more than ~5m across the road (a genuine wall-threatening
        // excursion, not normal line variation) it logs every component's swing in
        // the same units, so the ONE whose swing matches the car's is the cause:
        //   drawnLine  -> the precomputed racing line itself is running edge-to-edge
        //                 on this straight (fix = the line data / straightTrim).
        //   lineBias   -> the blend/slew is amplifying or lagging the line target.
        //   overtakeOff-> the pass state machine is throwing the car sideways.
        //   (none big) -> the car swings but no target does => the steering
        //                 CONTROLLER is oscillating (pursuit/slew/yaw loop), not the
        //                 target. That is the pure limit-cycle case.
        void DiagnoseSwerveAmplitude(TrackProgress progress, float severityHere, float lineBias)
        {
            if (raceManager == null || raceManager.CurrentSession == RaceWeekendSession.Qualifying)
            {
                return;
            }

            // A corner legitimately swings the car across the road; only measure
            // where the road is genuinely straight. Any corner sample voids the
            // whole window so a corner's line move is never miscounted as a weave.
            if (severityHere >= 0.12f)
            {
                swerveAmpHasSample = false;
                swerveAmpHasPrevLat = false;
                swerveAmpWindowStart = Time.time;
                return;
            }

            // Flyover leg-misattribution guard (same failure the [PitDiag] wall-hit
            // log guards with dy): near the start/finish flyover the two track legs
            // overlap in XZ, so GetProgress's nearest-point match can snap to the
            // WRONG leg for a frame and report a wildly wrong lateralDistance - which
            // is exactly the phantom 20-27m "swings" logged at norm ~0.00-0.03 with
            // every steering input dead calm (steer=3, lineBias ~5m). Reject any
            // sample whose matched centreline point is far from the car in elevation:
            // that |dy| proves the match jumped to the crossing leg, and folding its
            // bogus lateral into the min/max is what produced the impossible swing.
            float dyToLine = 0f;
            {
                Vector3 lp, lf, lr;
                track.SampleAtDistance(progress.distance, out lp, out lf, out lr);
                dyToLine = transform.position.y - lp.y;
            }
            if (Mathf.Abs(dyToLine) > 2f)
            {
                return;
            }

            float lat = progress.lateralDistance;

            // Teleport guard: a real car at racing speed physically cannot jump
            // several metres sideways between two frames. When the matched
            // centreline snaps to the wrong leg near the start/finish crossing
            // (the two legs sit at equal elevation there, so the |dy| guard above
            // does NOT catch it), lateralDistance leaps 10-20m for a single frame
            // and folding that into the min/max fabricates the impossible 20-25m
            // "swings" logged at norm ~0.00-0.05 and ~0.90-1.00 while every command
            // is calm. Reject any frame whose lateral jumped more than a car could
            // really move in one step; the window keeps its clean samples instead
            // of being poisoned by the one bad match.
            if (swerveAmpHasPrevLat && Mathf.Abs(lat - swerveAmpPrevLat) > 2.5f)
            {
                return;
            }

            swerveAmpPrevLat = lat;
            swerveAmpHasPrevLat = true;

            float drawn = hasLastDrawnOffset ? lastDrawnOffset : 0f;
            if (!swerveAmpHasSample)
            {
                swerveAmpHasSample = true;
                swerveAmpWindowStart = Time.time;
                swerveAmpLatMin = swerveAmpLatMax = lat;
                swerveAmpDrawnMin = swerveAmpDrawnMax = drawn;
                swerveAmpLineMin = swerveAmpLineMax = lineBias;
                swerveAmpAggMin = swerveAmpAggMax = aggressionOffset;
                return;
            }

            swerveAmpLatMin = Mathf.Min(swerveAmpLatMin, lat);
            swerveAmpLatMax = Mathf.Max(swerveAmpLatMax, lat);
            swerveAmpDrawnMin = Mathf.Min(swerveAmpDrawnMin, drawn);
            swerveAmpDrawnMax = Mathf.Max(swerveAmpDrawnMax, drawn);
            swerveAmpLineMin = Mathf.Min(swerveAmpLineMin, lineBias);
            swerveAmpLineMax = Mathf.Max(swerveAmpLineMax, lineBias);
            swerveAmpAggMin = Mathf.Min(swerveAmpAggMin, aggressionOffset);
            swerveAmpAggMax = Mathf.Max(swerveAmpAggMax, aggressionOffset);

            if (Time.time - swerveAmpWindowStart < 4f)
            {
                return;
            }

            float latSwing = swerveAmpLatMax - swerveAmpLatMin;
            float drawnSwing = swerveAmpDrawnMax - swerveAmpDrawnMin;
            float lineSwing = swerveAmpLineMax - swerveAmpLineMin;
            float aggSwing = swerveAmpAggMax - swerveAmpAggMin;

            // Re-arm the window regardless of whether we log.
            swerveAmpHasSample = false;
            swerveAmpWindowStart = Time.time;

            // Bar raised 5 -> 9m: the racing line legitimately crosses the road
            // once per straight (capped gates put that at up to ~8m of drift
            // over a long window), and a pass is legitimately a ~6-8m lane
            // change - both were being logged as "swung across a straight",
            // which made every clean lap read as a swerve report. 9m+ within
            // one 4s window means genuinely anomalous lateral travel.
            if (latSwing < 9f || Time.time - swerveAmpLastLog < 4f)
            {
                return;
            }
            swerveAmpLastLog = Time.time;

            string who = participant != null && participant.driverData != null
                ? participant.driverData.displayName : "AI";
            // Whichever COMMAND swing most closely accounts for the car's swing.
            string verdict;
            float biggest = Mathf.Max(drawnSwing, Mathf.Max(lineSwing, aggSwing));

            // Reference-frame artifact guard. progress.lateralDistance is measured
            // against the racing-line reference, which itself curves; on some
            // sections the matched centreline sweeps several metres even as the car
            // tracks dead straight, so lateralDistance reports a large "swing" that
            // no steering input produced (the tell: latSwing of 12-38m while every
            // command swing - drawnLine/lineBias/overtakeOff - reads ~0). A real car
            // at these steer amplitudes physically cannot translate this far sideways
            // in 4s. When the biggest command swing can't account for at least half
            // the measured lateral swing, it's the measurement lying, not the car
            // weaving - stay silent instead of crying wolf.
            if (latSwing > 6f && biggest < latSwing * 0.5f)
            {
                return;
            }
            if (biggest < 2f)
            {
                verdict = "STEERING CONTROLLER (no target moved - pursuit/slew/yaw limit cycle)";
            }
            else if (aggSwing >= drawnSwing && aggSwing >= lineSwing)
            {
                verdict = "OVERTAKE OFFSET (pass state machine)";
            }
            else if (drawnSwing >= lineSwing * 0.75f)
            {
                verdict = "RACING LINE (drawn line runs edge-to-edge on this straight)";
            }
            else
            {
                verdict = "LINE BLEND/SLEW (target amplified vs the drawn line)";
            }

            Debug.LogWarning("[SwerveDiag amplitude] " + who + " swung " + latSwing.ToString("0.0") +
                "m across a straight over 4s (norm " + progress.normalized.ToString("0.00") +
                ") | swings: drawnLine=" + drawnSwing.ToString("0.0") +
                "m lineBias=" + lineSwing.ToString("0.0") +
                "m overtakeOff=" + aggSwing.ToString("0.0") +
                "m | lineBias now=" + lineBias.ToString("0.0") +
                " trafficBlend=" + lineTrafficBlend.ToString("0.00") +
                " state=" + overtakeState +
                " => CAUSE: " + verdict);
            DumpSwerveTape(who, "lateral-swing(" + latSwing.ToString("0.0") + "m/4s)", progress.normalized);
        }

        // [SwerveTape] dump: the full recorded steering pipeline for the last
        // few seconds, one row per ~0.15s, plus a measured verdict. The
        // attribution rule is mechanical: at every frame where the FINAL steer
        // flips sign (past a 0.08 deadband), the term whose own change that
        // frame was largest gets the blame point. Whichever term collects the
        // points is, by construction, the thing physically moving the wheel -
        // no interpretation required. Also reports: how often the authority
        // compensation and the final command sat saturated, how much each
        // TARGET (drawn racing line, blended line bias, overtake offset)
        // actually moved, and how many progress-reference snaps poisoned the
        // window.
        // Public trigger for external systems (RaceManager's [WallDiag] wall-hit
        // handler): dump the pipeline history that led INTO the event.
        public void DumpSwerveTape(string reason, float norm)
        {
            string who = participant != null && participant.driverData != null
                ? participant.driverData.displayName : "AI";
            // A wall hit always dumps (short anti-spam floor only) - it is the
            // event the whole recorder exists for.
            DumpSwerveTape(who, reason, norm, 3f);
        }

        void DumpSwerveTape(string who, string reason, float norm)
        {
            DumpSwerveTape(who, reason, norm, 20f);
        }

        void DumpSwerveTape(string who, string reason, float norm, float cooldownSeconds)
        {
            if (Time.time - swerveTapeLastDump < cooldownSeconds)
            {
                return;
            }

            int available = Mathf.Min(swerveTapeCount, swerveTape.Length);
            if (available < 40)
            {
                return;
            }
            swerveTapeLastDump = Time.time;

            int start = swerveTapeCount - available;
            SwerveTapeFrame First(int k) { return swerveTape[(start + k) % swerveTape.Length]; }

            // --- verdict statistics over the window ---
            int blamePursuit = 0, blameEdge = 0, blameYaw = 0, blameTraffic = 0, blameLine = 0;
            int finalSign = 0, reversals = 0, snaps = 0, compSat = 0, steerSat = 0;
            float sumPur = 0f, sumEdge = 0f, sumYaw = 0f, sumTraffic = 0f;
            float drawnMin = float.MaxValue, drawnMax = float.MinValue;
            float biasMin = float.MaxValue, biasMax = float.MinValue;
            float aggMin = float.MaxValue, aggMax = float.MinValue;
            float latMin = float.MaxValue, latMax = float.MinValue;
            for (int k = 0; k < available; k++)
            {
                SwerveTapeFrame f = First(k);
                float pursuitEff = Mathf.Clamp(f.pursuit * f.comp, -1f, 1f);
                sumPur += Mathf.Abs(pursuitEff);
                sumEdge += Mathf.Abs(f.edge);
                sumYaw += Mathf.Abs(f.yawDamp);
                sumTraffic += Mathf.Abs(f.traffic);
                if (f.refSnap) snaps++;
                if (f.comp >= 3.95f) compSat++;
                if (Mathf.Abs(f.final) > 0.98f) steerSat++;
                drawnMin = Mathf.Min(drawnMin, f.drawn); drawnMax = Mathf.Max(drawnMax, f.drawn);
                biasMin = Mathf.Min(biasMin, f.lineBias); biasMax = Mathf.Max(biasMax, f.lineBias);
                aggMin = Mathf.Min(aggMin, f.agg); aggMax = Mathf.Max(aggMax, f.agg);
                // Reference snaps teleport the measured lateral - exclude them from
                // the car-swing measure exactly like the amplitude detector does.
                if (!f.refSnap)
                {
                    latMin = Mathf.Min(latMin, f.lat); latMax = Mathf.Max(latMax, f.lat);
                }

                int sign = Mathf.Abs(f.final) > 0.08f ? (f.final > 0f ? 1 : -1) : 0;
                if (sign != 0 && finalSign != 0 && sign != finalSign && k > 0)
                {
                    reversals++;
                    SwerveTapeFrame p = First(k - 1);
                    float dPursuit = Mathf.Abs(pursuitEff - Mathf.Clamp(p.pursuit * p.comp, -1f, 1f));
                    float dEdge = Mathf.Abs(f.edge - p.edge);
                    float dYaw = Mathf.Abs(f.yawDamp - p.yawDamp);
                    float dTraffic = Mathf.Abs(f.traffic - p.traffic);
                    // Target motion expressed in approximate steer units so it can
                    // compete for blame on the same scale (a 1m target move at the
                    // ~28m lookahead is roughly 0.08 of steer via the 2.2 gain).
                    float dLine = Mathf.Abs((f.drawn + f.lineBias) - (p.drawn + p.lineBias)) * 0.08f;
                    float best = Mathf.Max(dPursuit, Mathf.Max(dEdge, Mathf.Max(dYaw, Mathf.Max(dTraffic, dLine))));
                    if (best <= 0.0001f || best == dPursuit) blamePursuit++;
                    else if (best == dEdge) blameEdge++;
                    else if (best == dYaw) blameYaw++;
                    else if (best == dTraffic) blameTraffic++;
                    else blameLine++;
                }
                if (sign != 0)
                {
                    finalSign = sign;
                }
            }

            float span = First(available - 1).time - First(0).time;
            float inv = available > 0 ? 1f / available : 0f;
            var sb = new System.Text.StringBuilder(4096);
            sb.Append("[SwerveTape] ").Append(who).Append(" | trigger=").Append(reason)
              .Append(" norm=").Append(norm.ToString("0.000"))
              .Append(" | ").Append(available).Append(" frames / ").Append(span.ToString("0.0")).Append("s\n");
            sb.Append("VERDICT wheel-mover at each of ").Append(reversals).Append(" final-steer reversals: ")
              .Append("pursuitXcomp=").Append(blamePursuit)
              .Append(" edgeRecovery=").Append(blameEdge)
              .Append(" yawDamp=").Append(blameYaw)
              .Append(" trafficNudge=").Append(blameTraffic)
              .Append(" lineTarget=").Append(blameLine).Append('\n');
            sb.Append("mean|term|: purXcomp=").Append((sumPur * inv).ToString("0.00"))
              .Append(" edge=").Append((sumEdge * inv).ToString("0.00"))
              .Append(" yawDamp=").Append((sumYaw * inv).ToString("0.00"))
              .Append(" traffic=").Append((sumTraffic * inv).ToString("0.00"))
              .Append(" | comp@max ").Append(Mathf.RoundToInt(100f * compSat * inv)).Append("%")
              .Append(" |steer|>0.98 ").Append(Mathf.RoundToInt(100f * steerSat * inv)).Append("%")
              .Append(" refSnaps=").Append(snaps).Append('\n');
            sb.Append("targets p2p: drawnLine=").Append((drawnMax - drawnMin).ToString("0.0"))
              .Append("m lineBias=").Append((biasMax - biasMin).ToString("0.0"))
              .Append("m overtakeOff=").Append((aggMax - aggMin).ToString("0.0"))
              .Append("m carLat=").Append(latMax > latMin ? (latMax - latMin).ToString("0.0") : "n/a").Append("m\n");
            sb.Append("t+s | kph | lat/halfW | sev | drawn bias agg | pur comp edge yawD traf | FIN | urgT urgP rate | yaw\n");

            int rows = 26;
            int stride = Mathf.Max(1, available / rows);
            float t0 = First(0).time;
            for (int k = 0; k < available; k += stride)
            {
                SwerveTapeFrame f = First(k);
                sb.Append((f.time - t0).ToString("0.00")).Append(" | ")
                  .Append(f.speedKph.ToString("0")).Append(" | ")
                  .Append(f.lat.ToString("0.0")).Append('/').Append(f.halfW.ToString("0.0")).Append(" | ")
                  .Append(f.sev.ToString("0.00")).Append(" | ")
                  .Append(f.drawn.ToString("0.0")).Append(' ')
                  .Append(f.lineBias.ToString("0.0")).Append(' ')
                  .Append(f.agg.ToString("0.0")).Append(" | ")
                  .Append(f.pursuit.ToString("0.00")).Append(' ')
                  .Append(f.comp.ToString("0.0")).Append(' ')
                  .Append(f.edge.ToString("0.00")).Append(' ')
                  .Append(f.yawDamp.ToString("0.00")).Append(' ')
                  .Append(f.traffic.ToString("0.00")).Append(" | ")
                  .Append(f.final.ToString("0.00")).Append(" | ")
                  .Append(f.urgT.ToString("0.00")).Append(' ')
                  .Append(f.urgP.ToString("0.00")).Append(' ')
                  .Append(f.towardRate.ToString("0.0")).Append(" | ")
                  .Append(f.yawMeas.ToString("0"));
                if (f.refSnap)
                {
                    sb.Append(" SNAP");
                }
                sb.Append('\n');
            }

            Debug.LogWarning(sb.ToString());
        }

        // Returns 1 when the signal, once it exceeds the deadband, flips sign
        // versus the last above-deadband sample - i.e. one genuine reversal.
        static int CountReversal(float value, float deadband, ref int prevSign)
        {
            int sign = Mathf.Abs(value) > deadband ? (value > 0f ? 1 : -1) : 0;
            if (sign == 0)
            {
                return 0;
            }

            int reversal = (prevSign != 0 && sign != prevSign) ? 1 : 0;
            prevSign = sign;
            return reversal;
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

                // Barrier-crash fix (Italy hairpin / turn 3-4 chicane): this used
                // to break the instant severity dipped just 0.12 below the peak
                // found so far - fine for a single isolated corner, but for a
                // closely-spaced pair (a chicane, or a hairpin following another
                // corner shortly after, e.g. Monza's Roggia/Ascari chicanes and
                // the run into Parabolica) the road only straightens PARTIALLY
                // between the two apexes. A relative dip of 0.12 is easily
                // crossed by that partial straighten, so the scan stopped and
                // completely missed the SECOND apex - the braking-point model
                // then had zero awareness of it until the car was already on top
                // of it next frame, with drastically less braking distance left:
                // exactly the "carrying too much speed into the corner and
                // hitting the barrier" pattern reported. Now only breaks once the
                // road has genuinely opened up (an ABSOLUTE low severity, not a
                // relative dip from the peak) - a real straight after a single
                // corner still breaks early as before, but a chicane/back-to-back
                // corner pair keeps scanning until it finds the (possibly
                // tighter) second apex.
                // Round 2 (per request - "mostly fixed but could use
                // improvement"): threshold tightened further (0.2 -> 0.12) so
                // the scan keeps going through an even milder partial
                // straighten between two corners before concluding the road
                // has genuinely opened up.
                if (apexSeverity > 0.55f && severity < 0.12f)
                {
                    break;
                }
            }
        }

        void UpdateMistake(int consistency, int aggression, RaceManager.AiDifficultyProfile profile)
        {
            mistakeBrakeErrorTimer = Mathf.Max(0f, mistakeBrakeErrorTimer - Time.deltaTime);
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
                // Twitch magnitude cut 0.9 -> 0.6m (per report - mistakes too
                // common/violent, and a 0.9m instant lateral jerk reads as a
                // swerve). A mistake is now a believable wobble/correction, not a
                // near lane-change; the real time-loss consequence is the
                // braking-point error below, not the size of this steer blip.
                // Twitch cut again 0.6 -> 0.4 (per request, alongside halved
                // mistake rates): with barriers now flush to the track edge a
                // mistake twitch must never be big enough to carry a car into
                // the wall on its own.
                mistakeSteer = Random.Range(-0.4f, 0.4f);
                mistakeTimer = Random.Range(0.5f, 1.2f);

                // Consequence pass: a steer wobble alone is clamped back inside
                // the legal-offset corridor by ConstrainLegalLineOffset, so the
                // worst "mistake" the AI could previously make was a sub-metre
                // twitch with zero time loss - the player was the only driver on
                // track who could actually throw a race away, and Easy's 35x
                // higher mistake rate was invisible. A share of mistakes are now
                // a genuinely misjudged braking point for the upcoming corner:
                // the entry target is inflated for a couple of seconds, the car
                // arrives hot, the edge emergency brake has to gather it up and
                // the driver runs deep/wide and loses real time. Share and
                // severity both scale with the tier's mistake quality axis, so
                // sharper fields make rarer AND smaller braking errors.
                float mistakeQuality = Mathf.Clamp01(1f - profile.mistakeChancePerLap / 0.16f);
                if (Random.value < Mathf.Lerp(0.45f, 0.15f, mistakeQuality))
                {
                    mistakeBrakeErrorTimer = Random.Range(1.5f, 3f);
                    mistakeBrakeErrorSeverity = Mathf.Lerp(Random.Range(0.2f, 0.35f), Random.Range(0.08f, 0.16f), mistakeQuality);
                }
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
            // Swarming fix (per request - "everyone rushing to get onto the
            // line" right after the start): this state machine drove every AI's
            // overtake/defend jockeying, and it was NEVER suppressed during the
            // opening fan-out window - so from lights-out, cars were both trying
            // to hold their fan-out lane AND simultaneously fighting for
            // overtaking/defending position against their immediate neighbours,
            // reading as a chaotic scrum. Held off entirely for the same opening
            // window the fan-out lane hold already uses, decaying any in-progress
            // move cleanly rather than snapping it off.
            if (raceManager.CurrentSession != RaceWeekendSession.Qualifying && raceManager.RaceElapsed < OpeningFanDuration)
            {
                aggressionOffset = Mathf.MoveTowards(aggressionOffset, 0f, Time.deltaTime * 4f);
                overtakeState = OvertakeState.Following;
                return;
            }

            // Search range 46 -> 150m (per report - "on a straight where im
            // just a bit ahead theyd much rather stick to their line than pull
            // in behind me... the slipstream fix doesnt work"): the tow-seek
            // and every attack qualifier in this state machine can only react
            // to a car this search actually RETURNS - and 46m is 0.55s at
            // 300kph, while the physics tow (RaceManager.Slipstream's
            // SlipstreamMaxDistance) reaches 150m. From any realistic
            // following distance the leader was simply invisible to this
            // machine, so the follower held its own line straight through the
            // wake. The range now matches the slipstream's own reach; nothing
            // fires earlier than its own time-gap gates allow (attack triggers
            // still require gapSeconds under their 2.6-3.4s thresholds, the
            // tow-seek under 1.8s), so this widens PERCEPTION, not aggression.
            RaceParticipant ahead = raceManager.FindCarAhead(participant, 150f);
            RaceParticipant behind = raceManager.FindCarBehind(participant, 32f);
            float legalLimit = LegalOffsetLimit(severityHere, progress.distance);
            // Racecraft sharpness: lean the tier/stat commitment TOWARD full
            // rather than multiplying it there. The old x2.6 pre-clamp buff
            // saturated every tier at 1.0 (Easy 0.35 x ~1.09 x 2.6 ~= 0.99), so
            // Easy lunged and covered the line exactly like Expert and the
            // overtakeCommitment/defendCommitment difficulty columns were dead
            // knobs. A 30% lean keeps the ordering and the driver-stat spread:
            // Easy lands ~0.55, Medium ~0.7, Hard ~0.9, Expert ~1.0.
            const float RacecraftLean = 0.3f;
            float commitment = Mathf.Lerp(Mathf.Clamp01(profile.overtakeCommitment * Mathf.Lerp(0.7f, 1.15f, (aggression + overtaking) / 200f)), 1f, RacecraftLean);
            float defendCommitment = Mathf.Lerp(Mathf.Clamp01(profile.defendCommitment * Mathf.Lerp(0.7f, 1.15f, defending / 100f)), 1f, RacecraftLean);

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
            bool pitZoneNearby = track.IsInPitApproach(progress.normalized) || track.IsInPitExitLimiterZone(progress.normalized) ||
                                  participant.pitExitLaneHoldTimer > 0f || participant.pitExitLaneHoldDistanceRemaining > 0f;
            bool eitherUnderPitLimiter = vehicle.PitLimiterActive || (ahead != null && ahead.vehicle != null && ahead.vehicle.PitLimiterActive);
            // Blue flag: a car being lapped must not fight the traffic lapping
            // it (FlagRules.MustYield) - no new attacks while the flag is shown.
            // Launch discipline (start-massacre fix - 18 of 21 AI out inside
            // 15s): attack maneuvers had NO launch gating, so cars threw
            // full-commitment 6m lateral swings from second zero at maximum
            // grid density, spinning each other into the now-flush walls.
            // Real drivers hold their lane and survive the opening scrum; no
            // new attacks until the field has had a chance to string out.
            bool launchScrum = raceManager.RaceElapsed < 12f && raceManager.CurrentSession != RaceWeekendSession.Qualifying;
            bool suppressAttackManeuvers = pitZoneNearby || eitherUnderPitLimiter || launchScrum || raceManager.IsShownBlueFlag(participant);
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

            // Anti-thrash bookkeeping: how long we've held the current state, and
            // the post-pass re-attack cooldown ticking down. Both consumed by the
            // transition guards below.
            OvertakeState overtakeStateAtStart = overtakeState;
            float overtakeStateAge = Time.time - overtakeStateEnteredTime;
            overtakeReattackCooldown = Mathf.Max(0f, overtakeReattackCooldown - Time.deltaTime);

            switch (overtakeState)
            {
                case OvertakeState.Following:
                {
                    // No more follower peek (per report - "that slight swerving
                    // movement... they'll never have a confident overtake if
                    // they keep on swerving like that and slowing down"): the
                    // old peek pulled the car a couple of metres toward its
                    // preferred side the moment it was within 2.5s - a constant
                    // small swerve that scrubbed exactly the overspeed the
                    // decisive-commit gate needs to see before it launches a
                    // real attack (and threw away the slipstream on straights).
                    // The one and only ATTACK move is the full committed swing
                    // when an attack state actually fires.
                    //
                    // Tow-seeking (per report - "on a straight where im just a
                    // bit ahead theyd much rather stick to their line than pull
                    // in behind me"): holding-their-own-line went too far the
                    // other way - a follower whose line is laterally offset
                    // from the leader's never entered the wake at all, so the
                    // slipstream they're supposed to exploit never activated.
                    // On a straight, within tow range, the follower now steers
                    // to ALIGN with the car ahead (chasing the leader's lateral
                    // position, not a peek to one side) - one purposeful move
                    // into the tow, with a dead-band so it never micro-swerves
                    // once roughly aligned. Corners keep the line-hold.
                    float followTarget = 0f;
                    if (ahead != null && ahead.vehicle != null && raceManager.CanParticipantOvertake(participant, ahead))
                    {
                        float towGapSeconds = raceManager.GetIntervalToAheadSeconds(participant);
                        bool towStraight = severityHere < 0.14f && (apexDistanceAhead > 70f || apexSeverity < 0.14f);
                        if (towStraight && towGapSeconds < 1.8f)
                        {
                            Vector3 aheadLocalTow = transform.InverseTransformPoint(ahead.transform.position);
                            if (Mathf.Abs(aheadLocalTow.z) > 6f && Mathf.Abs(aheadLocalTow.x) > 0.6f)
                            {
                                followTarget = aggressionOffset + aheadLocalTow.x;
                            }
                            else
                            {
                                // Roughly aligned (or genuinely alongside):
                                // hold what we have - no dithering in the wake.
                                followTarget = aggressionOffset;
                            }
                        }
                    }

                    // A follower only ever nudges a couple of metres into the tow -
                    // it never chases a leader that is itself running wide across
                    // the full track half-width. Without this, a leader on a wide
                    // racing line drags the whole queue behind it edge-to-edge
                    // (the reported agg=+-9m "weave" while merely Following).
                    const float FollowTowLimit = 2.5f;
                    followTarget = Mathf.Clamp(followTarget, -FollowTowLimit, FollowTowLimit);
                    // Tow-chase rate cut 5 -> 1.5 m/s ([SwerveDiag]: with the cap in,
                    // Following agg pinned at exactly +-2.50 and FLIPPED SIGN as the
                    // leader drifted across centre - the follower mirrored every
                    // leader movement as its own 5m swerve, propagating one car's
                    // wiggle down the whole train). Entering the tow is a patient
                    // positioning drift, not a dodge; at 1.5 m/s a full cap-to-cap
                    // flip takes ~3.3s and reads as a smooth lean, and a leader
                    // wiggle faster than that simply gets averaged out.
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, followTarget, Time.deltaTime * 1.5f);
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

                        // THE "they just brake forever" fix: every qualifier below
                        // except this one needs either a corner, DRS, or a LIVE
                        // measured speed advantage - but by the time a genuinely
                        // faster car is close enough to attack, its own traffic-
                        // avoidance braking has already equalised the two cars'
                        // speeds, so clearlySlower reads false and on a plain
                        // straight the attack never qualifies at all. The car
                        // orbits in the leader's wake, braking every time it
                        // closes, for the whole stint. Sitting inside 1.8s for
                        // several seconds IS proof of pace (you cannot hold that
                        // gap without it), so sustained pressure becomes its own
                        // attack qualifier.
                        // Tuned WAY up (per request): near-instant - being in
                        // the wake at all is the trigger.
                        bool sustainedPressure = followingTimer > 0.4f;

                        // DRS conversion: a real advantage should turn into real
                        // attacks, scaled by commitment so Expert converts a tow far
                        // more often than a tentative Easy/Medium driver does.
                        float drsBonus = drsHelp ? Mathf.Lerp(1.2f, 2.6f, commitment) : 1f;

                        // Part A.3: Expert's attack-trigger gap threshold is far wider
                        // than the other tiers, which keep the original threshold
                        // untouched. Slight nerf pass: trimmed back a bit from
                        // 1.8s/3.0s so Expert doesn't launch attacks from quite as
                        // far back as before.
                        // Buffed for non-Expert too (was a flat 1.1s regardless of
                        // difficulty, while commitment above is now saturated at
                        // 1.0 for most tiers - the gap threshold, not commitment,
                        // was the real ceiling on how often Easy/Medium/Hard even
                        // considered an attack).
                        float attackGapThreshold = isExpert ? (aheadIsBackmarker ? 3.4f : 2.8f) : 2.6f;
                        bool attackTrigger = gapSeconds < attackGapThreshold && (approachingBrakeZone || drsHelp || clearlySlower || positiveSpeedDeltaExpert || sustainedPressure) && hasPace;

                        // Narrow-section discretion (per request - "if the cars
                        // are on an obvious very narrow part of the track where
                        // overtaking's difficult... if its obvious its unlikely
                        // the move's gonna get done id rather you just not do it
                        // instead of doing it and sending it into the wall"):
                        // a pass needs two car widths plus real margin - when
                        // the road here OR through the next ~70m is pinched
                        // below that, the percentage move is to wait for the
                        // wider stretch. Most drivers now simply don't launch
                        // there. The boldest (combined aggression+overtaking in
                        // the top band - "some good ai are gonna take risks
                        // regardless") may still send one, at a heavily damped
                        // rate, and even Expert loses its automatic
                        // deterministic launch in a pinch - risk is a roll for
                        // everyone when the walls are this close.
                        float narrowHalfWidth = Mathf.Min(LocalHalfWidthAt(progress.distance), LocalHalfWidthAt(progress.distance + 70f));
                        bool narrowSection = narrowHalfWidth < 9.5f;
                        bool attackPermitted;
                        if (narrowSection)
                        {
                            bool boldEnough = (aggression + overtaking) >= 170;
                            attackPermitted = boldEnough && Random.value < commitment * Time.deltaTime * (20f + patienceBonus) * drsBonus * 0.25f;
                        }
                        else
                        {
                            // Part A.2: Expert is fully deterministic once
                            // attackTrigger is true - no dice roll for
                            // permission to attack (open track only).
                            attackPermitted = isExpert || Random.value < commitment * Time.deltaTime * (20f + patienceBonus) * drsBonus;
                        }

                        if (attackTrigger && !suppressAttackManeuvers && !inCornerCommitmentZone && attackPermitted && overtakeReattackCooldown <= 0f)
                        {
                            // Versatile side choice (per request - the AI always
                            // took the same side): reads the corner, the
                            // defender's actual road position and the driver's
                            // craft instead of defaulting to the side the car
                            // happened to lean toward.
                            attackSide = ChooseAttackSide(ahead, apexSeverity, apexDistanceAhead, turnSign, overtaking);
                            followingTimer = 0f;

                            // Decisive commitment (per request - "if they want a
                            // move done they should just go and do it instead of
                            // swerving a tiny bit slowing themselves down"): the
                            // PreparingAttack feint exists for probing moves
                            // where the attacker is still manufacturing an
                            // advantage. With a REAL advantage already in hand -
                            // genuine overspeed, a DRS/slipstream run, or
                            // already overlapped alongside the defender - the
                            // half-width shimmy is pure dithering that bleeds
                            // the very speed delta the move depends on, so those
                            // launch straight into the full attack. Skill still
                            // decides what counts as "real": a low-commitment
                            // driver needs roughly double the overspeed before
                            // it feels decisive, and the tow/DRS routes lean on
                            // stats the same way everything else here does.
                            Vector3 aheadLocalNow = transform.InverseTransformPoint(ahead.transform.position);
                            bool overlappedAlongside = Mathf.Abs(aheadLocalNow.z) < 8f && Mathf.Abs(aheadLocalNow.x) > 1.1f;
                            bool decisiveAdvantage = speedDeltaKph > Mathf.Lerp(7f, 3f, commitment) ||
                                                     (drsHelp && speedDeltaKph > 1f) ||
                                                     (vehicle.SlipstreamActive && speedDeltaKph > 2f) ||
                                                     overlappedAlongside;
                            if (decisiveAdvantage)
                            {
                                overtakeTarget = ahead;
                                overtakeState = attackSide < 0f ? OvertakeState.AttackingOutside : OvertakeState.AttackingInside;
                                overtakeStateTimer = attackingTimer;
                            }
                            else
                            {
                                overtakeState = OvertakeState.PreparingAttack;
                                overtakeStateTimer = preparingAttackTimer;
                            }
                        }
                    }
                    else
                    {
                        followingTimer = 0f;
                    }
                    break;
                }

                case OvertakeState.PreparingAttack:
                {
                    // attackSide is chosen once on entry above and only ever read from
                    // here on, so it cannot flip mid-attempt.
                    // No prep shimmy (same swerving report as the follower peek
                    // above): the old half-width feint toward attackSide was
                    // the visible "questioning whether to make the move"
                    // wobble - it bled the speed advantage during exactly the
                    // seconds the car was deciding, so probes kept arriving at
                    // the commit roll with nothing in hand. PreparingAttack now
                    // holds the line dead-straight (staying in the tow, keeping
                    // its overspeed building); the FIRST lateral movement is
                    // the full committed swing of AttackingInside/Outside. The
                    // side was already chosen on entry, so nothing is lost by
                    // not telegraphing it.
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, 0f, Time.deltaTime * 5f);
                    bool stillThere = ahead != null && raceManager.GetIntervalToAheadSeconds(participant) < 1.4f;

                    // Decisive commitment, prep half (per request - no swerving
                    // around wondering whether to make the move): if a genuine
                    // advantage MATERIALISES during the feint - the tow kicks
                    // in, DRS opens, or real overspeed builds - the car commits
                    // to the attack right now instead of riding the prep timer
                    // out and then rolling dice on whether it dares. The bail
                    // roll stays only for the no-advantage case, which keeps
                    // the skill spread: low-commitment drivers still talk
                    // themselves out of speculative moves, but nobody talks
                    // themselves out of an advantage they already hold.
                    bool liveAdvantage = false;
                    if (stillThere && ahead.vehicle != null)
                    {
                        float prepSpeedDeltaKph = Mathf.Abs(vehicle.CurrentSpeedKph) - Mathf.Abs(ahead.vehicle.CurrentSpeedKph);
                        liveAdvantage = prepSpeedDeltaKph > 2f || vehicle.SlipstreamActive || raceManager.IsDrsAvailable(participant);
                    }

                    if (liveAdvantage && !inCornerCommitmentZone)
                    {
                        overtakeTarget = ahead;
                        overtakeState = attackSide < 0f ? OvertakeState.AttackingOutside : OvertakeState.AttackingInside;
                        overtakeStateTimer = attackingTimer;
                    }
                    else if (!stillThere || overtakeStateTimer <= 0f)
                    {
                        // Part A.2: Expert commits to the attack whenever the target is
                        // still there, no roll.
                        if (stillThere && (isExpert || Random.value < commitment))
                        {
                            overtakeTarget = ahead;
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
                    // Overtake lunge capped to a pass width, not the full half-width
                    // ([SwerveDiag amplitude]: with the line and traffic-hold calmed,
                    // the dominant remaining swing was OVERTAKE OFFSET pinning to
                    // +/-legalLimit ~= 9.9m - the car lunging the ENTIRE half-width to
                    // the track edge to pass, and two cars doing that in opposite
                    // directions read as violent swerving and made contact). A pass
                    // only needs to put the car alongside; ~6.5m clears the racing
                    // line and gets the nose past without riding the wall. Well wider
                    // than the +/-5.5m holding lane, so overtaking authority is kept.
                    float overtakeReach = Mathf.Min(legalLimit, 6.5f);
                    float attackOffset = attackSide * Mathf.Lerp(2f, overtakeReach, commitment);
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
                    // Minimum dwell: hold the committed attack line for at least
                    // OvertakeMinStateDwell before registering any hand-off, so a
                    // single frame of the target flickering level (|z|<6) or out
                    // of FindCarAhead's reach can't ping-pong the state.
                    if (overtakeStateAge < OvertakeMinStateDwell)
                    {
                        // hold - keep pressing the current attack line.
                    }
                    else if (sideBySideNow)
                    {
                        overtakeState = OvertakeState.SideBySide;
                        overtakeStateTimer = sideBySideTimer;
                    }
                    else if (ahead == null && OvertakeTargetIsBehind())
                    {
                        overtakeState = OvertakeState.CompletingPass;
                        overtakeStateTimer = 1.4f;
                        overtakeReattackCooldown = OvertakeReattackCooldownSeconds;
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
                    // Same minimum dwell before a side-by-side can be declared
                    // "completed": FindCarAhead(12) flickering null while the cars
                    // are level is exactly what re-triggered the pass loop.
                    if (overtakeStateAge < OvertakeMinStateDwell)
                    {
                        // hold alongside.
                    }
                    else if ((ahead == null || raceManager.FindCarAhead(participant, 12f) == null) &&
                             OvertakeTargetIsBehind())
                    {
                        overtakeState = OvertakeState.CompletingPass;
                        overtakeStateTimer = 1.2f;
                        overtakeReattackCooldown = OvertakeReattackCooldownSeconds;
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
                        // THE ATTACK-CHURN FIX ([SwerveDiag]: overtakeStateChanges up
                        // to 6/2s with agg slamming +-6.3m - the dominant remaining
                        // on-screen weave). The re-attack cooldown was only ever set
                        // on a SUCCESSFUL pass (-> CompletingPass); an ABORTED attack
                        // paid nothing and re-launched the very next frame, so in a
                        // dense pack where passes rarely stick every car looped
                        // attack -> back-out -> attack forever, each cycle a full 6m
                        // lateral swing and back. An abort now costs a longer
                        // cooldown than a success: the situation that just failed
                        // won't be different 0.2s later, so sit in the tow and wait
                        // for a genuinely new opportunity instead of sawing at it.
                        overtakeReattackCooldown = OvertakeReattackCooldownSeconds * 2f;
                    }
                    break;
            }

            // Defend once per approaching braking zone: cover the inside line if a
            // real threat is close behind, then leave it alone until the next corner
            // instead of weaving repeatedly. Part A.4: Expert covers from much
            // further out than the other tiers - earlier inside-cover before the
            // braking zone - while still only ever committing once per zone.
            // Buffed for non-Expert (was 70m) alongside the attack-gap widening
            // above - defendCommitment saturates for most tiers now too, so the
            // detection distance was the real ceiling on how often Easy/Medium/
            // Hard even attempted a defensive cover.
            float approachTriggerDistance = isExpert ? 95f : 82f;
            bool approaching = apexDistanceAhead < approachTriggerDistance && apexSeverity > 0.16f;
            if (!approaching)
            {
                hasCoveredThisApex = false;
            }
            else if (overtakeState == OvertakeState.Following && !hasCoveredThisApex && !suppressAttackManeuvers && !inCornerCommitmentZone && behind != null && behind.vehicle != null)
            {
                float behindGap = raceManager.GetIntervalToAheadSeconds(behind);
                bool behindHasDrs = raceManager.IsDrsAvailable(behind);
                // Threat window widened (1.3 -> 1.6) alongside the rest of this pass.
                bool threatClose = behindGap > 0f && (behindGap < 1.6f || behindHasDrs);
                // Part A.2: Expert commits to the cover whenever a real threat is
                // close, no roll.
                if (threatClose && (isExpert || Random.value < defendCommitment))
                {
                    // Cover ceiling raised for non-Expert too (was 2.3).
                    float coverCeiling = isExpert ? 2.7f : 2.5f;
                    float coverOffset = turnSign * Mathf.Lerp(1f, coverCeiling, defendCommitment);
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, Mathf.Clamp(coverOffset, -legalLimit, legalLimit), Time.deltaTime * 5f);
                    hasCoveredThisApex = true;
                    pressureFactor = Mathf.Max(pressureFactor, Mathf.Lerp(0.3f, 0.8f, defendCommitment));
                }
            }

            // Dynamic defence, part 2 (per request): covering the inside once is
            // not the whole art. When an attacker actually pulls ALONGSIDE -
            // on par, slightly behind or slightly ahead - a real defender uses
            // the road: drift toward them and make them complete the move
            // around the long outside. Continuous positional pressure while
            // they're alongside, hard-capped so the attacker is always left a
            // genuine car's width of racing room (a squeeze, never a wall),
            // and never under blue flags or race-control overtaking bans.
            // Squeeze only from a defensive posture. It used to run every frame
            // regardless of state, so while the car was mid-attack (offset toward
            // a car AHEAD) this simultaneously drove the offset toward a car
            // BEHIND - a per-frame tug-of-war that swung aggressionOffset and was
            // a direct cause of the pass-time weaving ([SwerveDiag] agg=7.9 in
            // CompletingPass, where it should be easing to 0). A car actively
            // making a pass is not also squeeze-defending.
            bool defensivePosture = overtakeState == OvertakeState.Following || overtakeState == OvertakeState.BackingOut;
            if (defensivePosture && behind != null && behind.vehicle != null && !suppressAttackManeuvers &&
                !behind.isPitting && raceManager.CanParticipantOvertake(behind, participant))
            {
                Vector3 rivalLocal = transform.InverseTransformPoint(behind.transform.position);
                bool rivalAlongside = rivalLocal.z > -6.5f && rivalLocal.z < 4.5f &&
                                      Mathf.Abs(rivalLocal.x) > 0.9f && Mathf.Abs(rivalLocal.x) < 5.5f;
                if (rivalAlongside)
                {
                    float squeezeSide = Mathf.Sign(rivalLocal.x);
                    float squeezeCeiling = Mathf.Max(0f, legalLimit - 2.2f);
                    float squeezeTarget = squeezeSide * Mathf.Min(squeezeCeiling, Mathf.Lerp(1.2f, 2.4f, defendCommitment));
                    aggressionOffset = Mathf.MoveTowards(aggressionOffset, squeezeTarget, Time.deltaTime * Mathf.Lerp(1.6f, 3.2f, defendCommitment));
                    pressureFactor = Mathf.Max(pressureFactor, Mathf.Lerp(0.35f, 0.9f, defendCommitment));
                }
            }

            // Stamp the entry time whenever the state actually changed this frame,
            // so overtakeStateAge above measures the real dwell in the new state.
            if (overtakeState != overtakeStateAtStart)
            {
                overtakeStateEnteredTime = Time.time;
            }
        }

        // Attack-side selection (versatile-overtaking pass). Priority order:
        // momentum (already meaningfully offset to one side of the defender -
        // finish what was started), then corner context (inside is the classic
        // move, but a skilled overtaker reads a defender who has already
        // covered the inside and commits around the outside instead; even an
        // open inside gets the occasional surprise outside sweep), then on
        // straights simply the emptier side of the road relative to where the
        // defender actually sits.
        float ChooseAttackSide(RaceParticipant ahead, float apexSeverity, float apexDistanceAhead, float turnSign, int overtaking)
        {
            float relativeLateral = Vector3.Dot(transform.position - ahead.transform.position, transform.right);
            if (Mathf.Abs(relativeLateral) > 1.2f)
            {
                return Mathf.Sign(relativeLateral);
            }

            float defenderLateral = track.GetProgress(ahead.transform.position).lateralDistance;
            bool cornerNearby = apexSeverity > 0.25f && apexDistanceAhead < 120f;
            if (cornerNearby && Mathf.Abs(turnSign) > 0.1f)
            {
                bool insideCovered = Mathf.Abs(defenderLateral) > 0.9f && Mathf.Sign(defenderLateral) == Mathf.Sign(turnSign);
                // Boosted hard (per request - "they should go for outside asw...
                // we need a lot more of it"): a covered inside now almost always
                // means an outside sweep for a skilled overtaker, and even an
                // open inside gets a genuine around-the-outside attempt roughly
                // one time in two at the top of the stat range. Still fully
                // skill-scaled through the overtaking stat - a clumsy overtaker
                // keeps defaulting to the classic inside dive.
                float outsideChance = insideCovered
                    ? Mathf.Lerp(0.6f, 0.92f, overtaking / 100f)
                    : Mathf.Lerp(0.25f, 0.55f, overtaking / 100f);
                return Random.value < outsideChance ? -turnSign : turnSign;
            }

            if (Mathf.Abs(defenderLateral) > 0.3f)
            {
                return -Mathf.Sign(defenderLateral);
            }

            return preferredSide;
        }

        float ConstrainLegalLineOffset(TrackProgress progress, float requestedOffset, float cornerSeverity)
        {
            float legalLimit = LegalOffsetLimit(cornerSeverity, progress.distance);
            float desired = Mathf.Clamp(requestedOffset, -legalLimit, legalLimit);
            // (The old inside-limit shrink that clamped the apex to a fraction of
            // the legal width is gone - the precomputed racing line already
            // respects the kerb corridor by construction, and shrinking the inside
            // here was exactly what kept clipping the computed apex back toward
            // the centre of the road.)

            // Near-wall recovery: engages just past the corridor's outer bound
            // (corridor tops out at halfWidth - 1.2 at the widest kerb allowance),
            // so a car on the computed line is never touched but one genuinely
            // drifting toward the barrier gets pulled back while there is still a
            // full metre of room - part of the "AI crash into the barriers" fix.
            if (Mathf.Abs(progress.lateralDistance) > LocalHalfWidthAt(progress.distance) - 0.8f)
            {
                desired = Mathf.MoveTowards(desired, 0f, Mathf.Lerp(3f, 6.5f, cornerSeverity));
            }

            return desired;
        }

        float LegalOffsetLimit(float cornerSeverity, float distance)
        {
            // Aligned EXACTLY with TrackRuntime.ComputeRacingLine's corridor (see
            // the corridor-round-4 comment there): the wall-safety bound is a hard
            // ceiling combined with Mathf.Min, never Max - a Max here would let
            // this runtime clamp re-permit exactly the too-close-to-the-wall
            // offset the corridor computation was just fixed to rule out.
            // Flush-barrier recalibration (report: "cars are still swerving
            // right into the walls", most of the field DNF): these margins were
            // tuned when barriers stood 0.9m behind the paved edge - 1.8m from
            // the edge then meant ~2.7m from the actual wall face. With
            // barriers flush, 1.8m of corridor margin left ~0.8m of physical
            // clearance for a car ~2m wide with pursuit overshoot at 300kph:
            // effectively aiming AT the wall. 3.2m/3.6m restores (and slightly
            // exceeds) the real wall clearance the tuning was built around, so
            // no commandable target - line, lane, fan-out, attack or defence,
            // all of which clamp through this - can put a car near a barrier.
            float localHalfWidth = LocalHalfWidthAt(distance);
            float wallSafetyLimit = localHalfWidth - (track.IsNearTightFenceCorner(distance) ? 3.6f : 3.2f);
            float kerbBasedLimit = track.kerbStart > 0f ? track.kerbStart + 0.5f : wallSafetyLimit;
            // Absolute racing-ribbon cap (Britain flyover pinball fix - 224
            // 'Bridge concrete wall' hits in one race, the amplitude diagnostic
            // blaming RACING LINE / OVERTAKE OFFSET "runs edge-to-edge on this
            // straight"): every commandable lateral target - drawn line, held
            // lane, attack, defend, pit fan-out - clamps through this one gate,
            // and it was purely (halfWidth - margin). On a normal ~7.5m-half
            // circuit that is ~4m and nothing changes. But Britain's authored
            // width balloons to 19-22m half on the flyover approaches, so the
            // gate opened to +-16m and the AI legally scythed 30m+ across the
            // wide straight - then the road necks to the elevated deck (~7.6m
            // half) and the car, still 16m off centre, drove straight into the
            // bridge wall. A real driver never uses 40m of width; the racing
            // line lives on a normal ribbon down the middle. Cap the offset to
            // that ribbon absolutely, so an over-wide section keeps cars
            // central (arriving at the neck already lined up) while the
            // wall-safety term still binds wherever the road is genuinely
            // narrow. Min only - it can never widen the corridor past the wall.
            const float AbsoluteRacingRibbon = 8.5f;
            return Mathf.Max(0.75f, Mathf.Min(Mathf.Min(wallSafetyLimit, kerbBasedLimit), AbsoluteRacingRibbon));
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
