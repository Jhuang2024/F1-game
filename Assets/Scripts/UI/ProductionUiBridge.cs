using System;
using System.Collections.Generic;
using F1Game.Race.Rules;
using F1Game.UI;
using F1Game.UI.Screens;
using F1Game.UI.Screens.CareerHub;
using F1Game.UI.Screens.CareerStandings;
using F1Game.UI.Screens.MainMenu;
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
                OnLegacyMenu = () => LeaveToLegacy(() => bootstrap.ShowCareer()),
                OnBack = () => shell.Router.Back(),
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
            }

            shell.Router.Show(CareerHubView.Id);
            careerHubPresenter.Present(model);
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

            Debug.LogError("[ProductionUI] Failed in " + where + ", falling back to legacy UI for this session: " + exception);
        }
    }
}
