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

                    if (cached.trackAchievements == null)
                    {
                        cached.trackAchievements = new System.Collections.Generic.List<TrackAchievementEntry>();
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

        // Career-wide stat tracking; called once when a race classification is
        // final. Trophy Cabinet additions (trackId/gridPosition/overtakes/wet)
        // are all optional with safe defaults so QuickRace/other callers that
        // don't have every piece of context can still call this without
        // breaking - only the fields they pass real data for get updated.
        public static void RecordRaceFinish(int position, int points, bool fastestLap, bool cleanRace, int trackLimitWarnings,
            string trackId = null, int gridPosition = 0, int overtakesMade = 0, bool wetRace = false)
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
                data.currentCleanRaceStreak++;
                data.cleanRaces++;
                if (data.currentCleanRaceStreak > data.bestCleanRaceStreak)
                {
                    data.bestCleanRaceStreak = data.currentCleanRaceStreak;
                }
            }
            else
            {
                data.currentCleanRaceStreak = 0;
            }

            data.totalPoints += Mathf.Max(0, points);
            data.trackLimitWarningsTotal += Mathf.Max(0, trackLimitWarnings);
            data.mostOvertakesInRace = Mathf.Max(data.mostOvertakesInRace, overtakesMade);

            if (position > 0 && gridPosition > position)
            {
                int comeback = gridPosition - position;
                data.biggestComebackPositions = Mathf.Max(data.biggestComebackPositions, comeback);
            }

            if (wetRace && position > 0 && (data.bestWetFinishPosition <= 0 || position < data.bestWetFinishPosition))
            {
                data.bestWetFinishPosition = position;
            }

            if (!string.IsNullOrEmpty(trackId))
            {
                TrackAchievementEntry achievement = FindOrCreateAchievement(data, trackId);
                if (position == 1)
                {
                    achievement.wins++;
                }

                if (position >= 1 && position <= 3)
                {
                    achievement.podiums++;
                }
            }

            LocalJsonStore.Save(RecordsFile, data);
        }

        static TrackAchievementEntry FindOrCreateAchievement(PlayerRecordsData data, string trackId)
        {
            for (int i = 0; i < data.trackAchievements.Count; i++)
            {
                if (data.trackAchievements[i].trackId == trackId)
                {
                    return data.trackAchievements[i];
                }
            }

            TrackAchievementEntry created = new TrackAchievementEntry { trackId = trackId };
            data.trackAchievements.Add(created);
            return created;
        }

        // Season-level trophies: called from CareerManager at season-end
        // (completedSeasons always increments; championship counts only when
        // the player's own driver/team standing is actually first).
        public static void RecordSeasonCompleted(bool wonDriverChampionship, bool wonConstructorsChampionship)
        {
            PlayerRecordsData data = Data;
            data.completedSeasons++;
            if (wonDriverChampionship)
            {
                data.championshipsWon++;
            }

            if (wonConstructorsChampionship)
            {
                data.constructorsChampionshipsWon++;
            }

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

        public static void RecordQualifyingResult(int position, string trackId = null)
        {
            if (position <= 0)
            {
                return;
            }

            PlayerRecordsData data = Data;
            if (position == 1)
            {
                data.polePositions++;
                if (!string.IsNullOrEmpty(trackId))
                {
                    FindOrCreateAchievement(data, trackId).poles++;
                }
            }

            if (data.bestQualifyingPosition <= 0 || position < data.bestQualifyingPosition)
            {
                data.bestQualifyingPosition = position;
            }

            LocalJsonStore.Save(RecordsFile, data);
        }
    }
}
