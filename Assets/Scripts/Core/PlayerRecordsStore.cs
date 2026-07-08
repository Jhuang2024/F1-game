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

        // Career-wide stat tracking; called once when a race classification is final.
        public static void RecordRaceFinish(int position, int points, bool fastestLap, bool cleanRace, int trackLimitWarnings)
        {
            PlayerRecordsData data = Data;
            data.racesFinished++;
            if (position == 1)
            {
                data.raceWins++;
            }

            if (position >= 1 && position <= 3)
            {
                data.podiums++;
            }

            if (fastestLap)
            {
                data.fastestLaps++;
            }

            if (cleanRace)
            {
                data.cleanRaces++;
            }

            data.totalPoints += Mathf.Max(0, points);
            data.trackLimitWarningsTotal += Mathf.Max(0, trackLimitWarnings);
            LocalJsonStore.Save(RecordsFile, data);
        }

        // Clears every stored per-track best-lap record (time trial and race
        // sessions alike) so the player can start a fresh set of records
        // without touching the separate career-wide stats (wins/podiums/
        // points/etc) tracked above.
        public static void ResetTrackRecords()
        {
            Data.trackRecords.Clear();
            LocalJsonStore.Save(RecordsFile, Data);
        }

        public static void RecordQualifyingResult(int position)
        {
            if (position <= 0)
            {
                return;
            }

            PlayerRecordsData data = Data;
            if (position == 1)
            {
                data.polePositions++;
            }

            if (data.bestQualifyingPosition <= 0 || position < data.bestQualifyingPosition)
            {
                data.bestQualifyingPosition = position;
            }

            LocalJsonStore.Save(RecordsFile, data);
        }
    }
}
