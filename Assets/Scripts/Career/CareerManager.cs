using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    public class CareerManager
    {
        const string CareerFile = "formula_racing_career.json";
        // Points table moved to F1Game.Race.Rules.ChampionshipPoints (single,
        // unit-tested source of truth); this class now only applies the results.
        const float UpgradeEffectScale = 2.25f;

        // Order matters: index into CareerSaveData.departmentLevels, names match
        // the upgrade `category` strings in upgrades.json exactly.
        public static readonly string[] DepartmentNames =
        {
            "Aerodynamics", "Chassis", "Power Unit", "Durability", "Tyre Management", "ERS"
        };

        public const int MaxDepartmentLevel = 5;
        public const int RiskConservative = 0;
        public const int RiskStandard = 1;
        public const int RiskRush = 2;
        public const int RiskExperimental = 3;
        public const int ProjectInDevelopment = 0;
        public const int ProjectCompleted = 1;
        public const int ProjectFailed = 2;
        public const int ProjectReworkAvailable = 3;

        // Part 20: news article categories, shared by AddNewsArticle callers so
        // a future news screen can filter/tag consistently.
        public const string NewsCategoryRace = "Race";
        public const string NewsCategoryRnd = "R&D";
        public const string NewsCategoryRivalry = "Rivalry";
        public const string NewsCategoryRaceControl = "Race Control";
        public const string NewsCategoryRegulations = "Regulations";
        public const string NewsCategoryRumour = "Paddock Rumour";
        public const string NewsCategoryGeneral = "Career";
        public const string NewsCategoryTransfers = "Driver Market";

        readonly GameDataRepository data;

        public CareerSaveData Save { get; private set; }

        public CareerManager(GameDataRepository repository)
        {
            data = repository;
            Save = LocalJsonStore.Load<CareerSaveData>(CareerFile, null);
            if (Save == null || string.IsNullOrEmpty(Save.playerTeamId))
            {
                StartNewCareer("Player Driver", "williams");
            }
            else
            {
                EnsureStandingLists();
            }
        }

        public void StartNewCareer(string driverName, string teamId)
        {
            StartNewCareer(driverName, teamId, false, "");
        }

        public void StartNewCareer(string driverName, string teamId, bool useExistingDriver, string selectedDriverId)
        {
            if (string.IsNullOrEmpty(driverName))
            {
                driverName = "Player Driver";
            }

            // Bug fix: this used to gate everything on the `useExistingDriver` flag
            // at the moment StartNewCareer runs rather than on whether a real
            // driver was actually resolved. Toggling the existing/custom mode
            // button after picking a real driver (selectedDriverId stays set,
            // useExistingDriver flips to false) used to leave that real driver
            // fully in the AI roster while the player's name/team still matched
            // them exactly - a visible duplicate (same name racing as both the
            // player and an AI car). Keying off `selected != null` instead means
            // "a real driver was actually picked" is the only thing that matters,
            // regardless of how the toggle happens to be sitting. Guarding the
            // FindDriver call against an empty id avoids its own fallback
            // (returns some default driver rather than null for an unresolved id)
            // ever masquerading as a real pick when the player never chose one.
            DriverData selected = string.IsNullOrEmpty(selectedDriverId) ? null : data.FindDriver(selectedDriverId);
            if (selected != null)
            {
                driverName = selected.displayName;
                teamId = selected.teamId;
            }

            Save = new CareerSaveData
            {
                currentSeason = 1,
                currentRound = 1,
                playerDriverName = driverName,
                playerTeamId = teamId,
                useExistingDriver = selected != null,
                selectedDriverId = selected != null ? selected.id : "",
                rivalDriverId = PickRivalId(teamId, selectedDriverId),
                contractTargetPosition = ContractTargetForTeam(teamId),
                reputation = 25,
                resourcePoints = 500,
                difficultyIndex = 1,
                driverStandings = data.CreateInitialDriverStandings(driverName, teamId, selected != null ? selected.id : ""),
                constructorStandings = data.CreateInitialConstructorStandings()
            };
            if (selected != null)
            {
                Save.driverStandings.RemoveAll(entry => entry.id == selected.id);
            }
            EnsureRndState();

            // The rival exists from race one, so say who it is - without this
            // announcement the pick was invisible until the first Rivalry-screen
            // visit or the first head-to-head news article rounds later.
            DriverData initialRival = string.IsNullOrEmpty(Save.rivalDriverId) ? null : data.FindDriver(Save.rivalDriverId);
            if (initialRival != null)
            {
                AddNewsArticle(
                    "Season rival: " + initialRival.displayName,
                    "The paddock pencils in " + initialRival.displayName + " as " + Save.playerDriverName +
                    "'s benchmark this season - the closest-matched car on the grid. Beat them on track to build reputation; the rivalry updates as the championship takes shape.",
                    NewsCategoryRivalry);
            }

            Write();
        }

        public void Write()
        {
            LocalJsonStore.Save(CareerFile, Save);
        }

        public CalendarEventData CurrentEvent()
        {
            return data.FindEventForRound(Save.currentRound);
        }

        public bool HasQualifyingForCurrentRound()
        {
            if (Save == null || Save.qualifyingResults == null)
            {
                return false;
            }

            for (int i = 0; i < Save.qualifyingResults.Count; i++)
            {
                QualifyingResultRecord record = Save.qualifyingResults[i];
                if (record.season == Save.currentSeason && record.round == Save.currentRound && record.results != null && record.results.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public CarPerformanceData GetPlayerCar()
        {
            TeamData team = data.FindTeam(Save.playerTeamId);
            return team == null ? data.Cars.cars[0] : data.FindCar(team.carPerformanceId);
        }

        // Part 20: race-weekend depth data for the current round - practice
        // program summary, a setup recommendation, a tyre strategy preview, a
        // weather forecast (built on the existing WeatherState/weatherProfile
        // system), track characteristics, and a pit window estimate. Generated
        // on demand rather than persisted, since it's fully re-derivable from
        // calendar + career state.
        public RaceWeekendBriefing GenerateRaceWeekendBriefing()
        {
            CalendarEventData currentEvent = CurrentEvent();
            RaceWeekendBriefing briefing = new RaceWeekendBriefing();
            briefing.track = data.GetTrackCharacteristics(currentEvent);
            briefing.weather = BuildWeatherForecast(currentEvent);
            briefing.practice = BuildPracticeProgramSummary();
            CarPerformanceData tunedCar = ApplyCareerUpgrades(GetPlayerCar());
            briefing.setup = BuildSetupRecommendation(briefing.track, tunedCar);
            briefing.tyreStrategy = BuildTyreStrategyPreview(briefing.track, briefing.weather, tunedCar);
            briefing.pitWindow = BuildPitWindowEstimate(currentEvent, briefing.tyreStrategy.recommendedStopCount);
            return briefing;
        }

        // Heuristic forecast built from CalendarEventData.weatherProfile (the
        // same field RaceManager reads to seed Track.weather / decide rain
        // transitions) - no separate weather system, just a read of the
        // existing one.
        WeatherForecastData BuildWeatherForecast(CalendarEventData currentEvent)
        {
            WeatherForecastData forecast = new WeatherForecastData();
            string profile = currentEvent == null || string.IsNullOrEmpty(currentEvent.weatherProfile) ? "" : currentEvent.weatherProfile.ToLowerInvariant();
            bool mixed = profile.Contains("mixed");
            bool wet = profile.Contains("wet") || profile.Contains("rain");
            bool hot = profile.Contains("hot") || profile.Contains("desert");

            WeatherState baseState = wet ? WeatherState.LightRain : (profile.Contains("cloud") ? WeatherState.Cloudy : WeatherState.Clear);
            forecast.practiceForecast = baseState;
            forecast.qualifyingForecast = mixed && Random.value < 0.35f ? WeatherState.Cloudy : baseState;
            forecast.raceForecast = mixed
                ? (Random.value < 0.4f ? WeatherState.LightRain : baseState)
                : (wet ? (Random.value < 0.3f ? WeatherState.HeavyRain : WeatherState.LightRain) : baseState);

            forecast.rainChancePercent = wet ? 70 : mixed ? 40 : hot ? 5 : 15;
            forecast.summaryText = mixed
                ? "Mixed conditions expected - showers may move through at any point across the weekend."
                : wet
                    ? "Wet weekend expected - rain is likely to affect multiple sessions."
                    : hot
                        ? "Hot and dry all weekend, with track temperature the main tyre concern."
                        : "Largely stable conditions expected across the weekend.";
            return forecast;
        }

        static readonly Dictionary<string, string> PracticeProgramDisplayNames = new Dictionary<string, string>
        {
            { "acclimatisation", "Track Acclimatisation" },
            { "tyreManagement", "Tyre Management" },
            { "ersManagement", "ERS Management" },
            { "qualifyingPace", "Qualifying Pace" },
            { "racePace", "Race Pace" }
        };

        // Reads the "s{season}_r{round}_{programId}" completion keys the
        // practice-program UI writes into Save.completedPracticePrograms and
        // summarises just the current round's entries.
        PracticeProgramSummaryData BuildPracticeProgramSummary()
        {
            PracticeProgramSummaryData summary = new PracticeProgramSummaryData();
            string prefix = "s" + Save.currentSeason + "_r" + Save.currentRound + "_";
            if (Save.completedPracticePrograms != null)
            {
                for (int i = 0; i < Save.completedPracticePrograms.Count; i++)
                {
                    string key = Save.completedPracticePrograms[i];
                    if (string.IsNullOrEmpty(key) || !key.StartsWith(prefix))
                    {
                        continue;
                    }

                    string programId = key.Substring(prefix.Length);
                    string friendly;
                    summary.completedProgramNames.Add(PracticeProgramDisplayNames.TryGetValue(programId, out friendly) ? friendly : programId);
                }
            }

            summary.programsCompleted = summary.completedProgramNames.Count;
            summary.qualityRating = Save.practiceQualityThisRound;
            summary.summaryText = summary.programsCompleted == 0
                ? "No practice programs completed yet this weekend."
                : summary.programsCompleted + " of " + summary.programsAvailable + " practice programs complete (" +
                  string.Join(", ", summary.completedProgramNames.ToArray()) + ").";
            return summary;
        }

        SetupRecommendationData BuildSetupRecommendation(TrackCharacteristicsSummaryData track, CarPerformanceData car)
        {
            SetupRecommendationData setup = new SetupRecommendationData();
            if (track.downforceLevel == "High")
            {
                setup.recommendedFrontWing = 4;
                setup.recommendedRearWing = 4;
            }
            else if (track.downforceLevel == "Low")
            {
                setup.recommendedFrontWing = 2;
                setup.recommendedRearWing = 2;
            }

            setup.recommendedRideHeight = track.streetCircuit ? 4 : 3;
            setup.recommendedSuspension = track.tyreDegradation == "High" ? 2 : 3;
            setup.recommendedBrakeBias = track.streetCircuit ? 4 : 3;

            if (car != null && car.chassisBalance < 70)
            {
                setup.recommendedRearWing = Mathf.Min(5, setup.recommendedRearWing + 1);
            }

            setup.reasoning = "Recommendation based on " + track.displayName + "'s " + track.downforceLevel.ToLowerInvariant() +
                "-downforce, " + track.tyreDegradation.ToLowerInvariant() + "-degradation characteristics" +
                (track.streetCircuit ? ", plus extra ride height for the kerbs." : ".");
            return setup;
        }

        TyreStrategyPreviewData BuildTyreStrategyPreview(TrackCharacteristicsSummaryData track, WeatherForecastData weather, CarPerformanceData car)
        {
            TyreStrategyPreviewData preview = new TyreStrategyPreviewData();
            bool wetRace = weather.raceForecast == WeatherState.LightRain || weather.raceForecast == WeatherState.HeavyRain;
            if (wetRace)
            {
                preview.recommendedStopCount = weather.raceForecast == WeatherState.HeavyRain ? 2 : 1;
                preview.recommendedStartCompound = weather.raceForecast == WeatherState.HeavyRain ? "Wet" : "Intermediate";
                preview.recommendedSecondCompound = "Intermediate";
                preview.recommendedThirdCompound = "Medium";
                preview.reasoning = "Wet forecast for the race - start on " + preview.recommendedStartCompound + "s and react to the track drying out.";
                return preview;
            }

            int tyreRating = car != null ? car.tyreManagement : 80;
            bool highDeg = track.tyreDegradation == "High";
            preview.recommendedStopCount = highDeg || tyreRating < 70 ? 2 : 1;
            preview.recommendedStartCompound = track.streetCircuit ? "Medium" : "Soft";
            preview.recommendedSecondCompound = preview.recommendedStopCount >= 2 ? "Medium" : "Hard";
            preview.recommendedThirdCompound = preview.recommendedStopCount >= 2 ? "Hard" : "";
            preview.reasoning = highDeg
                ? track.displayName + " chews through tyres - a " + preview.recommendedStopCount + "-stop looks safer than pushing one set too far."
                : "Tyre wear looks manageable at " + track.displayName + " - a " + preview.recommendedStopCount + "-stop should be quickest if track position allows.";
            return preview;
        }

        PitWindowEstimateData BuildPitWindowEstimate(CalendarEventData currentEvent, int stopCount)
        {
            PitWindowEstimateData window = new PitWindowEstimateData();
            int totalLaps = currentEvent == null
                ? 20
                : (currentEvent.laps25Percent > 0 ? currentEvent.laps25Percent : (currentEvent.laps5 > 0 ? currentEvent.laps5 : Mathf.Max(3, currentEvent.laps3)));
            window.totalLaps = totalLaps;
            int stintCount = Mathf.Max(1, stopCount + 1);
            window.estimatedStintLength = Mathf.Max(1, totalLaps / stintCount);
            window.earliestLap = Mathf.Clamp(Mathf.RoundToInt(totalLaps * 0.22f), 1, totalLaps);
            window.optimalLap = Mathf.Clamp(totalLaps / stintCount, 1, totalLaps);
            window.latestLap = Mathf.Clamp(Mathf.RoundToInt(totalLaps * 0.78f), window.earliestLap, totalLaps);
            return window;
        }

        public void ApplyRaceResults(CalendarEventData raceEvent, List<RaceResultEntry> results)
        {
            ApplyRaceResults(raceEvent, results, -1, -1, -1);
        }

        // incidentCount/safetyCarDeploymentCount/aiOvertakesCompletedCount mirror
        // RaceManager's own public IncidentCount / SafetyCarDeploymentCount /
        // AiOvertakesCompletedCount fields (Part 20 race-report plumbing) - pass
        // -1 for any that aren't available to mean "no data" rather than "zero".
        // Legacy 5-arg overload: red flag data defaults to "no data" (-1/"").
        public void ApplyRaceResults(CalendarEventData raceEvent, List<RaceResultEntry> results, int incidentCount, int safetyCarDeploymentCount, int aiOvertakesCompletedCount)
        {
            ApplyRaceResults(raceEvent, results, incidentCount, safetyCarDeploymentCount, aiOvertakesCompletedCount, -1, "");
        }

        public void ApplyRaceResults(CalendarEventData raceEvent, List<RaceResultEntry> results, int incidentCount, int safetyCarDeploymentCount, int aiOvertakesCompletedCount, int redFlagCount, string redFlagReason)
        {
            // Snapshot the player's standing before this race's points land, so
            // the post-race report can show actual movement rather than just an
            // after-the-fact total (Part 2 report / Championship Impact card).
            RaceResultEntry playerEntry = results.Find(entry => entry.isPlayer);
            int driverPositionBefore = playerEntry != null ? FindStandingPosition(Save.driverStandings, playerEntry.driverId) : -1;
            int driverPointsBefore = playerEntry != null ? FindStandingPoints(Save.driverStandings, playerEntry.driverId) : 0;
            int constructorPositionBefore = playerEntry != null ? FindStandingPosition(Save.constructorStandings, playerEntry.teamId) : -1;

            for (int i = 0; i < results.Count; i++)
            {
                int points = F1Game.Race.Rules.ChampionshipPoints.ForPosition(i + 1);
                results[i].finishingPosition = i + 1;
                results[i].points = points;
                ApplyDriverPoints(results[i], points);
                ApplyConstructorPoints(results[i].teamId, points, results[i].finishingPosition);
            }

            RaceResultRecord record = new RaceResultRecord
            {
                season = Save.currentSeason,
                round = Save.currentRound,
                eventName = raceEvent != null ? raceEvent.displayName : "Prototype GP",
                results = results
            };
            Save.raceResults.Add(record);

            RaceReportRecord report = BuildRaceReport(raceEvent, results, incidentCount, safetyCarDeploymentCount, aiOvertakesCompletedCount, redFlagCount, redFlagReason);
            Save.raceReports.Add(report);
            // Season-end fix: this was capped at 12 - fine for "recent reports"
            // display, but BuildSeasonArchive needs every report from THIS
            // season (up to a full 24-round calendar) to accurately total red
            // flags/safety cars/major incidents for the season review. Raised
            // to comfortably cover a full season plus a buffer into the next.
            while (Save.raceReports.Count > 40)
            {
                Save.raceReports.RemoveAt(0);
            }

            GenerateRaceControlNews(report);
            GenerateAiPerformanceNews(report);

            RaceResultEntry player = results.Find(entry => entry.isPlayer);
            if (player != null)
            {
                Save.resourcePoints += Mathf.RoundToInt((90 + Mathf.Max(0, 11 - player.finishingPosition) * 15) * Save.currentSeasonResourceMultiplier);
                int targetDelta = Mathf.Max(-4, Save.contractTargetPosition - player.finishingPosition);
                Save.reputation += player.finishingPosition <= 3 ? 4 : (targetDelta >= 0 ? 2 : -1);
                Save.resourcePoints += Mathf.RoundToInt(Mathf.Max(0, targetDelta) * 12 * Save.currentSeasonResourceMultiplier);
                UpdateRaceRivalryAndForm(results, player, raceEvent);
                UpdateSeasonObjectivesProgress(results, player, raceEvent);
                ApplyFuelStrategyReward(player);
            }

            UpdateDriverSeasonPerformanceFromRace(raceEvent, results);
            AdvanceUpgradeProjects();
            AdvanceAiTeamDevelopment();
            SyncPlayerTeamDevelopmentState();

            Save.currentRound++;
            if (Save.currentRound > data.Calendar.events.Count)
            {
                // Season-end root-cause fix: this used to silently roll straight
                // into "season+1, round 1" with nothing preserved of the season
                // that just finished - no archive, no review, no visible cue
                // beyond the Career Hub header ticking over. Snapshot a full,
                // frozen SeasonArchive of the season that just ended (final
                // standings, player/teammate/team stats, awards, the regulation
                // changes and team-performance swings that were in effect all
                // year) BEFORE touching any live state, so nothing about the
                // completed season is ever lost - then reset only what a new
                // season should reset (see BeginNewSeason) and flag the
                // transition so the post-race report routes into a proper
                // season-review flow instead of straight back to the hub.
                SortStandings(Save.driverStandings);
                SortStandings(Save.constructorStandings);
                // Standings-drift fix: make sure the season's FINAL standings are
                // internally consistent before they're frozen into the archive -
                // an archived mismatch would be permanent (SeasonArchive is never
                // touched again after this point).
                ValidateCurrentSeasonStandingsIntegrity();
                SeasonArchive completedSeason = BuildSeasonArchive(raceEvent);
                Save.seasonArchives.Add(completedSeason);
                Save.lastCompletedSeasonNumber = Save.currentSeason;

                // Trophy Cabinet: season-level trophies, recorded once per
                // completed season using the same archive snapshot above -
                // "player" is the standings id EnsurePlayerReplacesDriverSeat
                // always assigns the player's own entry (see driverChampionId/
                // constructorChampionId comparisons elsewhere in this file).
                PlayerRecordsStore.RecordSeasonCompleted(
                    completedSeason.driverChampionId == "player",
                    completedSeason.constructorChampionId == Save.playerTeamId);

                Save.currentRound = 1;
                Save.currentSeason++;
                BeginNewSeason(completedSeason);

                Save.seasonTransitionPending = true;
                Save.seasonPhase = CareerSeasonPhase.FinalRaceComplete;
            }

            Save.lastQualifyingResults = new List<QualifyingResultEntry>();

            SortStandings(Save.driverStandings);
            SortStandings(Save.constructorStandings);
            // Standings-drift fix: catch and repair any constructor/driver
            // mismatch immediately after every race is applied, before the
            // post-race report reads "after" standings positions below.
            ValidateCurrentSeasonStandingsIntegrity();

            if (playerEntry != null)
            {
                report.standings.driverPositionBefore = driverPositionBefore;
                report.standings.driverPointsBefore = driverPointsBefore;
                report.standings.constructorPositionBefore = constructorPositionBefore;
                report.standings.driverPositionAfter = FindStandingPosition(Save.driverStandings, playerEntry.driverId);
                report.standings.driverPointsAfter = FindStandingPoints(Save.driverStandings, playerEntry.driverId);
                report.standings.constructorPositionAfter = FindStandingPosition(Save.constructorStandings, playerEntry.teamId);
                GenerateStandingsMovementNews(report.standings, raceEvent);
            }

            GenerateFillerNewsIfNeeded();
            Write();
        }

        // Championship position lookup by id in an already-sorted standings
        // list; -1 means "no entry yet" (debut) rather than a real position.
        int FindStandingPosition(List<StandingEntry> standings, string id)
        {
            if (standings == null || string.IsNullOrEmpty(id))
            {
                return -1;
            }

            for (int i = 0; i < standings.Count; i++)
            {
                if (standings[i].id == id)
                {
                    return i + 1;
                }
            }

            return -1;
        }

        int FindStandingPoints(List<StandingEntry> standings, string id)
        {
            if (standings == null || string.IsNullOrEmpty(id))
            {
                return 0;
            }

            StandingEntry entry = standings.Find(item => item.id == id);
            return entry != null ? entry.points : 0;
        }

        // Only worth a headline when the player actually gained ground and had
        // a real prior position to gain it from (debut races have no "before").
        void GenerateStandingsMovementNews(StandingsMovementSummary standings, CalendarEventData raceEvent)
        {
            if (standings.driverPositionBefore <= 0 || standings.driverPositionAfter <= 0)
            {
                return;
            }

            if (standings.driverPositionAfter < standings.driverPositionBefore)
            {
                string eventName = raceEvent != null ? raceEvent.displayName : "the race";
                AddNewsArticle(
                    Save.playerDriverName + " climbs to P" + standings.driverPositionAfter + " in the championship",
                    eventName + " moves " + Save.playerDriverName + " up from P" + standings.driverPositionBefore + " to P" +
                    standings.driverPositionAfter + " in the drivers' standings on " + standings.driverPointsAfter + " points.",
                    NewsCategoryGeneral);
            }
        }

        // Part 20: aggregates the classification RaceManager already computed
        // (race control counts, if provided) with the per-driver result fields
        // RaceParticipant.ToResultEntry already fills in (pit stops, overtakes,
        // lockups, flat spots, track limit warnings, penalties, strategy) into
        // one report record for a results/report screen.
        RaceReportRecord BuildRaceReport(CalendarEventData raceEvent, List<RaceResultEntry> results, int incidentCount, int safetyCarDeploymentCount, int aiOvertakesCompletedCount, int redFlagCount, string redFlagReason)
        {
            RaceReportRecord report = new RaceReportRecord
            {
                season = Save.currentSeason,
                round = Save.currentRound,
                eventName = raceEvent != null ? raceEvent.displayName : "Prototype GP"
            };

            report.raceControl.incidentCount = incidentCount;
            report.raceControl.safetyCarDeployments = safetyCarDeploymentCount;
            report.raceControl.redFlagCount = redFlagCount;
            report.raceControl.redFlagReason = redFlagReason;
            report.raceControl.wasChaotic = incidentCount >= 5 || safetyCarDeploymentCount >= 2 || redFlagCount > 0;
            report.raceControl.narrative = BuildRaceControlNarrative(incidentCount, safetyCarDeploymentCount, report.eventName);

            int lockups = 0;
            int trackLimitWarnings = 0;
            int heavyLockupDrivers = 0;
            int pitStopTotal = 0;
            float flatSpotTotal = 0f;
            int penalizedDrivers = 0;
            float penaltySecondsTotal = 0f;
            Dictionary<string, int> penaltyReasons = new Dictionary<string, int>();
            Dictionary<string, int> finalCompoundCounts = new Dictionary<string, int>();
            for (int i = 0; i < results.Count; i++)
            {
                RaceResultEntry entry = results[i];
                lockups += entry.lockups;
                trackLimitWarnings += entry.trackLimitWarnings;
                flatSpotTotal += entry.flatSpotPercent;
                pitStopTotal += entry.pitStops;
                if (entry.lockups >= 3)
                {
                    heavyLockupDrivers++;
                }

                if (entry.penaltiesSeconds > 0f)
                {
                    penalizedDrivers++;
                    penaltySecondsTotal += entry.penaltiesSeconds;
                    string reason = string.IsNullOrEmpty(entry.penaltyReason) ? "Time penalty" : entry.penaltyReason;
                    penaltyReasons[reason] = penaltyReasons.ContainsKey(reason) ? penaltyReasons[reason] + 1 : 1;
                }

                if (!string.IsNullOrEmpty(entry.tyreCompound))
                {
                    finalCompoundCounts[entry.tyreCompound] = finalCompoundCounts.ContainsKey(entry.tyreCompound) ? finalCompoundCounts[entry.tyreCompound] + 1 : 1;
                }
            }

            report.incidents.totalLockups = lockups;
            report.incidents.totalTrackLimitWarnings = trackLimitWarnings;
            report.incidents.averageFlatSpotPercent = results.Count > 0 ? flatSpotTotal / results.Count : 0f;
            report.incidents.driversWithHeavyLockups = heavyLockupDrivers;

            report.penalties.driversPenalized = penalizedDrivers;
            report.penalties.totalPenaltySeconds = penaltySecondsTotal;
            report.penalties.mostCommonReason = PickMostCommonKey(penaltyReasons);

            RaceResultEntry player = results.Find(entry => entry.isPlayer);
            report.strategy.averagePitStops = results.Count > 0 ? pitStopTotal / (float)results.Count : 0f;
            report.strategy.playerPitStops = player != null ? player.pitStops : 0;
            report.strategy.playerStrategyText = player != null ? player.strategySummary : "";
            report.strategy.mostCommonFinalCompound = PickMostCommonKey(finalCompoundCounts);

            int aiOvertakes = aiOvertakesCompletedCount >= 0 ? aiOvertakesCompletedCount : SumAiOvertakes(results);
            report.aiPerformance.totalAiOvertakes = aiOvertakes;
            RaceResultEntry standoutAi = FindStandoutAiDriver(results);
            if (standoutAi != null)
            {
                report.aiPerformance.standoutAiDriverName = standoutAi.driverName;
                report.aiPerformance.standoutAiPositionsGained = standoutAi.gridPosition - standoutAi.finishingPosition;
            }

            report.headline = BuildRaceReportHeadline(report, player);
            return report;
        }

        string BuildRaceControlNarrative(int incidentCount, int safetyCarDeploymentCount, string eventName)
        {
            if (safetyCarDeploymentCount < 0 && incidentCount < 0)
            {
                return "Race control data unavailable for this round.";
            }

            if (safetyCarDeploymentCount >= 2 || incidentCount >= 6)
            {
                return "Safety car chaos at " + eventName + " - race control was called into action repeatedly.";
            }

            if (safetyCarDeploymentCount == 1)
            {
                return "A single safety car period reshuffled the order at " + eventName + ".";
            }

            if (incidentCount >= 3)
            {
                return "A scrappy, incident-filled race at " + eventName + ", though it stayed green.";
            }

            return "A clean race at " + eventName + " with race control largely a spectator.";
        }

        string BuildRaceReportHeadline(RaceReportRecord report, RaceResultEntry player)
        {
            string flavor = report.raceControl.wasChaotic ? "chaotic" : "controlled";
            if (player != null)
            {
                return "Race Report - " + report.eventName + ": " + player.driverName + " finishes P" + player.finishingPosition + " in a " + flavor + " race.";
            }

            return "Race Report - " + report.eventName + ": a " + flavor + " race.";
        }

        string PickMostCommonKey(Dictionary<string, int> counts)
        {
            string best = "";
            int bestCount = 0;
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Value > bestCount)
                {
                    bestCount = pair.Value;
                    best = pair.Key;
                }
            }

            return best;
        }

        int SumAiOvertakes(List<RaceResultEntry> results)
        {
            int total = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].isPlayer)
                {
                    total += results[i].overtakesMade;
                }
            }

            return total;
        }

        RaceResultEntry FindStandoutAiDriver(List<RaceResultEntry> results)
        {
            RaceResultEntry best = null;
            int bestGain = 4;
            for (int i = 0; i < results.Count; i++)
            {
                RaceResultEntry entry = results[i];
                if (entry.isPlayer)
                {
                    continue;
                }

                int gain = entry.gridPosition - entry.finishingPosition;
                if (gain > bestGain)
                {
                    bestGain = gain;
                    best = entry;
                }
            }

            return best;
        }

        // Only worth a headline when it was a genuinely notable safety car
        // period or incident-heavy race - safetyCarDeploymentCount < 0 means
        // the caller didn't supply race-control stats, so stay silent rather
        // than reporting "zero" as if it were real data.
        void GenerateRaceControlNews(RaceReportRecord report)
        {
            if (report.raceControl.safetyCarDeployments < 0)
            {
                return;
            }

            // Red flag news takes priority over the safety-car/incident
            // articles below - it's a rarer, bigger story than either, so it
            // should never be crowded out by a routine "safety car shuffles
            // the order" article from the same race.
            if (report.raceControl.redFlagCount > 0)
            {
                string reason = string.IsNullOrEmpty(report.raceControl.redFlagReason) ? "a serious incident" : report.raceControl.redFlagReason.ToLowerInvariant();
                AddNewsArticle(
                    "Red flag stops " + report.eventName,
                    "Race control suspended " + report.eventName + " after " + reason + ". The race resumed under a rolling restart" +
                    (report.raceControl.redFlagCount > 1 ? ", with a second suspension before it was over." : "."),
                    NewsCategoryRaceControl);
                return;
            }

            if (report.raceControl.safetyCarDeployments >= 2)
            {
                AddNewsArticle(
                    "Safety car chaos overshadows " + report.eventName,
                    "Race control deployed the safety car " + report.raceControl.safetyCarDeployments + " times amid " +
                    Mathf.Max(0, report.raceControl.incidentCount) + " recorded incidents at " + report.eventName + ", scrambling strategy up and down the field.",
                    NewsCategoryRaceControl);
            }
            else if (report.raceControl.safetyCarDeployments == 1)
            {
                AddNewsArticle(
                    "Safety car shuffles the order at " + report.eventName,
                    "A single safety car period at " + report.eventName + " bunched the field and forced a round of early strategy calls.",
                    NewsCategoryRaceControl);
            }
            else if (report.raceControl.incidentCount >= 5)
            {
                AddNewsArticle(
                    "Scrappy afternoon at " + report.eventName,
                    report.eventName + " stayed green throughout, but race control logged " + report.raceControl.incidentCount +
                    " incidents as drivers pushed the limits.",
                    NewsCategoryRaceControl);
            }
        }

        void GenerateAiPerformanceNews(RaceReportRecord report)
        {
            if (string.IsNullOrEmpty(report.aiPerformance.standoutAiDriverName) || report.aiPerformance.standoutAiPositionsGained < 5)
            {
                return;
            }

            AddNewsArticle(
                report.aiPerformance.standoutAiDriverName + " carves through the field at " + report.eventName,
                report.aiPerformance.standoutAiDriverName + " gained " + report.aiPerformance.standoutAiPositionsGained + " places at " +
                report.eventName + ", one of the drives of the weekend.",
                NewsCategoryRace);
        }

        // Part 20: regulation-rumour/silly-season filler articles for flavor
        // between races - templated but built from real save state (team,
        // rival, standings, resources, next event) so they aren't generic.
        void GenerateFillerNewsIfNeeded()
        {
            if (Random.value > 0.4f)
            {
                return;
            }

            TeamData playerTeam = data.FindTeam(Save.playerTeamId);
            string teamName = playerTeam != null ? playerTeam.name : "the team";
            DriverData rival = string.IsNullOrEmpty(Save.rivalDriverId) ? null : data.FindDriver(Save.rivalDriverId);
            CalendarEventData nextEvent = data.FindEventForRound(Save.currentRound);
            string nextTrackName = nextEvent != null ? nextEvent.displayName : "the next round";
            int position = PlayerStandingPosition();
            string focusDepartment = Save.regulationAffectedCategories != null && Save.regulationAffectedCategories.Count > 0
                ? Save.regulationAffectedCategories[Random.Range(0, Save.regulationAffectedCategories.Count)]
                : DepartmentNames[Random.Range(0, DepartmentNames.Length)];

            List<KeyValuePair<string, string>> templates = new List<KeyValuePair<string, string>>();
            templates.Add(new KeyValuePair<string, string>(
                "Paddock buzzes over " + teamName + "'s " + focusDepartment.ToLowerInvariant() + " plans",
                "Sources in the paddock suggest " + teamName + " is quietly reshuffling its " + focusDepartment.ToLowerInvariant() +
                " programme ahead of " + nextTrackName + ", though nothing has been confirmed."));
            templates.Add(new KeyValuePair<string, string>(
                "Silly season speculation grows",
                "With several seats still unresolved for next year, rumours continue to swirl in the paddock about who will partner whom."));
            templates.Add(new KeyValuePair<string, string>(
                "Contract talk follows " + Save.playerDriverName,
                Save.playerDriverName + " currently sits P" + position + " in the standings against a target of P" + Save.contractTargetPosition +
                " - insiders say the team is watching closely."));
            templates.Add(new KeyValuePair<string, string>(
                "FIA scrutiny on " + focusDepartment + " regulations",
                "Technical delegates are reportedly reviewing the " + focusDepartment.ToLowerInvariant() + " regulations again, with teams bracing for another shake-up."));
            templates.Add(new KeyValuePair<string, string>(
                nextTrackName + " expected to reward bold strategy",
                "Strategists are already debating tyre allocation for " + nextTrackName + ", with several teams expected to gamble on an aggressive approach."));
            templates.Add(new KeyValuePair<string, string>(
                teamName + " budget speculation",
                "With " + Save.resourcePoints + " development points banked, whispers suggest " + teamName + " could accelerate its upgrade programme in the coming rounds."));
            templates.Add(new KeyValuePair<string, string>(
                "Fans debate the championship picture",
                "With " + teamName + " sitting in the " + CompetitivenessLabel(playerTeam) + " bracket, fans are debating how much further the team can climb this season."));
            if (rival != null)
            {
                templates.Add(new KeyValuePair<string, string>(
                    rival.displayName + " talked up ahead of " + nextTrackName,
                    "Pundits are tipping " + rival.displayName + " for a strong showing at " + nextTrackName +
                    ", setting up another chapter in the rivalry with " + Save.playerDriverName + "."));
            }

            int index = Random.Range(0, templates.Count);
            AddNewsArticle(templates[index].Key, templates[index].Value, NewsCategoryRumour);
        }

        int PlayerStandingPosition()
        {
            for (int i = 0; i < Save.driverStandings.Count; i++)
            {
                if (Save.driverStandings[i].id == "player")
                {
                    return i + 1;
                }
            }

            return Save.driverStandings.Count;
        }

        string CompetitivenessLabel(TeamData team)
        {
            if (team == null)
            {
                return "midfield";
            }

            return team.reputation >= 88 ? "championship-contender" :
                team.reputation >= 78 ? "front-running" :
                team.reputation >= 68 ? "midfield" : "backmarker";
        }

        public void ApplyQualifyingResults(CalendarEventData raceEvent, List<QualifyingResultEntry> results)
        {
            Save.lastQualifyingResults = results;
            Save.qualifyingResults.Add(new QualifyingResultRecord
            {
                season = Save.currentSeason,
                round = Save.currentRound,
                eventName = raceEvent != null ? raceEvent.displayName : "Prototype GP",
                results = results
            });

            // Part 2/10: save qualifying performance for every driver into this
            // season's accumulator - ratings are not touched yet, only at
            // season end (GenerateDriverProgression).
            UpdateDriverSeasonPerformanceFromQualifying(results);

            QualifyingResultEntry player = results.Find(entry => entry.isPlayer);
            if (player != null)
            {
                Save.resourcePoints += Mathf.RoundToInt(Mathf.Max(8, 42 - player.position * 4) * Save.currentSeasonResourceMultiplier);
                Save.reputation += player.position <= Save.contractTargetPosition ? 1 : 0;
                UpdateQualifyingRivalry(results, player);
                if (player.position == 1 && Save.seasonObjectives != null)
                {
                    SeasonObjective poleObjective = Save.seasonObjectives.Find(o => o.id == "first_pole");
                    if (poleObjective != null && !poleObjective.achieved)
                    {
                        poleObjective.currentValue = 1;
                        poleObjective.achieved = true;
                    }
                }
            }

            Write();
        }

        // Part 3: rivalry / teammate battle. Head-to-head tallies, last-3-race
        // form, a reputation snapshot for the trend card, and a handful of
        // headlines for the career news feed - all derived from this race's
        // actual classification, not scripted.
        void UpdateRaceRivalryAndForm(List<RaceResultEntry> results, RaceResultEntry player, CalendarEventData raceEvent)
        {
            string eventName = raceEvent != null ? raceEvent.displayName : "the race";
            RaceResultEntry teammate = results.Find(entry => entry.teamId == Save.playerTeamId && entry.driverId != "player");
            if (teammate != null)
            {
                if (player.finishingPosition < teammate.finishingPosition)
                {
                    Save.teammateRaceWins++;
                    AddNewsArticle(
                        Save.playerDriverName + " beats teammate again at " + eventName,
                        Save.playerDriverName + " finished P" + player.finishingPosition + " to " + teammate.driverName + "'s P" + teammate.finishingPosition +
                        " at " + eventName + ", extending the intra-team head-to-head to " + Save.teammateRaceWins + "-" + Save.teammateRaceLosses + ".",
                        NewsCategoryRivalry);
                }
                else if (player.finishingPosition > teammate.finishingPosition)
                {
                    Save.teammateRaceLosses++;
                    AddNewsArticle(
                        teammate.driverName + " gets the upper hand at " + eventName,
                        teammate.driverName + " out-raced " + Save.playerDriverName + " at " + eventName + " (P" + teammate.finishingPosition + " to P" +
                        player.finishingPosition + "), with the teammate battle now " + Save.teammateRaceWins + "-" + Save.teammateRaceLosses + ".",
                        NewsCategoryRivalry);
                }
            }

            RaceResultEntry rival = string.IsNullOrEmpty(Save.rivalDriverId) ? null : results.Find(entry => entry.driverId == Save.rivalDriverId);
            if (rival != null)
            {
                if (player.finishingPosition < rival.finishingPosition)
                {
                    Save.rivalRaceWins++;
                    if (Save.rivalRaceWins % 2 == 0 || player.finishingPosition <= 3)
                    {
                        AddNewsArticle(
                            Save.playerDriverName + " gets the better of rival " + rival.driverName,
                            Save.playerDriverName + " beat championship rival " + rival.driverName + " at " + eventName +
                            " (P" + player.finishingPosition + " to P" + rival.finishingPosition + "), taking the head-to-head to " +
                            Save.rivalRaceWins + "-" + Save.rivalRaceLosses + ".",
                            NewsCategoryRivalry);
                    }
                }
                else if (player.finishingPosition > rival.finishingPosition)
                {
                    Save.rivalRaceLosses++;
                    AddNewsArticle(
                        rival.driverName + " gets the better of " + Save.playerDriverName,
                        rival.driverName + " out-raced " + Save.playerDriverName + " at " + eventName + " (P" + rival.finishingPosition +
                        " to P" + player.finishingPosition + "). The rivalry now stands at " + Save.rivalRaceWins + "-" + Save.rivalRaceLosses +
                        " in race wins.",
                        NewsCategoryRivalry);
                }
            }

            bool playerDnf = !string.IsNullOrEmpty(player.penaltyReason) && player.penaltyReason.Contains("DNF");
            if (playerDnf)
            {
                AddNewsArticle(
                    Save.playerDriverName + " retires from " + eventName,
                    Save.playerDriverName + "'s race at " + eventName + " ended early (" + player.penaltyReason +
                    "), a blow to the team's points chase.",
                    NewsCategoryRace);
            }
            else if (player.finishingPosition == 1)
            {
                StandingEntry playerStanding = Save.driverStandings.Find(entry => entry.id == "player");
                int winCount = playerStanding != null ? playerStanding.wins + 1 : 1;
                AddNewsArticle(
                    Save.playerDriverName + " wins at " + eventName + "!",
                    Save.playerDriverName + " took victory at " + eventName + ", career win number " + winCount + ".",
                    NewsCategoryRace);
            }
            else if (player.finishingPosition <= 3)
            {
                AddNewsArticle(
                    Save.playerDriverName + " takes a podium finish at " + eventName,
                    Save.playerDriverName + " crossed the line P" + player.finishingPosition + " at " + eventName + ", a solid points haul for the team.",
                    NewsCategoryRace);
            }
            else if (player.finishingPosition > Save.contractTargetPosition + 3)
            {
                AddNewsArticle(
                    "Difficult afternoon for " + Save.playerDriverName + " at " + eventName,
                    Save.playerDriverName + " could only manage P" + player.finishingPosition + " at " + eventName +
                    ", well short of the team's P" + Save.contractTargetPosition + " target.",
                    NewsCategoryRace);
            }

            Save.recentFormPositions.Add(player.finishingPosition);
            while (Save.recentFormPositions.Count > 3)
            {
                Save.recentFormPositions.RemoveAt(0);
            }

            int reputationBefore = Save.reputationHistory.Count > 0 ? Save.reputationHistory[Save.reputationHistory.Count - 1] : Save.reputation;
            if (Save.reputation - reputationBefore >= 3)
            {
                AddNewsArticle(
                    "Team confidence rises after " + eventName,
                    "A strong result at " + eventName + " has lifted spirits inside the team, with reputation climbing to " + Save.reputation + ".",
                    NewsCategoryGeneral);
            }

            Save.reputationHistory.Add(Save.reputation);
            while (Save.reputationHistory.Count > 6)
            {
                Save.reputationHistory.RemoveAt(0);
            }

            Save.roundsSinceRivalPicked++;
            if (Save.roundsSinceRivalPicked >= 4)
            {
                ReevaluateRival();
            }
        }

        void UpdateQualifyingRivalry(List<QualifyingResultEntry> results, QualifyingResultEntry player)
        {
            QualifyingResultEntry teammate = results.Find(entry => entry.teamId == Save.playerTeamId && entry.driverId != "player");
            if (teammate != null)
            {
                if (player.position < teammate.position)
                {
                    Save.teammateQualifyingWins++;
                }
                else if (player.position > teammate.position)
                {
                    Save.teammateQualifyingLosses++;
                }
            }

            QualifyingResultEntry rival = string.IsNullOrEmpty(Save.rivalDriverId) ? null : results.Find(entry => entry.driverId == Save.rivalDriverId);
            if (rival != null)
            {
                if (player.position < rival.position)
                {
                    Save.rivalQualifyingWins++;
                    if (player.position <= 3)
                    {
                        AddNewsArticle(
                            Save.playerDriverName + " out-qualifies rival again",
                            Save.playerDriverName + " starts P" + player.position + ", ahead of rival " + rival.driverName + " in P" + rival.position +
                            ". Qualifying head-to-head now " + Save.rivalQualifyingWins + "-" + Save.rivalQualifyingLosses + ".",
                            NewsCategoryRivalry);
                    }
                }
                else if (player.position > rival.position)
                {
                    Save.rivalQualifyingLosses++;
                    if (rival.position <= 3)
                    {
                        AddNewsArticle(
                            rival.driverName + " beats " + Save.playerDriverName + " to grid slot",
                            rival.driverName + " out-qualified " + Save.playerDriverName + ", starting P" + rival.position + " to P" + player.position +
                            ". Qualifying head-to-head now " + Save.rivalQualifyingWins + "-" + Save.rivalQualifyingLosses + ".",
                            NewsCategoryRivalry);
                    }
                }
            }
        }

        // Re-evaluates the rival every few rounds: prefers the nearest
        // championship rival by points that isn't the player or their teammate;
        // falls back to the teammate if nobody else is close. Keeps the rival
        // fixed if the current pick is still the closest match, so it doesn't
        // flip every cycle for no reason.
        void ReevaluateRival()
        {
            Save.roundsSinceRivalPicked = 0;
            StandingEntry playerStanding = Save.driverStandings.Find(entry => entry.id == "player");
            if (playerStanding == null)
            {
                return;
            }

            StandingEntry closest = null;
            int closestGap = int.MaxValue;
            for (int i = 0; i < Save.driverStandings.Count; i++)
            {
                StandingEntry candidate = Save.driverStandings[i];
                if (candidate.id == "player" || candidate.teamId == Save.playerTeamId)
                {
                    continue;
                }

                int gap = Mathf.Abs(candidate.points - playerStanding.points);
                if (gap < closestGap)
                {
                    closestGap = gap;
                    closest = candidate;
                }
            }

            if (closest == null)
            {
                // No other team on the grid - fall back to the teammate.
                List<DriverData> teamDrivers = data.GetDriversForTeam(Save.playerTeamId, Save.driverTransferRecords);
                DriverData teammateDriver = teamDrivers.Find(driver => driver.id != Save.selectedDriverId);
                if (teammateDriver != null)
                {
                    Save.rivalDriverId = teammateDriver.id;
                }

                return;
            }

            if (closest.id != Save.rivalDriverId)
            {
                Save.rivalDriverId = closest.id;
                Save.rivalRaceWins = 0;
                Save.rivalRaceLosses = 0;
                Save.rivalQualifyingWins = 0;
                Save.rivalQualifyingLosses = 0;
                AddNewsArticle(
                    "New championship rival: " + closest.displayName,
                    closest.displayName + " is now " + Save.playerDriverName + "'s closest title threat, sitting just " + closestGap +
                    " point" + (closestGap == 1 ? "" : "s") + " away in the standings.",
                    NewsCategoryRivalry);
            }
        }

        public void AddNews(string headline)
        {
            AddNewsArticle(headline, headline, NewsCategoryGeneral);
        }

        // Part 20: adds both the short headline (existing newsFeed strip) and a
        // full article (headline + body + category + timestamp) in one call, so
        // every career event only needs to be written once.
        public void AddNewsArticle(string headline, string body, string category)
        {
            if (string.IsNullOrEmpty(headline) || Save.newsFeed == null)
            {
                return;
            }

            Save.newsFeed.Add(headline);
            while (Save.newsFeed.Count > 10)
            {
                Save.newsFeed.RemoveAt(0);
            }

            if (Save.newsArticles == null)
            {
                Save.newsArticles = new List<NewsArticle>();
            }

            Save.newsArticles.Add(new NewsArticle
            {
                headline = headline,
                body = string.IsNullOrEmpty(body) ? headline : body,
                category = string.IsNullOrEmpty(category) ? NewsCategoryGeneral : category,
                season = Save.currentSeason,
                round = Save.currentRound,
                raceWeekLabel = "Season " + Save.currentSeason + ", Round " + Save.currentRound
            });
            while (Save.newsArticles.Count > 30)
            {
                Save.newsArticles.RemoveAt(0);
            }
        }

        public List<NewsArticle> GetNewsArticles()
        {
            return Save.newsArticles;
        }

        public List<RaceReportRecord> GetRaceReports()
        {
            return Save.raceReports;
        }

        public RaceReportRecord GetLatestRaceReport()
        {
            return Save.raceReports != null && Save.raceReports.Count > 0 ? Save.raceReports[Save.raceReports.Count - 1] : null;
        }

        // ---------- WDC/WCC championship graph data ----------
        // Save.raceResults already stores one RaceResultRecord per completed
        // race (season/round/eventName + the full grid's RaceResultEntry list,
        // including points earned that race) and is never trimmed/capped the
        // way raceReports is - so the whole season's points progression can be
        // derived from it directly rather than needing a new, separate history
        // list to keep in sync. Works identically whether a race was driven or
        // simulated, since both paths funnel through the same ApplyRaceResults
        // -> Save.raceResults.Add call.
        public class ChampionshipSeries
        {
            public string id;
            public string label;
            public bool isPlayer;
            public bool isPlayerTeam;
            // Index-aligned with ChampionshipProgression.rounds - cumulative
            // points AFTER that round.
            public List<int> cumulativePoints = new List<int>();
        }

        public class ChampionshipProgression
        {
            public List<int> rounds = new List<int>();
            public List<string> roundLabels = new List<string>();
            public List<ChampionshipSeries> series = new List<ChampionshipSeries>();
        }

        public ChampionshipProgression GetDriverChampionshipProgression()
        {
            return BuildChampionshipProgression(false, Save.currentSeason);
        }

        public ChampionshipProgression GetConstructorChampionshipProgression()
        {
            return BuildChampionshipProgression(true, Save.currentSeason);
        }

        // Part 21: season-archive detail view overloads - the graph-building
        // logic already worked purely off Save.raceResults filtered by season,
        // so making that season a parameter (instead of always the current
        // one) is all a completed-season's graph needs; no separate storage.
        public ChampionshipProgression GetDriverChampionshipProgression(int season)
        {
            return BuildChampionshipProgression(false, season);
        }

        public ChampionshipProgression GetConstructorChampionshipProgression(int season)
        {
            return BuildChampionshipProgression(true, season);
        }

        ChampionshipProgression BuildChampionshipProgression(bool constructors, int season)
        {
            ChampionshipProgression result = new ChampionshipProgression();
            if (Save.raceResults == null || Save.raceResults.Count == 0)
            {
                return result;
            }

            List<RaceResultRecord> seasonRecords = Save.raceResults.FindAll(r => r.season == season);
            seasonRecords.Sort((a, b) => a.round.CompareTo(b.round));

            Dictionary<string, ChampionshipSeries> byId = new Dictionary<string, ChampionshipSeries>();
            List<string> order = new List<string>();

            for (int r = 0; r < seasonRecords.Count; r++)
            {
                RaceResultRecord record = seasonRecords[r];
                result.rounds.Add(record.round);
                result.roundLabels.Add(string.IsNullOrEmpty(record.eventName) ? "Round " + record.round : record.eventName);

                // Points earned by each id THIS round, so a constructor's total
                // is its two drivers' points summed rather than double-adding a
                // stale per-driver cumulative value.
                Dictionary<string, int> gainedThisRound = new Dictionary<string, int>();
                for (int i = 0; i < record.results.Count; i++)
                {
                    RaceResultEntry entry = record.results[i];
                    string id = constructors ? entry.teamId : entry.driverId;
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    if (!byId.ContainsKey(id))
                    {
                        TeamData team = data.FindTeam(entry.teamId);
                        string label = constructors
                            ? (team != null ? team.shortName : entry.teamId)
                            : entry.driverName;
                        byId[id] = new ChampionshipSeries
                        {
                            id = id,
                            label = string.IsNullOrEmpty(label) ? id : label,
                            isPlayer = !constructors && entry.isPlayer,
                            isPlayerTeam = constructors && entry.teamId == Save.playerTeamId
                        };
                        order.Add(id);
                    }
                    else if (!constructors && entry.isPlayer)
                    {
                        // The player's driver id/name can change mid-season
                        // (a mid-career driver swap) - keep the series flagged
                        // correctly and relabelled to whoever is player now.
                        byId[id].isPlayer = true;
                        byId[id].label = entry.driverName;
                    }

                    int gained;
                    gainedThisRound.TryGetValue(id, out gained);
                    gainedThisRound[id] = gained + Mathf.Max(0, entry.points);
                }

                for (int i = 0; i < order.Count; i++)
                {
                    ChampionshipSeries series = byId[order[i]];
                    int previous = series.cumulativePoints.Count > 0 ? series.cumulativePoints[series.cumulativePoints.Count - 1] : 0;
                    int gained;
                    gainedThisRound.TryGetValue(order[i], out gained);
                    series.cumulativePoints.Add(previous + gained);
                }
            }

            for (int i = 0; i < order.Count; i++)
            {
                result.series.Add(byId[order[i]]);
            }

            return result;
        }

        // Part 20: driver/team presentation passthroughs. Team lookups apply the
        // player's own R&D upgrades only when asking about the player's own car -
        // an AI team's card should reflect its own base car, not the player's R&D.
        public DriverProfileSummary GetDriverProfileSummary(string driverId)
        {
            return data.GetDriverProfileSummary(data.FindDriver(driverId));
        }

        public TeamProfileSummary GetTeamProfileSummary(string teamId)
        {
            TeamData team = data.FindTeam(teamId);
            if (team == null)
            {
                return data.GetTeamProfileSummary(null, null);
            }

            CarPerformanceData car = data.FindCar(team.carPerformanceId);
            if (team.id == Save.playerTeamId)
            {
                car = ApplyCareerUpgrades(car);
            }

            return data.GetTeamProfileSummary(team, car);
        }

        public TeamProfileSummary GetPlayerTeamProfileSummary()
        {
            return GetTeamProfileSummary(Save.playerTeamId);
        }

        public bool TryPurchaseUpgrade(string upgradeId)
        {
            return TryStartUpgradeProject(upgradeId, RiskStandard);
        }

        public bool TryStartUpgradeProject(string upgradeId, int riskMode)
        {
            UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == upgradeId);
            if (upgrade == null || Save.completedUpgradeIds.Contains(upgradeId) || Save.failedUpgradeIds.Contains(upgradeId))
            {
                return false;
            }

            if (FindProject(upgradeId) != null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(upgrade.requiredUpgradeId) && !Save.completedUpgradeIds.Contains(upgrade.requiredUpgradeId))
            {
                return false;
            }

            if (GetDepartmentLevel(GetDepartmentIndex(upgrade.category)) < Mathf.Max(1, upgrade.tier))
            {
                return false;
            }

            if (ActiveProjectCount() >= MaxActiveProjects())
            {
                return false;
            }

            int cost = ComputeProjectCost(upgrade, riskMode);
            if (Save.resourcePoints < cost)
            {
                return false;
            }

            int weeks = ComputeProjectWeeks(upgrade, riskMode);
            Save.resourcePoints -= cost;
            Save.activeUpgradeProjects.Add(new ActiveUpgradeProject
            {
                upgradeId = upgradeId,
                category = upgrade.category,
                startRound = Save.currentRound,
                remainingRaceWeeks = weeks,
                totalRaceWeeks = weeks,
                cost = cost,
                successChance = ComputeProjectSuccessChance(upgrade, riskMode),
                riskMode = riskMode,
                status = ProjectInDevelopment
            });
            Write();
            return true;
        }

        public bool TryReworkProject(string upgradeId)
        {
            ActiveUpgradeProject project = FindProject(upgradeId);
            if (project == null || project.status != ProjectReworkAvailable)
            {
                return false;
            }

            if (ActiveProjectCount() >= MaxActiveProjects())
            {
                return false;
            }

            int cost = GetReworkCost(project);
            if (Save.resourcePoints < cost)
            {
                return false;
            }

            Save.resourcePoints -= cost;
            project.status = ProjectInDevelopment;
            project.startRound = Save.currentRound;
            project.totalRaceWeeks = Mathf.Max(1, project.totalRaceWeeks / 2);
            project.remainingRaceWeeks = project.totalRaceWeeks;
            project.successChance = Mathf.Min(0.95f, project.successChance + 0.12f);
            Write();
            return true;
        }

        public void AbandonProject(string upgradeId)
        {
            ActiveUpgradeProject project = FindProject(upgradeId);
            if (project == null || project.status != ProjectReworkAvailable)
            {
                return;
            }

            Save.activeUpgradeProjects.Remove(project);
            if (!Save.failedUpgradeIds.Contains(upgradeId))
            {
                Save.failedUpgradeIds.Add(upgradeId);
            }

            Write();
        }

        public int GetReworkCost(ActiveUpgradeProject project)
        {
            return F1Game.Core.CarDevelopmentRules.ReworkCost(project.cost);
        }

        public ActiveUpgradeProject FindProject(string upgradeId)
        {
            if (Save.activeUpgradeProjects == null)
            {
                return null;
            }

            return Save.activeUpgradeProjects.Find(item => item.upgradeId == upgradeId);
        }

        public int ActiveProjectCount()
        {
            int count = 0;
            for (int i = 0; i < Save.activeUpgradeProjects.Count; i++)
            {
                if (Save.activeUpgradeProjects[i].status == ProjectInDevelopment)
                {
                    count++;
                }
            }

            return count;
        }

        // Base 2 slots; every two facility levels bought across the six
        // departments frees one more engineering slot, capped at 5.
        public int MaxActiveProjects()
        {
            int extraLevels = 0;
            for (int i = 0; i < Save.departmentLevels.Count; i++)
            {
                extraLevels += Mathf.Max(0, Save.departmentLevels[i] - 1);
            }

            // The 2-base / +1-per-two-levels / cap-5 slot formula is the pure
            // CarDevelopmentRules.EngineeringSlots; the department-levels sum stays
            // here (it reads live save state).
            return F1Game.Core.CarDevelopmentRules.EngineeringSlots(extraLevels);
        }

        public int GetDepartmentIndex(string category)
        {
            for (int i = 0; i < DepartmentNames.Length; i++)
            {
                if (DepartmentNames[i] == category)
                {
                    return i;
                }
            }

            return -1;
        }

        public int GetDepartmentLevel(int departmentIndex)
        {
            if (departmentIndex < 0 || Save.departmentLevels == null || departmentIndex >= Save.departmentLevels.Count)
            {
                return 1;
            }

            return Save.departmentLevels[departmentIndex];
        }

        public int GetDepartmentUpgradeCost(int departmentIndex)
        {
            return F1Game.Core.CarDevelopmentRules.DepartmentUpgradeCost(GetDepartmentLevel(departmentIndex));
        }

        public bool TryUpgradeDepartment(int departmentIndex)
        {
            if (departmentIndex < 0 || departmentIndex >= Save.departmentLevels.Count)
            {
                return false;
            }

            int level = Save.departmentLevels[departmentIndex];
            if (level >= MaxDepartmentLevel)
            {
                return false;
            }

            int cost = level * 400;
            if (Save.resourcePoints < cost)
            {
                return false;
            }

            Save.resourcePoints -= cost;
            Save.departmentLevels[departmentIndex] = level + 1;
            Write();
            return true;
        }

        public int ComputeProjectWeeks(UpgradeData upgrade, int riskMode)
        {
            return ComputeProjectWeeksForLevel(upgrade, riskMode, GetDepartmentLevel(GetDepartmentIndex(upgrade.category)));
        }

        int ComputeProjectWeeksForLevel(UpgradeData upgrade, int riskMode, int level)
        {
            return F1Game.Core.CarDevelopmentRules.DevelopmentWeeks(upgrade.developmentDays, level, riskMode);
        }

        public float ComputeProjectSuccessChance(UpgradeData upgrade, int riskMode)
        {
            return ComputeProjectSuccessChanceForLevel(upgrade, riskMode, GetDepartmentLevel(GetDepartmentIndex(upgrade.category)));
        }

        float ComputeProjectSuccessChanceForLevel(UpgradeData upgrade, int riskMode, int level)
        {
            // Maths live in the engine-free CarDevelopmentRules (testable); this
            // layer supplies the upgrade's base chance and the resolved level.
            return F1Game.Core.CarDevelopmentRules.SuccessChance(upgrade.successChance, level, riskMode);
        }

        public int ComputeProjectCost(UpgradeData upgrade, int riskMode)
        {
            return F1Game.Core.CarDevelopmentRules.Cost(upgrade.cost, riskMode);
        }

        public void AdvanceUpgradeProjects()
        {
            float practiceBonus = Save.practiceQualityThisRound > 0 ? 0.03f : 0f;
            for (int i = 0; i < Save.activeUpgradeProjects.Count; i++)
            {
                ActiveUpgradeProject project = Save.activeUpgradeProjects[i];
                if (project.status != ProjectInDevelopment)
                {
                    continue;
                }

                project.remainingRaceWeeks--;
                if (project.remainingRaceWeeks > 0)
                {
                    continue;
                }

                project.remainingRaceWeeks = 0;
                string projectId = project.upgradeId;
                UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == projectId);
                string projectName = upgrade != null ? upgrade.displayName : projectId;
                float chance = Mathf.Min(0.97f, project.successChance + practiceBonus);
                if (Random.value <= chance)
                {
                    project.status = ProjectCompleted;
                    if (!Save.completedUpgradeIds.Contains(projectId))
                    {
                        Save.completedUpgradeIds.Add(projectId);
                    }

                    Save.reputation += 1;
                    string department = upgrade != null ? upgrade.category : "engineering";
                    if (project.riskMode == RiskExperimental && Random.value <= 0.25f)
                    {
                        project.bonusApplied = true;
                        Save.pendingRndMessages.Add(projectName + " delivered with an experimental breakthrough - effect boosted 30%.");
                        AddNewsArticle(
                            projectName + " lands with an experimental breakthrough",
                            "The " + department + " department's gamble on " + projectName + " has paid off in style, with early data showing a bigger " +
                            "gain than planned - a real step forward for the team.",
                            NewsCategoryRnd);
                    }
                    else
                    {
                        Save.pendingRndMessages.Add(projectName + " development complete - fitted to the car.");
                        AddNewsArticle(
                            projectName + " completes development",
                            "The " + department + " department has finished work on " + projectName + ", which will be fitted to the car from the next round.",
                            NewsCategoryRnd);
                    }
                }
                else
                {
                    project.status = ProjectReworkAvailable;
                    Save.pendingRndMessages.Add(projectName + " development failed - rework available at 40% cost.");
                    string department = upgrade != null ? upgrade.category : "engineering";
                    AddNewsArticle(
                        projectName + " development setback",
                        "The " + department + " department has hit a setback with " + projectName + " - the project has stalled and will need a " +
                        "costly rework to get back on track.",
                        NewsCategoryRnd);
                }
            }

            Save.practiceQualityThisRound = 0;
        }

        public CarPerformanceData ApplyCareerUpgrades(CarPerformanceData baseCar)
        {
            return ApplyUpgradeSet(baseCar, Save.completedUpgradeIds, FindProject);
        }

        // Part 5/6/7: the one place upgrade stat deltas are actually applied to
        // a car - shared by the player's own R&D (via ApplyCareerUpgrades,
        // Save.completedUpgradeIds/FindProject) and every AI team's R&D (via
        // GetEffectiveTeamCar, that team's TeamDevelopmentState.completedUpgradeIds
        // and its own project list) so AI development uses exactly the same
        // UpgradeData stat deltas and scaling the player's does - never a
        // secret "+N overall" shortcut.
        CarPerformanceData ApplyUpgradeSet(CarPerformanceData baseCar, List<string> completedIds, System.Func<string, ActiveUpgradeProject> projectLookup)
        {
            CarPerformanceData tuned = new CarPerformanceData
            {
                id = baseCar.id,
                topSpeed = baseCar.topSpeed,
                acceleration = baseCar.acceleration,
                cornering = baseCar.cornering,
                braking = baseCar.braking,
                reliability = baseCar.reliability,
                ersEfficiency = baseCar.ersEfficiency,
                tyreManagement = baseCar.tyreManagement,
                aeroEfficiency = baseCar.aeroEfficiency,
                chassisBalance = baseCar.chassisBalance,
                enginePower = baseCar.enginePower
            };

            if (completedIds == null)
            {
                return tuned;
            }

            for (int i = 0; i < completedIds.Count; i++)
            {
                UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == completedIds[i]);
                if (upgrade == null)
                {
                    continue;
                }

                ActiveUpgradeProject project = projectLookup != null ? projectLookup(upgrade.id) : null;
                bool bonus = project != null && project.bonusApplied;

                // Per-stat delta scaling + experimental boost live in
                // CarDevelopmentRules; top speed and reliability keep their own
                // scales, the rest share UpgradeEffectScale.
                tuned.topSpeed = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.topSpeed, upgrade.topSpeedDelta, 1.7f, bonus);
                tuned.acceleration = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.acceleration, upgrade.accelerationDelta, UpgradeEffectScale, bonus);
                tuned.cornering = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.cornering, upgrade.corneringDelta, UpgradeEffectScale, bonus);
                tuned.braking = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.braking, upgrade.brakingDelta, UpgradeEffectScale, bonus);
                tuned.reliability = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.reliability, upgrade.reliabilityDelta, 1.6f, bonus);
                tuned.ersEfficiency = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.ersEfficiency, upgrade.ersDelta, UpgradeEffectScale, bonus);
                tuned.tyreManagement = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.tyreManagement, upgrade.tyreDelta, UpgradeEffectScale, bonus);
                tuned.aeroEfficiency = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.aeroEfficiency, upgrade.aeroDelta, UpgradeEffectScale, bonus);
                tuned.chassisBalance = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.chassisBalance, upgrade.chassisDelta, UpgradeEffectScale, bonus);
                tuned.enginePower = F1Game.Core.CarDevelopmentRules.ApplyStatDelta(tuned.enginePower, upgrade.engineDelta, UpgradeEffectScale, bonus);
            }

            // Cap-overflow redistribution (per report - "everyone else has
            // brought like 16-18 upgrades and improved by around the same
            // amount... ive brought 20 and ive only gotten better by 11"): the
            // per-stat 125 clamp below used to silently VAPORIZE any upgrade
            // points landing on an already-maxed stat - and a front-running
            // car (several stats pinned at 125) lost a large share of every
            // upgrade it bought, while low-rated rivals banked full value. A
            // real team whose braking programme is maxed reallocates the
            // effort - so overflow past a cap now spills, point by point, into
            // the weakest stat that still has headroom. Shared by player and
            // every AI team (this is the one place upgrades apply), so
            // machinery fairness is preserved; top speed (absolute kph, its
            // own scale) stays outside the redistribution.
            tuned.topSpeed = Mathf.Clamp(tuned.topSpeed, 315, 360);
            int[] statValues =
            {
                tuned.acceleration, tuned.cornering, tuned.braking, tuned.reliability, tuned.ersEfficiency,
                tuned.tyreManagement, tuned.aeroEfficiency, tuned.chassisBalance, tuned.enginePower
            };
            int overflow = 0;
            for (int s = 0; s < statValues.Length; s++)
            {
                if (statValues[s] > 125)
                {
                    overflow += statValues[s] - 125;
                    statValues[s] = 125;
                }
            }

            int spillGuard = 0;
            while (overflow > 0 && spillGuard++ < 1024)
            {
                int lowest = -1;
                for (int s = 0; s < statValues.Length; s++)
                {
                    if (statValues[s] < 125 && (lowest < 0 || statValues[s] < statValues[lowest]))
                    {
                        lowest = s;
                    }
                }

                if (lowest < 0)
                {
                    break;
                }

                statValues[lowest]++;
                overflow--;
            }

            tuned.acceleration = Mathf.Clamp(statValues[0], 45, 125);
            tuned.cornering = Mathf.Clamp(statValues[1], 45, 125);
            tuned.braking = Mathf.Clamp(statValues[2], 45, 125);
            tuned.reliability = Mathf.Clamp(statValues[3], 35, 125);
            tuned.ersEfficiency = Mathf.Clamp(statValues[4], 45, 125);
            tuned.tyreManagement = Mathf.Clamp(statValues[5], 45, 125);
            tuned.aeroEfficiency = Mathf.Clamp(statValues[6], 45, 125);
            tuned.chassisBalance = Mathf.Clamp(statValues[7], 45, 125);
            tuned.enginePower = Mathf.Clamp(statValues[8], 45, 125);
            return tuned;
        }

        // Fuel system pass: small reward for genuinely successful underfuelling
        // (chose to run light AND actually made it to the finish with a sensible
        // small margin, not stranded or carrying it needlessly), small penalty for
        // a fuel-starvation DNF. Deliberately modest either way - fuel strategy is
        // a nice-to-reward skill element, not a headline career mechanic.
        void ApplyFuelStrategyReward(RaceResultEntry player)
        {
            if (player == null)
            {
                return;
            }

            if (player.fuelStarvedRetirement)
            {
                Save.reputation -= 2;
                return;
            }

            bool chosenLight = player.fuelLoadChoice == (int)FuelLoadChoice.AggressiveUnderfuel || player.fuelLoadChoice == (int)FuelLoadChoice.LightUnderfuel;
            if (chosenLight && player.finishFuelKg >= 0.15f && player.finishFuelKg <= 2.5f)
            {
                Save.reputation += 1;
                Save.resourcePoints += Mathf.RoundToInt(20f * Save.currentSeasonResourceMultiplier);
            }
        }

        void ApplyDriverPoints(RaceResultEntry result, int points)
        {
            StandingEntry entry = Save.driverStandings.Find(item => item.id == result.driverId);
            if (entry == null)
            {
                entry = new StandingEntry
                {
                    id = result.driverId,
                    displayName = result.driverName,
                    teamId = result.teamId
                };
                Save.driverStandings.Add(entry);
            }
            else
            {
                // Standings-drift fix: teamId/displayName used to only ever be set
                // when this entry was first CREATED - every following race for the
                // rest of the season kept whatever team the driver was on the very
                // first time they scored, even after a mid-season transfer. Refresh
                // both every time so this driver's own standing always reflects
                // who/where they most recently raced for, matching the constructor
                // points ApplyConstructorPoints (below) awards for this exact result.
                entry.teamId = result.teamId;
                entry.displayName = result.driverName;
            }

            entry.points += points;
            if (result.finishingPosition == 1)
            {
                entry.wins++;
            }

            if (result.finishingPosition <= 3)
            {
                entry.podiums++;
            }
        }

        void ApplyConstructorPoints(string teamId, int points, int position)
        {
            StandingEntry entry = Save.constructorStandings.Find(item => item.id == teamId);
            TeamData team = data.FindTeam(teamId);
            if (entry == null)
            {
                entry = new StandingEntry
                {
                    id = teamId,
                    displayName = team == null ? teamId : team.name,
                    teamId = teamId
                };
                Save.constructorStandings.Add(entry);
            }

            entry.points += points;
            if (position == 1)
            {
                entry.wins++;
            }

            if (position <= 3)
            {
                entry.podiums++;
            }
        }

        void SortStandings(List<StandingEntry> standings)
        {
            standings.Sort((a, b) => F1Game.Race.Rules.ChampionshipPoints.CompareStandings(
                a.points, a.wins, a.podiums,
                b.points, b.wins, b.podiums));
        }

        // Standings-drift fix: the single canonical answer to "what team is this
        // driver on RIGHT NOW". Every place that needs to attribute a driver's
        // points to a constructor should go through here instead of reading a
        // (possibly stale) StandingEntry.teamId or a driver's raw, un-transferred
        // DriverData.teamId directly.
        public string GetCurrentTeamForDriver(string driverId)
        {
            if (string.IsNullOrEmpty(driverId))
            {
                return null;
            }

            if (driverId == "player")
            {
                return Save.playerTeamId;
            }

            DriverData driver = data.FindDriver(driverId);
            if (driver != null)
            {
                return data.EffectiveTeamId(driver, Save.driverTransferRecords);
            }

            // Old saves / a driver id that no longer resolves against the current
            // roster JSON: fall back to whatever team this driver's own standing
            // last recorded, rather than dropping them out of constructor
            // standings entirely.
            StandingEntry existing = Save.driverStandings != null ? Save.driverStandings.Find(item => item.id == driverId) : null;
            return existing != null ? existing.teamId : null;
        }

        // Standings-drift fix: constructor standings are derived FROM driver
        // standings here, rather than being an independently-incremented total
        // that can drift away from them (ApplyDriverPoints/ApplyConstructorPoints
        // still increment both directly during a live race for responsiveness,
        // but this is the periodic correction pass that guarantees they can never
        // stay out of sync for long - constructor points always end up exactly
        // equal to the sum of that constructor's current drivers' points).
        void RecalculateConstructorStandingsFromDrivers()
        {
            if (Save.driverStandings == null)
            {
                return;
            }

            if (Save.constructorStandings == null)
            {
                Save.constructorStandings = new List<StandingEntry>();
            }

            for (int i = 0; i < Save.constructorStandings.Count; i++)
            {
                StandingEntry c = Save.constructorStandings[i];
                c.points = 0;
                c.wins = 0;
                c.podiums = 0;
            }

            for (int i = 0; i < Save.driverStandings.Count; i++)
            {
                StandingEntry d = Save.driverStandings[i];
                string teamId = GetCurrentTeamForDriver(d.id);
                if (string.IsNullOrEmpty(teamId))
                {
                    GameLog.Warn("[CareerValidation] Driver standing has no current team: " + d.id);
                    continue;
                }

                // Keep the driver's own standing in sync with the resolved current
                // team too, so nothing else reading StandingEntry.teamId directly
                // (Career Hub driver list, rival/teammate lookups) can see a stale
                // value either.
                d.teamId = teamId;

                StandingEntry c = Save.constructorStandings.Find(item => item.id == teamId);
                if (c == null)
                {
                    TeamData team = data.FindTeam(teamId);
                    c = new StandingEntry
                    {
                        id = teamId,
                        displayName = team == null ? teamId : team.name,
                        teamId = teamId
                    };
                    Save.constructorStandings.Add(c);
                }

                c.points += d.points;
                c.wins += d.wins;
                c.podiums += d.podiums;
            }

            SortStandings(Save.constructorStandings);
        }

        // Standings-drift fix: cheap integrity check run every time standings are
        // touched/loaded (EnsureStandingLists) and after every race/season
        // transition - if constructor totals ever disagree with the sum of their
        // current drivers' points (stale team mappings, a missed increment, a
        // corrupted/old save), rebuild constructors from driver standings rather
        // than displaying or persisting the mismatch.
        void ValidateCurrentSeasonStandingsIntegrity()
        {
            if (Save == null || Save.driverStandings == null || Save.constructorStandings == null)
            {
                return;
            }

            bool mismatch = false;
            for (int i = 0; i < Save.constructorStandings.Count && !mismatch; i++)
            {
                StandingEntry c = Save.constructorStandings[i];
                int driverPointsForTeam = 0;
                for (int j = 0; j < Save.driverStandings.Count; j++)
                {
                    if (GetCurrentTeamForDriver(Save.driverStandings[j].id) == c.id)
                    {
                        driverPointsForTeam += Save.driverStandings[j].points;
                    }
                }

                if (driverPointsForTeam != c.points)
                {
                    mismatch = true;
                    GameLog.Warn("[CareerValidation] Constructor mismatch: " + c.displayName + " has " + c.points +
                                 " but driver sum is " + driverPointsForTeam + ". Rebuilding constructors from driver standings.");
                }
            }

            if (mismatch)
            {
                RecalculateConstructorStandingsFromDrivers();
            }
        }

        void EnsureStandingLists()
        {
            if (Save.driverStandings == null || Save.driverStandings.Count == 0)
            {
                Save.driverStandings = data.CreateInitialDriverStandings(Save.playerDriverName, Save.playerTeamId, Save.selectedDriverId, Save.driverTransferRecords);
            }

            if (Save.constructorStandings == null || Save.constructorStandings.Count == 0)
            {
                Save.constructorStandings = data.CreateInitialConstructorStandings();
            }

            if (Save.raceResults == null)
            {
                Save.raceResults = new List<RaceResultRecord>();
            }

            if (Save.qualifyingResults == null)
            {
                Save.qualifyingResults = new List<QualifyingResultRecord>();
            }

            if (Save.lastQualifyingResults == null)
            {
                Save.lastQualifyingResults = new List<QualifyingResultEntry>();
            }

            if (string.IsNullOrEmpty(Save.rivalDriverId))
            {
                Save.rivalDriverId = PickRivalId(Save.playerTeamId, Save.selectedDriverId);
            }

            if (Save.contractTargetPosition <= 0)
            {
                Save.contractTargetPosition = ContractTargetForTeam(Save.playerTeamId);
            }

            EnsureRndState();
            EnsurePlayerReplacesDriverSeat();

            // Standings-drift fix: EnsureStandingLists is already called before
            // essentially every career screen/race-setup path, so this is the one
            // reliable choke point to catch and repair a mismatch regardless of
            // which specific caller is about to read the standings.
            ValidateCurrentSeasonStandingsIntegrity();
        }

        // Backwards-compatible defaults for the R&D fields: JsonUtility leaves
        // list fields missing from an old save file as the field initializer,
        // but a save written by an older build can still deserialize with nulls
        // when the whole object graph predates these fields.
        void EnsureRndState()
        {
            if (Save.completedUpgradeIds == null)
            {
                Save.completedUpgradeIds = new List<string>();
            }

            if (Save.failedUpgradeIds == null)
            {
                Save.failedUpgradeIds = new List<string>();
            }

            if (Save.activeUpgradeProjects == null)
            {
                Save.activeUpgradeProjects = new List<ActiveUpgradeProject>();
            }

            if (Save.pendingRndMessages == null)
            {
                Save.pendingRndMessages = new List<string>();
            }

            if (Save.departmentLevels == null)
            {
                Save.departmentLevels = new List<int>();
            }

            while (Save.departmentLevels.Count < DepartmentNames.Length)
            {
                Save.departmentLevels.Add(1);
            }

            for (int i = 0; i < Save.departmentLevels.Count; i++)
            {
                Save.departmentLevels[i] = Mathf.Clamp(Save.departmentLevels[i], 1, MaxDepartmentLevel);
            }

            if (Save.regulationAffectedCategories == null)
            {
                Save.regulationAffectedCategories = new List<string>();
            }

            if (Save.regulationAffectedCategories.Count == 0)
            {
                PickRegulationTargets();
            }

            if (Save.recentFormPositions == null)
            {
                Save.recentFormPositions = new List<int>();
            }

            if (Save.reputationHistory == null)
            {
                Save.reputationHistory = new List<int>();
            }

            if (Save.newsFeed == null)
            {
                Save.newsFeed = new List<string>();
            }

            if (Save.newsArticles == null)
            {
                Save.newsArticles = new List<NewsArticle>();
            }

            if (Save.raceReports == null)
            {
                Save.raceReports = new List<RaceReportRecord>();
            }

            // Part 21: end-of-season / new-season state. Never null-guarded to a
            // non-empty default here (unlike departmentLevels/regulationAffected
            // Categories above) - an empty archive/regulation/objective list on
            // an old save just means "no seasons completed / nothing generated
            // yet under the new system", which is the correct, honest state
            // rather than one manufactured retroactively.
            if (Save.seasonArchives == null)
            {
                Save.seasonArchives = new List<SeasonArchive>();
            }

            if (Save.regulationChanges == null)
            {
                Save.regulationChanges = new List<RegulationChange>();
            }

            if (Save.teamPerformanceModifiers == null)
            {
                Save.teamPerformanceModifiers = new List<TeamPerformanceModifier>();
            }

            if (Save.driverTransferRecords == null)
            {
                Save.driverTransferRecords = new List<DriverTransferRecord>();
            }

            if (Save.driverRatingModifiers == null)
            {
                Save.driverRatingModifiers = new List<DriverRatingModifier>();
            }

            if (Save.seasonObjectives == null)
            {
                Save.seasonObjectives = new List<SeasonObjective>();
            }

            if (Save.seasonObjectives.Count == 0 && !Save.seasonTransitionPending)
            {
                GenerateSeasonObjectives();
            }

            // Part 9: full-grid R&D / performance-driven progression save
            // migration - every list below defaults via its field initializer
            // for a brand-new save, but an OLDER save (written before this
            // system existed) can still deserialize with these null, so guard
            // them the same way every field above already is.
            if (Save.teamDevelopmentStates == null)
            {
                Save.teamDevelopmentStates = new List<TeamDevelopmentState>();
            }

            if (Save.driverSeasonPerformances == null)
            {
                Save.driverSeasonPerformances = new List<DriverSeasonPerformance>();
            }

            EnsureAllTeamDevelopmentStates();

            // Bridge the player's existing completedUpgradeIds into their
            // TeamDevelopmentState exactly once per load so the season archive/
            // Team Ratings screen have something to show immediately on an old
            // save - GetEffectiveTeamCar never reads the player's upgrades from
            // this mirror (only from Save.completedUpgradeIds directly), so
            // this can never cause the player's upgrades to be applied twice.
            SyncPlayerTeamDevelopmentState();

            if (!Save.useExistingDriver && Save.customPlayerDriverBase == null)
            {
                EnsureCustomPlayerDriverBase();
            }

            // Round-out-of-range save repair (per report - career hub showing
            // "Round 25/24"): a save written when the calendar had more events
            // (or corrupted) can carry a currentRound past today's calendar
            // length. The season-rollover check only runs at race completion,
            // so on load the stale round persisted forever - FindEventForRound
            // fell back to the last event while the header showed an impossible
            // round number. Clamp to the real calendar so the final round plays
            // (and rolls the season over) normally.
            if (data != null && data.Calendar != null && data.Calendar.events != null && data.Calendar.events.Count > 0 &&
                (Save.currentRound < 1 || Save.currentRound > data.Calendar.events.Count))
            {
                GameLog.Warn("[Career] currentRound " + Save.currentRound + " outside the " +
                             data.Calendar.events.Count + "-event calendar - clamped.");
                Save.currentRound = Mathf.Clamp(Save.currentRound, 1, data.Calendar.events.Count);
            }

            // Legacy DriverRatingModifier entries (written before the per-stat
            // fields existed) only ever have ratingDelta set - fold each into
            // the new per-stat fields exactly once so an old save's driver
            // progression is preserved instead of silently reset to zero.
            for (int i = 0; i < Save.driverRatingModifiers.Count; i++)
            {
                MigrateLegacyRatingModifier(Save.driverRatingModifiers[i]);
            }

            RepairProgressionScaleBias();

            // Trim ancient per-season telemetry so a very long career's save
            // file doesn't grow without bound - GenerateDriverProgression only
            // ever needs the season that just completed.
            if (Save.driverSeasonPerformances.Count > 400)
            {
                Save.driverSeasonPerformances.RemoveAll(p => p.season < Save.currentSeason - 3);
            }
        }

        // One-shot save repair (v2) for careers whose past seasons were scored
        // under the progression expectation-scale bug (team rank 1..11 compared
        // directly against driver positions 1..22, plus the unreachable
        // 1.2-overtakes neutral point). The bias was NOT uniform: the
        // expectation error grew with team rank (a top car was "expected"
        // ~P1.3, off by ~0.3 places, while a backmarker was "expected" ~P8
        // when its car is realistically a P20 car - off by ~7), and the
        // per-season score clamps saturated on the big errors. Several seasons
        // of that warped the field non-uniformly, so v1's uniform mean-shift
        // could not reconstruct the true distribution, and the per-race data
        // needed to invert the damage exactly is not stored (the biased sums
        // are baked into the season accumulators, with clamping on top).
        //
        // The honest repair is to DISCARD the corrupted data: reset the four
        // bug-poisoned stats' cumulative deltas (pace/racecraft/qualifying/
        // overtaking) to zero, returning every driver to their authored base
        // ratings on those stats. Progression on the stats the bug never
        // touched - consistency, defending, awareness, tyre, wet, aggression,
        // experience, all scored from clean signals like teammate
        // head-to-heads and incident counts - is preserved. Future seasons
        // accrue correctly under the fixed zero-sum scoring.
        //
        // Versioned (progressionScaleBiasRepairVersion) so this v2 supersedes
        // v1's mean-shift whether or not v1 already ran on the save: the reset
        // is absolute, so applying it after a v1 shift yields the same state
        // as applying it alone. Only the current season's live modifiers are
        // touched - that is all GetEffectiveDriver ever reads, and what next
        // season's progression chains from; older seasons' entries stay as
        // historical record.
        void RepairProgressionScaleBias()
        {
            if (Save.progressionScaleBiasRepairVersion >= 4)
            {
                return;
            }

            bool runReset = Save.progressionScaleBiasRepairVersion < 2;
            bool runV3 = Save.progressionScaleBiasRepairVersion < 3;
            Save.progressionScaleBiasRepairVersion = 4;
            Save.progressionScaleBiasRepaired = true;

            if (runReset)
            {
                List<DriverRatingModifier> live = Save.driverRatingModifiers.FindAll(m => m != null && m.season == Save.currentSeason);
                int hadDrift = 0;
                for (int i = 0; i < live.Count; i++)
                {
                    DriverRatingModifier m = live[i];
                    if (m.paceDelta != 0 || m.racecraftDelta != 0 || m.qualifyingDelta != 0 || m.overtakingDelta != 0)
                    {
                        hadDrift++;
                    }

                    m.paceDelta = 0;
                    m.racecraftDelta = 0;
                    m.qualifyingDelta = 0;
                    m.overtakingDelta = 0;
                    // Clear the "last season" change badges for the wiped stats and
                    // the now-stale overall trend - showing last season's corrupted
                    // -3/-4 next to freshly restored ratings would just mislead.
                    m.lastPaceChange = 0;
                    m.lastRacecraftChange = 0;
                    m.lastQualifyingChange = 0;
                    m.lastOvertakingChange = 0;
                    m.lastOverallChange = 0;
                    m.trendLabel = "Steady";
                }

                Debug.Log("[ProgressionRepair] v2: reset corrupted pace/racecraft/qualifying/overtaking progression for " +
                    live.Count + " drivers (" + hadDrift + " carried drift) on season-" + Save.currentSeason +
                    " modifiers - all four stats return to authored base ratings; clean-signal stats keep their progression.");
            }

            // v3 (per request - "raise them by another 3? everyones ratings?"
            // and "you also reverted my ratings back to my original which is 4
            // lower than what i was supposed to be"): a field-wide overall
            // boost of ~+3 on top of the restored bases, plus +4 more for the
            // player, whose legitimately earned progression the v2 reset wiped
            // along with the corruption. Applied through the same four stats
            // the reset touched; they carry 0.57 of the overall blend (pace
            // .20 + racecraft .16 + qualifying .13 + overtaking .08), so +5
            // stat points ~= +3 overall and the player's +12 ~= +7 overall
            // (+3 field-wide + 4 restoration). Get-or-create so this lands
            // even on a save whose current season has no modifier rows yet;
            // per-stat reads still clamp to the 40-99 band, so drivers already
            // near the ceiling simply cap there.
            string playerId = Save.useExistingDriver ? Save.selectedDriverId : "player";
            if (runV3)
            {
                int boosted = 0;
                for (int i = 0; i < data.Drivers.drivers.Count; i++)
                {
                    DriverData rosterDriver = data.Drivers.drivers[i];
                    if (rosterDriver == null)
                    {
                        continue;
                    }

                    ApplyRepairStatBoost(rosterDriver.id, rosterDriver.id == playerId ? 12 : 5);
                    boosted++;
                }

                if (!Save.useExistingDriver)
                {
                    ApplyRepairStatBoost("player", 12);
                    boosted++;
                }

                Debug.Log("[ProgressionRepair] v3: field-wide rating raise applied to " + boosted +
                    " drivers (+5 to pace/racecraft/qualifying/overtaking, ~+3 overall; player '" + playerId +
                    "' +12, ~+7 overall = field raise + restoration of earned progression wiped by the v2 reset).");
            }

            // v4 (per request - "fix the ratings from last season too? boost
            // everyone by another 4... including the user"): the season that
            // rolled over BEFORE the field-relative scoring fix landed still
            // dragged nearly the whole grid down (absolute-neutral overtaking/
            // tyre/awareness scores - see GenerateDriverProgression), and that
            // decline is baked into the saved modifiers. One-time restitution:
            // +7 to the same four blend-heavy stats for EVERY driver, player
            // included at the same amount this time (the player's rating never
            // moved at all last rollover, so there is no extra wipe to restore
            // beyond the shared field raise). The four stats carry 0.57 of the
            // overall blend, so +7 stat points ~= +4 overall. Per-stat reads
            // still clamp to 40-99, so anyone near the ceiling simply caps.
            int raisedV4 = 0;
            for (int i = 0; i < data.Drivers.drivers.Count; i++)
            {
                DriverData rosterDriver = data.Drivers.drivers[i];
                if (rosterDriver == null)
                {
                    continue;
                }

                ApplyRepairStatBoost(rosterDriver.id, 7);
                raisedV4++;
            }

            if (!Save.useExistingDriver)
            {
                ApplyRepairStatBoost("player", 7);
                raisedV4++;
            }

            Debug.Log("[ProgressionRepair] v4: last-season decline restitution applied to " + raisedV4 +
                " drivers (+7 to pace/racecraft/qualifying/overtaking, ~+4 overall each, player '" + playerId +
                "' included at the same amount).");
            Write();
        }

        // v3 helper: bump the four repaired stats on a driver's current-season
        // live modifier, creating the row if the season doesn't have one yet.
        void ApplyRepairStatBoost(string driverId, int amount)
        {
            if (string.IsNullOrEmpty(driverId))
            {
                return;
            }

            DriverRatingModifier modifier = Save.driverRatingModifiers.Find(m => m != null && m.driverId == driverId && m.season == Save.currentSeason);
            if (modifier == null)
            {
                modifier = new DriverRatingModifier
                {
                    driverId = driverId,
                    season = Save.currentSeason,
                    legacyDeltaMigrated = true,
                    trendLabel = "Steady"
                };
                Save.driverRatingModifiers.Add(modifier);
            }

            modifier.paceDelta = Mathf.Clamp(modifier.paceDelta + amount, -40, 40);
            modifier.racecraftDelta = Mathf.Clamp(modifier.racecraftDelta + amount, -40, 40);
            modifier.qualifyingDelta = Mathf.Clamp(modifier.qualifyingDelta + amount, -40, 40);
            modifier.overtakingDelta = Mathf.Clamp(modifier.overtakingDelta + amount, -40, 40);
        }

        void PickRegulationTargets()
        {
            Save.regulationAffectedCategories = new List<string>();
            int count = Random.value < 0.5f ? 1 : 2;
            List<string> pool = new List<string>(DepartmentNames);
            for (int i = 0; i < count; i++)
            {
                int index = Random.Range(0, pool.Count);
                Save.regulationAffectedCategories.Add(pool[index]);
                pool.RemoveAt(index);
            }
        }

        void EnsurePlayerReplacesDriverSeat()
        {
            if (Save.driverStandings == null)
            {
                return;
            }

            StandingEntry player = Save.driverStandings.Find(entry => entry.id == "player");
            if (player == null)
            {
                Save.driverStandings.Add(new StandingEntry
                {
                    id = "player",
                    displayName = Save.playerDriverName,
                    teamId = Save.playerTeamId
                });
            }
            else
            {
                player.displayName = Save.playerDriverName;
                player.teamId = Save.playerTeamId;
            }

            string replacedDriverId = Save.selectedDriverId;
            if (string.IsNullOrEmpty(replacedDriverId))
            {
                List<DriverData> teamDrivers = data.GetDriversForTeam(Save.playerTeamId, Save.driverTransferRecords);
                if (teamDrivers.Count > 0)
                {
                    replacedDriverId = teamDrivers[0].id;
                }
            }

            if (!string.IsNullOrEmpty(replacedDriverId))
            {
                Save.driverStandings.RemoveAll(entry => entry.id == replacedDriverId);
            }

            // Missing-teammate fix: this overflow trim used to remove
            // whichever non-player entry happened to sit last in the list,
            // with no regard for who that was - if it ever landed on the
            // player's own teammate, they'd silently vanish from the
            // championship standings/leaderboard even though they were still
            // racing every round. Never evict a driver on the player's own
            // team; only trim from everyone else.
            while (Save.driverStandings.Count > 22)
            {
                int removeIndex = Save.driverStandings.FindLastIndex(entry => entry.id != "player" && entry.teamId != Save.playerTeamId);
                if (removeIndex < 0)
                {
                    break;
                }

                Save.driverStandings.RemoveAt(removeIndex);
            }
        }

        // Part 7: applies the SAME rule to every team, not just the player's -
        // a regulation-invalidated category scraps that category's completed
        // upgrades (and any in-progress project for them) identically across
        // the whole grid, with every removal logged.
        void ApplyRegulationReset()
        {
            EnsureRndState();
            EnsureAllTeamDevelopmentStates();
            int removed = 0;
            for (int i = Save.completedUpgradeIds.Count - 1; i >= 0; i--)
            {
                string upgradeId = Save.completedUpgradeIds[i];
                UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == upgradeId);
                if (upgrade == null || !Save.regulationAffectedCategories.Contains(upgrade.category))
                {
                    continue;
                }

                // Removing the project entry too lets the upgrade be developed
                // again under the new regulations.
                Save.activeUpgradeProjects.RemoveAll(item => item.upgradeId == upgradeId);
                Save.completedUpgradeIds.RemoveAt(i);
                removed++;
            }

            int aiRemoved = 0;
            for (int t = 0; t < Save.teamDevelopmentStates.Count; t++)
            {
                TeamDevelopmentState state = Save.teamDevelopmentStates[t];
                if (state == null || state.teamId == Save.playerTeamId)
                {
                    continue;
                }

                for (int i = state.completedUpgradeIds.Count - 1; i >= 0; i--)
                {
                    string upgradeId = state.completedUpgradeIds[i];
                    UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == upgradeId);
                    if (upgrade == null || !Save.regulationAffectedCategories.Contains(upgrade.category))
                    {
                        continue;
                    }

                    state.activeUpgradeProjects.RemoveAll(item => item.upgradeId == upgradeId);
                    state.completedUpgradeIds.RemoveAt(i);
                    aiRemoved++;
                }

                TeamData team = data.FindTeam(state.teamId);
                if (team != null)
                {
                    RecalculateTeamRating(team, state);
                }
            }

            if (aiRemoved > 0)
            {
                GameLog.Info("[R&D] Regulation reset scrapped " + aiRemoved + " completed AI upgrade(s) across the grid for Season " + Save.currentSeason + ".");
            }

            string affected = string.Join(", ", Save.regulationAffectedCategories.ToArray());
            if (removed > 0)
            {
                Save.pendingRndMessages.Add("Regulation change hit " + affected + ": " + removed + " development project" + (removed == 1 ? "" : "s") + " scrapped for Season " + Save.currentSeason + ".");
                AddNewsArticle(
                    "Regulation shake-up wipes out development work",
                    "New Season " + Save.currentSeason + " regulations targeting " + affected + " have scrapped " + removed +
                    " completed development project" + (removed == 1 ? "" : "s") + " across the field - teams will need to redevelop from scratch.",
                    NewsCategoryRegulations);
            }
            else
            {
                Save.pendingRndMessages.Add("Season " + Save.currentSeason + " regulation change in " + affected + " arrived - no completed projects affected.");
                AddNewsArticle(
                    "New season regulations announced",
                    "Season " + Save.currentSeason + " regulations target " + affected + ". None of the team's completed projects fell foul of the " +
                    "change, but the field begins adapting regardless.",
                    NewsCategoryRegulations);
            }

            PickRegulationTargets();
            Save.pendingRndMessages.Add("Next regulation focus: " + string.Join(", ", Save.regulationAffectedCategories.ToArray()) + " projects are at risk at season end.");
        }

        // The season-opening rival is picked from a different team on
        // purpose - the player's own teammate is a distinct relationship
        // (see EnsurePlayerReplacesDriverSeat/the "no other team on the
        // grid" fallback above) and should never double as the rival here.
        // GetAiRaceDrivers now guarantees the teammate is included (often
        // first) so it can no longer be excluded implicitly by fill order;
        // it has to be skipped explicitly by id instead.
        string PickRivalId(string teamId, string selectedDriverId)
        {
            // PickRivalId is also called from career creation, inside the
            // Save = new CareerSaveData { ... } object initializer itself - Save
            // is still null at that point, so this must not assume it's set.
            List<DriverData> candidates = data.GetAiRaceDrivers(teamId, 22, selectedDriverId, Save != null ? Save.driverTransferRecords : null);

            // Rivalry-coherence fix: this used to return the FIRST other-team
            // driver in roster order - an arbitrary pick with no relation to the
            // player's actual racing, which is why the early-career rival felt
            // meaningless ("no idea who my rival is"). Before any points exist
            // (ReevaluateRival takes over on standings after a few rounds), the
            // most sensible rival is the driver in the most COMPARABLE car -
            // the one the player will genuinely fight on track: pick the
            // other-team driver whose team reputation is closest to the
            // player's own team's.
            TeamData playerTeam = data.FindTeam(teamId);
            int playerTeamReputation = playerTeam != null ? playerTeam.reputation : 80;
            DriverData bestCandidate = null;
            int bestGap = int.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].id == selectedDriverId || candidates[i].teamId == teamId)
                {
                    continue;
                }

                TeamData candidateTeam = data.FindTeam(candidates[i].teamId);
                int gap = candidateTeam == null ? int.MaxValue : Mathf.Abs(candidateTeam.reputation - playerTeamReputation);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    bestCandidate = candidates[i];
                }
            }

            if (bestCandidate != null)
            {
                return bestCandidate.id;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].id != selectedDriverId)
                {
                    return candidates[i].id;
                }
            }

            return "";
        }

        int ContractTargetForTeam(string teamId)
        {
            TeamData team = data.FindTeam(teamId);
            if (team == null)
            {
                return 8;
            }

            if (team.reputation >= 90)
            {
                return 3;
            }

            if (team.reputation >= 82)
            {
                return 5;
            }

            if (team.reputation >= 74)
            {
                return 8;
            }

            return 10;
        }

        // =====================================================================
        // Part 21: end-of-season / new-season system.
        // =====================================================================

        // Snapshots a full, frozen record of the season that just finished -
        // called once, from ApplyRaceResults, right as the final calendar
        // round rolls over, BEFORE any live state (standings, rivalry
        // counters, upgrades, objectives) is touched for the new season.
        SeasonArchive BuildSeasonArchive(CalendarEventData lastRaceEvent)
        {
            SeasonArchive archive = new SeasonArchive
            {
                season = Save.currentSeason,
                finalDriverStandings = new List<StandingEntry>(Save.driverStandings),
                finalConstructorStandings = new List<StandingEntry>(Save.constructorStandings),
                contractTargetThatSeason = Save.contractTargetPosition
            };

            if (archive.finalDriverStandings.Count > 0)
            {
                archive.driverChampionId = archive.finalDriverStandings[0].id;
                archive.driverChampionName = archive.finalDriverStandings[0].displayName;
            }

            if (archive.finalConstructorStandings.Count > 0)
            {
                archive.constructorChampionId = archive.finalConstructorStandings[0].id;
                archive.constructorChampionName = archive.finalConstructorStandings[0].displayName;
            }

            StandingEntry playerStanding = Save.driverStandings.Find(entry => entry.id == "player");
            archive.playerDriverName = Save.playerDriverName;
            archive.playerFinalPosition = FindStandingPosition(Save.driverStandings, "player");
            archive.playerPoints = playerStanding != null ? playerStanding.points : 0;
            archive.playerWins = playerStanding != null ? playerStanding.wins : 0;
            archive.playerPodiums = playerStanding != null ? playerStanding.podiums : 0;
            archive.reputationAtSeasonEnd = Save.reputation;

            TeamData playerTeam = data.FindTeam(Save.playerTeamId);
            archive.playerTeamId = Save.playerTeamId;
            archive.playerTeamName = playerTeam != null ? playerTeam.name : Save.playerTeamId;
            StandingEntry teamStanding = Save.constructorStandings.Find(entry => entry.id == Save.playerTeamId);
            archive.playerTeamFinalPosition = FindStandingPosition(Save.constructorStandings, Save.playerTeamId);
            archive.playerTeamPoints = teamStanding != null ? teamStanding.points : 0;

            DriverData teammateDriver = data.FindTeammateDriver(Save.playerTeamId, Save.selectedDriverId, Save.driverTransferRecords);
            if (teammateDriver != null)
            {
                StandingEntry teammateStanding = Save.driverStandings.Find(entry => entry.id == teammateDriver.id);
                archive.teammateId = teammateDriver.id;
                archive.teammateName = teammateDriver.displayName;
                archive.teammatePoints = teammateStanding != null ? teammateStanding.points : 0;
                archive.teammateFinalPosition = FindStandingPosition(Save.driverStandings, teammateDriver.id);
            }

            archive.teammateHeadToHeadWins = Save.teammateRaceWins;
            archive.teammateHeadToHeadLosses = Save.teammateRaceLosses;
            archive.rivalId = Save.rivalDriverId;
            DriverData rivalDriver = data.FindDriver(Save.rivalDriverId);
            archive.rivalName = rivalDriver != null ? rivalDriver.displayName : "";
            archive.rivalRaceWins = Save.rivalRaceWins;
            archive.rivalRaceLosses = Save.rivalRaceLosses;

            List<RaceResultRecord> seasonRecords = Save.raceResults.FindAll(r => r.season == Save.currentSeason);
            seasonRecords.Sort((a, b) => a.round.CompareTo(b.round));

            int bestFinish = int.MaxValue;
            int worstFinish = -1;
            int bestComeback = 0;
            int worstCollapse = 0;
            int dnfs = 0;
            int penalizedRaces = 0;
            int fastestLaps = 0;
            Dictionary<string, int> penaltyCounts = new Dictionary<string, int>();

            for (int i = 0; i < seasonRecords.Count; i++)
            {
                RaceResultRecord record = seasonRecords[i];
                RaceResultEntry playerEntry = record.results.Find(entry => entry.isPlayer);
                if (playerEntry != null)
                {
                    if (playerEntry.finishingPosition < bestFinish)
                    {
                        bestFinish = playerEntry.finishingPosition;
                        archive.bestRaceEventName = record.eventName;
                        archive.bestRaceGridPosition = playerEntry.gridPosition;
                    }

                    if (playerEntry.finishingPosition > worstFinish)
                    {
                        worstFinish = playerEntry.finishingPosition;
                        archive.worstRaceEventName = record.eventName;
                    }

                    int gained = playerEntry.gridPosition - playerEntry.finishingPosition;
                    if (gained > bestComeback)
                    {
                        bestComeback = gained;
                        archive.biggestComebackEventName = record.eventName;
                        archive.biggestComebackPositionsGained = gained;
                    }

                    if (-gained > worstCollapse)
                    {
                        worstCollapse = -gained;
                        archive.biggestCollapseEventName = record.eventName;
                        archive.biggestCollapsePositionsLost = -gained;
                    }

                    if (!string.IsNullOrEmpty(playerEntry.penaltyReason) && playerEntry.penaltyReason.Contains("DNF"))
                    {
                        dnfs++;
                    }

                    if (playerEntry.penaltiesSeconds > 0f)
                    {
                        penalizedRaces++;
                    }
                }

                RaceResultEntry fastestLapEntry = null;
                for (int e = 0; e < record.results.Count; e++)
                {
                    RaceResultEntry entry = record.results[e];
                    if (entry.penaltiesSeconds > 0f)
                    {
                        penaltyCounts[entry.driverId] = penaltyCounts.ContainsKey(entry.driverId) ? penaltyCounts[entry.driverId] + 1 : 1;
                    }

                    if (entry.bestLapTime > 0f && (fastestLapEntry == null || entry.bestLapTime < fastestLapEntry.bestLapTime))
                    {
                        fastestLapEntry = entry;
                    }
                }

                if (fastestLapEntry != null && fastestLapEntry.isPlayer)
                {
                    fastestLaps++;
                }
            }

            archive.bestRaceFinish = bestFinish == int.MaxValue ? 0 : bestFinish;
            archive.worstRaceFinish = worstFinish < 0 ? 0 : worstFinish;
            archive.playerDnfs = dnfs;
            archive.playerPenalizedRaces = penalizedRaces;
            archive.playerFastestLaps = fastestLaps;

            List<QualifyingResultRecord> seasonQuali = Save.qualifyingResults.FindAll(r => r.season == Save.currentSeason);
            int poles = 0;
            for (int i = 0; i < seasonQuali.Count; i++)
            {
                QualifyingResultEntry playerQuali = seasonQuali[i].results.Find(entry => entry.isPlayer);
                if (playerQuali != null && playerQuali.position == 1)
                {
                    poles++;
                }
            }

            archive.playerPoles = poles;

            List<RaceReportRecord> seasonReports = Save.raceReports.FindAll(r => r.season == Save.currentSeason);
            int redFlags = 0;
            int safetyCars = 0;
            int majorIncidents = 0;
            for (int i = 0; i < seasonReports.Count; i++)
            {
                RaceControlSummary raceControl = seasonReports[i].raceControl;
                redFlags += Mathf.Max(0, raceControl.redFlagCount);
                safetyCars += Mathf.Max(0, raceControl.safetyCarDeployments);
                if (raceControl.wasChaotic)
                {
                    majorIncidents++;
                }
            }

            archive.redFlagCount = redFlags;
            archive.safetyCarCount = safetyCars;
            archive.majorIncidentCount = majorIncidents;

            archive.formSummary = Save.recentFormPositions.ConvertAll(position => "P" + position);
            archive.regulations = Save.regulationChanges.FindAll(r => r.season == Save.currentSeason);
            archive.teamPerformanceChanges = Save.teamPerformanceModifiers.FindAll(m => m.season == Save.currentSeason);
            archive.upgradesCompleted = new List<string>(Save.completedUpgradeIds);
            archive.objectives = Save.seasonObjectives != null ? new List<SeasonObjective>(Save.seasonObjectives) : new List<SeasonObjective>();

            List<string> headlines = new List<string>();
            for (int i = Save.newsArticles.Count - 1; i >= 0 && headlines.Count < 8; i--)
            {
                if (Save.newsArticles[i].season == Save.currentSeason)
                {
                    headlines.Add(Save.newsArticles[i].headline);
                }
            }

            archive.keyHeadlines = headlines;
            archive.awards = GenerateSeasonAwards(archive, seasonRecords, penaltyCounts);
            return archive;
        }

        List<SeasonAward> GenerateSeasonAwards(SeasonArchive archive, List<RaceResultRecord> seasonRecords, Dictionary<string, int> penaltyCounts)
        {
            List<SeasonAward> awards = new List<SeasonAward>();

            if (!string.IsNullOrEmpty(archive.driverChampionName))
            {
                awards.Add(new SeasonAward
                {
                    title = "Driver of the Year",
                    winnerName = archive.driverChampionName,
                    detail = "Crowned Season " + archive.season + " champion with " +
                              (archive.finalDriverStandings.Count > 0 ? archive.finalDriverStandings[0].points : 0) + " points."
                });
            }

            if (!string.IsNullOrEmpty(archive.constructorChampionName))
            {
                awards.Add(new SeasonAward
                {
                    title = "Team of the Year",
                    winnerName = archive.constructorChampionName,
                    detail = "Took the Season " + archive.season + " constructors' title."
                });
            }

            DriverData risingStar = null;
            int risingStarPosition = int.MaxValue;
            for (int i = 0; i < archive.finalDriverStandings.Count; i++)
            {
                DriverData candidate = data.FindDriver(archive.finalDriverStandings[i].id);
                if (candidate != null && candidate.experience <= 35 && i < risingStarPosition)
                {
                    risingStar = candidate;
                    risingStarPosition = i;
                }
            }

            if (risingStar != null)
            {
                awards.Add(new SeasonAward
                {
                    title = "Rising Star",
                    winnerName = risingStar.displayName,
                    detail = "Finished P" + (risingStarPosition + 1) + " with barely any experience coming in - one to watch."
                });
            }

            if (!string.IsNullOrEmpty(archive.bestRaceEventName))
            {
                awards.Add(new SeasonAward { title = "Best Race", winnerName = archive.playerDriverName, detail = "P" + archive.bestRaceFinish + " at " + archive.bestRaceEventName + "." });
            }

            if (!string.IsNullOrEmpty(archive.worstRaceEventName))
            {
                awards.Add(new SeasonAward { title = "Worst Race", winnerName = archive.playerDriverName, detail = "P" + archive.worstRaceFinish + " at " + archive.worstRaceEventName + "." });
            }

            if (Save.seasonArchives.Count > 0)
            {
                SeasonArchive previous = Save.seasonArchives[Save.seasonArchives.Count - 1];
                string mostImprovedName = null;
                int bestImprovement = 0;
                string disappointmentName = null;
                int worstDrop = 0;
                for (int i = 0; i < archive.finalDriverStandings.Count; i++)
                {
                    StandingEntry current = archive.finalDriverStandings[i];
                    int previousPosition = previous.finalDriverStandings.FindIndex(entry => entry.id == current.id) + 1;
                    if (previousPosition <= 0)
                    {
                        continue;
                    }

                    int change = previousPosition - (i + 1);
                    if (change > bestImprovement)
                    {
                        bestImprovement = change;
                        mostImprovedName = current.displayName;
                    }

                    if (-change > worstDrop)
                    {
                        worstDrop = -change;
                        disappointmentName = current.displayName;
                    }
                }

                if (mostImprovedName != null)
                {
                    awards.Add(new SeasonAward
                    {
                        title = "Most Improved Driver",
                        winnerName = mostImprovedName,
                        detail = "Climbed " + bestImprovement + " place" + (bestImprovement == 1 ? "" : "s") + " in the standings compared to last season."
                    });
                }

                if (disappointmentName != null)
                {
                    awards.Add(new SeasonAward
                    {
                        title = "Biggest Disappointment",
                        winnerName = disappointmentName,
                        detail = "Dropped " + worstDrop + " place" + (worstDrop == 1 ? "" : "s") + " in the standings compared to last season."
                    });
                }
            }

            if (archive.biggestComebackPositionsGained >= 5)
            {
                awards.Add(new SeasonAward
                {
                    title = "Strategy Masterclass",
                    winnerName = archive.playerDriverName,
                    detail = "Gained " + archive.biggestComebackPositionsGained + " places at " + archive.biggestComebackEventName + "."
                });
            }

            List<RaceReportRecord> seasonReports = Save.raceReports.FindAll(r => r.season == archive.season);
            RaceReportRecord mostChaotic = null;
            int mostChaoticScore = 0;
            for (int i = 0; i < seasonReports.Count; i++)
            {
                RaceReportRecord report = seasonReports[i];
                int score = Mathf.Max(0, report.raceControl.redFlagCount) * 5 + Mathf.Max(0, report.raceControl.safetyCarDeployments) * 3 + Mathf.Max(0, report.incidents.totalLockups);
                if (score > mostChaoticScore)
                {
                    mostChaoticScore = score;
                    mostChaotic = report;
                }
            }

            if (mostChaotic != null)
            {
                awards.Add(new SeasonAward { title = "Most Chaotic Race", winnerName = mostChaotic.eventName, detail = mostChaotic.raceControl.narrative });
            }

            string crashMagnetName = null;
            int mostPenalties = 0;
            foreach (KeyValuePair<string, int> pair in penaltyCounts)
            {
                if (pair.Value > mostPenalties)
                {
                    mostPenalties = pair.Value;
                    crashMagnetName = DriverNameOrFallback(pair.Key);
                }
            }

            if (crashMagnetName != null)
            {
                awards.Add(new SeasonAward
                {
                    title = "Crash Magnet",
                    winnerName = crashMagnetName,
                    detail = "Picked up penalties in " + mostPenalties + " race" + (mostPenalties == 1 ? "" : "s") + " this season."
                });
            }

            for (int i = 0; i < archive.finalDriverStandings.Count; i++)
            {
                string id = archive.finalDriverStandings[i].id;
                int penalties;
                penaltyCounts.TryGetValue(id, out penalties);
                if (penalties == 0)
                {
                    awards.Add(new SeasonAward
                    {
                        title = "Cleanest Driver",
                        winnerName = archive.finalDriverStandings[i].displayName,
                        detail = "Completed Season " + archive.season + " without a single penalty."
                    });
                    break;
                }
            }

            return awards;
        }

        string DriverNameOrFallback(string driverId)
        {
            DriverData driver = data.FindDriver(driverId);
            return driver != null ? driver.displayName : driverId;
        }

        // Resets everything a new season should reset (championship state,
        // rivalry/form momentum, contract target) while explicitly leaving
        // untouched everything a new season should preserve (reputation,
        // resource points, upgrades/department levels, player/team identity,
        // settings, unlocked features, and every historical archive/news/
        // race-result list) - then generates the new season's regulation
        // changes, team-performance evolution, and objectives, and writes the
        // offseason news reacting to the season that just closed.
        void BeginNewSeason(SeasonArchive completedSeason)
        {
            // Part 10 ordering: progression MUST use the completed season's
            // results and be available before transfers, teammate ratings, and
            // the new season's standings are finalized - so it runs first,
            // then the driver market reacts to the freshly-updated ratings.
            EnsureAllTeamDevelopmentStates();
            SyncPlayerTeamDevelopmentState();
            PopulateTeamDevelopmentArchive(completedSeason);
            GenerateDriverProgression(completedSeason);
            GenerateDriverTransfers(completedSeason);

            Save.driverStandings = data.CreateInitialDriverStandings(Save.playerDriverName, Save.playerTeamId, Save.selectedDriverId, Save.driverTransferRecords);
            Save.constructorStandings = data.CreateInitialConstructorStandings();
            EnsurePlayerReplacesDriverSeat();
            // Standings-drift fix: both lists are freshly built at zero above, so
            // this is a no-op in the normal case - but it guarantees the new
            // season starts from a provably-consistent state rather than trusting
            // CreateInitialConstructorStandings/CreateInitialDriverStandings to
            // agree by construction.
            RecalculateConstructorStandingsFromDrivers();

            Save.recentFormPositions = new List<int>();
            Save.teammateRaceWins = 0;
            Save.teammateRaceLosses = 0;
            Save.teammateQualifyingWins = 0;
            Save.teammateQualifyingLosses = 0;
            Save.rivalRaceWins = 0;
            Save.rivalRaceLosses = 0;
            Save.rivalQualifyingWins = 0;
            Save.rivalQualifyingLosses = 0;
            Save.roundsSinceRivalPicked = 0;

            // Rivalry evolution: a real rivalry mostly carries over rather than
            // resetting just because the calendar did, but there's a real
            // chance the paddock's pecking order has shifted enough by the new
            // season that a fresh rival makes more sense.
            if (Random.value < 0.25f)
            {
                string previousRivalId = Save.rivalDriverId;
                Save.rivalDriverId = PickRivalId(Save.playerTeamId, Save.selectedDriverId);
                DriverData newRival = string.IsNullOrEmpty(Save.rivalDriverId) ? null : data.FindDriver(Save.rivalDriverId);
                if (newRival != null && Save.rivalDriverId != previousRivalId)
                {
                    // Same visibility rule as ReevaluateRival: a rival change the
                    // player is never told about reads as the system picking
                    // names at random.
                    AddNewsArticle(
                        "New season, new rival: " + newRival.displayName,
                        "The winter shake-up puts " + newRival.displayName + " in " + Save.playerDriverName +
                        "'s sights as the season's benchmark rival.",
                        NewsCategoryRivalry);
                }
            }

            // Season-end stakes: pay out the objectives and judge the contract
            // target BEFORE GenerateSeasonObjectives/ContractTargetForTeam below
            // replace last season's objective list and target. Without this the
            // objectives panel and contract line were purely decorative - nothing
            // in the game ever read them back.
            ApplySeasonEndConsequences(completedSeason);

            Save.contractTargetPosition = ContractTargetForTeam(Save.playerTeamId);
            Save.preSeasonTestingSeen = false;

            // New car, new attempt: a project that failed against last season's
            // car is not un-developable forever. Leaving these latched permanently
            // locked whole departments out of tiers 2-3 (AI teams have no rework
            // path at all, and a player "abandon" silently walled off everything
            // behind the upgrade's prerequisite chain for the rest of the career).
            Save.failedUpgradeIds.Clear();
            for (int i = 0; i < Save.teamDevelopmentStates.Count; i++)
            {
                Save.teamDevelopmentStates[i].failedUpgradeIds.Clear();
            }

            ApplyRegulationReset();
            GenerateRegulationChanges();
            GenerateTeamPerformanceEvolution(completedSeason);

            // Part 7/10: persistent R&D carries into the new season (regulation
            // resets above already stripped any invalidated categories) - just
            // re-snapshot each team's season-start rating for the new season's
            // Team Ratings screen.
            for (int i = 0; i < Save.teamDevelopmentStates.Count; i++)
            {
                TeamDevelopmentState state = Save.teamDevelopmentStates[i];
                TeamData team = data.FindTeam(state.teamId);
                if (team != null)
                {
                    RecalculateTeamRating(team, state);
                }

                state.ratingAtSeasonStart = state.currentRating;
            }

            GenerateSeasonObjectives();
            GenerateOffseasonNews(completedSeason);
            Write();
        }

        // Season-end stakes: objectives pay out and the contract target is
        // actually judged. Called from BeginNewSeason while Save.seasonObjectives
        // still holds the COMPLETED season's list (GenerateSeasonObjectives
        // replaces it later in the same method) and completedSeason carries the
        // frozen final position/target for that season.
        void ApplySeasonEndConsequences(SeasonArchive completedSeason)
        {
            int achievedCount = 0;
            if (Save.seasonObjectives != null)
            {
                for (int i = 0; i < Save.seasonObjectives.Count; i++)
                {
                    if (Save.seasonObjectives[i] != null && Save.seasonObjectives[i].achieved)
                    {
                        achievedCount++;
                    }
                }
            }

            if (achievedCount > 0)
            {
                int objectiveBonus = Mathf.RoundToInt(achievedCount * 120f * Save.currentSeasonResourceMultiplier);
                Save.resourcePoints += objectiveBonus;
                Save.reputation += achievedCount * 2;
                AddNewsArticle(
                    Save.playerDriverName + " delivers on " + achievedCount + " season objective" + (achievedCount == 1 ? "" : "s"),
                    "The board signs off a satisfied end-of-season review: " + achievedCount + " of the season's objectives were met, releasing a " +
                    objectiveBonus + " RP development bonus for the new campaign.",
                    NewsCategoryGeneral);
            }

            if (completedSeason == null || completedSeason.playerFinalPosition <= 0)
            {
                return;
            }

            bool metTarget = completedSeason.playerFinalPosition <= completedSeason.contractTargetThatSeason;
            if (metTarget)
            {
                Save.consecutiveContractMisses = 0;
                int contractBonus = Mathf.RoundToInt(150f * Save.currentSeasonResourceMultiplier);
                Save.resourcePoints += contractBonus;
                Save.reputation += 3;
                AddNewsArticle(
                    Save.playerDriverName + " meets contract target",
                    "P" + completedSeason.playerFinalPosition + " against a P" + completedSeason.contractTargetThatSeason +
                    " target keeps the team management happy - a " + contractBonus + " RP vote of confidence follows the driver into the new season.",
                    NewsCategoryGeneral);
                return;
            }

            Save.consecutiveContractMisses++;
            Save.reputation = Mathf.Max(0, Save.reputation - 3);
            if (Save.consecutiveContractMisses >= 2)
            {
                // Escalating squeeze rather than a hard sacking (there is no
                // seat-change flow yet): repeated misses cost real development
                // budget, so under-delivering seasons compound instead of being
                // consequence-free.
                int budgetCut = Mathf.RoundToInt(Save.resourcePoints * 0.25f);
                Save.resourcePoints = Mathf.Max(0, Save.resourcePoints - budgetCut);
                AddNewsArticle(
                    Save.playerDriverName + "'s seat under real pressure",
                    "A second straight missed contract target (P" + completedSeason.playerFinalPosition + " vs P" +
                    completedSeason.contractTargetThatSeason + ") triggers a boardroom review - " + budgetCut +
                    " RP is pulled from the driver's development programme and patience is now openly said to be exhausted.",
                    NewsCategoryGeneral);
            }
            else
            {
                AddNewsArticle(
                    Save.playerDriverName + " misses contract target",
                    "P" + completedSeason.playerFinalPosition + " against a contracted P" + completedSeason.contractTargetThatSeason +
                    " puts the driver on notice - management makes clear a repeat next season will have consequences.",
                    NewsCategoryGeneral);
            }
        }

        // Part 8: freezes each team's development summary for the season that
        // just ended into its archive - called right at the start of
        // BeginNewSeason, before any TeamDevelopmentState is touched for the
        // new season, so ratingAtSeasonStart/currentRating still reflect the
        // completed season.
        void PopulateTeamDevelopmentArchive(SeasonArchive completedSeason)
        {
            if (completedSeason == null)
            {
                return;
            }

            List<TeamDevelopmentRecord> records = new List<TeamDevelopmentRecord>();
            for (int i = 0; i < data.Teams.teams.Count; i++)
            {
                TeamData team = data.Teams.teams[i];
                if (team == null)
                {
                    continue;
                }

                TeamDevelopmentState state = GetOrCreateTeamDevelopmentState(team.id);
                string strongestArea = "None";
                int strongestCount = 0;
                Dictionary<string, int> categoryCounts = new Dictionary<string, int>();
                for (int u = 0; u < state.completedUpgradeIds.Count; u++)
                {
                    UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == state.completedUpgradeIds[u]);
                    if (upgrade == null)
                    {
                        continue;
                    }

                    int count = categoryCounts.ContainsKey(upgrade.category) ? categoryCounts[upgrade.category] + 1 : 1;
                    categoryCounts[upgrade.category] = count;
                    if (count > strongestCount)
                    {
                        strongestCount = count;
                        strongestArea = upgrade.category;
                    }
                }

                records.Add(new TeamDevelopmentRecord
                {
                    teamId = team.id,
                    teamName = team.name,
                    ratingAtSeasonStart = state.ratingAtSeasonStart,
                    ratingAtSeasonEnd = state.currentRating,
                    ratingChange = state.currentRating - state.ratingAtSeasonStart,
                    upgradesCompleted = state.completedUpgradeIds.Count,
                    projectsFailed = state.failedUpgradeIds.Count,
                    strongestDevelopmentArea = strongestArea,
                    constructorFinish = FindStandingPosition(completedSeason.finalConstructorStandings, team.id)
                });
            }

            completedSeason.teamDevelopment = records;
        }

        static readonly string[] RegulationFlavorCategories = { "Tyre Wear", "Cost Cap", "Reliability", "DRS & Race Control", "Pit Equipment" };

        // Generates 1-3 RegulationChange entries for the new season on top of
        // the existing category-strip mechanic (ApplyRegulationReset, called
        // just before this) - Aero/Chassis/Power Unit/Durability/Tyre
        // Management/ERS categories feed that existing upgrade-scrapping
        // system and team-performance evolution below; the extra flavour
        // categories (Tyre Wear/Cost Cap/Reliability/DRS/Pit Equipment) get
        // their own small, explained effects instead.
        void GenerateRegulationChanges()
        {
            Save.currentSeasonTyreWearMultiplier = 1f;
            Save.currentSeasonResourceMultiplier = 1f;

            List<string> pool = new List<string>(DepartmentNames);
            pool.AddRange(RegulationFlavorCategories);

            float countRoll = Random.value;
            int count = Mathf.Min(pool.Count, countRoll < 0.4f ? 1 : (countRoll < 0.85f ? 2 : 3));
            for (int i = 0; i < count; i++)
            {
                int index = Random.Range(0, pool.Count);
                string category = pool[index];
                pool.RemoveAt(index);

                RegulationChange change = BuildRegulationChange(category);
                Save.regulationChanges.Add(change);

                if (change.category == "Tyre Wear")
                {
                    Save.currentSeasonTyreWearMultiplier = Mathf.Clamp(1f - change.magnitude, 0.82f, 1.22f);
                }
                else if (change.category == "Cost Cap")
                {
                    Save.currentSeasonResourceMultiplier = Mathf.Clamp(1f + change.magnitude, 0.78f, 1.28f);
                }

                AddNewsArticle("Regulation update: " + change.title, change.description, NewsCategoryRegulations);
            }
        }

        RegulationChange BuildRegulationChange(string category)
        {
            float magnitude = Random.Range(0.05f, 0.12f) * (Random.value < 0.5f ? -1f : 1f);
            RegulationChange change = new RegulationChange { season = Save.currentSeason, category = category, magnitude = magnitude };

            switch (category)
            {
                case "Tyre Wear":
                    change.title = magnitude > 0 ? "Tyre wear model tightened" : "Tyre wear model relaxed";
                    change.description = magnitude > 0
                        ? "Season " + Save.currentSeason + " compounds degrade faster - tyre management and pit strategy matter more than ever."
                        : "Season " + Save.currentSeason + " compounds degrade more gently - expect longer stints and fewer forced stops.";
                    change.beneficiaryHint = magnitude > 0 ? "Teams strong on tyre management" : "Teams that struggled with tyre life";
                    change.loserHint = magnitude > 0 ? "Teams weak on tyre management" : "Teams built around aggressive one-stop strategies";
                    break;
                case "Cost Cap":
                    change.title = magnitude < 0 ? "Cost cap eased" : "Cost cap tightened";
                    change.description = magnitude < 0
                        ? "A relaxed budget cap this season means development points go further."
                        : "A stricter budget cap this season squeezes every team's development budget.";
                    change.beneficiaryHint = magnitude < 0 ? "Every team, especially backmarkers" : "Well-funded front-runners";
                    change.loserHint = magnitude < 0 ? "None directly" : "Smaller teams with thin resource margins";
                    break;
                case "Reliability":
                    change.title = magnitude > 0 ? "Reliability crackdown" : "Reliability rules relaxed";
                    change.description = magnitude > 0
                        ? "Stricter component-life rules this season mean fragile cars are more likely to be caught out."
                        : "Looser component-life rules this season give engineers more room to push the limits safely.";
                    change.beneficiaryHint = magnitude > 0 ? "Teams with bullet-proof cars" : "Teams that push development hard";
                    change.loserHint = magnitude > 0 ? "Teams with fragile cars" : "None directly";
                    break;
                case "DRS & Race Control":
                    change.title = "DRS and race-control tweaks";
                    change.description = "Season " + Save.currentSeason + " brings minor changes to DRS activation and race-control procedures - expect slightly different overtaking and flag behaviour.";
                    change.beneficiaryHint = "Overtaking-focused drivers";
                    change.loserHint = "Track-position-focused teams";
                    break;
                case "Pit Equipment":
                    change.title = "Pit equipment changes";
                    change.description = "Updated Season " + Save.currentSeason + " pit equipment regulations shift the emphasis in the pit lane - crews will need to adapt.";
                    change.beneficiaryHint = "Teams with well-drilled pit crews";
                    change.loserHint = "Teams that struggled with pit execution";
                    break;
                default:
                    change.title = category + " regulation shift";
                    change.description = "Season " + Save.currentSeason + " brings a " + category.ToLowerInvariant() + " regulation change - completed development in this area may be affected.";
                    change.beneficiaryHint = magnitude > 0 ? "Teams weak in " + category : "Teams strong in " + category;
                    change.loserHint = magnitude > 0 ? "Teams strong in " + category : "Teams weak in " + category;
                    break;
            }

            return change;
        }

        // Small, bounded season-to-season team performance swing - mean
        // reversion (front-runners regress slightly, backmarkers gain
        // slightly on average) plus a regulation-driven bias for whichever
        // Aero/Chassis/Power Unit/Durability/Tyre Management/ERS categories
        // are affected this season, plus randomness. Never mutates the
        // shared TeamData/CarPerformanceData reference data - applied at
        // read time only (see GetEffectiveTeamCar).
        // Driver market: a small number of AI-only seat swaps each season - the
        // weakest-rated driver on one team trades places with the weakest-rated
        // driver on another, so every team keeps exactly its original driver
        // count. The player's own team is never a source or destination -
        // EnsurePlayerReplacesDriverSeat (called right after this in
        // BeginNewSeason) owns the player's own seat/teammate assignment, so a
        // market move here can never contest or duplicate it.
        void GenerateDriverTransfers(SeasonArchive completedSeason)
        {
            List<TeamData> eligibleTeams = data.Teams.teams.FindAll(t => t != null && t.id != Save.playerTeamId);
            if (eligibleTeams.Count < 2)
            {
                return;
            }

            int transferCount = Random.Range(1, 4);
            List<string> movedThisSeason = new List<string>();
            for (int attempt = 0; attempt < transferCount; attempt++)
            {
                TeamData teamA = eligibleTeams[Random.Range(0, eligibleTeams.Count)];
                TeamData teamB = eligibleTeams[Random.Range(0, eligibleTeams.Count)];
                if (teamA == null || teamB == null || teamA.id == teamB.id)
                {
                    continue;
                }

                DriverData driverA = WeakestDriverOnTeam(teamA.id, movedThisSeason);
                DriverData driverB = WeakestDriverOnTeam(teamB.id, movedThisSeason);
                if (driverA == null || driverB == null)
                {
                    continue;
                }

                Save.driverTransferRecords.Add(new DriverTransferRecord { driverId = driverA.id, newTeamId = teamB.id, season = Save.currentSeason });
                Save.driverTransferRecords.Add(new DriverTransferRecord { driverId = driverB.id, newTeamId = teamA.id, season = Save.currentSeason });
                movedThisSeason.Add(driverA.id);
                movedThisSeason.Add(driverB.id);

                AddNewsArticle(
                    driverA.displayName + " and " + driverB.displayName + " swap seats",
                    driverA.displayName + " moves to " + teamB.name + " while " + driverB.displayName + " heads to " + teamA.name + " as the driver market shakes up ahead of Season " + Save.currentSeason + ".",
                    NewsCategoryTransfers);
            }
        }

        DriverData WeakestDriverOnTeam(string teamId, List<string> excludeIds)
        {
            List<DriverData> roster = data.GetDriversForTeam(teamId, Save.driverTransferRecords);
            DriverData weakest = null;
            for (int i = 0; i < roster.Count; i++)
            {
                DriverData candidate = roster[i];
                if (candidate == null || excludeIds.Contains(candidate.id))
                {
                    continue;
                }

                if (weakest == null || candidate.OverallRating < weakest.OverallRating)
                {
                    weakest = candidate;
                }
            }

            return weakest;
        }

        // =====================================================================
        // Part 2/3: measuring each driver's season and turning it into
        // independent, per-subrating rating changes.
        // =====================================================================

        // "player" result/qualifying entries mean the player's OWN seat - when
        // the player is racing as a real drivers.json driver, that performance
        // belongs to Save.selectedDriverId for progression purposes (never a
        // separate "player" identity alongside their real one); when the
        // player made a custom driver, "player" IS the tracked identity.
        string MapDriverIdForProgression(string rawId)
        {
            if (rawId == "player" && Save.useExistingDriver && !string.IsNullOrEmpty(Save.selectedDriverId))
            {
                return Save.selectedDriverId;
            }

            return rawId;
        }

        DriverSeasonPerformance GetOrCreateSeasonPerformance(string driverId)
        {
            if (Save.driverSeasonPerformances == null)
            {
                Save.driverSeasonPerformances = new List<DriverSeasonPerformance>();
            }

            DriverSeasonPerformance perf = Save.driverSeasonPerformances.Find(p => p.driverId == driverId && p.season == Save.currentSeason);
            if (perf == null)
            {
                perf = new DriverSeasonPerformance { driverId = driverId, season = Save.currentSeason };
                Save.driverSeasonPerformances.Add(perf);
            }

            return perf;
        }

        // Ranks every team by this round's effective car rating (same
        // GetEffectiveTeamCar/RatingCalculator every other system uses), so
        // "expected result" reflects genuine competitiveness, not just grid slot.
        List<string> RankTeamsByCarStrength()
        {
            List<KeyValuePair<string, int>> ranked = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < data.Teams.teams.Count; i++)
            {
                TeamData team = data.Teams.teams[i];
                if (team == null)
                {
                    continue;
                }

                CarPerformanceData effective = GetEffectiveTeamCar(team, data.FindCar(team.carPerformanceId));
                ranked.Add(new KeyValuePair<string, int>(team.id, RatingCalculator.GetCarOverall(effective)));
            }

            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));
            List<string> order = new List<string>();
            for (int i = 0; i < ranked.Count; i++)
            {
                order.Add(ranked[i].Key);
            }

            return order;
        }

        int TeamCarRank(List<string> order, string teamId)
        {
            int index = order.IndexOf(teamId);
            return index < 0 ? Mathf.Max(1, order.Count / 2) : index + 1;
        }

        void UpdateDriverSeasonPerformanceFromQualifying(List<QualifyingResultEntry> results)
        {
            if (results == null || results.Count == 0)
            {
                return;
            }

            List<string> carRanking = RankTeamsByCarStrength();
            for (int i = 0; i < results.Count; i++)
            {
                QualifyingResultEntry entry = results[i];
                string driverId = MapDriverIdForProgression(entry.driverId);
                DriverSeasonPerformance perf = GetOrCreateSeasonPerformance(driverId);
                perf.qualifyingSessions++;

                // Expectation on the DRIVER scale, not the team scale: TeamCarRank
                // is 1..teamCount (~11) while qualifying positions run 1..~22, so
                // comparing them directly made the whole field "underperform" by
                // ~3.6 places per session by construction, bleeding every driver's
                // qualifying rating season after season. A team ranked r puts its
                // two drivers in slots 2r-1 and 2r; expecting their average
                // (2r - 0.5) makes the field exactly zero-sum.
                float expectedPosition = Mathf.Clamp(TeamCarRank(carRanking, entry.teamId) * 2f - 0.5f, 1f, results.Count);
                perf.sumQualifyingVsExpected += expectedPosition - entry.position;

                QualifyingResultEntry teammate = results.Find(r => r.teamId == entry.teamId && r.driverId != entry.driverId);
                if (teammate != null)
                {
                    if (entry.position < teammate.position)
                    {
                        perf.teammateQualifyingWins++;
                    }
                    else if (entry.position > teammate.position)
                    {
                        perf.teammateQualifyingLosses++;
                    }
                }
            }
        }

        void UpdateDriverSeasonPerformanceFromRace(CalendarEventData raceEvent, List<RaceResultEntry> results)
        {
            if (results == null || results.Count == 0)
            {
                return;
            }

            bool wetRace = raceEvent != null && !string.IsNullOrEmpty(raceEvent.weatherProfile) &&
                           (raceEvent.weatherProfile.ToLowerInvariant().Contains("rain") || raceEvent.weatherProfile.ToLowerInvariant().Contains("wet"));

            List<string> carRanking = RankTeamsByCarStrength();
            for (int i = 0; i < results.Count; i++)
            {
                RaceResultEntry entry = results[i];
                string driverId = MapDriverIdForProgression(entry.driverId);
                DriverSeasonPerformance perf = GetOrCreateSeasonPerformance(driverId);
                perf.racesCompleted++;

                // Same driver-scale conversion as the qualifying pass above: the
                // raw team rank (1..~11) is NOT comparable to finishing positions
                // (1..~22). Blending the team's expected driver slots (2r - 0.5)
                // with the actual grid slot keeps "expected" and "finished" on
                // one scale, so finishVsExpected is zero-sum across the field
                // instead of ~-3.6 for everyone, every race.
                int carRank = TeamCarRank(carRanking, entry.teamId);
                float carExpectedSlot = carRank * 2f - 0.5f;
                float expectedPosition = Mathf.Clamp(carExpectedSlot * 0.65f + entry.gridPosition * 0.35f, 1f, results.Count);

                bool isRetirement = !string.IsNullOrEmpty(entry.penaltyReason) && entry.penaltyReason.Contains("DNF");
                bool driverCaused = isRetirement && ContainsAny(entry.penaltyReason, "Collision", "Contact", "Crash", "Spin");
                bool mechanical = isRetirement && !driverCaused;

                if (isRetirement)
                {
                    if (mechanical)
                    {
                        perf.mechanicalRetirements++;
                    }
                    else
                    {
                        perf.driverCausedRetirements++;
                        perf.sumFinishVsExpected += -3f;
                        perf.finishVsExpectedSamples.Add(-3f);
                    }
                }
                else
                {
                    float finishVsExpected = expectedPosition - entry.finishingPosition;
                    perf.sumFinishVsExpected += finishVsExpected;
                    perf.finishVsExpectedSamples.Add(finishVsExpected);
                }

                perf.sumPositionsGained += entry.gridPosition - entry.finishingPosition;
                perf.overtakesMade += entry.overtakesMade;
                perf.lockups += entry.lockups;
                perf.trackLimitWarnings += entry.trackLimitWarnings;
                perf.sumFlatSpotPercent += entry.flatSpotPercent;
                if (entry.penaltiesSeconds > 0f)
                {
                    perf.penalizedRaces++;
                    perf.sumPenaltySeconds += entry.penaltiesSeconds;
                }

                if (wetRace && !isRetirement)
                {
                    perf.wetRacesCompleted++;
                    perf.sumWetPositionsGained += entry.gridPosition - entry.finishingPosition;
                }

                RaceResultEntry teammate = results.Find(r => r.teamId == entry.teamId && r.driverId != entry.driverId);
                if (teammate != null && !isRetirement)
                {
                    bool teammateRetired = !string.IsNullOrEmpty(teammate.penaltyReason) && teammate.penaltyReason.Contains("DNF");
                    if (!teammateRetired)
                    {
                        if (entry.finishingPosition < teammate.finishingPosition)
                        {
                            perf.teammateRaceWins++;
                        }
                        else if (entry.finishingPosition > teammate.finishingPosition)
                        {
                            perf.teammateRaceLosses++;
                        }
                    }
                }
            }
        }

        static bool ContainsAny(string text, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (text.IndexOf(needles[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // Confidence scaling by sample size - a driver with only a handful of
        // races this season (injury, late-season call-up) should barely move,
        // while a driver who ran most/all of the season gets the full swing.
        static float ConfidenceScale(int racesCompleted)
        {
            if (racesCompleted < 4)
            {
                return 0.35f;
            }

            if (racesCompleted <= 8)
            {
                return 0.7f;
            }

            return 1f;
        }

        // Converts a normalized [-1..1] performance score into a bounded,
        // integer rating change for one subrating - developmentPotential makes
        // good performance easier to convert into gains (and raises the
        // effective ceiling before diminishing returns bite), experience makes
        // poor form hit harder (a veteran in decline regresses faster than a
        // young driver protected by their potential), and a driver already
        // near the 40-99 ceiling gets diminishing returns on further gains.
        // The season subrating-change curve lives in the engine-adjacent
        // DriverProgressionRules (F1Game.Core); this wrapper keeps the private
        // signature every call site already uses.
        static int ScoreToDelta(float score, int currentEffectiveRating, int developmentPotential, int experience, float confidenceScale, float maxMagnitude)
        {
            return F1Game.Core.DriverProgressionRules.RatingDelta(
                score, currentEffectiveRating, developmentPotential, experience, confidenceScale, maxMagnitude);
        }

        // Driver progression: for every driver (plus the custom player driver,
        // if any), scores this completed season's actual performance per
        // subrating (Part 2/3 subrating rules) and generates independent,
        // bounded rating changes - never one shared delta applied to every
        // stat. GetEffectiveDriver applies the resulting cumulative per-stat
        // deltas at read time, clamped to a realistic 40-99 range, so this can
        // never surface as an unrealistic in-race stat even over a long career.
        void GenerateDriverProgression(SeasonArchive completedSeason)
        {
            int completedSeasonNumber = Save.currentSeason - 1;
            List<string> subjects = new List<string>();
            for (int i = 0; i < data.Drivers.drivers.Count; i++)
            {
                if (data.Drivers.drivers[i] != null)
                {
                    subjects.Add(data.Drivers.drivers[i].id);
                }
            }

            if (!Save.useExistingDriver)
            {
                EnsureCustomPlayerDriverBase();
                subjects.Add("player");
            }

            List<DriverRatingModifier> thisSeason = new List<DriverRatingModifier>();
            List<DriverSeasonChangeRecord> changeRecords = new List<DriverSeasonChangeRecord>();

            // Field-relative baselines (per report - "again the same thing
            // happened in a new season. everyones rating went down"): the
            // overtaking/tyre/awareness scores used ABSOLUTE neutral points
            // (0.8 overtakes/race, 8% flat spot, ~zero track-limit warnings),
            // so whenever the season's racing character drifted from those
            // constants - processional races, lockup-heavy tyres, a track-
            // limit-happy AI field - the ENTIRE grid scored negative on the
            // same stats and the whole field bled rating together, season
            // after season. Each of those scores is now centred on this
            // season's actual field average, making them zero-sum by
            // construction: a driver only gains or loses relative to the grid
            // they actually raced against.
            float fieldOvertakesPerRace = 0.8f;
            float fieldFlatSpotPercent = 8f;
            float fieldTrackLimitsPerRace = 0f;
            {
                float overtakesSum = 0f, flatSpotSum = 0f, trackLimitsSum = 0f;
                int fieldSamples = 0;
                for (int i = 0; i < Save.driverSeasonPerformances.Count; i++)
                {
                    DriverSeasonPerformance sample = Save.driverSeasonPerformances[i];
                    if (sample == null || sample.season != completedSeasonNumber || sample.racesCompleted <= 0)
                    {
                        continue;
                    }

                    fieldSamples++;
                    overtakesSum += sample.overtakesMade / (float)sample.racesCompleted;
                    flatSpotSum += sample.sumFlatSpotPercent / sample.racesCompleted;
                    trackLimitsSum += sample.trackLimitWarnings / (float)sample.racesCompleted;
                }

                if (fieldSamples > 0)
                {
                    fieldOvertakesPerRace = overtakesSum / fieldSamples;
                    fieldFlatSpotPercent = flatSpotSum / fieldSamples;
                    fieldTrackLimitsPerRace = trackLimitsSum / fieldSamples;
                }
            }

            // Car ranking for the championship-outcome term below. This runs
            // before GenerateTeamPerformanceEvolution in BeginNewSeason, so it
            // still reflects the machinery the completed season was raced with.
            List<string> progressionCarRanking = RankTeamsByCarStrength();

            for (int s = 0; s < subjects.Count; s++)
            {
                string driverId = subjects[s];
                DriverData driver = driverId == "player" ? Save.customPlayerDriverBase : data.FindDriver(driverId);
                if (driver == null)
                {
                    continue;
                }

                DriverRatingModifier previous = Save.driverRatingModifiers.Find(m => m.driverId == driverId && m.season == completedSeasonNumber);
                MigrateLegacyRatingModifier(previous);
                DriverSeasonPerformance perf = Save.driverSeasonPerformances.Find(p => p.driverId == driverId && p.season == completedSeasonNumber);
                int races = perf != null ? perf.racesCompleted : 0;
                float confidenceScale = ConfidenceScale(races);

                DriverData previousEffective = ApplyRatingModifier(driver, driver.teamId, previous);
                int previousOverall = RatingCalculator.GetDriverOverall(previousEffective);

                float paceScore = 0f, racecraftScore = 0f, qualifyingScore = 0f, overtakingScore = 0f, defendingScore = 0f;
                float tyreScore = 0f, wetScore = 0f, consistencyScore = 0f, awarenessScore = 0f, aggressionScore = 0f;

                if (perf != null && races > 0)
                {
                    float avgFinishVsExpected = perf.sumFinishVsExpected / races;
                    float avgPositionsGained = perf.sumPositionsGained / races;
                    float avgOvertakes = perf.overtakesMade / (float)races;
                    float avgFlatSpot = perf.sumFlatSpotPercent / races;

                    int qTotal = perf.teammateQualifyingWins + perf.teammateQualifyingLosses;
                    float qualH2H = qTotal > 0 ? (perf.teammateQualifyingWins - perf.teammateQualifyingLosses) / (float)qTotal : 0f;
                    float avgQualVsExpected = perf.qualifyingSessions > 0 ? perf.sumQualifyingVsExpected / perf.qualifyingSessions : 0f;

                    int rTotal = perf.teammateRaceWins + perf.teammateRaceLosses;
                    float raceH2H = rTotal > 0 ? (perf.teammateRaceWins - perf.teammateRaceLosses) / (float)rTotal : 0f;

                    paceScore = Mathf.Clamp(avgFinishVsExpected / 4f, -1f, 1f);
                    racecraftScore = Mathf.Clamp((avgFinishVsExpected * 0.5f + avgPositionsGained * 0.3f + raceH2H * 0.4f) / 3f, -1f, 1f);
                    qualifyingScore = Mathf.Clamp(qualH2H * 0.6f + Mathf.Clamp(avgQualVsExpected / 3f, -1f, 1f) * 0.4f, -1f, 1f);
                    // Neutral point lowered 1.2 -> 0.8 and the downside floored:
                    // a front-runner (or anyone in a short race) often has NO
                    // overtaking opportunities at all, and a dominant season of
                    // wins from pole used to read as an overtaking DECLINE. Few
                    // overtakes is mostly absence of opportunity, not lost skill,
                    // so it can only nudge the rating down slightly - while a
                    // genuinely busy season still earns the full upside.
                    // Field-relative (see the baseline block above): neutral is
                    // this season's average overtakes/race, not a constant.
                    overtakingScore = Mathf.Clamp((avgOvertakes - fieldOvertakesPerRace) / 2.5f, -0.25f, 1f);
                    // Defending has no dedicated per-battle telemetry yet (see
                    // DriverSeasonPerformance) - proxied from the same-car
                    // head-to-head record, kept intentionally small.
                    defendingScore = Mathf.Clamp(raceH2H * 0.5f, -0.6f, 0.6f);
                    // Field-relative: measured against this season's average
                    // flat-spotting, so a lockup-heavy physics/tyre meta can't
                    // read as the whole grid forgetting tyre management.
                    tyreScore = Mathf.Clamp(-(avgFlatSpot - fieldFlatSpotPercent) / 10f, -1f, 1f);

                    if (perf.wetRacesCompleted > 0)
                    {
                        float avgWetGain = perf.sumWetPositionsGained / perf.wetRacesCompleted;
                        wetScore = Mathf.Clamp(avgWetGain / 3f, -1f, 1f);
                    }

                    if (perf.finishVsExpectedSamples.Count >= 2)
                    {
                        float mean = 0f;
                        for (int i = 0; i < perf.finishVsExpectedSamples.Count; i++)
                        {
                            mean += perf.finishVsExpectedSamples[i];
                        }

                        mean /= perf.finishVsExpectedSamples.Count;
                        float variance = 0f;
                        for (int i = 0; i < perf.finishVsExpectedSamples.Count; i++)
                        {
                            float diff = perf.finishVsExpectedSamples[i] - mean;
                            variance += diff * diff;
                        }

                        variance /= perf.finishVsExpectedSamples.Count;
                        float stdDev = Mathf.Sqrt(variance);
                        consistencyScore = Mathf.Clamp((4f - stdDev) / 4f, -1f, 1f);
                    }

                    consistencyScore = Mathf.Clamp(consistencyScore - perf.driverCausedRetirements * 0.25f - perf.penalizedRaces * 0.05f, -1f, 1f);

                    float avgTrackLimits = perf.trackLimitWarnings / (float)races;
                    // Field-relative on the track-limits component: a season
                    // where the WHOLE grid collects warnings (track-limit-happy
                    // AI, tight new layouts) must not read as every driver's
                    // awareness declining at once. Retirements/penalties stay
                    // absolute - causing a crash is bad regardless of the meta.
                    awarenessScore = Mathf.Clamp(-(perf.driverCausedRetirements * 0.4f + perf.penalizedRaces * 0.12f + (avgTrackLimits - fieldTrackLimitsPerRace) * 0.08f), -1f, 0.4f);
                    if (perf.driverCausedRetirements == 0 && perf.penalizedRaces == 0 && races >= 6)
                    {
                        awarenessScore = Mathf.Max(awarenessScore, 0.3f);
                    }

                    aggressionScore = Mathf.Clamp((avgOvertakes * 0.4f + perf.penalizedRaces * 0.15f) / 3f, -1f, 1f);

                    // Championship-outcome term (per report - "my rating didnt
                    // change even tho i came p1 in the wdc by double second
                    // places points"): every score above measures form vs the
                    // car's per-race expectation, so a champion in a title-
                    // worthy car scores ~zero everywhere BY CONSTRUCTION and
                    // the season's actual outcome never counted for anything.
                    // The final standing now feeds pace/racecraft/consistency:
                    // beating the car's expected championship slot scores
                    // positively, the champion gets an explicit floor, and a
                    // dominant title (>= 1.5x the runner-up's points) reads as
                    // the statement season it is. NOTE the standings list keys
                    // the player's seat as "player" even in an existing-driver
                    // career, while progression subjects use the real driver
                    // id - the seat mapping below bridges that (this was also
                    // why the champion's own outcome was invisible here).
                    if (completedSeason != null && completedSeason.finalDriverStandings != null && completedSeason.finalDriverStandings.Count > 1)
                    {
                        bool isPlayerSeat = driverId == "player" || (Save.useExistingDriver && driverId == Save.selectedDriverId);
                        string standingsId = isPlayerSeat && FindStandingPosition(completedSeason.finalDriverStandings, "player") > 0 ? "player" : driverId;
                        int standingPos = FindStandingPosition(completedSeason.finalDriverStandings, standingsId);
                        if (standingPos > 0)
                        {
                            string rankTeamId = !string.IsNullOrEmpty(driver.teamId) ? driver.teamId : Save.playerTeamId;
                            float expectedStanding = TeamCarRank(progressionCarRanking, rankTeamId) * 2f - 0.5f;
                            float championshipScore = Mathf.Clamp((expectedStanding - standingPos) / 6f, -0.5f, 0.8f);
                            if (standingPos == 1)
                            {
                                int champPoints = completedSeason.finalDriverStandings[0].points;
                                int runnerUpPoints = completedSeason.finalDriverStandings[1].points;
                                bool dominantTitle = champPoints >= Mathf.RoundToInt(runnerUpPoints * 1.5f);
                                championshipScore = Mathf.Max(championshipScore, dominantTitle ? 0.6f : 0.35f);
                            }

                            paceScore = Mathf.Clamp(paceScore + championshipScore * 0.5f, -1f, 1f);
                            racecraftScore = Mathf.Clamp(racecraftScore + championshipScore * 0.4f, -1f, 1f);
                            consistencyScore = Mathf.Clamp(consistencyScore + championshipScore * 0.3f, -1f, 1f);
                        }
                    }
                }

                int prevPaceDelta = previous != null ? previous.paceDelta : 0;
                int prevRacecraftDelta = previous != null ? previous.racecraftDelta : 0;
                int prevQualifyingDelta = previous != null ? previous.qualifyingDelta : 0;
                int prevTyreDelta = previous != null ? previous.tyreManagementDelta : 0;
                int prevWetDelta = previous != null ? previous.wetSkillDelta : 0;
                int prevConsistencyDelta = previous != null ? previous.consistencyDelta : 0;
                int prevAggressionDelta = previous != null ? previous.aggressionDelta : 0;
                int prevDefendingDelta = previous != null ? previous.defendingDelta : 0;
                int prevOvertakingDelta = previous != null ? previous.overtakingDelta : 0;
                int prevAwarenessDelta = previous != null ? previous.awarenessDelta : 0;
                int prevExperienceDelta = previous != null ? previous.experienceDelta : 0;

                int paceChange = ScoreToDelta(paceScore, ClampRating(driver.pace + prevPaceDelta), driver.developmentPotential, driver.experience, confidenceScale, 5f);
                int racecraftChange = ScoreToDelta(racecraftScore, ClampRating(driver.racecraft + prevRacecraftDelta), driver.developmentPotential, driver.experience, confidenceScale, 5f);
                int qualifyingChange = ScoreToDelta(qualifyingScore, ClampRating(driver.qualifying + prevQualifyingDelta), driver.developmentPotential, driver.experience, confidenceScale, 5f);
                int tyreChange = ScoreToDelta(tyreScore, ClampRating(driver.tyreManagement + prevTyreDelta), driver.developmentPotential, driver.experience, confidenceScale, 4f);
                int wetChange = perf != null && perf.wetRacesCompleted > 0
                    ? ScoreToDelta(wetScore, ClampRating(driver.wetSkill + prevWetDelta), driver.developmentPotential, driver.experience, confidenceScale, 4f)
                    : 0;
                int consistencyChange = ScoreToDelta(consistencyScore, ClampRating(driver.consistency + prevConsistencyDelta), driver.developmentPotential, driver.experience, confidenceScale, 5f);
                int defendingChange = ScoreToDelta(defendingScore, ClampRating(driver.defending + prevDefendingDelta), driver.developmentPotential, driver.experience, confidenceScale, 4f);
                int overtakingChange = ScoreToDelta(overtakingScore, ClampRating(driver.overtaking + prevOvertakingDelta), driver.developmentPotential, driver.experience, confidenceScale, 5f);
                int awarenessChange = ScoreToDelta(awarenessScore, ClampRating(driver.awareness + prevAwarenessDelta), driver.developmentPotential, driver.experience, confidenceScale, 4f);
                // Aggression is a style rating - limited to roughly -1..+1
                // unless the data is extreme.
                int aggressionChange = Mathf.Clamp(ScoreToDelta(aggressionScore, driver.aggression + prevAggressionDelta, driver.developmentPotential, driver.experience, confidenceScale, 1f), Mathf.Abs(aggressionScore) > 0.85f ? -2 : -1, Mathf.Abs(aggressionScore) > 0.85f ? 2 : 1);

                int currentExperience = Mathf.Clamp(driver.experience + prevExperienceDelta, 1, 99);
                float growth = currentExperience < 60 ? 3f : currentExperience < 80 ? 1.5f : currentExperience < 92 ? 0.8f : 0.3f;
                if (races <= 0)
                {
                    growth *= 0.3f;
                }

                int experienceChange = Mathf.Clamp(Mathf.RoundToInt(growth), 0, 5);

                DriverRatingModifier modifier = new DriverRatingModifier
                {
                    driverId = driverId,
                    season = Save.currentSeason,
                    legacyDeltaMigrated = true,
                    paceDelta = Mathf.Clamp(prevPaceDelta + paceChange, -40, 40),
                    racecraftDelta = Mathf.Clamp(prevRacecraftDelta + racecraftChange, -40, 40),
                    qualifyingDelta = Mathf.Clamp(prevQualifyingDelta + qualifyingChange, -40, 40),
                    tyreManagementDelta = Mathf.Clamp(prevTyreDelta + tyreChange, -40, 40),
                    wetSkillDelta = Mathf.Clamp(prevWetDelta + wetChange, -40, 40),
                    consistencyDelta = Mathf.Clamp(prevConsistencyDelta + consistencyChange, -40, 40),
                    aggressionDelta = Mathf.Clamp(prevAggressionDelta + aggressionChange, -20, 20),
                    defendingDelta = Mathf.Clamp(prevDefendingDelta + defendingChange, -40, 40),
                    overtakingDelta = Mathf.Clamp(prevOvertakingDelta + overtakingChange, -40, 40),
                    awarenessDelta = Mathf.Clamp(prevAwarenessDelta + awarenessChange, -40, 40),
                    experienceDelta = Mathf.Clamp(prevExperienceDelta + experienceChange, 0, 60),
                    lastPaceChange = paceChange,
                    lastRacecraftChange = racecraftChange,
                    lastQualifyingChange = qualifyingChange,
                    lastTyreManagementChange = tyreChange,
                    lastWetSkillChange = wetChange,
                    lastConsistencyChange = consistencyChange,
                    lastAggressionChange = aggressionChange,
                    lastDefendingChange = defendingChange,
                    lastOvertakingChange = overtakingChange,
                    lastAwarenessChange = awarenessChange,
                    lastExperienceChange = experienceChange
                };

                DriverData newEffective = ApplyRatingModifier(driver, driver.teamId, modifier);
                int newOverall = RatingCalculator.GetDriverOverall(newEffective);
                modifier.lastOverallChange = newOverall - previousOverall;
                modifier.trendLabel = modifier.lastOverallChange > 0 ? "Improving" : (modifier.lastOverallChange < 0 ? "Declining" : "Steady");

                Save.driverRatingModifiers.Add(modifier);
                thisSeason.Add(modifier);

                // Debug.Log directly (GameLog.Info is verbosity-gated and this
                // fires 22 lines once per season - the one time it matters).
                Debug.Log("[Progression] " + driver.displayName + " (" + races + " races, confidence=" + confidenceScale.ToString("F2") +
                             ", avgFinishVsExpected=" + (races > 0 && perf != null ? (perf.sumFinishVsExpected / races).ToString("F2") : "n/a") + "): " +
                             "Pace " + Sign(paceChange) + ", Racecraft " + Sign(racecraftChange) + ", Qualifying " + Sign(qualifyingChange) +
                             ", Tyre " + Sign(tyreChange) + ", Wet " + Sign(wetChange) + ", Consistency " + Sign(consistencyChange) +
                             ", Defending " + Sign(defendingChange) + ", Overtaking " + Sign(overtakingChange) + ", Awareness " + Sign(awarenessChange) +
                             ", Aggression " + Sign(aggressionChange) + ", Experience " + Sign(experienceChange) + ", Overall " + Sign(modifier.lastOverallChange));

                if (driverId == "player")
                {
                    Save.customPlayerDriverBase = driver;
                }

                DriverSeasonChangeRecord change = new DriverSeasonChangeRecord
                {
                    driverId = driverId,
                    driverName = driver.displayName,
                    previousOverall = previousOverall,
                    newOverall = newOverall,
                    paceChange = paceChange,
                    racecraftChange = racecraftChange,
                    qualifyingChange = qualifyingChange,
                    tyreManagementChange = tyreChange,
                    wetSkillChange = wetChange,
                    consistencyChange = consistencyChange,
                    aggressionChange = aggressionChange,
                    defendingChange = defendingChange,
                    overtakingChange = overtakingChange,
                    awarenessChange = awarenessChange,
                    experienceChange = experienceChange,
                    teammateHeadToHeadWins = perf != null ? perf.teammateRaceWins : 0,
                    teammateHeadToHeadLosses = perf != null ? perf.teammateRaceLosses : 0,
                    actualChampionshipPosition = completedSeason != null ? FindStandingPosition(completedSeason.finalDriverStandings, driverId == "player" && !Save.useExistingDriver ? "player" : driverId) : -1,
                    performanceSummary = races > 0
                        ? "Completed " + races + " race" + (races == 1 ? "" : "s") + " this season."
                        : "Did not complete a race this season - rating changes kept minimal."
                };
                changeRecords.Add(change);
            }

            if (completedSeason != null)
            {
                completedSeason.driverChanges = changeRecords;
            }

            if (thisSeason.Count == 0)
            {
                return;
            }

            // Field-balance audit: driver progression should be roughly zero-sum
            // across the grid (someone's gain is someone's loss). A strongly
            // negative sum here means an expectation-scale bug is back and the
            // whole field is drifting down together.
            int sumOverallChange = 0, risers = 0, fallers = 0;
            for (int i = 0; i < thisSeason.Count; i++)
            {
                sumOverallChange += thisSeason[i].lastOverallChange;
                if (thisSeason[i].lastOverallChange > 0) risers++;
                else if (thisSeason[i].lastOverallChange < 0) fallers++;
            }

            Debug.Log("[Progression] season summary: drivers=" + thisSeason.Count +
                " risers=" + risers + " fallers=" + fallers +
                " sumOverallChange=" + sumOverallChange +
                " (should hover near zero; strongly negative = expectation-scale bias)");

            thisSeason.Sort((a, b) => b.lastOverallChange.CompareTo(a.lastOverallChange));
            DriverRatingModifier riser = thisSeason[0];
            DriverData riserDriver = riser.driverId == "player" ? Save.customPlayerDriverBase : data.FindDriver(riser.driverId);
            if (riser.lastOverallChange >= 3 && riserDriver != null)
            {
                AddNewsArticle(
                    riserDriver.displayName + " is the form driver of the paddock",
                    riserDriver.displayName + " enters Season " + Save.currentSeason + " on a real upward curve, sharper than ever after a strong development cycle.",
                    NewsCategoryRumour);
            }

            DriverRatingModifier faller = thisSeason[thisSeason.Count - 1];
            DriverData fallerDriver = faller.driverId == "player" ? Save.customPlayerDriverBase : data.FindDriver(faller.driverId);
            if (faller.lastOverallChange <= -3 && fallerDriver != null)
            {
                AddNewsArticle(
                    fallerDriver.displayName + " under pressure after a difficult stretch",
                    fallerDriver.displayName + " heads into Season " + Save.currentSeason + " having lost a step, with question marks starting to follow the seat.",
                    NewsCategoryRumour);
            }
        }

        static string Sign(int value)
        {
            return value > 0 ? "+" + value : value.ToString();
        }

        // Part 4: a custom (non-existing-driver) player's persistent rating
        // profile - created once, then never mutated directly; all subsequent
        // movement happens through DriverRatingModifier entries keyed
        // "player", exactly like a real driver's drivers.json entry. The
        // baseline mirrors the reputation-scaled formula RaceManager used to
        // compute fresh every qualifying session before this persistent
        // profile existed (roughly qualifying 70-90 for a mid-pack seat), so
        // an existing custom-driver save doesn't suddenly regress to a
        // rookie-level grid filler the first time this runs.
        void EnsureCustomPlayerDriverBase()
        {
            // Zero-stat save repair (per report - "[QualiSim] driver(qualifying=0
            // pace=0 consistency=0 experience=0)" pinning the player to P22): a
            // save written before customPlayerDriverBase carried stat fields
            // deserializes them all as 0 - non-null and not the legacy rookie
            // template, so this guard kept the zero-rated driver forever and
            // every simulated qualifying charged the player ~+2.7s of driver
            // effect plus worst-case consistency variance. Any base missing its
            // core ratings is now rebuilt like a missing one.
            if (Save.customPlayerDriverBase != null && !IsUnfixedRookieTemplate(Save.customPlayerDriverBase) &&
                !HasDegenerateStats(Save.customPlayerDriverBase))
            {
                return;
            }

            float reputationBonus = Mathf.Clamp((Save.reputation - 50f) * 0.10f, -6f, 6f);
            int qualifying = Mathf.Clamp(Mathf.RoundToInt(76f + reputationBonus), 58, 90);
            int consistency = Mathf.Clamp(qualifying - 2, 55, 92);
            Save.customPlayerDriverBase = new DriverData
            {
                id = "player",
                displayName = Save.playerDriverName,
                abbreviation = "PLR",
                teamId = Save.playerTeamId,
                pace = Mathf.Clamp(qualifying - 1, 50, 99),
                racecraft = Mathf.Clamp(qualifying - 3, 50, 99),
                qualifying = qualifying,
                tyreManagement = Mathf.Clamp(qualifying - 4, 50, 96),
                wetSkill = Mathf.Clamp(qualifying - 2, 50, 96),
                consistency = consistency,
                aggression = 72,
                defending = Mathf.Clamp(qualifying - 6, 45, 94),
                overtaking = Mathf.Clamp(qualifying - 4, 45, 94),
                awareness = consistency,
                experience = Mathf.Clamp(70 + Save.currentSeason * 2, 60, 94),
                developmentPotential = 84
            };
        }

        // One-time save repair: an earlier build seeded a much weaker fixed
        // rookie template (pace 68/racecraft 66/qualifying 66/experience 35)
        // regardless of the player's actual reputation, which made an
        // existing custom-driver career suddenly qualify near the back after
        // updating even with no season transition. Detects that exact
        // fingerprint (never itself a value the reputation-scaled formula
        // above can produce, since it always keys every stat off the same
        // qualifying baseline) and lets EnsureCustomPlayerDriverBase
        // regenerate it - safe to run unconditionally since a driver actually
        // progressed by GenerateDriverProgression will not match it.
        static bool IsUnfixedRookieTemplate(DriverData driver)
        {
            return driver.pace == 68 && driver.racecraft == 66 && driver.qualifying == 66 &&
                   driver.tyreManagement == 65 && driver.experience == 35 && driver.developmentPotential == 85;
        }

        // A custom player base whose core ratings are zero/absent (an old save
        // serialized before the stat fields existed) - must be rebuilt, never
        // used as a real driver rating.
        static bool HasDegenerateStats(DriverData driver)
        {
            return driver.pace <= 0 || driver.qualifying <= 0 || driver.consistency <= 0 || driver.experience <= 0;
        }

        void GenerateTeamPerformanceEvolution(SeasonArchive completedSeason)
        {
            List<StandingEntry> previousConstructorStandings = completedSeason != null ? completedSeason.finalConstructorStandings : null;
            int teamCount = Mathf.Max(1, data.Teams.teams.Count);

            for (int i = 0; i < data.Teams.teams.Count; i++)
            {
                TeamData team = data.Teams.teams[i];
                int previousPosition = previousConstructorStandings != null ? previousConstructorStandings.FindIndex(entry => entry.id == team.id) + 1 : 0;
                float positionBias = previousPosition <= 0 ? 0f : (Mathf.InverseLerp(1, teamCount, previousPosition) * 2f - 1f);

                float regulationBias = 0f;
                for (int r = 0; r < Save.regulationChanges.Count; r++)
                {
                    RegulationChange change = Save.regulationChanges[r];
                    if (change.season == Save.currentSeason && System.Array.IndexOf(DepartmentNames, change.category) >= 0)
                    {
                        regulationBias += change.magnitude * 0.4f;
                    }
                }

                float delta = Mathf.Clamp(positionBias * 0.035f + regulationBias + Random.Range(-0.03f, 0.03f), -0.09f, 0.09f);
                Save.teamPerformanceModifiers.Add(new TeamPerformanceModifier
                {
                    teamId = team.id,
                    season = Save.currentSeason,
                    performanceDelta = delta,
                    reputationDelta = Mathf.RoundToInt(delta * 40f),
                    trendLabel = delta > 0.02f ? "Gained ground over the winter" : (delta < -0.02f ? "Lost ground over the winter" : "Held steady over the winter")
                });
            }

            List<TeamPerformanceModifier> thisSeason = Save.teamPerformanceModifiers.FindAll(m => m.season == Save.currentSeason);
            thisSeason.Sort((a, b) => b.performanceDelta.CompareTo(a.performanceDelta));
            if (thisSeason.Count == 0)
            {
                return;
            }

            TeamPerformanceModifier gainer = thisSeason[0];
            TeamData gainerTeam = data.FindTeam(gainer.teamId);
            if (gainer.performanceDelta > 0.02f && gainerTeam != null)
            {
                AddNewsArticle(
                    gainerTeam.name + " make the biggest gains over the winter",
                    gainerTeam.name + " head into Season " + Save.currentSeason + " with the most improved car on the grid after a focused winter development push.",
                    NewsCategoryRegulations);
            }

            TeamPerformanceModifier loser = thisSeason[thisSeason.Count - 1];
            TeamData loserTeam = data.FindTeam(loser.teamId);
            if (loser.performanceDelta < -0.02f && loserTeam != null)
            {
                AddNewsArticle(
                    loserTeam.name + " slip back over the winter",
                    loserTeam.name + " arrive at Season " + Save.currentSeason + " having lost a step over the winter - a difficult development cycle leaves ground to make up.",
                    NewsCategoryRegulations);
            }
        }

        // Read-time-only team performance lookup (see TeamPerformanceModifier)
        // layered underneath the player's own upgrade tuning - every AI team
        // now gets a small season-to-season swing, not just the player.
        // Part 7: the one authoritative effective-car pipeline - base car +
        // this season's small performance swing + this team's persistent R&D
        // upgrades. Every reader (race AI, simulated qualifying/races, team
        // ratings, R&D screen, preseason testing, contracts) must come through
        // here rather than composing TeamPerformanceModifier/upgrades itself,
        // so no call site can ever apply a team's upgrades twice or skip them.
        public CarPerformanceData GetEffectiveTeamCar(TeamData team, CarPerformanceData baseCar)
        {
            if (team == null || baseCar == null)
            {
                return baseCar;
            }

            CarPerformanceData tuned = ApplyTeamPerformanceModifier(team.id, baseCar);
            if (team.id == Save.playerTeamId)
            {
                // The player's completed upgrades live on Save.completedUpgradeIds/
                // Save.activeUpgradeProjects directly (so the existing R&D screen
                // keeps working unchanged) - bridged through here rather than a
                // second, parallel TeamDevelopmentState copy that could drift out
                // of sync and double-apply.
                tuned = ApplyCareerUpgrades(tuned);
            }
            else
            {
                TeamDevelopmentState state = GetOrCreateTeamDevelopmentState(team.id);
                tuned = ApplyUpgradeSet(tuned, state.completedUpgradeIds, id => state.activeUpgradeProjects.Find(p => p.upgradeId == id));
            }

            return tuned;
        }

        CarPerformanceData ApplyTeamPerformanceModifier(string teamId, CarPerformanceData baseCar)
        {
            TeamPerformanceModifier modifier = Save.teamPerformanceModifiers.Find(m => m.teamId == teamId && m.season == Save.currentSeason);
            if (modifier == null || Mathf.Abs(modifier.performanceDelta) < 0.001f)
            {
                return baseCar;
            }

            float scale = 1f + modifier.performanceDelta;
            CarPerformanceData tuned = new CarPerformanceData
            {
                id = baseCar.id,
                // Top speed scaled at a fraction of the full swing - keeps a
                // team's straight-line pace from swinging as wildly as its
                // cornering/reliability, matching "avoid wild unrealistic
                // chaos" for the one stat drivers notice most directly.
                // Raised 0.3 -> 0.6 (per report - "my car got slower in the
                // new season but my top speed didn't change"): at 30% the
                // season swing moved top speed by barely a rounding error, so
                // a car that regressed everywhere else kept its old
                // straight-line pace. 60% is a real, felt change (~a few kph
                // per season swing) while still gentler than the other stats.
                topSpeed = Mathf.RoundToInt(baseCar.topSpeed * Mathf.Lerp(1f, scale, 0.6f)),
                acceleration = Mathf.RoundToInt(baseCar.acceleration * scale),
                cornering = Mathf.RoundToInt(baseCar.cornering * scale),
                braking = Mathf.RoundToInt(baseCar.braking * scale),
                reliability = Mathf.RoundToInt(baseCar.reliability * scale),
                ersEfficiency = Mathf.RoundToInt(baseCar.ersEfficiency * scale),
                tyreManagement = Mathf.RoundToInt(baseCar.tyreManagement * scale),
                aeroEfficiency = Mathf.RoundToInt(baseCar.aeroEfficiency * scale),
                chassisBalance = Mathf.RoundToInt(baseCar.chassisBalance * scale),
                enginePower = Mathf.RoundToInt(baseCar.enginePower * scale)
            };

            tuned.topSpeed = Mathf.Clamp(tuned.topSpeed, 310, 362);
            tuned.acceleration = Mathf.Clamp(tuned.acceleration, 40, 128);
            tuned.cornering = Mathf.Clamp(tuned.cornering, 40, 128);
            tuned.braking = Mathf.Clamp(tuned.braking, 40, 128);
            tuned.reliability = Mathf.Clamp(tuned.reliability, 30, 128);
            tuned.ersEfficiency = Mathf.Clamp(tuned.ersEfficiency, 40, 128);
            tuned.tyreManagement = Mathf.Clamp(tuned.tyreManagement, 40, 128);
            tuned.aeroEfficiency = Mathf.Clamp(tuned.aeroEfficiency, 40, 128);
            tuned.chassisBalance = Mathf.Clamp(tuned.chassisBalance, 40, 128);
            tuned.enginePower = Mathf.Clamp(tuned.enginePower, 40, 128);
            return tuned;
        }

        // =====================================================================
        // Part 5/7: full-grid, persistent, in-season R&D.
        // =====================================================================

        public TeamDevelopmentState GetOrCreateTeamDevelopmentState(string teamId)
        {
            if (Save.teamDevelopmentStates == null)
            {
                Save.teamDevelopmentStates = new List<TeamDevelopmentState>();
            }

            TeamDevelopmentState state = Save.teamDevelopmentStates.Find(s => s.teamId == teamId);
            if (state == null)
            {
                TeamData team = data.FindTeam(teamId);
                CarPerformanceData baseCar = team != null ? data.FindCar(team.carPerformanceId) : null;
                state = new TeamDevelopmentState
                {
                    teamId = teamId,
                    resourcePoints = 260,
                    archetype = AssignTeamDevelopmentArchetype(team, baseCar),
                    ratingAtSeasonStart = RatingCalculator.GetCarOverall(baseCar),
                    currentRating = RatingCalculator.GetCarOverall(baseCar)
                };
                while (state.departmentLevels.Count < DepartmentNames.Length)
                {
                    state.departmentLevels.Add(1);
                }

                Save.teamDevelopmentStates.Add(state);
            }

            return state;
        }

        void EnsureAllTeamDevelopmentStates()
        {
            for (int i = 0; i < data.Teams.teams.Count; i++)
            {
                TeamData team = data.Teams.teams[i];
                if (team != null)
                {
                    GetOrCreateTeamDevelopmentState(team.id);
                }
            }
        }

        // Different teams get genuinely different development behaviour: a
        // weak-aero car biases Aerodynamics/Chassis, an unreliable-but-fast car
        // biases Durability, a power-deficient car biases Power Unit/ERS, a
        // tyre-limited car biases Tyre Management - a well-rounded car falls
        // back to a Conservative (title contender) or Aggressive (backmarker)
        // style based on reputation, per the design brief's examples.
        string AssignTeamDevelopmentArchetype(TeamData team, CarPerformanceData baseCar)
        {
            if (baseCar == null)
            {
                return "Balanced";
            }

            if (baseCar.reliability <= 62)
            {
                return "Durability-Focused";
            }

            if (baseCar.aeroEfficiency <= 65 || baseCar.cornering <= 65)
            {
                return "Aero-Focused";
            }

            if (baseCar.enginePower <= 65 || baseCar.ersEfficiency <= 65)
            {
                return "Power-Focused";
            }

            if (baseCar.tyreManagement <= 65)
            {
                return "Tyre-Focused";
            }

            return team != null && team.reputation >= 85 ? "Conservative" : "Aggressive";
        }

        string PreferredDepartmentForArchetype(string archetype)
        {
            switch (archetype)
            {
                case "Aero-Focused": return "Aerodynamics";
                case "Durability-Focused": return "Durability";
                case "Power-Focused": return "Power Unit";
                case "Tyre-Focused": return "Tyre Management";
                default: return null;
            }
        }

        int ArchetypeRiskMode(string archetype)
        {
            if (archetype == "Conservative")
            {
                return RiskConservative;
            }

            if (archetype == "Aggressive")
            {
                return RiskRush;
            }

            return RiskStandard;
        }

        string WeakestCarDepartment(CarPerformanceData car)
        {
            if (car == null)
            {
                return DepartmentNames[Random.Range(0, DepartmentNames.Length)];
            }

            KeyValuePair<string, int>[] departmentStats =
            {
                new KeyValuePair<string, int>("Aerodynamics", car.aeroEfficiency),
                new KeyValuePair<string, int>("Chassis", car.chassisBalance),
                new KeyValuePair<string, int>("Power Unit", car.enginePower),
                new KeyValuePair<string, int>("Durability", car.reliability),
                new KeyValuePair<string, int>("Tyre Management", car.tyreManagement),
                new KeyValuePair<string, int>("ERS", car.ersEfficiency)
            };

            string weakest = departmentStats[0].Key;
            int weakestValue = departmentStats[0].Value;
            for (int i = 1; i < departmentStats.Length; i++)
            {
                if (departmentStats[i].Value < weakestValue)
                {
                    weakestValue = departmentStats[i].Value;
                    weakest = departmentStats[i].Key;
                }
            }

            return weakest;
        }

        int GetAiDepartmentLevel(TeamDevelopmentState state, string category)
        {
            int index = GetDepartmentIndex(category);
            if (index < 0 || state.departmentLevels == null || index >= state.departmentLevels.Count)
            {
                return 1;
            }

            return state.departmentLevels[index];
        }

        // Called once per race (see ApplyRaceResults) for every non-player
        // team: earns resources, progresses/completes/fails active projects
        // (applying completed stat deltas immediately, exactly like the
        // player's AdvanceUpgradeProjects), then may start a new project.
        void AdvanceAiTeamDevelopment()
        {
            // Results-scaled AI income (this runs before ApplyRaceResults' own
            // sort pass, so sort first - idempotent): the player's weekend income
            // scales with finishing position, but AI teams earned a flat stipend
            // regardless of results, which is a big part of why the field falls
            // behind the player's development over a career. Front-running teams
            // now bank up to ~180 RP a weekend and backmarkers ~80, bracketing
            // the old flat 95.
            SortStandings(Save.constructorStandings);

            // Catch-up development (per request - "speed up the other teams'
            // R&D by a LOT; this lead is incredulous"): every AI team's whole
            // development pipeline now scales with its rating gap to the BEST
            // car on the grid (usually the player's). A team 10+ points down
            // earns ~2.6x the resource points, burns through project weeks
            // ~3x faster, and spins up extra concurrent projects - so a
            // runaway leader drags the field's development up behind it
            // instead of lapping their R&D too.
            int leaderRating = 0;
            TeamData playerTeamForRating = data.FindTeam(Save.playerTeamId);
            if (playerTeamForRating != null)
            {
                leaderRating = RatingCalculator.GetCarOverall(GetEffectiveTeamCar(playerTeamForRating, data.FindCar(playerTeamForRating.carPerformanceId)));
            }

            for (int i = 0; i < data.Teams.teams.Count; i++)
            {
                TeamData ratingTeam = data.Teams.teams[i];
                if (ratingTeam == null || ratingTeam.id == Save.playerTeamId)
                {
                    continue;
                }

                TeamDevelopmentState ratingState = GetOrCreateTeamDevelopmentState(ratingTeam.id);
                leaderRating = Mathf.Max(leaderRating, ratingState.currentRating);
            }

            for (int i = 0; i < data.Teams.teams.Count; i++)
            {
                TeamData team = data.Teams.teams[i];
                if (team == null || team.id == Save.playerTeamId)
                {
                    continue;
                }

                TeamDevelopmentState state = GetOrCreateTeamDevelopmentState(team.id);
                int constructorPosition = FindStandingPosition(Save.constructorStandings, team.id);
                if (constructorPosition <= 0)
                {
                    constructorPosition = 6;
                }

                float ratingGap = Mathf.Max(0f, leaderRating - state.currentRating);
                float catchUp = 1f + Mathf.Min(ratingGap, 12f) * 0.16f;

                float weekendIncome = (70f + Mathf.Max(0, 12 - constructorPosition) * 10f) * catchUp;
                state.resourcePoints += Mathf.RoundToInt(weekendIncome * Save.currentSeasonResourceMultiplier);

                int weeksOfProgress = Mathf.Max(1, Mathf.RoundToInt(catchUp));
                for (int p = state.activeUpgradeProjects.Count - 1; p >= 0; p--)
                {
                    ActiveUpgradeProject project = state.activeUpgradeProjects[p];
                    if (project.status != ProjectInDevelopment)
                    {
                        continue;
                    }

                    project.remainingRaceWeeks -= weeksOfProgress;
                    if (project.remainingRaceWeeks > 0)
                    {
                        continue;
                    }

                    project.remainingRaceWeeks = 0;
                    UpgradeData upgrade = data.Upgrades.upgrades.Find(u => u.id == project.upgradeId);
                    string projectName = upgrade != null ? upgrade.displayName : project.upgradeId;
                    int ratingBefore = state.currentRating;

                    if (Random.value <= project.successChance)
                    {
                        project.status = ProjectCompleted;
                        if (!state.completedUpgradeIds.Contains(project.upgradeId))
                        {
                            state.completedUpgradeIds.Add(project.upgradeId);
                        }

                        // Every 3 completed upgrades in a category nudges that
                        // department's own facility level up (capped), so top
                        // teams' departments naturally get stronger over a
                        // career while still respecting diminishing returns via
                        // ComputeProjectWeeksForLevel/ComputeProjectSuccessChanceForLevel.
                        int deptIndex = GetDepartmentIndex(project.category);
                        if (deptIndex >= 0 && deptIndex < state.departmentLevels.Count)
                        {
                            int completedInCategory = 0;
                            for (int c = 0; c < state.completedUpgradeIds.Count; c++)
                            {
                                UpgradeData completedUpgrade = data.Upgrades.upgrades.Find(u => u.id == state.completedUpgradeIds[c]);
                                if (completedUpgrade != null && completedUpgrade.category == project.category)
                                {
                                    completedInCategory++;
                                }
                            }

                            if (completedInCategory % 3 == 0)
                            {
                                state.departmentLevels[deptIndex] = Mathf.Min(MaxDepartmentLevel, state.departmentLevels[deptIndex] + 1);
                            }
                        }

                        RecalculateTeamRating(team, state);
                        GameLog.Info("[R&D] " + team.name + " (" + state.archetype + ") completed " + projectName +
                                     " round " + Save.currentRound + " - car rating " + ratingBefore + " -> " + state.currentRating);

                        if (state.currentRating - ratingBefore >= 2 || Random.value < 0.3f)
                        {
                            AddNewsArticle(
                                team.name + " complete " + projectName,
                                team.name + "'s " + (upgrade != null ? upgrade.category.ToLowerInvariant() : "engineering") +
                                " department has finished work on " + projectName + ", fitted to the car from this round.",
                                NewsCategoryRnd);
                        }
                    }
                    else
                    {
                        project.status = ProjectFailed;
                        if (!state.failedUpgradeIds.Contains(project.upgradeId))
                        {
                            state.failedUpgradeIds.Add(project.upgradeId);
                        }

                        GameLog.Info("[R&D] " + team.name + " failed " + projectName + " (round " + Save.currentRound + ")");
                        if (Random.value < 0.2f)
                        {
                            AddNewsArticle(
                                team.name + "'s " + projectName + " project stalls",
                                team.name + " has hit a setback with " + projectName + " and the project has been shelved.",
                                NewsCategoryRnd);
                        }
                    }
                }

                state.activeUpgradeProjects.RemoveAll(p => p.status == ProjectCompleted || p.status == ProjectFailed);
                TryStartAiUpgradeProject(team, state);
                // Far-behind teams spin up additional projects the same weekend
                // (each still pays full cost from their own - catch-up-boosted -
                // resource pool).
                if (ratingGap >= 5f)
                {
                    TryStartAiUpgradeProject(team, state);
                }

                if (ratingGap >= 9f)
                {
                    TryStartAiUpgradeProject(team, state);
                }
            }
        }

        // AI project selection: weighs the team's weakest department (or its
        // archetype's preferred department), current regulation targets,
        // prerequisites, department-level gating and affordability - then
        // starts a project using the exact same cost/duration/success-chance
        // formulas the player's own R&D uses. A random per-race start chance
        // (rather than "always start something the moment a slot is free")
        // gives every team controlled variation in project timing instead of
        // every AI team developing in lockstep.
        void TryStartAiUpgradeProject(TeamData team, TeamDevelopmentState state)
        {
            int activeCount = 0;
            for (int i = 0; i < state.activeUpgradeProjects.Count; i++)
            {
                if (state.activeUpgradeProjects[i].status == ProjectInDevelopment)
                {
                    activeCount++;
                }
            }

            // R&D pace buff continued: one more concurrent project slot (2 -> 3)
            // and a much higher per-race-week chance to actually start one
            // (0.4 -> 0.7) - AI teams were frequently sitting on banked resource
            // points with nothing in development because this roll failed,
            // which is exactly the kind of stall that let the player's own,
            // uninterrupted R&D pull away to 104 while the grid sat in the 90s.
            // Catch-up round (per request): another slot (3 -> 4) and a
            // near-certain start roll (0.7 -> 0.9), paired with the gap-scaled
            // income/progress in AdvanceAiTeamDevelopment.
            if (activeCount >= 4 || Random.value > 0.9f)
            {
                return;
            }

            CarPerformanceData baseCar = data.FindCar(team.carPerformanceId);
            CarPerformanceData currentCar = GetEffectiveTeamCar(team, baseCar);
            string preferredDepartment = PreferredDepartmentForArchetype(state.archetype);
            string targetDepartment = !string.IsNullOrEmpty(preferredDepartment) && Random.value < 0.7f
                ? preferredDepartment
                : WeakestCarDepartment(currentCar);

            // Regulation-affected departments are a live risk this season - AI
            // teams occasionally reprioritise onto them rather than only ever
            // reacting to car weaknesses.
            if (Save.regulationAffectedCategories != null && Save.regulationAffectedCategories.Count > 0 && Random.value < 0.25f)
            {
                targetDepartment = Save.regulationAffectedCategories[Random.Range(0, Save.regulationAffectedCategories.Count)];
            }

            int level = GetAiDepartmentLevel(state, targetDepartment);
            List<UpgradeData> candidates = data.Upgrades.upgrades.FindAll(u =>
                u != null && u.category == targetDepartment &&
                !state.completedUpgradeIds.Contains(u.id) &&
                !state.failedUpgradeIds.Contains(u.id) &&
                state.activeUpgradeProjects.Find(p => p.upgradeId == u.id) == null &&
                (string.IsNullOrEmpty(u.requiredUpgradeId) || state.completedUpgradeIds.Contains(u.requiredUpgradeId)) &&
                u.tier <= level);

            if (candidates.Count == 0)
            {
                return;
            }

            UpgradeData chosen = candidates[Random.Range(0, candidates.Count)];
            int riskMode = ArchetypeRiskMode(state.archetype);
            int weeks = ComputeProjectWeeksForLevel(chosen, riskMode, level);
            float chance = ComputeProjectSuccessChanceForLevel(chosen, riskMode, level);
            int cost = ComputeProjectCost(chosen, riskMode);
            if (state.resourcePoints < cost)
            {
                return;
            }

            state.resourcePoints -= cost;
            state.activeUpgradeProjects.Add(new ActiveUpgradeProject
            {
                teamId = team.id,
                upgradeId = chosen.id,
                category = chosen.category,
                startRound = Save.currentRound,
                remainingRaceWeeks = weeks,
                totalRaceWeeks = weeks,
                cost = cost,
                successChance = chance,
                riskMode = riskMode,
                status = ProjectInDevelopment
            });
            state.lastProjectStartRound = Save.currentRound;
            GameLog.Info("[R&D] " + team.name + " (" + state.archetype + ") starts " + chosen.displayName + " [" + targetDepartment +
                         "] round " + Save.currentRound + ", weeks=" + weeks + ", chance=" + chance.ToString("F2"));
        }

        void RecalculateTeamRating(TeamData team, TeamDevelopmentState state)
        {
            CarPerformanceData baseCar = data.FindCar(team.carPerformanceId);
            CarPerformanceData effective = GetEffectiveTeamCar(team, baseCar);
            state.currentRating = RatingCalculator.GetCarOverall(effective);
        }

        // The player's team keeps its own Save.completedUpgradeIds as the
        // source of truth (see GetEffectiveTeamCar) - this mirrors that list
        // into the player's TeamDevelopmentState purely so the season archive
        // and Team Ratings screen can report the player's upgrade count/rating
        // through the exact same TeamDevelopmentState shape every AI team uses.
        void SyncPlayerTeamDevelopmentState()
        {
            TeamData playerTeam = data.FindTeam(Save.playerTeamId);
            if (playerTeam == null)
            {
                return;
            }

            TeamDevelopmentState state = GetOrCreateTeamDevelopmentState(Save.playerTeamId);
            state.completedUpgradeIds = new List<string>(Save.completedUpgradeIds);
            state.failedUpgradeIds = new List<string>(Save.failedUpgradeIds);
            state.resourcePoints = Save.resourcePoints;
            RecalculateTeamRating(playerTeam, state);
        }

        // Driver market + progression: read-time-only lookup (see
        // DriverTransferRecord/DriverRatingModifier) exactly like
        // GetEffectiveTeamCar above - never mutates the shared DriverData
        // reference from drivers.json. Corrects teamId to this season's
        // effective team (a driver's own .teamId field on the shared object is
        // never touched, so every downstream reader - car color, standings,
        // race grid - must go through this, not read driver.teamId directly)
        // and applies the season's rating swing to the core skill stats.
        public DriverData GetEffectiveDriver(DriverData baseDriver)
        {
            if (baseDriver == null)
            {
                return baseDriver;
            }

            string effectiveTeamId = data.EffectiveTeamId(baseDriver, Save.driverTransferRecords);
            DriverRatingModifier modifier = Save.driverRatingModifiers == null ? null : Save.driverRatingModifiers.Find(m => m.driverId == baseDriver.id && m.season == Save.currentSeason);
            MigrateLegacyRatingModifier(modifier);

            if (modifier == null && effectiveTeamId == baseDriver.teamId)
            {
                return baseDriver;
            }

            return ApplyRatingModifier(baseDriver, effectiveTeamId, modifier);
        }

        // Builds the effective DriverData for any base driver + team id +
        // modifier combination - shared by GetEffectiveDriver (real
        // drivers.json drivers) and the custom-player-driver path so both go
        // through identical per-stat delta application/clamping.
        static DriverData ApplyRatingModifier(DriverData baseDriver, string effectiveTeamId, DriverRatingModifier modifier)
        {
            if (modifier == null)
            {
                DriverData copy = CloneDriver(baseDriver);
                copy.teamId = effectiveTeamId;
                return copy;
            }

            DriverData result = CloneDriver(baseDriver);
            result.teamId = effectiveTeamId;
            result.pace = ClampRating(baseDriver.pace + modifier.paceDelta);
            result.racecraft = ClampRating(baseDriver.racecraft + modifier.racecraftDelta);
            result.qualifying = ClampRating(baseDriver.qualifying + modifier.qualifyingDelta);
            result.tyreManagement = ClampRating(baseDriver.tyreManagement + modifier.tyreManagementDelta);
            result.wetSkill = ClampRating(baseDriver.wetSkill + modifier.wetSkillDelta);
            result.consistency = ClampRating(baseDriver.consistency + modifier.consistencyDelta);
            result.aggression = Mathf.Clamp(baseDriver.aggression + modifier.aggressionDelta, 30, 99);
            result.defending = ClampRating(baseDriver.defending + modifier.defendingDelta);
            result.overtaking = ClampRating(baseDriver.overtaking + modifier.overtakingDelta);
            result.awareness = ClampRating(baseDriver.awareness + modifier.awarenessDelta);
            result.experience = Mathf.Clamp(baseDriver.experience + modifier.experienceDelta, 1, 99);
            result.developmentPotential = baseDriver.developmentPotential;
            return result;
        }

        static DriverData CloneDriver(DriverData source)
        {
            return new DriverData
            {
                id = source.id,
                displayName = source.displayName,
                abbreviation = source.abbreviation,
                number = source.number,
                teamId = source.teamId,
                pace = source.pace,
                racecraft = source.racecraft,
                qualifying = source.qualifying,
                tyreManagement = source.tyreManagement,
                wetSkill = source.wetSkill,
                consistency = source.consistency,
                aggression = source.aggression,
                defending = source.defending,
                overtaking = source.overtaking,
                awareness = source.awareness,
                experience = source.experience,
                developmentPotential = source.developmentPotential
            };
        }

        // Part 1/9: an old save's DriverRatingModifier only ever has
        // ratingDelta populated - fold it once into the relevant per-stat
        // fields (matching the old "apply the same delta everywhere" shape,
        // so the driver's effective stats don't visibly jump the moment this
        // update loads) and never touch it again afterwards.
        // Part 4/9 bugfix: on an existing-driver career, the OLD progression
        // system generated a ratingDelta for EVERY driver in drivers.json -
        // including whichever real driver the player is actually playing as -
        // as a pure random walk (rising talent / declining veteran / random
        // step), completely decoupled from the player's own results, because
        // the old GetEffectiveDriver was never applied to the player's own
        // seat (RaceManager read that driver's raw stats directly - one of
        // the exact bypasses this refactor was asked to close). That legacy
        // value was therefore never visible in the player's own gameplay -
        // it could have drifted arbitrarily (including sharply negative)
        // over many seasons with zero connection to how the player actually
        // performed. Migrating it now would dump that invisible, unearned
        // history onto the player's own driver in one shot the instant this
        // save loads - exactly the "suddenly qualifying P22 despite regularly
        // reaching Q3, with no season transition" regression this fixes.
        // Discard it for the player's own seat; genuine progression starts
        // fresh from here, driven by the player's actual results (see
        // MapDriverIdForProgression). Every other (true AI) driver's legacy
        // delta still migrates normally, preserving their visible history.
        void MigrateLegacyRatingModifier(DriverRatingModifier modifier)
        {
            if (modifier == null)
            {
                return;
            }

            // The player's seat comes in TWO shapes: an existing real driver
            // (modifier keyed by selectedDriverId) or a custom driver (modifier
            // keyed by the literal "player"). The interim buggy build folded the
            // legacy delta into BOTH shapes, but the first version of this guard
            // (and the repair below) only covered the existing-driver case - a
            // custom-driver career kept the baked-in damage and still qualified
            // P22 on every new weekend. Cover both.
            bool isPlayersOwnSeat = (Save.useExistingDriver && !string.IsNullOrEmpty(Save.selectedDriverId) && modifier.driverId == Save.selectedDriverId) ||
                                    (!Save.useExistingDriver && modifier.driverId == "player");

            // One-time repair for saves already migrated by the interim buggy
            // build: that build folded the legacy ratingDelta into all nine
            // per-stat fields for the player's OWN seat as well, then set
            // legacyDeltaMigrated=true - so the guard below skips it and the
            // damage (often a sharply negative delta, dropping the player to P22)
            // stays baked in forever. ratingDelta itself is still populated (the
            // fold never zeroed it), so we can subtract exactly what was added back
            // out of the same nine fields, then zero it. Runs once, gated by
            // legacyPlayerDeltaRepaired, and only for the player's own seat.
            if (isPlayersOwnSeat && modifier.legacyDeltaMigrated && !modifier.legacyPlayerDeltaRepaired && modifier.ratingDelta != 0)
            {
                int baked = modifier.ratingDelta;
                modifier.paceDelta -= baked;
                modifier.racecraftDelta -= baked;
                modifier.qualifyingDelta -= baked;
                modifier.tyreManagementDelta -= baked;
                modifier.wetSkillDelta -= baked;
                modifier.consistencyDelta -= baked;
                modifier.defendingDelta -= baked;
                modifier.overtakingDelta -= baked;
                modifier.awarenessDelta -= baked;
                modifier.ratingDelta = 0;
                modifier.legacyPlayerDeltaRepaired = true;
                GameLog.Info("[Progression] Repaired player's own driver (" + modifier.driverId + ", season " + modifier.season + "): backed out the interim build's folded legacy delta (" + baked + ") that had tanked qualifying to the back of the grid.");
                return;
            }

            if (isPlayersOwnSeat && !modifier.legacyDeltaMigrated)
            {
                modifier.ratingDelta = 0;
                modifier.legacyDeltaMigrated = true;
                modifier.legacyPlayerDeltaRepaired = true;
                GameLog.Info("[Progression] Discarded legacy rating history for the player's own driver (" + modifier.driverId + ", season " + modifier.season + ") - it never reflected actual player performance.");
                return;
            }

            if (modifier.legacyDeltaMigrated || modifier.ratingDelta == 0)
            {
                modifier.legacyDeltaMigrated = true;
                return;
            }

            int delta = modifier.ratingDelta;
            modifier.paceDelta += delta;
            modifier.racecraftDelta += delta;
            modifier.qualifyingDelta += delta;
            modifier.tyreManagementDelta += delta;
            modifier.wetSkillDelta += delta;
            modifier.consistencyDelta += delta;
            modifier.defendingDelta += delta;
            modifier.overtakingDelta += delta;
            modifier.awarenessDelta += delta;
            modifier.legacyDeltaMigrated = true;
        }

        static int ClampRating(int value)
        {
            return F1Game.Core.DriverProgressionRules.ClampRating(value);
        }

        // Part 4: the player's own effective driver, regardless of whether they
        // picked an existing real driver (delegates straight to
        // GetEffectiveDriver on the selected driver) or made a custom one
        // (layers this season's modifier onto the persistent
        // customPlayerDriverBase) - callers that need "the player's current
        // rated skill" should use this instead of re-deriving it themselves.
        public DriverData GetEffectivePlayerDriver()
        {
            if (Save.useExistingDriver)
            {
                return GetEffectiveDriver(data.FindDriver(Save.selectedDriverId));
            }

            EnsureCustomPlayerDriverBase();
            DriverRatingModifier modifier = Save.driverRatingModifiers == null ? null : Save.driverRatingModifiers.Find(m => m.driverId == "player" && m.season == Save.currentSeason);
            MigrateLegacyRatingModifier(modifier);
            return ApplyRatingModifier(Save.customPlayerDriverBase, Save.playerTeamId, modifier);
        }

        // Fresh player/team objectives for the new season, scaled off the
        // team's own contract target (itself reputation-scaled) so a
        // backmarker seat and a title-contending seat get genuinely
        // different, appropriately-scaled goals. Milestone objectives
        // (first win/podium/pole) only appear if not already achieved across
        // the whole career (checked against the never-trimmed raceResults/
        // qualifyingResults history, not the just-reset current standings).
        void GenerateSeasonObjectives()
        {
            Save.seasonObjectives = new List<SeasonObjective>();
            TeamData team = data.FindTeam(Save.playerTeamId);
            int target = Mathf.Max(1, Save.contractTargetPosition);

            Save.seasonObjectives.Add(new SeasonObjective
            {
                id = "wdc_position",
                description = "Finish Season " + Save.currentSeason + " P" + target + " or better in the drivers' championship",
                category = "Championship",
                targetValue = target
            });

            int wccTarget = Mathf.Clamp(Mathf.RoundToInt(target * 0.85f), 1, Mathf.Max(1, data.Teams.teams.Count));
            Save.seasonObjectives.Add(new SeasonObjective
            {
                id = "wcc_position",
                description = "Help " + (team != null ? team.name : "the team") + " finish P" + wccTarget + " or better in the constructors' championship",
                category = "Championship",
                targetValue = wccTarget,
                teamObjective = true
            });

            Save.seasonObjectives.Add(new SeasonObjective
            {
                id = "beat_teammate",
                description = "Win the head-to-head against your teammate this season",
                category = "Teammate",
                targetValue = 1
            });

            int podiumTarget = target <= 5 ? 6 : (target <= 8 ? 3 : 1);
            Save.seasonObjectives.Add(new SeasonObjective
            {
                id = "podiums",
                description = "Score " + podiumTarget + " podium finish" + (podiumTarget == 1 ? "" : "es") + " this season",
                category = "Podiums",
                targetValue = podiumTarget
            });

            int pointsTarget = Mathf.Max(20, (25 - target * 2) * 8);
            Save.seasonObjectives.Add(new SeasonObjective
            {
                id = "points",
                description = "Score at least " + pointsTarget + " championship points this season",
                category = "Points",
                targetValue = pointsTarget
            });

            Save.seasonObjectives.Add(new SeasonObjective
            {
                id = "clean_races",
                description = "Complete 5 clean races (no penalties) this season",
                category = "Clean",
                targetValue = 5
            });

            if (!PlayerHasCareerResult(1))
            {
                Save.seasonObjectives.Add(new SeasonObjective { id = "first_win", description = "Achieve your first race win", category = "Milestone", targetValue = 1 });
            }
            else if (!PlayerHasCareerResult(3))
            {
                Save.seasonObjectives.Add(new SeasonObjective { id = "first_podium", description = "Achieve your first podium finish", category = "Milestone", targetValue = 1 });
            }

            if (!PlayerHasCareerPole())
            {
                Save.seasonObjectives.Add(new SeasonObjective { id = "first_pole", description = "Achieve your first pole position", category = "Milestone", targetValue = 1 });
            }
        }

        bool PlayerHasCareerResult(int positionOrBetter)
        {
            for (int i = 0; i < Save.raceResults.Count; i++)
            {
                RaceResultEntry entry = Save.raceResults[i].results.Find(item => item.isPlayer);
                if (entry != null && entry.finishingPosition > 0 && entry.finishingPosition <= positionOrBetter)
                {
                    return true;
                }
            }

            return false;
        }

        bool PlayerHasCareerPole()
        {
            for (int i = 0; i < Save.qualifyingResults.Count; i++)
            {
                QualifyingResultEntry entry = Save.qualifyingResults[i].results.Find(item => item.isPlayer);
                if (entry != null && entry.position == 1)
                {
                    return true;
                }
            }

            return false;
        }

        // Called every race (not just at season end) so the Career Hub and
        // post-race report can always show live objective progress.
        void UpdateSeasonObjectivesProgress(List<RaceResultEntry> results, RaceResultEntry player, CalendarEventData raceEvent)
        {
            if (Save.seasonObjectives == null || Save.seasonObjectives.Count == 0 || player == null)
            {
                return;
            }

            bool playerDnf = !string.IsNullOrEmpty(player.penaltyReason) && player.penaltyReason.Contains("DNF");
            bool cleanRace = player.penaltiesSeconds <= 0f && !playerDnf;

            // Championship-position reads below are index-based, but this runs
            // before ApplyRaceResults' own SortStandings pass - on an unsorted
            // list the player is always entry 0 (creation order), so position
            // objectives latched "achieved" after round 1. Sorting is idempotent,
            // so doing it here as well is safe.
            SortStandings(Save.driverStandings);
            SortStandings(Save.constructorStandings);

            for (int i = 0; i < Save.seasonObjectives.Count; i++)
            {
                SeasonObjective objective = Save.seasonObjectives[i];
                // Standings-position and head-to-head objectives can regress
                // between rounds, so they are re-evaluated every race and only
                // become final when the season archive freezes them; count-up
                // objectives (podiums, points, firsts) stay latched once met.
                bool trackedLive = objective.id == "wdc_position"
                    || objective.id == "wcc_position"
                    || objective.id == "beat_teammate";
                if (objective.achieved && !trackedLive)
                {
                    continue;
                }

                bool positionObjective = false;
                switch (objective.id)
                {
                    case "wdc_position":
                        objective.currentValue = FindStandingPosition(Save.driverStandings, "player");
                        positionObjective = true;
                        break;
                    case "wcc_position":
                        objective.currentValue = FindStandingPosition(Save.constructorStandings, Save.playerTeamId);
                        positionObjective = true;
                        break;
                    case "beat_teammate":
                        // "Win the head-to-head this season" means leading the
                        // season tally, not merely beating the teammate once -
                        // judged from the running wins/losses counters that
                        // UpdateRaceRivalryAndForm has already advanced above.
                        objective.currentValue = Save.teammateRaceWins > Save.teammateRaceLosses ? 1 : 0;
                        break;
                    case "podiums":
                        if (player.finishingPosition <= 3)
                        {
                            objective.currentValue++;
                        }

                        break;
                    case "points":
                        objective.currentValue += Mathf.Max(0, player.points);
                        break;
                    case "clean_races":
                        if (cleanRace)
                        {
                            objective.currentValue++;
                        }

                        break;
                    case "first_win":
                        if (player.finishingPosition == 1)
                        {
                            objective.currentValue = 1;
                        }

                        break;
                    case "first_podium":
                        if (player.finishingPosition <= 3)
                        {
                            objective.currentValue = 1;
                        }

                        break;
                    default:
                        continue;
                }

                objective.achieved = positionObjective
                    ? (objective.currentValue > 0 && objective.currentValue <= objective.targetValue)
                    : (objective.currentValue >= objective.targetValue);
            }
        }

        // Offseason narrative reacting to the season that just closed -
        // separate from the regulation/team-evolution news above so a season
        // wrap-up always produces at least a few concrete articles even in a
        // quiet year with no regulation surprises.
        void GenerateOffseasonNews(SeasonArchive completedSeason)
        {
            if (completedSeason == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(completedSeason.driverChampionName))
            {
                AddNewsArticle(
                    completedSeason.driverChampionName + " crowned Season " + completedSeason.season + " champion",
                    completedSeason.driverChampionName + " wraps up the Season " + completedSeason.season + " drivers' title" +
                    (string.IsNullOrEmpty(completedSeason.constructorChampionName) ? "." : ", with " + completedSeason.constructorChampionName + " taking the constructors' crown."),
                    NewsCategoryGeneral);
            }

            string playerResultText = completedSeason.playerFinalPosition > 0
                ? "finished Season " + completedSeason.season + " P" + completedSeason.playerFinalPosition + " with " + completedSeason.playerPoints + " points"
                : "endured a season to forget";
            AddNewsArticle(
                "Season review: " + completedSeason.playerDriverName,
                completedSeason.playerDriverName + " " + playerResultText + ", with " + completedSeason.playerWins + " win" + (completedSeason.playerWins == 1 ? "" : "s") +
                " and " + completedSeason.playerPodiums + " podium" + (completedSeason.playerPodiums == 1 ? "" : "s") + ".",
                NewsCategoryGeneral);

            if (!string.IsNullOrEmpty(completedSeason.playerTeamName))
            {
                AddNewsArticle(
                    completedSeason.playerTeamName + " review the season",
                    completedSeason.playerTeamName + " finished P" + completedSeason.playerTeamFinalPosition + " in the constructors' championship on " +
                    completedSeason.playerTeamPoints + " points - the team now turns its attention to Season " + Save.currentSeason + ".",
                    NewsCategoryGeneral);
            }

            if (completedSeason.playerFinalPosition > 0 && completedSeason.playerFinalPosition <= completedSeason.contractTargetThatSeason)
            {
                AddNewsArticle(
                    "Contract talk: " + completedSeason.playerDriverName + " impresses the board",
                    completedSeason.playerDriverName + " met the team's target last year, and contract extension talks are already underway ahead of Season " + Save.currentSeason + ".",
                    NewsCategoryRumour);
            }
            else if (completedSeason.playerFinalPosition > 0)
            {
                AddNewsArticle(
                    "Contract talk: pressure mounts on " + completedSeason.playerDriverName,
                    "Missing the team's target last year has put " + completedSeason.playerDriverName + "'s seat under some scrutiny heading into Season " +
                    Save.currentSeason + ", though no change has been made.",
                    NewsCategoryRumour);
            }

            if (!string.IsNullOrEmpty(completedSeason.teammateName))
            {
                string comparison = completedSeason.teammateHeadToHeadWins >= completedSeason.teammateHeadToHeadLosses
                    ? completedSeason.playerDriverName + " held the edge over teammate " + completedSeason.teammateName
                    : completedSeason.teammateName + " came out on top in the intra-team battle";
                AddNewsArticle(
                    "Teammate battle: the final tally",
                    comparison + " (" + completedSeason.teammateHeadToHeadWins + "-" + completedSeason.teammateHeadToHeadLosses + " in race finishes) across Season " +
                    completedSeason.season + ".",
                    NewsCategoryRivalry);
            }
        }

        // On-demand pre-season testing report (same "not persisted, fully
        // re-derivable" convention as GenerateRaceWeekendBriefing) - built
        // from the current season's team-performance modifiers/regulations,
        // so it always reflects whatever was just generated for this season.
        public PreSeasonTestingReport GeneratePreSeasonTestingReport()
        {
            PreSeasonTestingReport report = new PreSeasonTestingReport();
            for (int i = 0; i < data.Teams.teams.Count; i++)
            {
                TeamData team = data.Teams.teams[i];
                CarPerformanceData car = GetEffectiveTeamCar(team, data.FindCar(team.carPerformanceId));
                if (car == null)
                {
                    continue;
                }

                float pace = (car.acceleration + car.cornering + car.braking + car.aeroEfficiency + car.chassisBalance + car.enginePower) / 6f;
                report.paceRanking.Add(new PreSeasonTestingEntry
                {
                    teamId = team.id,
                    teamName = team.name,
                    paceRating = pace,
                    reliabilityNote = car.reliability >= 85 ? "Strong reliability in testing" : (car.reliability <= 55 ? "Reliability concerns emerging" : "No major reliability concerns")
                });
            }

            report.paceRanking.Sort((a, b) => b.paceRating.CompareTo(a.paceRating));

            TeamData playerTeam = data.FindTeam(Save.playerTeamId);
            CarPerformanceData playerCar = GetEffectiveTeamCar(playerTeam, GetPlayerCar());
            int playerRank = report.paceRanking.FindIndex(entry => entry.teamId == Save.playerTeamId) + 1;
            report.playerExpectationText = playerRank > 0
                ? Save.playerDriverName + " and " + (playerTeam != null ? playerTeam.name : "the team") + " head into Season " + Save.currentSeason +
                  " ranked P" + playerRank + " on testing pace out of " + report.paceRanking.Count + " teams."
                : "Testing pace data unavailable.";

            DriverData teammate = data.FindTeammateDriver(Save.playerTeamId, Save.selectedDriverId, Save.driverTransferRecords);
            DriverData playerDriverData = string.IsNullOrEmpty(Save.selectedDriverId) ? null : data.FindDriver(Save.selectedDriverId);
            if (teammate != null)
            {
                int teammateRating = teammate.OverallRating;
                report.teammateBenchmarkText = playerDriverData != null
                    ? "Testing suggests a closely matched teammate battle with " + teammate.displayName + " (rated " + teammateRating + " vs your " + playerDriverData.OverallRating + ")."
                    : "Teammate " + teammate.displayName + " is rated " + teammateRating + " overall heading into testing.";
            }
            else
            {
                report.teammateBenchmarkText = "No teammate data available.";
            }

            float tyreWear = Save.currentSeasonTyreWearMultiplier;
            report.tyreDegradationText = tyreWear > 1.03f
                ? "New-season tyres are degrading faster in testing - expect tighter pit windows."
                : (tyreWear < 0.97f ? "New-season tyres are proving kinder in testing - longer stints look possible." : "Tyre degradation looks similar to last season so far.");

            if (playerCar != null)
            {
                List<KeyValuePair<string, int>> stats = new List<KeyValuePair<string, int>>
                {
                    new KeyValuePair<string, int>("Aerodynamics", playerCar.aeroEfficiency),
                    new KeyValuePair<string, int>("Chassis", playerCar.chassisBalance),
                    new KeyValuePair<string, int>("Power Unit", playerCar.enginePower),
                    new KeyValuePair<string, int>("Durability", playerCar.reliability),
                    new KeyValuePair<string, int>("Tyre Management", playerCar.tyreManagement),
                    new KeyValuePair<string, int>("ERS", playerCar.ersEfficiency)
                };

                stats.Sort((a, b) => a.Value.CompareTo(b.Value));
                for (int i = 0; i < Mathf.Min(2, stats.Count); i++)
                {
                    report.upgradeRecommendations.Add("Prioritise " + stats[i].Key + " development - currently the car's weakest area (" + stats[i].Value + ").");
                }
            }

            return report;
        }

        // Season-archive accessors for the Season Archive/History UI.
        public List<SeasonArchive> GetSeasonArchives()
        {
            return Save.seasonArchives;
        }

        public SeasonArchive GetSeasonArchive(int season)
        {
            return Save.seasonArchives.Find(archive => archive.season == season);
        }

        public SeasonArchive GetLatestSeasonArchive()
        {
            return Save.seasonArchives.Count > 0 ? Save.seasonArchives[Save.seasonArchives.Count - 1] : null;
        }

        // Career Hub / post-race-report state machine transitions. Each is a
        // one-way step forward through CareerSeasonPhase; ConfirmNewSeasonSetup
        // is the only one that clears seasonTransitionPending, so a reload
        // mid-flow always resumes exactly where it left off instead of
        // silently dropping back into ordinary Career Hub navigation.
        public void AdvanceToSeasonReview()
        {
            if (Save.seasonPhase == CareerSeasonPhase.FinalRaceComplete)
            {
                Save.seasonPhase = CareerSeasonPhase.SeasonReview;
                Write();
            }
        }

        public void AdvanceToOffseason()
        {
            Save.seasonPhase = CareerSeasonPhase.Offseason;
            Write();
        }

        public void AdvanceToPreseason()
        {
            Save.seasonPhase = CareerSeasonPhase.Preseason;
            Write();
        }

        public void ConfirmNewSeasonSetup()
        {
            Save.seasonTransitionPending = false;
            Save.seasonPhase = CareerSeasonPhase.NewSeasonActive;
            Save.preSeasonTestingSeen = true;
            Write();
        }
    }
}
