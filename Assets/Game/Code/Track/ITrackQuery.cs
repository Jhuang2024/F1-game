using UnityEngine;

namespace F1Game.Track
{
    /// <summary>
    /// Unified track-query interface consumed by the race layer (RaceManager,
    /// vehicle physics, AI, pit logic, race control, HUD/minimap, weather). Two
    /// adapters implement it — one over the legacy procedural TrackRuntime and one
    /// over the authored AuthoredTrackRuntime — so call sites can migrate off
    /// direct TrackManager internals incrementally while either backend supplies
    /// the data. The reference circuit runs through the authored adapter.
    /// </summary>
    public interface ITrackQuery
    {
        float Length { get; }
        bool IsAuthored { get; }

        /// <summary>Centerline distance nearest a world position.</summary>
        float ProgressDistance(Vector3 worldPos);

        /// <summary>Full road width at a lap distance.</summary>
        float WidthAt(float distance);

        /// <summary>Grip multiplier for the surface at a lap distance (1 = baseline).</summary>
        float SurfaceGrip(float distance);

        /// <summary>
        /// Active DRS zone at a lap distance as a ONE-BASED index (1 or 2), or 0
        /// when the distance is not in a zone. This matches TrackRuntime.
        /// GetDrsZoneIndex and the sentinel DrsRules.IsAvailable tests against.
        /// The two implementations of this interface used to disagree here - the
        /// legacy adapter returned 1-based with -1 for none, the authored adapter
        /// returned 0-based with -1 for none - so wiring the authored path to the
        /// rulebook would have read zone one as "no zone" and zone two as "zone one".
        /// </summary>
        int DrsZoneAt(float distance);

        /// <summary>1-based sector at a lap distance.</summary>
        int SectorAt(float distance);

        /// <summary>World point on the AI racing line at a lap distance.</summary>
        Vector3 RacingLinePoint(float distance);

        /// <summary>Grid slot pose; false if the index is out of range.</summary>
        bool TryGridSlot(int index, out Vector3 position, out Quaternion rotation);
    }
}
