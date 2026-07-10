# Continuation State

Living ledger for the production rebuild. Updated after every logical commit.

## Environment reality
- Unity cannot run here (no editor, no GPU, no package resolution). Everything
  below is **static-only / unverified**: not compiled, not tested, not run.
- No claim of compilation, runtime, visual, test, or performance success is made.

## Git
- Branch: `claude/unity-production-migration-so7zih` (pushed; tip `025301f`).
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

## Exact next action
- In-editor bring-up (Docs/EDITOR_BRINGUP.md): packages, TMP, tests, prefab
  bakes, then validate the live input/camera flags. After that, resume at
  directive §12 item 4 (pit lane + AI + rules integration).
