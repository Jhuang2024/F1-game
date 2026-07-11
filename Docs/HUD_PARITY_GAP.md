# Production HUD parity gap (vs legacy RaceHud)

Authoritative checklist for retiring the legacy `RaceHud` (3.2k lines).
Derived from a full element audit; line refs are RaceHud.cs. The legacy HUD
stays the ordinary default until every Tier 1-4 item is live in production
and validated in-editor. Update this file as gaps close.

Production modules already live: position, lap/clock, speed/gear/rpm,
ERS/DRS, tyre compound+wear, fuel laps, gaps, lap times, sectors, flag
(incl. blue), pit/penalty chip, weather (production-only), start lights,
timing tower (top-10 + player), notification feed (penalty/retirement/
pit-request/radio events).

## Tier 1 — Interactive (input routing needed)
- [ ] Cancel Pit Request button (1031/2235): only interactive HUD control.
      Needs button widget + routing + cancel-eligibility flag
      (`CanCancelManualPitRequest`).

## Tier 2 — Pit/strategy surface (snapshot fields)
- [ ] Pit phase pill incl. "BOX THIS LAP" (2265): `pitPhase`,
      `nextPlannedPitLap`, `pitAutoTriggered`.
- [ ] Pit plan line (2387): planned lap/compound + AUTO/LATE.
- [ ] Pit status line (2164): box number / limiter detail.
- [x] Pit stop progress meter (2210): `PitStopProgress01`. (PitStrategy tranche 1)
- [ ] SC window "BOX NOW?" prompt (2416): `RecommendedPitUnderSafetyCar`.
- [x] Fuel pill states STARVATION/LOW/CRITICAL (1593). (tranche 1)
- [ ] Slipstream/TOW pill (1562): strength/bonus/source.

## Tier 3 — Race-control safety states (snapshot fields)
- [ ] Full race-control banner with reasons + restart countdown (1634):
      `RaceControlState`, `RedFlagReason`, `RestartCountdownSeconds`,
      autopilot flag, yellow sector.
- [ ] Pace-compliance pill (1859): cap kph, SC gap, SLOW DOWN warnings.
- [ ] ERS DISABLED lockout state (3107).

## Tier 4 — Core telemetry (snapshot fields)
- [x] Damage meter (2106): `Damage01`. (tranche 1)
- [ ] Tyre corner grid + temps + lockup/flat-spot (2001/2009): temp status,
      lockup severity.
- [ ] Session label + event name (1919/1922): `SessionKind`, `EventName`.
- [ ] Session message line (1929).
- [ ] Input telemetry bars (627): effective throttle/brake.
- [ ] Track-limit warning flash (2469): warning count.

## Tier 5 — Spatial/order widgets
- [x] Tower per-row tyre compound + interval column. (tranche 1)
- [ ] Tower per-row DRS dot + PIT tag in interval slot + 22-row depth.
- [ ] Track map minimap (497) + progress strip (2664): needs a per-car
      position stream (heaviest new plumbing; no channel exists).

## Tier 6 — Notifications (event-bus work)
- [ ] Watcher toasts: tyre overheat, track limits, best lap, pit-stop
      complete, low fuel, lockup, damage band, pit window (2489).
- [ ] Relayed toasts: overtake/lost position, session fastest, teammate,
      podium (2590).
- [ ] DRS-available flash + audio (1419); big-moment flashes LIGHTS OUT/
      GREEN FLAG/FINAL LAP/VICTORY (1667/2566/2955/3030).

## Tier 7 — Session-specific
- [ ] Qualifying/ghost delta card (2442) - snapshot has unused
      `DeltaSeconds/HasDelta`.
- [ ] Qualifying feedback panel (1415); quali tower BEST/GAP layout (2873);
      TT single-row + checkpoint counter (1981); TT track record (1962).

## Tier 8 — Flourish/dev (lowest)
- [ ] Finish flourish (3014); micro-animations; hint bar (1262); debug
      overlay (2708).

Production-only additions (not gaps): weather chip, session clock.
