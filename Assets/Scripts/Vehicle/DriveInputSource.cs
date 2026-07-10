using F1Game.Input;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Device-agnostic driving inputs consumed by PlayerVehicleInput. Two
    /// implementations exist behind one feature flag: the legacy
    /// <see cref="UnityEngine.Input"/> reads (default, fallback) and the new
    /// Input System path. PlayerVehicleInput keeps ALL its race-manager wiring
    /// (pit routing, DRS legality, autopilot, limiter) and its own smoothing —
    /// only the raw reads move here, so the two input backends never poll the
    /// same action simultaneously.
    /// </summary>
    public interface IDriveInputSource
    {
        /// <summary>Poll once per frame before reading any property.</summary>
        void Tick(GameSettingsData settings);

        float TargetThrottle { get; }
        float TargetBrake { get; }
        float TargetSteer { get; }

        bool DrsPressed { get; }
        bool ErsDeployHeld { get; }
        bool ResetHeld { get; }             // hold of the ERS/reset key
        bool ResetReleased { get; }         // release edge of the ERS/reset key
        bool ShiftUpPressed { get; }
        bool ShiftDownPressed { get; }
        bool PitPressed { get; }
        bool PitCancelPressed { get; }
        bool CameraPressed { get; }
        bool LookBackHeld { get; }
        bool PausePressed { get; }

        /// <summary>Non-null when a tyre-select was pressed this frame.</summary>
        TyreCompound? TyreSelectPressed { get; }

        bool CycleTrackPressed { get; }
        bool ToggleVerbosePressed { get; }
        bool ToggleOverlayPressed { get; }

        string Backend { get; }
    }

    /// <summary>Chooses and builds the active driving input source.</summary>
    public static class DriveInputConfig
    {
        const string ToggleKey = "f1game_input_system";

        /// <summary>
        /// PlayerPrefs override: 1 = force Input System, 0 = force legacy,
        /// unset = auto (Input System when the action asset resolves).
        /// </summary>
        public static bool UseInputSystem
        {
            get
            {
                int toggle = PlayerPrefs.GetInt(ToggleKey, -1);
                if (toggle == 0) return false;
                if (toggle == 1) return true;
                return true; // auto-prefer the new path; source falls back if invalid
            }
        }

        public static void SetUseInputSystem(bool value)
        {
            PlayerPrefs.SetInt(ToggleKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static IDriveInputSource CreateSource()
        {
            if (UseInputSystem)
            {
                var source = new InputSystemDriveInputSource();
                if (source.Available)
                {
                    InputService.Instance.EnterDriving();
                    return source;
                }
            }

            return new LegacyDriveInputSource();
        }
    }

    /// <summary>Legacy UnityEngine.Input reads — the exact pre-migration behavior.</summary>
    public sealed class LegacyDriveInputSource : IDriveInputSource
    {
        float targetThrottle;
        float targetBrake;
        float targetSteer;

        public string Backend => "Legacy Input";

        public void Tick(GameSettingsData settings)
        {
            float throttle = Key(KeyCode.W) || Key(KeyCode.UpArrow) ? 1f : 0f;
            float brake = Key(KeyCode.S) || Key(KeyCode.DownArrow) ? 1f : 0f;
            float steer = 0f;
            if (Key(KeyCode.A) || Key(KeyCode.LeftArrow)) steer -= 1f;
            if (Key(KeyCode.D) || Key(KeyCode.RightArrow)) steer += 1f;

            float deadzone = settings != null ? settings.controllerDeadzone : 0.1f;
            float horizontal = ApplyDeadzone(SafeAxis("Horizontal"), deadzone);
            float vertical = ApplyDeadzone(SafeAxis("Vertical"), deadzone);
            if (Mathf.Abs(horizontal) > Mathf.Abs(steer)) steer = horizontal;
            if (vertical > 0.1f) throttle = Mathf.Max(throttle, vertical);
            else if (vertical < -0.1f) brake = Mathf.Max(brake, -vertical);

            targetThrottle = throttle;
            targetBrake = brake;
            targetSteer = steer;
        }

        public float TargetThrottle => targetThrottle;
        public float TargetBrake => targetBrake;
        public float TargetSteer => targetSteer;

        public bool DrsPressed => Input.GetKeyDown(KeyCode.Space);
        public bool ErsDeployHeld => Key(KeyCode.LeftShift) || Key(KeyCode.RightShift);
        public bool ResetHeld => Key(KeyCode.R);
        public bool ResetReleased => Input.GetKeyUp(KeyCode.R);
        public bool ShiftUpPressed => Input.GetKeyDown(KeyCode.E);
        public bool ShiftDownPressed => Input.GetKeyDown(KeyCode.Q);
        public bool PitPressed => Input.GetKeyDown(KeyCode.P);
        public bool PitCancelPressed => Input.GetKeyDown(KeyCode.O);
        public bool CameraPressed => Input.GetKeyDown(KeyCode.C);
        public bool LookBackHeld => Key(KeyCode.B);
        public bool PausePressed => Input.GetKeyDown(KeyCode.Escape);
        public bool CycleTrackPressed => Input.GetKeyDown(KeyCode.F2);
        public bool ToggleVerbosePressed => Input.GetKeyDown(KeyCode.F3);
        public bool ToggleOverlayPressed => Input.GetKeyDown(KeyCode.F9);

        public TyreCompound? TyreSelectPressed
        {
            get
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) return TyreCompound.Soft;
                if (Input.GetKeyDown(KeyCode.Alpha2)) return TyreCompound.Medium;
                if (Input.GetKeyDown(KeyCode.Alpha3)) return TyreCompound.Hard;
                if (Input.GetKeyDown(KeyCode.Alpha4)) return TyreCompound.Intermediate;
                if (Input.GetKeyDown(KeyCode.Alpha5)) return TyreCompound.Wet;
                return null;
            }
        }

        static bool Key(KeyCode code) => Input.GetKey(code);

        static float SafeAxis(string axisName)
        {
            try { return Input.GetAxis(axisName); }
            catch { return 0f; }
        }

        static float ApplyDeadzone(float value, float deadzone)
        {
            if (Mathf.Abs(value) < deadzone) return 0f;
            return Mathf.Sign(value) * Mathf.InverseLerp(deadzone, 1f, Mathf.Abs(value));
        }
    }

    /// <summary>Input System driving reads through the F1Game.Input reader.</summary>
    public sealed class InputSystemDriveInputSource : IDriveInputSource
    {
        readonly DrivingInputReader reader;
        InputCurves.AxisTuning steerTuning = InputCurves.AxisTuning.Default;
        InputCurves.AxisTuning throttleTuning = InputCurves.AxisTuning.Default;
        InputCurves.AxisTuning brakeTuning = InputCurves.AxisTuning.Default;
        float steer;
        float throttle;
        float brake;

        public bool Available => reader != null && reader.IsValid;
        public string Backend => "Input System";

        public InputSystemDriveInputSource()
        {
            reader = new DrivingInputReader(InputService.Instance);
        }

        public void Tick(GameSettingsData settings)
        {
            if (settings != null)
            {
                float dz = settings.controllerDeadzone;
                steerTuning = new InputCurves.AxisTuning { deadZone = dz, saturation = 0.98f, sensitivity = settings.steeringSensitivity, invert = false };
                throttleTuning = new InputCurves.AxisTuning { deadZone = dz * 0.5f, saturation = 0.98f, sensitivity = settings.throttleSensitivity, invert = false };
                brakeTuning = new InputCurves.AxisTuning { deadZone = dz * 0.5f, saturation = 0.98f, sensitivity = settings.brakeSensitivity, invert = false };
            }

            steer = reader.TargetSteer(steerTuning);
            throttle = reader.TargetThrottle(throttleTuning);
            brake = reader.TargetBrake(brakeTuning);
        }

        public float TargetThrottle => throttle;
        public float TargetBrake => brake;
        public float TargetSteer => steer;

        public bool DrsPressed => reader.DrsPressed;
        public bool ErsDeployHeld => reader.ErsDeployHeld;
        public bool ResetHeld => reader.ErsModeHeld;
        public bool ResetReleased => reader.ErsModeReleased;
        public bool ShiftUpPressed => reader.ShiftUpPressed;
        public bool ShiftDownPressed => reader.ShiftDownPressed;
        public bool PitPressed => reader.PitPressed;
        public bool PitCancelPressed => reader.PitCancelPressed;
        public bool CameraPressed => reader.CameraPressed;
        public bool LookBackHeld => reader.LookBackHeld;
        public bool PausePressed => reader.PausePressed;

        // Tyre selection lives on the MFD map; keyboard number keys remain a
        // convenience shortcut alongside the Input System driving path.
        public TyreCompound? TyreSelectPressed
        {
            get
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb == null) return null;
                if (kb.digit1Key.wasPressedThisFrame) return TyreCompound.Soft;
                if (kb.digit2Key.wasPressedThisFrame) return TyreCompound.Medium;
                if (kb.digit3Key.wasPressedThisFrame) return TyreCompound.Hard;
                if (kb.digit4Key.wasPressedThisFrame) return TyreCompound.Intermediate;
                if (kb.digit5Key.wasPressedThisFrame) return TyreCompound.Wet;
                return null;
            }
        }

        public bool CycleTrackPressed
        {
            get
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return kb != null && kb.f2Key.wasPressedThisFrame;
            }
        }

        public bool ToggleVerbosePressed
        {
            get
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return kb != null && kb.f3Key.wasPressedThisFrame;
            }
        }

        public bool ToggleOverlayPressed
        {
            get
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return kb != null && kb.f9Key.wasPressedThisFrame;
            }
        }
    }
}
