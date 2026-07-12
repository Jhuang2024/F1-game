using System;
using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Race.Rules;
using F1Game.UI;
using F1Game.UI.Screens;
using F1Game.UI.Screens.CareerCreation;
using F1Game.UI.Screens.CareerHub;
using F1Game.UI.Screens.CareerStats;
using F1Game.UI.Screens.DriverRatings;
using F1Game.UI.Screens.PracticePrograms;
using F1Game.UI.Screens.Rnd;
using F1Game.UI.Screens.TeamRatings;
using F1Game.UI.Screens.CareerStandings;
using F1Game.UI.Screens.Championship;
using F1Game.UI.Screens.DriverProfile;
using F1Game.UI.Screens.MainMenu;
using F1Game.UI.Screens.RaceHudShell;
using F1Game.UI.Screens.Results;
using F1Game.UI.Screens.PreRaceStrategy;
using F1Game.UI.Screens.Settings;
using F1Game.UI.Screens.TrackSelect;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Bridge between the legacy GameBootstrap flows and the new production UI
    /// (F1Game.UI): prefab-oriented screens, ScreenRouter navigation, TMP text,
    /// view/presenter separation. The bridge owns all mapping from monolith data
    /// types to the UI view-models, so the new assemblies never reference legacy
    /// code.
    ///
    /// Covered by the vertical slice: Main menu → Quick-race track select →
    /// Pre-race strategy → race start. Career, time trial and settings still
    /// route to the legacy screens (shell hides, RuntimeUi shows).
    ///
    /// Safety: any exception in the new path disables the bridge for the session
    /// and falls back to the legacy UI. Kill switch: PlayerPrefs
    /// "f1game_production_ui" = 0 forces legacy, 1 forces new, unset = auto.
    /// </summary>
    public static class ProductionUiBridge
    {
        const string ToggleKey = "f1game_production_ui";

        // Canonical compound order shared with the legacy UI
        // (RuntimeUi.TyreCompoundOrder) - settings store compounds as names.
        static readonly string[] CompoundNames = { "Soft", "Medium", "Hard", "Intermediate", "Wet" };

        static int CompoundIndex(string compoundName, int fallback)
        {
            int index = System.Array.IndexOf(CompoundNames, compoundName);
            return index >= 0 ? index : fallback;
        }

        static UiShell shell;
        static MainMenuPresenter mainMenuPresenter;
        static TrackSelectPresenter trackSelectPresenter;
        static PreRaceStrategyPresenter strategyPresenter;
        static CareerStandingsPresenter standingsPresenter;
        static ChampionshipChartPresenter championshipChartPresenter;
        static CareerHubPresenter careerHubPresenter;
        static CareerCreationPresenter careerCreationPresenter;
        static CareerStatsPresenter careerStatsPresenter;
        static CareerStatsPresenter trophyCabinetPresenter;
        static DriverRatingsPresenter driverRatingsPresenter;
        static TeamRatingsPresenter teamRatingsPresenter;
        static RndPresenter rndPresenter;
        static PracticeProgramsPresenter practiceProgramsPresenter;
        static DriverProfilePresenter profilePresenter;
        static SettingsPresenter settingsPresenter;
        static ResultsPresenter resultsPresenter;
        static PauseOverlay pauseOverlay;
        static bool failedThisSession;
        static CalendarEventData selectedEvent;
        // True while the shared track-select screen is being used to pick a time
        // trial (vs a quick race), so OnTrackChosen skips the strategy step.
        static bool timeTrialFlow;

        static GameBootstrap bootstrap;
        static GameDataRepository data;
        static CareerManager career;
        static GameSettingsStore settings;

        /// <summary>The production shell (exposed so the session-UI bridge can drive the HUD).</summary>
        public static UiShell Shell => shell;

        static bool Enabled
        {
            get
            {
                if (failedThisSession)
                {
                    return false;
                }

                // Explicit, versioned capability — NOT TMP-readiness alone.
                // Importing TMP no longer flips the active frontend.
                return ProductionUiReadiness.Enabled;
            }
        }

        public static bool TryShowMainMenu(GameBootstrap owner, GameDataRepository gameData, CareerManager careerManager, GameSettingsStore settingsStore)
        {
            if (!Enabled)
            {
                return false;
            }

            try
            {
                Adopt(owner, gameData, careerManager, settingsStore);
                EnsureShell();
                owner.Ui.Clear();
                shell.SetShellVisible(true);
                shell.Router.ResetStack();

                shell.Router.Show(MainMenuView.Id);
                mainMenuPresenter.Present(BuildMainMenuModel());
                return true;
            }
            catch (Exception exception)
            {
                Fail("main menu", exception);
                return false;
            }
        }

        public static bool TryShowQuickRaceFlow(GameBootstrap owner, GameDataRepository gameData, CareerManager careerManager, GameSettingsStore settingsStore)
        {
            if (!Enabled)
            {
                return false;
            }

            try
            {
                Adopt(owner, gameData, careerManager, settingsStore);
                EnsureShell();
                owner.Ui.Clear();
                shell.SetShellVisible(true);
                ShowTrackSelect();
                return true;
            }
            catch (Exception exception)
            {
                Fail("quick race flow", exception);
                return false;
            }
        }

        static void Adopt(GameBootstrap owner, GameDataRepository gameData, CareerManager careerManager, GameSettingsStore settingsStore)
        {
            bootstrap = owner;
            data = gameData;
            career = careerManager;
            settings = settingsStore;
        }

        static void EnsureShell()
        {
            if (shell != null)
            {
                return;
            }

            shell = UiShell.Create();
            ApplyAccessibilityToShell();

            var mainMenuView = (MainMenuView)ShowAndGet(MainMenuView.Id);
            mainMenuPresenter = new MainMenuPresenter(mainMenuView)
            {
                // Production career hub when a career exists; otherwise the production
                // creation screen (default), with the legacy career flow as the
                // emergency fallback (kill switch or initialization failure).
                OnCareer = () =>
                {
                    if (career != null && career.Save != null)
                    {
                        ShowCareerHub();
                    }
                    else if (ProductionCareerCreationEnabled)
                    {
                        ShowCareerCreation();
                    }
                    else
                    {
                        LeaveToLegacy(() => bootstrap.ShowCareer());
                    }
                },
                OnQuickRace = () => { timeTrialFlow = false; ShowTrackSelect(); },
                // Time trial now uses the production track-select screen too, then
                // hands off to the proven legacy BeginTimeTrial for the start.
                OnTimeTrial = () => { timeTrialFlow = true; ShowTrackSelect(); },
                OnStandings = ShowCareerStandings,
                OnSettings = ShowSettings,
            };

            var trackSelectView = (TrackSelectView)ShowAndGet(TrackSelectView.Id);
            trackSelectPresenter = new TrackSelectPresenter(trackSelectView)
            {
                OnTrackChosen = OnTrackChosen,
                OnBack = () => shell.Router.Back(),
            };

            var strategyView = (PreRaceStrategyView)ShowAndGet(PreRaceStrategyView.Id);
            strategyPresenter = new PreRaceStrategyPresenter(strategyView)
            {
                OnStartRace = OnStrategyConfirmed,
                OnBack = () => shell.Router.Back(),
            };

            var standingsView = (CareerStandingsView)ShowAndGet(CareerStandingsView.Id);
            standingsPresenter = new CareerStandingsPresenter(standingsView)
            {
                OnBack = () => shell.Router.Back(),
                OnGraph = () => ShowChampionshipChart(false),
            };

            var championshipView = (ChampionshipChartView)ShowAndGet(ChampionshipChartView.Id);
            championshipChartPresenter = new ChampionshipChartPresenter(championshipView)
            {
                OnModeSelected = ShowChampionshipChart,
                OnBack = () => shell.Router.Back(),
            };

            var careerHubView = (CareerHubView)ShowAndGet(CareerHubView.Id);
            careerHubPresenter = new CareerHubPresenter(careerHubView)
            {
                // The race start itself (tyre select / qualifying) is still the
                // legacy flow; the hub hands off exactly like the legacy hub's
                // own continue button does.
                OnContinue = () => LeaveToLegacy(() => bootstrap.StartCareerRace()),
                OnStandings = ShowCareerStandings,
                OnProfile = ShowDriverProfile,
                OnStats = ShowCareerStats,
                OnRatings = () => ShowDriverRatings("overall"),
                OnRnd = ShowRnd,
                OnPractice = ShowPracticePrograms,
                OnLegacyMenu = () => LeaveToLegacy(() => bootstrap.ShowCareer()),
                OnBack = () => shell.Router.Back(),
            };

            var careerCreationView = (CareerCreationView)ShowAndGet(CareerCreationView.Id);
            careerCreationPresenter = new CareerCreationPresenter(careerCreationView)
            {
                OnStart = StartProductionCareer,
                OnBack = () => shell.Router.Back(),
            };

            var careerStatsView = (CareerStatsView)ShowAndGet(CareerStatsView.Id);
            careerStatsPresenter = new CareerStatsPresenter(careerStatsView)
            {
                OnBack = () => shell.Router.Back(),
                OnSecondary = ShowTrophyCabinet,
            };

            var trophyCabinetView = (CareerStatsView)ShowAndGet(CareerStatsView.TrophyId);
            trophyCabinetPresenter = new CareerStatsPresenter(trophyCabinetView)
            {
                OnBack = () => shell.Router.Back(),
                OnSecondary = ShowCareerStats,
            };

            var driverRatingsView = (DriverRatingsView)ShowAndGet(DriverRatingsView.Id);
            driverRatingsPresenter = new DriverRatingsPresenter(driverRatingsView)
            {
                OnSort = ShowDriverRatings,
                OnTeams = ShowTeamRatings,
                OnBack = () => shell.Router.Back(),
            };

            var teamRatingsView = (TeamRatingsView)ShowAndGet(TeamRatingsView.Id);
            teamRatingsPresenter = new TeamRatingsPresenter(teamRatingsView)
            {
                OnDrivers = () => ShowDriverRatings("overall"),
                OnBack = () => shell.Router.Back(),
            };

            var rndView = (RndView)ShowAndGet(RndView.Id);
            rndPresenter = new RndPresenter(rndView)
            {
                OnUpgradeDepartment = RndUpgradeDepartment,
                OnStartUpgrade = RndStartUpgrade,
                OnReworkProject = RndReworkProject,
                OnAbandonProject = RndAbandonProject,
                OnBack = () => shell.Router.Back(),
            };

            var practiceView = (PracticeProgramsView)ShowAndGet(PracticeProgramsView.Id);
            practiceProgramsPresenter = new PracticeProgramsPresenter(practiceView)
            {
                OnRun = RunPracticeProgram,
                OnBack = () => shell.Router.Back(),
            };

            var profileView = (DriverProfileView)ShowAndGet(DriverProfileView.Id);
            profilePresenter = new DriverProfilePresenter(profileView)
            {
                OnBack = () => shell.Router.Back(),
            };

            var settingsView = (SettingsView)ShowAndGet(SettingsView.Id);
            settingsPresenter = new SettingsPresenter(settingsView)
            {
                // Full editing still routes to the legacy screen; production owns
                // the live summary plus a handful of inline accessibility toggles.
                OnClassic = () => LeaveToLegacy(() => bootstrap.Ui.ShowSettings(data, career, settings)),
                OnBack = () => shell.Router.Back(),
                OnCycleDifficulty = () => ToggleSetting(s => s.difficultyIndex = (s.difficultyIndex + 1) % DifficultyNames.Length),
                OnCycleErs = () => ToggleSetting(s => s.ersMode = (s.ersMode + 1) % ErsModeNames.Length),
                OnToggleManualGears = () => ToggleSetting(s => s.manualGears = !s.manualGears),
                OnToggleUnits = () => ToggleSetting(s => s.useMphUnits = !s.useMphUnits),
                OnToggleCameraShake = () => ToggleSetting(s => s.cameraShake = !s.cameraShake),
                OnToggleCompactHud = () => ToggleSetting(s => s.compactHud = !s.compactHud),
                OnToggleUiAnimations = () => ToggleSetting(s => s.uiAnimations = !s.uiAnimations),
                // Full production editor: adjust any setting by id + direction.
                OnFieldAdjusted = AdjustSettingField,
            };

            var resultsView = (ResultsView)ShowAndGet(ResultsView.Id);
            resultsPresenter = new ResultsPresenter(resultsView)
            {
                // Wired per-session in TryShowResults (the action depends on
                // whether the just-finished race was a career round).
            };

            // Instantiation pass done; leave the router parked on the main menu.
            shell.Router.ResetStack();
            shell.Router.Show(MainMenuView.Id);
        }

        /// <summary>Instantiates a screen through the router once so its presenter can bind.</summary>
        static F1Game.UI.Navigation.ScreenView ShowAndGet(string id)
        {
            shell.Router.Show(id);
            return shell.Router.Current;
        }

        static MainMenuModel BuildMainMenuModel()
        {
            bool hasCareer = career != null && career.Save != null;
            string summary = "";
            if (hasCareer)
            {
                int rounds = data != null && data.Calendar != null ? data.Calendar.events.Count : 0;
                summary = string.Format("Season {0} · Round {1}{2} · {3}",
                    career.Save.currentSeason,
                    career.Save.currentRound + 1,
                    rounds > 0 ? "/" + rounds : "",
                    string.IsNullOrEmpty(career.Save.playerDriverName) ? "" : career.Save.playerDriverName);
            }

            return new MainMenuModel
            {
                hasCareer = hasCareer,
                careerSummary = summary,
                playerName = hasCareer ? career.Save.playerDriverName : "",
                versionLabel = "v" + Application.version + " · production migration slice",
            };
        }

        static void ShowCareerHub()
        {
            var model = new CareerHubModel();
            if (career != null && career.Save != null)
            {
                int rounds = data != null && data.Calendar != null ? data.Calendar.events.Count : 0;
                model.seasonLabel = string.Format("Season {0} · Round {1}{2} · {3}",
                    career.Save.currentSeason,
                    career.Save.currentRound + 1,
                    rounds > 0 ? "/" + rounds : "",
                    career.Save.playerDriverName);

                CalendarEventData nextEvent = career.CurrentEvent();
                if (nextEvent != null)
                {
                    model.nextEventName = nextEvent.displayName;
                    model.nextEventDetail = string.Format("{0} · {1} laps · {2}",
                        nextEvent.country,
                        settings != null ? settings.Current.laps : nextEvent.laps5,
                        string.IsNullOrEmpty(nextEvent.weatherProfile) ? "Variable" : nextEvent.weatherProfile);
                }

                // The rulebook decides where the weekend goes next.
                model.continueLabel = SessionFlow.NextCareerStep(career.HasQualifyingForCurrentRound()) == WeekendSession.Qualifying
                    ? "Continue: Qualifying"
                    : "Continue: Race";

                // Player's championship position for the hub's context line.
                if (career.Save.driverStandings != null)
                {
                    for (int i = 0; i < career.Save.driverStandings.Count; i++)
                    {
                        StandingEntry entry = career.Save.driverStandings[i];
                        if (entry.displayName == career.Save.playerDriverName)
                        {
                            model.standingLine = string.Format("P{0} · {1} pts{2}",
                                i + 1, entry.points, entry.wins > 0 ? " · " + entry.wins + " wins" : "");
                            break;
                        }
                    }
                }
            }

            shell.Router.Show(CareerHubView.Id);
            careerHubPresenter.Present(model);
        }

        /// <summary>
        /// Production-first (only reachable once the production UI itself is active):
        /// the production career-creation screen is the default; PlayerPrefs=0 is the
        /// emergency kill switch back to the legacy career flow. ShowCareerCreation
        /// also auto-falls-back to legacy if it throws.
        /// </summary>
        public const string ProductionCareerCreationKey = "f1game_production_career_creation";

        public static bool ProductionCareerCreationEnabled => PlayerPrefs.GetInt(ProductionCareerCreationKey, 1) != 0;

        // Clearly-labelled PLACEHOLDER driver names (fictional) offered on the
        // production creation screen until free-text entry is validated in-editor.
        static readonly string[] PlaceholderDriverNames =
        {
            "Alex Vettori", "Jordan Vale", "Sam Reyes", "Robin Vasseur",
            "Chris Nakamura", "Dana Whitlock", "Player Driver",
        };

        static void ShowCareerCreation()
        {
            try
            {
                var model = new CareerCreationModel();
                for (int i = 0; i < PlaceholderDriverNames.Length; i++)
                {
                    model.driverNames.Add(PlaceholderDriverNames[i]);
                }

                if (data != null && data.Teams != null)
                {
                    for (int i = 0; i < data.Teams.teams.Count; i++)
                    {
                        TeamData team = data.Teams.teams[i];
                        if (team == null)
                        {
                            continue;
                        }

                        model.teams.Add(new CareerTeamOption
                        {
                            teamId = team.id,
                            teamName = string.IsNullOrEmpty(team.name) ? team.id : team.name,
                            detail = "Reputation " + team.reputation,
                        });
                    }
                }

                shell.Router.Show(CareerCreationView.Id);
                careerCreationPresenter.Present(model);
            }
            catch (Exception)
            {
                // Automatic emergency fallback: any failure hands off to the proven
                // legacy career flow rather than stranding the player.
                LeaveToLegacy(() => bootstrap.ShowCareer());
            }
        }

        // Create the new career from the production screen's choices, then continue
        // into the production career hub (the same destination the legacy flow lands on).
        static void StartProductionCareer(string driverName, string teamId)
        {
            if (career == null || string.IsNullOrEmpty(teamId))
            {
                LeaveToLegacy(() => bootstrap.ShowCareer());
                return;
            }

            career.StartNewCareer(driverName, teamId);
            ShowCareerHub();
        }

        // Production career-stats screen: read-only career records + local lap records.
        static void ShowCareerStats()
        {
            var model = new CareerStatsModel();
            PlayerRecordsData records = PlayerRecordsStore.Data;
            if (records != null)
            {
                Stat(model, "Races", records.racesFinished.ToString());
                Stat(model, "Wins", records.raceWins.ToString());
                Stat(model, "Podiums", records.podiums.ToString());
                Stat(model, "Poles", records.polePositions.ToString());
                Stat(model, "Fastest Laps", records.fastestLaps.ToString());
                Stat(model, "Points", records.totalPoints.ToString());
                Stat(model, "Clean Races", records.cleanRaces.ToString());
                Stat(model, "Best Qualifying", records.bestQualifyingPosition > 0 ? "P" + records.bestQualifyingPosition : "--");
                Stat(model, "Track Limit Warnings", records.trackLimitWarningsTotal.ToString());

                for (int i = 0; i < records.trackRecords.Count; i++)
                {
                    TrackRecordEntry entry = records.trackRecords[i];
                    model.records.Add(new TrackRecordRow
                    {
                        track = ResolveTrackName(entry.trackId),
                        time = UiFactory.FormatTime(entry.bestLapTime),
                        context = string.IsNullOrEmpty(entry.context) ? "" : entry.context.ToUpperInvariant(),
                    });
                }
            }

            model.emptyRecordsMessage = "No lap records yet. Run a time trial to set benchmarks.";
            model.secondaryLabel = "Trophy Cabinet";
            shell.Router.Show(CareerStatsView.Id);
            careerStatsPresenter.Present(model);
        }

        // Production trophy cabinet: season/championship trophies + per-track achievements.
        static void ShowTrophyCabinet()
        {
            var model = new CareerStatsModel();
            PlayerRecordsData records = PlayerRecordsStore.Data;
            if (records != null)
            {
                Stat(model, "Championships", records.championshipsWon.ToString());
                Stat(model, "Constructors' Titles", records.constructorsChampionshipsWon.ToString());
                Stat(model, "Seasons Completed", records.completedSeasons.ToString());
                Stat(model, "Best Clean Streak", records.bestCleanRaceStreak.ToString());
                Stat(model, "Biggest Comeback", records.biggestComebackPositions > 0 ? "+" + records.biggestComebackPositions : "--");
                Stat(model, "Most Overtakes (race)", records.mostOvertakesInRace.ToString());
                Stat(model, "Best Wet Result", records.bestWetFinishPosition > 0 ? "P" + records.bestWetFinishPosition : "--");

                if (records.trackAchievements != null)
                {
                    for (int i = 0; i < records.trackAchievements.Count; i++)
                    {
                        TrackAchievementEntry entry = records.trackAchievements[i];
                        if (entry.wins == 0 && entry.podiums == 0 && entry.poles == 0)
                        {
                            continue;
                        }

                        model.records.Add(new TrackRecordRow
                        {
                            track = ResolveTrackName(entry.trackId),
                            time = entry.wins + "W",
                            context = entry.podiums + " podiums · " + entry.poles + " poles",
                        });
                    }
                }
            }

            model.emptyRecordsMessage = "No wins, podiums or poles yet - they appear here after your first race weekend.";
            model.secondaryLabel = "Career Stats";
            shell.Router.Show(CareerStatsView.TrophyId);
            trophyCabinetPresenter.Present(model);
        }

        static void Stat(CareerStatsModel model, string label, string value)
        {
            model.stats.Add(new CareerStatCell { label = label, value = value });
        }

        // Production driver ratings: read-only, sortable. Sorting re-enters here.
        static void ShowDriverRatings(string sortKey)
        {
            string key = string.IsNullOrEmpty(sortKey) ? "overall" : sortKey;
            var model = new DriverRatingsModel { sortKey = key };
            if (data != null && data.Drivers != null)
            {
                var drivers = new List<DriverData>(data.Drivers.drivers);
                if (career != null && career.Save != null)
                {
                    for (int i = 0; i < drivers.Count; i++)
                    {
                        drivers[i] = career.GetEffectiveDriver(drivers[i]);
                    }
                }

                SortDriverList(drivers, key);
                string playerId = ResolveRatingsPlayerId();
                string teammateId = ResolveRatingsTeammateId(playerId);

                for (int i = 0; i < drivers.Count; i++)
                {
                    DriverData d = drivers[i];
                    TeamData t = data.FindTeam(d.teamId);
                    int highlight = !string.IsNullOrEmpty(playerId) && d.id == playerId ? 1
                        : (!string.IsNullOrEmpty(teammateId) && d.id == teammateId ? 2 : 0);
                    model.rows.Add(new DriverRatingRow
                    {
                        name = d.displayName,
                        team = t == null ? d.teamId : t.name,
                        overall = d.OverallRating.ToString(),
                        pace = d.pace.ToString(),
                        qualifying = d.qualifying.ToString(),
                        racecraft = d.racecraft.ToString(),
                        potential = d.developmentPotential.ToString(),
                        highlight = highlight,
                    });
                }
            }

            shell.Router.Show(DriverRatingsView.Id);
            driverRatingsPresenter.Present(model);
        }

        // Faithful copy of the legacy driver sort (pure; safe to duplicate).
        static void SortDriverList(List<DriverData> drivers, string sortKey)
        {
            drivers.Sort((a, b) =>
            {
                int primary;
                switch (sortKey)
                {
                    case "pace": primary = b.pace.CompareTo(a.pace); break;
                    case "qualifying": primary = b.qualifying.CompareTo(a.qualifying); break;
                    case "racecraft": primary = b.racecraft.CompareTo(a.racecraft); break;
                    case "potential": primary = b.developmentPotential.CompareTo(a.developmentPotential); break;
                    default: primary = b.OverallRating.CompareTo(a.OverallRating); break;
                }

                return primary != 0 ? primary : b.pace.CompareTo(a.pace);
            });
        }

        // Read-only player/team-mate detection (no write-back, unlike the legacy
        // ratings screen which repairs selectedDriverId as a side effect).
        static string ResolveRatingsPlayerId()
        {
            if (career == null || career.Save == null || data == null)
            {
                return "";
            }

            List<DriverData> teamDrivers = data.GetDriversForTeam(career.Save.playerTeamId, career.Save.driverTransferRecords);
            if (career.Save.useExistingDriver && !string.IsNullOrEmpty(career.Save.selectedDriverId) &&
                teamDrivers.Exists(d => d.id == career.Save.selectedDriverId))
            {
                return career.Save.selectedDriverId;
            }

            if (!string.IsNullOrEmpty(career.Save.playerDriverName))
            {
                DriverData byName = teamDrivers.Find(d => string.Equals(d.displayName, career.Save.playerDriverName, StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                {
                    return byName.id;
                }
            }

            return teamDrivers.Count > 0 ? teamDrivers[0].id : "";
        }

        static string ResolveRatingsTeammateId(string playerId)
        {
            if (career == null || career.Save == null || data == null || string.IsNullOrEmpty(playerId))
            {
                return "";
            }

            List<DriverData> teamDrivers = data.GetDriversForTeam(career.Save.playerTeamId, career.Save.driverTransferRecords);
            for (int i = 0; i < teamDrivers.Count; i++)
            {
                if (teamDrivers[i].id != playerId)
                {
                    return teamDrivers[i].id;
                }
            }

            return "";
        }

        // Production team ratings: read-only, sorted by effective car overall.
        static void ShowTeamRatings()
        {
            var model = new TeamRatingsModel();
            if (data != null && data.Teams != null)
            {
                var teams = new List<TeamData>(data.Teams.teams);
                teams.Sort((a, b) => CarOverallFor(b).CompareTo(CarOverallFor(a)));

                for (int i = 0; i < teams.Count; i++)
                {
                    TeamData team = teams[i];
                    if (team == null)
                    {
                        continue;
                    }

                    CarPerformanceData car = EffectiveCarFor(team);
                    bool isPlayerTeam = career != null && career.Save != null && team.id == career.Save.playerTeamId;
                    model.rows.Add(new TeamRatingRow
                    {
                        name = string.IsNullOrEmpty(team.name) ? team.id : team.name,
                        carOverall = (car == null ? 0 : RatingCalculator.GetCarOverall(car)).ToString(),
                        reliability = (car == null ? team.reliability : car.reliability).ToString(),
                        reputation = team.reputation.ToString(),
                        isPlayerTeam = isPlayerTeam,
                    });
                }
            }

            shell.Router.Show(TeamRatingsView.Id);
            teamRatingsPresenter.Present(model);
        }

        static CarPerformanceData EffectiveCarFor(TeamData team)
        {
            CarPerformanceData baseCar = data != null ? data.FindCar(team.carPerformanceId) : null;
            return career != null && career.Save != null ? career.GetEffectiveTeamCar(team, baseCar) : baseCar;
        }

        static int CarOverallFor(TeamData team)
        {
            CarPerformanceData car = EffectiveCarFor(team);
            return car == null ? 0 : RatingCalculator.GetCarOverall(car);
        }

        // Production R&D centre: presentation projected from authoritative saved state.
        static void ShowRnd()
        {
            var model = new RndModel();
            if (career != null && career.Save != null && data != null && data.Upgrades != null)
            {
                int rp = career.Save.resourcePoints;
                int active = career.ActiveProjectCount();
                int maxActive = career.MaxActiveProjects();
                model.summaryLine = rp + " RP · Slots " + active + "/" + maxActive + " · Season " + career.Save.currentSeason;

                for (int i = 0; i < CareerManager.DepartmentNames.Length; i++)
                {
                    int level = career.GetDepartmentLevel(i);
                    bool maxed = level >= CareerManager.MaxDepartmentLevel;
                    int cost = career.GetDepartmentUpgradeCost(i);
                    model.departments.Add(new RndRow
                    {
                        id = i.ToString(),
                        label = CareerManager.DepartmentNames[i],
                        detail = maxed ? ("Level " + level + " · MAX") : ("Level " + level + " · Upgrade " + cost + " RP"),
                        primaryLabel = "Upgrade",
                        primaryShown = true,
                        primaryEnabled = F1Game.Core.RndEligibility.CanUpgradeDepartment(level, CareerManager.MaxDepartmentLevel, rp),
                    });
                }

                for (int i = 0; i < career.Save.activeUpgradeProjects.Count; i++)
                {
                    ActiveUpgradeProject p = career.Save.activeUpgradeProjects[i];
                    if (p.status == CareerManager.ProjectInDevelopment)
                    {
                        model.projects.Add(new RndRow
                        {
                            id = p.upgradeId,
                            label = UpgradeName(p.upgradeId),
                            detail = p.remainingRaceWeeks + " weeks left · " + Mathf.RoundToInt(p.successChance * 100f) + "% success",
                        });
                    }
                    else if (p.status == CareerManager.ProjectReworkAvailable)
                    {
                        int reworkCost = career.GetReworkCost(p);
                        model.projects.Add(new RndRow
                        {
                            id = p.upgradeId,
                            label = UpgradeName(p.upgradeId),
                            detail = "Failed · rework " + reworkCost + " RP",
                            primaryLabel = "Rework",
                            primaryShown = true,
                            primaryEnabled = F1Game.Core.RndEligibility.CanRework(rp, p.cost, active, maxActive, true),
                            secondaryLabel = "Abandon",
                            secondaryShown = true,
                            secondaryEnabled = true,
                        });
                    }
                }

                model.emptyProjectsMessage = "No projects in development. Start one from the upgrade tree.";

                for (int i = 0; i < data.Upgrades.upgrades.Count; i++)
                {
                    UpgradeData u = data.Upgrades.upgrades[i];
                    bool owned = career.Save.completedUpgradeIds.Contains(u.id)
                        || career.Save.failedUpgradeIds.Contains(u.id)
                        || career.FindProject(u.id) != null;
                    if (owned)
                    {
                        continue;
                    }

                    int deptLevel = career.GetDepartmentLevel(career.GetDepartmentIndex(u.category));
                    int cost = career.ComputeProjectCost(u, CareerManager.RiskStandard);
                    bool prereqMet = string.IsNullOrEmpty(u.requiredUpgradeId) || career.Save.completedUpgradeIds.Contains(u.requiredUpgradeId);
                    model.upgrades.Add(new RndRow
                    {
                        id = u.id,
                        label = string.IsNullOrEmpty(u.displayName) ? u.id : u.displayName,
                        detail = u.category + " · Tier " + u.tier + " · " + cost + " RP",
                        primaryLabel = "Start",
                        primaryShown = true,
                        primaryEnabled = F1Game.Core.RndEligibility.CanStartProject(rp, cost, deptLevel, u.tier, active, maxActive, false, prereqMet),
                    });
                }
            }

            shell.Router.Show(RndView.Id);
            rndPresenter.Present(model);
        }

        static string UpgradeName(string upgradeId)
        {
            if (data != null && data.Upgrades != null)
            {
                UpgradeData u = data.Upgrades.upgrades.Find(x => x.id == upgradeId);
                if (u != null)
                {
                    return string.IsNullOrEmpty(u.displayName) ? u.id : u.displayName;
                }
            }

            return upgradeId;
        }

        // Command adapters: issue the authoritative CareerManager mutation, then
        // re-present from saved state (which also disables now-ineligible buttons,
        // so the same mutation can't be submitted twice). The Try* methods
        // re-validate and own all deduction/save writes.
        static void RndUpgradeDepartment(string idText)
        {
            if (career != null && int.TryParse(idText, out int index))
            {
                career.TryUpgradeDepartment(index);
            }

            ShowRnd();
        }

        static void RndStartUpgrade(string upgradeId)
        {
            if (career != null)
            {
                career.TryStartUpgradeProject(upgradeId, CareerManager.RiskStandard);
            }

            ShowRnd();
        }

        static void RndReworkProject(string upgradeId)
        {
            if (career != null)
            {
                career.TryReworkProject(upgradeId);
            }

            ShowRnd();
        }

        static void RndAbandonProject(string upgradeId)
        {
            if (career != null)
            {
                career.AbandonProject(upgradeId);
            }

            ShowRnd();
        }

        // The round's practice programs (id, title, description, RP reward, REP reward) -
        // the same set the legacy screen offers.
        static readonly string[][] PracticeProgramDefs =
        {
            new[] { "acclimatisation", "Track Acclimatisation", "Learn the braking points and kerbs. Steady laps, no risks.", "22", "1" },
            new[] { "tyreManagement", "Tyre Management", "Long-run stint watching temperatures and wear windows.", "20", "0" },
            new[] { "ersManagement", "ERS Management", "Deployment mapping over a full lap for better battery use.", "18", "0" },
            new[] { "qualifyingPace", "Qualifying Pace", "Low fuel, maximum attack simulation runs.", "24", "1" },
            new[] { "racePace", "Race Pace", "Heavy fuel race simulation with pit stop rehearsal.", "26", "1" },
        };

        static void ShowPracticePrograms()
        {
            var model = new PracticeProgramsModel();
            if (career != null && career.Save != null)
            {
                CalendarEventData current = career.CurrentEvent();
                model.summaryLine = "Round " + career.Save.currentRound
                    + (current != null ? " · " + current.displayName : "")
                    + " · Each program can be run once per round.";

                for (int i = 0; i < PracticeProgramDefs.Length; i++)
                {
                    string[] def = PracticeProgramDefs[i];
                    string key = "s" + career.Save.currentSeason + "_r" + career.Save.currentRound + "_" + def[0];
                    bool done = career.Save.completedPracticePrograms != null && career.Save.completedPracticePrograms.Contains(key);
                    string reward = "+" + def[3] + " RP" + (def[4] != "0" ? " +" + def[4] + " REP" : "");
                    model.programs.Add(new RndRow
                    {
                        id = def[0],
                        label = def[1],
                        detail = done ? ("COMPLETE · " + def[2]) : (reward + " · " + def[2]),
                        primaryLabel = "Run",
                        primaryShown = !done,
                        primaryEnabled = !done,
                    });
                }
            }

            shell.Router.Show(PracticeProgramsView.Id);
            practiceProgramsPresenter.Present(model);
        }

        // Running a program launches the gameplay practice session (which applies the
        // reward + marks it complete); hide the production shell exactly as the other
        // gameplay hand-offs do.
        static void RunPracticeProgram(string programId)
        {
            LeaveToLegacy(() => bootstrap.StartCareerPractice(programId));
        }

        // Production championship graph. Built from the AUTHORITATIVE career
        // progression via the engine-free ChampionshipChart geometry - no standings
        // are recomputed here.
        static void ShowChampionshipChart(bool constructors)
        {
            var model = new ChampionshipChartModel
            {
                constructorsMode = constructors,
                title = constructors ? "CONSTRUCTORS' CHAMPIONSHIP" : "DRIVERS' CHAMPIONSHIP",
                emptyMessage = "No completed rounds this season yet. Finish a race to build the graph.",
            };

            if (career != null && career.Save != null)
            {
                CareerManager.ChampionshipProgression prog = constructors
                    ? career.GetConstructorChampionshipProgression()
                    : career.GetDriverChampionshipProgression();

                if (prog != null)
                {
                    if (prog.roundLabels != null)
                    {
                        model.roundLabels.AddRange(prog.roundLabels);
                    }

                    int maxPoints = 0;
                    for (int s = 0; s < prog.series.Count; s++)
                    {
                        List<int> cp = prog.series[s].cumulativePoints;
                        if (cp != null && cp.Count > 0)
                        {
                            maxPoints = Mathf.Max(maxPoints, cp[cp.Count - 1]);
                        }
                    }

                    int axisMax = F1Game.Core.ChampionshipChart.AxisMax(maxPoints);
                    model.yTicks = F1Game.Core.ChampionshipChart.AxisTicks(axisMax, 4);
                    int roundCount = prog.rounds != null ? prog.rounds.Count : 0;

                    for (int s = 0; s < prog.series.Count; s++)
                    {
                        CareerManager.ChampionshipSeries cs = prog.series[s];
                        var sm = new ChampionshipSeriesModel
                        {
                            label = cs.label,
                            isPlayer = cs.isPlayer || cs.isPlayerTeam,
                            color = ResolveSeriesColor(cs, s),
                        };

                        List<F1Game.Core.ChampionshipChart.Point> norm =
                            F1Game.Core.ChampionshipChart.NormalizeSeries(cs.cumulativePoints, roundCount, axisMax);
                        for (int i = 0; i < norm.Count; i++)
                        {
                            sm.points.Add(new Vector2(norm[i].X, norm[i].Y));
                        }

                        int finalPts = cs.cumulativePoints != null && cs.cumulativePoints.Count > 0
                            ? cs.cumulativePoints[cs.cumulativePoints.Count - 1] : 0;
                        sm.finalValue = finalPts + " pts";
                        model.series.Add(sm);
                    }
                }
            }

            shell.Router.Show(ChampionshipChartView.Id);
            championshipChartPresenter.Present(model);
        }

        static Color ResolveSeriesColor(CareerManager.ChampionshipSeries cs, int index)
        {
            TeamData team = FindTeamStrict(cs.id);
            if (team == null && data != null && data.Drivers != null)
            {
                DriverData driver = data.Drivers.drivers.Find(x => x.id == cs.id);
                if (driver != null)
                {
                    team = FindTeamStrict(driver.teamId);
                }
            }

            if (team != null)
            {
                return team.PrimaryUnityColor;
            }

            // Fallback: a distinct generated hue so no two lines coincide.
            F1Game.Core.LiveryColor lc = F1Game.Core.LiveryGenerator.Generate(index, 20).Primary;
            return new Color(lc.R01, lc.G01, lc.B01, 1f);
        }

        static TeamData FindTeamStrict(string id)
        {
            if (data == null || data.Teams == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            return data.Teams.teams.Find(t => t != null && t.id == id);
        }

        static string ResolveTrackName(string trackId)
        {
            if (data != null && data.Calendar != null)
            {
                for (int e = 0; e < data.Calendar.events.Count; e++)
                {
                    if (data.Calendar.events[e].trackId == trackId)
                    {
                        return data.Calendar.events[e].displayName;
                    }
                }
            }

            return trackId;
        }

        /// <summary>
        /// Production post-race classification. Returns true when the production
        /// screen was shown (caller must NOT run the legacy results path);
        /// false when production UI is not the active frontend, so the legacy
        /// results screen remains the compatibility fallback. The action buttons
        /// reuse the exact bootstrap hooks the legacy results screen used.
        /// </summary>
        public static bool TryShowResults(List<RaceResultEntry> results, bool careerRace, string debriefLine = null)
        {
            // Uses the bridge state adopted when the frontend/race was started;
            // no fresh owner args (RaceManager does not hold GameBootstrap).
            if (!Enabled || results == null || shell == null || bootstrap == null || resultsPresenter == null)
            {
                return false;
            }

            try
            {
                // Append the engineer debrief (from the live telemetry capture) as
                // a second, smaller subtitle line when the race supplied one.
                string subtitle = ResultsSubtitle();
                if (!string.IsNullOrEmpty(debriefLine))
                {
                    subtitle = (string.IsNullOrEmpty(subtitle) ? "" : subtitle + "\n") + "<size=80%>" + debriefLine + "</size>";
                }

                var model = new ResultsModel
                {
                    title = "RACE RESULT",
                    subtitle = subtitle,
                    isCareer = careerRace,
                    primaryActionLabel = careerRace ? "Continue Career" : "Race Again",
                };

                float winnerTime = results.Count > 0 ? results[0].totalTime + results[0].penaltiesSeconds : 0f;
                for (int i = 0; i < results.Count; i++)
                {
                    RaceResultEntry e = results[i];
                    bool dnf = !string.IsNullOrEmpty(e.penaltyReason) && e.penaltyReason.Contains("DNF");
                    float classifiedTime = e.totalTime + e.penaltiesSeconds;
                    string gap = dnf ? "DNF"
                        : (i == 0 ? FormatLapTime(classifiedTime) : "+" + (classifiedTime - winnerTime).ToString("0.0") + "s");
                    TeamData team = data != null ? data.FindTeam(e.teamId) : null;
                    model.rows.Add(new ResultRowModel
                    {
                        position = e.finishingPosition > 0 ? e.finishingPosition : i + 1,
                        code = string.IsNullOrEmpty(e.driverName) ? "---" : e.driverName,
                        team = team != null ? team.name : "",
                        gapText = gap,
                        bestLapText = FormatLapTime(e.bestLapTime),
                        pitStops = e.pitStops,
                        points = e.points,
                        penaltyText = dnf ? e.penaltyReason
                            : (e.penaltiesSeconds > 0f ? "+" + e.penaltiesSeconds.ToString("0") + "s" : "--"),
                        isPlayer = e.isPlayer,
                        dnf = dnf,
                    });
                }

                resultsPresenter.OnPrimary = careerRace
                    ? (Action)(() => LeaveToLegacy(() => bootstrap.ShowCareer()))
                    : () => LeaveToLegacy(() => bootstrap.StartQuickRace());
                resultsPresenter.OnMenu = () => LeaveToLegacy(() => bootstrap.ShowMainMenu());

                // Show the results screen on the (visible) shell, replacing the
                // HUD; unlock frontend nav so its buttons work; enter Results.
                UiShell.NavigationLocked = false;
                shell.SetShellVisible(true);
                shell.Modals.CloseAll();
                shell.Router.ResetStack();
                shell.Router.Show(ResultsView.Id);
                resultsPresenter.Present(model);
                UiSessionCoordinator.EnterResults();
                return true;
            }
            catch (Exception exception)
            {
                Fail("results", exception);
                return false;
            }
        }

        /// <summary>
        /// Show the production pause overlay above the HUD (the legacy pause
        /// panel does not exist when the production HUD is active). Returns true
        /// when shown; false leaves pause handling to the legacy path.
        /// </summary>
        public static bool ShowPauseMenu(RaceManager race)
        {
            if (!Enabled || shell == null || bootstrap == null || race == null)
            {
                return false;
            }

            try
            {
                EnsurePauseOverlay();
                if (pauseOverlay == null)
                {
                    return false;
                }

                string session = race.IsTimeTrial ? "Time Trial"
                    : (race.CurrentSession == RaceWeekendSession.Qualifying ? "Qualifying"
                    : (race.CurrentSession == RaceWeekendSession.Practice ? "Practice" : "Race"));
                string eventLabel = race.EventData == null ? "" : "  ·  " + race.EventData.displayName;
                bool isPractice = race.CurrentSession == RaceWeekendSession.Practice;
                pauseOverlay.Configure(session + eventLabel, isPractice);

                pauseOverlay.OnResume = () => race.Resume();
                pauseOverlay.OnEndPractice = () => { HidePauseMenu(); bootstrap.EndPracticeSession(); };
                pauseOverlay.OnMainMenu = () =>
                {
                    HidePauseMenu();
                    // A time trial has no finish line; leaving to the menu with a
                    // valid lap shows a production result summary first, otherwise
                    // (or if production UI is off) it goes straight to the menu.
                    if (!TryShowTimeTrialResult(race))
                    {
                        bootstrap.ShowMainMenu();
                    }
                };
                pauseOverlay.OnRestart = () => { HidePauseMenu(); race.RestartRace(); };
                pauseOverlay.OnQuit = () => Application.Quit();

                pauseOverlay.SetVisible(true);
                return true;
            }
            catch (Exception exception)
            {
                Fail("pause overlay", exception);
                return false;
            }
        }

        public static void HidePauseMenu()
        {
            if (pauseOverlay != null)
            {
                pauseOverlay.SetVisible(false);
            }
        }

        static void EnsurePauseOverlay()
        {
            if (pauseOverlay != null || shell == null || shell.ModalLayer == null)
            {
                return;
            }

            pauseOverlay = UiScreenFactory.BuildPauseOverlay(shell.ModalLayer);
        }

        static string FormatLapTime(float seconds)
        {
            if (seconds <= 0f)
            {
                return "-:--.---";
            }

            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            return minutes + ":" + rest.ToString("00.000");
        }

        // Event/lap context line for the results screens, from the same bridge
        // state used to start the session (career next-event or the quick-race
        // selection).
        static string ResultsSubtitle()
        {
            CalendarEventData eventData = selectedEvent;
            if (eventData == null && career != null && career.Save != null)
            {
                eventData = career.CurrentEvent();
            }

            if (eventData == null)
            {
                return "";
            }

            int laps = settings != null ? settings.Current.laps : eventData.laps5;
            return eventData.displayName + "  ·  " + laps + " laps";
        }

        /// <summary>
        /// Production qualifying classification (career only; quick-race
        /// qualifying stays legacy). Same TryShow/legacy-fallback contract as
        /// TryShowResults, reusing the Results screen in its compact variant.
        /// </summary>
        public static bool TryShowQualifyingResults(List<QualifyingResultEntry> results, bool careerRace)
        {
            if (!Enabled || !careerRace || results == null || shell == null || bootstrap == null || resultsPresenter == null)
            {
                return false;
            }

            try
            {
                var model = new ResultsModel
                {
                    title = "QUALIFYING RESULT",
                    subtitle = ResultsSubtitle(),
                    isCareer = true,
                    showRaceColumns = false,
                    primaryActionLabel = "Continue to Race",
                };

                for (int i = 0; i < results.Count; i++)
                {
                    QualifyingResultEntry e = results[i];
                    TeamData team = data != null ? data.FindTeam(e.teamId) : null;
                    string tag = !string.IsNullOrEmpty(e.eliminatedIn) ? "OUT " + e.eliminatedIn
                        : (e.invalidated ? "LAP DELETED" : "--");
                    model.rows.Add(new ResultRowModel
                    {
                        position = e.position > 0 ? e.position : i + 1,
                        code = string.IsNullOrEmpty(e.driverName) ? "---" : e.driverName,
                        team = team != null ? team.name : "",
                        gapText = FormatLapTime(e.bestLapTime),
                        penaltyText = tag,
                        isPlayer = e.isPlayer,
                        dnf = !string.IsNullOrEmpty(e.eliminatedIn),
                    });
                }

                resultsPresenter.OnPrimary = () => LeaveToLegacy(() => bootstrap.StartCareerRace());
                resultsPresenter.OnMenu = () => LeaveToLegacy(() => bootstrap.ShowMainMenu());

                UiShell.NavigationLocked = false;
                shell.SetShellVisible(true);
                shell.Modals.CloseAll();
                shell.Router.ResetStack();
                shell.Router.Show(ResultsView.Id);
                resultsPresenter.Present(model);
                UiSessionCoordinator.EnterResults();
                return true;
            }
            catch (Exception exception)
            {
                Fail("qualifying results", exception);
                return false;
            }
        }

        /// <summary>
        /// Production time-trial result summary, shown when a time-trial player
        /// leaves to the menu with a valid lap. Reuses the compact Results screen
        /// (like qualifying). Returns true when shown (caller must NOT also run the
        /// straight-to-menu path); false when production UI is off, it isn't a time
        /// trial, or no valid lap was set - the caller then goes straight to menu.
        /// </summary>
        public static bool TryShowTimeTrialResult(RaceManager race)
        {
            if (!Enabled || race == null || !race.IsTimeTrial || shell == null || bootstrap == null || resultsPresenter == null)
            {
                return false;
            }

            RaceParticipant player = race.PlayerParticipant;
            if (player == null || player.lapTracker == null)
            {
                return false;
            }

            float sessionBest = player.lapTracker.BestLapTime;
            if (sessionBest <= 0f)
            {
                // No clean lap this session - nothing to celebrate; fall back to
                // the ordinary straight-to-menu exit.
                return false;
            }

            try
            {
                string trackId = race.EventData != null ? race.EventData.trackId : null;
                float record = string.IsNullOrEmpty(trackId) ? 0f : PlayerRecordsStore.GetBestLap(trackId);
                bool holdsRecord = record <= 0f || sessionBest <= record + 0.0005f;

                var model = new ResultsModel
                {
                    title = "TIME TRIAL RESULT",
                    subtitle = ResultsSubtitle(),
                    isCareer = false,
                    showRaceColumns = false,
                    primaryActionLabel = "Try Again",
                };

                TeamData team = data != null ? data.FindTeam(player.teamId) : null;
                model.rows.Add(new ResultRowModel
                {
                    position = 1,
                    code = string.IsNullOrEmpty(player.driverName) ? "YOU" : player.driverName,
                    team = team != null ? team.name : "",
                    gapText = FormatLapTime(sessionBest),
                    penaltyText = holdsRecord ? "TRACK RECORD" : "SESSION BEST",
                    isPlayer = true,
                });

                // Show the standing record too when the player didn't beat it.
                if (record > 0f && !holdsRecord)
                {
                    model.rows.Add(new ResultRowModel
                    {
                        position = 2,
                        code = "Track Record",
                        team = "",
                        gapText = FormatLapTime(record),
                        penaltyText = "--",
                    });
                }

                // "Try Again" returns to time-trial setup; "Main Menu" exits.
                resultsPresenter.OnPrimary = () => LeaveToLegacy(() => bootstrap.ShowTimeTrialSetup());
                resultsPresenter.OnMenu = () => LeaveToLegacy(() => bootstrap.ShowMainMenu());

                UiShell.NavigationLocked = false;
                shell.SetShellVisible(true);
                shell.Modals.CloseAll();
                shell.Router.ResetStack();
                shell.Router.Show(ResultsView.Id);
                resultsPresenter.Present(model);
                UiSessionCoordinator.EnterResults();
                return true;
            }
            catch (Exception exception)
            {
                Fail("time trial result", exception);
                return false;
            }
        }

        static void ShowDriverProfile()
        {
            var model = new DriverProfileModel();
            if (career != null && career.Save != null)
            {
                model.driverName = career.Save.playerDriverName;
                TeamData team = data != null ? data.FindTeam(career.Save.playerTeamId) : null;
                model.teamLine = (team != null ? team.name : "") + " · Season " + career.Save.currentSeason;
            }

            // Career-wide records are account-level (the local records store),
            // matching how the legacy profile presents them.
            PlayerRecordsData records = PlayerRecordsStore.Data;
            if (records != null)
            {
                model.stats.Add(new ProfileStatModel { label = "Races finished", value = records.racesFinished.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Wins", value = records.raceWins.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Podiums", value = records.podiums.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Pole positions", value = records.polePositions.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Fastest laps", value = records.fastestLaps.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Career points", value = records.totalPoints.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Drivers' titles", value = records.championshipsWon.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Constructors' titles", value = records.constructorsChampionshipsWon.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Seasons completed", value = records.completedSeasons.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Clean races", value = records.cleanRaces.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Best clean streak", value = records.bestCleanRaceStreak.ToString() });
                model.stats.Add(new ProfileStatModel { label = "Most overtakes in a race", value = records.mostOvertakesInRace.ToString() });
            }

            shell.Router.Show(DriverProfileView.Id);
            profilePresenter.Present(model);
        }

        static void ShowCareerStandings()
        {
            var model = new CareerStandingsModel();
            if (career != null && career.Save != null)
            {
                int rounds = data != null && data.Calendar != null ? data.Calendar.events.Count : 0;
                model.seasonLabel = string.Format("Season {0} · Round {1}{2}",
                    career.Save.currentSeason,
                    career.Save.currentRound + 1,
                    rounds > 0 ? "/" + rounds : "");

                if (career.Save.driverStandings != null)
                {
                    for (int i = 0; i < career.Save.driverStandings.Count; i++)
                    {
                        StandingEntry entry = career.Save.driverStandings[i];
                        TeamData team = data != null ? data.FindTeam(entry.teamId) : null;
                        model.drivers.Add(new StandingsRowModel
                        {
                            position = i + 1,
                            name = entry.displayName,
                            detail = team != null ? team.name : "",
                            points = entry.points,
                            wins = entry.wins,
                            isPlayer = entry.displayName == career.Save.playerDriverName,
                        });
                    }
                }

                if (career.Save.constructorStandings != null)
                {
                    for (int i = 0; i < career.Save.constructorStandings.Count; i++)
                    {
                        StandingEntry entry = career.Save.constructorStandings[i];
                        model.teams.Add(new StandingsRowModel
                        {
                            position = i + 1,
                            name = entry.displayName,
                            detail = "",
                            points = entry.points,
                            wins = entry.wins,
                            isPlayer = entry.id == career.Save.playerTeamId,
                        });
                    }
                }

                if (data != null && data.Calendar != null)
                {
                    for (int i = 0; i < data.Calendar.events.Count; i++)
                    {
                        CalendarEventData raceEvent = data.Calendar.events[i];
                        model.calendar.Add(new CalendarRowModel
                        {
                            round = i + 1,
                            trackName = raceEvent.displayName,
                            country = raceEvent.country,
                            laps = settings != null ? settings.Current.laps : raceEvent.laps5,
                            isDone = i < career.Save.currentRound,
                            isNext = i == career.Save.currentRound,
                        });
                    }
                }
            }

            shell.Router.Show(CareerStandingsView.Id);
            standingsPresenter.Present(model);
        }

        static readonly string[] DifficultyNames = { "Easy", "Medium", "Hard", "Expert" };
        static readonly string[] ErsModeNames = { "Balanced", "Attack", "Harvest" };
        static readonly string[] ColorBlindNames = { "Off", "Protanopia", "Deuteranopia", "Tritanopia" };

        static void ShowSettings()
        {
            shell.Router.Show(SettingsView.Id);
            settingsPresenter.Present(BuildSettingsModel());
        }

        // Flip one setting, persist it, and re-present so the summary + toggle
        // labels refresh - the same flip/Save/refresh cycle the legacy settings
        // controls use. No-op if the store is missing.
        static void ToggleSetting(Action<GameSettingsData> flip)
        {
            if (settings == null || settings.Current == null || flip == null)
            {
                return;
            }

            flip(settings.Current);
            settings.Save();
            ApplyAccessibilityToShell();
            settingsPresenter.Present(BuildSettingsModel());
        }

        // Pushes accessibility settings that the production shell honours directly.
        // Reduced motion (from the UI Animations toggle) makes TransitionService
        // complete every screen/panel transition instantly.
        static void ApplyAccessibilityToShell()
        {
            if (shell == null || shell.Transitions == null || settings == null || settings.Current == null)
            {
                return;
            }

            shell.Transitions.ReducedMotion = !settings.Current.uiAnimations;
            // Drive the colour-vision-accessibility palette from the player's setting.
            AccessibilityColors.SetMode(settings.Current.colorBlindMode);
        }

        static SettingsModel BuildSettingsModel()
        {
            var model = new SettingsModel();
            GameSettingsData s = settings != null ? settings.Current : null;
            if (s == null)
            {
                return model;
            }

            Heading(model, F1Game.Core.Localization.Get("settings.heading.gameplay", "GAMEPLAY"));
            Row(model, "Race Laps", s.laps.ToString());
            Row(model, "Difficulty", Pick(DifficultyNames, s.difficultyIndex));
            Row(model, "AI Opponents", s.aiOpponentCount.ToString());
            Row(model, "Default Tyre", string.IsNullOrEmpty(s.tyreCompound) ? "Medium" : s.tyreCompound);
            Row(model, "ERS Strategy", Pick(ErsModeNames, s.ersMode));

            Heading(model, F1Game.Core.Localization.Get("settings.heading.driving", "DRIVING"));
            Row(model, "Manual Gears", OnOff(s.manualGears));
            Row(model, "ABS", OnOff(s.absAssist));
            Row(model, "Traction Control", OnOff(s.tractionControl));
            Row(model, "Racing Line", OnOff(s.racingLineAssist));
            Row(model, "Auto Brake Assist", OnOff(s.autoBrakeAssist));
            Row(model, "Steering Sensitivity", Multiplier(s.steeringSensitivity));

            Heading(model, F1Game.Core.Localization.Get("settings.heading.audio", "AUDIO"));
            Row(model, "Sound Effects", OnOff(s.audioEnabled));
            Row(model, "Master Volume", Percent(s.masterVolume));
            Row(model, "Engine Volume", Percent(s.engineVolume));
            Row(model, "Radio Volume", Percent(s.radioVolume));

            Heading(model, F1Game.Core.Localization.Get("settings.heading.display", "DISPLAY & ACCESSIBILITY"));
            Row(model, "HUD Scale", Multiplier(s.hudScale));
            Row(model, "Compact HUD", OnOff(s.compactHud));
            Row(model, "UI Animations", OnOff(s.uiAnimations));
            Row(model, "Camera Shake", OnOff(s.cameraShake));
            Row(model, "Speed Units", s.useMphUnits ? "MPH" : "KPH");
            Row(model, "Graphics Quality", GraphicsQualityName(s.graphicsQuality));
            Row(model, "Colour-Vision Mode", Pick(ColorBlindNames, s.colorBlindMode));

            model.difficultyToggleLabel = "Difficulty: " + Pick(DifficultyNames, s.difficultyIndex);
            model.ersToggleLabel = "ERS: " + Pick(ErsModeNames, s.ersMode);
            model.manualGearsToggleLabel = "Gears: " + (s.manualGears ? "Manual" : "Auto");
            model.unitsToggleLabel = "Units: " + (s.useMphUnits ? "MPH" : "KPH");
            model.cameraShakeToggleLabel = "Camera Shake: " + OnOff(s.cameraShake);
            model.compactHudToggleLabel = "Compact HUD: " + OnOff(s.compactHud);
            model.uiAnimationsToggleLabel = "UI Animations: " + OnOff(s.uiAnimations);

            // Full production editor rows (production-first; the kill switch or a
            // field-build failure degrades to the summary + quick toggles + Classic
            // Settings, which stays as the emergency legacy editor).
            if (ProductionSettingsEditorEnabled)
            {
                try
                {
                    BuildEditorFields(model, s);
                }
                catch (Exception)
                {
                    model.fields.Clear();
                }
            }

            return model;
        }

        /// <summary>
        /// Production-first (within the already-active production UI): the complete
        /// inline settings editor is the default; PlayerPrefs=0 is the emergency kill
        /// switch, and a field-build failure degrades to the summary + Classic (legacy)
        /// editor automatically.
        /// </summary>
        public const string ProductionSettingsEditorKey = "f1game_production_settings_editor";

        public static bool ProductionSettingsEditorEnabled => PlayerPrefs.GetInt(ProductionSettingsEditorKey, 1) != 0;

        static readonly string[] CompoundNames = { "Soft", "Medium", "Hard", "Intermediate", "Wet" };

        // Every editable setting as an interactive field: id, label, value, and whether
        // each adjust control applies. Ids are matched in AdjustSettingField.
        static void BuildEditorFields(SettingsModel model, GameSettingsData s)
        {
            Numeric(model, "laps", "Race Laps", s.laps.ToString(), s.laps, 3, 99);
            Cycle(model, "difficulty", "Difficulty", Pick(DifficultyNames, s.difficultyIndex));
            Cycle(model, "tyre", "Default Tyre", string.IsNullOrEmpty(s.tyreCompound) ? "Medium" : s.tyreCompound);
            Cycle(model, "ers", "ERS Strategy", Pick(ErsModeNames, s.ersMode));

            Toggle(model, "manualGears", "Manual Gears", OnOff(s.manualGears));
            Toggle(model, "abs", "ABS", OnOff(s.absAssist));
            Toggle(model, "tc", "Traction Control", OnOff(s.tractionControl));
            Toggle(model, "racingLine", "Racing Line", OnOff(s.racingLineAssist));
            Toggle(model, "autoBrake", "Auto Brake Assist", OnOff(s.autoBrakeAssist));
            NumericF(model, "steering", "Steering Sensitivity", Multiplier(s.steeringSensitivity), s.steeringSensitivity, 0.45f, 1.65f);

            Toggle(model, "sfx", "Sound Effects", OnOff(s.audioEnabled));
            NumericF(model, "master", "Master Volume", Percent(s.masterVolume), s.masterVolume, 0f, 1f);
            NumericF(model, "engine", "Engine Volume", Percent(s.engineVolume), s.engineVolume, 0f, 1f);
            NumericF(model, "radio", "Radio Volume", Percent(s.radioVolume), s.radioVolume, 0f, 1f);

            NumericF(model, "hudScale", "HUD Scale", Multiplier(s.hudScale), s.hudScale, 0.75f, 1.3f);
            Toggle(model, "compactHud", "Compact HUD", OnOff(s.compactHud));
            Toggle(model, "uiAnimations", "UI Animations", OnOff(s.uiAnimations));
            Toggle(model, "cameraShake", "Camera Shake", OnOff(s.cameraShake));
            Toggle(model, "units", "Speed Units", s.useMphUnits ? "MPH" : "KPH");
            Cycle(model, "graphics", "Graphics Quality", GraphicsQualityName(s.graphicsQuality));
            Cycle(model, "colorBlind", "Colour-Vision Mode", Pick(ColorBlindNames, s.colorBlindMode));
        }

        static void Field(SettingsModel model, string id, string label, string value, bool canDec, bool canInc)
        {
            string key = "settings.row." + label.ToLowerInvariant().Replace(" ", "_");
            model.fields.Add(new SettingsFieldModel
            {
                id = id,
                label = F1Game.Core.Localization.Get(key, label),
                value = value,
                canDecrement = canDec,
                canIncrement = canInc,
            });
        }

        // Toggles and cycles: both controls always apply (flip / wrap).
        static void Toggle(SettingsModel model, string id, string label, string value) =>
            Field(model, id, label, value, true, true);

        static void Cycle(SettingsModel model, string id, string label, string value) =>
            Field(model, id, label, value, true, true);

        // Integer stepper: a control disables at its bound.
        static void Numeric(SettingsModel model, string id, string label, string value, int current, int min, int max) =>
            Field(model, id, label, value, current > min, current < max);

        // Float stepper: a control disables within an epsilon of its bound.
        static void NumericF(SettingsModel model, string id, string label, string value, float current, float min, float max) =>
            Field(model, id, label, value, current > min + 1e-4f, current < max - 1e-4f);

        // Apply an editor adjustment: mutate the matching setting by the direction,
        // then persist + re-present via ToggleSetting (the single settings-write path).
        static void AdjustSettingField(string id, int direction)
        {
            ToggleSetting(s =>
            {
                switch (id)
                {
                    case "laps": s.laps = ClampInt(s.laps + direction, 3, 99); break;
                    case "difficulty": s.difficultyIndex = Wrap(s.difficultyIndex + direction, DifficultyNames.Length); break;
                    case "tyre": s.tyreCompound = CompoundNames[Wrap(CompoundIndex(s.tyreCompound) + direction, CompoundNames.Length)]; break;
                    case "ers": s.ersMode = Wrap(s.ersMode + direction, ErsModeNames.Length); break;
                    case "manualGears": s.manualGears = !s.manualGears; break;
                    case "abs": s.absAssist = !s.absAssist; break;
                    case "tc": s.tractionControl = !s.tractionControl; break;
                    case "racingLine": s.racingLineAssist = !s.racingLineAssist; break;
                    case "autoBrake": s.autoBrakeAssist = !s.autoBrakeAssist; break;
                    case "steering": s.steeringSensitivity = ClampFloat(s.steeringSensitivity + direction * 0.05f, 0.45f, 1.65f); break;
                    case "sfx": s.audioEnabled = !s.audioEnabled; break;
                    case "master": s.masterVolume = ClampFloat(s.masterVolume + direction * 0.05f, 0f, 1f); break;
                    case "engine": s.engineVolume = ClampFloat(s.engineVolume + direction * 0.05f, 0f, 1f); break;
                    case "radio": s.radioVolume = ClampFloat(s.radioVolume + direction * 0.05f, 0f, 1f); break;
                    case "hudScale": s.hudScale = ClampFloat(s.hudScale + direction * 0.05f, 0.75f, 1.3f); break;
                    case "compactHud": s.compactHud = !s.compactHud; break;
                    case "uiAnimations": s.uiAnimations = !s.uiAnimations; break;
                    case "cameraShake": s.cameraShake = !s.cameraShake; break;
                    case "units": s.useMphUnits = !s.useMphUnits; break;
                    case "graphics": s.graphicsQuality = Wrap(s.graphicsQuality + direction, 4); break;
                    case "colorBlind": s.colorBlindMode = Wrap(s.colorBlindMode + direction, ColorBlindNames.Length); break;
                }
            });
        }

        static int CompoundIndex(string compound)
        {
            for (int i = 0; i < CompoundNames.Length; i++)
            {
                if (CompoundNames[i] == compound)
                {
                    return i;
                }
            }

            return 1; // default Medium
        }

        static int ClampInt(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        static float ClampFloat(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        static int Wrap(int v, int count) => count <= 0 ? 0 : ((v % count) + count) % count;

        static void Heading(SettingsModel model, string label)
        {
            model.rows.Add(new SettingsRowModel { label = label, isHeading = true });
        }

        static void Row(SettingsModel model, string label, string value)
        {
            // Localize the row label by a key derived from its English text
            // (e.g. "Race Laps" → settings.row.race_laps); the English text is the
            // fallback, so untranslated builds read exactly as before.
            string key = "settings.row." + label.ToLowerInvariant().Replace(" ", "_");
            model.rows.Add(new SettingsRowModel { label = F1Game.Core.Localization.Get(key, label), value = value });
        }

        static string Pick(string[] names, int index)
        {
            return index >= 0 && index < names.Length ? names[index] : index.ToString();
        }

        static string OnOff(bool value)
        {
            return value ? "On" : "Off";
        }

        static string Percent(float value01)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f) + "%";
        }

        static string Multiplier(float value)
        {
            return value.ToString("0.0") + "x";
        }

        static string GraphicsQualityName(int quality)
        {
            switch (quality)
            {
                case 0: return "Low";
                case 1: return "Medium";
                case 2: return "High";
                default: return "Ultra";
            }
        }

        static void ShowTrackSelect()
        {
            var models = new List<TrackCardModel>();
            if (data != null && data.Calendar != null)
            {
                foreach (CalendarEventData raceEvent in data.Calendar.events)
                {
                    models.Add(new TrackCardModel
                    {
                        eventId = raceEvent.trackId,
                        trackName = raceEvent.displayName,
                        location = raceEvent.country,
                        laps = settings != null ? settings.Current.laps : raceEvent.laps5,
                        weatherHint = string.IsNullOrEmpty(raceEvent.weatherProfile) ? "Variable" : raceEvent.weatherProfile,
                    });
                }
            }

            // Aurora Park: the authored-definition circuit. Quick-race only (it
            // is not a championship round), so it lives here rather than in the
            // calendar data.
            models.Add(new TrackCardModel
            {
                eventId = F1Game.Track.ReferenceTrackGenerator.ReferenceTrackId,
                trackName = "Aurora Park (Authored)",
                location = "Fictional",
                laps = settings != null ? settings.Current.laps : 5,
                weatherHint = "Variable",
            });

            shell.Router.Show(TrackSelectView.Id);
            trackSelectPresenter.Present(models);
        }

        static void OnTrackChosen(TrackCardModel model)
        {
            selectedEvent = null;
            if (data != null && data.Calendar != null)
            {
                selectedEvent = data.Calendar.events.Find(evt => evt.trackId == model.eventId);
            }

            if (selectedEvent == null && model.eventId == F1Game.Track.ReferenceTrackGenerator.ReferenceTrackId)
            {
                // Synthesized event for the authored circuit (not a calendar
                // round): TrackManager routes this id to the authored layout.
                selectedEvent = new CalendarEventData
                {
                    round = 0,
                    trackId = model.eventId,
                    displayName = model.trackName,
                    country = model.location,
                    laps3 = 3,
                    laps5 = 5,
                    laps25Percent = 12,
                    weatherProfile = "mixed",
                };
            }

            // Time trial skips the pit-strategy step (auto-tyres, no stops) and
            // hands straight off to the legacy BeginTimeTrial with the chosen
            // event, which owns the session-start transition.
            if (timeTrialFlow)
            {
                CalendarEventData ttEvent = selectedEvent;
                LeaveToLegacy(() => bootstrap.BeginTimeTrial(ttEvent));
                return;
            }

            var strategyModel = new StrategyModel
            {
                trackName = model.trackName,
                raceLaps = model.laps,
                weatherForecast = model.weatherHint,
                selectedCompoundIndex = CompoundIndex(settings.Current.tyreCompound, 1),
                plannedStopCount = Mathf.Clamp(settings.Current.plannedStopCount, 1, 2),
                plannedPitLapOne = settings.Current.plannedPitLapOne,
                plannedPitLapTwo = settings.Current.plannedPitLapTwo,
                stopOneCompoundIndex = CompoundIndex(settings.Current.plannedStopOneCompound, 2),
                stopTwoCompoundIndex = CompoundIndex(settings.Current.plannedStopTwoCompound, 1),
            };

            shell.Router.Show(PreRaceStrategyView.Id);
            strategyPresenter.Present(strategyModel);
        }

        /// <summary>
        /// Atomic Start-Race transition (fixes the strategy-screen-stuck bug):
        /// single-flight guarded, disables the button, saves the plan and event
        /// once, tears down transient frontend UI, hands off to the race, and
        /// restores a coherent frontend on failure. Repeated submit / Back /
        /// controller-East cannot re-enter this while it runs.
        /// </summary>
        static void OnStrategyConfirmed(StrategyChoice choice)
        {
            if (!UiSessionCoordinator.TryBeginSessionStart())
            {
                return; // a start is already in flight — ignore the duplicate.
            }

            // Lock frontend Back navigation for the duration of the session.
            UiShell.NavigationLocked = true;

            // Disable the Start button immediately so a second press is a no-op.
            if (strategyPresenter != null)
            {
                strategyPresenter.SetStartInteractable(false);
            }

            try
            {
                // Save the strategy exactly once.
                settings.Current.tyreCompound = CompoundNames[Mathf.Clamp(choice.StartCompoundIndex, 0, CompoundNames.Length - 1)];
                settings.Current.plannedStopCount = choice.PlannedStopCount;
                settings.Current.plannedPitLapOne = choice.PlannedPitLapOne;
                settings.Current.plannedPitLapTwo = choice.PlannedPitLapTwo;
                settings.Current.plannedStopOneCompound = CompoundNames[Mathf.Clamp(choice.StopOneCompoundIndex, 0, CompoundNames.Length - 1)];
                settings.Current.plannedStopTwoCompound = CompoundNames[Mathf.Clamp(choice.StopTwoCompoundIndex, 0, CompoundNames.Length - 1)];
                settings.Save();

                // Set the selected event exactly once.
                bootstrap.Ui.SetQuickRaceSelectedEvent(selectedEvent);

                // Tear down transient frontend UI and hide the strategy screen
                // BEFORE race init so it can never linger into the live session.
                shell.Modals.CloseAll();
                shell.Router.ResetStack();
                shell.SetShellVisible(false);

                // Start the race exactly once. RaceManager.StartSession then shows
                // exactly one HUD (production HudRoot here, via ProductionSessionUi)
                // and the coordinator moves to LiveSession.
                bootstrap.BeginQuickRace();
            }
            catch (Exception exception)
            {
                // Restore one coherent frontend state and re-enable the button.
                UiSessionCoordinator.FailToFrontend();
                UiShell.NavigationLocked = false;
                if (strategyPresenter != null)
                {
                    strategyPresenter.SetStartInteractable(true);
                }

                shell.SetShellVisible(true);
                shell.Router.Show(PreRaceStrategyView.Id);
                Fail("strategy start", exception);
            }
        }

        static void LeaveToLegacy(Action legacyAction)
        {
            shell.SetShellVisible(false);
            legacyAction();
        }

        static void Fail(string where, Exception exception)
        {
            failedThisSession = true;
            if (shell != null)
            {
                shell.SetShellVisible(false);
            }

            Debug.LogError(DiagnosticLog.FormatError(DiagnosticCode.HudBindFailed, "Production UI failed in " + where + ", falling back to legacy UI for this session: " + exception));
        }
    }
}
