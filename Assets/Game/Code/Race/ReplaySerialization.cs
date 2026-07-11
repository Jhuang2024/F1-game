using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace F1Game.Race
{
    /// <summary>
    /// Engine-free save/load for a <see cref="ReplayRecording"/>: a compact,
    /// human-diffable text format so a session's replay can be persisted and watched
    /// later. Round-trips exactly (floats use the "G9" format under the invariant
    /// culture - 9 significant digits guarantees a Single re-reads to the same bits -
    /// so a reloaded recording is identical to the original). The disk I/O itself
    /// stays in the
    /// engine layer; this owns only the string &lt;-&gt; data conversion, which keeps
    /// it unit-testable.
    /// </summary>
    public static class ReplaySerialization
    {
        const string Magic = "REPLAYv1";

        /// <summary>Serialize a recording to the replay text format.</summary>
        public static string ToText(ReplayRecording recording)
        {
            var sb = new StringBuilder();
            if (recording == null)
            {
                sb.Append(Magic).Append(' ').Append("0 0 0 0\n");
                return sb.ToString();
            }

            int frames = recording.FrameCount;
            int cars = recording.CarCount;
            IReadOnlyList<ReplayRecording.Marker> markers = recording.Markers;
            int markerCount = markers != null ? markers.Count : 0;

            sb.Append(Magic).Append(' ')
              .Append(F(recording.SchemaVersion)).Append(' ')
              .Append(cars).Append(' ')
              .Append(frames).Append(' ')
              .Append(markerCount).Append('\n');

            for (int i = 0; i < frames; i++)
            {
                if (!recording.TryGetFrame(i, out ReplayRecording.FrameHeader header))
                {
                    continue;
                }

                sb.Append('F').Append(' ').Append(F(header.Time));
                for (int c = 0; c < cars; c++)
                {
                    ReplayRecording.CarFrame cf = recording.GetCar(header, c);
                    sb.Append(' ').Append(F(cf.PosX)).Append(' ').Append(F(cf.PosY)).Append(' ').Append(F(cf.PosZ))
                      .Append(' ').Append(F(cf.RotX)).Append(' ').Append(F(cf.RotY)).Append(' ').Append(F(cf.RotZ)).Append(' ').Append(F(cf.RotW))
                      .Append(' ').Append(F(cf.SpeedKph));
                }

                sb.Append('\n');
            }

            for (int i = 0; i < markerCount; i++)
            {
                ReplayRecording.Marker m = markers[i];
                // Label is written last (rest-of-line) so spaces/commas survive;
                // newlines are flattened so a label can never break the line format.
                string label = (m.Label ?? "").Replace('\n', ' ').Replace('\r', ' ');
                sb.Append('M').Append(' ').Append(F(m.Time)).Append(' ')
                  .Append((int)m.Kind).Append(' ').Append(m.CarIndex).Append(' ').Append(label).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Parse the replay text format back into a recording. Returns null when the
        /// text is missing/empty or the header is not a recognized replay.
        /// </summary>
        public static ReplayRecording FromText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0)
            {
                return null;
            }

            string[] head = lines[0].Split(' ');
            if (head.Length < 5 || head[0] != Magic)
            {
                return null;
            }

            if (!TryF(head[2], out float carsF) || !TryF(head[3], out float framesF))
            {
                return null;
            }

            int cars = (int)carsF;
            int frames = (int)framesF;
            if (cars < 1)
            {
                cars = 1;
            }

            // Capacity must hold every stored frame; at least one slot.
            var recording = new ReplayRecording(cars, frames < 1 ? 1 : frames);
            var buffer = new ReplayRecording.CarFrame[cars];

            for (int li = 1; li < lines.Length; li++)
            {
                string line = lines[li];
                if (line.Length == 0)
                {
                    continue;
                }

                if (line[0] == 'F')
                {
                    string[] p = line.Split(' ');
                    // 'F' + time + cars * 8 values.
                    if (p.Length < 2 + cars * 8 || !TryF(p[1], out float time))
                    {
                        continue;
                    }

                    int idx = 2;
                    bool ok = true;
                    for (int c = 0; c < cars && ok; c++)
                    {
                        ok &= TryF(p[idx++], out float px) & TryF(p[idx++], out float py) & TryF(p[idx++], out float pz)
                            & TryF(p[idx++], out float rx) & TryF(p[idx++], out float ry) & TryF(p[idx++], out float rz) & TryF(p[idx++], out float rw)
                            & TryF(p[idx++], out float spd);
                        buffer[c] = new ReplayRecording.CarFrame
                        {
                            PosX = px, PosY = py, PosZ = pz,
                            RotX = rx, RotY = ry, RotZ = rz, RotW = rw,
                            SpeedKph = spd,
                        };
                    }

                    if (ok)
                    {
                        recording.RecordFrame(time, buffer);
                    }
                }
                else if (line[0] == 'M')
                {
                    // "M <time> <kind> <carIndex> <label...>" - label is the rest.
                    string[] p = line.Split(new[] { ' ' }, 5);
                    if (p.Length < 4 || !TryF(p[1], out float time) || !TryI(p[2], out int kind) || !TryI(p[3], out int carIndex))
                    {
                        continue;
                    }

                    string label = p.Length >= 5 ? p[4] : "";
                    recording.AddMarker(time, (ReplayRecording.MarkerKind)kind, carIndex, label);
                }
            }

            return recording;
        }

        static string F(float v) => v.ToString("G9", CultureInfo.InvariantCulture);

        static bool TryF(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        static bool TryI(string s, out int v) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }
}
