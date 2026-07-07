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
            shakeStrength = Mathf.Clamp(shakeAmount, 0f, 1.5f);
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

                // Look-ahead: aim down the velocity plus a steering-based bias so the
                // camera opens the corner before the car turns in.
                float steerBias = targetVehicle != null ? targetVehicle.CurrentCommand.steer : 0f;
                Vector3 cornerBias = target.right * steerBias * Mathf.Lerp(1.5f, 6.5f, speed01);
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

                // Corner lean from lateral velocity sells the load transfer.
                float lateral = Vector3.Dot(velocitySmoothed, target.right);
                float targetRoll = Mathf.Clamp(-lateral * 0.16f, -3.8f, 3.8f) * (mode == 1 ? 1.4f : 1f);
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
        // kerb strikes, and collisions (via AddImpulseShake). Cruise stays steady.
        Vector3 ComputeShakeOffset(float speed01)
        {
            impulseShake = Mathf.MoveTowards(impulseShake, 0f, Time.deltaTime * 0.6f);
            if (!cameraShake || mode == 2)
            {
                return Vector3.zero;
            }

            float amount = impulseShake;
            float highSpeed = Mathf.InverseLerp(0.74f, 1f, speed01);
            amount += highSpeed * 0.03f;

            if (targetVehicle != null)
            {
                float braking = targetVehicle.EffectiveBrake;
                if (braking > 0.45f && speed01 > 0.35f)
                {
                    amount += braking * speed01 * 0.045f;
                }

                if (targetVehicle.IsOnKerb && speed01 > 0.2f)
                {
                    amount += 0.035f;
                }
            }

            if (amount <= 0.001f)
            {
                return Vector3.zero;
            }

            return Random.insideUnitSphere * amount * shakeStrength;
        }

        public void AddImpulseShake(float amount)
        {
            if (!cameraShake || followCamera == null)
            {
                return;
            }

            impulseShake = Mathf.Min(0.32f, impulseShake + Mathf.Clamp(amount, 0f, 0.3f));
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
