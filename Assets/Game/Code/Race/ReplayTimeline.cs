using System.Collections.Generic;

namespace F1Game.Race
{
    /// <summary>
    /// Turns a captured <see cref="ReplayRecording"/> into an in-game session
    /// timeline: the ordered highlight markers (incidents, overtakes, pit stops,
    /// flags, lap completions) with a formatted session clock, plus per-kind
    /// counts and the recorded window length. Pure over the recording's markers
    /// and frame span, so it can be unit-tested and shown without touching the
    /// race path or needing playback. RaceManager exposes BuildReplayTimeline();
    /// a future replay/highlights UI renders <see cref="Highlights"/>.
    /// </summary>
    public static class ReplayTimeline
    {
        public struct Entry
        {
            public float Time;
            public ReplayRecording.MarkerKind Kind;
            public int CarIndex;
            public string Label;
            /// <summary>Session clock relative to the recording start, e.g. "1:23.4".</summary>
            public string Clock;
        }

        public struct Summary
        {
            public float DurationSeconds;
            public int FrameCount;
            public int CarCount;
            public int IncidentCount;
            public int OvertakeCount;
            public int PitStopCount;
            public int FlagCount;
            public int LapCount;
            public IReadOnlyList<Entry> Highlights;

            public bool HasData => Highlights != null && Highlights.Count > 0;
        }

        public static Summary Build(ReplayRecording recording)
        {
            var summary = new Summary { Highlights = System.Array.Empty<Entry>() };
            if (recording == null)
            {
                return summary;
            }

            summary.FrameCount = recording.FrameCount;
            summary.CarCount = recording.CarCount;

            IReadOnlyList<ReplayRecording.Marker> markers = recording.Markers;
            if (markers == null || markers.Count == 0)
            {
                return summary;
            }

            // The session origin is the earliest marker time (SessionStart is added
            // first), so the clock reads from zero even when the race clock had
            // already advanced when capture began.
            float origin = markers[0].Time;
            for (int i = 1; i < markers.Count; i++)
            {
                if (markers[i].Time < origin) origin = markers[i].Time;
            }

            var highlights = new List<Entry>(markers.Count);
            float lastTime = origin;
            for (int i = 0; i < markers.Count; i++)
            {
                ReplayRecording.Marker m = markers[i];
                if (m.Time > lastTime) lastTime = m.Time;

                switch (m.Kind)
                {
                    case ReplayRecording.MarkerKind.Incident: summary.IncidentCount++; break;
                    case ReplayRecording.MarkerKind.Overtake: summary.OvertakeCount++; break;
                    case ReplayRecording.MarkerKind.PitStop: summary.PitStopCount++; break;
                    case ReplayRecording.MarkerKind.Flag: summary.FlagCount++; break;
                    case ReplayRecording.MarkerKind.LapComplete: summary.LapCount++; break;
                }

                // Session start/end frame the recording; they are counted in the
                // duration but are not player-facing highlight rows.
                if (m.Kind == ReplayRecording.MarkerKind.SessionStart ||
                    m.Kind == ReplayRecording.MarkerKind.SessionEnd)
                {
                    continue;
                }

                highlights.Add(new Entry
                {
                    Time = m.Time,
                    Kind = m.Kind,
                    CarIndex = m.CarIndex,
                    Label = m.Label,
                    Clock = FormatClock(m.Time - origin),
                });
            }

            summary.DurationSeconds = lastTime > origin ? lastTime - origin : 0f;
            summary.Highlights = highlights;
            return summary;
        }

        /// <summary>Formats an elapsed span as m:ss.t (tenths), never negative.</summary>
        public static string FormatClock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int minutes = (int)(seconds / 60f);
            float remainder = seconds - minutes * 60f;
            // Two-digit seconds with one decimal place, e.g. 1:07.3.
            return minutes + ":" + remainder.ToString("00.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
