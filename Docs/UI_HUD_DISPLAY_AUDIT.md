# UI / HUD Display-Mistake Audit

An inventory of display **mistakes** — layout collisions, clipping/overflow,
wrong or missing data, dead visual subsystems, input-blocking, and state
desyncs — across all four UI stacks. Compiled 2026-07-17 by static review
(no Unity editor in this environment; line refs verified against the working
tree). This is the companion to `LOW_RES_RENDERING_AUDIT.md`, which covered
fidelity; nothing here is about resolution.

Stacks audited:
- **Legacy race HUD** — `Assets/Scripts/UI/RaceHud.cs` (the live default HUD)
- **Legacy frontend** — `Assets/Scripts/UI/RuntimeUi.cs` (menus/career)
- **Shared factory/primitives** — `Assets/Scripts/UI/UiFactory.cs` + helpers
- **Production TMP stack** — `Assets/Game/Code/UI/**` (default frontend path)

Severity is user-visible impact. "Manifests" = the conditions that trigger it.

---

## 1. HIGH severity

### H1. Toasts never render — the production toast subsystem is dead
`UiShell.cs:151` constructs `ToastService(toastColumn, null)` with a null
prefab; `ToastService.Show` (ToastService.cs:31-34) early-returns to a
`Debug.Log` whenever the prefab is null, and nothing anywhere assigns a
ToastView prefab. Every production `Toasts.Show(...)` produces a console line
and no on-screen toast; the top-right toast column stays permanently empty.

### H2. The UI-Scale setting does nothing on the default (production) frontend
`UiFactory.ApplyUiScale` (UiFactory.cs:374-387) is the only mechanism that
honours `GameSettingsStore.UiScale`, and it is only applied to the legacy
canvas. The production shell's canvas (UiShell.cs:105-113) hard-codes
1920×1080 and never reads the scale — yet the production frontend is the
default (`ProductionUiReadiness.DefaultWhenUnset = true`). Moving the slider
persists a value and changes nothing on screen. (`UiFactory.GlobalUiScale`
is written but never read — dead code.) `Docs/REFACTOR_MAP.md:64` claims this
is "done in new shell"; the wiring is absent.

### H3. Standings tables hard-capped at 10 rows — the player's own row can be hidden
`RuntimeUi.BuildStandingsRows` (RuntimeUi.cs:6703): `Mathf.Min(10, count)`.
Used for the drivers' championship on the Career Hub (line 322) and the
Season Review final standings (line 5014). With a 22-driver field, P11–P22
are never rendered — a player in a midfield/backmarker car cannot see their
own championship position, and the "final" season standings are silently
truncated forever. The container is a scroll panel, so the cap is purely
artificial.

### H4. Report screens overlap their own sections (single-pass layout rebuild)
`ShowResults` loops `ForceRebuildLayoutImmediate` 4× (RuntimeUi.cs:4896-4899)
with a comment explaining why one pass leaves nested
`VerticalLayoutGroup(childControlHeight=false)` containers under-reporting
height ("cards under-report their height, later sections overlap them...").
`ShowSeasonReview` (:5245), `ShowOffseasonHub` (:5546) and
`ShowPreSeasonTesting` (:5621) build the same nested auto-card structure but
rebuild only once — producing exactly the documented failure: overlapping
sections and short scroll ranges, worst on the content-heavy Season Review.
**Root cause is in the factory** (see F1): the auto-size containers use
`childControlHeight = false` (UiFactory.cs:1069) while relying on
ContentSizeFitter, and the factory docstring (UiFactory.cs:1425-1431)
wrongly promises a single outer rebuild suffices.

### H5. Race Control banner covers the top session band
RaceHud.cs:434-438 (banner, left-pinned, 480 wide, built after the band so it
renders on top) vs :366 (session band, centre-pinned, 980 wide). They
rectangle-overlap whenever reference width < 1972 — i.e. at **every** aspect
16:9 or narrower: ~26px at 16:9, ~75px at 16:10, ~154px at 4:3 (covering the
whole SESSION pill), 300px+ at hudScale 1.3. Manifests during any
yellow/VSC/SC/red/restart period — exactly when the banner appears.

### H6. Radio stack overlaps the right card stack under safety car
The right stack (RaceHud.cs:873-874) grows downward from y=−240; the radio
stack (:1135-1141) grows upward from the bottom. Nothing bounds their
combined extent: an SC-period stack (Car Status + Pit + SC Window ≈ 568px)
meets a full 3-card radio burst (≈ 348px) with ~92px of overlap at 1080p —
ballooning to ~350px at hudScale 1.3. The engineer's radio cards render over
the Pit/SC-Window text at precisely the moment both are most needed. The
comments at :1126-1134 claim this overlap was fixed by re-anchoring; the fix
bounds neither side.

### H7. Disabled production buttons look enabled
`ThemedButton` uses `transition = None`, computes tints only in `Apply()`
(reached from pointer/select/OnEnable events), and doesn't override
`DoStateTransition` — so setting `.interactable = false` from code repaints
nothing. Affected: `EditableSettingRow.cs:63,68` (stepper at min/max),
`CareerActionRow.cs:75` (ineligible actions),
`PreRaceStrategyPresenter.cs:64` (gated Start Race). Buttons keep full
enabled colours and silently no-op; if the disabled button happened to be
focused it *does* repaint (deselect → Refresh), making it inconsistent.

---

## 2. MEDIUM severity

### M1. Dual-stack canvas ordering: production shell occludes and eats clicks
Production canvas `sortingOrder = 40` (UiShell.cs:107); the legacy canvas has
none (default 0). Every production `ScreenScaffold` background is a
full-screen opaque Image with `raycastTarget = true` (UiScreenFactory.cs:
181-183) under its own GraphicRaycaster. Whenever both stacks are alive (the
documented KNOWN_ISSUES coexistence state), the production layer renders
above the legacy UI *and* swallows all its clicks. Both stacks also each
create an EventSystem (UiShell.cs:193, UiFactory.cs:689).

### M2. Styled buttons render their colour squared (double tint)
`CreateStyledButton` sets `image.color = face` **and** `colors.normalColor =
face` (UiFactory.cs:860, 864-870); with UGUI's ColorTint transition the two
multiply, so every legacy button renders `face²` — Primary red
(0.62,0.05,0.045) becomes near-black (0.38,0.003,0.002), Secondary becomes an
invisible blob, and hover/pressed deltas are compressed. Same double-apply in
`CreateModeCard` (:958,962-967) and `CreateToggleControl` (:2216-2221).
`CreateChartPoint` (:2685,2689) does it correctly (normalColor = white),
confirming the button path is the mistake.

### M3. Timing-card sector row wraps into the gap row
RaceHud.cs:845-850: the sector row (y −118..−142) sits 4px above the gap row
(:852-857). `CreateText` defaults to horizontal Wrap + vertical Overflow
(UiFactory.cs:764,772) and the row isn't overridden; the race-mode string
("S1 00:23.456 S2 00:24.891 S3 00:22.104 NOW S3 ...", ~58 chars at font 13)
far exceeds ~300px, wraps, and overflows down over the GAP/INT/PEN line.
Manifests in every race once the first sector time exists.

### M4. Top band grows over the progress strip at high HUD scale
`ApplyPanelScale` scales `localScale` about each panel's pivot without moving
anchors (RaceHud.cs:354-357). The top band's scaled bottom crosses the
progress strip's fixed top (y=−66) at hudScale ≳ 1.12 (slider max 1.3). The
header comment (:14) claims hudScale "can never push widgets offscreen" —
true, but localScale reserves no layout space, so neighbours overlap instead.

### M5. Tooltips are never clamped to the screen
`TooltipService.Request` (TooltipService.cs:44-47) sets
`tooltipRoot.position = screenPosition` with a centre pivot and no edge
clamping, despite the class doc claiming it clamps. Tooltips near the right/
top edges render partly offscreen.

### M6. "Biggest Loser" championship card can never show a loss
`FindChampionshipBiggestMover(..., gainer:false)` (RuntimeUi.cs:453-481)
computes deltas over cumulative points, which are monotonically
non-decreasing — the minimum delta is ≥ 0. The card (:800-808) therefore
always shows a zero/positive number labelled as a loss; it's really "fewest
recent points".

### M7. Career Hub render mutates and persists save state
`BuildRndReportCard` (RuntimeUi.cs:1468-1469) clears
`pendingRndMessages` and calls `career.Write()` **while drawing** the hub.
Any rebuild for a non-navigation reason destroys the one-shot R&D messages
before the player has read them. Rendering should not write saves.

### M8. Main-menu controller navigation dead-spot
`BuildMainMenu` wires explicit cyclic up/down navigation including the
Standings button (UiScreenFactory.cs:220), but `MainMenuView` hides Standings
without a career (MainMenuView.cs:68-70). Explicit nav targets aren't skipped
when inactive: on a fresh profile, pressing Down from Time Trial stalls on
the hidden button.

### M9. HUD flag chip flashes white-on-white at spawn
`BuildHudShell` (UiScreenFactory.cs:929-938) adds the flag chip's background
Image with no colour (defaults opaque white) and near-white label text
"GREEN"; until `FlagModule` first calls `StatusChip.Set`, the top-right chip
is a white box with invisible text.

### M10. Numeric text silently loses tabular alignment when the font token is missing
`UiScreenFactory.CreateText` (:53-59) and `HudModuleAssembler.Numeric` (:196)
null-guard the tabular font; a missing `UiTheme_Default`/`tabularNumeric`
silently falls back to the proportional TMP default — the HUD timing tower
and lap-time digits jitter instead of aligning, with no visible error.

---

## 3. LOW severity

- **L1. Thin accent bars blur under the new mipmapped rounded sprite.** The
  4×-density `BuildRoundedSprite` (mip + trilinear, UiFactory.cs:527-544) is
  consumed by 3-4px-tall accent chips/rules (:881-885, :1220-1226, :1238,
  :1283-1285, :1731); at that size they sample a blurry mip and read soft/
  translucent. *(Regression from the 2026-07-17 fidelity pass — fix: serve
  thin bars from a plain non-mipmapped sprite or disable mips on the small
  rounded sprite.)*
- **L2. Top-band segments overflow into neighbours** (RaceHud.cs:396, :372,
  :381): event names are unbounded and lap text at font 19 can spill across
  segment dividers (Overflow mode = no clipping).
- **L3. Minimap sector tints use index fraction, not distance fraction**
  (RaceHud.cs:538-543 vs TrackManager.cs:595): S1/S2/S3 colour boundaries sit
  slightly off the true sector splits when centerline points are non-uniform.
- **L4. Radio cards block raycasts while fading** (RaceHud.cs:1158 adds a
  CanvasGroup but never clears `blocksRaycasts`, unlike notifications at
  :704-705) — invisible cards swallow bottom-right clicks.
- **L5. Input-telemetry panel overlaps the bottom dash at 4:3 + hudScale 1.3**
  (RaceHud.cs:653 vs :755).
- **L6. `CreateBand` decorations stay raycast targets** (UiFactory.cs:
  732-738) — currently masked by creation order, but a latent click-blocker
  and wasted raycasts on every screen.
- **L7. Chart point tap targets are 9-13px** (UiFactory.cs:2680-2681;
  RuntimeUi.cs:662) — effectively untappable; only each series' last point is
  interactive at all.
- **L8. Fixed-height factory rows can wrap-bleed under localization**
  (`CreateBreakdownRow` label UiFactory.cs:1695, `CreateHudCard` header
  :1734, `CreateHudLabelValueRow` label :1746) — labels keep Wrap+vertical
  Overflow, so long strings render a second line over the row below.
- **L9. Rounded tracks, square fills** (UiFactory.cs:1319, :2413,
  :2513-2514): progress/stat/comparison fills poke square corners past their
  rounded tracks at high fill.
- **L10. Scroll panels: no scrollbar affordance + rectangular clip in rounded
  viewports** (UiFactory.cs:1549-1569, :2533-2557).
- **L11. Toggle/selected buttons keep stale pressed/disabled colours**
  (UiFactory.cs:2208-2231, :916-943): a green ON toggle flashes near-black
  when pressed.
- **L12. Chart gridline edge labels straddle the plot** (UiFactory.cs:
  2604-2622; no mask on the plot area): top/bottom labels sit half outside
  and can collide with the title.
- **L13. PauseOverlay card clips its 6th button** (UiScreenFactory.cs:305
  fixes the card at 520×520; PauseOverlay.cs:92-95 activates "End Practice",
  overflowing the ~424px inner height in practice sessions).
- **L14. TopLeftDock over-stacks** (~14 HUD modules into a 300px dock with no
  fitter or clipping; UiScreenFactory MakeDock + HudModuleAssembler.Populate)
  — lower modules can overrun toward the timing tower on smaller layouts.
- **L15. Minimap outline doesn't rebuild on container resize**
  (MinimapModule.cs: outline gated on `OutlineVersion` only, baked with the
  old `mapSize`; car dots re-place every frame) — after a mid-session
  resolution/HUD-scale change, dots drift off the drawn circuit until the
  track changes.
- **L16. TransitionService is dead code** — `FadeIn/FadeOut` are never
  called; `ScreenRouter` snaps alpha 0/1, so there are no screen transitions
  despite the design doc's motion spec. (If ever wired: `Fade` doesn't cancel
  an in-flight coroutine.)
- **L17. ModalService close events unpublished** (ModalService.cs:37 —
  open publishes a bus event, `CloseTop`/`CloseAll` publish nothing; bus
  observers can believe a modal is still open).
- **L18. 12px muted text on near-black panels** (RuntimeUi.cs:3131, :5987,
  :5993, :6002; main-menu status strip :146-149 is the lowest-contrast combo
  in the app), further shrunk at UI scales < 1.
- **L19. Duplicated medal/accent palettes drift** (UiFactory.cs:1663-1666 vs
  :2428-2430; :926 vs :838): the classification badge and podium card show
  subtly different golds/silvers/bronzes for the same finisher.
- **L20. AccessibilityColors is opt-in per call site** — the factory's status
  dots/badges/pills take raw colours and never route through
  `AccessibilityColors.Status`, so colour-vision mode covers only scattered
  call sites by construction.
- **L21. Championship chart empty-state label** isn't centred in the plot
  (default anchors; cosmetic, noted while verifying the changed screen).
- **L22. UI-scale clamp mismatch** — factory clamps 0.5-2.0
  (UiFactory.cs:381), settings clamp 0.85-1.15 (GameSettingsStore.cs:19-20);
  the wide range is unreachable dead code.

---

## 4. Cross-cutting themes

1. **The two-stack coexistence is the biggest structural display risk** (H1
   of KNOWN_ISSUES, M1 here): sorting order 40-vs-0, two EventSystems, two
   HUDs, opaque raycast-catching backgrounds. Until the legacy stack is
   retired, every screen transition is one flag away from a blocked or
   double-rendered UI.
2. **`localScale`-based HUD scaling reserves no layout space** (M4, L5): any
   panel scaled up can overlap its neighbours; the in-code claim that scaling
   is overlap-safe is false.
3. **`CreateText`'s global Wrap + vertical-Overflow default** (M3, L8) turns
   every fixed-height row with a long string into a bleed hazard —
   particularly under localization, whose pseudolocale deliberately lengthens
   strings by ~40%.
4. **Null-guarded theme assets fail silent** (M10, H1's null prefab, M9's
   default-white chip): missing assets change appearance instead of shouting.
   A visible dev-mode error state would catch all of these at bring-up.
5. **State-during-render** (M7) and **event asymmetry** (L17) break the
   passive-view principle the production stack's own design doc mandates.

## 5. Known/accepted gaps already tracked elsewhere (not re-listed above)

- Production HUD Tier 5–8 parity gaps (per-corner tyre grid, quali tower
  BEST/GAP layout, TT track record, finish flourish, hint bar, debug
  overlay, team-colour minimap dots): `Docs/HUD_PARITY_GAP.md`.
- Screen prefabs unbaked (runtime construction fallback), 9-slice flourish
  absent pending sprite assets, iconography rendered as text glyph ids by
  design: `Docs/MILESTONE_REPORT.md` §7, `Docs/ART_PIPELINE.md`.
- CinematicHud/ReplayScrubberUi occupy the same screen regions as the live
  HUD's top band/bottom dash — safe only while they remain mutually-exclusive
  opt-in modes (flagged for a guard, not a live defect).

## 6. Verified-clean (checked, not defects)

- No closure-over-loop-variable bugs in any legacy `AddListener` loop.
- Classification table cells clip correctly (RectMask2D + best-fit).
- Settings screens rebuild and re-read state after every change (no stale
  toggles). Timing-tower height math (44+22·21+10=516) is correct.
- Tyre wear colour thresholds are not inverted (`Wear` = grip remaining).
- Chart segment pivot math lands exactly on data points; the 2026-07-17
  chart-joint and minimap-polyline changes pool and reset state correctly.
- The 4×-density sprite rework preserves all on-screen geometry (ppu math
  verified for Sliced and Tiled consumers); only L1's thin-bar mip blur
  regressed.

## 7. Remediation pass (2026-07-17)

All HIGH and MEDIUM findings and nearly all LOW findings were fixed on this
branch the same day. Status per item:

**HIGH — all fixed**
- H1 ✅ `UiShell.BuildToastTemplate` builds a code-authored ToastView (raised
  surface, kind-coloured accent, body text) and passes it to ToastService —
  toasts render for the first time.
- H2 ✅ `UiShell.ApplyUiScale` scales the production canvas the same way the
  legacy one scales; pushed at boot (`ProductionUiBridge.
  ApplyAccessibilityToShell`) and live from `UiFactory.ApplyUiScale`
  (`UiShell.ActiveShell`). L22's clamp mismatch fixed alongside (factory now
  clamps to `GameSettingsStore.UiScaleMin/Max`).
- H3 ✅ `BuildStandingsRows` renders the full field (the container already
  scrolled); P11–P22 and the player's own row are visible.
- H4 ✅ Root-caused in the factory: new `UiFactory.ForceRebuildAutoLayout`
  (multi-pass) with a corrected docstring; ShowResults, ShowSeasonReview,
  ShowOffseasonHub and ShowPreSeasonTesting all use it.
- H5 ✅ Race Control banner (and pace pill) pinned below the top band's real
  scaled bottom via `TopBandClearance()` — no overlap at any aspect/scale.
- H6 ✅ Radio stack moved one column inboard of the right-edge column; the
  SC-period card stack and radio bursts no longer share a column. (Residual:
  at hudScale 1.3 an extreme 3-card burst can brush the scaled column's
  lower edge — flagged for the in-editor pass.)
- H7 ✅ `ThemedButton.DoStateTransition` override repaints on programmatic
  `interactable` changes — disabled buttons now look disabled.

**MEDIUM — all fixed**
- M1 ✅ `SetShellVisible` now also disables the shell's GraphicRaycaster
  (belt-and-braces against a hidden-but-enabled canvas eating legacy clicks).
- M2 ✅ Button/mode-card/toggle graphics stay white; the ColorBlock alone
  carries the tint — authored colours render exactly once. (L11 fixed in the
  same pass: selected/toggle states derive pressed/disabled from their own
  face colour.)
- M3 ✅ Sector row: single-line (no wrap) + `CompactSectorTimes` strips the
  redundant "00:" minute prefix so the line fits.
- M4 ✅ Progress strip tracks `TopBandClearance()` — the scaled band can no
  longer grow over it.
- M5 ✅ `TooltipService.ClampToScreen` at request and again at reveal.
- M6 ✅ "Biggest Loser" now measures championship POSITIONS lost over the
  momentum window (`FindChampionshipBiggestRankLoser`) — it can actually
  show a loss, or "--" when nobody dropped.
- M7 ✅ R&D report renders from a session cache; the save queue is consumed
  once, and hub rebuilds no longer destroy unread messages.
- M8 ✅ `MainMenuView.Render` re-points explicit navigation around the hidden
  Standings button.
- M9 ✅ Flag chip initialized via `StatusChip.Set("GREEN", Positive)` at
  build — no white-on-white frame.
- M10 ✅ One-time `Debug.LogWarning` in both TMP text factories when a theme
  font (esp. tabularNumeric) is missing — silent fallback now announces
  itself.

**LOW — fixed:** L1 (thin accent bars are plain solid Images again — button
chip, mode card, HUD card, radio card), L2 (event/lap band segments best-fit
+ clip), L3 (minimap sector tints by arc length), L4 (radio cards no longer
block raycasts), L5 (input telemetry shifts left as hudScale rises), L6
(`CreateBand` defaults `raycastTarget=false`), L7 (chart points get a
28px invisible hit pad), L8 (`ConstrainSingleLineLabel` on breakdown/HUD
row labels and card headers), L9 (bar fills inset 1.5px inside rounded
tracks), L10 (slim auto-hide scrollbar on both scroll panel builders), L11
(with M2), L12 (chart edge labels pivot inward), L13 (pause card 520→620
tall), L15 (minimap outline rebakes on container resize), L16
(ScreenRouter.EnterTransition wired to TransitionService.FadeIn, with
per-group fade cancellation), L17 (modal close publishes the mirror bus
event), L18 (12px labels → 13px; status strip brightened), L19 (shared
`MedalGold/Silver/Bronze` + `PrimaryButtonFace/Hover` constants), L21
(chart empty label centred), L22 (with H2).

**Mitigated:** L14 — timing tower dock anchored lower (0.45) and the
top-left column spacing tightened; full-worst-case stacking still needs the
in-editor visual pass to confirm.

**Accepted as designed:** L20 — `AccessibilityColors.Status` adoption is
explicitly incremental per its own contract; the factory's raw-Color
primitives can't infer semantic roles mechanically. Each call site adopts
`Status(role)` as screens migrate.

All of this is static-only (no Unity editor in this environment); the
in-editor bring-up pass in `EDITOR_BRINGUP.md` validates it visually.
