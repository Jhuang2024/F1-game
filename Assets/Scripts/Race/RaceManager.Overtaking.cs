using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager overtaking-legality subsystem (partial). The shared authority
    /// for whether a car may gain a position right now (global SC/VSC/restart ban
    /// vs a sector-wide local yellow, via the engine-free FlagRules), the
    /// order-correction exemption for cars that aren't really racing, the
    /// snapshot-based illegal-overtake-under-yellow penalty detection, and the
    /// player overtakes-completed tracking. Split out of the RaceManager monolith
    /// verbatim - same class, same members, identical behaviour, snapshot cadence,
    /// cooldown windows and penalty values; the public entry points stay public so
    /// AI/player callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        // ---------- Overtaking legality (shared by AI decisions, player
        // enforcement, and penalty detection) ----------

        // True when this specific car may not gain positions right now: any full
        // VSC/SC/restart ban applies everywhere, a local yellow only inside the
        // flagged sector.
        public bool IsOvertakingRestrictedForParticipant(RaceParticipant participant)
        {
            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return false;
            }

            // Field-wide caution (SC/VSC/red/restart): FlagRules owns the
            // consequence of the global flag.
            if (!FlagRules.OvertakingAllowed(GlobalRaceFlag))
            {
                return true;
            }

            // A local yellow bans passing sector-wide - deliberately wider
            // than the near-incident speed-cap window (you must not pass near
            // a hazard you might not see yet), so this stays a sector test
            // rather than reading FlagForParticipant.
            if (YellowFlagSector >= 0 && State != null && participant != null &&
                State.GetCurrentProgress(participant).sector == YellowFlagSector)
            {
                return !FlagRules.OvertakingAllowed(RaceFlag.LocalYellow);
            }

            return false;
        }

        // Passing a car that isn't actually racing (retired, pitting, being
        // guided, recovering from an incident, or crawling far off race pace) is
        // order correction, not an overtake - legal even under yellow/VSC/SC.
        public bool IsPositionCorrectionAllowed(RaceParticipant attacker, RaceParticipant defender)
        {
            if (defender == null || defender.vehicle == null || defender.retired || defender.finished)
            {
                return true;
            }

            if (defender.isPitting || defender.pitPhase != PitPhase.None || defender.pitLimiterUntilExit || defender.vehicle.IsPitGuided)
            {
                return true;
            }

            if (defender.recoveryState == CarRecoveryState.Recovering || defender.recoveryState == CarRecoveryState.ActuallyStranded)
            {
                return true;
            }

            // A car crawling because it's pacing behind the safety car queue is
            // NOT a hazard to be waved past - only a car that is slow for its
            // own reasons (damage, spin aftermath) counts as passable here.
            bool defenderPacingLegitimately = defender.recoveryState == CarRecoveryState.RaceControlPacing;
            bool defenderCrawling = Mathf.Abs(defender.vehicle.CurrentSpeedKph) < 30f;
            bool attackerAtPace = attacker != null && attacker.vehicle != null && Mathf.Abs(attacker.vehicle.CurrentSpeedKph) > 60f;
            return defenderCrawling && attackerAtPace && !defenderPacingLegitimately;
        }

        public bool CanParticipantOvertake(RaceParticipant attacker, RaceParticipant defender)
        {
            if (!IsOvertakingRestrictedForParticipant(attacker) && !IsOvertakingRestrictedForParticipant(defender))
            {
                return true;
            }

            return IsPositionCorrectionAllowed(attacker, defender);
        }

        // Illegal-overtake detection, rebuilt on full running-order snapshots
        // (Part 1): the old version compared only FindCarAhead/Behind within a
        // 40m window against the previous frame, which missed any pass where the
        // pair was ever more than 40m apart or where the order shuffled between
        // frames. This compares the complete eligible running order between two
        // snapshots ~0.5s apart: any car that moved ahead of a car it was behind
        // - while restrictions applied at BOTH snapshot times, and the move
        // isn't an allowed order correction - is penalized exactly once per
        // pair per cooldown window, player and AI identically.
        readonly List<RaceParticipant> raceControlOrderSnapshot = new List<RaceParticipant>();
        // Sector each snapshotted car was in at snapshot time, parallel to
        // raceControlOrderSnapshot - see the sector-escape fix below.
        readonly List<int> raceControlSectorSnapshot = new List<int>();
        readonly Dictionary<string, float> illegalOvertakePairCooldowns = new Dictionary<string, float>();
        readonly List<string> expiredPairCooldownKeys = new List<string>();
        float orderSnapshotTimer;
        bool restrictionActiveAtLastSnapshot;
        const float OrderSnapshotInterval = 0.5f;
        const float IllegalOvertakePairCooldownSeconds = 25f;

        void CheckIllegalOvertakesUnderYellow()
        {
            if (State == null || CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial || StartCountdown > 0f)
            {
                raceControlOrderSnapshot.Clear();
                raceControlSectorSnapshot.Clear();
                restrictionActiveAtLastSnapshot = false;
                return;
            }

            orderSnapshotTimer -= Time.deltaTime;
            if (orderSnapshotTimer > 0f)
            {
                return;
            }

            orderSnapshotTimer = OrderSnapshotInterval;

            expiredPairCooldownKeys.Clear();
            foreach (KeyValuePair<string, float> entry in illegalOvertakePairCooldowns)
            {
                if (RaceElapsed > entry.Value)
                {
                    expiredPairCooldownKeys.Add(entry.Key);
                }
            }
            for (int i = 0; i < expiredPairCooldownKeys.Count; i++)
            {
                illegalOvertakePairCooldowns.Remove(expiredPairCooldownKeys[i]);
            }

            List<RaceParticipant> currentOrder = new List<RaceParticipant>();
            List<int> currentSectors = new List<int>();
            List<RaceParticipant> running = GetRunningOrderSnapshot();
            for (int i = 0; i < running.Count; i++)
            {
                RaceParticipant candidate = running[i];
                // Race-control's own convoy autopilot moves cars around in the
                // queue for its own bookkeeping reasons (never a driver's overtake
                // decision) - excluded the same way pitting cars already are so it
                // can never false-positive an illegal-overtake penalty.
                if (candidate == null || candidate.vehicle == null || candidate.retired || candidate.finished ||
                    candidate.lapTracker == null || candidate.isPitting || candidate.pitPhase != PitPhase.None ||
                    candidate.isRaceControlAutopilot)
                {
                    continue;
                }

                currentOrder.Add(candidate);
                currentSectors.Add(State.GetCurrentProgress(candidate).sector);
            }

            bool restrictionActiveNow = !IsOvertakingAllowed || YellowFlagSector >= 0;
            // A pass must have happened between two snapshots that were BOTH
            // under restriction - a legal move made just before the flag came
            // out (or completed just after green) can never be penalized.
            if (restrictionActiveNow && restrictionActiveAtLastSnapshot && raceControlOrderSnapshot.Count > 1)
            {
                for (int c = 0; c < currentOrder.Count; c++)
                {
                    RaceParticipant mover = currentOrder[c];
                    int moverPreviousIndex = raceControlOrderSnapshot.IndexOf(mover);
                    if (moverPreviousIndex < 0)
                    {
                        // Not in the previous snapshot (was pitting/rejoining) -
                        // its first snapshot back establishes a baseline instead
                        // of being compared against a stale position.
                        continue;
                    }

                    for (int d = c + 1; d < currentOrder.Count; d++)
                    {
                        RaceParticipant passed = currentOrder[d];
                        int passedPreviousIndex = raceControlOrderSnapshot.IndexOf(passed);
                        if (passedPreviousIndex < 0 || passedPreviousIndex >= moverPreviousIndex)
                        {
                            continue;
                        }

                        // mover was behind `passed` last snapshot and is ahead
                        // now - a completed pass during the restricted window.
                        // Sector-escape fix (per report: yellow-flag overtake
                        // penalties never landed on the player): the local-
                        // yellow restriction is a sector test evaluated at THIS
                        // snapshot only, so a pass begun inside the yellow
                        // sector but completed just past the sector boundary
                        // read both cars in the next sector and went unpunished.
                        // The AI never exploits this (it suppresses attacks in
                        // yellow sectors); the player did, unknowingly, every
                        // time. The pair is now also restricted if EITHER car
                        // was in the yellow sector at the PREVIOUS snapshot.
                        bool previousSectorRestricted = false;
                        if (YellowFlagSector >= 0 && raceControlSectorSnapshot.Count == raceControlOrderSnapshot.Count)
                        {
                            previousSectorRestricted =
                                raceControlSectorSnapshot[moverPreviousIndex] == YellowFlagSector ||
                                raceControlSectorSnapshot[passedPreviousIndex] == YellowFlagSector;
                        }

                        bool restrictedPair = previousSectorRestricted || IsOvertakingRestrictedForParticipant(mover) || IsOvertakingRestrictedForParticipant(passed);
                        if (!restrictedPair || IsPositionCorrectionAllowed(mover, passed))
                        {
                            continue;
                        }

                        string pairKey = mover.driverId + ">" + passed.driverId;
                        if (illegalOvertakePairCooldowns.ContainsKey(pairKey))
                        {
                            continue;
                        }

                        illegalOvertakePairCooldowns[pairKey] = RaceElapsed + IllegalOvertakePairCooldownSeconds;
                        string stateLabel = IsFullSafetyCarPeriod ? "safety car" : (IsVirtualSafetyCarActive ? "VSC" : "yellow");
                        AddPenalty(mover, 5f, "Overtake under " + stateLabel);
                        GameLog.Warn("[RaceControl] Illegal overtake penalty: " + mover.driverName + " passed " + passed.driverName +
                            " under " + stateLabel + " (state=" + CurrentRaceControlState + ", yellowSector=" + YellowFlagSector + ") (+5s).");
                        if (mover.isPlayer)
                        {
                            SessionMessage = "Overtake under " + stateLabel + ": +5s - give the position back";
                            if (Settings != null && Settings.Current.raceControlMessages)
                            {
                                PostEngineerMessage("That pass wasn't allowed under " + stateLabel + " - give the position back.", true);
                            }
                        }
                        else if (passed.isPlayer && Settings != null && Settings.Current.raceControlMessages)
                        {
                            PostEngineerMessage(mover.driverName + " passed you illegally - race control has given them 5 seconds.", false, RaceAudioCue.Penalty);
                        }
                    }
                }
            }

            TrackPlayerOvertakesCompleted(currentOrder);

            raceControlOrderSnapshot.Clear();
            raceControlOrderSnapshot.AddRange(currentOrder);
            raceControlSectorSnapshot.Clear();
            raceControlSectorSnapshot.AddRange(currentSectors);
            restrictionActiveAtLastSnapshot = restrictionActiveNow;
        }

        // Overtakes-made fix: RaceParticipant.overtakesCompleted was only ever
        // incremented by ReportAiOvertakeCompleted, which AiVehicleController
        // calls from its own AttackingInside/AttackingOutside/SideBySide ->
        // CompletingPass state transition - a transition the player, driven by
        // PlayerVehicleInput, never runs through. That left the post-race
        // report's "Overtakes made" line hardcoded at 0 for a human-driven car
        // no matter how many cars it actually passed. This reuses the exact
        // same clean-pass definition CheckIllegalOvertakesUnderYellow already
        // established just above (two running-order snapshots ~0.5s apart,
        // IsPositionCorrectionAllowed excluding pit/retired/recovering/crawling
        // cars) so "genuine overtake" means the same thing here as it does for
        // the illegal-overtake penalty - just applied unconditionally (not
        // gated behind a yellow/VSC/SC restriction, since most real overtakes
        // happen under green) and only for the player, since AI already has
        // its own precise, independent counter and running this for AI too
        // would double-count against it.
        void TrackPlayerOvertakesCompleted(List<RaceParticipant> currentOrder)
        {
            if (raceControlOrderSnapshot.Count <= 1)
            {
                return;
            }

            RaceParticipant player = null;
            int playerCurrentIndex = -1;
            for (int i = 0; i < currentOrder.Count; i++)
            {
                if (currentOrder[i].isPlayer)
                {
                    player = currentOrder[i];
                    playerCurrentIndex = i;
                    break;
                }
            }

            if (player == null)
            {
                return;
            }

            int playerPreviousIndex = raceControlOrderSnapshot.IndexOf(player);
            if (playerPreviousIndex <= 0)
            {
                return;
            }

            for (int d = 0; d < playerPreviousIndex; d++)
            {
                RaceParticipant passed = raceControlOrderSnapshot[d];
                if (passed == null)
                {
                    continue;
                }

                int passedCurrentIndex = currentOrder.IndexOf(passed);
                if (passedCurrentIndex < 0 || passedCurrentIndex <= playerCurrentIndex)
                {
                    continue;
                }

                if (IsPositionCorrectionAllowed(player, passed))
                {
                    continue;
                }

                player.overtakesCompleted++;
            }
        }

    }
}
