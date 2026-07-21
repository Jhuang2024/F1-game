using UnityEngine;

namespace LocalFormulaRacing
{
    // Lightweight per-car visual feedback: the rear rain-light glows with brake input
    // and blinks while the ERS battery is harvesting, like modern formula cars.
    public class VehicleVisuals : MonoBehaviour
    {
        VehicleController vehicle;
        Material brakeLightMaterial;
        Material brakeDiscMaterial;
        float previousErs;

        // A brief "systems check" blink on the rain light right after the car
        // spawns, distinct from the steady brake/wet-weather glow UpdateRainLight
        // otherwise drives - a couple of quick confidence flashes before the
        // light settles into its normal behaviour, echoing the power-on check a
        // real car's rear light does on the grid. Runs once per car (field
        // defaults cover it - no explicit reset needed since a car is never
        // re-Initialized in place).
        const float StartupSequenceDuration = 1.1f;
        bool startupSequenceActive = true;
        float startupSequenceTimer = StartupSequenceDuration;

        // Wheel spin pivots; fronts also steer visually with the current command.
        Transform frontLeft;
        Transform frontRight;
        Transform rearLeft;
        Transform rearRight;
        float wheelSpinAngle;
        float visualSteerAngle;
        const float WheelRadius = 0.31f;

        // Cockpit steering wheel (per request - the onboard cameras must show
        // the wheel turning with the player's live input). Found lazily by
        // name like the other detail transforms; rotates about its own face
        // axis with a fast smoothing so keyboard taps read as crisp wheel
        // movements rather than instant snaps.
        Transform steeringWheel;
        bool steeringWheelSearched;
        Quaternion steeringWheelRestRotation;
        float steeringWheelAngle;

        // Rear wheels get their own spin angle, separate from wheelSpinAngle above,
        // so a wheelspin event (throttle overwhelming rear grip) can visibly overspin
        // just the rear tyres without also speeding up the fronts, which never lose
        // traction the same way. See UpdateWheels for how the extra spin is derived.
        float rearWheelSpinAngle;

        // Rear wing DRS flap: found lazily by name so RaceManager's car builder
        // doesn't need to hand off another reference explicitly.
        Transform rearWingFlap;
        bool rearWingFlapSearched;
        float wingFlapOpenAmount;
        static readonly Quaternion WingFlapClosedRotation = Quaternion.Euler(24f, 0f, 0f);
        static readonly Quaternion WingFlapOpenRotation = Quaternion.Euler(1f, 0f, 0f);

        // Skid trails behind the front wheels, only visible while a real lockup
        // event (see TyreState.LockupSeverity) is in progress. Anchored to loose
        // transforms parented under the car body rather than under the wheel
        // pivots themselves, since the wheel pivots are what UpdateWheels spins -
        // a trail parented directly under a spinning transform would swing around
        // with the wheel instead of tracking the car's path.
        Transform skidTrailLeftAnchor;
        Transform skidTrailRightAnchor;
        TrailRenderer skidTrailLeft;
        TrailRenderer skidTrailRight;
        bool skidTrailsInitialized;
        static Material sharedSkidMaterial;

        // Brake discs stay hot-looking for a moment after the pedal releases
        // rather than snapping cold instantly; a faint tint on the rims (shared
        // across all four wheels, see rimMaterial below) catches the same glow.
        float brakeGlowHeat;
        Material rimMaterial;
        bool rimMaterialSearched;

        // Tracks how long the disc has been sitting in its hottest instantaneous
        // band (see UpdateBrakeGlow) - a long, sustained heavy-braking zone builds
        // this up and pushes the displayed colour hotter than a single hard-but-brief
        // dab would reach, then relaxes quickly once the pedal comes off. A cheap
        // stand-in for real thermal mass rather than an actual temperature model.
        float sustainedBrakeTimer;

        // Tyre compound look (soft/medium/hard/inter/wet), applied once per
        // compound rather than every frame - only reapplied if a pit stop
        // actually changes the compound underneath us.
        Material tyreMaterial;
        bool tyreMaterialSearched;
        bool tyreLookApplied;
        TyreCompound appliedTyreCompound;
        // The compound's own "clean" colour, cached whenever GetTyreLook is
        // reapplied - UpdateSurfaceGrime below blends away from THIS rather
        // than a hardcoded constant, so the dirt overlay always fades back to
        // the correct compound tint instead of some fixed reference colour.
        Color appliedTyreColor;

        // Bodywork picks up a wet sheen in the rain rather than staying one
        // fixed dry finish all race; primaryMaterial is shared across most of
        // the car's panels (see CreateOpenWheelCar), so one lookup covers them.
        Material bodyMaterial;
        bool bodyMaterialSearched;
        float baseBodySmoothness = -1f;

        // Off-track/marbles grime: builds while the car is running off the
        // racing line (the same IsOffTrackSlowdown signal CameraRig's
        // off-track shake and VehicleEffects' dust emitter already key off),
        // and slowly shakes back off once back on clean tarmac rather than
        // wiping instantly - a brief excursion still shows for a while. Reset
        // to zero on a pit stop (fresh tyres, car wiped down) in
        // BeginPitStopVisual below.
        float offTrackGrime;
        static readonly Color TyreGrimeColor = new Color(0.16f, 0.13f, 0.07f);

        // Front wing droop under accumulated front-end damage: found lazily by
        // name the same way the rain light/rim/tyre materials above are, and
        // its undamaged local transform captured on first lookup so the droop
        // can be blended back toward "as built" after a pit repair instead of
        // only ever sagging further.
        Transform frontWingBase;
        bool frontWingSearched;
        Quaternion frontWingRestRotation;
        Vector3 frontWingRestPosition;

        // Flat-spotted tyres thump once per revolution rather than vibrating
        // smoothly, so this drives a small wheel-pivot bounce synced to
        // wheelSpinAngle (the same angle UpdateWheels spins the mesh with)
        // instead of a separate timer that could drift out of sync with the
        // visible rotation.
        bool wheelRestPositionsCaptured;
        Vector3 flRestLocalPos, frRestLocalPos, rlRestLocalPos, rrRestLocalPos;

        // Rear diffuser sags under floor damage the same way the front wing
        // droops under nose damage above - same lazy-find/rest-pose/ease-back
        // idiom, just driven by Damage.floor instead of Damage.frontWing.
        Transform rearDiffuser;
        bool rearDiffuserSearched;
        Quaternion rearDiffuserRestRotation;
        Vector3 rearDiffuserRestPosition;

        // Above the front-wing-droop/rear-diffuser-sag tiers, a heavily beaten
        // car (high accumulated Damage.OverallPercent, not tied to any single
        // part) also picks up a scuffed/scraped bodywork look - a soft dark
        // smudge decal on the nose and each sidepod flank that scales in from
        // hidden as the damage total climbs and eases back out again after a
        // repair, the same rest-pose/ease idiom as the wing/floor damage
        // above, just driven by the overall percentage instead of one part.
        bool damageScuffBuilt;
        Transform noseScuffDecal;
        Transform sidepodScuffDecalLeft;
        Transform sidepodScuffDecalRight;
        Vector3 noseScuffFullScale;
        Vector3 sidepodScuffFullScale;
        float scuffVisibility;
        static Material sharedScuffMaterial;

        // Suspension arms sink a hair under load - nose dives under braking,
        // rear squats under power - a cheap position-only cue rather than
        // re-deriving each arm's endpoints/orientation from scratch. Gathered
        // via GetComponentsInChildren since "suspension arm" is reused across
        // all eight arms (RaceManager.CreateSuspensionArm), unlike the other
        // lazy lookups above which each target one uniquely named transform.
        Transform[] suspensionArms;
        Vector3[] suspensionArmRestPositions;
        bool suspensionArmsSearched;
        bool suspensionArmsCaptured;

        // Brake caliper glows faintly with the same heat ramp as the disc/rim
        // above - shared across all four wheels the same way rimMaterial is,
        // so one lazy lookup lights every caliper.
        Material caliperMaterial;
        bool caliperMaterialSearched;

        // One-time paint/carbon/metal contrast pass - see ApplyMaterialContrastPass.
        bool materialContrastSearched;

        // Center-lock hub cap + lug ring built once per car directly under each
        // wheel pivot, mirroring RaceManager's own "wheel cover" cylinder
        // convention (outboard X offset, Euler(0,0,90) rotation) so it spins
        // correctly with the wheel without any extra per-frame work.
        bool wheelHubDetailBuilt;

        // Front wing upper flap flexes back a touch under aero load at speed -
        // the same "flexi-wing" aeroelastic cue real front wings show, driven
        // continuously by speed rather than a discrete on/off like the DRS flap
        // above. Targets a different transform than UpdateFrontWingDamage
        // (which owns "front wing base") so the two never fight over the same
        // local rotation.
        Transform frontWingUpperFlap;
        bool frontWingUpperFlapSearched;
        Quaternion frontWingUpperFlapRestRotation;

        // Cheap ground-contact shadow: a flat, soft-edged dark blob tracking the
        // car's footprint so it doesn't read as floating over bright tarmac -
        // no such trick exists yet for player/AI cars (only the real-time
        // shadow map, whose resolution is shared across the whole grid). Built
        // once like the other one-shot detail passes in this file, then kept
        // level (world up) and only following the car's yaw each frame rather
        // than parented rigidly, since a naive child would pick up any residual
        // chassis roll/pitch and visibly tilt out of the ground plane.
        Transform contactShadow;
        bool contactShadowBuilt;
        static Texture2D contactShadowTexture;
        static Material sharedContactShadowMaterial;

        // Endplate accent flashes (front + rear), halo mounting-point detail,
        // cockpit helmet/harness detail, and the livery accent-pattern variety
        // below are all one-shot additive passes over geometry RaceManager
        // already built, following the same lazy-find/build-once idiom as
        // EnsureWheelHubDetail and ApplyMaterialContrastPass.
        bool endplateAccentBuilt;
        bool haloMountDetailBuilt;
        bool cockpitDetailBuilt;
        bool liveryAccentBuilt;

        // Halo front pillar + side arcs (reads as a curved tube hoop rather
        // than the flat "halo rim" plate alone), rear light housing/bezel,
        // and front/rear wing Gurney trim strips - same one-shot additive
        // idiom as the passes above, added purely in this file so none of
        // RaceManager's own car-builder code needs to change.
        bool haloRingDetailBuilt;
        bool rearLightDetailBuilt;
        bool wingTrimDetailBuilt;

        // Sidepod undercut/intake shaping, front wing endplate outwash flicks and
        // rear wing endplate strakes - more additive one-shot geometry passes over
        // RaceManager's existing sidepod/wing endplate transforms, same idiom.
        bool sidepodDetailBuilt;
        bool frontWingFlickBuilt;
        bool rearWingLouvreBuilt;

        // Panel-seam accent lines dropped across real bodywork joins (nose/cockpit,
        // sidepod inlet trailing edge, engine cover/airbox, gearbox) - same one-shot
        // additive idiom as the passes above.
        bool bodyPanelLineDetailBuilt;

        // Tread-block suggestion baked into the tyre material's texture rather
        // than any extra geometry - applied once, independently of
        // UpdateTyreCompoundLook's own per-compound colour/sheen reapplication,
        // since the texture itself never changes with compound.
        bool tyreTreadTextureApplied;
        static Texture2D sharedTreadTexture;

        // Rim spoke suggestion, same idea as the tyre tread texture above but for
        // rimMaterial - a handful of bright/dark wedges baked once and tiled a
        // single time around the rim/wheel-cover UV so a bare disc-like rim reads
        // as a spoked wheel design instead of a flat painted circle.
        bool rimSpokeTextureApplied;
        static Texture2D sharedRimSpokeTexture;

        // Pit-stop tyre-change animation: a real, visible swap synced to the
        // service timer instead of the car just sitting still for a few
        // seconds. Four simple procedural "wheel gun" props (one per corner)
        // pop in, the tyre meshes shrink away ("old set pulled") then pop back
        // to full size with the new compound's look already applied ("fresh
        // set fitted"), and the props retract - all driven off one 0-1
        // progress value so the whole thing always finishes exactly when the
        // service timer does, however long that stop happens to be.
        bool pitStopAnimActive;
        float pitStopAnimDuration;
        float pitStopAnimTimer;
        int pitStopAudioPhase;
        Transform[] pitStopWheelTyres;
        // Post-pit tyre-scaling bug fix: the tyre mesh's authored local scale
        // is NOT (1,1,1) - CreateWheelPart builds "open wheel" as a squashed
        // cylinder (roughly 0.62 x 0.24 x 0.62, see RaceManager.cs) to get the
        // tyre's actual proportions. The old animation scaled toward/away from
        // Vector3.one directly and restored to Vector3.one at the end, which
        // silently threw away that authored shape - after the FIRST pit stop
        // every tyre was left at a full (1,1,1) cube-ish scale (visibly much
        // wider, since the real width axis was only 0.24). Capturing each
        // wheel's real original scale once, before any animation ever runs,
        // and always scaling/restoring relative to THIS (never a hardcoded
        // constant) means repeated stops in the same race can never compound
        // an error either.
        Vector3[] pitStopWheelRestScale;
        bool pitStopWheelTyresFound;
        GameObject[] pitStopGunProps;
        static readonly Vector3[] PitStopWheelLocalOffsets = new Vector3[]
        {
            new Vector3(-0.62f, 0f, 1.35f),
            new Vector3(0.62f, 0f, 1.35f),
            new Vector3(-0.62f, 0f, -1.35f),
            new Vector3(0.62f, 0f, -1.35f)
        };

        static readonly Color GlowColor = new Color(1f, 0.06f, 0.04f);
        static readonly Color DiscGlowColor = new Color(1f, 0.32f, 0.05f);
        static readonly Color DiscCoolColor = new Color(0.42f, 0.05f, 0.02f);
        static readonly Color DiscPeakColor = new Color(1f, 0.86f, 0.58f);

        public void Initialize(VehicleController controller, Material lightMaterial)
        {
            vehicle = controller;
            brakeLightMaterial = lightMaterial;
            if (brakeLightMaterial != null)
            {
                brakeLightMaterial.EnableKeyword("_EMISSION");
            }
        }

        public void SetWheels(Transform fl, Transform fr, Transform rl, Transform rr)
        {
            frontLeft = fl;
            frontRight = fr;
            rearLeft = rl;
            rearRight = rr;
        }

        // Called once by RaceManager.BeginPitStop with the exact same duration
        // the stationary service timer uses, so the animation always finishes
        // in lockstep with the gameplay stop regardless of how long it rolls
        // (1.8-3.0s per the pit-stop brief).
        public void BeginPitStopVisual(float duration)
        {
            pitStopAnimActive = true;
            pitStopAnimDuration = Mathf.Max(0.4f, duration);
            pitStopAnimTimer = 0f;
            pitStopAudioPhase = 0;
            SpawnPitStopGunProps();

            // The crew wipes the car down and fits a clean set of tyres during
            // every stop, so any off-track dirt/marbles accumulated so far is
            // gone - the grime overlay should ease back in from zero afterward
            // exactly like a real washed car, not carry the old build-up over.
            offTrackGrime = 0f;
        }

        void EnsurePitStopWheelTyresFound()
        {
            if (pitStopWheelTyresFound)
            {
                return;
            }

            pitStopWheelTyresFound = true;
            pitStopWheelTyres = new Transform[4];
            pitStopWheelRestScale = new Vector3[4];
            Transform[] pivots = { frontLeft, frontRight, rearLeft, rearRight };
            for (int i = 0; i < 4; i++)
            {
                pitStopWheelTyres[i] = pivots[i] != null ? pivots[i].Find("open wheel") : null;
                pitStopWheelRestScale[i] = pitStopWheelTyres[i] != null ? pitStopWheelTyres[i].localScale : Vector3.one;
            }
        }

        void SpawnPitStopGunProps()
        {
            EnsurePitStopWheelTyresFound();
            if (pitStopGunProps == null)
            {
                pitStopGunProps = new GameObject[4];
            }

            Transform[] pivots = { frontLeft, frontRight, rearLeft, rearRight };
            for (int i = 0; i < 4; i++)
            {
                if (pitStopGunProps[i] != null || pivots[i] == null)
                {
                    continue;
                }

                // A plain generic cylinder standing in for a wheel-gun/mechanic
                // prop - deliberately simple geometry, no branded/real-world
                // pit-crew asset, just enough to read as "someone is working on
                // this corner" from the chase camera.
                GameObject gun = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                gun.name = "Pit stop wheel gun";
                Object.Destroy(gun.GetComponent<Collider>());
                gun.transform.SetParent(transform, false);
                gun.transform.localPosition = pivots[i].localPosition + PitStopWheelLocalOffsets[i];
                gun.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                gun.transform.localScale = new Vector3(0.11f, 0.34f, 0.11f);
                Renderer renderer = gun.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = F1Game.Rendering.ShaderCompat.CreateLitMaterial();
                    renderer.sharedMaterial.color = new Color(0.85f, 0.1f, 0.08f);
                }

                pitStopGunProps[i] = gun;
            }
        }

        void ClearPitStopGunProps()
        {
            if (pitStopGunProps == null)
            {
                return;
            }

            for (int i = 0; i < pitStopGunProps.Length; i++)
            {
                if (pitStopGunProps[i] != null)
                {
                    Object.Destroy(pitStopGunProps[i]);
                    pitStopGunProps[i] = null;
                }
            }
        }

        void UpdatePitStopAnimation()
        {
            if (!pitStopAnimActive)
            {
                return;
            }

            EnsurePitStopWheelTyresFound();
            pitStopAnimTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(pitStopAnimTimer / pitStopAnimDuration);

            // Old set off (0-40%), swap moment (~45%), new set on (45-75%),
            // props clear and settle (75-100%) - always relative to progress,
            // so a 1.8s and a 3.0s stop both read the same shape, just faster
            // or slower.
            float wheelScale;
            if (progress < 0.4f)
            {
                wheelScale = Mathf.Lerp(1f, 0.1f, progress / 0.4f);
            }
            else if (progress < 0.75f)
            {
                wheelScale = Mathf.Lerp(0.1f, 1f, (progress - 0.4f) / 0.35f);
            }
            else
            {
                wheelScale = 1f;
            }

            if (pitStopWheelTyres != null)
            {
                for (int i = 0; i < pitStopWheelTyres.Length; i++)
                {
                    if (pitStopWheelTyres[i] != null)
                    {
                        pitStopWheelTyres[i].localScale = pitStopWheelRestScale[i] * wheelScale;
                    }
                }
            }

            if (pitStopAudioPhase == 0 && progress >= 0.02f)
            {
                pitStopAudioPhase = 1;
                SimpleAudioManager.PlayPitGun(transform.position);
            }
            else if (pitStopAudioPhase == 1 && progress >= 0.42f)
            {
                pitStopAudioPhase = 2;
                // New set going on - a second, shorter gun burst per corner.
                SimpleAudioManager.PlayPitGun(transform.position + transform.forward * 0.1f);
            }
            else if (pitStopAudioPhase == 2 && progress >= 0.8f)
            {
                pitStopAudioPhase = 3;
                SimpleAudioManager.PlayPitJackDown(transform.position);
            }

            if (progress >= 1f)
            {
                pitStopAnimActive = false;
                ClearPitStopGunProps();
                if (pitStopWheelTyres != null)
                {
                    for (int i = 0; i < pitStopWheelTyres.Length; i++)
                    {
                        if (pitStopWheelTyres[i] != null)
                        {
                            pitStopWheelTyres[i].localScale = pitStopWheelRestScale[i];
                        }
                    }
                }
            }
        }

        public void SetBrakeGlowMaterial(Material discMaterial)
        {
            brakeDiscMaterial = discMaterial;
            if (brakeDiscMaterial != null)
            {
                brakeDiscMaterial.EnableKeyword("_EMISSION");
            }
        }

        void Update()
        {
            // The car body is built before VehicleController is attached, so bind lazily.
            if (vehicle == null)
            {
                vehicle = GetComponent<VehicleController>();
            }

            if (vehicle == null)
            {
                return;
            }

            UpdateWheels();
            UpdatePitStopAnimation();
            UpdateFlatSpotWobble();
            UpdateBrakeGlow();
            UpdateRainLight();
            UpdateDrsFlap();
            UpdateAeroFlex();
            UpdateSkidTrails();
            UpdateTyreCompoundLook();
            UpdateSurfaceGrime();
            UpdateWetBodySheen();
            UpdateFrontWingDamage();
            UpdateFloorDamage();
            UpdateDamageScuffs();
            UpdateSuspensionFlex();
            UpdateContactShadow();
            ApplyMaterialContrastPass();
            EnsureWheelHubDetail();
            EnsureEndplateAccents();
            EnsureHaloMountDetail();
            EnsureCockpitDetail();
            EnsureLiveryAccentVariety();
            EnsureHaloRingDetail();
            EnsureRearLightDetail();
            EnsureWingTrimDetail();
            EnsureBodyPanelLineDetail();
            EnsureSidepodDetail();
            EnsureFrontWingEndplateFlick();
            EnsureRearWingEndplateLouvre();
            EnsureRimSpokeDetail();
        }

        // Front wing upper flap flexes back under aero load the faster the car
        // goes - quadratic so it stays essentially still at low speed and only
        // really shows itself on fast straights, eased rather than snapped so
        // it reads as load building rather than a mechanical twitch.
        void UpdateAeroFlex()
        {
            if (!frontWingUpperFlapSearched)
            {
                frontWingUpperFlapSearched = true;
                Transform found = transform.Find("front wing upper flap");
                if (found != null)
                {
                    frontWingUpperFlap = found;
                    frontWingUpperFlapRestRotation = found.localRotation;
                }
            }

            if (frontWingUpperFlap == null)
            {
                return;
            }

            float speed01 = Mathf.Clamp01(Mathf.Abs(vehicle.CurrentSpeedKph) / 320f);
            float flex = speed01 * speed01 * 5f;
            Quaternion target = frontWingUpperFlapRestRotation * Quaternion.Euler(-flex, 0f, 0f);
            frontWingUpperFlap.localRotation = Quaternion.Slerp(frontWingUpperFlap.localRotation, target, Time.deltaTime * 3f);
        }

        void UpdateDrsFlap()
        {
            if (!rearWingFlapSearched)
            {
                rearWingFlapSearched = true;
                Transform found = transform.Find("rear wing flap");
                if (found != null)
                {
                    rearWingFlap = found;
                }
            }

            if (rearWingFlap == null)
            {
                return;
            }

            wingFlapOpenAmount = Mathf.MoveTowards(wingFlapOpenAmount, vehicle.DrsActive ? 1f : 0f, Time.deltaTime * 6f);
            rearWingFlap.localRotation = Quaternion.Slerp(WingFlapClosedRotation, WingFlapOpenRotation, wingFlapOpenAmount);
        }

        void UpdateWheels()
        {
            float speedMps = vehicle.CurrentSpeedKph / 3.6f;
            float spinDelta = speedMps / WheelRadius * Mathf.Rad2Deg * Time.deltaTime;

            // Wheel spin is otherwise purely speed-derived, which never shows a real
            // lockup: a locked tyre stops rotating (or nearly does) while the car
            // keeps sliding. Blend the per-frame spin delta toward a near-stop the
            // more severe the current lockup is, instead of a full instant freeze,
            // so a partial lockup reads as reduced spin rather than a hard snap.
            float lockupSeverity = vehicle.Tyres != null ? vehicle.Tyres.LockupSeverity : 0f;
            if (lockupSeverity > 0f)
            {
                spinDelta *= Mathf.Lerp(1f, 0.05f, lockupSeverity);
            }

            wheelSpinAngle += spinDelta;
            wheelSpinAngle = Mathf.Repeat(wheelSpinAngle, 360f);

            // Rear wheelspin: OversteerAmount is throttle-modulated lateral slip
            // under low grip (VehicleController.ApplyForces), which is exactly the
            // "rear tyres overwhelming available traction under power" condition a
            // real wheelspin event is - there's no separate slip-ratio field on the
            // vehicle/tyre classes to read instead, so this reuses that existing
            // public signal (paired with EffectiveThrottle so a lifted throttle
            // never shows spin) rather than adding a new one. The rear wheels get
            // their own spin angle so they can visibly overspin past what road
            // speed alone implies while the fronts keep tracking speed normally.
            float wheelspinAmount = Mathf.Clamp01(vehicle.OversteerAmount * vehicle.EffectiveThrottle * 1.6f);
            float rearSpinDelta = spinDelta * (1f + wheelspinAmount * 2.4f);
            rearWheelSpinAngle += rearSpinDelta;
            rearWheelSpinAngle = Mathf.Repeat(rearWheelSpinAngle, 360f);

            float targetSteer = vehicle.CurrentCommand.steer * 16f;
            visualSteerAngle = Mathf.Lerp(visualSteerAngle, targetSteer, Time.deltaTime * 10f);

            // Steering wheel mirrors the live steer command (~95 degrees of
            // visual lock each way; negative z = clockwise from the driver's
            // seat for a right turn).
            if (!steeringWheelSearched)
            {
                steeringWheelSearched = true;
                Transform foundWheel = transform.Find("steering wheel");
                if (foundWheel != null)
                {
                    steeringWheel = foundWheel;
                    steeringWheelRestRotation = foundWheel.localRotation;
                }
            }

            if (steeringWheel != null)
            {
                float wheelTargetAngle = vehicle.CurrentCommand.steer * 95f;
                steeringWheelAngle = Mathf.Lerp(steeringWheelAngle, wheelTargetAngle, Time.deltaTime * 14f);
                steeringWheel.localRotation = steeringWheelRestRotation * Quaternion.Euler(0f, 0f, -steeringWheelAngle);
            }

            Quaternion spin = Quaternion.Euler(wheelSpinAngle, 0f, 0f);
            Quaternion rearSpin = Quaternion.Euler(rearWheelSpinAngle, 0f, 0f);
            Quaternion steer = Quaternion.Euler(0f, visualSteerAngle, 0f);
            if (frontLeft != null)
            {
                frontLeft.localRotation = steer * spin;
            }

            if (frontRight != null)
            {
                frontRight.localRotation = steer * spin;
            }

            if (rearLeft != null)
            {
                rearLeft.localRotation = rearSpin;
            }

            if (rearRight != null)
            {
                rearRight.localRotation = rearSpin;
            }
        }

        void UpdateBrakeGlow()
        {
            if (brakeDiscMaterial == null)
            {
                return;
            }

            // Discs glow only under real braking energy: hard pedal at speed.
            float targetHeat = vehicle.EffectiveBrake * Mathf.InverseLerp(90f, 300f, Mathf.Abs(vehicle.CurrentSpeedKph));

            // Quick to heat up under real braking; cooling itself slows as heat
            // drops (radiative-style falloff) so a glowing-hot disc sheds most
            // of its heat fast at first and then lingers as a faint afterglow,
            // instead of fading at one constant rate all the way to cold. Cool
            // rate ceiling nudged up slightly so a disc that's been glowing hot
            // sheds its peak a touch more briskly once the pedal lifts, while the
            // afterglow tail (low end of the range) is untouched.
            float coolRate = Mathf.Lerp(0.32f, 1.55f, brakeGlowHeat);
            float rate = targetHeat > brakeGlowHeat ? 9f : coolRate;
            brakeGlowHeat = Mathf.MoveTowards(brakeGlowHeat, targetHeat, Time.deltaTime * rate);

            // A long, sustained heavy-braking zone (a real "into Turn 1 from top
            // speed" stop) builds this timer up and pushes the displayed colour
            // hotter than brakeGlowHeat's own instantaneous ramp would reach on
            // its own, reading as the disc's thermal mass catching up under
            // continued load - a single hard-but-brief dab never sustains it long
            // enough to matter. Relaxes several times faster than it builds so it
            // never lingers once the braking zone ends.
            if (brakeGlowHeat > 0.82f)
            {
                sustainedBrakeTimer += Time.deltaTime;
            }
            else
            {
                sustainedBrakeTimer = Mathf.Max(0f, sustainedBrakeTimer - Time.deltaTime * 2.5f);
            }

            float overheatBoost = Mathf.Clamp01(sustainedBrakeTimer / 2.2f) * 0.4f;
            float displayHeat = Mathf.Min(1f, brakeGlowHeat + overheatBoost);

            Color rampColor = DiscTemperatureColor(displayHeat);
            brakeDiscMaterial.SetColor("_EmissionColor", rampColor * displayHeat * 1.4f);
            UpdateRimHighlight(rampColor, displayHeat);
            UpdateCaliperHighlight(rampColor, displayHeat);
        }

        // Colour, not just brightness, shifts with heat - dull red under a
        // light dab, through the old fixed orange, up to a near-white peak
        // under sustained heavy braking, closer to a real carbon disc.
        static Color DiscTemperatureColor(float heat)
        {
            if (heat < 0.55f)
            {
                return Color.Lerp(DiscCoolColor, DiscGlowColor, Mathf.InverseLerp(0f, 0.55f, heat));
            }

            return Color.Lerp(DiscGlowColor, DiscPeakColor, Mathf.InverseLerp(0.55f, 1f, heat));
        }

        // The rim/cover material is shared across all four wheels (RaceManager
        // builds one rim Material per car and hands the same reference to every
        // CreateWheel call), so tinting it once here lights up every wheel. Found
        // lazily by name, same approach as the DRS flap above, since this file
        // doesn't otherwise get a wheel Renderer reference. Kept to a faint
        // glint rather than a full disc-strength glow, and now follows the same
        // temperature colour ramp as the disc itself instead of one fixed hue.
        void UpdateRimHighlight(Color rampColor, float heat)
        {
            if (!rimMaterialSearched && frontLeft != null)
            {
                rimMaterialSearched = true;
                Transform rim = frontLeft.Find("wheel rim");
                if (rim != null)
                {
                    Renderer rimRenderer = rim.GetComponent<Renderer>();
                    if (rimRenderer != null)
                    {
                        rimMaterial = rimRenderer.sharedMaterial;
                        if (rimMaterial != null)
                        {
                            rimMaterial.EnableKeyword("_EMISSION");
                        }
                    }
                }
            }

            if (rimMaterial == null)
            {
                return;
            }

            rimMaterial.SetColor("_EmissionColor", rampColor * heat * 0.35f);
        }

        // Caliper is a direct child of the car root (not the wheel pivot, so it
        // doesn't spin - see RaceManager.CreateWheel), and all four wheels share
        // one caliperMaterial instance the same way rimMaterial is shared, so
        // finding the first "brake caliper" by name lights every one. Bumped to
        // a raw cast-metal look (high metallic, low gloss) the first time it's
        // found, distinct from the disc's polished carbon-ceramic finish.
        void UpdateCaliperHighlight(Color rampColor, float heat)
        {
            if (!caliperMaterialSearched)
            {
                caliperMaterialSearched = true;
                Transform caliper = transform.Find("brake caliper");
                if (caliper != null)
                {
                    Renderer caliperRenderer = caliper.GetComponent<Renderer>();
                    if (caliperRenderer != null)
                    {
                        caliperMaterial = caliperRenderer.sharedMaterial;
                        if (caliperMaterial != null)
                        {
                            caliperMaterial.EnableKeyword("_EMISSION");
                            caliperMaterial.SetFloat("_Metallic", 0.78f);
                            F1Game.Rendering.ShaderCompat.SetSmoothness(caliperMaterial, 0.42f);
                        }
                    }
                }
            }

            if (caliperMaterial == null)
            {
                return;
            }

            caliperMaterial.SetColor("_EmissionColor", rampColor * heat * 0.22f);
        }

        void UpdateRainLight()
        {
            if (brakeLightMaterial == null)
            {
                return;
            }

            if (startupSequenceActive)
            {
                startupSequenceTimer -= Time.deltaTime;
                float elapsed = StartupSequenceDuration - startupSequenceTimer;
                bool blinkOn = elapsed < StartupSequenceDuration - 0.25f && Mathf.FloorToInt(elapsed / 0.16f) % 2 == 0;
                brakeLightMaterial.SetColor("_EmissionColor", GlowColor * (blinkOn ? 1.4f : 0f));
                if (startupSequenceTimer <= 0f)
                {
                    startupSequenceActive = false;
                }

                return;
            }

            float brake = vehicle.EffectiveBrake;
            bool harvesting = vehicle.ErsBattery > previousErs + 0.0001f && Mathf.Abs(vehicle.CurrentSpeedKph) > 60f;
            previousErs = vehicle.ErsBattery;

            float intensity = brake;
            if (harvesting && brake < 0.2f)
            {
                intensity = Mathf.PingPong(Time.time * 6f, 1f) * 0.6f;
            }

            // Real wet-weather running keeps the rain light lit throughout, not just
            // under braking - it's a visibility aid for cars behind, mandatory any
            // time the track is wet. Braking/harvesting still glow brighter on top
            // of this floor rather than being masked by it.
            bool wet = vehicle.Weather == WeatherState.LightRain || vehicle.Weather == WeatherState.HeavyRain;
            if (wet)
            {
                intensity = Mathf.Max(intensity, 0.32f);
            }

            // Night/twilight rounds (Singapore/Vegas/Qatar-style night races,
            // Abu-Dhabi-style twilight) light the whole scene much dimmer -
            // see RaceManager.CreateLighting, which pushes RenderSettings'
            // ambient/fog noticeably darker for those than for a normal dry
            // or even wet daytime session. There's no per-car day/night field
            // to read directly, so this keys off the same global RenderSettings
            // signal CameraRig's own speed-vignette already reads (fogColor) -
            // a dim ambient floor keeps the light visibly lit at a floor level
            // even off the brake/harvest/wet triggers, and a brightness boost
            // on top makes sure the glow actually reads against a much darker
            // background instead of getting lost in it.
            bool dimAtmosphere = IsDimAtmosphere();
            if (dimAtmosphere)
            {
                intensity = Mathf.Max(intensity, 0.24f);
            }

            float visibilityBoost = dimAtmosphere ? 1.35f : 1f;
            brakeLightMaterial.SetColor("_EmissionColor", GlowColor * Mathf.Clamp01(intensity) * 1.6f * visibilityBoost);
        }

        // Cheap night/twilight heuristic: RaceManager.CreateLighting sets the
        // ambient sky colour noticeably darker for night (~0.14 average) and
        // twilight (~0.28 average) tracks than it does for a normal dry
        // (~0.58) or even wet (~0.35) daytime session - see that method's
        // "night"/"twilight" branches. Reading RenderSettings directly here
        // rather than adding a new field means this works for any track
        // without touching RaceManager/TrackManager at all.
        static bool IsDimAtmosphere()
        {
            Color sky = RenderSettings.ambientSkyColor;
            float brightness = (sky.r + sky.g + sky.b) / 3f;
            return brightness < 0.32f;
        }

        void UpdateSkidTrails()
        {
            EnsureSkidTrails();
            if (!skidTrailsInitialized || vehicle.Tyres == null)
            {
                return;
            }

            float severity = Mathf.Clamp01(vehicle.Tyres.LockupSeverity);
            bool active = severity > 0.1f;
            if (skidTrailLeft != null && frontLeft != null)
            {
                skidTrailLeftAnchor.position = frontLeft.position;
                skidTrailLeft.emitting = active;
                if (active)
                {
                    ApplySkidTrailSeverity(skidTrailLeft, severity);
                }
            }

            if (skidTrailRight != null && frontRight != null)
            {
                skidTrailRightAnchor.position = frontRight.position;
                skidTrailRight.emitting = active;
                if (active)
                {
                    ApplySkidTrailSeverity(skidTrailRight, severity);
                }
            }
        }

        // Widens, darkens and persists longer with LockupSeverity so a small
        // lockup leaves a faint, quickly-fading mark and a big one leaves an
        // obvious dark stripe that lingers on the tarmac, instead of one fixed
        // width/opacity/duration regardless of how hard the tyre is locked.
        // Performance fix: this used to allocate a brand-new Gradient (plus two
        // fresh key arrays) every single call - with every locking-wheel car
        // on track calling this every frame, that was real, avoidable GC
        // garbage piling up during exactly the moments (heavy braking, several
        // cars fighting for a corner) the frame rate can least afford it. A
        // single cached Gradient/key-array set is reused and just has its
        // alpha value overwritten each call; colorGradient's setter copies the
        // data out at assignment time, so it's safe to share across every
        // trail on every car.
        static Gradient sharedSkidGradient;
        static GradientColorKey[] sharedSkidColorKeys;
        static GradientAlphaKey[] sharedSkidAlphaKeys;

        static void ApplySkidTrailSeverity(TrailRenderer trail, float severity)
        {
            trail.startWidth = Mathf.Lerp(0.07f, 0.32f, severity);
            trail.endWidth = Mathf.Lerp(0.01f, 0.05f, severity);
            trail.time = Mathf.Lerp(1f, 2.6f, severity);

            if (sharedSkidGradient == null)
            {
                sharedSkidGradient = new Gradient();
                sharedSkidColorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) };
                sharedSkidAlphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, 1f) };
            }

            float alpha = Mathf.Lerp(0.14f, 0.7f, severity);
            sharedSkidAlphaKeys[0].alpha = alpha;
            sharedSkidAlphaKeys[1].alpha = 0f;
            sharedSkidGradient.SetKeys(sharedSkidColorKeys, sharedSkidAlphaKeys);
            trail.colorGradient = sharedSkidGradient;
        }

        // Lazily builds the skid trail renderers the first time we have both wheel
        // references and a confirmed particles-enabled setting. VehicleVisuals is
        // always attached (unlike VehicleEffects, which is only added at all when
        // Settings.Current.particlesEnabled is true) so this new trail work needs
        // its own explicit guard against that same setting.
        void EnsureSkidTrails()
        {
            if (skidTrailsInitialized)
            {
                return;
            }

            if (frontLeft == null || frontRight == null)
            {
                return;
            }

            if (vehicle.Settings != null && !vehicle.Settings.particlesEnabled)
            {
                return;
            }

            skidTrailsInitialized = true;
            skidTrailLeftAnchor = new GameObject("Skid trail anchor FL").transform;
            skidTrailLeftAnchor.SetParent(transform, false);
            skidTrailLeft = CreateSkidTrail(skidTrailLeftAnchor);

            skidTrailRightAnchor = new GameObject("Skid trail anchor FR").transform;
            skidTrailRightAnchor.SetParent(transform, false);
            skidTrailRight = CreateSkidTrail(skidTrailRightAnchor);
        }

        static TrailRenderer CreateSkidTrail(Transform anchor)
        {
            TrailRenderer trail = anchor.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = GetSkidMaterial();
            trail.time = 1.5f;
            trail.startWidth = 0.22f;
            trail.endWidth = 0.02f;
            trail.minVertexDistance = 0.05f;
            trail.emitting = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            return trail;
        }

        // This file has no existing material-creation helper of its own (unlike
        // VehicleEffects.GetParticleMaterial(), which this mirrors), so build a
        // minimal shared one directly - short, dark rubber mark. Alpha is left
        // at full here since ApplySkidTrailSeverity now drives the real, per-
        // trail opacity through each TrailRenderer's own colorGradient.
        static Material GetSkidMaterial()
        {
            if (sharedSkidMaterial != null)
            {
                return sharedSkidMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            sharedSkidMaterial = new Material(shader);
            sharedSkidMaterial.color = new Color(0.05f, 0.05f, 0.05f, 1f);
            return sharedSkidMaterial;
        }

        // Soft/medium/hard/inter/wet each get a distinct tread tint and sheen -
        // found lazily by name on the front-left tyre mesh, same pattern as the
        // rim above, and re-applied only when the compound underneath actually
        // changes (e.g. a pit stop) rather than every frame.
        void UpdateTyreCompoundLook()
        {
            if (!tyreMaterialSearched && frontLeft != null)
            {
                tyreMaterialSearched = true;
                Transform tyre = frontLeft.Find("open wheel");
                if (tyre != null)
                {
                    Renderer tyreRenderer = tyre.GetComponent<Renderer>();
                    if (tyreRenderer != null)
                    {
                        tyreMaterial = tyreRenderer.sharedMaterial;
                    }
                }
            }

            if (tyreMaterial != null && !tyreTreadTextureApplied)
            {
                tyreTreadTextureApplied = true;
                tyreMaterial.mainTexture = GetTreadTexture();
                tyreMaterial.mainTextureScale = new Vector2(10f, 1f);
            }

            if (tyreMaterial == null || vehicle.Tyres == null)
            {
                return;
            }

            TyreCompound compound = vehicle.Tyres.Compound;
            if (tyreLookApplied && compound == appliedTyreCompound)
            {
                return;
            }

            tyreLookApplied = true;
            appliedTyreCompound = compound;
            Color color;
            float metallic;
            float smoothness;
            GetTyreLook(compound, out color, out metallic, out smoothness);
            appliedTyreColor = color;
            tyreMaterial.color = Color.Lerp(color, TyreGrimeColor, offTrackGrime * 0.65f);
            tyreMaterial.SetFloat("_Metallic", metallic);
            F1Game.Rendering.ShaderCompat.SetSmoothness(tyreMaterial, smoothness);
        }

        // Off-track running (marbles, gravel, dust) leaves the tyres and
        // bodywork visibly duller/dirtier rather than always looking showroom
        // clean - offTrackGrime tracks accumulated exposure (0-1) and this
        // method re-tints the tyre compound colour toward TyreGrimeColor
        // every frame so the dirt reads immediately even between the rarer
        // compound-change repaints UpdateTyreCompoundLook itself does above.
        // UpdateWetBodySheen folds the same grime value into its own
        // smoothness target for the bodywork side of this effect.
        void UpdateSurfaceGrime()
        {
            if (vehicle.IsOffTrackSlowdown)
            {
                offTrackGrime = Mathf.Min(1f, offTrackGrime + Time.deltaTime * 0.12f);
            }
            else
            {
                offTrackGrime = Mathf.Max(0f, offTrackGrime - Time.deltaTime * 0.025f);
            }

            if (tyreMaterial != null && tyreLookApplied)
            {
                tyreMaterial.color = Color.Lerp(appliedTyreColor, TyreGrimeColor, offTrackGrime * 0.65f);
            }
        }

        // Deliberately subtle undertones and sheen differences rather than the
        // bright sidewall bands real tyres carry - enough to tell compounds
        // apart at a glance without reproducing any official colour marking.
        static void GetTyreLook(TyreCompound compound, out Color color, out float metallic, out float smoothness)
        {
            // Per-compound look lives in the engine-free CarVisualCurves (same numbers,
            // tested); the enum maps to the project's compound code (Soft 0 .. Wet 4).
            F1Game.Core.CarVisualCurves.TyreLook((int)compound, out float r, out float g, out float b,
                out metallic, out smoothness);
            color = new Color(r, g, b);
        }

        // Suggests circumferential tread blocks without any extra geometry - a
        // small strip of alternating light/dark bands, multiplied into the
        // tyre's base colour and tiled several times around the wheel via
        // mainTextureScale (see UpdateTyreCompoundLook), so a spinning wheel
        // reads as moulded tread rather than a smooth slick. Uneven spacing
        // (a narrow groove every few pixels rather than an even 50/50 split)
        // avoids a barcode look. Shared across every car the same way the
        // particle soft-dot texture is - the pattern itself never varies.
        static Texture2D GetTreadTexture()
        {
            if (sharedTreadTexture != null)
            {
                return sharedTreadTexture;
            }

            // High-res version of the original 64x8 strip: same groove rhythm
            // (a narrow groove every 9 units), rendered at 8x the density with
            // anti-aliased groove walls and mipmaps so the tiled pattern stays
            // clean at speed instead of shimmering.
            const int width = 512;
            const int height = 64;
            const int cell = 72;      // 9 units * 8x scale
            const int grooveEnd = 16; // 2 units * 8x scale
            const float edge = 3f;    // AA falloff in pixels on each groove wall
            sharedTreadTexture = new Texture2D(width, height, TextureFormat.RGBA32, true);
            sharedTreadTexture.wrapMode = TextureWrapMode.Repeat;
            sharedTreadTexture.filterMode = FilterMode.Trilinear;
            sharedTreadTexture.anisoLevel = 4;
            for (int x = 0; x < width; x++)
            {
                int phase = x % cell;
                // Signed distance to the groove band [0, grooveEnd): negative
                // inside the groove, positive on the tread block.
                float distance = phase < grooveEnd
                    ? -Mathf.Min(phase, grooveEnd - 1 - phase) - 1f
                    : Mathf.Min(phase - grooveEnd, cell - phase);
                float blend = Mathf.Clamp01(0.5f + distance / edge);
                float shade = Mathf.Lerp(0.38f, 1f, blend);
                for (int y = 0; y < height; y++)
                {
                    sharedTreadTexture.SetPixel(x, y, new Color(shade, shade, shade, 1f));
                }
            }

            sharedTreadTexture.Apply(true);
            return sharedTreadTexture;
        }

        // Bodywork gains a wet sheen in the rain instead of one fixed dry
        // finish all race. primaryMaterial is shared across most panels (see
        // RaceManager.CreateOpenWheelCar), so finding it once via "survival
        // cell" brightens the whole car, not just one part.
        void UpdateWetBodySheen()
        {
            if (!bodyMaterialSearched)
            {
                bodyMaterialSearched = true;
                Transform cell = transform.Find("survival cell");
                if (cell != null)
                {
                    Renderer cellRenderer = cell.GetComponent<Renderer>();
                    if (cellRenderer != null)
                    {
                        bodyMaterial = cellRenderer.sharedMaterial;
                        if (bodyMaterial != null)
                        {
                            // Race paint reads flat/plasticky at RaceManager's
                            // default mid-metallic/mid-gloss (same band
                            // ApplyMaterialContrastPass already calls out for
                            // carbon/technical parts below) - a touch of
                            // metallic flake and a higher base clearcoat sells
                            // a real sprayed-and-lacquered panel under the
                            // directional sun/floodlights, applied once here
                            // (on top of whatever RaceManager set) rather than
                            // every frame, and kept modest so the wet-sheen
                            // boost below still has headroom to read on top.
                            // Premium visual pass: with a real skybox +
                            // reflection probe + tonemapped HDR now in place,
                            // paint can afford genuine metallic flake and a
                            // hard lacquer clearcoat without blowing out -
                            // the old 0.22/0.75 caps were tuned for a scene
                            // with nothing worth reflecting.
                            float currentMetallic = bodyMaterial.GetFloat("_Metallic");
                            bodyMaterial.SetFloat("_Metallic", Mathf.Min(0.55f, currentMetallic + 0.38f));
                            float currentGloss = F1Game.Rendering.ShaderCompat.GetSmoothness(bodyMaterial);
                            F1Game.Rendering.ShaderCompat.SetSmoothness(bodyMaterial, Mathf.Min(0.85f, currentGloss + 0.2f));
                        }
                    }
                }
            }

            if (bodyMaterial == null)
            {
                return;
            }

            if (baseBodySmoothness < 0f)
            {
                baseBodySmoothness = F1Game.Rendering.ShaderCompat.GetSmoothness(bodyMaterial);
            }

            bool wet = vehicle.Weather == WeatherState.LightRain || vehicle.Weather == WeatherState.HeavyRain;
            float targetSmoothness = wet ? Mathf.Min(0.97f, baseBodySmoothness + 0.14f) : baseBodySmoothness;

            // Off-track grime dulls the paint's clearcoat instead of only
            // affecting the tyres - a dust/marbles-caked panel scatters light
            // rather than reflecting cleanly. Floored well above zero so a
            // fully grimy car still has SOME clearcoat left rather than
            // reading as a totally matte respray.
            targetSmoothness = Mathf.Max(baseBodySmoothness - 0.18f, targetSmoothness - offTrackGrime * 0.16f);

            float currentSmoothness = F1Game.Rendering.ShaderCompat.GetSmoothness(bodyMaterial);
            F1Game.Rendering.ShaderCompat.SetSmoothness(bodyMaterial, Mathf.MoveTowards(currentSmoothness, targetSmoothness, Time.deltaTime * 0.6f));
        }

        // A knocked-about front wing visibly sags rather than staying rigid while
        // Damage.frontWing climbs toward destroyed - "front wing base" is the widest,
        // most visible of the front wing elements RaceManager builds (see
        // CreateOpenWheelCar), so droop reads clearly without needing to touch every
        // flap/endplate individually. Repairs (RepairPitDamage lowering frontWing)
        // ease the wing back toward its rest pose the same way it sagged, instead of
        // snapping instantly on either end.
        void UpdateFrontWingDamage()
        {
            if (!frontWingSearched)
            {
                frontWingSearched = true;
                Transform found = transform.Find("front wing base");
                if (found != null)
                {
                    frontWingBase = found;
                    frontWingRestRotation = found.localRotation;
                    frontWingRestPosition = found.localPosition;
                }
            }

            if (frontWingBase == null || vehicle.Damage == null)
            {
                return;
            }

            float damage = Mathf.Clamp01(vehicle.Damage.frontWing);
            Quaternion targetRotation = frontWingRestRotation * Quaternion.Euler(damage * 22f, 0f, 0f);
            Vector3 targetPosition = frontWingRestPosition + new Vector3(0f, -damage * 0.09f, 0f);
            frontWingBase.localRotation = Quaternion.Slerp(frontWingBase.localRotation, targetRotation, Time.deltaTime * 4f);
            frontWingBase.localPosition = Vector3.MoveTowards(frontWingBase.localPosition, targetPosition, Time.deltaTime * 0.3f);
        }

        // A scraped/cracked floor visibly drags rather than staying rigid while
        // Damage.floor climbs - mirrors UpdateFrontWingDamage exactly (lazy find,
        // rest pose captured once, eased toward a damage-driven target rather
        // than snapping) just targeting the rear diffuser and Damage.floor
        // instead of the nose and Damage.frontWing.
        void UpdateFloorDamage()
        {
            if (!rearDiffuserSearched)
            {
                rearDiffuserSearched = true;
                Transform found = transform.Find("rear diffuser");
                if (found != null)
                {
                    rearDiffuser = found;
                    rearDiffuserRestRotation = found.localRotation;
                    rearDiffuserRestPosition = found.localPosition;
                }
            }

            if (rearDiffuser == null || vehicle.Damage == null)
            {
                return;
            }

            float damage = Mathf.Clamp01(vehicle.Damage.floor);
            Quaternion targetRotation = rearDiffuserRestRotation * Quaternion.Euler(-damage * 14f, 0f, 0f);
            Vector3 targetPosition = rearDiffuserRestPosition + new Vector3(0f, -damage * 0.07f, 0f);
            rearDiffuser.localRotation = Quaternion.Slerp(rearDiffuser.localRotation, targetRotation, Time.deltaTime * 4f);
            rearDiffuser.localPosition = Vector3.MoveTowards(rearDiffuser.localPosition, targetPosition, Time.deltaTime * 0.3f);
        }

        // A third damage tier above the front-wing droop/floor sag: once
        // Damage.OverallPercent gets genuinely high, soft dark scuff decals
        // scale in on the nose and both sidepod flanks, reading as scraped/
        // scuffed bodywork rather than a pristine shell no matter how beaten
        // up the car mechanically is. Built once (lazy-find, same idiom as
        // every other one-shot detail pass in this file), then eased in/out
        // by scaling from a captured "hidden" (zero) scale up to each decal's
        // own authored full scale - explicitly stored per decal rather than
        // assuming Vector3.one, the same lesson the pit-stop tyre-scale fix
        // above exists to teach.
        void EnsureDamageScuffDecals()
        {
            if (damageScuffBuilt)
            {
                return;
            }

            if (transform.Find("survival cell") == null)
            {
                return;
            }

            damageScuffBuilt = true;
            Material scuffMaterial = GetScuffMaterial();
            noseScuffFullScale = new Vector3(0.32f, 0.22f, 1f);
            sidepodScuffFullScale = new Vector3(0.26f, 0.18f, 1f);

            noseScuffDecal = CreateScuffQuad(transform, "damage scuff nose", new Vector3(0f, 0.4f, 2.05f), Quaternion.Euler(84f, 0f, 0f));
            sidepodScuffDecalLeft = CreateScuffQuad(transform, "damage scuff sidepod left", new Vector3(-0.865f, 0.32f, -0.5f), Quaternion.Euler(0f, 90f, 0f));
            sidepodScuffDecalRight = CreateScuffQuad(transform, "damage scuff sidepod right", new Vector3(0.865f, 0.32f, -0.5f), Quaternion.Euler(0f, -90f, 0f));

            ApplyScuffMaterial(noseScuffDecal, scuffMaterial);
            ApplyScuffMaterial(sidepodScuffDecalLeft, scuffMaterial);
            ApplyScuffMaterial(sidepodScuffDecalRight, scuffMaterial);
        }

        void UpdateDamageScuffs()
        {
            EnsureDamageScuffDecals();
            if (!damageScuffBuilt || vehicle.Damage == null)
            {
                return;
            }

            // Only the last, worst stretch of the damage range earns a scuff -
            // the wing/floor sag tiers already cover moderate damage, this is
            // reserved for a genuinely beaten-up car.
            float targetVisibility = Mathf.InverseLerp(55f, 92f, vehicle.Damage.OverallPercent);
            scuffVisibility = Mathf.MoveTowards(scuffVisibility, targetVisibility, Time.deltaTime * 0.5f);

            ApplyScuffScale(noseScuffDecal, noseScuffFullScale, scuffVisibility);
            ApplyScuffScale(sidepodScuffDecalLeft, sidepodScuffFullScale, scuffVisibility);
            ApplyScuffScale(sidepodScuffDecalRight, sidepodScuffFullScale, scuffVisibility);
        }

        static void ApplyScuffScale(Transform decal, Vector3 fullScale, float amount)
        {
            if (decal == null)
            {
                return;
            }

            decal.localScale = fullScale * amount;
        }

        static void ApplyScuffMaterial(Transform decal, Material material)
        {
            if (decal == null)
            {
                return;
            }

            Renderer decalRenderer = decal.GetComponent<Renderer>();
            if (decalRenderer != null)
            {
                decalRenderer.sharedMaterial = material;
            }
        }

        // Plain quad primitive, parented directly to the car root at absolute
        // local coordinates (same reasoning as EnsureBodyPanelLineDetail -
        // nesting under the thin body panels themselves would inherit their
        // squashed local scale). Starts at zero scale (hidden) - the caller
        // stores the intended full scale separately and UpdateDamageScuffs
        // eases the actual localScale between the two, rather than this
        // helper assuming any particular "rest" size on its own.
        static Transform CreateScuffQuad(Transform parent, string objectName, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = objectName;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = localRotation;
            quad.transform.localScale = Vector3.zero;
            Renderer quadRenderer = quad.GetComponent<Renderer>();
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            Collider quadCollider = quad.GetComponent<Collider>();
            if (quadCollider != null)
            {
                Destroy(quadCollider);
            }

            return quad.transform;
        }

        // Reuses the contact shadow's cached soft radial falloff texture (see
        // GetContactShadowTexture) rather than baking a dedicated scratch
        // texture - at decal scale a soft dark smudge reads fine as scuffed/
        // scraped bodywork, and the texture asset is already resident either
        // way. A separate Material instance (own colour tint) so tinting this
        // grey/dirty doesn't affect the actual ground contact shadow, which
        // shares the same texture but needs to stay a neutral dark blob.
        static Material GetScuffMaterial()
        {
            if (sharedScuffMaterial != null)
            {
                return sharedScuffMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            sharedScuffMaterial = new Material(shader);
            sharedScuffMaterial.mainTexture = GetContactShadowTexture();
            sharedScuffMaterial.color = new Color(0.5f, 0.48f, 0.45f, 0.6f);
            return sharedScuffMaterial;
        }

        // Front arms dive under braking, rear arms squat under power - a small,
        // position-only offset from each arm's captured rest pose (no
        // re-derivation of the arm's endpoints/rotation/scale, which would need
        // the original a/b anchor points CreateSuspensionArm computed and never
        // exposes) so it reads as suspension travel without touching the wheel
        // or chassis transforms driven elsewhere.
        void EnsureSuspensionArms()
        {
            if (suspensionArmsSearched)
            {
                return;
            }

            suspensionArmsSearched = true;
            Transform[] all = GetComponentsInChildren<Transform>();
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "suspension arm")
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return;
            }

            suspensionArms = new Transform[count];
            suspensionArmRestPositions = new Vector3[count];
            int index = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "suspension arm")
                {
                    suspensionArms[index] = all[i];
                    suspensionArmRestPositions[index] = all[i].localPosition;
                    index++;
                }
            }

            suspensionArmsCaptured = true;
        }

        void UpdateSuspensionFlex()
        {
            EnsureSuspensionArms();
            if (!suspensionArmsCaptured)
            {
                return;
            }

            float dive = vehicle.EffectiveBrake * 0.03f;
            float squat = vehicle.EffectiveThrottle * 0.022f;

            // Kerb strikes jolt the whole chassis on its springs rather than
            // just costing lap time silently - IsOnKerb is the same signal
            // CameraRig's own kerb rumble/shake already reads, reused here so
            // the car's own suspension visibly reacts too. Scaled by speed
            // (a crawl over a kerb barely registers, a fast strike thumps
            // harder) and given per-arm noise offsets so all eight arms don't
            // move in dead lockstep, which would read as one rigid bounce
            // rather than a real kerb rattle.
            float kerbJoltAmplitude = 0f;
            if (vehicle.IsOnKerb)
            {
                float kerbSpeed01 = Mathf.InverseLerp(20f, 220f, Mathf.Abs(vehicle.CurrentSpeedKph));
                kerbJoltAmplitude = Mathf.Lerp(0.006f, 0.022f, kerbSpeed01);
            }

            float t = Time.time;
            for (int i = 0; i < suspensionArms.Length; i++)
            {
                Transform arm = suspensionArms[i];
                if (arm == null)
                {
                    continue;
                }

                // Front arms were built at positive local Z, rear at negative
                // (matches every other front/rear split in this file, e.g. the
                // wheel pivots and front wing).
                Vector3 rest = suspensionArmRestPositions[i];
                float droop = rest.z > 0f ? -dive : -squat;
                float kerbJolt = kerbJoltAmplitude > 0f ? (Mathf.PerlinNoise(t * 34f, i * 7.1f) - 0.5f) * kerbJoltAmplitude : 0f;
                Vector3 target = rest + new Vector3(0f, droop + kerbJolt, 0f);
                arm.localPosition = Vector3.Lerp(arm.localPosition, target, Time.deltaTime * 8f);
            }
        }

        // One-time material tuning pass so paint / carbon / technical-metal /
        // caliper-metal read as genuinely distinct finishes instead of landing
        // in the same mid-metallic/mid-gloss band RaceManager's base
        // CreateMaterial calls default to. Every material touched here is
        // unique per car (driverName-prefixed in RaceManager, not literally
        // shared across the grid) so tweaking the shared instance only affects
        // this car, the same safety property UpdateWetBodySheen already relies on.
        void ApplyMaterialContrastPass()
        {
            if (materialContrastSearched)
            {
                return;
            }

            materialContrastSearched = true;

            // Carbon floor/diffuser: low metallic, pushed glossier for a
            // wet-look clearcoat weave rather than a flat matte panel, plus a
            // baked weave texture (see GetCarbonWeaveTexture) so it reads as
            // moulded carbon-fibre up close instead of a flat painted plate -
            // the same "procedural texture over an existing material" idiom
            // GetTreadTexture/GetRimSpokeTexture already established.
            TuneMaterialContrast("carbon floor", 0.3f, 0.78f, GetCarbonWeaveTexture(), new Vector2(5f, 5f));

            // Livery accent flash (shared with every other secondary-colour
            // panel - endplates, rear wing, halo rim, engine stripe): a
            // metallic-flake paint finish to contrast against the primary's
            // plainer gloss.
            TuneMaterialContrast("left livery flash", 0.32f, 0.88f);

            // Technical/detail parts (shared across wing pylons, halo stays,
            // bargeboards, steering wheel): brushed metal rather than painted
            // plastic.
            TuneMaterialContrast("steering wheel", 0.7f, 0.6f);
        }

        void TuneMaterialContrast(string childName, float metallic, float smoothness)
        {
            TuneMaterialContrast(childName, metallic, smoothness, null, Vector2.zero);
        }

        void TuneMaterialContrast(string childName, float metallic, float smoothness, Texture2D texture, Vector2 textureScale)
        {
            Transform found = transform.Find(childName);
            if (found == null)
            {
                return;
            }

            Renderer foundRenderer = found.GetComponent<Renderer>();
            if (foundRenderer == null || foundRenderer.sharedMaterial == null)
            {
                return;
            }

            if (texture != null)
            {
                foundRenderer.sharedMaterial.mainTexture = texture;
                foundRenderer.sharedMaterial.mainTextureScale = textureScale;
            }

            foundRenderer.sharedMaterial.SetFloat("_Metallic", metallic);
            F1Game.Rendering.ShaderCompat.SetSmoothness(foundRenderer.sharedMaterial, smoothness);
        }

        // Center-lock hub cap + a small lug ring on each wheel, built once the
        // wheel pivots are known. Mirrors RaceManager.CreateWheel's own "wheel
        // cover" cylinder convention (outboard X offset off the pivot, Euler
        // (0,0,90) rotation so the cylinder's height axis lies along the axle)
        // so it sits flush on the outboard face and spins for free with the
        // pivot UpdateWheels already rotates - no extra per-frame work needed.
        void EnsureWheelHubDetail()
        {
            if (wheelHubDetailBuilt)
            {
                return;
            }

            if (frontLeft == null || frontRight == null || rearLeft == null || rearRight == null)
            {
                return;
            }

            wheelHubDetailBuilt = true;
            Material hubCapMaterial = CreateMaterial("wheel hub cap material", new Color(0.045f, 0.045f, 0.05f), 0.85f, 0.55f);
            Material lugMaterial = CreateMaterial("wheel hub lug material", new Color(0.72f, 0.73f, 0.76f), 0.9f, 0.6f);
            BuildWheelHub(frontLeft, hubCapMaterial, lugMaterial);
            BuildWheelHub(frontRight, hubCapMaterial, lugMaterial);
            BuildWheelHub(rearLeft, hubCapMaterial, lugMaterial);
            BuildWheelHub(rearRight, hubCapMaterial, lugMaterial);
        }

        static void BuildWheelHub(Transform pivot, Material hubCapMaterial, Material lugMaterial)
        {
            if (pivot == null)
            {
                return;
            }

            float outboard = pivot.localPosition.x < 0f ? -0.29f : 0.29f;

            GameObject hubCap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hubCap.name = "wheel hub cap";
            hubCap.transform.SetParent(pivot, false);
            hubCap.transform.localPosition = new Vector3(outboard, 0f, 0f);
            hubCap.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            hubCap.transform.localScale = new Vector3(0.16f, 0.02f, 0.16f);
            hubCap.GetComponent<Renderer>().sharedMaterial = hubCapMaterial;
            Collider hubCollider = hubCap.GetComponent<Collider>();
            if (hubCollider != null)
            {
                Destroy(hubCollider);
            }

            // Small lug nuts ring the hub between the cap and the rim's outer
            // edge - spheres rather than oriented cubes so there's no rotation
            // math to get subtly wrong per lug, just a radius and an angle.
            const int lugCount = 5;
            for (int i = 0; i < lugCount; i++)
            {
                float angle = i * (360f / lugCount) * Mathf.Deg2Rad;
                GameObject lug = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lug.name = "wheel hub lug";
                lug.transform.SetParent(pivot, false);
                lug.transform.localPosition = new Vector3(outboard * 1.05f, Mathf.Cos(angle) * 0.11f, Mathf.Sin(angle) * 0.11f);
                lug.transform.localScale = new Vector3(0.028f, 0.028f, 0.028f);
                lug.GetComponent<Renderer>().sharedMaterial = lugMaterial;
                Collider lugCollider = lug.GetComponent<Collider>();
                if (lugCollider != null)
                {
                    Destroy(lugCollider);
                }
            }
        }

        // Shared helper for all the proud additive-detail panels below (endplate
        // accents, halo mounts, cockpit trim, livery accents) - mirrors
        // RaceManager's own CreateChildCube (create primitive, parent, position/
        // scale, material, strip the collider) since this file otherwise builds
        // its one-off primitives inline (see BuildWheelHub above).
        static void CreateAccentCube(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            CreateAccentCube(parent, objectName, localPosition, Quaternion.identity, localScale, material);
        }

        static void CreateAccentCube(Transform parent, string objectName, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = localRotation;
            cube.transform.localScale = localScale;
            Renderer cubeRenderer = cube.GetComponent<Renderer>();
            cubeRenderer.sharedMaterial = material;

            // Explicit rather than relying on the primitive's default, so every
            // additive detail piece this file builds is guaranteed to cast and
            // receive shadows like the rest of the car body regardless of
            // whatever Unity/project default happens to be in force.
            cubeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            cubeRenderer.receiveShadows = true;
            Collider cubeCollider = cube.GetComponent<Collider>();
            if (cubeCollider != null)
            {
                Destroy(cubeCollider);
            }
        }

        // Thin structural bar between two points in `parent`'s local space -
        // mirrors RaceManager.CreateSuspensionArm's own midpoint/LookRotation/
        // magnitude technique for a tube-like strut, reused here so the halo
        // pillar/arcs below don't need per-piece hand-tuned Euler angles.
        static void CreateAccentBar(Transform parent, string objectName, Vector3 a, Vector3 b, float thickness, Material material)
        {
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.001f)
            {
                return;
            }

            Vector3 midpoint = (a + b) * 0.5f;
            Quaternion rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            CreateAccentCube(parent, objectName, midpoint, rotation, new Vector3(thickness, thickness, length), material);
        }

        // A thin contrast tip on each of the four endplates RaceManager already
        // builds (front left/right, rear left/right) so the wings read with more
        // detail up close. Parented to the car root at the endplates' own known
        // coordinates rather than nested under the endplate transforms
        // themselves - nesting would inherit their thin (~0.05-wide) local
        // scale and shrink the accent down to nothing.
        void EnsureEndplateAccents()
        {
            if (endplateAccentBuilt)
            {
                return;
            }

            if (transform.Find("left front endplate") == null || transform.Find("right front endplate") == null ||
                transform.Find("left rear endplate") == null || transform.Find("right rear endplate") == null)
            {
                return;
            }

            endplateAccentBuilt = true;
            Material accentMaterial = CreateMaterial("endplate accent", new Color(0.92f, 0.92f, 0.9f), 0.15f, 0.9f);
            CreateAccentCube(transform, "front endplate accent left", new Vector3(-1.065f, 0.4f, 2.6f), Quaternion.Euler(0f, -6f, 0f), new Vector3(0.012f, 0.05f, 0.5f), accentMaterial);
            CreateAccentCube(transform, "front endplate accent right", new Vector3(1.065f, 0.4f, 2.6f), Quaternion.Euler(0f, 6f, 0f), new Vector3(0.012f, 0.05f, 0.5f), accentMaterial);
            CreateAccentCube(transform, "rear endplate accent left", new Vector3(-0.915f, 1.0f, -2.06f), Quaternion.Euler(0f, 0f, 4f), new Vector3(0.012f, 0.06f, 0.46f), accentMaterial);
            CreateAccentCube(transform, "rear endplate accent right", new Vector3(0.915f, 1.0f, -2.06f), Quaternion.Euler(0f, 0f, -4f), new Vector3(0.012f, 0.06f, 0.46f), accentMaterial);
        }

        // Small bracket plates where each halo stay meets the survival cell,
        // echoing the wheel hub's cap-and-lug idiom above (a couple of small
        // primitives at a real structural point) so the halo reads as bolted on
        // rather than merged seamlessly into the tub.
        void EnsureHaloMountDetail()
        {
            if (haloMountDetailBuilt)
            {
                return;
            }

            if (transform.Find("left halo stay") == null || transform.Find("right halo stay") == null)
            {
                return;
            }

            haloMountDetailBuilt = true;
            Material mountMaterial = CreateMaterial("halo mount plate", new Color(0.1f, 0.11f, 0.12f), 0.72f, 0.5f);
            CreateAccentCube(transform, "halo mount plate left", new Vector3(-0.32f, 0.615f, 0.24f), new Vector3(0.09f, 0.03f, 0.11f), mountMaterial);
            CreateAccentCube(transform, "halo mount plate right", new Vector3(0.32f, 0.615f, 0.24f), new Vector3(0.09f, 0.03f, 0.11f), mountMaterial);
        }

        // Cockpit detail pass: a visor-line stripe across the helmet so the
        // driver's head reads clearly under the halo, plus a crossed seatbelt
        // harness over the cockpit surround pad - found lazily off the same
        // "driver helmet"/"cockpit surround pad" transforms RaceManager already
        // places.
        void EnsureCockpitDetail()
        {
            if (cockpitDetailBuilt)
            {
                return;
            }

            if (transform.Find("driver helmet") == null || transform.Find("cockpit surround pad") == null)
            {
                return;
            }

            cockpitDetailBuilt = true;
            Material stripeMaterial = CreateMaterial("helmet visor stripe", new Color(0.05f, 0.05f, 0.06f), 0.2f, 0.7f);
            CreateAccentCube(transform, "helmet visor stripe", new Vector3(0f, 0.86f, 0.3f), new Vector3(0.3f, 0.05f, 0.08f), stripeMaterial);

            Material harnessMaterial = CreateMaterial("seatbelt harness", new Color(0.62f, 0.05f, 0.04f), 0.05f, 0.5f);
            CreateAccentCube(transform, "seatbelt harness left", new Vector3(-0.12f, 0.68f, 0.36f), Quaternion.Euler(0f, 0f, 32f), new Vector3(0.05f, 0.24f, 0.05f), harnessMaterial);
            CreateAccentCube(transform, "seatbelt harness right", new Vector3(0.12f, 0.68f, 0.36f), Quaternion.Euler(0f, 0f, -32f), new Vector3(0.05f, 0.24f, 0.05f), harnessMaterial);
        }

        // Team-colour livery accent variety: three generated pattern variants
        // (twin pinstripe, gradient fade, colour-split nose) layered on top of
        // RaceManager's base livery flashes, chosen deterministically from the
        // car's own name so grid cars read as distinct liveries within their own
        // team colours instead of every car on the grid looking identical.
        // Colours are sampled from the primary/secondary materials RaceManager
        // already built (the body material UpdateWetBodySheen finds, and the
        // livery flash material) rather than any new data, so this works for
        // any team without extra plumbing.
        void EnsureLiveryAccentVariety()
        {
            if (liveryAccentBuilt || bodyMaterial == null)
            {
                return;
            }

            Transform flash = transform.Find("left livery flash");
            if (flash == null)
            {
                return;
            }

            Renderer flashRenderer = flash.GetComponent<Renderer>();
            if (flashRenderer == null || flashRenderer.sharedMaterial == null)
            {
                return;
            }

            liveryAccentBuilt = true;
            Color primary = bodyMaterial.color;
            Color secondary = flashRenderer.sharedMaterial.color;
            int variant = Mathf.Abs(gameObject.name.GetHashCode()) % 3;
            if (variant == 0)
            {
                BuildTwinPinstripeLivery(secondary);
            }
            else if (variant == 1)
            {
                BuildGradientFadeLivery(primary, secondary);
            }
            else
            {
                BuildNoseSplitLivery(secondary);
            }
        }

        // Variant 0: a pair of thin pinstripes flanking each existing livery
        // flash, above and below it.
        void BuildTwinPinstripeLivery(Color secondary)
        {
            Material pinMaterial = CreateMaterial("livery pinstripe", secondary, 0.3f, 0.85f);
            CreateAccentCube(transform, "livery pinstripe left upper", new Vector3(-0.885f, 0.58f, -0.44f), new Vector3(0.02f, 0.05f, 1.0f), pinMaterial);
            CreateAccentCube(transform, "livery pinstripe left lower", new Vector3(-0.885f, 0.46f, -0.44f), new Vector3(0.02f, 0.05f, 1.0f), pinMaterial);
            CreateAccentCube(transform, "livery pinstripe right upper", new Vector3(0.885f, 0.58f, -0.44f), new Vector3(0.02f, 0.05f, 1.0f), pinMaterial);
            CreateAccentCube(transform, "livery pinstripe right lower", new Vector3(0.885f, 0.46f, -0.44f), new Vector3(0.02f, 0.05f, 1.0f), pinMaterial);
        }

        // Variant 1: a cluster of thin bands across the engine cover, each
        // interpolated a step further from secondary toward primary, reading as
        // a fade rather than a hard-edged block.
        void BuildGradientFadeLivery(Color primary, Color secondary)
        {
            const int bands = 5;
            for (int i = 0; i < bands; i++)
            {
                float t = i / (float)(bands - 1);
                Color bandColor = Color.Lerp(secondary, primary, t);
                Material bandMaterial = CreateMaterial("livery gradient band " + i, bandColor, 0.28f, 0.82f);
                float z = Mathf.Lerp(-0.02f, -1.32f, t);
                CreateAccentCube(transform, "livery gradient band " + i, new Vector3(0f, 0.865f, z), new Vector3(0.36f, 0.012f, 0.16f), bandMaterial);
            }
        }

        // Variant 2: an angled colour-split wedge over the nose, secondary over
        // primary, reading as a diagonal livery split rather than a plain nose.
        void BuildNoseSplitLivery(Color secondary)
        {
            Material splitMaterial = CreateMaterial("livery nose split", secondary, 0.26f, 0.84f);
            CreateAccentCube(transform, "livery nose split", new Vector3(0f, 0.34f, 2.02f), Quaternion.Euler(0f, 0f, 20f), new Vector3(0.16f, 0.05f, 0.42f), splitMaterial);
        }

        // RaceManager's halo is "halo center" (a small post near the front of
        // the ring) plus "halo rim" (one flat plate) and two rear "halo stay"
        // legs - readable, but the rim reads as a slab rather than a curved
        // hoop. A real halo's most distinctive trait is its tripod structure:
        // one front pillar plus two rear legs holding up a loop. Bar struts
        // from the existing front node down to a lower front-bulkhead point,
        // and out to the two existing stay points, suggest that curve/tripod
        // without needing actual geometry booleans (not available with the
        // primitive-only toolset this codebase uses throughout).
        void EnsureHaloRingDetail()
        {
            if (haloRingDetailBuilt)
            {
                return;
            }

            Transform center = transform.Find("halo center");
            Transform leftStay = transform.Find("left halo stay");
            Transform rightStay = transform.Find("right halo stay");
            if (center == null || leftStay == null || rightStay == null)
            {
                return;
            }

            haloRingDetailBuilt = true;
            Material tubeMaterial = CreateMaterial("halo tube", new Color(0.07f, 0.08f, 0.09f), 0.7f, 0.4f);
            // Modern halos are moulded carbon-fibre, not plain painted tube -
            // the same weave texture the carbon floor uses (see
            // ApplyMaterialContrastPass/GetCarbonWeaveTexture), tiled tighter
            // to suit the halo's much smaller surface.
            tubeMaterial.mainTexture = GetCarbonWeaveTexture();
            tubeMaterial.mainTextureScale = new Vector2(2f, 1f);

            Vector3 frontNode = new Vector3(0f, 0.88f, 0.56f);
            CreateAccentBar(transform, "halo front pillar", new Vector3(0f, 0.5f, 0.94f), frontNode, 0.045f, tubeMaterial);
            CreateAccentBar(transform, "halo arc left", frontNode, new Vector3(-0.32f, 0.79f, 0.23f), 0.04f, tubeMaterial);
            CreateAccentBar(transform, "halo arc right", frontNode, new Vector3(0.32f, 0.79f, 0.23f), 0.04f, tubeMaterial);

            // A short lateral brace between the tops of the two rear stays closes
            // the tripod into a real hoop rather than leaving the two rear legs
            // reading as separate, unconnected posts.
            CreateAccentBar(transform, "halo rear cross-brace", new Vector3(-0.32f, 0.79f, 0.23f), new Vector3(0.32f, 0.79f, 0.23f), 0.035f, tubeMaterial);
        }

        // A small dark housing/bezel behind the single rear rain light so it
        // reads as a lens set into a real light pod rather than a bare glowing
        // block - z is kept closer to the car body (less negative) than the
        // light itself, which sits toward the very back of the gearbox, so
        // the bezel forms a backing plate/frame rather than covering the lens.
        void EnsureRearLightDetail()
        {
            if (rearLightDetailBuilt)
            {
                return;
            }

            Transform light = transform.Find("rear rain light");
            if (light == null)
            {
                return;
            }

            rearLightDetailBuilt = true;
            Material housingMaterial = CreateMaterial("rear light housing", new Color(0.025f, 0.025f, 0.03f), 0.55f, 0.3f);
            CreateAccentCube(transform, "rear light housing", new Vector3(0f, 0.42f, -2.06f), new Vector3(0.16f, 0.28f, 0.05f), housingMaterial);
            CreateAccentCube(transform, "rear light bezel", new Vector3(0f, 0.42f, -2.145f), new Vector3(0.14f, 0.25f, 0.02f), housingMaterial);
        }

        // A thin Gurney-flap lip on the front wing's widest element and the
        // rear wing's main plane - both fixed (undamaged/non-DRS) transforms,
        // so the trim never has to chase a moving part (the front wing base
        // does still droop under damage and the rear flap swings with DRS,
        // exactly like RaceManager's own endplate accents already sit near
        // without literally being parented to those moving pieces).
        void EnsureWingTrimDetail()
        {
            if (wingTrimDetailBuilt)
            {
                return;
            }

            Transform frontWing = transform.Find("front wing base");
            Transform rearWing = transform.Find("rear wing main plane");
            if (frontWing == null || rearWing == null)
            {
                return;
            }

            wingTrimDetailBuilt = true;
            Material trimMaterial = CreateMaterial("wing gurney trim", new Color(0.85f, 0.86f, 0.88f), 0.2f, 0.55f);
            CreateAccentCube(transform, "front wing gurney", new Vector3(0f, 0.185f, 2.62f), new Vector3(1.9f, 0.025f, 0.05f), trimMaterial);
            CreateAccentCube(transform, "rear wing gurney", new Vector3(0f, 0.705f, -2.23f), Quaternion.Euler(9f, 0f, 0f), new Vector3(1.55f, 0.025f, 0.05f), trimMaterial);
        }

        // Thin dark seam lines dropped across the real panel breaks RaceManager's
        // bodywork already has - nose-to-cockpit, sidepod inlet trailing edge, engine
        // cover-to-airbox, and the gearbox/rear-bodywork break ahead of the diffuser -
        // so up close the car reads as separate moulded panels bolted together
        // instead of one seamless painted shell. Same additive-accent idiom as the
        // endplate/halo/cockpit detail above: found lazily off known body transforms,
        // built once, no collider (CreateAccentCube already strips it).
        void EnsureBodyPanelLineDetail()
        {
            if (bodyPanelLineDetailBuilt)
            {
                return;
            }

            if (transform.Find("survival cell") == null)
            {
                return;
            }

            bodyPanelLineDetailBuilt = true;
            Material seamMaterial = CreateMaterial("body panel seam", new Color(0.03f, 0.03f, 0.035f), 0.15f, 0.35f);

            // Nose-to-cockpit join, just ahead of the cockpit surround pad.
            CreateAccentCube(transform, "nose panel seam", new Vector3(0f, 0.34f, 1.42f), new Vector3(0.9f, 0.012f, 0.02f), seamMaterial);

            // Trailing edge of each sidepod inlet, where the inlet's own moulding
            // would meet the rest of the sidepod skin.
            if (transform.Find("left sidepod inlet") != null && transform.Find("right sidepod inlet") != null)
            {
                CreateAccentCube(transform, "left sidepod trailing seam", new Vector3(-0.86f, 0.4f, -0.24f), new Vector3(0.02f, 0.2f, 0.012f), seamMaterial);
                CreateAccentCube(transform, "right sidepod trailing seam", new Vector3(0.86f, 0.4f, -0.24f), new Vector3(0.02f, 0.2f, 0.012f), seamMaterial);
            }

            // Engine cover to airbox transition.
            if (transform.Find("airbox") != null)
            {
                CreateAccentCube(transform, "engine cover seam", new Vector3(0f, 0.93f, -0.52f), new Vector3(0.36f, 0.012f, 0.02f), seamMaterial);
            }

            // Gearbox/rear-bodywork break just ahead of the beam wing/diffuser.
            CreateAccentCube(transform, "gearbox panel seam", new Vector3(0f, 0.5f, -1.62f), new Vector3(0.62f, 0.012f, 0.02f), seamMaterial);
        }

        // Sidepod undercut vanes (a diagonal fin trailing back from each inlet
        // down toward the floor, echoing the sculpted "coke bottle" undercut a
        // real sidepod uses to accelerate air toward the diffuser) plus a lip trim
        // framing each inlet opening - found lazily off RaceManager's existing
        // sidepod/inlet transforms, same additive idiom as everything else in this
        // file. The vane also doubles as a new home for a team accent colour,
        // picked up from the same livery flash secondary colour the pinstripe/
        // gradient/nose-split variants above use, so it reads as part of the same
        // livery rather than a mismatched extra colour.
        void EnsureSidepodDetail()
        {
            if (sidepodDetailBuilt)
            {
                return;
            }

            Transform leftSidepod = transform.Find("left sidepod");
            Transform rightSidepod = transform.Find("right sidepod");
            Transform leftInlet = transform.Find("left sidepod inlet");
            Transform rightInlet = transform.Find("right sidepod inlet");
            if (leftSidepod == null || rightSidepod == null || leftInlet == null || rightInlet == null)
            {
                return;
            }

            sidepodDetailBuilt = true;

            Color secondary = new Color(0.85f, 0.86f, 0.88f);
            Transform flash = transform.Find("left livery flash");
            if (flash != null)
            {
                Renderer flashRenderer = flash.GetComponent<Renderer>();
                if (flashRenderer != null && flashRenderer.sharedMaterial != null)
                {
                    secondary = flashRenderer.sharedMaterial.color;
                }
            }

            Material vaneMaterial = CreateMaterial("sidepod undercut vane", secondary, 0.3f, 0.82f);
            CreateAccentCube(transform, "left sidepod undercut vane", new Vector3(-0.72f, 0.2f, -0.88f), Quaternion.Euler(16f, 0f, -10f), new Vector3(0.045f, 0.15f, 0.56f), vaneMaterial);
            CreateAccentCube(transform, "right sidepod undercut vane", new Vector3(0.72f, 0.2f, -0.88f), Quaternion.Euler(16f, 0f, 10f), new Vector3(0.045f, 0.15f, 0.56f), vaneMaterial);

            Material lipMaterial = CreateMaterial("sidepod inlet lip", new Color(0.9f, 0.9f, 0.88f), 0.2f, 0.75f);
            CreateAccentCube(transform, "left sidepod inlet lip", new Vector3(-0.855f, 0.535f, 0.02f), new Vector3(0.055f, 0.02f, 0.38f), lipMaterial);
            CreateAccentCube(transform, "right sidepod inlet lip", new Vector3(0.855f, 0.535f, 0.02f), new Vector3(0.055f, 0.02f, 0.38f), lipMaterial);
        }

        // A small outwash flick at the base of each front wing endplate, angled
        // outward - real endplates use exactly this kind of strake to steer
        // turbulent front-tyre wake outward and away from the floor/sidepod.
        void EnsureFrontWingEndplateFlick()
        {
            if (frontWingFlickBuilt)
            {
                return;
            }

            if (transform.Find("left front endplate") == null || transform.Find("right front endplate") == null)
            {
                return;
            }

            frontWingFlickBuilt = true;
            Material flickMaterial = CreateMaterial("front wing endplate flick", new Color(0.85f, 0.86f, 0.88f), 0.25f, 0.6f);
            CreateAccentCube(transform, "front endplate flick left", new Vector3(-1.1f, 0.14f, 2.5f), Quaternion.Euler(0f, -24f, 0f), new Vector3(0.14f, 0.03f, 0.22f), flickMaterial);
            CreateAccentCube(transform, "front endplate flick right", new Vector3(1.1f, 0.14f, 2.5f), Quaternion.Euler(0f, 24f, 0f), new Vector3(0.14f, 0.03f, 0.22f), flickMaterial);
        }

        // A pair of thin diagonal strakes across each rear wing endplate, echoing
        // the louvred vertical strakes a real rear endplate uses to bleed off
        // trailing-edge vortex energy - purely decorative geometry here, same as
        // the Gurney trim/endplate accent passes elsewhere in this file.
        void EnsureRearWingEndplateLouvre()
        {
            if (rearWingLouvreBuilt)
            {
                return;
            }

            if (transform.Find("left rear endplate") == null || transform.Find("right rear endplate") == null)
            {
                return;
            }

            rearWingLouvreBuilt = true;
            Material louvreMaterial = CreateMaterial("rear endplate louvre", new Color(0.06f, 0.06f, 0.07f), 0.4f, 0.35f);
            CreateAccentCube(transform, "rear endplate louvre left upper", new Vector3(-0.9f, 0.86f, -2.06f), Quaternion.Euler(0f, 0f, 18f), new Vector3(0.065f, 0.32f, 0.02f), louvreMaterial);
            CreateAccentCube(transform, "rear endplate louvre left lower", new Vector3(-0.9f, 0.58f, -2.06f), Quaternion.Euler(0f, 0f, 18f), new Vector3(0.065f, 0.32f, 0.02f), louvreMaterial);
            CreateAccentCube(transform, "rear endplate louvre right upper", new Vector3(0.9f, 0.86f, -2.06f), Quaternion.Euler(0f, 0f, -18f), new Vector3(0.065f, 0.32f, 0.02f), louvreMaterial);
            CreateAccentCube(transform, "rear endplate louvre right lower", new Vector3(0.9f, 0.58f, -2.06f), Quaternion.Euler(0f, 0f, -18f), new Vector3(0.065f, 0.32f, 0.02f), louvreMaterial);
        }

        // Applies a baked spoke pattern to rimMaterial the first time UpdateBrakeGlow's
        // own lazy lookup (UpdateRimHighlight) has actually found it - rimMaterial is
        // shared across all four wheels' "wheel rim" and "wheel cover" meshes (see
        // RaceManager.CreateWheel), so one assignment here lights up every wheel's
        // rim with the same design, the same sharing property UpdateRimHighlight's
        // own emission tint already relies on.
        void EnsureRimSpokeDetail()
        {
            if (rimSpokeTextureApplied || rimMaterial == null)
            {
                return;
            }

            rimSpokeTextureApplied = true;
            rimMaterial.mainTexture = GetRimSpokeTexture();
            rimMaterial.mainTextureScale = Vector2.one;
        }

        // A handful of bright/dark wedges around one full texture wrap, the same
        // multiply-over-base-colour trick GetTreadTexture uses for tyres - tiled
        // exactly once (mainTextureScale left at 1,1 above) rather than repeated,
        // since a rim wants a fixed spoke count rather than a repeating band.
        static Texture2D GetRimSpokeTexture()
        {
            if (sharedRimSpokeTexture != null)
            {
                return sharedRimSpokeTexture;
            }

            // High-res version of the original 128x16 wrap: the cosine wedge
            // profile is resolution-independent, so raising the sample density
            // (with mipmaps + trilinear) just removes the banding the old
            // 128-sample wrap showed on the rim face.
            const int width = 1024;
            const int height = 128;
            const int spokeCount = 6;
            sharedRimSpokeTexture = new Texture2D(width, height, TextureFormat.RGBA32, true);
            sharedRimSpokeTexture.wrapMode = TextureWrapMode.Repeat;
            sharedRimSpokeTexture.filterMode = FilterMode.Trilinear;
            sharedRimSpokeTexture.anisoLevel = 4;
            for (int x = 0; x < width; x++)
            {
                float angle = (x / (float)width) * spokeCount * Mathf.PI * 2f;
                float wave = Mathf.Cos(angle) * 0.5f + 0.5f;
                float shade = Mathf.Lerp(0.3f, 1f, Mathf.Pow(wave, 3f));
                for (int y = 0; y < height; y++)
                {
                    sharedRimSpokeTexture.SetPixel(x, y, new Color(shade, shade, shade, 1f));
                }
            }

            sharedRimSpokeTexture.Apply(true);
            return sharedRimSpokeTexture;
        }

        // Basket-weave carbon texture, multiplied into a dark base colour the
        // same way GetTreadTexture/GetRimSpokeTexture suggest tread blocks/
        // spokes without any extra geometry. Tiled via mainTextureScale (see
        // ApplyMaterialContrastPass/EnsureHaloRingDetail). Built at 256x256
        // with the same 8x8 weave cells per wrap the original 16x16 texture
        // had: each cell now carries a directional fibre-sheen gradient and
        // soft cell borders, so a panel reads as woven twill up close instead
        // of a hard checkerboard.
        static Texture2D sharedCarbonWeaveTexture;

        static Texture2D GetCarbonWeaveTexture()
        {
            if (sharedCarbonWeaveTexture != null)
            {
                return sharedCarbonWeaveTexture;
            }

            const int size = 256;
            const int cellSize = 32; // 8x8 weave cells per wrap, as before
            sharedCarbonWeaveTexture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            sharedCarbonWeaveTexture.wrapMode = TextureWrapMode.Repeat;
            sharedCarbonWeaveTexture.filterMode = FilterMode.Trilinear;
            sharedCarbonWeaveTexture.anisoLevel = 4;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cellX = x / cellSize;
                    int cellY = y / cellSize;
                    bool warp = ((cellX + cellY) % 2) == 0;
                    // Position within the cell, 0..1. The fibre sheen runs
                    // along the tow direction: horizontal on warp cells,
                    // vertical on weft cells - a cosine highlight across the
                    // perpendicular axis mimics the rounded tow surface.
                    float u = (x % cellSize + 0.5f) / cellSize;
                    float v = (y % cellSize + 0.5f) / cellSize;
                    float across = warp ? v : u;
                    float along = warp ? u : v;
                    float sheen = Mathf.Cos((across - 0.5f) * Mathf.PI) * 0.5f + 0.5f;
                    float baseShade = warp ? 0.82f : 0.4f;
                    float shade = baseShade * Mathf.Lerp(0.72f, 1.12f, sheen);
                    // Darken the tow ends where a cell tucks under its
                    // neighbour, so the weave reads as interlaced.
                    float endFade = Mathf.Min(along, 1f - along) * cellSize;
                    shade *= Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(endFade / 3f));
                    shade = Mathf.Clamp01(shade);
                    sharedCarbonWeaveTexture.SetPixel(x, y, new Color(shade, shade, shade, 1f));
                }
            }

            sharedCarbonWeaveTexture.Apply(true);
            return sharedCarbonWeaveTexture;
        }

        // Cheap contact-shadow blob (see field comments above) built the first
        // time the car root has a valid transform - which is immediately, so
        // this really only needs to run once on the first Update tick.
        void EnsureContactShadow()
        {
            if (contactShadowBuilt)
            {
                return;
            }

            contactShadowBuilt = true;
            GameObject shadowObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadowObject.name = "Contact shadow";
            Collider shadowCollider = shadowObject.GetComponent<Collider>();
            if (shadowCollider != null)
            {
                Destroy(shadowCollider);
            }

            Renderer shadowRenderer = shadowObject.GetComponent<Renderer>();
            shadowRenderer.sharedMaterial = GetContactShadowMaterial();
            shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;
            contactShadow = shadowObject.transform;
            contactShadow.SetParent(transform, false);
            contactShadow.localScale = new Vector3(1.9f, 4.5f, 1f);
        }

        void UpdateContactShadow()
        {
            EnsureContactShadow();
            if (contactShadow == null)
            {
                return;
            }

            // Positioned a little below the chassis root toward the tarmac and
            // kept level (world up), tracking only the car's yaw - the chassis
            // itself stays close to flat (see VehicleController.StabilizeChassis)
            // but even a small residual roll/pitch would otherwise tilt a
            // naively-parented shadow visibly out of the ground plane.
            contactShadow.position = transform.position + Vector3.down * 0.36f;
            contactShadow.rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
        }

        static Material GetContactShadowMaterial()
        {
            if (sharedContactShadowMaterial != null)
            {
                return sharedContactShadowMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            sharedContactShadowMaterial = new Material(shader);
            sharedContactShadowMaterial.color = Color.white;
            sharedContactShadowMaterial.mainTexture = GetContactShadowTexture();
            return sharedContactShadowMaterial;
        }

        // Soft radial falloff so the shadow reads as a grounding cue rather than
        // a hard-edged dark decal - darkest under the car's centre, fading to
        // fully transparent by the texture's edge, the same idea as the
        // particle soft-dot texture in VehicleEffects below, just darker and
        // capped well short of full black so it never looks like a hole in the
        // track.
        static Texture2D GetContactShadowTexture()
        {
            if (contactShadowTexture != null)
            {
                return contactShadowTexture;
            }

            // 256px with mipmaps: the blob is stretched to ~1.9x4.5 m under the
            // car (and reused as the scuff decal), so the old 32px source
            // showed visible bilinear diamonds at its centre.
            const int size = 256;
            contactShadowTexture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            contactShadowTexture.wrapMode = TextureWrapMode.Clamp;
            contactShadowTexture.filterMode = FilterMode.Trilinear;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - (size - 1) * 0.5f) / (size * 0.5f);
                    float dy = (y - (size - 1) * 0.5f) / (size * 0.5f);
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    float alpha = falloff * falloff * 0.55f;
                    contactShadowTexture.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
                }
            }

            contactShadowTexture.Apply(true);
            return contactShadowTexture;
        }

        // This file otherwise reaches into RaceManager-built materials by name
        // rather than creating its own, except for the skid trail material
        // below - mirrors RaceManager.CreateMaterial's own two overloads
        // (Standard shader, _Metallic/_Glossiness, optional _EmissionColor) so
        // any new procedural detail built here follows the same convention.
        static Material CreateMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            Material material = F1Game.Rendering.ShaderCompat.CreateLitMaterial();
            material.name = materialName;
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            F1Game.Rendering.ShaderCompat.SetSmoothness(material, smoothness);
            return material;
        }

        static Material CreateMaterial(string materialName, Color color, float metallic, float smoothness, Color emission)
        {
            Material material = CreateMaterial(materialName, color, metallic, smoothness);
            if (emission.r > 0f || emission.g > 0f || emission.b > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
            }

            return material;
        }

        // A flat-spotted tyre thumps once per revolution rather than smoothly
        // vibrating - the bump is synced to wheelSpinAngle (the same angle driving
        // the visible spin in UpdateWheels) via a rectified sine, so it always lands
        // twice per full turn regardless of how fast the wheel is currently
        // spinning, scaled by both FlatSpotLevel and road speed so a stationary or
        // barely-moving car with a flat spot doesn't visibly bounce in the pits.
        void UpdateFlatSpotWobble()
        {
            if (vehicle.Tyres == null)
            {
                return;
            }

            float flatSpot = Mathf.Clamp01(vehicle.Tyres.FlatSpotLevel);
            float speedKph = Mathf.Abs(vehicle.CurrentSpeedKph);
            if (flatSpot <= 0.01f || speedKph < 8f)
            {
                if (wheelRestPositionsCaptured)
                {
                    ResetWheelBump();
                }

                return;
            }

            CaptureWheelRestPositions();
            float amplitude = Mathf.Lerp(0f, 0.02f, flatSpot) * Mathf.InverseLerp(8f, 65f, speedKph);
            float bump = Mathf.Abs(Mathf.Sin(wheelSpinAngle * Mathf.Deg2Rad)) * amplitude;
            ApplyWheelBump(frontLeft, flRestLocalPos, bump);
            ApplyWheelBump(frontRight, frRestLocalPos, bump);
            ApplyWheelBump(rearLeft, rlRestLocalPos, bump);
            ApplyWheelBump(rearRight, rrRestLocalPos, bump);
        }

        void CaptureWheelRestPositions()
        {
            if (wheelRestPositionsCaptured)
            {
                return;
            }

            if (frontLeft == null || frontRight == null || rearLeft == null || rearRight == null)
            {
                return;
            }

            wheelRestPositionsCaptured = true;
            flRestLocalPos = frontLeft.localPosition;
            frRestLocalPos = frontRight.localPosition;
            rlRestLocalPos = rearLeft.localPosition;
            rrRestLocalPos = rearRight.localPosition;
        }

        void ResetWheelBump()
        {
            if (frontLeft != null)
            {
                frontLeft.localPosition = flRestLocalPos;
            }

            if (frontRight != null)
            {
                frontRight.localPosition = frRestLocalPos;
            }

            if (rearLeft != null)
            {
                rearLeft.localPosition = rlRestLocalPos;
            }

            if (rearRight != null)
            {
                rearRight.localPosition = rrRestLocalPos;
            }
        }

        static void ApplyWheelBump(Transform wheel, Vector3 restLocalPos, float bump)
        {
            if (wheel == null)
            {
                return;
            }

            wheel.localPosition = restLocalPos + new Vector3(0f, bump, 0f);
        }
    }

    // Lightweight per-car particle effects: off-track dust, wet spray, lockup smoke,
    // and collision sparks. Emitters share one generated soft-dot texture; the whole
    // component is only added when the particles setting is enabled.
    public class VehicleEffects : MonoBehaviour
    {
        VehicleController vehicle;
        ParticleSystem dust;
        ParticleSystem spray;
        ParticleSystem lockupSmoke;
        ParticleSystem wheelspinSmoke;
        ParticleSystem sparks;
        ParticleSystem heatHaze;
        ParticleSystem damageSmoke;
        float previousDamagePercent = -1f;

        static Texture2D softDot;
        static Material sharedParticleMaterial;

        // [VfxDiag] attribution telemetry: THIS class is the live per-car VFX
        // system (VehicleVfxDriver is deliberately not attached - see
        // ProductionCarSpawner). Static accumulators across all cars, flushed
        // unconditionally every 20s via Debug.Log (GameLog is verbosity-gated
        // and would silently drop it), so a console paste attributes any
        // sparks/smoke/cloud sighting to the exact emitter that produced it.
        static float diagDustSec;
        static float diagSpraySec;
        static float diagLockupSec;
        static float diagWheelspinSec;
        static float diagKerbSparkSec;
        static float diagFloorSparkSec;
        static float diagDamageSmokeSec;
        static int diagCollisionBursts;
        static int diagDamageJumpBursts;
        static float diagNextFlush;
        const float DiagFlushInterval = 20f;

        static void FlushVfxDiag()
        {
            if (Time.time < diagNextFlush)
            {
                return;
            }

            diagNextFlush = Time.time + DiagFlushInterval;
            Debug.Log("[VfxDiag] emitter-active seconds last " + DiagFlushInterval + "s (summed over all cars):" +
                " dust=" + diagDustSec.ToString("0.0") +
                " spray=" + diagSpraySec.ToString("0.0") +
                " lockup=" + diagLockupSec.ToString("0.0") +
                " wheelspin=" + diagWheelspinSec.ToString("0.0") +
                " kerbSparks=" + diagKerbSparkSec.ToString("0.0") +
                " floorSparks=" + diagFloorSparkSec.ToString("0.0") +
                " damageSmoke=" + diagDamageSmokeSec.ToString("0.0") +
                " collisionBursts=" + diagCollisionBursts +
                " damageJumpBursts=" + diagDamageJumpBursts +
                " trackWetness=" + TyreState.TrackWetness01.ToString("0.00"));
            diagDustSec = 0f;
            diagSpraySec = 0f;
            diagLockupSec = 0f;
            diagWheelspinSec = 0f;
            diagKerbSparkSec = 0f;
            diagFloorSparkSec = 0f;
            diagDamageSmokeSec = 0f;
            diagCollisionBursts = 0;
            diagDamageJumpBursts = 0;
        }

        public void Initialize(VehicleController controller)
        {
            vehicle = controller;
            dust = CreateEmitter("Dust emitter", new Vector3(0f, 0.28f, -1.9f), new Color(0.62f, 0.51f, 0.35f, 0.5f), 0.9f, 1.5f, 2.6f);
            spray = CreateEmitter("Spray emitter", new Vector3(0f, 0.34f, -2.15f), new Color(0.7f, 0.78f, 0.84f, 0.35f), 0.65f, 1.2f, 3.4f);
            lockupSmoke = CreateEmitter("Lockup smoke emitter", new Vector3(0f, 0.2f, 1.35f), new Color(0.86f, 0.86f, 0.86f, 0.45f), 0.75f, 1.05f, 1.9f);

            // Rear-axle counterpart to lockupSmoke above: real wheelspin (rear
            // tyres overwhelming grip under power - launches, greasy corner exits,
            // a damaged floor costing rear traction) puffs off the back of the car
            // rather than the front, so this gets its own emitter positioned at
            // the rear axle instead of reusing the nose-mounted lockup one.
            wheelspinSmoke = CreateEmitter("Wheelspin smoke emitter", new Vector3(0f, 0.2f, -1.35f), new Color(0.82f, 0.8f, 0.78f, 0.35f), 0.7f, 0.85f, 1.6f);
            sparks = CreateEmitter("Spark emitter", new Vector3(0f, 0.22f, 0f), new Color(1f, 0.74f, 0.28f, 0.9f), 0.4f, 0.14f, 7.5f);
            ParticleSystem.MainModule sparkMain = sparks.main;
            sparkMain.gravityModifier = 1.3f;
            sparkMain.maxParticles = 256;

            // Faint heat-haze puffs off the engine cover under hard acceleration.
            // Deliberately understated (small, sparse, quick to fade) - a fake
            // shimmer that draws attention to itself reads worse than none.
            heatHaze = CreateEmitter("Heat haze emitter", new Vector3(0f, 0.58f, -1.05f), new Color(1f, 0.95f, 0.85f, 0.16f), 0.5f, 0.4f, 0.5f);
            ParticleSystem.MainModule heatMain = heatHaze.main;
            heatMain.gravityModifier = -0.2f;
            heatMain.maxParticles = 96;
            heatHaze.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // Dark engine-damage smoke off the cover, distinct from the pale tyre
            // smoke: slower, longer-lived and rising, so a wounded car trails a
            // visible plume rather than only slowing down mysteriously.
            damageSmoke = CreateEmitter("Damage smoke emitter", new Vector3(0f, 0.7f, -1.35f), new Color(0.28f, 0.27f, 0.26f, 0.5f), 1.4f, 1.1f, 2.2f);
            ParticleSystem.MainModule damageMain = damageSmoke.main;
            damageMain.gravityModifier = -0.12f;
            damageMain.maxParticles = 384;
        }

        ParticleSystem CreateEmitter(string emitterName, Vector3 localPosition, Color color, float lifetime, float size, float speed)
        {
            GameObject emitter = new GameObject(emitterName);
            emitter.transform.SetParent(transform, false);
            emitter.transform.localPosition = localPosition;
            emitter.transform.localRotation = Quaternion.Euler(-72f, 0f, 0f);
            ParticleSystem system = emitter.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startSpeed = speed;
            main.startColor = color;
            main.maxParticles = 512;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = 0.28f;
            ParticleSystemRenderer particleRenderer = emitter.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = GetParticleMaterial();
            return system;
        }

        static Material GetParticleMaterial()
        {
            if (sharedParticleMaterial != null)
            {
                return sharedParticleMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            sharedParticleMaterial = new Material(shader);
            sharedParticleMaterial.mainTexture = GetSoftDot();
            return sharedParticleMaterial;
        }

        static Texture2D GetSoftDot()
        {
            if (softDot != null)
            {
                return softDot;
            }

            // 128px with mipmaps: smoke/spray particles reach ~1.5 m across,
            // where the old 32px dot's edge banding was visible in every plume.
            const int size = 128;
            softDot = new Texture2D(size, size, TextureFormat.RGBA32, true);
            softDot.wrapMode = TextureWrapMode.Clamp;
            softDot.filterMode = FilterMode.Trilinear;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - (size - 1) * 0.5f) / (size * 0.5f);
                    float dy = (y - (size - 1) * 0.5f) / (size * 0.5f);
                    float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    softDot.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
                }
            }

            softDot.Apply(true);
            return softDot;
        }

        void Update()
        {
            if (vehicle == null)
            {
                return;
            }

            FlushVfxDiag();
            float speedKph = Mathf.Abs(vehicle.CurrentSpeedKph);
            float speed01 = Mathf.InverseLerp(40f, 240f, speedKph);

            bool dusting = vehicle.IsOffTrackSlowdown && speedKph > 35f;
            if (dusting)
            {
                diagDustSec += Time.deltaTime;
            }

            SetRate(dust, dusting ? Mathf.Lerp(14f, 70f, speed01) : 0f);

            // Spray follows the PHYSICAL track wetness (per report - spray
            // "coming from them constantly... should only happen in rainy
            // conditions"): the old gate keyed off the session's weather FLAG,
            // so a rain-flagged session sprayed all race even when the visuals
            // read dry, and a drying/soaking track couldn't be represented at
            // all. TrackWetness01 is the shared standing-water state the grip
            // model already uses - spray now builds as the track soaks and
            // stops as it dries, exactly like the real thing.
            bool wetSurface = TyreState.TrackWetness01 > 0.2f;
            bool spraying = wetSurface && speedKph > 85f;
            if (spraying)
            {
                diagSpraySec += Time.deltaTime;
            }

            SetRate(spray, spraying ? Mathf.Lerp(18f, 85f, speed01) * Mathf.Clamp01(TyreState.TrackWetness01) : 0f);

            // Scales continuously with LockupSeverity so a small lockup puffs
            // lightly and a big one smokes hard, instead of one binary rate.
            // Colour, plume size and lifetime scale with it too - a light
            // scrub reads as a thin pale wisp, a hard lock as a bigger, darker,
            // longer-hanging burnt-rubber cloud, rather than one fixed puff
            // that only ever fires faster or slower.
            float lockupSeverity = vehicle.Tyres != null ? vehicle.Tyres.LockupSeverity : 0f;
            bool locking = lockupSeverity > 0.12f && speedKph > 60f;
            if (locking)
            {
                diagLockupSec += Time.deltaTime;
                ParticleSystem.MainModule smokeMain = lockupSmoke.main;
                smokeMain.startColor = Color.Lerp(new Color(0.86f, 0.86f, 0.86f, 0.4f), new Color(0.3f, 0.27f, 0.25f, 0.62f), lockupSeverity);
                smokeMain.startSize = Mathf.Lerp(0.65f, 1.55f, lockupSeverity);
                smokeMain.startLifetime = Mathf.Lerp(0.7f, 1.7f, lockupSeverity);
            }

            SetRate(lockupSmoke, locking ? Mathf.Lerp(15f, 90f, lockupSeverity) : 0f);

            // Rear wheelspin puff, driven by the same OversteerAmount/EffectiveThrottle
            // proxy VehicleVisuals.UpdateWheels uses for the rear tyres' extra visual
            // overspin - see that method's comment for why OversteerAmount stands in
            // for a dedicated slip-ratio field. Excluded off-track and at very high
            // speed, where the dust emitter above and normal running respectively
            // already cover the visual, and only while actually moving so a stalled
            // car spinning its wheels on the grid doesn't smoke indefinitely.
            // Threshold raised 0.12 -> 0.42 (per report - the constant "cloud
            // behind the car"): the hair-trigger fired a smoke plume on every
            // ordinary corner exit. Only a genuine, visible slide smokes now.
            float wheelspinAmount = Mathf.Clamp01(vehicle.OversteerAmount * vehicle.EffectiveThrottle * 1.6f);
            bool spinning = wheelspinAmount > 0.42f && speedKph > 15f && speedKph < 200f && !vehicle.IsOffTrackSlowdown;
            if (spinning)
            {
                diagWheelspinSec += Time.deltaTime;
                ParticleSystem.MainModule spinMain = wheelspinSmoke.main;
                spinMain.startColor = Color.Lerp(new Color(0.82f, 0.8f, 0.78f, 0.35f), new Color(0.35f, 0.3f, 0.26f, 0.55f), wheelspinAmount);
                spinMain.startSize = Mathf.Lerp(0.55f, 1.25f, wheelspinAmount);
                spinMain.startLifetime = Mathf.Lerp(0.55f, 1.3f, wheelspinAmount);
            }

            SetRate(wheelspinSmoke, spinning ? Mathf.Lerp(6f, 30f, wheelspinAmount) : 0f);

            // Heat haze OFF (per report - "the cloud/smoke behind the car...
            // should only be existent in rain"): despite the intent above, at
            // >70% throttle it ran for most of every lap and the soft-dot
            // particles read as a permanent smoke cloud trailing every car in
            // the dry. The only always-on trailing cloud is now the rain spray
            // (TrackWetness-gated above); lockup and genuine-slide smokes stay
            // as momentary events.
            SetRate(heatHaze, 0f);

            UpdateKerbSparks(speedKph);
            UpdateDamageEffects();
        }

        // Kerb strikes and a heavily scraped/cracked floor throw a scatter of
        // sparks off the skid plate at real speed - reuses the same spark
        // emitter as collisions/damage bursts (fixed at floor height) rather
        // than a new system, just driven by a continuous rate instead of the
        // one-off Emit() bursts those use. Position is re-pinned to the floor
        // each time this fires a rate above zero since OnCollisionEnter/
        // UpdateDamageEffects both relocate this same emitter to a world-space
        // contact point for their one-off bursts.
        void UpdateKerbSparks(float speedKph)
        {
            if (sparks == null)
            {
                return;
            }

            // Kerb sparks made genuinely RARE (per report, round 2): the
            // racing line legally rides kerbs at the apexes, so "on a kerb
            // above 130" fired for a stretch of nearly every corner. Sparks
            // now need real speed (200+) AND an intermittent per-car flicker
            // window - a burst of sparks off a hard kerb strike here and
            // there, not a grinder trail on every apex.
            float sparkFlicker = Mathf.PerlinNoise(Time.time * 0.7f, (GetInstanceID() & 0xffff) * 0.013f);
            bool kerbSparking = vehicle.IsOnKerb && speedKph > 200f && sparkFlicker > 0.72f;
            // Sparks made rare again (per report - "the sparks arent supposed
            // to be that common"): the floor-scrape term used to fire
            // CONTINUOUSLY at speed from any accumulated floor damage - one
            // bad kerb strike early in a race meant a permanent spark shower
            // for the rest of the stint. Real floor sparks need a genuinely
            // wrecked floor: the term now only engages past 45% floor damage,
            // ramps from zero there, and at half the old intensity. Kerb-strike
            // sparks (momentary, on the kerb, at speed) are unchanged.
            float floorScrape = vehicle.Damage != null ? Mathf.Clamp01(vehicle.Damage.floor) : 0f;
            float wreckedFloor = Mathf.Clamp01((floorScrape - 0.45f) / 0.55f);
            float rate = kerbSparking ? Mathf.Lerp(3f, 12f, Mathf.InverseLerp(200f, 340f, speedKph)) : 0f;
            if (kerbSparking)
            {
                diagKerbSparkSec += Time.deltaTime;
            }

            float floorSparkRate = wreckedFloor * Mathf.InverseLerp(80f, 300f, speedKph) * 7f;
            if (floorSparkRate > 0f)
            {
                diagFloorSparkSec += Time.deltaTime;
            }

            rate += floorSparkRate;
            if (rate > 0f)
            {
                sparks.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            }

            SetRate(sparks, rate);
        }

        // Accumulated damage above ~60% trails a progressively thicker, darker
        // plume, and a sudden jump in the damage total (a fresh hit, as opposed
        // to gradual scraping) fires the existing spark emitter so the moment of
        // impact registers even below the OnCollisionEnter speed threshold.
        void UpdateDamageEffects()
        {
            float damagePercent = vehicle.Damage != null ? vehicle.Damage.OverallPercent : 0f;
            if (previousDamagePercent >= 0f && sparks != null && damagePercent > previousDamagePercent + 4f)
            {
                sparks.Emit(Mathf.Clamp(Mathf.RoundToInt((damagePercent - previousDamagePercent) * 2.2f), 8, 30));
                diagDamageJumpBursts++;
            }

            previousDamagePercent = damagePercent;

            if (damageSmoke == null)
            {
                return;
            }

            float damage01 = Mathf.InverseLerp(60f, 100f, damagePercent);
            if (damage01 > 0f)
            {
                diagDamageSmokeSec += Time.deltaTime;
                ParticleSystem.MainModule damageMain = damageSmoke.main;
                damageMain.startColor = Color.Lerp(new Color(0.45f, 0.44f, 0.42f, 0.35f), new Color(0.12f, 0.11f, 0.1f, 0.6f), damage01);
                damageMain.startSize = Mathf.Lerp(0.8f, 1.6f, damage01);
                damageMain.startLifetime = Mathf.Lerp(1.1f, 2f, damage01);
            }

            SetRate(damageSmoke, damage01 > 0f ? Mathf.Lerp(6f, 55f, damage01) : 0f);
        }

        void SetRate(ParticleSystem system, float rate)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate;
        }

        // Heavy hits (a real crash into a wall/another car, not a glancing knock)
        // additionally throw a short, bigger debris-style burst so the moment
        // reads as more than just "extra sparks". Reuses the damageSmoke emitter
        // for the puff (no new ParticleSystem/GameObject spun up here, keeping this
        // cheap with ~20 cars able to collide on track) via one-off EmitParams
        // calls, which only fire on this rare collision event rather than every
        // frame, so it stays well clear of the no-per-frame-allocation rule.
        const float HeavyImpactSpeed = 20f;

        void OnCollisionEnter(Collision collision)
        {
            if (sparks == null || collision.contactCount == 0)
            {
                return;
            }

            // Impact = closing speed INTO the surface (normal component), the
            // same measure the damage model uses (ProcessDamageCollision).
            // The old full relativeVelocity.magnitude counted rolling/sliding
            // contact too: a car re-entering contact with road-mesh seams and
            // kerb objects at speed has tangential relative velocity equal to
            // its road speed, so every seam crossing read as an 80+ m/s
            // "crash" - [VfxDiag] measured 4,700-7,300 collision spark bursts
            // per 20s in a clean race with zero damage events. Normal closing
            // speed is ~0 when rolling along a surface and only large when
            // genuinely hitting something.
            ContactPoint contact = collision.GetContact(0);
            float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (impactSpeed < 8.5f)
            {
                return;
            }

            Vector3 contactPoint = contact.point;
            sparks.transform.position = contactPoint;
            sparks.Emit(Mathf.Clamp(Mathf.RoundToInt(impactSpeed * 1.4f), 6, 24));
            diagCollisionBursts++;

            if (impactSpeed >= HeavyImpactSpeed && damageSmoke != null)
            {
                ParticleSystem.EmitParams debrisParams = new ParticleSystem.EmitParams();
                debrisParams.position = contactPoint;
                debrisParams.velocity = -collision.relativeVelocity.normalized * 2.5f + Vector3.up * 3f;
                debrisParams.startSize = 1.1f;
                debrisParams.startLifetime = 0.6f;
                debrisParams.startColor = new Color(0.32f, 0.3f, 0.28f, 0.6f);
                int debrisBurst = Mathf.Clamp(Mathf.RoundToInt((impactSpeed - HeavyImpactSpeed) * 1.2f) + 6, 6, 18);
                for (int i = 0; i < debrisBurst; i++)
                {
                    damageSmoke.Emit(debrisParams, 1);
                }
            }
        }
    }
}
