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
            UpdateBrakeGlow();
            UpdateRainLight();
            UpdateDrsFlap();
            UpdateSkidTrails();
            UpdateTyreCompoundLook();
            UpdateWetBodySheen();
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

        void UpdateRainLight()
        {
            if (brakeLightMaterial == null)
            {
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
