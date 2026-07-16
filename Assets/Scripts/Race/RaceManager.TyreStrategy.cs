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

                // Wet-race fairness fix (per report - "unexpected wet weather in
                // an expected dry race: all the AI are on inters but I'm on
                // slicks"): weather is rolled per race AFTER the player picks
                // their compound in the pre-race menu, and only the AI branch
                // below reacted to it - a surprise wet race put every AI on the
                // correct rain tyre while the player was left on their
                // pre-selected slick. The player's garage now fits the same
                // weather-correct compound the AI get at the grid; the player's
                // own selection still applies whenever the race starts dry.
                if (Track != null && (Track.weather == WeatherState.HeavyRain || Track.weather == WeatherState.LightRain))
                {
                    return Track.weather == WeatherState.HeavyRain ? TyreCompound.Wet : TyreCompound.Intermediate;
                }

                return Settings.SelectedTyreCompound;
            }

            if (Track != null && (Track.weather == WeatherState.HeavyRain || Track.weather == WeatherState.LightRain))
            {
                return Track.weather == WeatherState.HeavyRain ? TyreCompound.Wet : TyreCompound.Intermediate;
            }

            // RNG stays here (call order unchanged); the 0-2 roll -> compound
            // mapping is the engine-free TyreStrategyRules.DryStartCompoundFromRoll.
            // The temperature-aware overload keeps the AI from starting on a soft
            // that a hot track would burn through inside a single lap.
            int roll = Random.Range(0, 3);
            float startTrackTempC = Track != null ? Track.trackTemperatureC : TyreStrategyRules.StandardTrackTempC;
            return (TyreCompound)TyreStrategyRules.DryStartCompoundFromRoll(roll, startTrackTempC);
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

            // Smarter AI strategy: fit the softest (fastest) compound whose stint
            // life at THIS track temperature still reaches the flag, so the stop is
            // the last one instead of committing to another. The dry decision lives
            // in the engine-free TyreStrategyRules (which owns the temperature->
            // stint-length gradient); this partial owns the live state reads (laps
            // remaining, the session track temperature) and the wet/inter override
            // above, and delegates the dry pick. A hotter track shortens every
            // stint there, so the same call naturally reaches for harder rubber and
            // more stops. The compound codes match the TyreCompound enum ordering,
            // so the cast is exact.
            int lapsRemainingAfterStop = participant.lapTracker == null ? RaceLaps : Mathf.Max(0, RaceLaps - participant.lapTracker.CompletedLaps);
            float trackTempC = Track != null ? Track.trackTemperatureC : TyreStrategyRules.StandardTrackTempC;
            return (TyreCompound)TyreStrategyRules.NextDryCompound(lapsRemainingAfterStop, trackTempC);
        }

    }
}
