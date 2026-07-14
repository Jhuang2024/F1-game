# Changelog

All notable changes to the production migration. Dates omitted (no reliable
clock in the authoring environment). Static-only: nothing below has been
compiled or run in Unity.

## Migration — production rebuild (in progress)

### Added (live paths)
- Modular assembly structure (`Assets/Game/Code`) with 11+ assembly definitions;
  typed event bus (`F1Game.Core.Events`); hardened JSON persistence with backups,
  corruption recovery, and schema migration.
- URP 14 + Linear colour; High/Medium/Low pipeline tiers; URP Volume
  post-processing replacing the removed `CameraPostFx`; lighting-mood profiles.
- Input System driving path behind a flag (dead-zone/sensitivity/saturation/
  inversion, gamepad rumble); Cinemachine race-camera path behind a flag.
- Production car spawn interface (`CarDefinition`/`CarRuntimeFactory`/
  `CarRigBinding`/`ProductionCarSpawner`) with livery via MaterialPropertyBlock.
- Event-driven audio director + layered engine audio; centralized material
  instancing; graphics-preset tier routing.
- Authored-track runtime (sampler, query services, mesh builder, reference
  circuit) + `ITrackQuery` adapters with a live race call site.
- Pooled VFX controller wired to live vehicle events.
- Extracted, unit-tested race rulebook (points, classification, penalties,
  qualifying progression, pit-request gates, flags, start procedures).

### Fixed
- Critical UI transition bug: TMP import no longer auto-activates the production
  UI; `UiSessionCoordinator` is the single UI-ownership authority; strategy→race
  is atomic and single-flight; exactly one HUD renders; pause/results/menu-return
  detach the HUD cleanly and never resurrect the strategy screen.
- Focus-outline overlay, progress-bar fill mode, and several extraction
  correctness issues surfaced by static review.
- Production HUD legibility pass: every meter in the top-right status column
  now carries a caption and a numeric readout (ERS %, tyre life %, damage),
  the duplicate flag chip is gone (the shell chip is driven solely by the
  telemetry FlagModule), bright chip backgrounds (hard/medium compound) get a
  contrast-aware dark label, and the DRS / pit / weather chips spell out their
  state ("DRS READY", "NO PENALTY", "WEATHER · CLEAR").
- Race-start gantry: the five start lights moved from the mid-left timing-tower
  stack to a new top-center HUD dock, doubled in size, and now hold visibly
  DARK for a beat at lights-out (with the LIGHTS OUT flash directly beneath)
  instead of vanishing on the exact frame the race goes live. Big-moment and
  race-control banners moved to the same centre stage.
- Bottom-center HUD meters labelled (RPM / THROTTLE / BRAKE); healthy telemetry
  meters (ERS charge, sub-redline RPM) use a new cool `energy` palette colour
  instead of the warm accent, which was nearly identical to danger red.
- Pit-stop duration: the pit entry ramp and service boxes were lap FRACTIONS
  (0.85/0.885/0.9), so on realistically-scaled tracks the guided pit visit ran
  ~1200m at 58-68 km/h ("more than half a lap", 70+ seconds). Entry/corridor/box
  anchors are now fixed metres before the start/finish line (matching the
  earlier fixed-metre pit-EXIT conversion) and the rail pace runs at the
  realistic ~75-80 km/h pit-limit ballpark - a full stop is now ~20 seconds on
  every track, for player and AI alike. Pit paint/signage and the approach/entry
  HUD zones re-anchored to the same metre-based boundaries.
- Street-circuit "bubbles": city/street tracks no longer spawn the rolling
  grass hill domes or the untextured mountain-ridge sphere ring (their horizon
  identity is the dedicated skyline/parallax building layers); the spheres read
  as giant grey bubbles floating between the city blocks.
- Car visibility: near-black team liveries get a luminance floor plus a subtle
  team-colour self-illumination on body panels, and night races run brighter
  floodlit ambient/directional lighting - car silhouettes no longer melt into
  the equally-dark asphalt (worst on night street circuits).
- Time trials always run dry: the event's wet/mixed weather profile no longer
  applies (track surface, car physics, audio and the gloomy rain lighting mood
  all forced to Clear), so hot-lap conditions are repeatable and comparable.

### Known incomplete
See `Docs/KNOWN_ISSUES.md` for the honest list of partial / not-started systems
(full UI/career migration, HUD parity, track migration, physics/AI/pit/rules
integration, replay/broadcast/photo, accessibility/localization, multiplayer,
monolith decomposition). These are not claimed complete.
