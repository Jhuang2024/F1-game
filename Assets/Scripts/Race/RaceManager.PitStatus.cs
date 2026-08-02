using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager pit-status display subsystem (partial). Formats the player's
    /// live pit-status line (approach/queue/service/exit phrasing) and the 0-1
    /// service-progress value for the HUD gauge. Split out of the RaceManager
    /// monolith verbatim - same class, same members, identical phrasing and
    /// progress maths; the public entry points stay public so HUD callers resolve
    /// in-class.
    /// </summary>
    public partial class RaceManager
    {
        public string PitStatusText(RaceParticipant participant)
        {
            if (CurrentSession == RaceWeekendSession.Qualifying || participant == null)
            {
                return "";
            }

            if (IsTimeTrial && participant.pitPhase == PitPhase.None && !participant.isPitting)
            {
                return "";
            }

            if (participant.pitPhase == PitPhase.Entry)
            {
                // Shared entry pace for player and AI alike; read off the live
                // constant so the HUD can't drift from the rule again.
                return "PIT LANE  TO BOX " + (participant.pitBoxIndex + 1) + "  LIMITER " + PitEntryPaceKph.ToString("0");
            }

            if (participant.pitPhase == PitPhase.Service)
            {
                if (participant.pitAwaitingRelease)
                {
                    return "PIT HOLD  AWAITING RELEASE GAP";
                }

                float elapsed = Mathf.Max(0f, participant.pitServiceDuration - participant.pitTimer);
                return "PIT STOP  " + elapsed.ToString("0.0") + "s / " + participant.pitServiceDuration.ToString("0.0") + "s  " + participant.nextPitCompound;
            }

            if (participant.pitPhase == PitPhase.Release)
            {
                // Reads the live constant instead of a stale literal: release/exit
                // run at PitExitPaceKph (106), so the HUD said 80 while the
                // speedometer showed 106.
                return "PIT RELEASE  LIMITER " + PitExitPaceKph.ToString("0");
            }

            if (participant.pitPhase == PitPhase.ExitMerge)
            {
                return "PIT EXIT  MERGING";
            }

            if (participant.pitLimiterUntilExit)
            {
                return "PIT EXIT  LIMITER " + PitExitPaceKph.ToString("0");
            }

            if (participant.pitTyreSelectionActive && participant.vehicle != null && participant.vehicle.PitRequested)
            {
                return "PIT TYRE " + participant.requestedPitCompound + "  1S 2M 3H 4I 5W";
            }

            // Pit strategy display fix: this used to check pitStops > 0 first,
            // so a 2-stop plan's already-queued second request showed the
            // stale "MANDATORY STOP COMPLETE" from stop 1 instead of the
            // actually-queued state - checked first here instead, and now
            // distinguishes an auto-scheduled stop (the strategy plan
            // triggered it) from a manually-called one for the HUD.
            if (participant.vehicle != null && participant.vehicle.PitRequested)
            {
                return participant.pitAutoTriggered
                    ? "AUTO-PIT QUEUED  " + participant.requestedPitCompound
                    : "PIT REQUEST QUEUED";
            }

            // VSC/SC interactive pit-window offer: makes the radio call's "press P"
            // instruction visible on the HUD itself too, not just in the radio
            // message text, while the offer is still open for this participant.
            if (participant.isPlayer && playerHasActiveRaceControlPitOffer)
            {
                string offerLabel = playerRaceControlPitOfferType == RaceControlPitOfferType.SafetyCar ? "SC" : "VSC";
                return offerLabel + " PIT WINDOW OPEN  PRESS P TO BOX";
            }

            // Very short arcade races are exempt from the two-compound rule (see
            // PenaltyRules.TwoCompoundMinimumRaceLaps).
            if (RaceLaps < F1Game.Race.Rules.PenaltyRules.TwoCompoundMinimumRaceLaps)
            {
                return "";
            }

            // Reports the REAL rule: two different dry compounds, not "have you
            // stopped". A wet race voids it entirely.
            if (RaceDeclaredWet)
            {
                return "";
            }

            int distinctDry;
            bool usedWet;
            participant.CountDryCompoundsUsed(out distinctDry, out usedWet);
            if (usedWet)
            {
                return "";
            }

            if (distinctDry >= 2)
            {
                return NextPlannedPitLapFor(participant) > 0
                    ? "TYRE RULE MET  PLANNED STOP PENDING"
                    : "TYRE RULE MET";
            }

            return "TWO COMPOUNDS REQUIRED";
        }

        public float PitStopProgress01(RaceParticipant participant)
        {
            if (participant == null)
            {
                return 0f;
            }

            if (participant.pitPhase == PitPhase.Entry)
            {
                return 0.12f;
            }

            if (participant.pitPhase == PitPhase.Release || participant.pitLimiterUntilExit)
            {
                return 1f;
            }

            if (participant.pitPhase != PitPhase.Service || participant.pitServiceDuration <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(1f - participant.pitTimer / participant.pitServiceDuration);
        }

    }
}
