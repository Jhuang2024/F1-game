using F1Game.Track;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// Exposes the legacy procedural <see cref="TrackRuntime"/> through the shared
    /// <see cref="ITrackQuery"/> interface, so race-layer call sites can migrate
    /// off direct TrackManager internals onto the interface incrementally while
    /// the legacy backend still supplies the data. The authored adapter
    /// (F1Game.Track) is the drop-in replacement per circuit.
    /// </summary>
    public sealed class LegacyTrackQueryAdapter : ITrackQuery
    {
        readonly TrackRuntime track;

        public LegacyTrackQueryAdapter(TrackRuntime track)
        {
            this.track = track;
        }

        public float Length => track != null ? track.length : 0f;
        public bool IsAuthored => false;

        public float ProgressDistance(Vector3 worldPos)
        {
            return track != null ? track.GetProgress(worldPos).distance : 0f;
        }

        public float WidthAt(float distance)
        {
            return track != null ? track.HalfWidthAt(distance) * 2f : 0f;
        }

        public float SurfaceGrip(float distance)
        {
            // The legacy runtime models surface grip through rubber/wetness state
            // rather than a single scalar; baseline until that is exposed here.
            return 1f;
        }

        public int DrsZoneAt(float distance)
        {
            if (track == null || track.length <= 0f)
            {
                return 0;
            }

            // Callers routinely pass look-ahead distances (progress.distance +
            // lookAhead) that run past the end of the lap. Dividing without
            // wrapping produced normalized > 1, which IsInZone treats as inside
            // ANY wrapping zone unconditionally. Wrap first.
            float normalized = track.WrapDistance(distance) / track.length;
            // GetDrsZoneIndex is already the interface's 1-based/0-for-none form.
            return track.GetDrsZoneIndex(normalized);
        }

        public int SectorAt(float distance)
        {
            // Three even sectors from the lap length. The split literals must match
            // TrackProgress.sector / LapTracker exactly - using 1/3 and 2/3 here
            // while those use 0.333/0.666 put a car sitting on a sector line in
            // two different sectors depending on which API was asked.
            if (track == null || track.length <= 0f)
            {
                return 1;
            }

            float t = Mathf.Clamp01(track.WrapDistance(distance) / track.length);
            return t < 0.333f ? 1 : (t < 0.666f ? 2 : 3);
        }

        public Vector3 RacingLinePoint(float distance)
        {
            return track != null ? track.RacingLinePointAt(distance) : Vector3.zero;
        }

        public bool TryGridSlot(int index, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            // "Try" that always returned true: an out-of-range index silently
            // produced a pose on top of an existing slot instead of telling the
            // caller the slot doesn't exist.
            if (track == null || index < 0 || index >= TrackRuntime.GridSlotCount)
            {
                return false;
            }

            track.GetGridSlot(index, out float distance, out float lateralOffset);
            track.SampleAtDistance(distance, out Vector3 point, out Vector3 forward, out Vector3 right);
            position = point + right * lateralOffset;
            rotation = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
            return true;
        }
    }
}
