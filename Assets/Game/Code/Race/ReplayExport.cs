using System.Text;

namespace F1Game.Race
{
    /// <summary>
    /// Serializes a captured <see cref="ReplayRecording"/>'s highlight timeline to
    /// CSV for offline review - symmetric with the telemetry CSV export. Only the
    /// markers (flags/overtakes/pit stops/incidents/laps/session) are written, not
    /// every per-frame car transform, so the file stays small and diff-able. Pure
    /// (engine-free, no file IO): the race layer supplies the recording and writes
    /// the returned text. Uses the session-relative clock from
    /// <see cref="ReplayTimeline"/> so rows read like a race report.
    /// </summary>
    public static class ReplayExport
    {
        public const string Header = "time,clock,kind,carIndex,label";

        /// <summary>CSV of the recording's markers; header-only when empty/null.</summary>
        public static string MarkersToCsv(ReplayRecording recording)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            if (recording == null || recording.Markers == null || recording.Markers.Count == 0)
            {
                return sb.ToString();
            }

            System.Collections.Generic.IReadOnlyList<ReplayRecording.Marker> markers = recording.Markers;
            float origin = markers[0].Time;
            for (int i = 1; i < markers.Count; i++)
            {
                if (markers[i].Time < origin) origin = markers[i].Time;
            }

            for (int i = 0; i < markers.Count; i++)
            {
                ReplayRecording.Marker m = markers[i];
                sb.Append(m.Time.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(ReplayTimeline.FormatClock(m.Time - origin)).Append(',')
                  .Append(m.Kind).Append(',')
                  .Append(m.CarIndex).Append(',')
                  .Append(Escape(m.Label)).Append('\n');
            }

            return sb.ToString();
        }

        // Keeps a label with a comma or quote from breaking the CSV columns.
        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
