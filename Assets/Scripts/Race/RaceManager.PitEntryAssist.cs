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

        // Kept in sync with AiVehicleController.PitApproachTargetSpeedKph so the
        // assisted player and the AI approach pit entry at the identical speed
        // (pit-entry parity fix). If one changes, change both.
        const float PitEntryAssistTargetSpeedKph = 90f;
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

            float speedGapKph = PitEntryAssistTargetSpeedKph - speedKph;
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
            if (speedKph < 3f)
            {
                bool againstWall = progress.lateralDistance > LocalHalfWidthAt(progress.distance) - 1.6f;
                if (againstWall)
                {
                    steer = -0.45f;
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
            if (participant.activePitRequestSource == PitRequestSource.PreRacePlan)
            {
                command.steer = steer;
            }
            else
            {
                command.steer = Mathf.Clamp(steer * 0.55f + fallback.steer * 0.7f, -1f, 1f);
            }
            command.ers = false;
            command.drs = false;
            return command;
        }

    }
}
