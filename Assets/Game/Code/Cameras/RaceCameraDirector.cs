using System.Collections.Generic;
using Cinemachine;
using UnityEngine;

namespace F1Game.Cameras
{
    /// <summary>
    /// Cinemachine-based replacement for the hand-rolled CameraRig: builds one
    /// virtual camera per authored CameraProfile (chase, T-cam, cockpit, nose,
    /// trackside), cycles between them by priority, applies speed-dependent
    /// FOV, camera collision and impulse-based impacts/kerb vibration.
    ///
    /// Integration status: constructed and compilable, but the legacy CameraRig
    /// remains the active race camera until this director is validated
    /// in-editor (flip <see cref="UseCinemachine"/> to switch). Camera shake is
    /// impulse-driven and fully tunable per profile — it is a feedback accent,
    /// not the speed presentation.
    /// </summary>
    public class RaceCameraDirector : MonoBehaviour
    {
        /// <summary>Feature gate read by the spawn path once validated in-editor.</summary>
        public static bool UseCinemachine;

        public const string ProfileResourceFolder = "CameraProfiles";

        readonly List<CinemachineVirtualCamera> cameras = new List<CinemachineVirtualCamera>();
        readonly List<CameraProfile> profiles = new List<CameraProfile>();

        CinemachineBrain brain;
        CinemachineImpulseSource impulseSource;
        Transform followTarget;
        System.Func<float> speed01Provider;
        int activeIndex;

        public int ActiveIndex => activeIndex;
        public CameraProfile ActiveProfile => profiles.Count > 0 ? profiles[Mathf.Clamp(activeIndex, 0, profiles.Count - 1)] : null;

        /// <summary>Creates the director on the main camera and builds the vcam set.</summary>
        public static RaceCameraDirector Attach(Camera outputCamera, Transform car, System.Func<float> speed01)
        {
            var director = outputCamera.gameObject.AddComponent<RaceCameraDirector>();
            director.Build(outputCamera, car, speed01);
            return director;
        }

        void Build(Camera outputCamera, Transform car, System.Func<float> speed01)
        {
            followTarget = car;
            speed01Provider = speed01;

            brain = outputCamera.GetComponent<CinemachineBrain>();
            if (brain == null)
            {
                brain = outputCamera.gameObject.AddComponent<CinemachineBrain>();
            }

            brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0f);

            CameraProfile[] loaded = Resources.LoadAll<CameraProfile>(ProfileResourceFolder);
            if (loaded == null || loaded.Length == 0)
            {
                Debug.LogWarning("[Cameras] No CameraProfile assets in Resources/" + ProfileResourceFolder + " - using built-in defaults.");
                loaded = BuildDefaultProfiles();
            }

            var rigRoot = new GameObject("Cinemachine Rigs").transform;
            rigRoot.SetParent(transform, false);

            impulseSource = rigRoot.gameObject.AddComponent<CinemachineImpulseSource>();
            impulseSource.m_ImpulseDefinition.m_ImpulseDuration = 0.28f;

            foreach (CameraProfile profile in loaded)
            {
                profiles.Add(profile);
                cameras.Add(BuildVirtualCamera(rigRoot, profile, car));
            }

            SetActive(0);
        }

        CinemachineVirtualCamera BuildVirtualCamera(Transform parent, CameraProfile profile, Transform car)
        {
            var go = new GameObject("VCam_" + profile.kind);
            go.transform.SetParent(parent, false);
            var vcam = go.AddComponent<CinemachineVirtualCamera>();
            vcam.Priority = 0;
            vcam.m_Lens.FieldOfView = profile.baseFov;

            Transform mount = car != null && !string.IsNullOrEmpty(profile.mountNode)
                ? car.Find(profile.mountNode)
                : null;
            Transform anchor = mount != null ? mount : car;

            vcam.Follow = anchor;
            vcam.LookAt = car;

            if (profile.kind == CameraProfile.Kind.Cockpit || profile.kind == CameraProfile.Kind.Nose ||
                profile.kind == CameraProfile.Kind.TCam)
            {
                // Hard-mounted views: lock to the mount, inherit car motion.
                var hardLock = vcam.AddCinemachineComponent<CinemachineHardLockToTarget>();
                hardLock.m_Damping = 0f;
                vcam.AddCinemachineComponent<CinemachineSameAsFollowTarget>();
            }
            else
            {
                var transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
                transposer.m_FollowOffset = profile.followOffset;
                transposer.m_XDamping = profile.followDamping;
                transposer.m_YDamping = profile.followDamping;
                transposer.m_ZDamping = profile.followDamping;
                // Horizon stability: world-up binding keeps the horizon level;
                // lock-to-target rolls with the car.
                transposer.m_BindingMode = profile.horizonStability > 0.5f
                    ? CinemachineTransposer.BindingMode.LockToTargetWithWorldUp
                    : CinemachineTransposer.BindingMode.LockToTarget;

                var composer = vcam.AddCinemachineComponent<CinemachineComposer>();
                composer.m_HorizontalDamping = profile.rotationDamping;
                composer.m_VerticalDamping = profile.rotationDamping;
            }

            if (profile.cameraCollision)
            {
                var collider = go.AddComponent<CinemachineCollider>();
                collider.m_AvoidObstacles = true;
                collider.m_SmoothingTime = 0.2f;
            }

            var listener = go.AddComponent<CinemachineImpulseListener>();
            listener.m_Gain = profile.impactImpulseScale;

            return vcam;
        }

        static CameraProfile[] BuildDefaultProfiles()
        {
            CameraProfile Make(CameraProfile.Kind kind, string mount, Vector3 offset, float fov, float stability)
            {
                var profile = ScriptableObject.CreateInstance<CameraProfile>();
                profile.name = "Cam_" + kind + " (runtime default)";
                profile.kind = kind;
                profile.mountNode = mount;
                profile.followOffset = offset;
                profile.baseFov = fov;
                profile.horizonStability = stability;
                return profile;
            }

            return new[]
            {
                Make(CameraProfile.Kind.Chase, "Cameras/ChaseMount", new Vector3(0f, 1.6f, -6.8f), 58f, 0.8f),
                Make(CameraProfile.Kind.TCam, "Cameras/TCamMount", Vector3.zero, 55f, 0.2f),
                Make(CameraProfile.Kind.Cockpit, "Cameras/CockpitMount", Vector3.zero, 62f, 0.1f),
                Make(CameraProfile.Kind.Nose, "Cameras/NoseMount", Vector3.zero, 60f, 0.15f),
            };
        }

        public void NextCamera()
        {
            SetActive((activeIndex + 1) % Mathf.Max(1, cameras.Count));
        }

        public void SetActive(int index)
        {
            activeIndex = Mathf.Clamp(index, 0, cameras.Count - 1);
            for (int i = 0; i < cameras.Count; i++)
            {
                cameras[i].Priority = i == activeIndex ? 10 : 0;
            }
        }

        /// <summary>Impact impulse (collision, kerb strike). Magnitude ~0..1.</summary>
        public void AddImpulse(float magnitude)
        {
            if (impulseSource != null && ActiveProfile != null)
            {
                impulseSource.GenerateImpulse(Vector3.one * (magnitude * ActiveProfile.impactImpulseScale * 0.35f));
            }
        }

        /// <summary>Continuous kerb vibration hook; call per-frame while on kerb.</summary>
        public void KerbVibrationTick(float intensity01)
        {
            if (impulseSource != null && ActiveProfile != null && intensity01 > 0.05f)
            {
                impulseSource.GenerateImpulse(Vector3.up * (intensity01 * ActiveProfile.kerbVibrationScale * 0.03f));
            }
        }

        void LateUpdate()
        {
            CameraProfile profile = ActiveProfile;
            if (profile == null || cameras.Count == 0 || speed01Provider == null)
            {
                return;
            }

            // Speed-dependent FOV on the active vcam.
            CinemachineVirtualCamera vcam = cameras[activeIndex];
            float target = profile.baseFov + profile.fovSpeedWiden * Mathf.Clamp01(speed01Provider());
            vcam.m_Lens.FieldOfView = Mathf.Lerp(vcam.m_Lens.FieldOfView, target, Time.deltaTime * profile.fovDamping);
        }
    }
}
