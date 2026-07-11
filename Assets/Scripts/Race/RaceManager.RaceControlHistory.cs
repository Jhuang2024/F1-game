namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager race-control history subsystem (partial). Records each
    /// race-control event into the rolling history (also emitting a replay-timeline
    /// flag marker and pruning to the cap) and counts entries by label. Split out
    /// of the RaceManager monolith verbatim - same class, same members, identical
    /// cap and marker hook; the history list and RaceControlHistoryEntry nested type
    /// stay in main and resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        void LogRaceControlHistory(string label, string detail)
        {
            int lap = PlayerParticipant != null && PlayerParticipant.lapTracker != null ? PlayerParticipant.lapTracker.DisplayLap : 0;
            raceControlHistory.Add(new RaceControlHistoryEntry { label = label, detail = detail, raceTimeSeconds = RaceElapsed, lap = lap });
            // Every race-control event is a replay timeline marker (single hook).
            replayCapture.AddFlagMarker(RaceElapsed, label);
            if (raceControlHistory.Count > MaxRaceControlHistoryEntries)
            {
                raceControlHistory.RemoveAt(0);
            }
        }

        int CountRaceControlHistoryLabel(string label)
        {
            int count = 0;
            for (int i = 0; i < raceControlHistory.Count; i++)
            {
                if (raceControlHistory[i].label == label)
                {
                    count++;
                }
            }

            return count;
        }

    }
}
