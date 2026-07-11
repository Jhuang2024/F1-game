using System.Collections.Generic;
using F1Game.Race;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Unity-facing replay camera. Drives the engine-free ReplayPlaybackController
    /// transport, repositions every car GameObject to its recorded transform at the
    /// current playhead (ReplayPlayback sampling), and chases the ReplayDirector-
    /// chosen focus car. Default-off and inert until Configure() + Activate() are
    /// called (replay mode): it never touches the live race loop or the in-car
    /// camera, so the live path is unchanged. The transport/sampling/director maths
    /// are already unit-tested; this component is the thin engine binding.
    /// VISUAL VALIDATION PENDING (needs an in-editor replay run).
    /// </summary>
    public sealed class ReplayCameraController : MonoBehaviour
    {
        /// <summary>Default-off feature switch: the replay camera path is opt-in.</summary>
        public const string EnabledKey = "f1game_replay_camera";

        public static bool FeatureEnabled => PlayerPrefs.GetInt(EnabledKey, 0) == 1;

        public static void SetFeatureEnabled(bool value)
        {
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        // Chase-pose framing (mirrors the in-car chase feel; not a gameplay value).
        [SerializeField] float followDistance = 8f;
        [SerializeField] float followHeight = 3.2f;
        [SerializeField] float followDamping = 6f;
        [SerializeField] float lookHeight = 0.8f;

        readonly ReplayPlaybackController transport = new ReplayPlaybackController();
        ReplayRecording recording;
        Transform[] carTransforms = System.Array.Empty<Transform>();
        Camera outputCamera;
        int defaultFocusCar;
        bool active;

        /// <summary>The transport a scrubber UI reads/drives (play state, playhead).</summary>
        public ReplayPlaybackController Transport => transport;
        public bool IsActive => active;
        public float NormalizedPlayhead => transport.NormalizedPlayhead;
        public bool IsPlaying => transport.IsPlaying;

        /// <summary>
        /// Bind a recording, the per-car-index Transforms to reposition, the output
        /// camera and the fallback focus car (usually the player). Resets to the
        /// recording start, paused. Safe to call with nulls (stays inert).
        /// </summary>
        public void Configure(ReplayRecording rec, IReadOnlyList<Transform> cars, Camera camera, int defaultCarIndex)
        {
            recording = rec;
            carTransforms = cars != null ? new List<Transform>(cars).ToArray() : System.Array.Empty<Transform>();
            outputCamera = camera;
            defaultFocusCar = defaultCarIndex;
            transport.SetRecording(rec);
            active = false;
        }

        /// <summary>Enter replay mode (only if a recording and camera are bound).</summary>
        public void Activate()
        {
            active = recording != null && recording.FrameCount > 0 && outputCamera != null;
            if (active)
            {
                transport.Play();
                ApplyFrame(false);
            }
        }

        /// <summary>Leave replay mode; the live camera path resumes ownership.</summary>
        public void Deactivate()
        {
            active = false;
            transport.Pause();
        }

        // --- transport passthroughs for the scrubber controls ---
        public void Play() => transport.Play();
        public void Pause() => transport.Pause();
        public void TogglePlay() => transport.TogglePlay();
        public void SetSpeed(float multiplier) => transport.SetSpeed(multiplier);

        /// <summary>Scrub to a 0-1 bar position and snap the view (no damping on a scrub).</summary>
        public void SeekNormalized(float normalized)
        {
            transport.SeekNormalized(normalized);
            if (active)
            {
                ApplyFrame(false);
            }
        }

        void LateUpdate()
        {
            if (!active)
            {
                return;
            }

            transport.Advance(Time.deltaTime);
            ApplyFrame(true);
        }

        void ApplyFrame(bool damped)
        {
            if (recording == null || outputCamera == null)
            {
                return;
            }

            float time = transport.PlayheadSeconds;

            // 1) Replay the whole field: each car index -> its recorded transform.
            int cars = Mathf.Min(recording.CarCount, carTransforms.Length);
            for (int i = 0; i < cars; i++)
            {
                Transform t = carTransforms[i];
                if (t == null)
                {
                    continue;
                }

                ReplayRecording.CarFrame f = ReplayPlayback.SampleCar(recording, time, i);
                t.SetPositionAndRotation(
                    new Vector3(f.PosX, f.PosY, f.PosZ),
                    new Quaternion(f.RotX, f.RotY, f.RotZ, f.RotW));
            }

            // 2) Chase the directed focus car.
            int focus = ReplayDirector.FocusCarIndex(recording, time, defaultFocusCar);
            if (focus < 0 || focus >= carTransforms.Length || carTransforms[focus] == null)
            {
                return;
            }

            ComputeChasePose(carTransforms[focus], out Vector3 camPos, out Quaternion camRot);
            Transform cam = outputCamera.transform;
            if (damped)
            {
                // Frame-rate-independent smoothing toward the target pose.
                float k = 1f - Mathf.Exp(-followDamping * Mathf.Max(Time.deltaTime, 0.0001f));
                cam.position = Vector3.Lerp(cam.position, camPos, k);
                cam.rotation = Quaternion.Slerp(cam.rotation, camRot, k);
            }
            else
            {
                cam.SetPositionAndRotation(camPos, camRot);
            }
        }

        void ComputeChasePose(Transform car, out Vector3 pos, out Quaternion rot)
        {
            Vector3 forward = car.forward;
            pos = car.position - forward * followDistance + Vector3.up * followHeight;
            Vector3 lookTarget = car.position + Vector3.up * lookHeight;
            Vector3 dir = lookTarget - pos;
            rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : car.rotation;
        }
    }
}
