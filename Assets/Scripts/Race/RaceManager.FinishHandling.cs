using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager per-participant finish + penalty subsystem (partial). Handles a
    /// car crossing the line for the last time (HandleFinish - mandatory-pit
    /// penalty, State.OnParticipantFinished, the player podium radio and the finish
    /// camera flourish), the finish-position radio line, the mandatory-pit penalty
    /// (gated by the unit-tested PenaltyRules), and the shared AddPenalty utility
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
                ApplyMandatoryPitPenalty(participant);
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

        void ApplyMandatoryPitPenalty(RaceParticipant participant)
        {
            // Gates (incl. the RaceLaps<=3 short-race exemption) live in the
            // unit-tested rulebook - see PenaltyRules.ShouldApplyMandatoryPitPenalty.
            if (!PenaltyRules.ShouldApplyMandatoryPitPenalty(
                    CurrentSession == RaceWeekendSession.Qualifying,
                    IsTimeTrial,
                    RaceLaps,
                    participant.pitStops,
                    participant.mandatoryPitPenaltyApplied))
            {
                return;
            }

            participant.mandatoryPitPenaltyApplied = true;
            AddPenalty(participant, PenaltyRules.MandatoryPitPenaltySeconds, PenaltyRules.MandatoryPitReason);
            if (participant.isPlayer)
            {
                // Read the tariff off the constant rather than hard-coding it. The
                // literal here still said +10s after MandatoryPitPenaltySeconds was
                // raised to 30, so the player was told 10, the race-control timeline
                // said 30, and the results were computed with 30 - a one-to-two
                // position discrepancy they had no way to account for.
                SessionMessage = "No mandatory stop: +" + PenaltyRules.MandatoryPitPenaltySeconds.ToString("0") + "s";
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
