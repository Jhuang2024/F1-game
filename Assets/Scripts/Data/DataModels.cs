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

        // Canonical weighted overall - see RatingCalculator.GetDriverOverall,
        // the single formula every screen/AI/simulation must use. Kept as an
        // instance property purely for convenience call sites already using
        // driver.OverallRating; it delegates rather than duplicating weights.
        public int OverallRating
        {
            get { return RatingCalculator.GetDriverOverall(this); }
        }
    }

    // Part 1/6: the single, canonical rating formula for a driver's overall
    // skill and a car's overall performance - every driver card, transfer
    // decision, AI balancer, teammate comparison, simulated qualifying/race,
    // team-ratings screen, R&D screen, preseason report and season review
    // must go through these two methods rather than computing their own
    // weighted average, so "the same effective car/driver always shows the
    // same overall number" everywhere.
    public static class RatingCalculator
    {
        public static int GetDriverOverall(DriverData driver)
        {
            if (driver == null)
            {
                return 0;
            }

            float weighted =
                driver.pace * 0.20f +
                driver.racecraft * 0.16f +
                driver.qualifying * 0.13f +
                driver.tyreManagement * 0.10f +
                driver.consistency * 0.10f +
                driver.overtaking * 0.08f +
                driver.defending * 0.08f +
                driver.awareness * 0.07f +
                driver.wetSkill * 0.05f +
                driver.experience * 0.03f;
            // aggression and developmentPotential deliberately excluded - a
            // style rating and a long-term ceiling should never directly
            // inflate the current overall rating.
            return Mathf.RoundToInt(weighted);
        }

        public static int GetCarOverall(CarPerformanceData car)
        {
            if (car == null)
            {
                return 0;
            }

            // topSpeed lives on a different numeric scale (an absolute kph
            // value, clamped 315-360 by the upgrade system) than every other
            // stat. Normalization fix (per report - "why is top speed capped
            // at 360... it stops teams from reaching their absolute peak of
            // 125 rating?"): the old divide-by-3.55 mapped the 360 CEILING to
            // only ~101, so a completely maxed car (every stat 125, top speed
            // at the physical cap) could never rate above ~123. The attainable
            // kph range now maps linearly onto the stat band itself: 315 kph
            // -> 45, 360 kph -> 125 - a car at its absolute peak rates at the
            // absolute peak.
            float normalizedTopSpeed = Mathf.Lerp(45f, 125f, Mathf.InverseLerp(315f, 360f, car.topSpeed));
            float weighted =
                normalizedTopSpeed * 0.10f +
                car.acceleration * 0.12f +
                car.cornering * 0.14f +
                car.braking * 0.10f +
                car.reliability * 0.14f +
                car.ersEfficiency * 0.08f +
                car.tyreManagement * 0.10f +
                car.aeroEfficiency * 0.10f +
                car.chassisBalance * 0.08f +
                car.enginePower * 0.04f;
            return Mathf.RoundToInt(weighted);
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
        // Part 5/7: which team's R&D this project belongs to. Empty/null means
        // "the player's own project" for old saves (the player's projects were
        // the only ones that existed before per-team R&D) - AI projects always
        // stamp their owning team's id so one global list can never mix up
        // which team a project belongs to.
        public string teamId;
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

    // Part 5/7: persistent, per-team development state - every team on the
    // grid (not just the player's) accumulates resources, runs projects, and
    // carries completed upgrades from one season into the next. The player's
    // team still authoritatively tracks its own resourcePoints/departmentLevels/
    // completedUpgradeIds/activeUpgradeProjects on CareerSaveData directly (so
    // the existing R&D screen keeps working unchanged) - this state object is
    // created for the player's team too, purely to track archetype/rating
    // bookkeeping in parallel, kept in sync from the Save.* fields rather than
    // being a second source of truth for the player.
    [Serializable]
    public class TeamDevelopmentState
    {
        public string teamId;
        public int resourcePoints;
        public List<int> departmentLevels = new List<int>();
        public List<string> completedUpgradeIds = new List<string>();
        public List<string> failedUpgradeIds = new List<string>();
        public List<ActiveUpgradeProject> activeUpgradeProjects = new List<ActiveUpgradeProject>();

        public int cumulativeTopSpeedDelta;
        public int cumulativeAccelerationDelta;
        public int cumulativeCorneringDelta;
        public int cumulativeBrakingDelta;
        public int cumulativeReliabilityDelta;
        public int cumulativeErsDelta;
        public int cumulativeTyreDelta;
        public int cumulativeAeroDelta;
        public int cumulativeChassisDelta;
        public int cumulativeEngineDelta;

        public int ratingAtSeasonStart;
        public int currentRating;

        // AI-only development behaviour archetype (see
        // CareerManager.AssignTeamDevelopmentArchetype) - ignored for the
        // player's team, whose development is player-directed.
        public string archetype = "Balanced";
        public int lastProjectStartRound;
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
        // Seasons in a row the contract target was missed; drives the escalating
        // season-end consequences. Defaults to 0 so pre-existing saves load clean.
        public int consecutiveContractMisses;
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
        public List<DriverTransferRecord> driverTransferRecords = new List<DriverTransferRecord>();
        public List<DriverRatingModifier> driverRatingModifiers = new List<DriverRatingModifier>();
        public List<SeasonObjective> seasonObjectives = new List<SeasonObjective>();
        public bool preSeasonTestingSeen;

        // One-shot save repair flag: seasons scored before the progression
        // expectation-scale fix (team rank 1..11 compared against driver
        // positions 1..22) accumulated a systematic negative drift into every
        // driver's cumulative rating deltas. CareerManager.
        // RepairProgressionScaleBias removes the common-mode drift exactly once
        // per save; this records that it ran.
        public bool progressionScaleBiasRepaired;

        // Part 5/7: full-grid persistent R&D state, one entry per team
        // (including the player's, for archetype/rating bookkeeping - see
        // TeamDevelopmentState). Created lazily by
        // CareerManager.GetOrCreateTeamDevelopmentState so old saves migrate
        // without losing anything.
        public List<TeamDevelopmentState> teamDevelopmentStates = new List<TeamDevelopmentState>();

        // Part 2/3: in-season driver telemetry accumulators, one entry per
        // (driverId, season). Consumed at season end by GenerateDriverProgression,
        // then kept as a historical record (trimmed to a few recent seasons).
        public List<DriverSeasonPerformance> driverSeasonPerformances = new List<DriverSeasonPerformance>();

        // Part 4: persistent rating profile for a custom (non-existing-driver)
        // player character - never mutated directly; DriverRatingModifier
        // entries keyed by driverId "player" are layered on top of this at
        // read time, exactly like every AI driver's drivers.json entry.
        public DriverData customPlayerDriverBase;

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

    // Driver market: a driver's team reassignment for a given season, layered
    // on top of the static drivers.json teamId at read time (see
    // GameDataRepository.EffectiveTeamId) - never mutates the shared DriverData
    // reference, exactly like TeamPerformanceModifier above never mutates
    // CarPerformanceData. Append-only across seasons; the most recent record
    // for a given driverId (by season) is the one currently in effect.
    [Serializable]
    public class DriverTransferRecord
    {
        public string driverId;
        public string newTeamId;
        public int season;
    }

    // Driver progression: a driver's small season-to-season rating swing,
    // layered on top of the static drivers.json stats at read time (see
    // CareerManager.GetEffectiveDriver) - same never-mutate-the-source
    // convention as TeamPerformanceModifier/DriverTransferRecord. Append-only
    // across seasons, looked up by (driverId, season); each season's entry
    // stores the FULL cumulative delta to date (not just that season's step),
    // exactly like the old ratingDelta field did.
    [Serializable]
    public class DriverRatingModifier
    {
        public string driverId;
        public int season;

        // Legacy field: pre-refactor saves stored one flat delta applied
        // identically to every subrating. CareerManager.MigrateLegacyRatingModifier
        // folds this into the per-stat fields below exactly once (guarded by
        // legacyDeltaMigrated) so an old save's progression is preserved
        // rather than silently discarded, then the per-stat system takes over.
        public int ratingDelta;
        public bool legacyDeltaMigrated;
        // One-time repair marker: an interim build folded the legacy ratingDelta
        // into every per-stat field for the player's OWN seat too (before that was
        // recognised as dumping the AI's invisible random-walk history onto the
        // player - the "suddenly qualifying P22" regression). Saves touched by that
        // build have legacyDeltaMigrated already true with the damage baked in, so
        // the ordinary migration guard skips them. CareerManager.MigrateLegacyRatingModifier
        // uses this flag to undo that fold exactly once for the player's own seat.
        public bool legacyPlayerDeltaRepaired;

        // Independent cumulative deltas, one per meaningful subrating -
        // GetEffectiveDriver applies each to its matching DriverData field
        // (clamped 40-99), instead of one shared delta applied to everything.
        public int paceDelta;
        public int racecraftDelta;
        public int qualifyingDelta;
        public int tyreManagementDelta;
        public int wetSkillDelta;
        public int consistencyDelta;
        public int aggressionDelta;
        public int defendingDelta;
        public int overtakingDelta;
        public int awarenessDelta;
        public int experienceDelta;

        public string trendLabel;

        // The change made specifically AT this season's transition (not
        // cumulative) - lets the UI show "Pace +2, Qualifying +1, Racecraft -1,
        // Overall +1" for the season that just ended.
        public int lastPaceChange;
        public int lastRacecraftChange;
        public int lastQualifyingChange;
        public int lastTyreManagementChange;
        public int lastWetSkillChange;
        public int lastConsistencyChange;
        public int lastAggressionChange;
        public int lastDefendingChange;
        public int lastOvertakingChange;
        public int lastAwarenessChange;
        public int lastExperienceChange;
        public int lastOverallChange;
    }

    // Part 2/8: one driver's accumulated in-season telemetry, built up race by
    // race (see CareerManager.UpdateDriverSeasonPerformanceFromQualifying/
    // FromRace) and consumed once at season end by GenerateDriverProgression,
    // then left in place as a historical record. Never judges a driver by
    // championship position alone - every field here is either a direct
    // measurement (overtakes, penalties) or a same-car teammate/expected-result
    // comparison, the strongest signal available without full lap telemetry.
    [Serializable]
    public class DriverSeasonPerformance
    {
        public string driverId;
        public int season;

        public int racesCompleted;
        public int qualifyingSessions;
        public int wetRacesCompleted;

        // Sum of (expected finish/qualifying position - actual position) across
        // completed sessions - positive means "did better than the car/grid
        // slot suggested they should".
        public float sumFinishVsExpected;
        public float sumQualifyingVsExpected;
        public List<float> finishVsExpectedSamples = new List<float>();

        // Same-car teammate comparison - the strongest single signal per the
        // design brief, since it cancels out car performance entirely.
        public int teammateRaceWins;
        public int teammateRaceLosses;
        public int teammateQualifyingWins;
        public int teammateQualifyingLosses;

        public float sumPositionsGained;
        public int overtakesMade;
        public int lockups;
        public int trackLimitWarnings;
        public float sumFlatSpotPercent;
        public int penalizedRaces;
        public float sumPenaltySeconds;

        // Retirements the driver didn't cause (engine/hydraulics/etc.) never
        // reduce a rating; retirements the driver did cause (collision/spin)
        // do.
        public int mechanicalRetirements;
        public int driverCausedRetirements;

        public float sumWetPositionsGained;
    }

    // Part 8: one driver's rating change at a single season transition,
    // frozen into that season's SeasonArchive so the season-review/driver
    // ratings screens can always show exactly what changed and why, even
    // long after CareerSaveData.driverRatingModifiers has moved on to a
    // newer season's entry.
    [Serializable]
    public class DriverSeasonChangeRecord
    {
        public string driverId;
        public string driverName;
        public int previousOverall;
        public int newOverall;
        public int paceChange;
        public int racecraftChange;
        public int qualifyingChange;
        public int tyreManagementChange;
        public int wetSkillChange;
        public int consistencyChange;
        public int aggressionChange;
        public int defendingChange;
        public int overtakingChange;
        public int awarenessChange;
        public int experienceChange;
        public string performanceSummary;
        public int teammateHeadToHeadWins;
        public int teammateHeadToHeadLosses;
        public int expectedChampionshipPosition;
        public int actualChampionshipPosition;
    }

    // Part 5/8: one team's development summary for a completed season -
    // frozen into that season's SeasonArchive alongside DriverSeasonChangeRecord.
    [Serializable]
    public class TeamDevelopmentRecord
    {
        public string teamId;
        public string teamName;
        public int ratingAtSeasonStart;
        public int ratingAtSeasonEnd;
        public int ratingChange;
        public int upgradesCompleted;
        public int projectsFailed;
        public string strongestDevelopmentArea;
        public int constructorFinish;
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

        // Part 8: full per-driver rating changes and per-team development
        // summaries for this completed season.
        public List<DriverSeasonChangeRecord> driverChanges = new List<DriverSeasonChangeRecord>();
        public List<TeamDevelopmentRecord> teamDevelopment = new List<TeamDevelopmentRecord>();
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

        // Fuel strategy pass: report-facing fuel figures, captured once at
        // finish/retirement. Field initializers keep an older saved race report
        // (written before these existed) deserializing safely at 0/Target/false.
        public float startFuelKg;
        public float finishFuelKg;
        public int fuelLoadChoice = (int)FuelLoadChoice.Target;
        public float liftAndCoastSavedKg;
        public bool fuelStarvedRetirement;
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
        // Gamepad vibration / force-feedback strength (0..1). Consumed by the
        // F1Game.Input force-feedback adapter (GamepadRumbleFeedback).
        public float controllerVibration = 1f;
        public bool useMphUnits;
        public float sceneryDensity = 1f;
        public int graphicsQuality = 2;
        public bool uiAnimations = true;

        // HUD / visual additions.
        public bool compactHud;
        public bool particlesEnabled = true;

        // Accessibility: colour-vision mode for status/semantic colours.
        // 0=None (default palette), 1=Protanopia, 2=Deuteranopia, 3=Tritanopia -
        // matches F1Game.Core.AccessibilityPalette.ColorVisionMode. Field
        // initializer keeps existing saves on the default palette.
        public int colorBlindMode;

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
        // Dynamic track evolution: grip rises slightly over a session as rubber
        // goes down (washed away again by heavy rain) - see
        // RaceManager.UpdateTrackEvolution. On by default; players who want fully
        // static per-lap grip can turn it off.
        public bool trackEvolutionEnabled = true;

        // Fuel strategy: pre-race fuel-load choice, same "plain int field on
        // settings + companion enum for readability" pattern as ersMode above.
        // Defaults to FuelLoadChoice.Target (2) so an old save with no key present
        // starts a race exactly the way a deliberate "target fuel" choice would -
        // enough to finish on normal driving, no free speed and no starvation risk.
        public int fuelLoadChoice = (int)FuelLoadChoice.Target;
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
        Race,
        // Playable practice programs: a free-running session (see
        // RaceManager.StartSession/RaceLaps, GameBootstrap.StartCareerPractice)
        // that never auto-finishes on lap count - the player ends it manually
        // from the pause menu once satisfied, and RaceManager.EvaluatePracticeSession
        // scores the selected program from real session telemetry.
        Practice
    }

    public enum ErsStrategyMode
    {
        Balanced,
        Attack,
        Harvest
    }

    // Fuel strategy pass: pre-race fuel-load choice, expressed as a lap-count
    // delta around the calculated "Target" load (RaceManager.ComputeRaceStartFuelKg).
    // Target = 0 is deliberately the enum's default value (int 2, but every consumer
    // should still read the stored int through this enum rather than assume the
    // ordinal) so an old save with fuelLoadChoice left at its zero-value default
    // would actually resolve to AggressiveUnderfuel if this were ordered
    // differently - GameSettingsData.fuelLoadChoice's own field initializer
    // ((int)FuelLoadChoice.Target) is what actually guarantees the safe default,
    // not enum ordinal position.
    public enum FuelLoadChoice
    {
        AggressiveUnderfuel,
        LightUnderfuel,
        Target,
        Safe,
        Heavy
    }
}
