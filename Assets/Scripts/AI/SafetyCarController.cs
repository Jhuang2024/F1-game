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
        float despawnTimer;
        // Distance from the safety car back to the race leader, fed by
        // RaceManager each tick during a full SC period - the car waits (slow
        // pickup pace) while the leader is far behind and releases to full
        // cruise as the queue forms up.
        float leaderGapMeters;

        const float CruiseSpeedKph = 140f;
        const float MinCornerSpeedKph = 90f;
        const float PickupSpeedKph = 70f;
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

            ProgressDistance = track.WrapDistance(atDistance);
            IsActive = true;
            IsReturningToPits = false;
            despawnTimer = 0f;
            leaderGapMeters = 999f;
            CurrentSpeedKph = PickupSpeedKph;
            previousSpeedKph = CurrentSpeedKph;
            gameObject.SetActive(true);
            ForceRenderersVisible();
            SnapToProgress();
        }

        public void SetLeaderGapMeters(float gapMeters)
        {
            leaderGapMeters = gapMeters;
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

        // Guards against any child renderer having been disabled (or the whole
        // object left inactive) between deployments - the safety car must never
        // be "on track" as pure race-control state with nothing visible.
        void ForceRenderersVisible()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
            }
        }

        void SnapToProgress()
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            track.SampleAtDistance(ProgressDistance, out point, out forward, out right);
            transform.position = point + Vector3.up * 0.08f;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            transform.localScale = Vector3.one;
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
            float targetKph;
            if (IsReturningToPits)
            {
                targetKph = CruiseSpeedKph * 1.1f;
            }
            else
            {
                targetKph = Mathf.Lerp(CruiseSpeedKph, MinCornerSpeedKph, severity);
                // Leader pickup: hold a slow waiting pace until the leader has
                // closed to within ~120m, blending up to full cruise by ~35m so
                // the queue forms without the safety car driving off alone.
                float pickupBlend = Mathf.InverseLerp(120f, 35f, leaderGapMeters);
                targetKph = Mathf.Lerp(PickupSpeedKph, targetKph, pickupBlend);
            }

            CurrentSpeedKph = Mathf.MoveTowards(CurrentSpeedKph, targetKph, Time.deltaTime * (targetKph < CurrentSpeedKph ? 55f : 25f));

            bool braking = CurrentSpeedKph < previousSpeedKph - 0.5f;
            previousSpeedKph = CurrentSpeedKph;
            if (brakeLightMaterial != null)
            {
                brakeLightMaterial.SetColor("_EmissionColor", braking ? brakeLightOnColor : brakeLightOffColor);
            }

            if (beaconMaterial != null)
            {
                // Fast alternating double-flash reads as an emergency light bar
                // from much further away than a smooth sine pulse does.
                float phase = Mathf.Repeat(Time.time * 2.4f, 1f);
                bool lit = phase < 0.12f || (phase > 0.2f && phase < 0.32f);
                beaconMaterial.SetColor("_EmissionColor", lit ? beaconBaseColor * 2.6f : beaconBaseColor * 0.2f);
            }

            ProgressDistance = track.WrapDistance(ProgressDistance + CurrentSpeedKph / 3.6f * Time.deltaTime);
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            track.SampleAtDistance(ProgressDistance, out point, out forward, out right);
            // Blend the heading with a short lookahead sample so a single
            // centerline segment boundary (where SampleAtDistance's forward can
            // jump slightly between linear segments) doesn't read as a visible
            // snap in the car's rotation - this smooths cornering without
            // affecting the position track, which is already MoveTowards'd.
            Vector3 lookaheadPoint;
            Vector3 lookaheadForward;
            Vector3 lookaheadRight;
            track.SampleAtDistance(track.WrapDistance(ProgressDistance + 6f), out lookaheadPoint, out lookaheadForward, out lookaheadRight);
            Vector3 blendedForward = (forward + lookaheadForward).normalized;
            Vector3 targetPosition = point + Vector3.up * 0.08f;
            Quaternion targetRotation = Quaternion.LookRotation(blendedForward, Vector3.up);
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, Time.deltaTime * (CurrentSpeedKph / 3.6f + 8f));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, Time.deltaTime * 160f);
            // If the smoothing ever falls far behind the sampled point (a teleport,
            // a respawn, a long pause), snap rather than drift through scenery.
            if ((transform.position - targetPosition).sqrMagnitude > 40f * 40f)
            {
                SnapToProgress();
            }

            if (body != null)
            {
                body.MovePosition(transform.position);
                body.MoveRotation(transform.rotation);
            }

            if (IsReturningToPits)
            {
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
            gameObject.SetActive(false);
        }
    }
}
