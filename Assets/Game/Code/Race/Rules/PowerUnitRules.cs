namespace F1Game.Race.Rules
{
    /// <summary>
    /// Power-unit supply for the 2026 grid, and what that means for development.
    ///
    /// Eleven teams, five engine manufacturers. Only four teams build their own PU;
    /// the other seven are customers, and a customer runs the SAME power unit as its
    /// supplier - not a similar one, the same one. The game had no notion of this at
    /// all: every team's enginePower and ersEfficiency drifted independently over a
    /// career, so Williams could end up with a Mercedes-badged engine two seasons
    /// better than Mercedes' own, and a customer team could out-develop the works
    /// team it buys from. That is the single most visible thing about F1's engine
    /// landscape and it was simply absent.
    ///
    /// 2026 is also the season the rules make this matter most: the new formula moves
    /// to a roughly 50/50 split between the internal combustion engine and the MGU-K
    /// and drops the MGU-H entirely, so power-unit differences are a bigger share of
    /// lap time than they have been for a decade.
    ///
    /// Engine-free (F1Game.Race has no UnityEngine reference).
    /// </summary>
    public static class PowerUnitRules
    {
        public const string Mercedes = "mercedes";
        public const string Ferrari = "ferrari";
        public const string RedBullFord = "red_bull_ford";
        public const string Audi = "audi";
        public const string Honda = "honda";

        /// <summary>
        /// The power unit a team runs. Customers map to their supplier; a works team
        /// maps to its own manufacturer.
        /// </summary>
        public static string SupplierForTeam(string teamId)
        {
            switch (teamId)
            {
                // Mercedes works plus three customers.
                case "mercedes":
                case "mclaren":
                case "williams":
                case "alpine":
                    return Mercedes;

                // Ferrari works plus two customers.
                case "ferrari":
                case "haas":
                case "cadillac":
                    return Ferrari;

                // Red Bull Powertrains with Ford, supplying both Red Bull teams.
                case "red_bull":
                case "rb":
                    return RedBullFord;

                case "audi":
                    return Audi;

                // Honda works partnership.
                case "aston_martin":
                    return Honda;

                default:
                    return Mercedes;
            }
        }

        /// <summary>
        /// Whether this team builds the power unit it races, as opposed to buying it.
        /// A works team gets the benefit of designing the car around its own PU, which
        /// is the part a customer cannot buy.
        /// </summary>
        public static bool IsWorksTeam(string teamId)
        {
            switch (teamId)
            {
                case "mercedes":
                case "ferrari":
                case "red_bull":
                case "audi":
                case "aston_martin":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Manufacturer name for display, e.g. "Red Bull Ford".</summary>
        public static string SupplierDisplayName(string teamId)
        {
            switch (SupplierForTeam(teamId))
            {
                case Ferrari: return "Ferrari";
                case RedBullFord: return "Red Bull Ford";
                case Audi: return "Audi";
                case Honda: return "Honda";
                default: return "Mercedes";
            }
        }

        /// <summary>
        /// Share of a season's development swing that comes from the POWER UNIT rather
        /// than the chassis. Applied to enginePower and ersEfficiency, and shared by
        /// every team on the same supplier, so customers rise and fall with the
        /// manufacturer they buy from instead of drifting independently.
        /// </summary>
        public const float PowerUnitShareOfSwing = 0.75f;

        /// <summary>
        /// The engine-side development delta a team sees: mostly its supplier's swing,
        /// with a small remainder from its own installation and cooling work. A works
        /// team keeps slightly more of its own contribution, because it designs the
        /// car and the PU together.
        /// </summary>
        public static float EngineDeltaForTeam(float supplierDelta, float ownChassisDelta, bool isWorksTeam)
        {
            float share = isWorksTeam ? PowerUnitShareOfSwing : PowerUnitShareOfSwing + 0.15f;
            if (share > 1f)
            {
                share = 1f;
            }

            return supplierDelta * share + ownChassisDelta * (1f - share);
        }
    }
}
