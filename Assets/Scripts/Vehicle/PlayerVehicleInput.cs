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
        float steerValue;
        float throttleValue;
        float brakeValue;
        bool drsLatched;
        float resetHoldTime;
        bool resetTriggered;
        float lastDamagePercent = -1f;

        void Awake()
        {
            vehicle = GetComponent<VehicleController>();
        }

        void Update()
        {
            if (raceManager == null || raceManager.IsRaceFinished)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                raceManager.TogglePause();
            }

            if (Input.GetKeyDown(KeyCode.C) && cameraRig != null)
            {
                cameraRig.NextMode();
            }

            // Track test: cycle to the next calendar circuit while in a time trial.
            if (Input.GetKeyDown(KeyCode.F2) && raceManager.IsTimeTrial)
            {
                raceManager.CycleToNextTrack();
                return;
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                GameLog.Verbose = !GameLog.Verbose;
                Debug.Log("[GameLog] Verbose logging " + (GameLog.Verbose ? "enabled" : "disabled"));
            }

            GameSettingsData settings = raceManager.Settings.Current;

            // R: tap cycles ERS mode, hold ~1 second recovers a stuck car.
            if (Input.GetKey(KeyCode.R))
            {
                resetHoldTime += Time.deltaTime;
                if (!resetTriggered && resetHoldTime >= 1.0f)
                {
                    resetTriggered = true;
                    raceManager.ResetPlayerToSafePose(participant);
                }
            }

            if (Input.GetKeyUp(KeyCode.R))
            {
                if (!resetTriggered && resetHoldTime < 0.45f)
                {
                    settings.ersMode = (settings.ersMode + 1) % 3;
                }

                resetHoldTime = 0f;
                resetTriggered = false;
            }

            VehicleCommand command = new VehicleCommand();
            float targetThrottle = Key(KeyCode.W) || Key(KeyCode.UpArrow) ? 1f : 0f;
            float targetBrake = Key(KeyCode.S) || Key(KeyCode.DownArrow) ? 1f : 0f;
            float targetSteer = 0f;
            if (Key(KeyCode.A) || Key(KeyCode.LeftArrow))
            {
                targetSteer -= 1f;
            }

            if (Key(KeyCode.D) || Key(KeyCode.RightArrow))
            {
                targetSteer += 1f;
            }

            float horizontalAxis = SafeAxis("Horizontal");
            float verticalAxis = SafeAxis("Vertical");
            horizontalAxis = ApplyDeadzone(horizontalAxis, settings.controllerDeadzone);
            verticalAxis = ApplyDeadzone(verticalAxis, settings.controllerDeadzone);
            if (Mathf.Abs(horizontalAxis) > Mathf.Abs(targetSteer))
            {
                targetSteer = horizontalAxis;
            }

            if (verticalAxis > 0.1f)
            {
                targetThrottle = Mathf.Max(targetThrottle, verticalAxis);
            }
            else if (verticalAxis < -0.1f)
            {
                targetBrake = Mathf.Max(targetBrake, -verticalAxis);
            }

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
            if (Input.GetKeyDown(KeyCode.Space))
            {
                drsLatched = raceManager.IsDrsAvailable(participant) && !drsLatched;
            }

            if (!raceManager.IsDrsAvailable(participant))
            {
                drsLatched = false;
            }

            command.ers = Key(KeyCode.LeftShift) || Key(KeyCode.RightShift);
            command.drs = drsLatched;
            command.pitRequest = Input.GetKeyDown(KeyCode.P);
            if (command.pitRequest)
            {
                raceManager.OpenPlayerPitTyreSelector(participant);
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                raceManager.SelectPlayerPitTyre(participant, TyreCompound.Soft);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                raceManager.SelectPlayerPitTyre(participant, TyreCompound.Medium);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                raceManager.SelectPlayerPitTyre(participant, TyreCompound.Hard);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                raceManager.SelectPlayerPitTyre(participant, TyreCompound.Intermediate);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                raceManager.SelectPlayerPitTyre(participant, TyreCompound.Wet);
            }

            command.shiftDown = Input.GetKeyDown(KeyCode.Q);
            command.shiftUp = Input.GetKeyDown(KeyCode.E);
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

            // Race control pace parity (Task 2/3): AI has been VSC/SC pace-clamped
            // for several passes, the player never was. This shapes throttle/brake
            // toward the current cap and force-disables ERS/DRS while pace-limited,
            // so holding Shift or a latched DRS press can never bypass race control.
            command = raceManager.ApplyPlayerRaceControlLimiter(participant, command, Mathf.Abs(vehicle.CurrentSpeedKph));
            if (!command.drs)
            {
                drsLatched = false;
            }

            vehicle.SetCommand(command);

            // Kerb vibration: a tiny camera impulse sells the rumble without nausea.
            if (cameraRig != null && vehicle.IsOnKerb && Mathf.Abs(vehicle.CurrentSpeedKph) > 70f)
            {
                cameraRig.AddImpulseShake(0.018f);
            }

            // Impact hit: a short, proportional jolt when new damage lands.
            if (cameraRig != null && vehicle.Damage != null)
            {
                float damagePercent = vehicle.Damage.OverallPercent;
                if (lastDamagePercent >= 0f && damagePercent > lastDamagePercent + 0.05f)
                {
                    float hit = Mathf.Clamp((damagePercent - lastDamagePercent) * 0.01f, 0.02f, 0.12f);
                    cameraRig.AddImpulseShake(hit);
                }

                lastDamagePercent = damagePercent;
            }
        }

        bool Key(KeyCode code)
        {
            return Input.GetKey(code);
        }

        float SafeAxis(string axisName)
        {
            try
            {
                return Input.GetAxis(axisName);
            }
            catch
            {
                return 0f;
            }
        }

        float ApplyDeadzone(float value, float deadzone)
        {
            if (Mathf.Abs(value) < deadzone)
            {
                return 0f;
            }

            return Mathf.Sign(value) * Mathf.InverseLerp(deadzone, 1f, Mathf.Abs(value));
        }
    }
}
