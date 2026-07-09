using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    public class CareerManager
    {
        const string CareerFile = "formula_racing_career.json";
        static readonly int[] Points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };
        const float UpgradeEffectScale = 2.25f;
        const float ExperimentalBonusScale = 1.3f;

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
                int points = i < Points.Length ? Points[i] : 0;
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

            AdvanceUpgradeProjects();

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
            return Mathf.RoundToInt(project.cost * 0.4f);
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

            return Mathf.Min(5, 2 + extraLevels / 2);
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
            return GetDepartmentLevel(departmentIndex) * 400;
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
            int weeks = Mathf.Max(1, Mathf.CeilToInt(upgrade.developmentDays / 10f));
            int level = GetDepartmentLevel(GetDepartmentIndex(upgrade.category));
            if (level > 1)
            {
                weeks = Mathf.Max(1, Mathf.CeilToInt(weeks * (1f - 0.1f * (level - 1))));
            }

            if (riskMode == RiskConservative)
            {
                weeks = Mathf.CeilToInt(weeks * 1.25f);
            }
            else if (riskMode == RiskRush)
            {
                weeks = Mathf.Max(1, Mathf.RoundToInt(weeks * 0.65f));
            }

            return weeks;
        }

        public float ComputeProjectSuccessChance(UpgradeData upgrade, int riskMode)
        {
            float chance = upgrade.successChance;
            int level = GetDepartmentLevel(GetDepartmentIndex(upgrade.category));
            chance += 0.04f * (level - 1);
            if (riskMode == RiskConservative)
            {
                chance = Mathf.Min(0.97f, chance + 0.10f);
            }
            else
            {
                if (riskMode == RiskRush)
                {
                    chance -= 0.15f;
                }
                else if (riskMode == RiskExperimental)
                {
                    chance -= 0.12f;
                }

                chance = Mathf.Min(0.95f, chance);
            }

            return Mathf.Max(0.05f, chance);
        }

        public int ComputeProjectCost(UpgradeData upgrade, int riskMode)
        {
            if (riskMode == RiskConservative)
            {
                return Mathf.RoundToInt(upgrade.cost * 1.15f);
            }

            return upgrade.cost;
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

            for (int i = 0; i < Save.completedUpgradeIds.Count; i++)
            {
                UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == Save.completedUpgradeIds[i]);
                if (upgrade == null)
                {
                    continue;
                }

                ActiveUpgradeProject project = FindProject(upgrade.id);
                float bonus = project != null && project.bonusApplied ? ExperimentalBonusScale : 1f;

                tuned.topSpeed += Mathf.RoundToInt(upgrade.topSpeedDelta * 1.7f * bonus);
                tuned.acceleration += Mathf.RoundToInt(upgrade.accelerationDelta * UpgradeEffectScale * bonus);
                tuned.cornering += Mathf.RoundToInt(upgrade.corneringDelta * UpgradeEffectScale * bonus);
                tuned.braking += Mathf.RoundToInt(upgrade.brakingDelta * UpgradeEffectScale * bonus);
                tuned.reliability += Mathf.RoundToInt(upgrade.reliabilityDelta * 1.6f * bonus);
                tuned.ersEfficiency += Mathf.RoundToInt(upgrade.ersDelta * UpgradeEffectScale * bonus);
                tuned.tyreManagement += Mathf.RoundToInt(upgrade.tyreDelta * UpgradeEffectScale * bonus);
                tuned.aeroEfficiency += Mathf.RoundToInt(upgrade.aeroDelta * UpgradeEffectScale * bonus);
                tuned.chassisBalance += Mathf.RoundToInt(upgrade.chassisDelta * UpgradeEffectScale * bonus);
                tuned.enginePower += Mathf.RoundToInt(upgrade.engineDelta * UpgradeEffectScale * bonus);
            }

            tuned.topSpeed = Mathf.Clamp(tuned.topSpeed, 315, 360);
            tuned.acceleration = Mathf.Clamp(tuned.acceleration, 45, 125);
            tuned.cornering = Mathf.Clamp(tuned.cornering, 45, 125);
            tuned.braking = Mathf.Clamp(tuned.braking, 45, 125);
            tuned.reliability = Mathf.Clamp(tuned.reliability, 35, 125);
            tuned.ersEfficiency = Mathf.Clamp(tuned.ersEfficiency, 45, 125);
            tuned.tyreManagement = Mathf.Clamp(tuned.tyreManagement, 45, 125);
            tuned.aeroEfficiency = Mathf.Clamp(tuned.aeroEfficiency, 45, 125);
            tuned.chassisBalance = Mathf.Clamp(tuned.chassisBalance, 45, 125);
            tuned.enginePower = Mathf.Clamp(tuned.enginePower, 45, 125);
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
            standings.Sort((a, b) =>
            {
                int pointsCompare = b.points.CompareTo(a.points);
                if (pointsCompare != 0)
                {
                    return pointsCompare;
                }

                int winsCompare = b.wins.CompareTo(a.wins);
                if (winsCompare != 0)
                {
                    return winsCompare;
                }

                return b.podiums.CompareTo(a.podiums);
            });
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

        void ApplyRegulationReset()
        {
            EnsureRndState();
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
            List<DriverData> candidates = data.GetAiRaceDrivers(teamId, 8, selectedDriverId, Save != null ? Save.driverTransferRecords : null);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].id != selectedDriverId && candidates[i].teamId != teamId)
                {
                    return candidates[i].id;
                }
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
            // Driver market + progression decided before the new season's
            // standings are built from the roster, so the standings/AI grid
            // already reflect this season's team moves and rating swings.
            GenerateDriverTransfers(completedSeason);
            GenerateDriverProgression(completedSeason);

            Save.driverStandings = data.CreateInitialDriverStandings(Save.playerDriverName, Save.playerTeamId, Save.selectedDriverId, Save.driverTransferRecords);
            Save.constructorStandings = data.CreateInitialConstructorStandings();
            EnsurePlayerReplacesDriverSeat();

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
                Save.rivalDriverId = PickRivalId(Save.playerTeamId, Save.selectedDriverId);
            }

            Save.contractTargetPosition = ContractTargetForTeam(Save.playerTeamId);
            Save.preSeasonTestingSeen = false;

            ApplyRegulationReset();
            GenerateRegulationChanges();
            GenerateTeamPerformanceEvolution(completedSeason);
            GenerateSeasonObjectives();
            GenerateOffseasonNews(completedSeason);
            Write();
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

        // Driver progression: a small season-to-season rating swing per driver,
        // biased by developmentPotential vs experience (a young, high-potential
        // driver trends up; an old, low-potential one trends down) and
        // compounding season over season - GetEffectiveDriver clamps the
        // resulting stats to a realistic 40-99 range at the point of use, so an
        // unbounded stored ratingDelta can never surface as an unrealistic
        // in-race stat even over a very long career.
        void GenerateDriverProgression(SeasonArchive completedSeason)
        {
            List<DriverRatingModifier> thisSeason = new List<DriverRatingModifier>();
            for (int i = 0; i < data.Drivers.drivers.Count; i++)
            {
                DriverData driver = data.Drivers.drivers[i];
                if (driver == null)
                {
                    continue;
                }

                DriverRatingModifier previous = Save.driverRatingModifiers.Find(m => m.driverId == driver.id && m.season == Save.currentSeason - 1);
                int previousDelta = previous == null ? 0 : previous.ratingDelta;

                bool risingTalent = driver.developmentPotential >= 75 && driver.experience < 75;
                bool decliningVeteran = driver.developmentPotential <= 55 && driver.experience >= 80;
                int step = risingTalent ? Random.Range(1, 4) : (decliningVeteran ? -Random.Range(1, 4) : Random.Range(-1, 2));
                int newDelta = previousDelta + step;

                DriverRatingModifier modifier = new DriverRatingModifier
                {
                    driverId = driver.id,
                    season = Save.currentSeason,
                    ratingDelta = newDelta,
                    trendLabel = step > 0 ? "Improving" : (step < 0 ? "Declining" : "Steady")
                };
                Save.driverRatingModifiers.Add(modifier);
                thisSeason.Add(modifier);
            }

            if (thisSeason.Count == 0)
            {
                return;
            }

            thisSeason.Sort((a, b) => b.ratingDelta.CompareTo(a.ratingDelta));
            DriverRatingModifier riser = thisSeason[0];
            DriverData riserDriver = data.FindDriver(riser.driverId);
            if (riser.ratingDelta >= 4 && riserDriver != null)
            {
                AddNewsArticle(
                    riserDriver.displayName + " is the form driver of the paddock",
                    riserDriver.displayName + " enters Season " + Save.currentSeason + " on a real upward curve, sharper than ever after a strong development cycle.",
                    NewsCategoryRumour);
            }

            DriverRatingModifier faller = thisSeason[thisSeason.Count - 1];
            DriverData fallerDriver = data.FindDriver(faller.driverId);
            if (faller.ratingDelta <= -4 && fallerDriver != null)
            {
                AddNewsArticle(
                    fallerDriver.displayName + " under pressure after a difficult stretch",
                    fallerDriver.displayName + " heads into Season " + Save.currentSeason + " having lost a step, with question marks starting to follow the seat.",
                    NewsCategoryRumour);
            }
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
        public CarPerformanceData GetEffectiveTeamCar(TeamData team, CarPerformanceData baseCar)
        {
            if (team == null || baseCar == null)
            {
                return baseCar;
            }

            CarPerformanceData tuned = ApplyTeamPerformanceModifier(team.id, baseCar);
            if (team.id == Save.playerTeamId)
            {
                tuned = ApplyCareerUpgrades(tuned);
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
                topSpeed = Mathf.RoundToInt(baseCar.topSpeed * Mathf.Lerp(1f, scale, 0.3f)),
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
            int delta = modifier == null ? 0 : modifier.ratingDelta;

            if (effectiveTeamId == baseDriver.teamId && delta == 0)
            {
                return baseDriver;
            }

            return new DriverData
            {
                id = baseDriver.id,
                displayName = baseDriver.displayName,
                abbreviation = baseDriver.abbreviation,
                number = baseDriver.number,
                teamId = effectiveTeamId,
                pace = ClampRating(baseDriver.pace + delta),
                racecraft = ClampRating(baseDriver.racecraft + delta),
                qualifying = ClampRating(baseDriver.qualifying + delta),
                tyreManagement = ClampRating(baseDriver.tyreManagement + delta),
                wetSkill = ClampRating(baseDriver.wetSkill + delta),
                consistency = ClampRating(baseDriver.consistency + delta),
                aggression = baseDriver.aggression,
                defending = ClampRating(baseDriver.defending + delta),
                overtaking = ClampRating(baseDriver.overtaking + delta),
                awareness = ClampRating(baseDriver.awareness + delta),
                experience = baseDriver.experience,
                developmentPotential = baseDriver.developmentPotential
            };
        }

        static int ClampRating(int value)
        {
            return Mathf.Clamp(value, 40, 99);
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

            RaceResultEntry teammate = results.Find(entry => entry.teamId == Save.playerTeamId && entry.driverId != "player");
            bool playerDnf = !string.IsNullOrEmpty(player.penaltyReason) && player.penaltyReason.Contains("DNF");
            bool cleanRace = player.penaltiesSeconds <= 0f && !playerDnf;

            for (int i = 0; i < Save.seasonObjectives.Count; i++)
            {
                SeasonObjective objective = Save.seasonObjectives[i];
                if (objective.achieved)
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
                        if (teammate != null && player.finishingPosition < teammate.finishingPosition)
                        {
                            objective.currentValue = 1;
                        }

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
