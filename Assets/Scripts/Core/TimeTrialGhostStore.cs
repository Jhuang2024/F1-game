namespace LocalFormulaRacing
{
    // Local persistence for time-trial ghost laps, one best lap per track.
    // Mirrors PlayerRecordsStore's load/cache/save pattern exactly, kept in
    // its own file/JSON blob rather than folded into PlayerRecordsData since
    // a ghost's sample list is a fundamentally different shape (and size)
    // than the flat stat fields that class holds.
    public static class TimeTrialGhostStore
    {
        const string GhostFile = "formula_racing_ghosts.json";
        // Bounds worst-case file size regardless of how long a track/lap is -
        // a recorded lap longer than this is simply resampled to fit before
        // being stored (see RaceManager's ghost recorder).
        public const int MaxSamplesPerGhost = 800;

        static GhostStoreData cached;

        static GhostStoreData Data
        {
            get
            {
                if (cached == null)
                {
                    cached = LocalJsonStore.Load(GhostFile, new GhostStoreData());
                    if (cached.bestGhosts == null)
                    {
                        cached.bestGhosts = new System.Collections.Generic.List<GhostLapData>();
                    }
                }

                return cached;
            }
        }

        public static GhostLapData GetBestGhost(string trackId)
        {
            if (string.IsNullOrEmpty(trackId))
            {
                return null;
            }

            for (int i = 0; i < Data.bestGhosts.Count; i++)
            {
                if (Data.bestGhosts[i].trackId == trackId && Data.bestGhosts[i].samples != null && Data.bestGhosts[i].samples.Count > 1)
                {
                    return Data.bestGhosts[i];
                }
            }

            return null;
        }

        // Returns true and stores the lap when it's faster than whatever ghost
        // is already saved for this track (or there isn't one yet).
        public static bool TrySaveIfBest(string trackId, GhostLapData lap)
        {
            if (string.IsNullOrEmpty(trackId) || lap == null || lap.samples == null || lap.samples.Count < 2 || lap.lapTime <= 0f)
            {
                return false;
            }

            GhostStoreData data = Data;
            for (int i = 0; i < data.bestGhosts.Count; i++)
            {
                if (data.bestGhosts[i].trackId == trackId)
                {
                    if (lap.lapTime >= data.bestGhosts[i].lapTime)
                    {
                        return false;
                    }

                    data.bestGhosts[i] = lap;
                    LocalJsonStore.Save(GhostFile, data);
                    return true;
                }
            }

            data.bestGhosts.Add(lap);
            LocalJsonStore.Save(GhostFile, data);
            return true;
        }

        public static void ClearGhost(string trackId)
        {
            if (string.IsNullOrEmpty(trackId))
            {
                return;
            }

            Data.bestGhosts.RemoveAll(g => g.trackId == trackId);
            LocalJsonStore.Save(GhostFile, Data);
        }
    }
}
