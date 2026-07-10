# File-by-file refactoring map

Line numbers refer to the pre-migration baseline (see BASELINE_AUDIT.md).
Statuses: **done** (this milestone), **extracted** (logic now lives in a new
module, monolith delegates), **planned** (next milestones, in order).

Rule enforced from now on: **no new major system lands inside these files.**
New behavior goes into `Assets/Game/Code/*` modules; monoliths only shrink.

---

## RaceManager.cs (11,620 lines)

| Cluster (baseline lines) | Target module | Status |
|---|---|---|
| Points/classification ordering (`FinishRace` 10270–10360) | `F1Game.Race.Rules.RaceClassifier` | **extracted** + tested |
| Penalty accumulation (`AddPenalty` 10090, `ApplyMandatoryPitPenalty` 10055, track-limit thresholds 9916) | `F1Game.Race.Rules.PenaltyRules` | **extracted** + tested; detection stays with physics |
| Q1/Q2/Q3 elimination (10833–10872) | `F1Game.Race.Rules.QualifyingProgression` | **extracted** + tested |
| Pit request accept/cancel gates (3748–3907) | `F1Game.Race.Rules.PitRequestRules` | **extracted** + tested |
| Session start/transition (`StartSession` 664, Q-phase advance 1002) | `SessionStateMachine` + `RaceSessionController` | planned (Phase 2); pure transition helpers already in `SessionFlow` |
| Race control / SC / VSC / red flag (1626–3660) | `IncidentService`, `SafetyCarCoordinator`, `RaceControlStateMachine` | planned (Phase 3, with rulebook) |
| Timing/gaps (5967–7160) | `RaceOrderService` + `LapTimingService` (merge with RaceStateManager) | planned (Phase 2) |
| Engineer/radio (4325–5322) | `RadioService` publishing `RadioMessageEvent` | planned (Phase 2) — HUD side already event-driven |
| Car mesh construction (`CreateOpenWheelCar` 8426–8878) | **deleted** when car prefab pipeline replaces primitives (CarRigSpec/CarPrefabBuilder ready) | planned (Phase 2) |
| Lighting/skybox (`CreateLighting` 8890) | `F1Game.Rendering` lighting profiles | **partially done** — post/grade moved to `RaceVolumeService`; sun/sky next |
| Fuel model (1330–1520) | `FuelModel` (pure) | planned |
| Ghost record/playback (5592–5800) | `GhostService` | planned |
| Pit rail execution (9103–9910) | `PitLaneController` | planned (Phase 3) |

## TrackManager.cs (10,995 lines)

| Cluster | Target | Status |
|---|---|---|
| Track data (implicit in builder) | `F1Game.Track.TrackDefinitionAsset` (spline/width/camber/kerbs/surfaces/DRS/pit/grid/sectors/cameras/marshals/crowd/lighting) | **model done**; exporter tool added; reference track migration next |
| Procedural textures (3328–3597) | Authored PBR surface library (`Resources/BaseMaterials`, 14 track slots) | **library seeded** (placeholder mats); texture sets pending assets |
| Material helpers (3301–3634) | `F1Game.Rendering.ShaderCompat` | **done** (URP-compatible) |
| `TrackRuntime` queries (16–1590) | Stays; becomes a consumer of `TrackDefinitionAsset` | planned |
| Mesh building (~1765–10800) | `TrackMeshBuilder` as pure function of authored data + baked meshes/colliders | planned (Phase 2) |
| Environment spawning | `TrackEnvironmentSpawner` + authored environment kits | planned (Phase 2/3) |
| `RaceControlVisualDriver` (10809) | Own file under Track module | planned (mechanical move) |

## RuntimeUi.cs (7,132 lines)

| Cluster | Target | Status |
|---|---|---|
| Canvas lifecycle (destroy-all per `Show*`, 30 screens) | `ScreenRouter` + `NavigationStack` + CanvasGroup toggling | **replacement built**; legacy path still used for un-migrated screens |
| Main menu (66–197) | `MainMenuView/Presenter` | **rebuilt** (vertical slice) |
| Quick-race track select (3824–3994) | `TrackSelectView/Presenter` | **rebuilt** |
| Race/tyre strategy select (3431–3823) | `PreRaceStrategyView/Presenter` | **rebuilt** (quick-race path) |
| Career hub, R&D, settings, ratings, results, season screens | One view+presenter pair each, on the same router | planned (Phase 4, screen by screen) |
| Modal/backdrop ad-hoc code | `ModalService` (dedicated layer) | **service done**; adoption per screen |
| State fields held in UI | View-models via bridge | pattern established |

## UiFactory.cs (2,658 lines)

| Cluster | Target | Status |
|---|---|---|
| Text creation (legacy `Text`) | TMP widgets (`UiScreenFactory.CreateText`) | **replacement done**; legacy factory remains for un-migrated screens |
| Procedural sprites (rounded rect/glow/checker 515–613) | Authored 9-slice sprites in `Art/UI` | planned (asset-dependent; placeholder = flat theme colours) |
| Buttons/cards/rows/meters | `F1Game.UI.Widgets` prefab library | **core set done** (button, chip, tile, row, tabs, progress, modal, toast, prompt) |
| Palette constants (390–403) | `UiTheme` asset | **done** |
| `GlobalUiScale`/CanvasScaler | `UiShell` CanvasScaler + theme | **done** in new shell |

## RaceHud.cs (3,149 lines)

| Cluster | Target | Status |
|---|---|---|
| HUD shell + layout | `HudRoot` docks (safe-area anchored) | **shell built** |
| Notifications (`PushNotification`) | `NotificationFeed` (pooled, event-driven, priority eviction) | **built**; legacy feed still live until HUD migration completes |
| Flag banner | `StatusChip` + `FlagChangedEvent` | **built** (event publisher on race side still to be added for flags) |
| Timing tower / telemetry / tyres / delta / map | One widget class per module docking into `HudRoot` | planned (Phase 2, next milestone) |

## VehicleVisuals.cs (2,600 lines)

| Cluster | Target | Status |
|---|---|---|
| Materials/textures (2051–2216) | `ShaderCompat` + `MaterialLibrary` | **done** (URP-safe); authored textures pending |
| Car body construction | Car prefab pipeline (`CarRigSpec` + `CarPrefabBuilder`) | **pipeline built**; runtime swap planned Phase 2 |
| Wheel/suspension/steering visuals | `WheelVisualController` bound to rig pivots | planned |
| Skid/scuff spawning | Pooled via `GameObjectPool` | planned |
| `VehicleEffects` (2318) | `VehicleVfxController` + pooled VFX library | planned |

## CameraRig.cs (636)

Replaced by `RaceCameraDirector` + authored `CameraProfile` assets
(Cinemachine: chase/T-cam/cockpit/trackside, speed-FOV, collision, impulse
impacts/kerb vibration, horizon stability). **Built, gated off** until
validated in-editor; CameraRig now calls `UrpCameraSetup` instead of the
removed post-fx component. Deletion follows validation.

## CameraPostFx.cs (134) — **deleted**

Replaced by `F1Game.Rendering.RaceVolumeService` (URP Volume: ACES tonemap,
colour adjustments, bloom, vignette, mood fog) driven by authored
`LightingMoodProfile` assets. `RacePostUber.shader` deleted.

## SimpleAudioManager.cs (525)

| Cluster | Target | Status |
|---|---|---|
| Clip generation | `AudioBank` slots resolve first; generators demoted to labelled fallbacks | **done** |
| Cue routing / volumes | `AudioBankPlayer` service consuming GameEvents | planned (Phase 2) |

## VehicleAudio.cs (112)

Engine/scrub loops resolve from the bank (`vehicle_engine_loop`,
`vehicle_scrub_loop`) before generating. Layered RPM/load/gear engine audio
model defined on `AudioBank.EngineLayer`; runtime layer mixer planned with the
first real recordings.
