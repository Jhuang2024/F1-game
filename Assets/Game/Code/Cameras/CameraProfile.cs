using UnityEngine;

namespace F1Game.Cameras
{
    /// <summary>
    /// Authored camera profile for one view (chase, T-cam, cockpit, trackside).
    /// Consumed by RaceCameraDirector to configure Cinemachine virtual cameras;
    /// the numbers live in assets so camera feel is tunable without code edits.
    /// </summary>
    [CreateAssetMenu(menuName = "F1 Game/Cameras/Camera Profile", fileName = "Cam_New")]
    public class CameraProfile : ScriptableObject
    {
        public enum Kind { Chase, TCam, Cockpit, Nose, Trackside }

        public Kind kind = Kind.Chase;

        [Header("Rig")]
        [Tooltip("Car mount node this camera anchors to (CarRigSpec camera mounts), ignored for Trackside.")]
        public string mountNode = "Cameras/ChaseMount";
        public Vector3 followOffset = new Vector3(0f, 1.6f, -6.8f);
        public float followDamping = 0.9f;
        public float rotationDamping = 0.35f;
        [Tooltip("0 = horizon locked to car roll, 1 = fully world-stable horizon.")]
        [Range(0f, 1f)] public float horizonStability = 0.75f;

        [Header("Field of view")]
        public float baseFov = 58f;
        [Tooltip("Extra FOV at top speed (speed-dependent widening).")]
        public float fovSpeedWiden = 9f;
        public float fovDamping = 2.5f;

        [Header("Feel")]
        [Tooltip("Multiplier on impact impulses for this view.")]
        public float impactImpulseScale = 1f;
        [Tooltip("Multiplier on kerb vibration for this view.")]
        public float kerbVibrationScale = 1f;
        [Tooltip("Collision avoidance against track geometry (Cinemachine collider).")]
        public bool cameraCollision = true;

        [Header("User adjustments (applied on top at runtime)")]
        public Vector2 userOffsetRange = new Vector2(-0.5f, 0.5f);
    }
}
