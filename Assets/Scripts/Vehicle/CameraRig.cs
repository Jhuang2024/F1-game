using UnityEngine;

namespace LocalFormulaRacing
{
    public class CameraRig : MonoBehaviour
    {
        public Transform target;
        public bool cameraShake = true;
        public float shakeStrength = 1f;
        public float baseFov = 60f;

        Camera followCamera;
        Rigidbody targetBody;
        VehicleController targetVehicle;
        int mode;
        Vector3 velocitySmoothed;
        float rollAngle;
        float impulseShake;
        float smoothedSteer;

        // Chase, cockpit/halo, high TV, rear chase, low nose cam.
        readonly Vector3[] offsets =
        {
            new Vector3(0f, 4.35f, -12.6f),
            new Vector3(0f, 2.02f, 1.55f),
            new Vector3(0f, 26f, -11f),
            new Vector3(0f, 4.6f, 14.5f),
            new Vector3(0f, 0.72f, 2.5f)
        };

        public void Initialize(Transform followTarget, bool shake)
        {
            Initialize(followTarget, shake ? 1f : 0f, 60f);
        }

        public void Initialize(Transform followTarget, float shakeAmount, float fieldOfView)
        {
            target = followTarget;
            targetBody = target == null ? null : target.GetComponent<Rigidbody>();
            targetVehicle = target == null ? null : target.GetComponent<VehicleController>();
            shakeStrength = Mathf.Clamp(shakeAmount, 0f, 0.6f);
            cameraShake = shakeStrength > 0.01f;
            baseFov = Mathf.Clamp(fieldOfView, 45f, 80f);
            followCamera = GetComponentInChildren<Camera>();
            if (followCamera == null)
            {
                GameObject cameraObject = new GameObject("Race camera");
                cameraObject.transform.SetParent(transform);
                followCamera = cameraObject.AddComponent<Camera>();
            }

            followCamera.fieldOfView = baseFov;
            followCamera.nearClipPlane = 0.12f;
            followCamera.farClipPlane = 2600f;
            followCamera.allowHDR = true;
            followCamera.allowMSAA = true;
            followCamera.backgroundColor = RenderSettings.fogColor;
            AudioListener listener = followCamera.GetComponent<AudioListener>();
            if (listener == null)
            {
                followCamera.gameObject.AddComponent<AudioListener>();
            }

            SnapToTarget();
        }

        public void NextMode()
        {
            mode = (mode + 1) % offsets.Length;
        }

        float ModeFov(float speed01)
        {
            if (mode == 1)
            {
                // Cockpit: a touch of tunnel widening with speed, kept gentle.
                return baseFov + 8f + speed01 * 5f;
            }

            if (mode == 2)
            {
                return baseFov - 8f;
            }

            if (mode == 4)
            {
                return baseFov + 6f + speed01 * 7f;
            }

            // Chase: a non-linear widen so the last 100 km/h really stretch the view
            // without the mid-range constantly pumping the lens.
            float curve = Mathf.Pow(speed01, 1.6f);
            return Mathf.Lerp(baseFov - 3f, baseFov + 11f, curve);
        }

        void LateUpdate()
        {
            if (target == null || followCamera == null)
            {
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 targetVelocity = targetBody != null ? targetBody.velocity : Vector3.zero;
            velocitySmoothed = Vector3.Lerp(velocitySmoothed, targetVelocity, dt * 4.5f);
            float speed01 = Mathf.Clamp01(velocitySmoothed.magnitude / 88f);

            Vector3 offset = offsets[mode];
            Vector3 desired;
            Quaternion desiredRotation;
            if (mode == 2)
            {
                // TV cam drifts alongside the racing line rather than sitting glued
                // overhead, which reads much more like a broadcast crane.
                desired = target.position + offset + velocitySmoothed * 0.22f;
                desiredRotation = Quaternion.LookRotation(target.position + velocitySmoothed * 0.55f + Vector3.up * 0.5f - desired, Vector3.up);
            }
            else
            {
                desired = target.TransformPoint(offset);

                // Steering influence is heavily smoothed and kept subtle: a light
                // hint of corner look-ahead, not a camera that whips sideways every
                // time the wheel turns.
                float rawSteer = targetVehicle != null ? targetVehicle.CurrentCommand.steer : 0f;
                smoothedSteer = Mathf.Lerp(smoothedSteer, rawSteer, 1f - Mathf.Exp(-dt * 5f));
                float cornerBiasScale = mode == 1 || mode == 4 ? Mathf.Lerp(0.12f, 0.5f, speed01) : Mathf.Lerp(0.25f, 1.4f, speed01);
                Vector3 cornerBias = target.right * smoothedSteer * cornerBiasScale;
                Vector3 lookTarget = target.position + Vector3.up * 1.05f + velocitySmoothed * (mode == 1 ? 0.07f : 0.2f) + cornerBias;
                Vector3 lookDirection = lookTarget - desired;
                if (mode == 3)
                {
                    lookDirection = target.position + Vector3.up * 1.05f - desired;
                }
                else if (mode == 4)
                {
                    // Nose cam hugs the tarmac and always looks down the road.
                    lookDirection = target.forward * 12f + velocitySmoothed * 0.3f + cornerBias * 0.5f + Vector3.up * 0.1f;
                }

                if (lookDirection.sqrMagnitude < 0.01f)
                {
                    lookDirection = target.forward;
                }

                desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);

                // Corner lean from lateral velocity sells the load transfer, kept mild.
                float lateral = Vector3.Dot(velocitySmoothed, target.right);
                float rollClamp = mode == 1 ? 1.6f : 1.2f;
                float targetRoll = Mathf.Clamp(-lateral * 0.05f, -rollClamp, rollClamp);
                rollAngle = Mathf.Lerp(rollAngle, targetRoll, dt * 4f);
                desiredRotation *= Quaternion.Euler(0f, 0f, rollAngle);
            }

            desired += ComputeShakeOffset(speed01);

            float followRate = mode == 1 || mode == 4 ? 17f : (mode == 2 ? 3.2f : 7.4f);
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followRate * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-8.2f * dt));

            followCamera.fieldOfView = Mathf.Lerp(followCamera.fieldOfView, ModeFov(speed01), dt * 3f);
            followCamera.transform.localPosition = Vector3.zero;
            followCamera.transform.localRotation = Quaternion.identity;
        }

        // Shake only when the situation earns it: very high speed, heavy braking,
        // kerb strikes, and collisions (via AddImpulseShake). Cruise stays steady,
        // and steering alone contributes nothing here.
        Vector3 ComputeShakeOffset(float speed01)
        {
            impulseShake = Mathf.MoveTowards(impulseShake, 0f, Time.deltaTime * 0.9f);
            if (!cameraShake || mode == 2 || shakeStrength <= 0.001f)
            {
                return Vector3.zero;
            }

            float amount = impulseShake;
            float highSpeed = Mathf.InverseLerp(0.86f, 1f, speed01);
            amount += highSpeed * 0.012f;

            if (targetVehicle != null)
            {
                float braking = targetVehicle.EffectiveBrake;
                if (braking > 0.55f && speed01 > 0.45f)
                {
                    amount += braking * speed01 * 0.016f;
                }

                if (targetVehicle.IsOnKerb && speed01 > 0.2f)
                {
                    amount += 0.016f;
                }
            }

            if (amount <= 0.001f)
            {
                return Vector3.zero;
            }

            // Smoothed noise instead of raw per-frame randomness, and mostly local
            // X/Y so it never introduces wild forward/back jitter.
            float t = Time.unscaledTime;
            float noiseX = Mathf.PerlinNoise(t * 11f, 0.13f) - 0.5f;
            float noiseY = Mathf.PerlinNoise(0.42f, t * 13f) - 0.5f;
            float noiseZ = (Mathf.PerlinNoise(t * 7f, t * 7f) - 0.5f) * 0.3f;
            return new Vector3(noiseX, noiseY, noiseZ) * amount * shakeStrength * 1.6f;
        }

        public void AddImpulseShake(float amount)
        {
            if (!cameraShake || followCamera == null)
            {
                return;
            }

            impulseShake = Mathf.Min(0.16f, impulseShake + Mathf.Clamp(amount, 0f, 0.15f));
        }

        void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desired = target.TransformPoint(offsets[0]);
            transform.position = desired;
            transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 1.25f - desired, Vector3.up);
        }
    }
}
