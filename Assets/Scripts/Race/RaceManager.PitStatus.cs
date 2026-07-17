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
                // Player entry runs the raised 105 kph pace (player pit-entry
                // buff); AI keep 80 - this text is only ever shown for the
                // player's own HUD.
                return "PIT LANE  TO BOX " + (participant.pitBoxIndex + 1) + "  LIMITER 105";
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
                return "PIT RELEASE  LIMITER 80";
            }

            if (participant.pitPhase == PitPhase.ExitMerge)
            {
                return "PIT EXIT  MERGING";
            }

            if (participant.pitLimiterUntilExit)
            {
                return "PIT EXIT  LIMITER 80";
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

            // Sprint races carry no mandatory stop (see
            // PenaltyRules.MandatoryPitMinimumRaceLaps) - showing "MANDATORY
            // STOP REQUIRED" on one would demand a stop the rulebook never
            // enforces.
            if (RaceLaps < F1Game.Race.Rules.PenaltyRules.MandatoryPitMinimumRaceLaps)
            {
                return "";
            }

            if (participant.pitStops > 0 && NextPlannedPitLapFor(participant) <= 0)
            {
                return "MANDATORY STOP COMPLETE";
            }

            return "MANDATORY STOP REQUIRED";
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
