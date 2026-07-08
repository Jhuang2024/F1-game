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
        public void ApplyRaceResults(CalendarEventData raceEvent, List<RaceResultEntry> results, int incidentCount, int safetyCarDeploymentCount, int aiOvertakesCompletedCount)
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

            RaceReportRecord report = BuildRaceReport(raceEvent, results, incidentCount, safetyCarDeploymentCount, aiOvertakesCompletedCount);
            Save.raceReports.Add(report);
            while (Save.raceReports.Count > 12)
            {
                Save.raceReports.RemoveAt(0);
            }

            GenerateRaceControlNews(report);
            GenerateAiPerformanceNews(report);

            RaceResultEntry player = results.Find(entry => entry.isPlayer);
            if (player != null)
            {
                Save.resourcePoints += 90 + Mathf.Max(0, 11 - player.finishingPosition) * 15;
                int targetDelta = Mathf.Max(-4, Save.contractTargetPosition - player.finishingPosition);
                Save.reputation += player.finishingPosition <= 3 ? 4 : (targetDelta >= 0 ? 2 : -1);
                Save.resourcePoints += Mathf.Max(0, targetDelta) * 12;
                UpdateRaceRivalryAndForm(results, player, raceEvent);
            }

            AdvanceUpgradeProjects();

            Save.currentRound++;
            if (Save.currentRound > data.Calendar.events.Count)
            {
                Save.currentRound = 1;
                Save.currentSeason++;
                ApplyRegulationReset();
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
        RaceReportRecord BuildRaceReport(CalendarEventData raceEvent, List<RaceResultEntry> results, int incidentCount, int safetyCarDeploymentCount, int aiOvertakesCompletedCount)
        {
            RaceReportRecord report = new RaceReportRecord
            {
                season = Save.currentSeason,
                round = Save.currentRound,
                eventName = raceEvent != null ? raceEvent.displayName : "Prototype GP"
            };

            report.raceControl.incidentCount = incidentCount;
            report.raceControl.safetyCarDeployments = safetyCarDeploymentCount;
            report.raceControl.wasChaotic = incidentCount >= 5 || safetyCarDeploymentCount >= 2;
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
                Save.resourcePoints += Mathf.Max(8, 42 - player.position * 4);
                Save.reputation += player.position <= Save.contractTargetPosition ? 1 : 0;
                UpdateQualifyingRivalry(results, player);
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
                List<DriverData> teamDrivers = data.GetDriversForTeam(Save.playerTeamId);
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
                Save.driverStandings = data.CreateInitialDriverStandings(Save.playerDriverName, Save.playerTeamId, Save.selectedDriverId);
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
                List<DriverData> teamDrivers = data.GetDriversForTeam(Save.playerTeamId);
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
            List<DriverData> candidates = data.GetAiRaceDrivers(teamId, 8, selectedDriverId);
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
    }
}
