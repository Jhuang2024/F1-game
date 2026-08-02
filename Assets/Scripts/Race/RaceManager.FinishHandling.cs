using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager per-participant finish + penalty subsystem (partial). Handles a
    /// car crossing the line for the last time (HandleFinish - the two-compound
    /// rule check, State.OnParticipantFinished, the player podium radio and the
    /// finish camera flourish), the finish-position radio line, the two-compound
    /// dry-tyre rule (gated by the unit-tested PenaltyRules), and the AddPenalty utility
    /// (penalty seconds/reason, the PenaltyIssuedEvent and the player-only timeline
    /// entry). Split out of the RaceManager monolith verbatim - same class, same
    /// members, identical penalty values and call order; callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        void HandleFinish(RaceParticipant participant)
        {
            if (participant == null || participant.finished || participant.lapTracker == null || State == null)
            {
                return;
            }

            if (participant.lapTracker.CompletedRace)
            {
                ApplyTwoCompoundRule(participant);
                State.OnParticipantFinished(participant, RaceElapsed);

                if (participant.isPlayer && !engineerPodiumMessageSent)
                {
                    engineerPodiumMessageSent = true;
                    PostEngineerMessage(FinishEngineerMessage(participant.finishingPosition), true);

                    // Part 15/19: Cinematic race presentation gets a small finish-line
                    // camera flourish (a FOV punch-in) instead of nothing happening
                    // when the chequered flag falls.
                    if (Settings != null && Settings.Current.racePresentation >= 2 && participant.vehicle != null)
                    {
                        PlayerVehicleInput playerInput = participant.vehicle.GetComponent<PlayerVehicleInput>();
                        if (playerInput != null && playerInput.cameraRig != null)
                        {
                            playerInput.cameraRig.AddImpulseShake(0.14f);
                        }
                    }
                }
            }
        }

        // Part 1: a finish-position-appropriate radio line instead of nothing at
        // all once the chequered flag falls.
        string FinishEngineerMessage(int position)
        {
            if (position == 1)
            {
                return "That's the win! Fantastic drive, take the flag.";
            }

            if (position <= 3)
            {
                return "P" + position + " and on the podium! Great result, well driven.";
            }

            if (position <= 10)
            {
                return "P" + position + ", points on the board. Solid job out there.";
            }

            return "P" + position + " at the flag. We'll take the data and come back stronger.";
        }

        /// <summary>
        /// Enforces the REAL dry-race tyre regulation: at least two different dry
        /// specifications must be used, on pain of disqualification.
        ///
        /// This replaces a "mandatory pit stop" rule carrying a +30s time penalty,
        /// which does not exist in F1. That rule never inspected which compounds
        /// were fitted (soft->soft complied), applied in wet races where the real
        /// requirement is void, and by making non-compliance a survivable time loss
        /// turned a hard rule into a strategy option.
        /// </summary>
        void ApplyTwoCompoundRule(RaceParticipant participant)
        {
            if (participant == null || participant.twoCompoundRuleChecked)
            {
                return;
            }

            participant.twoCompoundRuleChecked = true;

            int distinctDry;
            bool usedWet;
            participant.CountDryCompoundsUsed(out distinctDry, out usedWet);

            if (!PenaltyRules.ShouldDisqualifyForTwoCompoundRule(
                    CurrentSession == RaceWeekendSession.Qualifying,
                    IsTimeTrial,
                    RaceDeclaredWet,
                    usedWet,
                    RaceLaps,
                    distinctDry,
                    participant.retired))
            {
                return;
            }

            participant.disqualified = true;
            participant.penaltyReason = PenaltyRules.AppendPenaltyReason(participant.penaltyReason, PenaltyRules.TwoCompoundReason);
            GameEvents.Publish(new PenaltyIssuedEvent(
                participant.driverId,
                PenaltyKind.Disqualification,
                0f,
                PenaltyRules.TwoCompoundReason));

            GameLog.Warn("[RaceControl] " + participant.driverName +
                " disqualified: only " + distinctDry + " dry compound(s) used.");

            if (participant.isPlayer)
            {
                SessionMessage = "DISQUALIFIED: you must use two different dry compounds";
                PostEngineerMessage("We've been disqualified - the rules require two different dry compounds and we only ran one.", true, RaceAudioCue.Penalty);
                LogRaceControlHistory("DSQ", PenaltyRules.TwoCompoundReason);
            }
        }

        void AddPenalty(RaceParticipant participant, float seconds, string reason)
        {
            participant.penaltiesSeconds += seconds;
            participant.penaltyReason = PenaltyRules.AppendPenaltyReason(participant.penaltyReason, reason);
            GameEvents.Publish(new PenaltyIssuedEvent(
                participant.driverId,
                PenaltyKind.TimePenalty,
                seconds,
                reason));

            // Player-only: an AI penalty lands every few laps across a 21-car
            // field, which would flood a shared timeline with noise the player
            // has no reason to care about. Their own penalties are always
            // worth a timeline entry.
            if (participant.isPlayer)
            {
                LogRaceControlHistory("PENALTY", "+" + seconds.ToString("0") + "s - " + reason);
            }
        }

    }
}
