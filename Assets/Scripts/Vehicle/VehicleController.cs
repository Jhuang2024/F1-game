using F1Game.Race.Physics;
using F1Game.Race.Rules;
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
        // Player pit-entry buff (per request - "up my speed to 100 kph in the
        // pit entry lane"): the PLAYER's entry limiter caps at 100 while the AI
        // keep the 80 kph limit, a deliberate player advantage through pit
        // entry. Paired with the player-only rail entry pace in
        // RaceManager.Pit (UpdatePitRail) and the 100 kph pit-entry assist
        // target so the whole player entry sequence runs at the same speed.
        const float AiPitEntryLimiterCapKph = 80f;
        // Round 2 (per request): raised again, 100 -> 150.
        // Round 3 (per request): eased back, 150 -> 125.
        // Round 4 (per request): eased back again, 125 -> 110.
        // Round 5 (per request): 110 -> 105.
        const float PlayerPitEntryLimiterCapKph = 105f;
        float PitEntryLimiterCapKph
        {
            get { return IsPlayerControlled ? PlayerPitEntryLimiterCapKph : AiPitEntryLimiterCapKph; }
        }
        const float PitExitLimiterCapKph = 108f;

        public TyreState Tyres { get; private set; }
        public DamageState Damage { get; private set; }
        public float CurrentSpeedKph { get; private set; }
        public int CurrentGear { get; private set; }
        public float ErsBattery { get; private set; }
        public bool ErsDeploying { get; private set; }
        public bool ErsHarvesting { get; private set; }
        public bool DrsActive { get; private set; }
        // DRS speed boost, live only while the wing is genuinely open (DrsActive)
        // and the car is above DrsBoostThresholdKph. DRS fix: this used to be a
        // 15-second timer armed by a single activation that kept granting the
        // full +kph bonus and its push force after the wing closed - through the
        // braking zone, the following corners, and far beyond the DRS zone
        // itself (one tap at the end of a zone bought a boosted next sector).
        // The boost now lives and dies with the wing, like real DRS.
        public bool DrsBoostActive { get; private set; }
        // One-shot flag for the [LaunchBoost] diagnostic log below - logs only the
        // first frame the AI launch boost lands on this car per race session.
        bool launchBoostLoggedThisRace;
        // Throttle for the [ErsDrain] diagnostic below - printed at most once
        // every 0.5s while deploying, so a held Shift key doesn't spam the
        // console for the whole DRS zone.
        float ersDrainLogTimer;
        // Self-armed race-start launch window (see ArmRaceLaunchBoost): RaceManager
        // arms this directly at lights-out, so the launch boost no longer depends
        // on ANY part of the AI command pipeline (window flags, throttle values,
        // traffic logic) having behaved - the vehicle itself knows the race just
        // started and boosts. -1 = never armed.
        float raceLaunchBoostUntil = -1f;
        // One-shot "the boost never fired" alarm - if the window was armed and ran
        // out without the boost ever landing, log WHY (which gate was closed).
        bool launchFailureLogged;
        // Slipstream: automatic, physics-based tow from running in another car's
        // wake on a straight - distinct from DRS (button/AI-commanded, gated by
        // race eligibility rules, much bigger effect). RaceManager.UpdateSlipstreamEffects
        // computes strength for every participant every frame and calls
        // SetSlipstream; smoothed here so a car drifting in/out of the wake band
        // doesn't flicker the bonus on and off frame to frame.
        public bool SlipstreamActive { get { return slipstreamStrength > 0.05f; } }
        public float SlipstreamStrength { get { return slipstreamStrength; } }
        public float SlipstreamBonusKph { get { return slipstreamStrength * SlipstreamTopSpeedBonusKph; } }
        public string SlipstreamSourceCode { get { return slipstreamSourceCode; } }
        float slipstreamStrength;
        float targetSlipstreamStrength;
        string slipstreamSourceCode = "";
        // Slipstream effect speed doubled (was 10f) - both the top-speed ceiling
        // bonus here and the additive acceleration push below (slipstreamBoost)
        // are doubled together so the tow's felt speed gain doubles consistently,
        // not just the ceiling it's allowed to reach.
        // Max slipstream effect decreased to 15kph (was 20f) - slipstreamBoost
        // below scaled down proportionally (0.75x) to keep the same relationship.
        const float SlipstreamTopSpeedBonusKph = 15f;

        public void SetSlipstream(float strength01, string sourceCode)
        {
            targetSlipstreamStrength = Mathf.Clamp01(strength01);
            slipstreamSourceCode = targetSlipstreamStrength > 0.05f ? sourceCode : "";
        }

        // Dirty-air (turbulent-wake) cornering grip loss: the opposite of the
        // straight-line tow, wired from AeroModel.DirtyAirLoss behind a default-off
        // switch (f1game_dirty_air). RaceManager feeds the gap to the car ahead in
        // car-lengths; the penalty only applies while cornering (see ApplyForces),
        // so a following car keeps its straight-line tow but loses some front-end
        // in the corners, as in real racing. Default off preserves the tuned feel;
        // the on-path magnitude is scaled conservatively but remains PENDING
        // in-editor validation. Sentinel: a large gap means clear air.
        const string DirtyAirEnabledKey = "f1game_dirty_air";
        const float DirtyAirMaxCarLengths = 4f;
        const float DirtyAirClearGap = 999f;
        // Downforce loss (AeroModel) is only part of total grip; this share keeps
        // the max in-corner grip loss modest (~7% right behind) rather than the
        // raw 35% downforce figure.
        const float DirtyAirGripShare = 0.2f;
        const float DirtyAirSteerThreshold = 0.15f;

        static int dirtyAirEnabledCache = -1;
        static bool DirtyAirEnabled
        {
            get
            {
                if (dirtyAirEnabledCache < 0)
                {
                    dirtyAirEnabledCache = PlayerPrefs.GetInt(DirtyAirEnabledKey, 0);
                }

                return dirtyAirEnabledCache == 1;
            }
        }

        float dirtyAirGapCarLengths = DirtyAirClearGap;

        /// <summary>Gap (car-lengths) to the car directly ahead; large = clear air.</summary>
        public void SetDirtyAirGap(float carLengths)
        {
            dirtyAirGapCarLengths = carLengths <= 0f ? 0f : carLengths;
        }

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
        // Fuel system pass: distance-scaled fuel replacing the old flat 35kg-for-
        // every-session load. startFuelKg/fuelPerLapEstimateKg are set once by
        // RaceManager right after Initialize (see SetStartFuel) from
        // RaceManager.ComputeRaceStartFuelKg/ComputeQualifyingFuelKg/
        // ComputeTimeTrialFuelKg/ComputePracticeFuelKg - VehicleController itself
        // stays session-agnostic and just burns/reports against whatever it's told.
        public float StartFuelKg { get { return startFuelKg; } }
        public float FuelPerLapEstimateKg { get { return fuelPerLapEstimateKg; } }
        public float ProjectedFuelDeltaKg { get; private set; }
        public float ProjectedFuelDeltaLaps { get; private set; }
        public float LiftAndCoastSavedKg { get; private set; }
        public int LiftAndCoastEvents { get; private set; }
        public bool FuelStarved { get { return fuelStarved; } }
        public float FuelStarvedTimer { get { return fuelStarvedTimer; } }
        public float UndersteerAmount { get; private set; }
        public float OversteerAmount { get; private set; }
        /// <summary>Last applied steering command (-1..1); read by telemetry capture.</summary>
        public float LastSteerInput { get; private set; }
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
        // Fuel system pass: 35f is only the pre-SetStartFuel fallback (editor
        // preview, or a car that somehow never gets spawned through RaceManager) -
        // every real session sets startFuelKg/fuelKg explicitly via SetStartFuel.
        float fuelKg = 35f;
        float startFuelKg = 35f;
        float fuelPerLapEstimateKg;
        bool fuelStarved;
        float fuelStarvedTimer;
        bool liftAndCoastActive;
        float pitCooldown;
        Vector3 gridHoldPosition;
        Quaternion gridHoldRotation;
        float scrapeDamageCooldown;
        bool damageEnabled = true;
        float stuckPowerDebugTimer;
        float smoothedThrottle;
        float smoothedBrake;
        bool lowBatteryForcedHarvest;
        float topSpeedLogTimer;
        // The mode the player actually had selected when the low-battery
        // failsafe forced Harvest, restored once the battery recovers (the
        // failsafe used to hard-reset to Balanced instead).
        int ersModeBeforeForcedHarvest = (int)ErsStrategyMode.Balanced;
        // Velocity at the top of the current physics tick (see ApplyForces /
        // SoftenCarContact) - the baseline for clamping car-car impulses.
        Vector3 lastPrePhysicsVelocity;
        // The driver's raw brake pedal this tick, pre-assist (see
        // GetAssistedCommand) - the DRS wing's brake sensor.
        float lastDriverBrakeInput;
        // Hysteresis state for the ERS auto-deploy gates in GetAssistedCommand:
        // true while an auto deploy is running, which lowers the keep-alive
        // thresholds so a throttle/speed value hovering at the start threshold
        // can't flap the deploy on and off every frame.
        bool autoDeployLatched;
        // ERS empty-recharge-delay: once the battery is fully drained, non-
        // braking recharge (the off-throttle coast rate and the passive
        // trickle) is suppressed for ErsEmptyRechargeDelaySeconds - braking-
        // zone recharge is completely unaffected. ersWasEmpty only arms the
        // timer on the frame the battery first reads empty, so the cooldown
        // counts down normally afterward instead of being perpetually
        // re-armed by its own lock keeping the battery at zero.
        bool ersWasEmpty;
        float ersEmptyCooldownTimer;
        const float ErsEmptyRechargeDelaySeconds = 5f;
        static PhysicMaterial vehiclePhysicsMaterial;

        // Garage setup trade-offs (player car only); all neutral at 1.
        float setupTopSpeedMultiplier = 1f;
        float setupGripMultiplier = 1f;
        float setupBrakeMultiplier = 1f;
        float setupKerbGrip = 0.92f;
        float setupWearBias = 1f;

        const int GearCount = 8;
        const float RaceSpeedCeilingKph = 350f;
        // Ceiling for the CAR STAT's own contribution to top speed (see the
        // stat-honesty fix in CalculateTargetTopSpeedKph) - generous enough
        // that a heavily upgraded career car (~415 stat) keeps its full edge,
        // while still bounding data errors.
        const float StatTopSpeedCeilingKph = 435f;
        // Player-only straightline speed buff - never applies to AI (see
        // CalculateTargetTopSpeedKph, gated on IsPlayerControlled). Raised
        // from 4 to 9 (an additional +5), then lowered by 2 to 7, then
        // lowered by a further 2 to 5. This constant alone only ever raises
        // the governor's target/ceiling, not an actual push - see
        // playerTopSpeedBoost in ApplyForces for the dedicated additive force
        // that was missing, without which a car whose real drag-limited
        // equilibrium speed already sat at or below the ceiling never
        // actually reached (or felt) this bonus at all.
        // Removed (per request): after the difficulty rework stripped every
        // AI-side artificial speed term, this was the last asymmetric speed
        // bonus in the game - a hidden player-only +5. Zeroed so player and
        // AI machinery are perfectly symmetric; the matching
        // playerTopSpeedBoost force in ApplyForces is gone with it.
        const float PlayerTopSpeedBonusKph = 0f;
        // AI-only straightline speed buff - never applies to the player (see
        // CalculateTargetTopSpeedKph, gated on !IsPlayerControlled). Same
        // ceiling-plus-dedicated-force pairing as PlayerTopSpeedBonusKph
        // above (see aiTopSpeedBoost in ApplyForces) - a ceiling bump alone
        // is aspirational unless something actually pushes the car up to it.
        // Fairness: held equal to PlayerTopSpeedBonusKph. This briefly sat at
        // 8 vs the player's 5, which handed the AI a hidden +3 kph straight-
        // line edge in identical machinery at every difficulty - contradicting
        // the project's own rule that AI straight-line pace never silently
        // exceeds the player's in the same car.
        // Difficulty round 5 (per request - deliberate, explicit advantage):
        // no longer a hidden constant. RaceManager.Grid sets these per
        // difficulty at spawn via SetAiPerformanceAssist - Easy keeps player
        // parity, the top tiers run genuinely faster/grippier machinery as an
        // intentional difficulty mechanism.
        float aiTopSpeedBonusKph = 5f;
        float aiGripAssist = 1f;
        // Pit-entry parity: set true for the tick whenever an AI car is
        // physically approaching the pits (pit requested + inside the pit
        // approach window). While true, the AI's difficulty grip/turn and
        // top-speed assists are neutralised, so the AI corners the pit-entry
        // turn-in with exactly the player's grip instead of carrying its
        // assisted cornering speed through the peel-off - the residual reason
        // the AI still cleared pit entry faster than the player even at a
        // matched approach speed target.
        bool aiPitApproachNeutralize;
        // AI rotation authority (per request - "the AI need better rotation
        // when turning"): a dedicated AI-only yaw-rate multiplier applied to
        // turnRate but NOT to grip, so the AI turns in crisper / rotates into
        // the corner rather than washing out into understeer, without handing
        // it extra grip-limited corner speed (that lever, aiGripAssist, is
        // being trimmed at the same time). Neutralised in the pit approach
        // alongside the grip assist so pit-entry parity holds.
        const float AiCorneringRotationBoost = 1.1f;

        /// <summary>Difficulty-scaled AI performance advantage (AI cars only):
        /// straight-line top-speed bonus in kph and a grip/turn multiplier.</summary>
        public void SetAiPerformanceAssist(float topSpeedBonusKph, float gripAssist)
        {
            aiTopSpeedBonusKph = Mathf.Clamp(topSpeedBonusKph, 0f, 30f);
            // Lower bound opened 1.0 -> 0.8 (skill grip-utilization pass, see
            // RaceManager.Grid): values BELOW 1 express a driver using less of
            // the car's real grip - never more than the player's identical
            // machinery (the 1.25 unfair-advantage ceiling is unused; every
            // current caller passes <= 1).
            aiGripAssist = Mathf.Clamp(gripAssist, 0.75f, 1.25f);
        }
        // Flat DRS speed boost: +DrsBoostAmountKph, uncapped by the normal
        // top-speed ceiling, while the wing is open above DrsBoostThresholdKph -
        // so it never does anything at pit-lane/low-corner speed and ends the
        // instant the wing closes (brake, zone exit, availability lost).
        const float DrsBoostThresholdKph = 150f;
        const float DrsBoostAmountKph = 30f;
        // Dedicated additive force (same reasoning as playerTopSpeedBoost/
        // aiTopSpeedBoost/ersBoost above) so the raised target is actually
        // reached quickly rather than being aspirational against drag.
        const float DrsBoostForceAccel = 45f;
        // ERS buff: raised from 20, then 26 - with the stronger deploy force
        // below the car can now actually accelerate up to a ceiling this much
        // higher within a normal straight, instead of the old ceiling being
        // mostly aspirational because the underlying push was too weak to
        // reach it.
        const float ErsTopSpeedBonusKph = 30f;
        static readonly float[] AutoShiftUpKph = { 0f, 62f, 102f, 142f, 186f, 232f, 282f, 322f };

        // Read-only view of the authoritative auto-shift speed schedule (index g = the
        // speed at which gear g upshifts). Exposed for the presentation-only audio RPM
        // reconstruction (F1Game.Race.Physics.AudioRpmModel); the array is never mutated
        // through this and gear/physics behaviour is unaffected.
        public static System.Collections.Generic.IReadOnlyList<float> AutoShiftUpSchedule => AutoShiftUpKph;
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
            // Collision-violence fix (per report - contacts threw cars across
            // the track): the launch mechanism is Unity's depenetration solve -
            // two overlapping car boxes get popped apart at up to the default
            // depenetration velocity, which reads as an explosion rather than a
            // nudge. Capping it (plus the spin cap below) turns overlap
            // resolution into a firm shove; the car-car impulse clamp in
            // ProcessDamageCollision handles the direct-hit case.
            body.maxDepenetrationVelocity = 2.5f;
            body.maxAngularVelocity = 3.5f;
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

        // Fuel system pass: called once by RaceManager right after Initialize, with
        // a session-appropriate load already computed (race: distance-scaled from
        // lap count + the player's/AI's fuel-load choice; qualifying/time
        // trial/practice: their own low/fixed loads - see RaceManager's
        // ComputeXFuelKg helpers). Re-applies body.mass immediately since
        // Initialize above already set it from the old flat 35kg field default.
        public void SetStartFuel(float startingFuelKg, float perLapEstimateKg)
        {
            startFuelKg = Mathf.Max(0f, startingFuelKg);
            fuelKg = startFuelKg;
            fuelPerLapEstimateKg = Mathf.Max(0f, perLapEstimateKg);
            fuelStarved = false;
            fuelStarvedTimer = 0f;
            LiftAndCoastSavedKg = 0f;
            LiftAndCoastEvents = 0;
            if (body != null)
            {
                body.mass = 760f + fuelKg;
            }
        }

        // Fuel system pass: Time Trial is a pure lap-time comparison tool - a
        // depleting/varying fuel load across repeated laps would distort record
        // comparisons for no gameplay benefit, so RaceManager sets this true right
        // after SetStartFuel for a Time Trial session (never for Race/Qualifying/
        // Practice). Mass/power stay fixed at the Time Trial fuel load the whole
        // session; fuel simply never burns down.
        bool fuelBurnDisabled;

        public void SetFuelBurnDisabled(bool disabled)
        {
            fuelBurnDisabled = disabled;
        }

        // Time trial: bring the tyres straight up to their optimal window so the
        // single flying lap has full grip from the first corner.
        public void PreheatTyres()
        {
            if (Tyres != null)
            {
                Tyres.WarmToOptimal();
            }
        }

        // Time trial: collision/contact damage is turned off entirely so a lap
        // attempt is never ended by a scrape. Defaults on for races/qualifying.
        public void SetDamageEnabled(bool enabled)
        {
            damageEnabled = enabled;
        }

        // Fuel system pass: RaceManager calls this once per tick (player and every
        // AI car) with how much of the race is actually left, so the HUD/AI can
        // reason about "will the current fuel load actually make it" rather than
        // just a raw kg number. remainingLaps includes the fractional part of the
        // lap in progress (e.g. 2.35 laps left), not just whole completed laps.
        public void UpdateFuelProjection(float remainingLaps)
        {
            if (fuelPerLapEstimateKg <= 0.001f)
            {
                ProjectedFuelDeltaKg = 0f;
                ProjectedFuelDeltaLaps = 0f;
                return;
            }

            // Projection maths owned by the extracted FuelStrategy rulebook -
            // behavior-identical, now stated and tested in one place.
            ProjectedFuelDeltaKg = FuelStrategy.DeltaKg(fuelKg, remainingLaps, fuelPerLapEstimateKg);
            ProjectedFuelDeltaLaps = FuelStrategy.DeltaLaps(fuelKg, remainingLaps, fuelPerLapEstimateKg);
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

        // Dynamic track evolution: ticked every frame by RaceManager.UpdateTrackEvolution
        // from the session-wide TrackRuntime.RubberLevel, same live-update pattern as
        // SetWeather above.
        public float TrackGripMultiplier { get; private set; } = 1f;

        public void SetTrackGripMultiplier(float value)
        {
            TrackGripMultiplier = value;
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

        // AI ERS strategy dial (see the mode block in ApplyForces): the same
        // Harvest/Balanced/Attack multipliers the player's settings dial maps
        // to, driven for AI by RaceManager's ERS brain. Defaults to neutral.
        float aiHarvestModeMultiplier = 1f;
        float aiDeployModeMultiplier = 1f;

        // [PitDiag] wall-contact telemetry rate limit (see ProcessDamageCollision).
        float lastPitWallHitLogTime;

        public void SetAiErsMode(float harvestMultiplier, float deployMultiplier)
        {
            aiHarvestModeMultiplier = harvestMultiplier;
            aiDeployModeMultiplier = deployMultiplier;
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
        //
        // Pit-exit handoff fix: releaseSpeedMps used to be silently ignored - the
        // handoff always injected a fixed ~14 m/s (~50 km/h) regardless of how
        // fast the car was actually being guided just before this call, halving a
        // pit-exit merge's real ~100 km/h speed right in front of following pit
        // traffic. Callers that know the real exit speed (RaceManager's
        // ExitMerge completion) now pass it through; the 14 m/s default is kept
        // only for callers with no meaningful guided speed of their own (e.g. a
        // red-flag pit-sequence cancellation).
        public void SetPitGuidance(bool guided, float releaseSpeedMps = 14f)
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
                    body.velocity = transform.forward * Mathf.Max(0f, releaseSpeedMps);
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

        // Collision-aware pit-exit fix: collisionAware is only ever passed true
        // for the ExitMerge phase (see RaceManager.UpdatePitExitMerge). Entry/
        // Service/Release stay fully non-colliding - queued cars in the pit lane
        // must never physics-fight, spin each other, or take damage while boxed
        // in - but a car merging back onto the live racing surface needs to
        // actually register contact with other traffic and solid geometry
        // instead of silently ghosting through the pit divider or the outside
        // barrier. When collisionAware is set, detectCollisions is re-enabled
        // and the kinematic body is advanced with Rigidbody.MovePosition/
        // MoveRotation (the physics-engine-aware way to move a kinematic body)
        // rather than a raw position teleport, so those contacts are actually
        // generated. transform.position/rotation are still also written
        // directly in the same call so every caller reading the participant's
        // transform later in the same tick (progress projection, completion
        // checks) sees the authoritative, up-to-date pose immediately rather
        // than waiting for the next physics step.
        public float GuideToPitPose(Vector3 position, Quaternion rotation, float moveSpeed, float rotateSpeed, bool collisionAware = false)
        {
            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            Vector3 previousPosition = transform.position;
            Vector3 nextPosition = Vector3.MoveTowards(transform.position, position, moveSpeed * dt);
            Quaternion nextRotation = Quaternion.RotateTowards(transform.rotation, rotation, rotateSpeed * dt);
            transform.position = nextPosition;
            transform.rotation = nextRotation;
            if (body != null)
            {
                if (collisionAware && body.isKinematic)
                {
                    body.detectCollisions = true;
                    body.MovePosition(nextPosition);
                    body.MoveRotation(nextRotation);
                }
                else
                {
                    body.position = nextPosition;
                    body.rotation = nextRotation;
                }

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

        // Arms the AI launch boost for the next durationSeconds - called by
        // RaceManager at the exact lights-out frame. The vehicle then applies its
        // launch push itself (see ApplyForces), fully independent of the AI
        // command pipeline; the player's car ignores this entirely.
        public void ArmRaceLaunchBoost(float durationSeconds)
        {
            if (IsPlayerControlled)
            {
                return;
            }

            raceLaunchBoostUntil = Time.time + Mathf.Max(0f, durationSeconds);
            launchBoostLoggedThisRace = false;
            launchFailureLogged = false;
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
            aiPitApproachNeutralize = !IsPlayerControlled && PitRequested && Track != null &&
                                      Track.IsInPitApproach(progress.normalized);

            VehicleCommand assisted = GetAssistedCommand(command, absoluteSpeedKph, progress);
            SmoothDriveCommand(ref assisted, absoluteSpeedKph, dt);
            EffectiveThrottle = assisted.throttle;
            EffectiveBrake = assisted.brake;
            float lateralSpeed = Mathf.Abs(Vector3.Dot(body.velocity, transform.right));
            float slipEnergy = Mathf.Clamp01(lateralSpeed / Mathf.Max(6f, body.velocity.magnitude * 0.32f)) * Mathf.InverseLerp(28f, 260f, absoluteSpeedKph);
            slipEnergy = Mathf.Clamp01(slipEnergy + Mathf.Abs(assisted.steer) * Mathf.InverseLerp(80f, 270f, absoluteSpeedKph) * 0.35f);
            UpdateGear(absoluteSpeedKph);
            float trackTemperatureC = Track != null ? Track.trackTemperatureC : F1Game.Race.Rules.TyreStrategyRules.StandardTrackTempC;
            Tyres.Tick(absoluteSpeedKph, assisted.brake, assisted.steer, assisted.throttle, slipEnergy * setupWearBias, Weather, CarData.tyreManagement, dt, trackTemperatureC);
            ApplyForces(assisted, absoluteSpeedKph, progress, dt);
            ApplySteering(assisted, absoluteSpeedKph, dt);
            StabilizeChassis(dt);

            UpdateFuel(assisted, absoluteSpeedKph, progress, dt);
            pitCooldown = Mathf.Max(0f, pitCooldown - dt);
            LogSuspiciousPowerLoss(absoluteSpeedKph, dt);
        }

        // Fuel system pass: burn is DISTANCE-based (fuel per metre of track,
        // derived from fuelPerLapEstimateKg / track length) rather than a flat
        // kg-per-second constant - a flat per-second rate has no idea how long a
        // lap actually takes, so the same burn rate would over- or under-shoot the
        // intended per-lap consumption on a short vs. long circuit. Distance-based
        // burn is self-calibrating: drive one full lap at the "reference" throttle
        // multiplier below and total consumption lands almost exactly on
        // fuelPerLapEstimateKg, on any track, with no per-circuit tuning needed.
        // throttleBurnMultiplier spans 0.5x (fully lifted) to 1.6x (full throttle)
        // around that 1x reference, so a typical mixed-throttle lap (lots of full
        // throttle, some lift/braking) averages out close to the estimate while
        // still rewarding a genuinely light right foot.
        //
        // Old behaviour used to floor fuel at 4kg (Mathf.Max(4f, ...)) - cars could
        // never actually run out. Fuel can now reach true zero; see fuelStarved
        // below and ApplyForces' starvation power cut.
        void UpdateFuel(VehicleCommand assisted, float absoluteSpeedKph, TrackProgress progress, float dt)
        {
            if (fuelBurnDisabled)
            {
                return;
            }

            float distanceThisFrame = body.velocity.magnitude * dt;
            float trackLength = Track != null && Track.length > 1f ? Track.length : 4650f;
            float fuelPerMeter = fuelPerLapEstimateKg > 0f ? fuelPerLapEstimateKg / trackLength : 1.5f / 4650f;
            // Neutral point (1.0x, i.e. "matches fuelPerLapEstimateKg exactly") sits
            // at throttle=0.75 rather than the middle of the range - this game's own
            // AI cornering model already keeps cars near full throttle through most
            // corner types (HighSpeed/Medium/Slow buckets all target close to
            // straight-line pace), so a realistic full-lap throttle average here
            // runs meaningfully higher than a flat 50/50 duty cycle would suggest.
            float throttleBurnMultiplier = Mathf.Lerp(0.5f, 1.17f, Mathf.Clamp01(assisted.throttle));
            float burnKg = fuelPerMeter * distanceThisFrame * throttleBurnMultiplier;
            fuelKg = Mathf.Max(0f, fuelKg - burnKg);
            body.mass = 760f + fuelKg;

            fuelStarved = fuelKg <= 0f;
            fuelStarvedTimer = fuelStarved ? fuelStarvedTimer + dt : 0f;

            // Lift-and-coast: only counts as genuine fuel-saving when it happens
            // heading into a real braking/corner-approach zone (ApproachingBrakingZone
            // below) - lifting randomly on a straight for no reason isn't a driving
            // technique worth crediting, it's just slower. liftAndCoastActive is an
            // edge-detect flag so one sustained lift counts as one event, not one
            // event per physics tick.
            bool liftingHigh = absoluteSpeedKph > 90f && assisted.throttle < 0.15f && assisted.brake < 0.05f;
            bool approachingCorner = liftingHigh && ApproachingBrakingZone(progress);
            if (approachingCorner && !IsOffTrackSlowdown && !PitLimiterActive && !IsHeldInPit)
            {
                const float fullThrottleMultiplier = 1.17f;
                float savedThisFrame = fuelPerMeter * distanceThisFrame * Mathf.Max(0f, fullThrottleMultiplier - throttleBurnMultiplier);
                LiftAndCoastSavedKg += savedThisFrame;
                if (!liftAndCoastActive)
                {
                    LiftAndCoastEvents++;
                    liftAndCoastActive = true;
                }
            }
            else
            {
                liftAndCoastActive = false;
            }
        }

        // Cheap curvature lookahead (two chords, ~50m spread) just to answer "is
        // there a real corner coming up" for lift-and-coast crediting - deliberately
        // simpler than AiVehicleController's own corner-severity model since this
        // only needs a yes/no, not a speed target.
        bool ApproachingBrakingZone(TrackProgress progress)
        {
            if (Track == null)
            {
                return false;
            }

            Vector3 pointNear, forwardNear, rightNear, pointFar, forwardFar, rightFar;
            Track.SampleAtDistance(progress.distance + 20f, out pointNear, out forwardNear, out rightNear);
            Track.SampleAtDistance(progress.distance + 75f, out pointFar, out forwardFar, out rightFar);
            return Vector3.Angle(forwardNear, forwardFar) > 12f;
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
            // The driver's ACTUAL brake pedal this tick, before any assist trims
            // - the DRS wing gate reads this (see ApplyForces): an auto-brake
            // speed trim is not the driver braking.
            lastDriverBrakeInput = raw.brake;

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
                // THE 345-kph mystery, solved (per the [TopSpeed] log - target
                // 380, car stuck at ~350): this curve used a FLAT 335 base, so
                // even on a dead straight with zero corner severity the assist
                // considered anything above 335 kph "too fast", injected brake
                // ((v-335)/115) and cut throttle to 0.55 - pinning every car at
                // ~345-355 regardless of its stat. It was tuned when all cars
                // topped out ~350 and never visibly bound; the honest car-stat
                // targets made it the binding cap, invisible in the target
                // decomposition because it corrupts the pedal INPUTS. The
                // straight-line end of the curve now sits safely above this
                // car's own top-speed target, so the assist only ever slows the
                // car for genuine corners.
                float desiredSpeed = Mathf.Lerp(TargetTopSpeedKph + 40f, 108f, brakeSeverity * brakeSeverity);
                if (speedKph > desiredSpeed)
                {
                    assisted.brake = Mathf.Max(assisted.brake, Mathf.Clamp01((speedKph - desiredSpeed) / 115f));
                    assisted.throttle = Mathf.Min(assisted.throttle, Mathf.Lerp(0.55f, 0.18f, brakeSeverity));
                }
            }

            // Random-mode-change fix (per report - "ERS randomly won't harvest
            // and auto-deploys, don't know if that's the strategy"): this
            // failsafe used to hard-reset the mode to BALANCED on recovery
            // regardless of what the player had actually selected - combined
            // with Balanced's (now removed) own auto-deploy it produced a
            // visible deploy/harvest/deploy cycle around the 24-28% band that
            // the player never asked for. It now remembers and restores the
            // player's own mode instead.
            if (ErsBattery < 0.02f)
            {
                if (!lowBatteryForcedHarvest)
                {
                    ersModeBeforeForcedHarvest = settings.ersMode;
                }

                lowBatteryForcedHarvest = true;
                settings.ersMode = (int)ErsStrategyMode.Harvest;
            }
            else if (lowBatteryForcedHarvest && ErsBattery > 0.28f)
            {
                lowBatteryForcedHarvest = false;
                settings.ersMode = ersModeBeforeForcedHarvest;
            }

            // ERS mode governs when the car deploys automatically, but holding the
            // manual override key must always be able to trigger a deploy while
            // there is meaningful charge left - the mode is a strategy default, not
            // a hard lock on the driver's own overtake button.
            // Deploy-flicker fix: these gates used to read assisted.throttle
            // with a single threshold - but the traction/auto-brake assists trim
            // assisted.throttle every frame, so it hovered around the threshold
            // and auto-deploy flapped on/off ("flashes between ready and
            // deploying"). Same diagnosis and same two fixes as the manual path
            // below (which was already keyed off RAW throttle for exactly this
            // reason): the driver's raw throttle decides intent, and hysteresis
            // (a lower keep-alive threshold once deploying, via
            // autoDeployLatched) means a value hovering at the start threshold
            // can never flap the deploy state.
            // Surprise-deploy fix (per report - "ERS randomly auto-deploys on
            // track"): BALANCED used to auto-deploy too (throttle > 0.88 and
            // speed > 130), which read as the car spending the battery by
            // itself with no input. Auto-deploy is now exclusively an ATTACK
            // mode behaviour - an explicit player choice - while Balanced and
            // Harvest only ever deploy on the manual override key.
            bool autoDeployRequested = false;
            if (settings.ersMode == (int)ErsStrategyMode.Attack && ErsBattery > 0.06f &&
                raw.throttle > (autoDeployLatched ? 0.38f : 0.55f))
            {
                autoDeployRequested = true;
            }

            autoDeployLatched = autoDeployRequested;

            // The manual overtake key always wins over the strategy mode's own
            // auto-deploy logic (including Harvest, which has no auto-deploy branch
            // above) as long as there is meaningful charge and throttle - a driver
            // holding Shift should never find ERS refusing to fire just because the
            // dial is set to Harvest, nor find it stuck on with no way to cut it.
            //
            // Battery-never-reaches-0% fix: this used to cut manual deploy off
            // at 3% charge remaining, well above true empty - since this is the
            // ONE place that decides whether a manual Shift-hold deploy is even
            // requested at all, the battery could never be driven down past that
            // floor no matter how long the button was held. Lowered to a true
            // "still has charge" floor instead of an arbitrary reserve, so
            // holding the deploy key can genuinely empty the battery to 0%
            // (ApplyForces's own ErsDeploying/boost gates below are the same
            // true-empty floor, so nothing downstream re-imposes a hidden one).
            // ERS deploy gate fix: this used assisted.throttle, which the
            // traction-control and auto-brake assists above can trim right
            // down - so a driver holding the deploy key with the throttle
            // pinned could still have ERS silently refuse to fire (or drain)
            // whenever an assist momentarily cut the assisted throttle, e.g.
            // easing toward the end of a DRS straight. Keyed off the driver's
            // RAW throttle instead, so holding the deploy key with real
            // throttle applied always deploys AND drains, regardless of what
            // the assists do to the smoothed value.
            bool manualDeployRequested = raw.ers && ErsBattery > 0f && raw.throttle > 0.05f;
            assisted.ers = autoDeployRequested || manualDeployRequested;

            return assisted;
        }

        void SmoothDriveCommand(ref VehicleCommand assisted, float speedKph, float dt)
        {
            float throttleRise = Mathf.Lerp(2.35f, 5.4f, Mathf.InverseLerp(80f, 240f, speedKph));
            // AI launch fix: the low-speed rise rate above (2.35/s, ~0.43s to
            // full throttle) exists to make normal corner-exit throttle feel
            // human, but off a standing start it lops nearly half a second off
            // the AI's launch on top of its reaction delay - while the player's
            // instant reaction makes their own smoothing invisible. While the
            // AI's launch boost window is armed, let the smoothed throttle rise
            // near-instantly; everything after the launch window smooths normally.
            if (assisted.launchBoost > 0.01f)
            {
                throttleRise = Mathf.Max(throttleRise, 14f);
            }
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
            // Reference velocity for SoftenCarContact's post-solve impulse
            // clamp: captured at the top of the physics tick, before this
            // tick's drive/grip forces and any collision impulses land.
            lastPrePhysicsVelocity = body.velocity;
            LastSteerInput = activeCommand.steer;
            float speedMps = body.velocity.magnitude;
            float forwardSpeed = Vector3.Dot(body.velocity, transform.forward);

            // Slipstream: smoothed toward whatever RaceManager.UpdateSlipstreamEffects
            // last set this frame/last frame, so a car drifting in/out of the wake
            // band (following distance/lateral offset shifting slightly lap to lap)
            // doesn't flicker the bonus on and off.
            slipstreamStrength = Mathf.MoveTowards(slipstreamStrength, targetSlipstreamStrength, dt * 4f);

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
            //
            // Flicker fix round 4 - THE root cause, finally: every prior round
            // tuned thresholds against activeCommand.brake, the ASSISTED value
            // - but the auto-brake assist legitimately trims 0.2-0.5 through
            // the back half of every DRS straight (it is already braking for
            // the corner at the end of the zone), so with assists on the wing
            // kept refusing to open regardless of where the threshold sat.
            // Real DRS keys off the driver's brake PEDAL, and so does this
            // now: lastDriverBrakeInput is the raw pre-assist pedal (for AI,
            // its commanded brake). Assist trims can neither block an open nor
            // slam the wing shut; the driver touching the brakes still closes
            // it instantly, with hysteresis so an analog pedal resting on the
            // threshold cannot flap.
            bool wingRequested = activeCommand.drs && absoluteSpeedKph > 90f;
            DrsActive = wingRequested && lastDriverBrakeInput < (DrsActive ? 0.3f : 0.15f);

            // DRS boost is tied directly to the open wing (DrsActive already
            // folds in the button/latch, the >90kph gate and the brake auto-
            // close): no arming timer, no persistence after the wing closes.
            DrsBoostActive = DrsActive && absoluteSpeedKph > DrsBoostThresholdKph;

            TargetTopSpeedKph = CalculateTargetTopSpeedKph(activeCommand);
            if (PitLimiterActive)
            {
                DrsActive = false;
                DrsBoostActive = false;
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
                DrsBoostActive = false;
                TargetTopSpeedKph = Mathf.Min(TargetTopSpeedKph, RaceControlSpeedCapKph);
            }

            float speedCapFloorKph = PitLimiterActive ? (PitExitFastLimiter ? PitExitLimiterCapKph : PitEntryLimiterCapKph) : (raceControlCapActive ? RaceControlSpeedCapKph : 0f);
            bool speedCapEngaged = PitLimiterActive || raceControlCapActive;

            // Unconditional diagnostic (same pattern as [ErsDrain]/[PlayerCar])
            // for the persistent "360-stat car maxes at 345" report: prints the
            // full top-speed target decomposition while the player is actually
            // at speed, so the missing kph can be pinned to a specific term
            // (stat, setup, compound offset, an unexpected cap) from a real
            // play session instead of another round of static guessing.
            if (IsPlayerControlled && absoluteSpeedKph > 275f)
            {
                topSpeedLogTimer -= dt;
                if (topSpeedLogTimer <= 0f)
                {
                    topSpeedLogTimer = 3f;
                    Debug.Log("[TopSpeed] v=" + absoluteSpeedKph.ToString("0") +
                              " target=" + TargetTopSpeedKph.ToString("0") +
                              " carStat=" + (CarData != null ? CarData.topSpeed : -1) +
                              " setupMult=" + setupTopSpeedMultiplier.ToString("0.000") +
                              " compoundOffset=" + (Tyres != null ? Tyres.CompoundSpeedOffsetKph(Weather) : 0f).ToString("0") +
                              " drsBoost=" + DrsBoostActive + " ers=" + ErsDeploying +
                              " slip=" + slipstreamStrength.ToString("0.00") +
                              " pitLim=" + PitLimiterActive + " rcCap=" + (raceControlCapActive ? RaceControlSpeedCapKph.ToString("0") : "off") +
                              " punctured=" + (Tyres != null && Tyres.Punctured));
                }
            }

            float topSpeed = TargetTopSpeedKph / 3.6f;
            float tyreGrip = Tyres.GripMultiplier(Weather, TrackGripMultiplier);
            LastTyreGripMultiplier = tyreGrip;
            LastPowerMultiplier = Damage.PowerMultiplier;
            LastGearTorqueMultiplier = GearTorqueMultiplier(absoluteSpeedKph);
            float gripStat = Mathf.Lerp(0.9f, 1.28f, CarData.cornering / 100f);
            float grip = tyreGrip * gripStat * Damage.HandlingMultiplier * setupGripMultiplier * (IsPlayerControlled || aiPitApproachNeutralize ? 1f : aiGripAssist);
            // Dirty-air cornering penalty (switch-gated, default off): a close car
            // ahead robs front-end grip in the corners only, via AeroModel's decay
            // curve scaled to a modest share of total grip.
            if (DirtyAirEnabled && dirtyAirGapCarLengths < DirtyAirMaxCarLengths &&
                Mathf.Abs(LastSteerInput) > DirtyAirSteerThreshold)
            {
                grip *= 1f - AeroModel.DirtyAirLoss(dirtyAirGapCarLengths) * DirtyAirGripShare;
            }

            ActiveSlowdownReason = "NONE";
            if (Tyres.Punctured)
            {
                // Puncture (per request): the wear-grip curve already bottoms
                // out, and CalculateTargetTopSpeedKph halves the achievable
                // speed outright - this labels the state for the HUD.
                ActiveSlowdownReason = "PUNCTURE";
            }

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
            // Compound-contrast fix (per report - "grip difference between tyre
            // types is not nearly noticeable enough"): this used to be
            // (10 + grip*18) - the flat 10 base meant more than a third of the
            // car's lateral grip was identical no matter what tyre was fitted,
            // compressing a 28% compound spread to ~19% of felt force. Weight
            // shifted from the flat base into the grip-scaled term (5 + grip*23),
            // tuned so a medium at neutral conditions produces the SAME total
            // force as before - softs now gain and hards/worn/wrong-weather
            // tyres lose proportionally more, so the compound (and rain-state)
            // grip actually reads through to the handling.
            float lateralGripForce = (5f + grip * 23f) * Mathf.Lerp(1.12f, 0.78f, UndersteerAmount);
            body.AddForce(-lateralVelocity * lateralGripForce, ForceMode.Acceleration);

            float accelerationStat = Mathf.Lerp(11.4f, 20.4f, CarData.acceleration / 100f);
            float engineStat = Mathf.Lerp(0.96f, 1.24f, CarData.enginePower / 100f);
            // Fuel-weight fix: this was Lerp(1, 0.94, fuelKg/startFuelKg) - a
            // penalty RELATIVE to the car's own starting load, so an underfueled
            // and an overfueled car both started at the identical full penalty
            // and the only strategy difference was a few kg of rigidbody mass.
            // The "underfueled cars are faster" effect literally did not exist.
            // The penalty is now a function of the ABSOLUTE kilograms on board
            // (~0.2% drive force per kg, in line with the real-sport ~0.03s/lap
            // per kg), so a light-fueled car is genuinely, visibly quicker than
            // a heavy one all race, and every car naturally picks up pace as the
            // tank burns down. Applies to AI and player alike (shared path).
            float fuelPenalty = Mathf.Lerp(1f, 0.75f, Mathf.Clamp01(fuelKg / 120f));
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
            else if (!IsPlayerControlled)
            {
                // ERS strategy-dial parity (per report - "i dont know if the AI
                // ERS even gives the same speed boost it does for me"): the
                // boost FORMULA was always identical, but the player's mode
                // dial (Attack = 1.2x punch, Harvest = 1.9x recharge) was
                // player-only - AI were pinned to the neutral multipliers, so
                // an Attack-mode player genuinely out-boosted every AI on
                // every deploy. RaceManager's AI ERS brain now drives the same
                // dial (SetAiErsMode) with a competent policy; identical
                // multiplier values, identical formula.
                harvestModeMultiplier = aiHarvestModeMultiplier;
                deployModeMultiplier = aiDeployModeMultiplier;
            }

            float ersBoost = 0f;
            // Battery-never-reaches-0% fix: lowered from 0.01f (a 1% reserve
            // that quietly stopped every deploy path here a shade above true
            // empty, on top of GetAssistedCommand's own separate 3% reserve
            // above) to a true "still has any charge" floor, so a battery that
            // is genuinely being drained can reach 0% rather than asymptotically
            // stalling just above it.
            // Empty-battery boost fix (per report: with the battery reading 0%
            // a deploy still boosted): the HUD rounds the charge to a whole
            // percent, so a fraction of a percent displays as 0% while the
            // old "> 0" gate happily deployed it. Starting a deploy now needs
            // a genuinely usable 1.5% minimum; once running it drains to true
            // zero (hysteresis), so the boost always dies before - never
            // after - the gauge reads empty.
            // Deploy/harvest mutual exclusivity, round 2. Round 1 cancelled the
            // deploy whenever the ASSISTED brake read > 0.1 - but the auto-brake
            // assist raises that brake for much of a DRS straight at 330+ kph
            // (its corner-approach desired speed sits below DRS pace), so ERS
            // stopped deploying (and draining) at all under DRS - the exact
            // opposite regression. The deploy is intent-driven (raw key/throttle)
            // and now WINS: it never checks the brake, and the braking-harvest
            // branch below yields with a !ErsDeploying gate instead. States stay
            // strictly mutually exclusive - deploying drains, visibly, every
            // frame; harvesting only happens when not deploying.
            ErsDeploying = activeCommand.ers && ErsBattery > (ErsDeploying ? 0f : 0.015f);
            ErsHarvesting = false;
            if (ErsDeploying)
            {
                // Boost force and drain rate are PowertrainModel's numbers
                // (F1Game.Race.Physics) - the boost is tuned for a 15-20 km/h
                // felt gain on a typical straight, and the drain range carries
                // nine rounds of play-tuning history (see the model).
                ersBoost = PowertrainModel.ErsBoostForce(CarData.ersEfficiency / 100f, deployModeMultiplier);
                float ersBefore = ErsBattery;
                ErsBattery = Mathf.Clamp01(ErsBattery - dt * PowertrainModel.ErsDrainPerSecond(activeCommand.throttle));
                // Unconditional diagnostic (not GameLog.Info, which is silently
                // dropped unless verbose/F3 is on) for the reported "battery not
                // decreasing while deploying during DRS" - the drain math here
                // reads correctly on inspection (unconditional on DrsActive,
                // never re-added by any recharge branch this same frame), so
                // this proves or disproves it directly from a real play session
                // instead of another round of static-code guessing.
                if (IsPlayerControlled)
                {
                    ersDrainLogTimer -= dt;
                    if (ersDrainLogTimer <= 0f)
                    {
                        ersDrainLogTimer = 0.5f;
                        Debug.Log("[ErsDrain] battery " + (ersBefore * 100f).ToString("0.0") + "% -> " +
                                  (ErsBattery * 100f).ToString("0.0") + "% drsActive=" + DrsActive +
                                  " drsBoostActive=" + DrsBoostActive + " throttle=" + activeCommand.throttle.ToString("0.00") +
                                  " ersMode=" + (settings != null ? settings.ersMode : -1));
                    }
                }
            }

            // Empty-battery recharge delay: arm a 5-second no-non-braking-
            // recharge cooldown the instant the battery first reads fully
            // empty (0), rather than re-arming it every frame the battery
            // happens to still read 0 - the lock below is exactly what holds
            // it at 0, so re-arming every frame would make the cooldown
            // permanent instead of a real 5-second window. Braking-zone
            // recharge is checked and applied first, below, completely
            // unaffected by this cooldown either way.
            if (ErsBattery <= 0f)
            {
                if (!ersWasEmpty)
                {
                    ersEmptyCooldownTimer = ErsEmptyRechargeDelaySeconds;
                }

                ersWasEmpty = true;
            }
            else
            {
                ersWasEmpty = false;
            }

            if (ersEmptyCooldownTimer > 0f)
            {
                ersEmptyCooldownTimer = Mathf.Max(0f, ersEmptyCooldownTimer - dt);
            }

            bool ersEmptyCooldownActive = ersEmptyCooldownTimer > 0f;

            // Braking-zone recharge rebalance: cut to a third (was 0.42-0.63).
            // At the old rate a single ~2s hard stop banked 0.8-1.2 of the whole
            // battery - harvest outran the deploy drain ~5:1, the gauge pinned
            // at 100% and ERS management stopped being a decision at all. A hard
            // braking zone now banks a meaningful ~0.2-0.3 charge instead of a
            // full refill, so the battery genuinely cycles over a lap.
            // Round 2 (per request): braking-zone harvest cut a further 30%
            // (was 0.14-0.21), paired with the 10% deploy-drain raise in
            // PowertrainModel so the battery is a genuinely scarcer resource.
            // Round 3 (per request): braking-zone harvest raised 30% - the 1.3
            // factor is kept separate so the prior tuning stays traceable.
            // !ErsDeploying: harvest yields to an active deploy (see the
            // mutual-exclusivity note above ErsDeploying) so the battery can
            // never fill and drain in the same frame.
            if (!ErsDeploying && activeCommand.brake > 0.1f)
            {
                ErsBattery = Mathf.Clamp01(ErsBattery + dt * activeCommand.brake * activeCommand.brake * Mathf.Lerp(0.098f, 0.147f, CarData.ersEfficiency / 100f) * 1.3f * harvestModeMultiplier);
                ErsHarvesting = true;
            }
            // Non-braking recharge fix round 3: both the off-throttle coasting rate
            // and the passive trickle rate below are raised a further 60% on top of
            // their previous (already tripled) rate - only recovery that happens
            // outside a genuine braking zone (which keeps its own separately-tuned
            // rate above, untouched here), so the battery fills faster through the
            // rest of a lap without changing how fast a braking zone itself charges.
            // Round 4: raised a further 20% on top of that (was 0.13-0.288).
            // Round 5: cut back down 20% (was 0.156-0.346).
            // Round 6: cut a further 30% (was 0.1248-0.2768).
            // Round 7: cut a further 30% (was 0.0874-0.1938).
            // Round 8: cut a further 20% (was 0.0612-0.1357) and gated behind
            // ersEmptyCooldownActive - once the battery has hit empty it no
            // longer recharges outside a braking zone at all for the next 5
            // seconds (see ersEmptyCooldownTimer above).
            // Round 9: cut a further 10% on top of round 8 (was 0.0612-0.1357
            // * 0.8) - braking-zone recharge above is unaffected.
            // Round 10: cut a further 10% on top of round 9 (was 0.0612-0.1357
            // * 0.72) - braking-zone recharge above is unaffected.
            // Round 11: halve all harvesting outside braking zones (per request).
            // Round 12: cut a further 20% (per request), applied as an extra
            // multiplier so the prior 0.324 factor stays traceable -
            // braking-zone recharge above is unaffected.
            else if (!ErsDeploying && !ersEmptyCooldownActive && activeCommand.throttle < 0.08f && absoluteSpeedKph > 80f)
            {
                // Non-braking regen raised 30% (per request) - the 1.3 factor is
                // kept separate so the prior tuning stays traceable. Braking-zone
                // recharge above is unaffected.
                // Raised a further 30% (per request) - second 1.3 factor.
                // Cut 30% (per request) - the 0.7 factor; braking-zone
                // recharge above is unaffected.
                ErsBattery = Mathf.Clamp01(ErsBattery + dt * Mathf.Lerp(0.0612f, 0.1357f, CarData.ersEfficiency / 100f) * 0.324f * 0.8f * 1.3f * 1.3f * 0.7f * harvestModeMultiplier);
                ErsHarvesting = true;
            }
            else if (!ersEmptyCooldownActive && !ErsDeploying)
            {
                // ERS passive trickle: real ERS also recovers some energy outside of
                // hard braking or a full lift-off coast (residual MGU-H/engine-driven
                // recovery) - without this the battery only ever moved under braking
                // or an off-throttle coast, both comparatively rare moments in a lap
                // compared to normal throttle-on driving. Deliberately much slower
                // than either the braking or coasting rate above (roughly 1/20th of
                // the braking rate, well under half the coasting rate) so it reads as
                // a slow background trickle, not a third harvesting mode.
                // Round 4: raised a further 20% on top of the round-3 rate (was
                // 0.0408-0.0864), same non-braking-only regen buff as the coasting
                // rate above.
                // Round 5: cut back down 20% (was 0.049-0.104), same non-braking-only
                // regen cut as the coasting rate above.
                // Round 6: cut a further 30% (was 0.0392-0.0832), same non-braking-only
                // regen cut as the coasting rate above.
                // Round 7: cut a further 30% (was 0.0274-0.0582), same non-braking-only
                // regen cut as the coasting rate above.
                // Round 8: cut a further 20% (was 0.0192-0.0407) and gated behind
                // ersEmptyCooldownActive, same empty-battery delay as the coasting
                // rate above.
                // Round 9: cut a further 10% on top of round 8 (was 0.0192-0.0407
                // * 0.8), same non-braking-only regen cut as the coasting rate
                // above.
                // Round 10: cut a further 10% on top of round 9 (was 0.0192-0.0407
                // * 0.72), same non-braking-only regen cut as the coasting rate
                // above.
                // Round 11: cut a further 15% (per request), applied as an extra
                // multiplier so the whole prior 0.648 factor stays traceable.
                // Round 12: halve all harvesting outside braking zones (per request).
                // Round 13: cut a further 20% (per request), same non-braking-only
                // regen cut as the coasting rate above - braking-zone recharge is
                // unaffected.
                // Non-braking passive regen raised 30% (per request) - same 1.3
                // factor as the coasting rate above; braking recharge unaffected.
                // Raised a further 30% (per request) - second 1.3 factor.
                // Cut 30% (per request) - the 0.7 factor; braking-zone
                // recharge above is unaffected.
                ErsBattery = Mathf.Clamp01(ErsBattery + dt * Mathf.Lerp(0.0192f, 0.0407f, CarData.ersEfficiency / 100f) * 0.324f * 0.85f * 0.8f * 1.3f * 1.3f * 0.7f * harvestModeMultiplier);
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

            // Flat DRS boost force: DrsBoostActive (see ApplyForces' timer above)
            // already grants an uncapped +DrsBoostAmountKph to TargetTopSpeedKph
            // while it's active, but a raised ceiling alone is aspirational unless
            // something actually pushes the car toward it - same reasoning as
            // ERS/slipstream/top-speed-buff's own dedicated additive terms below.
            // No speed ramp here on purpose: the request is a flat, on/off boost
            // above DrsBoostThresholdKph, not a gradual build like the old model.
            float drsBoostForce = DrsBoostActive ? DrsBoostForceAccel : 0f;

            // Slipstream force: same reasoning as DRS above - a raised top-speed
            // ceiling alone may not be reachable before the straight ends, so this
            // gives the tow a small genuine additive push too. Deliberately
            // smaller/narrower than DRS (a real tow, not an open rear wing): does
            // almost nothing below ~130kph, builds through 130-255kph, and the
            // additive term itself is roughly a third of DRS's.
            // Slipstream effect speed doubled (was 8-15, matching the doubled
            // SlipstreamTopSpeedBonusKph above) - the tow's felt acceleration push
            // doubles alongside the higher ceiling it can now reach.
            // Max slipstream effect decreased to 15kph - scaled down 0.75x (was
            // 16-30) to match SlipstreamTopSpeedBonusKph's own reduction.
            float slipstreamSpeedRamp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(130f, 255f, forwardSpeedKph));
            float slipstreamBoost = slipstreamStrength > 0.05f
                ? Mathf.Lerp(12f, 22.5f, CarData.aeroEfficiency / 100f) * slipstreamStrength * slipstreamSpeedRamp
                : 0f;

            // Player straightline speed buff, round 2: PlayerTopSpeedBonusKph
            // above only ever raised CalculateTargetTopSpeedKph's target/ceiling
            // (the governor threshold speedLimiter cuts drive force against) -
            // it was never an actual push. That's the exact same diagnosis DRS,
            // ERS and slipstream all needed their own dedicated boost force
            // terms for (see the comments on drsBoostForce/ersBoost/slipstreamBoost):
            // if the car's real drag-limited equilibrium speed already sits at
            // or below the OLD ceiling, raising the ceiling further changes
            // nothing actually reached on a real straight, because nothing is
            // pushing the car any harder to get there. Gives the player bonus
            // its own genuine additive force, ramped in only at high speed (a
            // straightline tool, not extra corner-exit grunt) so the car
            // actually earns the extra top speed instead of it being
            // aspirational. Never applies to AI (gated on IsPlayerControlled,
            // same as PlayerTopSpeedBonusKph itself).
            // Removed (per request - symmetric machinery): the player-only
            // top-speed push is gone along with PlayerTopSpeedBonusKph. The
            // stat-scaled statTopSpeedBoost below applies to player and AI
            // identically, so straight-line pace now comes purely from the car.
            const float playerTopSpeedBoost = 0f;

            // AI straightline speed buff: same reasoning as playerTopSpeedBoost
            // above, mirrored for aiTopSpeedBonusKph - never applies to the
            // player. Held to exactly the player's force curve so the shared
            // ceiling is reachable for both without a hidden AI edge.
            float aiTopSpeedBoost = !IsPlayerControlled
                ? Mathf.Lerp(10f, 16f, Mathf.Clamp01(CarData.aeroEfficiency / 100f)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(120f, 260f, forwardSpeedKph)) * (aiTopSpeedBonusKph / 5f)
                : 0f;

            // Stat-honesty part 2 (per report - "my 360-stat car only reaches
            // ~355 even with a tow"): raising the top-speed TARGET for high
            // stats was aspirational - the drag equilibrium sits ~350-355 for
            // every car regardless of stat, the exact same "ceiling without a
            // push" failure DRS/ERS/the player bonus each needed their own
            // dedicated force for. This is that force for the topSpeed STAT:
            // zero at the old flat 335-and-below behaviour, ramping up with the
            // stat so a genuinely fast car is actually pushed to its number.
            // High-speed-only (a straight-line tool, not corner-exit grunt),
            // applies to player and AI alike - the stat is the stat.
            float statTopSpeedBoost = Mathf.InverseLerp(335f, 420f, CarData != null ? CarData.topSpeed : 337f) * 26f *
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(170f, 300f, forwardSpeedKph));

            // AI launch boost: a genuine additive forward force off a standing
            // start and off VSC/SC/yellow restarts (activeCommand.launchBoost, set
            // by AiVehicleController during its launch/recovery window). Doubling
            // the throttle INPUT ramp never helped because both cars reach full
            // throttle almost instantly and then share identical physics - so this
            // is the lever that actually makes AI accelerate as hard as (or harder
            // than) the player off the line. Strongest from a standstill and ramps
            // fully out by ~150 km/h so it's a launch/traction tool, never extra
            // straight-line top speed. Never applies to the player (the launchBoost
            // channel is AI-only) and is gated off any active speed cap so it can't
            // fight the pit/race-control limiter.
            // AI launch boost, fully self-contained: the window comes from
            // ArmRaceLaunchBoost (called by RaceManager at the lights-out frame)
            // OR from the AI's own command flag (VSC/SC restart recovery). It is
            // applied as its OWN force, never multiplied by throttle - every
            // previous attempt fed it through the throttle-multiplied drive term,
            // so any AI throttle trim silently strangled it (an offline replica of
            // this physics shows a throttle-capped car reproduces the reported
            // "AI crawl off the line" exactly).
            bool launchWindowArmed = raceLaunchBoostUntil > 0f && Time.time < raceLaunchBoostUntil;
            float launchCommand = Mathf.Max(Mathf.Clamp01(activeCommand.launchBoost), launchWindowArmed ? 1f : 0f);
            // The boost yields progressively to a hard traffic throttle cut
            // (urgency braking is suppressed in the launch window, so the throttle
            // cut is what regulates a closing car - an unbrakeable full-strength
            // shove would otherwise ram it into the car ahead): full boost at
            // >=0.72 commanded throttle, fading to zero as the cut approaches 0.
            // Nerf history: -25%, then -30%, then -30% again (peak 30 -> 22.5 ->
            // 15.75 -> 11.025). Per request, reduced by a final -100%: the
            // dedicated AI launch boost force is now fully disabled. AI launch
            // performance is whatever the shared throttle-ramp/physics model on
            // its own produces, identical in kind to the player's - no AI-only
            // additive push at all. launchCommand/launchWindowArmed above are
            // left in place (harmless - they just no longer drive a nonzero
            // force) so this can be re-enabled with a single number if ever
            // needed again.
            // Force is disabled (see comment above), so the FIRING/NOT-FIRING
            // diagnostics are pointless noise now - a "NOT FIRING" warning would
            // spam every single race even though nothing is actually wrong. Both
            // removed; launchGatesOpen/the AddForce call go with them since
            // there's nothing left to gate.

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
                // Fuel starvation: an empty tank doesn't just weaken power, it
                // starves the engine almost entirely - the car should crawl/coast,
                // not just accelerate a bit slower. ERS/DRS boosts are cut too (no
                // fuel to run the hybrid system either). RaceManager reads
                // FuelStarved/FuelStarvedTimer to retire the car after a short
                // grace period rather than DNFing the instant the tank hits zero.
                float starvationPower = fuelStarved ? 0.1f : 1f;
                if (fuelStarved)
                {
                    ActiveSlowdownReason = "FUEL STARVATION";
                }

                body.AddForce(transform.forward * activeCommand.throttle * ((driveAcceleration * speedLimiter) + ersBoost * ersSpeedRamp + drsBoostForce + slipstreamBoost + playerTopSpeedBoost + aiTopSpeedBoost + statTopSpeedBoost * speedLimiter) * starvationPower, ForceMode.Acceleration);
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

            // Aero coefficients and formulas are AeroModel's (F1Game.Race.Physics);
            // this block only applies the car's live modifiers - efficiency stat,
            // gear, damage and the slipstream tow - to the base coefficient.
            float dragCoefficient = AeroModel.DrsClosedDragCoefficient;
            dragCoefficient *= Mathf.Lerp(1.1f, 0.84f, CarData.aeroEfficiency / 100f);
            dragCoefficient *= Mathf.Lerp(1.02f, 0.88f, Mathf.InverseLerp(1, GearCount, CurrentGear));
            dragCoefficient /= Mathf.Max(0.55f, Damage.AeroMultiplier);
            if (slipstreamStrength > 0.05f)
            {
                dragCoefficient *= AeroModel.SlipstreamDragFactor(slipstreamStrength);
            }
            body.AddForce(-body.velocity.normalized * AeroModel.Drag(speedMps, dragCoefficient, DrsActive, AeroModel.DrsDragReductionFraction), ForceMode.Acceleration);

            if (forwardSpeed > topSpeed)
            {
                float excessSpeed = forwardSpeed - topSpeed;
                float limiterDrag = speedCapEngaged ? Mathf.Min(5.5f, excessSpeed * 0.55f) : Mathf.Min(9f, excessSpeed * 2.2f);
                body.AddForce(-transform.forward * limiterDrag, ForceMode.Acceleration);
            }

            float downforce = AeroModel.Downforce(
                speedMps,
                AeroModel.DownforceCoefficient * Mathf.Lerp(0.85f, 1.18f, CarData.aeroEfficiency / 100f),
                1f,
                Damage.AeroMultiplier);
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
            // Barrier-avoidance fix round 4: floor raised again (was 0.66) - the Slow
            // corner-speed bucket now targets ~300-310kph, a genuinely tight corner's
            // actual radius carried at essentially top-speed pace, and cars were
            // running wide on corner exit because turning authority was still being
            // cut by a third at that speed. More authority retained at genuine high
            // speed again.
            float highSpeedLimit = Mathf.Lerp(1f, 0.8f, Mathf.InverseLerp(90f, 320f, speedKph));
            float tyreGrip = Tyres.GripMultiplier(Weather, TrackGripMultiplier);
            float turnRate = Mathf.Lerp(68f, 112f, CarData.chassisBalance / 100f) * speedFactor * highSpeedLimit * tyreGrip * Damage.HandlingMultiplier * (IsPlayerControlled || aiPitApproachNeutralize ? 1f : aiGripAssist * AiCorneringRotationBoost);
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
            // Barrier-avoidance fix round 4: pushed again (was 1.5->1.2 low segment,
            // 1.2->1.08 high segment) - the ~300-310kph Slow-bucket tight-corner
            // target now sits well past the old high-speed segment's own range, so
            // that segment needed real headroom above 1.08x, not just the low-speed
            // segment.
            // Rotation-authority raise (per report: "in tight corners the AI
            // literally can't turn enough"): the yaw-rate math bears it out -
            // at the old authority a car at 200 kph could hold a ~32m radius
            // and at 300 kph only ~56m, while the VeryTight corner class spans
            // ~25-44m. Both segments raised (1.65->1.85 low, 1.35->1.5 /
            // 1.22->1.3 high, continuous at 120 kph): 180 kph now holds ~28m
            // and ~280 kph holds ~54m, which the AI corner-speed floors in
            // AiVehicleController are matched against so targeted speed and
            // achievable rotation finally agree. Shared by player and AI.
            // Round 2 (per request - cornering still far too slow): authority
            // raised drastically again (1.85->2.4 low, 1.5->1.9 / 1.3->1.6
            // high). The yaw math at this level: 200 kph rotates a ~24m radius,
            // 250 kph ~33m, 300 kph ~43m - so genuinely tight street corners
            // are drivable at 200+ and the AI corner floors are re-matched to
            // those numbers. Shared by player and AI alike.
            float tightCorneringBoost = speedKph <= 120f
                ? Mathf.Lerp(2.4f, 1.9f, Mathf.Clamp01((speedKph - 35f) / 85f))
                : Mathf.Lerp(1.9f, 1.6f, Mathf.Clamp01((speedKph - 120f) / 160f));
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
            // Stat-honesty fix (per report - "are the car stats even applied?"):
            // this used to clamp to RaceSpeedCeilingKph (350), so ANY topSpeed
            // stat above 335 mapped to the identical 350 in-game. Stock cars
            // span 341-350 - all clamped flat - and every career top-speed
            // upgrade past that point (a player car can reach 415) did literally
            // NOTHING, while the difficulty AI kph bonus then put a slower-stat
            // car in front on the straights. The stat now carries through
            // honestly up to a generous hardware ceiling, so team differences
            // and upgrades are real straight-line speed.
            // Baseline removed (per request - "get rid of the 15 kph every car
            // has it baseline"): the topSpeed STAT now IS the car's unassisted
            // top speed, for player and AI alike - no hidden additive on top.
            // Floor lowered to match (the old 342 assumed the +15).
            float statTarget = Mathf.Clamp(carTopSpeed, 335f, StatTopSpeedCeilingKph) * setupTopSpeedMultiplier;
            float target = statTarget;

            // ERS needs a ceiling above the base clamp, otherwise its bonus
            // is silently absorbed since the unassisted target already sits close
            // to it. DRS no longer touches this ceiling at all - see the flat,
            // uncapped DrsBoostActive bonus applied after every clamp below.
            // Bonus-absorption fix (per report - "360 stat car still can't reach
            // 360"): every bump below used to raise the ceiling relative to the
            // OLD flat RaceSpeedCeilingKph (350) base. For a stock car the
            // resulting ceiling exceeded its target, so nothing was lost - but
            // for a high-stat car whose statTarget already sits above 350+bonus,
            // the ceiling stayed at bare statTarget and the player bonus, ERS
            // and the tow were all silently clamped away (target 395, capped
            // straight back to 375). The ceiling is now built ADDITIVELY from
            // the same base the target uses, so every bonus genuinely stacks on
            // top of the stat for fast and slow cars alike.
            float ceiling = Mathf.Max(RaceSpeedCeilingKph, statTarget);

            // Player-only straightline speed buff: AiVehicleController's own
            // straightTargetSpeed reads this same TargetTopSpeedKph, so this
            // must stay gated on IsPlayerControlled or every AI car would
            // silently receive the identical bonus too.
            if (IsPlayerControlled)
            {
                target += PlayerTopSpeedBonusKph;
                ceiling += PlayerTopSpeedBonusKph;
            }
            else
            {
                target += aiTopSpeedBonusKph;
                ceiling += aiTopSpeedBonusKph;
            }

            // Empty-battery ERS boost during DRS fix (per report): this used
            // ErsDeploying, but CalculateTargetTopSpeedKph runs BEFORE
            // ErsDeploying is recomputed each frame (see ApplyForces), so it
            // read last frame's value - on a DRS straight where the battery
            // drained to empty, the stale read kept adding the ERS top-speed
            // bonus with no charge left. Now a LIVE battery check: the bonus
            // only applies while the driver is actually holding deploy AND
            // there is real charge (>1%), so a drained battery gives nothing,
            // DRS or not.
            if (activeCommand.ers && ErsBattery > 0.01f)
            {
                target += ErsTopSpeedBonusKph;
                ceiling += ErsTopSpeedBonusKph;
            }

            // Slipstream: a genuine top-speed bonus (see SlipstreamBonusKph), same
            // pattern as DRS/ERS above - stacks with both rather than being
            // absorbed by them, since a tow and an open rear wing are physically
            // independent effects. The safe overall cap right below (405) keeps
            // DRS + ERS + slipstream all stacking at once from producing an
            // unreasonable top speed.
            if (slipstreamStrength > 0.05f)
            {
                target += SlipstreamTopSpeedBonusKph * slipstreamStrength;
                ceiling += SlipstreamTopSpeedBonusKph;
            }

            // Stacking safety cap: was a flat 405 (the old 350 base + ~55 of
            // bonuses). Now relative to the car's own honest stat target so a
            // genuinely fast car keeps its full advantage under ERS/tow while
            // the bonuses still can't stack unboundedly (player bonus 5 + ERS
            // 30 + tow all fit inside the +60 headroom). Floored at the old 405
            // so stock cars behave exactly as before.
            ceiling = Mathf.Min(ceiling, Mathf.Max(405f, statTarget + 60f));

            // Tyre-difference pass: straight-line top speed previously never varied
            // by compound at all - only cornering/acceleration did, via tyre grip.
            // Subtracts TyreState's flat, weather-aware compound penalty (see
            // CompoundSpeedOffsetKph) directly from the top-speed target so a slower
            // compound is genuinely, consistently that many kph down on the
            // straights too, for both the player and every AI car (AiVehicleController's
            // own straightTargetSpeed reads this same TargetTopSpeedKph).
            if (Tyres != null)
            {
                target -= Tyres.CompoundSpeedOffsetKph(Weather);
            }

            float cappedTarget = Mathf.Min(Mathf.Max(target, 60f), ceiling);

            // Flat DRS speed boost: deliberately added AFTER every ceiling clamp
            // above (the 350 base ceiling and the 405 shared safety cap both) so
            // it is never absorbed by them - a genuinely uncapped +DrsBoostAmountKph
            // on top of whatever the car could otherwise reach, exactly as
            // requested, for as long as DrsBoostActive holds (see ApplyForces).
            if (DrsBoostActive)
            {
                cappedTarget += DrsBoostAmountKph;
            }

            // Puncture (per request): a tyre at 0% remaining is gone - every
            // speed source above (base pace, tow, ERS ceiling, even an open
            // DRS wing) is halved outright until the car reaches the pits.
            if (Tyres != null && Tyres.Punctured)
            {
                cappedTarget *= 0.5f;
            }

            return cappedTarget;
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
            SoftenCarContact(collision);
            ProcessDamageCollision(collision, false);
        }

        // Collision-violence fix (per report): car-vs-car contact should trade
        // paint and cost momentum, not launch either car across the road. By
        // the time OnCollisionEnter runs, the solver has already applied its
        // impulse - so the velocity CHANGE this contact just caused is
        // reconstructed against the last pre-physics velocity and clamped to a
        // firm-shove ceiling, and any spin the impulse imparted is capped.
        // Wall/barrier hits keep full physics (hitting a wall SHOULD be
        // violent); only contact with another car is softened.
        const float MaxCarContactVelocityChange = 4.5f;
        void SoftenCarContact(Collision collision)
        {
            if (body == null || collision.rigidbody == null ||
                collision.collider == null || collision.collider.GetComponentInParent<VehicleController>() == null)
            {
                return;
            }

            Vector3 velocityChange = body.velocity - lastPrePhysicsVelocity;
            float changeMagnitude = velocityChange.magnitude;
            if (changeMagnitude > MaxCarContactVelocityChange)
            {
                body.velocity = lastPrePhysicsVelocity + velocityChange * (MaxCarContactVelocityChange / changeMagnitude);
            }

            if (body.angularVelocity.magnitude > 1.6f)
            {
                body.angularVelocity = body.angularVelocity.normalized * 1.6f;
            }
        }

        void OnCollisionStay(Collision collision)
        {
            ProcessDamageCollision(collision, true);
        }

        void ProcessDamageCollision(Collision collision, bool sustained)
        {
            if (!initialized || !damageEnabled || Damage == null || IsHeldOnGrid || IsHeldInPit || collision.contactCount == 0 || collision.collider == null)
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

                // [PitDiag] wall-contact telemetry (per report - "still get
                // slammed into the walls when pitting", after multiple rounds
                // of assist fixes): the pit pipeline has three distinct control
                // regimes (racing-speed guide, braked pre-position, kinematic
                // rail) and the right fix depends entirely on WHERE the contact
                // happens - so every wall hit during a pit-bound phase logs the
                // exact position, speed and steering state. Rate-limited so a
                // grind doesn't spam.
                if (IsPlayerControlled && (PitRequested || PitLimiterActive) && Time.time - lastPitWallHitLogTime > 1.5f)
                {
                    lastPitWallHitLogTime = Time.time;
                    TrackProgress hitProgress = Track != null ? Track.GetProgress(transform.position) : new TrackProgress();
                    Debug.LogWarning("[PitDiag] PLAYER WALL HIT while pit-bound: obj=" + objectName +
                                     " norm=" + hitProgress.normalized.ToString("0.000") +
                                     " lateral=" + hitProgress.lateralDistance.ToString("0.0") +
                                     " halfW=" + (Track != null ? Track.HalfWidthAt(hitProgress.distance).ToString("0.0") : "?") +
                                     " speed=" + (body.velocity.magnitude * 3.6f).ToString("0") + "kph" +
                                     " impact=" + normalSpeedKph.ToString("0") + "kph" +
                                     " steer=" + LastSteerInput.ToString("0.00") +
                                     " limiter=" + PitLimiterActive +
                                     " sustained=" + sustained);
                }
            }

            Vector3 localPoint = transform.InverseTransformPoint(contact.point);
            // AI take NO damage (per request): the collision physics response
            // (dampening above) still applies, but AI cars never accumulate any
            // damage, so contact can never sap their pace. The player still
            // takes full damage - their contact is their own doing.
            if (!IsPlayerControlled)
            {
                LastDamageDebug = "AI - no damage (" + objectName + ")";
                return;
            }

            // Player damage reduction (per request): the player takes 30% less
            // damage from every impact than the raw impact model would apply.
            // AI already take none (returned above), so this only scales the
            // player's own accumulation.
            // Round 2 (per request): a further 30% off (0.7 * 0.7 = 0.49).
            const float PlayerDamageScale = 0.49f;
            float delta = Damage.AddImpact(impactSpeedKph, normalSpeedKph, localPoint, impactType, sustained, PlayerDamageScale);
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
            // Round 2 wall-bounce fix: nudged up slightly on top of the
            // original pinball-physics fix - cars were still visibly bouncing
            // off barriers, so both the angular settle and (mainly) the
            // rebound-velocity cut below are strengthened further.
            float angularDampFactor = sustained ? Mathf.Lerp(0.3f, 0.48f, severity) : Mathf.Lerp(0.3f, 0.6f, severity);
            body.angularVelocity *= (1f - angularDampFactor);

            // Rebound cut: only the velocity component pointing back away from
            // the wall (along its own contact normal) is reduced, not the car's
            // along-the-wall/forward momentum - a graze keeps rolling speed, a
            // head-on hit loses the violent kickback that used to fire it back
            // across the track. Round 2: cut range raised further so barrier
            // hits lose most of their bounce-back speed instead of just most
            // of it - the car should scrub off along the wall, not spring away.
            Vector3 normal = contactNormal.sqrMagnitude > 0.001f ? contactNormal.normalized : Vector3.up;
            float reboundSpeed = Vector3.Dot(body.velocity, normal);
            if (reboundSpeed > 0f)
            {
                float reboundCut = sustained ? Mathf.Lerp(0.45f, 0.7f, severity) : Mathf.Lerp(0.62f, 0.9f, severity);
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
