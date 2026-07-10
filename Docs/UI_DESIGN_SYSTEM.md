# UI design system

The production UI (`F1Game.UI`) is a token-driven, prefab-oriented, TMP system.
This document is the reference the source files point to.

## Principles

- **One persistent shell.** `UiShell` owns a single canvas with dedicated
  layers (screens, modals, toasts, tooltip). Screens instantiate once and
  toggle via `CanvasGroup`; navigation never destroys the canvas (the legacy
  `RuntimeUi` rebuild-per-navigation pattern is the thing being replaced).
- **Views are passive; presenters own behavior.** A `ScreenView` renders a
  view-model and exposes widgets; a presenter wires callbacks. The
  Assembly-CSharp bridge maps monolith data → view-models, so new assemblies
  never reference legacy code.
- **Tokens, not magic numbers.** All typography, spacing, colour, interaction
  states, radii and motion come from the `UiTheme` asset.

## Tokens (`UiTheme`)

- **Typography scale:** display / h1 / h2 / h3 / body / bodySmall / label /
  button / numeric / timingCompact / caption. The `numeric` slot uses the
  tabular-numeral font for times, deltas, telemetry and standings so digits
  align.
- **Spacing:** 8-point grid (4 / 8 / 16 / 24 / 32 / 48 / 64).
- **Palette:** dark neutral base, high-contrast white text, a single warm
  accent. Team colours are reserved for data identity (status chips, data-row
  identity bars), never chrome.
- **Interaction states:** normal / hover / pressed / focused / selected /
  disabled / loading / error. Focus is always drawn as an outline (a toggled
  four-edge frame), never a colour shift alone, so controller/keyboard focus is
  visible.
- **Motion:** micro 150–250 ms, screen 250–450 ms, eased; honoured or skipped
  by `TransitionService` when reduced motion is set.

## Widgets (`F1Game.UI.Widgets`)

`ThemedButton` (all variants + all 8 states), `TabBar`, `StatusChip`,
`StatTile`, `DataRow` (tabular columns), `UiProgressBar` (filled-image meter),
`ModalView`, `ToastView` (pooled), `ControllerPrompt` (device-glyph aware).
Additional components (segmented control, toggle, slider, dropdown, stepper,
telemetry graph, strategy stint block, driver/team/track cards) are authored
against the same token set as screens are migrated.

## Screens

Built by `UiScreenFactory` (the authoring source the editor bake tool turns
into prefabs under `Resources/UI/Screens`). Rebuilt so far: main menu,
quick-race track select, pre-race strategy, race HUD shell. The bridge
(`ProductionUiBridge`) routes the menu → track select → strategy → race path
through the new UI with automatic legacy fallback; remaining screens still run
the legacy `RuntimeUi` until migrated (see the roadmap in the continuation
directive).

## Prefab baking (no Unity here)

Because Unity cannot run in this environment, screen prefabs are **not**
hand-authored as fragile YAML. The prefab-safe component architecture and the
editor bake tool (`F1 Game → UI → Bake Screen Prefabs`) are complete; runtime
one-time construction from `UiScreenFactory` is the explicit fallback until the
bake runs in-editor. This is static-only: not compiled or run.

## Accessibility hooks

Reduced motion (TransitionService), visible focus, tabular numerals, theme
opacity/contrast tokens, device-prompt glyph switching, controller navigation
via explicit Selectable navigation. Colourblind palettes and text scaling are
token extensions applied as the settings/accessibility screens migrate.
