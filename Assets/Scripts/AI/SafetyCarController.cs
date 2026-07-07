using UnityEngine;

namespace LocalFormulaRacing
{
    // Drives the real, visible safety car object along the track's own
    // centerline as a deterministic scripted mover - it never races, it only
    // needs to look right, block the road as a real physical obstacle, and
    // slow down convincingly for corners. Movement uses exactly one
    // application path: ProgressDistance advances every frame, a smoothed
    // visual position/rotation chases the sampled target, and the kinematic
    // Rigidbody is a passive mirror of the transform (for collision geometry
    // only) rather than an independent mover - it is never used to actually
    // move the car, so there is nothing left for transform and physics
    // movement to fight over. Time.timeScale is 0 while the race is paused,
    // which already zeroes every Time.deltaTime-scaled step below, so no
    // separate pause guard is needed here.
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

        // Internal visual state - the only thing ever written to
        // transform.position/rotation. Kept separate from ProgressDistance so
        // the two can never silently diverge: the visual is always a smoothed
        // chase of whatever ProgressDistance currently samples to, never an
        // independent value that can drift and then need a corrective snap.
        Vector3 visualPosition;
        Quaternion visualRotation;
        bool visualInitialized;

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
                // Kinematic rigidbodies used purely as passive collision
                // geometry should never be nudged by the solver - interpolation
                // is irrelevant here since we hard-sync body.position/rotation
                // to the transform every frame ourselves rather than calling
                // MovePosition/MoveRotation, which would otherwise schedule a
                // second, independent movement path racing the transform one.
                body.interpolation = RigidbodyInterpolation.None;
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
        // leader immediately has to slow and queue up behind it. This is the
        // ONLY normal entry point that hard-snaps the visual - a brand new
        // deployment is expected to simply appear on track, not drive there.
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

        // Hard-repositions the visual to exactly where ProgressDistance samples
        // to. Only ever called from EnterTrack (first spawn / re-deployment) -
        // normal per-frame movement in Update() never calls this, so the car
        // can never visibly teleport while it is already active and running.
        void SnapToProgress()
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            track.SampleAtDistance(ProgressDistance, out point, out forward, out right);
            visualPosition = point + Vector3.up * 0.08f;
            visualRotation = Quaternion.LookRotation(forward, Vector3.up);
            visualInitialized = true;
            ApplyVisualToTransformAndBody();
        }

        void ApplyVisualToTransformAndBody()
        {
            transform.position = visualPosition;
            transform.rotation = visualRotation;
            transform.localScale = Vector3.one;
            // Passive sync only - never MovePosition/MoveRotation, which would
            // create a second, independently-timed movement path for the same
            // object. Direct assignment on a kinematic body just relocates its
            // collider to match the transform we already committed to this frame.
            if (body != null)
            {
                body.position = visualPosition;
                body.rotation = visualRotation;
            }
        }

        void Update()
        {
            if (!IsActive || track == null)
            {
                return;
            }

            if (!visualInitialized)
            {
                SnapToProgress();
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

            // Advance progress at exactly the car's own pace - this is the single
            // source of truth for where the safety car "is" along the track.
            ProgressDistance = track.WrapDistance(ProgressDistance + CurrentSpeedKph / 3.6f * Time.deltaTime);
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            track.SampleAtDistance(ProgressDistance, out point, out forward, out right);
            // Blend the heading with a short lookahead sample so a single
            // centerline segment boundary (where SampleAtDistance's forward can
            // jump slightly between linear segments) doesn't read as a visible
            // snap in the car's rotation - this smooths cornering without
            // affecting the position track, which chases the sampled point
            // continuously below.
            Vector3 lookaheadPoint;
            Vector3 lookaheadForward;
            Vector3 lookaheadRight;
            track.SampleAtDistance(track.WrapDistance(ProgressDistance + 6f), out lookaheadPoint, out lookaheadForward, out lookaheadRight);
            Vector3 blendedForward = (forward + lookaheadForward).normalized;
            Vector3 targetPosition = point + Vector3.up * 0.08f;
            Quaternion targetRotation = Quaternion.LookRotation(blendedForward, Vector3.up);

            // Position chases the sampled target at a rate that always at least
            // matches the car's own advancing speed (+ a small buffer), so under
            // normal continuous movement the visual can never fall behind the
            // sampled point in the first place - there is no accumulating gap
            // left over to "catch up" on with a snap. Angular corners are
            // absorbed by smoothing rotation aggressively instead of snapping
            // position, per the brief.
            float chaseSpeed = CurrentSpeedKph / 3.6f + 12f;
            visualPosition = Vector3.MoveTowards(visualPosition, targetPosition, Time.deltaTime * chaseSpeed);
            visualRotation = Quaternion.RotateTowards(visualRotation, targetRotation, Time.deltaTime * 220f);
            ApplyVisualToTransformAndBody();

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
            visualInitialized = false;
            gameObject.SetActive(false);
        }
    }
}
