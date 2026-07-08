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

        // ---------- Race control / safety car ----------
        public enum RaceControlState { Green, YellowSector, VirtualSafetyCar, SafetyCarDeploying, SafetyCarActive, SafetyCarInThisLap, Restart }
        enum IncidentSeverity { Minor, Medium, Major }

        public RaceControlState CurrentRaceControlState { get; private set; } = RaceControlState.Green;
        // Absolute target speed for a full safety car period; only meaningful while
        // CurrentRaceControlState == SafetyCarActive.
        public float SafetyCarTargetSpeedKph { get; private set; } = 150f;
        // Percentage pace reduction under a Virtual Safety Car (no physical car, just
        // a pace delta) - only meaningful while CurrentRaceControlState == VirtualSafetyCar.
        public float VirtualSafetyCarPaceMultiplier { get; private set; } = 0.62f;
        public bool IsPitLaneOpen { get; private set; } = true;
        public bool IsOvertakingAllowed { get; private set; } = true;
        // -1 when no sector-local yellow is active; otherwise the 1-3 sector index
        // overtaking is currently banned in, independent of the full SC/VSC ban above.
        public int YellowFlagSector { get; private set; } = -1;
        public int IncidentCount { get; private set; }
        public int SafetyCarDeploymentCount { get; private set; }
        public int AiOvertakesCompletedCount { get; private set; }

        // Expert-only determinism switch (Part A.2): a handful of specific RNG gates
        // - the overtake attack-trigger roll, the ERS attack/defend racecraft-timing
        // roll, the defend-cover roll and the DRS commit-per-zone roll - resolve to
        // "always act once the surrounding condition is met" for Expert instead of
        // rolling dice for permission to race. Kept as one named constant + property
        // so every Expert-specific branch reads clearly instead of scattering an
        // unexplained `difficulty == RaceDifficulty.Expert` check at each site.
        const bool ExpertIsRuthless = true;
        public bool IsExpertDifficulty { get { return ExpertIsRuthless && Settings != null && Settings.Difficulty == RaceDifficulty.Expert; } }

        float raceControlCheckTimer;
        const float RaceControlCheckInterval = 0.35f;
        float safetyCarTimer;
        float restartControlTimer;
        bool safetyCarInThisLapMessageSent;
        bool coldTyresRestartWarningSent;
        bool playerScPitPromptSent;
        float yellowSectorClearTimer;
        // Part 2: per-sector and global cooldowns so yellow flags read as
        // localized, occasional warnings instead of a constant banner spam -
        // a sector that just cleared can't immediately re-trigger a fresh
        // episode, and a run of minor incidents anywhere on track can't chain
        // into back-to-back banners either.
        readonly Dictionary<int, float> yellowSectorCooldownUntil = new Dictionary<int, float>();
        float globalMinorYellowCooldownUntil;
        float yellowSectorEpisodeStartTime = -999f;
        // Part 3 retune: raised again (was 20/25) so a sector that just cleared,
        // or a run of scattered minor incidents anywhere on track, genuinely
        // cannot re-trigger a fresh banner for a good while - yellows should
        // read as occasional and localized, not a recurring background noise.
        const float YellowSectorCooldownAfterClearSeconds = 35f;
        const float GlobalMinorYellowCooldownSeconds = 40f;
        const float MaxYellowEpisodeSeconds = 26f;
        float drsRestartCooldownTimer;
        RaceParticipant safetyCarQueueLeader;
        float lastIncidentTime = -999f;
        float lastIncidentDistance = -99999f;

        // Part 1: the real, visible AI safety car - built lazily the first time
        // it's needed each session and reused for every deployment within that
        // session rather than instantiated fresh each time.
        GameObject safetyCarObject;
        SafetyCarController safetyCarController;
        readonly HashSet<RaceParticipant> aheadOfSafetyCarLastTick = new HashSet<RaceParticipant>();
        // Blocks a NEW VSC/SC escalation for a while after the field returns to
        // Green, so one incident's aftermath can't chain into a second SC/VSC the
        // moment the first ends - yellow flags themselves are unaffected.
        float postEscalationCooldownTimer;
        // Part 3 retune: lengthened again (was 90s) - a real cooling-off period
        // long enough that a full SC/VSC period genuinely reads as a rare,
        // meaningful event rather than something that can recur a couple of
        // times in one short race.
        const float PostEscalationCooldownSeconds = 110f;

        // Part 1/2 retune: how long a car must be nearly stationary, with none of
        // the legitimate exclusions active, before race control ever calls it
        // ActuallyStranded - and how much longer still before it is retired
        // outright. Both raised well past the old 5s/12s so a normal brief stop
        // (gathering the car after a spin, waiting for a gap) never reaches this
        // far. RecoveryGraceSeconds is the window after a spin/contact event
        // during which the sustained-stop timer can't accumulate at all, giving
        // the car a real chance to drive away before it's ever considered.
        // Part 3 retune: raised again (was 11/26) so declaring a car genuinely
        // stranded takes a real, sustained stop rather than a long-but-not-that-
        // long pause.
        const float StrandedDeclareSeconds = 14f;
        const float StrandedRetireSeconds = 30f;
        const float RecoveryGraceSeconds = 4f;

        // Player race-control pace-limiter compliance tracking (Task 2/3): how long
        // the player has been meaningfully over the current VSC/SC cap, and whether
        // a warning has already been issued this violation - escalates to a time
        // penalty only if they stay grossly over after being warned.
        float playerRaceControlOverspeedTimer;
        bool playerRaceControlWarningSent;
        public bool IsPlayerOverRaceControlPace { get; private set; }
        public bool IsPlayerRaceControlWarningActive { get; private set; }

        RuntimeUi ui;
        readonly List<RaceParticipant> emptyParticipants = new List<RaceParticipant>();
        GameObject raceWorld;
        float raceStartTime;
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
        float engineerMessageAnimTimer;
        float engineerCooldown;

        // Part 1: real radio message queue. PostEngineerMessage used to just
        // overwrite engineerMessageText/engineerMessageTimer outright, so two
        // messages triggered in the same window silently stepped on each other.
        // Now non-priority lines queue up (capped so a chatty session can't spam
        // a wall of messages) and priority lines interrupt whatever is currently
        // showing but do not wipe what's already queued behind it.
        struct EngineerMessageEntry { public string text; public float duration; }
        readonly List<EngineerMessageEntry> engineerMessageQueue = new List<EngineerMessageEntry>();
        const int EngineerMessageQueueCap = 4;
        const float EngineerMessageAnimInDuration = 0.35f;
        const float EngineerMessageAnimOutDuration = 0.4f;
        public int EngineerMessageQueueDepth { get { return engineerMessageQueue.Count; } }
        // 0-1 slide/fade progress: rises for the first EngineerMessageAnimInDuration
        // seconds after a message starts, sits at 1 while it holds, then falls back
        // toward 0 over the last EngineerMessageAnimOutDuration seconds before the
        // next message takes over - the HUD reads this to slide/fade the radio card.
        public float EngineerMessageAnimProgress01
        {
            get
            {
                if (engineerMessageTimer <= 0f)
                {
                    return 0f;
                }

                float inProgress = Mathf.Clamp01(engineerMessageAnimTimer / EngineerMessageAnimInDuration);
                float outProgress = Mathf.Clamp01(engineerMessageTimer / EngineerMessageAnimOutDuration);
                return Mathf.Min(inProgress, outProgress);
            }
        }

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
        bool engineerRivalSent;
        bool engineerTrackLimitsSent;
        // Part C.1: Expert-only radio warnings.
        bool engineerExpertWarningSent;
        float engineerDrsWarningCooldown;
        int lastGapReportLap = -1;
        bool weatherTransitionDone;
        bool weatherSecondTransitionDone;

        // Part 1: extra atmosphere/feedback state - overtake notifications,
        // session-fastest-lap tracking, teammate gap callouts, flat spot/lockup
        // warnings and the HUD toast relay queue.
        int playerLastPosition = -1;
        float overtakeCheckTimer;
        float sessionFastestLap = -1f;
        string sessionFastestLapDriverId = "";
        bool engineerFlatSpotWarningSent;
        bool engineerLockupWarningSent;
        int lastTeammateGapReportLap = -1;
        bool engineerPodiumMessageSent;
        struct HudToast { public string text; public int colorKind; }
        readonly Queue<HudToast> hudToastQueue = new Queue<HudToast>();
        const int HudToastQueueCap = 6;

        // HUD toast color kinds - kept as small ints so RaceManager (which has no
        // UI dependency) doesn't need to reference UiFactory colors directly;
        // RaceHud maps these back to its own palette when it drains the queue.
        public const int ToastColorNeutral = 0;
        public const int ToastColorGreen = 1;
        public const int ToastColorAmber = 2;
        public const int ToastColorCyan = 3;
        public const int ToastColorPurple = 4;
        public const int ToastColorAccent = 5;

        void QueueHudToast(string text, int colorKind)
        {
            if (string.IsNullOrEmpty(text) || hudToastQueue.Count >= HudToastQueueCap)
            {
                return;
            }

            hudToastQueue.Enqueue(new HudToast { text = text, colorKind = colorKind });
        }

        public bool TryDequeueHudToast(out string text, out int colorKind)
        {
            if (hudToastQueue.Count == 0)
            {
                text = "";
                colorKind = ToastColorNeutral;
                return false;
            }

            HudToast toast = hudToastQueue.Dequeue();
            text = toast.text;
            colorKind = toast.colorKind;
            return true;
        }
        float lastRecordedPlayerBestLap;
        bool pendingTimeTrial;
        float playerResetCooldown;
        // Pit lane release control: one car released at a time with a safe gap.
        float nextPitReleaseAllowedTime;
        const float PitReleaseGapSeconds = 1.6f;
        float stackResolveTimer;
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
            lastQualifyingResultWasSimulated = false;
            lightsOutTime = 0f;
            playerReactionTime = -1f;
            reactionDisplayTimer = 0f;
            waitingForPlayerReaction = false;
            lastRecordedPlayerBestLap = 0f;
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
            GameLog.Info("[RoadPhysics] Race start roadColliderExists=" + (Track.roadCollider != null) +
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
            SimQualifyingExplanation = "";
            for (int i = 0; i < playerSimBreakdowns.Length; i++)
            {
                playerSimBreakdowns[i] = null;
            }

            ResetPlayerQualifyingCaptures();
            ResetQualifyingSectorState();

            raceWorld = new GameObject("Runtime simulated qualifying world");
            TrackManager trackManager = new GameObject("Track Manager").AddComponent<TrackManager>();
            trackManager.transform.SetParent(raceWorld.transform);
            Track = trackManager.Build(eventData, false);
            SimpleAudioManager.SetRain(Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain);

            BuildSimulatedQualifyingField(playerName, playerTeamId);

            // Deterministic seed: the same weekend simulated twice produces the same
            // session, so results are reproducible and debuggable rather than dice.
            Random.State previousRandomState = Random.state;
            int seasonPart = Career != null && Career.Save != null ? Career.Save.currentSeason * 8887 + Career.Save.currentRound * 331 : 17;
            int trackPart = eventData != null && !string.IsNullOrEmpty(eventData.trackId) ? eventData.trackId.GetHashCode() : 0;
            int teamPart = string.IsNullOrEmpty(playerTeamId) ? 0 : playerTeamId.GetHashCode();
            Random.InitState(seasonPart ^ (trackPart * 31) ^ teamPart);

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

            Random.state = previousRandomState;

            List<QualifyingResultEntry> results = BuildFinalQualifyingResults();
            lastQualifyingResults = results;
            SimQualifyingExplanation = BuildSimQualifyingExplanation(results);
            if (IsCareerRace && Career != null)
            {
                Career.ApplyQualifyingResults(EventData, results);
            }

            ui.ShowQualifyingResults(this, results, IsCareerRace);
        }

        // Full transparency for the simulated player lap: every contribution to the
        // final time, plus the exact elimination reason if the player went out.
        public string SimQualifyingExplanation { get; private set; }

        string BuildSimQualifyingExplanation(List<QualifyingResultEntry> results)
        {
            QualifyingSimEntry player = qualifyingEntries.Find(item => item.isPlayer);
            if (player == null)
            {
                return "";
            }

            int decisivePhase = string.IsNullOrEmpty(player.eliminatedIn) ? 3 : int.Parse(player.eliminatedIn.Substring(1));
            QualifyingLapBreakdown breakdown = playerSimBreakdowns[Mathf.Clamp(decisivePhase, 1, 3) - 1];
            QualifyingResultEntry playerResult = results == null ? null : results.Find(entry => entry.isPlayer);
            int position = playerResult != null ? playerResult.position : 0;

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("YOUR ").Append("Q").Append(decisivePhase).Append(" LAP, ITEMIZED\n");
            if (breakdown != null)
            {
                text.Append("Circuit reference lap    ").Append(UiFactory.FormatTime(breakdown.baseLap)).Append("\n");
                text.Append("Car package              ").Append(SignedSeconds(breakdown.carEffect)).Append("\n");
                text.Append("Driver qualifying craft  ").Append(SignedSeconds(breakdown.driverEffect)).Append("\n");
                text.Append("AI difficulty setting    ").Append(SignedSeconds(breakdown.difficultyEffect)).Append("\n");
                text.Append("Track evolution (Q").Append(decisivePhase).Append(")     ").Append(SignedSeconds(breakdown.phaseEffect)).Append("\n");
                text.Append("Tyre preparation         ").Append(SignedSeconds(breakdown.tyrePrep)).Append("\n");
                text.Append("Tyre choice (").Append(Settings == null ? "Medium" : Settings.SelectedTyreCompound.ToString()).Append(")     ").Append(SignedSeconds(breakdown.tyreChoicePenalty)).Append("\n");
                text.Append("Weather                  ").Append(SignedSeconds(breakdown.weatherPenalty)).Append("\n");
                if (breakdown.mistakePenalty > 0.001f)
                {
                    text.Append("Driver mistake           ").Append(SignedSeconds(breakdown.mistakePenalty)).Append(breakdown.mistakePenalty > 1.8f ? "  (major error)" : "  (small error)").Append("\n");
                }
                else
                {
                    text.Append("Driver mistake           clean lap\n");
                }

                text.Append("Natural variance         ").Append(SignedSeconds(breakdown.variance)).Append("\n");
                text.Append("FINAL LAP                ").Append(UiFactory.FormatTime(breakdown.finalTime)).Append("\n\n");
            }

            text.Append("Classified P").Append(position > 0 ? position.ToString() : "--");
            if (!string.IsNullOrEmpty(player.eliminatedIn))
            {
                float cutoff = QualifyingCutoffTime(decisivePhase);
                float playerTime = GetQualifyingPhaseTime(player, decisivePhase);
                text.Append("  |  ELIMINATED IN ").Append(player.eliminatedIn);
                if (cutoff > 0f && playerTime > 0f && playerTime < 9998f)
                {
                    text.Append("  (missed the cut by ").Append(Mathf.Max(0f, playerTime - cutoff).ToString("0.000")).Append("s)");
                }
                else if (playerTime >= 9998f)
                {
                    text.Append("  (no valid time set)");
                }
            }
            else
            {
                text.Append("  |  Advanced to the final shootout");
            }

            return text.ToString();
        }

        // Slowest surviving time in a phase: the reference a player had to beat.
        float QualifyingCutoffTime(int phase)
        {
            int survivors = phase == 1 ? Q1SurvivorCount : (phase == 2 ? Q2SurvivorCount : qualifyingEntries.Count);
            List<QualifyingSimEntry> ranked = new List<QualifyingSimEntry>();
            for (int i = 0; i < qualifyingEntries.Count; i++)
            {
                float time = GetQualifyingPhaseTime(qualifyingEntries[i], phase);
                if (time > 0f)
                {
                    ranked.Add(qualifyingEntries[i]);
                }
            }

            ranked.Sort((a, b) => GetQualifyingPhaseTime(a, phase).CompareTo(GetQualifyingPhaseTime(b, phase)));
            if (ranked.Count == 0 || survivors <= 0 || survivors > ranked.Count)
            {
                return 0f;
            }

            float cutoff = GetQualifyingPhaseTime(ranked[survivors - 1], phase);
            return cutoff >= 9998f ? 0f : cutoff;
        }

        static string SignedSeconds(float value)
        {
            return (value >= 0f ? "+" : "") + value.ToString("0.000") + "s";
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

                if (participant.vehicle != null)
                {
                    if (participant.vehicle.ErsDeploying) participant.ersDeployFrameCount++;
                    if (participant.vehicle.DrsActive) participant.drsActiveFrameCount++;
                }
            }

            ResolveLowSpeedStacks();
            SortRunningOrder();
            CheckIllegalOvertakesUnderYellow();
            UpdateOvertakeAndFastestLapNotifications();
            UpdateRaceEngineer();
            UpdateWeatherTransition();
            UpdateRaceControl();
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
            SimQualifyingExplanation = "";
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

        // Simple dynamic weather: on mixed-forecast races the conditions flip once
        // past half distance — rain arrives on a dry track, or a wet track starts
        // drying. Grip, tyre wear, audio and lighting mood all follow.
        void UpdateWeatherTransition()
        {
            // Part 19: Weather Variability setting. Off locks the session to its
            // starting state (no transition at all); High allows a second, later
            // swing on top of the usual half-distance one for a mixed forecast.
            int variability = Settings == null ? 2 : Settings.Current.weatherVariability;
            if (variability <= 0 || IsTimeTrial || CurrentSession == RaceWeekendSession.Qualifying ||
                Track == null || EventData == null || string.IsNullOrEmpty(EventData.weatherProfile) ||
                !EventData.weatherProfile.ToLowerInvariant().Contains("mixed") ||
                PlayerParticipant == null || PlayerParticipant.lapTracker == null)
            {
                return;
            }

            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            bool wantSecondSwing = variability >= 3 && weatherTransitionDone && !weatherSecondTransitionDone;
            if (!weatherTransitionDone)
            {
                if (completedLaps < Mathf.Max(1, RaceLaps / 2))
                {
                    return;
                }

                weatherTransitionDone = true;
            }
            else if (wantSecondSwing)
            {
                if (completedLaps < Mathf.Max(1, (RaceLaps * 3) / 4))
                {
                    return;
                }

                weatherSecondTransitionDone = true;
            }
            else
            {
                return;
            }

            bool wasRaining = Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain;
            WeatherState next = wasRaining ? WeatherState.Cloudy : WeatherState.LightRain;
            Track.weather = next;
            for (int i = 0; i < Participants.Count; i++)
            {
                if (Participants[i] != null && Participants[i].vehicle != null)
                {
                    Participants[i].vehicle.SetWeather(next);
                }
            }

            bool raining = next == WeatherState.LightRain || next == WeatherState.HeavyRain;
            SimpleAudioManager.SetRain(raining);
            RenderSettings.fogColor = raining ? new Color(0.28f, 0.34f, 0.36f) : RenderSettings.fogColor;
            RenderSettings.reflectionIntensity = raining ? 0.78f : 0.46f;
            PostEngineerMessage(raining
                ? "Rain is arriving. Grip is dropping, intermediates will come alive."
                : "The rain has stopped and the track is drying. Slicks will come to you.", true);
        }

        void ResetRaceControlState()
        {
            CurrentRaceControlState = RaceControlState.Green;
            SafetyCarTargetSpeedKph = 150f;
            VirtualSafetyCarPaceMultiplier = 0.62f;
            IsPitLaneOpen = true;
            IsOvertakingAllowed = true;
            YellowFlagSector = -1;
            IncidentCount = 0;
            SafetyCarDeploymentCount = 0;
            AiOvertakesCompletedCount = 0;
            raceControlCheckTimer = 0f;
            safetyCarTimer = 0f;
            restartControlTimer = 0f;
            safetyCarInThisLapMessageSent = false;
            coldTyresRestartWarningSent = false;
            playerScPitPromptSent = false;
            yellowSectorClearTimer = 0f;
            yellowSectorCooldownUntil.Clear();
            globalMinorYellowCooldownUntil = 0f;
            yellowSectorEpisodeStartTime = -999f;
            drsRestartCooldownTimer = 0f;
            safetyCarQueueLeader = null;
            lastIncidentTime = -999f;
            lastIncidentDistance = -99999f;
            // The previous session's safety car object (if any) lived under the
            // old raceWorld root and is already gone by the time a new session
            // resets this state - null the references so EnsureSafetyCarBuilt
            // rebuilds a fresh one under the new raceWorld instead of touching a
            // destroyed object.
            safetyCarObject = null;
            safetyCarController = null;
            aheadOfSafetyCarLastTick.Clear();
            raceControlOrderSnapshot.Clear();
            illegalOvertakePairCooldowns.Clear();
            restrictionActiveAtLastSnapshot = false;
            orderSnapshotTimer = 0f;
            safetyCarWatchdogTimer = 0f;
            safetyCarWatchdogRespawnCount = 0;
            if (State != null)
            {
                for (int i = 0; i < State.Participants.Count; i++)
                {
                    RaceParticipant participant = State.Participants[i];
                    if (participant == null)
                    {
                        continue;
                    }

                    participant.stoppedOnTrackTimer = 0f;
                    participant.wrongWayTimer = 0f;
                    participant.incidentCooldownTimer = 0f;
                    participant.previousSpeedKphForIncident = 0f;
                    participant.previousDamagePercentForIncident = 0f;
                    participant.incidentSpeedHistory0 = 0f;
                    participant.incidentSpeedHistory1 = 0f;
                    participant.incidentSpeedHistory2 = 0f;
                    participant.incidentDamageHistory0 = 0f;
                    participant.incidentDamageHistory1 = 0f;
                    participant.incidentDamageHistory2 = 0f;
                    participant.overtakesCompleted = 0;
                    participant.previousCarAheadForOvertakeCheck = null;
                }
            }
        }

        // Race control state machine: detects incidents across the field and drives
        // yellow flag / VSC / full safety car escalation, then de-escalates back to
        // green through a scripted restart. Skipped entirely in qualifying/time
        // trial where there is no field-wide incident model.
        void UpdateRaceControl()
        {
            if (Track == null || CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial || State == null)
            {
                return;
            }

            drsRestartCooldownTimer = Mathf.Max(0f, drsRestartCooldownTimer - Time.deltaTime);
            postEscalationCooldownTimer = Mathf.Max(0f, postEscalationCooldownTimer - Time.deltaTime);

            if (yellowSectorNumber >= 0)
            {
                yellowSectorClearTimer -= Time.deltaTime;
                if (yellowSectorClearTimer <= 0f)
                {
                    yellowSectorNumber = -1;
                    YellowFlagSector = -1;
                    if (CurrentRaceControlState == RaceControlState.YellowSector)
                    {
                        CurrentRaceControlState = RaceControlState.Green;
                    }
                }
            }

            raceControlCheckTimer -= Time.deltaTime;
            if (raceControlCheckTimer <= 0f)
            {
                raceControlCheckTimer = RaceControlCheckInterval;
                DetectIncidents();
            }

            DriveRaceControlStateMachine();
            UpdateSafetyCar();
            ApplyRaceControlSpeedCaps();
            if (Track != null)
            {
                // Safe to call every tick - TrackRuntime.SetRaceControlVisual no-ops
                // internally when the state hasn't actually changed.
                Track.SetRaceControlVisual((int)CurrentRaceControlState);
            }
        }

        // Scans every active participant for the incident types the brief calls
        // out - collisions, stopped/stranded cars, wrong-way, severe damage and
        // rare mechanical failure - and classifies anything found into a severity
        // that ApplyIncidentSeverity can turn into a yellow/VSC/SC escalation.
        void DetectIncidents()
        {
            int freqSetting = Settings == null ? 2 : Mathf.Clamp(Settings.Current.safetyCarFrequency, 0, 3);
            bool escalationAllowed = freqSetting > 0;
            // Retuned (Part 3): cut roughly in half again from the previous
            // 0/0.12/0.26/0.55 scale. Off still fully disables VSC/SC
            // escalation; Reduced is now "almost never", Standard is "rare",
            // High is "occasional but never chaotic" - a short prototype race
            // should usually run green start to finish unless something
            // genuinely serious happens.
            float freqScale = freqSetting == 0 ? 0f : (freqSetting == 1 ? 0.06f : (freqSetting == 3 ? 0.28f : 0.13f));
            bool preRace = StartCountdown > 0f;
            int mechanicalMode = Settings == null ? 2 : Settings.Current.mechanicalFailureMode;

            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null || participant.vehicle == null || participant.retired || participant.finished)
                {
                    continue;
                }

                float speedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);
                float damagePercent = participant.vehicle.Damage == null ? 0f : participant.vehicle.Damage.OverallPercent;
                bool inPitPhaseOrPitting = participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit;
                // Computed early (Part 3) so it can gate collision detection too,
                // not just the stranded/wrong-way checks further down - a car
                // race control is actively pacing/driving under SC/VSC must be
                // fully exempt from incident detection, not just from a subset
                // of the checks.
                bool paceLimited = IsRaceControlPaceLimited ||
                    CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                    CurrentRaceControlState == RaceControlState.Restart ||
                    participant.isRaceControlAutopilot;

                // Bug fix (Part B.1): the previous version required BOTH a >25kph
                // speed-drop AND a >3-point damage-jump landing in the exact same
                // 0.35s poll tick, comparing only against the immediately previous
                // tick. A real crash's speed loss and damage registration can easily
                // straddle two poll windows, a hard hit that already-slow car takes
                // might never show a full 25kph drop, and a spin that scrubs speed
                // without wall contact produces no damage at all - the AND-gate on a
                // single-tick comparison could miss all three. Now either signal
                // alone (OR, not AND) over a rolling ~1s window (peak speed / lowest
                // damage across the last three ticks, not just the last one) is
                // enough to register a collision-class incident.
                float recentPeakSpeed = Mathf.Max(participant.previousSpeedKphForIncident,
                    Mathf.Max(participant.incidentSpeedHistory0, Mathf.Max(participant.incidentSpeedHistory1, participant.incidentSpeedHistory2)));
                float recentMinDamage = Mathf.Min(participant.previousDamagePercentForIncident,
                    Mathf.Min(participant.incidentDamageHistory0, Mathf.Min(participant.incidentDamageHistory1, participant.incidentDamageHistory2)));
                participant.incidentSpeedHistory2 = participant.incidentSpeedHistory1;
                participant.incidentSpeedHistory1 = participant.incidentSpeedHistory0;
                participant.incidentSpeedHistory0 = participant.previousSpeedKphForIncident;
                participant.incidentDamageHistory2 = participant.incidentDamageHistory1;
                participant.incidentDamageHistory1 = participant.incidentDamageHistory0;
                participant.incidentDamageHistory0 = participant.previousDamagePercentForIncident;

                float speedDrop = recentPeakSpeed - speedKph;
                float damageJump = damagePercent - recentMinDamage;
                // speedSignal must NOT fire on ordinary hard braking: a normal
                // braking zone easily loses 40+ kph within this ~1s rolling window
                // (well past the old 15kph bar), which would otherwise flag every
                // hairpin on every lap as a "collision". Require the speed loss to
                // be largely unexplained by the driver's own brake input - a real
                // spin/impact loses speed involuntarily, intentional braking doesn't
                // count here (a hit that happens mid-braking is still caught by
                // damageSignal below, which has no such gate).
                // Part 3 retune: raised again (was 20) - collision-class detection
                // needs a bigger, clearly-involuntary speed loss before it counts,
                // so ordinary AI bunching/braking/wall brushes stay well clear.
                bool speedSignal = speedDrop > 26f && participant.vehicle.EffectiveBrake < 0.3f;
                // Part 3 retune: raised from 6 - only a real hit registers, not a
                // graze.
                bool damageSignal = damageJump > 9f;
                // Part 3: a car under SC/VSC pacing or race-control autopilot is
                // fully exempt from collision detection - its speed/pace is race
                // control's own doing, never a driver incident.
                bool collision = !preRace && !inPitPhaseOrPitting && !paceLimited && (speedSignal || damageSignal);
                participant.previousSpeedKphForIncident = speedKph;
                participant.previousDamagePercentForIncident = damagePercent;
                if (collision)
                {
                    // A spin/contact event earns a grace window before the stranded
                    // timer can ever accumulate again - the car gets a real chance to
                    // gather itself and drive away before race control considers it.
                    participant.recoveryGraceTimer = RecoveryGraceSeconds;
                }

                TrackProgress progress = State.GetCurrentProgress(participant);
                float facingDot = Vector3.Dot(participant.transform.forward, progress.forward);

                // Recovery-state classification (Part 2): a naive "speed < 8kph for a
                // few seconds" check flagged every ordinary spin, brief blockage or
                // SC-pace crawl as "stranded", which was the actual root cause of
                // race control escalating far too often - not the escalation-chance
                // math itself. Only a car that is nearly stationary for a sustained
                // duration AND excluded from every legitimate reason to be slow can
                // ever reach ActuallyStranded; everything else is Recovering/Queued/
                // PitSequence/RaceControlPacing and never registers an incident.
                bool offTrackNow = Mathf.Abs(progress.lateralDistance) > Track.roadHalfWidth + 1.5f;
                // A car off track but still aimed roughly the right way (not spun
                // fully backward) and crawling is actively working its way back,
                // not stuck - this is the single biggest source of the old false
                // positives, since running wide through gravel routinely dips under
                // 10kph for a couple of seconds while genuinely recovering.
                bool pointedTowardTrack = facingDot > -0.15f;
                bool creepingWithPurpose = speedKph > 3.5f && pointedTowardTrack;

                // Directly behind a car that is itself slow (SC bunching, a first-lap
                // squeeze, a queue at a re-join point) means this car is queued in
                // traffic, not stuck on its own - checked against on-track cars only,
                // since a car buried in the gravel isn't "queued" behind anyone.
                RaceParticipant blockerAhead = !offTrackNow ? FindCarAhead(participant, 9f) : null;
                bool queuedBehindTraffic = blockerAhead != null && blockerAhead.vehicle != null &&
                    Mathf.Abs(blockerAhead.vehicle.CurrentSpeedKph) < 30f && speedKph < 30f;

                bool recoveryGraceActive = participant.recoveryGraceTimer > 0f;
                participant.recoveryGraceTimer = Mathf.Max(0f, participant.recoveryGraceTimer - RaceControlCheckInterval);

                bool nearStationary = speedKph < 8f;
                // paceLimited already covers the whole full-SC/VSC period field-
                // wide, but the explicit isRaceControlAutopilot check is kept too
                // as a direct guard - a car race control is actively driving
                // itself must never be declared stranded regardless of exactly
                // which race-control-state check caught it.
                bool strandedExcluded = preRace || inPitPhaseOrPitting || paceLimited || queuedBehindTraffic ||
                    creepingWithPurpose || recoveryGraceActive || participant.isRaceControlAutopilot;
                bool stoppedCandidate = nearStationary && !strandedExcluded;
                participant.stoppedOnTrackTimer = stoppedCandidate ? participant.stoppedOnTrackTimer + RaceControlCheckInterval : 0f;

                // Debug: a car that would have tripped the old blunt check (slow,
                // not pre-race/pitting) but is excused by one of the new reasons -
                // logged once per continuous slow episode, not every 0.35s tick.
                if (nearStationary && !preRace && !inPitPhaseOrPitting && strandedExcluded)
                {
                    if (!participant.falseStrandedLogged)
                    {
                        participant.falseStrandedLogged = true;
                        string reason = paceLimited ? "race-control pace" : (queuedBehindTraffic ? "queued behind traffic" : (creepingWithPurpose ? "creeping back with purpose" : "recovery grace period"));
                        GameLog.Info("[RaceControl] Ignoring false stranded case for " + participant.driverName + ": " + reason + " (speed=" + speedKph.ToString("0") + "kph)");
                    }
                }
                else if (!nearStationary)
                {
                    participant.falseStrandedLogged = false;
                }

                CarRecoveryState previousRecoveryState = participant.recoveryState;
                CarRecoveryState newRecoveryState;
                if (inPitPhaseOrPitting)
                {
                    newRecoveryState = CarRecoveryState.PitSequence;
                }
                else if (paceLimited)
                {
                    newRecoveryState = CarRecoveryState.RaceControlPacing;
                }
                else if (queuedBehindTraffic)
                {
                    newRecoveryState = CarRecoveryState.Queued;
                }
                else if (participant.stoppedOnTrackTimer > StrandedDeclareSeconds)
                {
                    newRecoveryState = CarRecoveryState.ActuallyStranded;
                }
                else if (nearStationary || recoveryGraceActive || offTrackNow)
                {
                    newRecoveryState = CarRecoveryState.Recovering;
                }
                else
                {
                    newRecoveryState = CarRecoveryState.Normal;
                }

                participant.recoveryState = newRecoveryState;
                if (newRecoveryState != previousRecoveryState)
                {
                    if (newRecoveryState == CarRecoveryState.Recovering)
                    {
                        GameLog.Info("[RaceControl] " + participant.driverName + " entering recovery (speed=" + speedKph.ToString("0") + "kph, offTrack=" + offTrackNow + ").");
                    }
                    else if (newRecoveryState == CarRecoveryState.Queued)
                    {
                        GameLog.Info("[RaceControl] " + participant.driverName + " considered queued behind traffic, not stranded.");
                    }
                    else if (newRecoveryState == CarRecoveryState.ActuallyStranded)
                    {
                        GameLog.Info("[RaceControl] " + participant.driverName + " declared ActuallyStranded: stoppedTimer=" + participant.stoppedOnTrackTimer.ToString("0.0") +
                            "s offTrack=" + offTrackNow + " facingDot=" + facingDot.ToString("0.00"));
                    }
                }

                // Wrong-way is excluded by the same legitimate-reasons list, plus a
                // longer sustained duration (Part 2) so a car merely gathering itself
                // out of a spin - briefly pointed backward while it turns back around,
                // or already in the Recovering state - isn't flagged the instant it
                // starts rolling again.
                bool wrongWayCandidate = !preRace && !inPitPhaseOrPitting && !paceLimited && !recoveryGraceActive &&
                    newRecoveryState != CarRecoveryState.Recovering && speedKph > 5f && facingDot < -0.35f;
                participant.wrongWayTimer = wrongWayCandidate ? participant.wrongWayTimer + RaceControlCheckInterval : 0f;

                participant.incidentCooldownTimer = Mathf.Max(0f, participant.incidentCooldownTimer - RaceControlCheckInterval);
                if (participant.incidentCooldownTimer > 0f)
                {
                    continue;
                }

                bool destroyed = participant.vehicle.Damage != null && participant.vehicle.Damage.IsDestroyed;
                bool blockingLine = Mathf.Abs(progress.lateralDistance) <= Track.roadHalfWidth * 0.85f;

                if (destroyed)
                {
                    RetireParticipant(participant, "Collision damage");
                    // Truly catastrophic (a destroyed car actually blocking the racing
                    // line) bypasses the escalation roll entirely rather than risking
                    // "no response" to a car sitting dead in the road - still fully
                    // subject to escalationAllowed, so Off never escalates regardless.
                    // A destroyed car that ISN'T blocking the line is Minor and does
                    // NOT need a yellow flag - it is off the racing line, not a hazard.
                    RegisterIncident(participant, blockingLine ? IncidentSeverity.Major : IncidentSeverity.Minor, progress, freqScale, escalationAllowed, "Destroyed", blockingLine, false);
                    participant.incidentCooldownTimer = 30f;
                    continue;
                }

                if (collision)
                {
                    // Severity now reflects either signal, not damageJump alone - a
                    // hard speed-only spin (no wall contact) can still read Medium,
                    // and a big simultaneous hit is always at least Medium. Part 3
                    // retune: bars raised again so only genuinely hard hits reach
                    // Medium/Major - a light scrape stays Minor and, per the yellow
                    // policy below, does not flag race control at all.
                    IncidentSeverity severity;
                    if (damageJump > 36f || speedDrop > 130f)
                    {
                        severity = IncidentSeverity.Major;
                    }
                    else if (damageJump > 20f || speedDrop > 70f || (speedSignal && damageSignal))
                    {
                        severity = IncidentSeverity.Medium;
                    }
                    else
                    {
                        severity = IncidentSeverity.Minor;
                    }

                    // Part 3: Medium is no longer an automatic yellow either - a
                    // "medium-ish" scrape/spin that isn't actually blocking the
                    // racing line is logged for stats only, same as Minor. Major
                    // always flags (that tier is reserved for genuinely serious
                    // events); Minor never does regardless of position.
                    bool collisionYellowJustified = severity == IncidentSeverity.Medium && blockingLine;
                    RegisterIncident(participant, severity, progress, freqScale, escalationAllowed, "Collision (speedDrop=" + speedDrop.ToString("0") + " damageJump=" + damageJump.ToString("0.0") + ")", false, collisionYellowJustified);
                    // Part 3: longer per-incident suppression (was 24s) so the same
                    // scrape/spin can't repeatedly re-roll an escalation chance.
                    participant.incidentCooldownTimer = 30f;
                    continue;
                }

                if (damagePercent >= 85f)
                {
                    RegisterIncident(participant, IncidentSeverity.Major, progress, freqScale, escalationAllowed, "Severe damage");
                    participant.incidentCooldownTimer = 38f;
                    continue;
                }

                // Part 2: only the ActuallyStranded classification above (which already
                // required the sustained StrandedDeclareSeconds duration with every
                // legitimate exclusion checked) can ever reach race control - no
                // separate, laxer off-track-only path any more. Minor (not blocking
                // the line) never raises a yellow - only a stranded car sitting in
                // or very near the racing line is an actual hazard, which is exactly
                // when this is Medium rather than Minor, so blockingLine alone is the
                // correct yellow-justification signal here.
                if (newRecoveryState == CarRecoveryState.ActuallyStranded)
                {
                    if (participant.stoppedOnTrackTimer > StrandedRetireSeconds)
                    {
                        RetireParticipant(participant, "Stranded");
                    }

                    RegisterIncident(participant, blockingLine ? IncidentSeverity.Medium : IncidentSeverity.Minor, progress, freqScale, escalationAllowed, "Stopped/stranded", false, blockingLine);
                    // Part 3: longer per-incident suppression so a car still sitting in
                    // the same stranded episode doesn't re-register (and re-roll a VSC/
                    // SC chance) every few seconds while race control already knows.
                    participant.incidentCooldownTimer = 38f;
                    continue;
                }

                // Part 3 retune: longer sustained duration (was 7s) so a car briefly
                // pointed backward while gathering itself out of a spin - already
                // excluded above while recoveryGraceActive/Recovering - isn't flagged
                // the moment it lapses if it's still slowly correcting.
                if (participant.wrongWayTimer > 10f)
                {
                    // A wrong-way car only warrants a yellow if it is actually near
                    // other traffic - alone on an empty stretch of track it is not
                    // yet a hazard to anyone, even though it still counts as an
                    // incident and still retires/cools down normally.
                    bool nearTraffic = FindCarAhead(participant, 100f) != null || FindCarBehind(participant, 100f) != null;
                    RegisterIncident(participant, IncidentSeverity.Minor, progress, freqScale, escalationAllowed, "Wrong way", false, nearTraffic);
                    participant.incidentCooldownTimer = 28f;
                    continue;
                }

                // Rare mechanical failure spice, gated by the settings toggle and this
                // car's reliability stat. Kept deliberately small and further halved
                // (Part 2) - over a full race this should be a rare talking point, not
                // a routine occurrence, and a car under SC/VSC pacing is excluded
                // entirely since its pace is race control's own doing.
                bool mechanicalEligible = mechanicalMode != 0 && !(mechanicalMode == 1 && participant.isPlayer) && !preRace && !paceLimited;
                if (mechanicalEligible)
                {
                    float reliability = participant.carData == null ? 88f : participant.carData.reliability;
                    float perSecondChance = Mathf.Lerp(0.000015f, 0.000001f, Mathf.Clamp01(reliability / 100f));
                    if (Random.value < perSecondChance * RaceControlCheckInterval)
                    {
                        RetireParticipant(participant, "Mechanical failure");
                        RegisterIncident(participant, blockingLine ? IncidentSeverity.Medium : IncidentSeverity.Minor, progress, freqScale, escalationAllowed, "Mechanical failure", false, blockingLine);
                        participant.incidentCooldownTimer = 48f;
                    }
                }
            }
        }

        // Groups incidents that land within a few seconds and a short stretch of
        // track into one escalated event (a simple pileup approximation) rather
        // than full physics collision-graph analysis.
        void RegisterIncident(RaceParticipant participant, IncidentSeverity severity, TrackProgress progress, float freqScale, bool escalationAllowed, string cause, bool forceEscalate = false, bool yellowJustified = false)
        {
            IncidentCount++;
            bool pileup = (RaceElapsed - lastIncidentTime) < 6f && Mathf.Abs(Track.WrapDistance(progress.distance - lastIncidentDistance)) < 40f;
            lastIncidentTime = RaceElapsed;
            lastIncidentDistance = progress.distance;
            if (pileup && severity != IncidentSeverity.Major)
            {
                severity = severity == IncidentSeverity.Minor ? IncidentSeverity.Medium : IncidentSeverity.Major;
                // A pileup that escalated an incident's severity is, by
                // construction, no longer an isolated case - it now qualifies
                // for a yellow on its own merits.
                yellowJustified = true;
            }

            GameLog.Info("[RaceControl] Incident: " + (participant == null ? "?" : participant.driverName) +
                " cause=" + cause + " severity=" + severity + " sector=" + progress.sector + (pileup ? " (pileup-escalated)" : "") + (forceEscalate ? " (force-escalate)" : ""));

            ApplyIncidentSeverity(participant, severity, progress, freqScale, escalationAllowed, forceEscalate, yellowJustified);
        }

        void ApplyIncidentSeverity(RaceParticipant participant, IncidentSeverity severity, TrackProgress progress, float freqScale, bool escalationAllowed, bool forceEscalate = false, bool yellowJustified = false)
        {
            // Part 3: only Major incidents always raise the local sector yellow
            // regardless of the safety-car frequency setting (Off only disables
            // VSC/SC escalation, not flags entirely) - that tier is reserved
            // for genuinely serious events. Medium AND Minor incidents only
            // ever raise a yellow when the call site has judged them still
            // genuinely dangerous (blocking the line, near traffic, etc) AND
            // the global minor-incident cooldown has lapsed - most incidents
            // reaching this point are simply logged for stats with no
            // race-control flag at all, so a run of ordinary scrapes/spins/
            // bunching never reads as a constant background of yellow flags.
            if (severity == IncidentSeverity.Major)
            {
                TriggerYellowSector(progress.sector);
            }
            else if (yellowJustified)
            {
                if (RaceElapsed >= globalMinorYellowCooldownUntil)
                {
                    TriggerYellowSector(progress.sector);
                    globalMinorYellowCooldownUntil = RaceElapsed + GlobalMinorYellowCooldownSeconds;
                }
                else
                {
                    GameLog.Info("[RaceControl] " + severity + " incident yellow suppressed by global minor-yellow cooldown (" +
                        (globalMinorYellowCooldownUntil - RaceElapsed).ToString("0.0") + "s remaining).");
                }
            }
            else
            {
                GameLog.Info("[RaceControl] " + severity + " incident logged without a race-control flag (not near the racing line / not significant).");
            }

            bool alreadyEscalated = CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                                     CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                                     CurrentRaceControlState == RaceControlState.SafetyCarInThisLap ||
                                     CurrentRaceControlState == RaceControlState.Restart;
            if (!escalationAllowed || alreadyEscalated || severity == IncidentSeverity.Minor)
            {
                return;
            }

            // Truly catastrophic cases (a destroyed car actually blocking the racing
            // line) skip the roll and the post-escalation cooldown entirely - every
            // other case still respects both, so one incident can't chain into
            // repeated SC/VSC periods.
            if (forceEscalate)
            {
                GameLog.Info("[RaceControl] Forced escalation (catastrophic, blocking): deploying safety car.");
                BeginSafetyCarDeployment();
                return;
            }

            if (postEscalationCooldownTimer > 0f)
            {
                GameLog.Info("[RaceControl] Escalation suppressed: post-escalation cooldown active (" + postEscalationCooldownTimer.ToString("0.0") + "s remaining).");
                return;
            }

            // Part 3: a Medium incident that isn't even yellow-justified (not
            // blocking the line / no nearby traffic) is not a real hazard to
            // anyone - it never gets a VSC roll at all, only a genuinely
            // positioned Medium incident does.
            if (severity == IncidentSeverity.Medium && !yellowJustified)
            {
                GameLog.Info("[RaceControl] Medium incident not blocking the line - no VSC roll.");
                return;
            }

            // Part 3 (fifth retune): cut again from 0.15/0.20/0.28 - with false
            // positives fixed and thresholds raised upstream, incidents reaching
            // this point are essentially all genuine, so the roll itself can be
            // much stingier and still produce a meaningful, rare event. VSC
            // should read as "uncommon", full SC as "rare and meaningful" - a
            // short prototype race should usually run green throughout.
            if (severity == IncidentSeverity.Medium)
            {
                if (CurrentRaceControlState != RaceControlState.VirtualSafetyCar)
                {
                    float roll = Random.value;
                    float chance = Mathf.Clamp01(0.08f * freqScale);
                    bool escalate = roll < chance;
                    GameLog.Info("[RaceControl] Medium incident escalation: roll=" + roll.ToString("0.00") + " chance=" + chance.ToString("0.00") + " result=" + (escalate ? "VSC deployed" : "no escalation"));
                    if (escalate)
                    {
                        BeginVirtualSafetyCar();
                    }
                }

                return;
            }

            // Major.
            float scRoll = Random.value;
            float scChance = Mathf.Clamp01(0.10f * freqScale);
            bool deploySc = scRoll < scChance;
            if (deploySc)
            {
                GameLog.Info("[RaceControl] Major incident escalation: roll=" + scRoll.ToString("0.00") + " chance=" + scChance.ToString("0.00") + " result=Safety car deployed");
                BeginSafetyCarDeployment();
                return;
            }

            // A failed full-SC roll no longer defaults deterministically to VSC -
            // it gets its own (separate, still freqScale-scaled) fallback chance, so
            // a Major incident can also resolve to "yellow only" rather than always
            // producing at least a VSC.
            if (CurrentRaceControlState != RaceControlState.VirtualSafetyCar)
            {
                float vscFallbackRoll = Random.value;
                float vscFallbackChance = Mathf.Clamp01(0.14f * freqScale);
                bool vscFallback = vscFallbackRoll < vscFallbackChance;
                GameLog.Info("[RaceControl] Major incident escalation: scRoll=" + scRoll.ToString("0.00") + " scChance=" + scChance.ToString("0.00") +
                    " (no SC) vscFallbackRoll=" + vscFallbackRoll.ToString("0.00") + " vscFallbackChance=" + vscFallbackChance.ToString("0.00") +
                    " result=" + (vscFallback ? "VSC deployed" : "no escalation, yellow only"));
                if (vscFallback)
                {
                    BeginVirtualSafetyCar();
                }
            }
        }

        int yellowSectorNumber = -1;

        void TriggerYellowSector(int sector)
        {
            bool sameActiveSector = yellowSectorNumber == sector && yellowSectorClearTimer > 0f;

            // Part 2: don't refresh the same persistent hazard's yellow forever -
            // once an episode has run for MaxYellowEpisodeSeconds, let it clear
            // naturally even if the underlying incident keeps re-registering
            // (by then the stranded-retire / cooldown logic elsewhere has
            // almost always already resolved it anyway).
            if (sameActiveSector && RaceElapsed - yellowSectorEpisodeStartTime > MaxYellowEpisodeSeconds)
            {
                return;
            }

            if (!sameActiveSector)
            {
                float cooldownUntil;
                if (yellowSectorCooldownUntil.TryGetValue(sector, out cooldownUntil) && RaceElapsed < cooldownUntil)
                {
                    GameLog.Info("[RaceControl] Yellow flag suppressed for sector " + sector + " - per-sector cooldown active (" +
                        (cooldownUntil - RaceElapsed).ToString("0.0") + "s remaining).");
                    return;
                }

                yellowSectorEpisodeStartTime = RaceElapsed;
            }

            if (CurrentRaceControlState == RaceControlState.Green)
            {
                CurrentRaceControlState = RaceControlState.YellowSector;
            }

            bool freshFlag = yellowSectorNumber != sector || yellowSectorClearTimer <= 0f;
            yellowSectorNumber = sector;
            YellowFlagSector = sector;
            // Part 2: shorter default duration (was 10s) - a yellow reads as a
            // brief, localized warning unless the hazard keeps re-registering,
            // which still refreshes this timer (up to the episode cap above).
            yellowSectorClearTimer = 7f;
            yellowSectorCooldownUntil[sector] = RaceElapsed + yellowSectorClearTimer + YellowSectorCooldownAfterClearSeconds;
            if (freshFlag)
            {
                GameLog.Info("[RaceControl] Yellow flag, sector " + sector + ".");
                if (Settings != null && Settings.Current.raceControlMessages)
                {
                    PostEngineerMessage("Yellow flag, sector " + sector + ".", false);
                }
            }
        }

        void BeginVirtualSafetyCar()
        {
            CurrentRaceControlState = RaceControlState.VirtualSafetyCar;
            safetyCarTimer = Random.Range(14f, 24f);
            IsOvertakingAllowed = false;
            playerScPitPromptSent = false;
            GameLog.Info("[RaceControl] Virtual safety car deployed. duration=" + safetyCarTimer.ToString("0.0") + "s");
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Virtual safety car deployed.", true);
            }
        }

        void BeginSafetyCarDeployment()
        {
            CurrentRaceControlState = RaceControlState.SafetyCarDeploying;
            safetyCarTimer = Random.Range(6f, 10f);
            IsOvertakingAllowed = false;
            SafetyCarDeploymentCount++;
            playerScPitPromptSent = false;
            playerScQueueWarningSent = false;
            List<RaceParticipant> order = GetRunningOrderSnapshot();
            safetyCarQueueLeader = order.Count > 0 ? order[0] : null;

            // Convoy autopilot (Part 2): freeze each car's legal running-order
            // slot at the instant of deployment as its safety-car queue index -
            // the leader gets 0, second place 1, and so on - so the whole field
            // forms up in a stable, predictable queue rather than re-sorting
            // itself against a moving target every frame while cars close up.
            for (int i = 0; i < order.Count; i++)
            {
                RaceParticipant queued = order[i];
                if (queued == null)
                {
                    continue;
                }

                queued.safetyCarQueueIndex = i;
                queued.preSafetyCarOrderIndex = i;
                if (!queued.retired && !queued.finished)
                {
                    queued.isRaceControlAutopilot = true;
                }
            }

            // Part 1: a real AI car, not just a HUD state - it joins the track a
            // short, safe distance ahead of whoever is currently leading, so the
            // leader immediately has to slow down and queue up behind it exactly
            // like a real safety car period.
            EnsureSafetyCarBuilt();
            float leaderDistance = safetyCarQueueLeader != null && State != null
                ? State.GetProgressDistance(safetyCarQueueLeader)
                : 0f;
            if (safetyCarController != null)
            {
                safetyCarController.EnterTrack(Track != null ? Track.WrapDistance(leaderDistance + 60f) : leaderDistance + 60f);
                SafetyCarTargetSpeedKph = Mathf.Max(110f, safetyCarController.CurrentSpeedKph);
                LogSafetyCarSpawnState("deployment");
            }
            else
            {
                SafetyCarTargetSpeedKph = Random.Range(140f, 160f);
                GameLog.Warn("[RaceControl] Safety car deployment WITHOUT a physical car: controller build failed, falling back to pace-cap-only period.");
            }

            safetyCarWatchdogTimer = 0f;
            safetyCarWatchdogRespawnCount = 0;
            GameLog.Info("[RaceControl] Safety car deployment triggered. targetSpeed=" + SafetyCarTargetSpeedKph.ToString("0") + "kph deploymentCount=" + SafetyCarDeploymentCount);
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Safety car deployed.", true);
                PostEngineerMessage("Safety car deployed, car is under race-control autopilot.", true);
                PostEngineerMessage("You can request a pit stop under safety car.", false);
            }
        }

        // Builds the safety car GameObject + controller once per session and
        // reuses it for every subsequent deployment - reactivated via EnterTrack
        // rather than destroyed/recreated each time.
        void EnsureSafetyCarBuilt()
        {
            if (safetyCarController != null)
            {
                return;
            }

            Renderer beaconRenderer;
            Renderer brakeLightRenderer;
            safetyCarObject = CreateSafetyCarVisual(out beaconRenderer, out brakeLightRenderer);
            if (raceWorld != null)
            {
                safetyCarObject.transform.SetParent(raceWorld.transform);
            }

            safetyCarController = safetyCarObject.AddComponent<SafetyCarController>();
            safetyCarController.Configure(Track, beaconRenderer, brakeLightRenderer);
            safetyCarObject.SetActive(false);
        }

        float safetyCarWatchdogTimer;
        int safetyCarWatchdogRespawnCount;
        // Sustained-duration bar before ANY watchdog respawn fires, including
        // the unambiguous null/inactive cases - a single skipped frame during a
        // scene transition, a renderer toggle, or brief physics hiccup must
        // never be enough on its own to trigger a visible respawn/teleport.
        const float SafetyCarWatchdogMissingThresholdSeconds = 4f;
        // A full SC period should essentially never need more than one genuine
        // respawn. If the watchdog wants to fire again after that, something is
        // structurally wrong (or the check itself is too loose) - repeated
        // respawns are themselves the visible "jumping" bug, so refuse to keep
        // respawning and log a warning instead of hiding the real problem.
        const int MaxWatchdogRespawnsPerScPeriod = 1;

        // Only real, unrecoverable failures count as "missing" - a loose
        // "!activeInHierarchy" check alone can flicker true for reasons
        // unrelated to a real failure (a single skipped frame during a scene
        // transition, a renderer toggle, etc.), and being far ahead of the
        // leader is NORMAL during queue formation on a long track, not a sign
        // of a lost car - so it is deliberately not checked here at all.
        bool IsSafetyCarGenuinelyMissing(out string reason)
        {
            if (safetyCarController == null || safetyCarObject == null)
            {
                reason = "null controller/object";
                return true;
            }

            if (!safetyCarController.IsActive || !safetyCarObject.activeInHierarchy)
            {
                reason = "inactive object";
                return true;
            }

            Renderer[] renderers = safetyCarObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                reason = "no renderers";
                return true;
            }

            Vector3 pos = safetyCarObject.transform.position;
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z) ||
                float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z))
            {
                reason = "NaN/invalid position";
                return true;
            }

            if (pos.y < -25f)
            {
                reason = "under terrain (y=" + pos.y.ToString("0.0") + ")";
                return true;
            }

            if (Track != null)
            {
                // Compare the visible object against where its OWN progress
                // distance says it should be - this catches a genuinely
                // derailed/desynced car without ever depending on the leader's
                // position, so normal queue-formation gaps can never trip it.
                Vector3 expectedPoint;
                Vector3 expectedForward;
                Vector3 expectedRight;
                Track.SampleAtDistance(safetyCarController.ProgressDistance, out expectedPoint, out expectedForward, out expectedRight);
                float distanceFromExpected = Vector3.Distance(pos, expectedPoint);
                if (distanceFromExpected > 150f)
                {
                    reason = "absurd distance from track (" + distanceFromExpected.ToString("0") + "m from expected)";
                    return true;
                }
            }

            reason = null;
            return false;
        }

        // Field-wide backstop (Part 1/2): keeps the pace-limiter's own target
        // number honest against the real car's actual speed instead of a single
        // number picked once at deployment, ends the period by sending the car
        // toward the pits once race control calls "safety car in this lap",
        // penalizes anyone who actually gets past it on track, and - watchdog -
        // rebuilds/respawns the whole car if a full SC period is somehow running
        // without a visible, active safety car object.
        void UpdateSafetyCar()
        {
            // Convoy autopilot upkeep: every non-retired/non-finished car is under
            // race-control autopilot for exactly as long as the field is in a
            // full safety car period, and not a tick longer - IsFullSafetyCarPeriod
            // already excludes RaceControlState.Restart, so control hands back to
            // the player/AI's own driving the instant the state machine advances
            // past SafetyCarInThisLap, with no extra bookkeeping needed here.
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant p = Participants[i];
                if (p == null)
                {
                    continue;
                }

                if (IsFullSafetyCarPeriod && !p.retired && !p.finished)
                {
                    p.isRaceControlAutopilot = true;
                }
                else if (p.isRaceControlAutopilot)
                {
                    p.isRaceControlAutopilot = false;
                }
            }

            if (IsFullSafetyCarPeriod)
            {
                string missingReason;
                bool visibleCarMissing = IsSafetyCarGenuinelyMissing(out missingReason);
                if (visibleCarMissing)
                {
                    bool wasAlreadyCounting = safetyCarWatchdogTimer > 0f;
                    safetyCarWatchdogTimer += Time.deltaTime;
                    if (!wasAlreadyCounting)
                    {
                        // One-shot heads-up the moment the watchdog starts
                        // counting, at Info level - if it fires repeatedly in the
                        // logs without ever reaching a real respawn, that is the
                        // signal of a flickering/loose "missing" check rather
                        // than an actual failure, without the noise of a
                        // respawn (which is logged at Warn) happening every time.
                        GameLog.Info("[RaceControl] Safety car watchdog started counting - reason=" + missingReason);
                    }

                    if (safetyCarWatchdogTimer > SafetyCarWatchdogMissingThresholdSeconds)
                    {
                        safetyCarWatchdogTimer = 0f;
                        if (safetyCarWatchdogRespawnCount >= MaxWatchdogRespawnsPerScPeriod)
                        {
                            // Repeated respawns are themselves the visible
                            // "car jumping around" symptom - refuse to keep
                            // doing it. One respawn per SC period is already a
                            // generous last resort; a second request in the
                            // same period means the underlying cause needs a
                            // real fix, not another teleport.
                            GameLog.Warn("[RaceControl] Safety car watchdog wants to respawn again this SC period (reason=" + missingReason +
                                ") but the per-period respawn cap (" + MaxWatchdogRespawnsPerScPeriod + ") was already reached - refusing to avoid visible jumping.");
                        }
                        else
                        {
                            safetyCarWatchdogRespawnCount++;
                            RespawnMissingSafetyCar(missingReason);
                        }
                    }
                }
                else
                {
                    safetyCarWatchdogTimer = 0f;
                }
            }

            if (safetyCarController == null || !safetyCarController.IsActive)
            {
                aheadOfSafetyCarLastTick.Clear();
                return;
            }

            if (IsFullSafetyCarPeriod)
            {
                SafetyCarTargetSpeedKph = Mathf.Max(60f, safetyCarController.CurrentSpeedKph);

                // Leader pickup (Part 4): the safety car waits (slows hard) while
                // the leader is still far behind, then releases to normal pace as
                // the queue forms up - controlled from here since the controller
                // itself knows nothing about participants.
                if (safetyCarQueueLeader != null && State != null && Track != null)
                {
                    float gapToLeader = Track.WrapDistance(safetyCarController.ProgressDistance - State.GetProgressDistance(safetyCarQueueLeader));
                    safetyCarController.SetLeaderGapMeters(gapToLeader);
                }
            }

            if (!IsFullSafetyCarPeriod && !safetyCarController.IsReturningToPits)
            {
                // State moved on (VSC/green/etc.) without the normal
                // "in this lap" hand-off - send it home defensively rather than
                // leaving it circulating with nothing driving it into the pits.
                safetyCarController.BeginPitReturn();
            }

            // One-shot heads-up as the player first closes on the physical queue.
            if (!playerScQueueWarningSent)
            {
                float playerGap = PlayerGapToSafetyCarMeters();
                if (playerGap >= 0f && playerGap < 150f)
                {
                    playerScQueueWarningSent = true;
                    if (Settings != null && Settings.Current.raceControlMessages)
                    {
                        PostEngineerMessage("Safety car queue ahead - slow down and hold your gap.", true);
                    }
                }
            }

            CheckSafetyCarOvertakes();
        }

        bool playerScQueueWarningSent;

        void RespawnMissingSafetyCar(string reason)
        {
            if (safetyCarController == null || safetyCarObject == null)
            {
                safetyCarObject = null;
                safetyCarController = null;
                EnsureSafetyCarBuilt();
            }

            if (safetyCarController == null)
            {
                GameLog.Warn("[RaceControl] Safety car respawn FAILED: controller could not be rebuilt. reason=" + reason);
                return;
            }

            List<RaceParticipant> order = GetRunningOrderSnapshot();
            RaceParticipant leader = order.Count > 0 ? order[0] : safetyCarQueueLeader;
            float leaderDistance = leader != null && State != null ? State.GetProgressDistance(leader) : 0f;
            safetyCarController.EnterTrack(Track != null ? Track.WrapDistance(leaderDistance + 60f) : leaderDistance + 60f);
            GameLog.Warn("[RaceControl] Safety car respawned (last resort). reason=" + reason + " respawnCountThisPeriod=" + safetyCarWatchdogRespawnCount);
            LogSafetyCarSpawnState("watchdog-respawn");
        }

        void LogSafetyCarSpawnState(string context)
        {
            if (safetyCarController == null || safetyCarObject == null)
            {
                GameLog.Warn("[RaceControl] SC spawn (" + context + "): controller/object is null.");
                return;
            }

            Renderer[] renderers = safetyCarObject.GetComponentsInChildren<Renderer>(true);
            float leaderDistance = safetyCarQueueLeader != null && State != null ? State.GetProgressDistance(safetyCarQueueLeader) : -1f;
            GameLog.Info("[RaceControl] SC spawn (" + context + "): state=" + CurrentRaceControlState +
                " active=" + safetyCarObject.activeInHierarchy +
                " controllerActive=" + safetyCarController.IsActive +
                " spawnDistance=" + safetyCarController.ProgressDistance.ToString("0") +
                " worldPos=" + safetyCarObject.transform.position +
                " leaderDistance=" + leaderDistance.ToString("0") +
                " scSpeed=" + safetyCarController.CurrentSpeedKph.ToString("0") +
                " renderers=" + renderers.Length);
        }

        // HUD support (Part 4): live gap from the player to the safety car when
        // the player is in the forming/formed queue; negative when not relevant.
        public float PlayerGapToSafetyCarMeters()
        {
            if (safetyCarController == null || !safetyCarController.IsActive || PlayerParticipant == null || State == null || Track == null)
            {
                return -1f;
            }

            float gap = Track.WrapDistance(safetyCarController.ProgressDistance - State.GetProgressDistance(PlayerParticipant));
            return gap < 400f ? gap : -1f;
        }

        // Passing the actual safety car (Part 3): a transition-based check, same
        // shape as CheckIllegalOvertakesUnderYellow - only fires the instant a
        // car's progress distance crosses from behind the safety car to ahead of
        // it, not on every tick it happens to already be ahead (lapped traffic a
        // full circuit away from the queue must never trip this).
        void CheckSafetyCarOvertakes()
        {
            if (State == null || Track == null)
            {
                return;
            }

            float safetyCarDistance = safetyCarController.ProgressDistance;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                // A car under race-control's own convoy autopilot can never
                // "illegally" pass the safety car - its movement is race
                // control's own doing, not a driver decision to penalize.
                if (participant == null || participant.vehicle == null || participant.retired || participant.finished ||
                    participant.isPitting || participant.pitPhase != PitPhase.None || participant.isRaceControlAutopilot)
                {
                    aheadOfSafetyCarLastTick.Remove(participant);
                    continue;
                }

                float gap = Track.WrapDistance(State.GetProgressDistance(participant) - safetyCarDistance);
                bool aheadNow = gap > 3f && gap < 250f;
                bool wasAhead = aheadOfSafetyCarLastTick.Contains(participant);
                if (aheadNow && !wasAhead)
                {
                    AddPenalty(participant, 5f, "Passed the safety car");
                    GameLog.Warn("[RaceControl] " + participant.driverName + " illegally passed the safety car (+5s).");
                    if (participant.isPlayer)
                    {
                        SessionMessage = "Passed the safety car: +5s";
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("You passed the safety car - that's a penalty, fall back into line.", true);
                        }
                    }
                }

                if (aheadNow)
                {
                    aheadOfSafetyCarLastTick.Add(participant);
                }
                else
                {
                    aheadOfSafetyCarLastTick.Remove(participant);
                }
            }
        }

        // Public read-only surface (Part 1/2/3): AI traffic-avoidance treats the
        // safety car as a real obstacle to queue behind, and the HUD shows a
        // distinct "follow the safety car" state, both without needing their own
        // copy of the deployment/despawn bookkeeping above.
        public Transform SafetyCarTransform { get { return safetyCarController != null && safetyCarController.IsActive ? safetyCarController.transform : null; } }
        public bool IsSafetyCarOnTrack { get { return safetyCarController != null && safetyCarController.IsActive; } }
        public float SafetyCarCurrentSpeedKph { get { return safetyCarController != null ? safetyCarController.CurrentSpeedKph : 0f; } }

        // Full safety car convoy autopilot (Part 2). True only while there's an
        // actual, active safety car object on track during a full SC period -
        // HUD and callers use this instead of IsFullSafetyCarPeriod alone so a
        // brief window where the controller/object hasn't finished spawning yet
        // never shows "autopilot active" with nothing physically driving it.
        public bool IsSafetyCarConvoyActive { get { return IsFullSafetyCarPeriod && safetyCarController != null && safetyCarController.IsActive; } }

        // HUD readouts (Part 10): 1-based queue position (0/-1 when not queued)
        // and live gap-to-target-slot in meters, both driven off the same state
        // BuildRaceControlAutopilotCommand itself uses every tick.
        public int PlayerSafetyCarQueuePosition
        {
            get { return PlayerParticipant != null && PlayerParticipant.safetyCarQueueIndex >= 0 ? PlayerParticipant.safetyCarQueueIndex + 1 : -1; }
        }

        public float PlayerSafetyCarGapToTargetMeters
        {
            get
            {
                if (PlayerParticipant == null || State == null || Track == null || !PlayerParticipant.isRaceControlAutopilot)
                {
                    return -1f;
                }

                float signed = Track.WrapDistance(PlayerParticipant.safetyCarTargetDistance - State.GetProgressDistance(PlayerParticipant));
                return signed > Track.length * 0.5f ? signed - Track.length : signed;
            }
        }

        // Convoy autopilot driving command (Part 2): builds a full throttle/
        // brake/steer command that drives `participant` toward its assigned
        // slot in the safety-car queue - the leader targets a fixed distance
        // behind the real safety car object, everyone else targets a further
        // fixed distance behind the leader's own slot, scaled by their frozen
        // queue index. Steering mirrors the same lookahead-point pattern
        // AiVehicleController's own normal driving uses (SampleAtDistance ahead
        // of the car's current position, steer toward it in local space) so the
        // convoy still tracks the racing line through corners instead of
        // cutting straight lines between distance samples; only the speed
        // target comes from the queue-slot error, not from the normal apex/
        // braking-point model.
        public VehicleCommand BuildRaceControlAutopilotCommand(RaceParticipant participant)
        {
            VehicleCommand command = new VehicleCommand();
            if (participant == null || participant.vehicle == null || Track == null || State == null)
            {
                command.brake = 1f;
                return command;
            }

            if (safetyCarController == null || !safetyCarController.IsActive)
            {
                // No physical safety car to queue behind (shouldn't normally
                // happen while this is being called) - hold speed down gently
                // rather than doing anything erratic.
                command.throttle = 0f;
                command.brake = 0.15f;
                return command;
            }

            float scSpeedKph = safetyCarController.CurrentSpeedKph;
            // Gap scales slightly with pace: a faster-moving queue needs a
            // little more following distance per car than a crawling one.
            float gapPerCar = Mathf.Lerp(14f, 22f, Mathf.Clamp01(scSpeedKph / 160f));
            int queueIndex = Mathf.Max(0, participant.safetyCarQueueIndex);
            float leaderTargetDistance = Track.WrapDistance(safetyCarController.ProgressDistance - 28f);
            float targetDistance = Track.WrapDistance(leaderTargetDistance - queueIndex * gapPerCar);
            participant.safetyCarTargetDistance = targetDistance;

            float ownDistance = State.GetProgressDistance(participant);
            // Signed distance from us to our slot: positive means the slot is
            // still ahead of us (behind on pace, catch up); negative means we've
            // already reached/overshot it (too close/ahead, back off).
            float rawGap = Track.WrapDistance(targetDistance - ownDistance);
            float signedSlotError = rawGap > Track.length * 0.5f ? rawGap - Track.length : rawGap;

            float mySpeedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);
            // Proportional speed target around the safety car's own pace: ahead
            // of slot -> brake back toward it, behind -> close in gently. Capped
            // well short of racing pace so a car that fell a long way back
            // during the deployment doesn't come storming up on the queue.
            float speedAdjustKph = Mathf.Clamp(signedSlotError * 1.2f, -45f, 25f);
            float targetSpeedKph = Mathf.Clamp(scSpeedKph + speedAdjustKph, 0f, scSpeedKph + 25f);

            float speedGapKph = targetSpeedKph - mySpeedKph;
            if (speedGapKph < -3f)
            {
                command.brake = Mathf.Clamp01(-speedGapKph / 40f);
                command.throttle = 0f;
            }
            else
            {
                command.brake = 0f;
                command.throttle = Mathf.Clamp01(0.15f + speedGapKph / 40f);
            }

            // Steering: same lookahead-sample-and-steer-toward-it pattern the AI's
            // own normal driving uses, just always aimed at the centerline (no
            // racing-line offset - a convoy holds station, it doesn't need to
            // find the fastest line through a corner).
            float lookAhead = Mathf.Lerp(14f, 38f, Mathf.Clamp01(mySpeedKph / 150f));
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Track.SampleAtDistance(Track.WrapDistance(ownDistance + lookAhead), out point, out forward, out right);
            Vector3 toTarget = point - participant.transform.position;
            float localSteer = Vector3.Dot(toTarget.normalized, participant.transform.right);
            command.steer = Mathf.Clamp(localSteer * 2.2f, -1f, 1f);

            return command;
        }

        // Ticks the active race-control state forward, including the safety-car
        // period's scripted restart chain (Active -> in this lap -> restart -> green).
        void DriveRaceControlStateMachine()
        {
            switch (CurrentRaceControlState)
            {
                case RaceControlState.Green:
                case RaceControlState.YellowSector:
                    IsOvertakingAllowed = true;
                    IsPitLaneOpen = true;
                    break;

                case RaceControlState.VirtualSafetyCar:
                    IsPitLaneOpen = true;
                    MaybePromptPlayerScPit();
                    safetyCarTimer -= Time.deltaTime;
                    if (safetyCarTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.Green;
                        IsOvertakingAllowed = true;
                        postEscalationCooldownTimer = PostEscalationCooldownSeconds;
                        GameLog.Info("[RaceControl] VSC ending, green flag.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("VSC ending, green flag.", true);
                        }
                    }
                    break;

                case RaceControlState.SafetyCarDeploying:
                    IsPitLaneOpen = true;
                    MaybePromptPlayerScPit();
                    safetyCarTimer -= Time.deltaTime;
                    if (safetyCarTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.SafetyCarActive;
                        safetyCarTimer = Random.Range(28f, 55f);
                        GameLog.Info("[RaceControl] Safety car now active on track. period=" + safetyCarTimer.ToString("0.0") + "s");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Safety car is now in position.", true);
                        }
                    }
                    break;

                case RaceControlState.SafetyCarActive:
                    IsPitLaneOpen = true;
                    MaybePromptPlayerScPit();
                    safetyCarTimer -= Time.deltaTime;
                    if (safetyCarTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.SafetyCarInThisLap;
                        safetyCarInThisLapMessageSent = false;
                        coldTyresRestartWarningSent = false;
                    }
                    break;

                case RaceControlState.SafetyCarInThisLap:
                    if (!safetyCarInThisLapMessageSent)
                    {
                        safetyCarInThisLapMessageSent = true;
                        restartControlTimer = 12f;
                        GameLog.Info("[RaceControl] Safety car in this lap, preparing restart.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Safety car in this lap.", true);
                            PostEngineerMessage("Safety car in this lap, control will return at restart.", false);
                        }

                        if (safetyCarController != null)
                        {
                            safetyCarController.BeginPitReturn();
                        }
                    }

                    restartControlTimer -= Time.deltaTime;
                    // Part C.2: a one-shot cold tyres/brakes warning partway through
                    // the "in this lap" window so it doesn't collide with the message
                    // right above it.
                    if (!coldTyresRestartWarningSent && restartControlTimer <= 8f)
                    {
                        coldTyresRestartWarningSent = true;
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Tyres will be cold at the restart, be careful on the first lap.", false);
                        }
                    }

                    if (restartControlTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.Restart;
                        restartControlTimer = 4f;
                        GameLog.Info("[RaceControl] Restart imminent.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Green flag imminent, get ready.", true);
                        }
                    }
                    break;

                case RaceControlState.Restart:
                    restartControlTimer -= Time.deltaTime;
                    if (restartControlTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.Green;
                        IsOvertakingAllowed = true;
                        drsRestartCooldownTimer = 45f;
                        postEscalationCooldownTimer = PostEscalationCooldownSeconds;
                        playerScPitPromptSent = false;
                        safetyCarQueueLeader = null;
                        GameLog.Info("[RaceControl] Restart complete, green flag.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Green flag, go go go!", true);
                            PostEngineerMessage("Green flag, control returned.", false);
                        }
                    }
                    break;
            }
        }

        void MaybePromptPlayerScPit()
        {
            if (playerScPitPromptSent || PlayerParticipant == null)
            {
                return;
            }

            if (!ShouldAiPitUnderSafetyCar(PlayerParticipant))
            {
                return;
            }

            playerScPitPromptSent = true;
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                // Feature: the prompt now reads correctly for whichever period is
                // actually active, and gives a rough strategic delta rather than a
                // generic "reduced time loss" line reused for both - a full SC
                // convoy is nearly free to pit into (the whole field is crawling)
                // while a VSC still costs the fixed pit-lane time-loss delta
                // relative to the reduced-pace field outside.
                bool fullSc = CurrentRaceControlState == RaceControlState.SafetyCarActive || CurrentRaceControlState == RaceControlState.SafetyCarDeploying;
                string message = fullSc
                    ? "Safety car deployed. Box now - the field is bunched, this is close to a free stop."
                    : "VSC deployed. Box now - the delta is much smaller than a green-flag stop.";
                PostEngineerMessage(message, true);
            }
        }

        // ---------- Overtaking legality (shared by AI decisions, player
        // enforcement, and penalty detection) ----------

        // True when this specific car may not gain positions right now: any full
        // VSC/SC/restart ban applies everywhere, a local yellow only inside the
        // flagged sector.
        public bool IsOvertakingRestrictedForParticipant(RaceParticipant participant)
        {
            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return false;
            }

            if (!IsOvertakingAllowed)
            {
                return true;
            }

            if (YellowFlagSector >= 0 && State != null && participant != null)
            {
                return State.GetCurrentProgress(participant).sector == YellowFlagSector;
            }

            return false;
        }

        // Passing a car that isn't actually racing (retired, pitting, being
        // guided, recovering from an incident, or crawling far off race pace) is
        // order correction, not an overtake - legal even under yellow/VSC/SC.
        public bool IsPositionCorrectionAllowed(RaceParticipant attacker, RaceParticipant defender)
        {
            if (defender == null || defender.vehicle == null || defender.retired || defender.finished)
            {
                return true;
            }

            if (defender.isPitting || defender.pitPhase != PitPhase.None || defender.pitLimiterUntilExit || defender.vehicle.IsPitGuided)
            {
                return true;
            }

            if (defender.recoveryState == CarRecoveryState.Recovering || defender.recoveryState == CarRecoveryState.ActuallyStranded)
            {
                return true;
            }

            // A car crawling because it's pacing behind the safety car queue is
            // NOT a hazard to be waved past - only a car that is slow for its
            // own reasons (damage, spin aftermath) counts as passable here.
            bool defenderPacingLegitimately = defender.recoveryState == CarRecoveryState.RaceControlPacing;
            bool defenderCrawling = Mathf.Abs(defender.vehicle.CurrentSpeedKph) < 30f;
            bool attackerAtPace = attacker != null && attacker.vehicle != null && Mathf.Abs(attacker.vehicle.CurrentSpeedKph) > 60f;
            return defenderCrawling && attackerAtPace && !defenderPacingLegitimately;
        }

        public bool CanParticipantOvertake(RaceParticipant attacker, RaceParticipant defender)
        {
            if (!IsOvertakingRestrictedForParticipant(attacker) && !IsOvertakingRestrictedForParticipant(defender))
            {
                return true;
            }

            return IsPositionCorrectionAllowed(attacker, defender);
        }

        // Illegal-overtake detection, rebuilt on full running-order snapshots
        // (Part 1): the old version compared only FindCarAhead/Behind within a
        // 40m window against the previous frame, which missed any pass where the
        // pair was ever more than 40m apart or where the order shuffled between
        // frames. This compares the complete eligible running order between two
        // snapshots ~0.5s apart: any car that moved ahead of a car it was behind
        // - while restrictions applied at BOTH snapshot times, and the move
        // isn't an allowed order correction - is penalized exactly once per
        // pair per cooldown window, player and AI identically.
        readonly List<RaceParticipant> raceControlOrderSnapshot = new List<RaceParticipant>();
        readonly Dictionary<string, float> illegalOvertakePairCooldowns = new Dictionary<string, float>();
        readonly List<string> expiredPairCooldownKeys = new List<string>();
        float orderSnapshotTimer;
        bool restrictionActiveAtLastSnapshot;
        const float OrderSnapshotInterval = 0.5f;
        const float IllegalOvertakePairCooldownSeconds = 25f;

        void CheckIllegalOvertakesUnderYellow()
        {
            if (State == null || CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial || StartCountdown > 0f)
            {
                raceControlOrderSnapshot.Clear();
                restrictionActiveAtLastSnapshot = false;
                return;
            }

            orderSnapshotTimer -= Time.deltaTime;
            if (orderSnapshotTimer > 0f)
            {
                return;
            }

            orderSnapshotTimer = OrderSnapshotInterval;

            expiredPairCooldownKeys.Clear();
            foreach (KeyValuePair<string, float> entry in illegalOvertakePairCooldowns)
            {
                if (RaceElapsed > entry.Value)
                {
                    expiredPairCooldownKeys.Add(entry.Key);
                }
            }
            for (int i = 0; i < expiredPairCooldownKeys.Count; i++)
            {
                illegalOvertakePairCooldowns.Remove(expiredPairCooldownKeys[i]);
            }

            List<RaceParticipant> currentOrder = new List<RaceParticipant>();
            List<RaceParticipant> running = GetRunningOrderSnapshot();
            for (int i = 0; i < running.Count; i++)
            {
                RaceParticipant candidate = running[i];
                // Race-control's own convoy autopilot moves cars around in the
                // queue for its own bookkeeping reasons (never a driver's overtake
                // decision) - excluded the same way pitting cars already are so it
                // can never false-positive an illegal-overtake penalty.
                if (candidate == null || candidate.vehicle == null || candidate.retired || candidate.finished ||
                    candidate.lapTracker == null || candidate.isPitting || candidate.pitPhase != PitPhase.None ||
                    candidate.isRaceControlAutopilot)
                {
                    continue;
                }

                currentOrder.Add(candidate);
            }

            bool restrictionActiveNow = !IsOvertakingAllowed || YellowFlagSector >= 0;
            // A pass must have happened between two snapshots that were BOTH
            // under restriction - a legal move made just before the flag came
            // out (or completed just after green) can never be penalized.
            if (restrictionActiveNow && restrictionActiveAtLastSnapshot && raceControlOrderSnapshot.Count > 1)
            {
                for (int c = 0; c < currentOrder.Count; c++)
                {
                    RaceParticipant mover = currentOrder[c];
                    int moverPreviousIndex = raceControlOrderSnapshot.IndexOf(mover);
                    if (moverPreviousIndex < 0)
                    {
                        // Not in the previous snapshot (was pitting/rejoining) -
                        // its first snapshot back establishes a baseline instead
                        // of being compared against a stale position.
                        continue;
                    }

                    for (int d = c + 1; d < currentOrder.Count; d++)
                    {
                        RaceParticipant passed = currentOrder[d];
                        int passedPreviousIndex = raceControlOrderSnapshot.IndexOf(passed);
                        if (passedPreviousIndex < 0 || passedPreviousIndex >= moverPreviousIndex)
                        {
                            continue;
                        }

                        // mover was behind `passed` last snapshot and is ahead
                        // now - a completed pass during the restricted window.
                        bool restrictedPair = IsOvertakingRestrictedForParticipant(mover) || IsOvertakingRestrictedForParticipant(passed);
                        if (!restrictedPair || IsPositionCorrectionAllowed(mover, passed))
                        {
                            continue;
                        }

                        string pairKey = mover.driverId + ">" + passed.driverId;
                        if (illegalOvertakePairCooldowns.ContainsKey(pairKey))
                        {
                            continue;
                        }

                        illegalOvertakePairCooldowns[pairKey] = RaceElapsed + IllegalOvertakePairCooldownSeconds;
                        string stateLabel = IsFullSafetyCarPeriod ? "safety car" : (IsVirtualSafetyCarActive ? "VSC" : "yellow");
                        AddPenalty(mover, 5f, "Overtake under " + stateLabel);
                        GameLog.Warn("[RaceControl] Illegal overtake penalty: " + mover.driverName + " passed " + passed.driverName +
                            " under " + stateLabel + " (state=" + CurrentRaceControlState + ", yellowSector=" + YellowFlagSector + ") (+5s).");
                        if (mover.isPlayer)
                        {
                            SessionMessage = "Overtake under " + stateLabel + ": +5s - give the position back";
                            if (Settings != null && Settings.Current.raceControlMessages)
                            {
                                PostEngineerMessage("That pass wasn't allowed under " + stateLabel + " - give the position back.", true);
                            }
                        }
                        else if (passed.isPlayer && Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage(mover.driverName + " passed you illegally - race control has given them 5 seconds.", false);
                        }
                    }
                }
            }

            raceControlOrderSnapshot.Clear();
            raceControlOrderSnapshot.AddRange(currentOrder);
            restrictionActiveAtLastSnapshot = restrictionActiveNow;
        }

        // AI (and, via RecommendedPitUnderSafetyCar, the player HUD) pit-under-SC
        // decision: strongly favour pitting when there is a real strategic reason to
        // (mandatory stop owed, tyres worn, the planned window is close, or the
        // compound is wrong for the current weather) and there is enough race left
        // for it to matter; avoid it otherwise (just stopped, tyres still fresh, or
        // the race is basically over).
        public bool ShouldAiPitUnderSafetyCar(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.vehicle.Tyres == null || participant.lapTracker == null ||
                CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return false;
            }

            bool underSafetyPeriod = CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                                      CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                                      CurrentRaceControlState == RaceControlState.SafetyCarDeploying;
            if (!underSafetyPeriod)
            {
                return false;
            }

            if (participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return false;
            }

            int completedLaps = participant.lapTracker.CompletedLaps;
            if (completedLaps < 1)
            {
                return false;
            }

            int lapsRemaining = Mathf.Max(0, RaceLaps - completedLaps);
            if (lapsRemaining <= 1)
            {
                return false;
            }

            float wear = participant.vehicle.Tyres.Wear;
            bool freshTyres = participant.pitStops > 0 && wear > 0.85f;
            if (freshTyres)
            {
                return false;
            }

            // Part A.9: avoid double-stacking two SC-triggered pit calls into the
            // same box at once - if another car is already servicing or entering a
            // box at or adjacent to this car's own box index, hold this request back
            // (re-checked every tick, so it releases the moment that box clears)
            // instead of sending both cars down pit lane into the same slot.
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant)
                {
                    continue;
                }

                bool otherOccupyingBox = other.pitPhase == PitPhase.Service || other.pitPhase == PitPhase.Entry;
                if (otherOccupyingBox && Mathf.Abs(other.pitBoxIndex - participant.pitBoxIndex) <= 1)
                {
                    return false;
                }
            }

            bool mandatoryStopOwed = participant.pitStops == 0;
            bool tyresWorn = wear < 0.55f;

            int windowLap = NextPlannedPitLapFor(participant);
            bool windowClose = windowLap > 0 && completedLaps >= windowLap - 3;

            WeatherState currentWeather = Track == null ? WeatherState.Clear : Track.weather;
            bool wetNow = currentWeather == WeatherState.LightRain || currentWeather == WeatherState.HeavyRain;
            TyreCompound currentCompound = participant.vehicle.Tyres.Compound;
            bool onWetTyre = currentCompound == TyreCompound.Intermediate || currentCompound == TyreCompound.Wet;
            bool weatherMismatch = wetNow != onWetTyre;

            if (mandatoryStopOwed && (tyresWorn || windowClose || weatherMismatch))
            {
                return true;
            }

            if (!mandatoryStopOwed && (weatherMismatch || (tyresWorn && windowClose)))
            {
                return true;
            }

            return false;
        }

        // Player-facing counterpart for a parallel HUD pass: identical logic, named
        // for what it means from the player's seat rather than the AI's.
        public bool RecommendedPitUnderSafetyCar(RaceParticipant participant)
        {
            return ShouldAiPitUnderSafetyCar(participant);
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
            engineerRivalSent = false;
            engineerTrackLimitsSent = false;
            engineerExpertWarningSent = false;
            engineerDrsWarningCooldown = 0f;
            lastGapReportLap = -1;
            weatherTransitionDone = false;
            weatherSecondTransitionDone = false;
            engineerMessageQueue.Clear();
            engineerMessageAnimTimer = 0f;
            playerLastPosition = -1;
            overtakeCheckTimer = 0f;
            sessionFastestLap = -1f;
            sessionFastestLapDriverId = "";
            engineerFlatSpotWarningSent = false;
            engineerLockupWarningSent = false;
            lastTeammateGapReportLap = -1;
            engineerPodiumMessageSent = false;
            hudToastQueue.Clear();
        }

        // Number of stops the player planned on the strategy screen, defensively
        // clamped to the 1-2 range GameSettingsStore already enforces on load.
        public int GetPlannedStopCount()
        {
            return Mathf.Clamp(Settings == null ? 1 : Settings.Current.plannedStopCount, 1, 2);
        }

        // stopIndex is 1 or 2. Falls back to a RecommendedPitLap-style window when the
        // player left the lap at 0 (engineer's recommendation). Stop 2, when it has no
        // explicit lap, targets roughly two-thirds of the way through what remains
        // after stop 1 and is always strictly later than the resolved stop-1 lap.
        public int GetPlannedPitLapForStop(int stopIndex)
        {
            int maxPitLap = Mathf.Max(1, RaceLaps - 1);
            if (stopIndex <= 1)
            {
                int plannedLapOne = Settings == null ? 0 : Settings.Current.plannedPitLapOne;
                if (plannedLapOne > 0)
                {
                    return Mathf.Clamp(plannedLapOne, 1, maxPitLap);
                }

                return Mathf.Clamp(RecommendedPitLap(PlayerParticipant), 1, maxPitLap);
            }

            int stopOneLap = GetPlannedPitLapForStop(1);
            int minStopTwoLap = Mathf.Min(maxPitLap, stopOneLap + 1);
            int plannedLapTwo = Settings == null ? 0 : Settings.Current.plannedPitLapTwo;
            if (plannedLapTwo > 0)
            {
                return Mathf.Clamp(plannedLapTwo, minStopTwoLap, maxPitLap);
            }

            int remaining = Mathf.Max(1, RaceLaps - stopOneLap);
            int recommended = stopOneLap + Mathf.RoundToInt(remaining * 0.66f);
            return Mathf.Clamp(recommended, minStopTwoLap, maxPitLap);
        }

        // stopIndex is 1 or 2; returns the planned compound name for that stop.
        public string GetPlannedCompoundForStop(int stopIndex)
        {
            if (Settings == null)
            {
                return stopIndex <= 1 ? "Hard" : "Medium";
            }

            return stopIndex <= 1 ? Settings.Current.plannedStopOneCompound : Settings.Current.plannedStopTwoCompound;
        }

        // Which lap the NEXT still-pending planned stop should happen on. Returns -1
        // when there is no more planned stop (1-stop plan already taken, or both
        // stops of a 2-stop plan already taken) so callers know not to prompt.
        // Non-player participants have no strategy plan and just use the generic
        // engineer recommendation, same as before.
        public int NextPlannedPitLapFor(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer)
            {
                return RecommendedPitLap(participant);
            }

            if (participant.pitStops == 0)
            {
                return GetPlannedPitLapForStop(1);
            }

            if (participant.pitStops == 1 && GetPlannedStopCount() >= 2)
            {
                return GetPlannedPitLapForStop(2);
            }

            return -1;
        }

        // Compound for the player's next pending planned stop, parsed from the
        // strategy screen's stored string the same way Settings.SelectedTyreCompound
        // parses the qualifying/race tyre choice. Falls back to the automatic
        // weather/degradation-based NextPitCompound when there is no plan to read
        // (parse failure, no pending planned stop, or a non-player participant) so
        // AI behaviour is unchanged.
        public TyreCompound NextPlannedPitCompoundFor(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer)
            {
                return NextPitCompound(participant);
            }

            int stopIndex = participant.pitStops + 1;
            if (stopIndex > GetPlannedStopCount())
            {
                return NextPitCompound(participant);
            }

            TyreCompound parsed;
            string planned = GetPlannedCompoundForStop(stopIndex);
            if (!string.IsNullOrEmpty(planned) && System.Enum.TryParse(planned, true, out parsed))
            {
                return parsed;
            }

            return NextPitCompound(participant);
        }

        // Gate for the engineer's pit prompts: true while a planned stop is still
        // owed for this participant.
        public bool ShouldPromptPlannedStop(RaceParticipant participant)
        {
            return NextPlannedPitLapFor(participant) > 0;
        }

        // Player pit plan: kept for any other/legacy callers. Now stop-aware -
        // resolves to whichever stop is currently pending (stop 1 if none taken yet,
        // stop 2 if the first is done and a 2-stop plan is selected), falling back to
        // the generic recommendation once there is no more planned stop.
        public int PlannedPitLapFor(RaceParticipant participant)
        {
            int next = NextPlannedPitLapFor(participant);
            return next > 0 ? next : RecommendedPitLap(participant);
        }

        void TickEngineerTimers()
        {
            engineerMessageAnimTimer += Time.deltaTime;
            engineerMessageTimer = Mathf.Max(0f, engineerMessageTimer - Time.deltaTime);
            engineerCooldown = Mathf.Max(0f, engineerCooldown - Time.deltaTime);
            reactionDisplayTimer = Mathf.Max(0f, reactionDisplayTimer - Time.deltaTime);
            playerResetCooldown = Mathf.Max(0f, playerResetCooldown - Time.deltaTime);
            engineerDrsWarningCooldown = Mathf.Max(0f, engineerDrsWarningCooldown - Time.deltaTime);

            if (engineerMessageTimer <= 0f && engineerMessageQueue.Count > 0)
            {
                AdvanceEngineerMessageQueue();
            }
        }

        void AdvanceEngineerMessageQueue()
        {
            if (engineerMessageQueue.Count == 0)
            {
                engineerMessageText = "";
                engineerMessageTimer = 0f;
                return;
            }

            EngineerMessageEntry next = engineerMessageQueue[0];
            engineerMessageQueue.RemoveAt(0);
            engineerMessageText = next.text;
            engineerMessageTimer = next.duration;
            engineerMessageAnimTimer = 0f;
        }

        // Part 1: messages now queue instead of instantly replacing one another.
        // Non-priority lines (routine pace/tyre/strategy chatter) append to a
        // small capped queue and animate in once their turn comes; priority lines
        // (safety car, penalties, pit calls) interrupt whatever is showing right
        // now but leave the rest of the queue intact so nothing is lost, just
        // delayed. Settings.raceControlMessages / engineerMessageVerbosity can
        // mute all of this without touching a single call site.
        void PostEngineerMessage(string message, bool priority)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (Settings != null && Settings.Current.engineerMessageVerbosity <= 0)
            {
                return;
            }

            string formatted = "ENGINEER: " + message;
            if (formatted == engineerMessageText && engineerMessageTimer > 1f)
            {
                return;
            }

            for (int i = 0; i < engineerMessageQueue.Count; i++)
            {
                if (engineerMessageQueue[i].text == formatted)
                {
                    return;
                }
            }

            // Minimal verbosity: only priority (urgent) lines get through at all.
            if (!priority && Settings != null && Settings.Current.engineerMessageVerbosity == 1)
            {
                return;
            }

            float duration = priority ? 7.5f : 5.5f;
            if (priority)
            {
                engineerMessageQueue.Insert(0, new EngineerMessageEntry { text = formatted, duration = duration });
                AdvanceEngineerMessageQueue();
            }
            else
            {
                if (engineerMessageQueue.Count >= EngineerMessageQueueCap)
                {
                    return;
                }

                engineerMessageQueue.Add(new EngineerMessageEntry { text = formatted, duration = duration });
                if (engineerMessageTimer <= 0f)
                {
                    AdvanceEngineerMessageQueue();
                }
            }
        }

        // Settings.cameraShakeLevel: 0 Off, 1 Low, 2 Standard (matches the historic
        // default feel), 3 High.
        static float CameraShakeLevelMultiplier(int level)
        {
            if (level <= 0) return 0f;
            if (level == 1) return 0.55f;
            if (level == 2) return 1f;
            return 1.4f;
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

            string planLine = GetPlannedStopCount() >= 2
                ? "Two-stop plan. First window around lap " + GetPlannedPitLapForStop(1) + " for " + GetPlannedCompoundForStop(1) +
                  "s, second around lap " + GetPlannedPitLapForStop(2) + " for " + GetPlannedCompoundForStop(2) + "s."
                : "One-stop plan. Target window around lap " + GetPlannedPitLapForStop(1) + " for " + GetPlannedCompoundForStop(1) + "s.";
            return "Weather is " + weather + ". Mandatory stop is active. " + planLine;
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

        // Part 1: player overtake/position-lost toasts+radio calls, and a
        // session-wide fastest-lap callout when the player sets the best lap of
        // the race so far (distinct from TrackPlayerBestLapRecord's personal-best/
        // all-time-local-record tracking). Throttled to a short interval - cheap
        // either way, but there's no reason to run the fastest-lap scan every frame.
        void UpdateOvertakeAndFastestLapNotifications()
        {
            if (PlayerParticipant == null || IsTimeTrial || CurrentSession == RaceWeekendSession.Qualifying)
            {
                return;
            }

            overtakeCheckTimer -= Time.deltaTime;
            if (overtakeCheckTimer > 0f)
            {
                return;
            }

            overtakeCheckTimer = 0.6f;

            int position = GetPosition(PlayerParticipant);
            bool eligibleForOvertakeCallout = playerLastPosition > 0 && position > 0 && position != playerLastPosition &&
                !PlayerParticipant.isPitting && PlayerParticipant.pitPhase == PitPhase.None &&
                StartCountdown <= 0f && RaceElapsed > 3f;
            if (eligibleForOvertakeCallout)
            {
                if (position < playerLastPosition)
                {
                    QueueHudToast("OVERTAKE! P" + position, ToastColorGreen);
                    PostEngineerMessage("Good overtake, you're up to P" + position + ".", false);
                }
                else
                {
                    QueueHudToast("POSITION LOST - P" + position, ToastColorAmber);
                    PostEngineerMessage("Position lost, now P" + position + ". Fight back.", false);
                }
            }

            if (position > 0)
            {
                playerLastPosition = position;
            }

            if (State != null)
            {
                for (int i = 0; i < State.Participants.Count; i++)
                {
                    RaceParticipant participant = State.Participants[i];
                    if (participant == null || participant.lapTracker == null)
                    {
                        continue;
                    }

                    float best = participant.lapTracker.BestLapTime;
                    if (best <= 0f || (sessionFastestLap > 0f && best >= sessionFastestLap - 0.001f))
                    {
                        continue;
                    }

                    sessionFastestLap = best;
                    sessionFastestLapDriverId = participant.driverId;
                    if (participant.isPlayer)
                    {
                        QueueHudToast("FASTEST LAP OF THE RACE", ToastColorPurple);
                        PostEngineerMessage("That's the fastest lap of the race so far.", true);
                    }
                }
            }
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

            // Part C.1: Expert-only radio warning, once, early in the race - staged
            // behind a short RaceElapsed gate so it doesn't collide with the opening
            // weather/strategy message's own display window.
            if (!engineerExpertWarningSent && Settings != null && Settings.Difficulty == RaceDifficulty.Expert && RaceElapsed > 2f)
            {
                engineerExpertWarningSent = true;
                PostEngineerMessage("Expert AI on track. It won't back off - defend hard and pick your moments to attack.", false);
                return;
            }

            // Part C.1: Expert-only "car behind has DRS and is closing" warning,
            // repeatable on a cooldown rather than a one-shot flag since the threat
            // can come and go several times over a race.
            if (Settings != null && Settings.Difficulty == RaceDifficulty.Expert && engineerDrsWarningCooldown <= 0f)
            {
                RaceParticipant chaserForDrsWarning = FindCarBehind(PlayerParticipant, 60f);
                if (chaserForDrsWarning != null && IsDrsAvailable(chaserForDrsWarning))
                {
                    engineerDrsWarningCooldown = 15f;
                    PostEngineerMessage("Car behind has DRS and is closing. Defend the inside line.", false);
                    return;
                }
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
            if (ShouldPromptPlannedStop(PlayerParticipant) && !PlayerParticipant.isPitting)
            {
                int targetLap = NextPlannedPitLapFor(PlayerParticipant);
                bool mandatoryStopStillOwed = PlayerParticipant.pitStops == 0;
                if (completedLaps >= targetLap && lastEngineerPitLapPrompt != completedLaps)
                {
                    lastEngineerPitLapPrompt = completedLaps;
                    TyreCompound plannedCompound = NextPlannedPitCompoundFor(PlayerParticipant);
                    float undercutGap = GetIntervalToAheadSeconds(PlayerParticipant);
                    string undercut = undercutGap > 0f && undercutGap < 2.5f ? " The undercut on the car ahead is live." : "";
                    string requirement = mandatoryStopStillOwed ? "Mandatory stop still required." : "Second stop window is here.";
                    PostEngineerMessage("Box this lap for " + plannedCompound + "s. " + requirement + undercut, true);
                    return;
                }

                if (completedLaps == Mathf.Max(0, targetLap - 1) && lastEngineerPitLapPrompt != completedLaps)
                {
                    lastEngineerPitLapPrompt = completedLaps;
                    string label = mandatoryStopStillOwed ? "Pit window opens next lap. Think about the undercut." : "Second stop window opens next lap.";
                    PostEngineerMessage(label, false);
                    return;
                }
            }

            if (PlayerParticipant.trackLimitWarnings >= 2 && !engineerTrackLimitsSent)
            {
                engineerTrackLimitsSent = true;
                PostEngineerMessage("Careful with track limits. One more warning is a time penalty.", true);
                return;
            }

            if (!engineerRivalSent && IsCareerRace && Career != null && Career.Save != null && !string.IsNullOrEmpty(Career.Save.rivalDriverId))
            {
                RaceParticipant rivalAhead = FindCarAhead(PlayerParticipant, 70f);
                RaceParticipant rivalBehind = FindCarBehind(PlayerParticipant, 70f);
                if (rivalAhead != null && rivalAhead.driverId == Career.Save.rivalDriverId)
                {
                    engineerRivalSent = true;
                    PostEngineerMessage("That's your rival ahead." + RivalTraitHint(rivalAhead) + " Beat him and the team will notice.", false);
                    return;
                }

                if (rivalBehind != null && rivalBehind.driverId == Career.Save.rivalDriverId)
                {
                    engineerRivalSent = true;
                    PostEngineerMessage("Your rival is right behind." + RivalTraitHint(rivalBehind) + " Keep it clean, hold the position.", false);
                    return;
                }
            }

            // Teammate gap callout, on the off-parity laps so it never collides
            // with the every-2-laps pace report below.
            if (completedLaps >= 3 && completedLaps % 2 == 1 && lastTeammateGapReportLap != completedLaps && engineerCooldown <= 0f)
            {
                RaceParticipant teammate = FindTeammate(PlayerParticipant);
                if (teammate != null)
                {
                    lastTeammateGapReportLap = completedLaps;
                    float gapSeconds = GetGapBetweenSeconds(PlayerParticipant, teammate);
                    bool teammateAhead = GetPosition(teammate) < GetPosition(PlayerParticipant);
                    string gapText = Mathf.Abs(gapSeconds).ToString("0.0");
                    PostEngineerMessage(teammateAhead
                        ? "Teammate is " + gapText + " seconds ahead."
                        : "Teammate is " + gapText + " seconds behind.", false);
                    return;
                }
            }

            // Periodic pace report every couple of laps when nothing urgent is up.
            if (completedLaps >= 2 && completedLaps % 2 == 0 && lastGapReportLap != completedLaps && engineerCooldown <= 0f)
            {
                lastGapReportLap = completedLaps;
                if (GetPosition(PlayerParticipant) == 1)
                {
                    float gapBehind = 0f;
                    RaceParticipant chaser = FindCarBehind(PlayerParticipant, 400f);
                    if (chaser != null)
                    {
                        gapBehind = GetIntervalToAheadSeconds(chaser);
                    }

                    PostEngineerMessage(gapBehind > 0.05f
                        ? "You're leading, gap behind " + gapBehind.ToString("0.0") + "s. Manage the tyres."
                        : "You're leading. Manage the tyres and keep it clean.", false);
                    return;
                }

                float interval = GetIntervalToAheadSeconds(PlayerParticipant);
                if (interval > 0.05f && interval < 1.2f)
                {
                    PostEngineerMessage("Car ahead " + interval.ToString("0.0") + "s. You're in DRS range, go get him.", false);
                }
                else if (interval > 0.05f)
                {
                    PostEngineerMessage("Gap to the car ahead " + interval.ToString("0.0") + "s. Consistent laps now.", false);
                }

                return;
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

            if (car.Tyres.FlatSpotLevel > 0.6f && !engineerFlatSpotWarningSent)
            {
                engineerFlatSpotWarningSent = true;
                PostEngineerMessage("Heavy flat spot. Consider boxing, it will vibrate through the braking zones.", true);
                return;
            }

            if (car.Tyres.LastLockupSeverity > 0.55f && !engineerLockupWarningSent)
            {
                engineerLockupWarningSent = true;
                PostEngineerMessage("Big lockup there. Ease the brake pressure into the next few corners.", false);
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
                // Distinguish "still turning into the lane" from "aligned and now
                // driving to the box" - both used to collapse into one vague string.
                return participant.pitEntryAligned
                    ? "PIT LANE  DRIVING TO BOX " + (participant.pitBoxIndex + 1) + "  LIMITER 80"
                    : "PIT ENTRY  LIMITER 80";
            }

            if (participant.pitPhase == PitPhase.Service)
            {
                if (participant.pitAwaitingRelease)
                {
                    return "PIT HOLD  AWAITING RELEASE GAP";
                }

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

        // ---------- Player race-control pace parity (Task 2/3/5) ----------
        // AI has been pace-clamped under VSC/SC in AiVehicleController for several
        // passes; the player was never held to the same rule and could just drive
        // flat-out through a safety car period. These give the player the same
        // physical constraint instead of relying on penalties alone.
        public bool IsVirtualSafetyCarActive { get { return CurrentRaceControlState == RaceControlState.VirtualSafetyCar; } }

        public bool IsFullSafetyCarPeriod
        {
            get
            {
                return CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                       CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                       CurrentRaceControlState == RaceControlState.SafetyCarInThisLap;
            }
        }

        public bool IsRaceControlPaceLimited { get { return IsVirtualSafetyCarActive || IsFullSafetyCarPeriod; } }

        // Part 2: a local yellow only limits speed for cars actually near the
        // incident that caused it, not the entire lap-third sector it's flagged
        // in - a genuine "progress window around the incident", tighter than the
        // sector-wide overtake ban above (which deliberately stays sector-wide,
        // since that's about not passing near a hazard you might not see yet).
        const float LocalYellowSpeedCapWindowMeters = 180f;
        const float LocalYellowSpeedCapKph = 210f;

        public bool IsNearLocalYellowIncident(RaceParticipant participant)
        {
            if (participant == null || State == null || Track == null || CurrentRaceControlState != RaceControlState.YellowSector)
            {
                return false;
            }

            TrackProgress progress = State.GetCurrentProgress(participant);
            return Mathf.Abs(Track.WrapDistance(progress.distance - lastIncidentDistance)) < LocalYellowSpeedCapWindowMeters;
        }

        // The allowed speed cap for this specific car's current race-control
        // situation - pit lane has its own dedicated limiter so it's excluded
        // here, VSC/SC apply field-wide, and local yellow only applies to a car
        // actually near the incident. Shared by both the player's own warning/
        // penalty logic below and the field-wide physical cap applied to every
        // car (player and AI alike) in ApplyRaceControlSpeedCaps.
        public float RaceControlSpeedCapKphFor(RaceParticipant participant)
        {
            if (participant == null || participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return 9999f;
            }

            // Deploying and "in this lap" allow a little more than the steady
            // active/VSC caps since the field is still catching up to the queue
            // or about to go green.
            switch (CurrentRaceControlState)
            {
                case RaceControlState.VirtualSafetyCar:
                    return 190f;
                case RaceControlState.SafetyCarDeploying:
                    return SafetyCarTargetSpeedKph + 30f;
                case RaceControlState.SafetyCarActive:
                    return SafetyCarTargetSpeedKph;
                case RaceControlState.SafetyCarInThisLap:
                    return SafetyCarTargetSpeedKph + 15f;
                default:
                    return IsNearLocalYellowIncident(participant) ? LocalYellowSpeedCapKph : 9999f;
            }
        }

        // Physically caps every car's real top speed to its own current
        // race-control situation - the "pit-limiter-style" hard enforcement the
        // brief calls for, applied identically to the player and every AI car
        // rather than the player alone relying on the softer shaping below.
        void ApplyRaceControlSpeedCaps()
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null || participant.vehicle == null || participant.retired || participant.finished)
                {
                    continue;
                }

                participant.vehicle.SetRaceControlSpeedCap(RaceControlSpeedCapKphFor(participant));
            }
        }

        // Soft pace limiter applied to the PLAYER's own command, mirroring the AI
        // speed clamp in AiVehicleController.Update(). Never touches the AI's
        // command (they're already clamped elsewhere), never fights pit entry/
        // exit/limiter, and always forces ERS/DRS off while pace-limited - it is
        // the single place both of those get enforced for the player, so a Shift-
        // key press or a still-latched DRS press can never bypass race control.
        public VehicleCommand ApplyPlayerRaceControlLimiter(RaceParticipant participant, VehicleCommand command, float currentSpeedKph)
        {
            IsPlayerOverRaceControlPace = false;
            if (participant == null || !participant.isPlayer || CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return command;
            }

            if (participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                // Pit limiter/guidance already governs speed here - don't stack a
                // second, conflicting speed rule on top of it.
                playerRaceControlOverspeedTimer = 0f;
                playerRaceControlWarningSent = false;
                IsPlayerRaceControlWarningActive = false;
                return command;
            }

            bool localYellowHere = IsNearLocalYellowIncident(participant);
            if (!IsRaceControlPaceLimited && !localYellowHere)
            {
                playerRaceControlOverspeedTimer = 0f;
                playerRaceControlWarningSent = false;
                IsPlayerRaceControlWarningActive = false;
                return command;
            }

            // Both reasons this method is active (VSC/SC pace limiting, or being
            // near a local yellow incident) already ban DRS via IsDrsAvailable and
            // ERS deployment makes no sense while being held to a reduced pace
            // either way - force both off unconditionally here as the single place
            // a stale Shift press or already-latched DRS can't sneak past either.
            command.ers = false;
            command.drs = false;

            float cap = RaceControlSpeedCapKphFor(participant);
            float overspeed = currentSpeedKph - cap;
            if (overspeed <= 0f)
            {
                playerRaceControlOverspeedTimer = 0f;
                playerRaceControlWarningSent = false;
                IsPlayerRaceControlWarningActive = false;
                return command;
            }

            IsPlayerOverRaceControlPace = true;

            // Soft shaping, not a pit-limiter-style hard wall: throttle bleeds off
            // over the first 15kph of overspeed, and only meaningfully-over cars
            // (>15kph) get proportional brake on top, capped well short of a full
            // stomp so it never fights the player violently mid-corner.
            float throttleCut = Mathf.Clamp01(overspeed / 15f);
            command.throttle = Mathf.Min(command.throttle, 1f - throttleCut);
            if (overspeed > 15f)
            {
                float brakeAmount = Mathf.Clamp01((overspeed - 15f) / 40f) * 0.55f;
                command.brake = Mathf.Max(command.brake, brakeAmount);
            }

            // Warn first, then penalize only if the player stays grossly over the
            // cap after the warning - never an instant penalty.
            playerRaceControlOverspeedTimer += Time.deltaTime;
            if (!playerRaceControlWarningSent && playerRaceControlOverspeedTimer > 2.5f)
            {
                playerRaceControlWarningSent = true;
                IsPlayerRaceControlWarningActive = true;
                SessionMessage = "Slow down: over the race control pace limit";
                if (Settings != null && Settings.Current.raceControlMessages)
                {
                    PostEngineerMessage("You're over the pace limit, slow down.", true);
                }
            }
            else if (playerRaceControlWarningSent && overspeed > 25f && playerRaceControlOverspeedTimer > 6f)
            {
                AddPenalty(participant, 5f, localYellowHere && !IsRaceControlPaceLimited ? "Ignored yellow flag speed limit" : "Ignored safety car pace");
                GameLog.Warn("[RaceControl] Player pace penalty: " + overspeed.ToString("0") + "kph over cap for " + playerRaceControlOverspeedTimer.ToString("0.0") + "s (+5s).");
                SessionMessage = "Pace limit ignored: +5s";
                playerRaceControlOverspeedTimer = 0f;
                playerRaceControlWarningSent = false;
                IsPlayerRaceControlWarningActive = false;
            }

            return command;
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

            // DRS is off under any safety-car period and for a short cooldown after
            // the restart - real F1 rule, and the simplest correct place to gate it.
            if (drsRestartCooldownTimer > 0f ||
                CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                CurrentRaceControlState == RaceControlState.SafetyCarInThisLap ||
                CurrentRaceControlState == RaceControlState.Restart)
            {
                return false;
            }

            // A local yellow disables DRS for any car in the flagged sector (the
            // same scope as the overtaking ban - DRS exists purely to overtake),
            // plus the tighter near-incident window used by the speed cap.
            if (IsNearLocalYellowIncident(participant) || IsOvertakingRestrictedForParticipant(participant))
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

            float battery = participant.vehicle.ErsBattery;
            if (cornerSeverity > 0.24f || battery < 0.18f)
            {
                return false;
            }

            // ERS deployment is disabled through the safety-car/VSC period itself -
            // nobody is racing for position, so there is nothing to spend it on -
            // but it comes back at the restart where a strong launch matters.
            if (CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                CurrentRaceControlState == RaceControlState.SafetyCarInThisLap ||
                IsNearLocalYellowIncident(participant) ||
                IsOvertakingRestrictedForParticipant(participant))
            {
                return false;
            }

            AiDifficultyProfile profile = GetAiDifficultyProfile();
            int awareness = participant.driverData == null ? 78 : participant.driverData.awareness;
            float ersQuality = Mathf.Clamp01(profile.ersDeploymentQuality * Mathf.Lerp(0.8f, 1.08f, awareness / 100f));

            bool finalLap = CurrentSession != RaceWeekendSession.Qualifying && participant.lapTracker != null && participant.lapTracker.CompletedLaps >= RaceLaps - 1;
            float normalized = participant.lapTracker == null ? 0f : (State == null ? participant.lapTracker.CurrentProgress.normalized : State.GetCurrentProgress(participant).normalized);
            bool finalSector = normalized > 0.68f;
            bool batteryHigh = battery > 0.85f;

            // Never hoard a near-full battery, and always spend it coming home - these
            // are decisions even a weak driver gets right, so they bypass the quality
            // gate entirely.
            if (batteryHigh || finalLap || finalSector)
            {
                return true;
            }

            float aheadInterval = GetIntervalToAheadSeconds(participant);
            RaceParticipant behind = FindCarBehind(participant, 70f);
            bool attacking = aheadInterval < 1.6f;
            bool behindHasDrs = behind != null && IsDrsAvailable(behind);
            bool isExpert = IsExpertDifficulty;

            // Part A.4: Expert's defend trigger is far more sensitive - a chasing car
            // with DRS, a healthily-charged battery, or simply closing fast all count
            // as a real threat, not only a comfortably-charged battery alone.
            bool closingFast = isExpert && behind != null && behind.vehicle != null &&
                (Mathf.Abs(behind.vehicle.CurrentSpeedKph) - Mathf.Abs(participant.vehicle.CurrentSpeedKph)) > 6f;
            float defendBatteryThreshold = isExpert ? 0.15f : 0.32f;
            bool defending = behind != null && (battery > defendBatteryThreshold || behindHasDrs || closingFast);

            if (!attacking && !defending)
            {
                // Push-lap deploy: a real driver spends ERS on a clear straight with
                // battery to spare generally, not only while directly racing someone.
                // Kept modest and scaled by difficulty so it never becomes constant spam.
                if (battery > 0.5f)
                {
                    // Part A.2: Expert-only, deterministic - a push-lap deploy with
                    // battery to spare is an obvious call, not a coin flip.
                    return isExpert || Random.value < profile.ersDeploymentQuality * 0.5f;
                }

                return false;
            }

            // Racecraft calls (attack/defend timing) are where difficulty and driver
            // awareness actually show up: Expert nails them almost every time, Easy
            // fluffs a meaningful share. Part A.2: Expert is fully deterministic here
            // - once attacking/defending is true the condition itself is the decision,
            // not a dice roll on top of it.
            return isExpert || Random.value < ersQuality;
        }

        // Difficulty as decision-making quality, never a raw speed/grip multiplier.
        // brakeDistanceMultiplier, minimumCornerSpeedConfidence, apexErrorMeters,
        // throttleDelay, exitThrottleConfidence, reactionTimeSeconds,
        // mistakeChancePerLap, trafficAvoidanceCaution, overtakeCommitment,
        // defendCommitment, ersDeploymentQuality and drsUsageQuality are all consumed
        // by AiVehicleController; wetWeatherCaution and lineOffsetNoise round out the
        // per-corner and per-frame driving model. Ordering must hold on every axis:
        // Expert closest to the true limit, Easy the most forgiving.
        public struct AiDifficultyProfile
        {
            public float brakeDistanceMultiplier;
            public float minimumCornerSpeedConfidence;
            public float apexErrorMeters;
            public float throttleDelay;
            public float exitThrottleConfidence;
            public float lineOffsetNoise;
            public float reactionTimeSeconds;
            public float overtakeCommitment;
            public float defendCommitment;
            public float ersDeploymentQuality;
            public float drsUsageQuality;
            public float mistakeChancePerLap;
            public float trafficAvoidanceCaution;
            public float wetWeatherCaution;
            public float tyreSavingBias;

            // Explicit pace scaling on top of the decision-quality model above - same
            // car, same physical envelope, but a more skilled/confident difficulty
            // tier actually drives closer to that envelope instead of only deciding
            // slightly better. straightSpeedMultiplier is always clamped to <= 1.0
            // wherever it touches a real top-speed ceiling; the others may legitimately
            // exceed 1.0 for Hard/Expert since a corner apex or braking point is a
            // driving-skill judgment call, not a hard physics limit.
            public float paceMultiplier;
            public float cornerSpeedMultiplier;
            public float straightSpeedMultiplier;
            public float brakeConfidenceMultiplier;
            public float throttleAggressionMultiplier;
        }

        public AiDifficultyProfile GetAiDifficultyProfile()
        {
            RaceDifficulty difficulty = Settings.Difficulty;
            if (difficulty == RaceDifficulty.Easy)
            {
                return new AiDifficultyProfile
                {
                    brakeDistanceMultiplier = 0.80f,
                    minimumCornerSpeedConfidence = 0.72f,
                    apexErrorMeters = 2.6f,
                    throttleDelay = 0.55f,
                    exitThrottleConfidence = 0.62f,
                    lineOffsetNoise = 1.4f,
                    reactionTimeSeconds = 0.85f,
                    overtakeCommitment = 0.35f,
                    defendCommitment = 0.30f,
                    ersDeploymentQuality = 0.40f,
                    drsUsageQuality = 0.55f,
                    mistakeChancePerLap = 0.16f,
                    trafficAvoidanceCaution = 1.35f,
                    wetWeatherCaution = 1.5f,
                    tyreSavingBias = 0.35f,
                    paceMultiplier = 0.96f,
                    cornerSpeedMultiplier = 0.94f,
                    straightSpeedMultiplier = 0.95f,
                    brakeConfidenceMultiplier = 0.85f,
                    throttleAggressionMultiplier = 0.75f
                };
            }

            if (difficulty == RaceDifficulty.Medium)
            {
                return new AiDifficultyProfile
                {
                    brakeDistanceMultiplier = 0.94f,
                    minimumCornerSpeedConfidence = 0.85f,
                    apexErrorMeters = 1.4f,
                    throttleDelay = 0.30f,
                    exitThrottleConfidence = 0.78f,
                    lineOffsetNoise = 0.75f,
                    reactionTimeSeconds = 0.55f,
                    overtakeCommitment = 0.55f,
                    defendCommitment = 0.55f,
                    ersDeploymentQuality = 0.65f,
                    drsUsageQuality = 0.75f,
                    mistakeChancePerLap = 0.09f,
                    trafficAvoidanceCaution = 1.05f,
                    wetWeatherCaution = 1.2f,
                    tyreSavingBias = 0.20f,
                    paceMultiplier = 1.01f,
                    cornerSpeedMultiplier = 1.00f,
                    straightSpeedMultiplier = 0.98f,
                    brakeConfidenceMultiplier = 1.00f,
                    throttleAggressionMultiplier = 1.00f
                };
            }

            if (difficulty == RaceDifficulty.Hard)
            {
                // Nudged up from the previous Hard tier so the Hard -> Expert gap stays
                // meaningful once Expert is pushed to its ceiling below - Hard is now a
                // clearly strong, but not ruthless, tier of its own.
                return new AiDifficultyProfile
                {
                    brakeDistanceMultiplier = 1.04f,
                    minimumCornerSpeedConfidence = 0.96f,
                    apexErrorMeters = 0.55f,
                    throttleDelay = 0.11f,
                    exitThrottleConfidence = 0.93f,
                    lineOffsetNoise = 0.28f,
                    reactionTimeSeconds = 0.26f,
                    overtakeCommitment = 0.82f,
                    defendCommitment = 0.84f,
                    ersDeploymentQuality = 0.90f,
                    drsUsageQuality = 0.97f,
                    mistakeChancePerLap = 0.032f,
                    trafficAvoidanceCaution = 0.65f,
                    wetWeatherCaution = 0.95f,
                    tyreSavingBias = 0.10f,
                    paceMultiplier = 1.12f,
                    cornerSpeedMultiplier = 1.10f,
                    straightSpeedMultiplier = 1.00f,
                    brakeConfidenceMultiplier = 1.18f,
                    throttleAggressionMultiplier = 1.32f
                };
            }

            // Expert - pushed to the practical ceiling on every decision-quality axis
            // (Part A.1). straightSpeedMultiplier is the one hard rule that can never
            // move past 1.0 since it scales against vehicle.TargetTopSpeedKph, the
            // same DRS/ERS-aware physics ceiling the player's own car uses; every
            // other axis here is at or effectively at its practical maximum.
            return new AiDifficultyProfile
            {
                brakeDistanceMultiplier = 1.10f,
                minimumCornerSpeedConfidence = 1.00f,
                apexErrorMeters = 0.05f,
                throttleDelay = 0.01f,
                exitThrottleConfidence = 1.00f,
                lineOffsetNoise = 0.05f,
                reactionTimeSeconds = 0.04f,
                overtakeCommitment = 0.99f,
                defendCommitment = 0.99f,
                ersDeploymentQuality = 1.00f,
                drsUsageQuality = 1.00f,
                mistakeChancePerLap = 0.0015f,
                trafficAvoidanceCaution = 0.22f,
                wetWeatherCaution = 0.85f,
                tyreSavingBias = 0.05f,
                paceMultiplier = 1.20f,
                cornerSpeedMultiplier = 1.20f,
                straightSpeedMultiplier = 1.00f,
                brakeConfidenceMultiplier = 1.50f,
                throttleAggressionMultiplier = 1.80f
            };
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

        // Generic gap-in-seconds between any two participants (not necessarily
        // adjacent on track), used for teammate/rival callouts where the pair
        // could be several cars apart. Positive when `a` is ahead of `b`.
        public float GetGapBetweenSeconds(RaceParticipant a, RaceParticipant b)
        {
            if (a == null || b == null || a.lapTracker == null || b.lapTracker == null)
            {
                return 0f;
            }

            float aDistance = State == null ? a.lapTracker.TotalProgressDistance : State.GetProgressDistance(a);
            float bDistance = State == null ? b.lapTracker.TotalProgressDistance : State.GetProgressDistance(b);
            float deltaMeters = aDistance - bDistance;
            float refSpeed = Mathf.Max(24f, a.vehicle == null ? 36f : Mathf.Abs(a.vehicle.CurrentSpeedKph) / 3.6f);
            return deltaMeters / refSpeed;
        }

        // Part 8: a short trait-flavored aside for the rival radio callout - only
        // fires for the traits that actually change how to race them.
        string RivalTraitHint(RaceParticipant rival)
        {
            if (rival == null || rival.driverData == null)
            {
                return "";
            }

            List<string> traits = DriverTraits.Compute(rival.driverData);
            if (traits.Contains("Aggressive Overtaker"))
            {
                return " He attacks early, don't leave a gap.";
            }

            if (traits.Contains("Defensive Wall"))
            {
                return " He defends hard, get a clean run before you commit.";
            }

            if (traits.Contains("Error-Prone"))
            {
                return " He's error-prone under pressure, stay close.";
            }

            return "";
        }

        public RaceParticipant FindTeammate(RaceParticipant participant)
        {
            if (participant == null || State == null)
            {
                return null;
            }

            for (int i = 0; i < State.Participants.Count; i++)
            {
                RaceParticipant candidate = State.Participants[i];
                if (candidate != null && candidate != participant && candidate.teamId == participant.teamId)
                {
                    return candidate;
                }
            }

            return null;
        }

        public List<RaceParticipant> GetRunningOrderSnapshot()
        {
            SortRunningOrder();
            return State != null ? new List<RaceParticipant>(State.SortedOrder) : new List<RaceParticipant>();
        }

        // Called by AiVehicleController on the AttackingInside/AttackingOutside/
        // SideBySide -> CompletingPass edge, once per completed overtake, for the
        // post-race diagnostics log.
        public void ReportAiOvertakeCompleted(RaceParticipant participant)
        {
            AiOvertakesCompletedCount++;
            if (participant != null)
            {
                participant.overtakesCompleted++;
            }
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

        // Structured qualifying timing tower row so RaceHud renders directly into
        // real Text cells instead of parsing a hand-padded string back apart.
        public struct QualifyingTowerRow
        {
            public int position;
            public string driverCode;
            public string bestTimeText;
            public string gapText;
            public bool isPlayer;
        }

        public List<QualifyingTowerRow> BuildQualifyingTowerRows(int maxRows)
        {
            List<QualifyingTowerRow> rows = new List<QualifyingTowerRow>();
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            active.Sort((a, b) => GetDisplayQualifyingTime(a).CompareTo(GetDisplayQualifyingTime(b)));
            float pole = GetQualifyingPoleReferenceTime();
            int count = Mathf.Min(maxRows, active.Count);
            for (int i = 0; i < count; i++)
            {
                QualifyingSimEntry entry = active[i];
                float time = GetDisplayQualifyingTime(entry);
                string best = time >= 9998f ? "--:--.---" : UiFactory.FormatTime(time);
                string gap = time >= 9998f || pole <= 0f ? "--" : (Mathf.Abs(time - pole) < 0.001f ? "P1" : "+" + (time - pole).ToString("0.000"));
                rows.Add(new QualifyingTowerRow
                {
                    position = i + 1,
                    driverCode = DriverCode(entry.driverName),
                    bestTimeText = best,
                    gapText = gap,
                    isPlayer = entry.isPlayer
                });
            }

            return rows;
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

            // Without a usable qualifying result (the common quick-race path, since
            // quick race is never a career race) the player no longer defaults to
            // pole - the fallback itself is difficulty-scaled. AI fallback slots are
            // then built around whichever slot the player lands in so the two streams
            // can never collide.
            int playerGridFallback = CurrentSession == RaceWeekendSession.Qualifying ? 0 : ResolvePlayerGridFallback();
            PlayerParticipant = SpawnParticipant(
                "player",
                playerName,
                playerTeam.id,
                playerTeam.shortName,
                true,
                null,
                playerTeam,
                playerCar,
                ResolveGridIndex("player", playerGridFallback));

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

            List<DriverData> aiDrivers = GetDefensiveAiRoster(playerTeamId, playerName);
            int aiFallbackSlot = 0;
            for (int i = 0; i < aiDrivers.Count; i++)
            {
                if (aiFallbackSlot == playerGridFallback)
                {
                    aiFallbackSlot++;
                }

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
                    ResolveGridIndex(driver.id, aiFallbackSlot));
                aiFallbackSlot++;
            }
        }

        // Difficulty-scaled starting slot used only when no real qualifying result
        // exists for this session (quick race with no qualifying run, in practice).
        // 0-based index; Expert lands dead last against the full 21-car AI field.
        int ResolvePlayerGridFallback()
        {
            int lastIndex = Mathf.Max(0, FullWeekendAiCount);
            RaceDifficulty difficulty = Settings.Difficulty;
            if (difficulty == RaceDifficulty.Easy)
            {
                return Mathf.Clamp(Random.Range(4, 8), 0, lastIndex);
            }

            if (difficulty == RaceDifficulty.Medium)
            {
                return Mathf.Clamp(Random.Range(9, 14), 0, lastIndex);
            }

            if (difficulty == RaceDifficulty.Hard)
            {
                return Mathf.Clamp(Random.Range(15, 20), 0, lastIndex);
            }

            return lastIndex;
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

            List<DriverData> aiDrivers = GetDefensiveAiRoster(playerTeamId, PlayerParticipant != null ? PlayerParticipant.driverName : "");
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

            List<DriverData> aiDrivers = GetDefensiveAiRoster(playerTeamId, playerName);
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

        // Defensive duplicate check (career roster fix): the normal exclusion
        // above (via Data.GetAiRaceDrivers's replacedDriverId param) is the
        // primary mechanism, but this is the one place all three roster builders
        // (race grid, live qualifying, sim qualifying) funnel through, so a
        // second, independent identity check lives here as a safety net - if the
        // primary exclusion is ever wrong (a future bug, a save file from before
        // this fix, or a custom driver name that happens to collide with a real
        // one), the player never ends up racing against an AI copy of themselves.
        // Removing a collision without backfilling would silently shrink the
        // grid below 22, so a replacement is pulled from the full driver
        // database rather than just dropping the seat.
        List<DriverData> GetDefensiveAiRoster(string playerTeamId, string playerDisplayName)
        {
            string replacedId = ReplacedDriverIdForPlayerTeam(playerTeamId);
            List<DriverData> aiDrivers = Data.GetAiRaceDrivers(playerTeamId, FullWeekendAiCount, replacedId);

            string playerDriverId = Career != null && Career.Save != null ? Career.Save.selectedDriverId : "";
            // Sim qualifying never spawns a PlayerParticipant (it's a pure
            // simulation), so the identity has to come from whichever caller
            // actually knows the player's name for this session rather than
            // reading a participant reference that may be null or stale here.
            string playerName = string.IsNullOrEmpty(playerDisplayName) && PlayerParticipant != null ? PlayerParticipant.driverName : playerDisplayName;
            int removed = 0;
            for (int i = aiDrivers.Count - 1; i >= 0; i--)
            {
                DriverData candidate = aiDrivers[i];
                bool idCollision = !string.IsNullOrEmpty(playerDriverId) && candidate.id == playerDriverId;
                bool nameCollision = !string.IsNullOrEmpty(playerName) && candidate.displayName == playerName;
                if (idCollision || nameCollision)
                {
                    GameLog.Warn("[Roster] Removed duplicate AI driver '" + candidate.displayName + "' (" + candidate.id + ") - matches the player's identity.");
                    aiDrivers.RemoveAt(i);
                    removed++;
                }
            }

            for (int i = 0; removed > 0 && i < Data.Drivers.drivers.Count; i++)
            {
                DriverData candidate = Data.Drivers.drivers[i];
                if (candidate.id == replacedId || candidate.id == playerDriverId || candidate.displayName == playerName)
                {
                    continue;
                }

                bool alreadyUsed = false;
                for (int j = 0; j < aiDrivers.Count; j++)
                {
                    if (aiDrivers[j].id == candidate.id)
                    {
                        alreadyUsed = true;
                        break;
                    }
                }

                if (!alreadyUsed)
                {
                    aiDrivers.Add(candidate);
                    removed--;
                }
            }

            return aiDrivers;
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
            float gridDistance;
            float lane;
            Track.GetGridSlot(gridIndex, out gridDistance, out lane);
            Track.SampleAtDistance(gridDistance, out point, out forward, out right);
            Vector3 spawnPosition = FindRoadSpawnPosition(point + right * lane, driverName, out bool hitRoad);
            Quaternion spawnRotation = Quaternion.LookRotation(forward, Vector3.up);
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                // Qualifying runs launch from the car's own pit box, not a shared point.
                Track.GetPitServicePose(Mathf.Clamp(gridIndex, 0, TrackRuntime.PitBoxCount - 1), out spawnPosition, out spawnRotation);
                spawnPosition += Vector3.up * 0.1f;
            }

            GameObject carObject = CreateOpenWheelCar(driverName, team.PrimaryUnityColor, team.SecondaryUnityColor);
            carObject.transform.SetParent(raceWorld.transform);
            carObject.transform.position = spawnPosition;
            carObject.transform.rotation = spawnRotation;
            if (!player)
            {
                CreateDriverLabel(carObject.transform, driver != null && !string.IsNullOrEmpty(driver.abbreviation) ? driver.abbreviation : driverName, team.SecondaryUnityColor);
            }

            VehicleController controller = carObject.AddComponent<VehicleController>();
            LapTracker lapTracker = carObject.AddComponent<LapTracker>();
            RaceParticipant participant = carObject.AddComponent<RaceParticipant>();
            participant.Initialize(driverId, driverName, teamId, teamShort, player, driver, team, car);
            participant.gridPosition = gridIndex + 1;
            participant.pitBoxIndex = Mathf.Clamp(gridIndex, 0, TrackRuntime.PitBoxCount - 1);
            participant.startReactionDelay = player ? 0f : ResolveAiStartReactionDelay(driver);
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
            if (Settings.Current.particlesEnabled)
            {
                VehicleEffects effects = carObject.AddComponent<VehicleEffects>();
                effects.Initialize(controller);
            }
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
                    Settings.Current.cameraShake ? Settings.Current.cameraShakeStrength * CameraShakeLevelMultiplier(Settings.Current.cameraShakeLevel) : 0f,
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

        // Reaction delay scales with difficulty's reactionTimeSeconds and the
        // driver's own awareness/consistency, instead of one flat random range for
        // every AI regardless of difficulty or driver skill. Lower skill/difficulty
        // launches later and less consistently; Expert-tier AI launches sharp.
        float ResolveAiStartReactionDelay(DriverData driver)
        {
            AiDifficultyProfile profile = GetAiDifficultyProfile();
            float skillBlend = driver == null ? 0.5f : Mathf.Clamp01((driver.awareness + driver.consistency) / 200f);
            float baseDelay = profile.reactionTimeSeconds * Mathf.Lerp(1.3f, 0.75f, skillBlend);
            float variance = Mathf.Lerp(0.28f, 0.05f, skillBlend);
            return Mathf.Max(0f, baseDelay + Random.Range(-variance, variance));
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
                GameLog.Info("[RoadPhysics] Spawn raycast hit road for " + driverName + " spawn=" + bestPoint);
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
            GameLog.Info("[RoadPhysics] Player spawn position=" + PlayerParticipant.transform.position +
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
            float heightOffset = participant.transform.position.y - progress.nearestPoint.y;
            bool stableOnRoad =
                Mathf.Abs(progress.lateralDistance) <= Track.roadHalfWidth &&
                heightOffset >= -0.35f &&
                heightOffset <= 2.25f;

            if (stableOnRoad && participant.fallRespawnCooldown <= 0f)
            {
                participant.hasLastSafePosition = true;
                participant.lastSafePosition = participant.transform.position;
                participant.lastSafeRotation = participant.transform.rotation;
            }

            // Two independent triggers: a hard, fast drop clearly past the track
            // surface, and a sustained mismatch for cars that settle on lower
            // ground beneath an elevated section without ever registering a single
            // instantaneous deep-fall frame. The second case was the actual "car
            // becomes impossible to drive" report - it never truly fell forever,
            // it just got physically stuck at the wrong height.
            bool hardFall = heightOffset < -3f;
            if (heightOffset < -1.5f && !hardFall)
            {
                participant.belowTrackTimer += Time.deltaTime;
            }
            else
            {
                participant.belowTrackTimer = 0f;
            }

            if (!hardFall && participant.belowTrackTimer <= 0.6f)
            {
                return;
            }

            participant.belowTrackTimer = 0f;
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
            GameLog.Warn("[RoadPhysics] Recovered " + participant.driverName +
                             " from an invalid below-track position (offset=" + heightOffset.ToString("0.0") +
                             "m). respawn=" + respawnPosition);
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

            // Front wing: cascaded elements at increasing attack angles so it reads
            // as an aero surface, not a stack of shelves.
            CreateChildCube(root.transform, "front wing base", new Vector3(0f, 0.15f, 2.55f), new Vector3(2.0f, 0.05f, 0.5f), Quaternion.Euler(-4f, 0f, 0f), secondaryMaterial);
            CreateChildCube(root.transform, "front wing mid flap", new Vector3(0f, 0.24f, 2.66f), new Vector3(1.9f, 0.04f, 0.3f), Quaternion.Euler(-11f, 0f, 0f), primaryMaterial);
            CreateChildCube(root.transform, "front wing upper flap", new Vector3(0f, 0.33f, 2.78f), new Vector3(1.72f, 0.035f, 0.22f), Quaternion.Euler(-18f, 0f, 0f), detailMaterial);
            CreateChildCube(root.transform, "left front endplate", new Vector3(-1.04f, 0.26f, 2.6f), new Vector3(0.05f, 0.36f, 0.56f), Quaternion.Euler(0f, -6f, 0f), secondaryMaterial);
            CreateChildCube(root.transform, "right front endplate", new Vector3(1.04f, 0.26f, 2.6f), new Vector3(0.05f, 0.36f, 0.56f), Quaternion.Euler(0f, 6f, 0f), secondaryMaterial);
            CreateChildCube(root.transform, "left endplate winglet", new Vector3(-1.02f, 0.42f, 2.62f), new Vector3(0.16f, 0.03f, 0.4f), Quaternion.Euler(0f, 0f, 14f), primaryMaterial);
            CreateChildCube(root.transform, "right endplate winglet", new Vector3(1.02f, 0.42f, 2.62f), new Vector3(0.16f, 0.03f, 0.4f), Quaternion.Euler(0f, 0f, -14f), primaryMaterial);

            // Rear wing with swan-neck pillar, angled flap, and beam wing.
            CreateChildCube(root.transform, "rear wing pillar", new Vector3(0f, 0.68f, -1.9f), new Vector3(0.07f, 0.42f, 0.1f), Quaternion.Euler(16f, 0f, 0f), detailMaterial);
            CreateChildCube(root.transform, "rear wing main plane", new Vector3(0f, 0.66f, -2.04f), new Vector3(1.72f, 0.07f, 0.42f), Quaternion.Euler(9f, 0f, 0f), secondaryMaterial);
            CreateChildCube(root.transform, "rear wing flap", new Vector3(0f, 0.85f, -2.16f), new Vector3(1.66f, 0.05f, 0.3f), Quaternion.Euler(24f, 0f, 0f), primaryMaterial);
            CreateChildCube(root.transform, "rear beam wing", new Vector3(0f, 0.42f, -2.02f), new Vector3(1.5f, 0.05f, 0.24f), Quaternion.Euler(14f, 0f, 0f), detailMaterial);
            CreateChildCube(root.transform, "left rear endplate", new Vector3(-0.9f, 0.72f, -2.06f), new Vector3(0.06f, 0.66f, 0.52f), Quaternion.Euler(0f, 0f, 4f), secondaryMaterial);
            CreateChildCube(root.transform, "right rear endplate", new Vector3(0.9f, 0.72f, -2.06f), new Vector3(0.06f, 0.66f, 0.52f), Quaternion.Euler(0f, 0f, -4f), secondaryMaterial);

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

            // Detail pass: mirrors, bargeboards, and livery accents that make each
            // team car read as designed rather than assembled from crates.
            CreateChildCube(root.transform, "left mirror", new Vector3(-0.5f, 0.72f, 0.72f), new Vector3(0.14f, 0.07f, 0.06f), secondaryMaterial);
            CreateChildCube(root.transform, "right mirror", new Vector3(0.5f, 0.72f, 0.72f), new Vector3(0.14f, 0.07f, 0.06f), secondaryMaterial);
            CreateChildCube(root.transform, "left bargeboard", new Vector3(-0.58f, 0.26f, 0.62f), new Vector3(0.035f, 0.24f, 0.5f), detailMaterial);
            CreateChildCube(root.transform, "right bargeboard", new Vector3(0.58f, 0.26f, 0.62f), new Vector3(0.035f, 0.24f, 0.5f), detailMaterial);
            CreateChildCube(root.transform, "engine cover stripe", new Vector3(0f, 0.86f, -0.66f), new Vector3(0.1f, 0.05f, 1.3f), secondaryMaterial);
            CreateChildCube(root.transform, "nose number panel", new Vector3(0f, 0.42f, 2.1f), new Vector3(0.24f, 0.03f, 0.3f), CreateMaterial(driverName + " number panel", Color.Lerp(Color.white, secondary, 0.15f), 0.1f, 0.7f));
            CreateChildCube(root.transform, "cockpit surround pad", new Vector3(0f, 0.72f, 0.34f), new Vector3(0.58f, 0.08f, 0.5f), inletMaterial);

            // Nose tip cone softens the front silhouette.
            CreateChildSphere(root.transform, "nose tip", new Vector3(0f, 0.28f, 2.62f), new Vector3(0.2f, 0.18f, 0.42f), primaryMaterial);

            // Rear rain light: glows under braking, blinks while harvesting.
            Material rainLightMaterial = CreateMaterial(driverName + " rain light", new Color(0.28f, 0.02f, 0.02f), 0.1f, 0.6f);
            CreateChildCube(root.transform, "rear rain light", new Vector3(0f, 0.42f, -2.12f), new Vector3(0.1f, 0.22f, 0.05f), rainLightMaterial);

            CreateSuspension(root.transform, floorMaterial, detailMaterial);
            Transform wheelFl = CreateWheel(root.transform, new Vector3(-1.06f, 0.24f, 1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);
            Transform wheelFr = CreateWheel(root.transform, new Vector3(1.06f, 0.24f, 1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);
            Transform wheelRl = CreateWheel(root.transform, new Vector3(-1.06f, 0.24f, -1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);
            Transform wheelRr = CreateWheel(root.transform, new Vector3(1.06f, 0.24f, -1.35f), tyreMaterial, rimMaterial, brakeDiscMaterial, caliperMaterial);

            VehicleVisuals visuals = root.AddComponent<VehicleVisuals>();
            visuals.Initialize(root.GetComponent<VehicleController>(), rainLightMaterial);
            visuals.SetWheels(wheelFl, wheelFr, wheelRl, wheelRr);
            visuals.SetBrakeGlowMaterial(brakeDiscMaterial);

            return root;
        }

        // A simple polished coupe silhouette - deliberately NOT an open-wheel F1
        // shape, so it reads as a distinct support vehicle rather than another
        // race car. Kinematic Rigidbody + a real collider (Part 1): the object is
        // solid, so a careless approach gets a genuine physical bump rather than
        // clipping through, while SafetyCarController drives it directly via
        // transform/rigidbody movement instead of engine/tyre physics - it never
        // races, it only needs to look right and block the road.
        // Generic, unbranded high-visibility "safety car" livery: bright
        // fluorescent body with black contrast panels and an amber light bar -
        // deliberately NOT any real series' colour scheme, just built to read
        // clearly and instantly as "official car, not a competitor" from far
        // down the straight, per the graphics brief (larger/readable
        // silhouette, smoother body, clear generic livery, no branding).
        GameObject CreateSafetyCarVisual(out Renderer beaconRenderer, out Renderer brakeLightRenderer)
        {
            GameObject root = new GameObject("Safety car");
            root.layer = 0;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(2f, 1.2f, 4.7f);
            collider.center = new Vector3(0f, 0.6f, 0f);
            collider.sharedMaterial = GetCarBodyPhysicsMaterial();

            // Fluorescent lime-yellow with black contrast roof/skirt - high
            // visibility, unmistakably not a competitor's livery, and easy to
            // read as an "official" car at a glance.
            Color bodyColor = new Color(0.62f, 0.95f, 0.06f);
            Color contrastColor = new Color(0.03f, 0.03f, 0.04f);
            Color accentColor = new Color(1f, 0.55f, 0.02f);
            Material bodyMaterial = CreateMaterial("Safety car body", bodyColor, 0.35f, 0.65f);
            Material contrastMaterial = CreateMaterial("Safety car contrast", contrastColor, 0.4f, 0.75f);
            Material accentMaterial = CreateMaterial("Safety car accent", accentColor, 0.1f, 0.7f);
            Material glassMaterial = CreateMaterial("Safety car glass", new Color(0.08f, 0.12f, 0.16f, 0.9f), 0.2f, 0.95f);
            Material wheelMaterial = CreateMaterial("Safety car wheel", new Color(0.02f, 0.02f, 0.02f), 0.05f, 0.3f);
            Material rimMaterial = CreateMaterial("Safety car rim", new Color(0.78f, 0.78f, 0.76f), 0.7f, 0.85f);
            Material headlightMaterial = CreateMaterial("Safety car headlight", new Color(1.3f, 1.3f, 1.1f), 0f, 0.9f, new Color(1f, 1f, 0.85f));
            Material beaconMaterial = CreateMaterial("Safety car beacon", accentColor, 0f, 0.9f, accentColor);
            Material blueMarkerMaterial = CreateMaterial("Safety car marker", new Color(0.1f, 0.35f, 1f), 0f, 0.8f, new Color(0.15f, 0.4f, 1.4f));
            Material brakeLightMaterial = CreateMaterial("Safety car brake light", new Color(0.12f, 0.01f, 0.01f), 0.1f, 0.6f, new Color(0.12f, 0.01f, 0.01f));
            Material markerPanelMaterial = CreateMaterial("Safety car marker panel", Color.white, 0.05f, 0.5f);

            // Smoother, less boxy shell: a tapered nose/tail instead of flat
            // cube fronts/rears, slightly larger than a standard car for a
            // bigger, more readable silhouette.
            CreateTaperedBox(root.transform, "SC body lower", new Vector3(0f, 0.42f, 0.2f), 1.7f, 1.9f, 0.62f, 4.0f, bodyMaterial);
            CreateTaperedBox(root.transform, "SC nose", new Vector3(0f, 0.34f, 2.55f), 1.2f, 1.9f, 0.46f, 0.9f, bodyMaterial);
            CreateChildCube(root.transform, "SC cabin", new Vector3(0f, 0.96f, -0.15f), new Vector3(1.55f, 0.5f, 2.3f), contrastMaterial);
            CreateChildCube(root.transform, "SC roof panel", new Vector3(0f, 1.22f, -0.15f), new Vector3(1.5f, 0.06f, 2.1f), contrastMaterial);
            CreateChildCube(root.transform, "SC windshield", new Vector3(0f, 1.0f, 1.0f), new Vector3(1.44f, 0.42f, 0.06f), Quaternion.Euler(-24f, 0f, 0f), glassMaterial);
            CreateChildCube(root.transform, "SC rear glass", new Vector3(0f, 1.0f, -1.24f), new Vector3(1.44f, 0.4f, 0.06f), Quaternion.Euler(20f, 0f, 0f), glassMaterial);
            CreateChildCube(root.transform, "SC side glass left", new Vector3(-0.78f, 1.02f, -0.15f), new Vector3(0.04f, 0.32f, 1.95f), glassMaterial);
            CreateChildCube(root.transform, "SC side glass right", new Vector3(0.78f, 1.02f, -0.15f), new Vector3(0.04f, 0.32f, 1.95f), glassMaterial);
            CreateChildCube(root.transform, "SC front bumper", new Vector3(0f, 0.28f, 2.3f), new Vector3(1.96f, 0.3f, 0.22f), contrastMaterial);
            CreateChildCube(root.transform, "SC rear bumper", new Vector3(0f, 0.28f, -2.28f), new Vector3(1.96f, 0.3f, 0.22f), contrastMaterial);
            CreateChildCube(root.transform, "SC skirt left", new Vector3(-0.96f, 0.24f, 0.1f), new Vector3(0.05f, 0.16f, 3.9f), contrastMaterial);
            CreateChildCube(root.transform, "SC skirt right", new Vector3(0.96f, 0.24f, 0.1f), new Vector3(0.05f, 0.16f, 3.9f), contrastMaterial);
            CreateChildCube(root.transform, "SC bonnet stripe", new Vector3(0f, 0.66f, 1.7f), new Vector3(0.5f, 0.03f, 1.6f), contrastMaterial);

            // Generic bold door marker panels - a clear "this is an official
            // car" identity read without any real branding/text/logos.
            CreateChildCube(root.transform, "SC door marker left", new Vector3(-0.99f, 0.62f, -0.2f), new Vector3(0.03f, 0.34f, 0.9f), markerPanelMaterial);
            CreateChildCube(root.transform, "SC door marker right", new Vector3(0.99f, 0.62f, -0.2f), new Vector3(0.03f, 0.34f, 0.9f), markerPanelMaterial);
            CreateChildCube(root.transform, "SC door marker accent left", new Vector3(-1.0f, 0.62f, -0.2f), new Vector3(0.01f, 0.34f, 0.9f), accentMaterial);
            CreateChildCube(root.transform, "SC door marker accent right", new Vector3(1.0f, 0.62f, -0.2f), new Vector3(0.01f, 0.34f, 0.9f), accentMaterial);

            // Wing mirrors - a small detail pass that reads well in chase cam.
            CreateChildCube(root.transform, "SC mirror left", new Vector3(-0.92f, 0.86f, 1.1f), new Vector3(0.14f, 0.1f, 0.22f), contrastMaterial);
            CreateChildCube(root.transform, "SC mirror right", new Vector3(0.92f, 0.86f, 1.1f), new Vector3(0.14f, 0.1f, 0.22f), contrastMaterial);

            GameObject headlightLeft = CreateChildCubeReturn(root.transform, "SC headlight left", new Vector3(-0.66f, 0.42f, 2.34f), new Vector3(0.28f, 0.15f, 0.08f), headlightMaterial);
            GameObject headlightRight = CreateChildCubeReturn(root.transform, "SC headlight right", new Vector3(0.66f, 0.42f, 2.34f), new Vector3(0.28f, 0.15f, 0.08f), headlightMaterial);
            MakeVisualOnlyIfPossible(headlightLeft);
            MakeVisualOnlyIfPossible(headlightRight);

            GameObject brakeLightLeft = CreateChildCubeReturn(root.transform, "SC brake light left", new Vector3(-0.64f, 0.46f, -2.34f), new Vector3(0.32f, 0.17f, 0.06f), brakeLightMaterial);
            CreateChildCubeReturn(root.transform, "SC brake light right", new Vector3(0.64f, 0.46f, -2.34f), new Vector3(0.32f, 0.17f, 0.06f), brakeLightMaterial);
            brakeLightRenderer = brakeLightLeft.GetComponent<Renderer>();

            // Roof light bar: the clearest "this is the safety car" identity read
            // from a distance - wider and taller than before for a bigger
            // silhouette, plus static blue corner markers flanking the pulsing
            // amber beacon SafetyCarController drives for extra contrast.
            CreateChildCube(root.transform, "SC roof bar mount", new Vector3(0f, 1.26f, -0.2f), new Vector3(1.1f, 0.06f, 0.4f), wheelMaterial);
            GameObject beacon = CreateChildCubeReturn(root.transform, "SC roof beacon", new Vector3(0f, 1.36f, -0.2f), new Vector3(1.0f, 0.16f, 0.34f), beaconMaterial);
            beaconRenderer = beacon.GetComponent<Renderer>();
            CreateChildCubeReturn(root.transform, "SC roof marker left", new Vector3(-0.58f, 1.34f, -0.2f), new Vector3(0.14f, 0.12f, 0.28f), blueMarkerMaterial);
            CreateChildCubeReturn(root.transform, "SC roof marker right", new Vector3(0.58f, 1.34f, -0.2f), new Vector3(0.14f, 0.12f, 0.28f), blueMarkerMaterial);

            CreateSafetyCarWheel(root.transform, new Vector3(-1.0f, 0.34f, 1.48f), wheelMaterial, rimMaterial);
            CreateSafetyCarWheel(root.transform, new Vector3(1.0f, 0.34f, 1.48f), wheelMaterial, rimMaterial);
            CreateSafetyCarWheel(root.transform, new Vector3(-1.0f, 0.34f, -1.48f), wheelMaterial, rimMaterial);
            CreateSafetyCarWheel(root.transform, new Vector3(1.0f, 0.34f, -1.48f), wheelMaterial, rimMaterial);

            return root;
        }

        void CreateSafetyCarWheel(Transform parent, Vector3 localPosition, Material tyreMaterial, Material rimMaterial)
        {
            GameObject tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tyre.name = "SC wheel";
            tyre.transform.SetParent(parent);
            tyre.transform.localPosition = localPosition;
            tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tyre.transform.localScale = new Vector3(0.34f, 0.34f, 0.34f);
            tyre.GetComponent<Renderer>().sharedMaterial = tyreMaterial;
            MakeVisualOnlyIfPossible(tyre);

            GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "SC wheel rim";
            rim.transform.SetParent(tyre.transform);
            rim.transform.localPosition = Vector3.zero;
            rim.transform.localScale = new Vector3(0.62f, 1.05f, 0.62f);
            rim.GetComponent<Renderer>().sharedMaterial = rimMaterial;
            MakeVisualOnlyIfPossible(rim);
        }

        GameObject CreateChildCubeReturn(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            MakeVisualOnlyIfPossible(cube);
            return cube;
        }

        void MakeVisualOnlyIfPossible(GameObject visualObject)
        {
            Collider objectCollider = visualObject.GetComponent<Collider>();
            if (objectCollider != null)
            {
                Destroy(objectCollider);
            }
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
            CreateChildCube(parent, objectName, localPosition, localScale, Quaternion.identity, material);
        }

        void CreateChildCube(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = localRotation;
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

        // Builds one wheel assembly under a spin pivot so VehicleVisuals can rotate
        // the whole wheel with road speed and steer the fronts. Returns the pivot.
        Transform CreateWheel(Transform parent, Vector3 localPosition, Material tyreMaterial, Material rimMaterial, Material brakeDiscMaterial, Material caliperMaterial)
        {
            GameObject pivot = new GameObject(localPosition.z > 0f ? "wheel pivot front" : "wheel pivot rear");
            pivot.transform.SetParent(parent);
            pivot.transform.localPosition = localPosition;
            pivot.transform.localRotation = Quaternion.identity;

            CreateWheelPart(pivot.transform, "open wheel", Vector3.zero, new Vector3(0.62f, 0.24f, 0.62f), tyreMaterial);
            CreateWheelPart(pivot.transform, "wheel rim", Vector3.zero, new Vector3(0.4f, 0.245f, 0.4f), rimMaterial);

            // Aero wheel cover on the outboard face.
            float outboard = localPosition.x < 0f ? -0.25f : 0.25f;
            CreateWheelPart(pivot.transform, "wheel cover", new Vector3(outboard, 0f, 0f), new Vector3(0.5f, 0.012f, 0.5f), rimMaterial);

            float inboard = localPosition.x < 0f ? 0.14f : -0.14f;
            CreateWheelPart(pivot.transform, "brake disc", new Vector3(inboard, 0f, 0f), new Vector3(0.3f, 0.035f, 0.3f), brakeDiscMaterial);

            // Caliper stays on the upright (parent), it must not spin with the wheel.
            GameObject caliper = GameObject.CreatePrimitive(PrimitiveType.Cube);
            caliper.name = "brake caliper";
            caliper.transform.SetParent(parent);
            caliper.transform.localPosition = localPosition + new Vector3(inboard * 1.08f, 0.12f, 0.07f);
            caliper.transform.localRotation = Quaternion.identity;
            caliper.transform.localScale = new Vector3(0.07f, 0.2f, 0.16f);
            caliper.GetComponent<Renderer>().sharedMaterial = caliperMaterial;
            Collider caliperCollider = caliper.GetComponent<Collider>();
            if (caliperCollider != null)
            {
                Destroy(caliperCollider);
            }

            return pivot.transform;
        }

        void CreateWheelPart(Transform pivot, string partName, Vector3 localOffset, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            part.name = partName;
            part.transform.SetParent(pivot);
            part.transform.localPosition = localOffset;
            part.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
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

        Material CreateMaterial(string materialName, Color color, float metallic, float smoothness, Color emission)
        {
            Material material = CreateMaterial(materialName, color, metallic, smoothness);
            if (emission.r > 0f || emission.g > 0f || emission.b > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
            }

            return material;
        }

        void CreateLighting()
        {
            string trackId = EventData == null || string.IsNullOrEmpty(EventData.trackId) ? "" : EventData.trackId;
            bool night = trackId.Contains("singapore") || trackId.Contains("las_vegas") || trackId.Contains("qatar");
            bool twilight = trackId.Contains("abu_dhabi");
            bool desert = trackId.Contains("bahrain") || trackId.Contains("abu_dhabi") || trackId.Contains("qatar");
            bool coastal = trackId.Contains("jeddah") || trackId.Contains("miami") || trackId.Contains("zandvoort") || trackId.Contains("monaco") || trackId.Contains("baku");
            bool mountain = trackId.Contains("austria") || trackId.Contains("spa") || trackId.Contains("austin") || trackId.Contains("mexico");
            bool park = trackId.Contains("silverstone") || trackId.Contains("melbourne") || trackId.Contains("monza") || trackId.Contains("interlagos") || trackId.Contains("suzuka") || trackId.Contains("zandvoort");
            string weatherProfile = EventData == null || string.IsNullOrEmpty(EventData.weatherProfile) ? "" : EventData.weatherProfile.ToLowerInvariant();
            bool rainThreat = weatherProfile.Contains("wet") || weatherProfile.Contains("mixed");

            int quality = Settings == null ? 2 : Mathf.Clamp(Settings.Current.graphicsQuality, 0, 3);
            QualitySettings.antiAliasing = quality == 0 ? 0 : (quality == 1 ? 2 : (quality == 2 ? 4 : 8));
            QualitySettings.shadows = quality == 0 ? ShadowQuality.HardOnly : ShadowQuality.All;
            QualitySettings.shadowDistance = 140f + quality * 120f;
            QualitySettings.shadowResolution = quality <= 1 ? ShadowResolution.Medium : (quality == 2 ? ShadowResolution.High : ShadowResolution.VeryHigh);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            if (twilight)
            {
                RenderSettings.ambientSkyColor = new Color(0.3f, 0.2f, 0.34f);
                RenderSettings.ambientEquatorColor = new Color(0.42f, 0.24f, 0.2f);
                RenderSettings.ambientGroundColor = new Color(0.1f, 0.07f, 0.09f);
            }
            else
            {
                RenderSettings.ambientSkyColor = night ? new Color(0.08f, 0.12f, 0.22f) : (rainThreat ? new Color(0.28f, 0.36f, 0.42f) : new Color(0.42f, 0.58f, 0.74f));
                RenderSettings.ambientEquatorColor = night ? new Color(0.05f, 0.08f, 0.14f) : (rainThreat ? new Color(0.28f, 0.32f, 0.34f) : new Color(0.45f, 0.42f, 0.38f));
                RenderSettings.ambientGroundColor = night ? new Color(0.01f, 0.01f, 0.02f) : (rainThreat ? new Color(0.08f, 0.09f, 0.1f) : (park ? new Color(0.12f, 0.18f, 0.12f) : new Color(0.18f, 0.16f, 0.14f)));
            }

            RenderSettings.reflectionIntensity = rainThreat ? 0.78f : 0.46f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = rainThreat ? 0.00024f : (mountain ? 0.00019f : 0.00015f);
            Color dryFog = desert ? new Color(0.65f, 0.55f, 0.42f)
                : (coastal ? new Color(0.5f, 0.62f, 0.68f)
                : (mountain ? new Color(0.4f, 0.5f, 0.46f)
                : new Color(0.44f, 0.54f, 0.52f)));
            if (twilight)
            {
                dryFog = new Color(0.48f, 0.3f, 0.3f);
            }

            RenderSettings.fogColor = night ? new Color(0.015f, 0.02f, 0.035f) : (rainThreat ? new Color(0.28f, 0.34f, 0.36f) : dryFog);
            RenderSettings.skybox = null;

            GameObject lightObject = new GameObject("Primary Sun");
            lightObject.transform.SetParent(raceWorld.transform);
            lightObject.transform.rotation = Quaternion.Euler(night ? -15f : (twilight ? 12f : (desert ? 32f : (mountain ? 38f : 48f))), desert ? -42f : (coastal ? -30f : -56f), 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = night ? 0.08f : (twilight ? 0.85f : (rainThreat ? 0.92f : (desert ? 1.55f : (coastal ? 1.4f : 1.25f))));
            light.color = night ? new Color(0.6f, 0.7f, 1f)
                : (twilight ? new Color(1f, 0.62f, 0.4f)
                : (rainThreat ? new Color(0.76f, 0.86f, 0.92f)
                : (desert ? new Color(1f, 0.85f, 0.65f)
                : (coastal ? new Color(1f, 0.94f, 0.85f)
                : new Color(0.98f, 0.96f, 0.94f)))));
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
            participant.vehicle.SetPitGuidance(true);
            if (participant.isPlayer)
            {
                SessionMessage = "Q" + qualifyingPhase + " complete: returning to pits";
                PostEngineerMessage("Good, bring it back to the pits. We will reset for the next segment.", true);
            }
        }

        void UpdateQualifyingPitReturn(RaceParticipant participant)
        {
            // Each car returns to its own garage box, never a shared stack point.
            Vector3 servicePosition;
            Quaternion serviceRotation;
            Track.GetPitServicePose(participant.pitBoxIndex, out servicePosition, out serviceRotation);
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitGuidance(true);
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
            participant.pitAwaitingRelease = false;
            participant.pitTimer = 0f;
            participant.pitServiceDuration = 0f;
            participant.nextPitCompound = participant.requestedPitCompoundSet ? participant.requestedPitCompound : NextPlannedPitCompoundFor(participant);
            participant.pitTyreSelectionActive = false;
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitGuidance(true);
            participant.vehicle.ClearPitRequest();
            if (participant.isPlayer)
            {
                SessionMessage = "Pit entry: limiter active";
                PostEngineerMessage("Pit entry. Hold steady, box " + (participant.pitBoxIndex + 1) + " is ready with " + participant.nextPitCompound + ".", true);
            }
        }

        void UpdatePitEntry(RaceParticipant participant)
        {
            if (!participant.pitEntryAligned)
            {
                // Two cars entering the pit lane in the same few-second window must
                // never both be guided onto, then snapped onto, the single shared
                // entry coordinate Track.GetPitEntryPose returns. Hold the trailing
                // car a short distance back along the approach until the leader has
                // cleared the entry point, mirroring how GetPitQueuePose already
                // holds cars back from a shared box target.
                RaceParticipant entryBlocker = FindPitEntryCarAhead(participant);
                if (entryBlocker != null)
                {
                    Vector3 holdPosition;
                    Quaternion holdRotation;
                    GetPitEntryHoldPose(participant, out holdPosition, out holdRotation);
                    participant.vehicle.SetPitLimiter(true);
                    participant.vehicle.SetPitServiceHold(true);
                    participant.vehicle.GuideToPitPose(holdPosition, holdRotation, 14f, 130f);
                    if (participant.isPlayer)
                    {
                        SessionMessage = "Pit entry: holding for the car ahead";
                    }

                    return;
                }

                Vector3 entryPosition;
                Quaternion entryRotation;
                Track.GetPitEntryPose(out entryPosition, out entryRotation);
                participant.vehicle.SetPitLimiter(true);
                participant.vehicle.SetPitServiceHold(true);
                float entryDistance = participant.vehicle.GuideToPitPose(entryPosition, entryRotation, 14f, 130f);
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

            // Queue behind any car still rolling to a deeper box: hold a lane gap
            // so cars process through the pit lane like beads on a string.
            RaceParticipant blocking = FindPitLaneCarAhead(participant);
            Vector3 servicePosition;
            Quaternion serviceRotation;
            if (blocking != null)
            {
                Track.GetPitQueuePose(participant.pitBoxIndex, PitQueueHoldback(participant, blocking), out servicePosition, out serviceRotation);
            }
            else
            {
                Track.GetPitServicePose(participant.pitBoxIndex, out servicePosition, out serviceRotation);
            }

            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            float distance = participant.vehicle.GuideToPitPose(servicePosition, serviceRotation, 15f, 150f);
            if (participant.isPlayer)
            {
                SessionMessage = blocking != null ? "Pit lane: queueing for box " + (participant.pitBoxIndex + 1) : "Pit lane: rolling to box " + (participant.pitBoxIndex + 1);
            }

            if (blocking == null && distance <= 0.45f)
            {
                participant.vehicle.SnapToPitPose(servicePosition, serviceRotation);
                BeginPitStop(participant);
            }
        }

        // Nearest other car occupying the pit lane directly ahead of this one
        // (between this car and its box). Used for simple queue spacing.
        RaceParticipant FindPitLaneCarAhead(RaceParticipant participant)
        {
            float ownTarget = Track.PitBoxDistance(participant.pitBoxIndex);
            TrackProgress own = Track.GetProgress(participant.transform.position);
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant || other.vehicle == null)
                {
                    continue;
                }

                bool inLane = other.pitPhase == PitPhase.Entry || other.pitPhase == PitPhase.Service ||
                              (other.pitPhase == PitPhase.Release && other.pitAwaitingRelease);
                if (!inLane)
                {
                    continue;
                }

                float gap = Vector3.Distance(other.transform.position, participant.transform.position);
                if (gap > 14f)
                {
                    continue;
                }

                TrackProgress otherProgress = Track.GetProgress(other.transform.position);
                float aheadBy = Track.WrapDistance(otherProgress.distance - own.distance);
                if (aheadBy > 0.5f && aheadBy < 13f && otherProgress.distance <= ownTarget + 1f)
                {
                    return other;
                }
            }

            return null;
        }

        float PitQueueHoldback(RaceParticipant participant, RaceParticipant blocking)
        {
            float ownDistance = Track.GetProgress(participant.transform.position).distance;
            float target = Track.PitBoxDistance(participant.pitBoxIndex);
            return Mathf.Clamp(target - ownDistance + 8f, 8f, 60f);
        }

        // Any other car still occupying the shared pit-entry coordinate: either
        // approaching it (Entry, not yet aligned) or having only just aligned onto it
        // and not yet moved on toward its own box. Gated on real proximity to the
        // entry point itself, not just phase, so the hold clears the moment the
        // leader has actually moved away from that specific spot.
        RaceParticipant FindPitEntryCarAhead(RaceParticipant participant)
        {
            float entryTarget = Track.length * 0.885f;
            TrackProgress own = Track.GetProgress(participant.transform.position);
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant || other.vehicle == null || other.pitPhase != PitPhase.Entry)
                {
                    continue;
                }

                TrackProgress otherProgress = Track.GetProgress(other.transform.position);
                float gap = Vector3.Distance(other.transform.position, participant.transform.position);
                float aheadBy = Track.WrapDistance(otherProgress.distance - own.distance);
                float otherDistanceFromEntry = Track.WrapDistance(entryTarget - otherProgress.distance);
                if (gap < 16f && aheadBy > 0.3f && aheadBy < 16f && otherDistanceFromEntry < 16f)
                {
                    return other;
                }
            }

            return null;
        }

        // A point a few car lengths before the shared entry coordinate, along the
        // same approach direction - mirrors GetPitQueuePose's "distance minus
        // holdback" pattern but anchored to the entry point rather than a pit box.
        void GetPitEntryHoldPose(RaceParticipant participant, out Vector3 position, out Quaternion rotation)
        {
            float entryTarget = Track.length * 0.885f;
            float ownDistance = Track.GetProgress(participant.transform.position).distance;
            float holdback = Mathf.Clamp(entryTarget - ownDistance + 8f, 8f, 45f);
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Track.SampleAtDistance(entryTarget - holdback, out point, out forward, out right);
            position = point + right * (Track.roadHalfWidth + 5.6f) + Vector3.up * 0.58f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        void BeginPitStop(RaceParticipant participant)
        {
            participant.pitPhase = PitPhase.Service;
            participant.isPitting = true;
            participant.pitAwaitingRelease = false;
            participant.pitServiceDuration = participant.isPlayer ? Random.Range(2.7f, 4.3f) : Random.Range(2.8f, 4.4f);
            participant.pitTimer = participant.pitServiceDuration;
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.ClearPitRequest();
            if (participant.isPlayer)
            {
                SessionMessage = "Pit box " + (participant.pitBoxIndex + 1) + ": changing to " + participant.nextPitCompound;
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

            // Hold the car in its box until the lane grants a safe release gap, so
            // back-to-back stops never dump two cars onto the same release point.
            if (Time.time < nextPitReleaseAllowedTime)
            {
                participant.pitTimer = 0f;
                participant.pitAwaitingRelease = true;
                if (participant.isPlayer)
                {
                    SessionMessage = "Held in box: waiting for release gap";
                }

                return;
            }

            nextPitReleaseAllowedTime = Time.time + PitReleaseGapSeconds;
            participant.pitAwaitingRelease = false;
            participant.vehicle.CompletePitStop(participant.nextPitCompound);
            participant.pitStops++;
            participant.compoundStints.Add(participant.nextPitCompound.ToString());
            participant.requestedPitCompoundSet = false;
            participant.pitTyreSelectionActive = false;
            participant.pitTimer = 0f;
            // The release gap only throttles how often a new car is admitted to
            // Release - a car already guided toward the (single) release point can
            // still be mid-transit when the next one is let go. Stagger each car's
            // actual release target by how many others are already in Release so two
            // of them never both guide onto, then snap onto, the identical point.
            participant.pitReleaseStagger = CountParticipantsInPitPhase(PitPhase.Release);
            participant.pitPhase = PitPhase.Release;
            participant.pitServiceDuration = 0f;
            if (participant.isPlayer)
            {
                SessionMessage = "Pit release: limiter active";
                PostEngineerMessage("Stop complete. Release, limiter remains active until pit exit.", true);
            }
        }

        // Number of other participants currently sitting in the given pit phase;
        // used to stagger cars that would otherwise share one fixed pose.
        int CountParticipantsInPitPhase(PitPhase phase)
        {
            int count = 0;
            for (int i = 0; i < Participants.Count; i++)
            {
                if (Participants[i] != null && Participants[i].pitPhase == phase)
                {
                    count++;
                }
            }

            return count;
        }

        void UpdatePitRelease(RaceParticipant participant)
        {
            Vector3 releasePosition;
            Quaternion releaseRotation;
            Track.GetPitReleasePose(participant.pitReleaseStagger, out releasePosition, out releaseRotation);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitLimiter(true);
            float distance = participant.vehicle.GuideToPitPose(releasePosition, releaseRotation, 21f, 210f);
            if (distance > 0.55f)
            {
                return;
            }

            participant.vehicle.SnapToPitPose(releasePosition, releaseRotation);
            participant.vehicle.SetPitGuidance(false);
            participant.vehicle.SetPitServiceHold(false);
            participant.vehicle.SetPitLimiter(true);
            participant.pitPhase = PitPhase.None;
            participant.isPitting = false;
            participant.pitAwaitingRelease = false;
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

                if (participant.isPlayer && !engineerPodiumMessageSent)
                {
                    engineerPodiumMessageSent = true;
                    PostEngineerMessage(FinishEngineerMessage(participant.finishingPosition), true);

                    // Part 15/19: Cinematic race presentation gets a small finish-line
                    // camera flourish (a FOV punch-in) instead of nothing happening
                    // when the chequered flag falls.
                    if (Settings != null && Settings.Current.racePresentation >= 2 && participant.vehicle != null)
                    {
                        PlayerVehicleInput playerInput = participant.vehicle.GetComponent<PlayerVehicleInput>();
                        if (playerInput != null && playerInput.cameraRig != null)
                        {
                            playerInput.cameraRig.AddImpulseShake(0.14f);
                        }
                    }
                }
            }
        }

        // Part 1: a finish-position-appropriate radio line instead of nothing at
        // all once the chequered flag falls.
        string FinishEngineerMessage(int position)
        {
            if (position == 1)
            {
                return "That's the win! Fantastic drive, take the flag.";
            }

            if (position <= 3)
            {
                return "P" + position + " and on the podium! Great result, well driven.";
            }

            if (position <= 10)
            {
                return "P" + position + ", points on the board. Solid job out there.";
            }

            return "P" + position + " at the flag. We'll take the data and come back stronger.";
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

        // Gentle anti-pile pass: when two active cars end up nearly stationary and
        // overlapping (turn-one scrums, restart concertinas), ease them apart along
        // track-right instead of letting physics grind them together. The nudge is
        // damage-free and far too small to launch a car.
        void ResolveLowSpeedStacks()
        {
            stackResolveTimer -= Time.deltaTime;
            if (stackResolveTimer > 0f)
            {
                return;
            }

            stackResolveTimer = 0.12f;
            const float overlapDistance = 3.4f;
            const float maxSpeedKph = 34f;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant a = Participants[i];
                if (!IsStackResolveCandidate(a))
                {
                    continue;
                }

                for (int j = i + 1; j < Participants.Count; j++)
                {
                    RaceParticipant b = Participants[j];
                    if (!IsStackResolveCandidate(b))
                    {
                        continue;
                    }

                    Vector3 delta = b.transform.position - a.transform.position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > overlapDistance * overlapDistance)
                    {
                        continue;
                    }

                    if (Mathf.Abs(a.vehicle.CurrentSpeedKph) > maxSpeedKph || Mathf.Abs(b.vehicle.CurrentSpeedKph) > maxSpeedKph)
                    {
                        continue;
                    }

                    TrackProgress progress = Track.GetProgress(a.transform.position);
                    Vector3 trackRight = Vector3.Cross(Vector3.up, progress.forward).normalized;
                    float side = Vector3.Dot(delta, trackRight);
                    if (Mathf.Abs(side) < 0.05f)
                    {
                        side = (i + j) % 2 == 0 ? 1f : -1f;
                    }

                    Vector3 separation = trackRight * Mathf.Sign(side) * 0.55f;
                    NudgeStackedCar(a, -separation, progress);
                    NudgeStackedCar(b, separation, progress);
                }
            }
        }

        bool IsStackResolveCandidate(RaceParticipant participant)
        {
            return participant != null &&
                   participant.vehicle != null &&
                   !participant.retired &&
                   !participant.finished &&
                   !participant.isPitting &&
                   participant.pitPhase == PitPhase.None &&
                   !participant.vehicle.IsHeldOnGrid &&
                   !participant.vehicle.IsPitGuided &&
                   participant.gameObject.activeSelf;
        }

        void NudgeStackedCar(RaceParticipant participant, Vector3 separation, TrackProgress reference)
        {
            // Never push a car off the road; clamp the nudge inside the surface.
            Vector3 target = participant.transform.position + separation;
            TrackProgress targetProgress = Track.GetProgress(target);
            if (Mathf.Abs(targetProgress.lateralDistance) > Track.roadHalfWidth - 1.2f)
            {
                return;
            }

            Rigidbody body = participant.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic)
            {
                return;
            }

            body.position = target;
            Vector3 velocity = body.velocity;
            velocity.x *= 0.9f;
            velocity.z *= 0.9f;
            body.velocity = velocity;
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
                    entry.totalTime = RaceElapsed + lapsRemaining * Mathf.Max(72f, Track.length / 56f);
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

            RecordPlayerRaceStats(results);
            LogAiDiagnostics(results);
            ui.ShowResults(this, results, IsCareerRace);
        }

        // One-shot post-race summary so an Expert AI balance pass can be checked
        // from the log instead of only from playtesting feel.
        void LogAiDiagnostics(List<RaceResultEntry> results)
        {
            if (PlayerParticipant == null || results == null || results.Count == 0)
            {
                return;
            }

            float playerBest = PlayerParticipant.lapTracker == null ? 0f : PlayerParticipant.lapTracker.BestLapTime;
            List<float> aiBests = new List<float>();
            int ersFrameTotal = 0;
            int drsFrameTotal = 0;
            int aiCount = 0;
            int aiOvertakesTotal = 0;
            int aiLockupsTotal = 0;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant p = Participants[i];
                if (p == null || p.isPlayer)
                {
                    continue;
                }

                if (p.lapTracker != null && p.lapTracker.BestLapTime > 0f)
                {
                    aiBests.Add(p.lapTracker.BestLapTime);
                }

                ersFrameTotal += p.ersDeployFrameCount;
                drsFrameTotal += p.drsActiveFrameCount;
                aiOvertakesTotal += p.overtakesCompleted;
                aiLockupsTotal += p.vehicle == null || p.vehicle.Tyres == null ? 0 : p.vehicle.Tyres.TotalLockups;
                aiCount++;
            }

            aiBests.Sort();
            float fastestAi = aiBests.Count > 0 ? aiBests[0] : 0f;
            float medianAi = aiBests.Count > 0 ? aiBests[aiBests.Count / 2] : 0f;
            float slowestAi = aiBests.Count > 0 ? aiBests[aiBests.Count - 1] : 0f;

            RaceResultEntry playerResult = results.Find(entry => entry.isPlayer);
            RaceResultEntry winner = results[0];
            float playerGapToWinner = playerResult != null
                ? (playerResult.totalTime + playerResult.penaltiesSeconds) - (winner.totalTime + winner.penaltiesSeconds)
                : 0f;

            GameLog.Info("[AIDiagnostics] difficulty=" + Settings.Difficulty +
                         " playerBestLap=" + (playerBest > 0f ? UiFactory.FormatTime(playerBest) : "--") +
                         " aiFastestLap=" + (fastestAi > 0f ? UiFactory.FormatTime(fastestAi) : "--") +
                         " aiMedianLap=" + (medianAi > 0f ? UiFactory.FormatTime(medianAi) : "--") +
                         " aiSlowestLap=" + (slowestAi > 0f ? UiFactory.FormatTime(slowestAi) : "--") +
                         " playerFinish=P" + (playerResult != null ? playerResult.finishingPosition.ToString() : "--") +
                         " winner=" + winner.driverName +
                         " playerGapToWinner=" + playerGapToWinner.ToString("0.0") + "s" +
                         " aiAvgErsDeployFrames=" + (aiCount > 0 ? (ersFrameTotal / (float)aiCount).ToString("0") : "0") +
                         " aiAvgDrsActiveFrames=" + (aiCount > 0 ? (drsFrameTotal / (float)aiCount).ToString("0") : "0") +
                         " aiTotalDrsActiveFrames=" + drsFrameTotal +
                         " aiTotalErsDeployFrames=" + ersFrameTotal +
                         " aiTotalOvertakesCompleted=" + aiOvertakesTotal +
                         " aiTotalLockups=" + aiLockupsTotal +
                         " incidentCount=" + IncidentCount +
                         " safetyCarDeployments=" + SafetyCarDeploymentCount);

            if (Settings.Difficulty == RaceDifficulty.Expert && playerBest > 0f && fastestAi > 0f && fastestAi - playerBest > 10f)
            {
                GameLog.Info("[AIDiagnostics] Expert AI too slow: investigate corner speed/braking/traffic.");
            }
        }

        void RecordPlayerRaceStats(List<RaceResultEntry> results)
        {
            RaceResultEntry playerResult = results == null ? null : results.Find(entry => entry.isPlayer);
            if (playerResult == null)
            {
                return;
            }

            RaceResultEntry fastest = null;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].bestLapTime > 0f && (fastest == null || results[i].bestLapTime < fastest.bestLapTime))
                {
                    fastest = results[i];
                }
            }

            bool fastestLap = fastest != null && fastest.isPlayer;
            int trackLimitWarnings = PlayerParticipant != null ? PlayerParticipant.trackLimitWarnings : 0;
            bool cleanRace = playerResult.penaltiesSeconds <= 0.01f &&
                             trackLimitWarnings == 0 &&
                             PlayerParticipant != null &&
                             PlayerParticipant.vehicle != null &&
                             PlayerParticipant.vehicle.Damage.OverallPercent < 20f;
            PlayerRecordsStore.RecordRaceFinish(playerResult.finishingPosition, playerResult.points, fastestLap, cleanRace, trackLimitWarnings);
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

            QualifyingResultEntry playerQualifying = results.Find(entry => entry.isPlayer);
            if (playerQualifying != null)
            {
                PlayerRecordsStore.RecordQualifyingResult(playerQualifying.position);
            }

            LogAiQualifyingDiagnostics(results, playerQualifying);
            ui.ShowQualifyingResults(this, results, IsCareerRace);
        }

        // Qualifying-side counterpart to LogAiDiagnostics: a one-shot internal log
        // comparing the player's actual/simulated time against the fastest AI and the
        // field median, so a balance pass can be checked from the log alone. Plain
        // GameLog only, never player-visible.
        void LogAiQualifyingDiagnostics(List<QualifyingResultEntry> results, QualifyingResultEntry playerQualifying)
        {
            if (results == null || results.Count == 0)
            {
                return;
            }

            List<float> aiTimes = new List<float>();
            for (int i = 0; i < results.Count; i++)
            {
                if (!results[i].isPlayer && results[i].bestLapTime > 0f)
                {
                    aiTimes.Add(results[i].bestLapTime);
                }
            }

            aiTimes.Sort();
            float fastestAi = aiTimes.Count > 0 ? aiTimes[0] : 0f;
            float medianAi = aiTimes.Count > 0 ? aiTimes[aiTimes.Count / 2] : 0f;

            GameLog.Info("[AIQualifyingDiagnostics] difficulty=" + Settings.Difficulty +
                         " playerTime=" + (playerQualifying != null && playerQualifying.bestLapTime > 0f ? UiFactory.FormatTime(playerQualifying.bestLapTime) : "--") +
                         " playerPosition=" + (playerQualifying != null ? "P" + playerQualifying.position : "--") +
                         " aiFastest=" + (fastestAi > 0f ? UiFactory.FormatTime(fastestAi) : "--") +
                         " aiMedian=" + (medianAi > 0f ? UiFactory.FormatTime(medianAi) : "--") +
                         " fieldSize=" + results.Count);
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

        // One fully-itemized simulated lap so the result screen can show the player
        // exactly where their time came from. Same model the AI runs through, plus
        // the player's actual tyre choice.
        class QualifyingLapBreakdown
        {
            public int phase;
            public float baseLap;
            public float carEffect;
            public float driverEffect;
            public float difficultyEffect;
            public float phaseEffect;
            public float tyrePrep;
            public float weatherPenalty;
            public float mistakePenalty;
            public float variance;
            public float tyreChoicePenalty;
            public float finalTime;
        }

        readonly QualifyingLapBreakdown[] playerSimBreakdowns = new QualifyingLapBreakdown[3];

        float SimulatePlayerQualifyingTime(QualifyingSimEntry entry, int phase)
        {
            QualifyingLapBreakdown best = null;
            for (int run = 0; run < 2; run++)
            {
                QualifyingLapBreakdown attempt = SimulateQualifyingRunDetailed(entry, phase, run == 1);
                if (run == 1)
                {
                    // Second run improvement scales with craft, mirroring the AI model.
                    float qualifying = entry.driverData == null ? 78f : entry.driverData.qualifying;
                    float consistency = entry.driverData == null ? 78f : entry.driverData.consistency;
                    float secondRunGain = Mathf.Lerp(0.03f, 0.34f, Mathf.Clamp01((qualifying + consistency) / 200f));
                    float gain = Random.Range(0f, secondRunGain);
                    attempt.variance -= gain;
                    attempt.finalTime -= gain;
                }

                attempt.tyreChoicePenalty = PlayerQualifyingTyreWeatherPenalty(Settings == null ? TyreCompound.Medium : Settings.SelectedTyreCompound);
                attempt.finalTime += attempt.tyreChoicePenalty;
                if (best == null || attempt.finalTime < best.finalTime)
                {
                    best = attempt;
                }
            }

            best.finalTime = Mathf.Max(20f, best.finalTime);
            if (phase >= 1 && phase <= 3)
            {
                playerSimBreakdowns[phase - 1] = best;
            }

            return best.finalTime;
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
            return SimulateQualifyingRunDetailed(entry, phase, secondRun).finalTime;
        }

        QualifyingLapBreakdown SimulateQualifyingRunDetailed(QualifyingSimEntry entry, int phase, bool secondRun)
        {
            QualifyingLapBreakdown breakdown = new QualifyingLapBreakdown { phase = phase };
            DriverData driver = entry.driverData;
            CarPerformanceData car = entry.carData;
            breakdown.baseLap = EstimateReferenceLapTime(car, Track);
            float consistency = driver == null ? 80f : driver.consistency;
            float qualifying = driver == null ? 82f : driver.qualifying;
            float pace = driver == null ? 82f : driver.pace;
            float confidence = driver == null ? 80f : driver.experience;
            float tyreManagement = driver == null ? 80f : driver.tyreManagement;
            float carRating = car == null ? 84f : car.cornering * 0.34f + car.enginePower * 0.24f + car.aeroEfficiency * 0.24f + car.braking * 0.18f;
            // Coefficients widened ~20% over the original so skill/car gaps stay meaningful
            // now that baseLap is a realistic (much larger) reference time.
            breakdown.driverEffect = (qualifying - 88f) * -0.058f + (pace - 88f) * -0.019f + (confidence - 80f) * -0.006f;
            breakdown.carEffect = (carRating - 86f) * -0.062f;
            // Percentage of baseLap rather than a flat constant, so difficulty stays
            // meaningful regardless of track length: Easy is clearly the slowest,
            // Expert clearly the fastest/most aggressive, Medium close to neutral.
            float difficultyPercent = Settings.Difficulty == RaceDifficulty.Easy ? 0.035f : Settings.Difficulty == RaceDifficulty.Medium ? 0.005f : Settings.Difficulty == RaceDifficulty.Hard ? -0.030f : -0.060f;
            breakdown.difficultyEffect = breakdown.baseLap * difficultyPercent;
            breakdown.phaseEffect = phase == 1 ? 0.08f : (phase == 2 ? -0.18f : -0.36f);
            breakdown.tyrePrep = Mathf.Lerp(0.14f, 0.0f, tyreManagement / 100f) + Random.Range(0f, 0.04f);
            breakdown.weatherPenalty = WeatherQualifyingPenalty(driver);
            breakdown.mistakePenalty = QualifyingMistakePenalty(driver, phase);
            float variance = Mathf.Lerp(0.24f, 0.035f, consistency / 100f);
            breakdown.variance = Random.Range(-variance, variance) + (secondRun ? Random.Range(-0.08f, 0.05f) : 0f);
            breakdown.finalTime = breakdown.baseLap + breakdown.driverEffect + breakdown.carEffect +
                                  breakdown.difficultyEffect + breakdown.phaseEffect + breakdown.tyrePrep +
                                  breakdown.weatherPenalty + breakdown.mistakePenalty + breakdown.variance;
            return breakdown;
        }

        // Reference lap time derived from what the car's actual top speed rating can
        // achieve on this track's layout, instead of a flat length/time divisor.
        // Used for both AI and player qualifying simulation so the pace floor is
        // consistently calibrated for everyone.
        float EstimateReferenceLapTime(CarPerformanceData car, TrackRuntime track)
        {
            float carTopSpeedKph = car == null || car.topSpeed <= 0 ? 337f : car.topSpeed;
            float styleFactor = TrackAverageSpeedFactor(track);
            float referenceSpeedMps = (carTopSpeedKph / 3.6f) * styleFactor;
            float trackLength = track == null ? 4650f : track.length;
            return Mathf.Max(45f, trackLength / referenceSpeedMps);
        }

        // Fraction of top speed a well-driven qualifying lap averages, by track
        // character. Tight/low-speed circuits (Monaco, street layouts) average much
        // lower than top speed; flowing high-speed circuits average much closer to
        // it. Named-circuit checks run before the generic street check since several
        // real high-speed circuits (Jeddah, Baku, Las Vegas) are technically street
        // layouts but should not be bucketed with tight street pace.
        float TrackAverageSpeedFactor(TrackRuntime track)
        {
            if (track == null)
            {
                return 0.60f;
            }

            string id = track.trackId ?? "";
            string style = (track.styleName ?? "").ToLowerInvariant();
            if (id.Contains("monaco"))
            {
                return 0.44f;
            }

            if (id.Contains("spa") || id.Contains("monza") || id.Contains("silverstone") ||
                id.Contains("baku") || id.Contains("jeddah") || id.Contains("las_vegas") ||
                id.Contains("suzuka") || id.Contains("qatar"))
            {
                return 0.76f;
            }

            if (id.Contains("hungary"))
            {
                return 0.50f;
            }

            if (style.Contains("street") || track.roadHalfWidth < 12f)
            {
                return 0.53f;
            }

            return 0.66f;
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
                // Tail trimmed from the old 2.0-4.5s range so a single unlucky AI lap
                // doesn't produce a comically slow outlier.
                penalty += Random.Range(1.2f, 3.0f);
            }

            return penalty;
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
