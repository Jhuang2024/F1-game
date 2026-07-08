using UnityEngine;

namespace LocalFormulaRacing
{
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        // Pit-exit speed fix: entry/service approach keeps the real-world 80kph
        // pit-lane limit (blind merge into the pits, crew/cones/boxes nearby -
        // this should stay cautious); the cleared exit stretch after release
        // (see PitExitFastLimiter) gets a meaningfully higher cap so the drive
        // back to the racing line doesn't read as a fixed-length "wait it out"
        // tunnel once the car has nothing left to be careful of.
        const float PitEntryLimiterCapKph = 80f;
        const float PitExitLimiterCapKph = 108f;

        public TyreState Tyres { get; private set; }
        public DamageState Damage { get; private set; }
        public float CurrentSpeedKph { get; private set; }
        public int CurrentGear { get; private set; }
        public float ErsBattery { get; private set; }
        public bool ErsDeploying { get; private set; }
        public bool ErsHarvesting { get; private set; }
        public bool DrsActive { get; private set; }
        public bool PitRequested { get; private set; }
        public bool IsOnRoad { get; private set; }
        public bool IsOnKerb { get; private set; }
        public bool IsOffTrackSlowdown { get; private set; }
        public bool IsPlayerControlled { get; private set; }
        public bool IsHeldInPit { get; private set; }
        public bool IsHeldOnGrid { get; private set; }
        public bool PitLimiterActive { get; private set; }
        // Pit-exit speed fix: the pit limiter previously enforced the same 80kph
        // cap on the way IN (approaching a blind merge, cones, crew) and on the
        // way OUT (a straight, already-cleared lane back to the racing line) -
        // real pit lanes are just as limited both ways, but a game pit lane this
        // short made the exit crawl feel like a fake "wait it out" tunnel rather
        // than a real corner of the track. Set true only for the post-release
        // exit stretch (RaceManager.UpdatePitRelease/HandlePitService), never for
        // the entry/service approach, so entry stays exactly as controlled as
        // before while exit gets a genuinely higher, still-limited pace.
        public bool PitExitFastLimiter { get; private set; }
        public float FuelKg { get { return fuelKg; } }
        public float UndersteerAmount { get; private set; }
        public float OversteerAmount { get; private set; }
        public float EffectiveThrottle { get; private set; }
        public float EffectiveBrake { get; private set; }
        public float LastTyreGripMultiplier { get; private set; }
        public float LastPowerMultiplier { get; private set; }
        public float LastGearTorqueMultiplier { get; private set; }
        public float TargetTopSpeedKph { get; private set; }
        public float LateralDistance { get; private set; }
        public string ActiveSlowdownReason { get; private set; }
        public string LastDamageDebug { get; private set; }
        public VehicleCommand CurrentCommand { get { return command; } }

        public CarPerformanceData CarData { get; private set; }
        public TrackRuntime Track { get; private set; }
        public WeatherState Weather { get; private set; }
        // Exposed read-only so other vehicle-owned components (e.g. VehicleVisuals)
        // can gate new visual work behind the same settings every car already holds
        // a reference to, without needing their own separate settings plumbing.
        public GameSettingsData Settings { get { return settings; } }

        Rigidbody body;
        VehicleCommand command;
        bool initialized;
        bool manualGears;
        GameSettingsData settings;
        float fuelKg = 35f;
        float pitCooldown;
        Vector3 gridHoldPosition;
        Quaternion gridHoldRotation;
        float scrapeDamageCooldown;
        float stuckPowerDebugTimer;
        float smoothedThrottle;
        float smoothedBrake;
        bool lowBatteryForcedHarvest;
        static PhysicMaterial vehiclePhysicsMaterial;

        // Garage setup trade-offs (player car only); all neutral at 1.
        float setupTopSpeedMultiplier = 1f;
        float setupGripMultiplier = 1f;
        float setupBrakeMultiplier = 1f;
        float setupKerbGrip = 0.92f;
        float setupWearBias = 1f;

        const int GearCount = 8;
        const float RaceSpeedCeilingKph = 350f;
        // DRS needs headroom above the normal ceiling or its top-speed bonus gets
        // clamped away to nothing on cars whose base target is already near 350.
        const float DrsSpeedCeilingKph = 392f;
        const float DrsTopSpeedBonusKph = 32f;
        // ERS buff: raised from 20 - with the stronger deploy force below the
        // car can now actually accelerate up to a ceiling this much higher
        // within a normal straight, instead of the old ceiling being mostly
        // aspirational because the underlying push was too weak to reach it.
        const float ErsTopSpeedBonusKph = 26f;
        static readonly float[] AutoShiftUpKph = { 0f, 62f, 102f, 142f, 186f, 232f, 282f, 322f };
        static readonly float[] GearTorqueMultipliers = { 1.72f, 1.52f, 1.34f, 1.18f, 1.05f, 0.94f, 0.84f, 0.76f };

        public void Initialize(CarPerformanceData carData, TrackRuntime track, TyreCompound compound, bool useManualGears, GameSettingsData gameSettings, bool playerControlled)
        {
            CarData = carData;
            Track = track;
            Weather = track == null ? WeatherState.Clear : track.weather;
            manualGears = useManualGears;
            settings = gameSettings;
            IsPlayerControlled = playerControlled;
            body = GetComponent<Rigidbody>();
            body.mass = 760f + fuelKg;
            body.drag = 0.004f;
            body.angularDrag = 4.8f;
            body.centerOfMass = new Vector3(0f, -0.42f, 0.05f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            ApplyLowFrictionPhysicsMaterial();
            ApplyCarSetup();
            Tyres = new TyreState();
            Tyres.Reset(compound);
            Damage = new DamageState();
            CurrentGear = 1;
            ErsBattery = 0.72f;
            TargetTopSpeedKph = CalculateTargetTopSpeedKph(new VehicleCommand());
            LastGearTorqueMultiplier = GearTorqueMultipliers[0];
            initialized = true;
            GameLog.Info("[Damage] " + name + " starting damage " + Damage.OverallPercent.ToString("0.0") + "%");
        }

        // Translate the saved garage setup into small, readable physics trade-offs.
        // More wing: corner grip up, straight-line speed down. Brake bias off-center:
        // stronger stopping power but a stability cost. Stiff suspension: slightly
        // more grip on smooth tarmac, worse kerb behavior, faster tyre wear. Low
        // ride height: less drag but the car hates kerbs.
        void ApplyCarSetup()
        {
            setupTopSpeedMultiplier = 1f;
            setupGripMultiplier = 1f;
            setupBrakeMultiplier = 1f;
            setupKerbGrip = 0.92f;
            setupWearBias = 1f;
            if (!IsPlayerControlled || settings == null)
            {
                return;
            }

            float wing = (settings.setupFrontWing + settings.setupRearWing) * 0.5f - 3f; // -2 .. +2
            float bias = settings.setupBrakeBias - 3f;
            float stiffness = settings.setupSuspension - 3f;
            float ride = settings.setupRideHeight - 3f; // negative = low

            setupGripMultiplier = (1f + wing * 0.016f + stiffness * 0.004f) * (1f - Mathf.Abs(bias) * 0.004f);
            setupTopSpeedMultiplier = 1f - wing * 0.011f - ride * 0.0045f;
            setupBrakeMultiplier = 1f + bias * 0.014f;
            setupKerbGrip = Mathf.Clamp(0.92f - stiffness * 0.018f + Mathf.Min(0f, ride) * 0.02f, 0.78f, 0.98f);
            setupWearBias = Mathf.Clamp(1f + stiffness * 0.05f, 0.85f, 1.2f);
        }

        // Mid-session weather transitions (mixed forecasts) update grip and tyre
        // behavior without respawning the field.
        public void SetWeather(WeatherState weather)
        {
            Weather = weather;
        }

        public void SetCommand(VehicleCommand newCommand)
        {
            command = newCommand;
            if (newCommand.pitRequest)
            {
                PitRequested = true;
            }
        }

        public void ClearPitRequest()
        {
            PitRequested = false;
        }

        // Automatic pit stop fix: lets RaceManager latch a pit request directly
        // (e.g. the player's pre-race strategy plan reaching its target lap
        // without them pressing the manual pit key) without going through
        // SetCommand, which would otherwise require overwriting this frame's
        // whole throttle/steer/brake command just to smuggle pitRequest=true
        // through it.
        public void RequestPit()
        {
            PitRequested = true;
        }

        public void CompletePitStop(TyreCompound compound)
        {
            Tyres.Reset(compound);
            Damage.RepairPitDamage();
            pitCooldown = 4f;
            ClearPitRequest();
        }

        public void SetPitServiceHold(bool held)
        {
            IsHeldInPit = held;
        }

        public bool IsPitGuided { get; private set; }

        // While a car is animated through the pit lane it moves on rails: kinematic
        // and non-colliding so queued cars can never physics-fight, spin each other,
        // or take damage in the lane. Always restored on release.
        public void SetPitGuidance(bool guided)
        {
            if (IsPitGuided == guided)
            {
                return;
            }

            IsPitGuided = guided;
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            if (body != null)
            {
                if (guided)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.isKinematic = true;
                    body.detectCollisions = false;
                }
                else
                {
                    body.isKinematic = false;
                    body.detectCollisions = true;
                    body.velocity = transform.forward * 14f;
                }
            }
        }

        public void SetPitLimiter(bool active)
        {
            PitLimiterActive = active;
        }

        public void SetPitExitFastLimiter(bool active)
        {
            PitExitFastLimiter = active;
        }

        // Race-control speed cap (Part 2): the same hard, physical speed-cap
        // mechanism the pit limiter already uses, generalized to any race-control
        // reason (local yellow / VSC / safety car) instead of only the fixed pit
        // lane number. RaceManager calls this every tick for every car (player and
        // AI alike) with whatever cap currently applies to that specific car;
        // 9999 means "no cap", the sentinel ApplyForces checks against below.
        public float RaceControlSpeedCapKph { get; private set; } = 9999f;
        public void SetRaceControlSpeedCap(float capKph)
        {
            RaceControlSpeedCapKph = capKph;
        }

        public void SnapToPitPose(Vector3 position, Quaternion rotation)
        {
            transform.position = position;
            transform.rotation = rotation;
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            smoothedThrottle = 0f;
            smoothedBrake = 1f;
        }

        public float GuideToPitPose(Vector3 position, Quaternion rotation, float moveSpeed, float rotateSpeed)
        {
            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            Vector3 previousPosition = transform.position;
            Vector3 nextPosition = Vector3.MoveTowards(transform.position, position, moveSpeed * dt);
            Quaternion nextRotation = Quaternion.RotateTowards(transform.rotation, rotation, rotateSpeed * dt);
            transform.position = nextPosition;
            transform.rotation = nextRotation;
            if (body != null)
            {
                body.position = nextPosition;
                body.rotation = nextRotation;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.MoveTowards(body.velocity, Vector3.zero, 18f * dt);
                    body.angularVelocity = Vector3.zero;
                }
            }

            smoothedThrottle = 0f;
            smoothedBrake = 1f;
            CurrentSpeedKph = Vector3.Distance(previousPosition, nextPosition) / dt * 3.6f;
            return Vector3.Distance(nextPosition, position);
        }

        public void SetGridHold(bool held)
        {
            if (held && !IsHeldOnGrid)
            {
                gridHoldPosition = transform.position;
                gridHoldRotation = transform.rotation;
            }

            IsHeldOnGrid = held;
        }

        void FixedUpdate()
        {
            if (!initialized)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;
            if (IsPitGuided)
            {
                // On rails through the pit lane: RaceManager drives the transform,
                // physics stays out of it entirely.
                EffectiveThrottle = 0f;
                EffectiveBrake = 1f;
                ActiveSlowdownReason = "PIT GUIDE";
                return;
            }

            if (IsHeldOnGrid)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.MovePosition(gridHoldPosition);
                body.MoveRotation(gridHoldRotation);
                CurrentSpeedKph = 0f;
                EffectiveThrottle = 0f;
                EffectiveBrake = 1f;
                ActiveSlowdownReason = "GRID HOLD";
                return;
            }

            scrapeDamageCooldown = Mathf.Max(0f, scrapeDamageCooldown - dt);
            CurrentSpeedKph = Vector3.Dot(body.velocity, transform.forward) * 3.6f;
            float absoluteSpeedKph = body.velocity.magnitude * 3.6f;
            TrackProgress progress = Track == null ? new TrackProgress() : Track.GetProgress(transform.position);
            LateralDistance = progress.lateralDistance;
            // Uses the actual (possibly hairpin-widened) drivable half-width at this
            // point on track - the flat field would apply an off-track slowdown
            // penalty on tarmac that's legitimately part of a widened hairpin.
            IsOffTrackSlowdown = Track != null && Mathf.Abs(progress.lateralDistance) > Track.HalfWidthAt(progress.distance) + 1.6f;
            if (PitLimiterActive)
            {
                IsOffTrackSlowdown = false;
            }

            IsOnRoad = Track == null || !IsOffTrackSlowdown;
            IsOnKerb = Track != null && Track.IsOnKerb(transform.position);

            VehicleCommand assisted = GetAssistedCommand(command, absoluteSpeedKph, progress);
            SmoothDriveCommand(ref assisted, absoluteSpeedKph, dt);
            EffectiveThrottle = assisted.throttle;
            EffectiveBrake = assisted.brake;
            float lateralSpeed = Mathf.Abs(Vector3.Dot(body.velocity, transform.right));
            float slipEnergy = Mathf.Clamp01(lateralSpeed / Mathf.Max(6f, body.velocity.magnitude * 0.32f)) * Mathf.InverseLerp(28f, 260f, absoluteSpeedKph);
            slipEnergy = Mathf.Clamp01(slipEnergy + Mathf.Abs(assisted.steer) * Mathf.InverseLerp(80f, 270f, absoluteSpeedKph) * 0.35f);
            UpdateGear(absoluteSpeedKph);
            Tyres.Tick(absoluteSpeedKph, assisted.brake, assisted.steer, assisted.throttle, slipEnergy * setupWearBias, Weather, CarData.tyreManagement, dt);
            ApplyForces(assisted, absoluteSpeedKph, progress, dt);
            ApplySteering(assisted, absoluteSpeedKph, dt);
            StabilizeChassis(dt);

            float burn = Mathf.Lerp(0.012f, 0.03f, Mathf.Clamp01(assisted.throttle));
            fuelKg = Mathf.Max(4f, fuelKg - dt * burn);
            body.mass = 760f + fuelKg;
            pitCooldown = Mathf.Max(0f, pitCooldown - dt);
            LogSuspiciousPowerLoss(absoluteSpeedKph, dt);
        }

        void LogSuspiciousPowerLoss(float absoluteSpeedKph, float dt)
        {
            if (EffectiveThrottle > 0.55f && EffectiveBrake < 0.15f && absoluteSpeedKph < 12f && !IsHeldInPit && !IsHeldOnGrid)
            {
                stuckPowerDebugTimer -= dt;
                if (stuckPowerDebugTimer <= 0f)
                {
                    stuckPowerDebugTimer = 1.2f;
                    GameLog.Warn("[DriveDebug] " + name +
                                     " low speed despite throttle speedKph=" + absoluteSpeedKph.ToString("0.0") +
                                     " throttle=" + EffectiveThrottle.ToString("0.00") +
                                     " brake=" + EffectiveBrake.ToString("0.00") +
                                     " damage=" + (Damage == null ? -1f : Damage.OverallPercent).ToString("0.0") +
                                     " offTrack=" + IsOffTrackSlowdown +
                                     " lateral=" + LateralDistance.ToString("0.0") +
                                     " tyreGrip=" + LastTyreGripMultiplier.ToString("0.00") +
                                     " power=" + LastPowerMultiplier.ToString("0.00") +
                                     " slowdown=" + ActiveSlowdownReason +
                                     " lastDamage=" + LastDamageDebug);
                }

                return;
            }

            stuckPowerDebugTimer = 0f;
        }

        VehicleCommand GetAssistedCommand(VehicleCommand raw, float speedKph, TrackProgress progress)
        {
            VehicleCommand assisted = raw;
            if (!IsPlayerControlled || settings == null)
            {
                return assisted;
            }

            float lateralSpeed = Mathf.Abs(Vector3.Dot(body.velocity, transform.right));
            float slip = Mathf.Clamp01(lateralSpeed / 18f);
            if (settings.absAssist && assisted.brake > 0.1f)
            {
                float steeringBrakeLimit = Mathf.Lerp(1f, 0.72f, Mathf.Abs(assisted.steer));
                float lockupLimit = Mathf.Lerp(1f, 0.76f, Mathf.InverseLerp(90f, 270f, speedKph));
                assisted.brake = Mathf.Min(assisted.brake, Mathf.Clamp(Mathf.Lerp(0.98f, 0.72f, slip) * steeringBrakeLimit * lockupLimit + 0.16f, 0.68f, 1f));
            }

            if (settings.tractionControl && assisted.throttle > 0.1f)
            {
                float tractionLimit = Mathf.Lerp(1f, 0.58f, slip);
                tractionLimit *= Mathf.Lerp(0.72f, 1f, Mathf.InverseLerp(0f, 120f, speedKph));
                assisted.throttle = Mathf.Min(assisted.throttle, Mathf.Clamp01(tractionLimit + 0.16f));
            }

            if (settings.autoBrakeAssist && Track != null)
            {
                float severity = EstimateUpcomingCorner(progress.distance);
                float brakeSeverity = Mathf.Clamp01((severity - 0.18f) / 0.82f);
                float desiredSpeed = Mathf.Lerp(335f, 108f, brakeSeverity * brakeSeverity);
                if (speedKph > desiredSpeed)
                {
                    assisted.brake = Mathf.Max(assisted.brake, Mathf.Clamp01((speedKph - desiredSpeed) / 115f));
                    assisted.throttle = Mathf.Min(assisted.throttle, Mathf.Lerp(0.55f, 0.18f, brakeSeverity));
                }
            }

            if (ErsBattery < 0.02f)
            {
                lowBatteryForcedHarvest = true;
                settings.ersMode = (int)ErsStrategyMode.Harvest;
            }
            else if (lowBatteryForcedHarvest && ErsBattery > 0.28f)
            {
                lowBatteryForcedHarvest = false;
                settings.ersMode = (int)ErsStrategyMode.Balanced;
            }

            // ERS mode governs when the car deploys automatically, but holding the
            // manual override key must always be able to trigger a deploy while
            // there is meaningful charge left - the mode is a strategy default, not
            // a hard lock on the driver's own overtake button.
            bool autoDeployRequested = false;
            if (settings.ersMode == (int)ErsStrategyMode.Attack && ErsBattery > 0.06f && assisted.throttle > 0.55f)
            {
                autoDeployRequested = true;
            }
            else if (settings.ersMode == (int)ErsStrategyMode.Balanced && ErsBattery > 0.24f && assisted.throttle > 0.88f && speedKph > 130f)
            {
                autoDeployRequested = true;
            }

            // The manual overtake key always wins over the strategy mode's own
            // auto-deploy logic (including Harvest, which has no auto-deploy branch
            // above) as long as there is meaningful charge and throttle - a driver
            // holding Shift should never find ERS refusing to fire just because the
            // dial is set to Harvest, nor find it stuck on with no way to cut it.
            bool manualDeployRequested = raw.ers && ErsBattery > 0.03f && assisted.throttle > 0.05f;
            assisted.ers = autoDeployRequested || manualDeployRequested;

            return assisted;
        }

        void SmoothDriveCommand(ref VehicleCommand assisted, float speedKph, float dt)
        {
            float throttleRise = Mathf.Lerp(2.35f, 5.4f, Mathf.InverseLerp(80f, 240f, speedKph));
            float throttleFall = 10.5f;
            smoothedThrottle = Mathf.MoveTowards(
                smoothedThrottle,
                assisted.throttle,
                dt * (assisted.throttle > smoothedThrottle ? throttleRise : throttleFall));

            smoothedBrake = Mathf.MoveTowards(
                smoothedBrake,
                assisted.brake,
                dt * (assisted.brake > smoothedBrake ? 26f : 15f));

            assisted.throttle = Mathf.Clamp01(smoothedThrottle);
            assisted.brake = Mathf.Clamp01(smoothedBrake);
        }

        void ApplyForces(VehicleCommand activeCommand, float absoluteSpeedKph, TrackProgress progress, float dt)
        {
            float speedMps = body.velocity.magnitude;
            float forwardSpeed = Vector3.Dot(body.velocity, transform.forward);

            // AI stuck-recovery nudge (Part 2/3): a small, speed-capped rearward
            // push so a car pressed against a barrier can actually back away
            // instead of endlessly wheelspinning into it. Bounded to low speed so
            // it can never be used as an actual reverse gear during racing.
            if (activeCommand.reverseAssist && forwardSpeed > -14f)
            {
                body.AddForce(-transform.forward * 7.5f, ForceMode.Acceleration);
                ActiveSlowdownReason = "RECOVERY REVERSE";
            }

            // DRS deployment fix: the wing must auto-close the instant the driver
            // brakes (real F1 behaviour - a brake-pressure sensor kills the actuator),
            // not stay open bleeding drag/downforce reduction into the braking zone.
            // This is the single place DrsActive is decided, so it governs the drag
            // coefficient and top-speed bonus below for both the player and every AI
            // car identically - no separate logic to keep in sync.
            DrsActive = activeCommand.drs && absoluteSpeedKph > 90f && activeCommand.brake < 0.05f;
            TargetTopSpeedKph = CalculateTargetTopSpeedKph(activeCommand);
            if (PitLimiterActive)
            {
                DrsActive = false;
                TargetTopSpeedKph = Mathf.Min(TargetTopSpeedKph, PitExitFastLimiter ? PitExitLimiterCapKph : PitEntryLimiterCapKph);
            }

            // Race-control cap uses the pit limiter's own gentle enforcement curve
            // (a bled-off approach to the cap, not the harsher generic overspeed
            // drag further below) rather than a second, differently-tuned limiter -
            // one physical mechanism, driven by two different reasons.
            bool raceControlCapActive = RaceControlSpeedCapKph < 900f;
            if (raceControlCapActive)
            {
                DrsActive = false;
                TargetTopSpeedKph = Mathf.Min(TargetTopSpeedKph, RaceControlSpeedCapKph);
            }

            float speedCapFloorKph = PitLimiterActive ? (PitExitFastLimiter ? PitExitLimiterCapKph : PitEntryLimiterCapKph) : (raceControlCapActive ? RaceControlSpeedCapKph : 0f);
            bool speedCapEngaged = PitLimiterActive || raceControlCapActive;

            float topSpeed = TargetTopSpeedKph / 3.6f;
            float tyreGrip = Tyres.GripMultiplier(Weather);
            LastTyreGripMultiplier = tyreGrip;
            LastPowerMultiplier = Damage.PowerMultiplier;
            LastGearTorqueMultiplier = GearTorqueMultiplier(absoluteSpeedKph);
            float gripStat = Mathf.Lerp(0.9f, 1.28f, CarData.cornering / 100f);
            float grip = tyreGrip * gripStat * Damage.HandlingMultiplier * setupGripMultiplier;
            ActiveSlowdownReason = "NONE";
            if (IsOffTrackSlowdown)
            {
                grip *= 0.58f;
                ActiveSlowdownReason = "OFF TRACK DRAG";
            }
            else if (IsOnKerb)
            {
                grip *= setupKerbGrip;
                ActiveSlowdownReason = "KERB";
            }

            Vector3 lateralVelocity = Vector3.Dot(body.velocity, transform.right) * transform.right;
            float lateralSlip = Mathf.Clamp01(lateralVelocity.magnitude / Mathf.Max(6f, speedMps * 0.38f));
            UndersteerAmount = Mathf.Clamp01(Mathf.Abs(activeCommand.steer) * Mathf.InverseLerp(120f, 310f, absoluteSpeedKph) * Mathf.Lerp(0.45f, 1.25f, lateralSlip) * (activeCommand.throttle > 0.35f ? 1.1f : 0.8f));
            OversteerAmount = Mathf.Clamp01(lateralSlip * Mathf.Lerp(0.4f, 1.2f, activeCommand.throttle) * (1f - Mathf.Clamp01(tyreGrip)));
            float lateralGripForce = (10f + grip * 18f) * Mathf.Lerp(1.12f, 0.78f, UndersteerAmount);
            body.AddForce(-lateralVelocity * lateralGripForce, ForceMode.Acceleration);

            float accelerationStat = Mathf.Lerp(11.4f, 20.4f, CarData.acceleration / 100f);
            float engineStat = Mathf.Lerp(0.96f, 1.24f, CarData.enginePower / 100f);
            float fuelPenalty = Mathf.Lerp(0.9f, 1f, Mathf.InverseLerp(42f, 5f, fuelKg));
            // Harvest mode banks charge faster at the cost of a weaker deploy punch;
            // Attack mode hits harder on deploy but recovers charge more slowly. Only
            // the human player's own strategy dial drives this - AI cars run their
            // own ShouldAiUseErs logic and always use the neutral multiplier.
            float harvestModeMultiplier = 1f;
            float deployModeMultiplier = 1f;
            if (IsPlayerControlled && settings != null)
            {
                if (settings.ersMode == (int)ErsStrategyMode.Harvest)
                {
                    harvestModeMultiplier = 1.9f;
                    deployModeMultiplier = 0.82f;
                }
                else if (settings.ersMode == (int)ErsStrategyMode.Attack)
                {
                    harvestModeMultiplier = 0.8f;
                    deployModeMultiplier = 1.2f;
                }
            }

            float ersBoost = 0f;
            ErsDeploying = activeCommand.ers && ErsBattery > 0.01f;
            ErsHarvesting = false;
            if (ErsDeploying)
            {
                // ERS buff: raised from 11-18 - the old range only ever translated
                // into a ~6 km/h felt gain on a straight because the force was too
                // weak to meaningfully move the equilibrium speed against drag
                // before the straight ran out. This range, combined with the
                // steeper/earlier ramp-in below, is tuned to land in the
                // requested 15-20 km/h felt-gain range on a typical straight.
                ersBoost = Mathf.Lerp(19f, 30f, CarData.ersEfficiency / 100f) * deployModeMultiplier;
                ErsBattery = Mathf.Clamp01(ErsBattery - dt * Mathf.Lerp(0.11f, 0.16f, activeCommand.throttle));
            }

            if (activeCommand.brake > 0.1f)
            {
                ErsBattery = Mathf.Clamp01(ErsBattery + dt * activeCommand.brake * activeCommand.brake * Mathf.Lerp(0.28f, 0.42f, CarData.ersEfficiency / 100f) * harvestModeMultiplier);
                ErsHarvesting = true;
            }
            else if (activeCommand.throttle < 0.08f && absoluteSpeedKph > 80f)
            {
                ErsBattery = Mathf.Clamp01(ErsBattery + dt * Mathf.Lerp(0.022f, 0.05f, CarData.ersEfficiency / 100f) * harvestModeMultiplier);
                ErsHarvesting = true;
            }

            float forwardSpeedKph = Mathf.Max(0f, forwardSpeed * 3.6f);
            float speedRatio = Mathf.Clamp01(forwardSpeedKph / Mathf.Max(1f, TargetTopSpeedKph));
            if (speedCapEngaged && forwardSpeedKph > speedCapFloorKph + 2f)
            {
                float limiterBrake = Mathf.Min(12f, (forwardSpeedKph - speedCapFloorKph) * 0.12f);
                body.AddForce(-transform.forward * limiterBrake, ForceMode.Acceleration);
                ActiveSlowdownReason = PitLimiterActive ? "PIT LIMITER" : "RACE CONTROL LIMITER";
            }

            float highSpeedPower = Mathf.Lerp(1.2f, 0.82f, speedRatio);
            if (DrsActive)
            {
                highSpeedPower += 0.1f;
            }

            float limiterWindow = speedCapEngaged ? 11f / 3.6f : 0.7f;
            float speedLimiter = Mathf.Clamp01((topSpeed + limiterWindow - forwardSpeed) / limiterWindow);
            if (!IsHeldInPit && activeCommand.throttle > 0.01f && speedLimiter > 0.01f)
            {
                float driveAcceleration = accelerationStat *
                                          engineStat *
                                          fuelPenalty *
                                          Tyres.TractionMultiplier *
                                          Damage.PowerMultiplier *
                                          LastGearTorqueMultiplier *
                                          highSpeedPower *
                                          Mathf.Lerp(0.72f, 1f, Mathf.InverseLerp(32f, 145f, forwardSpeedKph));
                // ERS deploy is throttled down at low speed so the extra shove doesn't
                // turn corner exits into wheelspin chaos - it ramps up to full strength
                // by the time the car is doing meaningful straight-line speed. Ramp
                // widened/moved earlier (was 40-140) so the boost is already near full
                // strength for most of a straight rather than only right at the end.
                float ersSpeedRamp = Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(25f, 105f, forwardSpeedKph));
                body.AddForce(transform.forward * activeCommand.throttle * ((driveAcceleration * speedLimiter) + ersBoost * ersSpeedRamp), ForceMode.Acceleration);
                if (activeCommand.brake < 0.05f && !IsOffTrackSlowdown && forwardSpeedKph < TargetTopSpeedKph - 6f)
                {
                    float pullThrough = Mathf.Lerp(5.6f, 2.0f, speedRatio) * activeCommand.throttle * speedLimiter;
                    body.AddForce(transform.forward * pullThrough, ForceMode.Acceleration);
                }
            }
            else if (IsHeldInPit)
            {
                ActiveSlowdownReason = "PIT HOLD";
            }
            else if (forwardSpeed >= topSpeed)
            {
                ActiveSlowdownReason = "TOP SPEED LIMIT";
            }

            // A locked tyre slides instead of gripping, so it brakes LESS effectively
            // than a gripping one - this is why lockups cost you time in real racing.
            // Scales continuously with LockupSeverity rather than a binary cliff.
            float lockupBrakeFactor = Tyres.LockupSeverity > 0f ? Mathf.Lerp(1f, 0.55f, Tyres.LockupSeverity) : 1f;
            float brakeStat = Mathf.Lerp(33f, 56f, CarData.braking / 100f) *
                              Tyres.BrakingMultiplier *
                              Mathf.Lerp(1.04f, 1.42f, Mathf.InverseLerp(80f, 330f, absoluteSpeedKph)) *
                              Damage.HandlingMultiplier *
                              setupBrakeMultiplier *
                              lockupBrakeFactor;
            if (activeCommand.brake > 0.01f || IsHeldInPit)
            {
                if (activeCommand.brake > 0.01f)
                {
                    ActiveSlowdownReason = "BRAKE INPUT";
                }

                float brakeInput = IsHeldInPit ? 1f : BrakeResponse(activeCommand.brake);
                if (forwardSpeed > 2f)
                {
                    body.AddForce(-transform.forward * brakeInput * brakeStat, ForceMode.Acceleration);
                }
                else if (forwardSpeed < -2f)
                {
                    body.AddForce(transform.forward * brakeInput * brakeStat, ForceMode.Acceleration);
                }
                else
                {
                    body.AddForce(-transform.forward * brakeInput * 8f, ForceMode.Acceleration);
                }

                if (speedMps > 5f)
                {
                    body.AddForce(-body.velocity.normalized * brakeInput * brakeStat * 0.18f, ForceMode.Acceleration);
                }
            }

            float dragCoefficient = DrsActive ? 0.0003f : 0.00054f;
            dragCoefficient *= Mathf.Lerp(1.1f, 0.84f, CarData.aeroEfficiency / 100f);
            dragCoefficient *= Mathf.Lerp(1.02f, 0.88f, Mathf.InverseLerp(1, GearCount, CurrentGear));
            dragCoefficient /= Mathf.Max(0.55f, Damage.AeroMultiplier);
            body.AddForce(-body.velocity.normalized * speedMps * speedMps * dragCoefficient, ForceMode.Acceleration);

            if (forwardSpeed > topSpeed)
            {
                float excessSpeed = forwardSpeed - topSpeed;
                float limiterDrag = speedCapEngaged ? Mathf.Min(5.5f, excessSpeed * 0.55f) : Mathf.Min(9f, excessSpeed * 2.2f);
                body.AddForce(-transform.forward * limiterDrag, ForceMode.Acceleration);
            }

            float downforce = speedMps * speedMps * 0.0022f * Mathf.Lerp(0.85f, 1.18f, CarData.aeroEfficiency / 100f) * Damage.AeroMultiplier;
            body.AddForce(Vector3.down * downforce, ForceMode.Acceleration);

            if (IsOffTrackSlowdown)
            {
                body.AddForce(-body.velocity * 0.95f, ForceMode.Acceleration);
            }

            if (IsOnKerb)
            {
                body.AddForce(transform.right * Mathf.Sin(Time.time * 35f) * 1.1f, ForceMode.Acceleration);
            }

            if (Damage.OverallPercent > 35f && ActiveSlowdownReason == "NONE")
            {
                ActiveSlowdownReason = "DAMAGE";
            }

            if (ActiveSlowdownReason == "NONE" && speedCapEngaged)
            {
                ActiveSlowdownReason = PitLimiterActive ? "PIT LIMITER" : "RACE CONTROL LIMITER";
            }
        }

        void ApplySteering(VehicleCommand activeCommand, float speedKph, float dt)
        {
            float speedFactor = Mathf.Lerp(0.34f, 1f, Mathf.Clamp01(speedKph / 62f));
            // Barrier-avoidance fix round 3: floor raised (was 0.54) - cars were
            // still running wide into barriers through corners at real speed even
            // after the medium-speed authority extension below, because this floor
            // still cut turning authority nearly in half by 320kph regardless of
            // that extension. More authority retained at genuine high speed too.
            float highSpeedLimit = Mathf.Lerp(1f, 0.66f, Mathf.InverseLerp(90f, 320f, speedKph));
            float tyreGrip = Tyres.GripMultiplier(Weather);
            float turnRate = Mathf.Lerp(68f, 112f, CarData.chassisBalance / 100f) * speedFactor * highSpeedLimit * tyreGrip * Damage.HandlingMultiplier;
            // Tight-corner authority: a genuine hairpin's real turn radius needs more
            // rotational authority than cruising-speed turnRate provides even at
            // speedFactor's max (1.0 is reached by ~62kph already, well above a real
            // hairpin's actual radius requirement) - without this, cars physically
            // could not tighten their arc enough and ran wide through the tightest
            // corners no matter how low a speed the driver braked to.
            // Medium/fast-corner extension: this used to fade out completely by
            // 120kph, so a car carrying real medium/fast-corner speed (which the AI
            // now legitimately targets, up to ~100% of straight-line pace) had no
            // extra turning margin at all beyond the base curve - any small line
            // error clipped the barrier instead of being correctable. Extended into
            // a second, gentler taper through the medium-speed range instead of
            // snapping straight to 1x, so cars can actually hold a tighter line at
            // the higher speeds they're now carrying without needing to slow down
            // further.
            // Barrier-avoidance fix round 3: pushed again (was 1.4->1.12 low
            // segment, 1.12->1f high segment) and no longer fully converges back
            // to 1x at high speed - keeps a genuine, permanent margin of extra
            // authority even at top speed instead of fully giving it back, paired
            // with the highSpeedLimit floor raise above.
            float tightCorneringBoost = speedKph <= 120f
                ? Mathf.Lerp(1.5f, 1.2f, Mathf.Clamp01((speedKph - 35f) / 85f))
                : Mathf.Lerp(1.2f, 1.08f, Mathf.Clamp01((speedKph - 120f) / 160f));
            turnRate *= tightCorneringBoost;
            turnRate *= Mathf.Lerp(1.04f, 0.72f, UndersteerAmount);
            float steerAmount = activeCommand.steer * turnRate * dt;
            if (Mathf.Abs(steerAmount) > 0.0001f)
            {
                body.MoveRotation(body.rotation * Quaternion.Euler(0f, steerAmount, 0f));
            }
        }

        float EstimateUpcomingCorner(float distance)
        {
            Vector3 pointA;
            Vector3 forwardA;
            Vector3 rightA;
            Vector3 pointB;
            Vector3 forwardB;
            Vector3 rightB;
            // Windows sized for normalized 4-5.6 km circuits: braking assist needs to
            // see the corner from further out now that straights are full length.
            Track.SampleAtDistance(distance + 26f, out pointA, out forwardA, out rightA);
            Track.SampleAtDistance(distance + 98f, out pointB, out forwardB, out rightB);
            return Mathf.Clamp01(Vector3.Angle(forwardA, forwardB) / 74f);
        }

        void StabilizeChassis(float dt)
        {
            Vector3 euler = transform.eulerAngles;
            Quaternion target = Quaternion.Euler(0f, euler.y, 0f);
            body.MoveRotation(Quaternion.Slerp(body.rotation, target, dt * 5f));
            Vector3 position = body.position;
            float targetRideHeight = 0.42f;
            if (Track != null)
            {
                targetRideHeight = Track.GetProgress(position).nearestPoint.y + 0.42f;
            }

            if (position.y < targetRideHeight - 0.1f || position.y > targetRideHeight + 0.35f)
            {
                position.y = Mathf.Lerp(position.y, targetRideHeight, dt * 8f);
                body.position = position;
            }
        }

        void UpdateGear(float speedKph)
        {
            if (manualGears)
            {
                if (command.shiftUp)
                {
                    CurrentGear = Mathf.Clamp(CurrentGear + 1, 1, GearCount);
                }

                if (command.shiftDown)
                {
                    CurrentGear = Mathf.Clamp(CurrentGear - 1, 1, GearCount);
                }

                return;
            }

            int gear = 1;
            for (int i = 1; i < AutoShiftUpKph.Length; i++)
            {
                if (speedKph > AutoShiftUpKph[i])
                {
                    gear = i + 1;
                }
            }

            CurrentGear = Mathf.Clamp(gear, 1, GearCount);
        }

        float GearTorqueMultiplier(float speedKph)
        {
            int index = Mathf.Clamp(CurrentGear - 1, 0, GearCount - 1);
            float topFade = Mathf.Lerp(1f, 0.72f, Mathf.InverseLerp(260f, RaceSpeedCeilingKph, speedKph));
            return GearTorqueMultipliers[index] * topFade;
        }

        float CalculateTargetTopSpeedKph(VehicleCommand activeCommand)
        {
            float carTopSpeed = CarData == null || CarData.topSpeed <= 0 ? 337f : CarData.topSpeed;
            float target = Mathf.Clamp(carTopSpeed + 15f, 342f, RaceSpeedCeilingKph) * setupTopSpeedMultiplier;

            // DRS and ERS both need a ceiling above the normal ~350 clamp, otherwise
            // their bonus is silently absorbed since the unassisted target already
            // sits close to it. Real F1 DRS: better terminal speed on straights, not
            // an arcade boost, so the drag reduction (see ApplyForces) does most of
            // the work; this raises the ceiling that drag reduction is allowed to reach.
            float ceiling = RaceSpeedCeilingKph;
            if (DrsActive)
            {
                target += DrsTopSpeedBonusKph;
                ceiling = DrsSpeedCeilingKph;
            }

            if (activeCommand.ers && ErsBattery > 0.01f)
            {
                target += ErsTopSpeedBonusKph;
                ceiling = Mathf.Max(ceiling, RaceSpeedCeilingKph + ErsTopSpeedBonusKph);
            }

            return Mathf.Min(target, ceiling);
        }

        float BrakeResponse(float input)
        {
            input = Mathf.Clamp01(input);
            return Mathf.Clamp01(Mathf.Pow(input, 0.72f) * Mathf.Lerp(0.72f, 1.1f, input));
        }

        void ApplyLowFrictionPhysicsMaterial()
        {
            if (vehiclePhysicsMaterial == null)
            {
                vehiclePhysicsMaterial = new PhysicMaterial("Open wheel low-friction body");
                vehiclePhysicsMaterial.dynamicFriction = 0.02f;
                vehiclePhysicsMaterial.staticFriction = 0.02f;
                vehiclePhysicsMaterial.bounciness = 0f;
                vehiclePhysicsMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
                vehiclePhysicsMaterial.bounceCombine = PhysicMaterialCombine.Minimum;
            }

            Collider[] colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].sharedMaterial = vehiclePhysicsMaterial;
                }
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            ProcessDamageCollision(collision, false);
        }

        void OnCollisionStay(Collision collision)
        {
            ProcessDamageCollision(collision, true);
        }

        void ProcessDamageCollision(Collision collision, bool sustained)
        {
            if (!initialized || Damage == null || IsHeldOnGrid || IsHeldInPit || collision.contactCount == 0 || collision.collider == null)
            {
                return;
            }

            if (sustained && scrapeDamageCooldown > 0f)
            {
                return;
            }

            string classificationReason;
            DamageImpactType impactType = ClassifyDamageCollision(collision, out classificationReason);
            ContactPoint contact = collision.GetContact(0);
            string objectName = collision.collider.gameObject.name;
            if (impactType == DamageImpactType.None)
            {
                LastDamageDebug = "ignored " + objectName + " " + classificationReason;
                if (!sustained && IsSuspiciousIgnoredCollisionName(objectName))
                {
                    GameLog.Info("[Damage] ignored object=" + objectName + " reason=" + classificationReason);
                }

                return;
            }

            float impactSpeedKph = collision.relativeVelocity.magnitude * 3.6f;
            float normalSpeedKph = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal)) * 3.6f;
            if (sustained)
            {
                normalSpeedKph = Mathf.Max(normalSpeedKph, body.velocity.magnitude * 3.6f * 0.18f);
            }

            if (impactType == DamageImpactType.Car)
            {
                DampenCarContactResponse(normalSpeedKph, sustained);
            }
            else
            {
                DampenWallContactResponse(normalSpeedKph, contact.normal, sustained);
            }

            Vector3 localPoint = transform.InverseTransformPoint(contact.point);
            float delta = Damage.AddImpact(impactSpeedKph, normalSpeedKph, localPoint, impactType, sustained);
            LastDamageDebug = "object=" + objectName +
                              " type=" + impactType +
                              " impact=" + impactSpeedKph.ToString("0.0") +
                              " normal=" + normalSpeedKph.ToString("0.0") +
                              " delta=" + delta.ToString("0.0") +
                              " total=" + Damage.OverallPercent.ToString("0.0");
            if (delta > 0f)
            {
                scrapeDamageCooldown = sustained ? 0.45f : 0.08f;
                GameLog.Info("[Damage] applied object=" + objectName +
                          " reason=" + classificationReason +
                          " impactKph=" + impactSpeedKph.ToString("0.0") +
                          " normalKph=" + normalSpeedKph.ToString("0.0") +
                          " sustained=" + sustained +
                          " delta=" + delta.ToString("0.0") +
                          "% total=" + Damage.OverallPercent.ToString("0.0") + "%");
                SimpleAudioManager.PlayCollision(transform.position, impactSpeedKph / 3.6f, impactType);
            }
            else if (!sustained)
            {
                GameLog.Info("[Damage] no damage object=" + objectName +
                          " reason=below threshold type=" + impactType +
                          " impactKph=" + impactSpeedKph.ToString("0.0") +
                          " normalKph=" + normalSpeedKph.ToString("0.0") +
                          " total=" + Damage.OverallPercent.ToString("0.0") + "%");
            }
        }

        // Wheel-to-wheel collision tuning: PhysX's own default collision
        // response (still substantial even with zero bounciness/near-zero
        // friction - see GetCarBodyPhysicsMaterial in RaceManager) treated
        // ordinary racing contact exactly the same as slamming into a wall,
        // throwing the car sideways and spinning it far harder than a real
        // light rub or side-by-side touch would. This bleeds off most of the
        // collision-induced angular velocity and a portion of the lateral
        // velocity change right after a car-to-car hit, scaled by how hard
        // the contact actually was - a graze is barely felt, a genuinely
        // hard side-on hit still unsettles the car, just not into a
        // full-blown launch. Wall/barrier contact deliberately does NOT go
        // through this - a real impact there should still throw the car
        // around and matter physically.
        void DampenCarContactResponse(float normalSpeedKph, bool sustained)
        {
            if (body == null || body.isKinematic)
            {
                return;
            }

            float severity = Mathf.Clamp01((normalSpeedKph - 30f) / 90f);
            // Sustained (OnCollisionStay) contact is damped more gently per
            // tick - it already reapplies every physics step for as long as
            // the two cars stay touching, so a strong per-tick correction
            // would read as the car being magnetically glued straight rather
            // than naturally settling out of a prolonged rub.
            float dampFactor = (sustained ? Mathf.Lerp(0.45f, 0.12f, severity) : Mathf.Lerp(0.82f, 0.28f, severity));
            body.angularVelocity *= (1f - dampFactor);

            Vector3 lateral = Vector3.Dot(body.velocity, transform.right) * transform.right;
            body.velocity -= lateral * dampFactor * 0.6f;
        }

        // Wall/barrier/solid-object pinball-physics fix: this used to be
        // entirely undamped by design (a real wall hit was meant to "still
        // throw the car around and matter physically"), but PhysX's raw
        // contact response - even at zero bounciness/near-zero friction on
        // this body's own physics material - was still launching a car
        // sideways hard enough to rocket it clean across the track into the
        // opposite wall after one hit. Tuned as a genuinely separate curve
        // from DampenCarContactResponse above: lighter angular damping (a
        // wall hit should still spin the car, this only keeps it from
        // amplifying into an uncontrollable tumble) but a much stronger cut
        // to the rebound velocity specifically (the component of velocity
        // pointing back away from the wall along its contact normal) - that
        // rebound is what was actually firing the car back across the
        // circuit, not the spin. A hard, high-speed impact still visibly
        // unsettles and slows the car; it no longer bounces it like a pinball.
        void DampenWallContactResponse(float normalSpeedKph, Vector3 contactNormal, bool sustained)
        {
            if (body == null || body.isKinematic)
            {
                return;
            }

            float severity = Mathf.Clamp01((normalSpeedKph - 20f) / 110f);

            // Angular damping stays noticeably lighter than the car-to-car curve
            // (max ~0.5 vs ~0.82) so a genuine hard hit still spins the car
            // visibly - it just can't compound into an unrecoverable tumble.
            float angularDampFactor = sustained ? Mathf.Lerp(0.22f, 0.4f, severity) : Mathf.Lerp(0.2f, 0.5f, severity);
            body.angularVelocity *= (1f - angularDampFactor);

            // Rebound cut: only the velocity component pointing back away from
            // the wall (along its own contact normal) is reduced, not the car's
            // along-the-wall/forward momentum - a graze keeps rolling speed, a
            // head-on hit loses the violent kickback that used to fire it back
            // across the track.
            Vector3 normal = contactNormal.sqrMagnitude > 0.001f ? contactNormal.normalized : Vector3.up;
            float reboundSpeed = Vector3.Dot(body.velocity, normal);
            if (reboundSpeed > 0f)
            {
                float reboundCut = sustained ? Mathf.Lerp(0.3f, 0.55f, severity) : Mathf.Lerp(0.45f, 0.78f, severity);
                body.velocity -= normal * reboundSpeed * reboundCut;
            }
        }

        DamageImpactType ClassifyDamageCollision(Collision collision, out string reason)
        {
            Collider hitCollider = collision.collider;
            if (hitCollider == null)
            {
                reason = "no collider";
                return DamageImpactType.None;
            }

            if (Track != null && hitCollider == Track.roadCollider)
            {
                reason = "road collider";
                return DamageImpactType.None;
            }

            if (hitCollider.isTrigger)
            {
                reason = "trigger collider";
                return DamageImpactType.None;
            }

            VehicleController otherCar = hitCollider.GetComponentInParent<VehicleController>();
            if (otherCar != null && otherCar != this)
            {
                reason = "car-to-car contact";
                return DamageImpactType.Car;
            }

            string hitName = hitCollider.gameObject.name.ToLowerInvariant();
            if (IsVisualOrRoadCollisionName(hitName))
            {
                reason = "visual road marking or kerb";
                return DamageImpactType.None;
            }

            TrackSolidObstacle obstacle = hitCollider.GetComponentInParent<TrackSolidObstacle>();
            if (obstacle != null)
            {
                reason = obstacle.obstacleType;
                if (obstacle.obstacleType.Contains("wall"))
                {
                    return DamageImpactType.Wall;
                }

                if (obstacle.obstacleType.Contains("barrier"))
                {
                    return DamageImpactType.Barrier;
                }

                return DamageImpactType.SolidObject;
            }

            if (hitCollider.GetComponentInParent<TrackManager>() != null)
            {
                reason = "non-obstacle track object";
                return DamageImpactType.None;
            }

            reason = "unclassified solid object";
            return DamageImpactType.SolidObject;
        }

        bool IsVisualOrRoadCollisionName(string hitName)
        {
            return hitName.Contains("road") ||
                   hitName.Contains("paint") ||
                   hitName.Contains("grid") ||
                   hitName.Contains("line") ||
                   hitName.Contains("start") ||
                   hitName.Contains("finish") ||
                   hitName.Contains("sector") ||
                   hitName.Contains("kerb") ||
                   hitName.Contains("rubber") ||
                   hitName.Contains("drs") ||
                   hitName.Contains("racing");
        }

        bool IsSuspiciousIgnoredCollisionName(string objectName)
        {
            string lowered = objectName.ToLowerInvariant();
            return lowered.Contains("road") || lowered.Contains("paint") || lowered.Contains("grid") || lowered.Contains("line") || lowered.Contains("kerb");
        }
    }
}
