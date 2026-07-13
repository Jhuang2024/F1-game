# First editor open — bring-up checklist for this branch

This branch was authored without a Unity editor available (packages cannot be
resolved or compiled headless in the working environment). The code and assets
are written to compile clean, but the following one-time steps in Unity
2022.3.62f1 complete and verify the migration. Expected total time: ~15 min.

1. **Open the project.** Package Manager resolves the new manifest (URP
   14.0.11, TMP 3.0.6, Cinemachine 2.9.7, Input System 1.7.0, Test Framework).
   A restart prompt for the Input System backend may appear — accept ("Both"
   is already configured in ProjectSettings).
2. **TMP Essentials + theme fonts import themselves.** On editor load,
   `UiSetupTools.AutoSetupTextPipeline` imports TMP Essential Resources if the
   TMP Settings asset is missing, then builds the Rajdhani TMP fonts and
   assigns them to `UiTheme_Default` (watch the console for `[UI Setup]`
   lines). The `F1 Game → UI` menu items 0 and 1 remain as manual fallbacks.
   Until this has run once, the production UI auto-falls back to the legacy UI
   and logs a `[UI] Production UI inactive:` warning explaining why.
3. Run **F1 Game → Rendering → Validate URP Setup**. It verifies Linear
   colour, pipeline/quality assignment and URP-Renderer, and repairs the
   PostProcessData reference if the hand-authored GUID didn't resolve.
   (An auto-repair also runs on load; this validates the result.)
4. **Commit the auto-created assets** (`Assets/TextMesh Pro/**`,
   `Assets/Game/Art/Fonts/*SDF*.asset`, the updated `UiTheme_Default.asset`)
   so other machines skip the import.
5. Run **F1 Game → UI → 2. Bake Screen Prefabs** (authors the four screen
   prefabs into `Assets/Game/Resources/UI/Screens`; commit them).
6. Run **F1 Game → Art → Build Placeholder Car Prefab**, then
   **Validate Car Prefab** (commit the prefab).
7. Run the **EditMode tests** (Test Runner → EditMode → run all). Expected:
   all green (~45 tests: rules, events, persistence).
8. **Play Mode smoke test:** main menu (new UI) → Quick Race → track select →
   strategy → race start; verify 22-car race, pit stop, results, save/load,
   and that the legacy fallback works with `PlayerPrefs f1game_production_ui=0`.
9. **Performance captures:** press F10 (Debug map) or call
   `PerformanceCapture.Begin("baseline-urp")` during a 22-car race at each
   quality tier; commit the JSON reports and fill in
   `Docs/MILESTONE_REPORT.md` §Performance.
10. **Screenshots:** capture main menu, track select, strategy, race at
    1080p/1440p/4K/16:10/ultrawide for the report's resolution matrix.

Troubleshooting:
- Magenta materials → re-run step 3; check the console for ShaderCompat
  warnings.
- Production UI didn't appear → check the console: a `[UI] Production UI
  inactive:` warning states the reason (kill switch vs text pipeline), and any
  `[ERR:4001]` error means a production screen threw and latched the legacy
  fallback for the session. TMP essentials missing is the usual cause; the
  auto-setup fixes that on the next editor load (then restart Play Mode).
- URP assets show "missing script" → recreate via Assets → Create → Rendering
  → URP Asset (with Universal Renderer) using the same file names, then rerun
  step 3.
