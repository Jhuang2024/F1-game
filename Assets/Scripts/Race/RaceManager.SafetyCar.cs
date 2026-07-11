using System.Collections;
using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager safety-car + race-control state machine (partial, part 2 of
    /// 2). VSC/SC deployment, the safety-car car build/respawn/overtake checks,
    /// UpdateSafetyCar pacing, the red-flag grid teleport, the green->SC->restart
    /// DriveRaceControlStateMachine and the player SC-pit offer (public
    /// AcceptRaceControlPitOffer / PlayerGapToSafetyCarMeters). Verbatim split;
    /// pacing, timings, RNG and call order unchanged.
    /// </summary>
    public partial class RaceManager
    {
        void BeginVirtualSafetyCar(RaceParticipant involved = null, int sector = 0)
        {
            CurrentRaceControlState = RaceControlState.VirtualSafetyCar;
            safetyCarTimer = Random.Range(14f, 24f);
            playerScPitPromptSent = false;
            // Radio clarity: declares who caused it and where instead of a
            // bare "virtual safety car deployed".
            string cause = involved != null ? " - " + involved.driverName + (sector > 0 ? " in trouble at Sector " + sector : " in trouble") : "";
            GameLog.Info("[RaceControl] Virtual safety car deployed. duration=" + safetyCarTimer.ToString("0.0") + "s" + cause);
            LogRaceControlHistory("VSC", "Virtual safety car deployed" + cause);
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Virtual safety car deployed" + cause + ".", true, RaceAudioCue.Vsc);
            }
        }

        void BeginSafetyCarDeployment(RaceParticipant involved = null, int sector = 0)
        {
            CurrentRaceControlState = RaceControlState.SafetyCarDeploying;
            safetyCarTimer = Random.Range(6f, 10f);
            SafetyCarDeploymentCount++;
            playerScPitPromptSent = false;
            playerScQueueWarningSent = false;
            // Radio clarity: declares who caused it and where instead of a
            // bare "safety car deployed".
            string safetyCarCause = involved != null ? " - " + involved.driverName + (sector > 0 ? " has crashed at Sector " + sector : " has crashed") : "";
            List<RaceParticipant> order = GetRunningOrderSnapshot();
            safetyCarQueueLeader = order.Count > 0 ? order[0] : null;

            // Convoy autopilot (Part 2): freeze each car's legal running-order
            // slot at the instant of deployment as its safety-car queue index -
            // the leader gets 0, second place 1, and so on - so the whole field
            // forms up in a stable, predictable queue rather than re-sorting
            // itself against a moving target every frame while cars close up.
            for (int i = 0; i < order.Count; i++)
            {
                RaceParticipant queued = order[i];
                if (queued == null)
                {
                    continue;
                }

                queued.safetyCarQueueIndex = i;
                queued.preSafetyCarOrderIndex = i;
                if (ShouldQueueUnderRaceControl(queued))
                {
                    queued.isRaceControlAutopilot = true;
                }
            }

            // Part 1: a real AI car, not just a HUD state - it joins the track a
            // short, safe distance ahead of whoever is currently leading, so the
            // leader immediately has to slow down and queue up behind it exactly
            // like a real safety car period.
            EnsureSafetyCarBuilt();
            float leaderDistance = safetyCarQueueLeader != null && State != null
                ? State.GetProgressDistance(safetyCarQueueLeader)
                : 0f;
            if (safetyCarController != null)
            {
                safetyCarController.EnterTrack(Track != null ? Track.WrapDistance(leaderDistance + 60f) : leaderDistance + 60f);
                SafetyCarTargetSpeedKph = Mathf.Max(110f, safetyCarController.CurrentSpeedKph);
                LogSafetyCarSpawnState("deployment");
            }
            else
            {
                SafetyCarTargetSpeedKph = Random.Range(140f, 160f);
                GameLog.Warn("[RaceControl] Safety car deployment WITHOUT a physical car: controller build failed, falling back to pace-cap-only period.");
            }

            safetyCarWatchdogTimer = 0f;
            safetyCarWatchdogRespawnCount = 0;
            GameLog.Info("[RaceControl] Safety car deployment triggered. targetSpeed=" + SafetyCarTargetSpeedKph.ToString("0") + "kph deploymentCount=" + SafetyCarDeploymentCount);
            LogRaceControlHistory("SAFETY CAR", "Deployment #" + SafetyCarDeploymentCount);
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Safety car deployed.", true, RaceAudioCue.SafetyCar);
                PostEngineerMessage("Safety car deployed, car is under race-control autopilot.", true);
                PostEngineerMessage("You can request a pit stop under safety car.", false);
            }
        }

        // Builds the safety car GameObject + controller once per session and
        // reuses it for every subsequent deployment - reactivated via EnterTrack
        // rather than destroyed/recreated each time.
        void EnsureSafetyCarBuilt()
        {
            if (safetyCarController != null)
            {
                return;
            }

            Renderer beaconRenderer;
            Renderer brakeLightRenderer;
            safetyCarObject = CarVisualFactory.CreateSafetyCarVisual(out beaconRenderer, out brakeLightRenderer);
            if (raceWorld != null)
            {
                safetyCarObject.transform.SetParent(raceWorld.transform);
            }

            safetyCarController = safetyCarObject.AddComponent<SafetyCarController>();
            safetyCarController.Configure(Track, beaconRenderer, brakeLightRenderer);
            safetyCarObject.SetActive(false);
        }

        float safetyCarWatchdogTimer;
        int safetyCarWatchdogRespawnCount;
        // Sustained-duration bar before ANY watchdog respawn fires, including
        // the unambiguous null/inactive cases - a single skipped frame during a
        // scene transition, a renderer toggle, or brief physics hiccup must
        // never be enough on its own to trigger a visible respawn/teleport.
        const float SafetyCarWatchdogMissingThresholdSeconds = 4f;
        // A full SC period should essentially never need more than one genuine
        // respawn. If the watchdog wants to fire again after that, something is
        // structurally wrong (or the check itself is too loose) - repeated
        // respawns are themselves the visible "jumping" bug, so refuse to keep
        // respawning and log a warning instead of hiding the real problem.
        const int MaxWatchdogRespawnsPerScPeriod = 1;

        // Only real, unrecoverable failures count as "missing" - a loose
        // "!activeInHierarchy" check alone can flicker true for reasons
        // unrelated to a real failure (a single skipped frame during a scene
        // transition, a renderer toggle, etc.), and being far ahead of the
        // leader is NORMAL during queue formation on a long track, not a sign
        // of a lost car - so it is deliberately not checked here at all.
        bool IsSafetyCarGenuinelyMissing(out string reason)
        {
            if (safetyCarController == null || safetyCarObject == null)
            {
                reason = "null controller/object";
                return true;
            }

            if (!safetyCarController.IsActive || !safetyCarObject.activeInHierarchy)
            {
                reason = "inactive object";
                return true;
            }

            Renderer[] renderers = safetyCarObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                reason = "no renderers";
                return true;
            }

            Vector3 pos = safetyCarObject.transform.position;
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z) ||
                float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z))
            {
                reason = "NaN/invalid position";
                return true;
            }

            if (pos.y < -25f)
            {
                reason = "under terrain (y=" + pos.y.ToString("0.0") + ")";
                return true;
            }

            if (Track != null)
            {
                // Compare the visible object against where its OWN progress
                // distance says it should be - this catches a genuinely
                // derailed/desynced car without ever depending on the leader's
                // position, so normal queue-formation gaps can never trip it.
                Vector3 expectedPoint;
                Vector3 expectedForward;
                Vector3 expectedRight;
                Track.SampleAtDistance(safetyCarController.ProgressDistance, out expectedPoint, out expectedForward, out expectedRight);
                float distanceFromExpected = Vector3.Distance(pos, expectedPoint);
                if (distanceFromExpected > 150f)
                {
                    reason = "absurd distance from track (" + distanceFromExpected.ToString("0") + "m from expected)";
                    return true;
                }
            }

            reason = null;
            return false;
        }

        // Field-wide backstop (Part 1/2): keeps the pace-limiter's own target
        // number honest against the real car's actual speed instead of a single
        // number picked once at deployment, ends the period by sending the car
        // Safety-car capture gate: a car is pulled under race-control convoy
        // autopilot ONLY if it isn't pitting and doesn't intend to. A pending
        // pit request (or a car already on the pit rail) means the car
        // continues its stop instead of being yanked into the queue - real
        // cars dive for the pits under a safety car, they don't abandon a
        // committed stop to form up. Because the per-tick upkeep re-evaluates
        // this every frame, a car that finishes its stop and clears its
        // request rejoins the convoy automatically while the hold period
        // lasts.
        bool ShouldQueueUnderRaceControl(RaceParticipant p)
        {
            if (p == null || p.retired || p.finished)
            {
                return false;
            }

            if (p.pitPhase != PitPhase.None || p.isPitting)
            {
                return false;
            }

            if (p.vehicle != null && p.vehicle.PitRequested)
            {
                return false;
            }

            return true;
        }

        // toward the pits once race control calls "safety car in this lap",
        // penalizes anyone who actually gets past it on track, and - watchdog -
        // rebuilds/respawns the whole car if a full SC period is somehow running
        // without a visible, active safety car object.
        void UpdateSafetyCar()
        {
            // Keeps the shared virtual convoy reference (position + speed every
            // queued car steers/paces against) honest every frame, whether the
            // real safety car object is still physically on track or has already
            // peeled into the pits during the Restart/green-flag ramp hold.
            UpdateRaceControlReference();

            // Convoy autopilot upkeep: every non-retired/non-finished car is under
            // race-control autopilot for as long as IsRaceControlAutopilotHoldPeriod
            // holds true - the full physical safety car period, PLUS the Restart
            // hold, PLUS a short ramp once Green begins. Handing control back only
            // once this goes false (rather than the instant Restart begins) is what
            // gives AiVehicleController a stable, non-stale progress reference and a
            // controlled ramp to resync from instead of an instant snap.
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant p = Participants[i];
                if (p == null)
                {
                    continue;
                }

                if (IsRaceControlAutopilotHoldPeriod && ShouldQueueUnderRaceControl(p))
                {
                    p.isRaceControlAutopilot = true;
                }
                else if (p.isRaceControlAutopilot)
                {
                    p.isRaceControlAutopilot = false;
                }
            }

            if (IsFullSafetyCarPeriod)
            {
                string missingReason;
                bool visibleCarMissing = IsSafetyCarGenuinelyMissing(out missingReason);
                if (visibleCarMissing)
                {
                    bool wasAlreadyCounting = safetyCarWatchdogTimer > 0f;
                    safetyCarWatchdogTimer += Time.deltaTime;
                    if (!wasAlreadyCounting)
                    {
                        // One-shot heads-up the moment the watchdog starts
                        // counting, at Info level - if it fires repeatedly in the
                        // logs without ever reaching a real respawn, that is the
                        // signal of a flickering/loose "missing" check rather
                        // than an actual failure, without the noise of a
                        // respawn (which is logged at Warn) happening every time.
                        GameLog.Info("[RaceControl] Safety car watchdog started counting - reason=" + missingReason);
                    }

                    if (safetyCarWatchdogTimer > SafetyCarWatchdogMissingThresholdSeconds)
                    {
                        safetyCarWatchdogTimer = 0f;
                        if (safetyCarWatchdogRespawnCount >= MaxWatchdogRespawnsPerScPeriod)
                        {
                            // Repeated respawns are themselves the visible
                            // "car jumping around" symptom - refuse to keep
                            // doing it. One respawn per SC period is already a
                            // generous last resort; a second request in the
                            // same period means the underlying cause needs a
                            // real fix, not another teleport.
                            GameLog.Warn("[RaceControl] Safety car watchdog wants to respawn again this SC period (reason=" + missingReason +
                                ") but the per-period respawn cap (" + MaxWatchdogRespawnsPerScPeriod + ") was already reached - refusing to avoid visible jumping.");
                        }
                        else
                        {
                            safetyCarWatchdogRespawnCount++;
                            RespawnMissingSafetyCar(missingReason);
                        }
                    }
                }
                else
                {
                    safetyCarWatchdogTimer = 0f;
                }
            }

            if (safetyCarController == null || !safetyCarController.IsActive)
            {
                aheadOfSafetyCarLastTick.Clear();
                return;
            }

            if (IsFullSafetyCarPeriod)
            {
                SafetyCarTargetSpeedKph = Mathf.Max(60f, safetyCarController.CurrentSpeedKph);

                // Leader pickup (Part 4): the safety car waits (slows hard) while
                // the leader is still far behind, then releases to normal pace as
                // the queue forms up - controlled from here since the controller
                // itself knows nothing about participants.
                if (safetyCarQueueLeader != null && State != null && Track != null)
                {
                    float gapToLeader = Track.WrapDistance(safetyCarController.ProgressDistance - State.GetProgressDistance(safetyCarQueueLeader));
                    safetyCarController.SetLeaderGapMeters(gapToLeader);
                }
            }

            if (!IsFullSafetyCarPeriod && !safetyCarController.IsReturningToPits)
            {
                // State moved on (VSC/green/etc.) without the normal
                // "in this lap" hand-off - send it home defensively rather than
                // leaving it circulating with nothing driving it into the pits.
                safetyCarController.BeginPitReturn();
            }

            // One-shot heads-up as the player first closes on the physical queue.
            if (!playerScQueueWarningSent)
            {
                float playerGap = PlayerGapToSafetyCarMeters();
                if (playerGap >= 0f && playerGap < 150f)
                {
                    playerScQueueWarningSent = true;
                    if (Settings != null && Settings.Current.raceControlMessages)
                    {
                        PostEngineerMessage("Safety car queue ahead - slow down and hold your gap.", true);
                    }
                }
            }

            CheckSafetyCarOvertakes();
        }

        bool playerScQueueWarningSent;

        // Single per-frame update of the shared "virtual convoy" reference that
        // BuildRaceControlAutopilotCommand steers/paces every queued car against.
        // While the real safety car is physically active it just mirrors the real
        // car (unchanged behaviour from before); once the car has peeled off
        // toward the pits but the field is still held (Restart, or the short
        // green-flag ramp after), the reference keeps advancing on its own,
        // ramping from the last real convoy speed up toward a brisk restart pace
        // - so the queue never stalls waiting on an object that isn't there
        // anymore, and BuildRaceControlAutopilotCommand never has to fall back to
        // a blind "brake and go straight" command during that window.
        void UpdateRaceControlReference()
        {
            bool scPhysicallyActive = safetyCarController != null && safetyCarController.IsActive;
            if (scPhysicallyActive)
            {
                raceControlReferenceDistance = Track != null
                    ? Track.WrapDistance(safetyCarController.ProgressDistance - RaceControlQueueLeadMeters)
                    : safetyCarController.ProgressDistance - RaceControlQueueLeadMeters;
                raceControlReferenceSpeedKph = safetyCarController.CurrentSpeedKph;
                return;
            }

            if (!IsRaceControlAutopilotHoldPeriod || Track == null)
            {
                return;
            }

            // RedFlagged cars are held by BuildRedFlagHoldCommand (a direct
            // brake-to-stop, not the moving-queue-target model below), so the
            // shared reference has nothing useful to advance toward here - just
            // pin its speed at 0 so the eventual RedFlagged -> Restart handoff
            // (see DriveRaceControlStateMachine) starts its ramp from a genuine
            // standstill instead of whatever stale convoy speed preceded it.
            if (CurrentRaceControlState == RaceControlState.RedFlagged)
            {
                raceControlReferenceSpeedKph = 0f;
                return;
            }

            // Only actually ramp the pace up once race control has called the
            // restart (Restart state) or is in the short Green ramp tail right
            // after it. The SafetyCarInThisLap tail - where the physical car has
            // already peeled off but race control hasn't called "go" yet - holds
            // the last known convoy pace flat instead of creeping toward restart
            // speed early.
            float targetSpeedKph = raceControlReferenceSpeedKph;
            if (CurrentRaceControlState == RaceControlState.Restart)
            {
                float rampProgress = 1f - Mathf.Clamp01(restartControlTimer / 4f);
                targetSpeedKph = Mathf.Lerp(raceControlReferenceSpeedKph, RestartFormationTargetSpeedKph, rampProgress);
            }
            else if (CurrentRaceControlState == RaceControlState.Green && restartRampTimer > 0f)
            {
                float rampProgress = 1f - Mathf.Clamp01(restartRampTimer / RestartRampDurationSeconds);
                targetSpeedKph = Mathf.Lerp(raceControlReferenceSpeedKph, RestartFormationTargetSpeedKph, rampProgress);
            }

            // Restart-acceleration buff: the shared convoy ramp every car under
            // race-control autopilot follows (SC/VSC restart + the green-flag
            // ramp tail) - raised again to 90 kph/s (2x the earlier 45) per
            // request, so the field gets back up to speed off a caution hard.
            raceControlReferenceSpeedKph = Mathf.MoveTowards(raceControlReferenceSpeedKph, targetSpeedKph, Time.deltaTime * 90f);
            raceControlReferenceDistance = Track.WrapDistance(raceControlReferenceDistance + raceControlReferenceSpeedKph / 3.6f * Time.deltaTime);
        }

        void RespawnMissingSafetyCar(string reason)
        {
            if (safetyCarController == null || safetyCarObject == null)
            {
                safetyCarObject = null;
                safetyCarController = null;
                EnsureSafetyCarBuilt();
            }

            if (safetyCarController == null)
            {
                GameLog.Warn("[RaceControl] Safety car respawn FAILED: controller could not be rebuilt. reason=" + reason);
                return;
            }

            List<RaceParticipant> order = GetRunningOrderSnapshot();
            RaceParticipant leader = order.Count > 0 ? order[0] : safetyCarQueueLeader;
            float leaderDistance = leader != null && State != null ? State.GetProgressDistance(leader) : 0f;
            safetyCarController.EnterTrack(Track != null ? Track.WrapDistance(leaderDistance + 60f) : leaderDistance + 60f);
            GameLog.Warn("[RaceControl] Safety car respawned (last resort). reason=" + reason + " respawnCountThisPeriod=" + safetyCarWatchdogRespawnCount);
            LogSafetyCarSpawnState("watchdog-respawn");
        }

        void LogSafetyCarSpawnState(string context)
        {
            if (safetyCarController == null || safetyCarObject == null)
            {
                GameLog.Warn("[RaceControl] SC spawn (" + context + "): controller/object is null.");
                return;
            }

            Renderer[] renderers = safetyCarObject.GetComponentsInChildren<Renderer>(true);
            float leaderDistance = safetyCarQueueLeader != null && State != null ? State.GetProgressDistance(safetyCarQueueLeader) : -1f;
            GameLog.Info("[RaceControl] SC spawn (" + context + "): state=" + CurrentRaceControlState +
                " active=" + safetyCarObject.activeInHierarchy +
                " controllerActive=" + safetyCarController.IsActive +
                " spawnDistance=" + safetyCarController.ProgressDistance.ToString("0") +
                " worldPos=" + safetyCarObject.transform.position +
                " leaderDistance=" + leaderDistance.ToString("0") +
                " scSpeed=" + safetyCarController.CurrentSpeedKph.ToString("0") +
                " renderers=" + renderers.Length);
        }

        // HUD support (Part 4): live gap from the player to the safety car when
        // the player is in the forming/formed queue; negative when not relevant.
        public float PlayerGapToSafetyCarMeters()
        {
            if (safetyCarController == null || !safetyCarController.IsActive || PlayerParticipant == null || State == null || Track == null)
            {
                return -1f;
            }

            float gap = Track.WrapDistance(safetyCarController.ProgressDistance - State.GetProgressDistance(PlayerParticipant));
            return gap < 400f ? gap : -1f;
        }

        // Passing the actual safety car (Part 3): a transition-based check, same
        // shape as CheckIllegalOvertakesUnderYellow - only fires the instant a
        // car's progress distance crosses from behind the safety car to ahead of
        // it, not on every tick it happens to already be ahead (lapped traffic a
        // full circuit away from the queue must never trip this).
        void CheckSafetyCarOvertakes()
        {
            if (State == null || Track == null)
            {
                return;
            }

            float safetyCarDistance = safetyCarController.ProgressDistance;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                // A car under race-control's own convoy autopilot can never
                // "illegally" pass the safety car - its movement is race
                // control's own doing, not a driver decision to penalize.
                if (participant == null || participant.vehicle == null || participant.retired || participant.finished ||
                    participant.isPitting || participant.pitPhase != PitPhase.None || participant.isRaceControlAutopilot)
                {
                    aheadOfSafetyCarLastTick.Remove(participant);
                    continue;
                }

                float gap = Track.WrapDistance(State.GetProgressDistance(participant) - safetyCarDistance);
                bool aheadNow = gap > 3f && gap < 250f;
                bool wasAhead = aheadOfSafetyCarLastTick.Contains(participant);
                if (aheadNow && !wasAhead)
                {
                    AddPenalty(participant, 5f, "Passed the safety car");
                    GameLog.Warn("[RaceControl] " + participant.driverName + " illegally passed the safety car (+5s).");
                    if (participant.isPlayer)
                    {
                        SessionMessage = "Passed the safety car: +5s";
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("You passed the safety car - that's a penalty, fall back into line.", true);
                        }
                    }
                }

                if (aheadNow)
                {
                    aheadOfSafetyCarLastTick.Add(participant);
                }
                else
                {
                    aheadOfSafetyCarLastTick.Remove(participant);
                }
            }
        }

        // Public read-only surface (Part 1/2/3): AI traffic-avoidance treats the
        // safety car as a real obstacle to queue behind, and the HUD shows a
        // distinct "follow the safety car" state, both without needing their own
        // copy of the deployment/despawn bookkeeping above.
        public Transform SafetyCarTransform { get { return safetyCarController != null && safetyCarController.IsActive ? safetyCarController.transform : null; } }
        public bool IsSafetyCarOnTrack { get { return safetyCarController != null && safetyCarController.IsActive; } }
        public float SafetyCarCurrentSpeedKph { get { return safetyCarController != null ? safetyCarController.CurrentSpeedKph : 0f; } }

        // Full safety car convoy autopilot (Part 2). True only while there's an
        // actual, active safety car object on track during a full SC period -
        // HUD and callers use this instead of IsFullSafetyCarPeriod alone so a
        // brief window where the controller/object hasn't finished spawning yet
        // never shows "autopilot active" with nothing physically driving it.
        public bool IsSafetyCarConvoyActive { get { return IsFullSafetyCarPeriod && safetyCarController != null && safetyCarController.IsActive; } }

        // HUD readouts (Part 10): 1-based queue position (0/-1 when not queued)
        // and live gap-to-target-slot in meters, both driven off the same state
        // BuildRaceControlAutopilotCommand itself uses every tick.
        public int PlayerSafetyCarQueuePosition
        {
            get { return PlayerParticipant != null && PlayerParticipant.safetyCarQueueIndex >= 0 ? PlayerParticipant.safetyCarQueueIndex + 1 : -1; }
        }

        public float PlayerSafetyCarGapToTargetMeters
        {
            get
            {
                if (PlayerParticipant == null || State == null || Track == null || !PlayerParticipant.isRaceControlAutopilot)
                {
                    return -1f;
                }

                float signed = Track.WrapDistance(PlayerParticipant.safetyCarTargetDistance - State.GetProgressDistance(PlayerParticipant));
                return signed > Track.length * 0.5f ? signed - Track.length : signed;
            }
        }

        // Convoy autopilot driving command (Part 2): builds a full throttle/
        // brake/steer command that drives `participant` toward its assigned
        // slot in the queue - the leader targets a fixed distance behind the
        // shared race-control reference point, everyone else targets a further
        // fixed distance behind the leader's own slot, scaled by their frozen
        // queue index. Steering mirrors the same lookahead-point pattern
        // AiVehicleController's own normal driving uses (SampleAtDistance ahead
        // of the car's current position, steer toward it in local space) so the
        // convoy still tracks the racing line through corners instead of
        // cutting straight lines between distance samples; only the speed
        // target comes from the queue-slot error, not from the normal apex/
        // braking-point model. The reference point itself (raceControlReferenceDistance/
        // Speed, kept current every frame by UpdateRaceControlReference) mirrors
        // the real safety car while it's physically on track and keeps advancing
        // smoothly on its own through the Restart hold and green-flag ramp once
        // the car has already peeled into the pits - so this command always has
        // a real target to steer/pace toward and never falls back to a blind
        // brake-and-go-straight command while a car is still under autopilot.
        public VehicleCommand BuildRaceControlAutopilotCommand(RaceParticipant participant)
        {
            VehicleCommand command = new VehicleCommand();
            if (participant == null || participant.vehicle == null || Track == null || State == null)
            {
                command.brake = 1f;
                return command;
            }

            if (!IsRaceControlAutopilotHoldPeriod)
            {
                // Shouldn't normally be called outside a hold period - hold
                // speed down gently rather than doing anything erratic.
                command.throttle = 0f;
                command.brake = 0.15f;
                return command;
            }

            // Red flag: every car brakes smoothly to a stop roughly where it is,
            // rather than chasing a moving queue-slot target - deliberately
            // simpler than the safety-car convoy model below (real grid-
            // reformation is out of scope) but still safe: gentle, speed-scaled
            // braking with a light steering correction back toward the
            // centerline so a braking car doesn't wander off-line or into
            // another car's path while it slows.
            if (CurrentRaceControlState == RaceControlState.RedFlagged)
            {
                return BuildRedFlagHoldCommand(participant);
            }

            float scSpeedKph = raceControlReferenceSpeedKph;
            // Gap scales slightly with pace: a faster-moving queue needs a
            // little more following distance per car than a crawling one.
            float gapPerCar = Mathf.Lerp(14f, 22f, Mathf.Clamp01(scSpeedKph / 160f));
            int queueIndex = Mathf.Max(0, participant.safetyCarQueueIndex);
            float targetDistance = Track.WrapDistance(raceControlReferenceDistance - queueIndex * gapPerCar);
            participant.safetyCarTargetDistance = targetDistance;

            float ownDistance = State.GetProgressDistance(participant);
            // Signed distance from us to our slot: positive means the slot is
            // still ahead of us (behind on pace, catch up); negative means we've
            // already reached/overshot it (too close/ahead, back off).
            float rawGap = Track.WrapDistance(targetDistance - ownDistance);
            float signedSlotError = rawGap > Track.length * 0.5f ? rawGap - Track.length : rawGap;

            float mySpeedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);
            // Proportional speed target around the safety car's own pace: ahead
            // of slot -> brake back toward it, behind -> close in gently. Capped
            // well short of racing pace so a car that fell a long way back
            // during the deployment doesn't come storming up on the queue.
            float speedAdjustKph = Mathf.Clamp(signedSlotError * 1.2f, -45f, 25f);
            float targetSpeedKph = Mathf.Clamp(scSpeedKph + speedAdjustKph, 0f, scSpeedKph + 25f);

            float speedGapKph = targetSpeedKph - mySpeedKph;
            if (speedGapKph < -3f)
            {
                command.brake = Mathf.Clamp01(-speedGapKph / 40f);
                command.throttle = 0f;
            }
            else
            {
                command.brake = 0f;
                command.throttle = Mathf.Clamp01(0.15f + speedGapKph / 40f);
            }

            // Steering: same lookahead-sample-and-steer-toward-it pattern the AI's
            // own normal driving uses, just always aimed at the centerline (no
            // racing-line offset - a convoy holds station, it doesn't need to
            // find the fastest line through a corner).
            float lookAhead = Mathf.Lerp(14f, 38f, Mathf.Clamp01(mySpeedKph / 150f));
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Track.SampleAtDistance(Track.WrapDistance(ownDistance + lookAhead), out point, out forward, out right);
            Vector3 toTarget = point - participant.transform.position;
            float localSteer = Vector3.Dot(toTarget.normalized, participant.transform.right);
            command.steer = Mathf.Clamp(localSteer * 2.2f, -1f, 1f);

            return command;
        }

        // Red flag hold: brake smoothly to a stop in place (not toward a moving
        // queue slot) and hold there, with a light steering correction back
        // toward the centerline. Braking is speed-scaled and capped well short
        // of a full stab so a queue of cars slowing down together doesn't brake-
        // check itself into the very kind of pileup a red flag exists to stop.
        VehicleCommand BuildRedFlagHoldCommand(RaceParticipant participant)
        {
            VehicleCommand command = new VehicleCommand();
            float speedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);
            command.throttle = 0f;
            command.brake = speedKph > 3f ? Mathf.Lerp(0.22f, 0.5f, Mathf.Clamp01(speedKph / 140f)) : 0.35f;

            TrackProgress progress = State.GetCurrentProgress(participant);
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Track.SampleAtDistance(Track.WrapDistance(progress.distance + 16f), out point, out forward, out right);
            Vector3 toTarget = point - participant.transform.position;
            float localSteer = Vector3.Dot(toTarget.normalized, participant.transform.right);
            command.steer = Mathf.Clamp(localSteer * 1.6f, -1f, 1f);
            return command;
        }

        // The actual "grid reset" step of the red-flag procedure: places every
        // still-running car back onto the starting-grid slots in exactly the
        // running order frozen the instant the flag was thrown (redFlagGridOrder -
        // never the original race-start grid, never re-sorted/randomized here).
        // Uses the same position/rotation math as the initial grid spawn
        // (Track.GetGridSlot + SampleAtDistance) and the same safe teleport
        // primitive pit stops already rely on (VehicleController.SnapToPitPose)
        // so the physics body, transform and internal throttle/brake smoothing
        // all move together with zero residual velocity - no floating, no drift.
        void TeleportFieldToRedFlagGrid()
        {
            if (redFlagGridTeleportDone || Track == null)
            {
                return;
            }

            redFlagGridTeleportDone = true;
            int slot = 0;
            for (int i = 0; i < redFlagGridOrder.Count; i++)
            {
                RaceParticipant participant = redFlagGridOrder[i];
                if (participant == null || participant.retired || participant.finished || participant.vehicle == null)
                {
                    continue;
                }

                float gridDistance;
                float lane;
                Track.GetGridSlot(slot, out gridDistance, out lane);
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Track.SampleAtDistance(gridDistance, out point, out forward, out right);
                Vector3 targetPosition = FindRoadSpawnPosition(point + right * lane, participant.driverName, out bool hitRoad);
                Quaternion targetRotation = Quaternion.LookRotation(forward, Vector3.up);

                // A car mid-pit-stop when the flag fell resumes fresh from its
                // grid slot rather than continuing a pit animation that no
                // longer matches its position.
                if (participant.pitPhase != PitPhase.None)
                {
                    CancelPitSequenceForRedFlag(participant);
                }

                participant.vehicle.SnapToPitPose(targetPosition, targetRotation);
                participant.hasLastSafePosition = true;
                participant.lastSafePosition = targetPosition;
                participant.lastSafeRotation = targetRotation;
                participant.gridPosition = slot + 1;
                participant.safetyCarQueueIndex = slot;
                participant.preSafetyCarOrderIndex = slot;
                participant.stoppedOnTrackTimer = 0f;
                participant.wrongWayTimer = 0f;
                participant.recoveryAttemptCount = 0;
                participant.stuckRepositionCooldown = 0f;

                AiVehicleController ai = participant.GetComponent<AiVehicleController>();
                if (ai != null)
                {
                    ai.ResyncAfterForcedReposition();
                }

                // Re-anchors the lap/checkpoint tracker to the new physical
                // position exactly like an initial grid start does - resets
                // the checkpoint bookkeeping and progress reference so the
                // teleport can never be misread as skipped checkpoints or a
                // phantom lap, while leaving CompletedLaps untouched (the
                // remaining-laps count is never reset by a red flag).
                if (participant.lapTracker != null)
                {
                    participant.lapTracker.ConfigureRaceGridStart(gridDistance);
                }

                if (State != null)
                {
                    State.RefreshTimingSnapshot(participant);
                }

                slot++;
            }

            GameLog.Info("[RaceControl] Field teleported to red-flag restart grid (" + slot + " cars, order preserved from the moment the flag was thrown).");
            LogRaceControlHistory("GRID RESET", "Field repositioned to the running order recorded when the red flag was thrown");
        }

        void CancelPitSequenceForRedFlag(RaceParticipant participant)
        {
            if (participant == null)
            {
                return;
            }

            participant.pitPhase = PitPhase.None;
            participant.isPitting = false;
            participant.hasPitGuideState = false;
            participant.pitLimiterUntilExit = false;
            participant.pitAwaitingRelease = false;
            participant.pitLaneHeldByOccupancy = false;
            participant.pitEntryCommitted = false;
            if (participant.vehicle != null)
            {
                participant.vehicle.SetPitGuidance(false);
                participant.vehicle.SetPitServiceHold(false);
                participant.vehicle.SetPitLimiter(false);
                participant.vehicle.SetPitExitFastLimiter(false);
            }
        }

        // Ticks the active race-control state forward, including the safety-car
        // period's scripted restart chain (Active -> in this lap -> restart -> green).
        void DriveRaceControlStateMachine()
        {
            switch (CurrentRaceControlState)
            {
                case RaceControlState.Green:
                case RaceControlState.YellowSector:
                    IsPitLaneOpen = true;
                    // Green-flag ramp tail: for a short window after a safety-car
                    // restart, cars are still held under race-control autopilot
                    // (see IsRaceControlAutopilotHoldPeriod) even though the state
                    // has already flipped to Green, so the handback to normal
                    // driving is a controlled ramp rather than an instant snap.
                    if (restartRampTimer > 0f)
                    {
                        restartRampTimer -= Time.deltaTime;
                        if (restartRampTimer <= 0f && !restartHandbackMessageSent)
                        {
                            restartHandbackMessageSent = true;
                            if (Settings != null && Settings.Current.raceControlMessages)
                            {
                                PostEngineerMessage("Full control's back with you - send it.", false);
                            }
                        }
                    }
                    break;

                case RaceControlState.VirtualSafetyCar:
                    IsPitLaneOpen = true;
                    MaybePromptPlayerScPit();
                    safetyCarTimer -= Time.deltaTime;
                    if (safetyCarTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.Green;
                        postEscalationCooldownTimer = PostEscalationCooldownSeconds;
                        GameLog.Info("[RaceControl] VSC ending, green flag.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("VSC ending, green flag.", true, RaceAudioCue.Green);
                        }
                    }
                    break;

                case RaceControlState.SafetyCarDeploying:
                    IsPitLaneOpen = true;
                    MaybePromptPlayerScPit();
                    safetyCarTimer -= Time.deltaTime;
                    if (safetyCarTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.SafetyCarActive;
                        safetyCarTimer = Random.Range(28f, 55f);
                        GameLog.Info("[RaceControl] Safety car now active on track. period=" + safetyCarTimer.ToString("0.0") + "s");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Safety car is now in position.", true);
                        }
                    }
                    break;

                case RaceControlState.SafetyCarActive:
                    IsPitLaneOpen = true;
                    MaybePromptPlayerScPit();
                    safetyCarTimer -= Time.deltaTime;
                    if (safetyCarTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.SafetyCarInThisLap;
                        safetyCarInThisLapMessageSent = false;
                        coldTyresRestartWarningSent = false;
                    }
                    break;

                case RaceControlState.SafetyCarInThisLap:
                    if (!safetyCarInThisLapMessageSent)
                    {
                        safetyCarInThisLapMessageSent = true;
                        restartControlTimer = 12f;
                        GameLog.Info("[RaceControl] Safety car in this lap, preparing restart.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Safety car in this lap.", true);
                            PostEngineerMessage("Safety car in this lap, control will return at restart.", false);
                        }

                        if (safetyCarController != null)
                        {
                            safetyCarController.BeginPitReturn();
                        }
                    }

                    restartControlTimer -= Time.deltaTime;
                    // Part C.2: a one-shot cold tyres/brakes warning partway through
                    // the "in this lap" window so it doesn't collide with the message
                    // right above it.
                    if (!coldTyresRestartWarningSent && restartControlTimer <= 8f)
                    {
                        coldTyresRestartWarningSent = true;
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Tyres will be cold at the restart, be careful on the first lap.", false);
                        }
                    }

                    if (restartControlTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.Restart;
                        RestartFollowsRedFlag = false;
                        restartControlTimer = 4f;
                        GameLog.Info("[RaceControl] Restart imminent.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Green flag imminent, get ready.", true, RaceAudioCue.Green);
                        }
                    }
                    break;

                case RaceControlState.Restart:
                    restartControlTimer -= Time.deltaTime;
                    // Restart countdown lights/audio: the same 5-light build-up
                    // tone sequence the original race start uses (PlayStartLight),
                    // over the final 5 seconds of the hold - a red flag or safety
                    // car restart used to be silent/text-only right up to the
                    // green flag, unlike the original start.
                    if (restartControlTimer <= 5f)
                    {
                        int lightsRemaining = Mathf.Clamp(Mathf.CeilToInt(restartControlTimer), 0, 5);
                        if (lightsRemaining != lastRestartLightCountPlayed)
                        {
                            lastRestartLightCountPlayed = lightsRemaining;
                            if (lightsRemaining > 0)
                            {
                                SimpleAudioManager.PlayStartLight(5 - lightsRemaining);
                            }
                        }
                    }

                    if (restartControlTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.Green;
                        drsRestartCooldownTimer = 45f;
                        postEscalationCooldownTimer = PostEscalationCooldownSeconds;
                        playerScPitPromptSent = false;
                        safetyCarQueueLeader = null;
                        lastRestartLightCountPlayed = -1;
                        SimpleAudioManager.PlayStartLight(5);
                        if (RestartFollowsRedFlag)
                        {
                            // Standing-restart fix: a red-flag restart is a real
                            // standing start, not a rolling one - the moment the
                            // lights go out, control returns immediately and in
                            // full, exactly like the original grid start
                            // (HoldGridCars(false) there does the same thing).
                            // No autopilot ramp: that mechanism exists to bring a
                            // moving safety-car convoy back up to racing pace
                            // together, which doesn't apply to cars launching
                            // individually from a dead stop.
                            HoldGridCars(false);
                            restartRampTimer = 0f;
                            restartHandbackMessageSent = true;
                            LogRaceControlHistory("RACE RESTART", "Standing restart - green flag from the red-flag grid, running order preserved");
                            if (Settings != null && Settings.Current.raceControlMessages)
                            {
                                PostEngineerMessage("Lights out - race restart, go go go!", true, RaceAudioCue.Green);
                            }
                        }
                        else
                        {
                            // Autopilot doesn't let go the instant Green fires - it
                            // keeps holding/pacing the field for a short ramp so every
                            // car accelerates away together instead of the whole
                            // grid snapping to full racing behaviour on the same frame.
                            // restartHandbackMessageSent fires the honest "control's
                            // yours now" message once that ramp actually finishes.
                            restartRampTimer = RestartRampDurationSeconds;
                            restartHandbackMessageSent = false;
                            LogRaceControlHistory("GREEN FLAG", "Restart after safety car");
                            if (Settings != null && Settings.Current.raceControlMessages)
                            {
                                PostEngineerMessage("Green flag - power builds back progressively, hold your line.", true, RaceAudioCue.Green);
                            }
                        }

                        GameLog.Info("[RaceControl] Restart complete, green flag.");
                    }
                    break;

                case RaceControlState.RedFlagged:
                    // Pit lane stays open through a red flag (real F1 uses it as
                    // the "return to the pits" option this simplified model
                    // deliberately leans on instead of a full grid-lane
                    // reformation) so a car needing repairs can route there
                    // while the field is held.
                    IsPitLaneOpen = true;
                    redFlagTimer -= Time.deltaTime;
                    if (redFlagTimer <= 0f)
                    {
                        // The actual grid-reset step: every still-running car is
                        // teleported back onto the starting grid in the exact
                        // running order frozen the instant the flag was thrown
                        // (redFlagGridOrder - see BeginRedFlag/TeleportFieldToRedFlagGrid).
                        // Never the original race-start grid, never re-sorted.
                        TeleportFieldToRedFlagGrid();

                        // Standing-restart fix: this state used to hand the field
                        // straight to the safety-car convoy-chase autopilot
                        // (BuildRaceControlAutopilotCommand) for the whole hold,
                        // which drives cars toward a moving queue-slot target - a
                        // rolling-start mechanism, not a standing one, and the
                        // reason a red-flag restart could read as "already
                        // moving" and let cars drift off their exact teleported
                        // slot before the green flag. HoldGridCars(true) uses the
                        // exact same hard physics freeze (VehicleController.
                        // SetGridHold, position/rotation pinned every FixedUpdate)
                        // the original grid start already relies on, so cars are
                        // genuinely stationary - not merely braking toward zero -
                        // for the whole restart hold. Released in the Restart ->
                        // Green transition below, only for a red-flag-originated
                        // restart; the safety-car restart path is untouched.
                        HoldGridCars(true);

                        float gridDistance;
                        float lane;
                        Track.GetGridSlot(0, out gridDistance, out lane);
                        raceControlReferenceDistance = gridDistance;
                        raceControlReferenceSpeedKph = 0f;
                        CurrentRaceControlState = RaceControlState.Restart;
                        RestartFollowsRedFlag = true;
                        restartControlTimer = 5f;
                        GameLog.Info("[RaceControl] Grid reset complete, restart in 5 seconds.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Grid reset based on the running order when the flag came out.", true);
                            PostEngineerMessage("Hold your grid slot - race restart in 5 seconds.", true, RaceAudioCue.Green);
                        }
                    }
                    break;
            }
        }

        void MaybePromptPlayerScPit()
        {
            if (playerScPitPromptSent || PlayerParticipant == null)
            {
                return;
            }

            if (!ShouldAiPitUnderSafetyCar(PlayerParticipant))
            {
                return;
            }

            playerScPitPromptSent = true;
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                // Feature: the prompt now reads correctly for whichever period is
                // actually active, and gives a rough strategic delta rather than a
                // generic "reduced time loss" line reused for both - a full SC
                // convoy is nearly free to pit into (the whole field is crawling)
                // while a VSC still costs the fixed pit-lane time-loss delta
                // relative to the reduced-pace field outside.
                bool fullSc = CurrentRaceControlState == RaceControlState.SafetyCarActive || CurrentRaceControlState == RaceControlState.SafetyCarDeploying;
                string message = fullSc
                    ? "Safety car deployed. Box now - the field is bunched, this is close to a free stop. Press P to box now or stay out to keep Plan A."
                    : "VSC deployed. Box now - the delta is much smaller than a green-flag stop. Press P to box now or stay out to keep Plan A.";
                PostEngineerMessage(message, true);

                // Interactive pit-window offer: this radio call is now something the
                // player actually answers rather than a passive heads-up - pressing P
                // in the next few seconds accepts the opportunistic stop and overrides
                // the original planned lap; staying silent leaves the plan untouched
                // (see AcceptRaceControlPitOffer/UpdatePlayerRaceControlPitOffer).
                playerHasActiveRaceControlPitOffer = true;
                playerRaceControlPitOfferExpiresAt = Time.time + 10f;
                playerRaceControlPitOfferType = fullSc ? RaceControlPitOfferType.SafetyCar : RaceControlPitOfferType.Vsc;
                playerDeclinedRaceControlPitOfferMessageSent = false;
            }
        }

        // Cancels/expires the VSC/SC pit-window offer once it's no longer valid -
        // race control period ended, offer timed out, the pit lane closed, or the
        // player is already pitting/retired/finished/has an outstanding pit request
        // of their own. Silent on expiry-with-no-input per spec (staying out is a
        // valid, intentional choice, not an error state) beyond a single optional
        // "staying out" acknowledgement.
        void UpdatePlayerRaceControlPitOffer()
        {
            if (!playerHasActiveRaceControlPitOffer)
            {
                return;
            }

            if (PlayerParticipant == null || PlayerParticipant.vehicle == null || PlayerParticipant.lapTracker == null ||
                PlayerParticipant.retired || PlayerParticipant.finished || PlayerParticipant.isPitting ||
                PlayerParticipant.pitPhase != PitPhase.None || PlayerParticipant.vehicle.PitRequested)
            {
                playerHasActiveRaceControlPitOffer = false;
                return;
            }

            bool stillUnderRaceControl = CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                                          CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                                          CurrentRaceControlState == RaceControlState.SafetyCarDeploying;
            bool expired = Time.time >= playerRaceControlPitOfferExpiresAt;
            if (!stillUnderRaceControl || expired || !IsPitLaneOpen)
            {
                playerHasActiveRaceControlPitOffer = false;
                if (expired && stillUnderRaceControl && !playerDeclinedRaceControlPitOfferMessageSent &&
                    Settings != null && Settings.Current.raceControlMessages)
                {
                    playerDeclinedRaceControlPitOfferMessageSent = true;
                    PostEngineerMessage("Okay, staying out. Plan A remains.", false);
                }
            }
        }

        // Player-facing gate PlayerVehicleInput checks before routing a P press to
        // AcceptRaceControlPitOffer instead of the normal pit-request toggle.
        public bool HasActiveRaceControlPitOfferForPlayer { get { return playerHasActiveRaceControlPitOffer; } }

        // Pressing P while a VSC/SC pit-window offer is active: box immediately
        // under the current window, using actual existing fields (vehicle.RequestPit
        // + requestedPitCompound/requestedPitCompoundSet, the same pair
        // UpdatePlayerAutoPitStrategy already uses for an automatic planned stop).
        // No separate "override" fields are needed to replace the original planned
        // lap - NextPlannedPitLapFor already resolves off participant.pitStops, so
        // once this stop completes and pitStops increments, the plan naturally moves
        // on to the next still-pending stop (or stops prompting entirely on a
        // one-stop plan) instead of firing again at the original lap.
        public void AcceptRaceControlPitOffer()
        {
            if (!PitRequestRules.CanAcceptRaceControlOffer(
                    playerHasActiveRaceControlPitOffer,
                    PlayerParticipant != null && PlayerParticipant.missedPitEntryThisLap))
            {
                return;
            }

            if (PlayerParticipant == null || PlayerParticipant.vehicle == null)
            {
                return;
            }

            if (PlayerParticipant.missedPitEntryThisLap)
            {
                // Deterministic-deadlock fix: the real physical opening is already
                // closed for this lap - re-requesting now would just re-arm
                // committingToPit while still inside the broad approach zone.
                return;
            }

            RaceControlPitOfferType offerType = playerRaceControlPitOfferType;
            playerHasActiveRaceControlPitOffer = false;

            PlayerParticipant.vehicle.RequestPit();
            PlayerParticipant.pitAutoTriggered = false;
            // Cancellable-manual-pit-stop fix: an accepted SC/VSC radio offer is
            // still a manually-initiated stop, not the pre-race plan - tagged
            // with its own source (rather than reusing Manual) so future
            // messaging/analytics can tell the two apart, but it is cancellable
            // through the exact same window (CanCancelManualPitRequest treats
            // Manual and SafetyCarPrompt identically).
            PlayerParticipant.activePitRequestSource = PitRequestSource.SafetyCarPrompt;
            PlayerParticipant.manualPitRequested = true;
            PlayerParticipant.manualPitCommitted = false;
            GameEvents.Publish(new PitRequestChangedEvent(PlayerParticipant.driverId, PitRequestState.Requested, -1));
            if (!PlayerParticipant.requestedPitCompoundSet)
            {
                PlayerParticipant.requestedPitCompound = NextPlannedPitCompoundFor(PlayerParticipant);
                PlayerParticipant.requestedPitCompoundSet = true;
            }

            string offerName = offerType == RaceControlPitOfferType.SafetyCar ? "the safety car" : "the VSC";
            SessionMessage = "Pit request: box under " + offerName;
            GameLog.Info("[Pit] Player accepted race-control pit offer (" + offerType + ") at lap " +
                         (PlayerParticipant.lapTracker != null ? PlayerParticipant.lapTracker.CompletedLaps + 1 : 0) + ".");
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Copy. Box this lap under " + offerName + ". Pit confirm.", true, RaceAudioCue.PitCall);
            }
        }
    }
}
