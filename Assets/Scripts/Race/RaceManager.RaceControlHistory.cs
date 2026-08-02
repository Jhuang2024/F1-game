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
        // Lifetime totals per label. The history list itself is a ROLLING window
        // pruned to MaxRaceControlHistoryEntries, so counting it under-reported the
        // moment a busy race pushed the earliest events off the front - the post-race
        // report and CareerManager's news narrative would then describe fewer yellows
        // and penalties than the race actually had, and the number would silently
        // shrink as the race went on. These counters are never pruned.
        readonly System.Collections.Generic.Dictionary<string, int> raceControlLabelCounts =
            new System.Collections.Generic.Dictionary<string, int>();

        void LogRaceControlHistory(string label, string detail)
        {
            int lap = PlayerParticipant != null && PlayerParticipant.lapTracker != null ? PlayerParticipant.lapTracker.DisplayLap : 0;
            raceControlHistory.Add(new RaceControlHistoryEntry { label = label, detail = detail, raceTimeSeconds = RaceElapsed, lap = lap });
            if (!string.IsNullOrEmpty(label))
            {
                int existing;
                raceControlLabelCounts.TryGetValue(label, out existing);
                raceControlLabelCounts[label] = existing + 1;
            }

            // Every race-control event is a replay timeline marker (single hook).
            replayCapture.AddFlagMarker(RaceElapsed, label);
            if (raceControlHistory.Count > MaxRaceControlHistoryEntries)
            {
                raceControlHistory.RemoveAt(0);
            }
        }

        void ResetRaceControlHistory()
        {
            raceControlHistory.Clear();
            raceControlLabelCounts.Clear();
        }

        int CountRaceControlHistoryLabel(string label)
        {
            int count;
            return raceControlLabelCounts.TryGetValue(label, out count) ? count : 0;
        }

    }
}
