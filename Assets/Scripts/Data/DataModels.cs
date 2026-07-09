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

        // Part 20: the same news events as newsFeed, but as full articles
        // (headline + body + category + timestamp) for a dedicated news
        // screen. Populated alongside newsFeed by CareerManager.AddNewsArticle,
        // capped independently (larger backlog than the short headline strip).
        public List<NewsArticle> newsArticles = new List<NewsArticle>();

        // Part 20: one aggregated report per completed race (race control,
        // incidents, penalties, strategy, AI performance) for a results/report
        // screen, most recent last, capped in CareerManager.RecordRaceReport.
        public List<RaceReportRecord> raceReports = new List<RaceReportRecord>();

        // R&D management state. Initializers are the old-save defaults; the
        // matching null/size guards live in CareerManager.EnsureStandingLists.
        public List<ActiveUpgradeProject> activeUpgradeProjects = new List<ActiveUpgradeProject>();
        public List<string> pendingRndMessages = new List<string>();
        public List<int> departmentLevels = new List<int>();
        public List<string> regulationAffectedCategories = new List<string>();
        public int practiceQualityThisRound;

        // Part 21: end-of-season / new-season system. seasonPhase drives the
        // Career Hub / post-race-report navigation state machine; the archive
        // list is append-only (one entry per completed season, never trimmed)
        // so old seasons are never lost when a new one begins. Regulation
        // changes and team-performance modifiers are also append-only and
        // tagged with the season they apply to (same convention as
        // raceResults/qualifyingResults) so a season-archive detail view can
        // always show exactly what was in effect that year, and the "current"
        // ones are simply whichever entries carry season == currentSeason.
        public CareerSeasonPhase seasonPhase = CareerSeasonPhase.InSeason;
        public bool seasonTransitionPending;
        public int lastCompletedSeasonNumber;
        public List<SeasonArchive> seasonArchives = new List<SeasonArchive>();
        public List<RegulationChange> regulationChanges = new List<RegulationChange>();
        public List<TeamPerformanceModifier> teamPerformanceModifiers = new List<TeamPerformanceModifier>();
        public List<SeasonObjective> seasonObjectives = new List<SeasonObjective>();
        public bool preSeasonTestingSeen;

        // Concrete numeric regulation hooks (see CareerManager.GenerateRegulation
        // Changes) - default 1 means "no active regulation effect", so an old
        // save or a season with no Tyre Wear/Cost Cap regulation this year
        // behaves exactly as before.
        public float currentSeasonTyreWearMultiplier = 1f;
        public float currentSeasonResourceMultiplier = 1f;
    }

    // Part 21: Career Hub / post-race-report state machine for the end-of-
    // season -> offseason -> new-season flow. InSeason is the ordinary state
    // for every round except the one right after the final calendar race.
    public enum CareerSeasonPhase
    {
        InSeason,
        FinalRaceComplete,
        SeasonReview,
        Offseason,
        Preseason,
        NewSeasonActive
    }

    // Part 21: a single new-season regulation change - small, explained, and
    // tagged with who it tends to help/hurt so the offseason/preseason UI can
    // present it as a real story rather than an abstract multiplier.
    [Serializable]
    public class RegulationChange
    {
        public int season;
        public string category;
        public string title;
        public string description;
        // Signed, small (-0.12..0.12 typical) - positive means the category
        // generally gets an easier time this season (e.g. a relaxed cost cap),
        // negative a harder one (a crackdown/ban).
        public float magnitude;
        public string beneficiaryHint;
        public string loserHint;
    }

    // Part 21: a team's small season-to-season performance swing, layered on
    // top of the static per-team CarPerformanceData at read time (see
    // CareerManager.GetEffectiveTeamCar) - never mutates the shared reference
    // data, exactly like the existing player-only upgrade tuning already
    // doesn't. Append-only across seasons, tagged by season, so an archived
    // season can show exactly what every team's swing was that year.
    [Serializable]
    public class TeamPerformanceModifier
    {
        public string teamId;
        public int season;
        // Small per-stat multiplier delta applied evenly across the car's
        // performance stats, e.g. 0.04 = a noticeable but modest step forward.
        public float performanceDelta;
        public int reputationDelta;
        public string trendLabel;
    }

    // Part 21: one season-award line (Driver of the Year, Cleanest Driver,
    // etc.) for the season-review page - a short, human-readable verdict
    // rather than a raw stat, even though it's derived from real season data.
    [Serializable]
    public class SeasonAward
    {
        public string title;
        public string winnerName;
        public string detail;
    }

    // Part 21: a single player/team objective for the current season, shown on
    // the Career Hub and referenced in post-race reports. Regenerated fresh
    // each new season (see CareerManager.GenerateSeasonObjectives) and
    // snapshotted into that season's SeasonArchive.objectives before being
    // replaced, so completed objectives are never silently lost.
    [Serializable]
    public class SeasonObjective
    {
        public string id;
        public string description;
        public string category;
        public int targetValue;
        public int currentValue;
        public bool achieved;
        public bool teamObjective;
    }

    // Part 21: a full, frozen snapshot of one completed season - built once,
    // right when the final calendar race's results are applied (see
    // CareerManager.ApplyRaceResults/BuildSeasonArchive), before anything
    // about that season's live state (standings, rivalry counters, upgrades)
    // gets reset for the next one. This is the only place season-review/
    // season-archive UI should ever read from for a *completed* season -
    // never the live Save fields, which have already moved on.
    [Serializable]
    public class SeasonArchive
    {
        public int season;
        public string driverChampionId;
        public string driverChampionName;
        public string constructorChampionId;
        public string constructorChampionName;
        public List<StandingEntry> finalDriverStandings = new List<StandingEntry>();
        public List<StandingEntry> finalConstructorStandings = new List<StandingEntry>();

        public string playerDriverName;
        public int contractTargetThatSeason;
        public int playerFinalPosition;
        public int playerPoints;
        public int playerWins;
        public int playerPodiums;
        public int playerPoles;
        public int playerFastestLaps;
        public int playerDnfs;
        public int playerPenalizedRaces;

        public string teammateId;
        public string teammateName;
        public int teammatePoints;
        public int teammateFinalPosition;
        public int teammateHeadToHeadWins;
        public int teammateHeadToHeadLosses;

        public string playerTeamId;
        public string playerTeamName;
        public int playerTeamFinalPosition;
        public int playerTeamPoints;

        public int redFlagCount;
        public int safetyCarCount;
        public int majorIncidentCount;

        public string bestRaceEventName;
        public int bestRaceFinish;
        public int bestRaceGridPosition;
        public string worstRaceEventName;
        public int worstRaceFinish;
        public string biggestComebackEventName;
        public int biggestComebackPositionsGained;
        public string biggestCollapseEventName;
        public int biggestCollapsePositionsLost;

        public string rivalId;
        public string rivalName;
        public int rivalRaceWins;
        public int rivalRaceLosses;

        // Reputation snapshot at the moment this season closed - comparing
        // consecutive archives' value gives a "rating change" trend without
        // needing a separate season-start snapshot.
        public int reputationAtSeasonEnd;

        public List<string> formSummary = new List<string>();
        public List<RegulationChange> regulations = new List<RegulationChange>();
        public List<TeamPerformanceModifier> teamPerformanceChanges = new List<TeamPerformanceModifier>();
        public List<string> upgradesCompleted = new List<string>();
        public List<SeasonAward> awards = new List<SeasonAward>();
        public List<string> keyHeadlines = new List<string>();
        public List<SeasonObjective> objectives = new List<SeasonObjective>();
    }

    // Part 20: a single news-feed entry with real body copy and a category,
    // generated from an actual career/race event (see CareerManager.AddNewsArticle).
    [Serializable]
    public class NewsArticle
    {
        public string headline;
        public string body;
        public string category;
        public int season;
        public int round;
        public string raceWeekLabel;
    }

    // Part 20: per-race report aggregated from the classification passed into
    // CareerManager.ApplyRaceResults, stored into career history for a
    // results/report screen built elsewhere.
    [Serializable]
    public class RaceReportRecord
    {
        public int season;
        public int round;
        public string eventName;
        public string headline;
        public RaceControlSummary raceControl = new RaceControlSummary();
        public IncidentSummary incidents = new IncidentSummary();
        public PenaltySummary penalties = new PenaltySummary();
        public StrategySummary strategy = new StrategySummary();
        public AiPerformanceSummary aiPerformance = new AiPerformanceSummary();
        public StandingsMovementSummary standings = new StandingsMovementSummary();
    }

    // Post-race report "Championship Impact" data: driver/constructor standing
    // and points immediately before and after this race's points were applied.
    // -1 for a *Before field means the driver/team had no prior standings entry
    // (debut race) rather than "was P-1".
    [Serializable]
    public class StandingsMovementSummary
    {
        public int driverPositionBefore = -1;
        public int driverPositionAfter = -1;
        public int driverPointsBefore;
        public int driverPointsAfter;
        public int constructorPositionBefore = -1;
        public int constructorPositionAfter = -1;
    }

    [Serializable]
    public class RaceControlSummary
    {
        // -1 means "not reported by the race session" (older saves / a race
        // that didn't pass safety-car stats through), so UI can distinguish
        // "no data" from "a clean race with zero incidents".
        public int incidentCount = -1;
        public int safetyCarDeployments = -1;
        // Same -1-means-no-data convention as the two fields above.
        public int redFlagCount = -1;
        public string redFlagReason = "";
        public bool wasChaotic;
        public string narrative;
    }

    [Serializable]
    public class IncidentSummary
    {
        public int totalLockups;
        public int totalTrackLimitWarnings;
        public float averageFlatSpotPercent;
        public int driversWithHeavyLockups;
    }

    [Serializable]
    public class PenaltySummary
    {
        public int driversPenalized;
        public float totalPenaltySeconds;
        public string mostCommonReason;
    }

    [Serializable]
    public class StrategySummary
    {
        public float averagePitStops;
        public int playerPitStops;
        public string playerStrategyText;
        public string mostCommonFinalCompound;
    }

    [Serializable]
    public class AiPerformanceSummary
    {
        public int totalAiOvertakes;
        public string standoutAiDriverName;
        public int standoutAiPositionsGained;
    }

    // Part 20: race-weekend depth data, generated on demand by
    // CareerManager.GenerateRaceWeekendBriefing for the current round - not
    // persisted, since it's re-derivable from the calendar/career state.
    [Serializable]
    public class RaceWeekendBriefing
    {
        public PracticeProgramSummaryData practice = new PracticeProgramSummaryData();
        public SetupRecommendationData setup = new SetupRecommendationData();
        public TyreStrategyPreviewData tyreStrategy = new TyreStrategyPreviewData();
        public WeatherForecastData weather = new WeatherForecastData();
        public TrackCharacteristicsSummaryData track = new TrackCharacteristicsSummaryData();
        public PitWindowEstimateData pitWindow = new PitWindowEstimateData();
    }

    [Serializable]
    public class PracticeProgramSummaryData
    {
        public int programsCompleted;
        public int programsAvailable = 5;
        public int qualityRating;
        public List<string> completedProgramNames = new List<string>();
        public string summaryText;
    }

    [Serializable]
    public class SetupRecommendationData
    {
        // 1..5, 3 = neutral, matching GameSettingsData.setupFrontWing etc.
        public int recommendedFrontWing = 3;
        public int recommendedRearWing = 3;
        public int recommendedBrakeBias = 3;
        public int recommendedSuspension = 3;
        public int recommendedRideHeight = 3;
        public string reasoning;
    }

    [Serializable]
    public class TyreStrategyPreviewData
    {
        public int recommendedStopCount = 1;
        public string recommendedStartCompound;
        public string recommendedSecondCompound;
        public string recommendedThirdCompound;
        public string reasoning;
    }

    [Serializable]
    public class WeatherForecastData
    {
        public WeatherState practiceForecast;
        public WeatherState qualifyingForecast;
        public WeatherState raceForecast;
        public int rainChancePercent;
        public string summaryText;
    }

    [Serializable]
    public class TrackCharacteristicsSummaryData
    {
        public string trackId;
        public string displayName;
        public bool streetCircuit;
        public string overtakingDifficulty;
        public string tyreDegradation;
        public string downforceLevel;
        public float estimatedPitLaneLossSeconds;
        public string styleDescription;
    }

    [Serializable]
    public class PitWindowEstimateData
    {
        public int totalLaps;
        public int earliestLap;
        public int optimalLap;
        public int latestLap;
        public int estimatedStintLength;
    }

    // Part 21: pre-season testing report - generated on demand from the
    // current season's team-performance modifiers/regulations, same "not
    // persisted, fully re-derivable" convention as RaceWeekendBriefing above.
    [Serializable]
    public class PreSeasonTestingEntry
    {
        public string teamId;
        public string teamName;
        public float paceRating;
        public string reliabilityNote;
    }

    [Serializable]
    public class PreSeasonTestingReport
    {
        public List<PreSeasonTestingEntry> paceRanking = new List<PreSeasonTestingEntry>();
        public string playerExpectationText;
        public string teammateBenchmarkText;
        public string tyreDegradationText;
        public List<string> upgradeRecommendations = new List<string>();
    }

    // Part 20: richer driver-card / team-rating-card presentation data,
    // derived from the existing DriverData / TeamData / CarPerformanceData
    // stat blocks rather than duplicating them.
    [Serializable]
    public class DriverProfileSummary
    {
        public string driverId;
        public string displayName;
        public int overallRating;
        public List<string> strengths = new List<string>();
        public List<string> weaknesses = new List<string>();
        public string archetype;
    }

    [Serializable]
    public class TeamProfileSummary
    {
        public string teamId;
        public string teamName;
        public int overallCarRating;
        public List<string> strengths = new List<string>();
        public List<string> weaknesses = new List<string>();
        public string competitivenessTier;
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
        // Track-limit stewarding depth: paired with trackLimitWarnings above -
        // this is the per-event detail ("Lap 4 - Sector 2") the post-race
        // report lists, while trackLimitWarnings stays the simple running
        // total every existing reader (records, clean-race check) already
        // uses. Defaults to an empty list so older saved race reports
        // (written before this field existed) deserialize with no events
        // rather than failing.
        public List<string> trackLimitEvents = new List<string>();
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
        public float masterVolume = 0.8f;
        public float engineVolume = 0.8f;
        public float uiVolume = 0.7f;
        public float radioVolume = 0.85f;
        public float ambienceVolume = 0.6f;
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

        // Time-trial ghost: 0=Off, 1=Session best (this session's own best
        // lap, not carried from a prior session), 2=All-time best (the
        // locally stored record, see TimeTrialGhostStore). Defaults to
        // all-time best since that's the most useful default for a returning
        // player and an existing save with no ghost data yet behaves exactly
        // like "no ghost available" either way.
        public int ghostMode = 2;

        // Per-module HUD visibility toggles. All default true so an existing
        // save's HUD looks identical to before these fields existed - only an
        // explicit opt-out in Display Settings hides a module.
        public bool hudShowTimingTower = true;
        public bool hudShowTrackMap = true;
        public bool hudShowInputTelemetry = true;
        public bool hudShowCarStatus = true;
        public bool hudShowRadio = true;
        public bool hudShowRaceControlBanner = true;
        public bool hudShowProgressStrip = true;

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

        // Trophy Cabinet additions. Per-track wins/podiums/poles live in
        // trackAchievements (fastest lap by track already exists via
        // trackRecords + its context field, filtered to "race" context - no
        // need to duplicate that here). Field defaults keep old record files
        // loading cleanly.
        public List<TrackAchievementEntry> trackAchievements = new List<TrackAchievementEntry>();
        public int mostOvertakesInRace;
        public int currentCleanRaceStreak;
        public int bestCleanRaceStreak;
        public int bestWetFinishPosition;
        public int biggestComebackPositions;
        public int championshipsWon;
        public int constructorsChampionshipsWon;
        public int completedSeasons;
    }

    [Serializable]
    public class TrackRecordEntry
    {
        public string trackId;
        public float bestLapTime;
        public string context;
    }

    [Serializable]
    public class TrackAchievementEntry
    {
        public string trackId;
        public int wins;
        public int podiums;
        public int poles;
    }

    // Time-trial ghost: a compact trace of one lap, sampled at a fixed time
    // interval (see RaceManager's ghost recording) rather than every physics
    // frame, so a multi-minute lap stays a few hundred samples instead of
    // thousands. distanceAlongLap lets playback/delta lookups interpolate by
    // track progress instead of only by elapsed time.
    [Serializable]
    public class GhostSample
    {
        public float elapsedSeconds;
        public float distanceAlongLap;
        public Vector3 position;
        public float headingDegrees;
        public float speedKph;
    }

    [Serializable]
    public class GhostLapData
    {
        public string trackId;
        public float lapTime;
        public List<GhostSample> samples = new List<GhostSample>();
    }

    [Serializable]
    public class GhostStoreData
    {
        public List<GhostLapData> bestGhosts = new List<GhostLapData>();
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
