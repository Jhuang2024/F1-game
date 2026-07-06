using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    public class GameDataRepository
    {
        public TeamDatabase Teams { get; private set; }
        public DriverDatabase Drivers { get; private set; }
        public CalendarDatabase Calendar { get; private set; }
        public CarPerformanceDatabase Cars { get; private set; }
        public UpgradeDatabase Upgrades { get; private set; }

        public static GameDataRepository Load()
        {
            GameDataRepository repository = new GameDataRepository();
            repository.Teams = LoadResourceJson<TeamDatabase>("Data/teams", new TeamDatabase());
            repository.Drivers = LoadResourceJson<DriverDatabase>("Data/drivers", new DriverDatabase());
            repository.Calendar = LoadResourceJson<CalendarDatabase>("Data/calendar", new CalendarDatabase());
            repository.Cars = LoadResourceJson<CarPerformanceDatabase>("Data/carPerformance", new CarPerformanceDatabase());
            repository.Upgrades = LoadResourceJson<UpgradeDatabase>("Data/upgrades", new UpgradeDatabase());
            repository.EnsureMinimumData();
            return repository;
        }

        static T LoadResourceJson<T>(string resourcePath, T fallback)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null || string.IsNullOrEmpty(asset.text))
            {
                return fallback;
            }

            try
            {
                T parsed = JsonUtility.FromJson<T>(asset.text);
                return parsed == null ? fallback : parsed;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Could not parse " + resourcePath + ": " + exception.Message);
                return fallback;
            }
        }

        void EnsureMinimumData()
        {
            if (Teams.teams.Count == 0)
            {
                Teams.teams.Add(new TeamData
                {
                    id = "williams",
                    name = "Williams",
                    shortName = "WIL",
                    primaryColor = "#2A71D0",
                    secondaryColor = "#F7F7F7",
                    carPerformanceId = "fallback_car",
                    reliability = 80,
                    reputation = 76
                });
            }

            if (Drivers.drivers.Count == 0)
            {
                Drivers.drivers.Add(new DriverData
                {
                    id = "sample_driver",
                    displayName = "Sample Driver",
                    abbreviation = "SAM",
                    teamId = Teams.teams[0].id,
                    pace = 82,
                    racecraft = 82,
                    qualifying = 82,
                    tyreManagement = 82,
                    wetSkill = 82,
                    consistency = 82,
                    aggression = 70,
                    defending = 80,
                    overtaking = 80,
                    awareness = 80,
                    experience = 70,
                    developmentPotential = 80
                });
            }

            if (Calendar.events.Count == 0)
            {
                Calendar.events.Add(new CalendarEventData
                {
                    round = 1,
                    trackId = "bahrain_desert",
                    displayName = "Bahrain-style Desert GP",
                    country = "Local Prototype",
                    laps3 = 3,
                    laps5 = 5,
                    laps25Percent = 14,
                    weatherProfile = "clear_hot"
                });
            }

            if (Cars.cars.Count == 0)
            {
                Cars.cars.Add(new CarPerformanceData
                {
                    id = "fallback_car",
                    topSpeed = 350,
                    acceleration = 88,
                    cornering = 82,
                    braking = 82,
                    reliability = 80,
                    ersEfficiency = 80,
                    tyreManagement = 80,
                    aeroEfficiency = 80,
                    chassisBalance = 80,
                    enginePower = 80
                });
            }
        }

        public TeamData FindTeam(string id)
        {
            for (int i = 0; i < Teams.teams.Count; i++)
            {
                if (Teams.teams[i].id == id)
                {
                    return Teams.teams[i];
                }
            }

            return Teams.teams.Count > 0 ? Teams.teams[0] : null;
        }

        public DriverData FindDriver(string id)
        {
            for (int i = 0; i < Drivers.drivers.Count; i++)
            {
                if (Drivers.drivers[i].id == id)
                {
                    return Drivers.drivers[i];
                }
            }

            return Drivers.drivers.Count > 0 ? Drivers.drivers[0] : null;
        }

        public CarPerformanceData FindCar(string id)
        {
            for (int i = 0; i < Cars.cars.Count; i++)
            {
                if (Cars.cars[i].id == id)
                {
                    return Cars.cars[i];
                }
            }

            return Cars.cars.Count > 0 ? Cars.cars[0] : null;
        }

        public CalendarEventData FindEventForRound(int round)
        {
            for (int i = 0; i < Calendar.events.Count; i++)
            {
                if (Calendar.events[i].round == round)
                {
                    return Calendar.events[i];
                }
            }

            return Calendar.events.Count > 0 ? Calendar.events[0] : null;
        }

        public List<DriverData> GetDriversForTeam(string teamId)
        {
            List<DriverData> result = new List<DriverData>();
            for (int i = 0; i < Drivers.drivers.Count; i++)
            {
                if (Drivers.drivers[i].teamId == teamId)
                {
                    result.Add(Drivers.drivers[i]);
                }
            }

            return result;
        }

        public List<DriverData> GetAiRaceDrivers(string playerTeamId, int count)
        {
            return GetAiRaceDrivers(playerTeamId, count, "");
        }

        public List<DriverData> GetAiRaceDrivers(string playerTeamId, int count, string replacedDriverId)
        {
            List<DriverData> result = new List<DriverData>();
            string replacedSeatId = ResolveReplacedDriverId(playerTeamId, replacedDriverId);
            for (int i = 0; i < Drivers.drivers.Count && result.Count < count; i++)
            {
                if (Drivers.drivers[i].id != replacedSeatId && Drivers.drivers[i].teamId != playerTeamId)
                {
                    result.Add(Drivers.drivers[i]);
                }
            }

            for (int i = 0; i < Drivers.drivers.Count && result.Count < count; i++)
            {
                if (Drivers.drivers[i].id != replacedSeatId && !result.Contains(Drivers.drivers[i]))
                {
                    result.Add(Drivers.drivers[i]);
                }
            }

            return result;
        }

        string ResolveReplacedDriverId(string playerTeamId, string replacedDriverId)
        {
            if (!string.IsNullOrEmpty(replacedDriverId))
            {
                return replacedDriverId;
            }

            for (int i = 0; i < Drivers.drivers.Count; i++)
            {
                if (Drivers.drivers[i].teamId == playerTeamId)
                {
                    return Drivers.drivers[i].id;
                }
            }

            return "";
        }

        public List<StandingEntry> CreateInitialDriverStandings(string playerName, string playerTeamId)
        {
            return CreateInitialDriverStandings(playerName, playerTeamId, "");
        }

        public List<StandingEntry> CreateInitialDriverStandings(string playerName, string playerTeamId, string replacedDriverId)
        {
            List<StandingEntry> standings = new List<StandingEntry>();
            string replacedSeatId = ResolveReplacedDriverId(playerTeamId, replacedDriverId);
            standings.Add(new StandingEntry
            {
                id = "player",
                displayName = playerName,
                teamId = playerTeamId,
                points = 0,
                wins = 0,
                podiums = 0
            });

            for (int i = 0; i < Drivers.drivers.Count; i++)
            {
                if (Drivers.drivers[i].id == replacedSeatId)
                {
                    continue;
                }

                standings.Add(new StandingEntry
                {
                    id = Drivers.drivers[i].id,
                    displayName = Drivers.drivers[i].displayName,
                    teamId = Drivers.drivers[i].teamId,
                    points = 0,
                    wins = 0,
                    podiums = 0
                });
            }

            return standings;
        }

        public List<StandingEntry> CreateInitialConstructorStandings()
        {
            List<StandingEntry> standings = new List<StandingEntry>();
            for (int i = 0; i < Teams.teams.Count; i++)
            {
                standings.Add(new StandingEntry
                {
                    id = Teams.teams[i].id,
                    displayName = Teams.teams[i].name,
                    teamId = Teams.teams[i].id,
                    points = 0,
                    wins = 0,
                    podiums = 0
                });
            }

            return standings;
        }
    }
}
