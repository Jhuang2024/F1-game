# Continuation State

Living ledger for the production rebuild. Updated after every logical commit.

## Environment reality
- Unity cannot run here (no editor, no GPU, no package resolution). Everything
  below is **static-only / unverified**: not compiled, not tested, not run.
- No claim of compilation, runtime, visual, test, or performance success is made.

## Git
- Branch: `claude/unity-production-migration-so7zih`
- Base: `origin/main` (NOT diverged — ff-only merge to main is possible at end)
- Last completed commit at session resume: `7fa2f42`

## Phase status
- Phase 0 (stabilization/architecture): **implemented**
- Phase 1 (production vertical slice): **integration in progress** (this run)
- Phases 2–6: architecture/code being added this run

## Completed this continuation run
- (updated as batches land)

## Current phase / task
- Phase 1 live integration → driving input adapter (feature-flagged), camera
  director live, production car spawn interface, authored-track runtime,
  production HUD modules, audio runtime, rendering centralization.

## Decisions
- Input: abstract raw driving reads behind `IDriveInputSource`; legacy and new
  Input System sources swappable behind one flag (`DriveInputConfig.UseInputSystem`),
  so all RaceManager wiring in PlayerVehicleInput is preserved.
- Car/track/camera new paths land behind explicit feature flags defaulting to the
  legacy path until in-editor validation; flags documented, not permanent.
- New runtime assemblies never reference Assembly-CSharp; legacy bridges live in
  Assembly-CSharp and reference the new modules.

## Known unverified risks
- All package-API usage (URP 14 / Cinemachine 2.9 / Input System 1.7 / TMP 3.0.6)
  verified only by static review, not compilation.
- Hand-authored .asset/.meta GUIDs rely on package script GUIDs resolving in-editor.

## External-asset blockers
- Final car/track/UI/audio art. Slots + specs exist (Docs/ART_PIPELINE.md).

## Remaining ordered tasks (see continuation directive §12)
1. New architecture live path (input/camera/car/track).
2. Production UI + HUD completion.
3. Audio + rendering integration.
4. Pit lane, AI, rules, reliability.
5. Physics + weather.
6. Career + frontend migration.
7. Replay, telemetry, broadcast, photo mode, accessibility, localization.
8. Multiplayer architecture.

## Exact next action
- Author the driving input adapter (F1Game.Input.DrivingInputReader) + legacy
  bridge (IDriveInputSource) and wire PlayerVehicleInput behind the flag.
