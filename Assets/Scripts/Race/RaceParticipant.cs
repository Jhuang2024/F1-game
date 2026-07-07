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
        // Lightweight per-race ERS/DRS usage counters for post-session diagnostics.
        public int ersDeployFrameCount;
        public int drsActiveFrameCount;
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
        public TyreCompound startingCompound = TyreCompound.Medium;
        public TyreCompound nextPitCompound = TyreCompound.Medium;
        public TyreCompound requestedPitCompound = TyreCompound.Medium;
        public bool requestedPitCompoundSet;
        public bool pitTyreSelectionActive;
        public bool mandatoryPitPenaltyApplied;

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
                tyreCompound = vehicle == null || vehicle.Tyres == null ? "Medium" : vehicle.Tyres.Compound.ToString()
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
