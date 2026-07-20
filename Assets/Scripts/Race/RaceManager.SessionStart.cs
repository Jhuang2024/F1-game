using F1Game.Core.Diagnostics;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager session-start subsystem (partial). The public session entry
    /// points that bootstrap a session from the data repository and settings -
    /// StartRace, StartTimeTrial and StartSession (build the field, grid, lighting,
    /// weather and player car, then hand off to the live loop) plus CycleToNextTrack.
    /// Split out of the RaceManager monolith verbatim - same class, same members,
    /// identical setup order, RNG call order and tuned values; the sector-colour and
    /// session-cap consts stay in main, and the public entry points stay public so
    /// GameBootstrap and the menus resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
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

        // Track test: jump straight to the next calendar circuit in a fresh time trial.
        // Bound to F2 while a time trial is running so all 24 layouts can be checked fast.
        public void CycleToNextTrack()
        {
            if (!IsTimeTrial || Data == null || Data.Calendar == null || Data.Calendar.events.Count == 0)
            {
                return;
            }

            int currentIndex = EventData == null ? -1 : Data.Calendar.events.IndexOf(EventData);
            CalendarEventData next = Data.Calendar.events[(currentIndex + 1 + Data.Calendar.events.Count) % Data.Calendar.events.Count];
            StartTimeTrial(Data, Career, Settings, ui, next, Career.Save.playerDriverName, Career.Save.playerTeamId);
        }

        // Legends Championship race: a single race weekend against the all-time
        // greats. careerRace is false (so it never hits CareerManager), and the
        // legends manager is armed so the field becomes legendary (LegendaryDriversOn)
        // and results route to LegendsManager (RaceManager.Results).
        public void StartLegendsRace(
            GameDataRepository repository,
            CareerManager career,
            GameSettingsStore settings,
            RuntimeUi runtimeUi,
            CalendarEventData eventData,
            string playerName,
            string playerTeamId,
            LegendsManager legends)
        {
            pendingLegends = legends;
            StartSession(repository, career, settings, runtimeUi, eventData, playerName, playerTeamId, false, RaceWeekendSession.QuickRace);
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
            // Legends context is armed by StartLegendsRace right before this call
            // and consumed here, so every other session-start path (career, quick
            // race, time trial) resets cleanly to "not a legends race".
            Legends = pendingLegends;
            pendingLegends = null;
            IsLegendsRace = Legends != null;
            // Part 21 regulation hook: reset every session start (not just career
            // ones) so Quick Race/Time Trial always get the neutral 1f default
            // regardless of whatever a career season's regulation last set it to.
            // Time Trial removes tyre wear entirely (per request): a hot-lap mode
            // is a pure lap-time exercise, so the tyre must never degrade under
            // the player across a long practice session. A 0 multiplier holds Wear
            // pinned at full for the whole run (see TyreState.Tick, where wearLoss
            // is scaled by this). Every other session keeps the career/neutral
            // value, and this resets each session start so it never leaks out.
            TyreState.RegulationWearMultiplier = pendingTimeTrial ? 0f
                : (careerRace && career != null && career.Save != null) ? career.Save.currentSeasonTyreWearMultiplier : 1f;
            IsTimeTrial = pendingTimeTrial;
            pendingTimeTrial = false;
            CurrentSession = session;
            IsRaceFinished = false;
            IsPaused = false;
            qualifyingTransitionPending = false;
            qualifyingTransitionTimer = 0f;
            QualifyingFeedbackText = "";
            lastQualifyingResultWasSimulated = false;
            lightsOutTime = 0f;
            playerReactionTime = -1f;
            reactionDisplayTimer = 0f;
            waitingForPlayerReaction = false;
            lastRecordedPlayerBestLap = 0f;
            ersRefillLapMarker = -1;
            playerResetCooldown = 0f;
            ResetEngineerState();
            ResetRaceControlState();
            if (session == RaceWeekendSession.Qualifying && !preserveQualifyingState)
            {
                qualifyingPhase = 1;
                qualifyingEntries.Clear();
                ResetPlayerQualifyingCaptures();
            }
            preserveQualifyingState = false;
            raceStartSequenceDuration = session == RaceWeekendSession.Qualifying || IsTimeTrial
                ? StartProcedureRules.NonRaceSequenceSeconds
                : StartProcedureRules.RaceSequenceDuration(Random.value);
            StartCountdown = raceStartSequenceDuration;
            lastStartLightCountPlayed = -1;
            lastRestartLightCountPlayed = -1;
            SessionMessage = session == RaceWeekendSession.Qualifying ? "Q" + qualifyingPhase + " out lap ready"
                : (IsTimeTrial ? "Time trial: set a lap"
                : (session == RaceWeekendSession.Practice ? "Practice: drive your program laps" : "Race start"));
            Time.timeScale = 1f;

            // Tear the frontend (tyre / strategy select screen) down IMMEDIATELY on
            // race start, before any race-world construction runs. The HUD swap that
            // normally clears it is the very last thing this method does, so if any
            // setup step below were to throw, the strategy screen would otherwise be
            // left mounted on top of the race with only a logged exception to show
            // for it. Clearing here makes the transition robust: the tyre-selection
            // screen can never survive into a live session, independent of anything
            // that happens during spawn/track/telemetry setup.
            if (ui != null)
            {
                ui.Clear();
            }

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
            // Time trials always run dry: hot-lap times only mean anything under
            // repeatable conditions, so the event's wet/mixed forecast is ignored.
            trackManager.forceDryWeather = IsTimeTrial;
            Track = trackManager.Build(eventData, Settings.Current.racingLineAssist);
            // The probe created in CreateLighting only covered a 520m box at the
            // origin (start/finish) and rendered before the world existed - refit
            // it over the whole circuit now that the track is built, so the
            // reflective road look reaches the full lap.
            FitReflectionProbeToTrack();
            telemetryCorners = Track.ClassifyCorners();
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
            SimpleAudioManager.SetRaceAmbience(true);
            GameLog.Info("[RoadPhysics] Race start roadColliderExists=" + (Track.roadCollider != null) +
                      " roadLayer=" + (Track.roadCollider == null ? "none" : LayerMask.LayerToName(Track.roadCollider.gameObject.layer)) +
                      " roadIsTrigger=" + (Track.roadCollider != null && Track.roadCollider.isTrigger) +
                      " roadCollidesWithDefaultCars=" + (Track.roadCollider != null && !Physics.GetIgnoreLayerCollision(Track.roadCollider.gameObject.layer, 0)));
            // Select the active track-query backend for this race: the authored
            // adapter for the reference circuit (or when forced), else the legacy
            // TrackRuntime. This is the live call site for the authored-track path.
            try
            {
                TrackQueryProvider.Select(EventData != null ? EventData.trackId : null, Track);
            }
            catch (System.Exception trackQueryException)
            {
                // The active track-query backend is an interim, non-essential seam
                // for the legacy race path (nothing on the live legacy loop reads
                // it yet). A failure selecting it must never abort race start or
                // leave the frontend stuck.
                GameLog.Warn(LogCategory.Track, "TrackQuery select failed; continuing without it: " + trackQueryException);
            }

            SpawnRaceGrid(playerName, playerTeamId, careerRace);
            replayCapture.Begin(Participants, RaceElapsed);
            telemetryCapture.Begin();
            SpawnGhostIfAvailable();
            PostEngineerMessage(OpeningEngineerMessage(), true);
            engineerWeatherSent = true;
            raceStartTime = Time.time + StartCountdown;
            // Exactly one HUD: the production HudRoot when the production UI owns
            // the frontend, otherwise the legacy RaceHud. Never both.
            if (!ProductionSessionUi.TryShowRaceHud())
            {
                ui.ShowRaceHud(this, PlayerParticipant);
            }

            LogPlayerSpawnPhysics();

            // Cinematic camera integration (default-off; camera-only modes during a
            // live race). Created last so the grid + player camera already exist, and
            // parented to raceWorld so it tears down with the session.
            SetupCinematicDirector();
        }

    }
}
