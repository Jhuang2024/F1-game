using System.Collections.Generic;

namespace LocalFormulaRacing
{
    // Legends mode: an optional all-time-greats grid the player can toggle on in
    // Settings > Race Control ("Legendary Drivers"). When enabled the entire field
    // is replaced by legendary drivers - every one a flat 99 in every skill (a 99
    // overall) - in fully maxed 125-rated cars, so the result comes down to pure
    // racecraft and the roll of the race rather than the machinery underneath.
    //
    // Kept as a self-contained provider so the toggle is a single branch at each
    // roster/car chokepoint (GetDefensiveAiRoster, ResolveTeamCarPerformance,
    // ResolveDriverTeam) and NOTHING in the shipped career/roster data is mutated -
    // flip the toggle off and the normal 2026 grid is untouched.
    public static class LegendaryRoster
    {
        public const int DriverStat = 99;    // flat rating for every legend skill -> 99 overall
        public const int CarStat = 125;      // maxed rating for every car attribute
        public const int CarTopSpeed = 360;  // physical top-speed cap (maps to a 125 rating)

        static DriverData Legend(string id, string name, string abbr, int number, string teamId)
        {
            return new DriverData
            {
                id = id,
                displayName = name,
                abbreviation = abbr,
                number = number,
                teamId = teamId,
                pace = DriverStat,
                racecraft = DriverStat,
                qualifying = DriverStat,
                tyreManagement = DriverStat,
                wetSkill = DriverStat,
                consistency = DriverStat,
                aggression = DriverStat,
                defending = DriverStat,
                overtaking = DriverStat,
                awareness = DriverStat,
                experience = DriverStat,
                developmentPotential = DriverStat,
            };
        }

        // Two legends per 2026 team id, so team colours, the calendar and the
        // team -> car mapping all keep working untouched. Pairings lean into the
        // real history (Senna & Prost at McLaren, Schumacher & Lauda at Ferrari,
        // Mansell & Piquet at Williams, ...).
        public static List<DriverData> All()
        {
            return new List<DriverData>
            {
                Legend("legend_senna", "Ayrton Senna", "SEN", 12, "mclaren"),
                Legend("legend_prost", "Alain Prost", "PRO", 1, "mclaren"),
                Legend("legend_schumacher", "Michael Schumacher", "MSC", 3, "ferrari"),
                Legend("legend_lauda", "Niki Lauda", "LAU", 2, "ferrari"),
                Legend("legend_hamilton", "Lewis Hamilton", "HAM", 44, "mercedes"),
                Legend("legend_fangio", "Juan Manuel Fangio", "FAN", 4, "mercedes"),
                Legend("legend_vettel", "Sebastian Vettel", "VET", 5, "red_bull"),
                Legend("legend_stewart", "Jackie Stewart", "STE", 6, "red_bull"),
                Legend("legend_mansell", "Nigel Mansell", "MAN", 8, "williams"),
                Legend("legend_piquet", "Nelson Piquet", "PIQ", 9, "williams"),
                Legend("legend_alonso", "Fernando Alonso", "ALO", 14, "rb"),
                Legend("legend_hakkinen", "Mika Hakkinen", "HAK", 10, "rb"),
                Legend("legend_raikkonen", "Kimi Raikkonen", "RAI", 7, "aston"),
                Legend("legend_damon_hill", "Damon Hill", "HIL", 11, "aston"),
                Legend("legend_clark", "Jim Clark", "CLA", 13, "haas"),
                Legend("legend_graham_hill", "Graham Hill", "GRA", 15, "haas"),
                Legend("legend_fittipaldi", "Emerson Fittipaldi", "FIT", 16, "audi"),
                Legend("legend_keke_rosberg", "Keke Rosberg", "ROS", 17, "audi"),
                Legend("legend_ascari", "Alberto Ascari", "ASC", 18, "alpine"),
                Legend("legend_moss", "Stirling Moss", "MOS", 19, "alpine"),
                Legend("legend_brabham", "Jack Brabham", "BRA", 20, "cadillac"),
                Legend("legend_villeneuve", "Gilles Villeneuve", "VIL", 27, "cadillac"),
            };
        }

        // The AI legends for a session: the full 22 minus one seat on the player's
        // own team (the player fills that seat, so the team ends up with the player
        // plus one legendary team-mate, never three cars), capped to count.
        public static List<DriverData> AiDrivers(string playerTeamId, int count)
        {
            List<DriverData> all = All();

            if (!string.IsNullOrEmpty(playerTeamId))
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].teamId == playerTeamId)
                    {
                        all.RemoveAt(i);
                        break;
                    }
                }
            }

            if (count >= 0 && all.Count > count)
            {
                all.RemoveRange(count, all.Count - count);
            }

            return all;
        }

        // A maxed clone of a car: every attribute at the ceiling, top speed at the
        // physical cap. The id is preserved so team-colour / car lookups still line
        // up. Never mutates the passed-in reference car.
        public static CarPerformanceData MaxedCar(CarPerformanceData baseCar)
        {
            return new CarPerformanceData
            {
                id = baseCar != null ? baseCar.id : "legend_car",
                topSpeed = CarTopSpeed,
                acceleration = CarStat,
                cornering = CarStat,
                braking = CarStat,
                reliability = CarStat,
                ersEfficiency = CarStat,
                tyreManagement = CarStat,
                aeroEfficiency = CarStat,
                chassisBalance = CarStat,
                enginePower = CarStat,
            };
        }
    }
}
