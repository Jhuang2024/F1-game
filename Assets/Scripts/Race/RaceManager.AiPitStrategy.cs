using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager AI pit-strategy subsystem (partial). The pit-under-safety-car
    /// decision (favour pitting when there is a real strategic reason and enough
    /// race left, avoid it otherwise), the undercut opportunity check, and the
    /// player-facing HUD counterpart. Split out of the RaceManager monolith
    /// verbatim - same class, same members, identical thresholds and call order;
    /// the pure decision maths already live in AiPitStrategyRules, and the public
    /// entry points stay public so AI/HUD callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        // AI (and, via RecommendedPitUnderSafetyCar, the player HUD) pit-under-SC
        // decision: strongly favour pitting when there is a real strategic reason to
        // (mandatory stop owed, tyres worn, the planned window is close, or the
        // compound is wrong for the current weather) and there is enough race left
        // for it to matter; avoid it otherwise (just stopped, tyres still fresh, or
        // the race is basically over).
        public bool ShouldAiPitUnderSafetyCar(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.vehicle.Tyres == null || participant.lapTracker == null ||
                CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return false;
            }

            bool underSafetyPeriod = CurrentRaceControlState == RaceControlState.SafetyCarActive ||
                                      CurrentRaceControlState == RaceControlState.VirtualSafetyCar ||
                                      CurrentRaceControlState == RaceControlState.SafetyCarDeploying;
            if (!underSafetyPeriod)
            {
                return false;
            }

            if (participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return false;
            }

            int completedLaps = participant.lapTracker.CompletedLaps;
            if (completedLaps < 1)
            {
                return false;
            }

            int lapsRemaining = Mathf.Max(0, RaceLaps - completedLaps);
            if (lapsRemaining <= 1)
            {
                return false;
            }

            float wear = participant.vehicle.Tyres.Wear;
            bool freshTyres = participant.pitStops > 0 && wear > AiPitStrategyRules.SafetyCarFreshTyreWear;
            if (freshTyres)
            {
                return false;
            }

            // Part A.9: avoid double-stacking two SC-triggered pit calls into the
            // same box at once - if another car is already servicing or entering a
            // box at or adjacent to this car's own box index, hold this request back
            // (re-checked every tick, so it releases the moment that box clears)
            // instead of sending both cars down pit lane into the same slot.
            for (int i = 0; i < Participants.Count; i++)
            {
                RaceParticipant other = Participants[i];
                if (other == null || other == participant)
                {
                    continue;
                }

                bool otherOccupyingBox = other.pitPhase == PitPhase.Service || other.pitPhase == PitPhase.Entry;
                if (otherOccupyingBox && Mathf.Abs(other.pitBoxIndex - participant.pitBoxIndex) <= 1)
                {
                    return false;
                }
            }

            // Decision core lives in AiPitStrategyRules; this method supplies
            // the live inputs.
            bool mandatoryStopOwed = participant.pitStops == 0;
            bool tyresWorn = wear < AiPitStrategyRules.SafetyCarWornTyreWear;

            int windowLap = NextPlannedPitLapFor(participant);
            bool windowClose = windowLap > 0 && completedLaps >= windowLap - AiPitStrategyRules.SafetyCarWindowCloseLaps;

            WeatherState currentWeather = Track == null ? WeatherState.Clear : Track.weather;
            bool wetNow = currentWeather == WeatherState.LightRain || currentWeather == WeatherState.HeavyRain;
            TyreCompound currentCompound = participant.vehicle.Tyres.Compound;
            bool onWetTyre = currentCompound == TyreCompound.Intermediate || currentCompound == TyreCompound.Wet;
            bool weatherMismatch = wetNow != onWetTyre;

            return AiPitStrategyRules.ShouldPitUnderSafetyCar(mandatoryStopOwed, tyresWorn, windowClose, weatherMismatch);
        }

        // Smarter AI strategy: undercut awareness. NextPitCompound/RecommendedPitLap
        // already give every AI a stable target window, but until now nothing ever
        // reacted to who was actually around it on track - every car pitted right at
        // its own jittered lap regardless of a car directly ahead offering a live
        // undercut. This lets an AI still on its first stint pit up to 2 laps EARLY
        // (inside its own recommended window, never before it opens) specifically to
        // undercut a car it's closely following that hasn't stopped yet - the same
        // real-world tactic RaceHud already narrates to the player via the "undercut
        // is live" engineer line.
        public bool ShouldAiPitForUndercut(RaceParticipant participant)
        {
            if (participant == null || participant.vehicle == null || participant.vehicle.Tyres == null || participant.lapTracker == null ||
                CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return false;
            }

            if (participant.pitStops != 0 || participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return false;
            }

            // Decision core lives in AiPitStrategyRules; this method supplies
            // the live inputs (window, wear, who is actually ahead and how far).
            RaceParticipant ahead = FindCarAhead(participant, 40f);
            bool rivalAheadUnstopped = ahead != null && ahead.pitStops == 0 && !ahead.retired && !ahead.finished;
            return AiPitStrategyRules.ShouldPitForUndercut(
                participant.lapTracker.CompletedLaps,
                RecommendedPitLap(participant),
                participant.vehicle.Tyres.Wear,
                rivalAheadUnstopped,
                GetIntervalToAheadSeconds(participant));
        }

        // Player-facing counterpart for a parallel HUD pass: identical logic, named
        // for what it means from the player's seat rather than the AI's.
        public bool RecommendedPitUnderSafetyCar(RaceParticipant participant)
        {
            return ShouldAiPitUnderSafetyCar(participant);
        }

    }
}
