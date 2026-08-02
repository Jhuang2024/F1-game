using System.Collections.Generic;

namespace LocalFormulaRacing
{
    // Legends Championship: a full season the player races against the 21 all-time
    // greats (see LegendaryRoster), with its own standings persisted to its own
    // save file. Kept entirely separate from CareerManager so a legends season can
    // never write into - or read from - a real career save. Mirrors just the slice
    // of the career loop a championship needs: seed standings, pick the current
    // round's event, apply a race result (points + advance the round + roll the
    // season over), and hand the standings tables to the hub UI.
    public class LegendsManager
    {
        const string LegendsFile = "formula_racing_legends.json";
        const int AiFieldSize = 21;   // 21 legends + the player = a 22-car grid

        readonly GameDataRepository data;

        public LegendsSaveData Save { get; private set; }

        public bool HasChampionship => Save != null && Save.active;

        public LegendsManager(GameDataRepository repository)
        {
            data = repository;
            Save = LocalJsonStore.Load<LegendsSaveData>(LegendsFile, null);
        }

        public int TotalRounds => data != null && data.Calendar != null ? data.Calendar.events.Count : 0;

        public CalendarEventData CurrentEvent()
        {
            return data != null ? data.FindEventForRound(Save != null ? Save.round : 1) : null;
        }

        public void StartNewChampionship(string playerName, string playerTeamId)
        {
            LegendsSaveData save = new LegendsSaveData
            {
                active = true,
                season = 1,
                round = 1,
                playerName = string.IsNullOrEmpty(playerName) ? "Player Driver" : playerName,
                playerTeamId = string.IsNullOrEmpty(playerTeamId) ? "mclaren" : playerTeamId,
            };
            SeedStandings(save);
            Save = save;
            Write();
        }

        // Soft reset: mark the championship wrapped up so the hub falls back to the
        // team picker. The file is rewritten (active=false) so it stays consistent
        // across a restart rather than silently reappearing.
        public void Abandon()
        {
            if (Save != null)
            {
                Save.active = false;
                Write();
            }
        }

        void SeedStandings(LegendsSaveData save)
        {
            save.driverStandings = new List<StandingEntry>();
            save.driverStandings.Add(new StandingEntry
            {
                id = "player",
                displayName = save.playerName,
                teamId = save.playerTeamId,
            });

            List<DriverData> ai = LegendaryRoster.AiDrivers(save.playerTeamId, AiFieldSize);
            for (int i = 0; i < ai.Count; i++)
            {
                save.driverStandings.Add(new StandingEntry
                {
                    id = ai[i].id,
                    displayName = ai[i].displayName,
                    teamId = ai[i].teamId,
                });
            }

            save.constructorStandings = data.CreateInitialConstructorStandings();
        }

        // Called by RaceManager when a legends race finishes. results are already in
        // finishing order (position i+1), exactly like CareerManager.ApplyRaceResults.
        public void ApplyRaceResults(CalendarEventData raceEvent, List<RaceResultEntry> results)
        {
            if (Save == null || !Save.active || results == null)
            {
                return;
            }

            for (int i = 0; i < results.Count; i++)
            {
                // Only CLASSIFIED cars score, exactly as in the career championship
                // (CareerManager.ApplyRaceResults). The legends table paid points off
                // the finishing index unconditionally, so a car that retired on lap 1
                // still took championship points here - and with enough retirements it
                // could take the "win". RaceManager.FinishRace has already resolved
                // the 90% distance rule and any disqualification before this runs.
                int points = results[i].classified
                    ? F1Game.Race.Rules.ChampionshipPoints.ForPosition(i + 1)
                    : 0;
                results[i].finishingPosition = i + 1;
                results[i].points = points;
                ApplyDriverPoints(results[i], points);
                ApplyConstructorPoints(results[i].teamId, points, results[i].finishingPosition, results[i].classified);
            }

            Save.raceResults.Add(new RaceResultRecord
            {
                season = Save.season,
                round = Save.round,
                eventName = raceEvent != null ? raceEvent.displayName : "Legends GP",
                results = results,
            });

            Sort(Save.driverStandings);
            Sort(Save.constructorStandings);

            Save.round++;
            if (TotalRounds > 0 && Save.round > TotalRounds)
            {
                string champion = Save.driverStandings.Count > 0 ? Save.driverStandings[0].displayName : "-";
                Save.pastChampions.Add("Season " + Save.season + ": " + champion);
                Save.season++;
                Save.round = 1;
                SeedStandings(Save);   // fresh tables for the new season
            }

            Write();
        }

        void ApplyDriverPoints(RaceResultEntry result, int points)
        {
            StandingEntry entry = Save.driverStandings.Find(item => item.id == result.driverId);
            if (entry == null)
            {
                entry = new StandingEntry
                {
                    id = result.driverId,
                    displayName = result.driverName,
                    teamId = result.teamId,
                };
                Save.driverStandings.Add(entry);
            }
            else
            {
                entry.teamId = result.teamId;
                entry.displayName = result.driverName;
            }

            entry.points += points;
            // An unclassified car has no finishing position in the real
            // classification, so it records no win, podium or countback entry.
            if (!result.classified)
            {
                return;
            }

            entry.RecordFinish(result.finishingPosition);
            if (result.finishingPosition == 1)
            {
                entry.wins++;
            }

            if (result.finishingPosition <= 3)
            {
                entry.podiums++;
            }
        }

        void ApplyConstructorPoints(string teamId, int points, int finishingPosition, bool classified)
        {
            if (string.IsNullOrEmpty(teamId))
            {
                return;
            }

            StandingEntry entry = Save.constructorStandings.Find(item => item.id == teamId);
            if (entry == null)
            {
                TeamData team = data.FindTeam(teamId);
                entry = new StandingEntry
                {
                    id = teamId,
                    displayName = team != null ? team.name : teamId,
                    teamId = teamId,
                };
                Save.constructorStandings.Add(entry);
            }

            entry.points += points;
            // Constructor wins/podiums and the countback histogram were never
            // recorded here at all, so the legends constructors' table showed 0 in
            // both columns all season and any points tie fell straight through to
            // the (previously unstable) sort order.
            if (!classified)
            {
                return;
            }

            entry.RecordFinish(finishingPosition);
            if (finishingPosition == 1)
            {
                entry.wins++;
            }

            if (finishingPosition <= 3)
            {
                entry.podiums++;
            }
        }

        static void Sort(List<StandingEntry> standings)
        {
            standings.Sort((a, b) =>
            {
                // Same real countback as the career championship, plus the same
                // deterministic final tiebreak - this used to call the points/wins/
                // podiums comparator with no stable fallback, so a legends title
                // decided on a tie was settled by introsort partitioning.
                int ranked = F1Game.Race.Rules.ChampionshipPoints.CompareStandingsWithCountback(
                    a.points, a.finishCounts,
                    b.points, b.finishCounts);
                return ranked != 0 ? ranked : string.CompareOrdinal(a.id, b.id);
            });
        }

        void Write()
        {
            if (Save != null)
            {
                LocalJsonStore.Save(LegendsFile, Save);
            }
        }
    }
}
