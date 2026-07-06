using UnityEngine;

namespace LocalFormulaRacing
{
    public class CameraRig : MonoBehaviour
    {
        public Transform target;
        public bool cameraShake = true;

        Camera followCamera;
        Rigidbody targetBody;
        int mode;

        readonly Vector3[] offsets =
        {
            new Vector3(0f, 5.8f, -17.5f),
            new Vector3(0f, 2.25f, 2.25f),
            new Vector3(0f, 28f, -12f)
        };

        public void Initialize(Transform followTarget, bool shake)
        {
            target = followTarget;
            targetBody = target == null ? null : target.GetComponent<Rigidbody>();
            cameraShake = shake;
            followCamera = GetComponentInChildren<Camera>();
            if (followCamera == null)
            {
                GameObject cameraObject = new GameObject("Race camera");
                cameraObject.transform.SetParent(transform);
                followCamera = cameraObject.AddComponent<Camera>();
            }

            followCamera.fieldOfView = 60f;
            followCamera.nearClipPlane = 0.12f;
            followCamera.farClipPlane = 1200f;
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
            if (followCamera != null)
            {
                followCamera.fieldOfView = mode == 1 ? 68f : (mode == 2 ? 54f : 60f);
            }
        }

        void LateUpdate()
        {
            if (target == null || followCamera == null)
            {
                return;
            }

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
                desiredRotation = Quaternion.LookRotation(target.position + Vector3.up * 1.1f - desired, Vector3.up);
            }

            if (cameraShake && mode != 2 && targetBody != null)
            {
                float speedShake = Mathf.Clamp01(targetBody.velocity.magnitude / 92f);
                if (speedShake > 0.08f)
                {
                    desired += Random.insideUnitSphere * speedShake * 0.028f;
                }
            }

            transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * (mode == 2 ? 3.5f : 4.8f));
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, Time.deltaTime * 5.4f);
            if (targetBody != null)
            {
                float speed01 = Mathf.Clamp01(targetBody.velocity.magnitude / 88f);
                float targetFov = mode == 1 ? 68f : (mode == 2 ? 54f : Mathf.Lerp(58f, 67f, speed01));
                followCamera.fieldOfView = Mathf.Lerp(followCamera.fieldOfView, targetFov, Time.deltaTime * 2.2f);
            }

            followCamera.transform.localPosition = Vector3.zero;
            followCamera.transform.localRotation = Quaternion.identity;
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
