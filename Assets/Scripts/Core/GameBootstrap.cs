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
            SimpleAudioManager.Ensure(transform);
            SimpleAudioManager.ApplySettings(settings.Current);
            UiFactory.AnimationsEnabled = settings.Current.uiAnimations;
        }

        void Start()
        {
            ShowMainMenu();
        }

        public void ShowMainMenu()
        {
            if (raceManager != null)
            {
                raceManager.CleanupRaceWorld();
            }

            SimpleAudioManager.ApplySettings(settings.Current);
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
            if (!career.HasQualifyingForCurrentRound())
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
