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

### Known incomplete
See `Docs/KNOWN_ISSUES.md` for the honest list of partial / not-started systems
(full UI/career migration, HUD parity, track migration, physics/AI/pit/rules
integration, replay/broadcast/photo, accessibility/localization, multiplayer,
monolith decomposition). These are not claimed complete.
