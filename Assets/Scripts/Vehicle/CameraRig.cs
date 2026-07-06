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
        int mode;
        Vector3 velocitySmoothed;
        float rollAngle;

        // Chase, cockpit/halo, high TV, rear chase.
        readonly Vector3[] offsets =
        {
            new Vector3(0f, 5.4f, -16.5f),
            new Vector3(0f, 2.05f, 1.6f),
            new Vector3(0f, 28f, -12f),
            new Vector3(0f, 5.4f, 16.5f)
        };

        public void Initialize(Transform followTarget, bool shake)
        {
            Initialize(followTarget, shake ? 1f : 0f, 60f);
        }

        public void Initialize(Transform followTarget, float shakeAmount, float fieldOfView)
        {
            target = followTarget;
            targetBody = target == null ? null : target.GetComponent<Rigidbody>();
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
            followCamera.farClipPlane = 1400f;
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
                return baseFov + 9f + speed01 * 4f;
            }

            if (mode == 2)
            {
                return baseFov - 6f;
            }

            // Chase and rear chase widen smoothly with speed for a sense of pace.
            return Mathf.Lerp(baseFov - 2f, baseFov + 8f, speed01 * speed01);
        }

        void LateUpdate()
        {
            if (target == null || followCamera == null)
            {
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 targetVelocity = targetBody != null ? targetBody.velocity : Vector3.zero;
            velocitySmoothed = Vector3.Lerp(velocitySmoothed, targetVelocity, dt * 4f);
            float speed01 = Mathf.Clamp01(velocitySmoothed.magnitude / 88f);

            Vector3 offset = offsets[mode];
            Vector3 desired;
            Quaternion desiredRotation;
            if (mode == 2)
            {
                desired = target.position + offset;
                desiredRotation = Quaternion.Euler(68f, target.eulerAngles.y, 0f);
            }
            else
            {
                desired = target.TransformPoint(offset);

                // Look-ahead: aim slightly along the velocity so corners open up naturally.
                Vector3 lookTarget = target.position + Vector3.up * 1.05f + velocitySmoothed * (mode == 1 ? 0.06f : 0.14f);
                Vector3 lookDirection = lookTarget - desired;
                if (mode == 3)
                {
                    lookDirection = target.position + Vector3.up * 1.05f - desired;
                }

                if (lookDirection.sqrMagnitude < 0.01f)
                {
                    lookDirection = target.forward;
                }

                desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);

                // Subtle corner lean based on lateral velocity.
                float lateral = Vector3.Dot(velocitySmoothed, target.right);
                float targetRoll = Mathf.Clamp(-lateral * 0.14f, -3.2f, 3.2f) * (mode == 1 ? 1.4f : 1f);
                rollAngle = Mathf.Lerp(rollAngle, targetRoll, dt * 4f);
                desiredRotation *= Quaternion.Euler(0f, 0f, rollAngle);
            }

            if (cameraShake && mode != 2 && targetBody != null)
            {
                float speedShake = Mathf.Clamp01(targetBody.velocity.magnitude / 92f);
                if (speedShake > 0.08f)
                {
                    desired += Random.insideUnitSphere * speedShake * 0.028f * shakeStrength;
                }
            }

            float followRate = mode == 1 ? 16f : (mode == 2 ? 3.5f : 6.2f);
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followRate * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-7.5f * dt));

            followCamera.fieldOfView = Mathf.Lerp(followCamera.fieldOfView, ModeFov(speed01), dt * 2.6f);
            followCamera.transform.localPosition = Vector3.zero;
            followCamera.transform.localRotation = Quaternion.identity;
        }

        public void AddImpulseShake(float amount)
        {
            if (!cameraShake || followCamera == null)
            {
                return;
            }

            transform.position += Random.insideUnitSphere * Mathf.Clamp(amount, 0f, 0.3f) * shakeStrength;
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
