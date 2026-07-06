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
            SimpleAudioManager.SetEnabled(settings.Current.audioEnabled);
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

            SimpleAudioManager.SetEnabled(settings.Current.audioEnabled);
            ui.ShowMainMenu(data, career, settings);
        }

        public void ShowCareer()
        {
            if (raceManager != null)
            {
                raceManager.CleanupRaceWorld();
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
            ui.ShowRaceTyreSelect(data, career, settings, false);
        }

        public void BeginQuickRace()
        {
            CalendarEventData raceEvent = data.Calendar.events.Count > 0 ? data.Calendar.events[0] : career.CurrentEvent();
            SimpleAudioManager.SetEnabled(settings.Current.audioEnabled);
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
            SimpleAudioManager.SetEnabled(settings.Current.audioEnabled);
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
            SimpleAudioManager.SetEnabled(settings.Current.audioEnabled);
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
            SimpleAudioManager.SetEnabled(settings.Current.audioEnabled);
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
