using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager spatial-helper subsystem (partial). Whether a car is within the
    /// flagged local-yellow sector / incident-proximity window (IsNearLocalYellowIncident,
    /// with its speed-cap consts) and the shared local track half-width lookup
    /// (LocalHalfWidthAt, consumed across the pit/grid/geometry code). Split out of
    /// the RaceManager monolith verbatim - same class, same members, identical
    /// sector test and geometry; the public entry points stay public so external
    /// callers resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        // Part 2: a local yellow only limits speed for cars actually near the
        // incident that caused it, not the entire lap-third sector it's flagged
        // in - a genuine "progress window around the incident", tighter than the
        // sector-wide overtake ban above (which deliberately stays sector-wide,
        // since that's about not passing near a hazard you might not see yet).
        const float LocalYellowSpeedCapWindowMeters = 180f;
        const float LocalYellowSpeedCapKph = FlagRules.LocalYellowSpeedCapKph;

        public bool IsNearLocalYellowIncident(RaceParticipant participant)
        {
            if (participant == null || State == null || Track == null || CurrentRaceControlState != RaceControlState.YellowSector)
            {
                return false;
            }

            TrackProgress progress = State.GetCurrentProgress(participant);
            // Limiter-duration fix: the cap used to release the moment the car
            // passed a narrow metre-window around the incident, while the
            // yellow FLAG itself covers the whole sector until race control
            // clears it - the limiter visibly ended before the flag did. The
            // cap now holds throughout the flagged sector (the exact same
            // sector test the flag display and the overtaking ban already
            // use), with the incident-proximity window kept as a fallback for
            // a car straddling the sector boundary right next to the incident.
            if (YellowFlagSector >= 0 && progress.sector == YellowFlagSector)
            {
                return true;
            }

            return Mathf.Abs(Track.WrapDistance(progress.distance - lastIncidentDistance)) < LocalYellowSpeedCapWindowMeters;
        }

        float LocalHalfWidthAt(float distance)
        {
            F1Game.Track.ITrackQuery query = TrackQueryProvider.Active;
            return query != null ? query.WidthAt(distance) * 0.5f : Track.HalfWidthAt(distance);
        }

    }
}
