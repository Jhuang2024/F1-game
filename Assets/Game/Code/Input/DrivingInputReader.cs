using UnityEngine.InputSystem;

namespace F1Game.Input
{
    /// <summary>
    /// Samples the Driving action map and exposes device-agnostic driving inputs
    /// with the shared axis shaping (dead zone / saturation / sensitivity /
    /// inversion) applied. The legacy PlayerVehicleInput consumes this through a
    /// thin Assembly-CSharp bridge so the Input System becomes the live driving
    /// path without duplicating the race-manager wiring.
    ///
    /// Steering/throttle/brake are returned as combined "targets"; the caller
    /// keeps its own smoothing (rate-limited MoveTowards) so feel is unchanged.
    /// </summary>
    public sealed class DrivingInputReader
    {
        readonly InputAction steer;
        readonly InputAction throttle;
        readonly InputAction brake;
        readonly InputAction shiftUp;
        readonly InputAction shiftDown;
        readonly InputAction drs;
        readonly InputAction ersMode;
        readonly InputAction ersDeploy;
        readonly InputAction pit;
        readonly InputAction pitCancel;
        readonly InputAction camera;
        readonly InputAction lookBack;
        readonly InputAction pause;

        public bool IsValid { get; }

        public DrivingInputReader(InputService service)
        {
            InputActionMap map = service?.Driving;
            if (map == null)
            {
                IsValid = false;
                return;
            }

            steer = map.FindAction("Steer");
            throttle = map.FindAction("Throttle");
            brake = map.FindAction("Brake");
            shiftUp = map.FindAction("ShiftUp");
            shiftDown = map.FindAction("ShiftDown");
            drs = map.FindAction("DRS");
            ersMode = map.FindAction("ErsMode");
            ersDeploy = map.FindAction("ErsDeploy");
            pit = map.FindAction("PitRequest");
            pitCancel = map.FindAction("PitCancel");
            camera = map.FindAction("CycleCamera");
            lookBack = map.FindAction("LookBack");
            pause = map.FindAction("Pause");

            IsValid = steer != null && throttle != null && brake != null;
        }

        // --- Analog targets (shaped) ---

        public float TargetSteer(in InputCurves.AxisTuning tuning) =>
            InputCurves.Shape(Read(steer), tuning);

        public float TargetThrottle(in InputCurves.AxisTuning tuning) =>
            InputCurves.Shape(Read(throttle), tuning);

        public float TargetBrake(in InputCurves.AxisTuning tuning) =>
            InputCurves.Shape(Read(brake), tuning);

        // --- Buttons ---

        public bool ShiftUpPressed => Pressed(shiftUp);
        public bool ShiftDownPressed => Pressed(shiftDown);
        public bool DrsPressed => Pressed(drs);
        public bool ErsModeHeld => Held(ersMode);
        public bool ErsModeReleased => Released(ersMode);
        public bool ErsDeployHeld => Held(ersDeploy);
        public bool PitPressed => Pressed(pit);
        public bool PitCancelPressed => Pressed(pitCancel);
        public bool CameraPressed => Pressed(camera);
        public bool LookBackHeld => Held(lookBack);
        public bool PausePressed => Pressed(pause);

        static float Read(InputAction action) => action != null ? action.ReadValue<float>() : 0f;
        static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();
        static bool Released(InputAction action) => action != null && action.WasReleasedThisFrame();
        static bool Held(InputAction action) => action != null && action.IsPressed();
    }
}
