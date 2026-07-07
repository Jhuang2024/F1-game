using System.Collections.Generic;

namespace LocalFormulaRacing
{
    // Part 8: lightweight driver personality tags, derived purely from the
    // existing DriverData stats (no new data files, no separate tuning
    // dials) so a driver's traits are always consistent with how they
    // actually already race, qualify and manage tyres. Used for UI chips
    // (driver rating cards, rivalry cards), career/radio message flavor and
    // a handful of small, bounded AI nudges.
    public static class DriverTraits
    {
        static readonly Dictionary<string, List<string>> cache = new Dictionary<string, List<string>>();

        public static List<string> Compute(DriverData driver)
        {
            if (driver == null)
            {
                return emptyList;
            }

            List<string> cached;
            if (cache.TryGetValue(driver.id, out cached))
            {
                return cached;
            }

            List<string> traits = new List<string>();

            if (driver.overtaking >= 85 && driver.aggression >= 82)
            {
                traits.Add("Aggressive Overtaker");
            }

            if (driver.aggression >= 88 && driver.consistency >= 68)
            {
                traits.Add("Late Braker");
            }

            if (driver.tyreManagement >= 85)
            {
                traits.Add("Tyre Saver");
            }

            if (driver.wetSkill >= 85)
            {
                traits.Add("Wet Specialist");
            }

            if (driver.qualifying >= 90)
            {
                traits.Add("Qualifying Monster");
            }

            if (driver.defending >= 88)
            {
                traits.Add("Defensive Wall");
            }

            if (driver.consistency >= 87 && driver.experience >= 55)
            {
                traits.Add("Consistent Finisher");
            }

            if (driver.consistency <= 45)
            {
                traits.Add("Error-Prone");
            }

            if (driver.experience <= 32)
            {
                traits.Add("Rookie");
            }
            else if (driver.experience >= 88)
            {
                traits.Add("Veteran");
            }

            if (driver.developmentPotential >= 85 && driver.experience <= 55)
            {
                traits.Add("High Potential");
            }

            // Cap at three so a chip row never overflows a card - keep the
            // strongest signals (largest stat margin over its threshold) first.
            if (traits.Count > 3)
            {
                traits = traits.GetRange(0, 3);
            }

            cache[driver.id] = traits;
            return traits;
        }

        static readonly List<string> emptyList = new List<string>();

        public static bool HasTrait(DriverData driver, string trait)
        {
            return Compute(driver).Contains(trait);
        }
    }
}
