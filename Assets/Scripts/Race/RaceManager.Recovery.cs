using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager car-recovery subsystem (partial). Handles a car that has fallen
    /// off/under the track (HandleFallRespawn) and the escalating response to a car
    /// that keeps failing its own recovery attempts (HandleStuckEscalation, up to a
    /// last-resort force-reposition). Split out of the RaceManager monolith verbatim
    /// - same class, same members, identical thresholds, escalation order and call
    /// order; callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        // Post-reset ghost window (per request): seconds a recovered car stays
        // intangible to other cars, and how close another car may be when the
        // window expires before it gets briefly extended (never rematerialise
        // inside someone).
        const float ResetGhostSeconds = 3f;
        const float ResetGhostClearanceMeters = 3.5f;

        // Heavy-crash DNF (per request - "if a car hits the barriers HARD they
        // should just be out of the grand prix"): perpendicular wall-impact
        // speed above which a race-session car retires on the spot.
        // Start-massacre recalibration (18 of 21 AI OUT in 15s): at 350kph even
        // a ~20-degree glancing brush along a flush wall carries a 110+kph
        // perpendicular component, so the first-corner scrum mass-retired the
        // field. 150kph perpendicular means a genuine head-on/deep-angle shunt
        // - the crash a real car doesn't drive away from - while fast glancing
        // wall contact scrubs off and bounces free like it should. The launch
        // exemption is widened to cover the whole opening scrum.
        // Raised 150 -> 180 (per report - "cars being out are still a
        // problem", ~10 of 22 OUT): while the realistic-grip transition
        // settles, residual AI wall contacts remain more common than the DNF
        // rule was tuned for, and each 150+ hit permanently removed a car.
        // 180kph perpendicular is a genuinely unsurvivable head-on; anything
        // below bounces off damaged (and pays through the damage model, which
        // can still retire a repeatedly-crashing car via Collision damage).
        // Raised 180 -> 240 (per report - "way too easy to DNF"): the
        // [SwerveTape] round fixed the two measured crash drivers (the
        // traffic-nudge x compensation slam and its detection flapping), but
        // the crossing corner complex still produces glancing 180-240 hits
        // during lap-1 traffic; those now bounce off damaged instead of
        // instantly ending the race. 240+ perpendicular remains an
        // unsurvivable head-on, and the damage model still retires a
        // repeatedly-crashing car via Collision damage.
        // Raised again 240 -> 280 (second "way too easy to DNF" report - 8 of
        // 22 OUT): the understeer speed-shed reflex and the high-speed
        // cornering margin attack the crash mechanism itself, and instant
        // retirement is now reserved for a genuinely flat-out perpendicular
        // wall strike; everything below bounces off damaged and pays through
        // the damage model (which still retires repeat crashers).
        // 280 -> 320 (third "too easy to DNF" report, 13/22 OUT): instant
        // retirement is now reserved for an essentially unslowed max-speed
        // head-on. Everything else damages the car and the damage model
        // remains the only accumulating path out of the race.
        // 320 -> 380 ("STILL way too easy" round 5): with stranded cars now
        // rescued instead of retired, instant heavy-crash retirement is kept
        // only for a physically unslowed top-speed square hit - in practice
        // almost never. Mechanical/fuel remain the organic DNF sources.
        const float HardWallRetireImpactKph = 380f;
        const float HardWallRetireMinRaceSeconds = 25f;

        void UpdateResetGhost(RaceParticipant participant)
        {
            if (participant == null || participant.resetGhostTimer <= 0f)
            {
                return;
            }

            participant.resetGhostTimer -= Time.deltaTime;
            if (participant.resetGhostTimer > 0f)
            {
                return;
            }

            // Expiring: never rematerialise inside another car - hold the ghost
            // a moment longer until the overlap clears. The actual collision
            // toggle is owned by HandlePitService's SetCarToCarCollisionIgnored
            // call, which reads this timer every tick.
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant || other.retired || !other.gameObject.activeSelf)
                {
                    continue;
                }

                if ((other.transform.position - participant.transform.position).sqrMagnitude <
                    ResetGhostClearanceMeters * ResetGhostClearanceMeters)
                {
                    participant.resetGhostTimer = 0.3f;
                    return;
                }
            }

            participant.resetGhostTimer = 0f;
        }

        // Consumes VehicleController's hardest-fresh-wall-impact flag each tick
        // and retires the car when a genuinely heavy barrier crash happened in
        // a race session. Time trial and qualifying keep their crash physics
        // without ending the session; the launch seconds are excluded so grid
        // jostle can never DNF anyone.
        void HandleHeavyCrashRetirement(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null)
            {
                return;
            }

            // Wheel-to-wheel flicks stay disarmed through the launch scrum
            // (see VehicleController.ContactFlickEnabled) - constant grid-
            // density side contact plus random spins was most of the start
            // massacre on its own.
            participant.vehicle.ContactFlickEnabled = RaceElapsed > HardWallRetireMinRaceSeconds;

            // ERS caution gate round 2 (per request): recharge is legal under
            // VSC and the safety car - only a YELLOW FLAG (local yellow
            // sector / full-course yellow state) suppresses harvesting.
            participant.vehicle.ErsRechargeSuppressed =
                CurrentRaceControlState == RaceControlState.YellowSector || YellowFlagSector >= 0;

            float impactKph = participant.vehicle.PendingHardWallImpactKph;
            if (impactKph <= 0f)
            {
                return;
            }

            participant.vehicle.PendingHardWallImpactKph = 0f;

            // [WallDiag] every meaningful wall hit, retirement or not (per
            // report - cars "still going into barriers and retiring like
            // crazy"): names the driver, WHERE on the lap (norm), how hard,
            // and the local corner context, so the next report pinpoints
            // whether hits cluster at entries (norm just before a corner),
            // exits (just after), or one specific piece of geometry - three
            // different fixes, finally distinguishable from a log paste.
            if (impactKph > 60f && State != null && !participant.retired)
            {
                TrackProgress hitProgress = State.GetCurrentProgress(participant);
                float hitNorm = Track != null && Track.length > 1f ? hitProgress.distance / Track.length : 0f;
                float hitRadius = Track != null ? Track.CurvatureRadiusAt(hitProgress.distance) : 9999f;
                float radiusBehind = Track != null ? Track.CurvatureRadiusAt(hitProgress.distance - 45f) : 9999f;
                float radiusAhead = Track != null ? Track.CurvatureRadiusAt(hitProgress.distance + 45f) : 9999f;
                string phase = hitRadius < 250f ? "MID-CORNER"
                    : radiusBehind < 250f ? "CORNER-EXIT"
                    : radiusAhead < 250f ? "CORNER-ENTRY"
                    : "STRAIGHT";
                // Black-box round 2 (per report - "write code to diagnose it
                // properly"): WHAT was hit, its orientation relative to the
                // track, WHERE across the road the car was, the elevation
                // mismatch to the matched centreline (flyover/cliff detector),
                // and the AI controller's complete lateral state at dispatch
                // time. hitAngle reads: ~90 = face-on into something standing
                // ACROSS the road (cliff/crossing/rogue object - a track bug),
                // ~0-30 = glancing side-barrier contact (a driving bug).
                string colliderName = participant.vehicle.PendingWallHitColliderName;
                Vector3 hitNormal = participant.vehicle.PendingWallHitNormal;
                float hitAngle = hitNormal.sqrMagnitude > 0.001f
                    ? 90f - Vector3.Angle(hitProgress.forward, -hitNormal.normalized)
                    : 0f;
                float dy = 0f;
                if (Track != null)
                {
                    Vector3 matchedPoint, matchedForward, matchedRight;
                    Track.SampleAtDistance(hitProgress.distance, out matchedPoint, out matchedForward, out matchedRight);
                    dy = participant.transform.position.y - matchedPoint.y;
                }

                // [PitCrashDiag] (per report - "the same barrier crashing
                // problem while pitting ... write code to diagnose it"): when
                // the struck car is pit-bound, or the hit lands inside the pit
                // entry/exit windows, append the entire pit context - the
                // guided phase the car was in, where the drivable ramp
                // envelope actually was at that norm, and where the car was
                // relative to it. One line now answers "was the car off the
                // envelope (guidance bug) or was something solid ON the
                // envelope (geometry bug)".
                string pitContext = "";
                if (Track != null)
                {
                    bool carPitBound = participant.vehicle.PitRequested || participant.isPitting ||
                                       participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit;
                    bool inEntryWindow = hitNorm >= Track.PitEntryRampStartNormalized && hitNorm <= Track.PitCorridorStartNormalized;
                    bool inExitWindow = hitNorm > Track.PitExitRampStartNormalized || hitNorm <= Track.PitExitRampEndNormalized;
                    if (carPitBound || inEntryWindow)
                    {
                        float rampLateral;
                        float rampHalfWidth;
                        if (inExitWindow && !inEntryWindow)
                        {
                            Track.GetPitExitRampEnvelope(hitNorm, hitProgress.distance, out rampLateral, out rampHalfWidth);
                        }
                        else
                        {
                            Track.GetPitEntryRampEnvelope(hitNorm, hitProgress.distance, out rampLateral, out rampHalfWidth);
                        }

                        string envelopePosition = hitProgress.lateralDistance < rampLateral - rampHalfWidth ? "INSIDE-OF-RAMP(track side)"
                            : (hitProgress.lateralDistance > rampLateral + rampHalfWidth ? "OUTSIDE-OF-RAMP" : "ON-RAMP-ENVELOPE");
                        pitContext = " | [PitCrashDiag] pitBound=" + carPitBound +
                            " phase=" + participant.pitPhase +
                            " requested=" + participant.vehicle.PitRequested +
                            " limiter=" + participant.vehicle.PitLimiterActive +
                            " window=" + (inEntryWindow ? "entry" : (inExitWindow ? "exit" : "none")) +
                            " ramp=[" + (rampLateral - rampHalfWidth).ToString("0.0") + ".." + (rampLateral + rampHalfWidth).ToString("0.0") + "]m" +
                            " carLat=" + hitProgress.lateralDistance.ToString("0.0") + "m -> " + envelopePosition;
                    }
                }

                AiVehicleController hitAi = participant.vehicle.GetComponent<AiVehicleController>();
                Debug.LogWarning("[WallDiag] " + participant.driverName + " wall hit " + impactKph.ToString("0") +
                    "kph perpendicular at norm " + hitNorm.ToString("0.000") + " (" + phase +
                    ", local radius " + hitRadius.ToString("0") + "m, behind " + radiusBehind.ToString("0") +
                    "m, ahead " + radiusAhead.ToString("0") + "m) speed " +
                    Mathf.Abs(participant.vehicle.CurrentSpeedKph).ToString("0") + "kph t=" + RaceElapsed.ToString("0") + "s" +
                    " | obj='" + colliderName + "'" +
                    " hitAngle=" + hitAngle.ToString("0") + "deg(90=across-road,0=side-graze)" +
                    " lateral=" + hitProgress.lateralDistance.ToString("0.0") +
                    "/halfW=" + (Track != null ? Track.HalfWidthAt(hitProgress.distance).ToString("0.0") : "?") +
                    " dy=" + dy.ToString("0.0") +
                    pitContext +
                    (hitAi != null ? " | AI " + hitAi.DescribeLateralState() : ""));
                // [SwerveTape] the most valuable dump of all: the full steering
                // pipeline for the seconds BEFORE this wall contact.
                if (hitAi != null)
                {
                    hitAi.DumpSwerveTape("wall-hit(" + impactKph.ToString("0") + "kph,obj=" + colliderName + ")", hitNorm);
                }
            }

            participant.vehicle.PendingWallHitColliderName = "";
            participant.vehicle.PendingWallHitNormal = Vector3.zero;
            bool raceSession = (CurrentSession == RaceWeekendSession.Race || CurrentSession == RaceWeekendSession.QuickRace) && !IsTimeTrial;
            if (!raceSession || participant.retired || participant.finished || RaceElapsed < HardWallRetireMinRaceSeconds)
            {
                return;
            }

            if (impactKph >= HardWallRetireImpactKph)
            {
                GameLog.Warn("[RaceControl] " + participant.driverName + " OUT - heavy barrier impact (" + impactKph.ToString("0") + "kph perpendicular).");
                RetireParticipant(participant, "Heavy crash");
            }
        }

        void HandleFallRespawn(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || Track == null)
            {
                return;
            }

            participant.fallRespawnCooldown = Mathf.Max(0f, participant.fallRespawnCooldown - Time.deltaTime);
            TrackProgress progress = Track.GetProgress(participant.transform.position);
            float heightOffset = participant.transform.position.y - progress.nearestPoint.y;
            bool stableOnRoad =
                Mathf.Abs(progress.lateralDistance) <= LocalHalfWidthAt(progress.distance) &&
                heightOffset >= -0.35f &&
                heightOffset <= 2.25f;

            if (stableOnRoad && participant.fallRespawnCooldown <= 0f)
            {
                participant.hasLastSafePosition = true;
                participant.lastSafePosition = participant.transform.position;
                participant.lastSafeRotation = participant.transform.rotation;
            }

            // Two independent triggers: a hard, fast drop clearly past the track
            // surface, and a sustained mismatch for cars that settle on lower
            // ground beneath an elevated section without ever registering a single
            // instantaneous deep-fall frame. The second case was the actual "car
            // becomes impossible to drive" report - it never truly fell forever,
            // it just got physically stuck at the wrong height.
            bool hardFall = heightOffset < -3f;
            if (heightOffset < -1.5f && !hardFall)
            {
                participant.belowTrackTimer += Time.deltaTime;
            }
            else
            {
                participant.belowTrackTimer = 0f;
            }

            if (!hardFall && participant.belowTrackTimer <= 0.6f)
            {
                return;
            }

            participant.belowTrackTimer = 0f;
            Vector3 respawnPosition = participant.hasLastSafePosition
                ? participant.lastSafePosition + Vector3.up * 0.35f
                : progress.nearestPoint + Vector3.up * 0.45f;
            Quaternion respawnRotation = participant.hasLastSafePosition
                ? participant.lastSafeRotation
                : Quaternion.LookRotation(progress.forward, Vector3.up);

            Rigidbody body = participant.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = respawnPosition;
                body.rotation = respawnRotation;
            }
            else
            {
                participant.transform.position = respawnPosition;
                participant.transform.rotation = respawnRotation;
            }

            participant.fallRespawnCooldown = 2f;
            participant.resetGhostTimer = ResetGhostSeconds;
            GameLog.Warn("[RoadPhysics] Recovered " + participant.driverName +
                             " from an invalid below-track position (offset=" + heightOffset.ToString("0.0") +
                             "m). respawn=" + respawnPosition);
        }

        // Stuck-recovery escalation fix: AiVehicleController's own gentle
        // reverse/reorient maneuver (RecoveryManeuver in AiVehicleController.cs)
        // can genuinely fail to free a car wedged against a barrier, kerb, or
        // another car at a bad angle - and nothing previously escalated past
        // it, so a car that kept failing the same gentle maneuver could sit
        // there indefinitely (or only ever get removed by waiting out the
        // full StrandedRetireSeconds timer, well after it should have just
        // been able to keep racing). Once several real attempts have
        // genuinely failed - not one bad tick, a sustained pattern of still
        // barely moving after repeated tries - force a safe reposition to the
        // last known good on-track position/heading, the same
        // lastSafePosition mechanism HandleFallRespawn already uses for cars
        // that fall off an elevated section. Gated by both an attempt-count
        // floor and its own per-car cooldown so this is a genuine last
        // resort, never a repositioning loop, and it deliberately does NOT
        // register an incident/yellow flag itself - this is race control
        // quietly fixing a stuck car, not a new on-track event.
        const int StuckRepositionAttemptThreshold = 3;
        const float StuckRepositionCooldownSeconds = 25f;

        void HandleStuckEscalation(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || Track == null ||
                participant.retired || participant.finished || !participant.hasLastSafePosition ||
                participant.isRaceControlAutopilot || participant.isPitting || participant.pitPhase != PitPhase.None)
            {
                return;
            }

            participant.stuckRepositionCooldown = Mathf.Max(0f, participant.stuckRepositionCooldown - Time.deltaTime);
            if (participant.stuckRepositionCooldown > 0f)
            {
                return;
            }

            bool genuinelyStuck = (participant.recoveryState == CarRecoveryState.Recovering || participant.recoveryState == CarRecoveryState.ActuallyStranded) &&
                participant.recoveryAttemptCount >= StuckRepositionAttemptThreshold &&
                Mathf.Abs(participant.vehicle.CurrentSpeedKph) < 5f;

            if (!genuinelyStuck)
            {
                return;
            }

            // recoveryAttemptCount only ever increments inside
            // AiVehicleController's own maneuver-complete callback, so this
            // path naturally never triggers for the player (no
            // AiVehicleController component) without needing an explicit
            // isPlayer guard here.
            ForceRepositionToLastSafe(participant,
                "force-repositioned after " + StuckRepositionAttemptThreshold + "+ failed recovery attempts (last resort)");
        }

        // Shared last-resort rescue: snap a car back to its last known safe
        // on-track pose and clear every stuck/recovery timer. Used by the
        // stuck-escalation above AND by race control's stranded handler ("way
        // too easy to DNF" round 5): a car wedged against a wall is now
        // rescued and keeps racing instead of being permanently retired as
        // "Stranded" - the field only shrinks through genuinely terminal
        // events (mechanical, fuel, an unslowed max-speed head-on).
        public bool ForceRepositionToLastSafe(RaceParticipant participant, string reason)
        {
            if (participant == null || participant.vehicle == null || !participant.hasLastSafePosition ||
                participant.retired || participant.finished)
            {
                return false;
            }

            Vector3 respawnPosition = participant.lastSafePosition + Vector3.up * 0.35f;
            Quaternion respawnRotation = participant.lastSafeRotation;
            Rigidbody body = participant.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = respawnPosition;
                body.rotation = respawnRotation;
            }
            else
            {
                participant.transform.position = respawnPosition;
                participant.transform.rotation = respawnRotation;
            }

            participant.recoveryAttemptCount = 0;
            participant.stoppedOnTrackTimer = 0f;
            participant.wrongWayTimer = 0f;
            participant.stuckRepositionCooldown = StuckRepositionCooldownSeconds;
            participant.recoveryGraceTimer = RecoveryGraceSeconds;
            participant.resetGhostTimer = ResetGhostSeconds;

            AiVehicleController ai = participant.GetComponent<AiVehicleController>();
            if (ai != null)
            {
                ai.ResyncAfterForcedReposition();
            }

            GameLog.Warn("[RaceControl] " + participant.driverName + " " + reason + ".");
            return true;
        }

        // Stuck recovery: snap the player back to the middle of the road at their
        // current lap distance.
        //
        // No cooldown (per request - "why does the r for recovery have a cooldown?
        // get rid of that"). The 5s lockout was anti-exploit, but there is nothing
        // left to exploit: the reset places the car at the road centre at its CURRENT
        // lap distance, so it never advances the player, and the rejoin speed matches
        // the AI's own auto-recovery. All it actually achieved was leaving a player
        // who pressed R a fraction too early - while still tumbling, or before the
        // car had settled - stranded and unable to press it again, which is the exact
        // situation the key exists for.
        public void ResetPlayerToSafePose(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer || participant.vehicle == null || Track == null ||
                participant.isPitting || participant.pitPhase != PitPhase.None || !CanDrive)
            {
                return;
            }

            // Rolling rejoin (per request - "when the player presses R they
            // also start with speed like the AI do"): same 120kph the AI
            // off-track auto-recovery rejoins at, so R no longer dumps the
            // player at a standstill in live traffic.
            ResetParticipantToTrackCenter(participant, 120f);

            // Penalty removed (per request - "pressing R should not give the
            // player a 5 second penalty"): the reset places the car at the road
            // centre at its CURRENT distance, so it can't gain time; the 5s
            // reset cooldown above still prevents spamming. The AI get the
            // identical free recovery (crash/off-track auto-reset in
            // UpdateRaceControl), so this is symmetric.
            if (CurrentSession != RaceWeekendSession.Qualifying && !IsTimeTrial)
            {
                SessionMessage = "Car recovered";
                PostEngineerMessage("Car recovered to the track.", true);
            }
            else
            {
                SessionMessage = "Car recovered: lap invalidated";
                PostEngineerMessage("Car recovered. This lap will not count.", true);
            }
        }

        // The shared recovery action (the hold-R behaviour): place the car at
        // the MIDDLE of the road at its current lap distance, facing down the
        // track - the one spot guaranteed to be solid, drivable tarmac on
        // every layout (lastSafePosition was routinely right at the track
        // edge, or past it on layouts with boundary defects). Used by the
        // player's hold-R reset above and by the AI crawl-recovery rule in
        // UpdateRaceControl.
        public void ResetParticipantToTrackCenter(RaceParticipant participant, float rejoinSpeedKph = 0f)
        {
            if (participant == null || Track == null)
            {
                return;
            }

            TrackProgress progress = participant.lapTracker != null
                ? participant.lapTracker.CurrentProgress
                : Track.GetProgress(participant.transform.position);

            Vector3 centerPoint;
            Vector3 centerForward;
            Vector3 centerRight;
            Track.SampleAtDistance(progress.distance, out centerPoint, out centerForward, out centerRight);
            Vector3 respawnPosition = centerPoint + Vector3.up * 0.45f;
            Quaternion respawnRotation = Quaternion.LookRotation(centerForward, Vector3.up);

            // Rejoin at speed, not from a standstill (per report - "a lot of
            // cars slow by lap 2"): a car recovered mid-race used to be dropped
            // to zero velocity and had to crawl back up from 0, which - with
            // the first-lap contact under maxed aggression producing many
            // recoveries - left a chunk of the field re-accelerating from
            // standstill in the opening laps. AI recoveries now rejoin at a
            // sensible forward speed; the player's hold-R keeps the default 0.
            Vector3 rejoinVelocity = respawnRotation * Vector3.forward * (rejoinSpeedKph / 3.6f);
            Rigidbody body = participant.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = rejoinVelocity;
                body.angularVelocity = Vector3.zero;
                body.position = respawnPosition;
                body.rotation = respawnRotation;
            }

            participant.transform.position = respawnPosition;
            participant.transform.rotation = respawnRotation;
            participant.fallRespawnCooldown = 2f;
            // Ghost window: a car that just materialised mid-track must not be
            // T-boned by whoever was racing through that exact spot.
            participant.resetGhostTimer = ResetGhostSeconds;

            // Clear the AI's transient recovery state (the real "slow from lap 2"
            // bug): without this a crash-recovered car kept executing its stale
            // reverse/reorient maneuver AND its Recovering classification after
            // being placed, so it reversed/crawled right after rejoining at
            // speed instead of racing - a car that crashed in the lap-1 scrum
            // stayed slow for the rest of the race. Mirrors HandleStuckEscalation.
            participant.recoveryState = CarRecoveryState.Normal;
            participant.recoveryAttemptCount = 0;
            participant.stoppedOnTrackTimer = 0f;
            participant.wrongWayTimer = 0f;
            participant.recoveryGraceTimer = RecoveryGraceSeconds;
            AiVehicleController recoveredAi = participant.GetComponent<AiVehicleController>();
            if (recoveredAi != null)
            {
                recoveredAi.ResyncAfterForcedReposition();
            }

            if (participant.lapTracker != null)
            {
                participant.lapTracker.InvalidateCurrentLap();
            }
        }

    }
}
