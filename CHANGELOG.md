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
- Small tuning pass (per request): AI wall-aversion eased back ~10% across
  every boosted lever (margin band 8-14m -> 7.2-12.6m, tight-fence multiplier
  1.6x -> 1.45x, recovery steering 0.65-1.6 -> 0.58-1.44, emergency-brake
  floor/ceiling and overspeed multiplier both trimmed ~10%); ERS drain raised
  50% on top of the previous cut (0.02875-0.04125/s -> 0.043125-0.061875/s,
  a full deploy now empties in ~16-21s instead of ~24-32s).
- AI grid-start deadlock fixed: the wall-aversion boost below removed the
  emergency brake's old low-speed discount (its overspeed multiplier floor
  went from 0.7x to 1.0x while its base floor was also raised), so a
  stationary/launching car sitting in a grid box even slightly toward the
  track edge - normal with 22 staggered boxes, more likely with the wider
  margin band - now got a genuine brake demand at 0 kph, and cars never left
  the grid. Both the emergency brake and the recovery steering are now gated
  by a launch-speed factor: fully off below 15kph, ramped in by 40kph (well
  clear of the launch phase, still comfortably under even the hairpin crawl
  speed), so the boosted system is at full strength for every situation it's
  actually meant to catch and inert during a standing start.
- AI wall-aversion (reactive edge-avoidance system) significantly boosted:
  the margin band that triggers the recovery-steer/emergency-brake response
  widened (was 5.5-9.5m, now 8-14m, wider still near known tight-fence
  corners), the recovery steering pull strengthened (was 0.38-1.03, now
  0.65-1.6), and the emergency brake ramps up much earlier and harder (was an
  overspeed ramp starting at 130kph with a 0.22-1 x 0.7-1.55 brake-demand
  range; now starts at 70kph with a 0.45-1.3 x 1-2 range, saturating to full
  brake at meaningfully lower proximity/speed). The margin is capped at 60%
  of the local track half-width so the boost can't exceed narrow circuits'
  own width and fire everywhere instead of only near a genuine edge (the
  exact regression an earlier fix had to correct).
- ERS drain tuning: cut 75% off the deploy drain rate introduced in the
  previous battery-cycle rebalance (0.115-0.165/s -> 0.02875-0.04125/s). That
  rebalance was aimed at stopping the gauge pinning at 100% (braking harvest
  wildly outpaced drain) but overshot the other way - a full deploy drained in
  ~6-8s. Harvest is untouched, so the battery still doesn't pin, but a full
  tank now lasts ~24-32s of deploying instead.
- AI cornering pace fully reverted to its pre-wall-crash-fix values (Slow
  512.5-522.5 kph, VeryTight 302.5-337.5 kph, hairpin classification gate back
  to >=168 degrees): play-testing confirmed the corner-speed-floor collapse
  theory was NOT the actual cause of the AI's corner-crashing, so degrading
  cornering pace for it wasn't buying anything. The real crash cause is still
  open and needs separate investigation.
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
- HUD meters actually move now: Unity ignores Image.fillAmount on sprite-less
  images (falls back to a full quad), so every runtime-built bar (ERS, tyre,
  damage, RPM, throttle, brake) rendered permanently full. UiProgressBar now
  assigns a shared solid sprite so the Filled geometry applies.
- ERS battery genuinely cycles: braking harvest used to bank up to a full
  battery in one hard stop (~5x the deploy drain), pinning the gauge at 100%.
  Braking harvest cut to a third and deploy drain raised ~45%, so a full
  deploy empties in ~6-8s and management is a real decision.
- "Sim Qualifying" restored on the career path: the production Career Hub only
  exposed "Continue" (straight into driven qualifying), losing the legacy
  weekend hub's simulate option. The hub now has a Sim Qualifying action wired
  to the existing sim flow (tyre briefing -> full simulated classification),
  always available so a re-run replaces the stored result, matching legacy.
- AI wall crashes at tight corners fixed: 18 rounds of tuning had inflated the
  Slow/VeryTight corner-speed floors to 302-522 kph - beyond top speed - so
  both buckets clamped to straight-line pace and the AI never braked for any
  tight corner that wasn't a >=168-degree U-turn. Restored real floors
  (Slow ~115-150 kph, VeryTight ~82-110 kph), widened the hairpin gate to 140
  degrees, and the edge emergency-brake band starts 40% wider near known
  tight-fence corners. HighSpeed/Medium corner pace untouched.

### Known incomplete
See `Docs/KNOWN_ISSUES.md` for the honest list of partial / not-started systems
(full UI/career migration, HUD parity, track migration, physics/AI/pit/rules
integration, replay/broadcast/photo, accessibility/localization, multiplayer,
monolith decomposition). These are not claimed complete.
