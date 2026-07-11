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

        void LogRaceControlHistory(string label, string detail)
        {
            int lap = PlayerParticipant != null && PlayerParticipant.lapTracker != null ? PlayerParticipant.lapTracker.DisplayLap : 0;
            raceControlHistory.Add(new RaceControlHistoryEntry { label = label, detail = detail, raceTimeSeconds = RaceElapsed, lap = lap });
            // Every race-control event is a replay timeline marker (single hook).
            replayCapture.AddFlagMarker(RaceElapsed, label);
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

        /// <summary>
        /// Compact one-line engineer debrief for the results screen subtitle
        /// (empty when telemetry capture is off / produced no samples).
        /// </summary>
        public string TelemetryDebriefLine()
        {
            TelemetryDebrief.Summary d = BuildTelemetryDebrief();
            if (!d.HasData)
            {
                return "";
            }

            return "Top " + d.TopSpeedKph.ToString("0") + "kph · " +
                   d.FullThrottlePercent.ToString("0") + "% full throttle · " +
                   d.BrakingPercent.ToString("0") + "% braking · DRS " +
                   d.DrsPercent.ToString("0") + "% · tyre wear " +
                   (d.TyreWearDelta01 * 100f).ToString("0") + "%";
        }

        /// <summary>
        /// Race-events summary from the live replay capture (overtakes / incidents
        /// / pit stops), for the results screen. Empty when nothing to report.
        /// </summary>
        public string ReplayHighlightLine()
        {
            F1Game.Race.ReplayTimeline.Summary t = BuildReplayTimeline();
            if (!t.HasData)
            {
                return "";
            }

            return t.OvertakeCount + " overtakes · " +
                   t.IncidentCount + " incidents · " +
                   t.PitStopCount + " pit stops";
        }

        /// <summary>
        /// Combined results-screen debrief: race-events summary (replay) over the
        /// player's driving summary (telemetry). Either half is omitted when its
        /// capture produced nothing; returns empty when both are empty.
        /// </summary>
        public string RaceDebriefLine()
        {
            string events = ReplayHighlightLine();
            string driving = TelemetryDebriefLine();
            if (string.IsNullOrEmpty(events))
            {
                return driving;
            }

            return string.IsNullOrEmpty(driving) ? events : events + "\n" + driving;
        }
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

            // Publish at the source (in addition to the legacy queue) so the
            // production notification feed sees the same toasts without draining
            // the queue the legacy HUD still consumes - exactly one HUD is live,
            // but both paths stay correct. Tone maps the legacy colour kinds:
            // green -> positive, amber -> caution, everything else -> neutral.
            int tone = colorKind == ToastColorGreen ? 0 : (colorKind == ToastColorAmber ? 1 : 2);
            GameEvents.Publish(new HudToastEvent(tone, text));
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

        public int RecommendedPitLap(RaceParticipant participant)
        {
            // Boundary only: the window math (wet shift, management shift, and
            // the per-driver-stable jitter that keeps a midfield with similar
            // stats from converging on one lap) lives in AiPitStrategyRules.
            bool wetRace = Track != null && (Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain);
            float tyreManagement = participant == null || participant.driverData == null ? 78f : participant.driverData.tyreManagement;
            // 0.5 = no jitter (hashed from driverId, so it's the same every
            // time this is called for a given driver this race, never re-rolled).
            float driverJitter01 = participant == null || string.IsNullOrEmpty(participant.driverId) ? 0.5f
                : StableUnitInterval(participant.driverId);
            return AiPitStrategyRules.RecommendedPitLap(RaceLaps, wetRace, tyreManagement / 100f, driverJitter01);
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

            // Pit-timing fix (per request: AI was pitting a lap early): hold the
            // stop until the car has fully COMPLETED the recommended lap and is
            // running the one after it, rather than triggering the instant it
            // starts the recommended lap. Tyre-wear / SC / undercut triggers
            // elsewhere can still bring a stop forward when the situation
            // genuinely calls for it - this only shifts the routine strategy
            // stop one lap later.
            return currentLapNumber >= targetLap + 1;
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

            // The decision itself (source/state gates, commitment lockouts, the
            // PreRacePlan never-cancellable rule) lives in the unit-tested
            // rulebook; this method only assembles the live snapshot. The
            // limiter-line probe re-checks the SAME authoritative boundary
            // HandlePitService uses, directly against the car's current position,
            // because the cached pitPhase/pitEntryCommitted flags only update once
            // HandlePitService ticks this frame.
            bool crossedLimiterLine = false;
            if (Track != null && participant.lapTracker != null)
            {
                TrackProgress liveProgress = Track.GetProgressNear(participant.transform.position, participant.lapTracker.CurrentProgress.distance);
                crossedLimiterLine = Track.HasCrossedPitEntryLimiterLine(liveProgress);
            }

            var context = new PitRequestContext
            {
                IsQualifying = CurrentSession == RaceWeekendSession.Qualifying,
                IsTimeTrial = IsTimeTrial,
                RaceFinished = IsRaceFinished,
                ParticipantRetired = participant.retired,
                ParticipantFinished = participant.finished,
                ManualPitRequested = participant.manualPitRequested,
                ManualPitCommitted = participant.manualPitCommitted,
                Origin = MapPitRequestOrigin(participant.activePitRequestSource),
                VehiclePitRequested = participant.vehicle.PitRequested,
                InPitSequence = participant.pitPhase != PitPhase.None,
                IsPitting = participant.isPitting,
                PitEntryCommitted = participant.pitEntryCommitted,
                CrossedPitEntryLimiterLine = crossedLimiterLine,
            };

            return PitRequestRules.CanCancel(context);
        }

        static PitRequestOrigin MapPitRequestOrigin(PitRequestSource source)
        {
            switch (source)
            {
                case PitRequestSource.PreRacePlan: return PitRequestOrigin.PreRacePlan;
                case PitRequestSource.Manual: return PitRequestOrigin.Manual;
                case PitRequestSource.SafetyCarPrompt: return PitRequestOrigin.SafetyCarPrompt;
                default: return PitRequestOrigin.None;
            }
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
            GameEvents.Publish(new PitRequestChangedEvent(participant.driverId, PitRequestState.Cancelled, -1));

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
            bool freshTyres = participant.pitStops > 0 && wear > AiPitStrategyRules.SafetyCarFreshTyreWear;
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

            // Decision core lives in AiPitStrategyRules; this method supplies
            // the live inputs.
            bool mandatoryStopOwed = participant.pitStops == 0;
            bool tyresWorn = wear < AiPitStrategyRules.SafetyCarWornTyreWear;

            int windowLap = NextPlannedPitLapFor(participant);
            bool windowClose = windowLap > 0 && completedLaps >= windowLap - AiPitStrategyRules.SafetyCarWindowCloseLaps;

            WeatherState currentWeather = Track == null ? WeatherState.Clear : Track.weather;
            bool wetNow = currentWeather == WeatherState.LightRain || currentWeather == WeatherState.HeavyRain;
            TyreCompound currentCompound = participant.vehicle.Tyres.Compound;
            bool onWetTyre = currentCompound == TyreCompound.Intermediate || currentCompound == TyreCompound.Wet;
            bool weatherMismatch = wetNow != onWetTyre;

            return AiPitStrategyRules.ShouldPitUnderSafetyCar(mandatoryStopOwed, tyresWorn, windowClose, weatherMismatch);
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

            // Decision core lives in AiPitStrategyRules; this method supplies
            // the live inputs (window, wear, who is actually ahead and how far).
            RaceParticipant ahead = FindCarAhead(participant, 40f);
            bool rivalAheadUnstopped = ahead != null && ahead.pitStops == 0 && !ahead.retired && !ahead.finished;
            return AiPitStrategyRules.ShouldPitForUndercut(
                participant.lapTracker.CompletedLaps,
                RecommendedPitLap(participant),
                participant.vehicle.Tyres.Wear,
                rivalAheadUnstopped,
                GetIntervalToAheadSeconds(participant));
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
                return "PIT LANE  TO BOX " + (participant.pitBoxIndex + 1) + "  LIMITER 80";
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

            // Judgement + tariff live in the extracted rulebook
            // (StartProcedureRules); this method only supplies the detection.
            StartInfraction infraction = StartProcedureRules.Judge(true, -1f, true);
            float penaltySeconds = StartProcedureRules.PenaltySeconds(infraction);
            participant.jumpStartPenaltyApplied = true;
            AddPenalty(participant, penaltySeconds, "Jump start");
            SessionMessage = participant.isPlayer ? "Jump start: +" + penaltySeconds.ToString("0") + "s" : SessionMessage;
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

            // Anticipation rule: a throttle already committed as the lights go
            // out reads as a false start (StartProcedureRules judges the
            // threshold). Skipped if the harsher jump-start penalty already hit.
            if (!participant.jumpStartPenaltyApplied)
            {
                StartInfraction infraction = StartProcedureRules.Judge(false, playerReactionTime, true);
                if (infraction == StartInfraction.FalseStart)
                {
                    float penaltySeconds = StartProcedureRules.PenaltySeconds(infraction);
                    participant.jumpStartPenaltyApplied = true;
                    AddPenalty(participant, penaltySeconds, "False start");
                    SessionMessage = "False start: +" + penaltySeconds.ToString("0") + "s";
                    PostEngineerMessage("That was a false start - " + penaltySeconds.ToString("0") + " second penalty.", true);
                }
            }
        }

        public void OpenPlayerPitTyreSelector(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer || IsTimeTrial || CurrentSession == RaceWeekendSession.Qualifying || participant.vehicle == null || participant.isPitting)
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
            GameEvents.Publish(new PitRequestChangedEvent(participant.driverId, PitRequestState.Requested, -1));
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

        // Limiter-duration fix: the Restart state is included too - the field
        // is still under race control between the safety car peeling in and
        // the actual green flag, but the player's limiter (and the HUD pace
        // pill) used to switch off the instant the state left
        // SafetyCarInThisLap, several seconds before the flag actually ended.
        // The limiter must never end before the period it enforces does.
        public bool IsRaceControlPaceLimited
        {
            get { return FlagRules.RequiresPaceControl(GlobalRaceFlag); }
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

        // Pre-race pit lap fix: a scheduled PreRacePlan stop only ever latched
        // vehicle.PitRequested and otherwise left the player fully in manual
        // control - if the player missed the physical ramp (drove straight on,
        // got the line wrong, was mid-battle), missedPitEntryThisLap cleared the
        // request and it retried next lap, silently turning a "stop on lap 4"
        // plan into a real lap-5 stop. This is a narrow, opt-in-by-plan assist:
        // it only ever engages for a PreRacePlan request, only inside the pit
        // approach window, and only until BeginPitEntry takes over (at which
        // point pitPhase != None and ShouldAssistPlayerPitEntry stops matching,
        // handing off to the existing kinematic pit-guidance system). A manual
        // (P key) or accepted race-control offer request never matches
        // PitRequestSource.PreRacePlan, so manual entry stays exactly as
        // manual as it always was.
        public bool ShouldAssistPlayerPitEntry(RaceParticipant participant)
        {
            if (participant == null || !participant.isPlayer || participant.vehicle == null ||
                Track == null || State == null || participant.lapTracker == null)
            {
                return false;
            }

            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial || IsRaceFinished || StartCountdown > 0f)
            {
                return false;
            }

            if (participant.pitPhase != PitPhase.None || participant.isPitting || participant.retired || participant.finished)
            {
                return false;
            }

            if (!participant.vehicle.PitRequested || participant.activePitRequestSource != PitRequestSource.PreRacePlan ||
                participant.missedPitEntryThisLap)
            {
                return false;
            }

            TrackProgress progress = State.GetCurrentProgress(participant);
            return progress.normalized > TrackRuntime.PitApproachStartNormalized && progress.normalized <= TrackRuntime.PitCorridorStartNormalized;
        }

        const float PitEntryAssistTargetSpeedKph = 90f;
        // Same short, dedicated pit-entry look-ahead AiVehicleController uses
        // (PitEntryLookAheadMeters) - the normal racing-line lookahead is tuned
        // for reading corners far down the track, not for tracking the much
        // shorter pit-entry ramp whose lateral envelope changes quickly.
        const float PitEntryAssistLookAheadMeters = 18f;

        // Builds the actual steer/throttle/brake command for the assist window
        // identified by ShouldAssistPlayerPitEntry. Targeting geometry-fix: this
        // used to blend Track.PitEntryApproachLateral (a point deliberately
        // OUTSIDE the live track edge, behind the barrier that still stands
        // there before the real opening) against a lateral computed at the
        // car's current distance but attached to a racing-line point sampled
        // further down the track - together that steered the player straight
        // into the wall between PitApproachStartNormalized and
        // PitEntryRampStartNormalized, and the position/lateral mismatch meant
        // the two didn't even describe the same point on a curved approach.
        // Now delegates to TrackRuntime.ComputePitEntryTargetPoint, the exact
        // same two-stage (stay-on-track pre-position, then canonical ramp pose),
        // same-distance world-space target builder AiVehicleController's own
        // pit-entry steering already uses, so player and AI pit-entry geometry
        // can never diverge again. Callers must have already confirmed
        // ShouldAssistPlayerPitEntry(participant) is true.
        public VehicleCommand BuildPitEntryAssistCommand(RaceParticipant participant, VehicleCommand fallback)
        {
            VehicleCommand command = fallback;
            TrackProgress progress = State.GetCurrentProgress(participant);
            float speedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);

            Vector3 targetPoint;
            Quaternion targetRotation;
            Track.ComputePitEntryTargetPoint(progress.distance, PitEntryAssistLookAheadMeters, out targetPoint, out targetRotation);

            Vector3 toTarget = targetPoint - participant.transform.position;
            float steer = Mathf.Clamp(Vector3.Dot(toTarget.normalized, participant.transform.right) * 2.2f, -1f, 1f);

            float speedGapKph = PitEntryAssistTargetSpeedKph - speedKph;
            if (speedGapKph < -3f)
            {
                command.brake = Mathf.Clamp01(-speedGapKph / 35f);
                command.throttle = 0f;
            }
            else
            {
                command.brake = 0f;
                command.throttle = Mathf.Clamp01(0.2f + speedGapKph / 35f);
            }

            // Unstick fix: the old "ease off" branch capped throttle at 0.35
            // and kept steering at the (outboard) target - a car that touched
            // the pit wall just ground against it at 0 km/h forever, which is
            // exactly how the player ended up parked on the barrier with "Pit
            // entry approaching" on screen. A genuinely wedged car (near-zero
            // speed, hard against the right edge) now steers LEFT, away from
            // the wall the pit lane always sits on, with enough throttle to
            // actually free itself, then resumes chasing the target normally.
            if (speedKph < 3f)
            {
                bool againstWall = progress.lateralDistance > LocalHalfWidthAt(progress.distance) - 1.6f;
                if (againstWall)
                {
                    steer = -0.45f;
                    command.throttle = 0.5f;
                    command.brake = 0f;
                }
                else if (command.throttle > 0f)
                {
                    command.throttle = Mathf.Min(command.throttle, 0.5f);
                    steer = Mathf.Clamp(steer, -0.5f, 0.5f);
                }
            }

            command.steer = steer;
            command.ers = false;
            command.drs = false;
            return command;
        }

        float LocalHalfWidthAt(float distance)
        {
            F1Game.Track.ITrackQuery query = TrackQueryProvider.Active;
            return query != null ? query.WidthAt(distance) * 0.5f : Track.HalfWidthAt(distance);
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

            // ERS deployment is disabled while this car runs under any caution -
            // nobody is racing for position, so there is nothing to spend it on -
            // and it comes back the moment the green flag flies, where a strong
            // launch matters. FlagRules owns the flag consequence; the second
            // test adds the sector-wide local-yellow scope the passing ban uses.
            if (!FlagRules.OvertakingAllowed(FlagForParticipant(participant)) ||
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

        // The AiDifficultyProfile struct + GetAiDifficultyProfile (per-tier
        // decision-quality profiles) live in the RaceManager.AiProfiles.cs
        // partial (same class; the struct stays RaceManager.AiDifficultyProfile).

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
            GameEvents.Publish(new RetirementEvent(participant.driverId, participant.retirementReason));
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

            // A retired car can never move again, so it must be dropped off
            // the pit rail immediately - IsRailRolling/FindBayReleaseBlocker
            // ignore phase-None cars, so nothing behind it can queue on it.
            participant.pitPhase = PitPhase.None;
            participant.hasPitGuideState = false;
            participant.pitAwaitingRelease = false;
            participant.pitLaneHeldByOccupancy = false;

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
            // Premium visual pass: the post chain follows the same mood the
            // lighting uses, and quality 0 ("Low") turns it off entirely.
            // Both post backends are configured here: the URP Volume service (only
            // active under a scriptable pipeline) and the restored Built-in
            // CameraPostFx OnRenderImage chain (only attached when no SRP is active,
            // see CameraRig) - whichever matches the active pipeline takes effect.
            F1Game.Rendering.RaceVolumeService.GlobalEnabled = quality > 0;
            F1Game.Rendering.RaceVolumeService.ConfigureMood(night, rainThreat, twilight);
            CameraPostFx.GlobalEnabled = quality > 0;
            CameraPostFx.ConfigureMood(night, rainThreat, twilight);
            // URP migration: AA/shadow settings now come from the quality
            // level's pipeline tier asset; direct QualitySettings field writes
            // were inert under URP. The service switches the Unity quality
            // level (and thus the URP-Low/Medium/High asset) from the game's
            // 0-3 quality setting.
            F1Game.Rendering.GraphicsPresetService.Apply(quality);

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

            RenderSettings.reflectionIntensity = rainThreat ? 0.85f : 0.68f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // Premium visual pass: the old densities (0.00015-0.00024, exp-
            // squared) were effectively invisible inside the 2.6km draw
            // distance - the ground plane met the sky as a hard line with no
            // aerial perspective at all. These values leave the first ~300m
            // crisp and fade the far scenery gently into the horizon haze.
            RenderSettings.fogDensity = rainThreat ? 0.0011f : (mountain ? 0.0008f : 0.00065f);
            Color dryFog = desert ? new Color(0.65f, 0.55f, 0.42f)
                : (coastal ? new Color(0.5f, 0.62f, 0.68f)
                : (mountain ? new Color(0.4f, 0.5f, 0.46f)
                : new Color(0.44f, 0.54f, 0.52f)));
            if (twilight)
            {
                dryFog = new Color(0.48f, 0.3f, 0.3f);
            }

            RenderSettings.fogColor = night ? new Color(0.015f, 0.02f, 0.035f) : (rainThreat ? new Color(0.28f, 0.34f, 0.36f) : dryFog);
            GameObject lightObject = new GameObject("Primary Sun");
            lightObject.transform.SetParent(raceWorld.transform);
            lightObject.transform.rotation = Quaternion.Euler(night ? -15f : (twilight ? 12f : (desert ? 32f : (mountain ? 38f : 48f))), desert ? -42f : (coastal ? -30f : -56f), 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = night ? 0.08f : (twilight ? 0.95f : (rainThreat ? 1.0f : (desert ? 1.7f : (coastal ? 1.55f : 1.42f))));
            light.color = night ? new Color(0.6f, 0.7f, 1f)
                : (twilight ? new Color(1f, 0.62f, 0.4f)
                : (rainThreat ? new Color(0.76f, 0.86f, 0.92f)
                : (desert ? new Color(1f, 0.85f, 0.65f)
                : (coastal ? new Color(1f, 0.94f, 0.85f)
                : new Color(0.98f, 0.96f, 0.94f)))));
            // Performance: Hard rather than Soft shadows. Soft directional shadows
            // do a multi-tap PCF filter over the whole shadow map every frame; with
            // 22 cars each built from dozens of small cosmetic primitives plus
            // thousands of procedural track objects all casting into that map, the
            // soft filter was a dominant per-frame cost after the Built-in-RP revert.
            // Hard shadows keep grounded contact shadows at a fraction of the cost.
            light.shadows = LightShadows.Hard;
            light.shadowStrength = rainThreat ? 0.68f : 0.92f;
            light.shadowBias = 0.035f;
            light.shadowNormalBias = 0.22f;

            // Premium visual pass: a real sky. This used to be
            // RenderSettings.skybox = null - the camera cleared to a flat fog
            // color, so every horizon in the game was a solid-colored wall,
            // the single loudest "prototype" signal in any screenshot. The
            // built-in procedural skybox gives a physically-shaded sky
            // gradient, horizon haze, and an actual sun disc driven by the
            // primary sun light above (RenderSettings.sun), tuned per
            // environment mood. Assigned BEFORE the reflection probe below
            // renders, so car paint and glass pick up the sky too.
            RenderSettings.sun = light;
            Shader proceduralSky = Shader.Find("Skybox/Procedural");
            if (proceduralSky != null)
            {
                Material sky = new Material(proceduralSky);
                sky.name = "Race sky";
                sky.SetFloat("_SunSize", twilight ? 0.06f : 0.045f);
                sky.SetFloat("_SunSizeConvergence", 5f);
                sky.SetFloat("_AtmosphereThickness",
                    night ? 0.5f : (twilight ? 1.35f : (rainThreat ? 1.75f : (desert ? 0.9f : 1.05f))));
                sky.SetColor("_SkyTint",
                    night ? new Color(0.18f, 0.22f, 0.38f)
                    : (rainThreat ? new Color(0.38f, 0.42f, 0.46f)
                    : (desert ? new Color(0.55f, 0.5f, 0.42f)
                    : new Color(0.5f, 0.52f, 0.56f))));
                sky.SetColor("_GroundColor",
                    night ? new Color(0.03f, 0.035f, 0.05f)
                    : (rainThreat ? new Color(0.25f, 0.27f, 0.28f)
                    : (desert ? new Color(0.45f, 0.38f, 0.28f)
                    : (park ? new Color(0.26f, 0.32f, 0.24f) : new Color(0.32f, 0.31f, 0.29f)))));
                sky.SetFloat("_Exposure",
                    night ? 0.12f : (twilight ? 1.15f : (rainThreat ? 0.85f : 1.3f)));
                RenderSettings.skybox = sky;
            }

            GameObject fill = new GameObject("Atmospheric Fill");
            fill.transform.SetParent(raceWorld.transform);
            fill.transform.position = new Vector3(40f, 40f, -40f);
            Light fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.intensity = night ? 1.8f : 0.64f;
            fillLight.range = 350f;
            fillLight.shadows = LightShadows.None;
            // Performance: a 350m-range pixel point light in Built-in Forward adds an
            // extra per-object forward pass to everything it touches. As a broad
            // ambient fill it does not need per-pixel quality, so render it as a cheap
            // vertex light instead.
            fillLight.renderMode = LightRenderMode.ForceVertex;

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
            probe.intensity = rainThreat ? 0.85f : 0.68f;
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
                    floodLight.shadows = LightShadows.None;
                    // Performance: six additive pixel point lights on a night track
                    // each add a forward pass to every object in range. Vertex lighting
                    // keeps the floodlit look at a fraction of the fill cost.
                    floodLight.renderMode = LightRenderMode.ForceVertex;
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
            float localHalfWidth = LocalHalfWidthAt(progress.distance);
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
            // Gates (incl. the RaceLaps<=3 short-race exemption) live in the
            // unit-tested rulebook - see PenaltyRules.ShouldApplyMandatoryPitPenalty.
            if (!PenaltyRules.ShouldApplyMandatoryPitPenalty(
                    CurrentSession == RaceWeekendSession.Qualifying,
                    IsTimeTrial,
                    RaceLaps,
                    participant.pitStops,
                    participant.mandatoryPitPenaltyApplied))
            {
                return;
            }

            participant.mandatoryPitPenaltyApplied = true;
            AddPenalty(participant, PenaltyRules.MandatoryPitPenaltySeconds, PenaltyRules.MandatoryPitReason);
            if (participant.isPlayer)
            {
                SessionMessage = "No mandatory stop: +10s";
            }
        }

        void AddPenalty(RaceParticipant participant, float seconds, string reason)
        {
            participant.penaltiesSeconds += seconds;
            participant.penaltyReason = PenaltyRules.AppendPenaltyReason(participant.penaltyReason, reason);
            GameEvents.Publish(new PenaltyIssuedEvent(
                participant.driverId,
                PenaltyKind.TimePenalty,
                seconds,
                reason));

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
                // Time trial is a pure lap-time exercise: always start the player on
                // the fastest slick, whatever compound was last selected elsewhere.
                if (IsTimeTrial)
                {
                    return TyreCompound.Soft;
                }

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
            if (Mathf.Abs(targetProgress.lateralDistance) > LocalHalfWidthAt(targetProgress.distance) - 1.2f)
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
