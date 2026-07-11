using System.Collections.Generic;
using F1Game.Race;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Unity-facing TV-broadcast camera. Each frame it asks the engine-free
    /// <see cref="ReplayDirector"/> which car to cover, then the engine-free
    /// <see cref="BroadcastCameraSelector"/> which fixed trackside camera frames that
    /// car best, cuts the output camera to that trackside position and aims it at the
    /// car with a damped look. Both decisions are unit-tested; this component is the
    /// engine binding (positions + aim + cut).
    ///
    /// Default-off (<c>f1game_broadcast_camera</c>) and inert until Configure() +
    /// Activate(): it only moves its own output camera, never the live race loop or
    /// in-car camera, so the live path is unchanged. When no trackside cameras are
    /// authored it builds a clearly-labelled placeholder ring so the pipeline is
    /// exercisable before final content exists.
    /// VISUAL VALIDATION PENDING (needs an in-editor broadcast run).
    /// </summary>
    public sealed class BroadcastCameraController : MonoBehaviour
    {
        public const string EnabledKey = "f1game_broadcast_camera";

        public static bool FeatureEnabled => PlayerPrefs.GetInt(EnabledKey, 0) == 1;

        public static void SetFeatureEnabled(bool value)
        {
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        [SerializeField] float minRange = BroadcastCameraSelector.DefaultMinRange;
        [SerializeField] float maxRange = BroadcastCameraSelector.DefaultMaxRange;
        [SerializeField] float idealRange = BroadcastCameraSelector.DefaultIdealRange;
        [SerializeField] float switchMargin = BroadcastCameraSelector.DefaultSwitchMargin;
        [SerializeField] float aimHeight = 0.7f;
        [SerializeField] float aimDamping = 8f;

        Camera outputCamera;
        Transform[] carTransforms = System.Array.Empty<Transform>();
        Transform[] cameraTransforms = System.Array.Empty<Transform>();
        readonly List<BroadcastCameraSelector.CameraPoint> cameraPoints = new List<BroadcastCameraSelector.CameraPoint>();
        ReplayRecording recording;
        int defaultFocusCar;
        int currentCamera = -1;
        bool active;

        public bool IsActive => active;
        public int CurrentCameraIndex => currentCamera;

        /// <summary>
        /// Bind the field's car Transforms, the output camera, the fallback focus car
        /// and the trackside camera rig. Pass authored <paramref name="broadcastCameras"/>
        /// (parented empties looking down the track); when null/empty a placeholder ring
        /// is generated around the field so the pipeline still runs. An optional
        /// <paramref name="markerSource"/> recording lets the director cut to incidents/
        /// overtakes; without one it simply follows the default car.
        /// </summary>
        public void Configure(IReadOnlyList<Transform> cars, Camera camera, int defaultCarIndex,
            IReadOnlyList<Transform> broadcastCameras = null, ReplayRecording markerSource = null)
        {
            carTransforms = cars != null ? new List<Transform>(cars).ToArray() : System.Array.Empty<Transform>();
            outputCamera = camera;
            defaultFocusCar = defaultCarIndex;
            recording = markerSource;
            currentCamera = -1;

            cameraPoints.Clear();
            if (broadcastCameras != null && broadcastCameras.Count > 0)
            {
                cameraTransforms = new List<Transform>(broadcastCameras).ToArray();
                for (int i = 0; i < cameraTransforms.Length; i++)
                {
                    Vector3 p = cameraTransforms[i] != null ? cameraTransforms[i].position : Vector3.zero;
                    cameraPoints.Add(new BroadcastCameraSelector.CameraPoint(p.x, p.y, p.z));
                }
            }
            else
            {
                cameraTransforms = System.Array.Empty<Transform>();
                BuildPlaceholderRing();
            }

            active = false;
        }

        public void Activate()
        {
            active = outputCamera != null && carTransforms.Length > 0 && cameraPoints.Count > 0;
        }

        public void Deactivate() => active = false;

        // Placeholder trackside rig: a ring of cameras around the field centroid so
        // the broadcast path is exercisable before authored camera positions exist.
        // Clearly identified as placeholder content (not final broadcast framing).
        void BuildPlaceholderRing()
        {
            Vector3 centroid = Vector3.zero;
            int counted = 0;
            for (int i = 0; i < carTransforms.Length; i++)
            {
                if (carTransforms[i] != null)
                {
                    centroid += carTransforms[i].position;
                    counted++;
                }
            }

            if (counted > 0)
            {
                centroid /= counted;
            }

            const int ringCount = 8;
            const float radius = 65f;
            const float height = 14f;
            for (int i = 0; i < ringCount; i++)
            {
                float a = (i / (float)ringCount) * Mathf.PI * 2f;
                cameraPoints.Add(new BroadcastCameraSelector.CameraPoint(
                    centroid.x + Mathf.Cos(a) * radius,
                    centroid.y + height,
                    centroid.z + Mathf.Sin(a) * radius));
            }
        }

        void LateUpdate()
        {
            if (!active || outputCamera == null)
            {
                return;
            }

            int focus = FocusCar();
            if (focus < 0 || focus >= carTransforms.Length || carTransforms[focus] == null)
            {
                return;
            }

            Vector3 target = carTransforms[focus].position;
            currentCamera = BroadcastCameraSelector.SelectCamera(
                cameraPoints, target.x, target.y, target.z, currentCamera,
                minRange, maxRange, idealRange, switchMargin);
            if (currentCamera < 0 || currentCamera >= cameraPoints.Count)
            {
                return;
            }

            Transform cam = outputCamera.transform;
            cam.position = CameraPosition(currentCamera);

            Vector3 lookTarget = target + Vector3.up * aimHeight;
            Vector3 dir = lookTarget - cam.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
                // Damped aim so a fresh cut settles onto the car instead of snapping.
                float k = 1f - Mathf.Exp(-aimDamping * Mathf.Max(Time.deltaTime, 0.0001f));
                cam.rotation = Quaternion.Slerp(cam.rotation, want, k);
            }
        }

        int FocusCar()
        {
            if (recording != null)
            {
                // Playhead is the recording's own clock; broadcast typically covers the
                // live leader, so use the default car's running time proxy via markers.
                return ReplayDirector.FocusCarIndex(recording, recording.EndTime, defaultFocusCar);
            }

            return defaultFocusCar;
        }

        Vector3 CameraPosition(int index)
        {
            if (index >= 0 && index < cameraTransforms.Length && cameraTransforms[index] != null)
            {
                return cameraTransforms[index].position;
            }

            BroadcastCameraSelector.CameraPoint p = cameraPoints[index];
            return new Vector3(p.X, p.Y, p.Z);
        }
    }
}
