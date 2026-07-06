using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    public class RaceManager : MonoBehaviour
    {
        public GameDataRepository Data { get; private set; }
        public CareerManager Career { get; private set; }
        public GameSettingsStore Settings { get; private set; }
        public TrackRuntime Track { get; private set; }
        public CalendarEventData EventData { get; private set; }
        public RaceParticipant PlayerParticipant { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsRaceFinished { get; private set; }
        public bool IsCareerRace { get; private set; }
        public bool IsTimeTrial { get; private set; }
        public RaceWeekendSession CurrentSession { get; private set; }
        public float StartCountdown { get; private set; }
        public bool CanDrive { get { return StartCountdown <= 0f && !IsPaused && !IsRaceFinished && !qualifyingTransitionPending; } }
        public string SessionMessage { get; private set; }
        public string QualifyingFeedbackText { get; private set; }
        public string EngineerMessageText { get { return engineerMessageTimer > 0f ? engineerMessageText : ""; } }
        public string RaceStartReactionText { get { return reactionDisplayTimer > 0f && playerReactionTime >= 0f ? "RT " + playerReactionTime.ToString("0.000") + "s" : ""; } }
        public bool LastQualifyingResultWasSimulated { get { return lastQualifyingResultWasSimulated; } }
        public int RaceStartLightCount
        {
            get
            {
                if (CurrentSession == RaceWeekendSession.Qualifying || StartCountdown <= 0f)
                {
                    return 0;
                }

                float elapsed = Mathf.Max(0f, raceStartSequenceDuration - StartCountdown);
                if (elapsed < 0.55f)
                {
                    return 0;
                }

                return Mathf.Clamp(Mathf.FloorToInt((elapsed - 0.55f) / 0.48f) + 1, 0, 5);
            }
        }

        public bool RaceStartLightsVisible
        {
            get { return CurrentSession != RaceWeekendSession.Qualifying && !IsTimeTrial && StartCountdown > 0f; }
        }

        public RaceStateManager State { get; private set; }
        public List<RaceParticipant> Participants { get { return State != null ? State.Participants : emptyParticipants; } }

        RuntimeUi ui;
        readonly List<RaceParticipant> emptyParticipants = new List<RaceParticipant>();
        GameObject raceWorld;
        float raceStartTime;
        int finishedCount;
        List<QualifyingResultEntry> lastQualifyingResults = new List<QualifyingResultEntry>();
        List<QualifyingSimEntry> qualifyingEntries = new List<QualifyingSimEntry>();
        readonly string[] playerSectorColors = new string[3];
        readonly float[] playerQualifyingBestTimes = new float[3];
        readonly float[,] playerQualifyingBestSectors = new float[3, 3];
        int qualifyingPhase = 1;
        int recordedPlayerValidLapCount;
        float qualifyingTransitionTimer;
        bool qualifyingTransitionPending;
        bool qualifyingTransitionFinish;
        bool lastQualifyingResultWasSimulated;
        float raceStartSequenceDuration = 4.2f;
        bool preserveQualifyingState;
        string engineerMessageText = "";
        float engineerMessageTimer;
        float engineerCooldown;
        float lightsOutTime;
        float playerReactionTime = -1f;
        float reactionDisplayTimer;
        bool waitingForPlayerReaction;
        int lastEngineerPitLapPrompt = -1;
        bool engineerWeatherSent;
        bool engineerPitRequestConfirmed;
        bool engineerTyreWarningSent;
        bool engineerBatteryWarningSent;
        bool engineerFinalLapSent;
        bool engineerFuelWarningSent;
        bool engineerDamageWarningSent;
        float lastRecordedPlayerBestLap;
        bool pendingTimeTrial;
        float playerResetCooldown;
        static PhysicMaterial carBodyPhysicsMaterial;
        const int FullWeekendDriverCount = 22;
        const int FullWeekendAiCount = FullWeekendDriverCount - 1;
        const int Q1SurvivorCount = 16;
        const int Q2SurvivorCount = 10;
        const string SectorPurple = "#B86CFF";
        const string SectorGreen = "#63FF82";
        const string SectorYellow = "#FFD45C";

        public void StartRace(
            GameDataRepository repository,
            CareerManager career,
            GameSettingsStore settings,
            RuntimeUi runtimeUi,
            CalendarEventData eventData,
            string playerName,
            string playerTeamId,
            bool careerRace)
        {
            StartSession(repository, career, settings, runtimeUi, eventData, playerName, playerTeamId, careerRace, RaceWeekendSession.QuickRace);
        }

        public void StartTimeTrial(
            GameDataRepository repository,
            CareerManager career,
            GameSettingsStore settings,
            RuntimeUi runtimeUi,
            CalendarEventData eventData,
            string playerName,
            string playerTeamId)
        {
            pendingTimeTrial = true;
            StartSession(repository, career, settings, runtimeUi, eventData, playerName, playerTeamId, false, RaceWeekendSession.QuickRace);
        }

        public void StartSession(
            GameDataRepository repository,
            CareerManager career,
            GameSettingsStore settings,
            RuntimeUi runtimeUi,
            CalendarEventData eventData,
            string playerName,
            string playerTeamId,
            bool careerRace,
            RaceWeekendSession session)
        {
            Data = repository;
            Career = career;
            Settings = settings;
            ui = runtimeUi;
            EventData = eventData;
            IsCareerRace = careerRace;
            IsTimeTrial = pendingTimeTrial;
            pendingTimeTrial = false;
            CurrentSession = session;
            IsRaceFinished = false;
            IsPaused = false;
            qualifyingTransitionPending = false;
            qualifyingTransitionTimer = 0f;
            QualifyingFeedbackText = "";
            finishedCount = 0;
            lastQualifyingResultWasSimulated = false;
            lightsOutTime = 0f;
            playerReactionTime = -1f;
            reactionDisplayTimer = 0f;
            waitingForPlayerReaction = false;
            lastRecordedPlayerBestLap = 0f;
            playerResetCooldown = 0f;
            ResetEngineerState();
            if (session == RaceWeekendSession.Qualifying && !preserveQualifyingState)
            {
                qualifyingPhase = 1;
                qualifyingEntries.Clear();
                ResetPlayerQualifyingCaptures();
            }
            preserveQualifyingState = false;
            raceStartSequenceDuration = session == RaceWeekendSession.Qualifying || IsTimeTrial ? 1.5f : Random.Range(5.4f, 6.8f);
            StartCountdown = raceStartSequenceDuration;
            SessionMessage = session == RaceWeekendSession.Qualifying ? "Q" + qualifyingPhase + " out lap ready" : (IsTimeTrial ? "Time trial: set a lap" : "Race start");
            Time.timeScale = 1f;

            if (raceWorld != null)
            {
                Destroy(raceWorld);
            }

            raceWorld = new GameObject("Runtime race world");
            CreateLighting();

            State = new GameObject("Race State Manager").AddComponent<RaceStateManager>();
            State.transform.SetParent(raceWorld.transform);
            State.Initialize(session, qualifyingPhase);

            TrackManager trackManager = new GameObject("Track Manager").AddComponent<TrackManager>();
            trackManager.transform.SetParent(raceWorld.transform);
            trackManager.sceneryDensity = Settings.Current.sceneryDensity;
            Track = trackManager.Build(eventData, Settings.Current.racingLineAssist);
            if (session == RaceWeekendSession.Qualifying)
            {
                ResetQualifyingSectorState();
                ResetPlayerQualifyingPhaseCapture(qualifyingPhase);
            }

            if (Track.roadCollider != null)
            {
                Physics.IgnoreLayerCollision(Track.roadCollider.gameObject.layer, 0, false);
            }

            SimpleAudioManager.SetRain(Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain);
            Debug.Log("[RoadPhysics] Race start roadColliderExists=" + (Track.roadCollider != null) +
                      " roadLayer=" + (Track.roadCollider == null ? "none" : LayerMask.LayerToName(Track.roadCollider.gameObject.layer)) +
                      " roadIsTrigger=" + (Track.roadCollider != null && Track.roadCollider.isTrigger) +
                      " roadCollidesWithDefaultCars=" + (Track.roadCollider != null && !Physics.GetIgnoreLayerCollision(Track.roadCollider.gameObject.layer, 0)));
            SpawnRaceGrid(playerName, playerTeamId, careerRace);
            PostEngineerMessage(OpeningEngineerMessage(), true);
            engineerWeatherSent = true;
            raceStartTime = Time.time + StartCountdown;
            ui.ShowRaceHud(this, PlayerParticipant);
            LogPlayerSpawnPhysics();
        }

        public void SimulateQualifyingWeekend(
            GameDataRepository repository,
            CareerManager career,
            GameSettingsStore settings,
            RuntimeUi runtimeUi,
            CalendarEventData eventData,
            string playerName,
            string playerTeamId,
            bool careerRace)
        {
            CleanupRaceWorld();
            Data = repository;
            Career = career;
            Settings = settings;
            ui = runtimeUi;
            EventData = eventData;
            IsCareerRace = careerRace;
            CurrentSession = RaceWeekendSession.Qualifying;
            IsRaceFinished = true;
            IsPaused = false;
            StartCountdown = 0f;
            raceStartTime = Time.time;
            qualifyingTransitionPending = false;
            qualifyingTransitionTimer = 0f;
            qualifyingTransitionFinish = true;
            QualifyingFeedbackText = "";
            SessionMessage = "Sim qualifying complete";
            lastQualifyingResultWasSimulated = true;
            qualifyingPhase = 1;
            qualifyingEntries.Clear();
            ResetPlayerQualifyingCaptures();
            ResetQualifyingSectorState();

            raceWorld = new GameObject("Runtime simulated qualifying world");
            TrackManager trackManager = new GameObject("Track Manager").AddComponent<TrackManager>();
            trackManager.transform.SetParent(raceWorld.transform);
            Track = trackManager.Build(eventData, false);
            SimpleAudioManager.SetRain(Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain);

            BuildSimulatedQualifyingField(playerName, playerTeamId);
            for (int phase = 1; phase <= 3; phase++)
            {
                qualifyingPhase = phase;
                ResetQualifyingSectorState();
                List<QualifyingSimEntry> active = ActiveQualifyingEntries(phase);
                for (int i = 0; i < active.Count; i++)
                {
                    if (GetQualifyingPhaseTime(active[i], phase) > 0f)
                    {
                        continue;
                    }

                    if (active[i].isPlayer)
                    {
                        SetSimulatedPlayerQualifyingPhaseTime(active[i], phase, SimulatePlayerQualifyingTime(active[i], phase));
                    }
                    else
                    {
                        SetAiQualifyingPhaseTime(active[i], phase, SimulateAiQualifyingTime(active[i], phase));
                    }
                }

                ApplyQualifyingElimination(active, phase);
            }

            List<QualifyingResultEntry> results = BuildFinalQualifyingResults();
            lastQualifyingResults = results;
            if (IsCareerRace && Career != null)
            {
                Career.ApplyQualifyingResults(EventData, results);
            }

            ui.ShowQualifyingResults(this, results, IsCareerRace);
        }

        void Update()
        {
            if (IsPaused || IsRaceFinished || Track == null)
            {
                return;
            }

            TickEngineerTimers();

            if (StartCountdown > 0f)
            {
                HoldGridCars(true);
                StartCountdown = Mathf.Max(0f, StartCountdown - Time.deltaTime);
                if (CurrentSession != RaceWeekendSession.Qualifying && RaceStartLightCount >= 5)
                {
                    SessionMessage = "Hold... lights out pending";
                }
                if (StartCountdown <= 0f)
                {
                    HoldGridCars(false);
                    SessionMessage = CurrentSession == RaceWeekendSession.Qualifying ? "Q" + qualifyingPhase + " out lap: build tyre temp" : "Lights out";
                    raceStartTime = Time.time;
                    if (CurrentSession != RaceWeekendSession.Qualifying && !IsTimeTrial)
                    {
                        lightsOutTime = Time.time;
                        waitingForPlayerReaction = true;
                        playerReactionTime = -1f;
                    }
                }
                return;
            }

            if (qualifyingTransitionPending)
            {
                AnimateQualifyingReturnToPits();
                qualifyingTransitionTimer = Mathf.Max(0f, qualifyingTransitionTimer - Time.deltaTime);
                if (qualifyingTransitionTimer <= 0f)
                {
                    qualifyingTransitionPending = false;
                    QualifyingFeedbackText = "";
                    if (qualifyingTransitionFinish)
                    {
                        FinishQualifying();
                    }
                    else
                    {
                        qualifyingPhase++;
                        preserveQualifyingState = true;
                        CleanupRaceWorld();
                        ui.ShowQualifyingTyreSelect(Data, Career, Settings, qualifyingPhase);
                    }
                }

                return;
            }

            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant.lapTracker != null)
                {
                    participant.lapTracker.Tick();
                    if (State != null)
                    {
                        State.RefreshTimingSnapshot(participant);
                    }

                    UpdateSectorRecords(participant);
                    if (participant.isPlayer && CurrentSession == RaceWeekendSession.Qualifying)
                    {
                        CapturePlayerQualifyingBestLap(participant.lapTracker);
                        SessionMessage = QualifyingLapStatusText(participant.lapTracker);
                    }
                }

                HandleFallRespawn(participant);
                HandleTrackLimits(participant);
                HandlePitService(participant);
                HandleFinish(participant);
            }

            SortRunningOrder();
            UpdateRaceEngineer();
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                if (ShouldCompleteQualifyingRun())
                {
                    CompleteQualifyingRun();
                }
            }
            else if (PlayerParticipant != null && PlayerParticipant.finished)
            {
                FinishRace();
            }
        }

        string QualifyingLapStatusText(LapTracker lap)
        {
            if (lap == null)
            {
                return "Q" + qualifyingPhase;
            }

            if (lap.OutLapActive)
            {
                return "Q" + qualifyingPhase + " out lap";
            }

            if (lap.CurrentLapInvalidated)
            {
                return "Q" + qualifyingPhase + " timed lap invalid";
            }

            if (lap.CompletedLaps > 0 && lap.ValidLapsCompleted == 0)
            {
                return "Q" + qualifyingPhase + " second push lap";
            }

            if (lap.CompletedLaps > 0)
            {
                return "Q" + qualifyingPhase + " second push lap: improve";
            }

            return "Q" + qualifyingPhase + " push lap";
        }

        public void TogglePause()
        {
            if (IsRaceFinished)
            {
                return;
            }

            IsPaused = !IsPaused;
            Time.timeScale = IsPaused ? 0f : 1f;
            ui.SetPauseVisible(IsPaused);
        }

        public void Resume()
        {
            IsPaused = false;
            Time.timeScale = 1f;
            ui.SetPauseVisible(false);
        }

        public void RestartRace()
        {
            if (State != null)
            {
                State.ResetAllParticipants();
            }
            pendingTimeTrial = IsTimeTrial;
            StartSession(Data, Career, Settings, ui, EventData, Career.Save.playerDriverName, Career.Save.playerTeamId, IsCareerRace, CurrentSession);
        }

        public void CleanupRaceWorld()
        {
            Time.timeScale = 1f;
            IsPaused = false;
            IsRaceFinished = true;
            IsTimeTrial = false;
            State = null;
            PlayerParticipant = null;
            if (raceWorld != null)
            {
                Destroy(raceWorld);
                raceWorld = null;
            }

            SimpleAudioManager.SetRain(false);
        }

        public void PrepareNewQualifyingWeekend()
        {
            CleanupRaceWorld();
            qualifyingPhase = 1;
            qualifyingEntries.Clear();
            preserveQualifyingState = false;
            qualifyingTransitionPending = false;
            qualifyingTransitionFinish = false;
            qualifyingTransitionTimer = 0f;
            QualifyingFeedbackText = "";
            lastQualifyingResultWasSimulated = false;
            ResetPlayerQualifyingCaptures();
            ResetQualifyingSectorState();
        }

        public float RaceElapsed
        {
            get { return Mathf.Max(0f, Time.time - raceStartTime); }
        }

        public int RaceLaps
        {
            get { return IsTimeTrial ? 999 : Mathf.Max(3, Settings.Current.laps); }
        }

        public int RecommendedPitLap(RaceParticipant participant)
        {
            if (RaceLaps <= 3)
            {
                return 1;
            }

            int maxPitLap = Mathf.Max(1, RaceLaps - 1);
            float baseWindow = Track != null && (Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain) ? 0.46f : 0.55f;
            float tyreManagement = participant == null || participant.driverData == null ? 78f : participant.driverData.tyreManagement;
            float managementShift = Mathf.Lerp(-0.08f, 0.08f, Mathf.Clamp01(tyreManagement / 100f));
            int recommended = Mathf.RoundToInt(RaceLaps * (baseWindow + managementShift));
            return Mathf.Clamp(recommended, 1, maxPitLap);
        }

        void ResetEngineerState()
        {
            engineerMessageText = "";
            engineerMessageTimer = 0f;
            engineerCooldown = 0f;
            lastEngineerPitLapPrompt = -1;
            engineerWeatherSent = false;
            engineerPitRequestConfirmed = false;
            engineerTyreWarningSent = false;
            engineerBatteryWarningSent = false;
            engineerFinalLapSent = false;
            engineerFuelWarningSent = false;
            engineerDamageWarningSent = false;
        }

        void TickEngineerTimers()
        {
            engineerMessageTimer = Mathf.Max(0f, engineerMessageTimer - Time.deltaTime);
            engineerCooldown = Mathf.Max(0f, engineerCooldown - Time.deltaTime);
            reactionDisplayTimer = Mathf.Max(0f, reactionDisplayTimer - Time.deltaTime);
            playerResetCooldown = Mathf.Max(0f, playerResetCooldown - Time.deltaTime);
        }

        void PostEngineerMessage(string message, bool priority)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            string formatted = "ENGINEER: " + message;
            if (!priority && engineerCooldown > 0f && formatted == engineerMessageText)
            {
                return;
            }

            engineerMessageText = formatted;
            engineerMessageTimer = priority ? 7.5f : 5.5f;
            engineerCooldown = priority ? 1.2f : 5.5f;
        }

        string OpeningEngineerMessage()
        {
            string weather = Track == null ? "dry" : WeatherStateLabel(Track.weather);
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                return "Weather is " + weather + ". Out lap first, then push when the tyres are ready.";
            }

            if (IsTimeTrial)
            {
                float best = PlayerRecordsStore.GetBestLap(EventData == null ? "" : EventData.trackId);
                return best > 0f
                    ? "Time trial. Track record to beat: " + UiFactory.FormatTime(best) + "."
                    : "Time trial. No local record yet, set a benchmark lap.";
            }

            return "Weather is " + weather + ". Mandatory stop is active. Target window around lap " + RecommendedPitLap(PlayerParticipant) + ".";
        }

        string WeatherStateLabel(WeatherState weather)
        {
            if (weather == WeatherState.Cloudy)
            {
                return "cloudy";
            }

            if (weather == WeatherState.LightRain)
            {
                return "light rain";
            }

            if (weather == WeatherState.HeavyRain)
            {
                return "heavy rain";
            }

            return "dry";
        }

        void UpdateRaceEngineer()
        {
            if (PlayerParticipant == null || PlayerParticipant.vehicle == null || PlayerParticipant.lapTracker == null)
            {
                return;
            }

            VehicleController car = PlayerParticipant.vehicle;
            TrackPlayerBestLapRecord();
            if (!engineerWeatherSent)
            {
                engineerWeatherSent = true;
                PostEngineerMessage(OpeningEngineerMessage(), true);
                return;
            }

            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                if (car.Tyres.TemperatureStatus == "HOT" && !engineerTyreWarningSent)
                {
                    engineerTyreWarningSent = true;
                    PostEngineerMessage("Tyres are hot. Cool them before the next push lap.", false);
                }

                return;
            }

            if (IsTimeTrial)
            {
                return;
            }

            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            if (completedLaps == RaceLaps - 1 && !engineerFinalLapSent)
            {
                engineerFinalLapSent = true;
                PostEngineerMessage("Final lap. Bring it home, watch the tyres.", true);
                return;
            }

            if (car.FuelKg < 7f && !engineerFuelWarningSent)
            {
                engineerFuelWarningSent = true;
                PostEngineerMessage("Fuel is getting low. Lift and coast into the heavy braking zones.", false);
                return;
            }

            if (car.Damage.OverallPercent > 45f && !engineerDamageWarningSent)
            {
                engineerDamageWarningSent = true;
                PostEngineerMessage("We are seeing damage on the car. Consider a stop for repairs.", false);
                return;
            }
            int targetLap = RecommendedPitLap(PlayerParticipant);
            if (PlayerParticipant.pitStops == 0 && !PlayerParticipant.isPitting)
            {
                if (completedLaps >= targetLap && lastEngineerPitLapPrompt != completedLaps)
                {
                    lastEngineerPitLapPrompt = completedLaps;
                    PostEngineerMessage("Box this lap. Mandatory stop still required.", true);
                    return;
                }

                if (completedLaps == Mathf.Max(0, targetLap - 1) && lastEngineerPitLapPrompt != completedLaps)
                {
                    lastEngineerPitLapPrompt = completedLaps;
                    PostEngineerMessage("Pit window opens next lap. Think about the undercut.", false);
                    return;
                }
            }

            if (car.Tyres.WearPercent > 42f && !engineerTyreWarningSent)
            {
                engineerTyreWarningSent = true;
                PostEngineerMessage("Tyre wear is high. Avoid sliding and plan the stop.", false);
                return;
            }

            if (car.Tyres.TemperatureStatus == "HOT" && !engineerTyreWarningSent)
            {
                engineerTyreWarningSent = true;
                PostEngineerMessage("Tyres are overheating. Short-shift and reduce sliding.", false);
                return;
            }

            if (car.ErsBattery < 0.18f && !engineerBatteryWarningSent)
            {
                engineerBatteryWarningSent = true;
                PostEngineerMessage("Battery low. Harvest for a few corners.", false);
            }
        }

        void TrackPlayerBestLapRecord()
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null || EventData == null)
            {
                return;
            }

            float best = PlayerParticipant.lapTracker.BestLapTime;
            if (best <= 0f || Mathf.Approximately(best, lastRecordedPlayerBestLap))
            {
                return;
            }

            lastRecordedPlayerBestLap = best;
            string context = IsTimeTrial ? "Time Trial" : (CurrentSession == RaceWeekendSession.Qualifying ? "Qualifying" : "Race");
            if (PlayerRecordsStore.TryRecordLap(EventData.trackId, best, context))
            {
                PostEngineerMessage("New local track record: " + UiFactory.FormatTime(best) + "!", true);
            }
            else if (IsTimeTrial)
            {
                PostEngineerMessage("Personal best this session: " + UiFactory.FormatTime(best) + ".", false);
            }
        }

        // Stuck recovery: snap the player back to the last safe on-track pose.
        // Costs five seconds in competitive race sessions so it cannot be exploited.
        public void ResetPlayerToSafePose(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer || participant.vehicle == null || Track == null ||
                participant.isPitting || participant.pitPhase != PitPhase.None || playerResetCooldown > 0f || !CanDrive)
            {
                return;
            }

            playerResetCooldown = 5f;
            TrackProgress progress = participant.lapTracker != null
                ? participant.lapTracker.CurrentProgress
                : Track.GetProgress(participant.transform.position);

            Vector3 respawnPosition;
            Quaternion respawnRotation;
            if (participant.hasLastSafePosition)
            {
                respawnPosition = participant.lastSafePosition + Vector3.up * 0.35f;
                respawnRotation = participant.lastSafeRotation;
            }
            else
            {
                respawnPosition = progress.nearestPoint + Vector3.up * 0.45f;
                respawnRotation = Quaternion.LookRotation(progress.forward, Vector3.up);
            }

            Rigidbody body = participant.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = respawnPosition;
                body.rotation = respawnRotation;
            }

            participant.transform.position = respawnPosition;
            participant.transform.rotation = respawnRotation;
            participant.fallRespawnCooldown = 2f;

            if (participant.lapTracker != null)
            {
                participant.lapTracker.InvalidateCurrentLap();
            }

            if (CurrentSession != RaceWeekendSession.Qualifying && !IsTimeTrial)
            {
                AddPenalty(participant, 5f, "Car recovery");
                SessionMessage = "Car recovered: +5s";
                PostEngineerMessage("Car recovered to the track. Five second penalty added.", true);
            }
            else
            {
                SessionMessage = "Car recovered: lap invalidated";
                PostEngineerMessage("Car recovered. This lap will not count.", true);
            }
        }

        public string PitStatusText(RaceParticipant participant)
        {
            if (CurrentSession == RaceWeekendSession.Qualifying || participant == null)
            {
                return "";
            }

            if (IsTimeTrial && participant.pitPhase == PitPhase.None && !participant.isPitting)
            {
                return "";
            }

            if (participant.pitPhase == PitPhase.Entry)
            {
                return "PIT ENTRY  LIMITER 80";
            }

            if (participant.pitPhase == PitPhase.Service)
            {
                float elapsed = Mathf.Max(0f, participant.pitServiceDuration - participant.pitTimer);
                return "PIT STOP  " + elapsed.ToString("0.0") + "s / " + participant.pitServiceDuration.ToString("0.0") + "s  " + participant.nextPitCompound;
            }

            if (participant.pitPhase == PitPhase.Release)
            {
                return "PIT RELEASE  LIMITER 80";
            }

            if (participant.pitLimiterUntilExit)
            {
                return "PIT EXIT  LIMITER 80";
            }

            if (participant.pitTyreSelectionActive && participant.vehicle != null && participant.vehicle.PitRequested)
            {
                return "PIT TYRE " + participant.requestedPitCompound + "  1S 2M 3H 4I 5W";
            }

            if (participant.pitStops > 0)
            {
                return "MANDATORY STOP COMPLETE";
            }

            if (participant.vehicle != null && participant.vehicle.PitRequested)
            {
                return "PIT REQUEST QUEUED";
            }

            return "MANDATORY STOP REQUIRED";
        }

        public float PitStopProgress01(RaceParticipant participant)
        {
            if (participant == null)
            {
                return 0f;
            }

            if (participant.pitPhase == PitPhase.Entry)
            {
                return 0.12f;
            }

            if (participant.pitPhase == PitPhase.Release || participant.pitLimiterUntilExit)
            {
                return 1f;
            }

            if (participant.pitPhase != PitPhase.Service || participant.pitServiceDuration <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(1f - participant.pitTimer / participant.pitServiceDuration);
        }

        public int GetPosition(RaceParticipant participant)
        {
            if (CurrentSession == RaceWeekendSession.Qualifying && participant == PlayerParticipant)
            {
                return GetQualifyingPositionEstimate();
            }

            if (State == null || State.SortedOrder.Count == 0)
            {
                SortRunningOrder();
            }

            if (State == null) return Participants.Count;
            int index = State.SortedOrder.IndexOf(participant);
            return index < 0 ? Participants.Count : index + 1;
        }

        public int DisplayedEntrantCount
        {
            get
            {
                if (CurrentSession == RaceWeekendSession.Qualifying && qualifyingEntries.Count > 0)
                {
                    return ActiveQualifyingEntries(qualifyingPhase).Count;
                }

                return Participants.Count;
            }
        }

        public RaceParticipant FindCarAhead(RaceParticipant participant, float maxMeters)
        {
            if (participant == null || participant.lapTracker == null)
            {
                return null;
            }

            float self = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float bestDelta = maxMeters;
            RaceParticipant best = null;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == participant || other.lapTracker == null)
                {
                    continue;
                }

                float otherDistance = State == null ? other.lapTracker.TotalProgressDistance : State.GetProgressDistance(other);
                float delta = otherDistance - self;
                if (delta > 0f && delta < bestDelta)
                {
                    bestDelta = delta;
                    best = other;
                }
            }

            return best;
        }

        public RaceParticipant FindCarBehind(RaceParticipant participant, float maxMeters)
        {
            if (participant == null || participant.lapTracker == null)
            {
                return null;
            }

            float self = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float bestDelta = maxMeters;
            RaceParticipant best = null;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == participant || other.lapTracker == null)
                {
                    continue;
                }

                float otherDistance = State == null ? other.lapTracker.TotalProgressDistance : State.GetProgressDistance(other);
                float delta = self - otherDistance;
                if (delta > 0f && delta < bestDelta)
                {
                    bestDelta = delta;
                    best = other;
                }
            }

            return best;
        }

        public void ReportJumpStartIntent(RaceParticipant participant)
        {
            if (participant == null || participant.jumpStartPenaltyApplied || StartCountdown <= 0f || CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return;
            }

            participant.jumpStartPenaltyApplied = true;
            AddPenalty(participant, 5f, "Jump start");
            SessionMessage = participant.isPlayer ? "Jump start: +5s" : SessionMessage;
        }

        public void RecordPlayerLaunchInput(RaceParticipant participant, float throttle)
        {
            if (participant == null || !participant.isPlayer || !waitingForPlayerReaction || CurrentSession == RaceWeekendSession.Qualifying || lightsOutTime <= 0f || throttle < 0.12f)
            {
                return;
            }

            waitingForPlayerReaction = false;
            playerReactionTime = Mathf.Max(0f, Time.time - lightsOutTime);
            reactionDisplayTimer = 7f;
            SessionMessage = "Reaction " + playerReactionTime.ToString("0.000") + "s";
            PostEngineerMessage("Reaction time " + playerReactionTime.ToString("0.000") + " seconds.", true);
        }

        public void OpenPlayerPitTyreSelector(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer || CurrentSession == RaceWeekendSession.Qualifying || participant.vehicle == null || participant.isPitting)
            {
                return;
            }

            participant.pitTyreSelectionActive = true;
            participant.requestedPitCompound = participant.requestedPitCompoundSet ? participant.requestedPitCompound : NextPitCompound(participant);
            participant.requestedPitCompoundSet = true;
            SessionMessage = "Pit request: choose tyre 1-5";
            PostEngineerMessage("Pit request received. Select tyres: 1 Soft, 2 Medium, 3 Hard, 4 Intermediate, 5 Wet.", true);
        }

        public void SelectPlayerPitTyre(RaceParticipant participant, TyreCompound compound)
        {
            if (participant == null || !participant.isPlayer || CurrentSession == RaceWeekendSession.Qualifying)
            {
                return;
            }

            participant.requestedPitCompound = compound;
            participant.requestedPitCompoundSet = true;
            participant.pitTyreSelectionActive = participant.vehicle != null && participant.vehicle.PitRequested && !participant.isPitting;
            SessionMessage = "Pit tyre selected: " + compound;
            PostEngineerMessage("Pit tyres selected: " + compound + ".", true);
        }

        public bool IsDrsAvailable(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || Track == null)
            {
                return false;
            }

            if (!CanDrive || Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain)
            {
                return false;
            }

            TrackProgress progress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            if (!Track.IsInDrsZone(progress.normalized))
            {
                return false;
            }

            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return true;
            }

            if (participant.lapTracker.CompletedLaps < 2)
            {
                return false;
            }

            return GetIntervalToAheadSeconds(participant) <= 1f;
        }

        public string DrsStateText(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null || Track == null ||
                !Track.IsInDrsZone((State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant)).normalized))
            {
                return "UNAVAILABLE";
            }

            if (participant.vehicle.DrsActive)
            {
                return "ACTIVE";
            }

            return IsDrsAvailable(participant) ? "AVAILABLE" : "UNAVAILABLE";
        }

        public bool ShouldAiUseErs(RaceParticipant participant, float cornerSeverity)
        {
            if (participant == null || participant.vehicle == null)
            {
                return false;
            }

            if (cornerSeverity > 0.24f || participant.vehicle.ErsBattery < 0.18f)
            {
                return false;
            }

            float aheadInterval = GetIntervalToAheadSeconds(participant);
            RaceParticipant behind = FindCarBehind(participant, 70f);
            bool attacking = aheadInterval < 1.6f;
            bool defending = behind != null && participant.vehicle.ErsBattery > 0.32f;
            bool batteryHigh = participant.vehicle.ErsBattery > 0.78f;
            return attacking || defending || (batteryHigh && Random.value < 0.035f);
        }

        public float GetDifficultyPaceMultiplier()
        {
            RaceDifficulty difficulty = Settings.Difficulty;
            if (difficulty == RaceDifficulty.Easy)
            {
                return 0.9f;
            }

            if (difficulty == RaceDifficulty.Medium)
            {
                return 0.97f;
            }

            if (difficulty == RaceDifficulty.Hard)
            {
                return 1.03f;
            }

            return 1.08f;
        }

        public float GetDifficultyBrakeMargin()
        {
            RaceDifficulty difficulty = Settings.Difficulty;
            if (difficulty == RaceDifficulty.Easy)
            {
                return 1f;
            }

            if (difficulty == RaceDifficulty.Medium)
            {
                return 0.7f;
            }

            if (difficulty == RaceDifficulty.Hard)
            {
                return 0.38f;
            }

            return 0.18f;
        }

        public string GapAheadText(RaceParticipant participant)
        {
            RaceParticipant ahead = FindCarAhead(participant, 9999f);
            if (ahead == null || participant == null || participant.lapTracker == null)
            {
                return "--";
            }

            float aheadDistance = State == null ? ahead.lapTracker.TotalProgressDistance : State.GetProgressDistance(ahead);
            float selfDistance = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float deltaMeters = aheadDistance - selfDistance;
            float speed = Mathf.Max(18f, participant.vehicle == null ? 32f : participant.vehicle.CurrentSpeedKph / 3.6f);
            return (deltaMeters / speed).ToString("0.0") + "s";
        }

        public float GetIntervalToAheadSeconds(RaceParticipant participant)
        {
            RaceParticipant ahead = FindCarAhead(participant, 220f);
            if (ahead == null || participant == null || participant.lapTracker == null)
            {
                return 999f;
            }

            float aheadDistance = State == null ? ahead.lapTracker.TotalProgressDistance : State.GetProgressDistance(ahead);
            float participantDistance = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float deltaMeters = aheadDistance - participantDistance;
            float speed = Mathf.Max(24f, participant.vehicle == null ? 36f : Mathf.Abs(participant.vehicle.CurrentSpeedKph) / 3.6f);
            return Mathf.Max(0f, deltaMeters / speed);
        }

        public List<RaceParticipant> GetRunningOrderSnapshot()
        {
            SortRunningOrder();
            return State != null ? new List<RaceParticipant>(State.SortedOrder) : new List<RaceParticipant>();
        }

        public void RetireParticipant(RaceParticipant participant, string reason)
        {
            if (participant == null || participant.retired || participant.finished || CurrentSession == RaceWeekendSession.Qualifying || State == null)
            {
                return;
            }

            participant.retired = true;
            participant.retirementReason = string.IsNullOrEmpty(reason) ? "Damage" : reason;
            float retiredTime = RaceElapsed + 9999f + Mathf.Max(0f, RaceLaps - (participant.lapTracker == null ? 0 : participant.lapTracker.CompletedLaps)) * 120f;
            State.OnParticipantFinished(participant, retiredTime);
            if (string.IsNullOrEmpty(participant.penaltyReason))
            {
                participant.penaltyReason = "DNF " + participant.retirementReason;
            }
            else if (!participant.penaltyReason.Contains("DNF"))
            {
                participant.penaltyReason += ", DNF " + participant.retirementReason;
            }

            if (participant.vehicle != null)
            {
                participant.vehicle.SetCommand(new VehicleCommand { brake = 1f });
                participant.vehicle.SetGridHold(true);
            }

            participant.gameObject.SetActive(false);
            if (participant.isPlayer)
            {
                SessionMessage = "Retired: " + participant.retirementReason;
            }
        }

        public string GapToLeaderText(RaceParticipant participant)
        {
            SortRunningOrder();
            if (participant == null || State == null || State.SortedOrder.Count == 0 || State.SortedOrder[0] == participant)
            {
                return "LEADER";
            }

            RaceParticipant leader = State.SortedOrder[0];
            float leaderDistance = State.GetProgressDistance(leader);
            float participantDistance = State.GetProgressDistance(participant);
            float deltaMeters = leaderDistance - participantDistance;
            if (Track != null && deltaMeters >= Track.length * 0.92f)
            {
                int laps = Mathf.Max(1, Mathf.RoundToInt(deltaMeters / Mathf.Max(1f, Track.length)));
                return "+" + laps + "L";
            }

            float speed = Mathf.Max(24f, participant.vehicle == null ? 36f : Mathf.Abs(participant.vehicle.CurrentSpeedKph) / 3.6f);
            return "+" + (Mathf.Max(0f, deltaMeters) / speed).ToString("0.0") + "s";
        }

        public string IntervalAheadText(RaceParticipant participant)
        {
            SortRunningOrder();
            if (State == null) return "--";
            int index = State.SortedOrder.IndexOf(participant);
            if (index <= 0)
            {
                return "--";
            }

            RaceParticipant ahead = State.SortedOrder[index - 1];
            float aheadDistance = State.GetProgressDistance(ahead);
            float participantDistance = State.GetProgressDistance(participant);
            float deltaMeters = aheadDistance - participantDistance;
            if (Track != null && deltaMeters >= Track.length * 0.92f)
            {
                int laps = Mathf.Max(1, Mathf.RoundToInt(deltaMeters / Mathf.Max(1f, Track.length)));
                return "+" + laps + "L";
            }

            float speed = Mathf.Max(24f, participant.vehicle == null ? 36f : Mathf.Abs(participant.vehicle.CurrentSpeedKph) / 3.6f);
            return (Mathf.Max(0f, deltaMeters) / speed).ToString("0.0") + "s";
        }

        public string GapBehindText(RaceParticipant participant)
        {
            RaceParticipant behind = FindCarBehind(participant, 9999f);
            if (behind == null || participant == null || participant.lapTracker == null)
            {
                return "--";
            }

            float participantDistance = State == null ? participant.lapTracker.TotalProgressDistance : State.GetProgressDistance(participant);
            float behindDistance = State == null ? behind.lapTracker.TotalProgressDistance : State.GetProgressDistance(behind);
            float deltaMeters = participantDistance - behindDistance;
            float speed = Mathf.Max(18f, behind.vehicle == null ? 32f : behind.vehicle.CurrentSpeedKph / 3.6f);
            return (deltaMeters / speed).ToString("0.0") + "s";
        }

        public string BuildQualifyingTimingTowerText(RaceParticipant player)
        {
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            active.Sort((a, b) => GetDisplayQualifyingTime(a).CompareTo(GetDisplayQualifyingTime(b)));
            float pole = GetQualifyingPoleReferenceTime();
            string text = "Q" + qualifyingPhase + "  POS  DVR   BEST    GAP\n";
            int count = Mathf.Min(FullWeekendDriverCount, active.Count);
            for (int i = 0; i < count; i++)
            {
                QualifyingSimEntry entry = active[i];
                float time = GetDisplayQualifyingTime(entry);
                string marker = entry.isPlayer ? ">" : " ";
                string best = time >= 9998f ? "--:--.---" : UiFactory.FormatTime(time);
                string gap = time >= 9998f || pole <= 0f ? "--" : (Mathf.Abs(time - pole) < 0.001f ? "P1" : "+" + (time - pole).ToString("0.000"));
                text += marker + (i + 1).ToString("00") + "   " + DriverCode(entry.driverName) + "   " + best + "   " + gap + "\n";
            }

            return text;
        }

        public float GetQualifyingPoleReferenceTime()
        {
            if (CurrentSession != RaceWeekendSession.Qualifying)
            {
                return 0f;
            }

            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            float best = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                float time = GetDisplayQualifyingTime(active[i]);
                if (time > 0f && time < 9998f && time < best)
                {
                    best = time;
                }
            }

            return best == float.MaxValue ? 0f : best;
        }

        public void ReportSectorToState(RaceParticipant participant, int sector, float sectorTime, bool invalidated)
        {
            if (State != null)
            {
                State.OnSectorComplete(participant, sector, sectorTime, invalidated);
            }
        }

        public string QualifyingDeltaText(RaceParticipant participant)
        {
            if (CurrentSession != RaceWeekendSession.Qualifying || participant == null || participant.lapTracker == null)
            {
                return "--";
            }

            float pole = GetQualifyingPoleReferenceTime();
            if (pole <= 0f || participant.lapTracker.OutLapActive)
            {
                return "--";
            }

            TrackProgress currentProgress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            float progress = Mathf.Clamp(currentProgress.normalized, 0.02f, 0.995f);
            float reference = pole * progress;
            float delta = participant.lapTracker.CurrentLapTime - reference;
            string color = delta <= 0f ? "#6CFF8D" : "#FF6C6C";
            return "<color=" + color + ">" + (delta >= 0f ? "+" : "") + delta.ToString("0.000") + "</color>";
        }

        public string PlayerSectorText(int sector, float time)
        {
            if (time <= 0f)
            {
                return "--.---";
            }

            string formatted = UiFactory.FormatTime(time);
            if (CurrentSession != RaceWeekendSession.Qualifying || sector < 1 || sector > 3)
            {
                return formatted;
            }

            string color = playerSectorColors[sector - 1];
            return string.IsNullOrEmpty(color) ? formatted : "<color=" + color + ">" + formatted + "</color>";
        }

        public string LiveSectorText(float time)
        {
            if (time <= 0f)
            {
                return "--.---";
            }

            string formatted = UiFactory.FormatTime(time);
            return CurrentSession == RaceWeekendSession.Qualifying ? "<color=" + SectorYellow + ">" + formatted + "</color>" : formatted;
        }

        void ResetQualifyingSectorState()
        {
            if (State != null) State.Initialize(CurrentSession, qualifyingPhase);
            for (int i = 0; i < 3; i++)
            {
                playerSectorColors[i] = "";
            }
        }

        void ResetPlayerQualifyingCaptures()
        {
            for (int phase = 0; phase < playerQualifyingBestTimes.Length; phase++)
            {
                playerQualifyingBestTimes[phase] = 0f;
                for (int sector = 0; sector < 3; sector++)
                {
                    playerQualifyingBestSectors[phase, sector] = 0f;
                }
            }

            recordedPlayerValidLapCount = 0;
        }

        void ResetPlayerQualifyingPhaseCapture(int phase)
        {
            int index = Mathf.Clamp(phase, 1, 3) - 1;
            playerQualifyingBestTimes[index] = 0f;
            for (int sector = 0; sector < 3; sector++)
            {
                playerQualifyingBestSectors[index, sector] = 0f;
            }

            recordedPlayerValidLapCount = 0;
        }

        void CapturePlayerQualifyingBestLap(LapTracker lap)
        {
            if (lap == null || CurrentSession != RaceWeekendSession.Qualifying || qualifyingPhase < 1 || qualifyingPhase > 3)
            {
                return;
            }

            if (lap.ValidLapsCompleted <= recordedPlayerValidLapCount)
            {
                return;
            }

            recordedPlayerValidLapCount = lap.ValidLapsCompleted;
            if (lap.LastLapInvalidated || lap.LastLapTime <= 0f)
            {
                return;
            }

            int index = qualifyingPhase - 1;
            if (playerQualifyingBestTimes[index] <= 0f || lap.LastLapTime < playerQualifyingBestTimes[index])
            {
                playerQualifyingBestTimes[index] = lap.LastLapTime;
                playerQualifyingBestSectors[index, 0] = lap.LastSector1Time;
                playerQualifyingBestSectors[index, 1] = lap.LastSector2Time;
                playerQualifyingBestSectors[index, 2] = lap.LastSector3Time;
            }
        }

        bool ShouldCompleteQualifyingRun()
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null)
            {
                return false;
            }

            LapTracker lap = PlayerParticipant.lapTracker;
            if (RaceElapsed > 360f)
            {
                return true;
            }

            if (lap.ValidLapsCompleted > 0 && PlayerHoldsCurrentQualifyingPole())
            {
                return true;
            }

            return lap.CompletedLaps >= 2;
        }

        bool PlayerHoldsCurrentQualifyingPole()
        {
            if (qualifyingPhase < 1 || qualifyingPhase > 3)
            {
                return false;
            }

            int index = qualifyingPhase - 1;
            float playerTime = playerQualifyingBestTimes[index];
            if (playerTime <= 0f && PlayerParticipant != null && PlayerParticipant.lapTracker != null)
            {
                playerTime = PlayerParticipant.lapTracker.BestLapTime;
            }

            if (playerTime <= 0f)
            {
                return false;
            }

            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].isPlayer)
                {
                    continue;
                }

                float rivalTime = GetQualifyingPhaseTime(active[i], qualifyingPhase);
                if (rivalTime <= 0f)
                {
                    rivalTime = SimulateAiQualifyingTime(active[i], qualifyingPhase);
                    SetAiQualifyingPhaseTime(active[i], qualifyingPhase, rivalTime);
                }

                if (rivalTime > 0f && rivalTime < 9998f && rivalTime < playerTime - 0.001f)
                {
                    return false;
                }
            }

            return true;
        }

        void UpdateSectorRecords(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || State == null)
            {
                return;
            }

            LapTracker lap = participant.lapTracker;
            CheckCompletedSector(participant, 1, lap.LastSector1Time, lap.BestSector1Time, lap.CurrentLapInvalidated);
            CheckCompletedSector(participant, 2, lap.LastSector2Time, lap.BestSector2Time, lap.CurrentLapInvalidated);
            CheckCompletedSector(participant, 3, lap.LastSector3Time, lap.BestSector3Time, lap.LastLapInvalidated);
        }

        void CheckCompletedSector(RaceParticipant participant, int sector, float sectorTime, float personalBest, bool invalidated)
        {
            if (sectorTime <= 0f || sector < 1 || sector > 3 || State == null)
            {
                return;
            }

            State.OnSectorComplete(participant, sector, sectorTime, invalidated);

            if (CurrentSession != RaceWeekendSession.Qualifying || invalidated)
            {
                return;
            }

            bool purple = State.IsPurpleSector(sector, sectorTime);
            if (participant.isPlayer)
            {
                bool personalBestSector = personalBest > 0f && Mathf.Abs(personalBest - sectorTime) < 0.002f;
                playerSectorColors[sector - 1] = purple ? SectorPurple : (personalBestSector ? SectorGreen : SectorYellow);
            }
        }


        float GetDisplayQualifyingTime(QualifyingSimEntry entry)
        {
            if (entry == null)
            {
                return 9999f;
            }

            if (entry.isPlayer && qualifyingPhase >= 1 && qualifyingPhase <= 3 && playerQualifyingBestTimes[qualifyingPhase - 1] > 0f)
            {
                return playerQualifyingBestTimes[qualifyingPhase - 1];
            }

            if (entry.isPlayer && PlayerParticipant != null && PlayerParticipant.lapTracker != null && PlayerParticipant.lapTracker.BestLapTime > 0f)
            {
                return PlayerParticipant.lapTracker.BestLapTime;
            }

            float time = GetQualifyingPhaseTime(entry, qualifyingPhase);
            return time > 0f ? time : 9999f;
        }

        int GetQualifyingPositionEstimate()
        {
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            active.Sort((a, b) => GetDisplayQualifyingTime(a).CompareTo(GetDisplayQualifyingTime(b)));
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].isPlayer)
                {
                    return i + 1;
                }
            }

            return Mathf.Max(1, active.Count);
        }

        string DriverCode(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "---";
            }

            string compact = name.ToUpperInvariant().Replace(" ", "");
            return compact.Length > 3 ? compact.Substring(0, 3) : compact.PadRight(3, '-');
        }

        void SpawnRaceGrid(string playerName, string playerTeamId, bool careerRace)
        {
            TeamData playerTeam = Data.FindTeam(playerTeamId);
            CarPerformanceData playerCar = Data.FindCar(playerTeam.carPerformanceId);
            if (careerRace)
            {
                playerCar = Career.ApplyCareerUpgrades(playerCar);
            }

            PlayerParticipant = SpawnParticipant(
                "player",
                playerName,
                playerTeam.id,
                playerTeam.shortName,
                true,
                null,
                playerTeam,
                playerCar,
                ResolveGridIndex("player", 0));

            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                BuildQualifyingField(playerTeamId);
                PrepareAiQualifyingTargetsForPhase();
                return;
            }

            if (IsTimeTrial)
            {
                return;
            }

            List<DriverData> aiDrivers = Data.GetAiRaceDrivers(playerTeamId, FullWeekendAiCount, ReplacedDriverIdForPlayerTeam(playerTeamId));
            for (int i = 0; i < aiDrivers.Count; i++)
            {
                DriverData driver = aiDrivers[i];
                TeamData team = Data.FindTeam(driver.teamId);
                CarPerformanceData car = Data.FindCar(team.carPerformanceId);
                SpawnParticipant(
                    driver.id,
                    driver.displayName,
                    team.id,
                    team.shortName,
                    false,
                    driver,
                    team,
                    car,
                    ResolveGridIndex(driver.id, i + 1));
            }
        }

        void BuildQualifyingField(string playerTeamId)
        {
            QualifyingSimEntry playerEntry = qualifyingEntries.Find(item => item.isPlayer);
            if (playerEntry == null)
            {
                playerEntry = new QualifyingSimEntry
                {
                    driverId = PlayerParticipant.driverId,
                    driverName = PlayerParticipant.driverName,
                    teamId = PlayerParticipant.teamId,
                    isPlayer = true
                };
                qualifyingEntries.Add(playerEntry);
            }

            playerEntry.participant = PlayerParticipant;
            playerEntry.carData = PlayerParticipant.carData;

            if (qualifyingEntries.Count > 1)
            {
                return;
            }

            List<DriverData> aiDrivers = Data.GetAiRaceDrivers(playerTeamId, FullWeekendAiCount, ReplacedDriverIdForPlayerTeam(playerTeamId));
            for (int i = 0; i < aiDrivers.Count; i++)
            {
                DriverData driver = aiDrivers[i];
                TeamData team = Data.FindTeam(driver.teamId);
                CarPerformanceData car = team == null ? Data.Cars.cars[0] : Data.FindCar(team.carPerformanceId);
                qualifyingEntries.Add(new QualifyingSimEntry
                {
                    driverId = driver.id,
                    driverName = driver.displayName,
                    teamId = team == null ? driver.teamId : team.id,
                    driverData = driver,
                    carData = car,
                    isPlayer = false
                });
            }
        }

        void BuildSimulatedQualifyingField(string playerName, string playerTeamId)
        {
            qualifyingEntries.Clear();
            TeamData playerTeam = Data.FindTeam(playerTeamId);
            CarPerformanceData playerCar = Career == null ? null : Career.GetPlayerCar();
            if (playerCar == null)
            {
                playerCar = playerTeam == null ? Data.Cars.cars[0] : Data.FindCar(playerTeam.carPerformanceId);
            }

            qualifyingEntries.Add(new QualifyingSimEntry
            {
                driverId = "player",
                driverName = string.IsNullOrEmpty(playerName) ? "Player Driver" : playerName,
                teamId = playerTeam == null ? playerTeamId : playerTeam.id,
                driverData = ResolvePlayerQualifyingDriverData(playerName, playerTeamId),
                carData = playerCar,
                isPlayer = true
            });

            List<DriverData> aiDrivers = Data.GetAiRaceDrivers(playerTeamId, FullWeekendAiCount, ReplacedDriverIdForPlayerTeam(playerTeamId));
            for (int i = 0; i < aiDrivers.Count; i++)
            {
                DriverData driver = aiDrivers[i];
                TeamData team = Data.FindTeam(driver.teamId);
                CarPerformanceData car = team == null ? Data.Cars.cars[0] : Data.FindCar(team.carPerformanceId);
                qualifyingEntries.Add(new QualifyingSimEntry
                {
                    driverId = driver.id,
                    driverName = driver.displayName,
                    teamId = team == null ? driver.teamId : team.id,
                    driverData = driver,
                    carData = car,
                    isPlayer = false
                });
            }
        }

        DriverData ResolvePlayerQualifyingDriverData(string playerName, string playerTeamId)
        {
            if (Career != null && Career.Save != null && Career.Save.useExistingDriver && !string.IsNullOrEmpty(Career.Save.selectedDriverId))
            {
                DriverData selected = Data.FindDriver(Career.Save.selectedDriverId);
                if (selected != null)
                {
                    return selected;
                }
            }

            TeamData team = Data.FindTeam(playerTeamId);
            CarPerformanceData car = team == null ? Data.Cars.cars[0] : Data.FindCar(team.carPerformanceId);
            float carQualifyingBase = car == null ? 76f : car.cornering * 0.34f + car.enginePower * 0.26f + car.aeroEfficiency * 0.22f + car.braking * 0.18f;
            float reputationBonus = Career == null || Career.Save == null ? 0f : Mathf.Clamp((Career.Save.reputation - 25f) * 0.18f, -5f, 9f);
            int qualifying = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(68f, 88f, carQualifyingBase / 100f) + reputationBonus), 55, 96);
            int consistency = Mathf.Clamp(qualifying - 2 + (Career == null || Career.Save == null ? 0 : Career.Save.currentSeason), 55, 94);
            return new DriverData
            {
                id = "player",
                displayName = string.IsNullOrEmpty(playerName) ? "Player Driver" : playerName,
                abbreviation = DriverCode(playerName),
                teamId = playerTeamId,
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
                experience = Mathf.Clamp(70 + (Career == null || Career.Save == null ? 0 : Career.Save.currentSeason * 2), 60, 94),
                developmentPotential = 84
            };
        }

        string ReplacedDriverIdForPlayerTeam(string playerTeamId)
        {
            if (Career != null && Career.Save != null && !string.IsNullOrEmpty(Career.Save.selectedDriverId))
            {
                return Career.Save.selectedDriverId;
            }

            List<DriverData> teamDrivers = Data.GetDriversForTeam(playerTeamId);
            return teamDrivers.Count > 0 ? teamDrivers[0].id : "";
        }

        void PrepareAiQualifyingTargetsForPhase()
        {
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            for (int i = 0; i < active.Count; i++)
            {
                QualifyingSimEntry entry = active[i];
                if (!entry.isPlayer && GetQualifyingPhaseTime(entry, qualifyingPhase) <= 0f)
                {
                    SetAiQualifyingPhaseTime(entry, qualifyingPhase, SimulateAiQualifyingTime(entry, qualifyingPhase));
                }
            }
        }

        int ResolveGridIndex(string driverId, int fallback)
        {
            if (CurrentSession == RaceWeekendSession.Qualifying || !IsCareerRace)
            {
                return fallback;
            }

            List<QualifyingResultEntry> grid = Career == null || Career.Save == null ? lastQualifyingResults : Career.Save.lastQualifyingResults;
            if ((grid == null || grid.Count == 0) && Career != null && Career.Save != null && Career.Save.qualifyingResults != null)
            {
                for (int i = Career.Save.qualifyingResults.Count - 1; i >= 0; i--)
                {
                    QualifyingResultRecord record = Career.Save.qualifyingResults[i];
                    if (record.season == Career.Save.currentSeason && record.round == Career.Save.currentRound && record.results != null && record.results.Count > 0)
                    {
                        grid = record.results;
                        break;
                    }
                }
            }

            if (grid == null || grid.Count == 0)
            {
                return fallback;
            }

            for (int i = 0; i < grid.Count; i++)
            {
                if (grid[i].driverId == driverId)
                {
                    return Mathf.Max(0, grid[i].position - 1);
                }
            }

            if (driverId == "player")
            {
                for (int i = 0; i < grid.Count; i++)
                {
                    if (grid[i].isPlayer)
                    {
                        return Mathf.Max(0, grid[i].position - 1);
                    }
                }
            }

            return fallback;
        }

        RaceParticipant SpawnParticipant(
            string driverId,
            string driverName,
            string teamId,
            string teamShort,
            bool player,
            DriverData driver,
            TeamData team,
            CarPerformanceData car,
            int gridIndex)
        {
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            int row = gridIndex / 2;
            bool leftSlot = gridIndex % 2 == 0;
            float gridDistance = Track.length - 42f - row * 15.5f - (leftSlot ? 0f : 7.7f);
            Track.SampleAtDistance(gridDistance, out point, out forward, out right);
            float laneWidth = Mathf.Min(4.2f, Track.roadHalfWidth * 0.46f);
            float lane = leftSlot ? -laneWidth : laneWidth;
            Vector3 spawnPosition = FindRoadSpawnPosition(point + right * lane, driverName, out bool hitRoad);
            Quaternion spawnRotation = Quaternion.LookRotation(forward, Vector3.up);
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                Track.GetPitReleasePose(out spawnPosition, out spawnRotation);
            }

            GameObject carObject = CreateOpenWheelCar(driverName, team.PrimaryUnityColor, team.SecondaryUnityColor);
            carObject.transform.SetParent(raceWorld.transform);
            carObject.transform.position = spawnPosition;
            carObject.transform.rotation = spawnRotation;

            VehicleController controller = carObject.AddComponent<VehicleController>();
            LapTracker lapTracker = carObject.AddComponent<LapTracker>();
            RaceParticipant participant = carObject.AddComponent<RaceParticipant>();
            participant.Initialize(driverId, driverName, teamId, teamShort, player, driver, team, car);
            participant.gridPosition = gridIndex + 1;
            participant.startReactionDelay = player ? 0f : Random.Range(0.12f, 0.62f);
            participant.hasLastSafePosition = true;
            participant.lastSafePosition = spawnPosition;
            participant.lastSafeRotation = carObject.transform.rotation;
            TyreCompound startCompound = StartingTyreForParticipant(player);
            participant.startingCompound = startCompound;
            controller.Initialize(car, Track, startCompound, Settings.Current.manualGears && player, Settings.Current, player);
            controller.SetGridHold(StartCountdown > 0f);
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                participant.pitLimiterUntilExit = true;
                controller.SetPitLimiter(true);
            }

            VehicleAudio audio = carObject.AddComponent<VehicleAudio>();
            audio.Initialize(Settings.Current.audioEnabled, player ? 0.55f : 0.28f);
            lapTracker.Initialize(Track, CurrentSession == RaceWeekendSession.Qualifying ? 2 : RaceLaps);
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                lapTracker.ConfigureQualifyingOutLap();
            }
            else
            {
                lapTracker.ConfigureRaceGridStart(gridDistance);
            }

            participant.vehicle = controller;
            participant.lapTracker = lapTracker;

            if (player)
            {
                CameraRig rig = new GameObject("Player camera rig").AddComponent<CameraRig>();
                rig.transform.SetParent(raceWorld.transform);
                rig.transform.position = carObject.transform.position - carObject.transform.forward * 10f + Vector3.up * 4f;
                rig.Initialize(
                    carObject.transform,
                    Settings.Current.cameraShake ? Settings.Current.cameraShakeStrength : 0f,
                    Settings.Current.cameraFov);
                PlayerVehicleInput input = carObject.AddComponent<PlayerVehicleInput>();
                input.raceManager = this;
                input.cameraRig = rig;
                input.participant = participant;
            }
            else
            {
                AiVehicleController ai = carObject.AddComponent<AiVehicleController>();
                ai.Initialize(this, participant, Track);
            }

            if (State != null) State.RegisterParticipant(participant);
            return participant;
        }

        Vector3 FindRoadSpawnPosition(Vector3 desired, string driverName, out bool hitRoad)
        {
            hitRoad = false;
            Vector3 origin = desired + Vector3.up * 35f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 90f, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            Vector3 bestPoint = desired + Vector3.up * 0.18f;
            for (int i = 0; i < hits.Length; i++)
            {
                if (Track.roadCollider != null && hits[i].collider == Track.roadCollider && hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    bestPoint = hits[i].point + Vector3.up * 0.18f;
                    hitRoad = true;
                }
            }

            if (!hitRoad)
            {
                Debug.LogWarning("[RoadPhysics] No drivable road collider under spawn for " + driverName +
                                 " desired=" + desired +
                                 " roadColliderExists=" + (Track.roadCollider != null));
            }
            else
            {
                Debug.Log("[RoadPhysics] Spawn raycast hit road for " + driverName + " spawn=" + bestPoint);
            }

            return bestPoint;
        }

        void LogPlayerSpawnPhysics()
        {
            if (PlayerParticipant == null || PlayerParticipant.vehicle == null)
            {
                return;
            }

            Vector3 origin = PlayerParticipant.transform.position + Vector3.up * 6f;
            bool hitRoad = false;
            Vector3 hitPoint = Vector3.zero;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (Track.roadCollider != null && hits[i].collider == Track.roadCollider && hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    hitPoint = hits[i].point;
                    hitRoad = true;
                }
            }

            Rigidbody body = PlayerParticipant.GetComponent<Rigidbody>();
            Debug.Log("[RoadPhysics] Player spawn position=" + PlayerParticipant.transform.position +
                      " raycastHitRoad=" + hitRoad +
                      " roadHitPoint=" + hitPoint +
                      " rigidbodyY=" + (body == null ? -999f : body.position.y) +
                      " roadColliderExists=" + (Track.roadCollider != null) +
                      " roadLayer=" + (Track.roadCollider == null ? "none" : LayerMask.LayerToName(Track.roadCollider.gameObject.layer)) +
                      " carLayer=" + LayerMask.LayerToName(PlayerParticipant.gameObject.layer) +
                      " roadCollidesWithCarLayer=" + (Track.roadCollider != null && !Physics.GetIgnoreLayerCollision(Track.roadCollider.gameObject.layer, PlayerParticipant.gameObject.layer)));
            if (!hitRoad)
            {
                Debug.LogWarning("[RoadPhysics] No drivable road collider found below player spawn.");
            }
        }

        void HoldGridCars(bool held)
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                if (Participants[i] != null && Participants[i].vehicle != null)
                {
                    Participants[i].vehicle.SetGridHold(held);
                }
            }
        }

        void HandleFallRespawn(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || Track == null)
            {
                return;
            }

            participant.fallRespawnCooldown = Mathf.Max(0f, participant.fallRespawnCooldown - Time.deltaTime);
            TrackProgress progress = Track.GetProgress(participant.transform.position);
            bool stableOnRoad =
                Mathf.Abs(progress.lateralDistance) <= Track.roadHalfWidth &&
                participant.transform.position.y >= progress.nearestPoint.y - 0.35f &&
                participant.transform.position.y <= progress.nearestPoint.y + 2.25f;

            if (stableOnRoad && participant.fallRespawnCooldown <= 0f)
            {
                participant.hasLastSafePosition = true;
                participant.lastSafePosition = participant.transform.position;
                participant.lastSafeRotation = participant.transform.rotation;
            }

            if (participant.transform.position.y >= progress.nearestPoint.y - 5f)
            {
                return;
            }

            Vector3 respawnPosition = participant.hasLastSafePosition
                ? participant.lastSafePosition + Vector3.up * 0.35f
                : progress.nearestPoint + Vector3.up * 0.45f;
            Quaternion respawnRotation = participant.hasLastSafePosition
                ? participant.lastSafeRotation
                : Quaternion.LookRotation(progress.forward, Vector3.up);

            Rigidbody body = participant.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = respawnPosition;
                body.rotation = respawnRotation;
            }
            else
            {
                participant.transform.position = respawnPosition;
                participant.transform.rotation = respawnRotation;
            }

            participant.fallRespawnCooldown = 2f;
            Debug.LogWarning("[RoadPhysics] Respawned " + participant.driverName +
                             " after falling below track. respawn=" + respawnPosition);
        }

        GameObject CreateOpenWheelCar(string driverName, Color primary, Color secondary)
        {
            GameObject root = new GameObject(driverName + " car");
            root.layer = 0;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 16;
            body.solverVelocityIterations = 8;
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.75f, 0.68f, 4.7f);
            collider.center = new Vector3(0f, 0.22f, 0.08f);
            collider.sharedMaterial = GetCarBodyPhysicsMaterial();

            Material primaryMaterial = CreateMaterial(driverName + " primary", primary, 0.22f, 0.86f);
            Material secondaryMaterial = CreateMaterial(driverName + " secondary", secondary, 0.18f, 0.82f);
            Material tyreMaterial = CreateMaterial(driverName + " tyre", new Color(0.008f, 0.009f, 0.011f), 0.02f, 0.28f);
            Material rimMaterial = CreateMaterial(driverName + " rim", new Color(0.76f, 0.76f, 0.74f), 0.65f, 0.82f);
            Material floorMaterial = CreateMaterial(driverName + " carbon floor", new Color(0.012f, 0.014f, 0.016f), 0.35f, 0.68f);
            Material visorMaterial = CreateMaterial(driverName + " visor", new Color(0.01f, 0.04f, 0.08f), 0.45f, 0.96f);
            Material helmetMaterial = CreateMaterial(driverName + " helmet", Color.Lerp(secondary, Color.white, 0.35f), 0.15f, 0.88f);
            Material inletMaterial = CreateMaterial(driverName + " inlet shadow", new Color(0.002f, 0.003f, 0.004f), 0f, 0.48f);
            Material detailMaterial = CreateMaterial(driverName + " tech detail", new Color(0.12f, 0.14f, 0.16f), 0.55f, 0.78f);
            Material brakeDiscMaterial = CreateMaterial(driverName + " brake disc", new Color(0.34f, 0.34f, 0.32f), 0.42f, 0.48f);
            Material caliperMaterial = CreateMaterial(driverName + " brake caliper", Color.Lerp(secondary, Color.black, 0.18f), 0.12f, 0.55f);

            CreateTaperedBox(root.transform, "survival cell", new Vector3(0f, 0.38f, 0.0f), 0.64f, 1.04f, 0.42f, 2.35f, primaryMaterial);
            CreateTaperedBox(root.transform, "carbon floor", new Vector3(0f, 0.14f, -0.18f), 1.34f, 1.62f, 0.1f, 3.72f, floorMaterial);
            CreateTaperedBox(root.transform, "left sidepod", new Vector3(-0.64f, 0.35f, -0.48f), 0.22f, 0.42f, 0.34f, 1.28f, primaryMaterial);
            CreateTaperedBox(root.transform, "right sidepod", new Vector3(0.64f, 0.35f, -0.48f), 0.22f, 0.42f, 0.34f, 1.28f, primaryMaterial);
            CreateChildCube(root.transform, "left sidepod inlet", new Vector3(-0.86f, 0.42f, 0.02f), new Vector3(0.05f, 0.22f, 0.36f), inletMaterial);
            CreateChildCube(root.transform, "right sidepod inlet", new Vector3(0.86f, 0.42f, 0.02f), new Vector3(0.05f, 0.22f, 0.36f), inletMaterial);
            CreateChildCube(root.transform, "left livery flash", new Vector3(-0.88f, 0.52f, -0.44f), new Vector3(0.045f, 0.16f, 1.08f), secondaryMaterial);
            CreateChildCube(root.transform, "right livery flash", new Vector3(0.88f, 0.52f, -0.44f), new Vector3(0.045f, 0.16f, 1.08f), secondaryMaterial);
            CreateTaperedBox(root.transform, "narrow nose", new Vector3(0f, 0.3f, 1.62f), 0.2f, 0.48f, 0.22f, 1.95f, primaryMaterial);
            CreateChildCube(root.transform, "nose detail upper", new Vector3(0f, 0.46f, 1.63f), new Vector3(0.18f, 0.055f, 1.52f), secondaryMaterial);
            CreateChildCube(root.transform, "nose detail tip", new Vector3(0f, 0.22f, 2.58f), new Vector3(0.12f, 0.08f, 0.18f), detailMaterial);

            CreateChildCube(root.transform, "front wing base", new Vector3(0f, 0.17f, 2.55f), new Vector3(1.95f, 0.06f, 0.42f), secondaryMaterial);
            CreateChildCube(root.transform, "front wing upper flap", new Vector3(0f, 0.28f, 2.68f), new Vector3(1.85f, 0.04f, 0.22f), primaryMaterial);
            CreateChildCube(root.transform, "front wing carbon element", new Vector3(0f, 0.34f, 2.86f), new Vector3(1.58f, 0.045f, 0.16f), detailMaterial);
            CreateChildCube(root.transform, "left front endplate", new Vector3(-1.02f, 0.24f, 2.55f), new Vector3(0.06f, 0.35f, 0.48f), secondaryMaterial);
            CreateChildCube(root.transform, "right front endplate", new Vector3(1.02f, 0.24f, 2.55f), new Vector3(0.06f, 0.35f, 0.48f), secondaryMaterial);

            CreateChildCube(root.transform, "rear wing pillar left", new Vector3(-0.25f, 0.65f, -1.95f), new Vector3(0.05f, 0.35f, 0.08f), detailMaterial);
            CreateChildCube(root.transform, "rear wing pillar right", new Vector3(0.25f, 0.65f, -1.95f), new Vector3(0.05f, 0.35f, 0.08f), detailMaterial);
            CreateChildCube(root.transform, "rear wing main plane", new Vector3(0f, 0.62f, -2.02f), new Vector3(1.72f, 0.12f, 0.38f), secondaryMaterial);
            CreateChildCube(root.transform, "rear wing flap", new Vector3(0f, 0.82f, -2.18f), new Vector3(1.65f, 0.08f, 0.24f), primaryMaterial);
            CreateChildCube(root.transform, "rear beam wing", new Vector3(0f, 0.41f, -2.04f), new Vector3(1.52f, 0.07f, 0.2f), detailMaterial);
            CreateChildCube(root.transform, "left rear endplate", new Vector3(-0.92f, 0.72f, -2.08f), new Vector3(0.08f, 0.65f, 0.42f), secondaryMaterial);
            CreateChildCube(root.transform, "right rear endplate", new Vector3(0.92f, 0.72f, -2.08f), new Vector3(0.08f, 0.65f, 0.42f), secondaryMaterial);

            CreateTaperedBox(root.transform, "engine cover", new Vector3(0f, 0.66f, -0.72f), 0.42f, 0.72f, 0.58f, 1.38f, primaryMaterial);
            CreateChildCube(root.transform, "shark fin", new Vector3(0f, 0.88f, -1.15f), new Vector3(0.035f, 0.32f, 0.85f), secondaryMaterial);
            CreateTaperedBox(root.transform, "rear diffuser", new Vector3(0f, 0.18f, -1.94f), 1.12f, 1.48f, 0.18f, 0.72f, floorMaterial);
            CreateChildCube(root.transform, "airbox", new Vector3(0f, 0.98f, -0.34f), new Vector3(0.35f, 0.22f, 0.52f), secondaryMaterial);

            CreateChildCube(root.transform, "halo center", new Vector3(0f, 0.88f, 0.52f), new Vector3(0.06f, 0.18f, 0.08f), detailMaterial);
            CreateChildCube(root.transform, "halo rim", new Vector3(0f, 0.95f, 0.28f), new Vector3(0.74f, 0.06f, 0.72f), secondaryMaterial);
            CreateChildCube(root.transform, "left halo stay", new Vector3(-0.32f, 0.78f, 0.22f), new Vector3(0.055f, 0.32f, 0.07f), detailMaterial);
            CreateChildCube(root.transform, "right halo stay", new Vector3(0.32f, 0.78f, 0.22f), new Vector3(0.055f, 0.32f, 0.07f), detailMaterial);
            CreateChildSphere(root.transform, "cockpit visor", new Vector3(0f, 0.78f, 0.44f), new Vector3(0.48f, 0.24f, 0.52f), visorMaterial);
            CreateChildSphere(root.transform, "driver helmet", new Vector3(0f, 0.88f, 0.2f), new Vector3(0.32f, 0.32f, 0.32f), helmetMaterial);
            CreateChildCube(root.transform, "steering wheel", new Vector3(0f, 0.76f, 0.62f), new Vector3(0.24f, 0.18f, 0.05f), detailMaterial);

            CreateSuspension(root.transform, floorMaterial, detailMaterial);
            CreateWheel(root.transform, new Vector3(-1.08f, 0.22f, 1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);
            CreateWheel(root.transform, new Vector3(1.08f, 0.22f, 1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);
            CreateWheel(root.transform, new Vector3(-1.08f, 0.22f, -1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);
            CreateWheel(root.transform, new Vector3(1.08f, 0.22f, -1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);

            return root;
        }

        void CreateTaperedBox(Transform parent, string objectName, Vector3 localPosition, float frontWidth, float rearWidth, float height, float length, Material material)
        {
            GameObject meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(parent);
            meshObject.transform.localPosition = localPosition;
            meshObject.transform.localRotation = Quaternion.identity;

            float front = length * 0.5f;
            float rear = -length * 0.5f;
            float y0 = -height * 0.5f;
            float y1 = height * 0.5f;
            float fw = frontWidth * 0.5f;
            float rw = rearWidth * 0.5f;

            Mesh mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(-fw, y0, front), new Vector3(fw, y0, front), new Vector3(fw, y1, front), new Vector3(-fw, y1, front),
                new Vector3(-rw, y0, rear), new Vector3(rw, y0, rear), new Vector3(rw, y1, rear), new Vector3(-rw, y1, rear)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,
                3, 7, 6, 3, 6, 2,
                0, 1, 5, 0, 5, 4
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            MeshFilter filter = meshObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;
        }

        void CreateChildCube(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        void CreateChildSphere(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = objectName;
            sphere.transform.SetParent(parent);
            sphere.transform.localPosition = localPosition;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = localScale;
            sphere.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        void CreateWheel(Transform parent, Vector3 localPosition, Material tyreMaterial, Material rimMaterial, Material brakeDiscMaterial, Material caliperMaterial)
        {
            GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = "open wheel";
            wheel.transform.SetParent(parent);
            wheel.transform.localPosition = localPosition;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.transform.localScale = new Vector3(0.38f, 0.25f, 0.38f);
            wheel.GetComponent<Renderer>().sharedMaterial = tyreMaterial;
            Collider collider = wheel.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "wheel rim";
            rim.transform.SetParent(parent);
            rim.transform.localPosition = localPosition;
            rim.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            rim.transform.localScale = new Vector3(0.22f, 0.265f, 0.22f);
            rim.GetComponent<Renderer>().sharedMaterial = rimMaterial;
            Collider rimCollider = rim.GetComponent<Collider>();
            if (rimCollider != null)
            {
                Destroy(rimCollider);
            }

            float inboard = localPosition.x < 0f ? 0.14f : -0.14f;
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "brake disc";
            disc.transform.SetParent(parent);
            disc.transform.localPosition = localPosition + new Vector3(inboard, 0f, 0f);
            disc.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            disc.transform.localScale = new Vector3(0.17f, 0.035f, 0.17f);
            disc.GetComponent<Renderer>().sharedMaterial = brakeDiscMaterial;
            Collider discCollider = disc.GetComponent<Collider>();
            if (discCollider != null)
            {
                Destroy(discCollider);
            }

            GameObject caliper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            caliper.name = "brake caliper";
            caliper.transform.SetParent(parent);
            caliper.transform.localPosition = localPosition + new Vector3(inboard * 1.08f, 0.1f, 0.04f);
            caliper.transform.localRotation = Quaternion.identity;
            caliper.transform.localScale = new Vector3(0.07f, 0.16f, 0.12f);
            caliper.GetComponent<Renderer>().sharedMaterial = caliperMaterial;
            Collider caliperCollider = caliper.GetComponent<Collider>();
            if (caliperCollider != null)
            {
                Destroy(caliperCollider);
            }
        }

        void CreateSuspension(Transform parent, Material armMaterial, Material detailMaterial)
        {
            // Front
            CreateSuspensionArm(parent, new Vector3(-0.52f, 0.32f, 1.32f), new Vector3(-1.02f, 0.26f, 1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(0.52f, 0.32f, 1.32f), new Vector3(1.02f, 0.26f, 1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(-0.52f, 0.18f, 1.32f), new Vector3(-1.02f, 0.22f, 1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(0.52f, 0.18f, 1.32f), new Vector3(1.02f, 0.22f, 1.35f), armMaterial);

            // Rear
            CreateSuspensionArm(parent, new Vector3(-0.52f, 0.32f, -1.34f), new Vector3(-1.02f, 0.26f, -1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(0.52f, 0.32f, -1.34f), new Vector3(1.02f, 0.26f, -1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(-0.52f, 0.18f, -1.34f), new Vector3(-1.02f, 0.22f, -1.35f), armMaterial);
            CreateSuspensionArm(parent, new Vector3(0.52f, 0.18f, -1.34f), new Vector3(1.02f, 0.22f, -1.35f), armMaterial);

            // Brake assemblies
            CreateChildCube(parent, "brake fl", new Vector3(-1.02f, 0.26f, 1.35f), new Vector3(0.12f, 0.22f, 0.22f), detailMaterial);
            CreateChildCube(parent, "brake fr", new Vector3(1.02f, 0.26f, 1.35f), new Vector3(0.12f, 0.22f, 0.22f), detailMaterial);
            CreateChildCube(parent, "brake rl", new Vector3(-1.02f, 0.26f, -1.35f), new Vector3(0.12f, 0.22f, 0.22f), detailMaterial);
            CreateChildCube(parent, "brake rr", new Vector3(1.02f, 0.26f, -1.35f), new Vector3(0.12f, 0.22f, 0.22f), detailMaterial);
        }

        void CreateSuspensionArm(Transform parent, Vector3 a, Vector3 b, Material material)
        {
            Vector3 midpoint = (a + b) * 0.5f;
            Vector3 delta = b - a;
            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "suspension arm";
            arm.transform.SetParent(parent);
            arm.transform.localPosition = midpoint;
            arm.transform.localRotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            arm.transform.localScale = new Vector3(0.035f, 0.035f, delta.magnitude);
            arm.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = arm.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        void CreateDriverLabel(Transform parent, string driverName, Color color)
        {
            GameObject labelObject = new GameObject("driver label");
            labelObject.transform.SetParent(parent);
            labelObject.transform.localPosition = new Vector3(0f, 0.96f, -0.22f);
            labelObject.transform.localRotation = Quaternion.Euler(76f, 0f, 0f);
            TextMesh text = labelObject.AddComponent<TextMesh>();
            text.text = driverName.Length > 3 ? driverName.Substring(0, 3).ToUpper() : driverName.ToUpper();
            text.fontSize = 38;
            text.characterSize = 0.055f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.Lerp(color, Color.white, 0.35f);
        }

        Material CreateMaterial(string materialName, Color color)
        {
            return CreateMaterial(materialName, color, 0f, 0.35f);
        }

        PhysicMaterial GetCarBodyPhysicsMaterial()
        {
            if (carBodyPhysicsMaterial != null)
            {
                return carBodyPhysicsMaterial;
            }

            carBodyPhysicsMaterial = new PhysicMaterial("Open wheel low-friction body");
            carBodyPhysicsMaterial.dynamicFriction = 0.02f;
            carBodyPhysicsMaterial.staticFriction = 0.02f;
            carBodyPhysicsMaterial.bounciness = 0f;
            carBodyPhysicsMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
            carBodyPhysicsMaterial.bounceCombine = PhysicMaterialCombine.Minimum;
            return carBodyPhysicsMaterial;
        }

        Material CreateMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            Material material = new Material(Shader.Find("Standard"));
            material.name = materialName;
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        void CreateLighting()
        {
            string trackId = EventData == null || string.IsNullOrEmpty(EventData.trackId) ? "" : EventData.trackId;
            bool night = trackId.Contains("singapore") || trackId.Contains("las_vegas");
            bool desert = trackId.Contains("bahrain") || trackId.Contains("abu_dhabi") || trackId.Contains("qatar");
            bool park = trackId.Contains("silverstone") || trackId.Contains("melbourne") || trackId.Contains("monza") || trackId.Contains("interlagos") || trackId.Contains("spa") || trackId.Contains("suzuka") || trackId.Contains("austria") || trackId.Contains("zandvoort");
            string weatherProfile = EventData == null || string.IsNullOrEmpty(EventData.weatherProfile) ? "" : EventData.weatherProfile.ToLowerInvariant();
            bool rainThreat = weatherProfile.Contains("wet") || weatherProfile.Contains("mixed");

            int quality = Settings == null ? 2 : Mathf.Clamp(Settings.Current.graphicsQuality, 0, 2);
            QualitySettings.antiAliasing = quality == 0 ? 0 : (quality == 1 ? 4 : 8);
            QualitySettings.shadows = quality == 0 ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.shadowDistance = quality == 0 ? 180f : (quality == 1 ? 300f : 450f);
            QualitySettings.shadowResolution = quality == 0 ? ShadowResolution.Medium : (quality == 1 ? ShadowResolution.High : ShadowResolution.VeryHigh);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = night ? new Color(0.08f, 0.12f, 0.22f) : (rainThreat ? new Color(0.28f, 0.36f, 0.42f) : new Color(0.42f, 0.58f, 0.74f));
            RenderSettings.ambientEquatorColor = night ? new Color(0.05f, 0.08f, 0.14f) : (rainThreat ? new Color(0.28f, 0.32f, 0.34f) : new Color(0.45f, 0.42f, 0.38f));
            RenderSettings.ambientGroundColor = night ? new Color(0.01f, 0.01f, 0.02f) : (rainThreat ? new Color(0.08f, 0.09f, 0.1f) : new Color(0.18f, 0.16f, 0.14f));
            RenderSettings.reflectionIntensity = rainThreat ? 0.78f : 0.46f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = rainThreat ? 0.00024f : 0.00015f;
            RenderSettings.fogColor = night ? new Color(0.015f, 0.02f, 0.035f) : (rainThreat ? new Color(0.28f, 0.34f, 0.36f) : (desert ? new Color(0.65f, 0.55f, 0.42f) : new Color(0.44f, 0.54f, 0.52f)));
            RenderSettings.skybox = null;

            GameObject lightObject = new GameObject("Primary Sun");
            lightObject.transform.SetParent(raceWorld.transform);
            lightObject.transform.rotation = Quaternion.Euler(night ? -15f : (desert ? 32f : 48f), desert ? -42f : -56f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = night ? 0.08f : (rainThreat ? 0.92f : (desert ? 1.55f : 1.25f));
            light.color = night ? new Color(0.6f, 0.7f, 1f) : (rainThreat ? new Color(0.76f, 0.86f, 0.92f) : (desert ? new Color(1f, 0.85f, 0.65f) : new Color(0.98f, 0.96f, 0.94f)));
            light.shadows = LightShadows.Soft;
            light.shadowStrength = rainThreat ? 0.68f : 0.92f;
            light.shadowBias = 0.035f;
            light.shadowNormalBias = 0.22f;

            GameObject fill = new GameObject("Atmospheric Fill");
            fill.transform.SetParent(raceWorld.transform);
            fill.transform.position = new Vector3(40f, 40f, -40f);
            Light fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.intensity = night ? 1.8f : 0.64f;
            fillLight.range = 350f;
            fillLight.shadows = LightShadows.None;

            GameObject probeObject = new GameObject("Runtime reflection probe");
            probeObject.transform.SetParent(raceWorld.transform);
            probeObject.transform.position = new Vector3(40f, 18f, 40f);
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.EveryFrame;
            probe.intensity = rainThreat ? 0.78f : 0.46f;
            probe.size = new Vector3(520f, 120f, 520f);

            if (night)
            {
                for (int i = 0; i < 6; i++)
                {
                    GameObject flood = new GameObject("Night floodlight");
                    flood.transform.SetParent(raceWorld.transform);
                    flood.transform.position = new Vector3(-80f + i * 58f, 18f, 30f + (i % 2) * 75f);
                    Light floodLight = flood.AddComponent<Light>();
                    floodLight.type = LightType.Point;
                    floodLight.intensity = 1.25f;
                    floodLight.range = 95f;
                }
            }
        }

        void AnimateQualifyingReturnToPits()
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null || participant.vehicle == null)
                {
                    continue;
                }

                if (participant.pitPhase != PitPhase.QualifyingReturn)
                {
                    BeginQualifyingPitReturn(participant);
                }

                UpdateQualifyingPitReturn(participant);
            }
        }

        void BeginQualifyingPitReturn(RaceParticipant participant)
        {
            participant.pitPhase = PitPhase.QualifyingReturn;
            participant.isPitting = true;
            participant.pitLimiterUntilExit = false;
            participant.vehicle.ClearPitRequest();
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            if (participant.isPlayer)
            {
                SessionMessage = "Q" + qualifyingPhase + " complete: returning to pits";
                PostEngineerMessage("Good, bring it back to the pits. We will reset for the next segment.", true);
            }
        }

        void UpdateQualifyingPitReturn(RaceParticipant participant)
        {
            Vector3 servicePosition;
            Quaternion serviceRotation;
            Track.GetPitServicePose(out servicePosition, out serviceRotation);
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            float distance = participant.vehicle.GuideToPitPose(servicePosition, serviceRotation, 22f, 220f);
            if (distance <= 0.45f)
            {
                participant.vehicle.SnapToPitPose(servicePosition, serviceRotation);
                if (participant.isPlayer)
                {
                    SessionMessage = "Q" + qualifyingPhase + " complete: car in pits";
                }
            }
        }

        void HandlePitService(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null)
            {
                return;
            }

            TrackProgress currentProgress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            float normalized = currentProgress.normalized;
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                if (participant.pitPhase == PitPhase.QualifyingReturn)
                {
                    UpdateQualifyingPitReturn(participant);
                }
                else if (participant.pitLimiterUntilExit)
                {
                    participant.vehicle.SetPitLimiter(true);
                    if (!Track.IsInPitExitLimiterZone(normalized))
                    {
                        participant.pitLimiterUntilExit = false;
                        participant.vehicle.SetPitLimiter(false);
                    }
                }
                else
                {
                    participant.vehicle.SetPitLimiter(false);
                    participant.vehicle.ClearPitRequest();
                }

                return;
            }

            if (participant.pitLimiterUntilExit)
            {
                participant.vehicle.SetPitLimiter(true);
                if (!Track.IsInPitExitLimiterZone(normalized))
                {
                    participant.pitLimiterUntilExit = false;
                    participant.vehicle.SetPitLimiter(false);
                    if (participant.isPlayer)
                    {
                        SessionMessage = "Pit exit clear";
                        PostEngineerMessage("Pit exit clear. You can race at full speed.", true);
                    }
                }
            }

            if (participant.pitPhase == PitPhase.Entry)
            {
                UpdatePitEntry(participant);
                return;
            }

            if (participant.pitPhase == PitPhase.Service)
            {
                UpdatePitService(participant);
                return;
            }

            if (participant.pitPhase == PitPhase.Release)
            {
                UpdatePitRelease(participant);
                return;
            }

            if (!participant.vehicle.PitRequested)
            {
                if (!participant.pitLimiterUntilExit)
                {
                    participant.vehicle.SetPitLimiter(false);
                }

                if (participant.isPlayer)
                {
                    engineerPitRequestConfirmed = false;
                }
                return;
            }

            bool pitApproach = Track.IsInPitApproach(normalized);
            participant.vehicle.SetPitLimiter(pitApproach);
            if (pitApproach && participant.isPlayer && !engineerPitRequestConfirmed)
            {
                engineerPitRequestConfirmed = true;
                PostEngineerMessage("Pit request confirmed. Slow for pit entry, limiter is 80 km/h.", true);
            }

            if (Track.IsInPitEntryZone(normalized))
            {
                BeginPitEntry(participant);
            }
            else if (participant.isPlayer)
            {
                SessionMessage = pitApproach ? "Pit entry approaching" : "Pit request queued";
            }
        }

        void BeginPitEntry(RaceParticipant participant)
        {
            participant.pitPhase = PitPhase.Entry;
            participant.pitEntryAligned = false;
            participant.isPitting = true;
            participant.pitLimiterUntilExit = false;
            participant.pitTimer = 0f;
            participant.pitServiceDuration = 0f;
            participant.nextPitCompound = participant.requestedPitCompoundSet ? participant.requestedPitCompound : NextPitCompound(participant);
            participant.pitTyreSelectionActive = false;
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.ClearPitRequest();
            if (participant.isPlayer)
            {
                SessionMessage = "Pit entry: limiter active";
                PostEngineerMessage("Pit entry. Hold steady, we are turning into the lane for " + participant.nextPitCompound + ".", true);
            }
        }

        void UpdatePitEntry(RaceParticipant participant)
        {
            if (!participant.pitEntryAligned)
            {
                Vector3 entryPosition;
                Quaternion entryRotation;
                Track.GetPitEntryPose(out entryPosition, out entryRotation);
                participant.vehicle.SetPitLimiter(true);
                participant.vehicle.SetPitServiceHold(true);
                float entryDistance = participant.vehicle.GuideToPitPose(entryPosition, entryRotation, 11.5f, 115f);
                if (participant.isPlayer)
                {
                    SessionMessage = "Pit entry: turning into lane";
                }

                if (entryDistance > 0.55f)
                {
                    return;
                }

                participant.vehicle.SnapToPitPose(entryPosition, entryRotation);
                participant.pitEntryAligned = true;
            }

            Vector3 servicePosition;
            Quaternion serviceRotation;
            Track.GetPitServicePose(out servicePosition, out serviceRotation);
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            float distance = participant.vehicle.GuideToPitPose(servicePosition, serviceRotation, 13.5f, 145f);
            if (participant.isPlayer)
            {
                SessionMessage = "Pit lane: rolling to box";
            }

            if (distance <= 0.45f)
            {
                participant.vehicle.SnapToPitPose(servicePosition, serviceRotation);
                BeginPitStop(participant);
            }
        }

        void BeginPitStop(RaceParticipant participant)
        {
            participant.pitPhase = PitPhase.Service;
            participant.isPitting = true;
            participant.pitServiceDuration = participant.isPlayer ? Random.Range(2.7f, 4.3f) : Random.Range(2.8f, 4.4f);
            participant.pitTimer = participant.pitServiceDuration;
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.ClearPitRequest();
            if (participant.isPlayer)
            {
                SessionMessage = "Pit box: changing to " + participant.nextPitCompound;
                PostEngineerMessage("Pit stop in progress. Tyres ready: " + participant.nextPitCompound + ".", true);
            }
        }

        void UpdatePitService(RaceParticipant participant)
        {
            participant.pitTimer -= Time.deltaTime;
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitLimiter(true);
            if (participant.isPlayer)
            {
                SessionMessage = PitStatusText(participant);
            }

            if (participant.pitTimer > 0f)
            {
                return;
            }

            participant.vehicle.CompletePitStop(participant.nextPitCompound);
            participant.pitStops++;
            participant.requestedPitCompoundSet = false;
            participant.pitTyreSelectionActive = false;
            participant.pitTimer = 0f;
            participant.pitPhase = PitPhase.Release;
            participant.pitServiceDuration = 0f;
            if (participant.isPlayer)
            {
                SessionMessage = "Pit release: limiter active";
                PostEngineerMessage("Stop complete. Release, limiter remains active until pit exit.", true);
            }
        }

        void UpdatePitRelease(RaceParticipant participant)
        {
            Vector3 releasePosition;
            Quaternion releaseRotation;
            Track.GetPitReleasePose(out releasePosition, out releaseRotation);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitLimiter(true);
            float distance = participant.vehicle.GuideToPitPose(releasePosition, releaseRotation, 20f, 200f);
            if (distance > 0.55f)
            {
                return;
            }

            participant.vehicle.SnapToPitPose(releasePosition, releaseRotation);
            participant.vehicle.SetPitServiceHold(false);
            participant.vehicle.SetPitLimiter(true);
            participant.pitPhase = PitPhase.None;
            participant.isPitting = false;
            participant.pitLimiterUntilExit = true;
            if (participant.isPlayer)
            {
                SessionMessage = "Released: limiter until pit exit";
            }
        }

        void HandleTrackLimits(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || Track == null || participant.finished || participant.isPitting || participant.pitLimiterUntilExit || participant.pitPhase != PitPhase.None)
            {
                return;
            }

            TrackProgress progress = participant.lapTracker.CurrentProgress;
            float lateral = Mathf.Abs(progress.lateralDistance);
            bool outsideWhiteLine = lateral > Track.roadHalfWidth + 2.2f;
            bool gainedTime = lateral > Track.roadHalfWidth + 5.2f && participant.vehicle != null && Mathf.Abs(participant.vehicle.CurrentSpeedKph) > 70f;
            if (outsideWhiteLine)
            {
                participant.lapTracker.InvalidateCurrentLap();
                participant.offTrackTimer += Time.deltaTime;
            }
            else
            {
                participant.offTrackTimer = Mathf.Max(0f, participant.offTrackTimer - Time.deltaTime * 2.5f);
            }

            if (gainedTime && participant.offTrackTimer > 0.75f)
            {
                participant.trackLimitWarnings++;
                participant.offTrackTimer = -1.6f;
                if (participant.trackLimitWarnings >= 3)
                {
                    participant.trackLimitWarnings = 0;
                    AddPenalty(participant, 5f, "Track limits");
                    if (participant.isPlayer)
                    {
                        SessionMessage = "Track limits: +5s";
                    }
                }
                else if (participant.isPlayer)
                {
                    SessionMessage = "Track limits warning " + participant.trackLimitWarnings + "/3";
                }
            }
        }

        void HandleFinish(RaceParticipant participant)
        {
            if (participant == null || participant.finished || participant.lapTracker == null || State == null)
            {
                return;
            }

            if (participant.lapTracker.CompletedRace)
            {
                ApplyMandatoryPitPenalty(participant);
                State.OnParticipantFinished(participant, RaceElapsed);
            }
        }

        void ApplyMandatoryPitPenalty(RaceParticipant participant)
        {
            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return;
            }

            if (participant.pitStops > 0)
            {
                return;
            }

            if (participant.mandatoryPitPenaltyApplied)
            {
                return;
            }

            participant.mandatoryPitPenaltyApplied = true;
            AddPenalty(participant, 10f, "No mandatory stop");
            if (participant.isPlayer)
            {
                SessionMessage = "No mandatory stop: +10s";
            }
        }

        void AddPenalty(RaceParticipant participant, float seconds, string reason)
        {
            participant.penaltiesSeconds += seconds;
            if (string.IsNullOrEmpty(participant.penaltyReason))
            {
                participant.penaltyReason = reason;
            }
            else if (!participant.penaltyReason.Contains(reason))
            {
                participant.penaltyReason += ", " + reason;
            }
        }

        TyreCompound StartingTyreForParticipant(bool player)
        {
            if (player)
            {
                return Settings.SelectedTyreCompound;
            }

            if (Track != null && (Track.weather == WeatherState.HeavyRain || Track.weather == WeatherState.LightRain))
            {
                return Track.weather == WeatherState.HeavyRain ? TyreCompound.Wet : TyreCompound.Intermediate;
            }

            int roll = Random.Range(0, 3);
            return roll == 0 ? TyreCompound.Soft : (roll == 1 ? TyreCompound.Medium : TyreCompound.Hard);
        }

        TyreCompound NextPitCompound(RaceParticipant participant)
        {
            if (Track.weather == WeatherState.HeavyRain)
            {
                return TyreCompound.Wet;
            }

            if (Track.weather == WeatherState.LightRain)
            {
                return TyreCompound.Intermediate;
            }

            if (participant.vehicle == null || participant.vehicle.Tyres == null)
            {
                return TyreCompound.Medium;
            }

            if (participant.vehicle.Tyres.Compound == TyreCompound.Soft)
            {
                return TyreCompound.Medium;
            }

            if (participant.vehicle.Tyres.Compound == TyreCompound.Medium)
            {
                return TyreCompound.Hard;
            }

            return TyreCompound.Medium;
        }

        void SortRunningOrder()
        {
            if (State != null) State.Tick();
        }

        void FinishRace()
        {
            IsRaceFinished = true;
            Time.timeScale = 1f;
            SortRunningOrder();
            List<RaceResultEntry> results = new List<RaceResultEntry>();
            if (State == null) return;
            for (int i = 0; i < State.SortedOrder.Count; i++)
            {
                RaceParticipant participant = State.SortedOrder[i];
                ApplyMandatoryPitPenalty(participant);
                participant.finishingPosition = i + 1;
                RaceResultEntry entry = participant.ToResultEntry();
                entry.finishingPosition = i + 1;
                if (participant.retired)
                {
                    entry.totalTime = participant.finishTime;
                    results.Add(entry);
                    continue;
                }

                if (!participant.finished && participant.lapTracker != null)
                {
                    int lapsRemaining = Mathf.Max(0, RaceLaps - participant.lapTracker.CompletedLaps);
                    entry.totalTime = RaceElapsed + lapsRemaining * Mathf.Max(65f, Track.length / 72f);
                }
                results.Add(entry);
            }

            results.Sort((a, b) =>
            {
                float aTime = a.totalTime + a.penaltiesSeconds;
                float bTime = b.totalTime + b.penaltiesSeconds;
                return aTime.CompareTo(bTime);
            });

            for (int i = 0; i < results.Count; i++)
            {
                results[i].finishingPosition = i + 1;
            }

            if (IsCareerRace)
            {
                Career.ApplyRaceResults(EventData, results);
            }

            ui.ShowResults(this, results, IsCareerRace);
        }

        void CompleteQualifyingRun()
        {
            RecordQualifyingPhase();
            QualifyingSimEntry playerEntry = qualifyingEntries.Find(item => item.isPlayer);
            bool advances = playerEntry != null && string.IsNullOrEmpty(playerEntry.eliminatedIn) && qualifyingPhase < 3;
            BeginQualifyingFeedback(BuildQualifyingSegmentFeedback(playerEntry, qualifyingPhase, advances), advances);
        }

        void BeginQualifyingFeedback(string feedback, bool advances)
        {
            QualifyingFeedbackText = feedback;
            SessionMessage = feedback.Replace("\n", "  ");
            qualifyingTransitionPending = true;
            qualifyingTransitionFinish = !advances;
            qualifyingTransitionTimer = 4.2f;
        }

        string BuildQualifyingSegmentFeedback(QualifyingSimEntry playerEntry, int phase, bool advances)
        {
            int position = playerEntry == null ? 0 : GetQualifyingPhasePosition(playerEntry, phase);
            if (phase == 1)
            {
                return "You qualified P" + position.ToString("00") + " in Q1\n" + (advances ? "Advanced to Q2" : "Eliminated in Q1");
            }

            if (phase == 2)
            {
                return "You qualified P" + position.ToString("00") + " in Q2\n" + (advances ? "Advanced to Q3" : "Eliminated in Q2");
            }

            return "You qualified P" + position.ToString("00") + " overall\nQualifying complete";
        }

        int GetQualifyingPhasePosition(QualifyingSimEntry target, int phase)
        {
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(phase);
            active.Sort((a, b) => GetQualifyingPhaseTime(a, phase).CompareTo(GetQualifyingPhaseTime(b, phase)));
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] == target || active[i].driverId == target.driverId)
                {
                    return i + 1;
                }
            }

            return Mathf.Max(1, active.Count);
        }

        void RecordQualifyingPhase()
        {
            if (qualifyingEntries.Count == 0)
            {
                BuildQualifyingField(PlayerParticipant == null ? "" : PlayerParticipant.teamId);
            }

            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            for (int i = 0; i < active.Count; i++)
            {
                QualifyingSimEntry entry = active[i];
                if (entry.isPlayer)
                {
                    int phaseIndex = Mathf.Clamp(qualifyingPhase, 1, 3) - 1;
                    float capturedTime = playerQualifyingBestTimes[phaseIndex];
                    bool hasCapturedValidLap = capturedTime > 0f && capturedTime < 9998f;
                    bool hasTrackerValidLap = PlayerParticipant != null && PlayerParticipant.lapTracker != null && PlayerParticipant.lapTracker.BestLapTime > 0f;
                    bool invalidated = !hasCapturedValidLap && !hasTrackerValidLap;
                    float time = invalidated ? InvalidQualifyingTime(qualifyingPhase) : (hasCapturedValidLap ? capturedTime : PlayerParticipant.lapTracker.BestLapTime);
                    entry.invalidated = invalidated;
                    entry.participant = PlayerParticipant;
                    SetQualifyingPhaseTime(entry, qualifyingPhase, time);
                    SetPlayerQualifyingSectors(entry, qualifyingPhase, time, invalidated);
                }
                else
                {
                    if (GetQualifyingPhaseTime(entry, qualifyingPhase) <= 0f)
                    {
                        SetAiQualifyingPhaseTime(entry, qualifyingPhase, SimulateAiQualifyingTime(entry, qualifyingPhase));
                    }
                }
            }

            ApplyQualifyingElimination(active, qualifyingPhase);
        }

        void FinishQualifying()
        {
            IsRaceFinished = true;
            lastQualifyingResultWasSimulated = false;
            Time.timeScale = 1f;
            List<QualifyingResultEntry> results = BuildFinalQualifyingResults();
            lastQualifyingResults = results;
            if (IsCareerRace)
            {
                Career.ApplyQualifyingResults(EventData, results);
            }

            ui.ShowQualifyingResults(this, results, IsCareerRace);
        }

        List<QualifyingResultEntry> BuildFinalQualifyingResults()
        {
            EnsureQualifyingPhaseComplete(1);
            EnsureQualifyingPhaseComplete(2);
            EnsureQualifyingPhaseComplete(3);

            List<QualifyingResultEntry> results = new List<QualifyingResultEntry>();
            List<QualifyingSimEntry> q3 = qualifyingEntries.FindAll(item => string.IsNullOrEmpty(item.eliminatedIn));
            q3.Sort((a, b) => GetQualifyingPhaseTime(a, 3).CompareTo(GetQualifyingPhaseTime(b, 3)));
            AppendQualifyingResults(results, q3, "");

            List<QualifyingSimEntry> q2Eliminated = qualifyingEntries.FindAll(item => item.eliminatedIn == "Q2");
            q2Eliminated.Sort((a, b) => GetQualifyingPhaseTime(a, 2).CompareTo(GetQualifyingPhaseTime(b, 2)));
            AppendQualifyingResults(results, q2Eliminated, "Q2");

            List<QualifyingSimEntry> q1Eliminated = qualifyingEntries.FindAll(item => item.eliminatedIn == "Q1");
            q1Eliminated.Sort((a, b) => GetQualifyingPhaseTime(a, 1).CompareTo(GetQualifyingPhaseTime(b, 1)));
            AppendQualifyingResults(results, q1Eliminated, "Q1");

            for (int i = 0; i < results.Count; i++)
            {
                results[i].position = i + 1;
            }

            return results;
        }

        void EnsureQualifyingPhaseComplete(int phase)
        {
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(phase);
            if (active.Count == 0)
            {
                return;
            }

            for (int i = 0; i < active.Count; i++)
            {
                if (GetQualifyingPhaseTime(active[i], phase) <= 0f)
                {
                    if (active[i].isPlayer)
                    {
                        SetQualifyingPhaseTime(active[i], phase, InvalidQualifyingTime(phase));
                        active[i].invalidated = true;
                        SetPlayerQualifyingSectors(active[i], phase, GetQualifyingPhaseTime(active[i], phase), true);
                    }
                    else
                    {
                        SetAiQualifyingPhaseTime(active[i], phase, SimulateAiQualifyingTime(active[i], phase));
                    }
                }
            }

            ApplyQualifyingElimination(active, phase);
        }

        List<QualifyingSimEntry> ActiveQualifyingEntries(int phase)
        {
            if (phase == 1)
            {
                return new List<QualifyingSimEntry>(qualifyingEntries);
            }

            return qualifyingEntries.FindAll(item => string.IsNullOrEmpty(item.eliminatedIn) && GetQualifyingPhaseTime(item, phase - 1) > 0f);
        }

        void ApplyQualifyingElimination(List<QualifyingSimEntry> active, int phase)
        {
            active.Sort((a, b) => GetQualifyingPhaseTime(a, phase).CompareTo(GetQualifyingPhaseTime(b, phase)));
            for (int i = 0; i < active.Count; i++)
            {
                active[i].session = "Q" + phase;
                active[i].finalTime = GetQualifyingPhaseTime(active[i], phase);
            }

            if (phase >= 3)
            {
                return;
            }

            int eliminateCount = QualifyingEliminationCount(phase, active.Count);
            if (eliminateCount <= 0)
            {
                return;
            }

            for (int i = active.Count - eliminateCount; i < active.Count; i++)
            {
                active[i].eliminatedIn = "Q" + phase;
            }
        }

        int QualifyingEliminationCount(int phase, int activeCount)
        {
            if (phase == 1)
            {
                return Mathf.Clamp(activeCount - Q1SurvivorCount, 0, 6);
            }

            if (phase == 2)
            {
                return Mathf.Clamp(activeCount - Q2SurvivorCount, 0, 6);
            }

            return 0;
        }

        void AppendQualifyingResults(List<QualifyingResultEntry> results, List<QualifyingSimEntry> entries, string eliminatedIn)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                results.Add(new QualifyingResultEntry
                {
                    driverId = entries[i].driverId,
                    driverName = entries[i].driverName,
                    teamId = entries[i].teamId,
                    bestLapTime = entries[i].finalTime,
                    isPlayer = entries[i].isPlayer,
                    invalidated = entries[i].invalidated,
                    session = entries[i].session,
                    eliminatedIn = eliminatedIn
                });
            }
        }

        float SimulateAiQualifyingTime(QualifyingSimEntry entry, int phase)
        {
            float firstRun = SimulateAiQualifyingRun(entry, phase, false);
            float secondRun = SimulateAiQualifyingRun(entry, phase, true);
            float consistency = entry.driverData == null ? 80f : entry.driverData.consistency;
            float qualifying = entry.driverData == null ? 82f : entry.driverData.qualifying;
            float improveChance = Mathf.Lerp(0.46f, 0.78f, consistency / 100f);
            if (Random.value < improveChance)
            {
                secondRun -= Random.Range(0.04f, Mathf.Lerp(0.18f, 0.46f, qualifying / 100f));
            }
            else
            {
                secondRun += Random.Range(0.02f, 0.28f);
            }

            return Mathf.Min(firstRun, secondRun);
        }

        float SimulatePlayerQualifyingTime(QualifyingSimEntry entry, int phase)
        {
            float firstRun = SimulateAiQualifyingRun(entry, phase, false);
            float secondRun = SimulateAiQualifyingRun(entry, phase, true);
            float qualifying = entry.driverData == null ? 78f : entry.driverData.qualifying;
            float consistency = entry.driverData == null ? 78f : entry.driverData.consistency;
            float secondRunGain = Mathf.Lerp(0.03f, 0.34f, Mathf.Clamp01((qualifying + consistency) / 200f));
            secondRun -= Random.Range(0f, secondRunGain);
            float best = Mathf.Min(firstRun, secondRun);
            best += PlayerQualifyingTyreWeatherPenalty(Settings == null ? TyreCompound.Medium : Settings.SelectedTyreCompound);
            best += Random.Range(-0.06f, 0.12f);
            return Mathf.Max(20f, best);
        }

        float PlayerQualifyingTyreWeatherPenalty(TyreCompound compound)
        {
            WeatherState weather = Track == null ? WeatherState.Clear : Track.weather;
            if (weather == WeatherState.HeavyRain)
            {
                if (compound == TyreCompound.Wet)
                {
                    return -0.12f;
                }

                if (compound == TyreCompound.Intermediate)
                {
                    return 1.45f;
                }

                return 5.4f;
            }

            if (weather == WeatherState.LightRain)
            {
                if (compound == TyreCompound.Intermediate)
                {
                    return -0.12f;
                }

                if (compound == TyreCompound.Wet)
                {
                    return 0.74f;
                }

                return 2.75f;
            }

            if (compound == TyreCompound.Soft)
            {
                return -0.18f;
            }

            if (compound == TyreCompound.Medium)
            {
                return 0.08f;
            }

            if (compound == TyreCompound.Hard)
            {
                return 0.34f;
            }

            return compound == TyreCompound.Intermediate ? 1.7f : 3.1f;
        }

        float SimulateAiQualifyingRun(QualifyingSimEntry entry, int phase, bool secondRun)
        {
            float baseLap = Mathf.Max(22f, Track.length / 52f);
            DriverData driver = entry.driverData;
            CarPerformanceData car = entry.carData;
            float consistency = driver == null ? 80f : driver.consistency;
            float qualifying = driver == null ? 82f : driver.qualifying;
            float pace = driver == null ? 82f : driver.pace;
            float confidence = driver == null ? 80f : driver.experience;
            float tyreManagement = driver == null ? 80f : driver.tyreManagement;
            float carRating = car == null ? 84f : car.cornering * 0.34f + car.enginePower * 0.24f + car.aeroEfficiency * 0.24f + car.braking * 0.18f;
            float driverEffect = (qualifying - 88f) * -0.048f + (pace - 88f) * -0.016f + (confidence - 80f) * -0.005f;
            float carEffect = (carRating - 86f) * -0.052f;
            float difficulty = Settings.Difficulty == RaceDifficulty.Easy ? 0.85f : Settings.Difficulty == RaceDifficulty.Medium ? 0.05f : Settings.Difficulty == RaceDifficulty.Hard ? -0.35f : -0.65f;
            float phaseEffect = phase == 1 ? 0.08f : (phase == 2 ? -0.18f : -0.36f);
            float tyrePrep = Mathf.Lerp(0.14f, 0.0f, tyreManagement / 100f) + Random.Range(0f, 0.04f);
            float variance = Mathf.Lerp(0.24f, 0.035f, consistency / 100f);
            float runVariance = Random.Range(-variance, variance);
            float secondRunPressure = secondRun ? Random.Range(-0.08f, 0.05f) : 0f;
            return baseLap + driverEffect + carEffect + difficulty + phaseEffect + tyrePrep + WeatherQualifyingPenalty(driver) + QualifyingMistakePenalty(driver, phase) + runVariance + secondRunPressure;
        }

        float WeatherQualifyingPenalty(DriverData driver)
        {
            if (Track.weather == WeatherState.Clear)
            {
                return 0f;
            }

            if (Track.weather == WeatherState.Cloudy)
            {
                return 0.04f;
            }

            float wetSkill = driver == null ? 80f : driver.wetSkill;
            float basePenalty = Track.weather == WeatherState.HeavyRain ? 2.65f : 1.25f;
            return basePenalty * Mathf.Lerp(1.18f, 0.42f, wetSkill / 100f);
        }

        float QualifyingMistakePenalty(DriverData driver, int phase)
        {
            float consistency = driver == null ? 80f : driver.consistency;
            float awareness = driver == null ? 80f : driver.awareness;
            float chance = Mathf.Lerp(0.075f, 0.012f, consistency / 100f);
            if (Track.weather == WeatherState.LightRain)
            {
                chance += 0.025f;
            }
            else if (Track.weather == WeatherState.HeavyRain)
            {
                chance += 0.045f;
            }

            if (phase == 3)
            {
                chance += 0.008f;
            }

            if (Random.value > chance)
            {
                return 0f;
            }

            float penalty = Random.Range(0.25f, 1.4f) * Mathf.Lerp(1.35f, 0.65f, awareness / 100f);
            if (Random.value < 0.12f)
            {
                penalty += Random.Range(2.0f, 4.5f);
            }

            return penalty;
        }

        float SimulateFallbackPlayerQualifyingTime(RaceParticipant participant)
        {
            CarPerformanceData car = participant == null ? null : participant.carData;
            float carEffect = car == null ? 0f : ((car.cornering + car.enginePower) / 2f - 82f) * -0.04f;
            return Mathf.Max(56f, Track.length / 80f + 4.8f + carEffect);
        }

        float InvalidQualifyingTime(int phase)
        {
            return 9998f + Mathf.Clamp(phase, 1, 3) * 0.1f;
        }

        float GetQualifyingPhaseTime(QualifyingSimEntry entry, int phase)
        {
            return phase == 1 ? entry.q1 : (phase == 2 ? entry.q2 : entry.q3);
        }

        void SetQualifyingPhaseTime(QualifyingSimEntry entry, int phase, float time)
        {
            if (phase == 1)
            {
                entry.q1 = time;
            }
            else if (phase == 2)
            {
                entry.q2 = time;
            }
            else
            {
                entry.q3 = time;
            }

            entry.session = "Q" + phase;
            entry.finalTime = time;
        }

        void SetAiQualifyingPhaseTime(QualifyingSimEntry entry, int phase, float time)
        {
            SetQualifyingPhaseTime(entry, phase, time);
            float s1;
            float s2;
            float s3;
            SimulateQualifyingSectors(entry, phase, time, out s1, out s2, out s3);
            SetQualifyingPhaseSectors(entry, phase, s1, s2, s3);
            if (State != null && entry.participant != null)
            {
                State.OnSectorComplete(entry.participant, 1, s1, false);
                State.OnSectorComplete(entry.participant, 2, s2, false);
                State.OnSectorComplete(entry.participant, 3, s3, false);
            }
        }

        void SetSimulatedPlayerQualifyingPhaseTime(QualifyingSimEntry entry, int phase, float time)
        {
            SetQualifyingPhaseTime(entry, phase, time);
            entry.invalidated = false;
            float s1;
            float s2;
            float s3;
            SimulateQualifyingSectors(entry, phase, time, out s1, out s2, out s3);
            int phaseIndex = Mathf.Clamp(phase, 1, 3) - 1;
            playerQualifyingBestTimes[phaseIndex] = time;
            playerQualifyingBestSectors[phaseIndex, 0] = s1;
            playerQualifyingBestSectors[phaseIndex, 1] = s2;
            playerQualifyingBestSectors[phaseIndex, 2] = s3;
            SetQualifyingPhaseSectors(entry, phase, s1, s2, s3);
            if (State != null && entry.participant != null)
            {
                State.OnSectorComplete(entry.participant, 1, s1, false);
                State.OnSectorComplete(entry.participant, 2, s2, false);
                State.OnSectorComplete(entry.participant, 3, s3, false);
            }
        }

        void SetPlayerQualifyingSectors(QualifyingSimEntry entry, int phase, float lapTime, bool invalidated)
        {
            LapTracker lap = PlayerParticipant == null ? null : PlayerParticipant.lapTracker;
            int phaseIndex = Mathf.Clamp(phase, 1, 3) - 1;
            float s1 = invalidated ? 0f : playerQualifyingBestSectors[phaseIndex, 0];
            float s2 = invalidated ? 0f : playerQualifyingBestSectors[phaseIndex, 1];
            float s3 = invalidated ? 0f : playerQualifyingBestSectors[phaseIndex, 2];
            if (s1 <= 0f || s2 <= 0f || s3 <= 0f)
            {
                s1 = lap == null ? 0f : lap.LastSector1Time;
                s2 = lap == null ? 0f : lap.LastSector2Time;
                s3 = lap == null ? 0f : lap.LastSector3Time;
            }
            if (s1 <= 0f || s2 <= 0f || s3 <= 0f)
            {
                s1 = lapTime * 0.333f;
                s2 = lapTime * 0.334f;
                s3 = Mathf.Max(0.001f, lapTime - s1 - s2);
            }

            SetQualifyingPhaseSectors(entry, phase, s1, s2, s3);
            if (!invalidated && State != null && entry.participant != null)
            {
                State.OnSectorComplete(entry.participant, 1, s1, false);
                State.OnSectorComplete(entry.participant, 2, s2, false);
                State.OnSectorComplete(entry.participant, 3, s3, false);
            }
        }

        void SimulateQualifyingSectors(QualifyingSimEntry entry, int phase, float lapTime, out float s1, out float s2, out float s3)
        {
            float consistency = entry.driverData == null ? 80f : entry.driverData.consistency;
            float spread = Mathf.Lerp(0.028f, 0.008f, consistency / 100f);
            float w1 = 0.334f + Random.Range(-spread, spread);
            float w2 = 0.332f + Random.Range(-spread, spread);
            float w3 = Mathf.Max(0.25f, 1f - w1 - w2);
            float total = w1 + w2 + w3;
            s1 = lapTime * w1 / total;
            s2 = lapTime * w2 / total;
            s3 = Mathf.Max(0.001f, lapTime - s1 - s2);
        }

        void SetQualifyingPhaseSectors(QualifyingSimEntry entry, int phase, float s1, float s2, float s3)
        {
            if (phase == 1)
            {
                entry.q1s1 = s1;
                entry.q1s2 = s2;
                entry.q1s3 = s3;
            }
            else if (phase == 2)
            {
                entry.q2s1 = s1;
                entry.q2s2 = s2;
                entry.q2s3 = s3;
            }
            else
            {
                entry.q3s1 = s1;
                entry.q3s2 = s2;
                entry.q3s3 = s3;
            }
        }

        class QualifyingSimEntry
        {
            public RaceParticipant participant;
            public DriverData driverData;
            public CarPerformanceData carData;
            public string driverId;
            public string driverName;
            public string teamId;
            public bool isPlayer;
            public float q1;
            public float q2;
            public float q3;
            public float q1s1;
            public float q1s2;
            public float q1s3;
            public float q2s1;
            public float q2s2;
            public float q2s3;
            public float q3s1;
            public float q3s2;
            public float q3s3;
            public float finalTime;
            public bool invalidated;
            public string session;
            public string eliminatedIn;
        }

        class SectorSnapshot
        {
            public float s1;
            public float s2;
            public float s3;
        }
    }
}
