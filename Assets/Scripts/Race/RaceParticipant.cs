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
        public float pitTimer;
        public float pitServiceDuration;
        public bool pitLimiterUntilExit;
        public bool hasLastSafePosition;
        public Vector3 lastSafePosition;
        public Quaternion lastSafeRotation;
        public float fallRespawnCooldown;
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
