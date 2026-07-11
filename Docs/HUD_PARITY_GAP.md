# Production HUD parity gap (vs legacy RaceHud)

Authoritative checklist for retiring the legacy `RaceHud` (3.2k lines).
Derived from a full element audit; line refs are RaceHud.cs. The legacy HUD
stays the ordinary default until every Tier 1-4 item is live in production
and validated in-editor. Update this file as gaps close.

Production modules already live: position, lap/clock, speed/gear/rpm,
ERS/DRS, tyre compound+wear, fuel laps, gaps, lap times, sectors, flag
(incl. blue), pit/penalty chip, pit status line, weather (production-only),
start lights, timing tower (top-10 + player), track-limit flash, notification
feed (penalty/retirement/pit-request/radio events).

STATUS: every Tier 1-4 item is now implemented in production. The remaining
unchecked items are Tier 5-8 (sub-widget depth + lowest-priority flourish)
and are legacy-only refinements, not blockers. Per the retirement rule above,
the production HUD is now structurally at Tier 1-4 parity; switching it to the
ordinary default still requires the in-editor VISUAL pass (PENDING here - no
Unity), so RaceHud stays the live default until that validation lands.

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
- [x] Pit status line (2164): box number / limiter detail. (PitStatusModule -
      composed headline + emphasis from the relay, mirrors UpdatePitCard incl.
      the fast-exit 108 cap and the cancel-confirmation override)
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
- [x] Track-limit warning flash (2469): warning count. (TrackLimitFlashModule -
      UI-side edge detection over snapshot TrackLimitWarnings, amber pulse)

## Tier 5 — Spatial/order widgets
- [x] Tower per-row tyre compound + interval column. (tranche 1)
- [x] Tower per-row DRS dot + PIT tag. (tranche 9; 22-row depth still
      capped at top-10 + player)
- [x] Track map minimap (497): HudTrackMap channel (outline once per track +
      per-frame car dots) + pooled-dot MinimapModule. (tranche 9; team-colour
      dots + progress strip still legacy-only, needs in-editor visual pass)

## Tier 6 — Notifications (event-bus work)
- [x] Watcher + relayed toasts (2489/2590): RaceManager.QueueHudToast now
      publishes a HudToastEvent at the source (queue preserved for legacy);
      NotificationFeed subscribes. All existing toasts flow to production.
      (tranche 10)
- [x] Big-moment flashes LIGHTS OUT/GREEN FLAG/FINAL LAP (1667/2566/2955):
      BigMomentModule, UI-side edge detection over the snapshot. (tranche 11;
      DRS-available flash + audio and VICTORY flash still legacy-only)

## Tier 7 — Session-specific
- [x] Qualifying/ghost delta card (2442). (DeltaModule, tranche 8; numeric
      core TryGetQualifyingDelta/TryGetGhostDelta now shared with legacy text)
- [x] Qualifying feedback panel (1415) - QualiFeedbackModule; TT checkpoint
      counter (1981) - CheckpointsModule. (tranche 12) Quali tower BEST/GAP
      layout + TT single-row + TT track record still legacy-only.

## Tier 8 — Flourish/dev (lowest)
- [ ] Finish flourish (3014); micro-animations; hint bar (1262); debug
      overlay (2708).

Production-only additions (not gaps): weather chip, session clock.
