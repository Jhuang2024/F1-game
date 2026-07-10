using System;
using System.Collections.Generic;
using F1Game.UI;
using F1Game.UI.Screens;
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

        static UiShell shell;
        static MainMenuPresenter mainMenuPresenter;
        static TrackSelectPresenter trackSelectPresenter;
        static PreRaceStrategyPresenter strategyPresenter;
        static bool failedThisSession;
        static CalendarEventData selectedEvent;

        static GameBootstrap bootstrap;
        static GameDataRepository data;
        static CareerManager career;
        static GameSettingsStore settings;

        static bool Enabled
        {
            get
            {
                if (failedThisSession)
                {
                    return false;
                }

                int toggle = PlayerPrefs.GetInt(ToggleKey, -1);
                if (toggle == 0)
                {
                    return false;
                }

                if (toggle == 1)
                {
                    return true;
                }

                return UiShell.TextPipelineReady();
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
                OnCareer = () => LeaveToLegacy(() => bootstrap.ShowCareer()),
                OnQuickRace = ShowTrackSelect,
                OnTimeTrial = () => LeaveToLegacy(() => bootstrap.ShowTimeTrialSetup()),
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
                selectedCompoundIndex = Mathf.Clamp(settings.Current.tyreCompound, 0, 4),
                plannedStopCount = Mathf.Clamp(settings.Current.plannedStopCount, 1, 2),
                plannedPitLapOne = settings.Current.plannedPitLapOne,
                plannedPitLapTwo = settings.Current.plannedPitLapTwo,
                stopOneCompoundIndex = Mathf.Clamp(settings.Current.plannedStopOneCompound, 0, 4),
                stopTwoCompoundIndex = Mathf.Clamp(settings.Current.plannedStopTwoCompound, 0, 4),
            };

            shell.Router.Show(PreRaceStrategyView.Id);
            strategyPresenter.Present(strategyModel);
        }

        static void OnStrategyConfirmed(StrategyChoice choice)
        {
            settings.Current.tyreCompound = choice.StartCompoundIndex;
            settings.Current.plannedStopCount = choice.PlannedStopCount;
            settings.Current.plannedPitLapOne = choice.PlannedPitLapOne;
            settings.Current.plannedPitLapTwo = choice.PlannedPitLapTwo;
            settings.Current.plannedStopOneCompound = choice.StopOneCompoundIndex;
            settings.Current.plannedStopTwoCompound = choice.StopTwoCompoundIndex;
            settings.Save();

            bootstrap.Ui.SetQuickRaceSelectedEvent(selectedEvent);
            LeaveToLegacy(() => bootstrap.BeginQuickRace());
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
