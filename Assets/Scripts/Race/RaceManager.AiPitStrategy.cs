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

        public int RecommendedPitLap(RaceParticipant participant)
        {
            // Boundary only: the window math (wet shift, management shift, and
            // the per-driver-stable jitter that keeps a midfield with similar
            // stats from converging on one lap) lives in AiPitStrategyRules.
            bool wetRace = Track != null && (Track.weather == WeatherState.LightRain || Track.weather == WeatherState.HeavyRain);
            float tyreManagement = participant == null || participant.driverData == null ? 78f : participant.driverData.tyreManagement;
            // 0.5 = no jitter (hashed from driverId, so it's the same every
            // time this is called for a given driver this race, never re-rolled).
            float driverJitter01 = participant == null || string.IsNullOrEmpty(participant.driverId) ? 0.5f
                : StableUnitInterval(participant.driverId);
            return AiPitStrategyRules.RecommendedPitLap(RaceLaps, wetRace, tyreManagement / 100f, driverJitter01);
        }

        // Off-by-one fix: RecommendedPitLap returns a 1-based DISPLAY lap number
        // ("pit on lap 3") - CompletedLaps only reaches that number once lap 3 has
        // already been fully driven (i.e. the car is already on lap 4), so
        // comparing raw CompletedLaps against it fires a whole lap late. The
        // player's own auto-pit path already made this exact correction
        // (UpdatePlayerAutoPitStrategy's currentLapNumber = completedLaps + 1);
        // this is the single shared version AiVehicleController now calls instead
        // of re-deriving (and previously getting wrong) the same comparison.
        public bool ShouldAiPitByStrategyLap(RaceParticipant participant)
        {
            if (participant == null || participant.lapTracker == null)
            {
                return false;
            }

            if (CurrentSession == RaceWeekendSession.Qualifying || IsTimeTrial)
            {
                return false;
            }

            if (participant.pitStops > 0 || participant.isPitting || participant.pitPhase != PitPhase.None || participant.pitLimiterUntilExit)
            {
                return false;
            }

            int targetLap = RecommendedPitLap(participant);
            int currentLapNumber = participant.lapTracker.CompletedLaps + 1;

            // Pit-timing fix (per request: AI was pitting a lap early): hold the
            // stop until the car has fully COMPLETED the recommended lap and is
            // running the one after it, rather than triggering the instant it
            // starts the recommended lap. Tyre-wear / SC / undercut triggers
            // elsewhere can still bring a stop forward when the situation
            // genuinely calls for it - this only shifts the routine strategy
            // stop one lap later.
            return currentLapNumber >= targetLap + 1;
        }

        // Voluntary-stop cap: the fastest dry strategy for THIS race length and
        // track temperature already says how many stops are actually worth making
        // (a 5-lap race is a one-stopper, never a two). Once a car has made that
        // many stops, the routine-wear / planned-strategy-lap / undercut triggers
        // stop firing - only the genuine safety nets (destroyed tyre, collapsed
        // grip, weather crossover, mandatory-under-SC) may still bring it in. This
        // is what stops a whole field throwing away a second ~22s stop it never
        // needed - the reported "why so many 2 stops" in short races.
        public bool AiVoluntaryStopsExhausted(RaceParticipant participant)
        {
            if (participant == null)
            {
                return false;
            }

            float trackTempC = Track != null ? Track.trackTemperatureC : TyreStrategyRules.StandardTrackTempC;
            int startCompound;
            int optimalStops;
            TyreStrategyRules.FastestDryStrategy(RaceLaps, trackTempC, out startCompound, out optimalStops);
            return participant.pitStops >= optimalStops;
        }

        // Deterministic, race-independent value in the 0-1 range derived from a
        // string - used to
        // give each driver a small, stable personality offset (pit-window jitter,
        // etc.) without a persistent per-driver RNG state and without ever changing
        // between calls for the same driver within a race.
        static float StableUnitInterval(string key)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < key.Length; i++)
                {
                    hash = hash * 31 + key[i];
                }

                return (hash & 0x7fffffff) / (float)int.MaxValue;
            }
        }

    }
}
