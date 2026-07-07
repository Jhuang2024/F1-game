using UnityEngine;

namespace LocalFormulaRacing
{
    public class CameraRig : MonoBehaviour
    {
        public Transform target;
        public bool cameraShake = true;
        public float shakeStrength = 1f;
        public float baseFov = 60f;

        // A frame-to-frame speed loss bigger than this reads as an impact
        // rather than braking (max braking loses only a few kph per physics
        // step, a collision loses tens to hundreds), so it doubles as a
        // self-contained collision-shake trigger with no collision callback
        // needed on this component.
        const float CollisionSpeedDropKph = 20f;
        const float ModeBlendDuration = 0.4f;

        // Impulses below this magnitude are kerb-scale taps rather than real
        // impacts (a genuine crash - self-detected below, or a real damage
        // hit - always clears it), so AddImpulseShake routes them into their
        // own faster, lighter-decaying pool instead of the sharp impact one.
        const float ClatterImpulseThreshold = 0.03f;

        Camera followCamera;
        Rigidbody targetBody;
        VehicleController targetVehicle;
        int mode;
        Vector3 velocitySmoothed;
        float smoothedSpeedKph;
        float previousRawSpeedKph = -1f;
        float rollAngle;
        float impulseShake;
        float clatterShake;
        float smoothedSteer;
        float smoothedYawRate;
        float modeBlend = 1f;

        // Chase, cockpit/halo, high TV, rear chase, low nose cam.
        readonly Vector3[] offsets =
        {
            new Vector3(0f, 4.1f, -11.4f),
            new Vector3(0f, 2.02f, 1.55f),
            new Vector3(0f, 26f, -11f),
            new Vector3(0f, 4.6f, 14.5f),
            new Vector3(0f, 0.58f, 2.3f)
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

            // Start the blend timer fresh so the cut into the new angle eases
            // in over ModeBlendDuration instead of snapping straight there.
            modeBlend = 0f;
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
                // Nose cam: tarmac-level and close to the action, so let the
                // lens stretch hard at speed for a proper flat-out feel.
                return baseFov + 7f + speed01 * 10f;
            }

            // Chase: a non-linear widen so the last 100 km/h really stretch the view
            // without the mid-range constantly pumping the lens.
            float curve = Mathf.Pow(speed01, 1.6f);
            return Mathf.Lerp(baseFov - 3f, baseFov + 14f, curve);
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

            // Speed comes straight from the vehicle (kph, sign-aware so
            // reversing doesn't read as flat-out) rather than re-derived from
            // the rigidbody, and is normalised against that car's own top
            // speed so setup/DRS/ERS changes don't skew the FOV/damping feel.
            float rawSpeedKph = targetVehicle != null ? Mathf.Abs(targetVehicle.CurrentSpeedKph) : targetVelocity.magnitude * 3.6f;
            smoothedSpeedKph = Mathf.Lerp(smoothedSpeedKph, rawSpeedKph, dt * 4.5f);
            float topSpeedKph = targetVehicle != null ? Mathf.Max(200f, targetVehicle.TargetTopSpeedKph) : 316f;
            float speed01 = Mathf.Clamp01(smoothedSpeedKph / topSpeedKph);

            // Self-contained impact detection: a big instantaneous speed loss
            // (a wall, another car) trips the same impulse shake used for
            // kerbs, without needing a collision event wired into this file.
            if (previousRawSpeedKph >= 0f)
            {
                float speedDrop = previousRawSpeedKph - rawSpeedKph;
                if (speedDrop > CollisionSpeedDropKph)
                {
                    float impact = Mathf.Clamp01((speedDrop - CollisionSpeedDropKph) / CollisionSpeedDropKph);
                    AddImpulseShake(0.05f + impact * 0.09f);
                }
            }

            previousRawSpeedKph = rawSpeedKph;

            modeBlend = Mathf.Min(1f, modeBlend + dt / ModeBlendDuration);
            float blendEase = Mathf.SmoothStep(0f, 1f, modeBlend);

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

                // Steering and actual yaw rate both feed the corner look-ahead:
                // steering alone can lead the turn before the car has really
                // started rotating, while yaw rate catches the moment it's
                // genuinely sliding or rotating harder than the wheel angle
                // suggests. Both are heavily smoothed and kept subtle overall.
                float rawSteer = targetVehicle != null ? targetVehicle.CurrentCommand.steer : 0f;
                float rawYawRate = targetBody != null ? target.InverseTransformDirection(targetBody.angularVelocity).y : 0f;
                smoothedSteer = Mathf.Lerp(smoothedSteer, rawSteer, 1f - Mathf.Exp(-dt * 5f));
                smoothedYawRate = Mathf.Lerp(smoothedYawRate, rawYawRate, 1f - Mathf.Exp(-dt * 6f));
                float cornerSignal = smoothedSteer * 0.7f + Mathf.Clamp(smoothedYawRate * 0.45f, -1f, 1f) * 0.3f;
                float cornerBiasScale = mode == 1 || mode == 4 ? Mathf.Lerp(0.12f, 0.5f, speed01) : Mathf.Lerp(0.25f, 1.4f, speed01);
                Vector3 cornerBias = target.right * cornerSignal * cornerBiasScale;
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

            // Chase and rear-chase get quick, precise response at low speed
            // (good for threading a chicane) that loosens into a trailing,
            // slightly-behind feel at speed, which is what actually reads as
            // fast on screen rather than robotically glued in place. Cockpit
            // and nose stay rigidly mounted, and the TV crane stays slow and
            // floaty on purpose. Right after a mode switch, blendEase eases
            // both rates in from a slower start so the cut glides rather than
            // snaps into the new angle.
            bool chaseLike = mode == 0 || mode == 3;
            float baseFollowRate = mode == 1 || mode == 4 ? 17f : (mode == 2 ? 3.2f : Mathf.Lerp(11.5f, 5.6f, speed01));
            float baseRotRate = chaseLike ? Mathf.Lerp(9.6f, 6.6f, speed01) : 8.2f;
            float followRate = Mathf.Lerp(baseFollowRate * 0.35f, baseFollowRate, blendEase);
            float rotRate = Mathf.Lerp(baseRotRate * 0.35f, baseRotRate, blendEase);

            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followRate * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-rotRate * dt));

            float fovBlendRate = Mathf.Lerp(1.4f, 3f, blendEase);
            float desiredFov = Mathf.Lerp(followCamera.fieldOfView, ModeFov(speed01), dt * fovBlendRate);
            followCamera.fieldOfView = Mathf.Clamp(desiredFov, 40f, 100f);
            followCamera.transform.localPosition = Vector3.zero;
            followCamera.transform.localRotation = Quaternion.identity;
        }

        // Shake only when the situation earns it: very high speed, heavy braking,
        // kerb strikes, tyre lockups, off-track running, and collisions (via
        // AddImpulseShake, including the internal speed-drop detection above).
        // Cruise stays steady, and steering alone contributes nothing here.
        // Each distinct event reads on its own noise layer rather than
        // blending into one generic wobble: fine rumble for speed/braking/
        // kerb, a slower judder for a locked tyre, a broad low bounce for
        // off-track, a quick mid-frequency rattle for kerb-scale taps, and a
        // sharp high-frequency snap reserved for genuine impacts.
        Vector3 ComputeShakeOffset(float speed01)
        {
            impulseShake = Mathf.MoveTowards(impulseShake, 0f, Time.deltaTime * 1.8f);
            clatterShake = Mathf.MoveTowards(clatterShake, 0f, Time.deltaTime * 5f);
            if (!cameraShake || mode == 2 || shakeStrength <= 0.001f)
            {
                return Vector3.zero;
            }

            float rumble = 0f;
            float highSpeed = Mathf.InverseLerp(0.86f, 1f, speed01);
            rumble += highSpeed * 0.012f;

            float lockupSeverity = 0f;
            float offTrackAmount = 0f;
            if (targetVehicle != null)
            {
                float braking = targetVehicle.EffectiveBrake;
                if (braking > 0.55f && speed01 > 0.45f)
                {
                    rumble += braking * speed01 * 0.016f;
                }

                if (targetVehicle.IsOnKerb && speed01 > 0.2f)
                {
                    rumble += 0.016f;
                }

                lockupSeverity = targetVehicle.Tyres != null ? targetVehicle.Tyres.LockupSeverity : 0f;

                if (targetVehicle.IsOffTrackSlowdown && speed01 > 0.1f)
                {
                    offTrackAmount = Mathf.Lerp(0.35f, 1f, speed01);
                }
            }

            if (rumble <= 0.001f && impulseShake <= 0.001f && clatterShake <= 0.001f && lockupSeverity <= 0.05f && offTrackAmount <= 0.001f)
            {
                return Vector3.zero;
            }

            // Smoothed noise instead of raw per-frame randomness, and mostly local
            // X/Y so it never introduces wild forward/back jitter.
            float t = Time.unscaledTime;
            Vector3 rumbleOffset = Vector3.zero;
            if (rumble > 0.001f)
            {
                float noiseX = Mathf.PerlinNoise(t * 11f, 0.13f) - 0.5f;
                float noiseY = Mathf.PerlinNoise(0.42f, t * 13f) - 0.5f;
                float noiseZ = (Mathf.PerlinNoise(t * 7f, t * 7f) - 0.5f) * 0.3f;
                rumbleOffset = new Vector3(noiseX, noiseY, noiseZ) * rumble;
            }

            // Lockup gets its own coarse, low-frequency judder rather than
            // folding into the fine kerb/speed buzz above - a locked tyre
            // thuds and drags along the tarmac, it doesn't vibrate finely
            // like a kerb strip does.
            Vector3 judderOffset = Vector3.zero;
            if (lockupSeverity > 0.05f)
            {
                float judderX = Mathf.PerlinNoise(t * 6f + 2.3f, 9.1f) - 0.5f;
                float judderZ = Mathf.PerlinNoise(4.4f, t * 6f + 1.1f) - 0.5f;
                judderOffset = new Vector3(judderX, 0f, judderZ) * lockupSeverity * 0.05f;
            }

            // Off-track running is a broad, slow bounce over rough ground -
            // lower frequency still and mostly vertical, distinct from both
            // the kerb buzz and the lockup drag.
            Vector3 offTrackOffset = Vector3.zero;
            if (offTrackAmount > 0.001f)
            {
                float roughY = Mathf.PerlinNoise(t * 4.2f, 6.6f) - 0.5f;
                float roughX = (Mathf.PerlinNoise(8.8f, t * 4.2f) - 0.5f) * 0.4f;
                offTrackOffset = new Vector3(roughX, roughY, 0f) * offTrackAmount * 0.045f;
            }

            // Impact snap: a distinctly higher-frequency noise sample, driven
            // only by impulseShake, which now also decays about twice as fast
            // as before - short and sharp instead of a slow rolling rumble.
            Vector3 impactOffset = Vector3.zero;
            if (impulseShake > 0.001f)
            {
                float impactX = Mathf.PerlinNoise(t * 37f, 5.7f) - 0.5f;
                float impactY = Mathf.PerlinNoise(3.1f, t * 41f) - 0.5f;
                float impactZ = (Mathf.PerlinNoise(t * 29f + 1.7f, t * 29f) - 0.5f) * 0.4f;
                impactOffset = new Vector3(impactX, impactY, impactZ) * impulseShake * 1.4f;
            }

            // Kerb-scale taps: a quicker, mid-frequency rattle sitting between
            // the rumble and the impact snap in frequency - distinct from
            // both so a string of kerb hits never reads like a crash.
            Vector3 clatterOffset = Vector3.zero;
            if (clatterShake > 0.001f)
            {
                float clatterX = Mathf.PerlinNoise(t * 21f, 2.9f) - 0.5f;
                float clatterY = Mathf.PerlinNoise(1.4f, t * 23f) - 0.5f;
                clatterOffset = new Vector3(clatterX, clatterY, 0f) * clatterShake * 1.1f;
            }

            return (rumbleOffset + judderOffset + offTrackOffset + impactOffset + clatterOffset) * shakeStrength * 1.6f;
        }

        public void AddImpulseShake(float amount)
        {
            if (!cameraShake || followCamera == null)
            {
                return;
            }

            float clamped = Mathf.Clamp(amount, 0f, 0.15f);

            // Small taps (kerb strikes) get their own fast-clearing pool so
            // they read as a quick rattle instead of blending into the
            // sharp, slower-decaying snap reserved for real impacts.
            if (clamped < ClatterImpulseThreshold)
            {
                clatterShake = Mathf.Min(0.05f, clatterShake + clamped);
            }
            else
            {
                impulseShake = Mathf.Min(0.16f, impulseShake + clamped);
            }
        }

        void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            modeBlend = 1f;
            Vector3 desired = target.TransformPoint(offsets[0]);
            transform.position = desired;
            transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 1.25f - desired, Vector3.up);
        }
    }
}
