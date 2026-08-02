using UnityEngine;

namespace F1Game.Track
{
    /// <summary>Exposes an <see cref="AuthoredTrackRuntime"/> through <see cref="ITrackQuery"/>.</summary>
    public sealed class AuthoredTrackQueryAdapter : ITrackQuery
    {
        readonly AuthoredTrackRuntime runtime;

        public AuthoredTrackQueryAdapter(AuthoredTrackRuntime runtime)
        {
            this.runtime = runtime;
        }

        public float Length => runtime.Length;
        public bool IsAuthored => true;

        public float ProgressDistance(Vector3 worldPos) => runtime.GetProgress(worldPos).Distance;

        public float WidthAt(float distance) => runtime.Limits.HalfWidthAt(distance) * 2f;

        public float SurfaceGrip(float distance) => runtime.Surface.GripMultiplierAt(distance);

        // ZoneIndexAt is 0-based with -1 for "none"; the interface contract is
        // 1-based with 0 for "none", so this converts exactly like SectorAt below.
        public int DrsZoneAt(float distance) => runtime.Drs.ZoneIndexAt(distance) + 1;

        public int SectorAt(float distance) => runtime.Sectors.SectorAt(distance) + 1;

        public Vector3 RacingLinePoint(float distance) => runtime.AiLine.RacingLinePoint(distance);

        public bool TryGridSlot(int index, out Vector3 position, out Quaternion rotation)
            => runtime.Grid.TryGetSlot(index, out position, out rotation);
    }
}
