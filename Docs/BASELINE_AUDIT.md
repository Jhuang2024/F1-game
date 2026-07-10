# Baseline Audit — pre-migration state (Phase 0)

Audited on the migration branch before any changes. All numbers verified
against the working tree, not taken from the brief.

## Project / rendering

| Item | Baseline |
|---|---|
| Unity version | 2022.3.62f1 |
| Render pipeline | Built-in (`m_CustomRenderPipeline: {fileID: 0}`) |
| Colour space | Gamma (`m_ActiveColorSpace: 0`) |
| Post-processing | Custom `CameraPostFx.OnRenderImage` + `Hidden/RacePostUber` shader (bloom pyramid, filmic tonemap, grade, vignette) |
| Input | Legacy Input Manager only (`activeInputHandler: 0`); `Input.*` used in `PlayerVehicleInput`, `RaceHud` |
| UI | Legacy uGUI `Text`; TMP not installed |
| Packages | com.unity.ugui + core modules only — no URP, TMP, Cinemachine, Input System, or test framework |
| Scenes | 1 (`Boot.unity`, essentially empty; whole game built at runtime via `GameBootstrap.[RuntimeInitializeOnLoadMethod]`) |
| Prefabs / models / textures / audio clips / animations | none (folders contain `.gitkeep` only) |
| Fonts | Rajdhani SemiBold/Bold (OFL) |
| Data | 5 JSON files (teams, drivers, calendar, carPerformance, upgrades) |
| Quality levels | 6 (Very Low → Ultra), current 5; game code writes `QualitySettings.*` fields directly from its own 0–3 quality int |

## Code concentration (lines, verified)

| File | Lines |
|---|---|
| Race/RaceManager.cs | 11,620 |
| Race/TrackManager.cs | 10,995 |
| UI/RuntimeUi.cs | 7,132 |
| Career/CareerManager.cs | 4,720 |
| UI/RaceHud.cs | 3,149 |
| AI/AiVehicleController.cs | 2,832 |
| UI/UiFactory.cs | 2,658 |
| Vehicle/VehicleVisuals.cs | 2,600 |
| Vehicle/VehicleController.cs | 1,827 |
| **Total project** | **54,183** across 31 scripts |

## Visual construction method (counted)

- `GameObject.CreatePrimitive`: **55** call sites (TrackManager 40, RaceManager 9, VehicleVisuals 6).
- `new GameObject(`: **49** call sites across 8 files.
- `Shader.Find`: 13 call sites — `"Standard"` ×6, `"Sprites/Default"` ×5, `"Skybox/Procedural"` ×1, `"Hidden/RacePostUber"` ×1.
- Runtime `new Material(...)`: 13 call sites.
- Procedural texture generators: asphalt noise, window strips, chain-link, armco corrugation, concrete panels, tyre tread, rim spokes, carbon weave, contact shadows, rounded-rect/glow/checker UI sprites, helmet icon.
- Standard-shader-only conventions: `_Glossiness` ×9, `_Mode` transparency blocks ×3 — all magenta/broken under URP without conversion.

## UI construction method (counted)

- One persistent canvas; **every** screen entry (`Show*`, 30 methods) starts with `Clear()` which destroys all canvas children — full rebuild per navigation/interaction.
- Hard-coded layout: 80 `anchoredPosition` + 109 `sizeDelta` assignments across RuntimeUi/UiFactory/RaceHud.
- All text is legacy `UnityEngine.UI.Text` via `UiFactory.CreateText`.
- All UI chrome (rounded rects, glows, checker bands, charts, helmet icon) generated as `Texture2D` at runtime.

## Audio method

- Zero audio assets. All clips generated at startup via `AudioClip.Create`:
  sine tones, sweeps, chords, filtered noise (SimpleAudioManager, 5 generators,
  14 cached clips) + per-car 3-harmonic engine loop and noise scrub loop
  (VehicleAudio).

## Persistence

- 4 JSON stores via `LocalJsonStore` (career, settings, records, ghosts),
  direct `File.WriteAllText` — no backup, no atomic write, no schema version.
  A crash mid-write could truncate a career save. UI scale stored separately
  in PlayerPrefs.

## Camera

- Hand-rolled `CameraRig` (636 lines): 6 modes, multi-layer Perlin shake,
  speed/impact FOV kicks; attaches `CameraPostFx` directly.

## Test/tooling baseline

- No tests, no test framework package, no editor tools, no CI.

## Performance baseline

Runtime profiling is **not possible in this container** (no Unity editor /
player). Structural findings that bound performance:

- Track build generates all geometry/textures at session start (acceptable) but
  primitives are individual GameObjects — no static batching pass, no LODGroups,
  no occlusion, no instancing configuration.
- Skid marks, scuffs and debris instantiate live during racing (no pooling).
- HUD/menu rebuild pattern creates large GC churn on every navigation.
- `PerformanceCapture` (added this milestone) provides the repeatable
  measurement harness; the first captured numbers must be taken in-editor and
  recorded in `Docs/MILESTONE_REPORT.md` §Performance.

## Known bugs / risks observed while auditing

- Retired-car classification depends on a magic `+9999s` stamp
  (`RetireParticipant`) meeting the final time sort in `FinishRace` — now
  covered by tests (`RaceClassifierTests`).
- No fastest-lap championship point exists (notification only) — documented so
  it isn't "fixed" accidentally.
- Settings clamp forces `aiOpponentCount` to 21 (22-car field) on load.
