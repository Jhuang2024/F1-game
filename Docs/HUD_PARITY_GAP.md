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
- [x] Cancel Pit Request button (1031/2235): CancelPitButtonModule, shown
      only while cancellable. Click routes UI → race via HudCommands
      (mirror of HudTelemetry); race-layer eligibility gate makes a late
      click a no-op. (tranche 6)

## Tier 2 — Pit/strategy surface (snapshot fields)
- [x] Pit phase pill incl. "BOX THIS LAP" (2265). (PitStrategyModule,
      tranche 3; approach/exit sub-phases still legacy-only)
- [x] Pit plan line (2387): planned lap/compound. (tranche 3; AUTO/LATE
      tags still legacy-only)
- [ ] Pit status line (2164): box number / limiter detail.
- [x] Pit stop progress meter (2210): `PitStopProgress01`. (PitStrategy tranche 1)
- [x] SC window "BOX NOW?" prompt (2416). (tranche 3)
- [x] Fuel pill states STARVATION/LOW/CRITICAL (1593). (tranche 1)
- [x] Slipstream/TOW pill (1562): strength/bonus/source. (SlipstreamModule, tranche 8)

## Tier 3 — Race-control safety states (snapshot fields)
- [x] Race-control banner with reasons + restart countdown (1634).
      (RaceControlModule, tranche 2; autopilot-ramp wording not yet shown)
- [x] Pace cap surface (1859): live KEEP UNDER n KPH + SC queue slot.
      (tranche 2; SLOW DOWN over-cap warning not yet shown)
- [x] ERS lockout state (3107): meter dims under caution. (tranche 4)

## Tier 4 — Core telemetry (snapshot fields)
- [x] Damage meter (2106): `Damage01`. (tranche 1)
- [x] Tyre temp status + lockup warning (2009/2013). (TyresModule, tranche 7;
      per-corner 2x2 grid still legacy-only)
- [x] Session label + event name (1919/1922). (SessionLabelModule, tranche 4)
- [x] Session message line (1929). (SessionMessageModule, tranche 5)
- [x] Input telemetry bars (627): throttle/brake. (InputTelemetryModule, tranche 7)
- [ ] Track-limit warning flash (2469): warning count.

## Tier 5 — Spatial/order widgets
- [x] Tower per-row tyre compound + interval column. (tranche 1)
- [x] Tower per-row DRS dot + PIT tag. (tranche 9; 22-row depth still
      capped at top-10 + player)
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
- [x] Qualifying/ghost delta card (2442). (DeltaModule, tranche 8; numeric
      core TryGetQualifyingDelta/TryGetGhostDelta now shared with legacy text)
- [ ] Qualifying feedback panel (1415); quali tower BEST/GAP layout (2873);
      TT single-row + checkpoint counter (1981); TT track record (1962).

## Tier 8 — Flourish/dev (lowest)
- [ ] Finish flourish (3014); micro-animations; hint bar (1262); debug
      overlay (2708).

Production-only additions (not gaps): weather chip, session clock.
