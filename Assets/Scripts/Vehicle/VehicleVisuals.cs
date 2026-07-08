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

        // Tyre compound look (soft/medium/hard/inter/wet), applied once per
        // compound rather than every frame - only reapplied if a pit stop
        // actually changes the compound underneath us.
        Material tyreMaterial;
        bool tyreMaterialSearched;
        bool tyreLookApplied;
        TyreCompound appliedTyreCompound;

        // Bodywork picks up a wet sheen in the rain rather than staying one
        // fixed dry finish all race; primaryMaterial is shared across most of
        // the car's panels (see CreateOpenWheelCar), so one lookup covers them.
        Material bodyMaterial;
        bool bodyMaterialSearched;
        float baseBodySmoothness = -1f;

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

        // Tread-block suggestion baked into the tyre material's texture rather
        // than any extra geometry - applied once, independently of
        // UpdateTyreCompoundLook's own per-compound colour/sheen reapplication,
        // since the texture itself never changes with compound.
        bool tyreTreadTextureApplied;
        static Texture2D sharedTreadTexture;

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
            UpdateFlatSpotWobble();
            UpdateBrakeGlow();
            UpdateRainLight();
            UpdateDrsFlap();
            UpdateAeroFlex();
            UpdateSkidTrails();
            UpdateTyreCompoundLook();
            UpdateWetBodySheen();
            UpdateFrontWingDamage();
            UpdateFloorDamage();
            UpdateSuspensionFlex();
            UpdateContactShadow();
            ApplyMaterialContrastPass();
            EnsureWheelHubDetail();
            EnsureEndplateAccents();
            EnsureHaloMountDetail();
            EnsureCockpitDetail();
            EnsureLiveryAccentVariety();
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

            float targetSteer = vehicle.CurrentCommand.steer * 16f;
            visualSteerAngle = Mathf.Lerp(visualSteerAngle, targetSteer, Time.deltaTime * 10f);

            Quaternion spin = Quaternion.Euler(wheelSpinAngle, 0f, 0f);
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
                rearLeft.localRotation = spin;
            }

            if (rearRight != null)
            {
                rearRight.localRotation = spin;
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
            // instead of fading at one constant rate all the way to cold.
            float coolRate = Mathf.Lerp(0.35f, 1.3f, brakeGlowHeat);
            float rate = targetHeat > brakeGlowHeat ? 9f : coolRate;
            brakeGlowHeat = Mathf.MoveTowards(brakeGlowHeat, targetHeat, Time.deltaTime * rate);

            Color rampColor = DiscTemperatureColor(brakeGlowHeat);
            brakeDiscMaterial.SetColor("_EmissionColor", rampColor * brakeGlowHeat * 1.4f);
            UpdateRimHighlight(rampColor, brakeGlowHeat);
            UpdateCaliperHighlight(rampColor, brakeGlowHeat);
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
                            caliperMaterial.SetFloat("_Glossiness", 0.42f);
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

            brakeLightMaterial.SetColor("_EmissionColor", GlowColor * Mathf.Clamp01(intensity) * 1.6f);
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
        static void ApplySkidTrailSeverity(TrailRenderer trail, float severity)
        {
            trail.startWidth = Mathf.Lerp(0.07f, 0.32f, severity);
            trail.endWidth = Mathf.Lerp(0.01f, 0.05f, severity);
            trail.time = Mathf.Lerp(1f, 2.6f, severity);

            float alpha = Mathf.Lerp(0.14f, 0.7f, severity);
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
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
            tyreMaterial.color = color;
            tyreMaterial.SetFloat("_Metallic", metallic);
            tyreMaterial.SetFloat("_Glossiness", smoothness);
        }

        // Deliberately subtle undertones and sheen differences rather than the
        // bright sidewall bands real tyres carry - enough to tell compounds
        // apart at a glance without reproducing any official colour marking.
        static void GetTyreLook(TyreCompound compound, out Color color, out float metallic, out float smoothness)
        {
            metallic = 0.02f;
            if (compound == TyreCompound.Soft)
            {
                color = new Color(0.05f, 0.014f, 0.013f);
                smoothness = 0.34f;
            }
            else if (compound == TyreCompound.Hard)
            {
                color = new Color(0.05f, 0.05f, 0.054f);
                smoothness = 0.18f;
            }
            else if (compound == TyreCompound.Intermediate)
            {
                color = new Color(0.018f, 0.032f, 0.02f);
                smoothness = 0.24f;
            }
            else if (compound == TyreCompound.Wet)
            {
                color = new Color(0.014f, 0.02f, 0.045f);
                smoothness = 0.3f;
            }
            else
            {
                color = new Color(0.028f, 0.028f, 0.03f);
                smoothness = 0.26f;
            }
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

            const int width = 64;
            const int height = 8;
            sharedTreadTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            sharedTreadTexture.wrapMode = TextureWrapMode.Repeat;
            for (int x = 0; x < width; x++)
            {
                bool groove = (x % 9) < 2;
                float shade = groove ? 0.38f : 1f;
                for (int y = 0; y < height; y++)
                {
                    sharedTreadTexture.SetPixel(x, y, new Color(shade, shade, shade, 1f));
                }
            }

            sharedTreadTexture.Apply();
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
                    }
                }
            }

            if (bodyMaterial == null)
            {
                return;
            }

            if (baseBodySmoothness < 0f)
            {
                baseBodySmoothness = bodyMaterial.GetFloat("_Glossiness");
            }

            bool wet = vehicle.Weather == WeatherState.LightRain || vehicle.Weather == WeatherState.HeavyRain;
            float targetSmoothness = wet ? Mathf.Min(0.97f, baseBodySmoothness + 0.14f) : baseBodySmoothness;
            float currentSmoothness = bodyMaterial.GetFloat("_Glossiness");
            bodyMaterial.SetFloat("_Glossiness", Mathf.MoveTowards(currentSmoothness, targetSmoothness, Time.deltaTime * 0.6f));
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
                Vector3 target = rest + new Vector3(0f, droop, 0f);
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
            // wet-look clearcoat weave rather than a flat matte panel.
            TuneMaterialContrast("carbon floor", 0.3f, 0.78f);

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

            foundRenderer.sharedMaterial.SetFloat("_Metallic", metallic);
            foundRenderer.sharedMaterial.SetFloat("_Glossiness", smoothness);
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
            cube.GetComponent<Renderer>().sharedMaterial = material;
            Collider cubeCollider = cube.GetComponent<Collider>();
            if (cubeCollider != null)
            {
                Destroy(cubeCollider);
            }
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

            const int size = 32;
            contactShadowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
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

            contactShadowTexture.Apply();
            return contactShadowTexture;
        }

        // This file otherwise reaches into RaceManager-built materials by name
        // rather than creating its own, except for the skid trail material
        // below - mirrors RaceManager.CreateMaterial's own two overloads
        // (Standard shader, _Metallic/_Glossiness, optional _EmissionColor) so
        // any new procedural detail built here follows the same convention.
        static Material CreateMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            Material material = new Material(Shader.Find("Standard"));
            material.name = materialName;
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
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
        ParticleSystem sparks;
        ParticleSystem heatHaze;
        ParticleSystem damageSmoke;
        float previousDamagePercent = -1f;

        static Texture2D softDot;
        static Material sharedParticleMaterial;

        public void Initialize(VehicleController controller)
        {
            vehicle = controller;
            dust = CreateEmitter("Dust emitter", new Vector3(0f, 0.28f, -1.9f), new Color(0.62f, 0.51f, 0.35f, 0.5f), 0.9f, 1.5f, 2.6f);
            spray = CreateEmitter("Spray emitter", new Vector3(0f, 0.34f, -2.15f), new Color(0.7f, 0.78f, 0.84f, 0.35f), 0.65f, 1.2f, 3.4f);
            lockupSmoke = CreateEmitter("Lockup smoke emitter", new Vector3(0f, 0.2f, 1.35f), new Color(0.86f, 0.86f, 0.86f, 0.45f), 0.75f, 1.05f, 1.9f);
            sparks = CreateEmitter("Spark emitter", new Vector3(0f, 0.22f, 0f), new Color(1f, 0.74f, 0.28f, 0.9f), 0.4f, 0.14f, 7.5f);
            ParticleSystem.MainModule sparkMain = sparks.main;
            sparkMain.gravityModifier = 1.3f;
            sparkMain.maxParticles = 80;

            // Faint heat-haze puffs off the engine cover under hard acceleration.
            // Deliberately understated (small, sparse, quick to fade) - a fake
            // shimmer that draws attention to itself reads worse than none.
            heatHaze = CreateEmitter("Heat haze emitter", new Vector3(0f, 0.58f, -1.05f), new Color(1f, 0.95f, 0.85f, 0.16f), 0.5f, 0.4f, 0.5f);
            ParticleSystem.MainModule heatMain = heatHaze.main;
            heatMain.gravityModifier = -0.2f;
            heatMain.maxParticles = 40;
            heatHaze.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // Dark engine-damage smoke off the cover, distinct from the pale tyre
            // smoke: slower, longer-lived and rising, so a wounded car trails a
            // visible plume rather than only slowing down mysteriously.
            damageSmoke = CreateEmitter("Damage smoke emitter", new Vector3(0f, 0.7f, -1.35f), new Color(0.28f, 0.27f, 0.26f, 0.5f), 1.4f, 1.1f, 2.2f);
            ParticleSystem.MainModule damageMain = damageSmoke.main;
            damageMain.gravityModifier = -0.12f;
            damageMain.maxParticles = 160;
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
            main.maxParticles = 200;
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

            const int size = 32;
            softDot = new Texture2D(size, size, TextureFormat.RGBA32, false);
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

            softDot.Apply();
            return softDot;
        }

        void Update()
        {
            if (vehicle == null)
            {
                return;
            }

            float speedKph = Mathf.Abs(vehicle.CurrentSpeedKph);
            float speed01 = Mathf.InverseLerp(40f, 240f, speedKph);

            SetRate(dust, vehicle.IsOffTrackSlowdown && speedKph > 35f ? Mathf.Lerp(14f, 70f, speed01) : 0f);

            bool wet = vehicle.Weather == WeatherState.LightRain || vehicle.Weather == WeatherState.HeavyRain;
            SetRate(spray, wet && speedKph > 85f ? Mathf.Lerp(18f, 85f, speed01) : 0f);

            // Scales continuously with LockupSeverity so a small lockup puffs
            // lightly and a big one smokes hard, instead of one binary rate.
            // Colour, plume size and lifetime scale with it too - a light
            // scrub reads as a thin pale wisp, a hard lock as a bigger, darker,
            // longer-hanging burnt-rubber cloud, rather than one fixed puff
            // that only ever fires faster or slower.
            float lockupSeverity = vehicle.Tyres != null ? vehicle.Tyres.LockupSeverity : 0f;
            bool locking = lockupSeverity > 0.05f && speedKph > 60f;
            if (locking)
            {
                ParticleSystem.MainModule smokeMain = lockupSmoke.main;
                smokeMain.startColor = Color.Lerp(new Color(0.86f, 0.86f, 0.86f, 0.4f), new Color(0.3f, 0.27f, 0.25f, 0.62f), lockupSeverity);
                smokeMain.startSize = Mathf.Lerp(0.65f, 1.55f, lockupSeverity);
                smokeMain.startLifetime = Mathf.Lerp(0.7f, 1.7f, lockupSeverity);
            }

            SetRate(lockupSmoke, locking ? Mathf.Lerp(15f, 90f, lockupSeverity) : 0f);

            // Only under real load - hard on the throttle and actually moving,
            // not idling on the grid - and scaled by how hard, so it never
            // becomes a constant background effect.
            float engineLoad = vehicle.EffectiveThrottle;
            bool underLoad = engineLoad > 0.7f && speedKph > 25f;
            SetRate(heatHaze, underLoad ? Mathf.Lerp(3f, 12f, Mathf.InverseLerp(0.7f, 1f, engineLoad)) : 0f);

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

            bool kerbSparking = vehicle.IsOnKerb && speedKph > 130f;
            float floorScrape = vehicle.Damage != null ? Mathf.Clamp01(vehicle.Damage.floor) : 0f;
            float rate = kerbSparking ? Mathf.Lerp(4f, 24f, Mathf.InverseLerp(130f, 320f, speedKph)) : 0f;
            rate += floorScrape * Mathf.InverseLerp(80f, 300f, speedKph) * 14f;
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
            }

            previousDamagePercent = damagePercent;

            if (damageSmoke == null)
            {
                return;
            }

            float damage01 = Mathf.InverseLerp(60f, 100f, damagePercent);
            if (damage01 > 0f)
            {
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

        void OnCollisionEnter(Collision collision)
        {
            if (sparks == null || collision.relativeVelocity.magnitude < 8.5f)
            {
                return;
            }

            if (collision.contactCount > 0)
            {
                sparks.transform.position = collision.GetContact(0).point;
            }

            sparks.Emit(Mathf.Clamp(Mathf.RoundToInt(collision.relativeVelocity.magnitude * 1.4f), 6, 24));
        }
    }
}
