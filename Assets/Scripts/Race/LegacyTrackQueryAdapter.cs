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
                return -1;
            }

            float normalized = distance / track.length;
            return track.IsInDrsZone(normalized) ? track.GetDrsZoneIndex(normalized) : -1;
        }

        public int SectorAt(float distance)
        {
            // Three even sectors from the lap length (legacy timing uses the same split).
            if (track == null || track.length <= 0f)
            {
                return 1;
            }

            float t = Mathf.Clamp01(distance / track.length);
            return Mathf.Clamp(Mathf.FloorToInt(t * 3f) + 1, 1, 3);
        }

        public Vector3 RacingLinePoint(float distance)
        {
            return track != null ? track.RacingLinePointAt(distance) : Vector3.zero;
        }

        public bool TryGridSlot(int index, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (track == null)
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
