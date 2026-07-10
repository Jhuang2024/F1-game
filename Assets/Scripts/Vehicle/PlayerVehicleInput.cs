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
        bool drsLatched;
        // DRS queue fix: a Space press a frame or two before DRS actually becomes
        // legal (zone/gap) used to be silently dropped. Queue it for a short window
        // so it activates the instant DRS becomes available instead of requiring a
        // second, perfectly-timed press.
        float drsQueueTimer;
        float resetHoldTime;
        bool resetTriggered;
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

            // R: tap cycles ERS mode, hold ~1 second recovers a stuck car.
            if (input.ResetHeld)
            {
                resetHoldTime += Time.deltaTime;
                if (!resetTriggered && resetHoldTime >= 1.0f)
                {
                    resetTriggered = true;
                    raceManager.ResetPlayerToSafePose(participant);
                }
            }

            if (input.ResetReleased)
            {
                if (!resetTriggered && resetHoldTime < 0.45f)
                {
                    settings.ersMode = (settings.ersMode + 1) % 3;
                }

                resetHoldTime = 0f;
                resetTriggered = false;
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
            if (input.DrsPressed)
            {
                if (raceManager.IsDrsAvailable(participant))
                {
                    drsLatched = !drsLatched;
                }
                else
                {
                    drsQueueTimer = 0.4f;
                }
            }

            if (drsQueueTimer > 0f)
            {
                drsQueueTimer = Mathf.Max(0f, drsQueueTimer - Time.deltaTime);
                if (raceManager.IsDrsAvailable(participant))
                {
                    drsLatched = true;
                    drsQueueTimer = 0f;
                }
            }

            if (!raceManager.IsDrsAvailable(participant))
            {
                drsLatched = false;
            }

            // DRS deployment fix: braking auto-closes the wing (see
            // VehicleController.ApplyForces) and real DRS never silently
            // reopens on its own once closed mid-zone - the driver must press
            // the button again. Without this the latch stayed true through a
            // brake application and the wing would pop back open the instant
            // the brake was released, with no second press.
            if (command.brake > 0.05f)
            {
                drsLatched = false;
            }

            command.ers = input.ErsDeployHeld;
            command.drs = drsLatched;
            command.pitRequest = input.PitPressed;
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
