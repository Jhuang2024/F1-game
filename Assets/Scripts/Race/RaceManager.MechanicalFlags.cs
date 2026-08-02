using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager mechanical-flag subsystem (partial): the black-and-orange flag and
    /// the black flag.
    ///
    /// Both flags already existed in FlagRules - RaceFlag.MechanicalBlackOrange,
    /// RaceFlag.Black, EndsParticipation, RequiresPitForRepair - and nothing in the
    /// race layer ever showed either of them, so a car could run a whole grand prix
    /// with a rear wing hanging off and race control had no way to say anything about
    /// it. Damage now has the components to make the flags mean something (see
    /// DamageState.rearWing/suspension and DamagePerformance's thresholds).
    ///
    /// The real sequence: race control shows the black-and-orange to a car with a
    /// mechanical problem or loose bodywork; the driver must come in at the end of the
    /// current lap to have it put right. Ignore it and the flag escalates to the black
    /// flag - disqualification. Broken suspension is not something a stop fixes, so
    /// that goes straight to a retirement.
    /// </summary>
    public partial class RaceManager
    {
        /// <summary>
        /// Laps a car is given to report to the pits after the black-and-orange is
        /// shown before it is black-flagged. The real regulation is "at the end of the
        /// current lap"; two laps allows for a car shown the flag just past pit entry
        /// having to complete another lap before it can physically come in.
        /// </summary>
        const int BlackOrangeGraceLaps = 2;

        void UpdateMechanicalFlags(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null ||
                participant.retired || participant.finished || participant.disqualified ||
                CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return;
            }

            DamageState damage = participant.vehicle.Damage;
            if (damage == null)
            {
                return;
            }

            // Terminal suspension. A broken wishbone ends the race on the spot - the
            // car cannot be driven to the pits and there is nothing to repair when it
            // gets there. This is what makes suspension worth modelling separately
            // from bodywork rather than as more of the same damage percentage.
            if (damage.SuspensionIsTerminal)
            {
                RetireParticipant(participant, "Suspension failure");
                return;
            }

            bool needsFlag = damage.RequiresMechanicalFlag;

            if (!participant.blackOrangeShown)
            {
                if (!needsFlag)
                {
                    return;
                }

                participant.blackOrangeShown = true;
                participant.blackOrangeShownLap = participant.lapTracker.CompletedLaps;
                GameEvents.Publish(new PenaltyIssuedEvent(
                    participant.driverId,
                    PenaltyKind.TimePenalty,
                    0f,
                    "Black and orange flag - report to the pits"));
                GameLog.Warn("[RaceControl] " + participant.driverName +
                    " shown the black and orange flag - must pit for repairs.");
                LogRaceControlHistory("BLACK/ORANGE", participant.driverName + " - report to the pits for repairs");
                if (participant.isPlayer)
                {
                    SessionMessage = "BLACK AND ORANGE FLAG: box this lap for repairs";
                    PostEngineerMessage("Black and orange flag - we have damage race control wants fixed. Box this lap.", true, RaceAudioCue.Penalty);
                }

                return;
            }

            // Cleared: the damage has actually been repaired (a pit stop calls
            // DamageState.RepairPitDamage), so the flag comes down.
            if (!needsFlag)
            {
                participant.blackOrangeShown = false;
                participant.blackOrangeShownLap = -1;
                if (participant.isPlayer)
                {
                    PostEngineerMessage("Repairs done, the black and orange flag is withdrawn. Back to racing.", true);
                }

                return;
            }

            // Still damaged, and out of laps: the flag escalates.
            int lapsSinceShown = participant.lapTracker.CompletedLaps - participant.blackOrangeShownLap;
            if (lapsSinceShown < BlackOrangeGraceLaps)
            {
                return;
            }

            ApplyBlackFlag(participant, "Ignored the black and orange flag");
        }

        /// <summary>
        /// The black flag: disqualification, and the car is out of the race there and
        /// then. Shares the disqualified flag the two-compound rule sets, so the
        /// classification, the standings and the countback all already handle it.
        /// </summary>
        void ApplyBlackFlag(RaceParticipant participant, string reason)
        {
            if (participant == null || participant.disqualified)
            {
                return;
            }

            participant.disqualified = true;
            participant.penaltyReason = PenaltyRules.AppendPenaltyReason(participant.penaltyReason, reason);
            GameEvents.Publish(new PenaltyIssuedEvent(
                participant.driverId,
                PenaltyKind.Disqualification,
                0f,
                reason));
            GameLog.Warn("[RaceControl] " + participant.driverName + " BLACK FLAGGED: " + reason);
            LogRaceControlHistory("BLACK FLAG", participant.driverName + " - " + reason);
            if (participant.isPlayer)
            {
                SessionMessage = "BLACK FLAG: " + reason;
                PostEngineerMessage("That's the black flag. We're out - " + reason.ToLowerInvariant() + ".", true, RaceAudioCue.Penalty);
            }

            // A black-flagged car leaves the circuit immediately. RetireParticipant
            // parks it and publishes the retirement; the disqualified flag above is
            // what keeps it out of the points.
            RetireParticipant(participant, "Black flagged");
        }

        /// <summary>
        /// The flag this car is currently being shown for its own mechanical state,
        /// or Green. HUD-facing; layered on top of the course-wide flag rather than
        /// replacing it, since a black-and-orange is shown to ONE car.
        /// </summary>
        public RaceFlag MechanicalFlagFor(RaceParticipant participant)
        {
            if (participant == null)
            {
                return RaceFlag.Green;
            }

            if (participant.disqualified)
            {
                return RaceFlag.Black;
            }

            return participant.blackOrangeShown ? RaceFlag.MechanicalBlackOrange : RaceFlag.Green;
        }
    }
}
