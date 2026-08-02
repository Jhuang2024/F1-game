using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager grid-spawn subsystem (partial). Resolves each car's grid slot,
    /// spawns the participants and their vehicle/AI/player controllers, computes
    /// the AI start-reaction delays, finds a safe on-road spawn position, logs the
    /// player's spawn physics, and holds the field on the grid through the start
    /// countdown. Split out of the RaceManager monolith verbatim - same class, same
    /// members, identical spawn order, RNG call order (grid jitter / reaction
    /// delays) and tuned values; callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        int ResolveGridIndex(string driverId, int fallback)
        {
            if (CurrentSession == RaceWeekendSession.Qualifying || !IsCareerRace)
            {
                return fallback;
            }

            // Stale-grid fix: this used to try Career.Save.lastQualifyingResults
            // FIRST, regardless of round - that field is never cleared between
            // rounds, so if anything ever reached this path before a fresh
            // qualifying result existed for the CURRENT round, the race would
            // silently spawn using a previous round's grid instead of falling
            // back to the difficulty-based slot. The round/season-scoped
            // search is the only source of truth here now; lastQualifyingResults
            // (which is fine as a same-session "most recent" convenience for
            // UI display right after qualifying) is never consulted for this.
            List<QualifyingResultEntry> grid = null;
            if (Career != null && Career.Save != null && Career.Save.qualifyingResults != null)
            {
                for (int i = Career.Save.qualifyingResults.Count - 1; i >= 0; i--)
                {
                    QualifyingResultRecord record = Career.Save.qualifyingResults[i];
                    if (record.season == Career.Save.currentSeason && record.round == Career.Save.currentRound && record.results != null && record.results.Count > 0)
                    {
                        grid = record.results;
                        break;
                    }
                }
            }

            if (grid == null || grid.Count == 0)
            {
                return fallback;
            }

            for (int i = 0; i < grid.Count; i++)
            {
                if (grid[i].driverId == driverId)
                {
                    return Mathf.Max(0, grid[i].position - 1);
                }
            }

            if (driverId == "player")
            {
                for (int i = 0; i < grid.Count; i++)
                {
                    if (grid[i].isPlayer)
                    {
                        return Mathf.Max(0, grid[i].position - 1);
                    }
                }
            }

            return fallback;
        }

        RaceParticipant SpawnParticipant(
            string driverId,
            string driverName,
            string teamId,
            string teamShort,
            bool player,
            DriverData driver,
            TeamData team,
            CarPerformanceData car,
            int gridIndex)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            float gridDistance;
            float lane;
            Track.GetGridSlot(gridIndex, out gridDistance, out lane);
            Track.SampleAtDistance(gridDistance, out point, out forward, out right);
            Vector3 spawnPosition = FindRoadSpawnPosition(point + right * lane, driverName, out bool hitRoad);
            Quaternion spawnRotation = Quaternion.LookRotation(forward, Vector3.up);
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                // Qualifying runs launch from the car's own pit box, not a shared point.
                Track.GetPitServicePose(Mathf.Clamp(gridIndex, 0, TrackRuntime.PitBoxCount - 1), out spawnPosition, out spawnRotation);
                spawnPosition += Vector3.up * 0.1f;
            }

            GameObject carObject = ProductionCarSpawner.SpawnCar(driverName, team.PrimaryUnityColor, team.SecondaryUnityColor);
            carObject.transform.SetParent(raceWorld.transform);
            carObject.transform.position = spawnPosition;
            carObject.transform.rotation = spawnRotation;
            if (!player)
            {
                CarVisualFactory.CreateDriverLabel(carObject.transform, driver != null && !string.IsNullOrEmpty(driver.abbreviation) ? driver.abbreviation : driverName, team.SecondaryUnityColor);
            }

            VehicleController controller = carObject.AddComponent<VehicleController>();
            LapTracker lapTracker = carObject.AddComponent<LapTracker>();
            RaceParticipant participant = carObject.AddComponent<RaceParticipant>();
            participant.Initialize(driverId, driverName, teamId, teamShort, player, driver, team, car);
            participant.gridPosition = gridIndex + 1;
            participant.pitBoxIndex = Mathf.Clamp(gridIndex, 0, TrackRuntime.PitBoxCount - 1);
            participant.startReactionDelay = player ? 0f : ResolveAiStartReactionDelay(driver);
            // Rare AI jump start (StartProcedureRules): rolled once here; the
            // countdown loop physically releases the car that early and the
            // same rulebook judgement/penalty path as the player fires.
            float consistency01 = driver == null ? 0.78f : driver.consistency / 100f;
            participant.aiJumpStartWindowSeconds =
                !player && CurrentSession != RaceWeekendSession.Qualifying && !IsTimeTrial &&
                Random.value < StartProcedureRules.AiJumpStartChance(consistency01)
                    ? StartProcedureRules.JumpLaunchWindowSeconds(Random.value)
                    : 0f;
            participant.hasLastSafePosition = true;
            participant.lastSafePosition = spawnPosition;
            participant.lastSafeRotation = carObject.transform.rotation;
            TyreCompound startCompound = StartingTyreForParticipant(player);
            participant.startingCompound = startCompound;
            controller.Initialize(car, Track, startCompound, Settings.Current.manualGears && player, Settings.Current, player);
            // Unconditional diagnostic (same pattern as [QualiSim]/[ErsDrain])
            // for the reported "my upgraded car races like a stock one": logs
            // the EXACT car data the player's physics was initialised with, so
            // a stock-vs-upgraded mismatch can be pinned to the data pipeline
            // (career-effective car vs raw reference) instead of guessed at.
            if (player)
            {
                Debug.Log("[PlayerCar] id=" + (car != null ? car.id : "null") +
                          " topSpeed=" + (car != null ? car.topSpeed : -1) +
                          " accel=" + (car != null ? car.acceleration : -1) +
                          " engine=" + (car != null ? car.enginePower : -1) +
                          " aero=" + (car != null ? car.aeroEfficiency : -1) +
                          " careerRace=" + IsCareerRace);
            }
            if (!player)
            {
                // Difficulty round 5 (per request): explicit, difficulty-scaled
                // AI machinery advantage - Easy stays at player parity; the top
                // tiers run genuinely faster, grippier cars as a deliberate
                // difficulty mechanism (this replaces the old hidden flat kph
                // bonus with an intentional, documented knob).
                // AI top-speed advantage: a LIGHT trim off the original
                // 5/9/14/18 (the earlier 0/4/8/11 pass over-nerfed it - the AI
                // was too easy to drive away from on the straights). This keeps
                // the AI competitive while shaving a little straight-line speed.
                // Grip assist is unchanged - this is a straight-line knob only.
                // Nerf rework (per request): the previous "smidge" nerf trimmed
                // BOTH the straight-line bonus and the grip assist. Reverted -
                // grip assists are back at their pre-nerf values (1.04/1.09/
                // 1.15) - and the nerf is now purely STRAIGHT-LINE: every tier's
                // kph bonus is cut ~3 from its pre-nerf value (4/8/13/16 ->
                // 1/5/10/13), so the AI corner exactly as before but pull less
                // on the straights at every skill level.
                // Difficulty rework (per request - "AI shouldn't have artificial
                // speed increasers; straight-line speed should be the same
                // across all difficulties; what should change is their
                // overtaking and defensive abilities"): every tier now runs
                // IDENTICAL machinery to the player (no kph bonus, no grip
                // assist). Difficulty instead differentiates racecraft - a
                // per-tier overtaking/defending delta in AiVehicleController -
                // plus the existing per-tier behaviour profiles (corner
                // commitment, reaction, mistakes).
                //
                // Skill grip UTILIZATION (per report - "the swing feels not
                // only not noticeable but completely trivial"): forty-one buff
                // rounds pushed every driver's corner TARGETS beyond the
                // physical envelope, so the widened target bands stopped
                // separating anyone - the whole field cornered at the same
                // physics-bound limit and the driver-skill spread evaporated.
                // Skill now expresses where it can never saturate: how much of
                // the car's REAL grip the driver actually uses. The ceiling is
                // exactly 1.0 (the player's identical machinery - never above,
                // so no unfair physics at any difficulty), and a backmarker
                // leaves ~12% of the car on the table - a real, unsaturatable
                // corner-speed gap that stacks with the car's own stats.
                // Difficulty-independent by construction.
                float skillNorm = driver == null ? 0.5f : Mathf.InverseLerp(74f, 97f, driver.pace);
                // Widened again (per request - "the skill gap still isn't
                // nearly wide enough"): a backmarker now leaves ~22% of the
                // car on the table (was 12%). Ceiling stays exactly 1.0 - the
                // player's identical machinery, never above.
                // Widened again (season-standings report - "everyone but me
                // is on like 150 points... midfield and backmarkers should
                // have 0-80 points across the entire season MAX"): race-after-
                // race, finishing order must track driver quality tightly
                // enough that the points concentrate at the top the way a real
                // season's do. A backmarker now leaves ~28% of the car on the
                // table (was 22%). Ceiling stays exactly 1.0 - the best AI use
                // precisely the player's machinery, never more.
                // Pulled back up 0.72 -> 0.84 (per report + [PaceDiag]: the
                // elite tier already matches the player - 1:25.5 vs 1:25.2 -
                // but the rest of the field sat 3-10s off, reading as
                // "egregiously slow"). A backmarker now leaves ~16% of the
                // car on the table: still a genuine, race-deciding skill
                // spread, but the whole field races at a respectable level.
                // Ceiling stays exactly 1.0 - never an unfair advantage.
                // Floor 0.84 -> 0.93 (per report - "ai r still way too slow u need to
                // make them better somehow in a natural way"). This is the single
                // largest handicap in the system and the most natural place to give
                // the pace back, because it is not a fake assist in either direction:
                // it stays a driver-quality model, ceilinged at exactly 1.0, and the
                // ordering it produces is unchanged. It is only calibrated now.
                //
                // aiGripAssist multiplies real lateral grip AND turn rate, so it is
                // close to lap-time-linear through every corner: a 0.84 driver was
                // giving up 16% of the car's cornering everywhere, which is not a
                // driver difference, it is a different car. A real F1 grid spans
                // roughly 2-3% in lap time from front to back. 7% here is still
                // several times that - deliberately, so finishing order tracks driver
                // quality firmly - while no longer leaving the midfield driving as if
                // the track were wet.
                //
                // It also composes with the planning fix: CorneringRotationFactor is
                // gripUtilization x 1.1, so at a 0.93 floor it lands at 1.02 instead
                // of 0.92, and making MaxCorneringSpeedKph honest about the car's real
                // rotation no longer costs a slow driver anything on top.
                float gripUtilization = Mathf.Lerp(0.93f, 1f, skillNorm);
                controller.SetAiPerformanceAssist(0f, gripUtilization);
                // Unconditional diagnostic ([PlayerCar]/[QualiSim] pattern, per
                // report - "I don't know what you did with the skill level but
                // clearly something's wrong"): the exact skill inputs each AI
                // was spawned with, so a scrambled running order can be checked
                // against the actual numbers instead of inferred from a tower
                // screenshot.
                // tyre/weather appended (per the mass lap-2 stop-wave log):
                // one glance now shows whether the grid's compounds matched
                // the weather the race actually started with.
                Debug.Log("[SkillDiag] " + (driver != null ? driver.displayName : "?") +
                          " pace=" + (driver != null ? driver.pace : -1) +
                          " skillNorm=" + skillNorm.ToString("0.00") +
                          " gripUtil=" + gripUtilization.ToString("0.000") +
                          " car=" + (car != null ? car.id : "null") +
                          " startTyre=" + startCompound +
                          " weather=" + (Track != null ? Track.weather.ToString() : "?"));
            }
            if (IsTimeTrial)
            {
                // Time trial: warm the tyres to their optimal window and switch off
                // contact damage, so a flying lap has full grip immediately and can
                // never be ended by a scrape.
                controller.PreheatTyres();
                controller.SetDamageEnabled(false);
            }
            else
            {
                // Tyre blankets for the whole field at every session start. Real
                // blankets are regulated to 70 C, which is BELOW the operating
                // window - so the field leaves the grid warm but not switched on,
                // and the opening lap genuinely brings the tyre in. This used to
                // snap every car to the exact centre of its window, i.e. perfect
                // grip from lights out, which deleted the warm-up phase entirely.
                // The original concern this addressed still holds: everyone gets
                // the same blanket treatment, so the opening-lap order is not
                // scrambled by cold-tyre luck.
                controller.ApplyTyreBlankets();
            }

            // Fuel system pass: session-specific start fuel instead of the old flat
            // 35kg for every session (see VehicleController.SetStartFuel). Player
            // reads their own pre-race choice off settings; AI gets its own
            // per-driver roll (ResolveAiFuelChoice) - most AI runs Target, same as
            // a sensible player default.
            float startFuelKg;
            float fuelPerLapKg = EstimateFuelPerLapKg(Track, Settings.Difficulty);
            if (IsTimeTrial)
            {
                startFuelKg = ComputeTimeTrialFuelKg();
            }
            else if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                startFuelKg = ComputeQualifyingFuelKg(Track);
            }
            else if (CurrentSession == RaceWeekendSession.Practice)
            {
                startFuelKg = ComputePracticeFuelKg(Track);
            }
            else
            {
                FuelLoadChoice fuelChoice = player ? (FuelLoadChoice)Settings.Current.fuelLoadChoice : ResolveAiFuelChoice(driver);
                participant.chosenFuelLoad = fuelChoice;
                // AI loads go through the starvation-proof AI computation (burn
                // margin + capped underfuel delta - see ComputeAiRaceStartFuelKg);
                // the player's own gamble stays exactly as chosen.
                startFuelKg = player
                    ? ComputeRaceStartFuelKg(Mathf.Max(3, Settings.Current.laps), fuelChoice, Track, Settings.Current)
                    : ComputeAiRaceStartFuelKg(Mathf.Max(3, Settings.Current.laps), fuelChoice, Track, Settings.Current);
            }

            controller.SetStartFuel(startFuelKg, fuelPerLapKg);
            controller.SetFuelBurnDisabled(IsTimeTrial);
            controller.SetGridHold(StartCountdown > 0f);
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                participant.pitLimiterUntilExit = true;
                controller.SetPitLimiter(true);
            }

            VehicleAudio audio = carObject.AddComponent<VehicleAudio>();
            audio.Initialize(Settings.Current.audioEnabled, player ? 0.55f : 0.28f);
            if (Settings.Current.particlesEnabled)
            {
                VehicleEffects effects = carObject.AddComponent<VehicleEffects>();
                effects.Initialize(controller);
            }
            lapTracker.Initialize(Track, CurrentSession == RaceWeekendSession.Qualifying ? QualifyingSessionLapCap : RaceLaps);
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                lapTracker.ConfigureQualifyingOutLap();
            }
            else
            {
                lapTracker.ConfigureRaceGridStart(gridDistance);
            }

            participant.vehicle = controller;
            participant.lapTracker = lapTracker;

            if (player)
            {
                CameraRig rig = new GameObject("Player camera rig").AddComponent<CameraRig>();
                playerCameraRig = rig;
                rig.transform.SetParent(raceWorld.transform);
                rig.transform.position = carObject.transform.position - carObject.transform.forward * 10f + Vector3.up * 4f;
                rig.Initialize(
                    carObject.transform,
                    Settings.Current.cameraShake ? Settings.Current.cameraShakeStrength * CameraShakeLevelMultiplier(Settings.Current.cameraShakeLevel) : 0f,
                    Settings.Current.cameraFov);
                PlayerVehicleInput input = carObject.AddComponent<PlayerVehicleInput>();
                input.raceManager = this;
                input.cameraRig = rig;
                input.participant = participant;
            }
            else
            {
                AiVehicleController ai = carObject.AddComponent<AiVehicleController>();
                ai.Initialize(this, participant, Track);
            }

            if (State != null) State.RegisterParticipant(participant);
            return participant;
        }

        // Reaction delay scales with difficulty's reactionTimeSeconds and the
        // driver's own awareness/consistency, instead of one flat random range for
        // every AI regardless of difficulty or driver skill. Lower skill/difficulty
        // launches later and less consistently; Expert-tier AI launches sharp.
        float ResolveAiStartReactionDelay(DriverData driver)
        {
            AiDifficultyProfile profile = GetAiDifficultyProfile();
            // Skill blend + the 0.7-0.35 base-delay scale and 0.14-0.03 variance band
            // live in the engine-free StartProcedureRules (halved from the old
            // 1.3-0.75 so a competent AI is off the line almost as promptly as the
            // player, then the physics launch boost does the rest). The null-driver
            // default and the Random.Range that samples the variance stay here.
            float skillBlend = driver == null ? 0.5f : StartProcedureRules.AiReactionSkillBlend(driver.awareness, driver.consistency);
            float baseDelay = StartProcedureRules.AiReactionBaseDelaySeconds(profile.reactionTimeSeconds, skillBlend);
            float variance = StartProcedureRules.AiReactionVarianceSeconds(skillBlend);
            return Mathf.Max(0f, baseDelay + Random.Range(-variance, variance));
        }

        Vector3 FindRoadSpawnPosition(Vector3 desired, string driverName, out bool hitRoad)
        {
            hitRoad = false;
            Vector3 origin = desired + Vector3.up * 35f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 90f, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            Vector3 bestPoint = desired + Vector3.up * 0.18f;
            for (int i = 0; i < hits.Length; i++)
            {
                if (Track.roadCollider != null && hits[i].collider == Track.roadCollider && hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    bestPoint = hits[i].point + Vector3.up * 0.18f;
                    hitRoad = true;
                }
            }

            if (!hitRoad)
            {
                Debug.LogWarning("[RoadPhysics] No drivable road collider under spawn for " + driverName +
                                 " desired=" + desired +
                                 " roadColliderExists=" + (Track.roadCollider != null));
            }
            else
            {
                GameLog.Info("[RoadPhysics] Spawn raycast hit road for " + driverName + " spawn=" + bestPoint);
            }

            return bestPoint;
        }

        void LogPlayerSpawnPhysics()
        {
            if (PlayerParticipant == null || PlayerParticipant.vehicle == null)
            {
                return;
            }

            Vector3 origin = PlayerParticipant.transform.position + Vector3.up * 6f;
            bool hitRoad = false;
            Vector3 hitPoint = Vector3.zero;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (Track.roadCollider != null && hits[i].collider == Track.roadCollider && hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    hitPoint = hits[i].point;
                    hitRoad = true;
                }
            }

            Rigidbody body = PlayerParticipant.GetComponent<Rigidbody>();
            GameLog.Info("[RoadPhysics] Player spawn position=" + PlayerParticipant.transform.position +
                      " raycastHitRoad=" + hitRoad +
                      " roadHitPoint=" + hitPoint +
                      " rigidbodyY=" + (body == null ? -999f : body.position.y) +
                      " roadColliderExists=" + (Track.roadCollider != null) +
                      " roadLayer=" + (Track.roadCollider == null ? "none" : LayerMask.LayerToName(Track.roadCollider.gameObject.layer)) +
                      " carLayer=" + LayerMask.LayerToName(PlayerParticipant.gameObject.layer) +
                      " roadCollidesWithCarLayer=" + (Track.roadCollider != null && !Physics.GetIgnoreLayerCollision(Track.roadCollider.gameObject.layer, PlayerParticipant.gameObject.layer)));
            if (!hitRoad)
            {
                Debug.LogWarning("[RoadPhysics] No drivable road collider found below player spawn.");
            }
        }

        void HoldGridCars(bool held)
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null || participant.vehicle == null)
                {
                    continue;
                }

                // A jump-starting AI car whose rolled window has arrived stays
                // free - its physical launch and penalty are handled in the
                // countdown tick (see the StartCountdown block in Update).
                //
                // The `StartCountdown > 0f` term is load-bearing: aiJumpStartWindowSeconds
                // is rolled once at spawn and never cleared, so once the race is
                // under way StartCountdown is permanently 0 and the old condition
                // (StartCountdown <= window) was trivially true forever. This
                // exemption then leaked into the red-flag standing restart, which
                // relies on HoldGridCars(true) to pin every car to its teleported
                // slot - one AI would drive away from its grid box while the other
                // 21 sat frozen, and reach the green several car lengths up the road.
                if (held && StartCountdown > 0f && !participant.isPlayer &&
                    participant.aiJumpStartWindowSeconds > 0f &&
                    StartCountdown <= participant.aiJumpStartWindowSeconds)
                {
                    continue;
                }

                participant.vehicle.SetGridHold(held);
            }
        }

        void SpawnRaceGrid(string playerName, string playerTeamId, bool careerRace)
        {
            TeamData playerTeam = Data.FindTeam(playerTeamId);
            CarPerformanceData playerCar = ResolveTeamCarPerformance(playerTeam);

            // Without a usable qualifying result (the common quick-race path, since
            // quick race is never a career race) the player no longer defaults to
            // pole - the fallback itself is difficulty-scaled. AI fallback slots are
            // then built around whichever slot the player lands in so the two streams
            // can never collide.
            int playerGridFallback = CurrentSession == RaceWeekendSession.Qualifying ? 0 : ResolvePlayerGridFallback();
            // Career identity fix: this used to always pass null for the player's
            // DriverData, even when playing as a real driver (e.g. Oscar Piastri) -
            // RaceParticipant.driverData stayed null for the whole race, so the
            // timing tower/HUD/radio code (which all prefer driverData.abbreviation)
            // fell back to guessing a code from the display name instead of using
            // the real "PIA"-style abbreviation. ResolvePlayerQualifyingDriverData
            // already resolves the actual selected DriverData when one exists
            // (falling back to a synthesized one with a correctly-parsed
            // last-name-based abbreviation otherwise) - reused here for the real
            // race grid, not just the qualifying-sim path it was originally written
            // for.
            PlayerParticipant = SpawnParticipant(
                "player",
                playerName,
                playerTeam.id,
                playerTeam.shortName,
                true,
                ResolvePlayerQualifyingDriverData(playerName, playerTeamId),
                playerTeam,
                playerCar,
                ResolveGridIndex("player", playerGridFallback));

            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                BuildQualifyingField(playerTeamId);
                PrepareAiQualifyingTargetsForPhase();
                return;
            }

            if (IsTimeTrial)
            {
                return;
            }

            List<DriverData> aiDrivers = GetDefensiveAiRoster(playerTeamId, playerName);
            int aiFallbackSlot = 0;
            for (int i = 0; i < aiDrivers.Count; i++)
            {
                if (aiFallbackSlot == playerGridFallback)
                {
                    aiFallbackSlot++;
                }

                DriverData driver = aiDrivers[i];
                TeamData team = ResolveDriverTeam(driver);
                CarPerformanceData car = ResolveTeamCarPerformance(team);
                SpawnParticipant(
                    driver.id,
                    driver.displayName,
                    team.id,
                    team.shortName,
                    false,
                    driver,
                    team,
                    car,
                    ResolveGridIndex(driver.id, aiFallbackSlot));
                aiFallbackSlot++;
            }
        }

        // Difficulty-scaled starting slot used only when no real qualifying result
        // exists for this session (quick race with no qualifying run, in practice).
        // 0-based index; Expert lands dead last against the full 21-car AI field.
        int ResolvePlayerGridFallback()
        {
            int lastIndex = Mathf.Max(0, FullWeekendAiCount);
            RaceDifficulty difficulty = Settings.Difficulty;
            if (difficulty == RaceDifficulty.Easy)
            {
                return Mathf.Clamp(Random.Range(4, 8), 0, lastIndex);
            }

            if (difficulty == RaceDifficulty.Medium)
            {
                return Mathf.Clamp(Random.Range(9, 14), 0, lastIndex);
            }

            if (difficulty == RaceDifficulty.Hard)
            {
                return Mathf.Clamp(Random.Range(15, 20), 0, lastIndex);
            }

            return lastIndex;
        }

    }
}
