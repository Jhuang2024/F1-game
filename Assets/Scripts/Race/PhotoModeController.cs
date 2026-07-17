using System;
using System.IO;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Unity-facing photo mode: freezes the simulation (time scale 0), hides the HUD,
    /// lets the player free-fly the output camera and adjust field-of-view, and
    /// captures a screenshot to the persistent data path. Entering/leaving restores
    /// the exact prior time scale and HUD state, so it is a non-destructive overlay
    /// on a paused live session or a replay - it never mutates race state.
    ///
    /// Default-off (<c>f1game_photo_mode</c>) and inert until Configure(); Enter()/
    /// Exit() are explicit. HUD visibility is delegated to the host via a callback so
    /// this component owns no UI.
    /// VISUAL VALIDATION PENDING (needs an in-editor photo-mode run).
    /// </summary>
    public sealed class PhotoModeController : MonoBehaviour
    {
        public const string EnabledKey = "f1game_photo_mode";

        public static bool FeatureEnabled => PlayerPrefs.GetInt(EnabledKey, 0) == 1;

        public static void SetFeatureEnabled(bool value)
        {
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        [SerializeField] float flySpeed = 24f;
        [SerializeField] float flyBoostMultiplier = 3f;
        [SerializeField] float lookSensitivity = 2.5f;
        [SerializeField] float minFov = 18f;
        [SerializeField] float maxFov = 82f;
        [SerializeField] float fovSensitivity = 40f;

        Camera outputCamera;
        Action<bool> onHudVisibility;
        bool active;

        // Saved so Enter()/Exit() are exactly reversible.
        float savedTimeScale = 1f;
        float savedFov;
        Vector3 flyPosition;
        float yaw;
        float pitch;

        public bool IsActive => active;

        /// <summary>Bind the camera to fly and a HUD-visibility sink (photo mode hides the HUD while active).</summary>
        public void Configure(Camera camera, Action<bool> hudVisibilitySink)
        {
            outputCamera = camera;
            onHudVisibility = hudVisibilitySink;
        }

        /// <summary>Freeze the sim, hide the HUD and take control of the camera.</summary>
        public void Enter()
        {
            if (active || outputCamera == null)
            {
                return;
            }

            active = true;
            savedTimeScale = Time.timeScale;
            savedFov = outputCamera.fieldOfView;
            Time.timeScale = 0f;
            onHudVisibility?.Invoke(false);

            Transform cam = outputCamera.transform;
            flyPosition = cam.position;
            Vector3 e = cam.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        /// <summary>Restore the prior time scale, FOV and HUD, and release the camera.</summary>
        public void Exit()
        {
            if (!active)
            {
                return;
            }

            active = false;
            Time.timeScale = savedTimeScale;
            if (outputCamera != null)
            {
                outputCamera.fieldOfView = savedFov;
            }

            onHudVisibility?.Invoke(true);
        }

        public void Toggle()
        {
            if (active)
            {
                Exit();
            }
            else
            {
                Enter();
            }
        }

        void Update()
        {
            if (!active || outputCamera == null)
            {
                return;
            }

            // Mouse-look (right button), WASD/RF fly, wheel to zoom the lens.
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * lookSensitivity;
                pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
                pitch = Mathf.Clamp(pitch, -89f, 89f);
            }

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

            // Time is frozen, so drive motion off unscaled delta.
            float dt = Time.unscaledDeltaTime;
            float speed = flySpeed * (Input.GetKey(KeyCode.LeftShift) ? flyBoostMultiplier : 1f);
            Vector3 move = new Vector3(Input.GetAxis("Horizontal"), 0f, Input.GetAxis("Vertical"));
            if (Input.GetKey(KeyCode.R))
            {
                move += Vector3.up;
            }

            if (Input.GetKey(KeyCode.F))
            {
                move += Vector3.down;
            }

            flyPosition += rot * move * speed * dt;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                outputCamera.fieldOfView = Mathf.Clamp(
                    outputCamera.fieldOfView - scroll * fovSensitivity, minFov, maxFov);
            }

            outputCamera.transform.SetPositionAndRotation(flyPosition, rot);

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.F12))
            {
                Capture();
            }
        }

        void Capture()
        {
            string dir = Path.Combine(Application.persistentDataPath, "Screenshots");
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                // If the directory can't be made, fall back to the persistent root.
                dir = Application.persistentDataPath;
            }

            // Frame count gives a stable, monotonically increasing name without needing
            // wall-clock time (which the engine-free layers deliberately avoid).
            string path = Path.Combine(dir, "photo_" + Time.frameCount + ".png");
            // superSize 2: render the capture at twice the backbuffer resolution
            // (4x the pixels), so saved photos are genuinely high-resolution
            // rather than capped at whatever the current window size is.
            ScreenCapture.CaptureScreenshot(path, 2);
        }
    }
}
