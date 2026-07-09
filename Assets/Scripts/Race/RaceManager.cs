using System.Collections;
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
        // Percentage pace reduction under a Virtual Safety Car (no physical car, just
        // a pace delta) - only meaningful while CurrentRaceControlState == VirtualSafetyCar.
        public float VirtualSafetyCarPaceMultiplier { get; private set; } = 0.62f;
        public bool IsPitLaneOpen { get; private set; } = true;
        public bool IsOvertakingAllowed { get; private set; } = true;
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

        void LogRaceControlHistory(string label, string detail)
        {
            int lap = PlayerParticipant != null && PlayerParticipant.lapTracker != null ? PlayerParticipant.lapTracker.DisplayLap : 0;
            raceControlHistory.Add(new RaceControlHistoryEntry { label = label, detail = detail, raceTimeSeconds = RaceElapsed, lap = lap });
            if (raceControlHistory.Count > MaxRaceControlHistoryEntries)
            {
                raceControlHistory.RemoveAt(0);
            }
        }

        int CountRaceControlHistoryLabel(string label)
        {
            int count = 0;
            for (int i = 0; i < raceControlHistory.Count; i++)
            {
                if (raceControlHistory[i].label == label)
                {
                    count++;
                }
            }

            return count;
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
        bool weatherTransitionDone;
        bool weatherSecondTransitionDone;
        bool trackEvolutionHalfwayMessageSent;

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
        // Pit lane release control: one car released at a time with a safe gap.
        float nextPitReleaseAllowedTime;
        const float PitReleaseGapSeconds = 1.6f;
        float stackResolveTimer;
        static PhysicMaterial carBodyPhysicsMaterial;
        const int FullWeekendDriverCount = 22;
        const int FullWeekendAiCount = FullWeekendDriverCount - 1;
        const int Q1SurvivorCount = 16;
        const int Q2SurvivorCount = 10;
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
            raceStartSequenceDuration = session == RaceWeekendSession.Qualifying || IsTimeTrial ? 1.5f : Random.Range(5.4f, 6.8f);
            StartCountdown = raceStartSequenceDuration;
            lastStartLightCountPlayed = -1;
            lastRestartLightCountPlayed = -1;
            SessionMessage = session == RaceWeekendSession.Qualifying ? "Q" + qualifyingPhase + " out lap ready"
                : (IsTimeTrial ? "Time trial: set a lap"
                : (session == RaceWeekendSession.Practice ? "Practice: drive your program laps" : "Race start"));
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
            SpawnRaceGrid(playerName, playerTeamId, careerRace);
            SpawnGhostIfAvailable();
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

        // ---------- fuel system ----------
        // Distance-scaled fuel replacing the old flat 35kg-every-session load - a
        // 5-lap race used to start with as much fuel as a genuine 60-lap race would
        // need. Each session type gets its own, deliberately simple start-fuel
        // calculation below; VehicleController itself stays session-agnostic (see
        // VehicleController.SetStartFuel) and just burns/reports against whatever
        // it's told here.
        const float MinimumRaceFuelKg = 4.5f;
        const float MaximumRaceFuelKg = 220f;
        const float RaceFuelReserveKg = 1.2f;

        public static float FuelLoadChoiceLapDelta(FuelLoadChoice choice)
        {
            switch (choice)
            {
                case FuelLoadChoice.AggressiveUnderfuel: return -1.5f;
                case FuelLoadChoice.LightUnderfuel: return -0.7f;
                case FuelLoadChoice.Safe: return 0.7f;
                case FuelLoadChoice.Heavy: return 1.5f;
                default: return 0f;
            }
        }

        // Reference track length (this game's own long-standing fallback default
        // elsewhere - TrackManager/EstimateReferenceLapTime) lands right in the
        // middle of the range, at ~1.5kg/lap; genuinely long/short circuits scale
        // a little either side of that. Difficulty nudges it slightly too - a
        // sharper AI field pushes harder and burns a touch more per lap.
        public float EstimateFuelPerLapKg(TrackRuntime track, RaceDifficulty difficulty)
        {
            // Speed-rebalance pass: TrackManager.TargetTrackLength scaled every
            // circuit's target length up ~25% - both the fallback and the
            // short/long InverseLerp band here need to move with it, or every
            // rebalanced track (now mostly 4.9-7km) would sit near/above the old
            // 6200f ceiling and lose the intended short-vs-long fuel variation.
            // Round 2: TrackLengthRebalanceScale stacked another 25% (1.5625x
            // total), so both the fallback and band move with it again.
            float trackLength = track != null && track.length > 1f ? track.length : 7266f;
            float perLap = Mathf.Lerp(1.35f, 1.65f, Mathf.Clamp01(Mathf.InverseLerp(5000f, 9688f, trackLength)));
            float difficultyFactor = difficulty == RaceDifficulty.Easy ? 0f : difficulty == RaceDifficulty.Medium ? 0.33f : difficulty == RaceDifficulty.Hard ? 0.66f : 1f;
            perLap *= Mathf.Lerp(0.97f, 1.06f, difficultyFactor);
            return perLap;
        }

        // requiredFuelKg = estimatedFuelPerLapKg * raceLaps + reserveFuelKg, then the
        // chosen fuel-load plan shifts that target by its own lap-count delta
        // (FuelLoadChoiceLapDelta) before the floor/ceiling clamp. raceLaps here
        // should be the real configured lap count (Mathf.Max(3, Settings.Current.laps)),
        // never the RaceLaps property's own 999 stand-in for Practice/Time Trial.
        public float ComputeRaceStartFuelKg(int raceLaps, FuelLoadChoice choice, TrackRuntime track, GameSettingsData settings)
        {
            // GameSettingsData only stores the raw difficultyIndex (0-3) - Difficulty
            // itself is a GameSettingsStore property, not reachable from the plain
            // data object this helper takes - so the same clamp that property
            // applies is repeated here.
            int difficultyIndex = settings == null ? 1 : Mathf.Clamp(settings.difficultyIndex, 0, 3);
            RaceDifficulty difficulty = (RaceDifficulty)difficultyIndex;
            float perLap = EstimateFuelPerLapKg(track, difficulty);
            float targetFuelKg = perLap * Mathf.Max(1, raceLaps) + RaceFuelReserveKg;
            float choiceFuelKg = targetFuelKg + FuelLoadChoiceLapDelta(choice) * perLap;
            return Mathf.Clamp(choiceFuelKg, MinimumRaceFuelKg, MaximumRaceFuelKg);
        }

        // Enough for an out lap plus a genuine push lap (this game's live qualifying
        // session is a short single/handful-of-attempt format - see
        // QualifyingSessionLapCap - not a long multi-run practice block), with a
        // small margin so a second push attempt after an aborted lap doesn't strand
        // the car mid-lap.
        public float ComputeQualifyingFuelKg(TrackRuntime track)
        {
            float perLap = EstimateFuelPerLapKg(track, RaceDifficulty.Medium);
            return perLap * 2.5f + 1f;
        }

        // Fixed and deliberately low - Time Trial is a pure lap-time comparison
        // tool, not a race, so it should never be distorted by a fuel strategy
        // choice or by fuel burning down over repeated laps.
        public float ComputeTimeTrialFuelKg()
        {
            return 5f;
        }

        // A practice program is a handful of laps at most (see
        // GameBootstrap.StartCareerPractice/EvaluatePracticeSession) - enough fuel
        // for several laps of real running, never a full race load.
        public float ComputePracticeFuelKg(TrackRuntime track)
        {
            float perLap = EstimateFuelPerLapKg(track, RaceDifficulty.Medium);
            return perLap * 6f + 2f;
        }

        // AI fuel-strategy pass: most of the field runs the same Target plan a
        // sensible player would; a genuinely aggressive driver occasionally takes
        // the pace gain of underfuelling, a cautious one occasionally plays it
        // safe. Deliberately conservative odds - "do not make AI fuel-out common"
        // is explicit in the design ask, so AggressiveUnderfuel (the only choice
        // that can plausibly starve a car) is rare and never stacks with an
        // already-aggressive roll.
        public FuelLoadChoice ResolveAiFuelChoice(DriverData driver)
        {
            int aggression = driver == null ? 75 : driver.aggression;
            int consistency = driver == null ? 80 : driver.consistency;
            float roll = Random.value;
            if (aggression >= 90 && roll < 0.10f)
            {
                return FuelLoadChoice.AggressiveUnderfuel;
            }

            if (aggression >= 78 && roll < 0.30f)
            {
                return FuelLoadChoice.LightUnderfuel;
            }

            if (consistency <= 55 && roll < 0.22f)
            {
                return FuelLoadChoice.Safe;
            }

            return FuelLoadChoice.Target;
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
            // Pit-strategy variety fix: every AI with similar tyre management used to
            // converge on nearly the same recommended lap, since managementShift is a
            // smooth function of one stat - a whole midfield pitting within a lap or
            // two of each other, with no room for a natural undercut/overcut. A small,
            // per-driver-stable offset (hashed from driverId, so it's the same every
            // time this is called for a given driver this race, never re-rolled)
            // spreads real strategy windows out without touching the tyre-wear-driven
            // triggers that still make the ACTUAL stop lap adaptive.
            float driverJitter = participant == null || string.IsNullOrEmpty(participant.driverId) ? 0f
                : (StableUnitInterval(participant.driverId) - 0.5f) * 0.1f;
            int recommended = Mathf.RoundToInt(RaceLaps * (baseWindow + managementShift + driverJitter));
            return Mathf.Clamp(recommended, 1, maxPitLap);
        }

        // Off-by-one fix: RecommendedPitLap returns a 1-based DISPLAY lap number
        // ("pit on lap 3") - CompletedLaps only reaches that number once lap 3 has
        // already been fully driven (i.e. the car is already on lap 4), so
        // comparing raw CompletedLaps against it fires a whole lap late. The
        // player's own auto-pit path already made this exact correction
        // (UpdatePlayerAutoPitStrategy's currentLapNumber = completedLaps + 1);
        // this is the single shared version AiVehicleController now calls instead
        // of re-deriving (and previously getting wrong) the same comparison.
        public bool ShouldAiPitByStrategyLap(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null)
            {
                return false;
            }

            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return false;
            }

            if (participant.pitStops > 0 || participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return false;
            }

            int targetLap = RecommendedPitLap(participant);
            int currentLapNumber = participant.lapTracker.CompletedLaps + 1;

            return currentLapNumber >= targetLap;
        }

        // Deterministic, race-independent value in the 0-1 range derived from a
        // string - used to
        // give each driver a small, stable personality offset (pit-window jitter,
        // etc.) without a persistent per-driver RNG state and without ever changing
        // between calls for the same driver within a race.
        static float StableUnitInterval(string key)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < key.Length; i++)
                {
                    hash = hash * 31 + key[i];
                }

                return (hash & 0x7fffffff) / (float)int.MaxValue;
            }
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

        // Dynamic track evolution: session-wide grip gradually rises as rubber goes
        // down over green-flag running (the real "the track is coming in" effect),
        // instead of every lap of a race or qualifying session running on
        // identical grip regardless of how many laps have already been driven.
        // Heavy rain washes the rubber back off, since a soaked track keeps none
        // of the built-up line. A single TrackRuntime.RubberLevel (0-1) feeds a
        // small multiplicative bonus applied identically to every car via
        // VehicleController.SetTrackGripMultiplier (read inside
        // TyreState.GripMultiplier), plus a subtle darkening tween on the shared
        // road material for visual feedback.
        const float TrackEvolutionMaxGripBonus = 0.05f;

        void UpdateTrackEvolution()
        {
            if (Track == null || Settings == null || !Settings.Current.trackEvolutionEnabled || IsTimeTrial)
            {
                return;
            }

            bool washedByRain = Track.weather == WeatherState.HeavyRain;
            float target = washedByRain ? 0f : 1f;
            float rampSpeed = washedByRain ? 0.3f : 0.012f;
            Track.RubberLevel = Mathf.MoveTowards(Track.RubberLevel, target, Time.deltaTime * rampSpeed);

            float gripMultiplier = 1f + Track.RubberLevel * TrackEvolutionMaxGripBonus;
            for (int i = 0; i < Participants.Count; i++)
            {
                if (Participants[i] != null && Participants[i].vehicle != null)
                {
                    Participants[i].vehicle.SetTrackGripMultiplier(gripMultiplier);
                }
            }

            Track.ApplyRubberEvolutionVisual(Track.RubberLevel);

            if (!trackEvolutionHalfwayMessageSent && !washedByRain && Track.RubberLevel > 0.5f && CurrentSession != RaceWeekendSession.Qualifying)
            {
                trackEvolutionHalfwayMessageSent = true;
                PostEngineerMessage("Track's rubbering in nicely, you should find a bit more grip now.", false);
            }
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
            redFlagCooldownTimer = Mathf.Max(0f, redFlagCooldownTimer - Time.deltaTime);

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
                bool offTrackNow = Mathf.Abs(progress.lateralDistance) > Track.HalfWidthAt(progress.distance) + 1.5f;
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
                bool blockingLine = Mathf.Abs(progress.lateralDistance) <= Track.HalfWidthAt(progress.distance) * 0.85f;
                // Wall/barrier-stuck fix: a car that stalls PINNED AGAINST THE
                // BARRIER sits right at (or just past) the true track edge, which
                // blockingLine's 85%-of-halfwidth test was deliberately designed to
                // exclude - fine for a car parked deep in a wide, harmless runoff,
                // but a car wedged against the wall is a very different case: with
                // barriers now flush to the paved edge (see TrackManager's
                // FlushBarrierLateral), that's still the outer edge of every other
                // car's racing line through the corner, not a safe verge. Only used
                // to widen the ActuallyStranded hazard check below - the collision-
                // severity thresholds above are untouched.
                bool pinnedAtEdge = Mathf.Abs(progress.lateralDistance) >= Track.HalfWidthAt(progress.distance) * 0.92f;

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
                    // Part 4 retune: raised again (was 36/130 and 20/70) - yellows
                    // were still reading as too common, and this collision
                    // classification is the single biggest source of them (Major
                    // always flags with no roll at all). A normal wheel-to-wheel
                    // rub, a small spin, or a wall brush needs to stay Minor.
                    // Part 5 retune (~50% further cut): raised again (was 28/85 and
                    // 46/150) - a tiny wall brush, a quick spin that catches itself,
                    // or ordinary wheel-to-wheel contact must never reach even Medium,
                    // let alone Major. Only a genuinely hard, damaging shunt now
                    // qualifies.
                    IncidentSeverity severity;
                    if (damageJump > 58f || speedDrop > 185f)
                    {
                        severity = IncidentSeverity.Major;
                    }
                    else if (damageJump > 38f || speedDrop > 110f || (speedSignal && damageSignal))
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
                    // Incident-cleanup classification only (never read by any
                    // escalation decision above) - another car within a tight
                    // radius at the moment of detection reads as car-to-car
                    // contact; anything else (a solo spin, a wall/barrier
                    // brush, a kerb strike) is a solo contact.
                    bool carNearby = FindCarAhead(participant, 7f) != null || FindCarBehind(participant, 7f) != null;
                    if (carNearby)
                    {
                        CarContactIncidentCount++;
                    }
                    else
                    {
                        SoloContactIncidentCount++;
                    }

                    string collisionCause = (carNearby ? "Car contact" : "Solo contact/wall") + " (speedDrop=" + speedDrop.ToString("0") + " damageJump=" + damageJump.ToString("0.0") + ")";
                    RegisterIncident(participant, severity, progress, freqScale, escalationAllowed, collisionCause, false, collisionYellowJustified);
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
                // Wall/barrier-stuck fix: a car pinned against the barrier (see
                // pinnedAtEdge above) is just as much a hazard as one sitting on the
                // racing line - this is exactly the "player crashes into a wall and
                // gets stuck" case, previously undervalued as Minor (no yellow) purely
                // because it sits outside the racing-line band, never because of who
                // (or what) is driving the car.
                bool strandedIsHazard = blockingLine || pinnedAtEdge;
                if (newRecoveryState == CarRecoveryState.ActuallyStranded)
                {
                    if (participant.stoppedOnTrackTimer > StrandedRetireSeconds)
                    {
                        RetireParticipant(participant, "Stranded");
                    }

                    string strandedCause = pinnedAtEdge && !blockingLine ? "Stuck against barrier/wall" : "Stopped/stranded";
                    RegisterIncident(participant, strandedIsHazard ? IncidentSeverity.Medium : IncidentSeverity.Minor, progress, freqScale, escalationAllowed, strandedCause, false, strandedIsHazard);
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
            // Part 4 retune: tightened from 6s/40m - with ~20 cars on track,
            // two genuinely unrelated minor incidents landing within a loose
            // 6s/40m window purely by chance was common enough to keep
            // manufacturing guaranteed-yellow Major escalations out of two
            // ordinary scrapes that never actually interacted.
            bool pileup = (RaceElapsed - lastIncidentTime) < 4f && Mathf.Abs(Track.WrapDistance(progress.distance - lastIncidentDistance)) < 25f;
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

            // Red flag: considered on genuinely catastrophic incidents only
            // (a forced escalation - a destroyed car blocking the line - or a
            // Major incident that actually justified its own yellow, i.e. is
            // really blocking/dangerous rather than just logged for stats).
            // Deliberately checked before ApplyIncidentSeverity's own SC/VSC
            // rolls below so a red-flag-worthy incident is never also
            // double-escalated into a safety car in the same call.
            if (escalationAllowed && (forceEscalate || (severity == IncidentSeverity.Major && yellowJustified)))
            {
                ConsiderRedFlag(participant, forceEscalate, freqScale, progress.distance);
                if (CurrentRaceControlState == RaceControlState.RedFlagged)
                {
                    return;
                }
            }

            ApplyIncidentSeverity(participant, severity, progress, freqScale, escalationAllowed, cause, forceEscalate, yellowJustified);
        }

        // Extremely rare by design - red flags must read as a genuine outlier,
        // never "a slightly worse safety car". Two paths in, both deliberately
        // stingy:
        // - RedFlagMultiCarThreshold or more DISTINCT cars catastrophically
        //   down within a short rolling window AND genuinely clustered at the
        //   same point on track - a real pileup that has actually blocked the
        //   track, not two unrelated incidents on opposite sides of the lap.
        // - A single forced-escalation incident (today, only a destroyed car
        //   blocking the line) on its own very rarely rolls into a red flag
        //   instead of just a safety car - a small fraction of the already-
        //   stingy Major-incident SC chance, so it stays a true outlier.
        // A strict cooldown (RedFlagCooldownSeconds) additionally blocks this
        // whole method for a long time after any red flag, so the field can
        // never see more than one or two red flags in a session, and safety
        // cars remain meaningfully more common than red flags.
        void ConsiderRedFlag(RaceParticipant participant, bool forceEscalateCause, float freqScale, float incidentDistance)
        {
            if (participant == null || CurrentRaceControlState == RaceControlState.RedFlagged)
            {
                return;
            }

            if (redFlagCooldownTimer > 0f)
            {
                return;
            }

            for (int i = recentCatastrophicIncidentTimes.Count - 1; i >= 0; i--)
            {
                if (RaceElapsed - recentCatastrophicIncidentTimes[i] > CatastrophicIncidentWindowSeconds)
                {
                    recentCatastrophicIncidentTimes.RemoveAt(i);
                    recentCatastrophicIncidents.RemoveAt(i);
                    recentCatastrophicIncidentDistances.RemoveAt(i);
                }
            }

            if (!recentCatastrophicIncidents.Contains(participant))
            {
                recentCatastrophicIncidents.Add(participant);
                recentCatastrophicIncidentTimes.Add(RaceElapsed);
                recentCatastrophicIncidentDistances.Add(incidentDistance);
            }

            // Only count incidents genuinely bunched at the same spot on track -
            // a real pileup, not scattered incidents that merely happened
            // within the same rolling time window.
            List<RaceParticipant> clusteredDrivers = new List<RaceParticipant>();
            for (int i = 0; i < recentCatastrophicIncidentDistances.Count; i++)
            {
                float separation = Track == null ? 0f : Mathf.Abs(Track.WrapDistance(recentCatastrophicIncidentDistances[i] - incidentDistance));
                if (separation <= CatastrophicIncidentClusterRadiusMeters)
                {
                    clusteredDrivers.Add(recentCatastrophicIncidents[i]);
                }
            }

            if (clusteredDrivers.Count >= RedFlagMultiCarThreshold)
            {
                BeginRedFlag("Huge multi-car pileup - track completely blocked", clusteredDrivers);
                return;
            }

            if (forceEscalateCause)
            {
                float roll = Random.value;
                // Cut to roughly a fifth of the previous chance (was 0.05) -
                // a red flag off a single incident must stay a genuine
                // rarity, well over the required 75% reduction.
                float chance = Mathf.Clamp01(0.01f * freqScale);
                GameLog.Info("[RaceControl] Red flag consideration (single catastrophic incident): roll=" + roll.ToString("0.000") + " chance=" + chance.ToString("0.000"));
                if (roll < chance)
                {
                    BeginRedFlag("Catastrophic accident - car destroyed and blocking the racing line", new List<RaceParticipant> { participant });
                }
            }
        }

        void BeginRedFlag(string reason, List<RaceParticipant> involvedDrivers)
        {
            // A red flag must mean the accident was serious enough that
            // whoever caused it is out of the race - never a driver who just
            // carries on after the restart as if nothing happened.
            string involvedNames = "";
            int sector = 0;
            if (involvedDrivers != null)
            {
                for (int i = 0; i < involvedDrivers.Count; i++)
                {
                    RaceParticipant involved = involvedDrivers[i];
                    if (involved == null)
                    {
                        continue;
                    }

                    if (sector == 0 && State != null)
                    {
                        sector = State.GetCurrentProgress(involved).sector;
                    }

                    involvedNames += (involvedNames.Length > 0 ? (i == involvedDrivers.Count - 1 ? " and " : ", ") : "") + involved.driverName;
                    RetireParticipant(involved, "Red flag - race-ending accident");
                }
            }

            string locationText = sector > 0 ? " in Sector " + sector : "";
            string fullReason = string.IsNullOrEmpty(involvedNames) ? reason : reason + " - " + involvedNames + locationText;

            CurrentRaceControlState = RaceControlState.RedFlagged;
            RedFlagCount++;
            RedFlagReason = fullReason;
            // Simple, fixed procedure: hold for exactly 5 seconds while every
            // car is neutralized in place, then the field is teleported back
            // to a grid built from the running order frozen right now (see
            // below) - never a random long "repair window" and never the
            // original race-start grid.
            redFlagTimer = RedFlagHoldSeconds;
            IsOvertakingAllowed = false;
            IsPitLaneOpen = true;
            recentCatastrophicIncidents.Clear();
            recentCatastrophicIncidentTimes.Clear();
            recentCatastrophicIncidentDistances.Clear();
            // Arms the strict cooldown immediately (not on clear) so back-to-
            // back catastrophic incidents in the same session still can't
            // chain into a second red flag before this one has even resolved.
            redFlagCooldownTimer = RedFlagCooldownSeconds;
            redFlagGridTeleportDone = false;

            // Freeze the running order at the exact moment of the flag - the
            // authoritative source for the post-red-flag grid (redFlagGridOrder,
            // used verbatim by TeleportFieldToRedFlagGrid - never re-sorted or
            // randomized) and a plain read-only snapshot for the post-race
            // report/race-control history.
            redFlagRunningOrderSnapshot.Clear();
            redFlagGridOrder.Clear();
            List<RaceParticipant> order = GetRunningOrderSnapshot();
            for (int i = 0; i < order.Count; i++)
            {
                RaceParticipant queued = order[i];
                if (queued == null)
                {
                    continue;
                }

                queued.safetyCarQueueIndex = i;
                queued.preSafetyCarOrderIndex = i;
                redFlagRunningOrderSnapshot.Add((i + 1) + ". " + queued.driverName);
                if (!queued.retired && !queued.finished)
                {
                    queued.isRaceControlAutopilot = true;
                    redFlagGridOrder.Add(queued);
                }
            }

            GameLog.Warn("[RaceControl] RED FLAG: " + fullReason + " (deployment #" + RedFlagCount + ")");
            LogRaceControlHistory("RED FLAG", fullReason);
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Red flag, red flag! Race suspended - " + fullReason + ".", true, RaceAudioCue.RedFlag);
                if (!string.IsNullOrEmpty(involvedNames))
                {
                    PostEngineerMessage(involvedNames + " will not be continuing - car retired on the spot.", true);
                }

                PostEngineerMessage("Hold position and bring the car to a safe stop. Restart in 5 seconds.", true);
            }
        }

        // Race-start yellow-flag dampener: the opening-lap scramble (grid still
        // bunched, cars still finding their braking points) produces far more
        // incidents than mid-race running, which read as "there's a yellow flag
        // basically every start" and disrupted the starting procedure itself.
        // Round 2: a 75% cut still let one in four opening-lap incidents through,
        // which was still enough to interrupt starts regularly - now fully
        // suppressed (100%) for the whole window, and the window itself widened
        // (30s -> 45s) to actually cover a full opening lap rather than cutting
        // out right as the pack is still sorting itself out through the first
        // sequence of corners. This never touches the underlying collision/
        // incident detection - a genuinely catastrophic, track-blocking incident
        // (forceEscalate) is still never suppressed, regardless of timing.
        const float RaceStartYellowGraceSeconds = 45f;
        const float RaceStartYellowSuppressionChance = 1f;

        void ApplyIncidentSeverity(RaceParticipant participant, IncidentSeverity severity, TrackProgress progress, float freqScale, bool escalationAllowed, string cause = null, bool forceEscalate = false, bool yellowJustified = false)
        {
            bool duringRaceStartWindow = CurrentSession != RaceWeekendSession.Qualifying && RaceElapsed < RaceStartYellowGraceSeconds;
            if (duringRaceStartWindow && !forceEscalate && (RaceStartYellowSuppressionChance >= 1f || Random.value < RaceStartYellowSuppressionChance))
            {
                GameLog.Info("[RaceControl] " + severity + " incident yellow/escalation suppressed by the race-start grace window (" + RaceElapsed.ToString("0.0") + "s in).");
                return;
            }

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
                TriggerYellowSector(progress.sector, participant, cause);
            }
            else if (yellowJustified)
            {
                if (RaceElapsed >= globalMinorYellowCooldownUntil)
                {
                    TriggerYellowSector(progress.sector, participant, cause);
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
                BeginSafetyCarDeployment(participant, progress.sector);
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
                        BeginVirtualSafetyCar(participant, progress.sector);
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
                BeginSafetyCarDeployment(participant, progress.sector);
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
                    BeginVirtualSafetyCar(participant, progress.sector);
                }
            }
        }

        int yellowSectorNumber = -1;

        void TriggerYellowSector(int sector, RaceParticipant involved = null, string cause = null)
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

                // Part 4 retune: a per-sector cooldown alone let a fresh yellow
                // fire in sector 2 the instant sector 1's own cooldown was
                // still running, reading as one continuous stream of flags
                // across the lap even though any one sector was individually
                // "cooling down". This global gate blocks any BRAND NEW yellow
                // (in any sector) for a while after the last one, without
                // touching an already-active episode's own ability to refresh
                // in its own sector (the sameActiveSector branch above).
                if (RaceElapsed < globalYellowFlagCooldownUntil)
                {
                    GameLog.Info("[RaceControl] Yellow flag suppressed - global cooldown active (" +
                        (globalYellowFlagCooldownUntil - RaceElapsed).ToString("0.0") + "s remaining).");
                    return;
                }

                yellowSectorEpisodeStartTime = RaceElapsed;
            }

            globalYellowFlagCooldownUntil = RaceElapsed + GlobalYellowFlagCooldownSeconds;

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
            // Radio clarity: declares WHO triggered the flag, roughly WHERE
            // (sector), and now WHY (RadioCausePhraseFor) instead of a bare
            // "yellow flag, sector N" or a generic "has gone off" regardless of
            // what actually happened - matches what a real broadcast/race-
            // engineer callout would say. The player is addressed directly
            // ("your car") rather than by name, same convention every other
            // player-facing race-control message in this file already uses.
            string involvedText = involved != null
                ? " - " + (involved.isPlayer ? "your car" : involved.driverName) + " " + RadioCausePhraseFor(cause)
                : "";
            if (freshFlag)
            {
                GameLog.Info("[RaceControl] Yellow flag, sector " + sector + involvedText + ".");
                LogRaceControlHistory("YELLOW FLAG", "Sector " + sector + involvedText);
                if (Settings != null && Settings.Current.raceControlMessages)
                {
                    PostEngineerMessage("Yellow flag, sector " + sector + involvedText + ".", false, RaceAudioCue.Yellow);
                }
            }
        }

        // Turns RegisterIncident's internal, diagnostic cause string (which can
        // include raw numbers, e.g. "Collision (speedDrop=45 damageJump=12.3)")
        // into a short, spoken-radio-appropriate phrase. Falls back to a generic
        // phrase for anything unrecognized rather than ever reading the raw
        // diagnostic text over the radio.
        static string RadioCausePhraseFor(string cause)
        {
            if (string.IsNullOrEmpty(cause))
            {
                return "in trouble";
            }

            if (cause.StartsWith("Stuck against barrier"))
            {
                return "stuck against the barrier";
            }

            if (cause.StartsWith("Stopped/stranded"))
            {
                return "stranded";
            }

            if (cause.StartsWith("Wrong way"))
            {
                return "facing the wrong way";
            }

            if (cause.StartsWith("Destroyed"))
            {
                return "in a heavy crash";
            }

            if (cause.StartsWith("Severe damage"))
            {
                return "stopped with severe damage";
            }

            if (cause.StartsWith("Mechanical failure"))
            {
                return "stopped with a mechanical failure";
            }

            if (cause.StartsWith("Collision") || cause.StartsWith("Car contact") || cause.StartsWith("Solo contact"))
            {
                return "involved in an incident";
            }

            return "in trouble";
        }

        void BeginVirtualSafetyCar(RaceParticipant involved = null, int sector = 0)
        {
            CurrentRaceControlState = RaceControlState.VirtualSafetyCar;
            safetyCarTimer = Random.Range(14f, 24f);
            IsOvertakingAllowed = false;
            playerScPitPromptSent = false;
            // Radio clarity: declares who caused it and where instead of a
            // bare "virtual safety car deployed".
            string cause = involved != null ? " - " + involved.driverName + (sector > 0 ? " in trouble at Sector " + sector : " in trouble") : "";
            GameLog.Info("[RaceControl] Virtual safety car deployed. duration=" + safetyCarTimer.ToString("0.0") + "s" + cause);
            LogRaceControlHistory("VSC", "Virtual safety car deployed" + cause);
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Virtual safety car deployed" + cause + ".", true, RaceAudioCue.Vsc);
            }
        }

        void BeginSafetyCarDeployment(RaceParticipant involved = null, int sector = 0)
        {
            CurrentRaceControlState = RaceControlState.SafetyCarDeploying;
            safetyCarTimer = Random.Range(6f, 10f);
            IsOvertakingAllowed = false;
            SafetyCarDeploymentCount++;
            playerScPitPromptSent = false;
            playerScQueueWarningSent = false;
            // Radio clarity: declares who caused it and where instead of a
            // bare "safety car deployed".
            string safetyCarCause = involved != null ? " - " + involved.driverName + (sector > 0 ? " has crashed at Sector " + sector : " has crashed") : "";
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
            LogRaceControlHistory("SAFETY CAR", "Deployment #" + SafetyCarDeploymentCount);
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Safety car deployed.", true, RaceAudioCue.SafetyCar);
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
            // Keeps the shared virtual convoy reference (position + speed every
            // queued car steers/paces against) honest every frame, whether the
            // real safety car object is still physically on track or has already
            // peeled into the pits during the Restart/green-flag ramp hold.
            UpdateRaceControlReference();

            // Convoy autopilot upkeep: every non-retired/non-finished car is under
            // race-control autopilot for as long as IsRaceControlAutopilotHoldPeriod
            // holds true - the full physical safety car period, PLUS the Restart
            // hold, PLUS a short ramp once Green begins. Handing control back only
            // once this goes false (rather than the instant Restart begins) is what
            // gives AiVehicleController a stable, non-stale progress reference and a
            // controlled ramp to resync from instead of an instant snap.
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant p = Participants[i];
                if (p == null)
                {
                    continue;
                }

                if (IsRaceControlAutopilotHoldPeriod && !p.retired && !p.finished)
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

        // Single per-frame update of the shared "virtual convoy" reference that
        // BuildRaceControlAutopilotCommand steers/paces every queued car against.
        // While the real safety car is physically active it just mirrors the real
        // car (unchanged behaviour from before); once the car has peeled off
        // toward the pits but the field is still held (Restart, or the short
        // green-flag ramp after), the reference keeps advancing on its own,
        // ramping from the last real convoy speed up toward a brisk restart pace
        // - so the queue never stalls waiting on an object that isn't there
        // anymore, and BuildRaceControlAutopilotCommand never has to fall back to
        // a blind "brake and go straight" command during that window.
        void UpdateRaceControlReference()
        {
            bool scPhysicallyActive = safetyCarController != null && safetyCarController.IsActive;
            if (scPhysicallyActive)
            {
                raceControlReferenceDistance = Track != null
                    ? Track.WrapDistance(safetyCarController.ProgressDistance - RaceControlQueueLeadMeters)
                    : safetyCarController.ProgressDistance - RaceControlQueueLeadMeters;
                raceControlReferenceSpeedKph = safetyCarController.CurrentSpeedKph;
                return;
            }

            if (!IsRaceControlAutopilotHoldPeriod || Track == null)
            {
                return;
            }

            // RedFlagged cars are held by BuildRedFlagHoldCommand (a direct
            // brake-to-stop, not the moving-queue-target model below), so the
            // shared reference has nothing useful to advance toward here - just
            // pin its speed at 0 so the eventual RedFlagged -> Restart handoff
            // (see DriveRaceControlStateMachine) starts its ramp from a genuine
            // standstill instead of whatever stale convoy speed preceded it.
            if (CurrentRaceControlState == RaceControlState.RedFlagged)
            {
                raceControlReferenceSpeedKph = 0f;
                return;
            }

            // Only actually ramp the pace up once race control has called the
            // restart (Restart state) or is in the short Green ramp tail right
            // after it. The SafetyCarInThisLap tail - where the physical car has
            // already peeled off but race control hasn't called "go" yet - holds
            // the last known convoy pace flat instead of creeping toward restart
            // speed early.
            float targetSpeedKph = raceControlReferenceSpeedKph;
            if (CurrentRaceControlState == RaceControlState.Restart)
            {
                float rampProgress = 1f - Mathf.Clamp01(restartControlTimer / 4f);
                targetSpeedKph = Mathf.Lerp(raceControlReferenceSpeedKph, RestartFormationTargetSpeedKph, rampProgress);
            }
            else if (CurrentRaceControlState == RaceControlState.Green && restartRampTimer > 0f)
            {
                float rampProgress = 1f - Mathf.Clamp01(restartRampTimer / RestartRampDurationSeconds);
                targetSpeedKph = Mathf.Lerp(raceControlReferenceSpeedKph, RestartFormationTargetSpeedKph, rampProgress);
            }

            raceControlReferenceSpeedKph = Mathf.MoveTowards(raceControlReferenceSpeedKph, targetSpeedKph, Time.deltaTime * 30f);
            raceControlReferenceDistance = Track.WrapDistance(raceControlReferenceDistance + raceControlReferenceSpeedKph / 3.6f * Time.deltaTime);
        }

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
        // slot in the queue - the leader targets a fixed distance behind the
        // shared race-control reference point, everyone else targets a further
        // fixed distance behind the leader's own slot, scaled by their frozen
        // queue index. Steering mirrors the same lookahead-point pattern
        // AiVehicleController's own normal driving uses (SampleAtDistance ahead
        // of the car's current position, steer toward it in local space) so the
        // convoy still tracks the racing line through corners instead of
        // cutting straight lines between distance samples; only the speed
        // target comes from the queue-slot error, not from the normal apex/
        // braking-point model. The reference point itself (raceControlReferenceDistance/
        // Speed, kept current every frame by UpdateRaceControlReference) mirrors
        // the real safety car while it's physically on track and keeps advancing
        // smoothly on its own through the Restart hold and green-flag ramp once
        // the car has already peeled into the pits - so this command always has
        // a real target to steer/pace toward and never falls back to a blind
        // brake-and-go-straight command while a car is still under autopilot.
        public VehicleCommand BuildRaceControlAutopilotCommand(RaceParticipant participant)
        {
            VehicleCommand command = new VehicleCommand();
            if (participant == null || participant.vehicle == null || Track == null || State == null)
            {
                command.brake = 1f;
                return command;
            }

            if (!IsRaceControlAutopilotHoldPeriod)
            {
                // Shouldn't normally be called outside a hold period - hold
                // speed down gently rather than doing anything erratic.
                command.throttle = 0f;
                command.brake = 0.15f;
                return command;
            }

            // Red flag: every car brakes smoothly to a stop roughly where it is,
            // rather than chasing a moving queue-slot target - deliberately
            // simpler than the safety-car convoy model below (real grid-
            // reformation is out of scope) but still safe: gentle, speed-scaled
            // braking with a light steering correction back toward the
            // centerline so a braking car doesn't wander off-line or into
            // another car's path while it slows.
            if (CurrentRaceControlState == RaceControlState.RedFlagged)
            {
                return BuildRedFlagHoldCommand(participant);
            }

            float scSpeedKph = raceControlReferenceSpeedKph;
            // Gap scales slightly with pace: a faster-moving queue needs a
            // little more following distance per car than a crawling one.
            float gapPerCar = Mathf.Lerp(14f, 22f, Mathf.Clamp01(scSpeedKph / 160f));
            int queueIndex = Mathf.Max(0, participant.safetyCarQueueIndex);
            float targetDistance = Track.WrapDistance(raceControlReferenceDistance - queueIndex * gapPerCar);
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

        // Red flag hold: brake smoothly to a stop in place (not toward a moving
        // queue slot) and hold there, with a light steering correction back
        // toward the centerline. Braking is speed-scaled and capped well short
        // of a full stab so a queue of cars slowing down together doesn't brake-
        // check itself into the very kind of pileup a red flag exists to stop.
        VehicleCommand BuildRedFlagHoldCommand(RaceParticipant participant)
        {
            VehicleCommand command = new VehicleCommand();
            float speedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);
            command.throttle = 0f;
            command.brake = speedKph > 3f ? Mathf.Lerp(0.22f, 0.5f, Mathf.Clamp01(speedKph / 140f)) : 0.35f;

            TrackProgress progress = State.GetCurrentProgress(participant);
            Vector3 point;
            Vector3 forward;
            Vector3 right;
            Track.SampleAtDistance(Track.WrapDistance(progress.distance + 16f), out point, out forward, out right);
            Vector3 toTarget = point - participant.transform.position;
            float localSteer = Vector3.Dot(toTarget.normalized, participant.transform.right);
            command.steer = Mathf.Clamp(localSteer * 1.6f, -1f, 1f);
            return command;
        }

        // The actual "grid reset" step of the red-flag procedure: places every
        // still-running car back onto the starting-grid slots in exactly the
        // running order frozen the instant the flag was thrown (redFlagGridOrder -
        // never the original race-start grid, never re-sorted/randomized here).
        // Uses the same position/rotation math as the initial grid spawn
        // (Track.GetGridSlot + SampleAtDistance) and the same safe teleport
        // primitive pit stops already rely on (VehicleController.SnapToPitPose)
        // so the physics body, transform and internal throttle/brake smoothing
        // all move together with zero residual velocity - no floating, no drift.
        void TeleportFieldToRedFlagGrid()
        {
            if (redFlagGridTeleportDone || Track == null)
            {
                return;
            }

            redFlagGridTeleportDone = true;
            int slot = 0;
            for (int i = 0; i < redFlagGridOrder.Count; i++)
            {
                RaceParticipant participant = redFlagGridOrder[i];
                if (participant == null || participant.retired || participant.finished || participant.vehicle == null)
                {
                    continue;
                }

                float gridDistance;
                float lane;
                Track.GetGridSlot(slot, out gridDistance, out lane);
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Track.SampleAtDistance(gridDistance, out point, out forward, out right);
                Vector3 targetPosition = FindRoadSpawnPosition(point + right * lane, participant.driverName, out bool hitRoad);
                Quaternion targetRotation = Quaternion.LookRotation(forward, Vector3.up);

                // A car mid-pit-stop when the flag fell resumes fresh from its
                // grid slot rather than continuing a pit animation that no
                // longer matches its position.
                if (participant.pitPhase != PitPhase.None)
                {
                    CancelPitSequenceForRedFlag(participant);
                }

                participant.vehicle.SnapToPitPose(targetPosition, targetRotation);
                participant.hasLastSafePosition = true;
                participant.lastSafePosition = targetPosition;
                participant.lastSafeRotation = targetRotation;
                participant.gridPosition = slot + 1;
                participant.safetyCarQueueIndex = slot;
                participant.preSafetyCarOrderIndex = slot;
                participant.stoppedOnTrackTimer = 0f;
                participant.wrongWayTimer = 0f;
                participant.recoveryAttemptCount = 0;
                participant.stuckRepositionCooldown = 0f;

                AiVehicleController ai = participant.GetComponent<AiVehicleController>();
                if (ai != null)
                {
                    ai.ResyncAfterForcedReposition();
                }

                // Re-anchors the lap/checkpoint tracker to the new physical
                // position exactly like an initial grid start does - resets
                // the checkpoint bookkeeping and progress reference so the
                // teleport can never be misread as skipped checkpoints or a
                // phantom lap, while leaving CompletedLaps untouched (the
                // remaining-laps count is never reset by a red flag).
                if (participant.lapTracker != null)
                {
                    participant.lapTracker.ConfigureRaceGridStart(gridDistance);
                }

                if (State != null)
                {
                    State.RefreshTimingSnapshot(participant);
                }

                slot++;
            }

            GameLog.Info("[RaceControl] Field teleported to red-flag restart grid (" + slot + " cars, order preserved from the moment the flag was thrown).");
            LogRaceControlHistory("GRID RESET", "Field repositioned to the running order recorded when the red flag was thrown");
        }

        void CancelPitSequenceForRedFlag(RaceParticipant participant)
        {
            if (participant == null)
            {
                return;
            }

            participant.pitPhase = PitPhase.None;
            participant.isPitting = false;
            participant.hasPitGuideState = false;
            participant.pitLimiterUntilExit = false;
            if (participant.vehicle != null)
            {
                participant.vehicle.SetPitGuidance(false);
                participant.vehicle.SetPitLimiter(false);
            }
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
                    // Green-flag ramp tail: for a short window after a safety-car
                    // restart, cars are still held under race-control autopilot
                    // (see IsRaceControlAutopilotHoldPeriod) even though the state
                    // has already flipped to Green, so the handback to normal
                    // driving is a controlled ramp rather than an instant snap.
                    if (restartRampTimer > 0f)
                    {
                        restartRampTimer -= Time.deltaTime;
                        if (restartRampTimer <= 0f && !restartHandbackMessageSent)
                        {
                            restartHandbackMessageSent = true;
                            if (Settings != null && Settings.Current.raceControlMessages)
                            {
                                PostEngineerMessage("Full control's back with you - send it.", false);
                            }
                        }
                    }
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
                            PostEngineerMessage("VSC ending, green flag.", true, RaceAudioCue.Green);
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
                        RestartFollowsRedFlag = false;
                        restartControlTimer = 4f;
                        GameLog.Info("[RaceControl] Restart imminent.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Green flag imminent, get ready.", true, RaceAudioCue.Green);
                        }
                    }
                    break;

                case RaceControlState.Restart:
                    restartControlTimer -= Time.deltaTime;
                    // Restart countdown lights/audio: the same 5-light build-up
                    // tone sequence the original race start uses (PlayStartLight),
                    // over the final 5 seconds of the hold - a red flag or safety
                    // car restart used to be silent/text-only right up to the
                    // green flag, unlike the original start.
                    if (restartControlTimer <= 5f)
                    {
                        int lightsRemaining = Mathf.Clamp(Mathf.CeilToInt(restartControlTimer), 0, 5);
                        if (lightsRemaining != lastRestartLightCountPlayed)
                        {
                            lastRestartLightCountPlayed = lightsRemaining;
                            if (lightsRemaining > 0)
                            {
                                SimpleAudioManager.PlayStartLight(5 - lightsRemaining);
                            }
                        }
                    }

                    if (restartControlTimer <= 0f)
                    {
                        CurrentRaceControlState = RaceControlState.Green;
                        IsOvertakingAllowed = true;
                        drsRestartCooldownTimer = 45f;
                        postEscalationCooldownTimer = PostEscalationCooldownSeconds;
                        playerScPitPromptSent = false;
                        safetyCarQueueLeader = null;
                        lastRestartLightCountPlayed = -1;
                        SimpleAudioManager.PlayStartLight(5);
                        if (RestartFollowsRedFlag)
                        {
                            // Standing-restart fix: a red-flag restart is a real
                            // standing start, not a rolling one - the moment the
                            // lights go out, control returns immediately and in
                            // full, exactly like the original grid start
                            // (HoldGridCars(false) there does the same thing).
                            // No autopilot ramp: that mechanism exists to bring a
                            // moving safety-car convoy back up to racing pace
                            // together, which doesn't apply to cars launching
                            // individually from a dead stop.
                            HoldGridCars(false);
                            restartRampTimer = 0f;
                            restartHandbackMessageSent = true;
                            LogRaceControlHistory("RACE RESTART", "Standing restart - green flag from the red-flag grid, running order preserved");
                            if (Settings != null && Settings.Current.raceControlMessages)
                            {
                                PostEngineerMessage("Lights out - race restart, go go go!", true, RaceAudioCue.Green);
                            }
                        }
                        else
                        {
                            // Autopilot doesn't let go the instant Green fires - it
                            // keeps holding/pacing the field for a short ramp so every
                            // car accelerates away together instead of the whole
                            // grid snapping to full racing behaviour on the same frame.
                            // restartHandbackMessageSent fires the honest "control's
                            // yours now" message once that ramp actually finishes.
                            restartRampTimer = RestartRampDurationSeconds;
                            restartHandbackMessageSent = false;
                            LogRaceControlHistory("GREEN FLAG", "Restart after safety car");
                            if (Settings != null && Settings.Current.raceControlMessages)
                            {
                                PostEngineerMessage("Green flag - power builds back progressively, hold your line.", true, RaceAudioCue.Green);
                            }
                        }

                        GameLog.Info("[RaceControl] Restart complete, green flag.");
                    }
                    break;

                case RaceControlState.RedFlagged:
                    IsOvertakingAllowed = false;
                    // Pit lane stays open through a red flag (real F1 uses it as
                    // the "return to the pits" option this simplified model
                    // deliberately leans on instead of a full grid-lane
                    // reformation) so a car needing repairs can route there
                    // while the field is held.
                    IsPitLaneOpen = true;
                    redFlagTimer -= Time.deltaTime;
                    if (redFlagTimer <= 0f)
                    {
                        // The actual grid-reset step: every still-running car is
                        // teleported back onto the starting grid in the exact
                        // running order frozen the instant the flag was thrown
                        // (redFlagGridOrder - see BeginRedFlag/TeleportFieldToRedFlagGrid).
                        // Never the original race-start grid, never re-sorted.
                        TeleportFieldToRedFlagGrid();

                        // Standing-restart fix: this state used to hand the field
                        // straight to the safety-car convoy-chase autopilot
                        // (BuildRaceControlAutopilotCommand) for the whole hold,
                        // which drives cars toward a moving queue-slot target - a
                        // rolling-start mechanism, not a standing one, and the
                        // reason a red-flag restart could read as "already
                        // moving" and let cars drift off their exact teleported
                        // slot before the green flag. HoldGridCars(true) uses the
                        // exact same hard physics freeze (VehicleController.
                        // SetGridHold, position/rotation pinned every FixedUpdate)
                        // the original grid start already relies on, so cars are
                        // genuinely stationary - not merely braking toward zero -
                        // for the whole restart hold. Released in the Restart ->
                        // Green transition below, only for a red-flag-originated
                        // restart; the safety-car restart path is untouched.
                        HoldGridCars(true);

                        float gridDistance;
                        float lane;
                        Track.GetGridSlot(0, out gridDistance, out lane);
                        raceControlReferenceDistance = gridDistance;
                        raceControlReferenceSpeedKph = 0f;
                        CurrentRaceControlState = RaceControlState.Restart;
                        RestartFollowsRedFlag = true;
                        restartControlTimer = 5f;
                        GameLog.Info("[RaceControl] Grid reset complete, restart in 5 seconds.");
                        if (Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage("Grid reset based on the running order when the flag came out.", true);
                            PostEngineerMessage("Hold your grid slot - race restart in 5 seconds.", true, RaceAudioCue.Green);
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
                    ? "Safety car deployed. Box now - the field is bunched, this is close to a free stop. Press P to box now or stay out to keep Plan A."
                    : "VSC deployed. Box now - the delta is much smaller than a green-flag stop. Press P to box now or stay out to keep Plan A.";
                PostEngineerMessage(message, true);

                // Interactive pit-window offer: this radio call is now something the
                // player actually answers rather than a passive heads-up - pressing P
                // in the next few seconds accepts the opportunistic stop and overrides
                // the original planned lap; staying silent leaves the plan untouched
                // (see AcceptRaceControlPitOffer/UpdatePlayerRaceControlPitOffer).
                playerHasActiveRaceControlPitOffer = true;
                playerRaceControlPitOfferExpiresAt = Time.time + 10f;
                playerRaceControlPitOfferType = fullSc ? RaceControlPitOfferType.SafetyCar : RaceControlPitOfferType.Vsc;
                playerDeclinedRaceControlPitOfferMessageSent = false;
            }
        }

        // Cancels/expires the VSC/SC pit-window offer once it's no longer valid -
        // race control period ended, offer timed out, the pit lane closed, or the
        // player is already pitting/retired/finished/has an outstanding pit request
        // of their own. Silent on expiry-with-no-input per spec (staying out is a
        // valid, intentional choice, not an error state) beyond a single optional
        // "staying out" acknowledgement.
        void UpdatePlayerRaceControlPitOffer()
        {
            if (!playerHasActiveRaceControlPitOffer)
            {
                return;
            }

            if (PlayerParticipant == null || PlayerParticipant.vehicle == null || PlayerParticipant.lapTracker == null ||
                PlayerParticipant.retired || PlayerParticipant.finished || PlayerParticipant.isPitting ||
                PlayerParticipant.pitPhase != PitPhase.None || PlayerParticipant.vehicle.PitRequested)
            {
                playerHasActiveRaceControlPitOffer = false;
                return;
            }

            bool stillUnderRaceControl = CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                                          CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                                          CurrentRaceControlState == RaceControlState.SafetyCarDeploying;
            bool expired = Time.time >= playerRaceControlPitOfferExpiresAt;
            if (!stillUnderRaceControl || expired || !IsPitLaneOpen)
            {
                playerHasActiveRaceControlPitOffer = false;
                if (expired && stillUnderRaceControl && !playerDeclinedRaceControlPitOfferMessageSent &&
                    Settings != null && Settings.Current.raceControlMessages)
                {
                    playerDeclinedRaceControlPitOfferMessageSent = true;
                    PostEngineerMessage("Okay, staying out. Plan A remains.", false);
                }
            }
        }

        // Player-facing gate PlayerVehicleInput checks before routing a P press to
        // AcceptRaceControlPitOffer instead of the normal pit-request toggle.
        public bool HasActiveRaceControlPitOfferForPlayer { get { return playerHasActiveRaceControlPitOffer; } }

        // Pressing P while a VSC/SC pit-window offer is active: box immediately
        // under the current window, using actual existing fields (vehicle.RequestPit
        // + requestedPitCompound/requestedPitCompoundSet, the same pair
        // UpdatePlayerAutoPitStrategy already uses for an automatic planned stop).
        // No separate "override" fields are needed to replace the original planned
        // lap - NextPlannedPitLapFor already resolves off participant.pitStops, so
        // once this stop completes and pitStops increments, the plan naturally moves
        // on to the next still-pending stop (or stops prompting entirely on a
        // one-stop plan) instead of firing again at the original lap.
        public void AcceptRaceControlPitOffer()
        {
            if (!playerHasActiveRaceControlPitOffer)
            {
                return;
            }

            if (PlayerParticipant == null || PlayerParticipant.vehicle == null)
            {
                return;
            }

            if (PlayerParticipant.missedPitEntryThisLap)
            {
                // Deterministic-deadlock fix: the real physical opening is already
                // closed for this lap - re-requesting now would just re-arm
                // committingToPit while still inside the broad approach zone.
                return;
            }

            RaceControlPitOfferType offerType = playerRaceControlPitOfferType;
            playerHasActiveRaceControlPitOffer = false;

            PlayerParticipant.vehicle.RequestPit();
            PlayerParticipant.pitAutoTriggered = false;
            // Cancellable-manual-pit-stop fix: an accepted SC/VSC radio offer is
            // still a manually-initiated stop, not the pre-race plan - tagged
            // with its own source (rather than reusing Manual) so future
            // messaging/analytics can tell the two apart, but it is cancellable
            // through the exact same window (CanCancelManualPitRequest treats
            // Manual and SafetyCarPrompt identically).
            PlayerParticipant.activePitRequestSource = PitRequestSource.SafetyCarPrompt;
            PlayerParticipant.manualPitRequested = true;
            PlayerParticipant.manualPitCommitted = false;
            if (!PlayerParticipant.requestedPitCompoundSet)
            {
                PlayerParticipant.requestedPitCompound = NextPlannedPitCompoundFor(PlayerParticipant);
                PlayerParticipant.requestedPitCompoundSet = true;
            }

            string offerName = offerType == RaceControlPitOfferType.SafetyCar ? "the safety car" : "the VSC";
            SessionMessage = "Pit request: box under " + offerName;
            GameLog.Info("[Pit] Player accepted race-control pit offer (" + offerType + ") at lap " +
                         (PlayerParticipant.lapTracker != null ? PlayerParticipant.lapTracker.CompletedLaps + 1 : 0) + ".");
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Copy. Box this lap under " + offerName + ". Pit confirm.", true, RaceAudioCue.PitCall);
            }
        }

        // ---------- Cancellable manual pit request ----------
        // Single shared validation for cancelling a manually-queued (P key or
        // accepted SC/VSC offer) pit stop. Every cancellation path - the HUD
        // button and the keyboard shortcut alike - must go through this exact
        // method, so there is never a difference between mouse and keyboard
        // behaviour, and the pre-race planned stop (NextPlannedPitLapFor/
        // GetPlannedPitLapForStop, driven purely by Settings.Current.plannedPitLapOne/
        // Two and pitStops) is never read, mutated, or otherwise touched by any
        // of this - it is a completely separate concept from the temporary
        // manual override this cancels.
        public bool CanCancelManualPitRequest()
        {
            RaceParticipant participant = PlayerParticipant;
            if (participant == null || participant.vehicle == null || !participant.isPlayer)
            {
                return false;
            }

            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial || IsRaceFinished || participant.retired || participant.finished)
            {
                return false;
            }

            // Source/state gate: only a Manual or SafetyCarPrompt request that
            // hasn't yet committed is cancellable - the pre-race plan's own
            // auto-trigger (PitRequestSource.PreRacePlan) is never cancellable
            // through this feature at all.
            if (!participant.manualPitRequested || participant.manualPitCommitted)
            {
                return false;
            }

            if (participant.activePitRequestSource != PitRequestSource.Manual &&
                participant.activePitRequestSource != PitRequestSource.SafetyCarPrompt)
            {
                return false;
            }

            if (!participant.vehicle.PitRequested)
            {
                return false;
            }

            // Phase/lockout gate: not already in the pit-entry sequence, pit
            // lane, box, exit or exit-merge - the project's actual PitPhase
            // values equivalent to Entry/PitLane/Service/Exit/ExitMerge.
            if (participant.pitPhase != PitPhase.None || participant.isPitting || participant.pitEntryCommitted)
            {
                return false;
            }

            // Live commitment-boundary gate: re-check the SAME authoritative
            // boundary HandlePitService itself uses
            // (Track.HasCrossedPitEntryLimiterLine) directly against the car's
            // actual current position, not the cached pitPhase/pitEntryCommitted
            // flags alone - those only update once HandlePitService ticks this
            // frame, so a player who has already physically crossed the line
            // this same frame (before that tick runs) must not be able to
            // cancel and suddenly regain normal racing control inside the
            // pit-entry lane.
            if (Track != null && participant.lapTracker != null)
            {
                TrackProgress liveProgress = Track.GetProgressNear(participant.transform.position, participant.lapTracker.CurrentProgress.distance);
                if (Track.HasCrossedPitEntryLimiterLine(liveProgress))
                {
                    return false;
                }
            }

            return true;
        }

        public void CancelManualPitRequest()
        {
            if (!CanCancelManualPitRequest())
            {
                return;
            }

            RaceParticipant participant = PlayerParticipant;
            participant.vehicle.ClearPitRequest();
            participant.pitTyreSelectionActive = false;
            participant.pitAutoTriggered = false;
            ClearManualPitRequestTracking(participant);

            // A cancelled manual request never touches the pre-race plan
            // (NextPlannedPitLapFor keeps reading Settings.Current.plannedPitLapOne/
            // Two + pitStops exactly as before) - UpdatePlayerAutoPitStrategy
            // simply resumes normal evaluation next tick with vehicle.PitRequested
            // false again. If the planned lap already passed while the manual
            // request was queued, ShouldPromptPlannedStop/NextPlannedPitLapFor
            // still report it due/overdue and UpdatePlayerAutoPitStrategy
            // re-requests it at the next tick - it is never silently dropped.
            SessionMessage = "Manual pit stop cancelled";
            PostEngineerMessage("Copy, staying out. Original strategy restored.", true);
            playerManualPitCancelMessageTimer = PlayerManualPitCancelMessageSeconds;

            GameLog.Info("[Pit] Player cancelled manual pit request at lap " +
                         (participant.lapTracker != null ? participant.lapTracker.CompletedLaps + 1 : 0) + ".");
        }

        // Shared reset for the three fields a cancelled/consumed manual request
        // must always clear together - see the "State separation" contract on
        // RaceParticipant (activePitRequestSource/manualPitRequested/manualPitCommitted).
        static void ClearManualPitRequestTracking(RaceParticipant participant)
        {
            participant.manualPitRequested = false;
            participant.manualPitCommitted = false;
            participant.activePitRequestSource = PitRequestSource.None;
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
                            PostEngineerMessage(mover.driverName + " passed you illegally - race control has given them 5 seconds.", false, RaceAudioCue.Penalty);
                        }
                    }
                }
            }

            TrackPlayerOvertakesCompleted(currentOrder);

            raceControlOrderSnapshot.Clear();
            raceControlOrderSnapshot.AddRange(currentOrder);
            restrictionActiveAtLastSnapshot = restrictionActiveNow;
        }

        // Overtakes-made fix: RaceParticipant.overtakesCompleted was only ever
        // incremented by ReportAiOvertakeCompleted, which AiVehicleController
        // calls from its own AttackingInside/AttackingOutside/SideBySide ->
        // CompletingPass state transition - a transition the player, driven by
        // PlayerVehicleInput, never runs through. That left the post-race
        // report's "Overtakes made" line hardcoded at 0 for a human-driven car
        // no matter how many cars it actually passed. This reuses the exact
        // same clean-pass definition CheckIllegalOvertakesUnderYellow already
        // established just above (two running-order snapshots ~0.5s apart,
        // IsPositionCorrectionAllowed excluding pit/retired/recovering/crawling
        // cars) so "genuine overtake" means the same thing here as it does for
        // the illegal-overtake penalty - just applied unconditionally (not
        // gated behind a yellow/VSC/SC restriction, since most real overtakes
        // happen under green) and only for the player, since AI already has
        // its own precise, independent counter and running this for AI too
        // would double-count against it.
        void TrackPlayerOvertakesCompleted(List<RaceParticipant> currentOrder)
        {
            if (raceControlOrderSnapshot.Count <= 1)
            {
                return;
            }

            RaceParticipant player = null;
            int playerCurrentIndex = -1;
            for (int i = 0; i < currentOrder.Count; i++)
            {
                if (currentOrder[i].isPlayer)
                {
                    player = currentOrder[i];
                    playerCurrentIndex = i;
                    break;
                }
            }

            if (player == null)
            {
                return;
            }

            int playerPreviousIndex = raceControlOrderSnapshot.IndexOf(player);
            if (playerPreviousIndex <= 0)
            {
                return;
            }

            for (int d = 0; d < playerPreviousIndex; d++)
            {
                RaceParticipant passed = raceControlOrderSnapshot[d];
                if (passed == null)
                {
                    continue;
                }

                int passedCurrentIndex = currentOrder.IndexOf(passed);
                if (passedCurrentIndex < 0 || passedCurrentIndex <= playerCurrentIndex)
                {
                    continue;
                }

                if (IsPositionCorrectionAllowed(player, passed))
                {
                    continue;
                }

                player.overtakesCompleted++;
            }
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

        // Smarter AI strategy: undercut awareness. NextPitCompound/RecommendedPitLap
        // already give every AI a stable target window, but until now nothing ever
        // reacted to who was actually around it on track - every car pitted right at
        // its own jittered lap regardless of a car directly ahead offering a live
        // undercut. This lets an AI still on its first stint pit up to 2 laps EARLY
        // (inside its own recommended window, never before it opens) specifically to
        // undercut a car it's closely following that hasn't stopped yet - the same
        // real-world tactic RaceHud already narrates to the player via the "undercut
        // is live" engineer line.
        public bool ShouldAiPitForUndercut(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.vehicle.Tyres == null || participant.lapTracker == null ||
                CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return false;
            }

            if (participant.pitStops != 0 || participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return false;
            }

            int completedLaps = participant.lapTracker.CompletedLaps;
            int recommendedLap = RecommendedPitLap(participant);
            // Only inside the last 2 laps of this car's OWN window, never before it
            // opens - this is a tactical nudge to jump a rival, not a way to skip the
            // tyre-life planning RecommendedPitLap already does.
            if (completedLaps < recommendedLap - 2 || completedLaps >= recommendedLap)
            {
                return false;
            }

            // Only worth undercutting off tyres that are already carrying real wear -
            // a car on a very fresh set has nothing to gain from stopping early.
            if (participant.vehicle.Tyres.Wear > 0.68f)
            {
                return false;
            }

            RaceParticipant ahead = FindCarAhead(participant, 40f);
            if (ahead == null || ahead.pitStops != 0 || ahead.retired || ahead.finished)
            {
                return false;
            }

            float gapSeconds = GetIntervalToAheadSeconds(participant);
            return gapSeconds > 0f && gapSeconds < 2.2f;
        }

        // Player-facing counterpart for a parallel HUD pass: identical logic, named
        // for what it means from the player's seat rather than the AI's.
        public bool RecommendedPitUnderSafetyCar(RaceParticipant participant)
        {
            return ShouldAiPitUnderSafetyCar(participant);
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

        // Recommendation reason (#67): short, additive clause naming whichever of
        // tyre wear / rain crossover is genuinely true right now, appended onto
        // the "Box this lap" call alongside the mandatory-rule/undercut reasons
        // already built into that message. Returns "" when neither applies -
        // the base message already stands on its own without this.
        string PitRecommendationReasonClause(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.vehicle.Tyres == null)
            {
                return "";
            }

            bool tyresWorn = participant.vehicle.Tyres.WearPercent > 65f;
            WeatherState currentWeather = Track == null ? WeatherState.Clear : Track.weather;
            bool wetNow = currentWeather == WeatherState.LightRain || currentWeather == WeatherState.HeavyRain;
            TyreCompound currentCompound = participant.vehicle.Tyres.Compound;
            bool onWetTyre = currentCompound == TyreCompound.Intermediate || currentCompound == TyreCompound.Wet;
            bool weatherMismatch = wetNow != onWetTyre;

            if (weatherMismatch)
            {
                return wetNow ? " Track's too wet for these tyres." : " Track's drying out - these won't last.";
            }

            if (tyresWorn)
            {
                return " Tyre wear is high.";
            }

            return "";
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

        // Radio message stacking fix: every message that fires becomes its own
        // independently-timed stack entry (newest inserted at index 0, so
        // RaceHud's newest-on-top rendering just walks the list in order)
        // instead of one shared "now showing" slot with everything else stuck
        // waiting behind it. Settings.raceControlMessages / engineerMessageVerbosity
        // can still mute all of this without touching a single call site.
        void PostEngineerMessage(string message, bool priority)
        {
            PostEngineerMessage(message, priority, RaceAudioCue.None);
        }

        // cue plays a short race-control stinger alongside the text (throttled
        // by SimpleAudioManager's own cooldown, so a burst of several messages
        // in one frame - e.g. safety-car deployment - never stacks several
        // sounds on top of each other). Only the moments that actually warrant
        // an audio cue pass one; everything else keeps using the 2-arg
        // overload above and stays silent.
        void PostEngineerMessage(string message, bool priority, RaceAudioCue cue)
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
            for (int i = 0; i < activeEngineerMessages.Count; i++)
            {
                if (activeEngineerMessages[i].text == formatted)
                {
                    return;
                }
            }

            // Minimal verbosity: only priority (urgent) lines get through at all.
            if (!priority && Settings != null && Settings.Current.engineerMessageVerbosity == 1)
            {
                return;
            }

            // Only reached once the message has cleared every suppression check
            // above, so the cue and the text it accompanies always agree.
            SimpleAudioManager.PlayRaceControlCue(cue);

            if (activeEngineerMessages.Count >= MaxActiveEngineerMessages)
            {
                // Make room rather than silently dropping the new (just
                // triggered, presumably relevant right now) message. Only
                // ever evicts a non-priority entry - the one closest to
                // expiring anyway - so a still-fresh safety-car/pit/damage
                // call is never bumped by routine flavor chatter. If every
                // active slot happens to be priority, a new priority message
                // may still evict the priority entry closest to expiring;
                // a new non-priority message just waits for natural expiry.
                int evictIndex = -1;
                for (int i = 0; i < activeEngineerMessages.Count; i++)
                {
                    if (activeEngineerMessages[i].priority)
                    {
                        continue;
                    }

                    if (evictIndex < 0 || activeEngineerMessages[i].remaining < activeEngineerMessages[evictIndex].remaining)
                    {
                        evictIndex = i;
                    }
                }

                if (evictIndex < 0 && priority)
                {
                    for (int i = 0; i < activeEngineerMessages.Count; i++)
                    {
                        if (evictIndex < 0 || activeEngineerMessages[i].remaining < activeEngineerMessages[evictIndex].remaining)
                        {
                            evictIndex = i;
                        }
                    }
                }

                if (evictIndex >= 0)
                {
                    activeEngineerMessages.RemoveAt(evictIndex);
                }
                else
                {
                    return;
                }
            }

            // Radio priority pass: a red flag is the single most critical thing
            // race control can say - it gets noticeably longer screen time than
            // an ordinary priority message (which itself already outlasts and
            // out-survives-eviction over routine flavor chatter, see the
            // eviction loop above), instead of sharing the same flat duration
            // as "box this lap for softs".
            float duration = cue == RaceAudioCue.RedFlag ? PriorityEngineerMessageDuration * 1.6f
                : (priority ? PriorityEngineerMessageDuration : RoutineEngineerMessageDuration);
            activeEngineerMessages.Insert(0, new EngineerMessageEntry { text = formatted, remaining = duration, age = 0f, priority = priority });
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

        // Slipstream: automatic, physics-based tow from running behind another car
        // on a straight - distinct from DRS (button/AI-commanded, race-eligibility
        // gated, much bigger effect). Computed for every participant every frame
        // and pushed straight into VehicleController.SetSlipstream, which smooths
        // and applies it identically for the player and every AI car - nothing
        // here is player-only.
        const float SlipstreamMinDistance = 8f;
        // Speed-rebalance pass: straights are now ~25% longer, giving a following
        // car more real room to build/hold a tow before the braking zone - nudged
        // up from 85f rather than left to feel weak on the longer straights.
        // Round 2: stacked another 25% (95f -> 119f) to match the further track
        // length increase.
        const float SlipstreamMaxDistance = 119f;
        const float SlipstreamFullLateralWidth = 3.5f;
        const float SlipstreamMaxLateralWidth = 7.5f;
        const float SlipstreamMinSpeedKph = 130f;

        void UpdateSlipstreamEffects()
        {
            if (Track == null || Participants == null)
            {
                return;
            }

            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (!IsSlipstreamEligible(participant) || Mathf.Abs(participant.vehicle.CurrentSpeedKph) < SlipstreamMinSpeedKph)
                {
                    if (participant != null && participant.vehicle != null)
                    {
                        participant.vehicle.SetSlipstream(0f, "");
                    }

                    continue;
                }

                TrackProgress followerProgress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
                float bestStrength = 0f;
                string bestSource = "";

                for (int j = 0; j < Participants.Count; j++)
                {
                    RaceParticipant other = Participants[j];
                    if (other == participant || !IsSlipstreamEligible(other))
                    {
                        continue;
                    }

                    TrackProgress otherProgress = State == null ? other.lapTracker.CurrentProgress : State.GetCurrentProgress(other);
                    float strength = ComputeSlipstreamStrength(followerProgress, otherProgress);
                    if (strength > bestStrength)
                    {
                        bestStrength = strength;
                        bestSource = DriverShortCode(other);
                    }
                }

                participant.vehicle.SetSlipstream(bestStrength, bestSource);
            }
        }

        // Shared eligibility for BOTH roles - a car being towed and a car creating
        // the wake. No slipstream for anything not genuinely racing on the live
        // track surface right now.
        bool IsSlipstreamEligible(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null)
            {
                return false;
            }

            if (participant.retired || participant.finished)
            {
                return false;
            }

            if (participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return false;
            }

            if (participant.vehicle.PitLimiterActive || participant.vehicle.IsPitGuided || !participant.vehicle.IsOnRoad)
            {
                return false;
            }

            if (participant.isRaceControlAutopilot || participant.vehicle.RaceControlSpeedCapKph < 900f)
            {
                return false;
            }

            if (CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                CurrentRaceControlState == RaceControlState.SafetyCarInThisLap ||
                CurrentRaceControlState == RaceControlState.RedFlagged ||
                CurrentRaceControlState == RaceControlState.Restart)
            {
                return false;
            }

            return true;
        }

        // Forward-only wrapped distance from one track distance to another, always
        // >= 0, going the direction of travel around the lap.
        float SlipstreamForwardDistance(float fromDistance, float toDistance)
        {
            float delta = toDistance - fromDistance;
            if (delta < 0f)
            {
                delta += Track.length;
            }

            return delta;
        }

        // Best tow around 18-35m behind, weaker further back or more offset,
        // scaled down off a straight and to zero in a proper corner. Deliberately
        // generous laterally ("somewhat behind", not laser-aligned) - full strength
        // inside SlipstreamFullLateralWidth, fading out to zero by
        // SlipstreamMaxLateralWidth.
        float ComputeSlipstreamStrength(TrackProgress followerProgress, TrackProgress leaderProgress)
        {
            float aheadDistance = SlipstreamForwardDistance(followerProgress.distance, leaderProgress.distance);
            if (aheadDistance < SlipstreamMinDistance || aheadDistance > SlipstreamMaxDistance)
            {
                return 0f;
            }

            float lateralDiff = Mathf.Abs(followerProgress.lateralDistance - leaderProgress.lateralDistance);
            if (lateralDiff > SlipstreamMaxLateralWidth)
            {
                return 0f;
            }

            float distanceStrength = Mathf.InverseLerp(SlipstreamMaxDistance, 18f, aheadDistance);
            float lateralStrength = Mathf.InverseLerp(SlipstreamMaxLateralWidth, SlipstreamFullLateralWidth, lateralDiff);
            float strength = Mathf.Clamp01(distanceStrength * lateralStrength);

            return strength * SlipstreamStraightSectionStrength(followerProgress.distance);
        }

        // Mild bends still get a partial tow (full below 6 degrees of heading
        // change over the sampled span, fading to none by 16) rather than a hard
        // corner/straight cutoff - a slipstream doesn't vanish the instant a
        // straight has the faintest kink in it.
        float SlipstreamStraightSectionStrength(float distance)
        {
            Vector3 point1;
            Vector3 forward1;
            Vector3 right1;
            Track.SampleAtDistance(distance + 10f, out point1, out forward1, out right1);
            Vector3 point2;
            Vector3 forward2;
            Vector3 right2;
            Track.SampleAtDistance(distance + 55f, out point2, out forward2, out right2);
            float angle = Vector3.Angle(forward1, forward2);
            return Mathf.Clamp01(Mathf.InverseLerp(16f, 6f, angle));
        }

        // Lap-gap radio feature: at the end of each completed player lap, the
        // engineer reports how much time was gained or lost against the relevant
        // car - the car directly ahead if the player isn't leading, or P2 if they
        // are. Edge-detects a newly completed lap and arms a short delay
        // (PlayerGapRadioDelaySeconds) before actually reading gaps/order, so a
        // read taken the instant the line is crossed (when standings/distances can
        // be momentarily noisy) never drives the call.
        const float PlayerGapRadioDelaySeconds = 1.2f;

        void UpdatePlayerLapGapRadio()
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null || IsTimeTrial || CurrentSession == RaceWeekendSession.Qualifying)
            {
                return;
            }

            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            if (completedLaps != playerGapRadioLastSeenCompletedLaps)
            {
                playerGapRadioLastSeenCompletedLaps = completedLaps;
                playerGapRadioPendingLapNumber = completedLaps;
                playerGapRadioPendingTimer = PlayerGapRadioDelaySeconds;
            }

            if (playerGapRadioPendingTimer < 0f)
            {
                return;
            }

            playerGapRadioPendingTimer -= Time.deltaTime;
            if (playerGapRadioPendingTimer > 0f)
            {
                return;
            }

            playerGapRadioPendingTimer = -1f;
            EvaluatePlayerLapGapRadio(playerGapRadioPendingLapNumber);
        }

        // Gap in seconds from `participant` to whoever is directly ahead of it in
        // the running order (not the distance-radius-capped GetIntervalToAheadSeconds,
        // which is tuned for DRS-range checks) - or -1f if `participant` is the
        // leader, has no valid data, or the car ahead is a full lap or more up the
        // road (not a meaningful "seconds" gap for a pace call).
        float GetRunningOrderGapAheadSeconds(RaceParticipant participant)
        {
            if (participant == null || State == null || participant.lapTracker == null)
            {
                return -1f;
            }

            int index = State.SortedOrder.IndexOf(participant);
            if (index <= 0)
            {
                return -1f;
            }

            RaceParticipant ahead = State.SortedOrder[index - 1];
            if (ahead == null || ahead.lapTracker == null)
            {
                return -1f;
            }

            float aheadDistance = State.GetProgressDistance(ahead);
            float participantDistance = State.GetProgressDistance(participant);
            float deltaMeters = aheadDistance - participantDistance;
            if (Track != null && deltaMeters >= Track.length * 0.92f)
            {
                return -1f;
            }

            float speed = Mathf.Max(24f, participant.vehicle == null ? 36f : Mathf.Abs(participant.vehicle.CurrentSpeedKph) / 3.6f);
            return Mathf.Max(0f, deltaMeters) / speed;
        }

        // The actual comparison, run once the post-lap delay above has elapsed.
        // Every exit path that isn't a genuine, clean, single-lap comparison
        // returns without touching the persistent snapshot fields, so a dirty lap
        // (pit stop, safety car, retired rival, lapped gap) never contaminates the
        // baseline the next clean lap compares against - it just self-heals once
        // a fully clean lap comes around again, rather than ever speaking a
        // misleading number.
        void EvaluatePlayerLapGapRadio(int completedLapNumber)
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null ||
                PlayerParticipant.retired || PlayerParticipant.finished || State == null)
            {
                return;
            }

            // Player pit-stop-this-lap detection: compares the pit-stop COUNT at
            // this lap boundary against the count recorded at the previous one,
            // which catches a stop that both started and fully completed within
            // the same lap - a plain "is currently pitting" check taken only at
            // this one delayed instant could miss that entirely.
            bool playerPittedThisLap = playerPitStopsAtLastGapRadioBoundary >= 0 && PlayerParticipant.pitStops != playerPitStopsAtLastGapRadioBoundary;
            playerPitStopsAtLastGapRadioBoundary = PlayerParticipant.pitStops;

            if (completedLapNumber < 1)
            {
                // Lap 1 (out/formation lap) has no previous-lap reference - never a
                // real comparison. Establish nothing yet; the snapshot starts once
                // lap 1 itself completes cleanly.
                return;
            }

            SortRunningOrder();
            int playerIndex = State.SortedOrder.IndexOf(PlayerParticipant);
            if (playerIndex < 0)
            {
                return;
            }

            bool playerIsLeader = playerIndex == 0;
            int position = playerIndex + 1;
            RaceParticipant comparisonDriver;
            float currentGap;
            if (playerIsLeader)
            {
                comparisonDriver = State.SortedOrder.Count > 1 ? State.SortedOrder[1] : null;
                currentGap = comparisonDriver == null ? -1f : GetRunningOrderGapAheadSeconds(comparisonDriver);
            }
            else
            {
                comparisonDriver = State.SortedOrder[playerIndex - 1];
                currentGap = GetRunningOrderGapAheadSeconds(PlayerParticipant);
            }

            bool raceControlUnsettled = drsRestartCooldownTimer > 0f ||
                CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                CurrentRaceControlState == RaceControlState.SafetyCarInThisLap ||
                CurrentRaceControlState == RaceControlState.RedFlagged ||
                CurrentRaceControlState == RaceControlState.Restart;

            bool comparisonDriverDirty = comparisonDriver == null || comparisonDriver.lapTracker == null ||
                comparisonDriver.retired || comparisonDriver.finished ||
                comparisonDriver.isPitting || comparisonDriver.pitPhase != PitPhase.None;

            bool gapValid = currentGap >= 0f && currentGap < 40f;

            bool lapIsClean = !playerPittedThisLap && !raceControlUnsettled && !comparisonDriverDirty && gapValid &&
                !PlayerParticipant.isPitting && PlayerParticipant.pitPhase == PitPhase.None;

            if (!lapIsClean)
            {
                return;
            }

            string comparisonId = comparisonDriver.driverId;
            bool hasContinuousSnapshot = lastPlayerGapRadioLap == completedLapNumber - 1;
            bool playerPositionChanged = hasContinuousSnapshot && previousPlayerGapRadioPosition > 0 && position != previousPlayerGapRadioPosition;

            if (playerPositionChanged)
            {
                string name = DriverRadioName(comparisonDriver);
                string gapText = FormatGapSeconds(currentGap);
                if (position < previousPlayerGapRadioPosition)
                {
                    PostEngineerMessage(playerIsLeader
                        ? "Position gained. Leading now, " + name + " is " + gapText + " behind."
                        : "Position gained. Now chasing " + name + ", gap " + gapText + ".", false);
                }
                else
                {
                    PostEngineerMessage("Position lost. Car ahead is " + name + ", gap " + gapText + ".", false);
                }
            }
            else if (hasContinuousSnapshot && !string.IsNullOrEmpty(previousComparisonDriverId) && previousComparisonDriverId == comparisonId &&
                     previousPlayerWasLeader == playerIsLeader)
            {
                SpeakPlayerLapGapDelta(currentGap - previousPlayerComparisonGap, comparisonDriver, playerIsLeader);
            }

            // else: comparison target shuffled for reasons other than the player's
            // own position (the car ahead pitted out/retired and got replaced by
            // someone else, or this is the first clean lap after a gap in
            // coverage) - stay silent, just refresh the baseline below.
            previousPlayerGapRadioPosition = position;
            lastPlayerGapRadioLap = completedLapNumber;
            previousPlayerComparisonGap = currentGap;
            previousComparisonDriverId = comparisonId;
            previousPlayerWasLeader = playerIsLeader;
        }

        const float PlayerGapRadioStableThreshold = 0.15f;

        void SpeakPlayerLapGapDelta(float delta, RaceParticipant comparisonDriver, bool playerIsLeader)
        {
            string name = DriverRadioName(comparisonDriver);
            if (Mathf.Abs(delta) < PlayerGapRadioStableThreshold)
            {
                PostEngineerMessage(playerIsLeader ? "Gap to P2 is stable." : "Gap to " + name + " is stable.", false);
                return;
            }

            string amount = FormatGapDelta(Mathf.Abs(delta));
            if (playerIsLeader)
            {
                // delta > 0: the gap to P2 grew - player pulled away.
                // delta < 0: the gap shrank - P2 gained on the player.
                PostEngineerMessage(delta > 0f
                    ? "You pulled " + amount + " on " + name + " last lap."
                    : name + " gained " + amount + " on you last lap.", false);
            }
            else
            {
                // delta < 0: the gap to the car ahead shrank - player gained.
                // delta > 0: the gap grew - player lost time to them.
                PostEngineerMessage(delta < 0f
                    ? "Good lap. You gained " + amount + " on " + name + "."
                    : "You lost " + amount + " to " + name + " last lap.", false);
            }
        }

        static readonly string[] TenthsWords = { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        // Under a second: spoken as tenths ("seven tenths", "half a second" for
        // exactly five). A second or more: plain numeric text - spec explicitly
        // allows this fallback rather than a full number-to-words conversion.
        string FormatGapDelta(float absSeconds)
        {
            if (absSeconds < 0.95f)
            {
                int tenths = Mathf.Clamp(Mathf.RoundToInt(absSeconds * 10f), 1, 9);
                if (tenths == 5)
                {
                    return "half a second";
                }

                return tenths == 1 ? "one tenth" : TenthsWords[tenths] + " tenths";
            }

            return absSeconds.ToString("0.0") + " seconds";
        }

        string FormatGapSeconds(float seconds)
        {
            return Mathf.Max(0f, seconds).ToString("0.0") + " seconds";
        }

        // Compact 3-letter driver code for HUD/debug display (slipstream source,
        // etc.) - distinct from DriverRadioName below, which prefers a spoken last
        // name for engineer radio text.
        string DriverShortCode(RaceParticipant participant)
        {
            if (participant == null)
            {
                return "";
            }

            if (participant.driverData != null && !string.IsNullOrEmpty(participant.driverData.abbreviation))
            {
                return participant.driverData.abbreviation.ToUpperInvariant();
            }

            return DriverCode(participant.driverName);
        }

        // Radio name for a driver: last name if driverName resolves to one,
        // falling back to the driver's real abbreviation/code.
        string DriverRadioName(RaceParticipant participant)
        {
            if (participant == null)
            {
                return "the car";
            }

            if (!string.IsNullOrEmpty(participant.driverName))
            {
                string[] parts = participant.driverName.Trim().Split(' ');
                string lastName = parts[parts.Length - 1];
                if (!string.IsNullOrEmpty(lastName))
                {
                    return lastName;
                }
            }

            if (participant.driverData != null && !string.IsNullOrEmpty(participant.driverData.abbreviation))
            {
                return participant.driverData.abbreviation.ToUpperInvariant();
            }

            return DriverCode(participant.driverName);
        }

        // Automatic pit stop fix: if the player set a stop plan on the
        // strategy screen and hasn't manually called for a stop themselves,
        // the car boxes on its own once the planned lap is reached - the
        // plan previously only ever surfaced as an engineer reminder message
        // (see the "Box this lap for..." line in UpdateRaceEngineer below),
        // with nothing actually acting on it, so a player who missed or
        // ignored that message never pitted at all. Runs every tick,
        // independent of the engineer message queue (which has several
        // early-return branches for other one-shot messages), so a message
        // skipped on a busy frame can never also skip the actual stop. Any
        // manual request (the P key, on any lap) sets vehicle.PitRequested
        // itself and is always checked first here, so it always wins and is
        // never second-guessed or replaced by the plan.
        void UpdatePlayerAutoPitStrategy()
        {
            if (PlayerParticipant == null || PlayerParticipant.vehicle == null || PlayerParticipant.lapTracker == null ||
                CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return;
            }

            if (PlayerParticipant.isPitting || PlayerParticipant.pitPhase != PitPhase.None ||
                PlayerParticipant.vehicle.PitRequested || PlayerParticipant.retired || PlayerParticipant.finished ||
                PlayerParticipant.missedPitEntryThisLap)
            {
                return;
            }

            if (!ShouldPromptPlannedStop(PlayerParticipant))
            {
                return;
            }

            // Same currentLapNumber >= targetLap trigger point UpdateRaceEngineer's
            // "Box this lap for..." message already uses, so the automatic stop
            // and the message the player sees stay in lockstep instead of
            // introducing a second, slightly different lap-comparison convention.
            //
            // Off-by-one fix: targetLap (from GetPlannedPitLapForStop/
            // RecommendedPitLap) is a 1-based lap NUMBER - "pit on lap 6" means
            // while DisplayLap (CompletedLaps + 1) reads 6, not once
            // CompletedLaps itself reaches 6. CompletedLaps only becomes 6 after
            // lap 6 is actually finished (i.e. once already driving lap 7), so
            // comparing raw CompletedLaps against targetLap fired the stop a
            // full lap after the one the player planned for.
            int targetLap = NextPlannedPitLapFor(PlayerParticipant);
            int completedLaps = PlayerParticipant.lapTracker.CompletedLaps;
            int currentLapNumber = completedLaps + 1;
            if (currentLapNumber < targetLap)
            {
                return;
            }

            PlayerParticipant.vehicle.RequestPit();
            PlayerParticipant.pitAutoTriggered = true;
            // Source tracking: this is the pre-race plan firing itself, never a
            // manual override, so it is never cancellable through
            // CanCancelManualPitRequest/CancelManualPitRequest.
            PlayerParticipant.activePitRequestSource = PitRequestSource.PreRacePlan;
            PlayerParticipant.manualPitRequested = false;
            PlayerParticipant.manualPitCommitted = false;
            if (!PlayerParticipant.requestedPitCompoundSet)
            {
                PlayerParticipant.requestedPitCompound = NextPlannedPitCompoundFor(PlayerParticipant);
                PlayerParticipant.requestedPitCompoundSet = true;
            }

            SessionMessage = "Auto-pit: strategy plan (" + PlayerParticipant.requestedPitCompound + ")";
            GameLog.Info("[Pit] Auto-triggered planned stop for player at lap " + currentLapNumber + " (target " + targetLap + "), compound=" + PlayerParticipant.requestedPitCompound + ".");
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                // Distinct from the earlier "Box this lap for..." reminder in
                // UpdateRaceEngineer (a heads-up before the fact) - this
                // confirms the car has actually committed to the stop on its
                // own, since the player never pressed the pit key themselves.
                PostEngineerMessage("Boxing automatically per the strategy plan - " + PlayerParticipant.requestedPitCompound + "s fitted this stop.", true);
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
                PostEngineerMessage("Final lap. Bring it home, watch the tyres.", true, RaceAudioCue.FinalLap);
                return;
            }

            if (car.FuelStarved && !engineerFuelStarvationSent)
            {
                engineerFuelStarvationSent = true;
                PostEngineerMessage("We are out of fuel. The engine is starving.", true, RaceAudioCue.Damage);
                return;
            }

            // Fuel system pass: delta-based instead of the old flat "<7kg" check -
            // that threshold was tuned for a 35kg flat start and would fire almost
            // immediately on a legitimate 8kg target-fuel short race. Reads the
            // same projected-delta figure the HUD fuel pill shows.
            float fuelDeltaLaps = car.ProjectedFuelDeltaLaps;
            if (fuelDeltaLaps < -0.25f)
            {
                engineerFuelEverNegative = true;
                if (!engineerFuelWarningSent)
                {
                    engineerFuelWarningSent = true;
                    PostEngineerMessage("Fuel target is negative. Lift and coast into heavy braking zones.", false);
                    return;
                }
            }
            else if (engineerFuelEverNegative && fuelDeltaLaps >= -0.05f && fuelDeltaLaps < 0.4f && !engineerFuelRecoverySent)
            {
                engineerFuelRecoverySent = true;
                PostEngineerMessage("Good fuel saving. You're back on target.", false);
                return;
            }
            else if (engineerFuelEverNegative && fuelDeltaLaps >= 0.4f && !engineerFuelSafeToPushSent)
            {
                engineerFuelSafeToPushSent = true;
                PostEngineerMessage("Fuel is safe now. You can push.", false);
                return;
            }

            if (car.Damage.OverallPercent > 45f && !engineerDamageWarningSent)
            {
                engineerDamageWarningSent = true;
                PostEngineerMessage("We are seeing damage on the car. Consider a stop for repairs.", false, RaceAudioCue.Damage);
                return;
            }
            if (ShouldPromptPlannedStop(PlayerParticipant) && !PlayerParticipant.isPitting)
            {
                int targetLap = NextPlannedPitLapFor(PlayerParticipant);
                // Same 1-based DisplayLap comparison UpdatePlayerAutoPitStrategy
                // uses - see the off-by-one fix comment there. Keeping both in
                // the same completedLaps+1 convention is what keeps this
                // reminder and the actual auto-triggered stop on the same lap.
                int currentLapNumber = completedLaps + 1;
                bool mandatoryStopStillOwed = PlayerParticipant.pitStops == 0;

                // Smarter strategy planner: proactive undercut alert, a couple of
                // laps before the planned stop rather than only the "Box this lap"
                // call right when the window arrives - the AI now pits early to
                // undercut a car it's following (see RaceManager.ShouldAiPitForUndercut),
                // so the player needs the same early notice to react before a rival
                // jumps them, not just a report of what already happened.
                if (mandatoryStopStillOwed && targetLap > 0 && currentLapNumber == Mathf.Max(0, targetLap - 2) && lastEngineerPitLapPrompt != completedLaps)
                {
                    RaceParticipant rivalAheadForUndercut = FindCarAhead(PlayerParticipant, 40f);
                    float rivalGap = GetIntervalToAheadSeconds(PlayerParticipant);
                    if (rivalAheadForUndercut != null && rivalAheadForUndercut.pitStops == 0 && rivalGap > 0f && rivalGap < 2.2f)
                    {
                        lastEngineerPitLapPrompt = completedLaps;
                        PostEngineerMessage("The car ahead hasn't stopped yet. Box early and you could undercut them.", false);
                        return;
                    }
                }

                if (currentLapNumber >= targetLap && lastEngineerPitLapPrompt != completedLaps)
                {
                    lastEngineerPitLapPrompt = completedLaps;
                    TyreCompound plannedCompound = NextPlannedPitCompoundFor(PlayerParticipant);
                    float undercutGap = GetIntervalToAheadSeconds(PlayerParticipant);
                    string undercut = undercutGap > 0f && undercutGap < 2.5f ? " The undercut on the car ahead is live." : "";
                    string requirement = mandatoryStopStillOwed ? "Mandatory stop still required." : "Second stop window is here.";
                    // Recommendation reason: the requirement/undercut clauses above
                    // already cover "mandatory rule" and "undercut threat" - this
                    // adds the other two most common real reasons (tyre wear, rain
                    // crossover) whenever they're ALSO genuinely true this stop, so
                    // the call reads as "why", not just "when".
                    string extraReason = PitRecommendationReasonClause(PlayerParticipant);
                    PostEngineerMessage("Box this lap for " + plannedCompound + "s. " + requirement + undercut + extraReason, true);
                    return;
                }

                if (currentLapNumber == Mathf.Max(0, targetLap - 1) && lastEngineerPitLapPrompt != completedLaps)
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
                PostEngineerMessage("Careful with track limits. One more warning is a time penalty.", true, RaceAudioCue.Penalty);
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

            PromoteGhostRecordingIfBest(best);
        }

        // Time-trial ghost: the buffer accumulated by RecordGhostSample IS the
        // lap that just produced this new best time (same-frame ordering -
        // both run from Update() after the same lapTracker.Tick() pass), so
        // it's promoted here rather than re-detecting the lap boundary a
        // second time. Session mode swaps the live ghost immediately so the
        // player is always chasing their own latest best; All-time mode only
        // ever touches the persistent store (loaded once at spawn) so the
        // on-track ghost stays a stable target for the whole session.
        void PromoteGhostRecordingIfBest(float lapTime)
        {
            if (!IsTimeTrial || EventData == null || ghostRecordingBuffer.Count < 2)
            {
                return;
            }

            GhostLapData candidate = new GhostLapData
            {
                trackId = EventData.trackId,
                lapTime = lapTime,
                samples = new List<GhostSample>(ghostRecordingBuffer)
            };

            int ghostMode = Settings != null ? Settings.Current.ghostMode : 0;
            if (ghostMode == 2)
            {
                TimeTrialGhostStore.TrySaveIfBest(EventData.trackId, candidate);
            }
            else if (ghostMode == 1 && ghostController != null)
            {
                ghostController.Initialize(candidate);
            }
        }

        // Called once per frame (not per participant - only the player is
        // recorded/played back) from Update(), gated on IsTimeTrial by both
        // callers below.
        void RecordGhostSample()
        {
            if (PlayerParticipant == null || PlayerParticipant.lapTracker == null || PlayerParticipant.vehicle == null)
            {
                return;
            }

            int currentLap = PlayerParticipant.lapTracker.CompletedLaps;
            if (currentLap != ghostRecordedLapNumber)
            {
                ghostRecordedLapNumber = currentLap;
                ghostRecordingBuffer.Clear();
                ghostRecordTimer = 0f;
            }

            ghostRecordTimer -= Time.deltaTime;
            if (ghostRecordTimer > 0f || ghostRecordingBuffer.Count >= TimeTrialGhostStore.MaxSamplesPerGhost)
            {
                return;
            }

            ghostRecordTimer = GhostSampleInterval;
            ghostRecordingBuffer.Add(new GhostSample
            {
                elapsedSeconds = PlayerParticipant.lapTracker.CurrentLapTime,
                distanceAlongLap = PlayerParticipant.lapTracker.CurrentProgress.normalized,
                position = PlayerParticipant.transform.position,
                headingDegrees = PlayerParticipant.transform.eulerAngles.y,
                speedKph = PlayerParticipant.vehicle.CurrentSpeedKph
            });
        }

        void UpdateGhostPlayback()
        {
            if (ghostController == null || PlayerParticipant == null || PlayerParticipant.lapTracker == null)
            {
                return;
            }

            ghostController.UpdatePlayback(PlayerParticipant.lapTracker.CurrentLapTime);
        }

        // Spawned once, right after the player's own car, at Time Trial
        // session start - reuses CreateOpenWheelCar (the exact same geometry
        // every real car uses) so the ghost silhouette is instantly
        // recognisable, then strips it down to a purely visual, non-colliding
        // shell (kinematic Rigidbody, disabled Collider) and re-tints every
        // renderer through a single shared translucent material so it always
        // reads as a ghost regardless of team livery colours.
        void SpawnGhostIfAvailable()
        {
            if (!IsTimeTrial || Settings == null || Settings.Current.ghostMode == 0 || EventData == null)
            {
                return;
            }

            GhostLapData stored = Settings.Current.ghostMode == 2 ? TimeTrialGhostStore.GetBestGhost(EventData.trackId) : null;
            if (Settings.Current.ghostMode == 2 && stored == null)
            {
                return;
            }

            ghostCarObject = CreateOpenWheelCar("Ghost", new Color(0.3f, 0.62f, 1f), new Color(0.55f, 0.8f, 1f));
            ghostCarObject.name = "Ghost car";
            ghostCarObject.transform.SetParent(raceWorld.transform);
            Rigidbody body = ghostCarObject.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }

            Collider[] colliders = ghostCarObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            Material ghostMaterial = CreateTranslucentMaterial("Ghost car shell", new Color(0.3f, 0.62f, 1f), 0.4f);
            Renderer[] renderers = ghostCarObject.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = ghostMaterial;
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            ghostController = ghostCarObject.AddComponent<GhostCarController>();
            if (stored != null)
            {
                ghostController.Initialize(stored);
            }

            if (Track != null)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Track.SampleAtDistance(0f, out point, out forward, out right);
                ghostCarObject.transform.position = point + Vector3.up * 0.3f;
                ghostCarObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        // Standard shader in alpha-blended Fade mode - same recipe
        // TrackManager.CreateTranslucentMaterial already uses for the wet-
        // track sheen/mist banks, duplicated here rather than shared across
        // files since TrackManager and RaceManager don't otherwise share a
        // material-helper base class.
        Material CreateTranslucentMaterial(string materialName, Color color, float alpha)
        {
            Material material = new Material(Shader.Find("Standard"));
            material.name = materialName;
            material.color = new Color(color.r, color.g, color.b, alpha);
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
            return material;
        }

        // HUD delta readout for Time Trial, parallel to QualifyingDeltaText -
        // RaceHud's qualifying-card slot shows whichever of the two applies
        // (see RaceHud.UpdateTimingCard).
        public string GhostDeltaText(RaceParticipant participant)
        {
            if (!IsTimeTrial || ghostController == null || !ghostController.HasLap || participant == null || participant.lapTracker == null)
            {
                return "--";
            }

            if (participant.lapTracker.OutLapActive)
            {
                return "--";
            }

            float ghostTime = ghostController.GhostTimeAtDistance(participant.lapTracker.CurrentProgress.normalized);
            if (ghostTime < 0f)
            {
                return "--";
            }

            float delta = participant.lapTracker.CurrentLapTime - ghostTime;
            string color = delta <= 0f ? "#6CFF8D" : "#FF6C6C";
            return "<color=" + color + ">" + (delta >= 0f ? "+" : "") + delta.ToString("0.000") + "</color>";
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

            if (participant.pitPhase == PitPhase.ExitMerge)
            {
                return "PIT EXIT  MERGING";
            }

            if (participant.pitLimiterUntilExit)
            {
                return "PIT EXIT  LIMITER 80";
            }

            if (participant.pitTyreSelectionActive && participant.vehicle != null && participant.vehicle.PitRequested)
            {
                return "PIT TYRE " + participant.requestedPitCompound + "  1S 2M 3H 4I 5W";
            }

            // Pit strategy display fix: this used to check pitStops > 0 first,
            // so a 2-stop plan's already-queued second request showed the
            // stale "MANDATORY STOP COMPLETE" from stop 1 instead of the
            // actually-queued state - checked first here instead, and now
            // distinguishes an auto-scheduled stop (the strategy plan
            // triggered it) from a manually-called one for the HUD.
            if (participant.vehicle != null && participant.vehicle.PitRequested)
            {
                return participant.pitAutoTriggered
                    ? "AUTO-PIT QUEUED  " + participant.requestedPitCompound
                    : "PIT REQUEST QUEUED";
            }

            // VSC/SC interactive pit-window offer: makes the radio call's "press P"
            // instruction visible on the HUD itself too, not just in the radio
            // message text, while the offer is still open for this participant.
            if (participant.isPlayer && playerHasActiveRaceControlPitOffer)
            {
                string offerLabel = playerRaceControlPitOfferType == RaceControlPitOfferType.SafetyCar ? "SC" : "VSC";
                return offerLabel + " PIT WINDOW OPEN  PRESS P TO BOX";
            }

            if (participant.pitStops > 0 && NextPlannedPitLapFor(participant) <= 0)
            {
                return "MANDATORY STOP COMPLETE";
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
            // Explicit manual call (the P key) always overrides/replaces
            // whatever the strategy plan would have done - see
            // UpdatePlayerAutoPitStrategy, which never fires once
            // vehicle.PitRequested is already latched true from here.
            participant.pitAutoTriggered = false;
            // Cancellable-manual-pit-stop fix: this is the one place the plain
            // manual (P key) request is created. Tagging it here - and nowhere
            // else - is what lets CanCancelManualPitRequest tell a manual
            // override apart from the pre-race plan's own auto-trigger, without
            // touching NextPlannedPitLapFor/GetPlannedPitLapForStop at all.
            participant.activePitRequestSource = PitRequestSource.Manual;
            participant.manualPitRequested = true;
            participant.manualPitCommitted = false;
            SessionMessage = "Pit request: choose tyre 1-5";
            PostEngineerMessage("Pit request received. Select tyres: 1 Soft, 2 Medium, 3 Hard, 4 Intermediate, 5 Wet.", true, RaceAudioCue.PitCall);
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

        // DRS fix: the 1-second gap requirement is evaluated ONCE, at each zone's own
        // detection point (a short distance before the zone starts - see
        // TrackRuntime.drsDetectionOne/Two), not continuously through the whole
        // activation zone. Real DRS works the same way: the gap is checked as you
        // cross the detection line, and if you're within a second you get DRS for
        // the ENTIRE following zone even if the gap opens back up past a second
        // halfway down the straight. The old code called GetIntervalToAheadSeconds
        // every frame inside the zone, so DRS would flicker off the instant the live
        // gap crept past 1.0s - which is exactly why it "did not stay open long
        // enough". Runs unconditionally every frame per participant (not just while
        // DRS-eligible) so a car approaching a zone always gets evaluated the moment
        // it crosses the detection point, regardless of what it was doing before.
        void UpdateDrsEligibility(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null || Track == null || participant.vehicle == null)
            {
                return;
            }

            TrackProgress progress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            float currentNormalized = progress.normalized;

            if (participant.retired || participant.finished || participant.isPitting || participant.pitPhase != PitPhase.None)
            {
                participant.drsEligibleZoneOne = false;
                participant.drsEligibleZoneTwo = false;
                participant.previousDrsProgressNormalized = currentNormalized;
                return;
            }

            if (participant.previousDrsProgressNormalized < 0f)
            {
                participant.previousDrsProgressNormalized = currentNormalized;
                return;
            }

            float previousNormalized = participant.previousDrsProgressNormalized;

            if (Track.CrossedDrsDetectionPoint(previousNormalized, currentNormalized, 1))
            {
                participant.drsEligibleZoneOne = EvaluateDrsDetectionGap(participant);
                participant.drsEligibilityLapZoneOne = participant.lapTracker.CompletedLaps;
            }

            if (Track.CrossedDrsDetectionPoint(previousNormalized, currentNormalized, 2))
            {
                participant.drsEligibleZoneTwo = EvaluateDrsDetectionGap(participant);
                participant.drsEligibilityLapZoneTwo = participant.lapTracker.CompletedLaps;
            }

            // Clear a zone's own eligibility once the car has actually left it, so a
            // stale "eligible" flag can never linger into some later, unrelated pass
            // through the same normalized band (e.g. after a forced reposition).
            if (Track.IsInDrsZone(1, previousNormalized) && !Track.IsInDrsZone(1, currentNormalized))
            {
                participant.drsEligibleZoneOne = false;
            }

            if (Track.IsInDrsZone(2, previousNormalized) && !Track.IsInDrsZone(2, currentNormalized))
            {
                participant.drsEligibleZoneTwo = false;
            }

            participant.previousDrsProgressNormalized = currentNormalized;
        }

        // The actual 1-second-gap decision, made once at the detection point.
        // Qualifying/time trial never require a gap at all - every zone is always
        // available with no car-ahead requirement.
        bool EvaluateDrsDetectionGap(RaceParticipant participant)
        {
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
                CurrentRaceControlState == RaceControlState.RedFlagged ||
                CurrentRaceControlState == RaceControlState.Restart)
            {
                return false;
            }

            // A local yellow disables DRS for any car in the flagged sector (the
            // same scope as the overtaking ban - DRS exists purely to overtake),
            // plus the tighter near-incident window used by the speed cap. Checked
            // continuously (every frame, not just at the detection point) so a
            // yellow that comes out AFTER a car already earned zone eligibility
            // still shuts DRS off immediately, matching the real rule.
            if (IsNearLocalYellowIncident(participant) || IsOvertakingRestrictedForParticipant(participant))
            {
                return false;
            }

            TrackProgress progress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            int zoneIndex = Track.GetDrsZoneIndex(progress.normalized);
            if (zoneIndex == 0)
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

            // The gap requirement itself was already decided at this zone's
            // detection point (UpdateDrsEligibility) and is NOT re-checked here -
            // that is the whole point of the fix. A car that qualified within 1s at
            // the line keeps DRS for the entire zone even if the gap opens back up.
            return zoneIndex == 1 ? participant.drsEligibleZoneOne : participant.drsEligibleZoneTwo;
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
                CurrentRaceControlState == RaceControlState.RedFlagged ||
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
            // Cornering buff round 8: no longer consumed by AiVehicleController's
            // apex-speed calculation - it used to stack multiplicatively on top of
            // EstimateApexSpeedForCornerType's own skillTier-scaled floor/ceiling,
            // which could push the AI's target speed past its own straight-line top
            // speed through a corner (physically unachievable) and send it straight
            // into the wall. Left declared/assigned per-tier rather than removed
            // outright, since ordering across the four difficulty profiles still
            // documents relative intent even though nothing reads it anymore.
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
                    // Reaction-time buff: explicit per-tier target ranges requested
                    // (Easy 0.4-0.6 / Medium 0.3-0.4 / Hard 0.25-0.3 / Expert 0.2-0.25) -
                    // round 1's uniform nerf (was 0.85/0.55/0.32/0.11) overshot,
                    // especially Easy at a full second.
                    reactionTimeSeconds = 0.5f,
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
                    // Corner-speed pass 3: Medium should stay clearly more
                    // cautious than Hard/Expert but shouldn't read as broken -
                    // a slightly quicker exit pickup (was 0.30/0.78).
                    throttleDelay = 0.24f,
                    // Cornering buff round 5: Medium gets a modest, deliberately small
                    // lift (was 0.82/1.00/1.00) - "decent, not broken", nowhere near
                    // the Hard/Expert jump below.
                    exitThrottleConfidence = 0.86f,
                    lineOffsetNoise = 0.75f,
                    reactionTimeSeconds = 0.35f,
                    overtakeCommitment = 0.55f,
                    defendCommitment = 0.55f,
                    ersDeploymentQuality = 0.65f,
                    drsUsageQuality = 0.75f,
                    mistakeChancePerLap = 0.09f,
                    trafficAvoidanceCaution = 1.05f,
                    wetWeatherCaution = 1.2f,
                    tyreSavingBias = 0.20f,
                    paceMultiplier = 1.01f,
                    // Cornering buff round 7: pushed up (was 1.05) alongside the wider
                    // HighSpeed/Medium bands in AiVehicleController - Medium now covers
                    // genuinely fast corners too, not just cautious ones.
                    cornerSpeedMultiplier = 1.16f,
                    straightSpeedMultiplier = 0.98f,
                    brakeConfidenceMultiplier = 1.05f,
                    throttleAggressionMultiplier = 1.05f
                };
            }

            if (difficulty == RaceDifficulty.Hard)
            {
                // Cornering buff round 5: pushed meaningfully further on top of the
                // corner-speed pass 4 numbers below - Hard should be clearly,
                // competitively fast through corners, not just "a notch above
                // Medium". trafficAvoidanceCaution raised alongside the speed/
                // confidence numbers (was 0.74) so the extra pace is paired with
                // more avoidance margin, not less - carrying more corner speed
                // shouldn't also mean crashing into traffic more often.
                return new AiDifficultyProfile
                {
                    brakeDistanceMultiplier = 1.00f,
                    // Corner-speed pass: Hard used to get none of the skillTier-
                    // gated corner-type bonuses in AiVehicleController (those were
                    // Expert-only) despite already having a fairly high confidence
                    // number here - the confidence stat alone wasn't enough to read
                    // as "meaningfully faster through corners". Bumped alongside
                    // the new skillTier blend so Hard is now clearly quicker than
                    // Medium through medium/fast corners, not just a hair sharper.
                    minimumCornerSpeedConfidence = 0.985f,
                    apexErrorMeters = 0.55f,
                    // Cornering buff round 5: exit hesitation shortened again (was
                    // 0.10) and exit confidence raised again (was 0.95) - "pick up
                    // throttle earlier on exit" was as much about this as the
                    // apex-speed floors themselves.
                    throttleDelay = 0.07f,
                    exitThrottleConfidence = 0.97f,
                    lineOffsetNoise = 0.36f,
                    reactionTimeSeconds = 0.275f,
                    overtakeCommitment = 0.77f,
                    defendCommitment = 0.79f,
                    ersDeploymentQuality = 0.87f,
                    drsUsageQuality = 0.94f,
                    mistakeChancePerLap = 0.045f,
                    trafficAvoidanceCaution = 0.82f,
                    wetWeatherCaution = 0.98f,
                    tyreSavingBias = 0.12f,
                    paceMultiplier = 1.08f,
                    // Cornering buff round 7: pushed again (was 1.22/1.24/1.28/1.30/
                    // 1.42) - "fast corners need to be A LOT faster". No longer
                    // touches genuine hairpins at all (see the Hairpin-type exemption
                    // in AiVehicleController), so this only ever buffs HighSpeed/
                    // Medium/Slow corners - safe to push hard without also making
                    // hairpins faster again.
                    cornerSpeedMultiplier = 1.60f,
                    straightSpeedMultiplier = 1.00f,
                    brakeConfidenceMultiplier = 1.34f,
                    throttleAggressionMultiplier = 1.38f
                };
            }

            // Expert - the ceiling tier. straightSpeedMultiplier is still the one
            // hard rule that can never move past 1.0, since it scales against
            // vehicle.TargetTopSpeedKph, the same DRS/ERS-aware physics ceiling the
            // player's own car uses - every buff below is a cornering-confidence/
            // braking-point/throttle-pickup number, never a straight-line speed one.
            // trafficAvoidanceCaution raised alongside the rest (was 0.31), same
            // reasoning as Hard above: more committed cornering pairs with more
            // avoidance margin, not less.
            return new AiDifficultyProfile
            {
                brakeDistanceMultiplier = 1.06f,
                // Cornering buff round 5: pushed to the practical ceiling (was
                // 0.995) - Expert should show essentially zero entry hesitation.
                minimumCornerSpeedConfidence = 0.999f,
                apexErrorMeters = 0.14f,
                throttleDelay = 0.03f,
                exitThrottleConfidence = 0.995f,
                lineOffsetNoise = 0.12f,
                reactionTimeSeconds = 0.225f,
                overtakeCommitment = 0.93f,
                defendCommitment = 0.93f,
                ersDeploymentQuality = 0.96f,
                drsUsageQuality = 0.98f,
                mistakeChancePerLap = 0.0045f,
                trafficAvoidanceCaution = 0.42f,
                wetWeatherCaution = 0.88f,
                tyreSavingBias = 0.07f,
                paceMultiplier = 1.15f,
                // Cornering buff round 7: pushed further still (was 1.34/1.58/1.70/
                // 1.44/1.62) - "fast corners need to be A LOT faster". Still never
                // touches genuine hairpins (see the Hairpin-type exemption in
                // AiVehicleController) - Expert should be the fastest, most committed
                // tier through fast corners specifically, not through hairpins too.
                cornerSpeedMultiplier = 1.85f,
                straightSpeedMultiplier = 1.00f,
                brakeConfidenceMultiplier = 1.70f,
                throttleAggressionMultiplier = 1.85f
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

        // Fuel system pass: keeps VehicleController's fuel projection current
        // (remaining laps, including the fractional lap in progress, so the HUD/AI
        // read a live "will this fuel actually make it" figure) and handles the
        // running-out-of-fuel DNF. Deliberately does NOT retire the instant fuel
        // hits zero - VehicleController.FuelStarved gives a short grace period
        // (FuelStarvedGraceSeconds) of crawling on starvation power first, so the
        // player feels the consequence before the car actually parks.
        const float FuelStarvedGraceSeconds = 11f;

        void UpdateFuelState(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null)
            {
                return;
            }

            float remainingLaps = Mathf.Max(0f, RaceLaps - (participant.lapTracker.CompletedLaps + participant.lapTracker.CurrentProgress.normalized));
            participant.vehicle.UpdateFuelProjection(remainingLaps);

            if (!participant.vehicle.FuelStarved || participant.retired || participant.finished || participant.fuelStarvationRetirementApplied)
            {
                return;
            }

            if (participant.vehicle.FuelStarvedTimer >= FuelStarvedGraceSeconds)
            {
                participant.fuelStarvationRetirementApplied = true;
                RetireParticipant(participant, "Fuel starvation");
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
                    driverCode = GetDisplayDriverCode(entry.driverData, entry.driverName),
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
            // Time budget widened alongside QualifyingSessionLapCap (was 360s for a
            // flat 2-lap session) so the extra flying-lap attempts actually fit on
            // longer circuits instead of being cut off by the clock before the lap
            // cap is ever reached.
            if (RaceElapsed > 480f)
            {
                return true;
            }

            if (lap.ValidLapsCompleted > 0 && PlayerHoldsCurrentQualifyingPole())
            {
                return true;
            }

            return lap.CompletedLaps >= QualifyingSessionLapCap;
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

        // Cornering performance telemetry: samples this car's actual speed the
        // moment it passes each classified corner's peak (see
        // TrackRuntime.ClassifyCorners, cached once as telemetryCorners at
        // track build time) against a simple reference speed for that corner's
        // risk tier, so a post-race/post-session report can show where time
        // was actually gained or lost by corner type. Deliberately a live
        // apex-speed-vs-reference comparison rather than a full ghost-lap
        // system - much cheaper to build and still answers the real question
        // ("am I carrying enough speed through high-speed corners specifically,
        // or hairpins specifically") without needing lap-to-lap replay data.
        const float TelemetryCornerCaptureRadius = 12f;

        void SampleCorneringTelemetry(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || telemetryCorners == null || telemetryCorners.Count == 0 || Track == null)
            {
                return;
            }

            if (participant.retired || participant.finished || participant.isPitting || participant.pitPhase != PitPhase.None)
            {
                return;
            }

            float distance = participant.lapTracker != null ? participant.lapTracker.CurrentProgress.distance : 0f;
            for (int i = 0; i < telemetryCorners.Count; i++)
            {
                TrackRuntime.CornerRiskInfo corner = telemetryCorners[i];
                float delta = Mathf.Abs(Track.WrapDistance(distance - corner.distance));
                float wrapped = Mathf.Min(delta, Track.length - delta);
                if (wrapped > TelemetryCornerCaptureRadius)
                {
                    continue;
                }

                // Only once per approach - a car crawling/recovering right at
                // a corner apex for several ticks (traffic, a spin) must not
                // flood the same sample in over and over.
                if (Mathf.Abs(participant.LastTelemetryCornerDistance - corner.distance) < 1f)
                {
                    continue;
                }

                participant.LastTelemetryCornerDistance = corner.distance;

                float carTopSpeed = participant.vehicle.CarData == null || participant.vehicle.CarData.topSpeed <= 0 ? 337f : participant.vehicle.CarData.topSpeed;
                // Rough real-world fraction of top speed a well-driven car
                // carries through each risk tier - deliberately simple and
                // clearly a reference/approximation, not a claim of physical
                // precision.
                float referenceFraction = corner.risk == TrackRuntime.CornerRisk.Low ? 0.90f : (corner.risk == TrackRuntime.CornerRisk.Medium ? 0.68f : 0.45f);
                float referenceSpeedKph = carTopSpeed * referenceFraction;
                float actualSpeedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);

                int index = (int)corner.risk;
                participant.cornerSpeedSumByRisk[index] += actualSpeedKph;
                participant.cornerReferenceSumByRisk[index] += referenceSpeedKph;
                participant.cornerSampleCountByRisk[index]++;
            }
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

        // Centralized driver-code resolution (career identity fix): every
        // consumer - race timing tower, qualifying tower, radio, standings,
        // track map labels, post-race classification - should resolve a
        // driver's displayed 3-letter code through this one function instead of
        // separately guessing from a full name. A real driver (AI, or the
        // player playing as a real driver) always uses their actual
        // DriverData.abbreviation; only a genuinely custom driver with no
        // matching DriverData falls back to parsing a name, and even then uses
        // the LAST name token (the real F1 convention - "PIA" for Oscar
        // Piastri), never the first three letters of the whole concatenated
        // name (the old bug, which produced "OSC").
        public string GetDisplayDriverCode(DriverData driver, string fallbackName)
        {
            if (driver != null && !string.IsNullOrEmpty(driver.abbreviation) && driver.abbreviation.Length >= 3)
            {
                return driver.abbreviation.Substring(0, 3).ToUpperInvariant();
            }

            string nameToParse = driver != null && !string.IsNullOrEmpty(driver.displayName) ? driver.displayName : fallbackName;
            if (!string.IsNullOrEmpty(nameToParse))
            {
                string[] parts = nameToParse.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    return CodeFromToken(parts[parts.Length - 1]);
                }
            }

            return "---";
        }

        string CodeFromToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return "---";
            }

            string upper = token.ToUpperInvariant();
            return upper.Length > 3 ? upper.Substring(0, 3) : upper.PadRight(3, '-');
        }

        // Legacy string-only entry point - now just delegates to
        // GetDisplayDriverCode so every existing caller automatically gets the
        // corrected last-name-token behavior above instead of the old
        // strip-spaces-then-first-three-characters logic.
        string DriverCode(string name)
        {
            return GetDisplayDriverCode(null, name);
        }

        // Part 21 team-performance-evolution hook: resolves whatever a team's
        // CarPerformanceData should actually be for a career race this season -
        // the shared static reference data plus that team's season-to-season
        // TeamPerformanceModifier (every team, applied evenly), plus the
        // player's own upgrade tuning on top if this is the player's own team.
        // Quick Race/Time Trial (IsCareerRace false) always get the raw,
        // unmodified reference car, exactly like before this system existed.
        CarPerformanceData ResolveTeamCarPerformance(TeamData team)
        {
            CarPerformanceData baseCar = team == null ? Data.Cars.cars[0] : Data.FindCar(team.carPerformanceId);
            if (IsCareerRace && Career != null && team != null)
            {
                return Career.GetEffectiveTeamCar(team, baseCar);
            }

            return baseCar;
        }

        // Career standings drift fix: this driver's CURRENT team, accounting for
        // any mid-career transfer (Career.Save.driverTransferRecords), not the raw
        // static DriverData.teamId from drivers.json. Every place that spawns a
        // grid/qualifying entry for an AI driver must resolve team through here -
        // using the raw teamId fed a transferred driver's race/qualifying result
        // (and hence ApplyConstructorPoints) the wrong constructor for the rest of
        // that season, which is exactly what let constructor standings drift away
        // from the sum of their drivers' points.
        TeamData ResolveDriverTeam(DriverData driver)
        {
            if (driver == null)
            {
                return null;
            }

            List<DriverTransferRecord> transfers = Career != null && Career.Save != null ? Career.Save.driverTransferRecords : null;
            string effectiveTeamId = Data.EffectiveTeamId(driver, transfers);
            TeamData team = Data.FindTeam(string.IsNullOrEmpty(effectiveTeamId) ? driver.teamId : effectiveTeamId);
            return team != null ? team : Data.FindTeam(driver.teamId);
        }

        void SpawnRaceGrid(string playerName, string playerTeamId, bool careerRace)
        {
            TeamData playerTeam = Data.FindTeam(playerTeamId);
            CarPerformanceData playerCar = ResolveTeamCarPerformance(playerTeam);

            // Without a usable qualifying result (the common quick-race path, since
            // quick race is never a career race) the player no longer defaults to
            // pole - the fallback itself is difficulty-scaled. AI fallback slots are
            // then built around whichever slot the player lands in so the two streams
            // can never collide.
            int playerGridFallback = CurrentSession == RaceWeekendSession.Qualifying ? 0 : ResolvePlayerGridFallback();
            // Career identity fix: this used to always pass null for the player's
            // DriverData, even when playing as a real driver (e.g. Oscar Piastri) -
            // RaceParticipant.driverData stayed null for the whole race, so the
            // timing tower/HUD/radio code (which all prefer driverData.abbreviation)
            // fell back to guessing a code from the display name instead of using
            // the real "PIA"-style abbreviation. ResolvePlayerQualifyingDriverData
            // already resolves the actual selected DriverData when one exists
            // (falling back to a synthesized one with a correctly-parsed
            // last-name-based abbreviation otherwise) - reused here for the real
            // race grid, not just the qualifying-sim path it was originally written
            // for.
            PlayerParticipant = SpawnParticipant(
                "player",
                playerName,
                playerTeam.id,
                playerTeam.shortName,
                true,
                ResolvePlayerQualifyingDriverData(playerName, playerTeamId),
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
                TeamData team = ResolveDriverTeam(driver);
                CarPerformanceData car = ResolveTeamCarPerformance(team);
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
                TeamData team = ResolveDriverTeam(driver);
                CarPerformanceData car = ResolveTeamCarPerformance(team);
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
                TeamData team = ResolveDriverTeam(driver);
                CarPerformanceData car = ResolveTeamCarPerformance(team);
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

            // Balance fix: driver skill used to be derived from the TEAM CAR's
            // own performance stats (cornering/enginePower/aero/braking), which
            // double-counted every car upgrade - once as a faster car via
            // carEffect in SimulateQualifyingRunDetailed, and again here as a
            // "better driver" via qualifying/pace/consistency, which is why
            // upgrades alone used to push the player toward pole almost every
            // simulated session. The car's contribution is already fully and
            // solely represented by carEffect; this rating now stands on its
            // own as a driver-skill number - a stable baseline nudged only by
            // career reputation (itself now a small, capped swing so it can
            // never compound into a second source of car-driven advantage),
            // never by the car underneath.
            float reputationBonus = Career == null || Career.Save == null ? 0f : Mathf.Clamp((Career.Save.reputation - 50f) * 0.10f, -6f, 6f);
            int qualifying = Mathf.Clamp(Mathf.RoundToInt(76f + reputationBonus + Random.Range(-3f, 3f)), 58, 90);
            int consistency = Mathf.Clamp(qualifying - 2 + (Career == null || Career.Save == null ? 0 : Career.Save.currentSeason), 55, 92);
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
            List<DriverData> teamDrivers = Data.GetDriversForTeam(playerTeamId, Career != null && Career.Save != null ? Career.Save.driverTransferRecords : null);

            // Teammate-duplicate fix: this used to trust Career.Save.selectedDriverId
            // completely whenever it was non-empty, with no validation - a save
            // whose selectedDriverId was ever empty/stale (a real possibility for
            // any career predating a stricter write-side fix, since nothing ever
            // repaired it on load) fell through to "the first driver on this
            // team", which is backwards: on a two-real-driver team that IS the
            // teammate's id, not the player's own. FindTeammateDriver then
            // excluded the teammate (mistaken for the player) and handed back
            // the player's OWN driver record as their "teammate" - the reported
            // bug of racing against a duplicate of yourself instead of your
            // real teammate. Now validated against this team's actual roster
            // before being trusted, with a same-name recovery path (and a
            // write-back repair) before ever falling back to "just pick one".
            if (Career != null && Career.Save != null && !string.IsNullOrEmpty(Career.Save.selectedDriverId) &&
                teamDrivers.Exists(d => d.id == Career.Save.selectedDriverId))
            {
                return Career.Save.selectedDriverId;
            }

            if (Career != null && Career.Save != null && !string.IsNullOrEmpty(Career.Save.playerDriverName))
            {
                DriverData byName = teamDrivers.Find(d => string.Equals(d.displayName, Career.Save.playerDriverName, System.StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                {
                    Career.Save.selectedDriverId = byName.id;
                    return byName.id;
                }
            }

            return teamDrivers.Count > 0 ? teamDrivers[0].id : "";
        }

        // Roster/participant construction, root-caused: the player occupies
        // exactly one seat on their team, identified purely by driver id
        // (Career.Save.selectedDriverId when playing as a real driver, or the
        // team's default seat id otherwise - see ReplacedDriverIdForPlayerTeam).
        // The teammate is, unconditionally, "the other driver registered to
        // that same team whose id is not the player's seat id" -
        // Data.GetAiRaceDrivers/FindTeammateDriver now guarantee that driver
        // is resolved and included by id before anything else fills the
        // field, so there is no fill-order or count arithmetic for the
        // teammate's presence to depend on. This is the one place all three
        // roster builders (race grid, live qualifying, sim qualifying) funnel
        // through; the loop below is a pure id-based safety net (never a
        // display-name comparison, which could misfire on a coincidental
        // name match) in case a stale save or future caller ever hands in a
        // roster that still contains the player's own seat id.
        List<DriverData> GetDefensiveAiRoster(string playerTeamId, string playerDisplayName)
        {
            List<DriverTransferRecord> transfers = Career != null && Career.Save != null ? Career.Save.driverTransferRecords : null;
            string replacedId = ReplacedDriverIdForPlayerTeam(playerTeamId);
            List<DriverData> aiDrivers = Data.GetAiRaceDrivers(playerTeamId, FullWeekendAiCount, replacedId, transfers);

            for (int i = aiDrivers.Count - 1; i >= 0; i--)
            {
                if (aiDrivers[i].id == replacedId)
                {
                    GameLog.Warn("[Roster] Removed AI driver '" + aiDrivers[i].displayName + "' (" + aiDrivers[i].id + ") - matches the player's own seat id.");
                    aiDrivers.RemoveAt(i);
                }
            }

            DriverData teammate = Data.FindTeammateDriver(playerTeamId, replacedId, transfers);
            if (teammate != null)
            {
                bool teammateIncluded = false;
                for (int j = 0; j < aiDrivers.Count; j++)
                {
                    if (aiDrivers[j].id == teammate.id)
                    {
                        teammateIncluded = true;
                        break;
                    }
                }

                if (!teammateIncluded)
                {
                    GameLog.Warn("[Roster] Teammate '" + teammate.displayName + "' (" + teammate.id + ") was missing from the AI roster - adding explicitly.");
                    if (aiDrivers.Count >= FullWeekendAiCount && aiDrivers.Count > 0)
                    {
                        aiDrivers.RemoveAt(aiDrivers.Count - 1);
                    }

                    aiDrivers.Insert(0, teammate);
                }
            }

            // Driver market + progression: this is the single chokepoint all
            // three roster builders (race grid, live qualifying, sim qualifying)
            // funnel through (see the comment above this method), so resolving
            // each driver to its effective (post-transfer, post-progression)
            // form here - never mutating the shared drivers.json objects -
            // covers all of them at once.
            if (Career != null)
            {
                for (int i = 0; i < aiDrivers.Count; i++)
                {
                    aiDrivers[i] = Career.GetEffectiveDriver(aiDrivers[i]);
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

            // Stale-grid fix: this used to try Career.Save.lastQualifyingResults
            // FIRST, regardless of round - that field is never cleared between
            // rounds, so if anything ever reached this path before a fresh
            // qualifying result existed for the CURRENT round, the race would
            // silently spawn using a previous round's grid instead of falling
            // back to the difficulty-based slot. The round/season-scoped
            // search is the only source of truth here now; lastQualifyingResults
            // (which is fine as a same-session "most recent" convenience for
            // UI display right after qualifying) is never consulted for this.
            List<QualifyingResultEntry> grid = null;
            if (Career != null && Career.Save != null && Career.Save.qualifyingResults != null)
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

            // Fuel system pass: session-specific start fuel instead of the old flat
            // 35kg for every session (see VehicleController.SetStartFuel). Player
            // reads their own pre-race choice off settings; AI gets its own
            // per-driver roll (ResolveAiFuelChoice) - most AI runs Target, same as
            // a sensible player default.
            float startFuelKg;
            float fuelPerLapKg = EstimateFuelPerLapKg(Track, Settings.Difficulty);
            if (IsTimeTrial)
            {
                startFuelKg = ComputeTimeTrialFuelKg();
            }
            else if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                startFuelKg = ComputeQualifyingFuelKg(Track);
            }
            else if (CurrentSession == RaceWeekendSession.Practice)
            {
                startFuelKg = ComputePracticeFuelKg(Track);
            }
            else
            {
                FuelLoadChoice fuelChoice = player ? (FuelLoadChoice)Settings.Current.fuelLoadChoice : ResolveAiFuelChoice(driver);
                participant.chosenFuelLoad = fuelChoice;
                startFuelKg = ComputeRaceStartFuelKg(Mathf.Max(3, Settings.Current.laps), fuelChoice, Track, Settings.Current);
            }

            controller.SetStartFuel(startFuelKg, fuelPerLapKg);
            controller.SetFuelBurnDisabled(IsTimeTrial);
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
            lapTracker.Initialize(Track, CurrentSession == RaceWeekendSession.Qualifying ? QualifyingSessionLapCap : RaceLaps);
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
                playerCameraRig = rig;
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
                Mathf.Abs(progress.lateralDistance) <= Track.HalfWidthAt(progress.distance) &&
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

        // Stuck-recovery escalation fix: AiVehicleController's own gentle
        // reverse/reorient maneuver (RecoveryManeuver in AiVehicleController.cs)
        // can genuinely fail to free a car wedged against a barrier, kerb, or
        // another car at a bad angle - and nothing previously escalated past
        // it, so a car that kept failing the same gentle maneuver could sit
        // there indefinitely (or only ever get removed by waiting out the
        // full StrandedRetireSeconds timer, well after it should have just
        // been able to keep racing). Once several real attempts have
        // genuinely failed - not one bad tick, a sustained pattern of still
        // barely moving after repeated tries - force a safe reposition to the
        // last known good on-track position/heading, the same
        // lastSafePosition mechanism HandleFallRespawn already uses for cars
        // that fall off an elevated section. Gated by both an attempt-count
        // floor and its own per-car cooldown so this is a genuine last
        // resort, never a repositioning loop, and it deliberately does NOT
        // register an incident/yellow flag itself - this is race control
        // quietly fixing a stuck car, not a new on-track event.
        const int StuckRepositionAttemptThreshold = 3;
        const float StuckRepositionCooldownSeconds = 25f;

        void HandleStuckEscalation(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || Track == null ||
                participant.retired || participant.finished || !participant.hasLastSafePosition ||
                participant.isRaceControlAutopilot || participant.isPitting || participant.pitPhase != PitPhase.None)
            {
                return;
            }

            participant.stuckRepositionCooldown = Mathf.Max(0f, participant.stuckRepositionCooldown - Time.deltaTime);
            if (participant.stuckRepositionCooldown > 0f)
            {
                return;
            }

            bool genuinelyStuck = (participant.recoveryState == CarRecoveryState.Recovering || participant.recoveryState == CarRecoveryState.ActuallyStranded) &&
                participant.recoveryAttemptCount >= StuckRepositionAttemptThreshold &&
                Mathf.Abs(participant.vehicle.CurrentSpeedKph) < 5f;

            if (!genuinelyStuck)
            {
                return;
            }

            Vector3 respawnPosition = participant.lastSafePosition + Vector3.up * 0.35f;
            Quaternion respawnRotation = participant.lastSafeRotation;
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

            participant.recoveryAttemptCount = 0;
            participant.stoppedOnTrackTimer = 0f;
            participant.wrongWayTimer = 0f;
            participant.stuckRepositionCooldown = StuckRepositionCooldownSeconds;
            participant.recoveryGraceTimer = RecoveryGraceSeconds;

            AiVehicleController ai = participant.GetComponent<AiVehicleController>();
            if (ai != null)
            {
                ai.ResyncAfterForcedReposition();
            }

            // recoveryAttemptCount only ever increments inside
            // AiVehicleController's own maneuver-complete callback, so this
            // path naturally never triggers for the player (no
            // AiVehicleController component) without needing an explicit
            // isPlayer guard here.
            GameLog.Warn("[RaceControl] " + participant.driverName + " force-repositioned after " + StuckRepositionAttemptThreshold + "+ failed recovery attempts (last resort).");
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

            // Performance fix: this was Realtime + EveryFrame - a full 6-face
            // cubemap re-render of the ENTIRE scene, every single frame, on
            // top of the main camera's own render. With a procedurally built
            // track carrying thousands of objects, this alone was enough to
            // crater the frame rate (users reporting ~20fps). A single
            // ViaScripting refresh right after the track/lighting finishes
            // building captures the same reflection once and never re-renders
            // it - correct for a track that doesn't change shape mid-race.
            GameObject probeObject = new GameObject("Runtime reflection probe");
            probeObject.transform.SetParent(raceWorld.transform);
            probeObject.transform.position = new Vector3(40f, 18f, 40f);
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.intensity = rainThreat ? 0.78f : 0.46f;
            probe.size = new Vector3(520f, 120f, 520f);
            probe.resolution = 128;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.RenderProbe();

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

        // Pit-lane stuck watchdog: pitting cars are deliberately excluded from the
        // general off-track/stranded incident detection (CarRecoveryState.PitSequence,
        // RaceManager.DetectIncidents) since that machinery assumes normal racing
        // physics, not a kinematic guided rail - but that also meant NOTHING was ever
        // watching for a pit-guided car that stops making real progress while actively
        // driving (Entry: turning in / queueing to box, Release: leaving the box /
        // merging out), whatever the underlying cause (a stale guide-distance
        // reference, a queue target that never resolves, or anything else no fix here
        // can fully rule out without live testing). Only watches the two DRIVING
        // sub-phases - Service itself is deliberately excluded, since sitting
        // stationary in the box for pitServiceDuration (or the release-gap hold) is
        // completely normal, not stuck.
        const float PitStuckProgressEpsilon = 1.5f;
        const float PitStuckTimeoutSeconds = 4.5f;
        const int PitStuckHardResetAfterAttempts = 3;

        void UpdatePitDrivingStuckWatchdog(RaceParticipant participant, TrackProgress currentProgress)
        {
            if (participant.pitPhase != PitPhase.Entry && participant.pitPhase != PitPhase.Release && participant.pitPhase != PitPhase.ExitMerge)
            {
                participant.pitStuckWatchdogTimer = 0f;
                participant.pitStuckLastDistance = -1f;
                participant.pitStuckRecoveryCount = 0;
                return;
            }

            // Authoritative progress source (bugfix): ExitMerge's own longitudinal
            // authority is pitGuideDistance (see UpdatePitExitMerge) - the cached
            // race-timing snapshot (currentProgress, from LapTracker/
            // RaceStateManager) can lag behind or briefly disagree with it, which
            // used to make a perfectly valid guided advance occasionally read as
            // "no progress" here. Entry/Release don't have their own authoritative
            // distance in the same sense (they chase a moving queue/box target via
            // AdvancePitGuideTarget), so they still watch the cached progress.
            float distance = participant.pitPhase == PitPhase.ExitMerge ? participant.pitGuideDistance : currentProgress.distance;
            if (participant.pitStuckLastDistance < 0f)
            {
                participant.pitStuckLastDistance = distance;
                participant.pitStuckWatchdogTimer = 0f;
                return;
            }

            float advanced = Track.WrapDistance(distance - participant.pitStuckLastDistance);
            if (advanced > Track.length * 0.5f)
            {
                advanced -= Track.length;
            }

            if (Mathf.Abs(advanced) >= PitStuckProgressEpsilon)
            {
                participant.pitStuckLastDistance = distance;
                participant.pitStuckWatchdogTimer = 0f;
                return;
            }

            participant.pitStuckWatchdogTimer += Time.deltaTime;
            if (participant.pitStuckWatchdogTimer < PitStuckTimeoutSeconds)
            {
                return;
            }

            participant.pitStuckWatchdogTimer = 0f;
            participant.pitStuckLastDistance = distance;
            participant.pitStuckRecoveryCount++;

            if (participant.pitStuckRecoveryCount >= PitStuckHardResetAfterAttempts)
            {
                // Nudging alone hasn't worked three times in a row - a genuine problem,
                // not a transient one. Fall back to an actual reposition onto a known-
                // good pose for whatever phase it's in, the true last resort, then
                // resync the AI's own progress reference exactly like every other
                // forced-reposition path in this file already does.
                if (participant.pitPhase == PitPhase.ExitMerge)
                {
                    // Single completion path (bugfix): this used to reposition the
                    // car a short distance further along the ramp and continue the
                    // SAME phase, hoping the normal completion checks would agree
                    // next tick - a separate, independently-timed recovery system
                    // from UpdatePitExitMerge's own distance/time failsafe, and the
                    // two could disagree about what "recovered" means (this is
                    // exactly the "freezes, briefly moves, freezes again" cycle).
                    // ExitMerge now has exactly one way to give up and finish -
                    // CompletePitExitMerge, force-completing immediately instead of
                    // repeatedly nudging the same broken phase.
                    participant.pitStuckRecoveryCount = 0;
                    ForcePitExitMergeCompletion(participant, "watchdog-timeout");
                    return;
                }

                Vector3 fallbackPosition;
                Quaternion fallbackRotation;
                if (participant.pitPhase == PitPhase.Entry)
                {
                    Track.GetPitServicePose(participant.pitBoxIndex, out fallbackPosition, out fallbackRotation);
                }
                else
                {
                    Track.GetPitReleasePose(participant.pitReleaseStagger, out fallbackPosition, out fallbackRotation);
                }

                participant.vehicle.SnapToPitPose(fallbackPosition, fallbackRotation);
                participant.hasPitGuideState = false;
                participant.pitStuckRecoveryCount = 0;
                AiVehicleController stuckAi = participant.GetComponent<AiVehicleController>();
                if (stuckAi != null)
                {
                    stuckAi.ResyncAfterForcedReposition();
                }

                GameLog.Warn("[PitLane] " + participant.driverName + " repeatedly stuck in " + participant.pitPhase + ", force-repositioned to a known-good pit-lane pose.");
                return;
            }

            if (participant.pitPhase == PitPhase.ExitMerge)
            {
                // No-op for ExitMerge specifically (bugfix): hasPitGuideState no
                // longer has any effect on this phase - pitGuideDistance/
                // pitGuideLateral are the sole authority regardless of that flag
                // (see UpdatePitExitMerge) - and a stall here is frequently a
                // legitimate, temporary occupancy hold (FindPitExitCarAhead /
                // IsPitExitMergeSpaceOccupied), not a stuck car. Just note it;
                // the hard-reset branch above is the one place ExitMerge actually
                // acts, once a stall has genuinely persisted.
                GameLog.Warn("[PitLane] " + participant.driverName + " made no ExitMerge guide progress for " + PitStuckTimeoutSeconds.ToString("0.0") + "s (attempt " + participant.pitStuckRecoveryCount + ") - likely a held occupancy gap.");
                return;
            }

            // Gentle recovery: re-seed the guide waypoint fresh from wherever the car
            // actually is right now (the same reset AdvancePitGuideTarget already uses
            // at the start of every new phase), which drops whatever stale distance/
            // lateral chase state was holding it in place and lets it resume closing
            // the gap toward the current target - the "next valid pit-lane path point"
            // - under its own normal guided pace next tick, without ever teleporting.
            participant.hasPitGuideState = false;
            GameLog.Warn("[PitLane] " + participant.driverName + " made no progress in " + participant.pitPhase + " for " + PitStuckTimeoutSeconds.ToString("0.0") + "s, nudged back onto the pit-lane path (attempt " + participant.pitStuckRecoveryCount + ").");
        }

        void HandlePitService(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null)
            {
                return;
            }

            UpdateMissedPitEntryReset(participant);

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
                        participant.vehicle.SetPitExitFastLimiter(false);
                    }
                }
                else
                {
                    participant.vehicle.SetPitLimiter(false);
                    participant.vehicle.ClearPitRequest();
                }

                return;
            }

            UpdatePitDrivingStuckWatchdog(participant, currentProgress);

            if (participant.pitLimiterUntilExit)
            {
                participant.vehicle.SetPitLimiter(true);
                if (!Track.IsInPitExitLimiterZone(normalized))
                {
                    participant.pitLimiterUntilExit = false;
                    participant.vehicle.SetPitLimiter(false);
                    participant.vehicle.SetPitExitFastLimiter(false);
                    if (participant.isPlayer)
                    {
                        SessionMessage = "Pit exit: limiter off, resume racing speed";
                        PostEngineerMessage("Pit exit. Limiter off. Resume racing speed.", true);
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

            if (participant.pitPhase == PitPhase.ExitMerge)
            {
                UpdatePitExitMerge(participant);
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

            // Limiter-consistency bugfix: this used to fire "Pit request
            // confirmed... limiter is 80 km/h" the instant the car entered the
            // broad IsInPitApproach zone (0.78) - roughly 7% of a lap before the
            // hard limiter (below) actually engaged, telling the player they were
            // limited before they were. pitApproach itself is kept only for the
            // informational "Pit entry approaching" fallback text further down;
            // the limiter-confirmation message now fires at the same shared
            // boundary (Track.HasCrossedPitEntryLimiterLine) that actually
            // engages PitLimiterActive, for player and AI alike.
            bool pitApproach = Track.IsInPitApproach(normalized);

            // Pit-entry timing fix: physical commit decisions (whether a car is "on
            // the ramp", and the missed-entry cutoff) now key off the REAL physical
            // ramp window (Track.IsInPitEntryRampWindow, 0.850-0.885) instead of the
            // broader approach/HUD zone (IsInPitEntryZone, 0.865-0.955). The old zone
            // both started (0.865) after roughly 43% of the real 0.850-0.885 opening
            // had already passed AND kept accepting a commit all the way to 0.955,
            // long after the ramp had fully flattened into the corridor and the
            // divider wall (TrackManager.BuildPitLaneDividerFence, which starts
            // exactly at PitCorridorStartNormalized) had already begun - AI cars were
            // reaching the wall and bouncing back onto the main straight instead of
            // ever being recognized as having entered. Also re-samples the car's own
            // ACTUAL current transform position fresh here (Track.GetProgressNear),
            // not the cached/timing-snapshot progress used elsewhere for steering, so
            // the commit decision is judged against where the car genuinely,
            // physically is right now.
            bool inPitEntryRampWindow = Track.IsInPitEntryRampWindow(normalized);
            if (inPitEntryRampWindow)
            {
                TrackProgress actualProgress = Track.GetProgressNear(participant.transform.position, currentProgress.distance);
                // Limiter-consistency fix: HasCrossedPitEntryLimiterLine is the ONE
                // shared boundary function every pit-entry-limiter consumer now
                // calls (the painted line/sign, this hard-limiter activation, and
                // the engineer message below) - it also folds in lateral position
                // (via IsOnPitEntryRamp), so a car with a pit request simply
                // passing this longitudinal point on the ordinary racing surface,
                // without actually steering onto the ramp, never trips it.
                bool crossedLimiterLine = Track.HasCrossedPitEntryLimiterLine(actualProgress);
                // Hard-limiter timing fix: this used to engage across the whole
                // broad IsInPitApproach zone (0.78-0.955) the instant a pit request
                // existed, forcing cars to crawl at 80 km/h through ordinary racing
                // corners long before they had any physical path toward the ramp.
                // BeginPitEntry (below) takes over limiter ownership for the rest
                // of the stop, so this only needs to cover the moment of commit.
                participant.vehicle.SetPitLimiter(crossedLimiterLine);
                if (crossedLimiterLine)
                {
                    if (participant.isPlayer && !engineerPitRequestConfirmed)
                    {
                        engineerPitRequestConfirmed = true;
                        PostEngineerMessage("Pit request confirmed. Slow for pit entry, limiter is 80 km/h.", true, RaceAudioCue.PitConfirm);
                    }

                    BeginPitEntry(participant);
                }
                else if (participant.isPlayer)
                {
                    SessionMessage = "Pit entry: steer right to commit";
                }

                if (!participant.isPlayer)
                {
                    GameLog.Info("[PitEntry] " + participant.driverName +
                                 " normalized=" + normalized.ToString("0.000") +
                                 " lateral=" + actualProgress.lateralDistance.ToString("0.00") +
                                 " halfWidth=" + Track.HalfWidthAt(actualProgress.distance).ToString("0.00") +
                                 " onRamp=" + crossedLimiterLine +
                                 " beganEntry=" + crossedLimiterLine);
                }
            }
            else if (normalized > TrackRuntime.PitCorridorStartNormalized)
            {
                // Past the real physical opening without ever committing - the
                // divider wall has already begun by here, so there is no longer a
                // physical path through. Mark the stop missed and retry next lap
                // instead of continuing to steer the car toward a wall until some
                // much later, physically meaningless deadline (this used to wait
                // until 0.952, ~7% of a lap after the real opening had already
                // closed). Recording the lap this happened on lets
                // UpdateMissedPitEntryReset clear the flag only once the car has
                // genuinely started a new lap, instead of an automatic trigger
                // (tyre wear, strategy lap, damage, undercut, VSC/SC) re-arming
                // PitRequested moments later while still inside the broad zone.
                participant.missedPitEntryThisLap = true;
                participant.missedPitEntryCompletedLap = participant.lapTracker.CompletedLaps;
                participant.vehicle.ClearPitRequest();
                participant.vehicle.SetPitLimiter(false);
                participant.pitAutoTriggered = false;
                // The request never reached the commitment boundary, so it was
                // never actually cancellable in the first place (missedPitEntryThisLap
                // itself is what prevents it silently re-arming) - but the
                // request itself is gone now, so the tracking that describes it
                // must go with it rather than lingering as a stale "still
                // queued" state.
                ClearManualPitRequestTracking(participant);
                if (participant.isPlayer)
                {
                    SessionMessage = "Pit entry missed. We'll box next lap.";
                    PostEngineerMessage("Pit entry missed. We'll box next lap.", true);
                }
                else
                {
                    GameLog.Warn("[PitLane] " + participant.driverName + " missed pit entry; retrying next lap.");
                }
            }
            else
            {
                participant.vehicle.SetPitLimiter(false);
                if (participant.isPlayer)
                {
                    SessionMessage = pitApproach ? "Pit entry approaching" : "Pit request queued";
                }
            }
        }

        // Deterministic-deadlock fix: missedPitEntryThisLap now actually gates
        // every automatic pit-request source (see AiVehicleController's
        // command.pitRequest suppression, and the guards added to
        // UpdatePlayerAutoPitStrategy/AcceptRaceControlPitOffer below), so it
        // must be cleared reliably once the miss is genuinely behind the car -
        // not merely once time or distance has passed, but once CompletedLaps
        // has advanced past the lap the miss was recorded on, i.e. the car has
        // crossed the line and physically started a fresh lap with a fresh
        // shot at the real opening.
        void UpdateMissedPitEntryReset(RaceParticipant participant)
        {
            if (!participant.missedPitEntryThisLap || participant.lapTracker == null)
            {
                return;
            }

            if (participant.lapTracker.CompletedLaps > participant.missedPitEntryCompletedLap)
            {
                participant.missedPitEntryThisLap = false;
                participant.missedPitEntryCompletedLap = -1;
            }
        }

        void BeginPitEntry(RaceParticipant participant)
        {
            participant.pitPhase = PitPhase.Entry;
            participant.pitEntryAligned = false;
            participant.pitEntryCommitted = true;
            // Cancellable-manual-pit-stop fix: this is the authoritative
            // commitment boundary the whole pit state machine already uses
            // (Track.HasCrossedPitEntryLimiterLine, checked just before this is
            // called) - a manual/SC-offer request becomes permanently
            // uncancellable the instant it's crossed, never merely on the next
            // pitPhase read, so CanCancelManualPitRequest can never race a
            // same-frame commit.
            if (participant.activePitRequestSource == PitRequestSource.Manual ||
                participant.activePitRequestSource == PitRequestSource.SafetyCarPrompt)
            {
                participant.manualPitCommitted = true;
            }

            participant.manualPitRequested = false;
            participant.missedPitEntryThisLap = false;
            participant.isPitting = true;
            participant.pitLimiterUntilExit = false;
            participant.pitAwaitingRelease = false;
            participant.pitTimer = 0f;
            participant.pitServiceDuration = 0f;
            participant.nextPitCompound = participant.requestedPitCompoundSet ? participant.requestedPitCompound : NextPlannedPitCompoundFor(participant);
            participant.pitTyreSelectionActive = false;
            participant.vehicle.SetPitLimiter(true);
            // Defensive reset: a fresh entry must always start at the cautious
            // entry-grade cap, never carry over the previous stop's exit-grade
            // one.
            participant.vehicle.SetPitExitFastLimiter(false);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitGuidance(true);
            participant.vehicle.ClearPitRequest();
            // Pit lane animation fix: start the waypoint model fresh from
            // wherever the car actually is right now, not leftover state from
            // a previous stop earlier in the race.
            participant.hasPitGuideState = false;
            if (participant.isPlayer)
            {
                SessionMessage = "Pit entry: limiter active";
                PostEngineerMessage("Pit entry. Hold steady, box " + (participant.pitBoxIndex + 1) + " is ready with " + participant.nextPitCompound + ".", true);
            }
        }

        // Pit lane animation fix (core): every pit-guided car chases a
        // (distance-along-track, lateral-offset) waypoint that advances a
        // bounded step toward the phase's true target each tick, instead of
        // GuideToPitPose being aimed directly at a single far-away fixed pose.
        // Sampling the advancing waypoint through Track.SamplePitLanePose (the
        // same SampleAtDistance the whole track/pit lane surface is built
        // from) means the path always follows the track/pit lane's own
        // curvature; the bounded lateral rate is what turns a lane change
        // into a gradual diagonal peel instead of an instant sideways snap.
        const float PitEntryPaceKph = 68f;
        const float PitLanePaceKph = 58f;
        // Pit-exit speed fix: raised from 74 so the guided release phase hands
        // off smoothly into VehicleController's own faster post-release cap
        // (PitExitLimiterCapKph, 108) instead of the car appearing to
        // accelerate abruptly the instant guidance ends - kept a few kph below
        // that real cap so the free-driving handoff is a gentle pickup, not a
        // jump.
        const float PitReleasePaceKph = 100f;
        const float PitGuideLateralRateMetersPerSecond = 9f;
        // Generous relative to the above paces so GuideToPitPose's own
        // MoveTowards/RotateTowards - now chasing a waypoint only a fraction
        // of a metre away each frame rather than one potentially 100+m away -
        // closes that tiny gap immediately instead of visibly lagging behind
        // the logical waypoint it's supposed to be tracking.
        const float PitGuideChaseSpeed = 45f;
        const float PitGuideChaseRotateSpeed = 260f;

        void AdvancePitGuideTarget(RaceParticipant participant, float targetDistance, float targetLateral, float paceKph, out Vector3 position, out Quaternion rotation)
        {
            if (!participant.hasPitGuideState)
            {
                // Lap-counter/floating-pit-lane fix: this used to be a blind
                // Track.GetProgress(transform.position) - a global nearest-
                // centerline-point search with no continuity bias. A car deep
                // in the pit corridor sits tens of metres off the centerline
                // (see Runtime.PitLaneLateral/PitOuterLateral), and on a track
                // where the pit lane runs close and parallel to another part
                // of the circuit (Silverstone's long pit straight especially),
                // that search can occasionally lock onto the wrong nearby
                // segment - sending the guide waypoint the wrong way down the
                // track (read by the driver as "floating to a random place")
                // and, since the same ambiguity can also perturb the lap
                // tracker's own progress for a frame right near the start/
                // finish line the pit exit sits so close to, corrupting the
                // checkpoint count. Seeding from the already continuity-
                // tracked lap-tracker progress (the same source HandlePitService
                // itself trusts) removes the ambiguity entirely.
                TrackProgress current = State != null ? State.GetCurrentProgress(participant) : Track.GetProgress(participant.transform.position);
                participant.pitGuideDistance = current.distance;
                participant.pitGuideLateral = current.lateralDistance;
                participant.hasPitGuideState = true;
            }

            float remaining = Track.WrapDistance(targetDistance - participant.pitGuideDistance);
            if (remaining > Track.length * 0.5f)
            {
                remaining -= Track.length;
            }

            // Forward-only fix: a negative "remaining" used to be allowed to step
            // the guide waypoint backward by the same clamp that let it step
            // forward. Combined with a target that could itself land behind the
            // car (see GetPitEntryHoldPose), that made the guided waypoint - and
            // the car chasing it - visibly reverse down the pit lane/entry, one of
            // the deadlock's contributing causes. Pit-guided cars never legitimately
            // need to back up, so any backward remaining now just holds position
            // instead of reversing toward it.
            float maxStep = Mathf.Max(0.01f, paceKph / 3.6f * Time.deltaTime);
            if (remaining < 0f)
            {
                GameLog.Warn("[PitGuide] " + participant.driverName + " target is behind current guide position (remaining=" +
                             remaining.ToString("0.0") + "); holding instead of reversing.");
                remaining = 0f;
            }

            float step = Mathf.Clamp(remaining, 0f, maxStep);
            participant.pitGuideDistance = Track.WrapDistance(participant.pitGuideDistance + step);
            participant.pitGuideLateral = Mathf.MoveTowards(participant.pitGuideLateral, targetLateral, PitGuideLateralRateMetersPerSecond * Time.deltaTime);
            Track.SamplePitLanePose(participant.pitGuideDistance, participant.pitGuideLateral, out position, out rotation);
        }

        bool IsPitGuideNear(RaceParticipant participant, float targetDistance, float targetLateral, float distanceTolerance, float lateralTolerance)
        {
            float delta = Mathf.Abs(Track.WrapDistance(targetDistance - participant.pitGuideDistance));
            if (delta > Track.length * 0.5f)
            {
                delta = Track.length - delta;
            }

            return delta <= distanceTolerance && Mathf.Abs(participant.pitGuideLateral - targetLateral) <= lateralTolerance;
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
                Vector3 entryTargetPosition;
                Quaternion entryTargetRotation;
                if (entryBlocker != null)
                {
                    GetPitEntryHoldPose(participant, out entryTargetPosition, out entryTargetRotation);
                }
                else
                {
                    Track.GetPitEntryPose(out entryTargetPosition, out entryTargetRotation);
                }

                TrackProgress entryTargetProgress = Track.GetProgress(entryTargetPosition);
                participant.vehicle.SetPitLimiter(true);
                participant.vehicle.SetPitServiceHold(true);
                Vector3 entryWaypoint;
                Quaternion entryWaypointRotation;
                AdvancePitGuideTarget(participant, entryTargetProgress.distance, entryTargetProgress.lateralDistance, PitEntryPaceKph, out entryWaypoint, out entryWaypointRotation);
                participant.vehicle.GuideToPitPose(entryWaypoint, entryWaypointRotation, PitGuideChaseSpeed, PitGuideChaseRotateSpeed);
                if (participant.isPlayer)
                {
                    SessionMessage = entryBlocker != null ? "Pit entry: holding for the car ahead" : "Pit entry: turning into lane";
                }

                if (entryBlocker != null || !IsPitGuideNear(participant, entryTargetProgress.distance, entryTargetProgress.lateralDistance, 1.5f, 0.35f))
                {
                    return;
                }

                participant.vehicle.SnapToPitPose(entryTargetPosition, entryTargetRotation);
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

            TrackProgress serviceTargetProgress = Track.GetProgress(servicePosition);
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitServiceHold(true);
            Vector3 serviceWaypoint;
            Quaternion serviceWaypointRotation;
            AdvancePitGuideTarget(participant, serviceTargetProgress.distance, serviceTargetProgress.lateralDistance, PitLanePaceKph, out serviceWaypoint, out serviceWaypointRotation);
            participant.vehicle.GuideToPitPose(serviceWaypoint, serviceWaypointRotation, PitGuideChaseSpeed, PitGuideChaseRotateSpeed);
            if (participant.isPlayer)
            {
                SessionMessage = blocking != null ? "Pit lane: queueing for box " + (participant.pitBoxIndex + 1) : "Pit lane: rolling to box " + (participant.pitBoxIndex + 1);
            }

            if (blocking == null && IsPitGuideNear(participant, serviceTargetProgress.distance, serviceTargetProgress.lateralDistance, 0.8f, 0.3f))
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
            // Continuity-tracked progress (same fix as AdvancePitGuideTarget
            // above) instead of a blind global nearest-point search, which can
            // mis-project a car deep in the pit corridor onto the wrong nearby
            // segment and scramble queue ordering.
            TrackProgress own = GetPitAwareProgress(participant);
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

                TrackProgress otherProgress = GetPitAwareProgress(other);
                float aheadBy = Track.WrapDistance(otherProgress.distance - own.distance);
                if (aheadBy > 0.5f && aheadBy < 13f && otherProgress.distance <= ownTarget + 1f)
                {
                    return other;
                }
            }

            return null;
        }

        // Shared "where is this car right now, along the track" lookup for
        // every pit-lane function that needs a participant's OWN current
        // progress - always the already continuity-tracked lap-tracker value
        // (see AdvancePitGuideTarget) rather than a fresh ambiguous global
        // nearest-point search, so pit-lane queueing/guidance and the lap
        // counter can never disagree about where a car actually is.
        TrackProgress GetPitAwareProgress(RaceParticipant participant)
        {
            if (participant == null)
            {
                return new TrackProgress();
            }

            return State != null ? State.GetCurrentProgress(participant) : Track.GetProgress(participant.transform.position);
        }

        // Queue-target fix: this used to derive the holdback purely from the
        // participant's OWN current distance to its OWN box - never actually
        // looking at where the blocking car in front of it really is. Two cars
        // queueing for boxes at different distances down the lane would each
        // compute a different, own-box-relative number, but neither number
        // described "how far behind the car actually ahead of me should I
        // wait" - so a trailing car could end up targeting a queue point that
        // was already occupied, sitting right on top of (or past) the real
        // blocker with nothing left to close, which reads as the car just
        // stopping dead / going nowhere even though it's "queueing". Basing
        // the holdback on the real live gap to the blocking car's own current
        // position keeps every queued car a consistent distance behind
        // whoever is actually ahead of it, regardless of box index.
        float PitQueueHoldback(RaceParticipant participant, RaceParticipant blocking)
        {
            float blockingDistance = GetPitAwareProgress(blocking).distance;
            float target = Track.PitBoxDistance(participant.pitBoxIndex);
            return Mathf.Clamp(Track.WrapDistance(target - blockingDistance) + 8f, 8f, 60f);
        }

        // Any other car still occupying the shared pit-entry coordinate: either
        // approaching it (Entry, not yet aligned) or having only just aligned onto it
        // and not yet moved on toward its own box. Gated on real proximity to the
        // entry point itself, not just phase, so the hold clears the moment the
        // leader has actually moved away from that specific spot.
        RaceParticipant FindPitEntryCarAhead(RaceParticipant participant)
        {
            float entryTarget = Track.length * TrackRuntime.PitCorridorStartNormalized;
            TrackProgress own = GetPitAwareProgress(participant);
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant || other.vehicle == null || other.pitPhase != PitPhase.Entry)
                {
                    continue;
                }

                TrackProgress otherProgress = GetPitAwareProgress(other);
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
        //
        // Pit-lane architecture fix: this used to place the hold point at a flat
        // Track.roadHalfWidth + 5.6 offset, an arbitrary lateral disconnected from
        // the real built ramp/corridor surface. It now samples the same canonical
        // envelope every other pit-entry pose uses (PitEntryPathLateral, which
        // reads GetPitEntryRampEnvelope and gracefully covers both the approach
        // and the ramp itself). It also used to be able to compute a hold distance
        // BEHIND the car's own current position (entryTarget - holdback, with no
        // floor at ownDistance) whenever the car was already close to the entry
        // target - since AdvancePitGuideTarget only ever steps forward, that sent
        // the car after a target it could never reach without going backward, one
        // of the deadlock's contributing causes. The hold distance is now always
        // clamped forward to at least the car's current position.
        void GetPitEntryHoldPose(RaceParticipant participant, out Vector3 position, out Quaternion rotation)
        {
            float entryTarget = Track.length * TrackRuntime.PitCorridorStartNormalized;
            float ownDistance = GetPitAwareProgress(participant).distance;
            float holdback = Mathf.Clamp(entryTarget - ownDistance + 8f, 8f, 45f);
            float holdDistance = Mathf.Max(ownDistance, entryTarget - holdback);
            Track.SamplePitLanePose(holdDistance, Track.PitEntryPathLateral(holdDistance), out position, out rotation);
        }

        void BeginPitStop(RaceParticipant participant)
        {
            participant.pitPhase = PitPhase.Service;
            participant.isPitting = true;
            participant.pitAwaitingRelease = false;
            // Tyre-change animation pass: a real stop is over in a few
            // seconds, not four-plus - matched to the visible wheel-off/
            // wheel-on animation below (VehicleVisuals.BeginPitStopVisual)
            // so the timer and the animation always finish together.
            participant.pitServiceDuration = participant.isPlayer ? Random.Range(1.8f, 2.6f) : Random.Range(2.0f, 3.0f);
            participant.pitTimer = participant.pitServiceDuration;
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.ClearPitRequest();
            VehicleVisuals visuals = participant.GetComponent<VehicleVisuals>();
            if (visuals != null)
            {
                visuals.BeginPitStopVisual(participant.pitServiceDuration);
            }

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

            // Spatial exit-occupancy gate (bugfix): the time gap above only ever
            // throttled how OFTEN a car could be admitted to Release - it said
            // nothing about whether the exit path itself was actually clear. A
            // car stuck or simply slow anywhere in Release/ExitMerge still let
            // the timer expire on schedule, so the next car released straight
            // onto (and, being non-colliding, straight through) whatever was
            // still occupying the lane ahead of it. A following car now stays in
            // its box - the timer is not consumed - until it has both a time gap
            // AND a real spatial gap to whoever is actually closest ahead of it.
            RaceParticipant exitBlocker = FindPitExitCarAhead(participant, Track.PitBoxDistance(participant.pitBoxIndex));
            if (exitBlocker != null)
            {
                participant.pitTimer = 0f;
                participant.pitAwaitingRelease = true;
                if (participant.isPlayer)
                {
                    SessionMessage = "Held in box: pit exit occupied";
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
            participant.pitAutoTriggered = false;
            // The stop this request described is now actually complete - clear
            // the source tag so a stale "committed manual stop" can never leak
            // into how the next lap's pit-request state reads.
            ClearManualPitRequestTracking(participant);
            participant.pitTimer = 0f;
            // The release gap only throttles how often a new car is admitted to
            // Release - a car already guided toward the (single) release point can
            // still be mid-transit when the next one is let go. Stagger each car's
            // actual release target by how many others are already in Release so two
            // of them never both guide onto, then snap onto, the identical point.
            participant.pitReleaseStagger = CountParticipantsInPitPhase(PitPhase.Release);
            participant.pitPhase = PitPhase.Release;
            participant.pitServiceDuration = 0f;
            // Pit lane animation fix: re-seed the waypoint fresh from the box
            // position (defensive - float drift over a multi-second
            // stationary stop should never accumulate, but this guarantees
            // release starts exactly where the car actually is).
            participant.hasPitGuideState = false;
            if (participant.isPlayer)
            {
                SessionMessage = "Pit release: limiter active";
                PostEngineerMessage("Stop complete. Release, limiter remains active until pit exit.", true, RaceAudioCue.PitConfirm);
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
            TrackProgress releaseTargetProgress = Track.GetProgress(releasePosition);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitLimiter(true);
            Vector3 releaseWaypoint;
            Quaternion releaseWaypointRotation;
            AdvancePitGuideTarget(participant, releaseTargetProgress.distance, releaseTargetProgress.lateralDistance, PitReleasePaceKph, out releaseWaypoint, out releaseWaypointRotation);
            participant.vehicle.GuideToPitPose(releaseWaypoint, releaseWaypointRotation, PitGuideChaseSpeed, PitGuideChaseRotateSpeed);
            if (participant.isPlayer)
            {
                SessionMessage = "Pit release: merging back onto the racing line";
            }

            if (!IsPitGuideNear(participant, releaseTargetProgress.distance, releaseTargetProgress.lateralDistance, 0.8f, 0.3f))
            {
                return;
            }

            // Pit-exit path fix: this used to hand the car straight back to normal
            // racing-line targeting right here (pitPhase = None, SetPitGuidance(false))
            // the instant it reached the release pose - AiVehicleController had no
            // idea it had just merged out of the pit lane and immediately steered
            // for the racing line/next apex, cutting across the pit-exit lane ~20m
            // before the real merge point. Snaps to the release pose and hands off
            // to PitPhase.ExitMerge instead, which keeps the car under the SAME
            // kinematic guidance (SetPitGuidance is deliberately NOT turned off
            // here - it stays true, continuing straight into UpdatePitExitMerge)
            // along the pit-exit lane until it genuinely clears the merge zone.
            participant.vehicle.SnapToPitPose(releasePosition, releaseRotation);
            // Continuity fix: ExitMerge's whole guided path (see UpdatePitExitMerge)
            // is driven by pitGuideDistance alone, so it must start from the exact
            // release pose just sampled and snapped to above - not from
            // LapTracker/RaceStateManager's cached progress, and not from a fresh
            // global nearest-track search next tick, either of which can resolve to
            // the wrong nearby segment (most dangerously right at the start/finish
            // wrap, or anywhere the pit lane runs close and parallel to another part
            // of the circuit). hasPitGuideState is intentionally left true here
            // (previous pit-lane-animation fixes cleared it at every OTHER phase
            // transition specifically so the guide reseeds fresh from wherever the
            // car actually is - that reasoning does not apply here, because the
            // guide distance already IS a known-correct value: the one just used to
            // place the car).
            participant.pitGuideDistance = releaseTargetProgress.distance;
            participant.pitGuideLateral = releaseTargetProgress.lateralDistance;
            participant.hasPitGuideState = true;
            participant.pitPhase = PitPhase.ExitMerge;
            // Distance-based merge tracking (round 2 fix): a staggered release can
            // land meaningfully before the fixed normalized merge-zone boundary, so
            // record this car's own actual release distance and the real ramp end
            // distance here - UpdatePitExitMerge measures completion against these,
            // not just the shared fixed zone.
            participant.pitExitMergeStartDistance = releaseTargetProgress.distance;
            participant.pitExitMergeEndDistance = Track.length * TrackRuntime.PitExitRampEndNormalized;
            participant.pitExitMergeElapsedTime = 0f;
            participant.vehicle.SetPitServiceHold(false);
            participant.vehicle.SetPitLimiter(true);
            // Pit-exit speed fix: from this point on the car is driving down an
            // already-cleared stretch of pit lane with nothing left to be
            // cautious of - switch to the higher exit cap instead of the
            // entry-grade one so it doesn't crawl. Speed discipline
            // (pitLimiterUntilExit) is intentionally independent of path
            // discipline (pitPhase == ExitMerge) - it already clears itself once
            // Track.IsInPitExitLimiterZone goes false, in HandlePitService above.
            participant.vehicle.SetPitExitFastLimiter(true);
            SimpleAudioManager.PlayPitRelease(releasePosition);
            participant.pitAwaitingRelease = false;
            participant.pitLimiterUntilExit = true;
            if (participant.isPlayer)
            {
                SessionMessage = "Pit release: merging onto the racing line";
            }
        }

        // Pit-exit path fix: guides the car along the pit-exit lane from the
        // release pose through the actual merge point, instead of handing it
        // straight back to normal racing-line targeting at the release pose (see
        // UpdatePitRelease above for why that read as "turns onto the track too
        // early"). Chases a rolling waypoint a short distance ahead - the same
        // "always target a point just ahead, not a single fixed destination"
        // approach AdvancePitGuideTarget already uses elsewhere - whose lateral
        // offset blends from the pit-exit-lane position down toward a safe legal
        // track offset as Track.PitExitMergeBlend fades from 1 (at release) to 0
        // (at the real merge end, the same boundary the physical pit-exit barrier
        // geometry itself uses - see TrackManager.PitZoneExitRampEnd), so the car
        // visibly follows the lane instead of cutting a diagonal to the apex.
        const float PitExitMergePaceKph = 100f;
        const float PitExitMergeLookaheadMeters = 12f;
        const float PitExitMergeCompletionBufferMeters = 10f;
        const float PitExitLaneHoldSeconds = 1.5f;
        const float PitExitLaneHoldDistanceMeters = 40f;

        // Wrapped forward-only distance from one track distance to another,
        // always >= 0 and always measured going the direction of travel (never the
        // shorter of the two wrap directions) - used to measure a car's own actual
        // progress through the pit-exit merge regardless of where its staggered
        // release distance happened to land relative to the fixed merge-zone
        // boundary.
        float WrappedForwardDistance(float fromDistance, float toDistance)
        {
            float delta = toDistance - fromDistance;
            if (delta < 0f)
            {
                delta += Track.length;
            }

            return delta;
        }

        // Defensive escape (bugfix): ExitMerge must never be able to continue for a
        // whole lap. If the car has traveled well beyond the ramp end and still
        // hasn't cleared the completion checks (e.g. the guide chase lagged, or it
        // got knocked/collided off the intended line), gently snap it onto a known-
        // legal on-track pose and force the merge to finish rather than let it keep
        // following the pit-exit path indefinitely.
        const float PitExitMergeHardEscapeBufferMeters = 60f;


        // Longitudinal agreement tolerance for the ExitMerge completion check
        // below: how far the car's own continuity-constrained physical
        // projection (Track.GetProgressNear, anchored to pitGuideDistance) is
        // allowed to disagree with pitGuideDistance itself before the merge is
        // refused. A collision, a stuck-watchdog nudge, or simple guide lag can
        // all open a small gap; anything larger means the car's real position
        // and the path state have genuinely diverged and the merge must wait.
        const float PitExitMergeLongitudinalToleranceMeters = 6f;

        // A car counts as "in the way" of a candidate release/merge if it is
        // this close ahead along the shared exit path - used both to hold a
        // Service car back from entering Release (see UpdatePitService) and,
        // defensively, as part of ExitMerge's own per-tick state.
        const float PitExitSafeForwardGapMeters = 25f;

        // Real spatial exit-occupancy check (bugfix): nextPitReleaseAllowedTime/
        // PitReleaseGapSeconds only ever throttled releases by TIME. If the
        // preceding car was still slow, blocked, or genuinely stuck anywhere in
        // Release or ExitMerge, the timer still expired on schedule and let the
        // next car guide onto the same non-colliding stretch of lane the first
        // car was still occupying - the two then overlapped and piled up right
        // at the exit. A candidate release now also needs a real forward gap to
        // whichever car is actually closest ahead of it along the shared exit
        // path (measured in pitGuideDistance, never a global world-space
        // nearest-track search), not just an elapsed-time gap.
        RaceParticipant FindPitExitCarAhead(RaceParticipant participant, float ownGuideDistance)
        {
            RaceParticipant closest = null;
            float closestGap = float.MaxValue;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant || other.vehicle == null)
                {
                    continue;
                }

                if (other.pitPhase != PitPhase.Release && other.pitPhase != PitPhase.ExitMerge)
                {
                    continue;
                }

                float otherDistance = other.hasPitGuideState ? other.pitGuideDistance : Track.PitBoxDistance(other.pitBoxIndex);
                float aheadBy = WrappedForwardDistance(ownGuideDistance, otherDistance);
                if (aheadBy < closestGap)
                {
                    closestGap = aheadBy;
                    closest = other;
                }
            }

            return closestGap <= PitExitSafeForwardGapMeters ? closest : null;
        }

        // How far before the real merge point (pitExitMergeEndDistance) live
        // racing traffic starts to matter. Before this window the exiting car is
        // still on its own physically separate ramp/corridor, so no live car can
        // actually be in the way yet.
        const float PitExitMergeLiveTrafficCheckLeadMeters = 20f;
        const float PitExitLiveTrafficForwardGapMeters = 18f;
        const float PitExitLiveTrafficRearGapMeters = 35f;

        // Explicit live-traffic occupancy check (bugfix): ExitMerge's kinematic
        // guidance is now fully non-colliding again (see UpdatePitExitMerge), so
        // it can no longer rely on physically bumping into ordinary racing
        // traffic to avoid it. Only cars already out on the live racing surface
        // (pitPhase == None) are considered here - other pit-guided cars ahead
        // are already covered by FindPitExitCarAhead's pitGuideDistance-based
        // check. A gap is required both ahead of and behind the candidate merge
        // distance, since pulling out directly in front of fast-approaching live
        // traffic is just as dangerous as merging into a car directly ahead.
        bool IsPitExitMergeSpaceOccupied(RaceParticipant participant, float candidateDistance)
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant || other.vehicle == null || other.retired || other.pitPhase != PitPhase.None)
                {
                    continue;
                }

                TrackProgress otherProgress = GetPitAwareProgress(other);
                float ahead = WrappedForwardDistance(candidateDistance, otherProgress.distance);
                float behind = WrappedForwardDistance(otherProgress.distance, candidateDistance);
                if (ahead < PitExitLiveTrafficForwardGapMeters || behind < PitExitLiveTrafficRearGapMeters)
                {
                    return true;
                }
            }

            return false;
        }

        // Single, shared ExitMerge completion path (bugfix): both the normal
        // physically-merged check and the deterministic failsafe below now
        // finish through exactly this one method - there is only ever one way
        // this phase actually ends, never a partial reposition that leaves the
        // car waiting on next tick's checks to agree, and never two
        // independently timed recovery systems (this failsafe and the generic
        // pit stuck watchdog) nudging the same car against each other.
        void CompletePitExitMerge(RaceParticipant participant, Vector3 finalPosition, Quaternion finalRotation, float finalDistance)
        {
            participant.vehicle.SnapToPitPose(finalPosition, finalRotation);
            participant.pitGuideDistance = finalDistance;

            // State resynchronisation: the car has been moved kinematically for
            // the entire pit sequence, so LapTracker and RaceStateManager must be
            // forced back into agreement with the known-correct exit-merge
            // distance BEFORE guidance is disabled - not left to drift back into
            // agreement over the next few ticks off Tick()'s own
            // continuity-biased search.
            if (participant.lapTracker != null)
            {
                participant.lapTracker.ResyncToDistance(finalDistance, finalPosition);
            }

            if (State != null)
            {
                State.RefreshTimingSnapshot(participant);
            }

            AiVehicleController releasedAi = participant.GetComponent<AiVehicleController>();
            if (releasedAi != null)
            {
                // Same reasoning as the safety-car handback and stuck-recovery
                // reposition fixes: the car's transform just moved under
                // kinematic guidance the whole time it was pit-guided, so its
                // cached track-progress reference is stale and needs a fresh
                // full-track resync rather than a near-search seeded from
                // wherever it was before pitting.
                releasedAi.ResyncAfterForcedReposition();
            }

            // Real exit-speed handoff: this used to inject a fixed, arbitrary
            // ~50 km/h (14 m/s) regardless of how fast the car was actually
            // being guided (PitExitMergePaceKph, 100 km/h) - abruptly halving
            // the car's speed right in front of following pit traffic. Physics
            // now resumes at the real guided exit speed, aligned with the track
            // forward direction.
            participant.vehicle.SetPitGuidance(false, PitExitMergePaceKph / 3.6f);
            participant.pitPhase = PitPhase.None;
            participant.isPitting = false;
            participant.hasPitGuideState = false;
            participant.pitEntryCommitted = false;
            participant.pitExitMergeElapsedTime = 0f;
            // Post-merge AI-side hold (AiVehicleController): even though the
            // guided merge just delivered the car to a safe outer line, normal
            // racing-line/overtake/defend logic resuming instantly can still dive
            // for the next apex too aggressively - hold the outer lane a little
            // longer and suppress overtaking/defending while it eases back in.
            participant.pitExitLaneHoldTimer = PitExitLaneHoldSeconds;
            participant.pitExitLaneHoldDistanceRemaining = PitExitLaneHoldDistanceMeters;

            if (participant.isPlayer)
            {
                SessionMessage = "Merged onto the racing line";
            }
        }

        // Shared failsafe entry point (bugfix): both UpdatePitExitMerge's own
        // per-tick distance/time failsafe and UpdatePitDrivingStuckWatchdog's
        // hard-reset branch call this SAME method to give up on ExitMerge -
        // there is exactly one way this phase force-finishes, never two
        // independently-timed recovery systems disagreeing about what
        // "recovered" means. Always targets a distance derived purely from the
        // car's own known guide progress (pitExitMergeEndDistance), never from a
        // fresh position search, and finishes the phase immediately rather than
        // merely repositioning and hoping a later tick's checks agree.
        void ForcePitExitMergeCompletion(RaceParticipant participant, string reason)
        {
            float recoveryDistance = Track.WrapDistance(participant.pitExitMergeEndDistance + 2f);
            float recoveryLateral = Track.PitExitMergeLegalLateral(recoveryDistance);
            Vector3 recoveryPosition;
            Quaternion recoveryRotation;
            Track.SamplePitLanePose(recoveryDistance, recoveryLateral, out recoveryPosition, out recoveryRotation);
            GameLog.Warn("[PitLane] " + participant.driverName + " hit the ExitMerge failsafe (" + reason + "), force-completing the merge at a known-safe pose.");
            CompletePitExitMerge(participant, recoveryPosition, recoveryRotation, recoveryDistance);
        }

        // Deterministic ExitMerge failsafe timing: a generous multiple of the
        // phase's own known required transit time (distance / exit pace), so
        // this only ever triggers on a genuine stall (e.g. persistent
        // live-traffic occupancy or an unexpected state disagreement), never
        // during a normal merge.
        const float PitExitMergeMaxTimeMultiplier = 2.5f;
        const float PitExitMergeMaxTimeBufferSeconds = 5f;

        void UpdatePitExitMerge(RaceParticipant participant)
        {
            float required = WrappedForwardDistance(participant.pitExitMergeStartDistance, participant.pitExitMergeEndDistance);
            participant.pitExitMergeElapsedTime += Time.deltaTime;

            // Deterministic exit-path fix: pitGuideDistance is the SOLE
            // longitudinal authority for the entire ExitMerge phase. It was set
            // directly to the known release distance by UpdatePitRelease and is
            // advanced forward here by exactly the exit pace every tick - it is
            // never re-derived from LapTracker, RaceStateManager, or a fresh
            // Track.GetProgress()/GetProgressNear() search. Either of those can
            // resolve to a different nearby circuit segment (most dangerously
            // right at the start/finish wrap, where the pit exit sits close to
            // another part of the lap, or anywhere the pit lane runs parallel to
            // a straight), and a bad projection there would silently redirect
            // the whole guided path onto the wrong stretch of track. The step is
            // always positive, so the guide can never move backward.
            //
            // Explicit occupancy holds (bugfix): with static/traffic collisions
            // removed below, blocking now has to be handled explicitly - the
            // guide simply does not advance this tick (holding position, never
            // reversing) while another pit-guided car is in the way
            // (FindPitExitCarAhead) or, in the final stretch before the real
            // merge point, while live racing traffic occupies the merge space
            // (IsPitExitMergeSpaceOccupied).
            float traveledBeforeStep = WrappedForwardDistance(participant.pitExitMergeStartDistance, participant.pitGuideDistance);
            bool nearingLiveMerge = traveledBeforeStep >= required - PitExitMergeLiveTrafficCheckLeadMeters;
            bool blockedByExitTraffic = FindPitExitCarAhead(participant, participant.pitGuideDistance) != null;
            bool blockedByLiveTraffic = nearingLiveMerge && IsPitExitMergeSpaceOccupied(participant, participant.pitGuideDistance);
            float step = (blockedByExitTraffic || blockedByLiveTraffic) ? 0f : Mathf.Max(0f, PitExitMergePaceKph / 3.6f * Time.deltaTime);
            participant.pitGuideDistance = Track.WrapDistance(participant.pitGuideDistance + step);
            float normalized = participant.pitGuideDistance / Mathf.Max(1f, Track.length);

            // Authoritative pose fix: the guided WAYPOINT POSITION is now
            // sampled at pitGuideDistance itself, not a separate point tens of
            // metres further ahead - a position lookahead was exactly what let
            // the physical destination diverge from the authoritative guide
            // distance. The lookahead is kept ONLY for the car's facing
            // direction below, which never changes what point the car is
            // actually being placed at.
            float rampLateral;
            float rampHalfWidth;
            Track.GetPitExitRampEnvelope(normalized, participant.pitGuideDistance, out rampLateral, out rampHalfWidth);
            float legalLateral = Track.PitExitMergeLegalLateral(participant.pitGuideDistance);
            float mergeBlend = Track.PitExitMergeBlend(normalized);
            float guidedLateral = Mathf.Lerp(legalLateral, rampLateral, mergeBlend);

            // Waypoint validation (bugfix): never apply a lateral that sits
            // outside BOTH the current exit-ramp envelope and the legal
            // on-track merge lane. The blended value above should always land
            // inside one or the other, but this is the explicit backstop the
            // fix calls for rather than an unchecked assumption.
            bool insideRampEnvelope = Mathf.Abs(guidedLateral - rampLateral) <= rampHalfWidth;
            bool insideLegalLane = Mathf.Abs(guidedLateral) <= Track.HalfWidthAt(participant.pitGuideDistance);
            if (!insideRampEnvelope && !insideLegalLane)
            {
                guidedLateral = legalLateral;
            }

            participant.pitGuideLateral = guidedLateral;
            Vector3 waypoint;
            Quaternion waypointRotation;
            Track.SamplePitLanePose(participant.pitGuideDistance, guidedLateral, out waypoint, out waypointRotation);

            // Heading-only lookahead: face toward a short distance further
            // along the same lateral blend so the car turns in smoothly. This
            // never creates a second physical destination - the position the
            // car is actually placed at is always exactly pitGuideDistance
            // above.
            float headingDistance = Track.WrapDistance(participant.pitGuideDistance + PitExitMergeLookaheadMeters);
            float headingNormalized = headingDistance / Mathf.Max(1f, Track.length);
            float headingRampLateral;
            float headingRampHalfWidth;
            Track.GetPitExitRampEnvelope(headingNormalized, headingDistance, out headingRampLateral, out headingRampHalfWidth);
            float headingLegalLateral = Track.PitExitMergeLegalLateral(headingDistance);
            float headingMergeBlend = Track.PitExitMergeBlend(headingNormalized);
            float headingLateral = Mathf.Lerp(headingLegalLateral, headingRampLateral, headingMergeBlend);
            Vector3 headingPoint;
            Quaternion headingRotation;
            Track.SamplePitLanePose(headingDistance, headingLateral, out headingPoint, out headingRotation);

            // Non-colliding kinematic movement (bugfix): ExitMerge used to
            // re-enable detectCollisions on the still-kinematic body
            // (collisionAware = true) so it would register contact with static
            // pit-lane/barrier geometry - but a kinematic body isn't actually
            // stopped by that contact, it just gets pinned/jittering against it
            // while pitGuideDistance kept advancing underneath, producing a
            // growing gap between the logical guide and the physical car
            // (freeze), occasionally "resolved" by the pit stuck watchdog's
            // periodic nudge/hard-reset (a brief move), then freezing again at
            // the next piece of geometry. The entire guided pit sequence -
            // including ExitMerge - now stays fully non-colliding, exactly like
            // Entry/Service/Release; live traffic and other pit-guided cars are
            // instead handled by the explicit occupancy holds above.
            participant.vehicle.GuideToPitPose(waypoint, headingRotation, PitGuideChaseSpeed, PitGuideChaseRotateSpeed, false);

            // Continuity-constrained physical check (bugfix): Track.GetProgress()/
            // Track.IsOnRoad() perform an UNRESTRICTED global nearest-centreline
            // search. Near the pit exit - where the pit lane can run close and
            // parallel to another part of the circuit - that search can project
            // the car onto a completely different segment and still report
            // onRoad/safeLateral/pastRampEnd as true, silently declaring the car
            // merged while it is actually somewhere else entirely (including
            // beyond a barrier or on the grass). Every physical check here is
            // instead anchored to the known guide distance via Track.GetProgressNear,
            // exactly as the fix requires.
            TrackProgress physicalProgress = Track.GetProgressNear(participant.transform.position, participant.pitGuideDistance);
            bool onRoad = Mathf.Abs(physicalProgress.lateralDistance) <= Track.HalfWidthAt(physicalProgress.distance);

            // Longitudinal agreement (bugfix): if the car's own physical
            // projection has drifted meaningfully from where the guide believes
            // it is, something has gone wrong (a stale guide state, a botched
            // hand-off) - the merge must not complete against a projection that
            // disagrees with the path the car is actually being driven along.
            float longitudinalDrift = Mathf.Abs(WrappedForwardDistance(participant.pitGuideDistance, physicalProgress.distance));
            if (longitudinalDrift > Track.length * 0.5f)
            {
                longitudinalDrift = Track.length - longitudinalDrift;
            }
            bool longitudinalAgreement = longitudinalDrift <= PitExitMergeLongitudinalToleranceMeters;

            // Heading agreement: the car must be pointed roughly the way the
            // track (and the canonical merge pose) actually faces, not sideways
            // or backward, before the merge is allowed to finish.
            bool facingForward = Vector3.Dot(participant.transform.forward, physicalProgress.forward) > 0.5f;

            float traveled = WrappedForwardDistance(participant.pitExitMergeStartDistance, participant.pitGuideDistance);
            bool pastRampEnd = traveled >= required + PitExitMergeCompletionBufferMeters;
            bool noLongerOnExitRamp = !Track.IsOnPitExitRamp(physicalProgress);
            // Bugfix: this used to compare the raw signed lateralDistance with no
            // Mathf.Abs() - a car sampled on the wrong side of the centerline (a
            // negative lateralDistance, since the pit lane itself only ever runs on
            // the positive/right side) trivially satisfied "<= HalfWidth - buffer"
            // every time, letting the merge falsely report "safe" without the car
            // ever actually being confirmed back on the correct side of the road.
            bool safeLateral = Mathf.Abs(physicalProgress.lateralDistance) <= Track.HalfWidthAt(physicalProgress.distance) - 0.3f;
            bool physicallyMerged = pastRampEnd && noLongerOnExitRamp && safeLateral && onRoad &&
                                     longitudinalAgreement && facingForward;

            // Deterministic failsafe (bugfix): a single distance- or time-based
            // trigger that FINISHES the phase immediately in this same frame via
            // CompletePitExitMerge, rather than repositioning and waiting for
            // next tick's checks to agree (which could itself repeat
            // indefinitely) or leaving the generic pit stuck watchdog to nudge
            // the same broken phase every 4.5s without ever completing it. Both
            // thresholds are generous multiples of the phase's own known
            // required distance/time, so neither ever fires during a normal
            // merge - only on a genuine, persistent stall.
            float expectedTransitSeconds = required / Mathf.Max(1f, PitExitMergePaceKph / 3.6f);
            bool distanceFailsafe = traveled >= required + PitExitMergeHardEscapeBufferMeters;
            bool timeFailsafe = participant.pitExitMergeElapsedTime >= expectedTransitSeconds * PitExitMergeMaxTimeMultiplier + PitExitMergeMaxTimeBufferSeconds;

            // AI pit-exit debug logging (verbose-gated, matches the existing
            // GameLog.Verbose convention - no cost/spam unless F3 is toggled on):
            // makes a bad merge obvious instead of silently dumping AI cars outside
            // the circuit.
            if (!participant.isPlayer)
            {
                GameLog.Info("[PitExitMerge] " + participant.driverName +
                             " phase=" + participant.pitPhase +
                             " pos=" + participant.transform.position.ToString("F1") +
                             " guideDistance=" + participant.pitGuideDistance.ToString("0.0") +
                             " normalized=" + physicalProgress.normalized.ToString("0.000") +
                             " lateral=" + physicalProgress.lateralDistance.ToString("0.00") +
                             " halfWidth=" + Track.HalfWidthAt(physicalProgress.distance).ToString("0.00") +
                             " onRoad=" + onRoad +
                             " onExitRamp=" + !noLongerOnExitRamp +
                             " longitudinalDrift=" + longitudinalDrift.ToString("0.0") +
                             " facingForward=" + facingForward +
                             " blockedByExitTraffic=" + blockedByExitTraffic +
                             " blockedByLiveTraffic=" + blockedByLiveTraffic +
                             " guided=" + participant.vehicle.IsPitGuided +
                             " mergeAttempted=" + physicallyMerged);
            }

            if (physicallyMerged)
            {
                CompletePitExitMerge(participant, participant.transform.position, participant.transform.rotation, participant.pitGuideDistance);
                return;
            }

            if (distanceFailsafe || timeFailsafe)
            {
                ForcePitExitMergeCompletion(participant, distanceFailsafe ? "distance" : "time");
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
            // Track-limits penalties must key off the actual (possibly hairpin-
            // widened) drivable surface, not the flat field - otherwise a car using
            // the extra tarmac a widened hairpin exists to provide would rack up
            // false track-limits warnings and penalties for it.
            float localHalfWidth = Track.HalfWidthAt(progress.distance);
            // Barrier-flush fix: these used to allow +2.2m/+5.2m of "legal"
            // space beyond the paved edge before even a warning - fine when
            // the nearest barrier's own inner face was 1.5m+ further out
            // still, but now that barriers sit flush against the edge
            // (EdgeBarrierClearance, TrackManager.cs) a car "legally" using
            // that old leniency would be driving straight through solid
            // barrier geometry. Tightened to match where the wall actually
            // is - AI never even approaches this (LegalOffsetLimit keeps it
            // 1.8m+ inside the edge), so this only changes how forgiving the
            // player's own track-limits margin is, not AI behaviour.
            bool outsideWhiteLine = lateral > localHalfWidth + 0.5f;
            bool gainedTime = lateral > localHalfWidth + 1.0f && participant.vehicle != null && Mathf.Abs(participant.vehicle.CurrentSpeedKph) > 70f;
            // Stewarding depth: capture whether the lap was already invalidated
            // BEFORE this call, so a "lap deleted" moment only fires once per
            // lap (the very first excursion) instead of every single frame the
            // car stays outside the line for the rest of that lap.
            bool alreadyInvalidated = participant.lapTracker.CurrentLapInvalidated;
            if (outsideWhiteLine)
            {
                participant.lapTracker.InvalidateCurrentLap();
                participant.offTrackTimer += Time.deltaTime;
                if (!alreadyInvalidated && participant.lapTracker.CurrentLapInvalidated && participant.isPlayer &&
                    CurrentSession == RaceWeekendSession.Qualifying)
                {
                    QueueHudToast("LAP DELETED - Sector " + progress.sector, ToastColorAmber);
                    PostEngineerMessage("Lap deleted, track limits in sector " + progress.sector + ". Push again on the next one.", false);
                }
            }
            else
            {
                participant.offTrackTimer = Mathf.Max(0f, participant.offTrackTimer - Time.deltaTime * 2.5f);
            }

            if (gainedTime && participant.offTrackTimer > 0.75f)
            {
                participant.trackLimitWarnings++;
                participant.offTrackTimer = -1.6f;
                // Stewarding depth: log the individual event (lap/sector) rather
                // than only the running count - capped so a persistent offender
                // over a long race can't grow this unbounded.
                int displayLap = participant.lapTracker.CompletedLaps + 1;
                participant.trackLimitEventLog.Add("Lap " + displayLap + " - Sector " + progress.sector);
                const int maxTrackLimitEventLog = 8;
                if (participant.trackLimitEventLog.Count > maxTrackLimitEventLog)
                {
                    participant.trackLimitEventLog.RemoveAt(0);
                }

                if (participant.trackLimitWarnings >= 3)
                {
                    participant.trackLimitWarnings = 0;
                    AddPenalty(participant, 5f, "Track limits");
                    if (participant.isPlayer)
                    {
                        SessionMessage = "Track limits: +5s";
                        QueueHudToast("5S PENALTY - TRACK LIMITS", ToastColorAmber);
                    }
                }
                else if (participant.isPlayer)
                {
                    // RaceHud already watches player.trackLimitWarnings itself and
                    // raises its own "TRACK LIMITS WARNING n/3" toast (see
                    // RaceHud.UpdateTopAccentFlash) - only SessionMessage (the small
                    // top status line) is set here, not a second QueueHudToast, to
                    // avoid showing the same warning twice. RaceHud reads the sector
                    // detail straight off trackLimitEventLog above.
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

            // Configurable race length: a compulsory stop makes no sense on a
            // genuinely short race (a 3-lap blast can't fit a competitive pit
            // window at all) - same RaceLaps<=3 threshold RecommendedPitLap
            // already uses for the same reason, rather than inventing a second
            // one here.
            if (RaceLaps <= 3)
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

            // Player-only: an AI penalty lands every few laps across a 21-car
            // field, which would flood a shared timeline with noise the player
            // has no reason to care about. Their own penalties are always
            // worth a timeline entry.
            if (participant.isPlayer)
            {
                LogRaceControlHistory("PENALTY", "+" + seconds.ToString("0") + "s - " + reason);
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

            // Smarter AI strategy: a short remaining stint (late in the race) should
            // reach for a faster compound regardless of the usual Soft->Medium->Hard
            // ladder below - there's no tyre-life reason to save rubber that will
            // never be needed again. Aggressive drivers push this a little further
            // than cautious ones.
            int lapsRemainingAfterStop = participant.lapTracker == null ? RaceLaps : Mathf.Max(0, RaceLaps - participant.lapTracker.CompletedLaps);
            if (lapsRemainingAfterStop > 0 && lapsRemainingAfterStop <= 8)
            {
                int aggression = participant.driverData == null ? 50 : participant.driverData.aggression;
                bool pushToSoft = aggression >= 65 || lapsRemainingAfterStop <= 4;
                return pushToSoft ? TyreCompound.Soft : TyreCompound.Medium;
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
            if (Mathf.Abs(targetProgress.lateralDistance) > Track.HalfWidthAt(targetProgress.distance) - 1.2f)
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
                // Was calling the 2-arg overload, which defaults incident/safety-car/AI-overtake
                // counts to -1 ("no data") - that silently suppressed every race-control news
                // article (GenerateRaceControlNews bails out on safetyCarDeployments < 0) even
                // though this race tracked all three. Pass the real counts through.
                //
                // 188-incidents fix: this used to pass the raw internal IncidentCount, which
                // counts every single minor scrape/spin/stranded-car detection tick (routinely
                // in the hundreds over a full race) - CareerManager's team-news narrative and
                // "wasChaotic" classification read that number directly, so a perfectly normal
                // race could generate a "188 recorded incidents, utter chaos" story. Passing
                // RaceControlIncidentCount instead (genuine yellow/VSC/SC/red-flag actions only)
                // makes the narrative match what race control actually did.
                Career.ApplyRaceResults(EventData, results, RaceControlIncidentCount, SafetyCarDeploymentCount, AiOvertakesCompletedCount, RedFlagCount, RedFlagReason);
            }

            RecordPlayerRaceStats(results);
            LogAiDiagnostics(results);
            SimpleAudioManager.SetRaceAmbience(false);
            SimpleAudioManager.PlayResultsFlourish();

            // Podium/parc fermé presentation (#25): only at the Cinematic
            // presentation tier, and only when there's an actual camera/field
            // to stage - everything else (Minimal/Standard, or a degenerate
            // 0-1 car field) goes straight to the existing 2D results screen
            // exactly as before, unchanged.
            bool cinematic = Settings != null && Settings.Current.racePresentation >= 2;
            if (cinematic && playerCameraRig != null && results.Count >= 1)
            {
                StartCoroutine(PodiumPresentationSequence(results));
            }
            else
            {
                ui.ShowResults(this, results, IsCareerRace);
            }
        }

        // Generated runtime podium: repositions the top-3 finishers' own
        // already-alive car GameObjects (the race world isn't torn down until
        // the player navigates away from results - see CleanupRaceWorld's
        // call sites) onto three stepped blocks, hijacks the player's own
        // camera rig for a brief pan, then hands off to the normal 2D results
        // screen. Every step is defensively guarded so any missing piece
        // (no top-3 car found, no track reference) just skips that piece
        // rather than aborting the whole sequence - it always ends by calling
        // ShowResults no matter what happened above it.
        IEnumerator PodiumPresentationSequence(List<RaceResultEntry> results)
        {
            List<Transform> podiumCars = new List<Transform>();
            int topCount = Mathf.Min(3, results.Count);
            for (int i = 0; i < topCount; i++)
            {
                RaceParticipant found = null;
                for (int p = 0; p < Participants.Count; p++)
                {
                    if (Participants[p] != null && Participants[p].driverId == results[i].driverId)
                    {
                        found = Participants[p];
                        break;
                    }
                }

                podiumCars.Add(found != null ? found.transform : null);
            }

            GameObject podiumRoot = new GameObject("Podium presentation");
            if (raceWorld != null)
            {
                podiumRoot.transform.SetParent(raceWorld.transform);
            }

            Vector3 podiumCenter = Vector3.zero;
            Vector3 podiumForward = Vector3.forward;
            if (Track != null)
            {
                Vector3 point;
                Vector3 forward;
                Vector3 right;
                Track.SampleAtDistance(0f, out point, out forward, out right);
                // Well clear of the track/pit lane on either side - this is a
                // purely cosmetic stage, not something a car ever drives past,
                // so it only needs to be visually clear, not lane-accurate.
                podiumCenter = point + right * (Track.roadHalfWidth + 40f);
                podiumForward = -forward;
            }

            // Three stepped blocks: P1 centre and tallest, P2/P3 lower to either
            // side - the same silhouette every real podium reads as.
            Vector3 podiumRight = Vector3.Cross(Vector3.up, podiumForward).normalized;
            float[] blockHeights = { 1.1f, 0.75f, 0.5f };
            Vector3[] blockOffsets = { Vector3.zero, podiumRight * -3.2f, podiumRight * 3.2f };
            Color[] blockColors =
            {
                new Color(0.85f, 0.68f, 0.15f),
                new Color(0.75f, 0.76f, 0.78f),
                new Color(0.62f, 0.42f, 0.22f)
            };

            for (int i = 0; i < topCount; i++)
            {
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Podium block P" + (i + 1);
                block.transform.SetParent(podiumRoot.transform);
                Vector3 blockCenter = podiumCenter + blockOffsets[i] + Vector3.up * (blockHeights[i] * 0.5f);
                block.transform.position = blockCenter;
                block.transform.rotation = Quaternion.LookRotation(podiumForward, Vector3.up);
                block.transform.localScale = new Vector3(2.6f, blockHeights[i], 2.6f);
                Renderer blockRenderer = block.GetComponent<Renderer>();
                if (blockRenderer != null)
                {
                    blockRenderer.sharedMaterial = CreateMaterial("Podium block P" + (i + 1), blockColors[i], 0.2f, 0.55f);
                }

                if (podiumCars[i] != null)
                {
                    // Freeze the car exactly where it's placed - a still-alive
                    // AiVehicleController/VehicleController would otherwise keep
                    // trying to drive it the instant it's teleported.
                    VehicleController vc = podiumCars[i].GetComponent<VehicleController>();
                    if (vc != null)
                    {
                        vc.enabled = false;
                    }

                    AiVehicleController ai = podiumCars[i].GetComponent<AiVehicleController>();
                    if (ai != null)
                    {
                        ai.enabled = false;
                    }

                    PlayerVehicleInput playerInput = podiumCars[i].GetComponent<PlayerVehicleInput>();
                    if (playerInput != null)
                    {
                        playerInput.enabled = false;
                    }

                    Rigidbody carBody = podiumCars[i].GetComponent<Rigidbody>();
                    if (carBody != null)
                    {
                        carBody.isKinematic = true;
                        carBody.velocity = Vector3.zero;
                        carBody.angularVelocity = Vector3.zero;
                    }

                    podiumCars[i].position = blockCenter + Vector3.up * (blockHeights[i] * 0.5f + 0.3f);
                    podiumCars[i].rotation = Quaternion.LookRotation(-podiumForward, Vector3.up);
                }
            }

            // Simple confetti: short burst of coloured particles above the
            // podium, gated the same way every other optional effect in this
            // codebase is (Settings.Current.particlesEnabled).
            if (Settings != null && Settings.Current.particlesEnabled && topCount > 0)
            {
                GameObject confettiObject = new GameObject("Podium confetti");
                confettiObject.transform.SetParent(podiumRoot.transform);
                confettiObject.transform.position = podiumCenter + Vector3.up * 4f;
                ParticleSystem confetti = confettiObject.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = confetti.main;
                main.startLifetime = 2.2f;
                main.startSpeed = 3.5f;
                main.startSize = 0.12f;
                main.maxParticles = 200;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.2f), new Color(0.3f, 0.6f, 1f));
                ParticleSystem.EmissionModule emission = confetti.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 120) });
                ParticleSystem.ShapeModule shape = confetti.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 25f;
                shape.radius = 1.5f;
                ParticleSystemRenderer confettiRenderer = confettiObject.GetComponent<ParticleSystemRenderer>();
                if (confettiRenderer != null)
                {
                    confettiRenderer.material = CreateMaterial("Confetti particle", Color.white, 0f, 0.2f);
                }

                confetti.Play();
            }

            // Camera: hand-drive the player's own rig instead of leaving
            // CameraRig's own per-frame follow logic fighting this - it gets
            // re-enabled implicitly by never being needed again (the race
            // world, camera included, is destroyed the moment the player
            // navigates away from the results screen that follows this).
            playerCameraRig.enabled = false;
            Vector3 camStart = podiumCenter + podiumForward * 14f + podiumRight * 6f + Vector3.up * 3.2f;
            Vector3 camEnd = podiumCenter + podiumForward * 12f - podiumRight * 6f + Vector3.up * 4.4f;
            Transform camTransform = playerCameraRig.transform;

            float duration = 4f;
            float elapsed = 0f;
            while (elapsed < duration && !Input.anyKeyDown)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                camTransform.position = Vector3.Lerp(camStart, camEnd, t);
                camTransform.rotation = Quaternion.LookRotation((podiumCenter + Vector3.up * 1.5f) - camTransform.position, Vector3.up);
                yield return null;
            }

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
                         " rawIncidentDetections=" + IncidentCount +
                         " raceControlIncidents=" + RaceControlIncidentCount +
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
            bool wetRace = Track != null && (Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain);
            string trackIdForRecords = EventData != null ? EventData.trackId : null;
            PlayerRecordsStore.RecordRaceFinish(playerResult.finishingPosition, playerResult.points, fastestLap, cleanRace, trackLimitWarnings,
                trackIdForRecords, playerResult.gridPosition, playerResult.overtakesMade, wetRace);
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
                PlayerRecordsStore.RecordQualifyingResult(playerQualifying.position, EventData != null ? EventData.trackId : null);
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

        // Qualifying rework: AI and player used to run two independently-written
        // "best of two laps" implementations with two different, uncalibrated
        // second-run-improvement models (the AI version could hand up to ~0.46s for
        // no reason beyond a coin flip; the player version up to ~0.34s). Both now
        // share this one helper - the second-run gain is a small, explicit,
        // reasoned term (see below) instead of raw per-path randomness, so AI and
        // player results are internally consistent with each other.
        QualifyingLapBreakdown SimulateBestOfTwoQualifyingAttempt(QualifyingSimEntry entry, int phase, TyreCompound? tyreChoice)
        {
            QualifyingLapBreakdown first = SimulateQualifyingRunDetailed(entry, phase, false);
            QualifyingLapBreakdown second = SimulateQualifyingRunDetailed(entry, phase, true);

            // Second-run improvement: a small baseline gain from track evolution,
            // better tyre prep and driver adaptation on a repeated lap - NOT a
            // random half-second lottery. A second run only gains meaningfully more
            // than that when the first lap was genuinely compromised by a mistake
            // (see QualifyingMistakePenalty) - recovering from a bad lap, not luck.
            float secondRunGain = Random.Range(0.03f, 0.10f);
            if (first.mistakePenalty > 0.05f)
            {
                secondRunGain += Mathf.Min(first.mistakePenalty * 0.5f, 0.5f);
            }

            second.variance -= secondRunGain;
            second.finalTime -= secondRunGain;

            if (tyreChoice.HasValue)
            {
                float tyrePenalty = PlayerQualifyingTyreWeatherPenalty(tyreChoice.Value);
                first.tyreChoicePenalty = tyrePenalty;
                first.finalTime += tyrePenalty;
                second.tyreChoicePenalty = tyrePenalty;
                second.finalTime += tyrePenalty;
            }

            return first.finalTime <= second.finalTime ? first : second;
        }

        float SimulateAiQualifyingTime(QualifyingSimEntry entry, int phase)
        {
            return SimulateBestOfTwoQualifyingAttempt(entry, phase, null).finalTime;
        }

        float SimulatePlayerQualifyingTime(QualifyingSimEntry entry, int phase)
        {
            TyreCompound compound = Settings == null ? TyreCompound.Medium : Settings.SelectedTyreCompound;
            QualifyingLapBreakdown best = SimulateBestOfTwoQualifyingAttempt(entry, phase, compound);
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

        // ---------- Qualifying model rework ----------
        // Full rework: the old model derived the ENTIRE base lap from a single
        // car's own top speed (treating top speed as if it were the car's average
        // speed around the whole circuit), so a few km/h of difference between two
        // cars swung the WHOLE lap by whole seconds. Car performance was then also
        // applied a second time via a separate, much smaller carEffect term using
        // different stats - two inconsistent mechanisms for the same thing. Every
        // driver now starts from the exact same neutral circuit reference lap
        // (CircuitReferenceLapTime, track-only, no car parameter at all); the ONE
        // place car performance enters the model is the composite, track-weighted
        // carEffect term below.
        const float DriverQualifyingCoefficient = 0.012f;
        const float DriverPaceCoefficient = 0.003f;
        const float DriverConfidenceCoefficient = 0.001f;
        const float CarEffectCoefficientPerPoint = 0.08f;
        const float CarEffectCapSeconds = 2.0f;

        QualifyingLapBreakdown SimulateQualifyingRunDetailed(QualifyingSimEntry entry, int phase, bool secondRun)
        {
            QualifyingLapBreakdown breakdown = new QualifyingLapBreakdown { phase = phase };
            DriverData driver = entry.driverData;
            CarPerformanceData car = entry.carData;

            // Neutral circuit reference lap: identical for every driver in the
            // field this session - car/driver/tyre/weather/mistake effects are
            // layered on top of this SAME starting point below, never baked into
            // it.
            breakdown.baseLap = CircuitReferenceLapTime(Track);

            float consistency = driver == null ? 80f : driver.consistency;
            float qualifying = driver == null ? 82f : driver.qualifying;
            float pace = driver == null ? 82f : driver.pace;
            float confidence = driver == null ? 80f : driver.experience;
            float tyreManagement = driver == null ? 80f : driver.tyreManagement;

            // Driver effect: qualifying ability is the PRIMARY term; pace and
            // confidence (experience) are smaller secondary flavors, per the
            // calibration targets (elite vs good ~0.05-0.25s, elite vs weak
            // ~0.25-0.55s in comparable machinery). Centered on the FIELD's own
            // average of each stat, not a fixed baseline number, so the whole grid
            // isn't shifted just because a given season's roster skews stronger or
            // weaker than some hardcoded expectation.
            float avgQualifying;
            float avgPace;
            float avgConfidence;
            FieldAverageDriverStats(out avgQualifying, out avgPace, out avgConfidence);
            breakdown.driverEffect = (avgQualifying - qualifying) * DriverQualifyingCoefficient +
                                      (avgPace - pace) * DriverPaceCoefficient +
                                      (avgConfidence - confidence) * DriverConfidenceCoefficient;

            // Car effect: ONE composite, track-weighted rating (see
            // CarQualifyingPerformanceRating/CarPerformanceWeights) covering every
            // relevant car stat, including top speed - now normalized onto the
            // same 45-125 scale every other stat already uses, instead of driving
            // the entire base lap on its own. Centered on the field's own average
            // composite rating (same reasoning as driverEffect above) and clamped
            // so an extreme outlier car can never dominate the result by itself.
            // This is the ONLY place car performance affects the result - it is
            // never applied a second time anywhere else in this model.
            float carRating = CarQualifyingPerformanceRating(car, Track);
            float fieldAverageCarRating = FieldAverageCarRating(Track);
            breakdown.carEffect = Mathf.Clamp((fieldAverageCarRating - carRating) * CarEffectCoefficientPerPoint, -CarEffectCapSeconds, CarEffectCapSeconds);

            // Percentage of baseLap rather than a flat constant, so difficulty stays
            // meaningful regardless of track length: Easy is clearly the slowest,
            // Expert clearly the fastest/most aggressive, Medium close to neutral.
            // Shared identically by every entry in the session (the chosen AI
            // difficulty is a session-wide setting, not a per-driver one), so this
            // never contributes to the inter-driver spread on its own.
            float difficultyPercent = Settings.Difficulty == RaceDifficulty.Easy ? 0.035f : Settings.Difficulty == RaceDifficulty.Medium ? 0.005f : Settings.Difficulty == RaceDifficulty.Hard ? -0.030f : -0.060f;
            breakdown.difficultyEffect = breakdown.baseLap * difficultyPercent;

            // Track evolution: small and gradual, shared identically by every
            // driver in the same session phase - a later session can be marginally
            // faster (more rubber down) without artificially exaggerating the
            // P1-P20 spread the way large fixed per-phase bonuses (previously
            // Q1 +0.08 / Q2 -0.18 / Q3 -0.36, a 0.44s swing across sessions on its
            // own) used to.
            breakdown.phaseEffect = phase == 1 ? 0.02f : (phase == 2 ? -0.02f : -0.05f);

            breakdown.tyrePrep = Mathf.Lerp(0.14f, 0.0f, tyreManagement / 100f) + Random.Range(0f, 0.04f);
            breakdown.weatherPenalty = WeatherQualifyingPenalty(driver);
            breakdown.mistakePenalty = QualifyingMistakePenalty(driver, phase, out breakdown.mistakeType);

            // Normal clean-lap variance only, narrowed to the calibration target
            // (was up to 0.03s at worst, now a proper 0.03-0.12s band scaled by
            // consistency). The extra second-run-specific noise this used to add
            // here is gone - second-run improvement is now its own explicit,
            // reasoned term (see SimulateBestOfTwoQualifyingAttempt).
            float variance = Mathf.Lerp(0.12f, 0.03f, consistency / 100f);
            breakdown.variance = Random.Range(-variance, variance);

            breakdown.finalTime = breakdown.baseLap + breakdown.driverEffect + breakdown.carEffect +
                                  breakdown.difficultyEffect + breakdown.phaseEffect + breakdown.tyrePrep +
                                  breakdown.weatherPenalty + breakdown.mistakePenalty + breakdown.variance;
            return breakdown;
        }

        // Neutral circuit reference lap: track length and character only, no car
        // parameter at all - every driver's simulated lap begins from this exact
        // same number. The "neutral expected average speed" is the CURRENT
        // field's own average top speed (a genuine km/h value, unlike every other
        // car stat which is a 0-125ish rating) rather than any one competitor's -
        // shared by the whole field, so it can never by itself favor one driver
        // over another the way the old per-car version did.
        float CircuitReferenceLapTime(TrackRuntime track)
        {
            float neutralTopSpeedKph = FieldAverageTopSpeedKph();
            float styleFactor = TrackAverageSpeedFactor(track);
            float referenceSpeedMps = (neutralTopSpeedKph / 3.6f) * styleFactor;
            float trackLength = track == null ? 7266f : track.length;
            return Mathf.Max(45f, trackLength / referenceSpeedMps);
        }

        float FieldAverageTopSpeedKph()
        {
            if (qualifyingEntries.Count == 0)
            {
                return 337f;
            }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < qualifyingEntries.Count; i++)
            {
                CarPerformanceData car = qualifyingEntries[i].carData;
                sum += car == null || car.topSpeed <= 0 ? 337f : car.topSpeed;
                count++;
            }

            return count > 0 ? sum / count : 337f;
        }

        void FieldAverageDriverStats(out float avgQualifying, out float avgPace, out float avgConfidence)
        {
            avgQualifying = 82f;
            avgPace = 82f;
            avgConfidence = 80f;
            if (qualifyingEntries.Count == 0)
            {
                return;
            }

            float sumQualifying = 0f;
            float sumPace = 0f;
            float sumConfidence = 0f;
            int count = 0;
            for (int i = 0; i < qualifyingEntries.Count; i++)
            {
                DriverData d = qualifyingEntries[i].driverData;
                sumQualifying += d == null ? 82f : d.qualifying;
                sumPace += d == null ? 82f : d.pace;
                sumConfidence += d == null ? 80f : d.experience;
                count++;
            }

            if (count > 0)
            {
                avgQualifying = sumQualifying / count;
                avgPace = sumPace / count;
                avgConfidence = sumConfidence / count;
            }
        }

        float FieldAverageCarRating(TrackRuntime track)
        {
            if (qualifyingEntries.Count == 0)
            {
                return 84f;
            }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < qualifyingEntries.Count; i++)
            {
                sum += CarQualifyingPerformanceRating(qualifyingEntries[i].carData, track);
                count++;
            }

            return count > 0 ? sum / count : 84f;
        }

        // Composite qualifying car-performance rating: every relevant car stat,
        // weighted by what actually matters on THIS circuit (see
        // CarPerformanceWeights) - a power-sensitive circuit weights top
        // speed/acceleration/engine power heavily, a high-downforce circuit
        // weights aero/cornering, a technical circuit weights cornering/braking/
        // chassis, and a balanced circuit spreads the weight evenly. topSpeed is
        // stored as a literal km/h value (roughly 310-362), not a 0-125 rating
        // like every other stat - normalized onto that same scale first
        // (NormalizeTopSpeedToRating) so it contributes proportionally instead of
        // dominating or being dwarfed by unit mismatch.
        float CarQualifyingPerformanceRating(CarPerformanceData car, TrackRuntime track)
        {
            if (car == null)
            {
                return 84f;
            }

            float wTopSpeed;
            float wAcceleration;
            float wCornering;
            float wBraking;
            float wAero;
            float wChassis;
            float wEngine;
            CarPerformanceWeights(track, out wTopSpeed, out wAcceleration, out wCornering, out wBraking, out wAero, out wChassis, out wEngine);

            float topSpeedRating = NormalizeTopSpeedToRating(car.topSpeed);
            return topSpeedRating * wTopSpeed + car.acceleration * wAcceleration + car.cornering * wCornering +
                   car.braking * wBraking + car.aeroEfficiency * wAero + car.chassisBalance * wChassis +
                   car.enginePower * wEngine;
        }

        // Same 45-125 scale every other car stat uses (CareerManager's own clamp
        // range for cornering/braking/aero/etc.) so topSpeed can be weighted-
        // averaged alongside them without a unit mismatch - this is exactly what
        // let a few km/h of difference swing the entire old lap-time formula by
        // whole seconds (topSpeed used to be a literal speed divisor for the
        // WHOLE lap, see CircuitReferenceLapTime's own history above).
        float NormalizeTopSpeedToRating(float topSpeedKph)
        {
            return Mathf.Lerp(45f, 125f, Mathf.InverseLerp(300f, 365f, topSpeedKph));
        }

        // Per-circuit car-stat weighting (must each sum to 1.0). Named-circuit
        // checks mirror TrackAverageSpeedFactor's own bucketing style.
        void CarPerformanceWeights(TrackRuntime track, out float wTopSpeed, out float wAcceleration, out float wCornering, out float wBraking, out float wAero, out float wChassis, out float wEngine)
        {
            string id = track == null ? "" : (track.trackId ?? "");
            string style = track == null ? "" : (track.styleName ?? "").ToLowerInvariant();

            if (id.Contains("monza") || id.Contains("las_vegas") || id.Contains("baku") || id.Contains("jeddah"))
            {
                // Power-sensitive: long straights, low downforce - top speed,
                // acceleration and engine power matter far more here than outright
                // cornering grip.
                wTopSpeed = 0.28f; wAcceleration = 0.20f; wCornering = 0.10f; wBraking = 0.08f;
                wAero = 0.07f; wChassis = 0.05f; wEngine = 0.22f;
                return;
            }

            if (id.Contains("spa") || id.Contains("silverstone") || id.Contains("suzuka") || id.Contains("qatar"))
            {
                // High-downforce, flowing high-speed corners - aero and cornering
                // grip dominate; top speed/engine power matter far less than on a
                // pure power circuit.
                wTopSpeed = 0.10f; wAcceleration = 0.08f; wCornering = 0.26f; wBraking = 0.10f;
                wAero = 0.26f; wChassis = 0.14f; wEngine = 0.06f;
                return;
            }

            if (id.Contains("monaco") || id.Contains("hungary") || style.Contains("street") || (track != null && track.roadHalfWidth < 12f))
            {
                // Technical/tight: low-speed cornering, braking and traction out of
                // slow corners matter most - top speed is nearly irrelevant.
                wTopSpeed = 0.04f; wAcceleration = 0.14f; wCornering = 0.28f; wBraking = 0.24f;
                wAero = 0.06f; wChassis = 0.20f; wEngine = 0.04f;
                return;
            }

            // Balanced/standard circuit: a fairly even mixture across every stat.
            wTopSpeed = 0.14f; wAcceleration = 0.14f; wCornering = 0.20f; wBraking = 0.16f;
            wAero = 0.16f; wChassis = 0.12f; wEngine = 0.08f;
        }

        // Fraction of top speed a well-driven qualifying lap averages, by track
        // character. Tight/low-speed circuits (Monaco, street layouts) average much
        // lower than top speed; flowing high-speed circuits average much closer to
        // it. Named-circuit checks run before the generic street check since several
        // real high-speed circuits (Jeddah, Baku, Las Vegas) are technically street
        // layouts but should not be bucketed with tight street pace.
        //
        // Qualifying-vs-race calibration fix round 2: this is the ONLY place AI
        // qualifying times come from - RecordQualifyingPhase falls back to
        // SimulateAiQualifyingTime for every AI entry even in the live/driven
        // qualifying session, so AI never actually banks a real physics-driven lap
        // time for qualifying purposes, live or simulated. Round 1's ~1.2x bump
        // still undershot actual race pace: AiVehicleController's corner-speed
        // model now keeps HighSpeed/Medium corners near 97-100% of straight-line
        // pace and Slow (tight) corners at a flat ~300-310kph (itself often
        // 90%+ of a car's real top speed) - only the narrow VeryTight/Hairpin
        // bands meaningfully cut into a lap's average anymore, so a track's real
        // achieved average speed fraction is far closer to its top speed than
        // these factors assumed even after round 1. Pushed further here so the
        // simulated qualifying baseline is genuinely faster than what the
        // buffed race physics produce, matching a real low-fuel flying lap.
        //
        // Round 3: the race-fastest-lap-beats-qualifying bug came back after this
        // session's slipstream buff (SlipstreamTopSpeedBonusKph/slipstreamBoost in
        // VehicleController doubled) - a genuine tow gives race laps a real speed
        // source this formula never modeled (no drafting in a simulated/solo
        // flying lap), so real race pace on drafting-heavy circuits pulled back
        // ahead of the quali baseline even with the earlier AI straight-line-speed
        // nerfs. Every factor nudged up again to buy back that headroom.
        float TrackAverageSpeedFactor(TrackRuntime track)
        {
            if (track == null)
            {
                return 0.83f;
            }

            string id = track.trackId ?? "";
            string style = (track.styleName ?? "").ToLowerInvariant();
            if (id.Contains("monaco"))
            {
                return 0.65f;
            }

            if (id.Contains("spa") || id.Contains("monza") || id.Contains("silverstone") ||
                id.Contains("baku") || id.Contains("jeddah") || id.Contains("las_vegas") ||
                id.Contains("suzuka") || id.Contains("qatar"))
            {
                return 1.02f;
            }

            if (id.Contains("hungary"))
            {
                return 0.71f;
            }

            if (style.Contains("street") || track.roadHalfWidth < 12f)
            {
                return 0.76f;
            }

            return 0.92f;
        }

        // All drivers under the same conditions share the same weather baseline
        // (Track.weather, clear/cloudy penalty) - wetSkill only creates a
        // controlled difference AROUND that shared baseline, narrowed here so wet
        // conditions can't stack into an extreme, barely-explainable gap on their
        // own (was 1.18-0.42, roughly a 2s spread between best/worst wet driver in
        // heavy rain).
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
            return basePenalty * Mathf.Lerp(1.1f, 0.6f, wetSkill / 100f);
        }

        // Mistakes are modeled explicitly and kept separate from ordinary
        // clean-lap variance (see SimulateQualifyingRunDetailed's own variance
        // term) - minor mistakes cost roughly 0.1-0.4s, a rare major mistake adds
        // substantially more on top. mistakeType names what happened so it's
        // visible in the qualifying breakdown instead of hidden inside a single
        // opaque number.
        float QualifyingMistakePenalty(DriverData driver, int phase, out string mistakeType)
        {
            mistakeType = "";
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

            string[] minorMistakeTypes = { "small lock-up", "poor corner exit", "traffic", "track limits" };
            mistakeType = minorMistakeTypes[Random.Range(0, minorMistakeTypes.Length)];
            float penalty = Random.Range(0.1f, 0.4f) * Mathf.Lerp(1.25f, 0.75f, awareness / 100f);
            if (Random.value < 0.1f)
            {
                // Rare, uncommon major mistake on top of the minor one - clearly
                // identifiable via mistakeType rather than a hidden random tail.
                mistakeType = "major mistake";
                penalty += Random.Range(0.8f, 2.0f);
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

        public class PracticeSessionResult
        {
            public string programId;
            public string title;
            public bool passed;
            public string metricSummary;
        }
    }
}
