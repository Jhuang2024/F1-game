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
        QualifyingReturn
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
        public bool jumpStartPenaltyApplied;
        public bool isPitting;
        public PitPhase pitPhase;
        public bool pitEntryAligned;
        // Unique pit box slot assigned at spawn; cars are guided to their own box
        // instead of one shared service pose.
        public int pitBoxIndex;
        // Set while the car is waiting for a safe release gap after service.
        public bool pitAwaitingRelease;
        // Which staggered release point (see TrackRuntime.GetPitReleasePose) this car
        // was assigned when it entered Release, so simultaneous releases never target
        // the identical shared coordinate.
        public int pitReleaseStagger;
        // Pit lane animation fix: while pit-guided, the car chases a
        // continuously-advancing (distance-along-track, lateral-offset)
        // waypoint (see RaceManager.AdvancePitGuideTarget /
        // TrackRuntime.SamplePitLanePose) instead of beelining straight at a
        // single far-away fixed pose - this is what makes the car actually
        // follow the pit lane's own curvature and peel in/out gradually
        // rather than cutting a straight diagonal line across the track or
        // snapping sideways at each phase transition. Reset (hasPitGuideState
        // = false) at the start of every phase that needs a fresh starting
        // point.
        public bool hasPitGuideState;
        public float pitGuideDistance;
        public float pitGuideLateral;
        // Pit-lane stuck watchdog (RaceManager.UpdatePitDrivingStuckWatchdog): last
        // distance-along-track this car was confirmed making real forward progress
        // while actively guided (Entry/Release), how long it's been stuck since,
        // and how many times it's already been nudged back onto the path this stop -
        // a car nudged repeatedly gets an actual (last-resort) reposition instead of
        // being nudged forever.
        public float pitStuckWatchdogTimer;
        public float pitStuckLastDistance = -1f;
        public int pitStuckRecoveryCount;
        // Lightweight per-race ERS/DRS usage counters for post-session diagnostics.
        public int ersDeployFrameCount;
        public int drsActiveFrameCount;
        // Denominator for the two counters above (post-race telemetry report -
        // see RuntimeUi.BuildDrivingTelemetryCard) - only counts frames this
        // participant was actually ticked (on track, not mid-pit-guide/finished),
        // the same set ersDeployFrameCount/drsActiveFrameCount already sample.
        public int trackedTickFrameCount;
        public float pitTimer;
        public float pitServiceDuration;
        public bool pitLimiterUntilExit;
        public bool hasLastSafePosition;
        public Vector3 lastSafePosition;
        public Quaternion lastSafeRotation;
        public float fallRespawnCooldown;
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
        // Post-race report (Part 2): compound fitted at each stop, in order, so
        // the strategy summary can read "Medium -> Hard -> Soft" instead of just
        // the compound the car happened to finish on.
        public List<string> compoundStints = new List<string>();
        public TyreCompound startingCompound = TyreCompound.Medium;
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
                strategySummary = strategySummary
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
