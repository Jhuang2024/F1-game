using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager driver/team identity-resolution subsystem (partial). The
    /// centralized 3-letter driver-code resolution (real DriverData.abbreviation
    /// first, last-name-token fallback only for genuinely custom drivers), the code
    /// token helpers, and the team/car-performance resolution that honours career
    /// driver transfers. Split out of the RaceManager monolith verbatim - same
    /// class, same members, identical resolution rules; the public
    /// GetDisplayDriverCode stays public so every timing/standings/label consumer
    /// resolves in-class.
    /// </summary>
    public partial class RaceManager
    {
        // Centralized driver-code resolution (career identity fix): every
        // consumer - race timing tower, qualifying tower, radio, standings,
        // track map labels, post-race classification - should resolve a
        // driver's displayed 3-letter code through this one function instead of
        // separately guessing from a full name. A real driver (AI, or the
        // player playing as a real driver) always uses their actual
        // DriverData.abbreviation; only a genuinely custom driver with no
        // matching DriverData falls back to parsing a name, and even then uses
        // the LAST name token (the real F1 convention - "PIA" for Oscar
        // Piastri), never the first three letters of the whole concatenated
        // name (the old bug, which produced "OSC").
        public string GetDisplayDriverCode(DriverData driver, string fallbackName)
        {
            if (driver != null && !string.IsNullOrEmpty(driver.abbreviation) && driver.abbreviation.Length >= 3)
            {
                return driver.abbreviation.Substring(0, 3).ToUpperInvariant();
            }

            string nameToParse = driver != null && !string.IsNullOrEmpty(driver.displayName) ? driver.displayName : fallbackName;
            if (!string.IsNullOrEmpty(nameToParse))
            {
                string[] parts = nameToParse.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    return CodeFromToken(parts[parts.Length - 1]);
                }
            }

            return "---";
        }

        string CodeFromToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return "---";
            }

            string upper = token.ToUpperInvariant();
            return upper.Length > 3 ? upper.Substring(0, 3) : upper.PadRight(3, '-');
        }

        // Legacy string-only entry point - now just delegates to
        // GetDisplayDriverCode so every existing caller automatically gets the
        // corrected last-name-token behavior above instead of the old
        // strip-spaces-then-first-three-characters logic.
        string DriverCode(string name)
        {
            return GetDisplayDriverCode(null, name);
        }

        // Part 21 team-performance-evolution hook: resolves whatever a team's
        // CarPerformanceData should actually be for a career race this season -
        // the shared static reference data plus that team's season-to-season
        // TeamPerformanceModifier (every team, applied evenly), plus the
        // player's own upgrade tuning on top if this is the player's own team.
        // Quick Race/Time Trial (IsCareerRace false) always get the raw,
        // unmodified reference car, exactly like before this system existed.
        CarPerformanceData ResolveTeamCarPerformance(TeamData team)
        {
            CarPerformanceData baseCar = team == null ? Data.Cars.cars[0] : Data.FindCar(team.carPerformanceId);
            if (IsCareerRace && Career != null && team != null)
            {
                return Career.GetEffectiveTeamCar(team, baseCar);
            }

            return baseCar;
        }

        // Career standings drift fix: this driver's CURRENT team, accounting for
        // any mid-career transfer (Career.Save.driverTransferRecords), not the raw
        // static DriverData.teamId from drivers.json. Every place that spawns a
        // grid/qualifying entry for an AI driver must resolve team through here -
        // using the raw teamId fed a transferred driver's race/qualifying result
        // (and hence ApplyConstructorPoints) the wrong constructor for the rest of
        // that season, which is exactly what let constructor standings drift away
        // from the sum of their drivers' points.
        TeamData ResolveDriverTeam(DriverData driver)
        {
            if (driver == null)
            {
                return null;
            }

            List<DriverTransferRecord> transfers = Career != null && Career.Save != null ? Career.Save.driverTransferRecords : null;
            string effectiveTeamId = Data.EffectiveTeamId(driver, transfers);
            TeamData team = Data.FindTeam(string.IsNullOrEmpty(effectiveTeamId) ? driver.teamId : effectiveTeamId);
            return team != null ? team : Data.FindTeam(driver.teamId);
        }

    }
}
