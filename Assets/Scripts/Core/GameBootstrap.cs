using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    public class GameBootstrap : MonoBehaviour
    {
        GameDataRepository data;
        CareerManager career;
        GameSettingsStore settings;
        RuntimeUi ui;
        RaceManager raceManager;

        /// <summary>Exposed for ProductionUiBridge (new UI shell) interop.</summary>
        public RuntimeUi Ui => ui;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateBootstrap()
        {
            if (FindObjectOfType<GameBootstrap>() != null)
            {
                return;
            }

            GameObject root = new GameObject("Local Formula Racing Bootstrap");
            root.AddComponent<GameBootstrap>();
            DontDestroyOnLoad(root);
        }

        void Awake()
        {
            if (FindObjectsOfType<GameBootstrap>().Length > 1)
            {
                Destroy(gameObject);
                return;
            }

            data = GameDataRepository.Load();
            settings = new GameSettingsStore();
            settings.Load();
            career = new CareerManager(data);
            ui = gameObject.AddComponent<RuntimeUi>();
            ui.Initialize(this);
            raceManager = gameObject.AddComponent<RaceManager>();
            // Interim typed-event publisher for flags/weather/laps/session state
            // (see RaceEventRelay) - feeds the event-driven HUD/audio layers.
            gameObject.AddComponent<RaceEventRelay>().Attach(raceManager);
            SimpleAudioManager.Ensure(transform);
            SimpleAudioManager.ApplySettings(settings.Current);
            // Event-driven authored-bank audio (race control, penalties, pit,
            // weather, radio). Empty bank slots defer to the generated cues.
            F1Game.Audio.RaceAudioDirector.Create(transform);
            UiFactory.AnimationsEnabled = settings.Current.uiAnimations;
            // Load a translation table if one is selected and present; defaults to
            // "en" (English source text, no table) so this is a no-op out of the box.
            F1Game.Core.LocalizationLoader.LoadLanguage(PlayerPrefs.GetString("f1game_language", "en"));
        }

        void Start()
        {
            ShowMainMenu();
        }

        void Update()
        {
            // F10 kicks off a fixed-window performance capture (frame-time
            // percentiles + GC/memory → JSON next to the saves) for before/after
            // pipeline comparisons. Dev diagnostics only; a no-op unless pressed.
            if (Input.GetKeyDown(KeyCode.F10))
            {
                string label = raceManager != null && raceManager.CanDrive ? "race" : "frontend";
                F1Game.Core.Diagnostics.PerformanceCapture.Begin(label, 20f);
            }
        }

        public void ShowMainMenu()
        {
            if (raceManager != null)
            {
                raceManager.CleanupRaceWorld();
            }

            // Return-to-frontend: clear any live-session UI ownership so the shell
            // navigates cleanly (nav unlocked, HUD hidden, state = Frontend).
            ProductionSessionUi.EndSession();

            SimpleAudioManager.ApplySettings(settings.Current);

            // Production-UI vertical slice: prefab/TMP main menu when available;
            // falls back to the legacy runtime-built menu automatically.
            if (ProductionUiBridge.TryShowMainMenu(this, data, career, settings))
            {
                return;
            }

            ui.ShowMainMenu(data, career, settings);
        }

        public void ShowCareer()
        {
            if (raceManager != null)
            {
                raceManager.CleanupRaceWorld();
            }

            // Part 21: every path back to the Career Hub goes through here, so
            // this is the one choke point that needs to know about a pending
            // season transition - whether the player just clicked "Continue
            // Career" off the final race's report, or reopened the app mid-
            // flow after quitting. Routing to the season-complete screen
            // instead of straight to the hub means the flow always resumes
            // exactly where it left off rather than silently dropping the
            // player back into ordinary hub navigation with the season never
            // properly wrapped up.
            if (career.Save.seasonTransitionPending)
            {
                ui.ShowSeasonComplete(data, career, settings);
                return;
            }

            ui.ShowCareerHub(data, career, settings);
        }

        public void ShowRaceWeekend()
        {
            if (raceManager != null)
            {
                raceManager.CleanupRaceWorld();
            }

            ui.ShowRaceWeekend(data, career, settings);
        }

        public void StartQuickRace()
        {
            // Production-UI vertical slice: track select + strategy screens in
            // the new shell when available, legacy flow otherwise.
            if (ProductionUiBridge.TryShowQuickRaceFlow(this, data, career, settings))
            {
                return;
            }

            // Track selection is now the first step of Quick Race instead of
            // jumping straight into tyre select against a hardcoded track - see
            // RuntimeUi.ShowQuickRaceTrackSelect / quickRaceSelectedEvent.
            ui.ShowQuickRaceTrackSelect(data, career, settings);
        }

        public void ShowTimeTrialSetup()
        {
            if (raceManager != null)
            {
                raceManager.CleanupRaceWorld();
            }

            // ShowTimeTrialSetup and ShowTrackInfo used to be two separate,
            // near-duplicate "browse every track" screens. They're merged into
            // one (RuntimeUi.ShowTrackInfo) - this entry point is kept so the
            // Main Menu's "Time Trial" card doesn't need to change.
            ui.ShowTrackInfo(data, career, settings);
        }

        public void ShowTrackInfo()
        {
            if (raceManager != null)
            {
                raceManager.CleanupRaceWorld();
            }

            ui.ShowTrackInfo(data, career, settings);
        }

        public void BeginTimeTrial(CalendarEventData raceEvent)
        {
            if (raceEvent == null)
            {
                raceEvent = data.Calendar.events.Count > 0 ? data.Calendar.events[0] : career.CurrentEvent();
            }

            SimpleAudioManager.ApplySettings(settings.Current);
            raceManager.StartTimeTrial(
                data,
                career,
                settings,
                ui,
                raceEvent,
                career.Save.playerDriverName,
                career.Save.playerTeamId);
        }

        public void BeginQuickRace()
        {
            // Was always data.Calendar.events[0] regardless of what the player
            // picked on ShowQuickRaceTrackSelect - use that selection now, falling
            // back to the old default only if the track-select screen was somehow
            // skipped (e.g. a stale save/shortcut).
            CalendarEventData raceEvent = ui.QuickRaceSelectedEvent != null
                ? ui.QuickRaceSelectedEvent
                : (data.Calendar.events.Count > 0 ? data.Calendar.events[0] : career.CurrentEvent());
            SimpleAudioManager.ApplySettings(settings.Current);
            raceManager.StartRace(
                data,
                career,
                settings,
                ui,
                raceEvent,
                career.Save.playerDriverName,
                career.Save.playerTeamId,
                false);
        }

        public void StartCareerQualifying()
        {
            raceManager.PrepareNewQualifyingWeekend();
            ui.ShowQualifyingTyreSelect(data, career, settings, 1);
        }

        public void StartCareerSimQualifying()
        {
            raceManager.PrepareNewQualifyingWeekend();
            ui.ShowQualifyingTyreSelect(data, career, settings, 1, true);
        }

        public void BeginCareerQualifying()
        {
            CalendarEventData raceEvent = career.CurrentEvent();
            SimpleAudioManager.ApplySettings(settings.Current);
            raceManager.StartSession(
                data,
                career,
                settings,
                ui,
                raceEvent,
                career.Save.playerDriverName,
                career.Save.playerTeamId,
                true,
                RaceWeekendSession.Qualifying);
        }

        // Playable practice programs: drives an actual free-running session
        // instead of instantly awarding the reward - RaceManager.EvaluatePracticeSession
        // scores it against programId's criteria once the player ends the
        // session from the pause menu (see RuntimeUi.BuildPausePanel).
        public void StartCareerPractice(string programId)
        {
            CalendarEventData raceEvent = career.CurrentEvent();
            SimpleAudioManager.ApplySettings(settings.Current);
            raceManager.StartSession(
                data,
                career,
                settings,
                ui,
                raceEvent,
                career.Save.playerDriverName,
                career.Save.playerTeamId,
                true,
                RaceWeekendSession.Practice);
            raceManager.ActivePracticeProgramId = programId;
        }

        // Called from the pause menu's "End Practice Session" button (only shown
        // during a Practice session) - scores the session BEFORE tearing down the
        // race world, since evaluation needs the still-live PlayerParticipant.
        public void EndPracticeSession()
        {
            RaceManager.PracticeSessionResult result = raceManager.EvaluatePracticeSession();
            raceManager.CleanupRaceWorld();
            ui.ShowPracticeSessionResult(data, career, settings, result);
        }

        public void SimulateCareerQualifying()
        {
            CalendarEventData raceEvent = career.CurrentEvent();
            SimpleAudioManager.ApplySettings(settings.Current);
            raceManager.SimulateQualifyingWeekend(
                data,
                career,
                settings,
                ui,
                raceEvent,
                career.Save.playerDriverName,
                career.Save.playerTeamId,
                true);
        }

        public void StartCareerRace()
        {
            // Weekend routing lives in the extracted rulebook: no stored
            // qualifying result for the round means the weekend goes to
            // qualifying first, otherwise straight to the race.
            if (SessionFlow.NextCareerStep(career.HasQualifyingForCurrentRound()) == WeekendSession.Qualifying)
            {
                StartCareerQualifying();
                return;
            }

            ui.ShowRaceTyreSelect(data, career, settings, true);
        }

        public void BeginCareerRace()
        {
            CalendarEventData raceEvent = career.CurrentEvent();
            SimpleAudioManager.ApplySettings(settings.Current);
            raceManager.StartSession(
                data,
                career,
                settings,
                ui,
                raceEvent,
                career.Save.playerDriverName,
                career.Save.playerTeamId,
                true,
                RaceWeekendSession.Race);
        }
    }
}
