# Low-Resolution / Placeholder Rendering Audit

An inventory of everything in the codebase that is **coded** to render at low
resolution, low fidelity, or placeholder quality. Compiled by static review of
the working tree (no Unity editor available in this environment).

**Headline:** the repository contains exactly **two binary art assets** — the
Rajdhani SemiBold/Bold fonts. There are no texture, model, sprite, or audio
files anywhere in `Assets/`. Every visible surface in the game is generated in
code at runtime: primitive geometry, small procedural bitmaps (16²–256² px),
and runtime-built materials. Much of this is *deliberate and documented*
placeholder policy (`Docs/ART_PIPELINE.md`, `PlaceholderArtMarker`), but this
audit lists it all concretely, plus a few genuine config bugs found on the way.

---

## 1. Render pipeline & quality-tier configuration

| Item | Value | Where |
|---|---|---|
| Active pipeline | URP, **URP-High** asset | `ProjectSettings/GraphicsSettings.asset` (guid `a9172a10…`) |
| URP-Low tier | MSAA **off**, HDR **off**, main shadow map **1024**, shadow distance **50 m**, **1** cascade, no soft shadows, no additional-light shadows, 2 lights/object, LUT 32 | `Assets/Game/Settings/Rendering/URP-Low.asset` |
| URP-Medium tier | MSAA 2×, shadow map 2048, distance 90 m, 2 cascades | `URP-Medium.asset` |
| URP-High tier | MSAA 4×, shadow map 4096, distance 150 m, 4 cascades, HDR grading | `URP-High.asset` |
| Unity quality levels | Very Low/Low → URP-Low; **Medium AND High → URP-Medium**; Very High/Ultra → URP-High | `ProjectSettings/QualitySettings.asset` (`customRenderPipeline` guids) |

Findings that look like bugs rather than intent:

1. **`GraphicsPresetService.cs:17-19` comment contradicts the asset mapping.**
   The comment says Unity levels "3-5 → URP-High", but `QualitySettings.asset`
   assigns level 3 (High) the **URP-Medium** guid (`76302956…`). Because the
   game's 0–3 quality int maps `{1, 2, 3, 5}`, game tier 2 silently renders on
   the Medium pipeline. Only game tier 3 (Ultra) reaches URP-High.
2. **`Docs/KNOWN_ISSUES.md` ("reverted to Built-in") is stale.** It states the
   pipeline was set back to Built-in with `customRenderPipeline: {fileID: 0}`
   everywhere; the current tree has URP assets assigned in GraphicsSettings and
   every quality tier. Whichever state is intended, docs and settings disagree.
3. The legacy per-tier `QualitySettings` fields are aggressive at the low end
   and inert under URP anyway: Very Low/Low have `shadows: 0`, `lodBias`
   0.3/0.4, `shadowDistance` 15/20, `antiAliasing: 0`
   (`ProjectSettings/QualitySettings.asset`).
4. Post-processing backends: under URP, `RaceVolumeService` drives ACES
   tonemap/bloom/vignette via a Volume; the hand-rolled `CameraPostFx` chain
   (§5) only attaches under Built-in. Quality 0 disables post entirely
   (`RaceVolumeService.GlobalEnabled`).

---

## 2. Cars — primitive geometry + tiny procedural textures

The **live** race car is the interim primitive build
(`CarVisualFactory.CreateOpenWheelCar`); the authored prefab path
(`CarRuntimeFactory`/`CarRigSpec`) returns null until real art exists. Every
car on track today is:

### Geometry (`Assets/Scripts/Vehicle/CarVisualFactory.cs`)
- One hand-built **8-vertex / 12-triangle** tapered box mesh (`CreateTaperedBox`,
  :313) for chassis, floor, sidepods, nose, engine cover, diffuser.
- ~40+ `PrimitiveType.Cube` for wings, flaps, endplates, halo, mirrors,
  steering wheel, livery flashes (`CreateChildCube`, :352).
- Spheres for helmet, visor, nose tip (:373); each wheel = 4 default ~20-side
  **cylinders** + a cube caliper (:391-439); suspension arms = 8 thin cubes
  (:462).
- `EnsureHaloRingDetail` (`VehicleVisuals.cs:1786`) admits the constraint:
  "…without needing actual geometry booleans (not available with the
  primitive-only toolset this codebase uses throughout)."
- Every spawned car is tagged with `PlaceholderArtMarker` (:162) — "NOT final
  art"; the content validator fails a release check while any marker remains.
- Near-black liveries are artificially brightened (`EnsureVisibleBodyColor`,
  :519) because unlit primitives "disappeared into the tarmac" at night.
- Safety car: coupe assembled from the same boxes/cylinders (:187-290).

### Procedural car textures (`Assets/Scripts/Vehicle/VehicleVisuals.cs`)
All RGBA32, **mipmaps off**, default bilinear, shared statics:

| Texture | Size | Lines | Used for |
|---|---|---|---|
| Tyre tread | **64×8** | 1069-1085 | groove pattern, tiled ×10 |
| Rim spokes | **128×16** | 2030-2054 | wheel rims |
| Carbon weave | **16×16** | 2064-2087 | floor/diffuser/halo — comment: "rather than built at high resolution" |
| Contact shadow blob | **32×32** | 2155-2178 | fake ground shadow, also reused as damage scuff decal (:1331) |
| Particle soft dot | **32×32** | 2386-2408 | every car particle (dust/spray/sparks/smoke) |

### Lighting/shadow downgrades
- Sun shadows forced to **Hard** (`RaceManager.Lighting.cs:105`) — comment:
  soft filtering across "22 cars each built from dozens of small cosmetic
  primitives" was "a dominant per-frame cost". Fill + 6 floodlights cast
  **no** shadows, all `ForceVertex`.
- Single scene reflection probe at **resolution 128** (:174), rendered once
  per session (`ViaScripting`), never refreshed.
- Fake **blob contact shadow quad** under each car with real shadows off on
  skids/scuffs (`VehicleVisuals.cs:2101-2133, 944, 1313`).

### Particles
- Emitter caps: default **200**, sparks **80**, haze **40**, damage smoke
  **160** (`VehicleVisuals.cs:2328-2360`); pooled alternative stack capped at
  **64** with objects literally named `VFX_<kind>_PLACEHOLDER`
  (`RaceVfxController.cs:98,108`).

### Ghost car (`RaceManager.Ghost.cs:127-193`)
- All renderers overwritten with a single translucent alpha-0.4 material
  (loses livery detail), shadows off, colliders off; playback lerps between
  sparse recorded samples (capped count/fixed interval) with no wheel spin or
  VFX.

---

## 3. Track & environment — primitives + 32²–256² noise bitmaps

### Surface textures
- **Shared placeholder library** (`Assets/Game/Code/Rendering/ProceduralSurfaceTextures.cs`):
  `const int Size = 128` (:30) — every material slot (asphalt, kerb, gravel,
  grass, barriers, tyre wall, fencing, pit/garage concrete…) gets one
  **128×128** generated albedo. Header comment: "this is a placeholder, not a
  competing system."
- **Live TrackManager generators** (`Assets/Scripts/Race/TrackManager.cs`):
  asphalt noise **256²** trilinear (:3316), building window strip **64²
  Point-filtered** (:3360, deliberately blocky "row of windows"), chain-link
  fence **32²** cutout (:3394), asphalt wear **128²** (:3429), armco ribs
  **64²** (:3464), concrete panels **128²** (:3500), tyre-barrier tread
  **64²** (:3534), generic noise called at **32²** (tree bark :3177) and
  **64²** (kerbs :3059/3065, mountain ridge :8751).

### Road mesh tessellation
- Live road: one flat 2-vertex ring every **8 m** (`RoadMeshStepMeters`,
  `TrackManager.cs:3847`) — a flat ribbon, no crown or thickness.
- Authored path: spline fixed at **12 subdivisions per control segment**
  (`TrackSplineSampler.cs:46`); road ribbon 2 verts/ring
  (`TrackMeshBuilder.cs:30-58`); reference circuit is a **48-point loop**
  (`ReferenceTrackGenerator.cs:31`).
- Kerbs: flat box primitives placed every 5.5 m (`TrackManager.cs:4082-4105`,
  `CreateKerbBlock` :6797) — no rounded profile.

### Scenery (all `CreatePrimitive` stand-ins, comments call them "cheap")
- Trees: trunk cylinder + sphere-stack canopies "so thousands of them stay
  affordable" (:8349, :10606, conifer 4 tiers :10666, palms = 6 **cube**
  fronds :10711).
- Mountains/horizon: rotated cube slabs — "reads as **low-poly faceted
  terrain**" (:8721); skyline/parallax rings of 6–16 cubes (:8657, :8745);
  dunes = flattened spheres (:10790), rocks = 2-3 cubes (:10843).
- Buildings: cubes + window-band boxes so they don't read "like grey crates"
  (:10485-10512).
- **Crowds are colored boxes**: "Grandstand crowd block" tinted boxes per tier
  (:10284-10372), stadium bowl 12 tiers of crowd boxes (:9733), bleachers 3
  rows (:10461). The authored-track path is thinner still: root object named
  **"Environment (placeholder)"** and crowd zones are **empty GameObjects
  with no renderer at all** (`TrackRuntimeBuilder.cs:25-49`).
- Billboards = single flat boxes with invented sponsor colors (:10014-10065);
  flags/bunting = thin boxes (:8414-8449, :10406).
- Start lights = 10 spheres; camera towers, floodlights = cylinder + cube
  (:7840, :7926, :8538).
- **No LODGroups, no impostors, no distance culling, no static batching** —
  every primitive is an individual GameObject (also flagged in
  `Docs/BASELINE_AUDIT.md`).

---

## 4. UI / HUD

### Procedural sprites (`Assets/Scripts/UI/UiFactory.cs`, RGBA32, no mipmaps)
| Sprite | Size | Filter | Used for |
|---|---|---|---|
| Rounded rect (9-slice) | **32×32** | Bilinear | all buttons/panels (:495-520) |
| Glow dot | **64×64** | Bilinear | **all minimap dots** (:508-555) |
| Checker | **40×40** (4×4 cells) | **Point** — hard aliased edges | finish-line motif (:585-597) |
| Circle | **64×64** | Bilinear | helmet/avatar icons, chart points (:2275-2287) |

- Driver avatars are fully procedural — "no external art, just a team-colored
  circle with a … visor band and initials" (:2306).
- Icons are text glyphs, not art, **by design** — `ART_PIPELINE.md`: "the theme
  renders flat colour surfaces and text glyph ids — by design, so missing art
  is visible rather than faked."

### Minimaps drawn as dot scatter, not lines
- Production HUD: outline capped at **256 samples**, ≤32 car dots
  (`HudTrackMap.cs:23-24`), rendered as pooled **3 px square Images**
  (`MinimapModule.cs:56`, cars 6 px, player 9 px).
- Legacy HUD: 196×196 px map, circuit subsampled to **≤110 glow dots** of
  3.4 px (`RaceHud.cs:276, 526-539`) — comment admits "cheap".

### Charts
- Polylines are chains of rotated `Image` rectangles, no smoothing/AA
  (`ChampionshipChartView.cs:187-228` — 900×330 plot, 3 px lines;
  `UiFactory.cs:2595-2625`); legend swatches are a Unicode "■" glyph (:242).

### Fonts / text
- Legacy UI is uGUI `Text` with a fallback chain that can still land on
  built-in **Arial** (`LegacyRuntime.ttf`) if the Rajdhani resource is missing
  (`UiFactory.cs:410-453`); the comment calls the old Arial state "the single
  loudest 'prototype UI' signal in the whole product".
- Text overflow forced to `Overflow` as a metrics workaround (:736-743).
- Production TMP path null-guards every font assignment — unassigned theme
  typography silently falls back to TMP's default LiberationSans SDF
  (`UiScreenFactory.cs:53-59`, `ChampionshipChartView.cs:286-288`).

### Scale hacks
- UI-scale slider implemented by **dividing the CanvasScaler reference
  resolution** (`UiFactory.cs:374-387`); HUD scale applied via
  `localScale` on panels (`RaceHud.cs:354-356`) — both can soften rasterized
  UI at non-1× scales. Reference resolution 1920×1080 in both stacks
  (`UiFactory.cs:651`, `UiShell.cs:109`).

---

## 5. Post-processing

- **Built-in-pipeline chain** (`CameraPostFx.cs`, active only when no SRP):
  bloom bright-pass and blur at **quarter resolution**, second blur level at
  **eighth resolution** (:88-115); degrades to a plain blit, then no-op, if
  the shader is missing; quality 0 disables it entirely.
- **`Assets/Shaders/RacePostUber.shader`**: 5-sample separable gaussian blur
  (:90-94), Narkowicz "cheap, stable, no LUT" ACES approximation (:56),
  `Fallback Off`.
- **URP path** (`RaceVolumeService.cs`, currently active): stock URP
  Bloom/Tonemap/Vignette — reasonable quality, but color grading LUT is 32
  (all tiers) and URP-Low disables HDR, so bloom banding is likely at tier 0/1.
- SMAA at **Medium** quality on cameras (`UrpCameraSetup.cs:35-36`).
- Photo mode captures at backbuffer resolution only — no supersampling
  (`PhotoModeController.cs:173`); both `PhotoModeController.cs:17` and
  `CinematicHud.cs:16` are marked **"VISUAL VALIDATION PENDING."**

---

## 6. Documented-placeholder inventory (by design, tracked for replacement)

- 21 `M_*_Placeholder.mat` flat URP/Lit materials
  (`Assets/Game/Resources/BaseMaterials/`), spec for real 2K PBR sets in
  `ART_PIPELINE.md`.
- `PlaceholderArtMarker` on every runtime car; editor prefab is
  `F1Car_PLACEHOLDER.prefab` built from `PLACEHOLDER_proxy` cubes
  (`CarPrefabBuilder.cs`).
- `LiveryGenerator.cs` produces "clearly-placeholder liveries" (HSV wheel).
- All audio is synthesized placeholder (27 generators) — not visual, listed
  for completeness.
- `MILESTONE_REPORT.md §7` and `CONTINUATION_STATE.md` maintain the same list.

---

## 7. Suggested priority order for fixing

1. **Config bugs (cheap wins):** correct the quality-tier mapping so game
   tier 2 actually gets URP-High (or fix the comment); reconcile
   KNOWN_ISSUES.md with the actual active-pipeline state.
2. **Highest-visibility runtime quality:** reflection probe 128 → 256/512 by
   tier; consider soft shadows on High/Ultra only; raise road mesh step below
   8 m near corners; mipmaps for the tiled car textures (tread/weave shimmer
   at speed without them).
3. **UI polish independent of art:** draw minimap outline as a line mesh
   instead of ≤110/256 dots; chart polylines via a single mesh/`UILineRenderer`
   rather than rotated Images.
4. **The art pipeline itself** (car FBX, 2K surface library, environment kit,
   icons) — already fully specified in `ART_PIPELINE.md`; everything in §2–§4
   is scaffolded to be replaced by it.
