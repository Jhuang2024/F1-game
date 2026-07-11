using System.Collections;
using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    public partial class RaceManager : MonoBehaviour
    {
        public GameDataRepository Data { get; private set; }
        public CareerManager Career { get; private set; }
        public GameSettingsStore Settings { get; private set; }
        public TrackRuntime Track { get; private set; }
        // Cached once per Build() (see SpawnRaceGrid/session setup) for
        // SampleCorneringTelemetry - TrackRuntime.ClassifyCorners does a real
        // curvature scan and must never be called per participant per frame.
        List<TrackRuntime.CornerRiskInfo> telemetryCorners;
        public CalendarEventData EventData { get; private set; }
        public RaceParticipant PlayerParticipant { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsRaceFinished { get; private set; }
        public bool IsCareerRace { get; private set; }
        public bool IsTimeTrial { get; private set; }
        // Which practice program (see RuntimeUi.ShowPracticePrograms) this
        // Practice session is being driven for, set by GameBootstrap.StartCareerPractice
        // right after StartSession returns and read by EvaluatePracticeSession
        // when the player ends the session from the pause menu.
        public string ActivePracticeProgramId;
        public RaceWeekendSession CurrentSession { get; private set; }
        public float StartCountdown { get; private set; }
        public bool CanDrive { get { return StartCountdown <= 0f && !IsPaused && !IsRaceFinished && !qualifyingTransitionPending; } }
        public string SessionMessage { get; private set; }
        public string QualifyingFeedbackText { get; private set; }
        // Radio message stacking fix: the single engineerMessageText/Timer pair
        // this used to read from is gone - see the activeEngineerMessages list
        // and the ActiveEngineerMessageCount/GetActiveEngineerMessage* accessors
        // further down for the multi-message replacement RaceHud's radio stack
        // now reads from.
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

                // Light timing is owned by the extracted rulebook so the HUD,
                // audio and any future broadcast layer all read one sequence.
                float elapsed = Mathf.Max(0f, raceStartSequenceDuration - StartCountdown);
                return StartProcedureRules.LitLightCount(elapsed);
            }
        }

        public bool RaceStartLightsVisible
        {
            get { return CurrentSession != RaceWeekendSession.Qualifying && !IsTimeTrial && StartCountdown > 0f; }
        }

        public RaceStateManager State { get; private set; }
        public List<RaceParticipant> Participants { get { return State != null ? State.Participants : emptyParticipants; } }

        // ---------- Race control / safety car ----------
        // RedFlagged: the one new state. Deliberately does NOT get its own
        // restart-formation/ramp machinery - once the clearance period ends it
        // hands straight into the existing Restart state (see
        // DriveRaceControlStateMachine), reusing the exact same queue-spacing/
        // ramp-to-green logic a safety-car restart already uses instead of
        // duplicating it.
        public enum RaceControlState { Green, YellowSector, VirtualSafetyCar, SafetyCarDeploying, SafetyCarActive, SafetyCarInThisLap, Restart, RedFlagged }
        enum IncidentSeverity { Minor, Medium, Major }

        // VSC/SC interactive pit-window offer: which race-control period the
        // currently active offer (if any) was raised under, so the accept
        // message/report can name it correctly instead of a generic "pitted".
        public enum RaceControlPitOfferType { None, Vsc, SafetyCar }

        public RaceControlState CurrentRaceControlState { get; private set; } = RaceControlState.Green;
        // Absolute target speed for a full safety car period; only meaningful while
        // CurrentRaceControlState == SafetyCarActive.
        public float SafetyCarTargetSpeedKph { get; private set; } = 150f;
        // VSC pace-cap fix: this used to be a flat percentage pace CUT
        // (VirtualSafetyCarPaceMultiplier, 0.62) applied multiplicatively to
        // the AI's already-scaled normal-racing target speeds
        // (AiVehicleController's straightTargetSpeed/cruiseTargetSpeed/
        // brakingApexSpeed) - a proportional reduction with no relationship at
        // all to the actual legal VSC limit below. On a fast straight (normal
        // pace 300+ kph) that put AI cars at ~185 kph or less even where the
        // real limit allows right up to 190, and on a slow corner it barely
        // reduced anything, since 62% of an already-slow apex speed is still
        // close to that apex speed - the AI's pace read as "far too slow on
        // straights, barely slowed at all in corners", not a consistent cap.
        // AI now clamps directly to this ONE absolute value instead - the
        // exact same constant RaceControlSpeedCapKphFor already uses for the
        // player's (and every AI car's) shared physical hard limiter in
        // VehicleController.ApplyForces, so both the AI's own throttle/brake
        // targeting and the physical enforcement backstop agree on the same
        // number, the same way the full Safety Car branch already clamps to
        // SafetyCarTargetSpeedKph rather than scaling it.
        public const float VirtualSafetyCarSpeedCapKph = FlagRules.VirtualSafetyCarSpeedCapKph;
        public bool IsPitLaneOpen { get; private set; } = true;

        // Single mapping from the live race-control state machine to the
        // extracted flag rulebook (FlagRules). Every policy consumer -
        // overtaking, DRS, pace caps - derives from this, so the consequence
        // of each flag is stated exactly once, in FlagRules.
        public RaceFlag GlobalRaceFlag
        {
            get
            {
                switch (CurrentRaceControlState)
                {
                    case RaceControlState.RedFlagged:
                        return RaceFlag.Red;
                    case RaceControlState.VirtualSafetyCar:
                        return RaceFlag.VirtualSafetyCar;
                    case RaceControlState.SafetyCarDeploying:
                    case RaceControlState.SafetyCarActive:
                    case RaceControlState.SafetyCarInThisLap:
                    // The field is still under race control between the safety
                    // car peeling in and the actual green flag.
                    case RaceControlState.Restart:
                        return RaceFlag.SafetyCar;
                    default:
                        // A sector-local yellow is scoped per participant (see
                        // FlagForParticipant); the global flag stays green.
                        return RaceFlag.Green;
                }
            }
        }

        // The flag THIS car is currently shown: the global flag, plus the
        // sector-scoped local yellow with its near-incident fallback window.
        public RaceFlag FlagForParticipant(RaceParticipant participant)
        {
            RaceFlag flag = GlobalRaceFlag;
            if (flag == RaceFlag.Green && IsNearLocalYellowIncident(participant))
            {
                return RaceFlag.LocalYellow;
            }

            return flag;
        }

        // Derived from the state machine through FlagRules rather than kept as
        // a separately-written bool, so it can never fall out of step with the
        // race-control state it reports on.
        public bool IsOvertakingAllowed { get { return FlagRules.OvertakingAllowed(GlobalRaceFlag); } }
        // -1 when no sector-local yellow is active; otherwise the 1-3 sector index
        // overtaking is currently banned in, independent of the full SC/VSC ban above.
        public int YellowFlagSector { get; private set; } = -1;
        // Raw internal incident-detector count: increments on every single
        // RegisterIncident call, including minor scrapes/spins/stranded cars/
        // mechanical gremlins that never come anywhere near a yellow flag -
        // with ~20 AI cars over a full race this is routinely in the
        // hundreds. Diagnostics/tuning only - see RaceControlIncidentCount
        // below for the number that's actually safe to show a player.
        public int IncidentCount { get; private set; }
        public int SafetyCarDeploymentCount { get; private set; }
        public int AiOvertakesCompletedCount { get; private set; }
        public int RedFlagCount { get; private set; }
        public string RedFlagReason { get; private set; } = "";

        // Report/UI-facing incident count fix: the post-race report and
        // CareerManager's team-news narrative used to read the raw
        // IncidentCount above directly, so a perfectly clean race with only
        // one real yellow flag could report "188 incidents" - every minor
        // scrape/spin/stranded-car detection tick counted equally with an
        // actual race-control action, and the number bore no relation to
        // what the race-control timeline actually showed. This instead
        // counts only genuine race-control escalations - exactly the entries
        // that appear in RaceControlHistory - so the summary and the
        // timeline can never disagree by construction.
        public int YellowFlagEventCount { get { return CountRaceControlHistoryLabel("YELLOW FLAG"); } }
        public int VirtualSafetyCarEventCount { get { return CountRaceControlHistoryLabel("VSC"); } }
        public int PenaltyEventCount { get { return CountRaceControlHistoryLabel("PENALTY"); } }
        public int RaceControlIncidentCount { get { return YellowFlagEventCount + VirtualSafetyCarEventCount + SafetyCarDeploymentCount + RedFlagCount; } }
        // Incident-cleanup breakdown (report "Incidents" card, not race-control
        // logic): a car-to-car contact has another participant within a tight
        // radius at the moment of detection; anything else that registers as a
        // collision (a spin, a wall/barrier brush, a kerb strike) is a solo
        // contact. Purely descriptive counters, never read by any escalation
        // decision - see DetectIncidents' collision branch.
        public int CarContactIncidentCount { get; private set; }
        public int SoloContactIncidentCount { get; private set; }
        // Seconds remaining in the current red-flag suspension; 0/negative once
        // race control has moved on to the restart. HUD-facing only.
        public float RedFlagTimeRemaining { get { return Mathf.Max(0f, redFlagTimer); } }
        public bool IsRedFlagged { get { return CurrentRaceControlState == RaceControlState.RedFlagged; } }
        // Seconds left in the current Restart-state countdown (used by both the
        // safety-car and red-flag restart chains) - HUD-facing only, so the
        // "restart in N seconds" banner can show a live number instead of a
        // static string.
        public float RestartCountdownSeconds { get { return Mathf.Max(0f, restartControlTimer); } }
        // True only while CurrentRaceControlState == Restart AND that restart
        // was entered from RedFlagged rather than the ordinary SafetyCarInThisLap
        // path - lets the HUD show a distinct "form up after the red flag"
        // restart banner instead of the generic safety-car one.
        public bool RestartFollowsRedFlag { get; private set; }

        // Frozen at the exact moment a red flag is thrown (see BeginRedFlag) -
        // driver name + position pairs, oldest/leader first. Read-only surface
        // for the post-race report / race-control history; never mutated after
        // the flag is thrown.
        readonly List<string> redFlagRunningOrderSnapshot = new List<string>();
        public IReadOnlyList<string> RedFlagRunningOrderSnapshot { get { return redFlagRunningOrderSnapshot; } }
        // The actual participant references behind the snapshot above, in the
        // exact same order - this (never the original race-start grid, never
        // re-sorted) is what TeleportFieldToRedFlagGrid places back onto the
        // grid once the 5-second hold elapses.
        readonly List<RaceParticipant> redFlagGridOrder = new List<RaceParticipant>();
        bool redFlagGridTeleportDone;
        const float RedFlagHoldSeconds = 5f;

        // Race-control history/timeline: a flat, chronological log of every
        // flag/SC/red-flag/restart/penalty event this session, for the post-
        // race report and any in-race history panel. Deliberately does NOT log
        // every minor incident (DetectIncidents fires often even for events
        // that never raise a flag) - only things race control actually acted
        // on, so the timeline reads as "what happened", not a debug feed.
        public struct RaceControlHistoryEntry
        {
            public string label;
            public string detail;
            public float raceTimeSeconds;
            public int lap;
        }

        readonly List<RaceControlHistoryEntry> raceControlHistory = new List<RaceControlHistoryEntry>();
        const int MaxRaceControlHistoryEntries = 60;
        public IReadOnlyList<RaceControlHistoryEntry> RaceControlHistory { get { return raceControlHistory; } }

        // A participant's index in the Participants list, i.e. its car index in
        // the replay frame's car order. -1 for a field-wide (no-car) marker.
        int ReplayCarIndex(RaceParticipant participant)
        {
            if (participant == null)
            {
                return -1;
            }

            IReadOnlyList<RaceParticipant> cars = Participants;
            for (int i = 0; i < cars.Count; i++)
            {
                if (cars[i] == participant)
                {
                    return i;
                }
            }

            return -1;
        }

        float redFlagTimer;
        // Distinct participants behind a genuinely catastrophic incident within
        // a short rolling window AND clustered at the same point on track -
        // this must read as one real, extreme pileup (track physically
        // blocked), never two unrelated incidents that happened to land
        // within the same time window somewhere else on the circuit. Pruned
        // by ConsiderRedFlag every time a new catastrophic incident comes in,
        // never polled on a timer.
        readonly List<RaceParticipant> recentCatastrophicIncidents = new List<RaceParticipant>();
        readonly List<float> recentCatastrophicIncidentTimes = new List<float>();
        readonly List<float> recentCatastrophicIncidentDistances = new List<float>();
        // Part 4 retune (aggressive): red flags must be an extreme rarity, not
        // "a slightly worse safety car" - tightened window, a real spatial
        // cluster requirement, and a much higher car count so only a genuine
        // multi-car pileup that has actually blocked the track can trigger
        // this path. Combined with the cooldown below, this alone accounts
        // for the bulk of the >=75% frequency reduction the fix calls for.
        const float CatastrophicIncidentWindowSeconds = 7f;
        const float CatastrophicIncidentClusterRadiusMeters = 55f;
        const int RedFlagMultiCarThreshold = 4;
        // Once thrown, a red flag cannot recur for a long time - it must read
        // as a true once-or-twice-a-race outlier, never a repeatable event,
        // and always rarer than a safety car (PostEscalationCooldownSeconds).
        const float RedFlagCooldownSeconds = 1500f;
        float redFlagCooldownTimer;

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

        // VSC/SC interactive pit-window offer state (player only). Set alongside
        // playerScPitPromptSent's own radio message; pressing P while active boxes
        // the player immediately under the VSC/SC window and overrides the original
        // pre-race planned pit lap for the current stop. Doing nothing lets it
        // expire and leaves the original plan untouched - see
        // AcceptRaceControlPitOffer/UpdatePlayerRaceControlPitOffer.
        bool playerHasActiveRaceControlPitOffer;
        float playerRaceControlPitOfferExpiresAt;
        RaceControlPitOfferType playerRaceControlPitOfferType;
        bool playerDeclinedRaceControlPitOfferMessageSent;
        // Cancellable-manual-pit-stop fix: brief "MANUAL PIT STOP CANCELLED"
        // HUD banner window - see CancelManualPitRequest/RaceHud.UpdatePitCard.
        const float PlayerManualPitCancelMessageSeconds = 3f;
        float playerManualPitCancelMessageTimer;
        public bool PlayerManualPitCancelMessageActive { get { return playerManualPitCancelMessageTimer > 0f; } }
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
        // Part 5 retune (~50% further cut): raised again (was 45/40) on top of
        // the severity-threshold tightening above, so the two fixes compound
        // instead of one alone having to carry the whole reduction.
        const float YellowSectorCooldownAfterClearSeconds = 65f;
        const float GlobalMinorYellowCooldownSeconds = 60f;
        const float MaxYellowEpisodeSeconds = 26f;
        // Part 4 retune: a genuinely global (cross-sector) cooldown, separate
        // from GlobalMinorYellowCooldownSeconds above (which only ever gated
        // the Minor/Medium yellowJustified path) - this one gates EVERY new
        // yellow flag, Major-severity included, which previously had no
        // global gate at all and could fire in a different sector the moment
        // that OTHER sector's own per-sector cooldown happened to be clear.
        // Part 5 retune: raised again (was 30f).
        const float GlobalYellowFlagCooldownSeconds = 48f;
        float globalYellowFlagCooldownUntil;
        float drsRestartCooldownTimer;
        RaceParticipant safetyCarQueueLeader;
        float lastIncidentTime = -999f;
        float lastIncidentDistance = -99999f;

        // Restart handoff (bug fix): race control keeps convoy autopilot through
        // the Restart hold AND a short green-flag ramp afterward, instead of
        // dropping every car back to normal driving the instant Restart -> Green
        // fires. raceControlReferenceDistance/Speed is a single shared "virtual
        // convoy" point that tracks the real safety car while it's physically on
        // track and then keeps advancing on its own (ramping toward full pace)
        // once the car has peeled into the pits, so the queue never stalls
        // waiting on an object that's already gone and BuildRaceControlAutopilotCommand
        // never has to fall back to a blind brake-and-go-straight command.
        float raceControlReferenceDistance;
        float raceControlReferenceSpeedKph;
        float restartRampTimer;
        bool restartHandbackMessageSent;
        const float RestartRampDurationSeconds = 2.5f;
        const float RestartFormationTargetSpeedKph = 215f;
        const float RaceControlQueueLeadMeters = 28f;

        // True for the full physical safety car period, through the Restart
        // hold, and for a short ramp afterward once Green begins - autopilot
        // only lets go of a car once this goes false, which is also the single
        // signal AiVehicleController uses to resync its own track-progress
        // reference (see HandleRaceControlAutopilotReleased) instead of
        // resuming normal driving off a lastProgressDistance that can be stale
        // by a lap or more.
        public bool IsRaceControlAutopilotHoldPeriod
        {
            get
            {
                return IsFullSafetyCarPeriod ||
                       CurrentRaceControlState == RaceControlState.RedFlagged ||
                       CurrentRaceControlState == RaceControlState.Restart ||
                       (CurrentRaceControlState == RaceControlState.Green && restartRampTimer > 0f);
            }
        }

        // Part 1: the real, visible AI safety car - built lazily the first time
        // it's needed each session and reused for every deployment within that
        // session rather than instantiated fresh each time.
        GameObject safetyCarObject;
        SafetyCarController safetyCarController;
        // Live full-session replay capture (Phase K); gated by its own flag,
        // read-only over the cars, bounded memory. RaceManager drives it.
        readonly ReplayCaptureService replayCapture = new ReplayCaptureService();
        /// <summary>The current session's replay recording (null when capture is off/not started).</summary>
        public F1Game.Race.ReplayRecording ReplayRecording => replayCapture.Recording;
        /// <summary>Build a session timeline/highlights summary from the captured replay markers.</summary>
        public F1Game.Race.ReplayTimeline.Summary BuildReplayTimeline() => F1Game.Race.ReplayTimeline.Build(replayCapture.Recording);
        // Live player telemetry capture (Phase K engineer debrief / CSV export);
        // gated by its own flag, read-only over the player car, bounded by a
        // sample cap. RaceManager drives Begin/Sample/ExportCsv.
        readonly TelemetryCaptureService telemetryCapture = new TelemetryCaptureService();
        /// <summary>Samples captured in the current player telemetry trace (0 when off/not started).</summary>
        public int TelemetrySampleCount => telemetryCapture.SampleCount;
        /// <summary>Export the current session's player telemetry to a CSV; returns the path or null.</summary>
        public string ExportTelemetryCsv(string fileName) => telemetryCapture.ExportCsv(fileName);
        /// <summary>Build an engineer debrief (speed/throttle/brake/DRS/tyre summary) from the captured telemetry.</summary>
        public TelemetryDebrief.Summary BuildTelemetryDebrief() => TelemetryDebrief.Build(telemetryCapture.Recorder);

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
        float engineerCooldown;

        // Radio message stacking fix: this used to be a single "now showing"
        // message plus a queue of everything waiting its turn, so a pit call
        // arriving while a DRS/tyre/gap flavor line was showing had to wait
        // for that line to fully finish before it ever appeared - exactly the
        // "important messages get delayed/hidden during busy moments" bug
        // report. Now every message that fires becomes its own independently
        // timed, independently fading entry in activeEngineerMessages (newest
        // first) and RaceHud renders as many of them as are currently active
        // as separate stacked cards, instead of forcing everything through one
        // shared text slot. Capped so a chatty session can't wall-of-text the
        // HUD; a low-priority entry is evicted early (not silently dropped -
        // its natural age already meant it was closest to expiring anyway) to
        // make room for a new one once the cap is hit.
        struct EngineerMessageEntry
        {
            public string text;
            public float remaining;
            public float age;
            public bool priority;
        }
        readonly List<EngineerMessageEntry> activeEngineerMessages = new List<EngineerMessageEntry>();
        const int MaxActiveEngineerMessages = 4;
        const float EngineerMessageAnimInDuration = 0.3f;
        const float EngineerMessageAnimOutDuration = 0.4f;
        const float PriorityEngineerMessageDuration = 8.5f;
        const float RoutineEngineerMessageDuration = 5f;

        public int ActiveEngineerMessageCount { get { return activeEngineerMessages.Count; } }

        public string GetActiveEngineerMessageText(int index)
        {
            return index >= 0 && index < activeEngineerMessages.Count ? activeEngineerMessages[index].text : "";
        }

        public bool GetActiveEngineerMessagePriority(int index)
        {
            return index >= 0 && index < activeEngineerMessages.Count && activeEngineerMessages[index].priority;
        }

        // 0-1 slide/fade progress for one stacked entry: rises over
        // EngineerMessageAnimInDuration after it first appears, holds at 1,
        // then falls over EngineerMessageAnimOutDuration right before it
        // expires and is removed.
        public float GetActiveEngineerMessageFade(int index)
        {
            if (index < 0 || index >= activeEngineerMessages.Count)
            {
                return 0f;
            }

            EngineerMessageEntry entry = activeEngineerMessages[index];
            float inProgress = Mathf.Clamp01(entry.age / EngineerMessageAnimInDuration);
            float outProgress = Mathf.Clamp01(entry.remaining / EngineerMessageAnimOutDuration);
            return Mathf.Min(inProgress, outProgress);
        }

        float lightsOutTime;
        float playerReactionTime = -1f;
        float reactionDisplayTimer;
        bool waitingForPlayerReaction;
        int lastEngineerPitLapPrompt = -1;
        // Single-source-of-truth fix: GetPlannedPitLapForStop's Auto branch used
        // to call RecommendedPitLap fresh on every single call, and
        // RecommendedPitLap reads Track.weather - which UpdateWeatherTransition
        // can flip mid-race. That meant an "Auto" planned stop's resolved lap
        // could silently drift partway through the race: an early engineer
        // message could name one lap, and the actual auto-trigger evaluate a
        // different one later after a weather transition, or the HUD could show
        // a third value depending on exactly when it last redrew. Resolving once
        // per race and caching here (reset alongside the rest of the
        // per-session engineer state in ResetEngineerState) makes every
        // consumer - engineer messages, HUD, and the actual auto-trigger - read
        // the exact same resolved target for the whole race, the same way an
        // explicit player-chosen lap already always has.
        int cachedPlannedPitLapStopOne = -1;
        int cachedPlannedPitLapStopTwo = -1;
        bool engineerWeatherSent;
        bool engineerPitRequestConfirmed;
        bool engineerTyreWarningSent;
        bool engineerBatteryWarningSent;
        bool engineerFinalLapSent;
        bool engineerFuelWarningSent;
        // Fuel system pass: tracks whether the fuel delta has EVER gone negative
        // this race, so the recovery/safe-to-push messages only ever fire after a
        // genuine negative-to-positive transition, not on a car that was always
        // comfortably on plan.
        bool engineerFuelEverNegative;
        bool engineerFuelRecoverySent;
        bool engineerFuelSafeToPushSent;
        bool engineerFuelStarvationSent;
        bool engineerDamageWarningSent;
        bool engineerRivalSent;
        bool engineerTrackLimitsSent;
        // Part C.1: Expert-only radio warnings.
        bool engineerExpertWarningSent;
        float engineerDrsWarningCooldown;
        int lastGapReportLap = -1;
        // Weather-transition + track-evolution state lives with its logic in the
        // RaceManager.Weather.cs partial (fields kept here would still be shared -
        // moved together so the whole subsystem reads in one place).

        // Part 1: extra atmosphere/feedback state - overtake notifications,
        // session-fastest-lap tracking, teammate gap callouts, flat spot/lockup
        // warnings and the HUD toast relay queue.
        int playerLastPosition = -1;
        float overtakeCheckTimer;
        float sessionFastestLap = -1f;
        string sessionFastestLapDriverId = "";
        // Read by the HUD telemetry relay; -1 until anyone sets a lap.
        public float SessionFastestLap { get { return sessionFastestLap; } }
        bool engineerFlatSpotWarningSent;
        bool engineerLockupWarningSent;
        int lastTeammateGapReportLap = -1;
        bool engineerPodiumMessageSent;

        // Lap-gap radio: engineer call comparing the player's gap to the relevant
        // car (car ahead, or P2 if the player leads) against the previous lap's
        // gap. Split into "pending" state (armed the instant a new lap is detected,
        // fires after a short delay once timing has settled) and a persistent
        // snapshot of the last successfully-spoken/refreshed comparison.
        float playerGapRadioPendingTimer = -1f;
        int playerGapRadioPendingLapNumber = -1;
        int playerGapRadioLastSeenCompletedLaps = -1;
        int playerPitStopsAtLastGapRadioBoundary = -1;
        int lastPlayerGapRadioLap = -1;
        float previousPlayerComparisonGap = -1f;
        string previousComparisonDriverId = "";
        bool previousPlayerWasLeader;
        int previousPlayerGapRadioPosition = -1;
        int lastStartLightCountPlayed = -1;
        // Restart countdown lights/audio (red flag and safety car restarts):
        // mirrors the original race-start light build-up (PlayStartLight)
        // over the final 5 seconds of the Restart state's hold, instead of a
        // restart being silent/text-only. -1 means "not yet armed for this
        // restart" so it can't fire mid-transition off a stale value.
        int lastRestartLightCountPlayed = -1;
        float lastRecordedPlayerBestLap;
        bool pendingTimeTrial;

        // Time-trial ghost recording/playback state (see GhostCarController,
        // TimeTrialGhostStore). ghostRecordingBuffer only ever holds the
        // CURRENT lap's samples - cleared the instant CompletedLaps advances,
        // promoted to storage only when that lap turns out to be a new best
        // (see TrackPlayerBestLapRecord).
        readonly List<GhostSample> ghostRecordingBuffer = new List<GhostSample>();
        float ghostRecordTimer;
        int ghostRecordedLapNumber = -1;
        const float GhostSampleInterval = 0.12f;
        GameObject ghostCarObject;
        GhostCarController ghostController;
        // Held so the cinematic podium presentation (see PodiumPresentationSequence)
        // can temporarily take manual control of the same camera the player was
        // just driving with, instead of creating a second competing camera.
        CameraRig playerCameraRig;
        float playerResetCooldown;
        // Throughput fix: the global nextPitReleaseAllowedTime/
        // PitReleaseMinDebounceSeconds release-rate timer that used to sit
        // here has been removed entirely - it hard-capped release throughput
        // field-wide (2.5 cars/second even at its smallest 0.4s value)
        // regardless of how clear the lane actually was. Spatial headway
        // (FindPitExitQueueCarAhead) is the ONLY release gate now; see
        // UpdatePitService for why no same-frame safeguard is needed on top
        // of it (participants are already processed sequentially each tick).
        float stackResolveTimer;
        const int FullWeekendDriverCount = 22;
        const int FullWeekendAiCount = FullWeekendDriverCount - 1;
        // Survivor counts moved to F1Game.Race.Rules.QualifyingProgression (the
        // unit-tested source of truth for Q1/Q2/Q3 elimination).
        const int Q1SurvivorCount = QualifyingProgression.Q1SurvivorCount;
        const int Q2SurvivorCount = QualifyingProgression.Q2SurvivorCount;
        // Qualifying-vs-race calibration fix (single-flying-lap bug): this used to
        // be a flat 2 laps (1 out lap + exactly 1 timed lap) for every car, AI and
        // player alike, in a live-driven qualifying session. With per-lap noise
        // (line wobble, apex-miss variance, mistakeChancePerLap) baked into the AI
        // driving model, a single flying-lap attempt has real variance and no
        // chance to throw away a scrappy lap and try again - while a full race
        // gives the same AI dozens of laps to post its best, LapTracker.BestLapTime
        // already tracks the minimum across all of them. That mismatch in sample
        // size, not underlying pace, was the real reason race best laps kept
        // beating qualifying best laps even after TrackAverageSpeedFactor was
        // recalibrated. Raised to a real multi-attempt session (1 out lap + up to
        // 4 timed attempts, same as real quali) so qualifying's best-of-many is
        // actually comparable to the race's.
        const int QualifyingSessionLapCap = 5;
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
            // Part 21 regulation hook: reset every session start (not just career
            // ones) so Quick Race/Time Trial always get the neutral 1f default
            // regardless of whatever a career season's regulation last set it to.
            TyreState.RegulationWearMultiplier = (careerRace && career != null && career.Save != null) ? career.Save.currentSeasonTyreWearMultiplier : 1f;
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
            Track = trackManager.Build(eventData, Settings.Current.racingLineAssist);
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

            if (!ProductionSessionUi.TryShowQualifyingResults(results, IsCareerRace))
            {
                ProductionSessionUi.BeginResults();
                ui.ShowQualifyingResults(this, results, IsCareerRace);
            }
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
                    string mistakeLabel = string.IsNullOrEmpty(breakdown.mistakeType) ? "mistake" : breakdown.mistakeType;
                    text.Append("Driver mistake           ").Append(SignedSeconds(breakdown.mistakePenalty)).Append("  (").Append(mistakeLabel).Append(")\n");
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

                // Physical AI jump starts: a car whose rolled window has
                // arrived is released by HoldGridCars above; launch it once and
                // judge it through the same rulebook path as the player
                // (ReportJumpStartIntent latches jumpStartPenaltyApplied).
                if (CurrentSession != RaceWeekendSession.Qualifying && !IsTimeTrial)
                {
                    for (int i = 0; i < Participants.Count; i++)
                    {
                        RaceParticipant jumper = Participants[i];
                        if (jumper == null || jumper.isPlayer || jumper.vehicle == null ||
                            jumper.jumpStartPenaltyApplied || jumper.aiJumpStartWindowSeconds <= 0f ||
                            StartCountdown > jumper.aiJumpStartWindowSeconds)
                        {
                            continue;
                        }

                        jumper.vehicle.SetGridHold(false);
                        jumper.vehicle.ArmRaceLaunchBoost(6f);
                        ReportJumpStartIntent(jumper);
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage(jumper.driverName + " jumped the start - penalty coming.", false, RaceAudioCue.Penalty);
                        }
                    }
                }

                StartCountdown = Mathf.Max(0f, StartCountdown - Time.deltaTime);
                if (CurrentSession != RaceWeekendSession.Qualifying && RaceStartLightCount >= 5)
                {
                    SessionMessage = "Hold... lights out pending";
                }

                // Edge-triggered beep per light as the build-up sequence lights each
                // one in turn, rather than every frame that light stays lit.
                if (CurrentSession != RaceWeekendSession.Qualifying && RaceStartLightCount != lastStartLightCountPlayed && RaceStartLightCount > 0)
                {
                    lastStartLightCountPlayed = RaceStartLightCount;
                    SimpleAudioManager.PlayStartLight(RaceStartLightCount - 1);
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
                        SimpleAudioManager.PlayStartLight(5);
                        // Arm every AI car's launch boost directly at the
                        // lights-out frame - the vehicle applies the boost itself
                        // (VehicleController.ArmRaceLaunchBoost), independent of
                        // the whole AI command pipeline.
                        for (int i = 0; i < Participants.Count; i++)
                        {
                            if (Participants[i] != null && Participants[i].vehicle != null && !Participants[i].isPlayer)
                            {
                                Participants[i].vehicle.ArmRaceLaunchBoost(6f);
                            }
                        }
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
                        // Phase progression is the rulebook's call (Q1->Q2->Q3);
                        // reaching here with no next phase would be a logic bug
                        // upstream (BeginQualifyingFeedback decides finish), so
                        // the false case deliberately leaves the phase alone.
                        int nextQualifyingPhase;
                        if (SessionFlow.TryAdvanceQualifyingPhase(qualifyingPhase, out nextQualifyingPhase))
                        {
                            qualifyingPhase = nextQualifyingPhase;
                        }

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
                    SampleCorneringTelemetry(participant);
                    if (participant.isPlayer && CurrentSession == RaceWeekendSession.Qualifying)
                    {
                        CapturePlayerQualifyingBestLap(participant.lapTracker);
                        SessionMessage = QualifyingLapStatusText(participant.lapTracker);
                    }
                }

                HandleFallRespawn(participant);
                HandleStuckEscalation(participant);
                HandleTrackLimits(participant);
                HandlePitService(participant);
                HandleFinish(participant);
                UpdateFuelState(participant);
                UpdateDrsEligibility(participant);

                if (participant.vehicle != null)
                {
                    participant.trackedTickFrameCount++;
                    if (participant.vehicle.ErsDeploying) participant.ersDeployFrameCount++;
                    if (participant.vehicle.DrsActive) participant.drsActiveFrameCount++;
                }
            }

            UpdateSlipstreamEffects();
            replayCapture.Tick(Participants, RaceElapsed);
            if (telemetryCapture.IsCapturing && PlayerParticipant != null)
            {
                float telemetryDelta = 0f;
                if (!TryGetQualifyingDelta(PlayerParticipant, out telemetryDelta))
                {
                    TryGetGhostDelta(PlayerParticipant, out telemetryDelta);
                }

                telemetryCapture.Sample(PlayerParticipant, RaceElapsed, telemetryDelta);
            }

            if (IsTimeTrial)
            {
                RecordGhostSample();
                UpdateGhostPlayback();
            }

            if (PlayerParticipant != null)
            {
                SimpleAudioManager.SetPitAmbienceTarget(PlayerParticipant.isPitting || PlayerParticipant.pitPhase != PitPhase.None);
            }

            ResolveLowSpeedStacks();
            SortRunningOrder();
            CheckIllegalOvertakesUnderYellow();
            UpdateOvertakeAndFastestLapNotifications();
            UpdatePlayerLapGapRadio();
            UpdatePlayerAutoPitStrategy();
            UpdatePlayerRaceControlPitOffer();
            UpdateRaceEngineer();
            UpdateWeatherTransition();
            UpdateTrackEvolution();
            UpdateRaceControl();
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                if (ShouldCompleteQualifyingRun())
                {
                    CompleteQualifyingRun();
                }
            }
            else if (!IsRaceFinished && PlayerParticipant != null && PlayerParticipant.finished)
            {
                // Re-entrancy fix: PlayerParticipant.finished is never cleared back to
                // false once set, and this whole method runs every frame - without this
                // guard, FinishRace() (which awards championship points via
                // Career.ApplyRaceResults, increments Save.currentRound, records
                // win/podium/pole career stats, etc.) fired again on every subsequent
                // frame for as long as this Update loop kept ticking after the player
                // crossed the line (e.g. while the results screen was coming up, before
                // the race world actually tore down) - each extra call re-applied a full
                // race's worth of points and round progress on top of the last. This is
                // exactly what could put a driver standing at ~700 points a handful of
                // rounds into a season instead of the real, much smaller total.
                // FinishRace() itself sets IsRaceFinished = true as its very first line,
                // so gating on it here makes the call strictly once-per-race.
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
            // Production HUD (if active) hides while paused so the legacy pause
            // menu is visible/interactable; restored on resume.
            ProductionSessionUi.SetPaused(this, IsPaused);
            ui.SetPauseVisible(IsPaused);
        }

        public void Resume()
        {
            IsPaused = false;
            Time.timeScale = 1f;
            ProductionSessionUi.SetPaused(this, false);
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
            TrackQueryProvider.Clear();
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

            // Ghost car is parented under raceWorld so Destroy above already
            // removes it from the scene - just drop the now-stale references.
            ghostCarObject = null;
            ghostController = null;
            ghostRecordingBuffer.Clear();
            ghostRecordedLapNumber = -1;
            playerCameraRig = null;
            ActivePracticeProgramId = null;

            SimpleAudioManager.SetRain(false);
            SimpleAudioManager.SetRaceAmbience(false);
        }

        // Playable practice programs: scores the just-driven Practice session
        // against the criteria for whichever program (ActivePracticeProgramId)
        // the player picked in RuntimeUi.ShowPracticePrograms, from real telemetry
        // captured during the session rather than an unconditional click reward.
        // Call this BEFORE CleanupRaceWorld() while PlayerParticipant is still live.
        public PracticeSessionResult EvaluatePracticeSession()
        {
            PracticeSessionResult result = new PracticeSessionResult { programId = ActivePracticeProgramId };
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null || PlayerParticipant.vehicle == null)
            {
                result.title = "Practice Session";
                result.passed = false;
                result.metricSummary = "No valid lap data was recorded.";
                return result;
            }

            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            float bestLap = PlayerParticipant.lapTracker.BestLapTime;
            float tyreWear = PlayerParticipant.vehicle.Tyres == null ? 1f : PlayerParticipant.vehicle.Tyres.Wear;
            float ersBattery = PlayerParticipant.vehicle.ErsBattery;
            int pitStops = PlayerParticipant.pitStops;

            switch (ActivePracticeProgramId)
            {
                case "acclimatisation":
                    result.title = "Track Acclimatisation";
                    result.passed = completedLaps >= 3;
                    result.metricSummary = completedLaps + " lap(s) completed (need 3).";
                    break;

                case "tyreManagement":
                    result.title = "Tyre Management";
                    result.passed = completedLaps >= 5 && tyreWear > 0.4f;
                    result.metricSummary = completedLaps + " lap(s) completed, tyres at " + Mathf.RoundToInt(tyreWear * 100f) + "% life (need 5 laps and above 40% life).";
                    break;

                case "ersManagement":
                    result.title = "ERS Management";
                    result.passed = completedLaps >= 3 && ersBattery > 0.5f;
                    result.metricSummary = completedLaps + " lap(s) completed, battery at " + Mathf.RoundToInt(ersBattery * 100f) + "% (need 3 laps and above 50%).";
                    break;

                case "qualifyingPace":
                {
                    result.title = "Qualifying Pace";
                    float bestAiLap = BestAiLapTimeThisSession();
                    bool haveBenchmark = bestAiLap > 0f;
                    result.passed = bestLap > 0f && haveBenchmark && bestLap <= bestAiLap * 1.03f;
                    result.metricSummary = bestLap > 0f
                        ? ("Best lap " + UiFactory.FormatTime(bestLap) + (haveBenchmark ? " vs field best " + UiFactory.FormatTime(bestAiLap) + " (need within 3%)." : "."))
                        : "No valid lap was set.";
                    break;
                }

                case "racePace":
                    result.title = "Race Pace";
                    result.passed = completedLaps >= 8 && pitStops >= 1;
                    result.metricSummary = completedLaps + " lap(s) completed, " + pitStops + " pit stop(s) (need 8 laps and 1 stop).";
                    break;

                default:
                    result.title = "Practice Session";
                    result.passed = completedLaps >= 1;
                    result.metricSummary = completedLaps + " lap(s) completed.";
                    break;
            }

            return result;
        }

        float BestAiLapTimeThisSession()
        {
            float best = -1f;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null || participant.isPlayer || participant.lapTracker == null)
                {
                    continue;
                }

                float lap = participant.lapTracker.BestLapTime;
                if (lap > 0f && (best < 0f || lap < best))
                {
                    best = lap;
                }
            }

            return best;
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
            // Practice free-runs the same way Time Trial does - see
            // GameBootstrap.StartCareerPractice / EvaluatePracticeSession, which
            // score the session from telemetry once the player manually ends it
            // rather than from a lap-count finish.
            get { return (IsTimeTrial || CurrentSession == RaceWeekendSession.Practice) ? 999 : Mathf.Max(3, Settings.Current.laps); }
        }

        // UpdateWeatherTransition + UpdateTrackEvolution live in the
        // RaceManager.Weather.cs partial (same class; behaviour unchanged).

        void ResetRaceControlState()
        {
            CurrentRaceControlState = RaceControlState.Green;
            SafetyCarTargetSpeedKph = 150f;
            IsPitLaneOpen = true;
            YellowFlagSector = -1;
            IncidentCount = 0;
            CarContactIncidentCount = 0;
            SoloContactIncidentCount = 0;
            SafetyCarDeploymentCount = 0;
            AiOvertakesCompletedCount = 0;
            RedFlagCount = 0;
            RedFlagReason = "";
            RestartFollowsRedFlag = false;
            redFlagTimer = 0f;
            redFlagCooldownTimer = 0f;
            redFlagGridTeleportDone = false;
            redFlagRunningOrderSnapshot.Clear();
            redFlagGridOrder.Clear();
            recentCatastrophicIncidents.Clear();
            recentCatastrophicIncidentTimes.Clear();
            recentCatastrophicIncidentDistances.Clear();
            raceControlHistory.Clear();
            raceControlCheckTimer = 0f;
            safetyCarTimer = 0f;
            restartControlTimer = 0f;
            safetyCarInThisLapMessageSent = false;
            coldTyresRestartWarningSent = false;
            playerScPitPromptSent = false;
            playerHasActiveRaceControlPitOffer = false;
            playerDeclinedRaceControlPitOfferMessageSent = false;
            yellowSectorClearTimer = 0f;
            yellowSectorCooldownUntil.Clear();
            globalMinorYellowCooldownUntil = 0f;
            globalYellowFlagCooldownUntil = 0f;
            yellowSectorEpisodeStartTime = -999f;
            drsRestartCooldownTimer = 0f;
            safetyCarQueueLeader = null;
            lastIncidentTime = -999f;
            lastIncidentDistance = -99999f;
            raceControlReferenceDistance = 0f;
            raceControlReferenceSpeedKph = 0f;
            restartRampTimer = 0f;
            restartHandbackMessageSent = false;
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

        void ResetEngineerState()
        {
            activeEngineerMessages.Clear();
            engineerCooldown = 0f;
            lastEngineerPitLapPrompt = -1;
            engineerWeatherSent = false;
            engineerPitRequestConfirmed = false;
            engineerTyreWarningSent = false;
            engineerBatteryWarningSent = false;
            engineerFinalLapSent = false;
            engineerFuelWarningSent = false;
            engineerFuelEverNegative = false;
            engineerFuelRecoverySent = false;
            engineerFuelSafeToPushSent = false;
            engineerFuelStarvationSent = false;
            engineerDamageWarningSent = false;
            engineerRivalSent = false;
            engineerTrackLimitsSent = false;
            engineerExpertWarningSent = false;
            engineerDrsWarningCooldown = 0f;
            lastGapReportLap = -1;
            weatherTransitionDone = false;
            weatherSecondTransitionDone = false;
            trackEvolutionHalfwayMessageSent = false;
            playerLastPosition = -1;
            overtakeCheckTimer = 0f;
            sessionFastestLap = -1f;
            sessionFastestLapDriverId = "";
            engineerFlatSpotWarningSent = false;
            engineerLockupWarningSent = false;
            lastTeammateGapReportLap = -1;
            engineerPodiumMessageSent = false;
            hudToastQueue.Clear();
            playerGapRadioPendingTimer = -1f;
            playerGapRadioPendingLapNumber = -1;
            playerGapRadioLastSeenCompletedLaps = -1;
            playerPitStopsAtLastGapRadioBoundary = -1;
            lastPlayerGapRadioLap = -1;
            previousPlayerComparisonGap = -1f;
            previousComparisonDriverId = "";
            previousPlayerWasLeader = false;
            previousPlayerGapRadioPosition = -1;
            cachedPlannedPitLapStopOne = -1;
            cachedPlannedPitLapStopTwo = -1;
        }

        void TickEngineerTimers()
        {
            engineerCooldown = Mathf.Max(0f, engineerCooldown - Time.deltaTime);
            reactionDisplayTimer = Mathf.Max(0f, reactionDisplayTimer - Time.deltaTime);
            playerResetCooldown = Mathf.Max(0f, playerResetCooldown - Time.deltaTime);
            engineerDrsWarningCooldown = Mathf.Max(0f, engineerDrsWarningCooldown - Time.deltaTime);
            playerManualPitCancelMessageTimer = Mathf.Max(0f, playerManualPitCancelMessageTimer - Time.deltaTime);

            // Radio message stacking fix: every active entry ages/counts down
            // independently and is removed the instant it expires - there is
            // no single "current" message to advance from a queue any more,
            // each one lives and dies entirely on its own timer.
            for (int i = activeEngineerMessages.Count - 1; i >= 0; i--)
            {
                EngineerMessageEntry entry = activeEngineerMessages[i];
                entry.age += Time.deltaTime;
                entry.remaining -= Time.deltaTime;
                if (entry.remaining <= 0f)
                {
                    activeEngineerMessages.RemoveAt(i);
                    continue;
                }

                activeEngineerMessages[i] = entry;
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

        // Part 2: a local yellow only limits speed for cars actually near the
        // incident that caused it, not the entire lap-third sector it's flagged
        // in - a genuine "progress window around the incident", tighter than the
        // sector-wide overtake ban above (which deliberately stays sector-wide,
        // since that's about not passing near a hazard you might not see yet).
        const float LocalYellowSpeedCapWindowMeters = 180f;
        const float LocalYellowSpeedCapKph = FlagRules.LocalYellowSpeedCapKph;

        public bool IsNearLocalYellowIncident(RaceParticipant participant)
        {
            if (participant == null || State == null || Track == null || CurrentRaceControlState != RaceControlState.YellowSector)
            {
                return false;
            }

            TrackProgress progress = State.GetCurrentProgress(participant);
            // Limiter-duration fix: the cap used to release the moment the car
            // passed a narrow metre-window around the incident, while the
            // yellow FLAG itself covers the whole sector until race control
            // clears it - the limiter visibly ended before the flag did. The
            // cap now holds throughout the flagged sector (the exact same
            // sector test the flag display and the overtaking ban already
            // use), with the incident-proximity window kept as a fallback for
            // a car straddling the sector boundary right next to the incident.
            if (YellowFlagSector >= 0 && progress.sector == YellowFlagSector)
            {
                return true;
            }

            return Mathf.Abs(Track.WrapDistance(progress.distance - lastIncidentDistance)) < LocalYellowSpeedCapWindowMeters;
        }

        float LocalHalfWidthAt(float distance)
        {
            F1Game.Track.ITrackQuery query = TrackQueryProvider.Active;
            return query != null ? query.WidthAt(distance) * 0.5f : Track.HalfWidthAt(distance);
        }

        // The AiDifficultyProfile struct + GetAiDifficultyProfile (per-tier
        // decision-quality profiles) live in the RaceManager.AiProfiles.cs
        // partial (same class; the struct stays RaceManager.AiDifficultyProfile).

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

        void SortRunningOrder()
        {
            if (State != null) State.Tick();
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
            public string mistakeType;
            public float variance;
            public float tyreChoicePenalty;
            public float finalTime;
        }

        readonly QualifyingLapBreakdown[] playerSimBreakdowns = new QualifyingLapBreakdown[3];

        // The shared best-of-two qualifying attempt orchestration
        // (SimulateBestOfTwoQualifyingAttempt, the AI/player time entry points
        // and the tyre/weather penalty) lives in RaceManager.Qualifying.cs.

        // The qualifying lap-time model (SimulateQualifyingRunDetailed, the
        // circuit reference lap, field-average helpers and the mistake penalty)
        // lives in the RaceManager.Qualifying.cs partial.


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

        public class PracticeSessionResult
        {
            public string programId;
            public string title;
            public bool passed;
            public string metricSummary;
        }
    }
}
