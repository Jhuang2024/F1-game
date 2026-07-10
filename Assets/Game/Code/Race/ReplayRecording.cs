using System.Collections.Generic;

namespace F1Game.Race
{
    /// <summary>
    /// Engine-free replay recording data model: a bounded ring buffer of
    /// per-frame car transforms plus a marker track (incidents, overtakes, pit
    /// stops, flags, laps). Bounded memory by design — the oldest frames are
    /// overwritten once capacity is reached, so a long race cannot grow without
    /// limit. The runtime replay system (camera/playback) consumes this; keeping
    /// the data model pure makes seeking and serialization testable.
    /// </summary>
    public sealed class ReplayRecording
    {
        public struct CarFrame
        {
            public float PosX, PosY, PosZ;
            public float RotX, RotY, RotZ, RotW;
            public float SpeedKph;
        }

        public struct FrameHeader
        {
            public float Time;
            public int CarCount;
            public int FrameStartIndex; // into the flattened car buffer
        }

        public enum MarkerKind { Incident, Overtake, PitStop, Flag, LapComplete, SessionStart, SessionEnd }

        public struct Marker
        {
            public float Time;
            public MarkerKind Kind;
            public int CarIndex;
            public string Label;
        }

        readonly int capacityFrames;
        readonly int carCount;
        readonly FrameHeader[] headers;
        readonly CarFrame[] carFrames;
        readonly List<Marker> markers = new List<Marker>();

        int head;      // next write slot
        int count;     // frames stored (<= capacity)

        public int CarCount => carCount;
        public int FrameCount => count;
        public IReadOnlyList<Marker> Markers => markers;
        public float SchemaVersion { get; } = 1f;

        public ReplayRecording(int carCount, int capacityFrames)
        {
            this.carCount = carCount < 1 ? 1 : carCount;
            this.capacityFrames = capacityFrames < 1 ? 1 : capacityFrames;
            headers = new FrameHeader[this.capacityFrames];
            carFrames = new CarFrame[this.capacityFrames * this.carCount];
        }

        /// <summary>Records one frame. <paramref name="frame"/> length must equal CarCount.</summary>
        public void RecordFrame(float time, CarFrame[] frame)
        {
            if (frame == null || frame.Length < carCount)
            {
                return;
            }

            int baseIndex = head * carCount;
            for (int i = 0; i < carCount; i++)
            {
                carFrames[baseIndex + i] = frame[i];
            }

            headers[head] = new FrameHeader { Time = time, CarCount = carCount, FrameStartIndex = baseIndex };
            head = (head + 1) % capacityFrames;
            if (count < capacityFrames)
            {
                count++;
            }
        }

        public void AddMarker(float time, MarkerKind kind, int carIndex, string label)
        {
            markers.Add(new Marker { Time = time, Kind = kind, CarIndex = carIndex, Label = label });
        }

        /// <summary>Oldest-to-newest logical index → storage slot.</summary>
        int SlotAt(int logicalIndex)
        {
            int oldest = count < capacityFrames ? 0 : head;
            return (oldest + logicalIndex) % capacityFrames;
        }

        public bool TryGetFrame(int logicalIndex, out FrameHeader header)
        {
            header = default;
            if (logicalIndex < 0 || logicalIndex >= count)
            {
                return false;
            }

            header = headers[SlotAt(logicalIndex)];
            return true;
        }

        public CarFrame GetCar(in FrameHeader header, int carIndex)
        {
            if (carIndex < 0 || carIndex >= carCount)
            {
                return default;
            }

            return carFrames[header.FrameStartIndex + carIndex];
        }

        /// <summary>Nearest recorded frame index at or before a time (seek).</summary>
        public int SeekToTime(float time)
        {
            // Frames are recorded monotonically in time; binary search over the
            // logical order.
            int lo = 0;
            int hi = count - 1;
            int result = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                TryGetFrame(mid, out FrameHeader h);
                if (h.Time <= time)
                {
                    result = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return result;
        }

        public float StartTime => count > 0 && TryGetFrame(0, out FrameHeader h) ? h.Time : 0f;
        public float EndTime => count > 0 && TryGetFrame(count - 1, out FrameHeader h) ? h.Time : 0f;
    }
}
