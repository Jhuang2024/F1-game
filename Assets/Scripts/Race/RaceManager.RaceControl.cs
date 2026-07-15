using System.Collections;
using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager race-control incidents subsystem (partial, part 1 of 2). The
    /// state-machine entry (UpdateRaceControl), field-wide incident detection,
    /// RegisterIncident + pileup grouping, severity escalation, red-flag
    /// consideration/onset and sector-yellow triggering. Split out of the
    /// monolith verbatim; escalation thresholds, RNG rolls and call order
    /// unchanged. Safety-car deployment + the SC state machine are part 2.
    /// </summary>
    public partial class RaceManager
    {
        // Race control state machine: detects incidents across the field and drives
        // yellow flag / VSC / full safety car escalation, then de-escalates back to
        // green through a scripted restart. Skipped entirely in qualifying/time
        // trial where there is no field-wide incident model.
        void UpdateRaceControl()
        {
            if (Track == null || CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial || State == null)
            {
                return;
            }

            drsRestartCooldownTimer = Mathf.Max(0f, drsRestartCooldownTimer - Time.deltaTime);
            postEscalationCooldownTimer = Mathf.Max(0f, postEscalationCooldownTimer - Time.deltaTime);
            redFlagCooldownTimer = Mathf.Max(0f, redFlagCooldownTimer - Time.deltaTime);

            if (yellowSectorNumber >= 0)
            {
                yellowSectorClearTimer -= Time.deltaTime;
                if (yellowSectorClearTimer <= 0f)
                {
                    yellowSectorNumber = -1;
                    YellowFlagSector = -1;
                    if (CurrentRaceControlState == RaceControlState.YellowSector)
                    {
                        CurrentRaceControlState = RaceControlState.Green;
                    }
                }
            }

            raceControlCheckTimer -= Time.deltaTime;
            if (raceControlCheckTimer <= 0f)
            {
                raceControlCheckTimer = RaceControlCheckInterval;
                DetectIncidents();
            }

            DriveRaceControlStateMachine();
            UpdateSafetyCar();
            ApplyRaceControlSpeedCaps();
            UpdateBlueFlags();
            if (Track != null)
            {
                // Safe to call every tick - TrackRuntime.SetRaceControlVisual no-ops
                // internally when the state hasn't actually changed.
                Track.SetRaceControlVisual((int)CurrentRaceControlState);
            }
        }

        // Scans every active participant for the incident types the brief calls
        // out - collisions, stopped/stranded cars, wrong-way, severe damage and
        // rare mechanical failure - and classifies anything found into a severity
        // that ApplyIncidentSeverity can turn into a yellow/VSC/SC escalation.
        void DetectIncidents()
        {
            int freqSetting = Settings == null ? 2 : Mathf.Clamp(Settings.Current.safetyCarFrequency, 0, 3);
            bool escalationAllowed = freqSetting > 0;
            // Retuned (Part 3): cut roughly in half again from the previous
            // 0/0.12/0.26/0.55 scale. Off still fully disables VSC/SC
            // escalation; Reduced is now "almost never", Standard is "rare",
            // High is "occasional but never chaotic" - a short prototype race
            // should usually run green start to finish unless something
            // genuinely serious happens.
            // Incident odds reduction (per request): freqScale is the single
            // factor every yellow/VSC/SC escalation chance below multiplies
            // by, so scaling it here reduces the whole family of race-control
            // interruptions uniformly. Now 0.063 - the compounded 0.126 with
            // another -50% on top (0.126 * 0.5), taking the standard setting's
            // base 0.13 down to an effective 0.008. Interruptions are now rare.
            float freqScale = (freqSetting == 0 ? 0f : (freqSetting == 1 ? 0.06f : (freqSetting == 3 ? 0.28f : 0.13f))) * 0.063f;
            bool preRace = StartCountdown > 0f;
            int mechanicalMode = Settings == null ? 2 : Settings.Current.mechanicalFailureMode;

            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null || participant.vehicle == null || participant.retired || participant.finished)
                {
                    continue;
                }

                float speedKph = Mathf.Abs(participant.vehicle.CurrentSpeedKph);
                float damagePercent = participant.vehicle.Damage == null ? 0f : participant.vehicle.Damage.OverallPercent;
                bool inPitPhaseOrPitting = participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit;
                // Computed early (Part 3) so it can gate collision detection too,
                // not just the stranded/wrong-way checks further down - a car
                // race control is actively pacing/driving under SC/VSC must be
                // fully exempt from incident detection, not just from a subset
                // of the checks.
                bool paceLimited = IsRaceControlPaceLimited ||
                    CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                    CurrentRaceControlState == RaceControlState.Restart ||
                    participant.isRaceControlAutopilot;

                // Bug fix (Part B.1): the previous version required BOTH a >25kph
                // speed-drop AND a >3-point damage-jump landing in the exact same
                // 0.35s poll tick, comparing only against the immediately previous
                // tick. A real crash's speed loss and damage registration can easily
                // straddle two poll windows, a hard hit that already-slow car takes
                // might never show a full 25kph drop, and a spin that scrubs speed
                // without wall contact produces no damage at all - the AND-gate on a
                // single-tick comparison could miss all three. Now either signal
                // alone (OR, not AND) over a rolling ~1s window (peak speed / lowest
                // damage across the last three ticks, not just the last one) is
                // enough to register a collision-class incident.
                float recentPeakSpeed = Mathf.Max(participant.previousSpeedKphForIncident,
                    Mathf.Max(participant.incidentSpeedHistory0, Mathf.Max(participant.incidentSpeedHistory1, participant.incidentSpeedHistory2)));
                float recentMinDamage = Mathf.Min(participant.previousDamagePercentForIncident,
                    Mathf.Min(participant.incidentDamageHistory0, Mathf.Min(participant.incidentDamageHistory1, participant.incidentDamageHistory2)));
                participant.incidentSpeedHistory2 = participant.incidentSpeedHistory1;
                participant.incidentSpeedHistory1 = participant.incidentSpeedHistory0;
                participant.incidentSpeedHistory0 = participant.previousSpeedKphForIncident;
                participant.incidentDamageHistory2 = participant.incidentDamageHistory1;
                participant.incidentDamageHistory1 = participant.incidentDamageHistory0;
                participant.incidentDamageHistory0 = participant.previousDamagePercentForIncident;

                float speedDrop = recentPeakSpeed - speedKph;
                float damageJump = damagePercent - recentMinDamage;
                // speedSignal must NOT fire on ordinary hard braking: a normal
                // braking zone easily loses 40+ kph within this ~1s rolling window
                // (well past the old 15kph bar), which would otherwise flag every
                // hairpin on every lap as a "collision". Require the speed loss to
                // be largely unexplained by the driver's own brake input - a real
                // spin/impact loses speed involuntarily, intentional braking doesn't
                // count here (a hit that happens mid-braking is still caught by
                // damageSignal below, which has no such gate).
                // Part 3 retune: raised again (was 20) - collision-class detection
                // needs a bigger, clearly-involuntary speed loss before it counts,
                // so ordinary AI bunching/braking/wall brushes stay well clear.
                bool speedSignal = speedDrop > 26f && participant.vehicle.EffectiveBrake < 0.3f;
                // Part 3 retune: raised from 6 - only a real hit registers, not a
                // graze.
                bool damageSignal = damageJump > 9f;
                // Part 3: a car under SC/VSC pacing or race-control autopilot is
                // fully exempt from collision detection - its speed/pace is race
                // control's own doing, never a driver incident.
                bool collision = !preRace && !inPitPhaseOrPitting && !paceLimited && (speedSignal || damageSignal);
                participant.previousSpeedKphForIncident = speedKph;
                participant.previousDamagePercentForIncident = damagePercent;
                if (collision)
                {
                    // A spin/contact event earns a grace window before the stranded
                    // timer can ever accumulate again - the car gets a real chance to
                    // gather itself and drive away before race control considers it.
                    participant.recoveryGraceTimer = RecoveryGraceSeconds;
                }

                TrackProgress progress = State.GetCurrentProgress(participant);
                float facingDot = Vector3.Dot(participant.transform.forward, progress.forward);

                // Recovery-state classification (Part 2): a naive "speed < 8kph for a
                // few seconds" check flagged every ordinary spin, brief blockage or
                // SC-pace crawl as "stranded", which was the actual root cause of
                // race control escalating far too often - not the escalation-chance
                // math itself. Only a car that is nearly stationary for a sustained
                // duration AND excluded from every legitimate reason to be slow can
                // ever reach ActuallyStranded; everything else is Recovering/Queued/
                // PitSequence/RaceControlPacing and never registers an incident.
                bool offTrackNow = Mathf.Abs(progress.lateralDistance) > LocalHalfWidthAt(progress.distance) + 1.5f;
                // A car off track but still aimed roughly the right way (not spun
                // fully backward) and crawling is actively working its way back,
                // not stuck - this is the single biggest source of the old false
                // positives, since running wide through gravel routinely dips under
                // 10kph for a couple of seconds while genuinely recovering.
                bool pointedTowardTrack = facingDot > -0.15f;
                bool creepingWithPurpose = speedKph > 3.5f && pointedTowardTrack;

                // Directly behind a car that is itself slow (SC bunching, a first-lap
                // squeeze, a queue at a re-join point) means this car is queued in
                // traffic, not stuck on its own - checked against on-track cars only,
                // since a car buried in the gravel isn't "queued" behind anyone.
                RaceParticipant blockerAhead = !offTrackNow ? FindCarAhead(participant, 9f) : null;
                // Root-cause fix (why the 3s crawl rule can fire at all): this
                // used to treat ANY car-ahead under 30 kph as legitimate
                // "queuing", INCLUDING a fully stopped one. A car sitting
                // nose-to-tail behind a genuinely dead car was therefore
                // mislabeled Queued forever - excluded from stoppedOnTrackTimer
                // (so never ActuallyStranded) and NOT eligible for the AI
                // reverse-out recovery maneuver (which only fires for Recovering/
                // ActuallyStranded) - so it just sat there crawling until the
                // blunt crawl rule teleported it. Real queuing needs the blocker
                // to actually be MOVING (>8 kph): a stopped blocker means both
                // cars are stuck, not queuing, so this car falls through to the
                // Recovering classification and gets the reverse maneuver.
                bool queuedBehindTraffic = blockerAhead != null && blockerAhead.vehicle != null &&
                    Mathf.Abs(blockerAhead.vehicle.CurrentSpeedKph) > 8f &&
                    Mathf.Abs(blockerAhead.vehicle.CurrentSpeedKph) < 30f && speedKph < 30f;

                bool recoveryGraceActive = participant.recoveryGraceTimer > 0f;
                participant.recoveryGraceTimer = Mathf.Max(0f, participant.recoveryGraceTimer - RaceControlCheckInterval);

                bool nearStationary = speedKph < 8f;
                // paceLimited already covers the whole full-SC/VSC period field-
                // wide, but the explicit isRaceControlAutopilot check is kept too
                // as a direct guard - a car race control is actively driving
                // itself must never be declared stranded regardless of exactly
                // which race-control-state check caught it.
                bool strandedExcluded = preRace || inPitPhaseOrPitting || paceLimited || queuedBehindTraffic ||
                    creepingWithPurpose || recoveryGraceActive || participant.isRaceControlAutopilot;
                bool stoppedCandidate = nearStationary && !strandedExcluded;
                participant.stoppedOnTrackTimer = stoppedCandidate ? participant.stoppedOnTrackTimer + RaceControlCheckInterval : 0f;

                // Crash recovery (per request - a car should NEVER be crawling
                // for a sustained window; if it is, it crashed). Under green
                // racing an AI at or under 10 kph is not slow, it is stopped -
                // no corner on any layout is taken below ~75 kph, so this band
                // means a wall/car impact. The reverse-out unstick
                // (AiVehicleController) starts freeing it at 0.8s; anything
                // still stopped at 1s the unstick could not free is genuinely
                // wedged, so it gets the hold-R action immediately (snapped to
                // the road centre at its current distance, facing forward, lap
                // invalidated) rather than lingering. 1s is the minimum
                // window that still distinguishes a crash from a hard brake
                // into the slowest hairpin. Excludes the player (R key),
                // pre-race, the launch window (first 10s), pit sequences and
                // SC/VSC pacing (legitimately slow).
                bool crashStopped = !participant.isPlayer && speedKph <= 10f &&
                    !preRace && RaceElapsed > 10f && !inPitPhaseOrPitting && !paceLimited && !participant.isRaceControlAutopilot;
                participant.slowCrawlRetireTimer = crashStopped ? participant.slowCrawlRetireTimer + RaceControlCheckInterval : 0f;
                if (participant.slowCrawlRetireTimer > 1f)
                {
                    participant.slowCrawlRetireTimer = 0f;
                    ResetParticipantToTrackCenter(participant, 130f);
                    GameLog.Info("[RaceControl] " + participant.driverName + " crash-recovered to the track centerline.");
                }

                // Debug: a car that would have tripped the old blunt check (slow,
                // not pre-race/pitting) but is excused by one of the new reasons -
                // logged once per continuous slow episode, not every 0.35s tick.
                if (nearStationary && !preRace && !inPitPhaseOrPitting && strandedExcluded)
                {
                    if (!participant.falseStrandedLogged)
                    {
                        participant.falseStrandedLogged = true;
                        string reason = paceLimited ? "race-control pace" : (queuedBehindTraffic ? "queued behind traffic" : (creepingWithPurpose ? "creeping back with purpose" : "recovery grace period"));
                        GameLog.Info("[RaceControl] Ignoring false stranded case for " + participant.driverName + ": " + reason + " (speed=" + speedKph.ToString("0") + "kph)");
                    }
                }
                else if (!nearStationary)
                {
                    participant.falseStrandedLogged = false;
                }

                CarRecoveryState previousRecoveryState = participant.recoveryState;
                CarRecoveryState newRecoveryState;
                if (inPitPhaseOrPitting)
                {
                    newRecoveryState = CarRecoveryState.PitSequence;
                }
                else if (paceLimited)
                {
                    newRecoveryState = CarRecoveryState.RaceControlPacing;
                }
                else if (queuedBehindTraffic)
                {
                    newRecoveryState = CarRecoveryState.Queued;
                }
                else if (participant.stoppedOnTrackTimer > StrandedDeclareSeconds)
                {
                    newRecoveryState = CarRecoveryState.ActuallyStranded;
                }
                else if (nearStationary || recoveryGraceActive || offTrackNow)
                {
                    newRecoveryState = CarRecoveryState.Recovering;
                }
                else
                {
                    newRecoveryState = CarRecoveryState.Normal;
                }

                participant.recoveryState = newRecoveryState;
                if (newRecoveryState != previousRecoveryState)
                {
                    if (newRecoveryState == CarRecoveryState.Recovering)
                    {
                        GameLog.Info("[RaceControl] " + participant.driverName + " entering recovery (speed=" + speedKph.ToString("0") + "kph, offTrack=" + offTrackNow + ").");
                    }
                    else if (newRecoveryState == CarRecoveryState.Queued)
                    {
                        GameLog.Info("[RaceControl] " + participant.driverName + " considered queued behind traffic, not stranded.");
                    }
                    else if (newRecoveryState == CarRecoveryState.ActuallyStranded)
                    {
                        GameLog.Info("[RaceControl] " + participant.driverName + " declared ActuallyStranded: stoppedTimer=" + participant.stoppedOnTrackTimer.ToString("0.0") +
                            "s offTrack=" + offTrackNow + " facingDot=" + facingDot.ToString("0.00"));
                    }
                }

                // Wrong-way is excluded by the same legitimate-reasons list, plus a
                // longer sustained duration (Part 2) so a car merely gathering itself
                // out of a spin - briefly pointed backward while it turns back around,
                // or already in the Recovering state - isn't flagged the instant it
                // starts rolling again.
                bool wrongWayCandidate = !preRace && !inPitPhaseOrPitting && !paceLimited && !recoveryGraceActive &&
                    newRecoveryState != CarRecoveryState.Recovering && speedKph > 5f && facingDot < -0.35f;
                participant.wrongWayTimer = wrongWayCandidate ? participant.wrongWayTimer + RaceControlCheckInterval : 0f;

                participant.incidentCooldownTimer = Mathf.Max(0f, participant.incidentCooldownTimer - RaceControlCheckInterval);
                if (participant.incidentCooldownTimer > 0f)
                {
                    continue;
                }

                bool destroyed = participant.vehicle.Damage != null && participant.vehicle.Damage.IsDestroyed;
                bool blockingLine = Mathf.Abs(progress.lateralDistance) <= LocalHalfWidthAt(progress.distance) * 0.85f;
                // Wall/barrier-stuck fix: a car that stalls PINNED AGAINST THE
                // BARRIER sits right at (or just past) the true track edge, which
                // blockingLine's 85%-of-halfwidth test was deliberately designed to
                // exclude - fine for a car parked deep in a wide, harmless runoff,
                // but a car wedged against the wall is a very different case: with
                // barriers now flush to the paved edge (see TrackManager's
                // FlushBarrierLateral), that's still the outer edge of every other
                // car's racing line through the corner, not a safe verge. Only used
                // to widen the ActuallyStranded hazard check below - the collision-
                // severity thresholds above are untouched.
                bool pinnedAtEdge = Mathf.Abs(progress.lateralDistance) >= LocalHalfWidthAt(progress.distance) * 0.92f;

                if (destroyed)
                {
                    RetireParticipant(participant, "Collision damage");
                    // Truly catastrophic (a destroyed car actually blocking the racing
                    // line) bypasses the escalation roll entirely rather than risking
                    // "no response" to a car sitting dead in the road - still fully
                    // subject to escalationAllowed, so Off never escalates regardless.
                    // A destroyed car that ISN'T blocking the line is Minor and does
                    // NOT need a yellow flag - it is off the racing line, not a hazard.
                    RegisterIncident(participant, blockingLine ? IncidentSeverity.Major : IncidentSeverity.Minor, progress, freqScale, escalationAllowed, "Destroyed", blockingLine, false);
                    participant.incidentCooldownTimer = 30f;
                    continue;
                }

                if (collision)
                {
                    // Severity now reflects either signal, not damageJump alone - a
                    // hard speed-only spin (no wall contact) can still read Medium,
                    // and a big simultaneous hit is always at least Medium. Part 3
                    // retune: bars raised again so only genuinely hard hits reach
                    // Medium/Major - a light scrape stays Minor and, per the yellow
                    // policy below, does not flag race control at all.
                    // Part 4 retune: raised again (was 36/130 and 20/70) - yellows
                    // were still reading as too common, and this collision
                    // classification is the single biggest source of them (Major
                    // always flags with no roll at all). A normal wheel-to-wheel
                    // rub, a small spin, or a wall brush needs to stay Minor.
                    // Part 5 retune (~50% further cut): raised again (was 28/85 and
                    // 46/150) - a tiny wall brush, a quick spin that catches itself,
                    // or ordinary wheel-to-wheel contact must never reach even Medium,
                    // let alone Major. Only a genuinely hard, damaging shunt now
                    // qualifies.
                    IncidentSeverity severity;
                    if (damageJump > 58f || speedDrop > 185f)
                    {
                        severity = IncidentSeverity.Major;
                    }
                    else if (damageJump > 38f || speedDrop > 110f || (speedSignal && damageSignal))
                    {
                        severity = IncidentSeverity.Medium;
                    }
                    else
                    {
                        severity = IncidentSeverity.Minor;
                    }

                    // Part 3: Medium is no longer an automatic yellow either - a
                    // "medium-ish" scrape/spin that isn't actually blocking the
                    // racing line is logged for stats only, same as Minor. Major
                    // always flags (that tier is reserved for genuinely serious
                    // events); Minor never does regardless of position.
                    bool collisionYellowJustified = severity == IncidentSeverity.Medium && blockingLine;
                    // Incident-cleanup classification only (never read by any
                    // escalation decision above) - another car within a tight
                    // radius at the moment of detection reads as car-to-car
                    // contact; anything else (a solo spin, a wall/barrier
                    // brush, a kerb strike) is a solo contact.
                    bool carNearby = FindCarAhead(participant, 7f) != null || FindCarBehind(participant, 7f) != null;
                    if (carNearby)
                    {
                        CarContactIncidentCount++;
                    }
                    else
                    {
                        SoloContactIncidentCount++;
                    }

                    string collisionCause = (carNearby ? "Car contact" : "Solo contact/wall") + " (speedDrop=" + speedDrop.ToString("0") + " damageJump=" + damageJump.ToString("0.0") + ")";
                    RegisterIncident(participant, severity, progress, freqScale, escalationAllowed, collisionCause, false, collisionYellowJustified);
                    // Part 3: longer per-incident suppression (was 24s) so the same
                    // scrape/spin can't repeatedly re-roll an escalation chance.
                    participant.incidentCooldownTimer = 30f;
                    continue;
                }

                if (damagePercent >= 85f)
                {
                    RegisterIncident(participant, IncidentSeverity.Major, progress, freqScale, escalationAllowed, "Severe damage");
                    participant.incidentCooldownTimer = 38f;
                    continue;
                }

                // Part 2: only the ActuallyStranded classification above (which already
                // required the sustained StrandedDeclareSeconds duration with every
                // legitimate exclusion checked) can ever reach race control - no
                // separate, laxer off-track-only path any more. Minor (not blocking
                // the line) never raises a yellow - only a stranded car sitting in
                // or very near the racing line is an actual hazard, which is exactly
                // when this is Medium rather than Minor, so blockingLine alone is the
                // correct yellow-justification signal here.
                // Wall/barrier-stuck fix: a car pinned against the barrier (see
                // pinnedAtEdge above) is just as much a hazard as one sitting on the
                // racing line - this is exactly the "player crashes into a wall and
                // gets stuck" case, previously undervalued as Minor (no yellow) purely
                // because it sits outside the racing-line band, never because of who
                // (or what) is driving the car.
                bool strandedIsHazard = blockingLine || pinnedAtEdge;
                if (newRecoveryState == CarRecoveryState.ActuallyStranded)
                {
                    if (participant.stoppedOnTrackTimer > StrandedRetireSeconds)
                    {
                        RetireParticipant(participant, "Stranded");
                    }

                    string strandedCause = pinnedAtEdge && !blockingLine ? "Stuck against barrier/wall" : "Stopped/stranded";
                    RegisterIncident(participant, strandedIsHazard ? IncidentSeverity.Medium : IncidentSeverity.Minor, progress, freqScale, escalationAllowed, strandedCause, false, strandedIsHazard);
                    // Part 3: longer per-incident suppression so a car still sitting in
                    // the same stranded episode doesn't re-register (and re-roll a VSC/
                    // SC chance) every few seconds while race control already knows.
                    participant.incidentCooldownTimer = 38f;
                    continue;
                }

                // Part 3 retune: longer sustained duration (was 7s) so a car briefly
                // pointed backward while gathering itself out of a spin - already
                // excluded above while recoveryGraceActive/Recovering - isn't flagged
                // the moment it lapses if it's still slowly correcting.
                if (participant.wrongWayTimer > 10f)
                {
                    // A wrong-way car only warrants a yellow if it is actually near
                    // other traffic - alone on an empty stretch of track it is not
                    // yet a hazard to anyone, even though it still counts as an
                    // incident and still retires/cools down normally.
                    bool nearTraffic = FindCarAhead(participant, 100f) != null || FindCarBehind(participant, 100f) != null;
                    RegisterIncident(participant, IncidentSeverity.Minor, progress, freqScale, escalationAllowed, "Wrong way", false, nearTraffic);
                    participant.incidentCooldownTimer = 28f;
                    continue;
                }

                // Rare mechanical failure spice, gated by the settings toggle and this
                // car's reliability stat. Kept deliberately small and further halved
                // (Part 2) - over a full race this should be a rare talking point, not
                // a routine occurrence, and a car under SC/VSC pacing is excluded
                // entirely since its pace is race control's own doing.
                bool mechanicalEligible = ReliabilityRules.IsEligible(mechanicalMode, participant.isPlayer, preRace, paceLimited);
                if (mechanicalEligible)
                {
                    float reliability = participant.carData == null ? ReliabilityRules.DefaultReliability : participant.carData.reliability;
                    if (ReliabilityRules.FailsThisCheck(reliability, RaceControlCheckInterval, Random.value))
                    {
                        RetireParticipant(participant, "Mechanical failure");
                        RegisterIncident(participant, blockingLine ? IncidentSeverity.Medium : IncidentSeverity.Minor, progress, freqScale, escalationAllowed, "Mechanical failure", false, blockingLine);
                        participant.incidentCooldownTimer = 48f;
                    }
                }
            }
        }

        // Groups incidents that land within a few seconds and a short stretch of
        // track into one escalated event (a simple pileup approximation) rather
        // than full physics collision-graph analysis.
        void RegisterIncident(RaceParticipant participant, IncidentSeverity severity, TrackProgress progress, float freqScale, bool escalationAllowed, string cause, bool forceEscalate = false, bool yellowJustified = false)
        {
            IncidentCount++;
            replayCapture.AddIncidentMarker(RaceElapsed, ReplayCarIndex(participant),
                string.IsNullOrEmpty(cause) ? "Incident" : cause);
            // Part 4 retune: tightened from 6s/40m - with ~20 cars on track,
            // two genuinely unrelated minor incidents landing within a loose
            // 6s/40m window purely by chance was common enough to keep
            // manufacturing guaranteed-yellow Major escalations out of two
            // ordinary scrapes that never actually interacted.
            bool pileup = (RaceElapsed - lastIncidentTime) < 4f && Mathf.Abs(Track.WrapDistance(progress.distance - lastIncidentDistance)) < 25f;
            lastIncidentTime = RaceElapsed;
            lastIncidentDistance = progress.distance;
            if (pileup && severity != IncidentSeverity.Major)
            {
                severity = severity == IncidentSeverity.Minor ? IncidentSeverity.Medium : IncidentSeverity.Major;
                // A pileup that escalated an incident's severity is, by
                // construction, no longer an isolated case - it now qualifies
                // for a yellow on its own merits.
                yellowJustified = true;
            }

            GameLog.Info("[RaceControl] Incident: " + (participant == null ? "?" : participant.driverName) +
                " cause=" + cause + " severity=" + severity + " sector=" + progress.sector + (pileup ? " (pileup-escalated)" : "") + (forceEscalate ? " (force-escalate)" : ""));

            // Red flag: considered on genuinely catastrophic incidents only
            // (a forced escalation - a destroyed car blocking the line - or a
            // Major incident that actually justified its own yellow, i.e. is
            // really blocking/dangerous rather than just logged for stats).
            // Deliberately checked before ApplyIncidentSeverity's own SC/VSC
            // rolls below so a red-flag-worthy incident is never also
            // double-escalated into a safety car in the same call.
            if (escalationAllowed && (forceEscalate || (severity == IncidentSeverity.Major && yellowJustified)))
            {
                ConsiderRedFlag(participant, forceEscalate, freqScale, progress.distance);
                if (CurrentRaceControlState == RaceControlState.RedFlagged)
                {
                    return;
                }
            }

            ApplyIncidentSeverity(participant, severity, progress, freqScale, escalationAllowed, cause, forceEscalate, yellowJustified);
        }

        // Extremely rare by design - red flags must read as a genuine outlier,
        // never "a slightly worse safety car". Two paths in, both deliberately
        // stingy:
        // - RedFlagMultiCarThreshold or more DISTINCT cars catastrophically
        //   down within a short rolling window AND genuinely clustered at the
        //   same point on track - a real pileup that has actually blocked the
        //   track, not two unrelated incidents on opposite sides of the lap.
        // - A single forced-escalation incident (today, only a destroyed car
        //   blocking the line) on its own very rarely rolls into a red flag
        //   instead of just a safety car - a small fraction of the already-
        //   stingy Major-incident SC chance, so it stays a true outlier.
        // A strict cooldown (RedFlagCooldownSeconds) additionally blocks this
        // whole method for a long time after any red flag, so the field can
        // never see more than one or two red flags in a session, and safety
        // cars remain meaningfully more common than red flags.
        void ConsiderRedFlag(RaceParticipant participant, bool forceEscalateCause, float freqScale, float incidentDistance)
        {
            if (participant == null || CurrentRaceControlState == RaceControlState.RedFlagged)
            {
                return;
            }

            if (redFlagCooldownTimer > 0f)
            {
                return;
            }

            for (int i = recentCatastrophicIncidentTimes.Count - 1; i >= 0; i--)
            {
                if (RaceElapsed - recentCatastrophicIncidentTimes[i] > CatastrophicIncidentWindowSeconds)
                {
                    recentCatastrophicIncidentTimes.RemoveAt(i);
                    recentCatastrophicIncidents.RemoveAt(i);
                    recentCatastrophicIncidentDistances.RemoveAt(i);
                }
            }

            if (!recentCatastrophicIncidents.Contains(participant))
            {
                recentCatastrophicIncidents.Add(participant);
                recentCatastrophicIncidentTimes.Add(RaceElapsed);
                recentCatastrophicIncidentDistances.Add(incidentDistance);
            }

            // Only count incidents genuinely bunched at the same spot on track -
            // a real pileup, not scattered incidents that merely happened
            // within the same rolling time window.
            List<RaceParticipant> clusteredDrivers = new List<RaceParticipant>();
            for (int i = 0; i < recentCatastrophicIncidentDistances.Count; i++)
            {
                float separation = Track == null ? 0f : Mathf.Abs(Track.WrapDistance(recentCatastrophicIncidentDistances[i] - incidentDistance));
                if (separation <= CatastrophicIncidentClusterRadiusMeters)
                {
                    clusteredDrivers.Add(recentCatastrophicIncidents[i]);
                }
            }

            if (clusteredDrivers.Count >= RedFlagMultiCarThreshold)
            {
                BeginRedFlag("Huge multi-car pileup - track completely blocked", clusteredDrivers);
                return;
            }

            if (forceEscalateCause)
            {
                float roll = Random.value;
                // Cut to roughly a fifth of the previous chance (was 0.05) -
                // a red flag off a single incident must stay a genuine
                // rarity, well over the required 75% reduction.
                float chance = Mathf.Clamp01(0.01f * freqScale);
                GameLog.Info("[RaceControl] Red flag consideration (single catastrophic incident): roll=" + roll.ToString("0.000") + " chance=" + chance.ToString("0.000"));
                if (roll < chance)
                {
                    BeginRedFlag("Catastrophic accident - car destroyed and blocking the racing line", new List<RaceParticipant> { participant });
                }
            }
        }

        void BeginRedFlag(string reason, List<RaceParticipant> involvedDrivers)
        {
            // A red flag must mean the accident was serious enough that
            // whoever caused it is out of the race - never a driver who just
            // carries on after the restart as if nothing happened.
            string involvedNames = "";
            int sector = 0;
            if (involvedDrivers != null)
            {
                for (int i = 0; i < involvedDrivers.Count; i++)
                {
                    RaceParticipant involved = involvedDrivers[i];
                    if (involved == null)
                    {
                        continue;
                    }

                    if (sector == 0 && State != null)
                    {
                        sector = State.GetCurrentProgress(involved).sector;
                    }

                    involvedNames += (involvedNames.Length > 0 ? (i == involvedDrivers.Count - 1 ? " and " : ", ") : "") + involved.driverName;
                    RetireParticipant(involved, "Red flag - race-ending accident");
                }
            }

            string locationText = sector > 0 ? " in Sector " + sector : "";
            string fullReason = string.IsNullOrEmpty(involvedNames) ? reason : reason + " - " + involvedNames + locationText;

            CurrentRaceControlState = RaceControlState.RedFlagged;
            RedFlagCount++;
            RedFlagReason = fullReason;
            // Simple, fixed procedure: hold for exactly 5 seconds while every
            // car is neutralized in place, then the field is teleported back
            // to a grid built from the running order frozen right now (see
            // below) - never a random long "repair window" and never the
            // original race-start grid.
            redFlagTimer = RedFlagHoldSeconds;
            IsPitLaneOpen = true;
            recentCatastrophicIncidents.Clear();
            recentCatastrophicIncidentTimes.Clear();
            recentCatastrophicIncidentDistances.Clear();
            // Arms the strict cooldown immediately (not on clear) so back-to-
            // back catastrophic incidents in the same session still can't
            // chain into a second red flag before this one has even resolved.
            redFlagCooldownTimer = RedFlagCooldownSeconds;
            redFlagGridTeleportDone = false;

            // Freeze the running order at the exact moment of the flag - the
            // authoritative source for the post-red-flag grid (redFlagGridOrder,
            // used verbatim by TeleportFieldToRedFlagGrid - never re-sorted or
            // randomized) and a plain read-only snapshot for the post-race
            // report/race-control history.
            redFlagRunningOrderSnapshot.Clear();
            redFlagGridOrder.Clear();
            List<RaceParticipant> order = GetRunningOrderSnapshot();
            for (int i = 0; i < order.Count; i++)
            {
                RaceParticipant queued = order[i];
                if (queued == null)
                {
                    continue;
                }

                queued.safetyCarQueueIndex = i;
                queued.preSafetyCarOrderIndex = i;
                redFlagRunningOrderSnapshot.Add((i + 1) + ". " + queued.driverName);
                if (!queued.retired && !queued.finished)
                {
                    queued.isRaceControlAutopilot = true;
                    redFlagGridOrder.Add(queued);
                }
            }

            GameLog.Warn("[RaceControl] RED FLAG: " + fullReason + " (deployment #" + RedFlagCount + ")");
            LogRaceControlHistory("RED FLAG", fullReason);
            if (Settings != null && Settings.Current.raceControlMessages)
            {
                PostEngineerMessage("Red flag, red flag! Race suspended - " + fullReason + ".", true, RaceAudioCue.RedFlag);
                if (!string.IsNullOrEmpty(involvedNames))
                {
                    PostEngineerMessage(involvedNames + " will not be continuing - car retired on the spot.", true);
                }

                PostEngineerMessage("Hold position and bring the car to a safe stop. Restart in 5 seconds.", true);
            }
        }

        // Race-start yellow-flag dampener: the opening-lap scramble (grid still
        // bunched, cars still finding their braking points) produces far more
        // incidents than mid-race running, which read as "there's a yellow flag
        // basically every start" and disrupted the starting procedure itself.
        // Round 2: a 75% cut still let one in four opening-lap incidents through,
        // which was still enough to interrupt starts regularly - now fully
        // suppressed (100%) for the whole window, and the window itself widened
        // (30s -> 45s) to actually cover a full opening lap rather than cutting
        // out right as the pack is still sorting itself out through the first
        // sequence of corners. This never touches the underlying collision/
        // incident detection - a genuinely catastrophic, track-blocking incident
        // (forceEscalate) is still never suppressed, regardless of timing.
        const float RaceStartYellowGraceSeconds = 45f;
        const float RaceStartYellowSuppressionChance = 1f;

        void ApplyIncidentSeverity(RaceParticipant participant, IncidentSeverity severity, TrackProgress progress, float freqScale, bool escalationAllowed, string cause = null, bool forceEscalate = false, bool yellowJustified = false)
        {
            bool duringRaceStartWindow = CurrentSession != RaceWeekendSession.Qualifying && RaceElapsed < RaceStartYellowGraceSeconds;
            if (duringRaceStartWindow && !forceEscalate && (RaceStartYellowSuppressionChance >= 1f || Random.value < RaceStartYellowSuppressionChance))
            {
                GameLog.Info("[RaceControl] " + severity + " incident yellow/escalation suppressed by the race-start grace window (" + RaceElapsed.ToString("0.0") + "s in).");
                return;
            }

            // Part 3: only Major incidents always raise the local sector yellow
            // regardless of the safety-car frequency setting (Off only disables
            // VSC/SC escalation, not flags entirely) - that tier is reserved
            // for genuinely serious events. Medium AND Minor incidents only
            // ever raise a yellow when the call site has judged them still
            // genuinely dangerous (blocking the line, near traffic, etc) AND
            // the global minor-incident cooldown has lapsed - most incidents
            // reaching this point are simply logged for stats with no
            // race-control flag at all, so a run of ordinary scrapes/spins/
            // bunching never reads as a constant background of yellow flags.
            if (severity == IncidentSeverity.Major)
            {
                TriggerYellowSector(progress.sector, participant, cause);
            }
            else if (yellowJustified)
            {
                if (RaceElapsed >= globalMinorYellowCooldownUntil)
                {
                    TriggerYellowSector(progress.sector, participant, cause);
                    globalMinorYellowCooldownUntil = RaceElapsed + GlobalMinorYellowCooldownSeconds;
                }
                else
                {
                    GameLog.Info("[RaceControl] " + severity + " incident yellow suppressed by global minor-yellow cooldown (" +
                        (globalMinorYellowCooldownUntil - RaceElapsed).ToString("0.0") + "s remaining).");
                }
            }
            else
            {
                GameLog.Info("[RaceControl] " + severity + " incident logged without a race-control flag (not near the racing line / not significant).");
            }

            bool alreadyEscalated = CurrentRaceControlState == RaceControlState.SafetyCarDeploying ||
                                     CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                                     CurrentRaceControlState == RaceControlState.SafetyCarInThisLap ||
                                     CurrentRaceControlState == RaceControlState.Restart;
            if (!escalationAllowed || alreadyEscalated || severity == IncidentSeverity.Minor)
            {
                return;
            }

            // Truly catastrophic cases (a destroyed car actually blocking the racing
            // line) skip the roll and the post-escalation cooldown entirely - every
            // other case still respects both, so one incident can't chain into
            // repeated SC/VSC periods.
            if (forceEscalate)
            {
                GameLog.Info("[RaceControl] Forced escalation (catastrophic, blocking): deploying safety car.");
                BeginSafetyCarDeployment(participant, progress.sector);
                return;
            }

            if (postEscalationCooldownTimer > 0f)
            {
                GameLog.Info("[RaceControl] Escalation suppressed: post-escalation cooldown active (" + postEscalationCooldownTimer.ToString("0.0") + "s remaining).");
                return;
            }

            // Part 3: a Medium incident that isn't even yellow-justified (not
            // blocking the line / no nearby traffic) is not a real hazard to
            // anyone - it never gets a VSC roll at all, only a genuinely
            // positioned Medium incident does.
            if (severity == IncidentSeverity.Medium && !yellowJustified)
            {
                GameLog.Info("[RaceControl] Medium incident not blocking the line - no VSC roll.");
                return;
            }

            // Part 3 (fifth retune): cut again from 0.15/0.20/0.28 - with false
            // positives fixed and thresholds raised upstream, incidents reaching
            // this point are essentially all genuine, so the roll itself can be
            // much stingier and still produce a meaningful, rare event. VSC
            // should read as "uncommon", full SC as "rare and meaningful" - a
            // short prototype race should usually run green throughout.
            if (severity == IncidentSeverity.Medium)
            {
                if (CurrentRaceControlState != RaceControlState.VirtualSafetyCar)
                {
                    float roll = Random.value;
                    float chance = Mathf.Clamp01(0.08f * freqScale);
                    bool escalate = roll < chance;
                    GameLog.Info("[RaceControl] Medium incident escalation: roll=" + roll.ToString("0.00") + " chance=" + chance.ToString("0.00") + " result=" + (escalate ? "VSC deployed" : "no escalation"));
                    if (escalate)
                    {
                        BeginVirtualSafetyCar(participant, progress.sector);
                    }
                }

                return;
            }

            // Major.
            float scRoll = Random.value;
            float scChance = Mathf.Clamp01(0.10f * freqScale);
            bool deploySc = scRoll < scChance;
            if (deploySc)
            {
                GameLog.Info("[RaceControl] Major incident escalation: roll=" + scRoll.ToString("0.00") + " chance=" + scChance.ToString("0.00") + " result=Safety car deployed");
                BeginSafetyCarDeployment(participant, progress.sector);
                return;
            }

            // A failed full-SC roll no longer defaults deterministically to VSC -
            // it gets its own (separate, still freqScale-scaled) fallback chance, so
            // a Major incident can also resolve to "yellow only" rather than always
            // producing at least a VSC.
            if (CurrentRaceControlState != RaceControlState.VirtualSafetyCar)
            {
                float vscFallbackRoll = Random.value;
                float vscFallbackChance = Mathf.Clamp01(0.14f * freqScale);
                bool vscFallback = vscFallbackRoll < vscFallbackChance;
                GameLog.Info("[RaceControl] Major incident escalation: scRoll=" + scRoll.ToString("0.00") + " scChance=" + scChance.ToString("0.00") +
                    " (no SC) vscFallbackRoll=" + vscFallbackRoll.ToString("0.00") + " vscFallbackChance=" + vscFallbackChance.ToString("0.00") +
                    " result=" + (vscFallback ? "VSC deployed" : "no escalation, yellow only"));
                if (vscFallback)
                {
                    BeginVirtualSafetyCar(participant, progress.sector);
                }
            }
        }

        int yellowSectorNumber = -1;
        // Hard per-race cap on brand-new yellow episodes (per request: at most
        // ~1 a race). Refreshing an already-active episode in its own sector
        // does not count; only genuinely new yellows do. Reset at session start.
        int yellowEpisodesThisRace;
        const int MaxYellowEpisodesPerRace = 1;

        void TriggerYellowSector(int sector, RaceParticipant involved = null, string cause = null)
        {
            bool sameActiveSector = yellowSectorNumber == sector && yellowSectorClearTimer > 0f;

            // Hard cap: once this race has used its allotment of new yellows,
            // no further NEW yellow ever comes out (an active episode can still
            // refresh via the sameActiveSector path below). Safety cars / VSC
            // are separate and unaffected.
            if (!sameActiveSector && yellowEpisodesThisRace >= MaxYellowEpisodesPerRace)
            {
                return;
            }

            // Part 2: don't refresh the same persistent hazard's yellow forever -
            // once an episode has run for MaxYellowEpisodeSeconds, let it clear
            // naturally even if the underlying incident keeps re-registering
            // (by then the stranded-retire / cooldown logic elsewhere has
            // almost always already resolved it anyway).
            if (sameActiveSector && RaceElapsed - yellowSectorEpisodeStartTime > MaxYellowEpisodeSeconds)
            {
                return;
            }

            if (!sameActiveSector)
            {
                float cooldownUntil;
                if (yellowSectorCooldownUntil.TryGetValue(sector, out cooldownUntil) && RaceElapsed < cooldownUntil)
                {
                    GameLog.Info("[RaceControl] Yellow flag suppressed for sector " + sector + " - per-sector cooldown active (" +
                        (cooldownUntil - RaceElapsed).ToString("0.0") + "s remaining).");
                    return;
                }

                // Part 4 retune: a per-sector cooldown alone let a fresh yellow
                // fire in sector 2 the instant sector 1's own cooldown was
                // still running, reading as one continuous stream of flags
                // across the lap even though any one sector was individually
                // "cooling down". This global gate blocks any BRAND NEW yellow
                // (in any sector) for a while after the last one, without
                // touching an already-active episode's own ability to refresh
                // in its own sector (the sameActiveSector branch above).
                if (RaceElapsed < globalYellowFlagCooldownUntil)
                {
                    GameLog.Info("[RaceControl] Yellow flag suppressed - global cooldown active (" +
                        (globalYellowFlagCooldownUntil - RaceElapsed).ToString("0.0") + "s remaining).");
                    return;
                }

                yellowSectorEpisodeStartTime = RaceElapsed;
                yellowEpisodesThisRace++;
            }

            globalYellowFlagCooldownUntil = RaceElapsed + GlobalYellowFlagCooldownSeconds;

            if (CurrentRaceControlState == RaceControlState.Green)
            {
                CurrentRaceControlState = RaceControlState.YellowSector;
            }

            bool freshFlag = yellowSectorNumber != sector || yellowSectorClearTimer <= 0f;
            yellowSectorNumber = sector;
            YellowFlagSector = sector;
            // Part 2: shorter default duration (was 10s) - a yellow reads as a
            // brief, localized warning unless the hazard keeps re-registering,
            // which still refreshes this timer (up to the episode cap above).
            yellowSectorClearTimer = 7f;
            yellowSectorCooldownUntil[sector] = RaceElapsed + yellowSectorClearTimer + YellowSectorCooldownAfterClearSeconds;
            // Radio clarity: declares WHO triggered the flag, roughly WHERE
            // (sector), and now WHY (RadioCausePhraseFor) instead of a bare
            // "yellow flag, sector N" or a generic "has gone off" regardless of
            // what actually happened - matches what a real broadcast/race-
            // engineer callout would say. The player is addressed directly
            // ("your car") rather than by name, same convention every other
            // player-facing race-control message in this file already uses.
            string involvedText = involved != null
                ? " - " + (involved.isPlayer ? "your car" : involved.driverName) + " " + RadioCausePhraseFor(cause)
                : "";
            if (freshFlag)
            {
                GameLog.Info("[RaceControl] Yellow flag, sector " + sector + involvedText + ".");
                LogRaceControlHistory("YELLOW FLAG", "Sector " + sector + involvedText);
                if (Settings != null && Settings.Current.raceControlMessages)
                {
                    PostEngineerMessage("Yellow flag, sector " + sector + involvedText + ".", false, RaceAudioCue.Yellow);
                }
            }
        }

        // Turns RegisterIncident's internal, diagnostic cause string (which can
        // include raw numbers, e.g. "Collision (speedDrop=45 damageJump=12.3)")
        // into a short, spoken-radio-appropriate phrase. Falls back to a generic
        // phrase for anything unrecognized rather than ever reading the raw
        // diagnostic text over the radio.
        static string RadioCausePhraseFor(string cause)
        {
            if (string.IsNullOrEmpty(cause))
            {
                return "in trouble";
            }

            if (cause.StartsWith("Stuck against barrier"))
            {
                return "stuck against the barrier";
            }

            if (cause.StartsWith("Stopped/stranded"))
            {
                return "stranded";
            }

            if (cause.StartsWith("Wrong way"))
            {
                return "facing the wrong way";
            }

            if (cause.StartsWith("Destroyed"))
            {
                return "in a heavy crash";
            }

            if (cause.StartsWith("Severe damage"))
            {
                return "stopped with severe damage";
            }

            if (cause.StartsWith("Mechanical failure"))
            {
                return "stopped with a mechanical failure";
            }

            if (cause.StartsWith("Collision") || cause.StartsWith("Car contact") || cause.StartsWith("Solo contact"))
            {
                return "involved in an incident";
            }

            return "in trouble";
        }
        void ResetRaceControlState()
        {
            CurrentRaceControlState = RaceControlState.Green;
            SafetyCarTargetSpeedKph = 150f;
            IsPitLaneOpen = true;
            YellowFlagSector = -1;
            IncidentCount = 0;
            CarContactIncidentCount = 0;
            SoloContactIncidentCount = 0;
            SafetyCarDeploymentCount = 0;
            AiOvertakesCompletedCount = 0;
            RedFlagCount = 0;
            RedFlagReason = "";
            RestartFollowsRedFlag = false;
            redFlagTimer = 0f;
            redFlagCooldownTimer = 0f;
            redFlagGridTeleportDone = false;
            redFlagRunningOrderSnapshot.Clear();
            redFlagGridOrder.Clear();
            recentCatastrophicIncidents.Clear();
            recentCatastrophicIncidentTimes.Clear();
            recentCatastrophicIncidentDistances.Clear();
            raceControlHistory.Clear();
            raceControlCheckTimer = 0f;
            safetyCarTimer = 0f;
            restartControlTimer = 0f;
            safetyCarInThisLapMessageSent = false;
            coldTyresRestartWarningSent = false;
            playerScPitPromptSent = false;
            playerHasActiveRaceControlPitOffer = false;
            playerDeclinedRaceControlPitOfferMessageSent = false;
            yellowSectorClearTimer = 0f;
            yellowEpisodesThisRace = 0;
            yellowSectorCooldownUntil.Clear();
            globalMinorYellowCooldownUntil = 0f;
            globalYellowFlagCooldownUntil = 0f;
            yellowSectorEpisodeStartTime = -999f;
            drsRestartCooldownTimer = 0f;
            safetyCarQueueLeader = null;
            lastIncidentTime = -999f;
            lastIncidentDistance = -99999f;
            raceControlReferenceDistance = 0f;
            raceControlReferenceSpeedKph = 0f;
            restartRampTimer = 0f;
            restartHandbackMessageSent = false;
            // The previous session's safety car object (if any) lived under the
            // old raceWorld root and is already gone by the time a new session
            // resets this state - null the references so EnsureSafetyCarBuilt
            // rebuilds a fresh one under the new raceWorld instead of touching a
            // destroyed object.
            safetyCarObject = null;
            safetyCarController = null;
            aheadOfSafetyCarLastTick.Clear();
            raceControlOrderSnapshot.Clear();
            illegalOvertakePairCooldowns.Clear();
            restrictionActiveAtLastSnapshot = false;
            orderSnapshotTimer = 0f;
            safetyCarWatchdogTimer = 0f;
            safetyCarWatchdogRespawnCount = 0;
            if (State != null)
            {
                for (int i = 0; i < State.Participants.Count; i++)
                {
                    RaceParticipant participant = State.Participants[i];
                    if (participant == null)
                    {
                        continue;
                    }

                    participant.stoppedOnTrackTimer = 0f;
                    participant.wrongWayTimer = 0f;
                    participant.incidentCooldownTimer = 0f;
                    participant.previousSpeedKphForIncident = 0f;
                    participant.previousDamagePercentForIncident = 0f;
                    participant.incidentSpeedHistory0 = 0f;
                    participant.incidentSpeedHistory1 = 0f;
                    participant.incidentSpeedHistory2 = 0f;
                    participant.incidentDamageHistory0 = 0f;
                    participant.incidentDamageHistory1 = 0f;
                    participant.incidentDamageHistory2 = 0f;
                    participant.overtakesCompleted = 0;
                    participant.previousCarAheadForOvertakeCheck = null;
                }
            }
        }

    }
}
