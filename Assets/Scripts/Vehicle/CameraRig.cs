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

        // A sustained yaw rate above this (rad/s, smoothed - see
        // smoothedYawRate) reads as a genuine spin/slide rather than normal
        // cornering, which never gets anywhere near this fast even in a tight
        // hairpin - see spinRecoveryAmount below.
        const float SpinYawRateThreshold = 2.3f;

        Camera followCamera;
        Rigidbody targetBody;
        VehicleController targetVehicle;

        /// <summary>The gameplay camera this rig drives (null until Initialize). Read-only accessor.</summary>
        public Camera Camera => followCamera;
        // When the Cinemachine backend is live, this director drives the camera
        // and CameraRig becomes a thin compatibility wrapper: it stops moving the
        // transform and forwards mode-cycling, impulses, shake scale and look-back.
        F1Game.Cameras.RaceCameraDirector director;
        bool directorActive;
        // Default view is the rear-chase camera (the "fourth camera" per the
        // earlier request - now index 5 after the two onboard views were
        // inserted at 1 and 2); C cycles onward from there in order.
        int mode = 5;
        Vector3 velocitySmoothed;
        float smoothedSpeedKph;
        float previousRawSpeedKph = -1f;
        float previousDamagePercent = -1f;
        float rollAngle;
        float impulseShake;
        float clatterShake;
        float smoothedSteer;
        float smoothedYawRate;

        // A second low-pass stage over the combined steer/yaw-rate corner
        // signal below (each input is already smoothed on its own), so a sharp
        // yaw-rate spike - a car catching a slide - doesn't swing the look-ahead
        // bias hard enough for the camera to visibly overshoot and correct.
        float smoothedCornerSignal;
        int previousGear = -1;
        float modeBlend = 1f;
        float smoothedDrsBoost;

        // A quick, decaying FOV kick on the instant a gear change lands -
        // distinct from smoothedDrsBoost's slow continuous widen above, this
        // is a discrete punch tied to the shift moment itself (echoing the
        // brief power interruption/surge of a real shift) that fades out over
        // a couple of tenths of a second. Reuses the same gear-change
        // detection that already feeds AddImpulseShake(0.012f) below.
        float gearShiftPulse;

        // How much a genuine spin/off-track slide is currently easing the
        // camera off its normal tight tracking - see SpinYawRateThreshold and
        // the recovery logic in LateUpdate. Real broadcast operators lag and
        // resettle after a car spins rather than instantly re-locking onto
        // it, so this briefly loosens rotational follow and pulls the FOV in
        // a touch, then eases back out well after the spin itself ends.
        float spinRecoveryAmount;

        // Chase, driver-eye cockpit, airbox T-cam, halo cam, high TV,
        // rear chase, low nose cam, side cinematic.
        // The two onboard views (per request, as the SECOND and THIRD camera
        // angles) are hard-mounted in car-local space: index 1 sits at the
        // driver's eye line (helmet height, wheel in frame - the wheel itself
        // is animated from live input by VehicleVisuals), index 2 sits on top
        // of the airbox above the cockpit, framing the helmet, wheel and the
        // road ahead.
        readonly Vector3[] offsets =
        {
            new Vector3(0f, 4.1f, -11.4f),
            // Driver eye sits INSIDE the visor/helmet shells on purpose:
            // both are closed primitives, so from inside them backface
            // culling hides them entirely - placed outside, the dark visor
            // dome (which encloses the steering wheel) would block the view.
            new Vector3(0f, 0.84f, 0.3f),
            new Vector3(0f, 1.16f, -0.52f),
            new Vector3(0f, 2.02f, 1.55f),
            new Vector3(0f, 26f, -11f),
            new Vector3(0f, 4.6f, 14.5f),
            new Vector3(0f, 0.58f, 2.3f),
            new Vector3(3.6f, 1.5f, -5.6f)
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
            // 2600 -> 9000 (per report - "so much stuff loaded but none of
            // that shows up"): the scenery build-out placed the mountain
            // backdrop/skyline rings ~2-3km out, past the old far plane, so
            // half the built world was clipped before fog even mattered.
            followCamera.farClipPlane = 9000f;
            // [SceneryDiag] one-line proof of the render envelope at start:
            // if distant scenery is still missing, these numbers name the
            // culprit (fog visibility vs draw distance) without guessing.
            float diagDensity = RenderSettings.fog ? RenderSettings.fogDensity : 0f;
            float diagHalfVisibility = diagDensity > 0f ? Mathf.Sqrt(0.693f) / diagDensity : float.PositiveInfinity;
            Debug.Log("[SceneryDiag] farClip=" + followCamera.farClipPlane.ToString("0") +
                      "m fog=" + RenderSettings.fog + " density=" + diagDensity.ToString("0.00000") +
                      " (50% haze at ~" + diagHalfVisibility.ToString("0") + "m)");
            followCamera.allowHDR = true;
            followCamera.allowMSAA = true;
            followCamera.backgroundColor = RenderSettings.fogColor;
            // Post-processing, per active pipeline: under URP the global race
            // Volume (RaceVolumeService) provides the chain and UrpCameraSetup
            // enables pipeline post/HDR/SMAA on the runtime-created camera; under
            // the Built-in pipeline (the current default - see KNOWN_ISSUES.md
            // "Render pipeline") URP Volumes never render, so the original
            // CameraPostFx OnRenderImage chain (bloom + filmic tonemap + grade +
            // vignette, restored from the pre-migration code) is attached instead.
            // Exactly one backend is active for any given pipeline.
            F1Game.Rendering.UrpCameraSetup.Apply(followCamera);
            if (!F1Game.Rendering.ShaderCompat.UrpActive && followCamera.GetComponent<CameraPostFx>() == null)
            {
                followCamera.gameObject.AddComponent<CameraPostFx>();
            }

            // Exactly-one-listener guarantee (per report - Unity's "There are
            // 2 audio listeners in the scene" warning): this used to only
            // check its OWN camera before adding a listener, so any second
            // camera in the scene (a leftover rig from a session restart, a
            // replay/secondary camera, a scene-file camera) duplicated it.
            // The active race camera is the single authority: every other
            // listener in the scene is disabled, ours is enabled.
            AudioListener[] allListeners = FindObjectsOfType<AudioListener>(true);
            for (int i = 0; i < allListeners.Length; i++)
            {
                if (allListeners[i] != null && allListeners[i].gameObject != followCamera.gameObject)
                {
                    allListeners[i].enabled = false;
                }
            }

            AudioListener listener = followCamera.GetComponent<AudioListener>();
            if (listener == null)
            {
                listener = followCamera.gameObject.AddComponent<AudioListener>();
            }

            listener.enabled = true;

            SnapToTarget();

            // Cinemachine live path: attach the director to the race camera. When
            // active, this rig stops driving the transform (the brain does) and
            // only forwards cycling/impulse/shake/look-back.
            if (CameraConfig.UseCinemachine && target != null)
            {
                VehicleController vehicle = targetVehicle;
                // FOV-pump fix (per report - a hit "fucks up the FOV"): this
                // normalised speed against the LIVE TargetTopSpeedKph, which
                // collapses the moment the car takes damage (and halves on a
                // puncture) - so an impact made speed01 jump and the
                // speed-widen FOV lurch while the actual speed barely changed.
                // Normalised against the car's stable base top speed instead,
                // so only genuine speed changes move the lens.
                director = F1Game.Cameras.RaceCameraDirector.Attach(
                    followCamera,
                    target,
                    () => vehicle != null
                        ? Mathf.Clamp01(Mathf.Abs(vehicle.CurrentSpeedKph) / Mathf.Max(200f, vehicle.CarData != null ? vehicle.CarData.topSpeed : 316f))
                        : 0f);
                directorActive = director != null;
                if (directorActive)
                {
                    F1Game.Cameras.RaceCameraDirector.UseCinemachine = true;
                    director.ShakeScale = shakeStrength / 0.6f; // normalise the 0..0.6 shake setting
                }
            }
        }

        public void NextMode()
        {
            if (directorActive)
            {
                director.NextCamera();
                return;
            }

            mode = (mode + 1) % offsets.Length;

            // Start the blend timer fresh so the cut into the new angle eases
            // in over ModeBlendDuration instead of snapping straight there.
            modeBlend = 0f;
        }

        /// <summary>Look-back forwarding (Cinemachine path only).</summary>
        public void SetLookBack(bool active)
        {
            if (directorActive)
            {
                director.SetLookBack(active);
            }
        }

        float ModeFov(float speed01)
        {
            if (mode == 1)
            {
                // Driver-eye cockpit: wide enough to keep the wheel and both
                // mirrorscape edges in frame, with gentle speed widening.
                return baseFov + 6f + speed01 * 6f;
            }

            if (mode == 2)
            {
                // Airbox T-cam: broadcast onboard framing - helmet, wheel and
                // the road ahead.
                return baseFov + 2f + speed01 * 5f;
            }

            if (mode == 3)
            {
                // Halo cam: a touch of tunnel widening with speed, kept gentle.
                return baseFov + 8f + speed01 * 5f;
            }

            if (mode == 4)
            {
                return baseFov - 8f;
            }

            if (mode == 6)
            {
                // Nose cam: tarmac-level and close to the action, so let the
                // lens stretch hard at speed for a proper flat-out feel.
                return baseFov + 7f + speed01 * 10f;
            }

            if (mode == 7)
            {
                // Side cinematic tracking shot: a touch of telephoto compression
                // rather than the wide, close-in feel of the other modes, with
                // only a light widen at speed so the compressed backdrop reads
                // as a considered framing choice rather than an accident.
                return baseFov - 14f + speed01 * 6f;
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

            // Cinemachine live path: the director + brain own the camera transform,
            // FOV and shake. This rig does no manual positioning in that mode.
            if (directorActive)
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

            // Faster smoothing on the way down than on the way up: rising speed
            // stays gently smoothed (avoids the follow rate below pumping
            // around every tiny throttle flicker), but a hard brake needs
            // smoothedSpeedKph - and therefore the follow rate that reads off
            // it - to catch up promptly, otherwise the camera keeps its loose
            // high-speed follow for a beat into the braking zone and visibly
            // lags the car settling into the corner.
            float speedSmoothRate = rawSpeedKph < smoothedSpeedKph ? 7.5f : 4.5f;
            smoothedSpeedKph = Mathf.Lerp(smoothedSpeedKph, rawSpeedKph, dt * speedSmoothRate);
            // Same FOV-pump fix as the director path: stable base top speed,
            // not the damage/puncture-sensitive live target.
            float topSpeedKph = targetVehicle != null && targetVehicle.CarData != null ? Mathf.Max(200f, targetVehicle.CarData.topSpeed) : 316f;
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

            // The speed-drop check above catches a hard, sudden hit but misses
            // a sustained scrape along a wall/barrier at fairly steady speed,
            // or a bottomed-out floor - both still climb Damage.OverallPercent,
            // so a meaningful jump there earns its own (smaller) impulse rather
            // than the crash going visually silent just because road speed
            // barely moved.
            if (targetVehicle != null && targetVehicle.Damage != null)
            {
                float damagePercent = targetVehicle.Damage.OverallPercent;
                if (previousDamagePercent >= 0f && damagePercent > previousDamagePercent + 3f)
                {
                    AddImpulseShake(Mathf.Clamp(0.02f + (damagePercent - previousDamagePercent) * 0.0022f, 0.02f, 0.11f));
                }

                previousDamagePercent = damagePercent;
            }

            // A tiny shift kick each time CurrentGear actually changes under
            // real speed - not on the grid where gear can flicker at a
            // standstill - layered in as its own light touch on top of the
            // rumble/kerb/lockup/impact/clatter shake already going, rather
            // than making any of those existing layers louder.
            if (targetVehicle != null)
            {
                int gear = targetVehicle.CurrentGear;
                if (previousGear >= 0 && gear != previousGear && rawSpeedKph > 40f)
                {
                    AddImpulseShake(0.012f);
                    gearShiftPulse = 1f;
                }

                previousGear = gear;
            }

            // Both decay every frame regardless of mode/shake settings, same
            // as impulseShake/clatterShake in ComputeShakeOffset below, so
            // they're always in a sane state by the time they're read further
            // down even on a frame where the shake pass itself early-outs.
            gearShiftPulse = Mathf.MoveTowards(gearShiftPulse, 0f, dt * 5f);
            spinRecoveryAmount = Mathf.MoveTowards(spinRecoveryAmount, 0f, dt * 0.6f);

            modeBlend = Mathf.Min(1f, modeBlend + dt / ModeBlendDuration);
            float blendEase = Mathf.SmoothStep(0f, 1f, modeBlend);

            Vector3 offset = offsets[mode];
            Vector3 desired;
            Quaternion desiredRotation;
            if (mode == 1 || mode == 2)
            {
                // Driver-eye (1) and airbox (2) are rigid onboard mounts: the
                // camera lives at a fixed point in car space and looks straight
                // down the chassis with the car's own up vector, so it banks
                // and pitches with the car exactly like a real onboard. The
                // slight downward tilt keeps the wheel/helmet in frame - a bit
                // more from the airbox since it sits higher and further back.
                desired = target.TransformPoint(offset);
                desiredRotation = Quaternion.LookRotation(target.forward + target.up * (mode == 2 ? -0.10f : -0.04f), target.up);
            }
            else if (mode == 4)
            {
                // TV cam drifts alongside the racing line rather than sitting glued
                // overhead, which reads much more like a broadcast crane. A slow,
                // low-amplitude Perlin sway on top of that sells a human operator
                // holding the rig rather than a perfectly locked-off camera.
                float swayTime = Time.unscaledTime;
                Vector3 craneSway = new Vector3(
                    (Mathf.PerlinNoise(swayTime * 0.12f, 3.7f) - 0.5f) * 0.7f,
                    (Mathf.PerlinNoise(5.1f, swayTime * 0.09f) - 0.5f) * 0.4f,
                    0f);
                desired = target.position + offset + velocitySmoothed * 0.22f + craneSway;
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

                // A real spin/slide, not just a hard corner - re-arms the
                // recovery easing below every frame it's still spinning fast,
                // so a long spin keeps the camera settled-back throughout
                // rather than the effect firing once and releasing early.
                if (Mathf.Abs(smoothedYawRate) > SpinYawRateThreshold)
                {
                    spinRecoveryAmount = 1f;
                }

                float rawCornerSignal = smoothedSteer * 0.7f + Mathf.Clamp(smoothedYawRate * 0.45f, -1f, 1f) * 0.3f;

                // A touch quicker than the two input stages feeding it (5/6)
                // so the combined corner signal doesn't add a third full lag
                // stage on top - the camera should start anticipating a turn
                // shortly after the driver actually commits to it, not noticeably
                // behind the front-wheel turn-in.
                smoothedCornerSignal = Mathf.Lerp(smoothedCornerSignal, rawCornerSignal, 1f - Mathf.Exp(-dt * 9.5f));
                float cornerSignal = smoothedCornerSignal;
                float cornerBiasScale = mode == 3 || mode == 6 ? Mathf.Lerp(0.12f, 0.5f, speed01) : Mathf.Lerp(0.25f, 1.4f, speed01);
                Vector3 cornerBias = target.right * cornerSignal * cornerBiasScale;
                Vector3 lookTarget = target.position + Vector3.up * 1.05f + velocitySmoothed * (mode == 3 ? 0.07f : 0.2f) + cornerBias;
                Vector3 lookDirection = lookTarget - desired;
                if (mode == 5)
                {
                    lookDirection = target.position + Vector3.up * 1.05f - desired;
                }
                else if (mode == 6)
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
                float rollClamp = mode == 3 ? 1.6f : 1.2f;
                float targetRoll = Mathf.Clamp(-lateral * 0.05f, -rollClamp, rollClamp);
                rollAngle = Mathf.Lerp(rollAngle, targetRoll, dt * 4f);
                desiredRotation *= Quaternion.Euler(0f, 0f, rollAngle);
            }

            desired += ComputeShakeOffset(speed01);

            // A touch of slow handheld-style breathing once the car is essentially
            // stationary (grid, pit box, post-spin standstill), so a long static hold
            // on the chase/rear-chase/side-cinematic angles doesn't read as a frozen
            // viewport - real broadcast operators never sit perfectly still even on a
            // parked car. Fades to nothing well before the car is actually rolling so
            // it never touches on-track framing; cockpit/nose stay rigidly mounted on
            // purpose and the TV crane already has its own craneSway above.
            if (mode == 0 || mode == 5 || mode == 7)
            {
                // A car can be essentially stationary (speed01 near zero) right after
                // slamming into something and still be settling from the impact/clatter
                // shake above - layering the slow handheld breathe on top of that at
                // full strength would fight the sharp settle rather than reading as a
                // calm operator holding the shot, so it's faded out while there's still
                // meaningful shake energy in the system and only ramps back in once
                // things have actually settled.
                float recentImpact = Mathf.Clamp01((impulseShake + clatterShake) / 0.04f);
                float idleAmount = Mathf.Clamp01(1f - speed01 / 0.05f) * (1f - recentImpact);
                if (idleAmount > 0.001f)
                {
                    float breatheTime = Time.unscaledTime;
                    Vector3 breathe = new Vector3(
                        (Mathf.PerlinNoise(breatheTime * 0.35f, 11.3f) - 0.5f) * 0.05f,
                        (Mathf.PerlinNoise(2.7f, breatheTime * 0.3f) - 0.5f) * 0.03f,
                        0f);
                    desired += breathe * idleAmount;
                }
            }

            // Chase and rear-chase get quick, precise response at low speed
            // (good for threading a chicane) that loosens into a trailing,
            // slightly-behind feel at speed, which is what actually reads as
            // fast on screen rather than robotically glued in place. Cockpit
            // and nose stay rigidly mounted, and the TV crane stays slow and
            // floaty on purpose. Right after a mode switch, blendEase eases
            // both rates in from a slower start so the cut glides rather than
            // snaps into the new angle.
            bool chaseLike = mode == 0 || mode == 5;

            // Side cinematic (mode 5) gets its own slow, deliberate follow -
            // floatier than the plain chase modes but still tracking the car
            // (unlike the TV crane, which is anchored in world space), so it
            // reads as a composed tracking shot rather than either a glued-on
            // chase cam or a locked-off broadcast angle.
            // Chase/rear-chase still loosen into a trailing feel as speed rises
            // (that's what reads as fast on screen), but the floor is raised a
            // little from the old 5.6/6.6 so the camera never feels fully
            // detached from the car at v-max - just looser, not laggy.
            float baseFollowRate = mode == 1 || mode == 2 ? 45f : (mode == 3 || mode == 6 ? 17f : (mode == 4 ? 3.2f : (mode == 7 ? Mathf.Lerp(4.6f, 3.4f, speed01) : Mathf.Lerp(11.5f, 6.4f, speed01))));
            float baseRotRate = mode == 1 || mode == 2 ? 35f : (chaseLike ? Mathf.Lerp(9.6f, 7.2f, speed01) : (mode == 7 ? 5.4f : 8.2f));
            float followRate = Mathf.Lerp(baseFollowRate * 0.35f, baseFollowRate, blendEase);
            float rotRate = Mathf.Lerp(baseRotRate * 0.35f, baseRotRate, blendEase);

            // Mid-spin/just-recovering, loosen the rotational follow so the
            // camera lags a beat behind a fast-spinning car rather than
            // whip-panning in lockstep with it - the TV crane (mode 4) is
            // excluded, same as the FOV kicks below, since it's meant to stay
            // detached from the chassis, and so are the two rigid onboard
            // mounts (modes 1-2), which should stay bolted to the car through
            // a spin rather than easing off it. This only ever loosens
            // rotRate, never the position followRate, so the camera still
            // tracks the car's location precisely through a spin - it just
            // stops snapping its facing to match every instant of rotation.
            float spinRecoveryEase = mode == 4 || mode == 1 || mode == 2 ? 0f : spinRecoveryAmount;
            if (spinRecoveryEase > 0f)
            {
                rotRate = Mathf.Lerp(rotRate, rotRate * 0.4f, spinRecoveryEase);
            }

            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followRate * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-rotRate * dt));

            // DRS opening stretches the lens a touch beyond the pure speed curve -
            // the flap and the extra top-end arrive together, so the widening sells
            // the surge - while a fresh impact briefly tightens it (impulseShake is
            // already fed and decayed by the shake pass above), a quick punch-in
            // that reads as the hit even with the offset shake kept subtle. The TV
            // crane (mode 4) stays at its fixed broadcast focal length.
            float drsTarget = targetVehicle != null && targetVehicle.DrsActive ? 1f : 0f;
            smoothedDrsBoost = Mathf.MoveTowards(smoothedDrsBoost, drsTarget, dt * 1.6f);
            float fovTarget = ModeFov(speed01);
            if (mode != 4)
            {
                // Same curved punch as the shake offset below (see ImpactPunchCurve)
                // so a solid hit snaps the lens in noticeably harder than a graze,
                // instead of the old flat linear pull that barely told them apart.
                // A locked tyre pulls the lens in slightly too - a mild tunnel-vision
                // cue riding on top of the existing DRS widen/impact punch terms. Hard
                // braking at real speed gets the same treatment but softer still - a
                // trail-braking "focus narrows under load" cue distinct from the sharp
                // lockup pull, only kicking in well into the brake pedal and scaled by
                // speed so a light dab at low speed never triggers it.
                float lockupSeverity = targetVehicle != null && targetVehicle.Tyres != null ? targetVehicle.Tyres.LockupSeverity : 0f;
                float brakeFocus = targetVehicle != null ? Mathf.InverseLerp(0.5f, 1f, targetVehicle.EffectiveBrake) : 0f;
                fovTarget += smoothedDrsBoost * 2.6f - ImpactPunchCurve(impulseShake) * 26f - lockupSeverity * 3.2f - brakeFocus * speed01 * 2f;

                // A gear shift gets its own small, quickly-fading widen on top
                // of the above - eased with its own square rather than linear
                // so it reads as a quick punch rather than a linear ramp - and
                // a spin/off-track slide pulls the lens in a touch as part of
                // the same settle-and-recover feel as the loosened rotRate
                // above, distinct from (and much smaller than) the sharp
                // impact punch already handled a couple lines up.
                fovTarget += gearShiftPulse * gearShiftPulse * 1.4f - spinRecoveryEase * 3.5f;
            }

            float fovBlendRate = Mathf.Lerp(1.4f, 3f, blendEase);
            float desiredFov = Mathf.Lerp(followCamera.fieldOfView, fovTarget, dt * fovBlendRate);
            followCamera.fieldOfView = Mathf.Clamp(desiredFov, 40f, 100f);
            followCamera.transform.localPosition = Vector3.zero;
            followCamera.transform.localRotation = Quaternion.identity;

            // Weather can transition mid-session (see VehicleController.SetWeather);
            // keep the camera's clear colour synced every frame rather than only
            // once at Initialize, so a dry-to-wet change doesn't leave the old fog
            // tint baked into the background between cars/skybox-less tracks.
            // RenderSettings.fogColor already carries the night/rain colour
            // grading RaceManager sets up per session - on top of that, a cheap
            // vignette/exposure-pull stand-in (there's no post-processing stack
            // here to do a real one) pulls the clear colour toward black at high
            // speed and punches in harder on a fresh impact, using the same
            // ImpactPunchCurve as the FOV kick and position shake above so all
            // three read as one consistent "how hard was that" response. Blended
            // in over time rather than assigned outright so a weather transition
            // eases in instead of popping instantly.
            float speedDarken = Mathf.Lerp(0f, 0.22f, speed01 * speed01) + ImpactPunchCurve(impulseShake) * 1.4f;
            Color gradedBackground = Color.Lerp(RenderSettings.fogColor, Color.black, Mathf.Clamp01(speedDarken));
            followCamera.backgroundColor = Color.Lerp(followCamera.backgroundColor, gradedBackground, 1f - Mathf.Exp(-dt * 6f));
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
            if (!cameraShake || mode == 4 || shakeStrength <= 0.001f)
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
            // Run through ImpactPunchCurve rather than a flat multiply so the
            // response is front-loaded toward real hits: a light kerb-adjacent
            // tap barely registers here (that's what clatterShake is for) while
            // a genuine crash punches noticeably harder than the old linear
            // scaling ever let it.
            Vector3 impactOffset = Vector3.zero;
            if (impulseShake > 0.001f)
            {
                float impactX = Mathf.PerlinNoise(t * 37f, 5.7f) - 0.5f;
                float impactY = Mathf.PerlinNoise(3.1f, t * 41f) - 0.5f;
                float impactZ = (Mathf.PerlinNoise(t * 29f + 1.7f, t * 29f) - 0.5f) * 0.4f;
                impactOffset = new Vector3(impactX, impactY, impactZ) * ImpactPunchCurve(impulseShake) * 1.9f;
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

            // Nose cam sits low and close to the tarmac, so the same underlying
            // shake should read stronger there; cockpit is meant to feel more
            // anchored/isolated from the chassis, so it gets a touch less. The
            // side cinematic shot is meant to feel like a composed camera
            // operator, not something bolted to the chassis, so it gets the
            // most damping of all.
            float modeShakeMultiplier = mode == 6 ? 1.22f : (mode == 1 || mode == 2 ? 0.6f : (mode == 3 ? 0.85f : (mode == 7 ? 0.7f : 1f)));
            return (rumbleOffset + judderOffset + offTrackOffset + impactOffset + clatterOffset) * shakeStrength * 1.6f * modeShakeMultiplier;
        }

        // Curves a raw impulseShake reading (0-MaxImpulseShake) so small taps
        // stay subdued while big hits punch disproportionately harder, rather
        // than the shake/FOV response scaling in a flat straight line with
        // impact size. Shared by the position shake and the FOV kick above so
        // both read as the same underlying "how hard was that" signal.
        const float MaxImpulseShake = 0.16f;

        static float ImpactPunchCurve(float raw)
        {
            float normalized = Mathf.Clamp01(raw / MaxImpulseShake);
            return Mathf.Pow(normalized, 1.7f) * MaxImpulseShake;
        }

        public void AddImpulseShake(float amount)
        {
            if (!cameraShake || followCamera == null)
            {
                return;
            }

            // Cinemachine live path: route impulses to the director's impulse
            // source (kerb taps and impacts alike) instead of the legacy pools.
            if (directorActive)
            {
                director.AddImpulse(Mathf.Clamp(amount, 0f, 0.15f) / 0.15f);
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
