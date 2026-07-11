using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager tyre-compound-selection subsystem (partial). Chooses the
    /// starting compound per participant (player selection or time-trial soft; AI
    /// weather-appropriate wets/inters or a random dry pick) and the next pit
    /// compound (weather override, then the short-stint faster-compound reach, then
    /// the Soft->Medium->Hard ladder). Split out of the RaceManager monolith
    /// verbatim - same class, same members, identical RNG call order and the
    /// aggression/stint-length heuristics; callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        TyreCompound StartingTyreForParticipant(bool player)
        {
            if (player)
            {
                // Time trial is a pure lap-time exercise: always start the player on
                // the fastest slick, whatever compound was last selected elsewhere.
                if (IsTimeTrial)
                {
                    return TyreCompound.Soft;
                }

                return Settings.SelectedTyreCompound;
            }

            if (Track != null && (Track.weather == WeatherState.HeavyRain || Track.weather == WeatherState.LightRain))
            {
                return Track.weather == WeatherState.HeavyRain ? TyreCompound.Wet : TyreCompound.Intermediate;
            }

            // RNG stays here (call order unchanged); the 0-2 roll -> compound
            // mapping is the engine-free TyreStrategyRules.DryStartCompoundFromRoll.
            int roll = Random.Range(0, 3);
            return (TyreCompound)TyreStrategyRules.DryStartCompoundFromRoll(roll);
        }

        TyreCompound NextPitCompound(RaceParticipant participant)
        {
            if (Track.weather == WeatherState.HeavyRain)
            {
                return TyreCompound.Wet;
            }

            if (Track.weather == WeatherState.LightRain)
            {
                return TyreCompound.Intermediate;
            }

            if (participant.vehicle == null || participant.vehicle.Tyres == null)
            {
                return TyreCompound.Medium;
            }

            // Smarter AI strategy: a short remaining stint (late in the race) should
            // reach for a faster compound regardless of the usual Soft->Medium->Hard
            // ladder - there's no tyre-life reason to save rubber that will never be
            // needed again. Aggressive drivers push this a little further than
            // cautious ones. The dry decision (short-stint reach + ladder) lives in
            // the engine-free TyreStrategyRules; this partial owns the live state
            // reads (laps remaining, current compound, driver aggression) and the
            // wet/inter override above, and delegates the dry pick. The compound
            // codes match the TyreCompound enum ordering, so the cast is exact.
            int lapsRemainingAfterStop = participant.lapTracker == null ? RaceLaps : Mathf.Max(0, RaceLaps - participant.lapTracker.CompletedLaps);
            int aggression = participant.driverData == null ? 50 : participant.driverData.aggression;
            int currentCompound = (int)participant.vehicle.Tyres.Compound;
            return (TyreCompound)TyreStrategyRules.NextDryCompound(lapsRemainingAfterStop, aggression, currentCompound);
        }

    }
}
