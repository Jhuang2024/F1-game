using System;
using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Race.Rules;
using F1Game.UI;
using F1Game.UI.Screens;
using F1Game.UI.Screens.CareerHub;
using F1Game.UI.Screens.CareerStandings;
using F1Game.UI.Screens.DriverProfile;
using F1Game.UI.Screens.MainMenu;
using F1Game.UI.Screens.RaceHudShell;
using F1Game.UI.Screens.Results;
using F1Game.UI.Screens.PreRaceStrategy;
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
        static CareerHubPresenter careerHubPresenter;
        static DriverProfilePresenter profilePresenter;
        static ResultsPresenter resultsPresenter;
        static PauseOverlay pauseOverlay;
        static bool failedThisSession;
        static CalendarEventData selectedEvent;

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

            var mainMenuView = (MainMenuView)ShowAndGet(MainMenuView.Id);
            mainMenuPresenter = new MainMenuPresenter(mainMenuView)
            {
                // Production career hub when a career exists; career creation
                // (and everything the hub doesn't cover yet) stays legacy.
                OnCareer = () =>
                {
                    if (career != null && career.Save != null)
                    {
                        ShowCareerHub();
                    }
                    else
                    {
                        LeaveToLegacy(() => bootstrap.ShowCareer());
                    }
                },
                OnQuickRace = ShowTrackSelect,
                OnTimeTrial = () => LeaveToLegacy(() => bootstrap.ShowTimeTrialSetup()),
                OnStandings = ShowCareerStandings,
                OnSettings = () => LeaveToLegacy(() => bootstrap.Ui.ShowSettings(data, career, settings)),
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
                OnLegacyMenu = () => LeaveToLegacy(() => bootstrap.ShowCareer()),
                OnBack = () => shell.Router.Back(),
            };

            var profileView = (DriverProfileView)ShowAndGet(DriverProfileView.Id);
            profilePresenter = new DriverProfilePresenter(profileView)
            {
                OnBack = () => shell.Router.Back(),
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
        /// Production post-race classification. Returns true when the production
        /// screen was shown (caller must NOT run the legacy results path);
        /// false when production UI is not the active frontend, so the legacy
        /// results screen remains the compatibility fallback. The action buttons
        /// reuse the exact bootstrap hooks the legacy results screen used.
        /// </summary>
        public static bool TryShowResults(List<RaceResultEntry> results, bool careerRace)
        {
            // Uses the bridge state adopted when the frontend/race was started;
            // no fresh owner args (RaceManager does not hold GameBootstrap).
            if (!Enabled || results == null || shell == null || bootstrap == null || resultsPresenter == null)
            {
                return false;
            }

            try
            {
                var model = new ResultsModel
                {
                    title = "RACE RESULT",
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
                pauseOverlay.OnMainMenu = () => { HidePauseMenu(); bootstrap.ShowMainMenu(); };
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
