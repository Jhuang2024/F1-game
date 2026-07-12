using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager manual-pit-request subsystem (partial). The single shared
    /// validation and cancellation path for a temporary manual pit override (UI
    /// button and keyboard shortcut alike), the request-origin mapping, and the
    /// state-separation clear. The pre-race planned stop is a completely separate
    /// concept and is never touched here. Split out of the RaceManager monolith
    /// verbatim - same class, same members, identical behaviour and the
    /// activePitRequestSource/manualPitRequested/manualPitCommitted clear-together
    /// contract; the public entry points stay public so UI/input callers resolve
    /// in-class.
    /// </summary>
    public partial class RaceManager
    {
        // ---------- Cancellable manual pit request ----------
        // Single shared validation for cancelling a manually-queued (P key or
        // accepted SC/VSC offer) pit stop. Every cancellation path - the HUD
        // button and the keyboard shortcut alike - must go through this exact
        // method, so there is never a difference between mouse and keyboard
        // behaviour, and the pre-race planned stop (NextPlannedPitLapFor/
        // GetPlannedPitLapForStop, driven purely by Settings.Current.plannedPitLapOne/
        // Two and pitStops) is never read, mutated, or otherwise touched by any
        // of this - it is a completely separate concept from the temporary
        // manual override this cancels.
        public bool CanCancelManualPitRequest()
        {
            RaceParticipant participant = PlayerParticipant;
            if (participant == null || participant.vehicle == null || !participant.isPlayer)
            {
                return false;
            }

            // The decision itself (source/state gates, commitment lockouts, the
            // PreRacePlan never-cancellable rule) lives in the unit-tested
            // rulebook; this method only assembles the live snapshot. The
            // limiter-line probe re-checks the SAME authoritative boundary
            // HandlePitService uses, directly against the car's current position,
            // because the cached pitPhase/pitEntryCommitted flags only update once
            // HandlePitService ticks this frame.
            bool crossedLimiterLine = false;
            if (Track != null && participant.lapTracker != null)
            {
                TrackProgress liveProgress = Track.GetProgressNear(participant.transform.position, participant.lapTracker.CurrentProgress.distance);
                crossedLimiterLine = Track.HasCrossedPitEntryLimiterLine(liveProgress);
            }

            var context = new PitRequestContext
            {
                IsQualifying = CurrentSession == RaceWeekendSession.Qualifying,
                IsTimeTrial = IsTimeTrial,
                RaceFinished = IsRaceFinished,
                ParticipantRetired = participant.retired,
                ParticipantFinished = participant.finished,
                ManualPitRequested = participant.manualPitRequested,
                ManualPitCommitted = participant.manualPitCommitted,
                Origin = MapPitRequestOrigin(participant.activePitRequestSource),
                VehiclePitRequested = participant.vehicle.PitRequested,
                InPitSequence = participant.pitPhase != PitPhase.None,
                IsPitting = participant.isPitting,
                PitEntryCommitted = participant.pitEntryCommitted,
                CrossedPitEntryLimiterLine = crossedLimiterLine,
            };

            return PitRequestRules.CanCancel(context);
        }

        static PitRequestOrigin MapPitRequestOrigin(PitRequestSource source)
        {
            switch (source)
            {
                case PitRequestSource.PreRacePlan: return PitRequestOrigin.PreRacePlan;
                case PitRequestSource.Manual: return PitRequestOrigin.Manual;
                case PitRequestSource.SafetyCarPrompt: return PitRequestOrigin.SafetyCarPrompt;
                default: return PitRequestOrigin.None;
            }
        }

        public void CancelManualPitRequest()
        {
            if (!CanCancelManualPitRequest())
            {
                return;
            }

            RaceParticipant participant = PlayerParticipant;
            participant.vehicle.ClearPitRequest();
            participant.pitTyreSelectionActive = false;
            participant.pitAutoTriggered = false;
            ClearManualPitRequestTracking(participant);
            GameEvents.Publish(new PitRequestChangedEvent(participant.driverId, PitRequestState.Cancelled, -1));

            // A cancelled manual request never touches the pre-race plan
            // (NextPlannedPitLapFor keeps reading Settings.Current.plannedPitLapOne/
            // Two + pitStops exactly as before) - UpdatePlayerAutoPitStrategy
            // simply resumes normal evaluation next tick with vehicle.PitRequested
            // false again. If the planned lap already passed while the manual
            // request was queued, ShouldPromptPlannedStop/NextPlannedPitLapFor
            // still report it due/overdue and UpdatePlayerAutoPitStrategy
            // re-requests it at the next tick - it is never silently dropped.
            SessionMessage = "Manual pit stop cancelled";
            PostEngineerMessage("Copy, staying out. Original strategy restored.", true);
            playerManualPitCancelMessageTimer = PlayerManualPitCancelMessageSeconds;

            GameLog.Info("[Pit] Player cancelled manual pit request at lap " +
                         (participant.lapTracker != null ? participant.lapTracker.CompletedLaps + 1 : 0) + ".");
        }

        // Shared reset for the three fields a cancelled/consumed manual request
        // must always clear together - see the "State separation" contract on
        // RaceParticipant (activePitRequestSource/manualPitRequested/manualPitCommitted).
        static void ClearManualPitRequestTracking(RaceParticipant participant)
        {
            participant.manualPitRequested = false;
            participant.manualPitCommitted = false;
            participant.activePitRequestSource = PitRequestSource.None;
        }

    }
}
