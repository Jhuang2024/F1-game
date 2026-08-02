using UnityEngine;

namespace LocalFormulaRacing
{
    [RequireComponent(typeof(VehicleController))]
    public class PlayerVehicleInput : MonoBehaviour
    {
        public RaceManager raceManager;
        public CameraRig cameraRig;
        public RaceParticipant participant;

        VehicleController vehicle;
        IDriveInputSource input;
        F1Game.Input.GamepadRumbleFeedback rumble;
        float steerValue;
        float throttleValue;
        float brakeValue;
        // Auto-DRS latch state - now driven automatically (see below), not by a
        // button press. Kept because several session/pit/pace-limit branches
        // force it false to shut the wing.
        bool drsLatched;
        // Auto-DRS hold-open grace: DRS deploys itself and this keeps the wing
        // open well past the end of a zone (per request - DRS should last ~10s
        // longer than it used to). In practice the wing now stays open from the
        // zone until the driver next brakes for a corner, rather than snapping
        // shut at the zone boundary. Refreshed every frame DRS is available, then
        // counts down once it isn't; braking still closes it immediately.
        float drsAutoHoldTimer;
        // Single source of truth, shared with the AI path (AiVehicleController)
        // so the hold-open grace is symmetric.
        const float DrsAutoHoldSeconds = VehicleController.DrsAutoHoldSeconds;
        // Tracks the reset key between frames so a single PRESS (the false->true
        // edge) fires the recovery exactly once - no hold required.
        bool resetKeyWasHeld;
        float lastDamagePercent = -1f;

        void Awake()
        {
            vehicle = GetComponent<VehicleController>();
            // Driving input backend chosen once here (Input System when enabled
            // and its action asset resolves, legacy Input otherwise). All the
            // race-manager wiring below is unchanged; only the raw reads route
            // through the source, so the two backends never poll in parallel.
            input = DriveInputConfig.CreateSource();

            // Connect the real gamepad-vibration adapter (replaces the permanent
            // no-op stub). Wheel-class force feedback still falls back to no-op
            // until wheel hardware APIs are wired.
            rumble = new F1Game.Input.GamepadRumbleFeedback();
            F1Game.Input.ForceFeedbackHub.Active = rumble;
        }

        void Update()
        {
            if (raceManager == null || raceManager.IsRaceFinished)
            {
                return;
            }

            GameSettingsData settings = raceManager.Settings != null ? raceManager.Settings.Current : null;
            input.Tick(settings);

            if (input.PausePressed)
            {
                raceManager.TogglePause();
            }

            if (input.CameraPressed && cameraRig != null)
            {
                cameraRig.NextMode();
            }

            if (cameraRig != null)
            {
                cameraRig.SetLookBack(input.LookBackHeld);
            }

            // Track test: cycle to the next calendar circuit while in a time trial.
            if (input.CycleTrackPressed && raceManager.IsTimeTrial)
            {
                raceManager.CycleToNextTrack();
                return;
            }

            if (input.ToggleVerbosePressed)
            {
                GameLog.Verbose = !GameLog.Verbose;
                Debug.Log("[GameLog] Verbose logging " + (GameLog.Verbose ? "enabled" : "disabled"));
            }

            // Mandatory extra feature: developer track-boundary overlay toggle -
            // draws the calculated track edge, barrier inner-face target line,
            // pit lane boundary, and pit/track separator line directly (see
            // TrackManager.BuildBoundaryDebugOverlay), so a gap between a
            // barrier and its intended line is obvious at a glance.
            if (input.ToggleOverlayPressed && raceManager.Track != null)
            {
                bool nowVisible = raceManager.Track.ToggleBoundaryDebugOverlay();
                Debug.Log("[Debug] Track boundary overlay " + (nowVisible ? "shown" : "hidden"));
            }

            if (settings == null)
            {
                return;
            }

            // R: a single press instantly recovers a stuck car - no hold needed
            // (per request). Fires once on the press edge (false -> true).
            if (input.ResetHeld && !resetKeyWasHeld)
            {
                raceManager.ResetPlayerToSafePose(participant);
            }

            resetKeyWasHeld = input.ResetHeld;

            // ERS strategy cycle moved off R (now a dedicated one-press reset)
            // onto the DRS button, which is free because DRS deploys itself now.
            if (input.DrsPressed)
            {
                settings.ersMode = (settings.ersMode + 1) % 3;
            }

            VehicleCommand command = new VehicleCommand();
            // Raw targets (keyboard digital + analog, deadzoned/shaped) come from
            // the active input source; this component keeps its own smoothing.
            float targetThrottle = input.TargetThrottle;
            float targetBrake = input.TargetBrake;
            float targetSteer = input.TargetSteer;

            // Return-to-center is faster than steering in, which keeps keyboard input
            // responsive without making the car twitchy at speed.
            float steerRate = Mathf.Lerp(6f, 13f, settings.steeringSensitivity);
            bool returningToCenter = Mathf.Abs(targetSteer) < Mathf.Abs(steerValue) || Mathf.Sign(targetSteer) != Mathf.Sign(steerValue);
            steerValue = Mathf.MoveTowards(steerValue, targetSteer, Time.deltaTime * steerRate * (returningToCenter ? 1.7f : 1f));
            throttleValue = Mathf.MoveTowards(throttleValue, targetThrottle, Time.deltaTime * Mathf.Lerp(4f, 11f, settings.throttleSensitivity));
            brakeValue = Mathf.MoveTowards(brakeValue, targetBrake, Time.deltaTime * Mathf.Lerp(8f, 18f, settings.brakeSensitivity));

            command.throttle = Mathf.Clamp01(throttleValue);
            command.brake = Mathf.Clamp01(brakeValue);
            command.steer = Mathf.Clamp(steerValue, -1f, 1f);
            // Auto-DRS (per request): the wing deploys itself the instant DRS is
            // available and the driver isn't braking, and retracts on its own when
            // DRS is no longer available - no button press needed. A short
            // hold-open grace (drsAutoHoldTimer) keeps it deployed a beat past the
            // end of a zone, so DRS lasts a little longer than the strict zone
            // boundary instead of snapping shut the instant the zone ends. Braking
            // still closes it immediately (here and in VehicleController), and
            // race control force-closes it while pace-limited regardless.
            bool drsAvailableNow = raceManager.IsDrsAvailable(participant);
            if (drsAvailableNow)
            {
                drsAutoHoldTimer = DrsAutoHoldSeconds;
            }
            else
            {
                drsAutoHoldTimer = Mathf.Max(0f, drsAutoHoldTimer - Time.deltaTime);
            }

            drsLatched = command.brake <= 0.2f && (drsAvailableNow || drsAutoHoldTimer > 0f);

            command.ers = input.ErsDeployHeld;
            command.drs = drsLatched;
            // Never latch a pit request while a stop is already under way.
            // VehicleController.SetCommand turns command.pitRequest into the
            // persistent PitRequested flag with no pit-state guard of its own, and
            // OpenPlayerPitTyreSelector's own isPitting bail-out happens too late to
            // stop it - so a P press any time between the tyres going on and the
            // physics handoff (the whole release wait plus the exit leg) survived the
            // stop. On the next tick the car read as pit-bound again on brand-new
            // tyres, and because activePitRequestSource was never set to Manual,
            // PitRequestRules.CanCancel returned false: the HUD showed "PIT REQUEST
            // QUEUED" with a cancel button that did nothing, and the player was
            // either dragged into a pointless second stop or told "pit entry missed"
            // every lap for the rest of the race.
            command.pitRequest = input.PitPressed &&
                                 participant != null &&
                                 !participant.isPitting &&
                                 participant.pitPhase == PitPhase.None;
            if (command.pitRequest)
            {
                // VSC/SC interactive pit-window offer: while the radio's offer is
                // active, P means "accept the opportunistic stop and box now",
                // not the normal manual pit-request toggle - see
                // RaceManager.AcceptRaceControlPitOffer.
                if (raceManager.HasActiveRaceControlPitOfferForPlayer)
                {
                    raceManager.AcceptRaceControlPitOffer();
                }
                else
                {
                    raceManager.OpenPlayerPitTyreSelector(participant);
                }
            }

            // Cancellable-manual-pit-stop fix: dedicated, previously-unbound key
            // (adjacent to P, never used elsewhere in this file) so cancelling a
            // manual request is never confused with queuing/accepting one on the
            // same key. Routed through the exact same validation
            // (CanCancelManualPitRequest) the HUD cancel button itself calls, so
            // keyboard and mouse behave identically - CancelManualPitRequest
            // re-validates internally regardless, but checking here too avoids
            // even attempting the call (and its GameLog line) on a no-op press.
            if (input.PitCancelPressed && raceManager.CanCancelManualPitRequest())
            {
                raceManager.CancelManualPitRequest();
            }

            TyreCompound? tyreSelect = input.TyreSelectPressed;
            if (tyreSelect.HasValue)
            {
                raceManager.SelectPlayerPitTyre(participant, tyreSelect.Value);
            }

            command.shiftDown = input.ShiftDownPressed;
            command.shiftUp = input.ShiftUpPressed;
            if (!raceManager.CanDrive)
            {
                if (command.throttle > 0.55f)
                {
                    raceManager.ReportJumpStartIntent(participant);
                }

                command.throttle = 0f;
                command.brake = 1f;
                command.ers = false;
                command.drs = false;
                drsLatched = false;
            }
            else if (command.throttle > 0.12f)
            {
                raceManager.RecordPlayerLaunchInput(participant, command.throttle);
            }

            // Full safety car convoy autopilot: race control drives the car
            // directly for the duration of the full SC period - the player's
            // raw steer/throttle/brake input above is discarded for this frame,
            // but the pit-request input already latched into `command` still
            // passes through so the player can still box under safety car.
            if (participant != null && participant.isRaceControlAutopilot)
            {
                bool pitRequest = command.pitRequest;
                command = raceManager.BuildRaceControlAutopilotCommand(participant);
                command.pitRequest = pitRequest;
                drsLatched = false;
            }
            else if (raceManager.ShouldAssistPlayerPitEntry(participant))
            {
                // Pre-race pit lap fix: a scheduled strategy-plan stop takes over
                // steering/throttle/brake for the short pit-approach window so it
                // physically enters on the lap it was planned for, instead of
                // relying on the player to spot and react to the "Box this lap"
                // call in time. Only ever engages for a PreRacePlan request (see
                // RaceManager.ShouldAssistPlayerPitEntry) - a manual P-key request
                // never takes this branch and stays fully manual. The pit-request
                // input above still passes through unchanged.
                bool pitRequest = command.pitRequest;
                command = raceManager.BuildPitEntryAssistCommand(participant, command);
                command.pitRequest = pitRequest;
                drsLatched = false;
            }
            else
            {
                // Race control pace parity (Task 2/3): AI has been VSC/SC pace-clamped
                // for several passes, the player never was. This shapes throttle/brake
                // toward the current cap and force-disables ERS/DRS while pace-limited,
                // so holding Shift or a latched DRS press can never bypass race control.
                command = raceManager.ApplyPlayerRaceControlLimiter(participant, command, Mathf.Abs(vehicle.CurrentSpeedKph));
            }

            if (!command.drs)
            {
                drsLatched = false;
            }

            vehicle.SetCommand(command);

            // Force-feedback strength tracks the user's vibration setting.
            if (rumble != null)
            {
                rumble.VibrationScale = Mathf.Clamp01(settings.controllerVibration);
            }

            bool onKerbFast = vehicle.IsOnKerb && Mathf.Abs(vehicle.CurrentSpeedKph) > 70f;

            // Kerb vibration: a tiny camera impulse sells the rumble without nausea,
            // plus a continuous high-frequency rumble term on the pad.
            if (onKerbFast && cameraRig != null)
            {
                cameraRig.AddImpulseShake(0.018f);
            }

            if (rumble != null)
            {
                rumble.SetTextureRumble(onKerbFast ? 0.35f : 0f, 60f);
            }

            // Impact hit: a short, proportional jolt when new damage lands.
            if (vehicle.Damage != null)
            {
                float damagePercent = vehicle.Damage.OverallPercent;
                if (lastDamagePercent >= 0f && damagePercent > lastDamagePercent + 0.05f)
                {
                    float hit = Mathf.Clamp((damagePercent - lastDamagePercent) * 0.01f, 0.02f, 0.12f);
                    if (cameraRig != null)
                    {
                        cameraRig.AddImpulseShake(hit);
                    }

                    rumble?.PulseImpulse(Mathf.Clamp01(hit * 6f), 0.22f);
                }

                lastDamagePercent = damagePercent;
            }

            rumble?.Tick();
        }

        void OnDisable()
        {
            rumble?.StopAll();
        }

    }
}
