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

        // Deliberately returns null (not Drivers.drivers[0]) when `id` doesn't
        // match anything: a silent substitute driver here used to mean an
        // unresolved/stale selectedDriverId quietly became a real driver's
        // identity (always the same one - whichever sits first in the data)
        // instead of surfacing as "no driver found", which every caller
        // already has to handle explicitly for the empty-id case anyway.
        public DriverData FindDriver(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < Drivers.drivers.Count; i++)
            {
                if (Drivers.drivers[i].id == id)
                {
                    return Drivers.drivers[i];
                }
            }

            return null;
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

        // Driver market: the effective team a driver is racing for this season,
        // read-time-layered from drivers.json's static teamId + the career's
        // append-only transfer log - never mutates the shared DriverData
        // reference. transfers is optional (defaults null) so every pre-existing
        // caller (Quick Race, Time Trial, any screen with no CareerManager in
        // scope) keeps reading the base roster completely unchanged; only
        // career-aware call sites pass Save.driverTransferRecords. Looks up the
        // MOST RECENT record for this driver (highest season), so a driver
        // transferred twice always resolves to their latest team.
        public string EffectiveTeamId(DriverData driver, List<DriverTransferRecord> transfers)
        {
            if (driver == null)
            {
                return null;
            }

            if (transfers == null || transfers.Count == 0)
            {
                return driver.teamId;
            }

            string latestTeamId = driver.teamId;
            int latestSeason = int.MinValue;
            for (int i = 0; i < transfers.Count; i++)
            {
                DriverTransferRecord record = transfers[i];
                if (record != null && record.driverId == driver.id && record.season > latestSeason)
                {
                    latestSeason = record.season;
                    latestTeamId = record.newTeamId;
                }
            }

            return latestTeamId;
        }

        public List<DriverData> GetDriversForTeam(string teamId)
        {
            return GetDriversForTeam(teamId, null);
        }

        public List<DriverData> GetDriversForTeam(string teamId, List<DriverTransferRecord> transfers)
        {
            List<DriverData> result = new List<DriverData>();
            for (int i = 0; i < Drivers.drivers.Count; i++)
            {
                if (EffectiveTeamId(Drivers.drivers[i], transfers) == teamId)
                {
                    result.Add(Drivers.drivers[i]);
                }
            }

            return result;
        }

        public List<DriverData> GetAiRaceDrivers(string playerTeamId, int count)
        {
            return GetAiRaceDrivers(playerTeamId, count, "", null);
        }

        // Root-cause rewrite: the previous two-pass version filled the roster
        // with every other team's drivers first and only reached the
        // player's own team in a second pass "if there was still room" - on
        // datasets where that room never appeared (or appeared for the
        // wrong reason) the teammate could be skipped entirely, and because
        // the exclusion test only ran against `replacedSeatId`, any drift
        // between that id and the driver the player actually selected could
        // let the selected driver itself slip back in as if they were a
        // separate AI opponent - a visible on-track duplicate of the player.
        // This version makes the teammate assignment explicit and
        // unconditional instead of an emergent side effect of fill order:
        // resolve the player's seat by id, find the other same-team driver
        // by id, and guarantee that driver occupies the seat before anyone
        // else is added. Every comparison is by driver id, never by name.
        public List<DriverData> GetAiRaceDrivers(string playerTeamId, int count, string replacedDriverId)
        {
            return GetAiRaceDrivers(playerTeamId, count, replacedDriverId, null);
        }

        public List<DriverData> GetAiRaceDrivers(string playerTeamId, int count, string replacedDriverId, List<DriverTransferRecord> transfers)
        {
            List<DriverData> result = new List<DriverData>();
            string replacedSeatId = ResolveReplacedDriverId(playerTeamId, replacedDriverId, transfers);

            DriverData teammate = FindTeammateDriver(playerTeamId, replacedSeatId, transfers);
            if (teammate != null && result.Count < count)
            {
                result.Add(teammate);
            }

            for (int i = 0; i < Drivers.drivers.Count && result.Count < count; i++)
            {
                DriverData candidate = Drivers.drivers[i];
                if (candidate.id == replacedSeatId || result.Contains(candidate))
                {
                    continue;
                }

                result.Add(candidate);
            }

            return result;
        }

        public DriverData FindTeammateDriver(string playerTeamId, string playerSeatDriverId)
        {
            return FindTeammateDriver(playerTeamId, playerSeatDriverId, null);
        }

        // The teammate is, by definition, the other driver registered to the
        // player's team whose id is not the seat the player occupies -
        // never a name-based lookup, so a custom driver name that happens to
        // collide with a real driver's display name can never misidentify
        // who the teammate is. "Registered to the player's team" now checks
        // EffectiveTeamId (driver market transfers), not just the static
        // drivers.json teamId, so a mid-career transfer onto the player's team
        // is reflected here too.
        public DriverData FindTeammateDriver(string playerTeamId, string playerSeatDriverId, List<DriverTransferRecord> transfers)
        {
            for (int i = 0; i < Drivers.drivers.Count; i++)
            {
                DriverData candidate = Drivers.drivers[i];
                if (EffectiveTeamId(candidate, transfers) == playerTeamId && candidate.id != playerSeatDriverId)
                {
                    return candidate;
                }
            }

            return null;
        }

        string ResolveReplacedDriverId(string playerTeamId, string replacedDriverId, List<DriverTransferRecord> transfers)
        {
            if (!string.IsNullOrEmpty(replacedDriverId))
            {
                return replacedDriverId;
            }

            for (int i = 0; i < Drivers.drivers.Count; i++)
            {
                if (EffectiveTeamId(Drivers.drivers[i], transfers) == playerTeamId)
                {
                    return Drivers.drivers[i].id;
                }
            }

            return "";
        }

        public List<StandingEntry> CreateInitialDriverStandings(string playerName, string playerTeamId)
        {
            return CreateInitialDriverStandings(playerName, playerTeamId, "", null);
        }

        public List<StandingEntry> CreateInitialDriverStandings(string playerName, string playerTeamId, string replacedDriverId)
        {
            return CreateInitialDriverStandings(playerName, playerTeamId, replacedDriverId, null);
        }

        public List<StandingEntry> CreateInitialDriverStandings(string playerName, string playerTeamId, string replacedDriverId, List<DriverTransferRecord> transfers)
        {
            List<StandingEntry> standings = new List<StandingEntry>();
            string replacedSeatId = ResolveReplacedDriverId(playerTeamId, replacedDriverId, transfers);
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
                    teamId = EffectiveTeamId(Drivers.drivers[i], transfers),
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

        // Part 20: driver-card presentation data. Strengths/weaknesses are the
        // stats that sit clearly above/below this driver's own average (not a
        // fixed threshold), so a well-rounded 90-rated driver and a raw 70-rated
        // rookie both get a sensible 2-3 item list instead of an empty one.
        public DriverProfileSummary GetDriverProfileSummary(DriverData driver)
        {
            DriverProfileSummary summary = new DriverProfileSummary();
            if (driver == null)
            {
                return summary;
            }

            summary.driverId = driver.id;
            summary.displayName = driver.displayName;
            summary.overallRating = driver.OverallRating;

            KeyValuePair<string, int>[] stats =
            {
                new KeyValuePair<string, int>("Pace", driver.pace),
                new KeyValuePair<string, int>("Racecraft", driver.racecraft),
                new KeyValuePair<string, int>("Qualifying", driver.qualifying),
                new KeyValuePair<string, int>("Tyre Management", driver.tyreManagement),
                new KeyValuePair<string, int>("Wet Weather", driver.wetSkill),
                new KeyValuePair<string, int>("Consistency", driver.consistency),
                new KeyValuePair<string, int>("Defending", driver.defending),
                new KeyValuePair<string, int>("Overtaking", driver.overtaking),
                new KeyValuePair<string, int>("Awareness", driver.awareness),
                new KeyValuePair<string, int>("Experience", driver.experience)
            };

            ClassifyStrengthsWeaknesses(stats, summary.strengths, summary.weaknesses);
            summary.archetype = ClassifyDriverArchetype(driver);
            return summary;
        }

        string ClassifyDriverArchetype(DriverData driver)
        {
            if (driver.wetSkill >= driver.pace + 8 && driver.wetSkill >= 80)
            {
                return "Wet Weather Ace";
            }

            if (driver.qualifying >= driver.racecraft + 8)
            {
                return "Qualifying Specialist";
            }

            if (driver.racecraft >= driver.qualifying + 8 || driver.overtaking >= driver.qualifying + 10)
            {
                return "Race-Day Charger";
            }

            if (driver.consistency >= 85 && driver.aggression <= 65)
            {
                return "Metronome";
            }

            if (driver.aggression >= 85 && driver.defending >= 80)
            {
                return "Uncompromising Racer";
            }

            if (driver.experience <= 45 && driver.developmentPotential >= 80)
            {
                return "Rising Talent";
            }

            return "All-Rounder";
        }

        // Part 20: team-rating-card presentation data. Pass the car currently
        // fitted to the team (career code should pass the upgrade-tuned car for
        // the player's own team) so strengths/weaknesses reflect the season so far.
        public TeamProfileSummary GetTeamProfileSummary(TeamData team, CarPerformanceData car)
        {
            TeamProfileSummary summary = new TeamProfileSummary();
            if (team == null)
            {
                return summary;
            }

            summary.teamId = team.id;
            summary.teamName = team.name;

            if (car != null)
            {
                KeyValuePair<string, int>[] stats =
                {
                    new KeyValuePair<string, int>("Top Speed", car.topSpeed / 3),
                    new KeyValuePair<string, int>("Acceleration", car.acceleration),
                    new KeyValuePair<string, int>("Cornering", car.cornering),
                    new KeyValuePair<string, int>("Braking", car.braking),
                    new KeyValuePair<string, int>("Reliability", car.reliability),
                    new KeyValuePair<string, int>("ERS Efficiency", car.ersEfficiency),
                    new KeyValuePair<string, int>("Tyre Management", car.tyreManagement),
                    new KeyValuePair<string, int>("Aero Efficiency", car.aeroEfficiency),
                    new KeyValuePair<string, int>("Chassis Balance", car.chassisBalance),
                    new KeyValuePair<string, int>("Engine Power", car.enginePower)
                };

                ClassifyStrengthsWeaknesses(stats, summary.strengths, summary.weaknesses);
                // Part 6: canonical shared formula - the same one RaceManager's
                // AI balancing, RuntimeUi's team-rating screens, and career R&D
                // all use, so no two screens can ever disagree on this team's
                // overall car rating.
                summary.overallCarRating = RatingCalculator.GetCarOverall(car);
            }

            summary.competitivenessTier = team.reputation >= 88 ? "Championship Contender" :
                team.reputation >= 78 ? "Front-runner" :
                team.reputation >= 68 ? "Midfield" : "Backmarker";
            return summary;
        }

        // Ranks the given (name, value) stat pairs against their own average so
        // the strongest/weakest 2-3 stand out regardless of the driver/car's
        // overall level, then writes the names into the caller's lists.
        void ClassifyStrengthsWeaknesses(KeyValuePair<string, int>[] stats, List<string> strengths, List<string> weaknesses)
        {
            if (stats.Length == 0)
            {
                return;
            }

            float average = 0f;
            for (int i = 0; i < stats.Length; i++)
            {
                average += stats[i].Value;
            }

            average /= stats.Length;

            List<KeyValuePair<string, int>> ranked = new List<KeyValuePair<string, int>>(stats);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            for (int i = 0; i < ranked.Count && strengths.Count < 3; i++)
            {
                if (ranked[i].Value > average + 1f)
                {
                    strengths.Add(ranked[i].Key);
                }
            }

            for (int i = ranked.Count - 1; i >= 0 && weaknesses.Count < 3; i--)
            {
                if (ranked[i].Value < average - 1f)
                {
                    weaknesses.Add(ranked[i].Key);
                }
            }
        }

        // Part 20: heuristic track-characteristics summary for race-weekend
        // depth data (setup recommendation, tyre strategy, pit window). Keyed
        // off the calendar trackId/displayName/weatherProfile the same way
        // TrackManager's own visual-style flags are, but kept independent
        // since this describes gameplay characteristics, not scenery.
        public TrackCharacteristicsSummaryData GetTrackCharacteristics(CalendarEventData eventData)
        {
            TrackCharacteristicsSummaryData summary = new TrackCharacteristicsSummaryData();
            if (eventData == null)
            {
                summary.trackId = "unknown";
                summary.displayName = "Unknown Circuit";
                summary.overtakingDifficulty = "Medium";
                summary.tyreDegradation = "Medium";
                summary.downforceLevel = "Medium";
                summary.estimatedPitLaneLossSeconds = 21f;
                summary.styleDescription = "No calendar data available for this round.";
                return summary;
            }

            string id = eventData.trackId == null ? "" : eventData.trackId.ToLowerInvariant();
            summary.trackId = eventData.trackId;
            summary.displayName = eventData.displayName;
            summary.streetCircuit = id.Contains("monaco") || id.Contains("singapore") || id.Contains("las_vegas") ||
                                     id.Contains("baku") || id.Contains("jeddah");

            bool highSpeed = id.Contains("monza") || id.Contains("spa") || id.Contains("silverstone") ||
                              id.Contains("las_vegas") || id.Contains("baku");
            bool tight = summary.streetCircuit || id.Contains("hungary") || id.Contains("zandvoort");
            bool abrasive = id.Contains("bahrain") || id.Contains("qatar") || id.Contains("barcelona") ||
                             id.Contains("silverstone");

            summary.downforceLevel = highSpeed ? "Low" : tight ? "High" : "Medium";
            summary.overtakingDifficulty = summary.streetCircuit ? "High" : highSpeed ? "Low" : "Medium";
            summary.tyreDegradation = abrasive ? "High" : tight ? "Low" : "Medium";
            summary.estimatedPitLaneLossSeconds = summary.streetCircuit ? 24f : highSpeed ? 18f : 21f;

            summary.styleDescription = summary.streetCircuit
                ? summary.displayName + " is a tight street circuit - track position and clean laps matter more than outright pace."
                : highSpeed
                    ? summary.displayName + " rewards a low-downforce setup and strong straight-line speed."
                    : tight
                        ? summary.displayName + " is a technical, high-downforce lap where mechanical grip counts."
                        : summary.displayName + " is a balanced, all-round circuit with no single dominant trait.";
            return summary;
        }
    }
}
