using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    [Serializable]
    public class TeamDatabase
    {
        public List<TeamData> teams = new List<TeamData>();
    }

    [Serializable]
    public class TeamData
    {
        public string id;
        public string name;
        public string shortName;
        public string primaryColor;
        public string secondaryColor;
        public string carPerformanceId;
        public int reliability;
        public int reputation;

        public Color PrimaryUnityColor
        {
            get
            {
                Color color;
                return ColorUtility.TryParseHtmlString(primaryColor, out color) ? color : Color.white;
            }
        }

        public Color SecondaryUnityColor
        {
            get
            {
                Color color;
                return ColorUtility.TryParseHtmlString(secondaryColor, out color) ? color : Color.black;
            }
        }
    }

    [Serializable]
    public class DriverDatabase
    {
        public List<DriverData> drivers = new List<DriverData>();
    }

    [Serializable]
    public class DriverData
    {
        public string id;
        public string displayName;
        public string abbreviation;
        public int number;
        public string teamId;
        public int pace;
        public int racecraft;
        public int qualifying;
        public int tyreManagement;
        public int wetSkill;
        public int consistency;
        public int aggression;
        public int defending;
        public int overtaking;
        public int awareness;
        public int experience;
        public int developmentPotential;

        public float AverageRating
        {
            get
            {
                return (qualifying + defending + overtaking + pace) / 4f;
            }
        }

        public int OverallRating
        {
            get { return Mathf.RoundToInt(AverageRating); }
        }
    }

    [Serializable]
    public class CalendarDatabase
    {
        public List<CalendarEventData> events = new List<CalendarEventData>();
    }

    [Serializable]
    public class CalendarEventData
    {
        public int round;
        public string trackId;
        public string displayName;
        public string country;
        public int laps3;
        public int laps5;
        public int laps25Percent;
        public string weatherProfile;
    }

    [Serializable]
    public class CarPerformanceDatabase
    {
        public List<CarPerformanceData> cars = new List<CarPerformanceData>();
    }

    [Serializable]
    public class CarPerformanceData
    {
        public string id;
        public int topSpeed;
        public int acceleration;
        public int cornering;
        public int braking;
        public int reliability;
        public int ersEfficiency;
        public int tyreManagement;
        public int aeroEfficiency;
        public int chassisBalance;
        public int enginePower;
    }

    [Serializable]
    public class UpgradeDatabase
    {
        public List<UpgradeData> upgrades = new List<UpgradeData>();
    }

    [Serializable]
    public class UpgradeData
    {
        public string id;
        public string category;
        public string displayName;
        public int cost;
        public int developmentDays;
        public float successChance;
        public string requiredUpgradeId;
        public int topSpeedDelta;
        public int accelerationDelta;
        public int corneringDelta;
        public int brakingDelta;
        public int reliabilityDelta;
        public int ersDelta;
        public int tyreDelta;
        public int aeroDelta;
        public int chassisDelta;
        public int engineDelta;

        // Field initializer keeps upgrades from data files without a tier key
        // usable as tier-1 projects.
        public int tier = 1;
    }

    [Serializable]
    public class ActiveUpgradeProject
    {
        public string upgradeId;
        public string category;
        public int startRound;
        public int remainingRaceWeeks;
        public int totalRaceWeeks;
        public int cost;
        public float successChance;

        // 0 Conservative, 1 Standard, 2 Rush, 3 Experimental.
        public int riskMode = 1;

        // 0 InDevelopment, 1 Completed, 2 Failed, 3 ReworkAvailable.
        public int status;
        public bool bonusApplied;
    }

    [Serializable]
    public class CareerSaveData
    {
        public int currentSeason = 1;
        public int currentRound = 1;
        public string playerDriverName = "Player Driver";
        public string playerTeamId = "williams";
        public bool useExistingDriver;
        public string selectedDriverId = "";
        public string rivalDriverId = "";
        public int contractTargetPosition = 8;
        public int reputation = 25;
        public int resourcePoints = 500;
        public int difficultyIndex = 1;
        public List<StandingEntry> driverStandings = new List<StandingEntry>();
        public List<StandingEntry> constructorStandings = new List<StandingEntry>();
        public List<RaceResultRecord> raceResults = new List<RaceResultRecord>();
        public List<QualifyingResultRecord> qualifyingResults = new List<QualifyingResultRecord>();
        public List<QualifyingResultEntry> lastQualifyingResults = new List<QualifyingResultEntry>();
        public List<string> completedUpgradeIds = new List<string>();
        public List<string> failedUpgradeIds = new List<string>();

        // Practice program completion keys ("s1_r3_qualiPace"); defaults keep old
        // saves loading cleanly.
        public List<string> completedPracticePrograms = new List<string>();

        // Part 3: rivalry / teammate battle tracking. Head-to-head win counts are
        // "player finished/qualified ahead of X" tallies, reset only when the
        // rival themselves changes (teammate counters never reset).
        public int teammateRaceWins;
        public int teammateRaceLosses;
        public int teammateQualifyingWins;
        public int teammateQualifyingLosses;
        public int rivalRaceWins;
        public int rivalRaceLosses;
        public int rivalQualifyingWins;
        public int rivalQualifyingLosses;
        // Last-3-races finishing positions, most recent last, for the Form Guide card.
        public List<int> recentFormPositions = new List<int>();
        // Reputation value snapshot right after each race, most recent last
        // (capped short), for the Reputation Trend card.
        public List<int> reputationHistory = new List<int>();
        public int roundsSinceRivalPicked;
        // Career news feed (Part 18): short headlines generated from real
        // race/R&D/rivalry events, most recent last, capped in CareerManager.
        public List<string> newsFeed = new List<string>();

        // R&D management state. Initializers are the old-save defaults; the
        // matching null/size guards live in CareerManager.EnsureStandingLists.
        public List<ActiveUpgradeProject> activeUpgradeProjects = new List<ActiveUpgradeProject>();
        public List<string> pendingRndMessages = new List<string>();
        public List<int> departmentLevels = new List<int>();
        public List<string> regulationAffectedCategories = new List<string>();
        public int practiceQualityThisRound;
    }

    [Serializable]
    public class StandingEntry
    {
        public string id;
        public string displayName;
        public string teamId;
        public int points;
        public int wins;
        public int podiums;
    }

    [Serializable]
    public class RaceResultRecord
    {
        public int season;
        public int round;
        public string eventName;
        public List<RaceResultEntry> results = new List<RaceResultEntry>();
    }

    [Serializable]
    public class RaceResultEntry
    {
        public string driverId;
        public string driverName;
        public string teamId;
        public int finishingPosition;
        public int gridPosition;
        public int completedLaps;
        public float totalTime;
        public float bestLapTime;
        public float penaltiesSeconds;
        public string penaltyReason;
        public int points;
        public bool isPlayer;
        public string tyreCompound;

        // Post-race report (Part 2) additions.
        public int pitStops;
        public int overtakesMade;
        public int lockups;
        public float flatSpotPercent;
        public int trackLimitWarnings;
        public string strategySummary;
    }

    [Serializable]
    public class QualifyingResultRecord
    {
        public int season;
        public int round;
        public string eventName;
        public List<QualifyingResultEntry> results = new List<QualifyingResultEntry>();
    }

    [Serializable]
    public class QualifyingResultEntry
    {
        public string driverId;
        public string driverName;
        public string teamId;
        public int position;
        public float bestLapTime;
        public bool isPlayer;
        public bool invalidated;
        public string session;
        public string eliminatedIn;
    }

    [Serializable]
    public class GameSettingsData
    {
        public int laps = 5;
        public int difficultyIndex = 1;
        public bool manualGears;
        public bool cameraShake = true;
        public bool audioEnabled = true;
        public string tyreCompound = "Medium";
        public int aiOpponentCount = 21;
        public bool autoBrakeAssist;
        public bool absAssist = true;
        public bool tractionControl = true;
        public bool racingLineAssist = true;
        public float steeringSensitivity = 1f;
        public float throttleSensitivity = 1f;
        public float brakeSensitivity = 1f;
        public float controllerDeadzone = 0.12f;
        public int ersMode;
        public int raceLengthPreset = 1;

        // Added fields: field initializers act as backwards-compatible defaults, because
        // JsonUtility keeps them when the key is absent from an older save file.
        public float hudScale = 1f;
        public float cameraFov = 60f;
        public float cameraShakeStrength = 0.2f;
        public bool useMphUnits;
        public float sceneryDensity = 1f;
        public int graphicsQuality = 2;
        public bool uiAnimations = true;

        // HUD / visual additions.
        public bool compactHud;
        public bool particlesEnabled = true;

        // Car setup (1..5, 3 = neutral). Applied as small physics trade-offs.
        public int setupFrontWing = 3;
        public int setupRearWing = 3;
        public int setupBrakeBias = 3;
        public int setupSuspension = 3;
        public int setupRideHeight = 3;

        // Pit strategy plan. plannedPitLap 0 = engineer's recommendation.
        // Kept for backward compatibility with older save files; GameSettingsStore.Load
        // migrates these into the stop-indexed fields below on first load.
        public int plannedPitLap;
        public string plannedSecondCompound = "Medium";

        // Full 1-stop/2-stop strategy plan. plannedStopCount selects how many
        // planned stops the engineer will call for; pit lap 0 for a stop means
        // "let the engineer pick the window" for that stop.
        public int plannedStopCount = 1;
        public int plannedPitLapOne;
        public int plannedPitLapTwo;
        public string plannedStopOneCompound = "Hard";
        public string plannedStopTwoCompound = "Medium";

        // Race control / safety car pass. safetyCarFrequency: 0=Off, 1=Reduced,
        // 2=Standard, 3=High. mechanicalFailureMode: 0=Off, 1=PlayerOff (AI can still
        // fail, player never does), 2=Standard. particlesEnabled (above) already
        // gates lockup smoke/skid visuals - no separate toggle needed for that.
        public int safetyCarFrequency = 2;
        public int mechanicalFailureMode = 2;
        public bool lockupsEnabled = true;
        public bool raceControlMessages = true;

        // Part 19: settings for the atmosphere/presentation pass. Engineer
        // messages: 0 Off, 1 Minimal (priority only), 2 Standard, 3 Frequent
        // (shortens the routine-chatter cooldown). Race presentation: 0 Minimal,
        // 1 Standard, 2 Cinematic (finish-line camera flourish, bigger banners).
        // Weather variability: 0 Off (locked to the forecast's base state), 1 Low,
        // 2 Standard, 3 High (more/faster transitions). cameraShakeLevel drives
        // cameraShakeStrength's effective multiplier without discarding the raw
        // slider value players already tuned.
        public int engineerMessageVerbosity = 2;
        public int racePresentation = 1;
        public int weatherVariability = 2;
        public int cameraShakeLevel = 2;
        public bool practiceProgramsEnabled = true;
        public bool careerNewsFeedEnabled = true;
    }

    [Serializable]
    public class PlayerRecordsData
    {
        public List<TrackRecordEntry> trackRecords = new List<TrackRecordEntry>();

        // Career-wide statistics. Field defaults keep old record files loading cleanly.
        public int racesFinished;
        public int raceWins;
        public int podiums;
        public int polePositions;
        public int fastestLaps;
        public int totalPoints;
        public int cleanRaces;
        public int trackLimitWarningsTotal;
        public int bestQualifyingPosition;
    }

    [Serializable]
    public class TrackRecordEntry
    {
        public string trackId;
        public float bestLapTime;
        public string context;
    }

    public enum RaceDifficulty
    {
        Easy,
        Medium,
        Hard,
        Expert
    }

    public enum TyreCompound
    {
        Soft,
        Medium,
        Hard,
        Intermediate,
        Wet
    }

    public enum WeatherState
    {
        Clear,
        Cloudy,
        LightRain,
        HeavyRain
    }

    public enum RaceWeekendSession
    {
        QuickRace,
        Qualifying,
        Race
    }

    public enum ErsStrategyMode
    {
        Balanced,
        Attack,
        Harvest
    }
}
