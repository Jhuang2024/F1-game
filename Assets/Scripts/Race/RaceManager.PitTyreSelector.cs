using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager player pit-tyre-selector subsystem (partial). Opens the in-race
    /// compound picker for the player's next stop and applies the chosen compound
    /// (OpenPlayerPitTyreSelector / SelectPlayerPitTyre). Split out of the
    /// RaceManager monolith verbatim - same class, same members, identical gating
    /// and selection behaviour; the public entry points stay public so UI/input
    /// callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        public void OpenPlayerPitTyreSelector(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer || IsTimeTrial || CurrentSession == RaceWeekendSession.Qualifying || participant.vehicle == null || participant.isPitting)
            {
                return;
            }

            participant.pitTyreSelectionActive = true;
            participant.requestedPitCompound = participant.requestedPitCompoundSet ? participant.requestedPitCompound : NextPitCompound(participant);
            participant.requestedPitCompoundSet = true;
            // Explicit manual call (the P key) always overrides/replaces
            // whatever the strategy plan would have done - see
            // UpdatePlayerAutoPitStrategy, which never fires once
            // vehicle.PitRequested is already latched true from here.
            participant.pitAutoTriggered = false;
            // Cancellable-manual-pit-stop fix: this is the one place the plain
            // manual (P key) request is created. Tagging it here - and nowhere
            // else - is what lets CanCancelManualPitRequest tell a manual
            // override apart from the pre-race plan's own auto-trigger, without
            // touching NextPlannedPitLapFor/GetPlannedPitLapForStop at all.
            participant.activePitRequestSource = PitRequestSource.Manual;
            participant.manualPitRequested = true;
            participant.manualPitCommitted = false;
            GameEvents.Publish(new PitRequestChangedEvent(participant.driverId, PitRequestState.Requested, -1));
            SessionMessage = "Pit request: choose tyre 1-5";
            PostEngineerMessage("Pit request received. Select tyres: 1 Soft, 2 Medium, 3 Hard, 4 Intermediate, 5 Wet.", true, RaceAudioCue.PitCall);
        }

        public void SelectPlayerPitTyre(RaceParticipant participant, TyreCompound compound)
        {
            if (participant == null || !participant.isPlayer || CurrentSession == RaceWeekendSession.Qualifying)
            {
                return;
            }

            participant.requestedPitCompound = compound;
            participant.requestedPitCompoundSet = true;
            participant.pitTyreSelectionActive = participant.vehicle != null && participant.vehicle.PitRequested && !participant.isPitting;
            SessionMessage = "Pit tyre selected: " + compound;
            PostEngineerMessage("Pit tyres selected: " + compound + ".", true);
        }

        // ---------- Player race-control pace parity (Task 2/3/5) ----------
        // AI has been pace-clamped under VSC/SC in AiVehicleController for several
        // passes; the player was never held to the same rule and could just drive
        // flat-out through a safety car period. These give the player the same
        // physical constraint instead of relying on penalties alone.
        public bool IsVirtualSafetyCarActive { get { return CurrentRaceControlState == RaceControlState.VirtualSafetyCar; } }

        public bool IsFullSafetyCarPeriod
        {
            get
            {
                return CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                       CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                       CurrentRaceControlState == RaceControlState.SafetyCarInThisLap;
            }
        }

        // Limiter-duration fix: the Restart state is included too - the field
        // is still under race control between the safety car peeling in and
        // the actual green flag, but the player's limiter (and the HUD pace
        // pill) used to switch off the instant the state left
        // SafetyCarInThisLap, several seconds before the flag actually ended.
        // The limiter must never end before the period it enforces does.
        public bool IsRaceControlPaceLimited
        {
            get { return FlagRules.RequiresPaceControl(GlobalRaceFlag); }
        }

    }
}
