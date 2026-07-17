# Known issues & honest completion status

This file is the authoritative, honest statement of what is and is not done. It
exists because the project has been asked to be described as "fully complete";
it is not, and this document says so plainly so no downstream reader is misled.

## Environment constraint (applies to everything)
- There is **no Unity editor, no compiler, and no runtime** in the environment
  where this code was written. Nothing here has been compiled, tested, or run.
  All correctness is by static review only. No claim of compilation, runtime,
  visual, test, or performance success is made anywhere in this repo.

## What is genuinely done and on the live path
- Modular assemblies, typed event bus, save protection/migration, URP + Linear
  colour + Volume post-processing, quality tiers.
- Input System driving path (flagged), Cinemachine camera path (flagged),
  production car spawn interface, event-driven audio director, centralized
  material instancing.
- Critical UI transition bug fixed: TMP import cannot activate the production UI
  by itself; one authoritative `UiSessionCoordinator`; atomic strategy→race
  transition; exactly one HUD; clean pause/results/menu-return.
- Pooled VFX wired to live vehicle events; authored-track query adapter has a
  live race call site (reference circuit).

## What is explicitly NOT done (and must not be described as complete)
The following are **partial, built-not-live, or not started**. They are large,
multi-week bodies of work and cannot be truthfully completed or made "the sole
live path" without a compiler and Unity:

- Full production-UI migration of the ~40 frontend/career/session screens; the
  legacy `RuntimeUi` still owns ordinary navigation.
- Production HUD parity and removal of the legacy `RaceHud` from the default path.
- Runtime validation of the authored-calendar conversion: every circuit's
  geometry now loads through `AuthoredCircuitCatalog` definitions and the 22
  per-circuit procedural layouts are retired (Bahrain template kept as the
  emergency fallback world), but nothing has been compiled or driven - and
  `TrackManager` still owns the world-build passes (mesh/kerbs/barriers/pit),
  deliberately, until the authored builder reaches parity.
- Wiring the remaining physics model functions (tyre slip curves, brake fade,
  torque curve) as live vehicle physics; aero and ERS already consume the
  rulebook.
- AI racecraft/strategy overhaul; pit-lane rework; making the extracted rule
  classes the sole live race-control authority.
- Full career-systems depth and career-UI migration; decomposition of
  `RaceManager`/`TrackManager`/`CareerManager`/`RuntimeUi`/`RaceHud`/`VehicleVisuals`.
- Replay/spectator/broadcast/photo-mode live systems; telemetry UI graphs.
- Accessibility, localization, full settings, loading/diagnostics/release depth.
- Multiplayer.

## Why these are not force-completed here
Deleting the working legacy systems (HUD, RuntimeUi, TrackManager, physics,
career) and routing the live game through thousands of lines of **uncompiled,
untested** replacement code would, in all likelihood, leave the game unable to
compile or run at all. The long-standing project rule — *preserve working
gameplay until a replacement is implemented AND validated* — is the correct
engineering constraint and is deliberately honored. New systems are added and
integrated incrementally behind flags or as delegated services, so the working
game is never traded for unverifiable code.

## Render pipeline: URP is the active pipeline (doc previously stale)
An earlier revision of this section described a revert to Built-in after the
hand-authored URP assets crashed at runtime. That description no longer matched
the working tree: `ProjectSettings/GraphicsSettings.asset` points at
`URP-High.asset` and every quality tier's `customRenderPipeline` is assigned a
URP tier asset (Very Low/Low → URP-Low, Medium → URP-Medium, High/Very
High/Ultra → URP-High). **URP is the pipeline the project currently runs.**

Both pipeline configurations remain supported by the runtime code, so a future
revert stays safe:
- `ShaderCompat` keys off `GraphicsSettings.currentRenderPipeline` and falls
  back to the Standard shader under Built-in.
- Exactly one post backend is active per pipeline: the `CameraPostFx`
  OnRenderImage chain (`Hidden/RacePostUber`) attaches only when no scriptable
  pipeline is active; the URP Volume (`RaceVolumeService`) drives post under
  URP.

If URP asset import problems resurface at bring-up (renderer shader resources /
global settings unpopulated), regenerate the URP asset **in-editor**
(Create ▸ Rendering ▸ URP Asset (with Universal Renderer)) and reassign it in
Graphics/Quality; `RenderPipelineValidator` repairs PostProcessData references.

## Runtime validation still required
Everything in `Docs/EDITOR_BRINGUP.md`: package resolution, TMP essentials, test
run, prefab bakes, and Play-Mode validation of every flagged live path.
