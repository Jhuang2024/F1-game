using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager pit-entry-assist subsystem (partial). A narrow, opt-in-by-plan
    /// assist that steers a PreRacePlan pit request onto the pit ramp inside the
    /// approach window (ShouldAssistPlayerPitEntry gate, BuildPitEntryAssistCommand
    /// steering) until BeginPitEntry takes over - a manual or race-control-offer
    /// request never matches, so manual entry is untouched. Split out of the
    /// RaceManager monolith verbatim - same class, same members, identical gate and
    /// steering behaviour; the public entry point stays public so input callers
    /// resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        // Pre-race pit lap fix: a scheduled PreRacePlan stop only ever latched
        // vehicle.PitRequested and otherwise left the player fully in manual
        // control - if the player missed the physical ramp (drove straight on,
        // got the line wrong, was mid-battle), missedPitEntryThisLap cleared the
        // request and it retried next lap, silently turning a "stop on lap 4"
        // plan into a real lap-5 stop. This is a narrow, opt-in-by-plan assist:
        // it only ever engages for a PreRacePlan request, only inside the pit
        // approach window, and only until BeginPitEntry takes over (at which
        // point pitPhase != None and ShouldAssistPlayerPitEntry stops matching,
        // handing off to the existing kinematic pit-guidance system). A manual
        // (P key) or accepted race-control offer request never matches
        // PitRequestSource.PreRacePlan, so manual entry stays exactly as
        // manual as it always was.
        public bool ShouldAssistPlayerPitEntry(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer || participant.vehicle == null ||
                Track == null || State == null || participant.lapTracker == null)
            {
                return false;
            }

            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial || IsRaceFinished || StartCountdown > 0f)
            {
                return false;
            }

            if (participant.pitPhase != PitPhase.None || participant.isPitting || participant.retired || participant.finished)
            {
                return false;
            }

            // Manual-pit fix (per request - "manual pits don't send me into the
            // pit lane like automatic ones do"): the assist was deliberately
            // PreRacePlan-only, leaving a P-key (or accepted SC-offer) request
            // fully manual - but the physical ramp opening is a narrow window
            // and missing it silently pushed the stop a lap later, which read
            // as manual pits simply not working. Every latched player pit
            // request now gets the same approach guidance; cancelling the
            // request (O key / HUD button) before the limiter line remains the
            // way to opt back out.
            if (!participant.vehicle.PitRequested || participant.missedPitEntryThisLap ||
                participant.activePitRequestSource == PitRequestSource.None)
            {
                return false;
            }

            TrackProgress progress = State.GetCurrentProgress(participant);
            return progress.normalized > TrackRuntime.PitApproachStartNormalized && progress.normalized <= Track.PitCorridorStartNormalized;
        }

        // Player pit-entry buff (per request): deliberately ABOVE the AI's
        // 90 kph approach target (AiVehicleController.PitApproachTargetSpeedKph)
        // and matched to the player's raised entry limiter (VehicleController)
        // - the player now approaches and enters the pits faster than the AI
        // by design. Round 2 (per request): raised again, 100 -> 150.
        // Round 3 (per request): eased back, 150 -> 125.
        // Round 4 (per request): eased back again, 125 -> 110.
        // Round 5 (per request): 110 -> 105.
        const float PitEntryAssistTargetSpeedKph = 105f;
        // Same short, dedicated pit-entry look-ahead AiVehicleController uses
        // (PitEntryLookAheadMeters) - the normal racing-line lookahead is tuned
        // for reading corners far down the track, not for tracking the much
        // shorter pit-entry ramp whose lateral envelope changes quickly.
        const float PitEntryAssistLookAheadMeters = 18f;

        // Builds the actual steer/throttle/brake command for the assist window
        // identified by ShouldAssistPlayerPitEntry. Targeting geometry-fix: this
        // used to blend Track.PitEntryApproachLateral (a point deliberately
        // OUTSIDE the live track edge, behind the barrier that still stands
        // there before the real opening) against a lateral computed at the
        // car's current distance but attached to a racing-line point sampled
        // further down the track - together that steered the player straight
        // into the wall between PitApproachStartNormalized and
        // PitEntryRampStartNormalized, and the position/lateral mismatch meant
        // the two didn't even describe the same point on a curved approach.
        // Now delegates to TrackRuntime.ComputePitEntryTargetPoint, the exact
        // same two-stage (stay-on-track pre-position, then canonical ramp pose),
        // same-distance world-space target builder AiVehicleController's own
        // pit-entry steering already uses, so player and AI pit-entry geometry
        // can never diverge again. Callers must have already confirmed
        // ShouldAssistPlayerPitEntry(participant) is true.
        public VehicleCommand BuildPitEntryAssistCommand(RaceParticipant participant, VehicleCommand fallback)
        {
            VehicleCommand command = fallback;
            TrackProgress progress = State.GetCurrentProgress(participant);
            float speedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);

            Vector3 targetPoint;
            Quaternion targetRotation;
            Track.ComputePitEntryTargetPoint(progress.distance, PitEntryAssistLookAheadMeters, out targetPoint, out targetRotation);

            Vector3 toTarget = targetPoint - participant.transform.position;
            float steer = Mathf.Clamp(Vector3.Dot(toTarget.normalized, participant.transform.right) * 2.2f, -1f, 1f);

            // Alignment-scaled approach speed (per report - "pinned against the
            // wall in pit entry again", now at the 150 kph entry pace): arriving
            // at full entry speed while still owing metres of sideways movement
            // is exactly what slams the car into the wall - the lateral
            // correction can't keep up and the oscillation ends on the barrier.
            // The assist now sheds speed in proportion to how much lateral error
            // remains (full 150 only once lined up, down to ~95 when far off
            // line), so the car positions first and runs the buffed speed once
            // straight.
            float lateralErrorMeters = Mathf.Abs(Vector3.Dot(toTarget, participant.transform.right));
            float alignedTargetKph = Mathf.Lerp(PitEntryAssistTargetSpeedKph, 95f, Mathf.Clamp01(lateralErrorMeters / 6f));

            // Pit-approach braking envelope (per report - "why are some AI
            // allowed to enter the pits later than I am? They're at full racing
            // speed for a solid one more corner"): the assist used to drag the
            // player down to the flat target from the very start of the
            // approach window (normalized 0.78), hundreds of metres before the
            // ramp physically exists - while an AI whose pit trigger latched
            // mid-approach kept racing deeper. Both now use the identical rule
            // (see AiVehicleController's committingToPit envelope, same decel,
            // same 80 m pre-ramp buffer): full racing speed until the kinematic
            // braking point for the ramp, one normal braking zone down to the
            // entry target. Until the envelope actually demands braking, the
            // player keeps their own throttle/brake (they're driving a normal
            // racing stretch - corners included - and only the steering guide
            // applies); once inside the braking zone the assist takes speed
            // over so the ramp is always made at the right pace.
            const float PitApproachBrakeDecelMs2 = 10f;
            const float PitApproachRampBufferMetres = 80f;
            float metresToRamp = (Track.PitEntryRampStartNormalized - progress.normalized) * Track.length;
            float envelopeDistance = Mathf.Max(0f, metresToRamp - PitApproachRampBufferMetres);
            float targetMs = alignedTargetKph / 3.6f;
            float envelopeKph = Mathf.Sqrt(targetMs * targetMs + 2f * PitApproachBrakeDecelMs2 * envelopeDistance) * 3.6f;
            // Fully automated again (per report - "why is it no longer
            // animated and I can press the throttle to make me go faster??"):
            // an earlier pass handed throttle/brake back to the player until
            // the braking point, which read as the pit entry being broken
            // rather than as freedom. The assist now drives the whole approach
            // itself - at full racing pace until the envelope's braking point
            // (so the old "crawls a corner early" complaint stays fixed), then
            // one clean braking zone to the entry speed. Player throttle input
            // can no longer add speed on top.
            float speedGapKph = envelopeKph - speedKph;
            if (speedGapKph < -3f)
            {
                command.brake = Mathf.Clamp01(-speedGapKph / 35f);
                command.throttle = 0f;
            }
            else
            {
                command.brake = 0f;
                command.throttle = Mathf.Clamp01(0.2f + speedGapKph / 35f);
            }

            // Unstick fix: the old "ease off" branch capped throttle at 0.35
            // and kept steering at the (outboard) target - a car that touched
            // the pit wall just ground against it at 0 km/h forever, which is
            // exactly how the player ended up parked on the barrier with "Pit
            // entry approaching" on screen. A genuinely wedged car (near-zero
            // speed, hard against the right edge) now steers LEFT, away from
            // the wall the pit lane always sits on, with enough throttle to
            // actually free itself, then resumes chasing the target normally.
            // Unstick round 2 (per report - "stuck against the wall in pit entry
            // again"): the old gate only engaged below 3 km/h, so a car grinding
            // along the barrier at 5-15 km/h was never freed - it kept steering
            // toward the outboard target and stayed pinned. Engages below
            // 12 km/h now, with a wider wall-detection band and a stronger
            // steer-away, so a wall-scrubbing car is pulled off the barrier
            // while it still has momentum instead of only after a dead stop.
            if (speedKph < 12f)
            {
                bool againstWall = progress.lateralDistance > LocalHalfWidthAt(progress.distance) - 2f;
                if (againstWall)
                {
                    steer = -0.6f;
                    command.throttle = 0.5f;
                    command.brake = 0f;
                }
                else if (command.throttle > 0f)
                {
                    command.throttle = Mathf.Min(command.throttle, 0.5f);
                    steer = Mathf.Clamp(steer, -0.5f, 0.5f);
                }
            }

            // Player-agency fix (per report - "pit entry slams me into the
            // barriers and I get stuck"): a PreRacePlan auto-stop leaves the car
            // on the racing line, so a full steering override is safe and
            // expected. But a MANUAL (P-key) or SC-offer request can be pressed
            // from anywhere on track, and fully overriding the wheel there drove
            // the player across the edge barrier with no way to correct. For
            // those sources the assist now only BLENDS with the driver's own
            // steering (a strong guide, not a lock), so the player can always
            // counter it away from a wall while still being pulled toward the
            // ramp; the planned auto-stop keeps its full override.
            // Unstick fix round 2 (cont.): the manual-source blend below diluted
            // the wall-escape steer (0.55x, plus the player's own wheel - often
            // still pointed AT the pit lane, i.e. at the wall) - which is
            // exactly how a pinned car stayed pinned with the assist "active".
            // A genuine wall-escape takes full steering authority regardless of
            // request source; the blend only applies to normal target-chasing.
            // Full steering authority for every request source now (per report
            // - "why can I steer the car"): the old manual-source blend existed
            // because the flat-speed assist could drag a fast-moving player
            // into walls, but the braking envelope above already guarantees the
            // car arrives at the ramp at a controlled speed, so the guided line
            // is safe to hold outright - and a guided pit entry is what the
            // player expects to see once a stop is latched. Cancelling the
            // request (O key / HUD button) before the limiter line remains the
            // way to take back control.
            command.steer = steer;
            command.ers = false;
            command.drs = false;
            return command;
        }

    }
}
