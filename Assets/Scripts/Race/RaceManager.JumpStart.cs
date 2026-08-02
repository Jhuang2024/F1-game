using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager race-start launch subsystem (partial). Tracks the player's
    /// launch input during the start countdown and applies the false-start penalty
    /// when a car moves before lights-out (ReportJumpStartIntent /
    /// RecordPlayerLaunchInput). Split out of the RaceManager monolith verbatim -
    /// same class, same members, identical penalty tariff and call order; the
    /// public entry points stay public so input callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        public void ReportJumpStartIntent(RaceParticipant participant)
        {
            if (participant == null || participant.jumpStartPenaltyApplied || StartCountdown <= 0f || CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return;
            }

            // A jump start means the car MOVED before lights out - that is the
            // rulebook's own definition, and it is what Judge(true, ...) asserts
            // below. A car still pinned in its grid box has not moved and cannot:
            // VehicleController pins its transform, zeroes its velocity and
            // discards its throttle for as long as IsHeldOnGrid is set.
            //
            // Without this guard the player's call site (PlayerVehicleInput, in the
            // !CanDrive branch) reported a jump start purely because the throttle
            // KEY was held during the countdown, while the car sat motionless and
            // the input was thrown away one line later. Pre-loading the throttle
            // during the randomised 2.9-4.3s hold - the natural thing to do, and
            // what the randomised hold invites - therefore meant a guaranteed +5s
            // on essentially every race start, for zero mechanical gain.
            //
            // AI jump starts are unaffected: RaceManager.Update releases a
            // jumping car with SetGridHold(false) BEFORE calling this, so that car
            // really is rolling and really is judged.
            //
            // Anticipating the lights is still penalised, through the separate and
            // correctly-modelled reaction-time rule in RecordPlayerLaunchInput.
            if (participant.vehicle != null && participant.vehicle.IsHeldOnGrid)
            {
                return;
            }

            // Judgement + tariff live in the extracted rulebook
            // (StartProcedureRules); this method only supplies the detection.
            StartInfraction infraction = StartProcedureRules.Judge(true, -1f, true);
            float penaltySeconds = StartProcedureRules.PenaltySeconds(infraction);
            participant.jumpStartPenaltyApplied = true;
            AddPenalty(participant, penaltySeconds, "Jump start");
            SessionMessage = participant.isPlayer ? "Jump start: +" + penaltySeconds.ToString("0") + "s" : SessionMessage;
        }

        public void RecordPlayerLaunchInput(RaceParticipant participant, float throttle)
        {
            if (participant == null || !participant.isPlayer || !waitingForPlayerReaction || CurrentSession == RaceWeekendSession.Qualifying || lightsOutTime <= 0f || throttle < 0.12f)
            {
                return;
            }

            waitingForPlayerReaction = false;
            playerReactionTime = Mathf.Max(0f, Time.time - lightsOutTime);
            reactionDisplayTimer = 7f;
            SessionMessage = "Reaction " + playerReactionTime.ToString("0.000") + "s";
            PostEngineerMessage("Reaction time " + playerReactionTime.ToString("0.000") + " seconds.", true);

            // Anticipation rule: a throttle already committed as the lights go
            // out reads as a false start (StartProcedureRules judges the
            // threshold). Skipped if the harsher jump-start penalty already hit.
            if (!participant.jumpStartPenaltyApplied)
            {
                StartInfraction infraction = StartProcedureRules.Judge(false, playerReactionTime, true);
                if (infraction == StartInfraction.FalseStart)
                {
                    float penaltySeconds = StartProcedureRules.PenaltySeconds(infraction);
                    participant.jumpStartPenaltyApplied = true;
                    AddPenalty(participant, penaltySeconds, "False start");
                    SessionMessage = "False start: +" + penaltySeconds.ToString("0") + "s";
                    PostEngineerMessage("That was a false start - " + penaltySeconds.ToString("0") + " second penalty.", true);
                }
            }
        }

    }
}
