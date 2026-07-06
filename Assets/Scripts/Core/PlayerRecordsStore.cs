using UnityEngine;

namespace LocalFormulaRacing
{
    // Local persistence for the player's best lap per track (time trial and race sessions).
    public static class PlayerRecordsStore
    {
        const string RecordsFile = "formula_racing_records.json";

        static PlayerRecordsData cached;

        public static PlayerRecordsData Data
        {
            get
            {
                if (cached == null)
                {
                    cached = LocalJsonStore.Load(RecordsFile, new PlayerRecordsData());
                    if (cached.trackRecords == null)
                    {
                        cached.trackRecords = new System.Collections.Generic.List<TrackRecordEntry>();
                    }
                }

                return cached;
            }
        }

        public static float GetBestLap(string trackId)
        {
            if (string.IsNullOrEmpty(trackId))
            {
                return 0f;
            }

            for (int i = 0; i < Data.trackRecords.Count; i++)
            {
                if (Data.trackRecords[i].trackId == trackId)
                {
                    return Data.trackRecords[i].bestLapTime;
                }
            }

            return 0f;
        }

        // Returns true when the lap set a new local track record.
        public static bool TryRecordLap(string trackId, float lapTime, string context)
        {
            if (string.IsNullOrEmpty(trackId) || lapTime <= 0f || lapTime >= 9000f)
            {
                return false;
            }

            TrackRecordEntry entry = null;
            for (int i = 0; i < Data.trackRecords.Count; i++)
            {
                if (Data.trackRecords[i].trackId == trackId)
                {
                    entry = Data.trackRecords[i];
                    break;
                }
            }

            if (entry == null)
            {
                entry = new TrackRecordEntry { trackId = trackId, bestLapTime = 0f, context = context };
                Data.trackRecords.Add(entry);
            }

            if (entry.bestLapTime > 0f && lapTime >= entry.bestLapTime)
            {
                return false;
            }

            entry.bestLapTime = lapTime;
            entry.context = string.IsNullOrEmpty(context) ? "Session" : context;
            LocalJsonStore.Save(RecordsFile, Data);
            return true;
        }
    }
}
