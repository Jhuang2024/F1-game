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

            // Two fixes here.
            //
            // (a) The reference is the FLAGGED incident's location, not
            // lastIncidentDistance. That field is the pileup-grouping variable and
            // is overwritten by every incident that registers - including the many
            // Minor ones that never raise a flag at all - so while a yellow was
            // active in sector 1, an unrelated scrape in sector 3 moved this 180m
            // window there. It drives a hard 210 kph cap and the DRS ban, so the
            // player was abruptly pace-limited at a point on the lap where nothing
            // had happened, and the affected stretch jumped around the circuit.
            //
            // (b) Mathf.Abs on a wrapped distance is a no-op: WrapDistance
            // normalises into [0, length), never negative. The value is the forward
            // distance FROM the incident TO the car, so the window only ever caught
            // cars that had already gone past - never cars approaching it, which is
            // precisely the case this fallback exists for. Use true circular
            // distance, the same form SampleCorneringTelemetry already uses.
            return CircularTrackDistance(progress.distance, activeYellowIncidentDistance) < LocalYellowSpeedCapWindowMeters;
        }

        /// <summary>Shortest distance between two lap positions, either way round.</summary>
        float CircularTrackDistance(float a, float b)
        {
            if (Track == null || Track.length <= 0f)
            {
                return Mathf.Abs(a - b);
            }

            float delta = Track.WrapDistance(a - b);
            return Mathf.Min(delta, Track.length - delta);
        }

        float LocalHalfWidthAt(float distance)
        {
            F1Game.Track.ITrackQuery query = TrackQueryProvider.Active;
            return query != null ? query.WidthAt(distance) * 0.5f : Track.HalfWidthAt(distance);
        }

    }
}
