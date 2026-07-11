# Continuation State

Living ledger for the production rebuild. Updated after every logical commit.

## Current run (branch `claude/read-and-complete-ipelrl`, from origin/main 15e2af3)
Rules-integration pass (directive §12 item 4: pit lane + AI + rules). All
static-only, not compiled or run:

1. `a014049` StartProcedureRules is the live start authority: light timing,
   sequence duration, jump-start tariff routed through it; NEW false-start
   (anticipation) judgement at lights-out. Tests added.
2. `4862172` FlagRules is the live flag-policy authority: one
   RaceControlState→RaceFlag mapping (GlobalRaceFlag / FlagForParticipant);
   IsOvertakingAllowed now DERIVED (eight scattered writers removed); DRS,
   ERS, overtaking, pace-limited all consult FlagRules; VSC/local-yellow cap
   numbers live in FlagRules. Behavior preserved. Tests added.
3. `75cc142` PitServiceRules owns stop duration: tyre windows preserved, NEW
   damage-repair hold (3-7.5s over 12% damage) and rare (4%) crew fumble,
   engineer messaging. Tests added.
4. `50038b4` AiPitStrategyRules owns the AI pit OR-chain thresholds
   (routine/compound/destroyed/grip/strategy-lap/final-lap); NEW green-flag
   weather-crossover trigger with per-driver reaction stagger. Tests added.
5. `27adfe2` Blue flags implemented end to end (previously absent): detection
   in RaceManager, AI yield (attack suppression + straight-line lift), HUD
   banner + radio for player, 20s→+5s compliance penalty via PenaltyRules.

6. `e15708c` SessionFlow wired live: career weekend routing
   (GameBootstrap.StartCareerRace) and the Q1→Q2→Q3 phase advance now consume
   it; no rules class in F1Game.Race.Rules is dead code any more.
7. `6dd9fe4` RaceManager-side AI strategy cores moved into AiPitStrategyRules:
   RecommendedPitLap (ties-to-even rounding preserved), undercut call, SC/VSC
   cheap-stop decision table. RaceManager keeps only the live-state boundary.

All eight rule classes (Flag, StartProcedure, Penalty, PitRequest, PitService,
AiPitStrategy, SessionFlow, QualifyingProgression + RaceClassifier/
ChampionshipPoints) now have live consumers and EditMode tests.

8. `f933663` Physics rulebook wired live: VehicleController consumes
   AeroModel (drag/DRS/downforce/slipstream constants + formulas) and
   PowertrainModel (ERS boost/drain) - algebraically identical, authority
   moved. PhysicsModelsTests added. Tyre slip-curves/brake fade remain the
   deeper migration target (would change handling; needs runtime validation).
9. `b818e22` Production HUD parity pass: GapsModule, FlagModule (incl. blue),
   PitPenaltyModule, StartLightsModule; snapshot now carries real FieldSize/
   Fuel01/gaps/flag/pit/penalty/lights. FIXED real bug: relay published
   remaining tyre life into the worn-fraction field (wear bar inverted).
10. `23c35e0` Production timing tower (TimingTowerModule) fed by HudRaceOrder,
   a fixed-buffer 2Hz running-order snapshot; relay OnDestroy now clears the
   static HUD snapshots (lifecycle fix).

11. `c995b6c` TimesModule (current/last-with-invalid-strike/best/session best;
    new public RaceManager.SessionFastestLap) + WeatherModule chip.
12. `e4ce731` SectorsModule (S1/S2/S3, green on session-best match) via new
    RaceStateManager.GetSectorTimes/GetOverallBestSector float accessors.

Production HUD module set now: position, lap/clock, speed/gear/rpm, ERS/DRS,
tyres, fuel, gaps, lap times, sectors, flag (incl. blue), pit/penalty,
weather, start lights, timing tower, notification feed. Legacy RaceHud stays
the ordinary default until in-editor validation; UiSessionCoordinator
guarantees exactly one live HUD.

13. `6a20f4a` First production career screen: CareerStandingsView/Presenter
    (drivers/teams tabs, pooled rows, player highlight), registered in
    UiShell, mapped in ProductionUiBridge, reachable live from a new
    Standings button on the production main menu (career-gated). Legacy
    career hub untouched.

14. `1c70f46` Season calendar tab on the standings screen (drivers/teams/
    calendar TabBar, DONE/NEXT markers from currentRound, pooled RenderLines).

15. `2eae476` Production career hub live: Career button routes to
    CareerHubView when a save exists (next-event card, Continue labelled by
    SessionFlow, Standings & Calendar, Full Career Menu legacy fallback).

16. `5a382e2` Track-query seam live: RaceManager DRS-zone permission +
    track-limits width read ITrackQuery (null-safe). COHERENCE FIX: authored
    adapter no longer auto-selected for the reference circuit (world is
    legacy-built on every circuit, so authored distances don't share the live
    parameterization); authored stays behind f1game_authored_track until
    track construction itself is authored.

17. `d7bcbb9` All 17 remaining RaceManager/AI half-width consumers migrated
    onto the LocalHalfWidthAt seam helpers (identical values today; authored
    backend now covers width queries everywhere the race layer asks).

18. `01d97c5` Aurora Park raceable: TrackManager builds the aurora-park
    world from the authored TrackDefinitionAsset (centerline/width/DRS from
    the definition, NormalizeTrackLength deliberately skipped for real-scale
    authored geometry, downstream mesh/kerb/barrier/pit passes unchanged).
    Quick-race card appended on the production track select with a
    synthesized event. World+queries share one parameterization; first drive
    needs the in-editor pass.

19. `385ff98` Authored per-point width honored: TrackRuntime carries an
    authored half-width profile interpolated by HalfWidthAt (single width
    source), so world build + all gameplay width checks follow it. Aurora
    Park fills it from the definition; procedural layouts unchanged.

20. `1388b98` Career hub shows the player's championship line (P/pts/wins);
    fixed the hub's season-label binding (scaffold header had been bound as
    the season text).

21. `7ede967` monza_low_downforce converted to the authored pipeline via the
    new AuthoredCircuitCatalog (trackId -> definition registry, single
    source); BuildMonzaLayout retired; authored branch generalized
    (BuildAuthoredLayout(definition)) with emergency procedural fallback;
    forced-authored validation path reads the catalog.

22. `4a81af3` Spec-based conversion infra (LegacyCircuitSpec; style/kerb/
    elevation fidelity; TrackDefinitionAsset environmentStyle +
    kerbStartOffset + anchorSubdivisions fields).
23. `ef6f84c` ENTIRE CALENDAR converted to the authored pipeline (scripted
    extraction, 24 specs incl. Bahrain + reference circuit); all 22
    remaining Build*Layout methods and the dispatch chain retired; Bahrain
    template kept as the single emergency fallback world. Procedural track
    ownership is out of the ordinary path (Phase C milestone) - runtime
    validation of the converted calendar is REQUIRED in-editor before this
    can be called complete.

Matrix note: "Authored tracks default / every track migrated" is now
implemented-but-runtime-unverified; "procedural track removed" holds except
the deliberate Bahrain emergency template.

24. `fed80ff` TRACK_PIPELINE + KNOWN_ISSUES updated to the authored-calendar
    reality (honest scope: geometry authored; world-build passes still
    TrackManager's; authored builder lacks kerb/barrier parity).

DEFERRED with reason - per-point camber in the legacy road mesh: tilting
only the road cross-section would vertically misalign kerbs/barriers (they
place at centerline height with a flat cross-section assumption, mismatch
up to ~0.4 m on Aurora Park). Doing it right needs one shared
SurfacePointAt(distance, lateral) consumed by road mesh + kerbs + barriers
+ stripes, audited together - an in-editor-verifiable change.

25. `d52ac1d` Production driver-profile screen (records-store stats + career
    identity), live from a new Driver Profile button on the career hub.

26. `8999c52` AuthoredCircuitCatalogTests: every calendar id + reference
    circuit produce structurally sound definitions (length band, widths,
    DRS bounds, sectors, grid/pit counts, racing-line alignment).

DEFERRED with reason - production results screen: the legacy results screen
is a rich nine-section report (podium, player summary, two telemetry cards,
teammate, strategy, incidents, achievements, race-control timeline, full
classification, championship impact, plus rematch/menu actions at
RuntimeUi.ShowResults:4759). Replacing it production-first before parity
would lose real function; it needs its own dedicated parity pass (likely
several sessions), not a thin v1 swap.

27. `14aeab4` AI jump starts are real: rolled at spawn (consistency-scaled
    chance in StartProcedureRules), physically released before lights-out,
    judged by the same rulebook path as the player. Tests added.
28. `caf86eb` HUD parity gap analysis captured in Docs/HUD_PARITY_GAP.md
    (tiered checklist, legacy stays default until Tiers 1-4 live) +
    tranche 1: DamageModule, fuel emergency states, live pit-box progress
    chip, tower compound letters + interval column.

29. `19dfaa3` HUD tranche 2: RaceControlModule (caution headline + restart
    countdown + red-flag reason/SC queue + live pace cap).
30. `a9d9265` HUD tranche 3: PitStrategyModule (plan lap/compound, BOX THIS
    LAP, BOX CONFIRMED, SC WINDOW prompt). Checklist updated both times.

31. `bc60d92` HUD tranche 4: SessionLabelModule (kind + event name), ERS
    meter dims under race-control lockout. Checklist updated.

32. HUD tranche 5: SessionMessageModule live.
33. `52b96d8` HUD tranche 6: interactive Cancel Pit Request button. NEW
    UI->race channel HudCommands (F1Game.Core, mirror of HudTelemetry);
    RaceEventRelay.Attach registers CancelPitRequest, OnDestroy clears it;
    snapshot CanCancelPit gates visibility. Tier 1 closed.
34. `8b47cbe` HUD tranche 7: tyre temp/lockup status + pedal input bars.

35. `1c9cb24` HUD tranche 8: quali/ghost delta (shared numeric core
    TryGetQualifyingDelta/TryGetGhostDelta) + slipstream tow pill.
36. `1a6c272` timing-tower per-row DRS dot.
37. `f0920ed` track minimap: HudTrackMap channel (outline + per-frame dots) +
    pooled-dot MinimapModule.
38. `3c40e70` HUD tranche 10: HudToastEvent - RaceManager.QueueHudToast
    publishes at source (legacy queue preserved), NotificationFeed subscribes;
    all watcher/relay toasts reach production.

39. `0ec330f` HUD tranche 11: BigMomentModule (LIGHTS OUT/GREEN FLAG/FINAL
    LAP centre flashes, UI-side edge detection, no race-layer change).
40. `1843bbb` normalized new .meta files to Unity's canonical format.

HUD FUNCTIONAL PARITY REACHED (Docs/HUD_PARITY_GAP.md): every core telemetry,
race-control, pit/strategy, interactive (cancel-pit), notification, minimap,
big-moment and session-delta element is live in production. REMAINING before
RaceHud can be RETIRED as default = small cosmetic tail (quali feedback
panel, TT checkpoint counter, per-corner 2x2 tyre grid, progress strip,
DRS-available cue, VICTORY flash) + an in-editor VISUAL validation pass
(layout, minimap rendering, dot scaling). Visual-verification-bound, so the
legacy RaceHud stays the ordinary default until that pass runs.

Engineering-rule note: the remaining directive items are dominated by work
that CANNOT be done coherently in a static-only (no-compiler, no-Unity)
environment without violating "preserve working gameplay until the
replacement is validated": results-screen replacement (rich 9-section report,
visual), Phase E service extraction from the 11k-line uncompilable
RaceManager (high regression risk uncompiled), physics handling swaps (tyre
slip/brake curves would CHANGE handling - not algebraically identical to the
live tuning, unlike the aero/ERS that were), and full career/settings/
accessibility/localization depth (large, visual). Multiplayer (Phase N) is
DEFERRED and out of scope.

41. `652cb4c` Phase M diagnostics: DiagnosticLog (F1Game.Core.Diagnostics) -
    log categories + per-category gating + stable DiagnosticCode error codes;
    GameLog category/Error overloads (plain overloads untouched);
    JsonSaveService emits coded save errors. DiagnosticLogTests added.

42. `0374a2a` routed RaceManager track-query + ProductionSessionUi HUD-show
    diagnostics onto the Track category / HudBindFailed code.

43. `37e65a1` routed every remaining runtime Debug.LogError onto a stable
    DiagnosticCode (new UiScreenMissing/InputActionsMissing codes + Input log
    category); editor-tool logs left as developer-facing. Tests extended.
44. `c1ccd1a` extracted FuelStrategy rulebook (needed/delta kg, delta laps,
    save-target); VehicleController.UpdateFuelProjection delegates -
    behavior-identical, now unit-tested. Mirrors the rules-extraction pattern.

45. `76ca131` extracted PitPlanRules (NextPlannedStopIndex / HasPending);
    RaceManager.NextPlannedPitLapFor player branch delegates -
    behavior-identical, unit-tested.

F1Game.Race.Rules now: ChampionshipPoints, QualifyingProgression,
RaceClassifier, PenaltyRules, PitRequestRules, PitServiceRules,
AiPitStrategyRules, SessionFlow, FlagRules, StartProcedureRules, FuelStrategy,
PitPlanRules - all with EditMode tests and live consumers.

46. `1382e91` PRODUCTION RESULTS SCREEN live with legacy fallback:
    ResultsView/Presenter (full classification, player highlight, actions
    reuse the legacy bootstrap hooks); ProductionUiBridge.TryShowResults maps
    RaceResultEntry (gap/DNF/penalty mirror the legacy table);
    RaceManager's two race-result sites try production first, fall back to
    BeginResults+ui.ShowResults(legacy). Registered in UiShell. Qualifying
    results still legacy.

47. `cd86ee9` Production qualifying-results screen (career): Results screen
    gains a compact variant; bridge maps QualifyingResultEntry; RaceManager's
    two qualifying sites gated production-first; quick-race quali stays legacy.
48. `7f8ab38` Production PAUSE OVERLAY: fills a real gap (paused production-HUD
    sessions had no menu). PauseOverlay on the shell overlay layer (UiShell
    exposes ModalLayer) with Resume/EndPractice/MainMenu/Restart/Quit wired to
    the legacy race+bootstrap hooks; SetPaused(race, paused) shows/hides it.

Session frontend migration status: HUD (functional parity), race + career
qualifying results, and pause are now production-first with legacy fallback.
Remaining legacy-only session UI: MFD/radio overlays, restart-confirm modal,
accessibility/settings overlays, replay/spectator/photo controls.

49. `58fae93` Restart confirmation on the pause overlay (arm-then-confirm,
    self-contained; ModalService is unused so a first prefab-clone usage would
    be unverifiable here).
50. `4d0a007` HUD tranche 12: QualiFeedbackModule + CheckpointsModule (TT).

§10 static review over the full run diff: clean (no merge markers, whitespace
clean, all metas present, no dup GUIDs, braces balanced, duplicate-HUD guard
intact - production-first with legacy fallback, never both).

51. `14dd343`/`4839b07` REPLAY CAPTURE made live (was built-not-live):
    ReplayCaptureService owns the ReplayRecording ring buffer, records all
    cars' transforms/speed at 20Hz into a 6000-frame bounded window, gated by
    `f1game_replay_capture`. RaceManager drives Begin/Tick/End and pushes
    timeline markers (session start/end, flags via LogRaceControlHistory,
    pit stops via BeginPitStop, incidents via RegisterIncident); exposes
    ReplayRecording. Read-only over cars, bounded memory, off-path when
    disabled. ReplayRecordingTests added.
52. TELEMETRY CAPTURE made live (was built-not-live): TelemetryCaptureService
    owns a TelemetryRecorder, samples the player car's channels (speed,
    throttle/brake/steer, gear, rpm proxy = speed/top-speed, ERS, DRS, tyre
    wear, live qualifying/ghost delta) at 20Hz into an 18000-sample cap, gated
    by `f1game_telemetry_capture`. RaceManager drives Begin at grid spawn and
    Sample each racing tick; exposes TelemetrySampleCount + ExportTelemetryCsv
    (writes persistentDataPath CSV for engineer debrief). Added
    VehicleController.LastSteerInput accessor for the steer channel.
    TelemetryRecorderTests added.
53. TELEMETRY DEBRIEF (in-game consumer of the live capture): TelemetryDebrief
    (F1Game.Core.Diagnostics) turns a captured TelemetryRecorder trace into a
    compact engineer summary - top/avg speed, top gear, full-throttle/braking/
    coasting time share, DRS %, avg ERS, tyre-wear start/end/delta, distance,
    duration. Pure over the samples (no race-path touch), so unit-tested.
    TelemetryRecorder now exposes read-only Samples; RaceManager exposes
    BuildTelemetryDebrief(). Completes the telemetry capture into a real
    in-game consumer (the CSV export remains for offline tools).
    TelemetryDebriefTests added.
54. REPLAY TIMELINE (in-game consumer of the captured markers): ReplayTimeline
    (F1Game.Race) turns a ReplayRecording into an ordered highlight list
    (flags/overtakes/pit stops/incidents/laps, session framing excluded) with a
    session-relative m:ss.t clock, per-kind counts and the recorded window
    length. Pure over the markers/frame span, so unit-tested. RaceManager
    exposes BuildReplayTimeline(). Both capture models (replay + telemetry) now
    have a pure in-game consumer. ReplayTimelineTests added.
55. WEATHER/TRACK-STATE rules extracted (directive §12 item 5 "weather/track-
    state depth"): WeatherRules (F1Game.Race.Rules, engine-free) now owns the
    mixed-forecast swing gating (half-distance first swing, High-variability
    three-quarter second swing, short-race clamp), the wet↔dry toggle, and the
    track-evolution rubber ramp + grip multiplier (build-in slow / rain-wash
    fast, +5% max grip). RaceManager.UpdateWeatherTransition/UpdateTrackEvolution
    delegate to it and keep the live state/engine calls; algebraically identical,
    authority moved. Local TrackEvolutionMaxGripBonus const removed (now in
    WeatherRules). WeatherRulesTests added.
56. RELIABILITY rules extracted (directive §12 item 4 "race reliability"):
    ReliabilityRules (F1Game.Race.Rules, engine-free) owns mechanical-failure
    eligibility (mode 0 off / mode 1 AI-only, player exempt / mode 2 everyone;
    excluded pre-race and while pace-limited under SC/VSC) and the reliability→
    per-second-chance curve (0-rated at max, 100-rated at min), plus the
    per-check roll test. RaceManager's race-control tick delegates to it; the RNG
    call (Random.value) and retire/incident wiring stay in RaceManager, so it is
    behavior-identical and deterministic. ReliabilityRulesTests added.
57. DRS availability consolidated into engine-free DrsRules (directive §12 item
    4 "full rules integration"): DrsRules owns the detection-point gap decision
    (quali/TT earn every zone; a race needs 2 completed laps + a ≤1s gap to the
    car ahead) AND the availability ordering (wet/restart-cooldown/flag → in a
    DRS zone → session → laps → earned zone eligibility, which is not re-checked
    against the live gap). RaceManager.EvaluateDrsDetectionGap and IsDrsAvailable
    resolve the live state and delegate; the race path still only runs the
    heavier interval scan when a gap is actually required. Behavior-identical.
    DrsRulesTests added.
58. Telemetry debrief now has a LIVE runtime consumer: FinishRace logs a
    compact one-line engineer debrief (samples, top/avg speed, full-throttle/
    braking/coasting %, DRS %, tyre-wear delta) via GameLog(LogCategory.Race)
    from the captured player telemetry. Behavior-changing (the capture now
    visibly produces a race-end artifact) but low-risk: read-only over the
    debrief, no UI/GPU, no-op when capture is off/empty. A future debrief panel
    reads the same TelemetryDebrief.Summary via BuildTelemetryDebrief().
59. Replay timeline now has a LIVE runtime consumer too (symmetric with #58):
    FinishRace logs a compact replay summary (frames, cars, length, flag/
    overtake/pit/incident/highlight counts) via GameLog(LogCategory.Race) from
    the captured markers. Same low-risk shape: read-only, no UI/GPU, no-op when
    capture is off/empty. Both capture models now feed the live loop end-to-end
    (capture → pure consumer → race-end log), with the richer UI surfaces (scrub
    playback, on-screen debrief panel) still waiting for a compiler/editor.

§10 static review re-run over the extended run diff (entries 51-59): clean -
123 files vs origin/main, git diff --check clean, no merge markers, every
changed .cs has a .meta, no duplicate GUIDs tree-wide, all new engine-free
assemblies (F1Game.Race rules/consumers) verified free of UnityEngine usage.
main remains the stable checkpoint (never pushed to); branch is 93 commits
ahead and current with origin/main. Runtime validation of everything in this
run remains PENDING (no compiler/editor here) and is not claimed.

Note on what was deliberately NOT extracted: ShouldAiUseErs interleaves
conditional Random.value calls with live scans; extracting it risks diverging
the race RNG stream for marginal gain, so it stays inline (behavior/determinism
preserved). Deeper physics handling (tyre slip-curves/brake fade), AI racecraft
retuning, new UI screens (TT result, replay scrub, debrief panel) and RaceManager
decomposition all genuinely require a compiler/editor/playtest and are honestly
deferred, not claimed complete.

60. HUD parity Tier 1-4 COMPLETED (ledger candidate b, toward switching HUD
    ownership): added the last two Tier ≤4 gaps as production modules with
    RaceHud as the live fallback. PitStatusModule renders a composed pit-status
    headline (box/limiter incl. fast-exit 108 cap / queued request / cancel
    confirmation) tinted by a new Events.PitEmphasis, built in RaceEventRelay
    (BuildPitStatus) mirroring RaceHud.UpdatePitCard. TrackLimitFlashModule does
    UI-side edge detection over a new snapshot TrackLimitWarnings count and
    pulses amber, like BigMomentModule. Snapshot gained PitStatusText/
    PitStatusEmphasis/TrackLimitWarnings; both modules assembled onto HudRoot.
    Every Tier 1-4 item is now implemented in production; HUD ownership switch
    now gated only on the in-editor visual pass (PENDING - no Unity here), so
    RaceHud stays the live default. HUD_PARITY_GAP.md updated.

61. PRODUCTION SETTINGS SCREEN built + wired (completion matrix "settings+a11y
    UI" - was the only unbuilt production frontend screen; menu/career/standings/
    profile/results were already wired behind the readiness flag): new
    F1Game.UI.Screens.Settings (SettingsView/SettingsPresenter, ViewModels
    SettingsModel/SettingsRowModel), UiScreenFactory.BuildSettings, registered in
    UiShell. Presents the live GameSettingsData as a categorised read-only
    summary (Gameplay / Driving / Audio / Display & Accessibility) via pooled TMP
    rows - production-first DISPLAY with a "Classic Settings" button that
    LeaveToLegacy for EDITING, so exactly one editor path stays live while the
    inline production controls are built. Production main menu OnSettings now
    routes here (was straight-to-legacy); OnClassic/OnBack wired. Behind the same
    ProductionUiReadiness flag as the rest of the production frontend, legacy
    RuntimeUi settings is the fallback. Visual validation PENDING (no editor).

74. LOCALIZATION foundation (matrix item "localization runtime", not previously
    started): Localization (F1Game.Core) - a key→string table with an English
    fallback baked in at every call site, so the game reads identically until a
    translation table is loaded (identity default = zero behaviour change).
    Unknown/blank keys degrade to the fallback (partial translations are safe).
    Demonstrated end-to-end by routing the production settings category headings
    through Localization.Get; expanding coverage is now mechanical.
    LocalizationTests added. This is the seam/foundation, not full coverage.
    Coverage expansion: the six static production screen titles (SELECT CIRCUIT,
    CAREER, DRIVER PROFILE, SETTINGS, CHAMPIONSHIP STANDINGS, RACE STRATEGY) in
    UiScreenFactory now route through Localization.Get with English fallbacks
    (Results/MainMenu titles are model-driven/none, left as-is). The settings
    screen's ~20 row labels are localized in one place via the Row helper
    (key derived from the English text, e.g. settings.row.race_laps), so the
    whole settings screen's static chrome is now translatable with English
    fallbacks throughout. Game-wide button coverage: UiScreenFactory.CreateButton
    localizes every button label by a derived key (button.<slug>) in one place,
    so all production buttons (menu, career hub, pause, results, settings,
    track/strategy) are translatable at once; dynamic labels (later SetText,
    tyre names) fall through to their text. HUD moments too: the BigMomentModule
    flashes (LIGHTS OUT / GREEN FLAG / FINAL LAP) and the track-limits flash now
    resolve via Localization.Get with English fallbacks. Also the HUD session
    label (QUALIFYING / TIME TRIAL / PRACTICE / RACE) and the LAP prefix. Net:
    the localization seam now covers the production frontend chrome (all screen
    titles, headings, settings rows, every button) and the prominent HUD strings,
    all with English fallbacks - a broad, safe foundation.
75. LOCALIZATION loading + validation infrastructure (ledger next-safe task):
    Localization.Parse (pure, testable) reads a key=value document (# comments,
    blank/malformed lines skipped, later duplicates win); LoadFromText loads it;
    MissingKeys(required) reports uncovered/blank keys for validation tooling.
    LocalizationLoader (engine side, kept separate so Localization stays pure)
    loads Resources/Localization/<lang>.txt, clearing to English on an empty/"en"
    language or a missing file. GameBootstrap.Awake loads the language from the
    f1game_language pref (default "en" → no-op), so the whole path is live end to
    end: pick a language + drop a translation file and it applies, with nothing
    breaking out of the box. LocalizationTests cover parse/load/validation.
76. PERFORMANCE CAPTURE diagnostics connected (was built-not-live, zero
    consumers; ledger task "live-consumer wiring + tests for built systems"):
    GameBootstrap.Update now triggers PerformanceCapture.Begin on F10 (labeled
    race/frontend by CanDrive) - a dev-only no-op unless pressed; legacy Input is
    safe (activeInputHandler=Both). The percentile statistic is extracted to a
    static, pure PerformanceCapture.Percentile(sorted, p) and unit-tested
    (nearest-rank, empty/null, clamp). PerformanceCaptureTests added.
77. Test coverage for the GameObjectPool utility (backs pooled VFX + HUD rows,
    previously untested): GameObjectPoolTests pin prewarm, take/return recycling
    (reuse the same instance, active-state toggling), grow-on-demand beyond the
    prewarm count, ReturnAll, and the no-op on returning a foreign object.
    Instances are created/destroyed within the test.
78. Test coverage for TrackSplineSampler (authored-track runtime, untested):
    TrackSplineSamplerTests pin the build invariants (samples + positive length),
    closed-loop distance wrapping (modulo, negative → near-end, always in
    [0,len)), open-spline clamping, and the zero cumulative distance at the
    start line, on a simple square loop (no interpolation-value assertions).
79. LOCALIZATION key-harvesting authoring tool (ledger task "content authoring
    tools"): Localization records each requested key + its English source while
    harvesting (StartRecording/StopRecording/IsRecording), and
    ExportRecordedTemplate emits a sorted key=english template - crucially
    capturing the runtime-DERIVED keys (button.<slug>, settings.row.<slug>) that
    no static source scan could find. GameBootstrap F11 toggles harvesting and
    writes localization_template.txt next to the saves. The template round-trips
    through Parse (load-ready). Default off, zero hot-path cost when idle.
    LocalizationTests cover recording/export/round-trip.
80. REPLAY timeline CSV export (ledger task "replay data integration behind a
    compatibility switch"; symmetric with the telemetry CSV): ReplayExport
    (F1Game.Race, pure) serializes a recording's highlight markers to CSV -
    time, session-relative clock, kind, car index, comma/quote-safe label -
    header-only when empty. RaceManager writes it at race end behind the
    default-off f1game_replay_export pref, track-named. Only markers (not every
    per-frame transform) are written, keeping the file small. ReplayExportTests
    cover header-only, per-marker rows + relative clock, and label quoting.
81. Physics-model test coverage completed: PhysicsModelsTests now also pin
    TyreModel.LongitudinalGrip (zero at no slip, rises with slip), LoadSensitivity
    (unity at reference load, falls under more), WearPerLap (aggression/compound
    scaling), and AeroModel Downforce (speed² law), Drag (DRS cut) and
    SlipstreamDragFactor (clamped tow) - the formulas that back the live aero/ERS
    authority and the built tyre model.
73. CAR-DEVELOPMENT (R&D) maths extracted into testable CarDevelopmentRules
    (F1Game.Core; matrix "Career systems / R&D"): the pure project success-chance
    (base + department-level nudge + risk-mode shift, clamped), development-weeks
    (budget → level shorten → risk stretch/compress, min 1) and cost (conservative
    premium) computations moved out of CareerManager, which now delegates
    (ComputeProjectSuccessChanceForLevel / ComputeProjectWeeksForLevel /
    ComputeProjectCost). Algebraically identical, authority moved; the career
    layer keeps the project state, RNG rolls and reward application. Also folds
    the department-upgrade cost (level × 400) into the same rules class
    (GetDepartmentUpgradeCost delegates). CarDevelopmentRulesTests added.
    Follow-up: the per-stat upgrade-effect math (raw delta × per-stat scale ×
    experimental boost, rounded) is now CarDevelopmentRules.ApplyStatDelta -
    ApplyUpgradeSet's ten inline stat lines delegate to it (the shared path for
    the player AND every AI team's R&D), and the duplicated ExperimentalBonusScale
    const moved to the rules class. Tests cover the boost.
72. DIRTY-AIR cornering model wired live behind a switch (makes the built-not-
    live AeroModel.DirtyAirLoss live; matrix "physics"): a close car ahead now
    robs front-end grip IN CORNERS only (gated on |LastSteerInput|), via
    AeroModel's decay curve scaled to a modest share (~7% max right behind).
    RaceManager's slipstream loop additionally feeds the nearest-ahead gap
    (car-lengths) to VehicleController.SetDirtyAirGap; the penalty is gated by
    the default-OFF f1game_dirty_air pref so the tuned race feel is unchanged
    until validated. The straight-line tow is untouched (dirty air is corner-
    only), so following still helps on straights and hurts in corners as in real
    racing. PhysicsModelsTests covers DirtyAirLoss. On-path magnitude PENDING
    in-editor tuning; default path behaviour-identical.
71. Telemetry CSV export now has a live trigger (completes the built-but-uncalled
    TelemetryRecorder.ExportCsv → RaceManager.ExportTelemetryCsv path): FinishRace
    writes the full player trace to persistentDataPath at race end, gated by the
    opt-in f1game_telemetry_csv_export pref (default OFF, so an ordinary race
    never touches disk), track-named so reruns overwrite. Logs the path. The
    telemetry capture now has three consumers: race-end debrief log, results-
    screen line, and the opt-in CSV for offline engineer analysis.
70. Time-trial ENTRY migrated to the production frontend: OnTimeTrial now routes
    through the shared production TrackSelect screen (a timeTrialFlow flag makes
    OnTrackChosen skip the pit-strategy step) and hands off to the proven legacy
    BeginTimeTrial(event) for the actual session start - production-first for the
    track picker, legacy for the delicate start transition (not reimplemented).
    Quick race sets the flag false. When production UI is off the legacy menu
    owns time-trial entry unchanged. Together with #69 the whole time-trial loop
    (entry → drive → result) is now production-first with legacy fallbacks.
69. PRODUCTION TIME-TRIAL RESULT screen (ledger candidate a, the last remaining
    production session-end screen): TryShowTimeTrialResult(race) reuses the
    compact ResultsView (like qualifying) to show the player's session best lap,
    a TRACK RECORD / SESSION BEST tag (vs PlayerRecordsStore.GetBestLap) and the
    standing record row when not beaten. Triggered from the production pause
    overlay's Main Menu action for a time trial WITH a valid lap - production-
    first, with the existing straight-to-menu as the fallback when production UI
    is off or no clean lap was set (so behaviour is unchanged on the legacy
    path). "Try Again" → time-trial setup, "Main Menu" → exit. Behind the
    ProductionUiReadiness switch. Visual/flow validation PENDING (no editor).
68. Replay capture gains a player-facing consumer on the RESULTS screen
    (symmetric with #64's telemetry): RaceManager.ReplayHighlightLine() summarises
    the race events (overtakes / incidents / pit stops) from BuildReplayTimeline;
    RaceDebriefLine() stacks it over the telemetry driving summary (either half
    omitted when empty), and FinishRace now passes the combined line to
    TryShowResults. Both capture models now feed a player-facing surface and the
    diagnostics log. No new layout (still the results subtitle).
67. Production HUD honours the Compact HUD setting (closes the last inline-
    toggle consumer gap): new snapshot CompactHud flag (from settings) hides the
    secondary readouts - the slipstream/TOW pill and the throttle/brake pedal
    bars - in compact mode. All four inline accessibility toggles now take live
    effect in production (Units→speed readout, UI Animations→transitions, Camera
    Shake→camera rig, Compact HUD→secondary HUD readouts).
66. Production settings inline editing extended to key GAMEPLAY settings: a
    "Quick Gameplay Settings" row (Difficulty cycle Easy→Medium→Hard→Expert,
    ERS Strategy cycle Balanced→Attack→Harvest, Manual/Auto Gears toggle) using
    the same flip/Save/re-present cycle as the a11y toggles. Production now owns
    inline editing for the highest-traffic gameplay + accessibility settings;
    the "Classic Settings" fallback is deliberately KEPT for the long-tail
    controls (and as a safety net) until in-editor validation - one editor path
    per setting preserved. SettingsView/Presenter/Model + BuildSettings extended.
65. Production UI honours the UI-Animations (reduced-motion) accessibility
    setting: the bridge sets UiShell.Transitions.ReducedMotion from
    settings.uiAnimations on shell creation and re-applies it when the toggle
    flips (ApplyAccessibilityToShell), so TransitionService completes every
    screen/panel fade instantly when animations are off. Closes the loop on #63's
    editable toggles: Units (HUD consumer, #62) and UI Animations (transition
    consumer) are now both edited AND consumed in production; Camera Shake /
    Compact HUD persist but their consumers (camera rig / HUD layout) need the
    in-editor visual pass.
64. Engineer debrief surfaced on the production RESULTS screen (exact-next-task
    item "placing the debrief summary on the results screen"): RaceManager.
    TelemetryDebriefLine() formats a compact one-liner (top speed, full-throttle/
    braking %, DRS %, tyre-wear delta) from the live telemetry capture;
    FinishRace passes it through ProductionSessionUi/ProductionUiBridge.
    TryShowResults (new optional debriefLine param, back-compatible), which
    appends it as a smaller second subtitle line. No new layout element (reuses
    the results subtitle), empty/no-op when capture is off. The telemetry capture
    now has a player-facing consumer, not just the diagnostics log.
63. Production settings screen gains INLINE editing for the accessibility
    subset (switch-ownership step for that slice): a "Quick Accessibility
    Toggles" row (Units KPH↔MPH, Camera Shake, Compact HUD, UI Animations) whose
    ThemedButtons flip the setting, persist via GameSettingsStore.Save(), and
    re-present - the exact flip/Save/refresh cycle the legacy controls use.
    Production now OWNS editing for these four; the "Classic Settings" button
    still owns the full remainder (one editor path per setting). SettingsView/
    Presenter/Model + BuildSettings extended; ToggleSetting helper in the bridge.
    Thematic with #62: toggling Units here takes live effect in the production
    HUD speed readout. Visual validation PENDING.
62. Production HUD honours the units setting (settings-consumer parity +
    accessibility): SpeedGearModule now converts to mph and shows the MPH/KPH
    unit when useMphUnits is set, matching RaceHud's own conversion (0.621371).
    New snapshot UseMphUnits populated by RaceEventRelay from race settings. A
    concrete example of a settings value having live effect in the production UI.

82. RaceManager DECOMPOSITION begun (matrix "RaceManager decomposed"), via the
    safest behavior-preserving technique - partial-class file split, zero
    dependency risk (same class, all members reachable, moved verbatim):
    RaceManager is now `partial`, and the weather-transition + track-evolution
    subsystem (UpdateWeatherTransition, UpdateTrackEvolution + their 3 state
    fields) moved into RaceManager.Weather.cs. Callers (racing tick) and the
    session-reset of the fields are unchanged and resolve within the class.
    RaceManager.cs: 11667 → 11576 lines. Behaviour identical; more cohesive
    subsystems will peel off the same way.
    Slice 2: the slipstream + dirty-air cluster (UpdateSlipstreamEffects,
    IsSlipstreamEligible, SlipstreamForwardDistance, ComputeSlipstreamStrength,
    SlipstreamStraightSectionStrength + the slipstream/dirty-air constants) moved
    verbatim into RaceManager.Slipstream.cs; DriverShortCode stays in main
    (shared). Racing-tick caller unchanged. RaceManager.cs → 11397 lines.
    Slice 3: the AI difficulty-profile block (the AiDifficultyProfile struct +
    GetAiDifficultyProfile's per-tier Easy/Medium/Hard/Expert profiles) moved to
    RaceManager.AiProfiles.cs. Because it's a PARTIAL, the struct stays nested as
    RaceManager.AiDifficultyProfile, so AiVehicleController's five references are
    completely unchanged (the earlier-noted risk of a struct MOVE does not apply
    to a partial split). No tuned value altered. RaceManager.cs → 11192 lines.
    Slice 4: the shared best-of-two qualifying attempt orchestration
    (SimulateBestOfTwoQualifyingAttempt, SimulateAiQualifyingTime,
    SimulatePlayerQualifyingTime, PlayerQualifyingTyreWeatherPenalty) moved to
    RaceManager.Qualifying.cs verbatim - RNG order and every tuned value
    unchanged; the deeper lap-time model stays in main for a later slice.
    Callers resolve in-class. RaceManager.cs → 11088 lines (579 out over 4 slices).
    Slice 5: the qualifying lap-time model (SimulateQualifyingRunDetailed, the
    circuit reference lap, the field-average driver/car/speed helpers, weather +
    mistake penalties and the model constants) moved verbatim into the same
    RaceManager.Qualifying.cs partial - RNG/values unchanged. RaceManager.cs →
    10666 lines: 1001 lines (8.6%) now peeled off across 5 partials (weather,
    slipstream, AI profiles, qualifying attempt + model). No behaviour change.
    Slice 6: the qualifying phase-time accessors and sector helpers
    (InvalidQualifyingTime, Get/SetQualifyingPhaseTime, SetAiQualifyingPhaseTime,
    SetSimulatedPlayerQualifyingPhaseTime, SetPlayerQualifyingSectors,
    SimulateQualifyingSectors, SetQualifyingPhaseSectors) moved into the
    Qualifying partial too; the QualifyingSimEntry/QualifyingLapBreakdown/
    SectorSnapshot nested types stay in main (still RaceManager.-nested, reachable
    from the partial). RaceManager.cs → 10536 lines (1131 out over 6 slices).
    Slice 7: the race-end debrief logging (LogTelemetryDebrief, LogReplaySummary
    + their opt-in CSV-export prefs) moved to RaceManager.Debrief.cs; the
    FinishRace callers resolve in-class. RaceManager.cs → 10456 lines (1211 out
    over 7 slices, ~10.4%). Full multi-partial review clean: all 6 files declare
    partial, braces balanced, metas present, only PostEngineerMessage overloads.
    Slice 8: the race-engineer + radio subsystem (PostEngineerMessage overloads,
    OpeningEngineerMessage, WeatherStateLabel, overtake/fastest-lap
    notifications, the lap-gap radio chain, gap formatting, DriverShortCode/
    DriverRadioName, auto-pit prompts and per-frame UpdateRaceEngineer) - 866
    lines - moved verbatim to RaceManager.Engineer.cs. Shared helpers
    (DriverShortCode, used by the Slipstream partial) resolve in-class. The
    partial carries the full main using-set so no reference is missed.
    RaceManager.cs → 9590 lines: 2077 out over 8 slices (~17.8%). No behaviour,
    threshold or call-order change.
    Slice 9: the time-trial ghost + best-lap subsystem (TrackPlayerBestLapRecord,
    PromoteGhostRecordingIfBest, RecordGhostSample, UpdateGhostPlayback + the
    PUBLIC TryGetGhostDelta/GhostDeltaText read by the relay/HUD) - 218 lines -
    moved to RaceManager.Ghost.cs. Public API unchanged (still public on
    RaceManager; external callers resolve). RaceManager.cs → 9372 lines: 2295 out
    over 9 slices (~19.7%).
    Slice 10: the pit-lane service subsystem (HandlePitService, BeginPitEntry,
    the rail approach/coordination - blocker finding, lateral rail, UpdatePitRail,
    CompletePitRail - BeginPitStop and missed-entry handling) - 838 lines - moved
    to RaceManager.Pit.cs; the pure duration/queue rules already live in
    F1Game.Race.Rules, this partial owns the live state machine. Timings, tyre
    windows and call order unchanged. RaceManager.cs → 8534 lines: 3133 out over
    10 slices (~26.8%).
    Slice 11: race-control incidents (part 1 of 2) - UpdateRaceControl,
    DetectIncidents, RegisterIncident + pileup grouping, ApplyIncidentSeverity
    escalation, ConsiderRedFlag/BeginRedFlag and TriggerYellowSector - 915 lines -
    moved to RaceManager.RaceControl.cs. Escalation thresholds, RNG rolls and
    call order unchanged; IncidentSeverity stays a main-nested enum (resolves
    in-class). RaceManager.cs → 7619 lines: 4048 out over 11 slices (~34.7%).
    Slice 12: race-control safety-car (part 2 of 2) - VSC/SC deployment, the
    safety-car build/respawn/overtake checks, UpdateSafetyCar pacing, the
    red-flag grid teleport, the green→SC→restart DriveRaceControlStateMachine and
    the player SC-pit offer (public AcceptRaceControlPitOffer /
    PlayerGapToSafetyCarMeters) - 1167 lines - moved to RaceManager.SafetyCar.cs.
    Pacing/timings/RNG/call order unchanged; the part-1 escalation calls into
    these deployers resolve in-class; the public API stays public (relay
    resolves). RaceManager.cs → 6452 lines: 5215 out over 12 slices (~44.7%).
    RaceManager is now spread across 11 focused partials (main + 10).
    Slice 13: race-end results - FinishRace (final classification via the
    engine-free RaceClassifier, mandatory-pit penalty pass, career results with
    the genuine race-control incident/SC/overtake counts), RecordPlayerRaceStats,
    LogAiDiagnostics (the Expert-balance log line) and the optional cinematic
    PodiumPresentationSequence coroutine - 370 lines - moved to
    RaceManager.Results.cs. Classification maths already live in RaceClassifier;
    this partial owns the live end-of-race orchestration and the engine-side
    podium staging. Execution order (RecordPlayerRaceStats → LogAiDiagnostics →
    LogTelemetryDebrief → LogReplaySummary), RNG order and the production-first
    results handoff (ProductionSessionUi.TryShowResults fallback to ui.ShowResults)
    unchanged; FinishRace is still called from the race tick and resolves in-class.
    RaceManager.cs → 6081 lines: 5586 out over 13 slices (~47.9%).
    RaceManager is now spread across 12 focused partials (main + 11).
    Slice 14: qualifying-session flow - CompleteQualifyingRun, the advance/
    eliminated segment feedback, RecordQualifyingPhase, FinishQualifying (career
    apply + player record + results handoff), LogAiQualifyingDiagnostics,
    BuildFinalQualifyingResults, the EnsureQualifyingPhaseComplete /
    ActiveQualifyingEntries / ApplyQualifyingElimination cut logic (counts from
    the engine-free QualifyingProgression) and AppendQualifyingResults - 256 lines
    - moved to RaceManager.QualifyingFlow.cs. The per-lap time model and shared
    accessors stay in RaceManager.Qualifying.cs; the sim nested types
    (QualifyingSimEntry/QualifyingLapBreakdown/SectorSnapshot) stay main-nested and
    resolve in-class. Elimination counts, RNG use, execution order and the
    production-first results handoff unchanged; the tick-loop callers
    (ShouldCompleteQualifyingRun→CompleteQualifyingRun, FinishQualifying) resolve
    in-class. RaceManager.cs → 5824 lines: 5843 out over 14 slices (~50.1%).
    RaceManager is now spread across 13 focused partials (main + 12).
    Slice 15: overtaking legality - the shared IsOvertakingRestrictedForParticipant
    / IsPositionCorrectionAllowed / CanParticipantOvertake authority (global
    SC/VSC/restart ban vs sector-wide local yellow via the engine-free FlagRules,
    plus the order-correction exemption), the snapshot-based
    CheckIllegalOvertakesUnderYellow penalty detection (with its pair-cooldown
    state/consts) and TrackPlayerOvertakesCompleted - 275 lines - moved to
    RaceManager.Overtaking.cs. Snapshot cadence (0.5s), the 25s pair cooldown, the
    +5s penalty and the RNG-free comparison logic unchanged; the public entry
    points stay public so AiVehicleController's calls resolve in-class.
    RaceManager.cs -> 5548 lines: 6119 out over 15 slices (~52.4%).
    RaceManager is now spread across 14 focused partials (main + 13).
    Slice 16: blue flags - IsShownBlueFlag, ClearBlueFlagState, UpdateBlueFlags and
    FindCloseLappingCar (the being-lapped detection + linger/hold bookkeeping, with
    the BlueFlagLingerSeconds const) - 113 lines - moved to
    RaceManager.BlueFlags.cs. The consequence (must-yield) stays in the engine-free
    FlagRules.MustYield and the penalty tariff in PenaltyRules; detection window,
    linger timing and call order unchanged. IsShownBlueFlag stays public so the AI,
    RaceHud and RaceEventRelay callers resolve in-class.
    RaceManager.cs -> 5434 lines: 6233 out over 16 slices (~53.4%).
    RaceManager is now spread across 15 focused partials (main + 14).
    Slice 17: race-control speed enforcement - RaceControlSpeedCapKphFor (the
    per-car allowed cap: pit lane excluded, VSC/SC field-wide, local yellow only
    near the incident), ApplyRaceControlSpeedCaps (the field-wide physical cap on
    every car alike) and ApplyPlayerRaceControlLimiter (the player overspeed
    warning + pace penalty) - 159 lines - moved to RaceManager.SpeedCaps.cs. Caps,
    warning/penalty thresholds and call order unchanged; the overspeed-timer /
    warning state stays main-nested and resolves in-class, and
    RaceControlSpeedCapKphFor stays public so the AI, RaceHud and RaceEventRelay
    callers resolve in-class.
    RaceManager.cs -> 5274 lines: 6393 out over 17 slices (~54.8%).
    RaceManager is now spread across 16 focused partials (main + 15).
    Slice 18: DRS eligibility - UpdateDrsEligibility (the detection-point gap
    check, evaluated once as a car crosses the line then held for the whole zone),
    DrsZoneIndexAt, EvaluateDrsDetectionGap, IsDrsAvailable and DrsStateText -
    ~163 lines across two ranges - moved to RaceManager.Drs.cs. The pure
    eligibility policy stays in the engine-free DrsRules; detection cadence, the
    hold-for-the-zone behaviour and call order unchanged. Public IsDrsAvailable /
    DrsStateText stay public so PlayerVehicleInput, the AI, RaceHud, RaceParticipant
    and RaceEventRelay callers resolve in-class. The LocalHalfWidthAt geometry
    helper (shared by pit/grid/stack code well beyond DRS) deliberately stays in
    the main partial, so this was a two-range slice around it.
    RaceManager.cs -> 5106 lines: 6561 out over 18 slices (~56.2%).
    RaceManager is now spread across 17 focused partials (main + 16).
    Slice 19: live timing - the qualifying timing tower (BuildQualifyingTowerRows),
    pole/delta references, the per-sector capture and records (ReportSectorToState,
    UpdateSectorRecords, CheckCompletedSector, SampleCorneringTelemetry), the player
    sector/live text, the qualifying best-lap captures and phase resets, and the
    display-time / position-estimate helpers - 380 lines - moved to
    RaceManager.LiveTiming.cs. Capture order and the RNG-free timing maths
    unchanged; the sim/tower nested types (QualifyingTowerRow, QualifyingSimEntry,
    SectorSnapshot) stay main-nested and resolve in-class, and the public
    tower/display entry points stay public so the HUD callers resolve in-class.
    RaceManager.cs -> 4725 lines: 6942 out over 19 slices (~59.5%).
    RaceManager is now spread across 18 focused partials (main + 17).
    Slice 20: grid spawn - ResolveGridIndex, SpawnParticipant (car + vehicle/AI/
    player controller setup), ResolveAiStartReactionDelay, FindRoadSpawnPosition,
    LogPlayerSpawnPhysics and HoldGridCars - 313 lines - moved to
    RaceManager.Grid.cs. Spawn order, RNG call order (grid jitter / reaction
    delays) and all tuned values unchanged; callers resolve in-class. The
    qualifying field builders stay in the main partial, the recovery/respawn
    handlers (HandleFallRespawn/HandleStuckEscalation) remain separate.
    RaceManager.cs -> 4411 lines: 7256 out over 20 slices (~62.2%).
    RaceManager is now spread across 19 focused partials (main + 18).
    Slice 21: qualifying-field composition - BuildQualifyingField,
    BuildSimulatedQualifyingField, ResolvePlayerQualifyingDriverData,
    ReplacedDriverIdForPlayerTeam, GetDefensiveAiRoster and
    PrepareAiQualifyingTargetsForPhase - 254 lines - moved to
    RaceManager.QualifyingField.cs. Field composition, RNG call order and tuned
    values unchanged; the sim nested types stay main-nested and resolve in-class.
    RaceManager.cs -> 4156 lines: 7511 out over 21 slices (~64.4%).
    RaceManager is now spread across 20 focused partials (main + 19).
    Slice 22: car recovery - HandleFallRespawn (a car fallen off/under the track)
    and HandleStuckEscalation (the escalating response to repeated failed recovery
    attempts, up to a last-resort force-reposition) - 150 lines - moved to
    RaceManager.Recovery.cs. Thresholds, escalation order and call order unchanged;
    callers resolve in-class.
    RaceManager.cs -> 4005 lines: 7662 out over 22 slices (~65.7%).
    RaceManager is now spread across 21 focused partials (main + 20).
    Slice 23: fuel system - the distance-scaled start-fuel per session type
    (ComputeRaceStartFuelKg/ComputeQualifyingFuelKg/ComputeTimeTrialFuelKg/
    ComputePracticeFuelKg), EstimateFuelPerLapKg, FuelLoadChoiceLapDelta and
    ResolveAiFuelChoice (plus the Min/Max/Reserve fuel consts) - 120 lines - moved
    to RaceManager.Fuel.cs. Fuel constants, RNG call order and tuned values
    unchanged; the public/static entry points stay public so VehicleController,
    RaceParticipant and DataModels callers resolve in-class.
    RaceManager.cs -> 3884 lines: 7783 out over 23 slices (~66.7%).
    RaceManager is now spread across 22 focused partials (main + 21).
    Slice 24: planned-pit schedule accessors - GetPlannedStopCount,
    GetPlannedPitLapForStop, GetPlannedCompoundForStop, NextPlannedPitLapFor,
    NextPlannedPitCompoundFor, ShouldPromptPlannedStop, PitRecommendationReasonClause
    and PlannedPitLapFor - 157 lines - moved to RaceManager.PlannedPit.cs. The
    read-side accessors over the player's strategy-screen pit plan; clamping and
    planned-or-fallback behaviour unchanged; the cached-plan state stays main-nested
    and resolves in-class, and the public entry points stay public.
    RaceManager.cs -> 3726 lines: 7941 out over 24 slices (~68.1%).
    RaceManager is now spread across 23 focused partials (main + 22).
    Slice 25: AI pit strategy - ShouldAiPitUnderSafetyCar (the pit-under-SC
    decision), ShouldAiPitForUndercut and the player-facing HUD counterpart
    RecommendedPitUnderSafetyCar - 123 lines - moved to
    RaceManager.AiPitStrategy.cs. Thresholds and call order unchanged; the pure
    decision maths already live in AiPitStrategyRules, and the public entry points
    stay public so AI/HUD callers resolve in-class.
    RaceManager.cs -> 3602 lines: 8065 out over 25 slices (~69.1%).
    RaceManager is now spread across 24 focused partials (main + 23).
    Slice 26: manual pit request - CanCancelManualPitRequest, MapPitRequestOrigin,
    CancelManualPitRequest and ClearManualPitRequestTracking (the single shared
    validation/cancellation path for a temporary manual pit override, UI button and
    keyboard shortcut alike) - 102 lines - moved to RaceManager.ManualPit.cs. The
    activePitRequestSource/manualPitRequested/manualPitCommitted clear-together
    contract and call order unchanged; the pre-race planned stop stays a separate
    concept and is untouched. Public entry points stay public so PlayerVehicleInput,
    RaceHud, RaceParticipant and RaceEventRelay callers resolve in-class.
    RaceManager.cs -> 3497 lines: 8170 out over 26 slices (~70.0%).
    RaceManager is now spread across 25 focused partials (main + 24).
    Slice 27: driver/team identity - GetDisplayDriverCode (centralized 3-letter
    code resolution: real DriverData.abbreviation first, last-name-token fallback
    only for custom drivers), CodeFromToken, DriverCode, ResolveTeamCarPerformance
    and ResolveDriverTeam (team/car resolution honouring career transfers) - 89
    lines - moved to RaceManager.Identity.cs. Resolution rules unchanged; the public
    GetDisplayDriverCode stays public so RaceHud and the LiveTiming partial resolve
    in-class.
    RaceManager.cs -> 3407 lines: 8260 out over 27 slices (~70.8%).
    RaceManager is now spread across 26 focused partials (main + 25).
    Slice 28: lighting - CreateLighting (the per-track sun/key light, ambient and
    fog mood by track and weather, and the night-track floodlights with the
    vertex-lit fill optimisation) - 174 lines - moved to RaceManager.Lighting.cs.
    Light colours, intensities and render modes unchanged; callers resolve in-class.
    RaceManager.cs -> 3232 lines: 8435 out over 28 slices (~72.3%).
    RaceManager is now spread across 27 focused partials (main + 26).
    Slice 29: qualifying pit return - AnimateQualifyingReturnToPits,
    BeginQualifyingPitReturn and UpdateQualifyingPitReturn (animate the field back
    into the pits between qualifying segments, snapping each car to its service
    pose and posting the player status) - 54 lines - moved to
    RaceManager.QualifyingPitReturn.cs. Poses and call order unchanged; callers
    resolve in-class.
    RaceManager.cs -> 3177 lines: 8490 out over 29 slices (~72.8%).
    RaceManager is now spread across 28 focused partials (main + 27).
    Slice 30: low-speed stack resolution - ResolveLowSpeedStacks,
    IsStackResolveCandidate and NudgeStackedCar (the gentle anti-pile pass that
    eases nearly-stationary overlapping cars apart along track-right, damage-free
    and clamped inside the road surface) - 93 lines - moved to
    RaceManager.StackResolve.cs. Overlap/speed thresholds, nudge magnitude and call
    order unchanged; the tiny SortRunningOrder tick stays in main; callers resolve
    in-class.
    RaceManager.cs -> 3083 lines: 8584 out over 30 slices (~73.6%).
    RaceManager is now spread across 29 focused partials (main + 28).
    Slice 31: tyre-compound selection - StartingTyreForParticipant (player
    selection or time-trial soft; AI weather-appropriate wets/inters or a random
    dry pick) and NextPitCompound (weather override, then the short-stint
    faster-compound reach, then the Soft->Medium->Hard ladder) - 65 lines - moved
    to RaceManager.TyreStrategy.cs. RNG call order and the aggression/stint-length
    heuristics unchanged; callers resolve in-class.
    RaceManager.cs -> 3017 lines: 8650 out over 31 slices (~74.1%).
    RaceManager is now spread across 30 focused partials (main + 29).
    Slice 32: finish handling + penalties - HandleFinish (a car crossing the line
    for the last time: mandatory-pit penalty, State.OnParticipantFinished, the
    player podium radio and finish camera flourish), FinishEngineerMessage,
    ApplyMandatoryPitPenalty (gated by the unit-tested PenaltyRules) and the shared
    AddPenalty utility (seconds/reason, the PenaltyIssuedEvent and the player-only
    timeline entry) - 95 lines - moved to RaceManager.FinishHandling.cs. Penalty
    values and call order unchanged; AddPenalty's cross-partial callers (Overtaking,
    BlueFlags, SpeedCaps, SafetyCar, main) resolve in-class.
    RaceManager.cs -> 2921 lines: 8746 out over 32 slices (~75.0%).
    RaceManager is now spread across 31 focused partials (main + 30).
    Slice 33: position queries - GetPosition (running-order position lookup) and
    the nearest-car-ahead / nearest-car-behind track-distance searches (FindCarAhead
    / FindCarBehind) used by the AI, HUD and radio gap logic - 89 lines - moved to
    RaceManager.PositionQueries.cs. Ordering and distance maths unchanged; the
    public entry points stay public so external callers resolve in-class.
    RaceManager.cs -> 2831 lines: 8836 out over 33 slices (~75.7%).
    RaceManager is now spread across 32 focused partials (main + 31).
    Slice 34: race-start launch - ReportJumpStartIntent and RecordPlayerLaunchInput
    (track the player's launch input during the start countdown and apply the
    false-start penalty when a car moves before lights-out) - 45 lines - moved to
    RaceManager.JumpStart.cs. Penalty tariff and call order unchanged; the public
    entry points stay public so input callers resolve in-class.
    RaceManager.cs -> 2785 lines: 8882 out over 34 slices (~76.1%).
    RaceManager is now spread across 33 focused partials (main + 32).
    Slice 35: player pit-tyre selector - OpenPlayerPitTyreSelector and
    SelectPlayerPitTyre (open the in-race compound picker for the player's next stop
    and apply the chosen compound) - 69 lines - moved to
    RaceManager.PitTyreSelector.cs. Gating and selection behaviour unchanged; the
    local-yellow speed-cap consts stay in main; the public entry points stay public
    so UI/input callers resolve in-class.
    RaceManager.cs -> 2715 lines: 8952 out over 35 slices (~76.7%).
    RaceManager is now spread across 34 focused partials (main + 33).
    Slice 36: pit-entry assist - ShouldAssistPlayerPitEntry (the opt-in-by-plan gate)
    and BuildPitEntryAssistCommand (the steering that guides a PreRacePlan pit
    request onto the ramp inside the approach window until BeginPitEntry takes over)
    - 118 lines - moved to RaceManager.PitEntryAssist.cs. Gate and steering
    behaviour unchanged; a manual or race-control-offer request never matches, so
    manual entry is untouched. The shared LocalHalfWidthAt helper stays in main; the
    public entry point stays public so input callers resolve in-class.
    RaceManager.cs -> 2596 lines: 9071 out over 36 slices (~77.7%).
    RaceManager is now spread across 35 focused partials (main + 34).
    Slice 37: AI ERS deployment - ShouldAiUseErs (decides when the AI deploys ERS
    from corner severity, the per-tier decision-quality profile and situational
    context) - 77 lines - moved to RaceManager.Ers.cs. Thresholds and RNG call
    order unchanged; the AiDifficultyProfile struct stays in the AiProfiles partial
    (same class), and the public entry point stays public so AI callers resolve
    in-class.
    RaceManager.cs -> 2518 lines: 9149 out over 37 slices (~78.4%).
    RaceManager is now spread across 36 focused partials (main + 35).
    Slice 38: gap/interval text - GapAheadText, GetIntervalToAheadSeconds,
    GetGapBetweenSeconds, GapToLeaderText, IntervalAheadText and GapBehindText (the
    radio/HUD timing strings) - ~117 lines across two ranges - moved to
    RaceManager.GapText.cs. Formatting and maths unchanged; the rival/order/retire/
    fuel helpers in between stay in main. The public entry points stay public so the
    HUD/radio callers resolve in-class, and GetIntervalToAheadSeconds still resolves
    for the DRS/Engineer/AiPitStrategy/Ers partials and RaceEventRelay.
    RaceManager.cs -> 2399 lines: 9268 out over 38 slices (~79.4%).
    RaceManager is now spread across 37 focused partials (main + 36).
    Slice 39: grid build - SpawnRaceGrid (builds the whole starting grid: player +
    AI field, teams/cars, starting tyres and grid slots) and its ResolvePlayerGridFallback
    helper - 95 lines - consolidated into the existing RaceManager.Grid.cs partial
    (which already owns ResolveGridIndex/SpawnParticipant/etc.), rather than a new
    file, to keep all grid-spawn concerns in one place. Spawn order, RNG call order
    and tuned values unchanged; callers resolve in-class. No new partial - still 37.
    RaceManager.cs -> 2303 lines: 9364 out over 39 slices (~80.3%).
    RaceManager is now spread across 37 focused partials (main + 36).
    Slice 40: track limits - HandleTrackLimits (detects repeated off-track
    excursions, logs each sector event, escalates the warning count and applies the
    deletion/penalty tariff, surfacing the "n/3" status to the HUD) - 84 lines -
    moved to RaceManager.TrackLimits.cs. Thresholds and call order unchanged;
    callers resolve in-class.
    RaceManager.cs -> 2218 lines: 9449 out over 40 slices (~81.0%).
    RaceManager is now spread across 38 focused partials (main + 37).
    Slice 41: pit-status display - PitStatusText (the player's live pit-status line:
    approach/queue/service/exit phrasing) and PitStopProgress01 (the 0-1
    service-progress value for the HUD gauge) - 102 lines - moved to
    RaceManager.PitStatus.cs. Phrasing and progress maths unchanged; the local-yellow
    speed-cap consts stay in main; the public entry points stay public so HUD callers
    resolve in-class.
    RaceManager.cs -> 2115 lines: 9552 out over 41 slices (~81.9%).
    RaceManager is now spread across 39 focused partials (main + 38).
    Slice 42: rival/teammate hints - RivalTraitHint (a short characterising hint
    about a rival's driving traits for radio/engineer colour) and FindTeammate (the
    teammate lookup) - 46 lines - moved to RaceManager.RivalHints.cs. Selection
    unchanged; the public FindTeammate stays public so external callers resolve
    in-class.
    RaceManager.cs -> 2068 lines: 9599 out over 42 slices (~82.3%).
    RaceManager is now spread across 40 focused partials (main + 39).
    Slice 43: AI pit-lap strategy - RecommendedPitLap, ShouldAiPitByStrategyLap and
    the StableUnitInterval per-driver-stable jitter helper - 70 lines - consolidated
    into the existing RaceManager.AiPitStrategy.cs partial (which already owns the
    pit-under-SC/undercut decisions), keeping AI pit-strategy in one place. The
    window maths already live in AiPitStrategyRules; RNG-free jitter, thresholds and
    call order unchanged; callers resolve in-class. No new partial - still 40.
    RaceManager.cs -> 1997 lines: 9670 out over 43 slices (~82.9%).
    RaceManager is now spread across 40 focused partials (main + 39).
    Slice 44: retirement + fuel state - RetireParticipant (reason, event publish,
    timeline, HUD) and UpdateFuelState (the per-frame tank drain that triggers a
    fuel-starvation retirement once the grace timer elapses) - 72 lines - moved to
    RaceManager.RetireFuel.cs. Drain/grace timing and call order unchanged; the
    public RetireParticipant stays public so AiVehicleController and the RaceControl
    partial resolve in-class.
    RaceManager.cs -> 1924 lines: 9743 out over 44 slices (~83.5%).
    RaceManager is now spread across 41 focused partials (main + 40).
    Slice 45: debrief text - TelemetryDebriefLine (the telemetry driving summary),
    ReplayHighlightLine (the replay race-events summary) and RaceDebriefLine (the
    combined results-screen line) - 52 lines - moved to RaceManager.DebriefText.cs.
    Formatting unchanged; they read BuildTelemetryDebrief/BuildReplayTimeline on the
    main partial, and the public entry points stay public so the Results partial
    resolves in-class. The telemetryCapture field and its expression-bodied
    accessors stay in main.
    RaceManager.cs -> 1872 lines: 9795 out over 45 slices (~84.0%).
    RaceManager is now spread across 42 focused partials (main + 41).
    Slice 46: HUD toasts - the HudToast struct, the toast queue + cap, the
    UI-agnostic ToastColor* colour-kind consts, QueueHudToast (which also publishes
    a HudToastEvent for the production notification feed) and TryDequeueHudToast -
    46 lines - moved to RaceManager.HudToast.cs. Queue cap and tone mapping
    unchanged; the HudToast struct and the public colour consts stay nested
    (RaceManager.ToastColor*) so RaceHud and the Engineer/TrackLimits partials
    resolve in-class.
    RaceManager.cs -> 1826 lines: 9841 out over 46 slices (~84.3%).
    RaceManager is now spread across 43 focused partials (main + 42).
    Slice 47: race-control history - LogRaceControlHistory (records each event into
    the rolling history, emits a replay-timeline flag marker and prunes to the cap)
    and CountRaceControlHistoryLabel - 25 lines - moved to
    RaceManager.RaceControlHistory.cs. Cap and marker hook unchanged; the history
    list and RaceControlHistoryEntry nested type stay in main and resolve in-class.
    RaceManager.cs -> 1800 lines: 9867 out over 47 slices (~84.6%).
    RaceManager is now spread across 44 focused partials (main + 43).
    Slice 48: engineer message accessors - the ActiveEngineerMessageCount property
    and GetActiveEngineerMessageText / GetActiveEngineerMessagePriority /
    GetActiveEngineerMessageFade (the HUD read-side over the stacked engineer
    messages, incl. the 0-1 slide/fade progress) - 28 lines - consolidated into the
    existing RaceManager.Engineer.cs partial (which already owns PostEngineerMessage
    and the radio logic). Fade maths unchanged; the anim-duration consts,
    activeEngineerMessages list and EngineerMessageEntry type stay in main and
    resolve in-class. No new partial - still 44.
    RaceManager.cs -> 1771 lines: 9896 out over 48 slices (~84.8%).
    RaceManager is now spread across 44 focused partials (main + 43).
    Slice 49: session start - the public session entry points StartRace,
    StartTimeTrial and StartSession (bootstrap a session from the data repository
    and settings: build the field, grid, lighting, weather and player car, then hand
    off to the live loop) plus CycleToNextTrack - 175 lines - moved to
    RaceManager.SessionStart.cs. Setup order, RNG call order and tuned values
    unchanged; the sector-colour / session-cap consts stay in main, and the public
    entry points stay public so GameBootstrap resolves in-class.
    RaceManager.cs -> 1595 lines: 10072 out over 49 slices (~86.3%).
    RaceManager is now spread across 45 focused partials (main + 44).
    Slice 50: quick-sim qualifying - SimulateQualifyingWeekend (runs a whole
    qualifying weekend in one pass without live driving), BuildSimQualifyingExplanation
    (the human-readable result explanation), QualifyingCutoffTime and SignedSeconds -
    191 lines - moved to RaceManager.QualifyingSim.cs. RNG call order and the sim
    model unchanged; the sector-colour / lap-cap consts and the sim nested types
    stay in main; the public entry point stays public so GameBootstrap resolves
    in-class.
    RaceManager.cs -> 1403 lines: 10264 out over 50 slices (~88.0%).
    RaceManager is now spread across 46 focused partials (main + 45).
    Slice 51: session control - TogglePause, Resume, RestartRace and CleanupRaceWorld
    (the pause/resume, restart-the-current-race and race-world teardown that
    destroys the spawned field/lighting and resets audio) - 60 lines - moved to
    RaceManager.SessionControl.cs. Teardown order unchanged; the public entry points
    stay public so the pause menu and GameBootstrap resolve in-class.
    RaceManager.cs -> 1342 lines: 10325 out over 51 slices (~88.5%).
    RaceManager is now spread across 47 focused partials (main + 46).
    Slice 52: practice session - EvaluatePracticeSession (scores a just-driven
    Practice session against the selected program from the real telemetry captured
    during it, before CleanupRaceWorld while the car is still live) and
    BestAiLapTimeThisSession - 90 lines - moved to RaceManager.Practice.cs. Scoring
    criteria unchanged; the PracticeSessionResult nested type stays in main, and the
    public entry point stays public so the practice UI resolves in-class.
    RaceManager.cs -> 1251 lines: 10416 out over 52 slices (~89.3%).
    RaceManager is now spread across 48 focused partials (main + 47).
    Slice 53: engineer resets - ResetEngineerState (clears the active radio-message
    stack and cooldowns) and TickEngineerTimers (ages/expires the stacked messages
    and reaction timers) - 72 lines - consolidated into the existing
    RaceManager.Engineer.cs partial. Timing and expiry order unchanged; the message
    stack/fields stay in main and resolve in-class. No new partial - still 48.
    RaceManager.cs -> 1176 lines: 10491 out over 53 slices (~89.9%).
    RaceManager is now spread across 48 focused partials (main + 47).
    Slice 54: player safe-pose recovery - ResetPlayerToSafePose (stuck recovery:
    snap the player back to the last safe on-track pose, costing five seconds in
    competitive sessions so it can't be exploited) - 58 lines - consolidated into
    the existing RaceManager.Recovery.cs partial (which already owns HandleFallRespawn
    / HandleStuckEscalation). Penalty and snap behaviour unchanged; the local-yellow
    consts stay in main; the public entry point stays public so PlayerVehicleInput
    resolves in-class. No new partial - still 48.
    RaceManager.cs -> 1117 lines: 10550 out over 54 slices (~90.4%).
    RaceManager is now spread across 48 focused partials (main + 47).
    Slice 55: race-control state reset - ResetRaceControlState (resets the
    green/VSC/SC/red state machine, target speeds, timers and per-session flags at
    session start) - 84 lines - consolidated into the existing
    RaceManager.RaceControl.cs partial (which already owns the incidents/escalation
    logic). Reset values and order unchanged; the local-yellow consts and Weather
    partial note-comment stay in main and resolve in-class. No new partial - still 48.
    RaceManager.cs -> 1032 lines: 10635 out over 55 slices (~91.2%).
    RaceManager is now spread across 48 focused partials (main + 47).
    Slice 56: running order - GetRunningOrderSnapshot (ticks classification and
    returns a snapshot of the current order) and ReportAiOvertakeCompleted (records
    a completed AI overtake for the post-race diagnostics) plus the tiny
    SortRunningOrder tick - 22 lines - moved to RaceManager.RunningOrder.cs.
    Ordering unchanged; the public entry points stay public so the AI and consumers
    resolve in-class (RaceStateManager has its own private SortRunningOrder, so its
    calls are unaffected).
    RaceManager.cs -> 1009 lines: 10658 out over 56 slices (~91.4%).
    RaceManager is now spread across 49 focused partials (main + 48).
    Slice 57: spatial helpers - IsNearLocalYellowIncident (whether a car is within
    the flagged local-yellow sector / incident-proximity window, with its speed-cap
    consts) and LocalHalfWidthAt (the shared local track half-width lookup consumed
    across the pit/grid/geometry code) - 37 lines - moved to
    RaceManager.SpatialHelpers.cs. Sector test and geometry unchanged; the public
    entry points stay public so callers (the Pit/RaceControl/TrackLimits/Recovery/
    StackResolve/PitEntryAssist partials, FlagForParticipant, SpeedCaps) resolve
    in-class.
    RaceManager.cs -> 971 lines: 10696 out over 57 slices (~91.7%).
    RaceManager is now spread across 50 focused partials (main + 49).
    Slice 58: qualifying display + new-weekend prep - QualifyingLapStatusText (the
    Qn push-lap status line) consolidated into RaceManager.LiveTiming.cs, and
    PrepareNewQualifyingWeekend (tears down the world and resets all qualifying
    phase/entry/transition/sector state for a fresh weekend) consolidated into
    RaceManager.QualifyingFlow.cs - 44 lines total. Behaviour and reset order
    unchanged; the RaceElapsed/RaceLaps properties stay in main, and the public
    PrepareNewQualifyingWeekend stays public so GameBootstrap resolves in-class. No
    new partials - still 50.
    RaceManager.cs -> 925 lines: 10742 out over 58 slices (~92.1%).
    RaceManager is now spread across 50 focused partials (main + 49).

    RaceManager partial-class decomposition: PHASE COMPLETE.
    The 11667-line monolith is now a 926-line core (RaceManager.cs) plus 49 focused
    behaviour-preserving partials, all in one class (partial class RaceManager,
    namespace LocalFormulaRacing). Full consolidated review passes: every file's
    braces balance and declares `partial`; 417 Assets metas with 0 duplicate GUIDs;
    no unexpected cross-partial duplicate definitions (only the two legitimate
    PostEngineerMessage overloads, both in Engineer.cs); `git diff --check` clean;
    main untouched at 286e94f. Every slice moved code verbatim - identical members,
    execution order, RNG call order, tuned values and public APIs; all callers
    (in-class partials and external classes: AiVehicleController, RaceHud,
    RaceEventRelay, PlayerVehicleInput, GameBootstrap, RaceParticipant, DataModels,
    VehicleController) resolve in-class because the type is unchanged.
    What remains in RaceManager.cs is the irreducible MonoBehaviour core and is NOT
    a candidate for further partial slicing: the field/property/state declarations
    that form the public API surface (race-control counts, flag state, capture
    fields), the two small state-derived accessors woven into them
    (FlagForParticipant, ReplayCarIndex) and the expression-bodied capture accessors,
    the Update() per-frame orchestration tick that drives every extracted subsystem,
    and the nested data types (RaceControlHistoryEntry, EngineerMessageEntry,
    QualifyingLapBreakdown, QualifyingSimEntry, SectorSnapshot, PracticeSessionResult,
    GhostSample). Extracting these individually would fragment the state surface and
    the central tick for no cohesion gain, so they stay together.
    Next recorded phase (Phase E, distinct and higher-risk): behaviour-preserving
    SERVICE extraction/delegation from these partials where dependencies verify
    statically - build the replacement service in coherent slices, keep the live
    implementation behind a default-off switch, and record Unity validation as
    pending. The pure cores that such services would wrap are already extracted
    (RaceClassifier, FlagRules, PenaltyRules, AiPitStrategyRules, DrsRules,
    QualifyingProgression, WeatherRules, PhysicsModels), so the remaining work is the
    stateful-glue delegation, which must be sliced carefully (blind extraction of the
    orchestration state risks regressions no static check can catch without Unity).

    Phase E progress (behaviour-preserving service extraction, compatibility-first):
    E1. Fuel start-load maths -> engine-free FuelStrategy. The distance/difficulty
        per-lap burn band and the start-fuel target/choice-shift/clamp were pure
        numeric formulas inline in RaceManager.Fuel.cs (EstimateFuelPerLapKg /
        ComputeRaceStartFuelKg). Extracted verbatim into FuelStrategy.PerLapBurnKg
        (trackLengthMeters, difficultyFactor01) and FuelStrategy.StartFuelKg
        (perLapKg, raceLaps, reserveKg, loadDeltaLaps, minKg, maxKg), taking
        primitives only (F1Game.Race is noEngineReferences, so the enums/TrackRuntime
        stay caller-side). Engine-free Clamp01/Clamp/Lerp/InverseLerp helpers mirror
        Mathf byte-for-byte (Mathf.Lerp = a+(b-a)*Clamp01(t); Mathf.InverseLerp
        clamps). RaceManager stays the owner: the partial still reads the live track
        length, maps the difficulty tier and FuelLoadChoice to primitives, owns the
        tank-window consts and the ResolveAiFuelChoice RNG, and now delegates the
        numeric core - algebraically identical, one live path (pure calc, no state
        mutation). Callers unchanged (RaceManager.Grid delegates as before).
        FuelStrategyTests extended (PerLapBurn band/fallback/clamp/monotonic;
        StartFuel target/shift/clamp/lap-floor). Unity/runtime validation PENDING.
    E2. Dry tyre-compound decision -> engine-free TyreStrategyRules. The short-stint
        faster-compound reach and the Soft->Medium->Hard ladder (NextPitCompound's
        dry path) and the 0-2 roll->compound start pick
        (StartingTyreForParticipant's dry branch) were pure decisions inline in
        RaceManager.TyreStrategy.cs. Extracted verbatim into
        TyreStrategyRules.NextDryCompound(lapsRemainingAfterStop, aggression,
        currentCompound) and DryStartCompoundFromRoll(roll), using int compound
        codes that match the live TyreCompound enum ordering (Soft 0/Medium 1/Hard
        2), so the caller casts at the boundary. RaceManager stays the owner: the
        partial keeps the wet/inter weather override, the missing-tyre null guard,
        the live state reads (laps remaining via lapTracker/RaceLaps, current
        compound, driver aggression) and the Random.Range roll (RNG call order
        unchanged), and delegates only the dry pick - identical branching, one live
        path (pure decision, no state mutation). In-class callers (Grid/PlannedPit/
        AiPitStrategy/PitTyreSelector) unchanged. TyreStrategyRulesTests added
        (short-stint reach boundaries, ladder incl. inter/wet fallthrough, roll
        pick). Unity/runtime validation PENDING.
    E3. Slipstream strength curves -> engine-free AeroModel (F1Game.Race.Physics).
        The tow-strength product (peak-at-18m/fade-to-150m distance curve x the
        full-width/fade lateral curve, clamped) in ComputeSlipstreamStrength and the
        straight-section fade (full below 9deg heading change, none above 22deg) in
        SlipstreamStraightSectionStrength were pure numeric formulas inline in
        RaceManager.Slipstream.cs. Extracted verbatim into
        AeroModel.SlipstreamTowStrength(aheadDistance, lateralDiff, maxDistance,
        peakDistance, fullLateralWidth, maxLateralWidth) and
        AeroModel.SlipstreamStraightFactor(headingAngleDeg, fullAtDeg, noneAtDeg),
        with engine-free Clamp01/InverseLerp mirroring Mathf. RaceManager stays the
        owner: the partial keeps the per-frame orchestration, the in-range distance/
        lateral cutoffs (so the early-outs still skip the straight-section track
        sampling exactly as before), every TrackProgress/Track.SampleAtDistance read,
        the tuned distances/widths, and the SetSlipstream/SetDirtyAirGap calls, and
        delegates only the two curves - byte-identical result, one live path (pure
        calc, no state mutation). UpdateSlipstreamEffects (called from the Update
        tick) unchanged. PhysicsModelsTests extended (tow peak/fade/lateral/monotonic,
        straight-factor fade + clamps). Unity/runtime validation PENDING.
    E4. Qualifying model (RNG-free pieces) -> engine-free QualifyingModel
        (F1Game.Race.Rules). TrackAverageSpeedFactor (circuit-character speed
        scaler), WeatherQualifyingPenalty (per-condition baseline + wetSkill spread),
        the mistake-probability build-up inside QualifyingMistakePenalty, and
        InvalidQualifyingTime were pure/RNG-free formulas inline in
        RaceManager.Qualifying.cs. Extracted verbatim into
        QualifyingModel.TrackSpeedFactor(trackId, styleName, roadHalfWidth),
        WeatherPenalty(weatherCode, wetSkill), MistakeChance(consistency,
        weatherCode, phase) and InvalidTime(phase), with weather int codes matching
        the live WeatherState ordering (Clear 0/Cloudy 1/LightRain 2/HeavyRain 3) and
        an engine-free Lerp mirroring Mathf. CRUCIAL: every Random roll stays in
        RaceManager - the mistake trigger (Random.value > chance), the type pick
        (Random.Range) and the magnitude (Random.Range x awareness spread, and the
        rare major-mistake tail) are untouched, so RNG call order is identical; only
        the pure chance build-up is delegated. Null-track baseline and the live
        Track.weather / driver-stat reads stay caller-side. Feel-sensitive numbers
        unchanged (relocation, not a retune). One live path (pure calc). In-class
        callers (QualifyingFlow, main) unchanged. QualifyingModelTests added (circuit
        table incl. case-sensitivity + null, weather baseline/spread, mistake-chance
        rain/consistency/Q3, invalid-time clamp). Unity/runtime validation PENDING.
    E5. Qualifying performance maths -> QualifyingModel (extends E4). Three more
        pure/RNG-free formulas from RaceManager.Qualifying.cs: NormalizeTopSpeedToRating
        (km/h -> the shared 45-125 rating scale), CircuitReferenceLapTime's formula
        core (track length / expected field speed, floored at 45 s) and
        CarPerformanceWeights (the per-circuit stat-weighting buckets, each summing
        to 1.0). Extracted verbatim into QualifyingModel.TopSpeedRating(topSpeedKph),
        ReferenceLapTime(neutralTopSpeedKph, styleFactor, trackLengthMeters) and
        CarPerformanceWeights(trackId, styleName, roadHalfWidth, out 7 weights); a
        null track maps to empty descriptors + roadHalfWidth 999 so the tight-circuit
        test is false exactly as the old "track != null && ..." short-circuit.
        RaceManager keeps the live field-average/track reads and delegates the
        formulas - byte-identical, one live path (pure calc, no RNG). All callers are
        in-class (Qualifying partial). QualifyingModelTests extended (top-speed
        scale/clamp, reference-lap length/floor/monotonic, weight buckets + sum-to-1).
        Unity/runtime validation PENDING.
    E6. Qualifying lap effect formulas -> QualifyingModel (extends E4/E5). The two
        core "stat gap -> time delta" formulas from SimulateQualifyingRunDetailed -
        driverEffect (field-centered, coefficient-weighted qualifying/pace/confidence)
        and carEffect (clamped composite-rating gap) - were pure inline formulas.
        Extracted verbatim into QualifyingModel.DriverEffect(...) and CarEffect(...).
        The tuned coefficients (DriverQualifying/Pace/Confidence 0.012/0.003/0.001,
        CarEffect 0.08/point cap 2.0s) and every live driver/car/field read stay
        owned in RaceManager; the interleaved RNG (tyrePrep/mistake/variance) is
        untouched and the final lap sum stays in the caller - byte-identical, one
        live path. QualifyingModelTests extended (driver-effect field-centering +
        secondary terms, car-effect gap + cap clamp). These are the qualifying
        competitive-balance core, now pinned. Unity/runtime validation PENDING.
    E7. Lapped-gap maths -> engine-free GapMath (dedup). GapToLeaderText and
        IntervalAheadText carried an identical inline block (deltaMeters >=
        trackLength*0.92 -> "+N L", laps = Max(1, RoundToInt(delta/Max(1,length)))).
        Extracted verbatim into GapMath.IsLapDownGap(deltaMeters, trackLength) and
        LapsDown(deltaMeters, trackLength); RoundToInt uses Math.Round ToEven so it
        matches UnityEngine.Mathf.RoundToInt's banker's rounding exactly. Both call
        sites now delegate; the null-track guard, live distance/speed reads and the
        string formatting stay in the caller - byte-identical, one live path, one
        copy of the rule instead of two. GapMathTests added (threshold boundary,
        round/floor, banker's-rounding halves). Unity/runtime validation PENDING.
    E8. Player tyre/weather qualifying penalty -> QualifyingModel. The weather x
        compound penalty table in RaceManager.PlayerQualifyingTyreWeatherPenalty
        (correct wet/inter a small bonus, wrong tyre a big penalty, dry slick ladder)
        was a pure lookup. Extracted verbatim into
        QualifyingModel.TyreWeatherPenalty(weatherCode, compoundCode) with a new
        Compound code table (Soft 0..Wet 4) matching the live TyreCompound ordering.
        The partial reads live Track.weather and delegates - byte-identical, one live
        path. QualifyingModelTests extended (heavy/light rain + dry rows, incl. the
        cloudy->dry-else path). Unity/runtime validation PENDING.
    E9. Safety-car convoy control-law -> engine-free SafetyCarPacing. The autopilot
        that paces cars behind the safety car (and holds them under a red flag) had
        its numeric control-law inline in RaceManager.SafetyCar.cs: gap-per-car (pace
        scaled), the proportional speed correction toward each car's queue slot (with
        the -45/+25 asymmetric cap), the convoy speed target, the brake/throttle
        mapping (with the -3 km/h deadband), the steering lookahead, and the red-flag
        hold braking. Extracted verbatim into SafetyCarPacing.GapPerCarMeters /
        SpeedAdjustKph / TargetSpeedKph / BrakeThrottle(out brake, out throttle) /
        LookAheadMeters / RedFlagHoldBrake, with engine-free Clamp/Clamp01/Lerp
        mirroring Mathf. RaceManager keeps every live read (queue index, slot distance
        via Track.WrapDistance, own speed/position, the Track.SampleAtDistance
        steering) and the VehicleCommand mutation, and delegates only the formulas -
        byte-identical, one live path (this is the AI's live SC behaviour, feel
        preserved, relocation not retune). SafetyCarPacingTests added (gap/speed/
        target caps, pedal deadband + clamps, lookahead, red-flag brake scaling).
        Unity/runtime validation PENDING (SC convoy feel needs an in-editor run).
    E10. AI start-reaction delay -> engine-free StartProcedureRules. The launch
        reaction in RaceManager.ResolveAiStartReactionDelay (Grid) had its pure maths
        inline: the awareness+consistency skill blend, the 0.7x-0.35x base-delay
        scale on the tier reaction time, and the 0.14-0.03 s variance band.
        Extracted verbatim into StartProcedureRules.AiReactionSkillBlend(awareness,
        consistency), AiReactionBaseDelaySeconds(reactionTimeSeconds, skillBlend01)
        and AiReactionVarianceSeconds(skillBlend01) - the natural home (this class
        already owns light timing and jump/false-start judgement). The null-driver
        default and the Random.Range that samples the variance stay in RaceManager -
        byte-identical, one live path. StartProcedureRulesTests extended (skill-blend
        average/clamp, base-delay scale + monotonic, variance band). Unity/runtime
        validation PENDING (AI launch feel needs an in-editor run).
    E11. AI ERS decision quality -> engine-free AiErsRules. The two awareness-
        modulated probabilities gating the RNG in RaceManager.ShouldAiUseErs - the
        racecraft-call quality (base tier quality x Lerp(0.8,1.08, awareness/100),
        clamped) and the push-lap deploy chance (half base quality) - were pure
        formulas inline. Extracted verbatim into AiErsRules.RacecraftDeployQuality
        (baseQuality, awareness) and PushLapDeployChance(baseQuality). The Expert
        deterministic bypass, every live read (corner severity, battery, flags,
        gaps) and the Random.value rolls stay in RaceManager - byte-identical, one
        live path. AiErsRulesTests added (quality awareness-scale/clamp/monotonic,
        push-lap half). Unity/runtime validation PENDING.

    Phase E status: the pure-calculation extraction pass across RaceManager is
    substantially complete (E1-E11). The rules/model layer (F1Game.Race.Rules +
    PhysicsModels) is now 21 engine-free, unit-tested classes covering fuel,
    tyres, aero/slipstream, the full qualifying lap-time model, safety-car pacing,
    AI start-reaction and ERS decision quality, alongside the pre-existing
    Championship/Qualifying/Classifier/Penalty/Pit/AiPitStrategy/Session/Flag/
    Start/Reliability/Drs/Weather/CarDevelopment rules. All extractions preserved
    values, call order, RNG order and public APIs; RaceManager stays the
    orchestration owner and delegates the maths. What remains inline in RaceManager
    is NOT a safe static extraction: thin one-liner thresholds (over-fragmenting to
    move), state-heavy decision trees with interleaved RNG (fragile to split), and
    logic bound to engine-side Track/State/Physics queries (the pit-rail geometry,
    incident detection) whose behaviour cannot be verified without Unity. Those are
    recorded as needing an in-editor run; the ownership stays with RaceManager and
    the live path is authoritative.

Exact next task: continue live integrations via compatibility paths + feature
switches. Replay + telemetry are now captured live AND each has a pure in-game
consumer (BuildReplayTimeline / BuildTelemetryDebrief); the remaining surface
work (a replay playback/scrub UI, and placing the debrief summary + highlight
list + CSV-export button on the results screen) needs Unity scenes/UI and
in-editor validation, so it waits for a compiler. Phase E RaceManager service extraction is the recorded larger item
but the results-build loop and race-control state are tightly-coupled
orchestration whose pure cores are already extracted (RaceClassifier,
FlagRules...) - blind extraction of the stateful glue risks regressions the
engineering rule warns against, so it waits for a compiler. Safe next
candidates: (a) remaining production session-end screens (time-trial PB/ghost
result) in the proven TryShow/legacy-fallback pattern; (b) more additive HUD
session-specific parity. In-editor VISUAL validation still required for every
migrated screen before production UI can be the default. Multiplayer (Phase N)
DEFERRED, out of scope.

## Environment reality
- Unity cannot run here (no editor, no GPU, no package resolution). Everything
  below is **static-only / unverified**: not compiled, not tested, not run.
- No claim of compilation, runtime, visual, test, or performance success is made.

## Completion matrix (honest — unchecked = NOT the live path)
- [x] TMP import cannot activate incomplete UI
- [ ] Production UI is default (intentionally opt-in until full parity)
- [ ] Full frontend migrated / [ ] career UI / [ ] race-weekend UI / [ ] settings+a11y UI
- [ ] Production HUD parity / [ ] legacy HUD removed
- [ ] Authored tracks default / [ ] every track migrated / [ ] procedural track removed
- [ ] Production car path authoritative (placeholder until authored car art)
- [ ] Material/rendering authoritative / [ ] VFX wired throughout / [ ] audio bank authoritative
- [ ] RaceManager/TrackManager/RuntimeUi/RaceHud/CareerManager/VehicleVisuals decomposed
- [ ] VehicleController modular physics / [ ] AI modular / [ ] pit lane / [ ] full rules authoritative
- [ ] Full session formats / weather / damage integrated
- [ ] Career systems / R&D / customization complete
- [ ] Replay / spectator / broadcast / photo / telemetry live
- [ ] Input+wheel / accessibility / localization / loading / diagnostics / release complete
- [ ] Editor/content tools complete
- [ ] Multiplayer complete
- [x] Static checks clean  [x] Documentation honest  [x] Main fast-forwarded and pushed  [x] Worktree clean

The unchecked items are genuine multi-week bodies of work that cannot be
truthfully completed or made the sole live path in an environment with no
compiler and no Unity. They are NOT claimed complete. See Docs/KNOWN_ISSUES.md.

## Completion run (final directive)
- Branch: `claude/unity-production-completion` (descends from `origin/main` a57712d).
- Critical UI transition bug FIXED: ProductionUiReadiness (no TMP auto-activation),
  UiSessionCoordinator (single-flight state machine), atomic strategy→race
  transition, single live HUD (production HudRoot or legacy, never both),
  NavigationLocked, clean pause/results/menu-return.
- Live integrations added: pooled VFX wired to vehicle events (VehicleVfxDriver);
  ITrackQuery adapters (legacy + authored) with a live TrackQueryProvider.Select
  call at race start (reference circuit runs the authored path).
- Still built-not-live (honest): full HUD-module parity + legacy RaceHud removal;
  full frontend/career migration; physics/replay/rulebook wiring; multiplayer.

## Git (prior)
- Branch: `claude/unity-production-migration-so7zih` (pushed; merged to main via PR #11).
- **`origin/main` has diverged** — it advanced to `7db7626`, the merge commit of
  PR #10 (which brought this branch's *earlier* Phase 0/1 work up to `7fa2f42`
  into main). That merge commit is not in this branch's history, so an
  **ff-only merge to main is impossible**, and the directive forbids
  rebase/reset/force/non-ff-merge to resolve a diverged main.
- Resolution (per directive §4/§206): the 11 new commits are safely committed
  and pushed on the branch. Land them via a **new PR** to main (same as PR #10)
  — main already contains everything up to `7fa2f42`; this PR adds `8e5fa7d..
  025301f`.
- Note: commits show GitHub "Unverified" (no GPG signing key in this
  environment); author identity is correct (`noreply@anthropic.com` / `Claude`).

## Phase status
- Phase 0 (stabilization/architecture): **implemented**
- Phase 1 (production vertical slice + integration): **live paths in place**
  (input, camera, car spawn interface, HUD modules, audio, rendering); authored
  track runtime built + reference circuit; production UI covers 4 screens.
- Phases 2–6: **additive architecture landed** (rulebook, replay model, VFX
  pool, physics models, telemetry). NOT full feature completion — see below.

## Completed this continuation run (commits after 7fa2f42)
1. Input System is the live driving path behind one flag (IDriveInputSource +
   legacy/InputSystem sources; gamepad rumble adapter).
2. Cinemachine is the live camera path behind one flag (CameraRig delegates to
   RaceCameraDirector; look-back, user offset, shake scale).
3. Production car spawn interface (CarDefinition/CarRuntimeFactory/CarRigBinding
   + ProductionCarSpawner); race + ghost spawn route through it.
4. Authored-track runtime (sampler + query runtimes + mesh builder + orchestrator
   + reference circuit generator + editor tools) + Docs/TRACK_PIPELINE.md.
5. Audio bank drives real events (RaceAudioDirector) + layered engine audio
   (EngineAudioLayers, used by VehicleAudio when the bank has layers).
6. Rendering centralization (MaterialInstanceService, LightingMoodApplier) +
   Docs/UI_DESIGN_SYSTEM.md.
7. Production HUD modules (telemetry snapshot + event-driven widgets on HudRoot).
8. Additive: FlagRules, StartProcedureRules, ReplayRecording, RaceVfxController,
   physics models (Tyre/Aero/Brake/Powertrain), TelemetryRecorder.

## What is LIVE vs BUILT-NOT-LIVE (honest)
- LIVE (default on, legacy behind flag): driving input (Input System), race
  camera (Cinemachine), car spawn (production interface), event audio director,
  graphics quality tier routing, save protection, extracted race rules.
- BUILT, NOT YET THE LIVE PATH (documented flags / follow-on extraction):
  - Production HUD shell/modules — legacy RaceHud is still the single live HUD
    (feature parity not reached, so not removed).
  - Authored-track runtime — legacy procedural TrackManager still builds the
    live race track (RaceManager query-interface extraction is Phase 3).
  - Physics models, replay model, VFX pool, flag/start rulebook — additive,
    not wired into the live loop yet.
  - Production UI covers 4 screens; career/settings/etc still legacy RuntimeUi.

## Known unverified risks
- All package-API usage verified only by static review, not compilation.
- Hand-authored .asset/.meta GUIDs rely on package script GUIDs resolving in-editor.
- Cinemachine/Input System live paths default ON; if a package API mismatch
  surfaces in-editor, flip the PlayerPrefs flags (f1game_cinemachine=0,
  f1game_input_system=0) to fall back to legacy while fixing.

## External-asset blockers
- Final car/track/UI/audio/VFX art + audio recordings. Slots + specs exist
  (Docs/ART_PIPELINE.md). Placeholders explicitly marked.

## Remaining ordered tasks (directive §12), not yet done
4. Pit-lane rework, AI racecraft, full rules integration, race reliability.
5. Physics wiring into VehicleController, weather/track-state depth.
6. Career screen migration to production UI + career systems depth.
7. Replay/telemetry/broadcast/photo-mode/accessibility/localization runtime.
8. Multiplayer architecture.

## Live-playtest run (post-merge, user-verified in Unity)
The game now compiles and runs in the user's editor. Fixed during live
playtesting, all on `main` (verified or directly reported-back by the user):
- Tyre-select screen surviving into a live race (frontend cleared at race start).
- Per-frame URP `CreatePipeline` NRE: **active pipeline reverted to Built-in**
  (hand-authored URP assets cannot carry renderer shader resources / global
  settings; see KNOWN_ISSUES.md). URP package + scaffolding remain, flag-gated.
- Built-in post FX **restored** (CameraPostFx/RacePostUber from pre-migration
  history), attached only when no SRP is active; mood/quality driven from
  CreateLighting alongside the URP Volume path.
- Hairpin outside-line barrier gap (barrier boxes now span the offset-edge chord).
- FPS: hard shadows, vertex fill/flood lights, removed duplicate per-car VFX
  stack (VehicleVfxDriver no longer attached; VehicleEffects is the single VFX
  system per car).
- AI: Italy-hairpin crash = classification bug (turn-angle probe measured short
  of the apex); fixed with an apex-walk probe + hairpin threshold raised to 168
  degrees. General turning speeds preserved per user preference. AI stays on the
  reactive line (pursuing the drawn ribbon cost corner speed via the edge-brake;
  reverted per user report).
- Time trial: auto-Softs, tyres preheated to their window, damage off, pit
  request/card disabled, DRS in normal zones only (user corrected an "anywhere"
  attempt).

## Rules integration status (directive §12 item 4)
Live: ChampionshipPoints, QualifyingProgression, RaceClassifier, PenaltyRules,
PitRequestRules. Deliberately NOT wired: FlagRules/SessionFlow (would replace
working, user-tuned race-control conditionals with a mapping that risks silent
divergence for zero user-visible gain) and StartProcedureRules' false-start rule
(would penalize the hold-throttle launch style the game currently allows - an
unrequested gameplay change). Jump start is already live with the rulebook's 5s
penalty, matching StartProcedureRules.PenaltySeconds.

## Exact next action
- In-editor bring-up (Docs/EDITOR_BRINGUP.md): packages, TMP, tests, prefab
  bakes, then validate the live input/camera flags. After that, resume at
  directive §12 item 4 remainder (pit-lane rework, AI racecraft depth, race
  reliability) - each needs live playtest feedback per change; this session
  showed autonomous rewrites of user-tuned behaviour cause regressions, so ship
  small, verify with the user, then continue.
