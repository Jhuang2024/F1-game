using UnityEngine;

namespace LocalFormulaRacing
{
    // Drives the real, visible safety car object along the track's own
    // centerline via direct kinematic transform/rigidbody movement rather than
    // engine/tyre physics - it never races, it only needs to look right, block
    // the road as a real physical obstacle, and slow down convincingly for
    // corners. Time.timeScale is 0 while the race is paused, which already
    // zeroes every Time.deltaTime-scaled step below, so no separate pause guard
    // is needed here.
    public class SafetyCarController : MonoBehaviour
    {
        TrackRuntime track;
        Rigidbody body;
        Material beaconMaterial;
        Material brakeLightMaterial;
        Color beaconBaseColor;
        Color brakeLightOffColor;
        Color brakeLightOnColor;

        public float ProgressDistance { get; private set; }
        public float CurrentSpeedKph { get; private set; }
        public bool IsActive { get; private set; }
        public bool IsReturningToPits { get; private set; }

        float previousSpeedKph;
        bool despawnRequested;
        float despawnTimer;

        const float CruiseSpeedKph = 140f;
        const float MinCornerSpeedKph = 90f;
        const float PitReturnDurationSeconds = 6f;

        public void Configure(TrackRuntime trackRuntime, Renderer beaconRenderer, Renderer brakeLightRenderer)
        {
            track = trackRuntime;
            body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.detectCollisions = true;
            }

            // sharedMaterial, not .material: these materials were built fresh for
            // this one safety car instance (never shared with a player/AI car), so
            // reading sharedMaterial here and on the mirrored left/right light both
            // reference the identical Material object - using .material instead
            // would silently clone a per-renderer copy the first time it's read,
            // leaving the other side's light unable to change with it.
            if (beaconRenderer != null)
            {
                beaconMaterial = beaconRenderer.sharedMaterial;
                beaconBaseColor = beaconMaterial.GetColor("_EmissionColor");
            }

            if (brakeLightRenderer != null)
            {
                brakeLightMaterial = brakeLightRenderer.sharedMaterial;
                brakeLightOffColor = new Color(0.12f, 0.01f, 0.01f);
                brakeLightOnColor = new Color(1.4f, 0.05f, 0.03f);
            }
        }

        // Called when race control deploys the safety car - positions it ahead
        // of the current race leader (RaceManager resolves the distance) so the
        // leader immediately has to slow and queue up behind it.
        public void EnterTrack(float atDistance)
        {
            if (track == null)
            {
                return;
            }

            ProgressDistance = atDistance;
            IsActive = true;
            IsReturningToPits = false;
            despawnRequested = false;
            despawnTimer = 0f;
            CurrentSpeedKph = CruiseSpeedKph * 0.4f;
            previousSpeedKph = CurrentSpeedKph;
            gameObject.SetActive(true);
            SnapToProgress();
        }

        // Called once race control is ready to end the safety car period - the
        // car speeds up and peels away, then despawns after a short delay
        // (standing in for actually threading pit-lane geometry, which this
        // simple kinematic mover doesn't need in order to look right disappearing
        // toward the pit entrance).
        public void BeginPitReturn()
        {
            IsReturningToPits = true;
        }

        void SnapToProgress()
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            track.SampleAtDistance(ProgressDistance, out point, out forward, out right);
            transform.position = point + Vector3.up * 0.05f;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            if (body != null)
            {
                body.position = transform.position;
                body.rotation = transform.rotation;
            }
        }

        void Update()
        {
            if (!IsActive || track == null)
            {
                return;
            }

            float severity = EstimateSeverityAhead();
            float targetKph = IsReturningToPits ? CruiseSpeedKph * 1.1f : Mathf.Lerp(CruiseSpeedKph, MinCornerSpeedKph, severity);
            CurrentSpeedKph = Mathf.MoveTowards(CurrentSpeedKph, targetKph, Time.deltaTime * (targetKph < CurrentSpeedKph ? 55f : 25f));

            bool braking = CurrentSpeedKph < previousSpeedKph - 0.5f;
            previousSpeedKph = CurrentSpeedKph;
            if (brakeLightMaterial != null)
            {
                brakeLightMaterial.SetColor("_EmissionColor", braking ? brakeLightOnColor : brakeLightOffColor);
            }

            if (beaconMaterial != null)
            {
                float pulse = Mathf.PingPong(Time.time * 3.4f, 1f);
                beaconMaterial.SetColor("_EmissionColor", Color.Lerp(beaconBaseColor * 0.3f, beaconBaseColor * 2.2f, pulse));
            }

            ProgressDistance += CurrentSpeedKph / 3.6f * Time.deltaTime;
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            track.SampleAtDistance(ProgressDistance, out point, out forward, out right);
            Vector3 targetPosition = point + Vector3.up * 0.05f;
            Quaternion targetRotation = Quaternion.LookRotation(forward, Vector3.up);
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, Time.deltaTime * (CurrentSpeedKph / 3.6f + 6f));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, Time.deltaTime * 220f);
            if (body != null)
            {
                body.MovePosition(transform.position);
                body.MoveRotation(transform.rotation);
            }

            if (IsReturningToPits)
            {
                despawnRequested = true;
                despawnTimer += Time.deltaTime;
                if (despawnTimer > PitReturnDurationSeconds)
                {
                    Deactivate();
                }
            }
        }

        float EstimateSeverityAhead()
        {
            Vector3 pointA;
            Vector3 forwardA;
            Vector3 rightA;
            Vector3 pointB;
            Vector3 forwardB;
            Vector3 rightB;
            track.SampleAtDistance(ProgressDistance + 20f, out pointA, out forwardA, out rightA);
            track.SampleAtDistance(ProgressDistance + 55f, out pointB, out forwardB, out rightB);
            return Mathf.Clamp01(Vector3.Angle(forwardA, forwardB) / 34f);
        }

        void Deactivate()
        {
            IsActive = false;
            IsReturningToPits = false;
            despawnRequested = false;
            gameObject.SetActive(false);
        }
    }
}
