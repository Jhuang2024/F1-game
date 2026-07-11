using System.IO;
using F1Game.Core.Diagnostics;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager race-end debrief logging (partial). Compact telemetry +
    /// replay-highlight summaries to the diagnostics log at race end, and the
    /// opt-in CSV exports (both default-off prefs). Split out of the monolith
    /// verbatim; reads the capture accessors on the main partial.
    /// </summary>
    public partial class RaceManager
    {
        // Race-end engineer debrief from the live telemetry capture: a compact
        // one-line summary of the player's session (speed, pedal-time share, DRS,
        // tyre wear) into the diagnostics log. This is the runtime consumer that
        // makes the telemetry capture visibly useful without any UI - a future
        // debrief panel reads the same TelemetryDebrief.Summary via
        // BuildTelemetryDebrief(). No-op when capture is off or empty.
        void LogTelemetryDebrief()
        {
            TelemetryDebrief.Summary debrief = BuildTelemetryDebrief();
            if (!debrief.HasData)
            {
                return;
            }

            GameLog.Info(LogCategory.Race,
                "[Debrief] samples=" + debrief.SampleCount +
                " top=" + debrief.TopSpeedKph.ToString("0") + "kph" +
                " avg=" + debrief.AverageSpeedKph.ToString("0") + "kph" +
                " fullThrottle=" + debrief.FullThrottlePercent.ToString("0") + "%" +
                " braking=" + debrief.BrakingPercent.ToString("0") + "%" +
                " coasting=" + debrief.CoastingPercent.ToString("0") + "%" +
                " drs=" + debrief.DrsPercent.ToString("0") + "%" +
                " tyreWear=" + (debrief.TyreWearDelta01 * 100f).ToString("0") + "%");

            // Opt-in CSV export of the full player trace (engineer analysis).
            // Off by default so an ordinary race never writes to disk; enabled
            // via the f1game_telemetry_csv_export pref. Filenamed by track so
            // repeated runs on a circuit overwrite rather than pile up.
            if (PlayerPrefs.GetInt(TelemetryCsvExportKey, 0) == 1)
            {
                string trackId = EventData != null ? EventData.trackId : "session";
                string path = ExportTelemetryCsv("telemetry_" + trackId + ".csv");
                if (!string.IsNullOrEmpty(path))
                {
                    GameLog.Info(LogCategory.Race, "[Debrief] telemetry CSV exported: " + path);
                }
            }
        }

        const string TelemetryCsvExportKey = "f1game_telemetry_csv_export";

        // Race-end summary from the live replay capture: the highlight-marker
        // tally (flags/overtakes/pit stops/incidents/laps) and recorded window
        // length into the diagnostics log. The runtime consumer that makes the
        // replay capture visibly useful without playback UI; a future
        // replay/highlights view reads the same ReplayTimeline.Summary via
        // BuildReplayTimeline(). No-op when capture is off or has no markers.
        void LogReplaySummary()
        {
            F1Game.Race.ReplayTimeline.Summary timeline = BuildReplayTimeline();
            if (!timeline.HasData)
            {
                return;
            }

            GameLog.Info(LogCategory.Race,
                "[Replay] frames=" + timeline.FrameCount +
                " cars=" + timeline.CarCount +
                " length=" + F1Game.Race.ReplayTimeline.FormatClock(timeline.DurationSeconds) +
                " flags=" + timeline.FlagCount +
                " overtakes=" + timeline.OvertakeCount +
                " pits=" + timeline.PitStopCount +
                " incidents=" + timeline.IncidentCount +
                " highlights=" + timeline.Highlights.Count);

            // Opt-in CSV export of the highlight timeline (symmetric with the
            // telemetry CSV): off by default so a race never writes to disk,
            // track-named so reruns overwrite.
            if (PlayerPrefs.GetInt(ReplayCsvExportKey, 0) == 1)
            {
                string trackId = EventData != null ? EventData.trackId : "session";
                string csv = F1Game.Race.ReplayExport.MarkersToCsv(ReplayRecording);
                string path = System.IO.Path.Combine(Application.persistentDataPath, "replay_" + trackId + ".csv");
                System.IO.File.WriteAllText(path, csv);
                GameLog.Info(LogCategory.Race, "[Replay] timeline CSV exported: " + path);
            }
        }

        const string ReplayCsvExportKey = "f1game_replay_export";
    }
}
