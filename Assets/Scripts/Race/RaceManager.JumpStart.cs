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
