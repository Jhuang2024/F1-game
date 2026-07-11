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

            int roll = Random.Range(0, 3);
            return roll == 0 ? TyreCompound.Soft : (roll == 1 ? TyreCompound.Medium : TyreCompound.Hard);
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
            // ladder below - there's no tyre-life reason to save rubber that will
            // never be needed again. Aggressive drivers push this a little further
            // than cautious ones.
            int lapsRemainingAfterStop = participant.lapTracker == null ? RaceLaps : Mathf.Max(0, RaceLaps - participant.lapTracker.CompletedLaps);
            if (lapsRemainingAfterStop > 0 && lapsRemainingAfterStop <= 8)
            {
                int aggression = participant.driverData == null ? 50 : participant.driverData.aggression;
                bool pushToSoft = aggression >= 65 || lapsRemainingAfterStop <= 4;
                return pushToSoft ? TyreCompound.Soft : TyreCompound.Medium;
            }

            if (participant.vehicle.Tyres.Compound == TyreCompound.Soft)
            {
                return TyreCompound.Medium;
            }

            if (participant.vehicle.Tyres.Compound == TyreCompound.Medium)
            {
                return TyreCompound.Hard;
            }

            return TyreCompound.Medium;
        }

    }
}
