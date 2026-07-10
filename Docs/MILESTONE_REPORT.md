# Milestone report — Phase 0 + Phase 1 production migration (first slice)

Branch: `claude/unity-production-migration-so7zih`
Scope executed: Phase 0 (stabilization/architecture) fully; Phase 1 (vertical
slice) to the limit of what can be done and honestly claimed **without a Unity
editor in the authoring environment** (see §Constraints and §Not done).

---

## 1. Changed and created files

371 files changed (10,942 insertions, 554 deletions): 142 added, 14 modified,
3 deleted (plus generated `.meta` files). Full list: `git diff --name-status
db437dd..HEAD`. Highlights:

**Modified (legacy code, minimal deliberate edits)**
- `Assets/Scripts/Race/RaceManager.cs` — delegates classification ordering,
  qualifying elimination, mandatory-pit + pit-cancel decisions and penalty
  reason handling to `F1Game.Race.Rules`; publishes PenaltyIssued / Retirement
  / PitRequestChanged events; post-fx statics → `RaceVolumeService`; material
  creation → `ShaderCompat`.
- `Assets/Scripts/Career/CareerManager.cs` — points table + standings
  tiebreak → `ChampionshipPoints`.
- `Assets/Scripts/Core/LocalJsonStore.cs` — facade over hardened
  `JsonSaveService` (same on-disk format/paths).
- `Assets/Scripts/Core/GameBootstrap.cs` — two guarded hooks into
  `ProductionUiBridge` (main menu, quick-race flow) + `Ui` accessor.
- `Assets/Scripts/UI/RuntimeUi.cs` — one setter (`SetQuickRaceSelectedEvent`).
- `Assets/Scripts/Vehicle/CameraRig.cs` — attaches URP camera setup instead of
  the deleted CameraPostFx.
- `Assets/Scripts/Vehicle/VehicleVisuals.cs`, `Race/TrackManager.cs` — material
  creation through `ShaderCompat` (URP-compatible).
- `Assets/Scripts/Core/SimpleAudioManager.cs`, `Vehicle/VehicleAudio.cs` —
  authored audio-bank resolution before generated fallbacks.
- `Packages/manifest.json`, `ProjectSettings/{ProjectSettings,
  GraphicsSettings, QualitySettings}.asset` — packages, Linear colour, input
  Both, URP assignment per quality tier.

**Deleted**
- `Assets/Scripts/Core/CameraPostFx.cs`, `Assets/Shaders/RacePostUber.shader`
  (the custom OnRenderImage pipeline), `Packages/packages-lock.json`
  (regenerates).

**Added — `Assets/Game` (new production tree, 11 asmdefs)**
- `Code/Core`: typed event bus + contracts, pooled GameObject pool,
  JsonSaveService (atomic writes, .bak rotation, corruption recovery),
  SaveMigrations (schema-version probe + migration chains),
  PerformanceCapture.
- `Code/Race/Rules` (engine-free): ChampionshipPoints, RaceClassifier,
  PenaltyRules, QualifyingProgression, PitRequestRules, SessionFlow.
- `Code/Rendering`: ShaderCompat, RaceVolumeService (URP Volume stack),
  LightingMoodProfile, UrpCameraSetup, MaterialLibrary.
- `Code/UI`: UiTheme, ScreenRouter/NavigationStack/ScreenView, modal/toast/
  tooltip/transition/device-prompt services, widget library (ThemedButton
  with 8 explicit states, TabBar, StatusChip, StatTile, DataRow,
  UiProgressBar, ModalView, ToastView, ControllerPrompt), UiScreenFactory,
  UiShell, four screens as view+presenter pairs.
- `Code/Input`: InputService (6 action maps), RebindService (interactive
  rebind, conflict detection, multi-slot, persisted overrides),
  DeviceWatcher, InputCurves, IForceFeedback + no-op impl.
- `Code/Cameras`: CameraProfile SO + RaceCameraDirector (Cinemachine).
- `Code/Audio`: AudioBank + AudioBankService.
- `Code/Track`: TrackDefinitionAsset (+validation).
- `Code/Vehicle`: CarRigSpec, PlaceholderArtMarker.
- `Code/Editor`: UiSetupTools (TMP fonts + prefab baking),
  RenderPipelineValidator, CarPrefabBuilder (+validator), TrackDataExporter.
- `Code/Tests/EditMode`: 8 test files (~45 tests).
- **Authored assets:** URP-High/Medium/Low + URP-Renderer, UiTheme_Default,
  5 LightingMoodProfiles, 4 CameraProfiles, 21 placeholder PBR materials,
  F1Controls.inputactions.
- `Assets/Scripts/UI/ProductionUiBridge.cs` (legacy↔new UI bridge).
- `Docs/`: BASELINE_AUDIT, REFACTOR_MAP, ART_PIPELINE, EDITOR_BRINGUP, this
  report.

## 2. New architecture

- **Modules over monoliths.** `Assets/Game/Code` has 11 assembly definitions.
  Legacy `Assembly-CSharp` may call into the new assemblies; the new
  assemblies never reference legacy code (the bridge lives on the legacy
  side). `F1Game.Race` is `noEngineReferences: true` — the rulebook is pure
  C# and unit-testable by construction.
- **Typed events.** `GameEvents` (allocation-free pub/sub with per-handler
  exception isolation) + contracts for session/lap/sector/position/flag/
  penalty/pit/weather/damage/retirement/radio/navigation/save/device events.
  RaceManager already publishes penalties, retirements and pit-request
  changes; the new HUD consumes them with zero polling.
- **Rules extracted verbatim** (points 25-18-…-1, tiebreak points→wins→
  podiums, Q1/Q2 elimination 6+6 capped, track-limit 3-warnings→5 s,
  mandatory-pit 10 s with ≤3-lap exemption, classification ordering incl.
  the retired-car stamp) so behavior is preserved and now pinned by tests.
- **Persistence hardening** behind the unchanged `LocalJsonStore` API:
  temp-file + replace writes, rotating `.bak`, backup recovery, schema-version
  migration registry. Existing saves load byte-for-byte unchanged (version 0).
- **Rendering:** URP 14 with three quality tiers wired into GraphicsSettings/
  QualitySettings; Linear colour; the OnRenderImage chain replaced by a URP
  Volume stack (ACES tonemap, colour adjustments, bloom, vignette, mood fog)
  driven by five authored lighting-mood assets; all runtime materials routed
  through a pipeline-aware factory; an authored (placeholder) PBR material
  library anchors URP/Lit and defines every car/track surface slot.
- **UI:** design tokens in `UiTheme`; one persistent shell canvas with
  screen/modal/toast/tooltip layers; router + back-stack; screens instantiate
  once and toggle (no canvas destruction); views are passive, presenters own
  behavior, the bridge maps legacy data to view-models; TMP everywhere in the
  new path; tabular-numeral slot reserved in the theme; full explicit
  interaction states incl. always-visible focus.
- **Input:** six action maps authored in `F1Controls.inputactions`; rebinding,
  conflict detection, hot-plug device classification and prompt-glyph
  switching implemented; wheel/FFB interface stubbed for a later milestone.
- **Cameras:** authored profiles + Cinemachine director (chase/T-cam/cockpit/
  trackside, speed-FOV, collision, impulse impacts/kerb vibration, horizon
  stability) — built, feature-gated off pending in-editor validation.
- **Audio:** authored bank resolves first everywhere; the procedural clips are
  now formally *labelled fallbacks*, not content.

## 3. Before/after screenshots

**Not possible in this environment** (no Unity editor/GPU). The capture list
is specified in `Docs/EDITOR_BRINGUP.md` step 10 and must accompany the first
in-editor run.

## 4. Before/after performance measurements

**Not measurable here** (no runtime). The structural baseline is documented in
`BASELINE_AUDIT.md`; `PerformanceCapture` (avg/p95/p99/worst frame ms, GC
delta, memory) is the repeatable harness; the 22-car target and capture
procedure are in `EDITOR_BRINGUP.md` step 9. Until those numbers exist, no
performance improvement is claimed.

## 5. Tests performed and results

- **Authored:** ~45 EditMode tests across 8 files — championship points table
  and tiebreaks; classification ordering incl. penalties, DNF stamps and
  monotonic lapped-car estimates; Q1/Q2/Q3 elimination for full, undersized
  and oversized fields; penalty gates and reason dedup; pit-request cancel
  gates (incl. the live limiter-line lockout and never-cancellable pre-race
  plan); session-flow decisions; event bus (delivery, unsubscribe, exception
  isolation, reset, per-type isolation); save round-trip, backup rotation,
  corruption recovery, schema-version probing, migration chains.
- **Executed: NOT run.** No .NET runtime or Unity editor exists in this
  container (verified: no dotnet/mono; package registry and dotnet CDN blocked
  by the network policy). The tests are written for the Unity Test Runner and
  must be executed at editor bring-up (checklist step 7). Static review of the
  extracted logic against the original code (verbatim mirrors) is the only
  verification performed here — stated plainly, not as a substitute for
  execution.

## 6. Existing features verified not to have regressed

Same constraint: no runtime here, so "verified" means *code-path analysis*,
not play-testing:

- All rule extractions delegate with identical constants/ordering; behavior
  differences would be test failures at bring-up.
- Save format, file names and paths unchanged; the new service adds backups
  around the same JSON.
- The production UI is strictly additive: both hooks fall back to the legacy
  path on any exception, when TMP is unusable, or via the
  `f1game_production_ui=0` kill switch; every other screen still runs the
  legacy RuntimeUi.
- `activeInputHandler = Both` keeps `PlayerVehicleInput`/`RaceHud` legacy
  input working alongside the new Input System.
- Cinemachine director is gated off; the legacy CameraRig continues to drive
  the race camera.
- Risk that could not be pre-verified: the URP switch changes how the
  runtime-generated world renders (lighting response, transparency, skybox).
  ShaderCompat covers the Standard-shader surface; visual parity must be
  checked in-editor (bring-up steps 3, 8).

## 7. Placeholder content (explicitly not finished)

- 21 `M_*_Placeholder` materials — flat URP/Lit values holding car/track PBR
  slots (spec: ART_PIPELINE.md).
- Placeholder car prefab from `CarPrefabBuilder` — correct hierarchy, pivots,
  slots, LOD/collision structure; proxy boxes tagged `PlaceholderArtMarker`.
  **Not final art**, and the primitive-built runtime car is still what races
  until Phase 2 swaps it.
- Controller prompts render glyph ids as text until icon art exists.
- Procedural audio remains as labelled fallback behind empty bank slots.
- Procedural track environment remains interim; `TrackDefinitionAsset` +
  exporter are the migration path for the reference track.
- Screen prefabs bake in-editor (step 5); until baked, the factory builds the
  same screens at boot (once, not per navigation).

## 8. Known issues

1. Nothing in this changeset has compiled or run — the environment has no
   Unity/.NET. Bring-up (EDITOR_BRINGUP.md) is the required next action; typos
   or API mismatches surface there first.
2. Hand-authored URP asset YAML relies on package script GUIDs; if any fail to
   import, `RenderPipelineValidator` repairs PostProcessData but the pipeline
   assets themselves would need one-time recreation (guide included).
3. Legacy code sets `QualitySettings.antiAliasing/shadowDistance` directly —
   harmless but inert under URP; the graphics preset should switch quality
   levels / pipeline assets instead (planned with the settings screen
   migration).
4. `_EmissionColor` writes in legacy code may need `EnableKeyword("_EMISSION")`
   under URP on some paths (same requirement as built-in; verify at bring-up).
5. UI Toolkit-style flourishes (9-slice rounded corners) absent in the new
   theme until sprite assets exist — surfaces are deliberately flat.
6. HUD shell coexists with the legacy RaceHud; only one should render once the
   HUD widget migration lands (currently the legacy HUD is the live one).
7. Fog is Exponential-Squared via RenderSettings; height/volumetric fog and
   SSAO renderer feature are not yet configured.

## 9. Exact next milestone

**"Slice bring-up and visual parity"** — everything requires the editor:
1. Execute EDITOR_BRINGUP.md fully (packages, TMP, validation, prefab bakes,
   tests green, smoke test, perf captures, screenshots) and fix whatever it
   surfaces; commit baked prefabs + perf JSON + screenshots.
2. Validate and enable the Cinemachine director; delete CameraRig.
3. Swap the runtime primitive car for the rig-spec prefab path (placeholder
   proxies first, so the art swap later is data-only) and pool skid/scuff/
   debris spawning.
4. Migrate the reference track into `TrackDefinitionAsset` via the exporter;
   render it from authored data.
5. HUD widget migration (timing tower, telemetry, tyres, delta) onto HudRoot
   docks, then retire the legacy RaceHud path.
6. Resolution matrix pass (1080p/1440p/4K/16:10/ultrawide) on the four new
   screens.

Only after that: Phase 2 content pipeline (per the brief's ordering).
