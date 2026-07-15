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

            AiVehicleController ai = participant.GetComponent<AiVehicleController>();
            if (ai != null)
            {
                ai.ResyncAfterForcedReposition();
            }

            // recoveryAttemptCount only ever increments inside
            // AiVehicleController's own maneuver-complete callback, so this
            // path naturally never triggers for the player (no
            // AiVehicleController component) without needing an explicit
            // isPlayer guard here.
            GameLog.Warn("[RaceControl] " + participant.driverName + " force-repositioned after " + StuckRepositionAttemptThreshold + "+ failed recovery attempts (last resort).");
        }

        // Stuck recovery: snap the player back to the last safe on-track pose.
        // Costs five seconds in competitive race sessions so it cannot be exploited.
        public void ResetPlayerToSafePose(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer || participant.vehicle == null || Track == null ||
                participant.isPitting || participant.pitPhase != PitPhase.None || playerResetCooldown > 0f || !CanDrive)
            {
                return;
            }

            playerResetCooldown = 5f;
            ResetParticipantToTrackCenter(participant);

            if (CurrentSession != RaceWeekendSession.Qualifying && !IsTimeTrial)
            {
                AddPenalty(participant, 5f, "Car recovery");
                SessionMessage = "Car recovered: +5s";
                PostEngineerMessage("Car recovered to the track. Five second penalty added.", true);
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

            if (participant.lapTracker != null)
            {
                participant.lapTracker.InvalidateCurrentLap();
            }
        }

    }
}
