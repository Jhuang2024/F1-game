using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager race-control speed-enforcement subsystem (partial). The allowed
    /// speed cap for a car's current situation (pit lane excluded, VSC/SC
    /// field-wide, local yellow only near the incident), the field-wide physical
    /// cap applied to every car alike, and the player-only overspeed warning +
    /// pace penalty limiter. Split out of the RaceManager monolith verbatim - same
    /// class, same members, identical caps, warning/penalty thresholds and call
    /// order; RaceControlSpeedCapKphFor stays public so external callers resolve
    /// in-class, and the overspeed-timer/warning state stays main-nested.
    /// </summary>
    public partial class RaceManager
    {
        // The allowed speed cap for this specific car's current race-control
        // situation - pit lane has its own dedicated limiter so it's excluded
        // here, VSC/SC apply field-wide, and local yellow only applies to a car
        // actually near the incident. Shared by both the player's own warning/
        // penalty logic below and the field-wide physical cap applied to every
        // car (player and AI alike) in ApplyRaceControlSpeedCaps.
        public float RaceControlSpeedCapKphFor(RaceParticipant participant)
        {
            if (participant == null || participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return 9999f;
            }

            // Deploying and "in this lap" allow a little more than the steady
            // active/VSC caps since the field is still catching up to the queue
            // or about to go green.
            switch (CurrentRaceControlState)
            {
                case RaceControlState.VirtualSafetyCar:
                    return VirtualSafetyCarSpeedCapKph;
                case RaceControlState.SafetyCarDeploying:
                    return SafetyCarTargetSpeedKph + 30f;
                case RaceControlState.SafetyCarActive:
                    return SafetyCarTargetSpeedKph;
                case RaceControlState.SafetyCarInThisLap:
                    return SafetyCarTargetSpeedKph + 15f;
                case RaceControlState.RedFlagged:
                    // A red flag had NO case here at all, so it fell through to the
                    // default - which returns 9999 unless the state is exactly
                    // YellowSector. FlagRules.RequiresPaceControl doesn't cover Red
                    // either, so IsRaceControlPaceLimited was false and the player's
                    // soft limiter never engaged. The only thing holding the field
                    // was the autopilot flag, and UpdateSafetyCar's upkeep loop
                    // strips that from any car with a queued pit request on the very
                    // next frame - so a car that had merely pressed P (pitPhase is
                    // still None until it reaches the ramp) was left completely
                    // un-neutralised and came flying through the stationary pack at
                    // full racing speed for the whole hold, before being teleported
                    // onto the restart grid anyway.
                    return RedFlagSpeedCapKph;
                case RaceControlState.Restart:
                    // Limiter-duration fix: the restart phase (safety car in,
                    // green not yet flown) used to fall through to the default
                    // 9999 - the player's hard cap vanished several seconds
                    // before the period actually ended while the AI convoy was
                    // still being paced. Same slightly-relaxed delta as
                    // SafetyCarInThisLap so the field can build to the restart.
                    return SafetyCarTargetSpeedKph + 15f;
                default:
                    return IsNearLocalYellowIncident(participant) ? LocalYellowSpeedCapKph : 9999f;
            }
        }

        // Physically caps every car's real top speed to its own current
        // race-control situation - the "pit-limiter-style" hard enforcement the
        // brief calls for, applied identically to the player and every AI car
        // rather than the player alone relying on the softer shaping below.
        void ApplyRaceControlSpeedCaps()
        {
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant participant = Participants[i];
                if (participant == null || participant.vehicle == null || participant.retired || participant.finished)
                {
                    continue;
                }

                participant.vehicle.SetRaceControlSpeedCap(RaceControlSpeedCapKphFor(participant));
            }
        }

        // Soft pace limiter applied to the PLAYER's own command, mirroring the AI
        // speed clamp in AiVehicleController.Update(). Never touches the AI's
        // command (they're already clamped elsewhere), never fights pit entry/
        // exit/limiter, and always forces ERS/DRS off while pace-limited - it is
        // the single place both of those get enforced for the player, so a Shift-
        // key press or a still-latched DRS press can never bypass race control.
        public VehicleCommand ApplyPlayerRaceControlLimiter(RaceParticipant participant, VehicleCommand command, float currentSpeedKph)
        {
            IsPlayerOverRaceControlPace = false;
            if (participant == null || !participant.isPlayer || CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return command;
            }

            if (participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                // Pit limiter/guidance already governs speed here - don't stack a
                // second, conflicting speed rule on top of it.
                playerRaceControlOverspeedTimer = 0f;
                playerRaceControlWarningSent = false;
                IsPlayerRaceControlWarningActive = false;
                return command;
            }

            bool localYellowHere = IsNearLocalYellowIncident(participant);
            if (!IsRaceControlPaceLimited && !localYellowHere)
            {
                playerRaceControlOverspeedTimer = 0f;
                playerRaceControlWarningSent = false;
                IsPlayerRaceControlWarningActive = false;
                return command;
            }

            // Both reasons this method is active (VSC/SC pace limiting, or being
            // near a local yellow incident) already ban DRS via IsDrsAvailable and
            // ERS deployment makes no sense while being held to a reduced pace
            // either way - force both off unconditionally here as the single place
            // a stale Shift press or already-latched DRS can't sneak past either.
            command.ers = false;
            command.drs = false;

            float cap = RaceControlSpeedCapKphFor(participant);
            float overspeed = currentSpeedKph - cap;
            if (overspeed <= 0f)
            {
                playerRaceControlOverspeedTimer = 0f;
                playerRaceControlWarningSent = false;
                IsPlayerRaceControlWarningActive = false;
                return command;
            }

            IsPlayerOverRaceControlPace = true;

            // Match-AI-deceleration fix: this used to bleed throttle off over a
            // 15kph grace window and only start braking past that same 15kph -
            // AiVehicleController never has that grace at all. AI's own
            // cruiseTargetSpeed (now set directly to the SC/VSC cap - see the
            // AI pace-cap fix) drives its throttle target via
            // speedGap = cruiseTargetSpeed - speedKph, which clamps to exactly
            // ZERO the instant speedKph reaches the cap - AI simply never keeps
            // accelerating past it. The player, driven by raw held-throttle
            // input, could previously keep receiving partial throttle for a
            // further 15kph past the cap before even reaching zero, and only a
            // soft, slowly-ramping brake after that - a much slower, looser
            // correction than AI's, which is exactly why the player visibly ran
            // several kph over the limit while AI cars snapped back to it fast.
            // Throttle now cuts to zero immediately at the cap (matching AI),
            // and braking ramps in immediately too instead of waiting for a
            // grace window - proportionally light for a small overspeed (a
            // gentle correction, similar to AI's own natural drag-only coast
            // right at the cap) and meaningfully firmer for a large one (e.g.
            // right after VSC/SC has just deployed), same as a real driver
            // reacting to a sudden new delta.
            command.throttle = 0f;
            float brakeAmount = Mathf.Clamp01(overspeed / 20f) * 0.6f;
            command.brake = Mathf.Max(command.brake, brakeAmount);

            // Warn first, then penalize only if the player stays grossly over the
            // cap after the warning - never an instant penalty.
            playerRaceControlOverspeedTimer += Time.deltaTime;
            if (!playerRaceControlWarningSent && playerRaceControlOverspeedTimer > 2.5f)
            {
                playerRaceControlWarningSent = true;
                IsPlayerRaceControlWarningActive = true;
                SessionMessage = "Slow down: over the race control pace limit";
                if (Settings != null && Settings.Current.raceControlMessages)
                {
                    PostEngineerMessage("You're over the pace limit, slow down.", true);
                }
            }
            else if (playerRaceControlWarningSent && overspeed > 25f && playerRaceControlOverspeedTimer > 6f)
            {
                AddPenalty(participant, 5f, localYellowHere && !IsRaceControlPaceLimited ? "Ignored yellow flag speed limit" : "Ignored safety car pace");
                GameLog.Warn("[RaceControl] Player pace penalty: " + overspeed.ToString("0") + "kph over cap for " + playerRaceControlOverspeedTimer.ToString("0.0") + "s (+5s).");
                SessionMessage = "Pace limit ignored: +5s";
                playerRaceControlOverspeedTimer = 0f;
                playerRaceControlWarningSent = false;
                IsPlayerRaceControlWarningActive = false;
            }

            return command;
        }

    }
}
