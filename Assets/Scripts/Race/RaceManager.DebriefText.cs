namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager debrief-text subsystem (partial). Builds the results-screen
    /// text lines from the live captures - the telemetry driving summary
    /// (speed/throttle/brake/DRS/tyre), the replay race-events summary
    /// (overtakes/incidents/pit stops) and the combined debrief line. Split out of
    /// the RaceManager monolith verbatim - same class, same members, identical
    /// formatting; reads BuildTelemetryDebrief/BuildReplayTimeline on the main
    /// partial, and the public entry points stay public so the results screen
    /// resolves in-class.
    /// </summary>
    public partial class RaceManager
    {
        /// <summary>
        /// Compact one-line engineer debrief for the results screen subtitle
        /// (empty when telemetry capture is off / produced no samples).
        /// </summary>
        public string TelemetryDebriefLine()
        {
            TelemetryDebrief.Summary d = BuildTelemetryDebrief();
            if (!d.HasData)
            {
                return "";
            }

            return "Top " + d.TopSpeedKph.ToString("0") + "kph · " +
                   d.FullThrottlePercent.ToString("0") + "% full throttle · " +
                   d.BrakingPercent.ToString("0") + "% braking · DRS " +
                   d.DrsPercent.ToString("0") + "% · tyre wear " +
                   (d.TyreWearDelta01 * 100f).ToString("0") + "%";
        }

        /// <summary>
        /// Race-events summary from the live replay capture (overtakes / incidents
        /// / pit stops), for the results screen. Empty when nothing to report.
        /// </summary>
        public string ReplayHighlightLine()
        {
            F1Game.Race.ReplayTimeline.Summary t = BuildReplayTimeline();
            if (!t.HasData)
            {
                return "";
            }

            return t.OvertakeCount + " overtakes · " +
                   t.IncidentCount + " incidents · " +
                   t.PitStopCount + " pit stops";
        }

        /// <summary>
        /// Combined results-screen debrief: race-events summary (replay) over the
        /// player's driving summary (telemetry). Either half is omitted when its
        /// capture produced nothing; returns empty when both are empty.
        /// </summary>
        public string RaceDebriefLine()
        {
            string events = ReplayHighlightLine();
            string driving = TelemetryDebriefLine();
            if (string.IsNullOrEmpty(events))
            {
                return driving;
            }

            return string.IsNullOrEmpty(driving) ? events : events + "\n" + driving;
        }

    }
}
