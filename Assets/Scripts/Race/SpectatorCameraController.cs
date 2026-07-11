using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Unity-facing free spectator camera. Two modes: orbit (yaw/pitch/zoom around a
    /// chosen car, cycling the focus car) and free-fly (WASD/mouse-look). It only
    /// moves its own output camera transform - it never touches the live race loop,
    /// the vehicles or the in-car camera, so gameplay is unaffected and it is safe to
    /// run alongside a live session or a replay.
    ///
    /// Default-off (<c>f1game_spectator_camera</c>) and inert until Configure() +
    /// Activate(). Input uses the legacy Input axes already used elsewhere in the
    /// project.
    /// VISUAL VALIDATION PENDING (needs an in-editor spectator run).
    /// </summary>
    public sealed class SpectatorCameraController : MonoBehaviour
    {
        public const string EnabledKey = "f1game_spectator_camera";

        public static bool FeatureEnabled => PlayerPrefs.GetInt(EnabledKey, 0) == 1;

        public static void SetFeatureEnabled(bool value)
        {
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        public enum Mode
        {
            Orbit,
            FreeFly,
        }

        [SerializeField] float orbitDistance = 12f;
        [SerializeField] float minOrbitDistance = 4f;
        [SerializeField] float maxOrbitDistance = 60f;
        [SerializeField] float orbitSensitivity = 180f;
        [SerializeField] float zoomSensitivity = 24f;
        [SerializeField] float flySpeed = 40f;
        [SerializeField] float flyBoostMultiplier = 3f;
        [SerializeField] float lookSensitivity = 2.5f;
        [SerializeField] float followHeight = 1.2f;

        Camera outputCamera;
        Transform[] carTransforms = System.Array.Empty<Transform>();
        int focusIndex;
        Mode mode = Mode.Orbit;
        bool active;

        float yaw;
        float pitch = 12f;
        Vector3 flyPosition;

        public bool IsActive => active;
        public Mode CurrentMode => mode;
        public int FocusIndex => focusIndex;

        /// <summary>Bind the field's car Transforms and the output camera. Starts orbiting the given car.</summary>
        public void Configure(IReadOnlyList<Transform> cars, Camera camera, int startCarIndex)
        {
            carTransforms = cars != null ? new List<Transform>(cars).ToArray() : System.Array.Empty<Transform>();
            outputCamera = camera;
            focusIndex = Mathf.Clamp(startCarIndex, 0, Mathf.Max(0, carTransforms.Length - 1));
            active = false;
        }

        public void Activate()
        {
            active = outputCamera != null;
            if (active)
            {
                flyPosition = outputCamera.transform.position;
                Vector3 e = outputCamera.transform.eulerAngles;
                yaw = e.y;
                pitch = e.x > 180f ? e.x - 360f : e.x;
            }
        }

        public void Deactivate() => active = false;

        public void SetMode(Mode value) => mode = value;

        public void ToggleMode() => mode = mode == Mode.Orbit ? Mode.FreeFly : Mode.Orbit;

        public void FocusNext() => StepFocus(1);

        public void FocusPrevious() => StepFocus(-1);

        void StepFocus(int step)
        {
            if (carTransforms.Length == 0)
            {
                return;
            }

            focusIndex = (focusIndex + step + carTransforms.Length) % carTransforms.Length;
        }

        void Update()
        {
            if (!active || outputCamera == null)
            {
                return;
            }

            // Cycle focus car and toggle mode with the keys spectators expect.
            if (Input.GetKeyDown(KeyCode.RightBracket) || Input.GetKeyDown(KeyCode.E))
            {
                FocusNext();
            }
            else if (Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.Q))
            {
                FocusPrevious();
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                ToggleMode();
            }

            if (mode == Mode.Orbit)
            {
                UpdateOrbit();
            }
            else
            {
                UpdateFreeFly();
            }
        }

        void UpdateOrbit()
        {
            Transform car = (focusIndex >= 0 && focusIndex < carTransforms.Length) ? carTransforms[focusIndex] : null;
            if (car == null)
            {
                return;
            }

            // Drag with the right mouse button to orbit; wheel to zoom.
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * orbitSensitivity * Time.unscaledDeltaTime;
                pitch -= Input.GetAxis("Mouse Y") * orbitSensitivity * Time.unscaledDeltaTime;
            }

            pitch = Mathf.Clamp(pitch, -20f, 75f);
            orbitDistance = Mathf.Clamp(
                orbitDistance - Input.GetAxis("Mouse ScrollWheel") * zoomSensitivity,
                minOrbitDistance, maxOrbitDistance);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = car.position + Vector3.up * followHeight;
            Vector3 pos = pivot - rot * Vector3.forward * orbitDistance;

            Transform cam = outputCamera.transform;
            cam.position = pos;
            cam.rotation = Quaternion.LookRotation((pivot - pos).normalized, Vector3.up);
        }

        void UpdateFreeFly()
        {
            Transform cam = outputCamera.transform;

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * lookSensitivity;
                pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
                pitch = Mathf.Clamp(pitch, -85f, 85f);
            }

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

            float speed = flySpeed * (Input.GetKey(KeyCode.LeftShift) ? flyBoostMultiplier : 1f);
            Vector3 move = Vector3.zero;
            move += new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            if (Input.GetKey(KeyCode.R))
            {
                move += Vector3.up;
            }

            if (Input.GetKey(KeyCode.F))
            {
                move += Vector3.down;
            }

            flyPosition += rot * move * speed * Time.unscaledDeltaTime;
            cam.SetPositionAndRotation(flyPosition, rot);
        }
    }
}
