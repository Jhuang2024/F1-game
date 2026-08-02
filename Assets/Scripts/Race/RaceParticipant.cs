using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    public enum PitPhase
    {
        None,
        Entry,
        Service,
        Release,
        // Pit-exit path fix: the car has reached the release pose but is still
        // guided (not handed back to normal racing-line targeting) along the
        // pit-exit lane until it genuinely clears the merge point - see
        // RaceManager.UpdatePitExitMerge. Path discipline (this phase) is
        // deliberately separate from speed discipline (participant.pitLimiterUntilExit,
        // which already clears independently once Track.IsInPitExitLimiterZone
        // goes false) - a car can legally be at racing speed while still
        // physically confined to the pit-exit lane for a few more metres.
        ExitMerge,
        QualifyingReturn
    }

    // Cancellable-manual-pit-stop fix: vehicle.PitRequested is a plain bool with
    // no memory of WHY it was set - manual P key, an accepted safety-car/VSC
    // radio offer, or the pre-race strategy's own auto-trigger all looked
    // identical once latched. That made it impossible to build a "cancel my
    // manual request" feature that could never accidentally cancel/interfere
    // with the pre-race plan. This tags every RequestPit() call with its real
    // source; see RaceManager.CanCancelManualPitRequest/CancelManualPitRequest.
    public enum PitRequestSource
    {
        None,
        PreRacePlan,
        Manual,
        SafetyCarPrompt
    }

    // Race-control recovery classification (RaceManager.DetectIncidents). Only
    // ActuallyStranded may ever escalate into a yellow/VSC/SC incident - every
    // other state describes a car that is slow/stopped for a legitimate reason
    // and should be left alone.
    public enum CarRecoveryState
    {
        Normal,
        Recovering,
        Queued,
        PitSequence,
        RaceControlPacing,
        ActuallyStranded
    }

    public class RaceParticipant : MonoBehaviour
    {
        public string driverId;
        public string driverName;
        public string teamId;
        public string teamShortName;
        public bool isPlayer;
        public bool finished;
        public bool retired;
        public string retirementReason = "";
        public int finishingPosition;
        public int gridPosition;
        public float finishTime;
        public float penaltiesSeconds;
        public string penaltyReason = "";
        public int trackLimitWarnings;
        public float offTrackTimer;
        // Track-limit stewarding depth: a short, capped log of individual
        // events ("Lap 4 - Sector 2") rather than just the running warning
        // count, so the post-race report can list exactly where/when each
        // one happened instead of only a bare total. Capped (see
        // RaceManager.HandleTrackLimits) since a long race with a persistent
        // offender could otherwise grow this unbounded.
        public List<string> trackLimitEventLog = new List<string>();
        // Cornering telemetry (post-race "where did I gain/lose time" report):
        // one running speed-sum/reference-sum/sample-count triple per
        // TrackRuntime.CornerRisk tier (Low/Medium/High), fed by
        // RaceManager.SampleCorneringTelemetry once per corner per lap.
        // Deliberately a few small running sums rather than a growing
        // per-corner-instance log, so this stays cheap over a long race with
        // many corners and many laps.
        public readonly float[] cornerSpeedSumByRisk = new float[3];
        public readonly float[] cornerReferenceSumByRisk = new float[3];
        public readonly int[] cornerSampleCountByRisk = new int[3];
        // Which corner (by its peak distance, rounded) was most recently
        // sampled, so a car lingering near the same corner for several ticks
        // (slow traffic, recovery) is only ever counted once.
        float lastTelemetryCornerDistance = -9999f;
        public float LastTelemetryCornerDistance { get { return lastTelemetryCornerDistance; } set { lastTelemetryCornerDistance = value; } }
        public float startReactionDelay;
        // Rolled once at spawn (StartProcedureRules.AiJumpStartChance): when
        // > 0, this AI car physically launches this many seconds BEFORE
        // lights-out and takes the jump-start penalty. 0 = clean start.
        public float aiJumpStartWindowSeconds;
        public bool jumpStartPenaltyApplied;
        // Blue-flag state (RaceManager.UpdateBlueFlags): shown while a lapping
        // car is close behind; held-seconds accumulate toward the compliance
        // penalty and reset once the episode clears.
        public bool blueFlagShown;
        public float blueFlagHeldSeconds;
        public float blueFlagLingerTimer;
        public bool blueFlagPenaltyApplied;
        public bool isPitting;
        public PitPhase pitPhase;
        // Pit-lane architecture fix: separates "the team wants to box"
        // (vehicle.PitRequested) from "the car has physically moved onto the pit-
        // entry ramp and may enter the guided pit sequence" (pitEntryCommitted).
        // BeginPitEntry now only ever fires once the car is genuinely on the ramp
        // (see Track.IsOnPitEntryRamp) - no more forced/fake commits from the
        // racing surface. missedPitEntryThisLap records a car that ran out of
        // entry-zone road without physically committing (never teleported into
        // the pits - it just stays out and retries at the next pit window).
        // pitRequestLapNumber is the display-lap the request was raised on, kept
        // for diagnostics/future strategy logic.
        public bool pitEntryCommitted;
        // Continuous seconds under 10 kph in green racing conditions - the
        // blunt crawl-retirement rule (RaceManager.UpdateRaceControl).
        public float slowCrawlRetireTimer;
        // Whether this car's colliders are currently ignoring car-to-car
        // contact (pit-sequence immunity - see RaceManager.HandlePitService).
        public bool carToCarCollisionIgnored;

        // ==== Unified pit rail (full pit-system rebuild) ====
        // One monotonic parameter drives the ENTIRE guided pit sequence from
        // commit to handoff: pitRailTraveled, metres actually travelled along
        // the canonical pit path since BeginPitEntry seeded the rail at the
        // car's own commit position. The landmark S-values below are computed
        // ONCE at commit by chaining wrapped forward segments (commit -> box ->
        // release -> ramp end), so no later tick ever performs wrapped
        // distance arithmetic that could misread a wrap as a whole lap. The
        // car's world pose is always sampled from the canonical pit-lane
        // geometry at (railStart + traveled) - there is no separate per-phase
        // seeding, no reprojection, and no chase target that can desync from
        // the authority.
        public float pitRailTraveled;
        public float pitRailBoxS;
        public float pitRailReleaseS;
        public float pitRailRampEndS;
        public float pitRailHardEndS;
        // Set once when the car first arrives at its box (visuals/timer
        // started), once when the tyres have actually been changed, and once
        // when the car has been released from the bay into the lane.
        public bool pitRailServiceStarted;
        public bool pitRailStopServed;
        public bool pitRailServiceDone;

        public bool missedPitEntryThisLap;
        // Deterministic-deadlock fix: missedPitEntryThisLap used to be a mostly
        // write-only field - nothing actually gated automatic pit-request sources
        // (strategy lap, tyre wear/grip collapse, damage, undercut, VSC/SC) against
        // it, so PitRequested got silently re-armed the same lap, the car was still
        // inside the broad approach zone, and pit-entry steering resumed toward an
        // opening that had already physically closed. This records which
        // CompletedLaps value the miss happened on; missedPitEntryThisLap only
        // clears again once CompletedLaps has genuinely advanced past it (the car
        // has crossed the line and started a new lap) - see
        // RaceManager.UpdateMissedPitEntryReset.
        public int missedPitEntryCompletedLap = -1;
        public int pitRequestLapNumber = -1;
        // Pre-race pit lap fix: the displayed CURRENT lap during pit exit is not
        // reliable evidence of which lap the car actually entered the pits on -
        // a car that enters pits late on lap 4 can cross the start/finish line
        // while still physically in the pit lane, at which point DisplayLap
        // reads 5 for the rest of the stop even though entry itself happened on
        // lap 4. pitEntryLap is captured once, in RaceManager.BeginPitEntry,
        // from DisplayLap at the exact moment of entry (before any such
        // line-crossing ambiguity can occur), and is the only field
        // strategy-compliance checks or "stop taken on lap" text should ever
        // read - never the lap number shown later during exit.
        public int pitEntryLap = -1;
        // Unique pit box slot assigned at spawn; cars are guided to their own box
        // instead of one shared service pose.
        public int pitBoxIndex;
        // Set while the car is waiting for a safe release gap after service.
        public bool pitAwaitingRelease;
        // Set true for the duration of any tick this car is intentionally held
        // behind the railed car ahead of it in the pit lane rather than
        // genuinely stuck - read by incident/recovery classification so a
        // legitimate queue hold never reads as a stranded car.
        public bool pitLaneHeldByOccupancy;
        // While pit-railed, the car's world pose is sampled every tick from
        // the canonical pit-lane geometry at pitGuideDistance
        // (TrackRuntime.SamplePitLanePose) with a rate-limited lateral, so it
        // follows the lane's own curvature and peels in/out gradually.
        public bool hasPitGuideState;
        public float pitGuideDistance;
        public float pitGuideLateral;
        // Lightweight per-race ERS/DRS usage counters for post-session diagnostics.
        public int ersDeployFrameCount;
        public int drsActiveFrameCount;
        // DRS fix: eligibility is decided once, at each zone's own detection point
        // (RaceManager.UpdateDrsEligibility/TrackRuntime.CrossedDrsDetectionPoint),
        // and then held for that whole activation zone rather than re-checked every
        // frame - see RaceManager.IsDrsAvailable. lapZone* records which lap the
        // decision was made on purely so a stale eligibility flag from a previous
        // lap's pass through the same zone can never leak into this lap's pass.
        public bool drsEligibleZoneOne;
        public bool drsEligibleZoneTwo;
        public int drsEligibilityLapZoneOne = -1;
        public int drsEligibilityLapZoneTwo = -1;
        public float previousDrsProgressNormalized = -1f;
        // Race position latched the moment this car entered its current DRS
        // zone (per report - "the AI passed me on that straight... I get DRS
        // too???"): live in-zone earning (RaceManager.UpdateDrsEligibility)
        // is only for a car CATCHING the one ahead; a car that got passed
        // inside the zone now sits behind the car that just passed it, which
        // used to satisfy the gap check and retroactively hand the passed
        // leader DRS. Live earning is gated on not having LOST position since
        // zone entry. -1 = not currently in a zone.
        public int drsZoneEntryRacePosition = -1;
        // Denominator for the two counters above (post-race telemetry report -
        // see RuntimeUi.BuildDrivingTelemetryCard) - only counts frames this
        // participant was actually ticked (on track, not mid-pit-guide/finished),
        // the same set ersDeployFrameCount/drsActiveFrameCount already sample.
        public int trackedTickFrameCount;
        public float pitTimer;
        public float pitServiceDuration;
        public bool pitLimiterUntilExit;
        // Short post-handoff AI-side lane hold (see AiVehicleController) so normal
        // racing-line/overtake/defend logic doesn't immediately dive for the apex
        // the instant guided pit-rail control hands back, even though the rail
        // itself already delivered the car to a safe outer line.
        public float pitExitLaneHoldTimer;
        public float pitExitLaneHoldDistanceRemaining;
        public bool hasLastSafePosition;
        public Vector3 lastSafePosition;
        public Quaternion lastSafeRotation;
        public float fallRespawnCooldown;
        // Post-reset ghost window (per request): any recovered/reset car - the
        // player's R reset or an AI auto-recovery - is intangible to OTHER CARS
        // for ~3s so nobody can plough into a car that just materialised on the
        // racing line. Enforced through the same pairwise car-to-car
        // IgnoreCollision path the pit sequence uses (HandlePitService owns the
        // toggle); walls/ground still collide normally. RaceManager extends the
        // window slightly at expiry if another car is physically overlapping so
        // two cars can never rematerialise inside each other.
        public float resetGhostTimer;
        // Tracks how long the car has been sitting notably below the intended
        // road height (e.g. settled on lower ground beneath an elevated section)
        // so recovery can trigger on a sustained mismatch, not only a hard fall.
        public float belowTrackTimer;
        // Race-control incident detection state (RaceManager.DetectIncidents): how
        // long this car has been sitting near-stationary on/off track, and how long
        // it has been facing the wrong way, plus a per-car cooldown so one ongoing
        // incident isn't re-classified every detection tick.
        public float stoppedOnTrackTimer;
        public float wrongWayTimer;
        public float incidentCooldownTimer;
        // Recovery-state classification (Part 2): the car's current category and
        // how long it's held it, a short grace window after a spin/off-track/
        // contact event during which it can never be declared stranded, a count
        // of how many times its own recovery maneuver has failed, and a one-shot
        // guard so the "ignored a false stranded case" debug log doesn't spam
        // once per 0.35s tick for the whole time a car is legitimately slow.
        public CarRecoveryState recoveryState = CarRecoveryState.Normal;
        public float recoveryGraceTimer;
        public int recoveryAttemptCount;
        public bool falseStrandedLogged;
        // Stuck-recovery escalation fix: cooldown gating RaceManager's
        // last-resort force-reposition (see HandleStuckEscalation) so it can
        // never fire more than once per window even if the car immediately
        // gets stuck again - a genuine last resort, not a repositioning loop.
        public float stuckRepositionCooldown;
        public float previousSpeedKphForIncident;
        public float previousDamagePercentForIncident;
        // Short rolling history (three RaceManager.RaceControlCheckInterval-sized
        // ticks, roughly one second) so DetectIncidents compares the current sample
        // against the recent peak speed / lowest damage in that window rather than
        // only the immediately previous 0.35s tick - a crash whose speed loss and
        // damage registration straddle two poll windows is still caught reliably.
        public float incidentSpeedHistory0;
        public float incidentSpeedHistory1;
        public float incidentSpeedHistory2;
        public float incidentDamageHistory0;
        public float incidentDamageHistory1;
        public float incidentDamageHistory2;
        // Illegal-overtake-under-yellow detection (Part B.7): who was immediately
        // ahead of this car last tick, so a pass that inverts that specific pair's
        // order while overtaking is banned can be penalized once, without needing
        // full running-order history tracking.
        public RaceParticipant previousCarAheadForOvertakeCheck;
        // Completed AI overtakes this race (AiVehicleController increments this on
        // the CompletingPass transition) - surfaced in the post-race diagnostics log.
        public int overtakesCompleted;
        public int pitStops;
        // Lap number on which the most recent stop was actually served (-1 = none).
        // Used to enforce a minimum stint so the planned-stop scheduler can never
        // drag a car back in on the lap after it pitted - see
        // RaceManager.UpdatePlayerAutoPitStrategy. VehicleController.pitCooldown was
        // meant to do this job but is written and decremented and never read.
        public int lastPitLapNumber = -1;
        // Post-race report (Part 2): compound fitted at each stop, in order, so
        // the strategy summary can read "Medium -> Hard -> Soft" instead of just
        // the compound the car happened to finish on.
        public List<string> compoundStints = new List<string>();
        public TyreCompound startingCompound = TyreCompound.Medium;
        // Two-compound rule bookkeeping (see RaceManager.ApplyTwoCompoundRule).
        public bool twoCompoundRuleChecked;
        public bool disqualified;

        /// <summary>
        /// How many DISTINCT dry specifications this car actually ran, and whether it
        /// ran any wet-weather tyre. The starting set counts - the real rule is about
        /// specifications used, not stops made.
        /// </summary>
        public void CountDryCompoundsUsed(out int distinctDryCompounds, out bool usedWetOrIntermediate)
        {
            bool soft = false, medium = false, hard = false;
            usedWetOrIntermediate = false;
            AccumulateCompound(startingCompound, ref soft, ref medium, ref hard, ref usedWetOrIntermediate);
            for (int i = 0; i < compoundStints.Count; i++)
            {
                TyreCompound parsed;
                if (System.Enum.TryParse(compoundStints[i], out parsed))
                {
                    AccumulateCompound(parsed, ref soft, ref medium, ref hard, ref usedWetOrIntermediate);
                }
            }

            distinctDryCompounds = (soft ? 1 : 0) + (medium ? 1 : 0) + (hard ? 1 : 0);
        }

        static void AccumulateCompound(TyreCompound compound, ref bool soft, ref bool medium, ref bool hard, ref bool wet)
        {
            switch (compound)
            {
                case TyreCompound.Soft: soft = true; break;
                case TyreCompound.Medium: medium = true; break;
                case TyreCompound.Hard: hard = true; break;
                default: wet = true; break;
            }
        }
        // Fuel system pass: the fuel-load plan this car actually raced with (player's
        // own pre-race choice, or the AI's per-driver roll - see
        // RaceManager.ResolveAiFuelChoice). Defaults to Target so a participant that
        // somehow skips fuel setup (should never happen) still reports sensibly.
        public FuelLoadChoice chosenFuelLoad = FuelLoadChoice.Target;
        // Fuel starvation DNF grace period (VehicleController.FuelStarved is the
        // live per-frame flag; this timer is RaceManager's own tracking for when to
        // actually retire the car - see UpdateFuelStarvation).
        public bool fuelStarvationRetirementApplied;
        // AI off-track auto-recovery accumulator (RaceManager.UpdateRaceControl):
        // seconds this AI has been continuously off the racing surface with no
        // legitimate reason; reset triggers the player-R-equivalent recovery.
        public float offTrackResetTimer;
        public TyreCompound nextPitCompound = TyreCompound.Medium;
        public TyreCompound requestedPitCompound = TyreCompound.Medium;
        public bool requestedPitCompoundSet;
        public bool pitTyreSelectionActive;
        public bool mandatoryPitPenaltyApplied;
        // Automatic pit stop fix: distinguishes a pit request the strategy
        // plan triggered on its own from one the player actively called (P
        // key) or picked a tyre for, purely so the HUD can tell the player
        // which one just happened - see RaceManager.UpdatePlayerAutoPitStrategy
        // and RaceHud's pit card.
        public bool pitAutoTriggered;

        // Cancellable-manual-pit-stop fields. activePitRequestSource is the
        // authoritative tag for the CURRENTLY queued request (set alongside
        // every vehicle.RequestPit() call site); manualPitRequested is true
        // exactly while that source is Manual or SafetyCarPrompt and the car
        // has not yet committed - the window RaceManager.CanCancelManualPitRequest
        // allows cancellation in. manualPitCommitted flips on once the car
        // crosses the same authoritative pit-entry commitment boundary the
        // rest of the pit state machine uses (Track.HasCrossedPitEntryLimiterLine
        // / BeginPitEntry) and never flips back for that stop - cancellation is
        // permanently impossible from that point on. None of this ever reads
        // from or writes to the pre-race planned stop itself (see
        // RaceManager.NextPlannedPitLapFor/GetPlannedPitLapForStop, which stay
        // driven purely by Settings.Current.plannedPitLapOne/Two and pitStops -
        // this project's actual equivalent of a standalone "preRacePlannedPitLap"
        // field), so creating, committing or cancelling a manual request can
        // never delete, overwrite or postpone it.
        public PitRequestSource activePitRequestSource = PitRequestSource.None;
        public bool manualPitRequested;
        public bool manualPitCommitted;

        // Full safety car convoy autopilot (RaceManager.BuildRaceControlAutopilotCommand):
        // this car's slot in the queue (0 = right behind the safety car), the
        // legal running-order index it held the instant the safety car was
        // deployed (restored, unchanged, once control returns at the restart),
        // the live target progress-distance the autopilot is currently steering
        // this car toward (surfaced for HUD gap readouts), and whether race
        // control - not the player or the AI's own state machine - is currently
        // driving this car at all.
        public int safetyCarQueueIndex = -1;
        public float safetyCarTargetDistance;
        public bool isRaceControlAutopilot;
        public int preSafetyCarOrderIndex = -1;

        public VehicleController vehicle;
        public LapTracker lapTracker;
        public DriverData driverData;
        public TeamData teamData;
        public CarPerformanceData carData;

        public void Initialize(
            string id,
            string displayName,
            string team,
            string shortName,
            bool player,
            DriverData driver,
            TeamData teamInfo,
            CarPerformanceData car)
        {
            driverId = id;
            driverName = displayName;
            teamId = team;
            teamShortName = shortName;
            isPlayer = player;
            driverData = driver;
            teamData = teamInfo;
            carData = car;
            vehicle = GetComponent<VehicleController>();
            lapTracker = GetComponent<LapTracker>();
        }

        public RaceResultEntry ToResultEntry()
        {
            string strategySummary = startingCompound.ToString();
            for (int i = 0; i < compoundStints.Count; i++)
            {
                strategySummary += " -> " + compoundStints[i];
            }

            return new RaceResultEntry
            {
                disqualified = disqualified,
                driverId = driverId,
                driverName = driverName,
                teamId = teamId,
                finishingPosition = finishingPosition,
                gridPosition = gridPosition,
                completedLaps = lapTracker == null ? 0 : lapTracker.CompletedLaps,
                totalTime = finished ? finishTime : Time.time,
                bestLapTime = lapTracker == null ? 0f : lapTracker.BestLapTime,
                penaltiesSeconds = penaltiesSeconds,
                penaltyReason = ResultPenaltyReason(),
                isPlayer = isPlayer,
                tyreCompound = vehicle == null || vehicle.Tyres == null ? "Medium" : vehicle.Tyres.Compound.ToString(),
                pitStops = pitStops,
                overtakesMade = overtakesCompleted,
                lockups = vehicle == null || vehicle.Tyres == null ? 0 : vehicle.Tyres.TotalLockups,
                flatSpotPercent = vehicle == null || vehicle.Tyres == null ? 0f : vehicle.Tyres.FlatSpotLevel * 100f,
                trackLimitWarnings = trackLimitWarnings,
                trackLimitEvents = new List<string>(trackLimitEventLog),
                strategySummary = strategySummary,
                startFuelKg = vehicle == null ? 0f : vehicle.StartFuelKg,
                finishFuelKg = vehicle == null ? 0f : vehicle.FuelKg,
                fuelLoadChoice = (int)chosenFuelLoad,
                liftAndCoastSavedKg = vehicle == null ? 0f : vehicle.LiftAndCoastSavedKg,
                fuelStarvedRetirement = retired && !string.IsNullOrEmpty(retirementReason) && retirementReason.Contains("Fuel starvation")
            };
        }

        string ResultPenaltyReason()
        {
            if (!retired)
            {
                return penaltyReason;
            }

            string reason = string.IsNullOrEmpty(retirementReason) ? "Damage" : retirementReason;
            if (string.IsNullOrEmpty(penaltyReason))
            {
                return "DNF " + reason;
            }

            return penaltyReason.Contains("DNF") ? penaltyReason : penaltyReason + ", DNF " + reason;
        }
    }
}
