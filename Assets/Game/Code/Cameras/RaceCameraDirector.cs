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
    /// The legacy CameraRig attaches this as the live race camera when the
    /// Cinemachine backend is enabled (CameraRig then stops driving the camera
    /// transform and delegates cycling/impulses here). Camera shake is
    /// impulse-driven and fully tunable per profile — a feedback accent, not the
    /// speed presentation.
    /// </summary>
    public class RaceCameraDirector : MonoBehaviour
    {
        /// <summary>True while a director is the live camera path (set on Attach).</summary>
        public static bool UseCinemachine;

        public const string ProfileResourceFolder = "CameraProfiles";

        readonly List<CinemachineVirtualCamera> cameras = new List<CinemachineVirtualCamera>();
        readonly List<CameraProfile> profiles = new List<CameraProfile>();
        readonly List<CinemachineTransposer> transposers = new List<CinemachineTransposer>();
        readonly List<Vector3> baseOffsets = new List<Vector3>();

        CinemachineBrain brain;
        CinemachineImpulseSource impulseSource;
        Transform followTarget;
        System.Func<float> speed01Provider;
        int activeIndex;

        /// <summary>Global multiplier on impulse strength (camera-shake / reduced-motion setting).</summary>
        public float ShakeScale = 1f;

        Vector3 userOffset;
        bool lookBack;

        /// <summary>User camera position offset (settings), applied to chase-type views.</summary>
        public void SetUserOffset(Vector3 offset) => userOffset = offset;

        /// <summary>Look-back toggle: mirrors the chase view to frame behind the car.</summary>
        public void SetLookBack(bool active) => lookBack = active;

        public int ActiveIndex => activeIndex;

        // Collision-camera fix (per report - "the camera goes weird if a
        // collision happens"): the chase transposer follows the car's yaw, so
        // an impact spin (or the capped-but-real post-contact rotation) whips
        // the camera around the car at spin speed. The car's yaw rate is
        // tracked here, and while it exceeds a genuine-spin threshold the
        // active chase camera's yaw damping is raised hard, easing back over a
        // second once the car settles - the broadcast-camera behaviour the
        // legacy rig already had (spinRecoveryAmount), ported to Cinemachine.
        const float SpinBaseYawDamping = 0.6f;
        const float SpinMaxYawDamping = 7f;
        const float SpinYawRateThresholdDegPerSec = 140f;
        float previousTargetYaw;
        bool hasPreviousTargetYaw;
        float spinDampingBlend;

        void UpdateSpinDamping()
        {
            if (followTarget == null)
            {
                return;
            }

            float yaw = followTarget.eulerAngles.y;
            if (!hasPreviousTargetYaw)
            {
                previousTargetYaw = yaw;
                hasPreviousTargetYaw = true;
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float yawRate = Mathf.Abs(Mathf.DeltaAngle(previousTargetYaw, yaw)) / dt;
            previousTargetYaw = yaw;

            bool spinning = yawRate > SpinYawRateThresholdDegPerSec;
            spinDampingBlend = Mathf.MoveTowards(spinDampingBlend, spinning ? 1f : 0f, dt * (spinning ? 6f : 1.2f));

            CinemachineTransposer activeTransposer = activeIndex < transposers.Count ? transposers[activeIndex] : null;
            if (activeTransposer != null)
            {
                activeTransposer.m_YawDamping = Mathf.Lerp(SpinBaseYawDamping, SpinMaxYawDamping, spinDampingBlend);
            }
        }
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
                cameras.Add(BuildVirtualCamera(rigRoot, profile, car, out CinemachineTransposer transposer));
                transposers.Add(transposer);
                baseOffsets.Add(profile.followOffset);
            }

            // Default view is the FOURTH camera (per request); C cycles onward
            // from there in the existing order.
            SetActive(cameras.Count > 3 ? 3 : 0);
        }

        CinemachineVirtualCamera BuildVirtualCamera(Transform parent, CameraProfile profile, Transform car, out CinemachineTransposer chaseTransposer)
        {
            chaseTransposer = null;
            var go = new GameObject("VCam_" + profile.kind);
            go.transform.SetParent(parent, false);
            var vcam = go.AddComponent<CinemachineVirtualCamera>();
            vcam.Priority = 0;
            vcam.m_Lens.FieldOfView = profile.baseFov;

            Transform mount = car != null && !string.IsNullOrEmpty(profile.mountNode)
                ? car.Find(profile.mountNode)
                : null;

            bool hardMounted = profile.kind == CameraProfile.Kind.Cockpit ||
                               profile.kind == CameraProfile.Kind.Nose ||
                               profile.kind == CameraProfile.Kind.TCam;
            Transform anchor = mount != null
                ? mount
                : hardMounted ? CreateFallbackHardMount(car, profile.kind) : car;

            vcam.Follow = anchor;
            vcam.LookAt = car;

            if (hardMounted)
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
                // Collision-camera fix: an explicit yaw-damping base, raised
                // dynamically while the car is spinning (see LateUpdate) so an
                // impact spin doesn't whip the orbiting chase camera around
                // with the car.
                transposer.m_YawDamping = SpinBaseYawDamping;

                var composer = vcam.AddCinemachineComponent<CinemachineComposer>();
                composer.m_HorizontalDamping = profile.rotationDamping;
                composer.m_VerticalDamping = profile.rotationDamping;
                chaseTransposer = transposer;
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

        static Transform CreateFallbackHardMount(Transform car, CameraProfile.Kind kind)
        {
            if (car == null)
            {
                return null;
            }

            // The live primitive/placeholder car does not contain the authored
            // CarRigSpec camera hierarchy. Falling back to the car root put every
            // hard-locked view at ground level inside the chassis. Recreate only
            // the missing mount at the exact local position used by
            // CarPrefabBuilder; authored cars continue to use their own mounts.
            Vector3 localPosition;
            switch (kind)
            {
                case CameraProfile.Kind.TCam:
                    localPosition = new Vector3(0f, 1.05f, -0.6f);
                    break;
                case CameraProfile.Kind.Cockpit:
                    localPosition = new Vector3(0f, 0.85f, 0.6f);
                    break;
                case CameraProfile.Kind.Nose:
                    localPosition = new Vector3(0f, 0.35f, 2.7f);
                    break;
                default:
                    return car;
            }

            var fallback = new GameObject("Runtime " + kind + " camera mount").transform;
            fallback.SetParent(car, false);
            fallback.localPosition = localPosition;
            fallback.localRotation = Quaternion.identity;
            Debug.LogWarning("[Cameras] Missing authored " + kind + " mount; using runtime fallback at " + localPosition + ".");
            return fallback;
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
            if (impulseSource != null && ActiveProfile != null && ShakeScale > 0.001f)
            {
                impulseSource.GenerateImpulse(Vector3.one * (magnitude * ActiveProfile.impactImpulseScale * ShakeScale * 0.35f));
            }
        }

        /// <summary>Continuous kerb vibration hook; call per-frame while on kerb.</summary>
        public void KerbVibrationTick(float intensity01)
        {
            if (impulseSource != null && ActiveProfile != null && ShakeScale > 0.001f && intensity01 > 0.05f)
            {
                impulseSource.GenerateImpulse(Vector3.up * (intensity01 * ActiveProfile.kerbVibrationScale * ShakeScale * 0.03f));
            }
        }

        void LateUpdate()
        {
            UpdateSpinDamping();

            CameraProfile profile = ActiveProfile;
            if (profile == null || cameras.Count == 0 || speed01Provider == null)
            {
                return;
            }

            // Speed-dependent FOV on the active vcam.
            CinemachineVirtualCamera vcam = cameras[activeIndex];
            float target = profile.baseFov + profile.fovSpeedWiden * Mathf.Clamp01(speed01Provider());
            vcam.m_Lens.FieldOfView = Mathf.Lerp(vcam.m_Lens.FieldOfView, target, Time.deltaTime * profile.fovDamping);

            // User offset + look-back applied to chase-type views (hard-mounted
            // cockpit/nose/T-cam have no transposer and are left untouched).
            CinemachineTransposer transposer = activeIndex < transposers.Count ? transposers[activeIndex] : null;
            if (transposer != null)
            {
                Vector3 baseOffset = baseOffsets[activeIndex] + userOffset;
                if (lookBack)
                {
                    // Mirror the rig in front of the car and aim back at it.
                    baseOffset = new Vector3(baseOffset.x, baseOffset.y, -baseOffset.z);
                }

                transposer.m_FollowOffset = Vector3.Lerp(transposer.m_FollowOffset, baseOffset, Time.deltaTime * 8f);
            }
        }

        /// <summary>Session teardown: the director lives on the camera object, so
        /// destroying the camera cleans it up; this stops any residual impulse.</summary>
        void OnDisable()
        {
            UseCinemachine = false;
        }
    }
}
