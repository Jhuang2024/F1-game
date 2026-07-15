# Changelog

All notable changes to the production migration. Dates omitted (no reliable
clock in the authoring environment). Static-only: nothing below has been
compiled or run in Unity.

## Deploy flicker, real racing line, drastic fuel strategy (per request)

- DRS/ERS deploy flicker killed with hysteresis, not thresholds:
  - The DRS wing now opens at brake < 0.1 and only closes past 0.3 — a
    hovering auto-brake trim can never flap it (any single threshold, 0.05 or
    0.2, oscillates when the smoothed assist value sits on it).
  - ERS auto-deploy (Attack/Balanced) read `assisted.throttle`, which the
    assists trim every frame, hovering it around the 0.55/0.88 gates — the
    reported ready↔deploying flashing. Now keyed off the driver's RAW throttle
    (same fix the manual path already had) with latched keep-alive thresholds.
- AI line v2 — pursue the precomputed racing line: the reactive heuristic
  computed its offset at the car but applied it 22–62 m ahead (the whole line
  ran ~1–2 s late = "not following the line"), and its entry-setup keyed off
  the SHARPEST corner ahead, so in chicanes it swung wide for the second
  element while ignoring the first — the reported "purposely going the
  opposite direction of the apex". The AI now samples the drawn
  minimum-radius line at the same lookahead the steering target uses, capped
  at halfWidth−2.6 m (outside the edge-emergency-brake's 2.4 m ramp band —
  the reason the original pure-pursuit was reverted), 0.94-scaled for apex
  air, with the heuristic kept only for layouts with no computed line. Line
  slew raised 4.5–9.5 → 6–14 m/s so the swings are actually tracked.
- Fuel strategy is real now: the drive-force fuel penalty was RELATIVE to the
  car's own start load (underfueled and overfueled cars started at identical
  penalty — the underfuel speed boost literally did not exist). It is now
  absolute (~0.2%/kg, Lerp to 0.75 at 120 kg), so light cars are visibly
  faster and every car gains pace as the tank empties. The reserve dropped
  1.2 → 0.4 kg and the load choices widened to ±1.5/±3 laps (was ±0.7/±1.5),
  so underfueling is a genuine management gamble instead of free time.

## AI optimal-line pass (per request)

- The AI line is now a genuine out-in-out arc with explicit safety margins.
  Previously positioning only began once the car was already inside a corner
  (severity gate — zero on the straight), so every corner was entered from
  mid-road and the arc had no "out"; a pure-pursuit of the drawn line had been
  tried and reverted because it rode the corridor edges and tripped the edge
  emergency brake. Now: on the straight approaching a genuine corner the car
  drifts smoothly to the outside of the upcoming turn (direction sampled
  around the apex itself, ramped over 140 m, scaled by corner sharpness,
  capped at 80% of the wall-safe corridor so it never edge-rides), then the
  existing in-corner sweep inherits the wide entry, and the apex peak is
  capped ~12% short of the corridor bound so the line clips the apex without
  parking on the kerb. All offsets remain inside LegalOffsetLimit's wall/kerb
  corridor (1.8 m wall margin, 2.6 m at tight-fence corners) and the two
  downstream lateral clamps are untouched.

## DRS flicker + ERS economy pass (per request)

- DRS READY/OPEN flicker fixed (three cooperating causes):
  - The wing-close brake threshold (0.05) was crossed constantly by the
    auto-brake assist's small speed trims, slamming the wing open/shut all the
    way down a zone. Raised to 0.2 - genuine braking still closes it instantly.
  - A one-frame blip of IsDrsAvailable cleared the player's latch outright,
    leaving the wing shut until a re-press. The latch clear is now debounced
    (~0.25s of sustained unavailability); braking and race control still close
    the wing immediately. The AI's per-zone commitment roll got the same
    treatment (re-rolls only after a real >=0.5s between-zones gap).
  - The HUD's "in a zone?" early-out asked a different geometry source than
    the availability check; both now ask the same one.
- ERS is a scarcer resource: deploy drain +10% (PowertrainModel band
  0.0683-0.0980, tests updated) and braking-zone harvest cut 30%
  (0.098-0.147). Both apply to AI identically (shared drain/harvest path).
- Verified the compound/ERS/DRS systems all bind to AI cars: same
  TyreState.Tick in the shared physics loop, same ErsBattery drain/harvest,
  same DrsActive/boost path - the new tyre spreads, ERS rates and DRS wing
  behaviour hit the whole field, not just the player.

## Tyres/DRS/start-reaction pass (per request)

- Tyre compounds are drastically differentiated (they previously differed by a
  rounding error): dry wear spread widened from 1.5/1.08/0.74 to 2.2/1.1/0.55
  (a soft now wears ~2x a medium, a medium ~2x a hard), grip spread widened
  from 1.11/1.00/0.93 to 1.16/1.00/0.87, flat compound speed offsets doubled
  (medium 7.5→15 kph, hard 15→30 kph slower than soft), and the simulated-
  qualifying dry tyre ladder widened to match (soft ~1s/lap quicker than hard).
- DRS fixed: the boost was a 15-second window armed by a single activation
  that kept delivering +30 kph and its push force after the wing closed —
  through braking zones, corners, and the whole next sector. The boost now
  lives and dies with the wing actually being open (brake/zone-exit/
  availability-loss all end it instantly); the HUD ACTIVE state follows the
  wing. `DrsBoostSecondsRemaining` and the timer plumbing removed.
- AI start reaction times are human again: the base-delay scale (0.7–0.35×)
  put Expert AI at 0.08–0.16s off the line — below the ~0.2s floor of human
  reaction. Rescaled to 1.4–0.9×, spanning ~0.2s (Expert, top driver, real
  F1-grade) to ~0.7s (Easy, poor driver). Tests updated.

## Game-problems review pass (design/balance/fairness, not bugs)

A dedicated audit of genuine game problems — unfair or dead mechanics, balance
collapse, degenerate strategies, punishing flows — followed by fixes. Static
review only (no Unity here); the AI cornering/mistake changes and the quali
difficulty rescale in particular want an in-editor drive.

### AI behaviour & difficulty
- ~~Cornering floors/braking-target change~~ REVERTED after play-testing: the
  joint Slow/VeryTight floor lowering + braking-apex-target de-inflation made
  the AI turn far too slowly, and was reverted verbatim to the round-18/12
  values (Slow bucket round 22 / VeryTight round 16 notes in
  AiVehicleController). The saturated-floor analysis is left in the tuning
  history for whoever next picks up the corner-crash thread.
- Racecraft desaturated: dropped the ×1.69 defending/overtaking multiply that
  clamped all 22 drivers to identical 100-rated clones, and replaced the ×2.6
  commitment buff (every tier saturated at 1.0 — Easy fought exactly like
  Expert) with a 30% lean toward full commitment (Easy ~0.55 → Expert ~1.0).
- AI mistakes are real now: a tier-scaled share of mistake rolls becomes a
  misjudged braking point (arrive hot, run deep, lose real time) instead of a
  sub-metre wobble clamped inside the legal-line corridor. Gated off during pit
  approach, off-track recovery and SC/VSC caps.
- AI qualifying difficulty now points the same way as AI race difficulty: the
  quali difficulty term is AI-only (it previously applied to the player's
  simulated lap too, cancelling out) and Easy/Medium are meaningfully slower
  (8%/1.5%), so grid position roughly previews race pace.
- Removed the hidden +3 kph AI top-speed edge over the player in identical
  machinery (AiTopSpeedBonusKph 8 → 5, force curve matched).

### Race rules
- Skipping the mandatory pit stop is no longer strictly optimal: the penalty is
  30s (was 10s — cheaper than the ~20–30s a real stop costs, so the rule
  punished compliance).

### Career, progression & economy
- The player's R&D upgrades now count in simulated qualifying (the sim built
  the player's entry from the raw base car while every AI entry — including
  the teammate's identical car — got the effective upgraded car).
- Season objectives fixed: standings are sorted before position objectives
  read them (they latched "achieved" after round 1 off a creation-ordered
  list), position/head-to-head objectives re-evaluate live instead of latching,
  and "beat teammate" is judged on the season tally, not the first race.
- Objectives and the contract target now have consequences: season-end RP +
  reputation payout per achieved objective, a bonus for meeting the contract
  target, reputation loss for missing it, and an escalating development-budget
  cut for consecutive misses (new `consecutiveContractMisses` save field).
- R&D no longer bricks permanently: `failedUpgradeIds` clear at season
  rollover for the player and every AI team. One tier-1 failure used to lock
  an AI team out of a department's tiers 2–3 for the whole career, and a
  player "abandon" silently walled off the upgrade's prerequisite chain.
- AI weekend R&D income scales with constructor standing (~80–180 RP,
  bracketing the old flat 95) so the field develops with its results instead
  of falling ever further behind the player.

### UX
- "Start New Career" over a save with real progress now requires a second
  press ("Overwrite saved career?") instead of destroying a multi-season save
  on one click.

### Verified fine (checked, no change needed)
- Rules symmetry: track limits, jump starts, blue flags, pit/SC/VSC speed caps
  and overtake restrictions all apply to AI and player through the same paths.
- No rubber-banding anywhere; AI shares the player's physics, fuel burn (mass
  follows fuel live), tyre wear, ERS battery and DRS gap rules.
- Championship points/tiebreaks, DNF classification, season rollover/archives,
  driver progression, practice-reward gating, tyre-compound trade-offs and the
  wet-tyre crossover, and README-vs-code control bindings.

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
