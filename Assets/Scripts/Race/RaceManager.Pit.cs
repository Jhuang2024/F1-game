using System.Collections.Generic;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager pit-lane service subsystem (partial). Pit entry, the rail
    /// approach/coordination (blocker finding, lateral rail, release), the box
    /// service (BeginPitStop) and missed-entry handling. Split out of the
    /// monolith verbatim; timings, tyre windows and call order unchanged. The
    /// pure duration/queue rules already live in F1Game.Race.Rules; this partial
    /// owns the live pit-lane state machine.
    /// </summary>
    public partial class RaceManager
    {

        // Pit-system rebuild: the stuck watchdog is gone. The unified pit rail
        // (UpdatePitRail) advances a single monotonic parameter and places the
        // car AT the sampled pose every tick - there is no chase target that
        // can lag, no reprojection that can resolve onto the wrong segment,
        // and no phase handoff that can strand a car, so there is nothing
        // left for a watchdog to watch.

        void HandlePitService(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.lapTracker == null)
            {
                return;
            }

            // Pit-sequence collision immunity (per request: a car colliding
            // with you while pitting must never break the pit procedure - and
            // shouldn't be able to hit you at all). Deep pit-lane phases were
            // already immune (SetPitGuided goes kinematic with
            // detectCollisions off), but the vulnerable windows either side -
            // the physics-driven entry rail and the exit merge - were not: a
            // racing car clipping a pitting one there knocked it off the rail
            // and wrecked the whole stop. Car-to-car contact is now ignored
            // for the ENTIRE pit sequence (pitPhase != None or isPitting) via
            // pairwise Physics.IgnoreCollision - ground, kerbs and barriers
            // still collide normally, and the pairs are restored the moment
            // the car is fully back in the race.
            // Post-reset ghost (resetGhostTimer) shares the same pairwise
            // ignore mechanism: a freshly recovered car is intangible to other
            // cars until its window expires, exactly like a pitting one.
            SetCarToCarCollisionIgnored(participant, participant.pitPhase != PitPhase.None || participant.isPitting || participant.resetGhostTimer > 0f);

            UpdateMissedPitEntryReset(participant);

            TrackProgress currentProgress = State == null ? participant.lapTracker.CurrentProgress : State.GetCurrentProgress(participant);
            float normalized = currentProgress.normalized;
            if (CurrentSession == RaceWeekendSession.Qualifying)
            {
                if (participant.pitPhase == PitPhase.QualifyingReturn)
                {
                    UpdateQualifyingPitReturn(participant);
                }
                else if (participant.pitLimiterUntilExit)
                {
                    participant.vehicle.SetPitLimiter(true);
                    if (!Track.IsInPitExitLimiterZone(normalized))
                    {
                        participant.pitLimiterUntilExit = false;
                        participant.vehicle.SetPitLimiter(false);
                        participant.vehicle.SetPitExitFastLimiter(false);
                    }
                }
                else
                {
                    participant.vehicle.SetPitLimiter(false);
                    participant.vehicle.ClearPitRequest();
                }

                return;
            }

            if (participant.pitLimiterUntilExit)
            {
                // Post-handoff limiter tail: the rail clears its own limiter
                // flags at CompletePitRail, so this only covers a car whose
                // handoff landed while still inside the limiter zone (short
                // exit ramps) - cleared off the car's own live progress.
                participant.vehicle.SetPitLimiter(true);
                if (!Track.IsInPitExitLimiterZone(normalized))
                {
                    participant.pitLimiterUntilExit = false;
                    participant.vehicle.SetPitLimiter(false);
                    participant.vehicle.SetPitExitFastLimiter(false);
                    if (participant.isPlayer)
                    {
                        SessionMessage = "Pit exit: limiter off, resume racing speed";
                        PostEngineerMessage("Pit exit. Limiter off. Resume racing speed.", true);
                    }
                }
            }

            // Pit-system rebuild: one update drives the entire guided sequence
            // (entry lane -> box -> exit lane -> handoff). pitPhase is still
            // maintained for external readers (HUD, incident classification,
            // AI avoidance) but no longer selects between separate updaters.
            if (participant.pitPhase != PitPhase.None)
            {
                UpdatePitRail(participant);
                return;
            }

            if (!participant.vehicle.PitRequested)
            {
                if (!participant.pitLimiterUntilExit)
                {
                    participant.vehicle.SetPitLimiter(false);
                }

                if (participant.isPlayer)
                {
                    engineerPitRequestConfirmed = false;
                }
                return;
            }

            // Limiter-consistency bugfix: this used to fire "Pit request
            // confirmed... limiter is 80 km/h" the instant the car entered the
            // broad IsInPitApproach zone (0.78) - roughly 7% of a lap before the
            // hard limiter (below) actually engaged, telling the player they were
            // limited before they were. pitApproach itself is kept only for the
            // informational "Pit entry approaching" fallback text further down;
            // the limiter-confirmation message now fires at the same shared
            // boundary (Track.HasCrossedPitEntryLimiterLine) that actually
            // engages PitLimiterActive, for player and AI alike.
            bool pitApproach = Track.IsInPitApproach(normalized);

            // Pit-entry timing fix: physical commit decisions (whether a car is "on
            // the ramp", and the missed-entry cutoff) now key off the REAL physical
            // ramp window (Track.IsInPitEntryRampWindow, 0.850-0.885) instead of the
            // broader approach/HUD zone (IsInPitEntryZone, 0.865-0.955). The old zone
            // both started (0.865) after roughly 43% of the real 0.850-0.885 opening
            // had already passed AND kept accepting a commit all the way to 0.955,
            // long after the ramp had fully flattened into the corridor and the
            // divider wall (TrackManager.BuildPitLaneDividerFence, which starts
            // exactly at PitCorridorStartNormalized) had already begun - AI cars were
            // reaching the wall and bouncing back onto the main straight instead of
            // ever being recognized as having entered. Also re-samples the car's own
            // ACTUAL current transform position fresh here (Track.GetProgressNear),
            // not the cached/timing-snapshot progress used elsewhere for steering, so
            // the commit decision is judged against where the car genuinely,
            // physically is right now.
            bool inPitEntryRampWindow = Track.IsInPitEntryRampWindow(normalized);
            if (inPitEntryRampWindow)
            {
                TrackProgress actualProgress = Track.GetProgressNear(participant.transform.position, currentProgress.distance);
                // Limiter-consistency fix: HasCrossedPitEntryLimiterLine is the ONE
                // shared boundary function every pit-entry-limiter consumer now
                // calls (the painted line/sign, this hard-limiter activation, and
                // the engineer message below) - it also folds in lateral position
                // (via IsOnPitEntryRamp), so a car with a pit request simply
                // passing this longitudinal point on the ordinary racing surface,
                // without actually steering onto the ramp, never trips it.
                bool crossedLimiterLine = Track.HasCrossedPitEntryLimiterLine(actualProgress);
                // Hard-limiter timing fix: this used to engage across the whole
                // broad IsInPitApproach zone (0.78-0.955) the instant a pit request
                // existed, forcing cars to crawl at 80 km/h through ordinary racing
                // corners long before they had any physical path toward the ramp.
                // BeginPitEntry (below) takes over limiter ownership for the rest
                // of the stop, so this only needs to cover the moment of commit.
                participant.vehicle.SetPitLimiter(crossedLimiterLine);
                if (crossedLimiterLine)
                {
                    if (participant.isPlayer && !engineerPitRequestConfirmed)
                    {
                        engineerPitRequestConfirmed = true;
                        PostEngineerMessage("Pit request confirmed. Slow for pit entry, limiter is " + PitServiceRules.PitLaneSpeedLimitKph.ToString("0") + " km/h.", true, RaceAudioCue.PitConfirm);
                    }

                    BeginPitEntry(participant, actualProgress);
                }
                else if (participant.isPlayer)
                {
                    SessionMessage = "Pit entry: steer right to commit";
                }

                if (!participant.isPlayer)
                {
                    GameLog.Info("[PitEntry] " + participant.driverName +
                                 " normalized=" + normalized.ToString("0.000") +
                                 " lateral=" + actualProgress.lateralDistance.ToString("0.00") +
                                 " halfWidth=" + LocalHalfWidthAt(actualProgress.distance).ToString("0.00") +
                                 " onRamp=" + crossedLimiterLine +
                                 " beganEntry=" + crossedLimiterLine);
                }
            }
            else if (normalized > Track.PitCorridorStartNormalized)
            {
                // Past the real physical opening without ever committing - the
                // divider wall has already begun by here, so there is no longer a
                // physical path through. Mark the stop missed and retry next lap
                // instead of continuing to steer the car toward a wall until some
                // much later, physically meaningless deadline (this used to wait
                // until 0.952, ~7% of a lap after the real opening had already
                // closed). Recording the lap this happened on lets
                // UpdateMissedPitEntryReset clear the flag only once the car has
                // genuinely started a new lap, instead of an automatic trigger
                // (tyre wear, strategy lap, damage, undercut, VSC/SC) re-arming
                // PitRequested moments later while still inside the broad zone.
                participant.missedPitEntryThisLap = true;
                participant.missedPitEntryCompletedLap = participant.lapTracker.CompletedLaps;
                participant.vehicle.ClearPitRequest();
                participant.vehicle.SetPitLimiter(false);
                participant.pitAutoTriggered = false;
                // The request never reached the commitment boundary, so it was
                // never actually cancellable in the first place (missedPitEntryThisLap
                // itself is what prevents it silently re-arming) - but the
                // request itself is gone now, so the tracking that describes it
                // must go with it rather than lingering as a stale "still
                // queued" state.
                ClearManualPitRequestTracking(participant);
                if (participant.isPlayer)
                {
                    SessionMessage = "Pit entry missed. We'll box next lap.";
                    PostEngineerMessage("Pit entry missed. We'll box next lap.", true);
                }
                else
                {
                    // Debug.LogWarning, not GameLog (Verbose-gated - this miss
                    // has been happening invisibly). The lateral/halfWidth pair
                    // is the discriminator: lateral well INSIDE halfWidth means
                    // the steering never got the car to the ramp opening;
                    // lateral AT/BEYOND halfWidth means it was physically there
                    // but the commit test never fired.
                    TrackProgress missProgress = Track.GetProgressNear(participant.transform.position, currentProgress.distance);
                    Debug.LogWarning("[PitDiag] " + participant.driverName + " MISSED pit entry (reached corridor without committing): " +
                                     "norm=" + normalized.ToString("0.000") +
                                     " lateral=" + missProgress.lateralDistance.ToString("0.0") +
                                     " halfWidth=" + LocalHalfWidthAt(missProgress.distance).ToString("0.0") +
                                     " speed=" + Mathf.Abs(participant.vehicle.CurrentSpeedKph).ToString("0") + "kph");
                }
            }
            else
            {
                participant.vehicle.SetPitLimiter(false);
                if (participant.isPlayer)
                {
                    SessionMessage = pitApproach ? "Pit entry approaching" : "Pit request queued";
                }
            }
        }

        // Deterministic-deadlock fix: missedPitEntryThisLap now actually gates
        // every automatic pit-request source (see AiVehicleController's
        // command.pitRequest suppression, and the guards added to
        // UpdatePlayerAutoPitStrategy/AcceptRaceControlPitOffer below), so it
        // must be cleared reliably once the miss is genuinely behind the car -
        // not merely once time or distance has passed, but once CompletedLaps
        // has advanced past the lap the miss was recorded on, i.e. the car has
        // crossed the line and physically started a fresh lap with a fresh
        // shot at the real opening.
        void UpdateMissedPitEntryReset(RaceParticipant participant)
        {
            if (!participant.missedPitEntryThisLap || participant.lapTracker == null)
            {
                return;
            }

            if (participant.lapTracker.CompletedLaps > participant.missedPitEntryCompletedLap)
            {
                participant.missedPitEntryThisLap = false;
                participant.missedPitEntryCompletedLap = -1;
            }
        }

        void BeginPitEntry(RaceParticipant participant, TrackProgress commitProgress)
        {
            // Pre-race pit lap fix: capture the actual entry lap right here,
            // before the car has any chance to cross the start/finish line while
            // still inside the pit lane (which would otherwise bump DisplayLap
            // and make a perfectly on-plan stop look like it happened a lap
            // late). This is the one and only place pitEntryLap is written.
            int plannedTargetLap = participant.isPlayer ? NextPlannedPitLapFor(participant) : -1;
            participant.pitEntryLap = participant.lapTracker != null ? participant.lapTracker.DisplayLap : -1;
            if (participant.isPlayer)
            {
                GameLog.Info("[Pit] Planned stop target lap=" + plannedTargetLap + ", actual pit-entry lap=" + participant.pitEntryLap + ".");
            }

            if (!participant.isPlayer)
            {
                // One line per successful AI commit (Debug.Log so it's visible
                // without Verbose) - together with the [PitDiag] MISS/abort
                // warnings this gives a complete visible record of every AI
                // pit-entry attempt's outcome.
                Debug.Log("[PitDiag] " + participant.driverName + " committed pit entry at norm=" +
                          commitProgress.normalized.ToString("0.000") + " speed=" +
                          Mathf.Abs(participant.vehicle.CurrentSpeedKph).ToString("0") + "kph");
            }

            // [PitStopDiag] commitment-side record (per report - "the 2 stop
            // problem still exists ... write code to diagnose it"): EVERY
            // stop that actually begins logs the full strategy state here, at
            // the one choke point all pit entries pass through regardless of
            // which trigger or system requested them. If a stop begins with
            // NO matching request-side [PitStopDiag] line that lap, the
            // request came from outside the AI strategy block - that absence
            // is itself the diagnosis. Round 2: extended from second-stop-only
            // to ALL stops after [PaceDiag] showed a mass lap-2 stop wave that
            // the first-stop blind spot swallowed whole.
            if (participant.vehicle != null && participant.vehicle.Tyres != null && participant.lapTracker != null)
            {
                float diagTempC = Track != null ? Track.trackTemperatureC : TyreStrategyRules.StandardTrackTempC;
                TyreCompound diagCompound = participant.vehicle.Tyres.Compound;
                int diagCode = diagCompound == TyreCompound.Soft ? TyreStrategyRules.Compound.Soft
                    : (diagCompound == TyreCompound.Hard ? TyreStrategyRules.Compound.Hard : TyreStrategyRules.Compound.Medium);
                float diagStintLaps = Mathf.Max(0.6f, TyreStrategyRules.ExpectedStintLapsAtTemp(diagCode, diagTempC));
                float diagLapsToFlag = RaceLaps - participant.lapTracker.CompletedLaps - commitProgress.normalized;
                float diagTyreLapsLeft = participant.vehicle.Tyres.Wear * diagStintLaps;
                Debug.LogWarning("[PitStopDiag] " + participant.driverName + " BEGINS stop #" + (participant.pitStops + 1) +
                    (participant.isPlayer ? " (PLAYER)" : "") +
                    " lap " + (participant.lapTracker.CompletedLaps + 1) + "/" + RaceLaps +
                    " source=" + participant.activePitRequestSource +
                    " compound=" + diagCompound +
                    " wear=" + participant.vehicle.Tyres.Wear.ToString("0.00") +
                    " tyreLapsLeft=" + diagTyreLapsLeft.ToString("0.0") +
                    " lapsToFlag=" + diagLapsToFlag.ToString("0.0") +
                    " reachesFlag=" + (diagTyreLapsLeft >= diagLapsToFlag - 0.1f) +
                    " expectedStint=" + diagStintLaps.ToString("0.0") + "@" + diagTempC.ToString("0") + "C" +
                    " weather=" + (Track != null ? Track.weather.ToString() : "?"));
            }

            participant.pitPhase = PitPhase.Entry;
            participant.pitEntryCommitted = true;
            // Cancellable-manual-pit-stop fix: this is the authoritative
            // commitment boundary (Track.HasCrossedPitEntryLimiterLine, checked
            // just before this is called) - a manual/SC-offer request becomes
            // permanently uncancellable the instant it's crossed.
            if (participant.activePitRequestSource == PitRequestSource.Manual ||
                participant.activePitRequestSource == PitRequestSource.SafetyCarPrompt)
            {
                participant.manualPitCommitted = true;
            }

            participant.manualPitRequested = false;
            participant.missedPitEntryThisLap = false;
            participant.isPitting = true;
            participant.pitLimiterUntilExit = false;
            participant.pitAwaitingRelease = false;
            participant.pitLaneHeldByOccupancy = false;
            participant.pitTimer = 0f;
            participant.pitServiceDuration = 0f;
            participant.nextPitCompound = participant.requestedPitCompoundSet ? participant.requestedPitCompound : NextPlannedPitCompoundFor(participant);
            participant.pitTyreSelectionActive = false;

            // ==== Rail seed (pit-system rebuild) ====
            // The rail's single authority starts exactly where the car
            // physically committed. Every landmark S-value is the distance
            // FROM COMMIT, measured forward, then clamped into one bounded,
            // monotonic corridor.
            //
            // Root-cause fix (the ~+290s "stuck exiting" stall): the pit lane
            // is one short forward corridor - commit -> boxes -> release ->
            // ramp end - normally ~0.12 of a lap total. But PitBoxDistance
            // returns 0.9*length + index*10.5m UNWRAPPED, and 22 boxes are
            // 231m of boxes; on a short circuit they physically overflow PAST
            // the release/exit point (and even past the start/finish line).
            // The old chain then computed the exit leg as
            // WrappedForwardDistance(box, release) with the box sitting AHEAD
            // of release - which wraps almost a whole lap. Cars in the
            // high-numbered boxes therefore rolled the ENTIRE track on the pit
            // rail at pit-lane pace before "reaching" the exit: ~+290s, hitting
            // exactly the block of ~10 cars seen stacked on the timing screen.
            //
            // Fix: wrap the box distance, measure every landmark from commit,
            // and clamp all of them into [0, corridor] (corridor = the real
            // commit->ramp-end forward span) with a monotonic order. No car
            // can ever be sent further than the actual pit corridor again,
            // whatever the track length or however the boxes fall.
            participant.pitGuideDistance = commitProgress.distance;
            participant.pitGuideLateral = commitProgress.lateralDistance;
            participant.hasPitGuideState = true;
            participant.pitRailTraveled = 0f;
            participant.pitRailServiceStarted = false;
            participant.pitRailStopServed = false;
            participant.pitRailServiceDone = false;
            float boxDistance = Track.WrapDistance(Track.PitBoxDistance(participant.pitBoxIndex));
            float releaseDistance = Track.WrapDistance(Track.length * Track.PitReleaseNormalized);
            float rampEndDistance = Track.WrapDistance(Track.length * Track.PitExitRampEndNormalized);
            float corridor = WrappedForwardDistance(commitProgress.distance, rampEndDistance);
            float rawBoxS = WrappedForwardDistance(commitProgress.distance, boxDistance);
            float rawReleaseS = WrappedForwardDistance(commitProgress.distance, releaseDistance);
            participant.pitRailRampEndS = corridor;
            participant.pitRailBoxS = Mathf.Clamp(rawBoxS, 0f, Mathf.Max(0f, corridor - 8f));
            participant.pitRailReleaseS = Mathf.Clamp(rawReleaseS, participant.pitRailBoxS, corridor);
            participant.pitRailHardEndS = participant.pitRailRampEndS + PitRailHardEscapeMeters;

            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.SetPitExitFastLimiter(false);
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitGuidance(true);
            participant.vehicle.ClearPitRequest();
            if (participant.isPlayer)
            {
                SessionMessage = "Pit entry: limiter active";
                PostEngineerMessage("Pit entry. Hold steady, box " + (participant.pitBoxIndex + 1) + " is ready with " + participant.nextPitCompound + ".", true);
            }

            GameLog.Info("[PitRail] " + participant.driverName + " committed at d=" + commitProgress.distance.ToString("0.0") +
                         " boxS=" + participant.pitRailBoxS.ToString("0.0") +
                         " releaseS=" + participant.pitRailReleaseS.ToString("0.0") +
                         " rampEndS=" + participant.pitRailRampEndS.ToString("0.0"));
        }

        // =====================================================================
        // Pit-system rebuild: the unified pit rail.
        //
        // The old system ran four separate phase updaters (Entry, Service,
        // Release, ExitMerge), each with its own seeding, its own completion
        // predicates, its own queue negotiation (FIFO sequence numbers, convoy
        // identity, live-traffic windows, world-space bubbles), plus a stuck
        // watchdog patrolling the seams between them. Every one of those seams
        // was a place cars could bunch up, fail to release, or get handed a
        // pose on the wrong piece of track. All of it is gone.
        //
        // The replacement is one rail: at commit, the car records where it
        // physically is (railStart) and computes, once, how many metres of
        // canonical pit path lie between that point and its own box, the
        // release point, and the exit-ramp end - by chaining strictly-forward
        // wrapped segments, so track wrap can never be misread as a lap of
        // travel. From then on a single monotonic parameter (pitRailTraveled)
        // advances by exactly the metres stepped each tick, and the car is
        // placed AT the canonical pose sampled for that parameter. Sections
        // (entry lane, box stop, exit lane, handoff) are just comparisons of
        // traveled against those landmark values.
        //
        // Traffic rules, in full:
        //  1. A rolling railed car follows the rolling railed car ahead of it
        //     at PitRailHeadwayMeters (distance-clamped, never binary stop/go).
        //  2. A car parked in its service bay is laterally out of the lane and
        //     never blocks anyone.
        //  3. Leaving the bay waits for a gap in railed lane traffic - the one
        //     and only queue wait in the system, and it is bounded because the
        //     cars it waits for keep moving (rule 4).
        //  4. The rail NEVER yields to live racing traffic. Live traffic
        //     avoids the exiting car (AiVehicleController treats a car in the
        //     exit section as real traffic to brake/steer around). The handoff
        //     back to physics waits only for genuine physical overlap to
        //     clear, and force-completes unconditionally a bounded distance
        //     past the ramp end. There is no code path on which a railed car
        //     can fail to eventually leave the pit lane.
        // =====================================================================
        // Pit-duration rebalance: paired with the fixed-metre entry/box anchors
        // (TrackRuntime.PitEntryRampStartLeadMetres etc.), the rail pace runs at
        // the realistic 80 km/h pit-speed-limit ballpark instead of the old
        // crawl - a full stop is now ~20s (entry+service+exit) on every track.
        // ONE pit-lane speed, from the pit entry line to the pit exit line, for
        // player and AI alike - which is how the real regulation reads. The game
        // used to run four different figures (105 entry / 75 past the boxes / 106
        // exit / 108 cap), none of them the real 80 km/h, while the painted sign on
        // track said "80" and the radio said "limiter is 105 km/h". Pit-lane time
        // loss is the input to every undercut and safety-car stop decision, so this
        // being ~30% fast systematically over-valued pit stops.
        const float PitEntryPaceKph = PitServiceRules.PitLaneSpeedLimitKph;
        const float PitLanePaceKph = PitServiceRules.PitLaneSpeedLimitKph;
        const float PitExitPaceKph = PitServiceRules.PitLaneSpeedLimitKph;
        const float PitGuideLateralRateMetersPerSecond = 9f;
        const float PitGuideChaseSpeed = 45f;
        const float PitGuideChaseRotateSpeed = 260f;
        const float PitRailHeadwayMeters = 9f;
        const float PitRailBayBlendMeters = 14f;
        const float PitRailHardEscapeMeters = 40f;
        const float PitRailHeadingLookaheadMeters = 12f;
        const float PitExitLaneHoldSeconds = 1.5f;
        const float PitExitLaneHoldDistanceMeters = 40f;
        const float PitExitOverlapRadiusMeters = 7f;

        // Wrapped forward-only distance from one track distance to another,
        // always >= 0 and always measured in the direction of travel.
        float WrappedForwardDistance(float fromDistance, float toDistance)
        {
            float delta = toDistance - fromDistance;
            if (delta < 0f)
            {
                delta += Track.length;
            }

            return delta;
        }

        // A car counts as rolling rail traffic while it is on the rail and not
        // parked in its service bay (a bay car is laterally out of the lane).
        bool IsRailRolling(RaceParticipant p)
        {
            if (p == null || p.retired || p.finished || p.vehicle == null || !p.hasPitGuideState)
            {
                return false;
            }

            if (p.pitPhase == PitPhase.None || p.pitPhase == PitPhase.QualifyingReturn)
            {
                return false;
            }

            return !(p.pitRailServiceStarted && !p.pitRailServiceDone);
        }

        // A rolling rail car is on one of two independent legs: driving IN to
        // its box (entry) or driving OUT toward the exit (exit). A car parked
        // in its bay is neither (excluded by IsRailRolling).
        bool IsRailRollingEntry(RaceParticipant p)
        {
            return IsRailRolling(p) && !p.pitRailServiceStarted;
        }

        bool IsRailRollingExit(RaceParticipant p)
        {
            return IsRailRolling(p) && p.pitRailServiceDone;
        }

        // Nearest matching-leg rolling car ahead of this one along the pit
        // path, within the shared headway.
        //
        // Decoupling fix (the release trickle / massive gaps): entry cars and
        // exit cars share the SAME physical fast lane, but they must NOT queue
        // on each other. A car rolling IN to a far box (e.g. the player
        // heading to box 22) sits directly in the forward exit path of every
        // car trying to leave a nearer box - so when a single shared pool was
        // used, those exiting cars piled up nose-to-tail behind the slow
        // entering car and couldn't pass until it finally parked, which is
        // exactly the "only two released, everyone else waiting, huge gaps"
        // seen on the timing screen. An entry car therefore only ever yields
        // to other ENTRY cars ahead of it, and an exit car only to other EXIT
        // cars. Cross-leg overlap is harmless: rail cars are kinematic and
        // non-colliding, and the physics handoff relocates to a genuinely
        // clear pose (CompletePitRail), so an exit car passing "through" the
        // lane space of an entering car never produces a real collision.
        // Wrapped forward distance is asymmetric, so two same-leg cars can
        // never mutually block each other.
        RaceParticipant FindRailCarAheadOnLeg(RaceParticipant participant, bool exitLeg)
        {
            RaceParticipant closest = null;
            float closestGap = float.MaxValue;
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant)
                {
                    continue;
                }

                bool match = exitLeg ? IsRailRollingExit(other) : IsRailRollingEntry(other);
                if (!match)
                {
                    continue;
                }

                float gap = WrappedForwardDistance(participant.pitGuideDistance, other.pitGuideDistance);
                if (gap > 0.01f && gap < closestGap)
                {
                    closestGap = gap;
                    closest = other;
                }
            }

            return closestGap <= PitRailHeadwayMeters ? closest : null;
        }

        // Bay-release gate: a car pulling out of its box joins the EXIT flow,
        // so it only waits for another EXIT car already rolling in the lane
        // just ahead of its own box - never for entry traffic still driving
        // deeper into the pits (that is the exact cross-leg block the
        // decoupling above removes).
        RaceParticipant FindBayReleaseBlocker(RaceParticipant participant)
        {
            return FindRailCarAheadOnLeg(participant, true);
        }

        // Genuine physical overlap only - the one remaining world-space
        // clearance rule at handoff. Unlike proximity windows, overlap always
        // self-resolves: both cars keep moving. Checks EVERY participant
        // (rail or live) - used only once, at the final handoff near the
        // exit ramp end, where "everyone" is safe and cheap to check.
        RaceParticipant FindOverlapBlocker(RaceParticipant participant, Vector3 candidatePosition)
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant || other.vehicle == null || other.retired)
                {
                    continue;
                }

                if (Vector3.Distance(candidatePosition, other.transform.position) < PitExitOverlapRadiusMeters)
                {
                    return other;
                }
            }

            return null;
        }

        // The canonical lateral for the car's current rail position: entry
        // ramp/lane on the way in (peeling into the bay just before the box),
        // pit lane on the way out, blending across the exit ramp onto a legal
        // on-track lateral by the ramp end. The bounded MoveTowards rate in
        // UpdatePitRail is what turns each transition into a gradual peel.
        float RailLateralTarget(RaceParticipant participant)
        {
            float distance = participant.pitGuideDistance;
            if (!participant.pitRailServiceStarted)
            {
                if (participant.pitRailBoxS - participant.pitRailTraveled <= PitRailBayBlendMeters)
                {
                    return Track.PitServiceBayLateral;
                }

                return Track.PitEntryPathLateral(distance);
            }

            float normalized = distance / Mathf.Max(1f, Track.length);
            if (Track.IsInPitExitMergeZone(normalized) || participant.pitRailTraveled >= participant.pitRailRampEndS)
            {
                float rampLateral;
                float rampHalfWidth;
                Track.GetPitExitRampEnvelope(normalized, distance, out rampLateral, out rampHalfWidth);
                float legalLateral = Track.PitExitMergeLegalLateral(distance);
                float mergeBlend = Track.PitExitMergeBlend(normalized);
                float lateral = Mathf.Lerp(legalLateral, rampLateral, mergeBlend);
                bool insideRampEnvelope = Mathf.Abs(lateral - rampLateral) <= rampHalfWidth;
                bool insideLegalLane = Mathf.Abs(lateral) <= LocalHalfWidthAt(distance);
                return insideRampEnvelope || insideLegalLane ? lateral : legalLateral;
            }

            return Track.PitLaneLateral;
        }

        // The one guided-pit update: entry lane -> box -> exit lane -> handoff.
        void UpdatePitRail(RaceParticipant participant)
        {
            if (participant.pitPhase == PitPhase.QualifyingReturn)
            {
                UpdateQualifyingPitReturn(participant);
                return;
            }

            // --- arrival at the box: snap into the bay, start the stop.
            if (!participant.pitRailServiceStarted && participant.pitRailTraveled >= participant.pitRailBoxS - 0.01f)
            {
                Vector3 servicePosition;
                Quaternion serviceRotation;
                Track.GetPitServicePose(participant.pitBoxIndex, out servicePosition, out serviceRotation);
                participant.vehicle.SnapToPitPose(servicePosition, serviceRotation);
                // Wrap: PitBoxDistance can exceed track length on short
                // circuits (see the rail-seed comment in BeginPitEntry) - an
                // unwrapped guide distance would then mis-measure every
                // subsequent gap/pose lookup on the exit leg.
                participant.pitGuideDistance = Track.WrapDistance(Track.PitBoxDistance(participant.pitBoxIndex));
                participant.pitGuideLateral = Track.PitServiceBayLateral;
                participant.pitRailServiceStarted = true;
                participant.pitLaneHeldByOccupancy = false;
                BeginPitStop(participant);
                return;
            }

            // --- box hold: tyres, then wait for a lane gap to pull out into.
            if (participant.pitRailServiceStarted && !participant.pitRailServiceDone)
            {
                participant.pitPhase = PitPhase.Service;
                participant.vehicle.SetPitServiceHold(true);
                participant.vehicle.SetPitLimiter(true);
                if (participant.isPlayer)
                {
                    SessionMessage = PitStatusText(participant);
                }

                if (!participant.pitRailStopServed)
                {
                    participant.pitTimer -= Time.deltaTime;
                    if (participant.pitTimer > 0f)
                    {
                        return;
                    }

                    participant.pitTimer = 0f;
                    participant.pitRailStopServed = true;
                    participant.vehicle.CompletePitStop(participant.nextPitCompound);
                    participant.pitStops++;
                    participant.lastPitLapNumber = participant.lapTracker != null
                        ? participant.lapTracker.CompletedLaps + 1
                        : -1;
                    participant.compoundStints.Add(participant.nextPitCompound.ToString());
                    participant.requestedPitCompoundSet = false;
                    participant.pitTyreSelectionActive = false;
                    participant.pitAutoTriggered = false;
                    // The stop this request described is now actually complete.
                    ClearManualPitRequestTracking(participant);
                }

                RaceParticipant bayBlocker = FindBayReleaseBlocker(participant);
                if (bayBlocker != null)
                {
                    participant.pitAwaitingRelease = true;
                    participant.pitLaneHeldByOccupancy = true;
                    return;
                }

                participant.pitAwaitingRelease = false;
                participant.pitLaneHeldByOccupancy = false;
                participant.pitRailServiceDone = true;
                participant.pitPhase = PitPhase.Release;
                participant.pitLimiterUntilExit = true;
                participant.vehicle.SetPitServiceHold(false);
                participant.vehicle.SetPitExitFastLimiter(true);
                SimpleAudioManager.PlayPitRelease(participant.transform.position);
                if (participant.isPlayer)
                {
                    SessionMessage = "Pit release: limiter active";
                    PostEngineerMessage("Stop complete. Release, limiter remains active until pit exit.", true, RaceAudioCue.PitConfirm);
                }

                return;
            }

            // --- rolling: entry leg (commit -> box) or exit leg (box -> handoff).
            bool beforeBox = !participant.pitRailServiceStarted;
            float normalizedHere = participant.pitGuideDistance / Mathf.Max(1f, Track.length);
            // Entry pace is now the SAME for the player and the AI.
            //
            // It used to be 105 kph for the player and 75 for everyone else - a 40%
            // advantage on a shared, mandatory, rule-governed stretch. Worse, the
            // entry leg runs from the commit point all the way to the car's own box,
            // and pitBoxIndex is the grid index, so the size of the advantage grew
            // with how far back the player qualified: ~1.4s from box 1, ~4-5s from
            // box 20. Undercuts that should sometimes fail always worked. (The AI
            // side of that ternary was also dead code - PitEntryPaceKph and
            // PitLanePaceKph are both 75, so both branches returned the same value.)
            //
            // Levelled UP rather than down, so the player's pit stops stay as quick
            // as the previous rounds of tuning made them and the AI simply matches.
            float paceKph = beforeBox
                ? PitEntryPaceKph
                : (participant.pitRailTraveled >= participant.pitRailReleaseS ? PitExitPaceKph : PitLanePaceKph);
            participant.pitPhase = beforeBox
                ? PitPhase.Entry
                : (participant.pitRailTraveled >= participant.pitRailReleaseS ? PitPhase.ExitMerge : PitPhase.Release);

            // Only ever queue behind a car on the SAME leg (see
            // FindRailCarAheadOnLeg) - entry cars never block exit cars and
            // vice-versa, which is what lets the exit flow drain freely past
            // cars still rolling in to far boxes.
            RaceParticipant blocker = FindRailCarAheadOnLeg(participant, !beforeBox);
            float step = Mathf.Max(0f, paceKph / 3.6f * Time.deltaTime);
            if (blocker != null)
            {
                float gap = WrappedForwardDistance(participant.pitGuideDistance, blocker.pitGuideDistance);
                step = Mathf.Min(step, Mathf.Max(0f, gap - PitRailHeadwayMeters));
            }

            if (beforeBox)
            {
                // Land exactly on the box, never past it.
                step = Mathf.Min(step, Mathf.Max(0f, participant.pitRailBoxS - participant.pitRailTraveled));
            }

            // Deadlock fix (pit-system): there is deliberately NO per-tick
            // "stop for a live car in the path" freeze here any more. It read
            // well in isolation but caused a full-field compression lock: an
            // exit car briefly held at 0 km/h (a slow just-handed-off car
            // ahead) blocked the car 9m behind it, which blocked the car 9m
            // behind THAT, and so on - a stop-wave that propagated back
            // through the entire 22-car pit lane, and if the head car was
            // held short of its own handoff point the hard-escape (which only
            // fires PAST the ramp end) never triggered, so nothing ever
            // recovered. Railed cars are kinematic and non-colliding, so a
            // brief visual overlap while rolling is completely harmless - the
            // ONLY moment overlap actually matters is the instant physics is
            // restored, and that is now handled safely and unconditionally in
            // CompletePitRail (it relocates forward along the rail to a
            // genuinely clear pose before restoring collisions, so two cars
            // can never go solid on top of each other). The rail therefore
            // never needs to freeze for live traffic at all, which means it
            // can never compression-lock.
            participant.pitLaneHeldByOccupancy = blocker != null && step <= 0.0001f;
            // Charge fuel for the rail distance. The car is kinematic here, so its
            // own distance-based burn in VehicleController.UpdateFuel sees zero
            // metres (and is skipped entirely while pit-guided) - the whole ~495m
            // corridor was covered free while still counting as race distance.
            if (participant.vehicle != null)
            {
                participant.vehicle.ConsumeGuidedFuel(step);
            }

            participant.pitRailTraveled += step;
            participant.pitGuideDistance = Track.WrapDistance(participant.pitGuideDistance + step);
            participant.pitGuideLateral = Mathf.MoveTowards(participant.pitGuideLateral, RailLateralTarget(participant), PitGuideLateralRateMetersPerSecond * Time.deltaTime);

            Vector3 waypoint;
            Quaternion waypointRotation;
            Track.SamplePitLanePose(participant.pitGuideDistance, participant.pitGuideLateral, out waypoint, out waypointRotation);

            // Heading-only lookahead so the car turns in smoothly; the position
            // it is placed at is always exactly the rail pose above.
            float headingDistance = Track.WrapDistance(participant.pitGuideDistance + PitRailHeadingLookaheadMeters);
            Vector3 headingPoint;
            Quaternion headingRotation;
            Track.SamplePitLanePose(headingDistance, participant.pitGuideLateral, out headingPoint, out headingRotation);

            participant.vehicle.SetPitServiceHold(beforeBox);
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.GuideToPitPose(waypoint, headingRotation, PitGuideChaseSpeed, PitGuideChaseRotateSpeed, false);

            if (participant.isPlayer)
            {
                SessionMessage = participant.pitLaneHeldByOccupancy
                    ? (beforeBox ? "Pit lane: queueing for box " + (participant.pitBoxIndex + 1) : "Pit exit: holding for the car ahead")
                    : (beforeBox ? "Pit lane: rolling to box " + (participant.pitBoxIndex + 1) : "Pit exit: merging onto the racing line");
            }

            // --- handoff: hand back to physics once past the ramp end when
            // the spot is genuinely clear, and force it a bounded distance
            // further on regardless. CompletePitRail itself guarantees the
            // final pose is overlap-free, so a forced handoff is just as safe
            // as a clear one - it can never restore collisions on top of
            // another car.
            if (!beforeBox && participant.pitRailTraveled >= participant.pitRailRampEndS)
            {
                bool hardEscape = participant.pitRailTraveled >= participant.pitRailHardEndS;
                if (hardEscape || FindOverlapBlocker(participant, participant.transform.position) == null)
                {
                    CompletePitRail(participant, hardEscape);
                }
            }
        }

        // Physics handoff at the end of the rail - the ONE way any pit stop
        // ends. Resyncs the lap tracker/timing/AI progress to the known rail
        // distance before restoring physics at the real guided exit speed.
        void CompletePitRail(RaceParticipant participant, bool forced)
        {
            // Safe-pose relocation (deadlock/flinging fix): the car has been
            // rolling kinematic and non-colliding, so at THIS instant it may
            // be visually overlapping a live car just ahead of the merge. If
            // physics were restored right here, Unity's depenetration would
            // violently fling both cars apart (the reported cars-off-track
            // bug). Instead, walk forward along the canonical exit rail in
            // small steps to the first genuinely clear pose (bounded, so a
            // pathological jam can't spin forever) and hand off THERE. This
            // is what lets the per-tick roll above never need to freeze for
            // live traffic - the one place overlap matters is handled once,
            // here, without ever stalling the lane.
            float finalDistance = participant.pitGuideDistance;
            float finalLateral = participant.pitGuideLateral;
            Vector3 finalPosition = participant.transform.position;
            Quaternion finalRotation = participant.transform.rotation;
            float searched = 0f;
            while (FindOverlapBlocker(participant, finalPosition) != null && searched < 60f)
            {
                finalDistance = Track.WrapDistance(finalDistance + 4f);
                searched += 4f;
                Track.SamplePitLanePose(finalDistance, finalLateral, out finalPosition, out finalRotation);
            }

            participant.vehicle.SnapToPitPose(finalPosition, finalRotation);
            participant.pitGuideDistance = finalDistance;

            if (participant.lapTracker != null)
            {
                participant.lapTracker.ResyncToDistance(finalDistance, finalPosition);
            }

            if (State != null)
            {
                State.RefreshTimingSnapshot(participant);
            }

            AiVehicleController releasedAi = participant.GetComponent<AiVehicleController>();
            if (releasedAi != null)
            {
                releasedAi.ResyncToKnownTrackDistance(finalDistance);
            }

            participant.vehicle.SetPitGuidance(false, PitExitPaceKph / 3.6f);
            participant.vehicle.SetPitServiceHold(false);
            // Belt-and-braces against a stray request surviving the stop: the
            // vehicle-side flag is cleared at BeginPitEntry/BeginPitStop but nothing
            // cleared it at the END of the sequence, so anything that latched it
            // mid-stop (see PlayerVehicleInput's pit press guard) left the car
            // reading as pit-bound the moment it was handed back to physics.
            participant.vehicle.ClearPitRequest();
            participant.pitPhase = PitPhase.None;
            participant.isPitting = false;
            participant.hasPitGuideState = false;
            participant.pitEntryCommitted = false;
            participant.pitLaneHeldByOccupancy = false;
            participant.pitAwaitingRelease = false;

            // The rail's own handoff point (ramp end + escape margin) is past
            // the physical limiter zone on every track layout, so the limiter
            // flags clear here; the dispatcher's zone check covers any layout
            // where that assumption is ever wrong.
            float handoffNormalized = finalDistance / Mathf.Max(1f, Track.length);
            if (!Track.IsInPitExitLimiterZone(handoffNormalized))
            {
                participant.pitLimiterUntilExit = false;
                participant.vehicle.SetPitLimiter(false);
                participant.vehicle.SetPitExitFastLimiter(false);
            }

            // Short AI-side outer-lane hold so racing logic doesn't dive for
            // the next apex the instant control hands back.
            participant.pitExitLaneHoldTimer = PitExitLaneHoldSeconds;
            participant.pitExitLaneHoldDistanceRemaining = PitExitLaneHoldDistanceMeters;

            if (forced)
            {
                GameLog.Warn("[PitRail] " + participant.driverName + " handoff force-completed at hard-escape margin.");
            }

            GameLog.Info("[PitRail] " + participant.driverName + " handoff complete at d=" + finalDistance.ToString("0.0") +
                         " traveled=" + participant.pitRailTraveled.ToString("0.0"));

            if (participant.isPlayer)
            {
                SessionMessage = "Merged onto the racing line";
            }
        }

        void BeginPitStop(RaceParticipant participant)
        {
            participant.pitPhase = PitPhase.Service;
            participant.isPitting = true;
            participant.pitAwaitingRelease = false;
            replayCapture.AddPitMarker(RaceElapsed, ReplayCarIndex(participant),
                participant.driverName + " pit stop");
            // Stop duration is owned by the extracted rulebook: the tyre change
            // (matched to the visible wheel-off/wheel-on animation below, so the
            // timer and the animation always finish together), plus repair time
            // when the car arrived damaged - the stop is also what repairs it
            // (VehicleController.CompletePitStop) - plus a rare crew fumble.
            // Price the repair off what the crew can actually fix (bodywork), not
            // off total damage - engine and gearbox wear are never repaired by a
            // stop, so charging for them billed time for work that never happened.
            float damagePercent = participant.vehicle.Damage == null ? 0f : participant.vehicle.Damage.RepairablePercent;
            float tyreSeconds = PitServiceRules.TyreChangeSeconds(participant.isPlayer, Random.value);
            float repairSeconds = PitServiceRules.RepairSeconds(damagePercent, Random.value);
            float fumbleSeconds = PitServiceRules.CrewErrorSeconds(Random.value, Random.value);
            participant.pitServiceDuration = tyreSeconds + repairSeconds + fumbleSeconds;
            participant.pitTimer = participant.pitServiceDuration;
            participant.vehicle.SetPitServiceHold(true);
            participant.vehicle.SetPitLimiter(true);
            participant.vehicle.ClearPitRequest();
            VehicleVisuals visuals = participant.GetComponent<VehicleVisuals>();
            if (visuals != null)
            {
                visuals.BeginPitStopVisual(participant.pitServiceDuration);
            }

            if (participant.isPlayer)
            {
                string work = "changing to " + participant.nextPitCompound;
                if (repairSeconds > 0f)
                {
                    work += " + repairing damage";
                }

                SessionMessage = "Pit box " + (participant.pitBoxIndex + 1) + ": " + work;
                PostEngineerMessage("Pit stop in progress. Tyres ready: " + participant.nextPitCompound +
                    (repairSeconds > 0f ? ". We're repairing that damage too - longer stop." : "."), true);
                if (fumbleSeconds > 0f)
                {
                    PostEngineerMessage("Problem on the front jack - hold, hold.", true);
                }
            }
        }
        void SetCarToCarCollisionIgnored(RaceParticipant participant, bool ignored)
        {
            if (participant == null || participant.carToCarCollisionIgnored == ignored)
            {
                return;
            }

            participant.carToCarCollisionIgnored = ignored;
            Collider[] ownColliders = participant.GetComponentsInChildren<Collider>(false);
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant)
                {
                    continue;
                }

                Collider[] otherColliders = other.GetComponentsInChildren<Collider>(false);
                for (int a = 0; a < ownColliders.Length; a++)
                {
                    if (ownColliders[a] == null || !ownColliders[a].enabled || ownColliders[a].isTrigger)
                    {
                        continue;
                    }

                    for (int b = 0; b < otherColliders.Length; b++)
                    {
                        if (otherColliders[b] == null || !otherColliders[b].enabled || otherColliders[b].isTrigger)
                        {
                            continue;
                        }

                        Physics.IgnoreCollision(ownColliders[a], otherColliders[b], ignored);
                    }
                }
            }
        }

    }
}
