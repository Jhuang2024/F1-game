using System.Collections.Generic;
using F1Game.Core.Diagnostics;
using F1Game.Core.Events;
using F1Game.Race.Rules;
using UnityEngine;

namespace LocalFormulaRacing
{
    /// <summary>
    /// RaceManager qualifying-field composition subsystem (partial). Builds the
    /// simulated qualifying field and driver lineup - the real/simulated entries,
    /// the player's resolved qualifying driver data, the replaced-driver identity
    /// for the player's team, the defensive AI roster and the per-phase AI target
    /// preparation. Split out of the RaceManager monolith verbatim - same class,
    /// same members, identical field composition, RNG call order and tuned values;
    /// the sim nested types stay main-nested and resolve in-class.
    /// </summary>
    public partial class RaceManager
    {
        void BuildQualifyingField(string playerTeamId)
        {
            QualifyingSimEntry playerEntry = qualifyingEntries.Find(item => item.isPlayer);
            if (playerEntry == null)
            {
                playerEntry = new QualifyingSimEntry
                {
                    driverId = PlayerParticipant.driverId,
                    driverName = PlayerParticipant.driverName,
                    teamId = PlayerParticipant.teamId,
                    isPlayer = true
                };
                qualifyingEntries.Add(playerEntry);
            }

            playerEntry.participant = PlayerParticipant;
            playerEntry.carData = PlayerParticipant.carData;

            if (qualifyingEntries.Count > 1)
            {
                return;
            }

            List<DriverData> aiDrivers = GetDefensiveAiRoster(playerTeamId, PlayerParticipant != null ? PlayerParticipant.driverName : "");
            for (int i = 0; i < aiDrivers.Count; i++)
            {
                DriverData driver = aiDrivers[i];
                TeamData team = ResolveDriverTeam(driver);
                CarPerformanceData car = ResolveTeamCarPerformance(team);
                qualifyingEntries.Add(new QualifyingSimEntry
                {
                    driverId = driver.id,
                    driverName = driver.displayName,
                    teamId = team == null ? driver.teamId : team.id,
                    driverData = driver,
                    carData = car,
                    isPlayer = false
                });
            }
        }

        void BuildSimulatedQualifyingField(string playerName, string playerTeamId)
        {
            qualifyingEntries.Clear();
            TeamData playerTeam = Data.FindTeam(playerTeamId);
            CarPerformanceData playerCar = Career == null ? null : Career.GetPlayerCar();
            if (playerCar == null)
            {
                playerCar = playerTeam == null ? Data.Cars.cars[0] : Data.FindCar(playerTeam.carPerformanceId);
            }

            DriverData playerQualiDriver = ResolvePlayerQualifyingDriverData(playerName, playerTeamId);
            qualifyingEntries.Add(new QualifyingSimEntry
            {
                driverId = "player",
                driverName = string.IsNullOrEmpty(playerName) ? "Player Driver" : playerName,
                teamId = playerTeam == null ? playerTeamId : playerTeam.id,
                driverData = playerQualiDriver,
                carData = playerCar,
                isPlayer = true
            });

            List<DriverData> aiDrivers = GetDefensiveAiRoster(playerTeamId, playerName);
            for (int i = 0; i < aiDrivers.Count; i++)
            {
                DriverData driver = aiDrivers[i];
                TeamData team = ResolveDriverTeam(driver);
                CarPerformanceData car = ResolveTeamCarPerformance(team);
                qualifyingEntries.Add(new QualifyingSimEntry
                {
                    driverId = driver.id,
                    driverName = driver.displayName,
                    teamId = team == null ? driver.teamId : team.id,
                    driverData = driver,
                    carData = car,
                    isPlayer = false
                });
            }
        }

        DriverData ResolvePlayerQualifyingDriverData(string playerName, string playerTeamId)
        {
            // Part 1/4/20: route through the career's own effective-driver
            // pipeline (season rating progression + team transfers) instead of
            // reading either a real driver's raw drivers.json stats or a
            // custom driver's identity with no progression applied - this is
            // the same GetEffectiveDriver every AI driver on the grid goes
            // through, so the player's own seat is never a special case that
            // silently skips career progression.
            if (Career != null && Career.Save != null)
            {
                DriverData effective = Career.GetEffectivePlayerDriver();
                if (effective != null)
                {
                    return effective;
                }
            }

            // Balance fix: driver skill used to be derived from the TEAM CAR's
            // own performance stats (cornering/enginePower/aero/braking), which
            // double-counted every car upgrade - once as a faster car via
            // carEffect in SimulateQualifyingRunDetailed, and again here as a
            // "better driver" via qualifying/pace/consistency, which is why
            // upgrades alone used to push the player toward pole almost every
            // simulated session. The car's contribution is already fully and
            // solely represented by carEffect; this rating now stands on its
            // own as a driver-skill number - a stable baseline nudged only by
            // career reputation (itself now a small, capped swing so it can
            // never compound into a second source of car-driven advantage),
            // never by the car underneath.
            float reputationBonus = Career == null || Career.Save == null ? 0f : Mathf.Clamp((Career.Save.reputation - 50f) * 0.10f, -6f, 6f);
            int qualifying = Mathf.Clamp(Mathf.RoundToInt(76f + reputationBonus + Random.Range(-3f, 3f)), 58, 90);
            int consistency = Mathf.Clamp(qualifying - 2 + (Career == null || Career.Save == null ? 0 : Career.Save.currentSeason), 55, 92);
            return new DriverData
            {
                id = "player",
                displayName = string.IsNullOrEmpty(playerName) ? "Player Driver" : playerName,
                abbreviation = DriverCode(playerName),
                teamId = playerTeamId,
                pace = Mathf.Clamp(qualifying - 1, 50, 99),
                racecraft = Mathf.Clamp(qualifying - 3, 50, 99),
                qualifying = qualifying,
                tyreManagement = Mathf.Clamp(qualifying - 4, 50, 96),
                wetSkill = Mathf.Clamp(qualifying - 2, 50, 96),
                consistency = consistency,
                aggression = 72,
                defending = Mathf.Clamp(qualifying - 6, 45, 94),
                overtaking = Mathf.Clamp(qualifying - 4, 45, 94),
                awareness = consistency,
                experience = Mathf.Clamp(70 + (Career == null || Career.Save == null ? 0 : Career.Save.currentSeason * 2), 60, 94),
                developmentPotential = 84
            };
        }

        string ReplacedDriverIdForPlayerTeam(string playerTeamId)
        {
            List<DriverData> teamDrivers = Data.GetDriversForTeam(playerTeamId, Career != null && Career.Save != null ? Career.Save.driverTransferRecords : null);

            // Teammate-duplicate fix: this used to trust Career.Save.selectedDriverId
            // completely whenever it was non-empty, with no validation - a save
            // whose selectedDriverId was ever empty/stale (a real possibility for
            // any career predating a stricter write-side fix, since nothing ever
            // repaired it on load) fell through to "the first driver on this
            // team", which is backwards: on a two-real-driver team that IS the
            // teammate's id, not the player's own. FindTeammateDriver then
            // excluded the teammate (mistaken for the player) and handed back
            // the player's OWN driver record as their "teammate" - the reported
            // bug of racing against a duplicate of yourself instead of your
            // real teammate. Now validated against this team's actual roster
            // before being trusted, with a same-name recovery path (and a
            // write-back repair) before ever falling back to "just pick one".
            if (Career != null && Career.Save != null && !string.IsNullOrEmpty(Career.Save.selectedDriverId) &&
                teamDrivers.Exists(d => d.id == Career.Save.selectedDriverId))
            {
                return Career.Save.selectedDriverId;
            }

            if (Career != null && Career.Save != null && !string.IsNullOrEmpty(Career.Save.playerDriverName))
            {
                DriverData byName = teamDrivers.Find(d => string.Equals(d.displayName, Career.Save.playerDriverName, System.StringComparison.OrdinalIgnoreCase));
                if (byName != null)
                {
                    Career.Save.selectedDriverId = byName.id;
                    return byName.id;
                }
            }

            return teamDrivers.Count > 0 ? teamDrivers[0].id : "";
        }

        // Roster/participant construction, root-caused: the player occupies
        // exactly one seat on their team, identified purely by driver id
        // (Career.Save.selectedDriverId when playing as a real driver, or the
        // team's default seat id otherwise - see ReplacedDriverIdForPlayerTeam).
        // The teammate is, unconditionally, "the other driver registered to
        // that same team whose id is not the player's seat id" -
        // Data.GetAiRaceDrivers/FindTeammateDriver now guarantee that driver
        // is resolved and included by id before anything else fills the
        // field, so there is no fill-order or count arithmetic for the
        // teammate's presence to depend on. This is the one place all three
        // roster builders (race grid, live qualifying, sim qualifying) funnel
        // through; the loop below is a pure id-based safety net (never a
        // display-name comparison, which could misfire on a coincidental
        // name match) in case a stale save or future caller ever hands in a
        // roster that still contains the player's own seat id.
        List<DriverData> GetDefensiveAiRoster(string playerTeamId, string playerDisplayName)
        {
            List<DriverTransferRecord> transfers = Career != null && Career.Save != null ? Career.Save.driverTransferRecords : null;
            string replacedId = ReplacedDriverIdForPlayerTeam(playerTeamId);
            List<DriverData> aiDrivers = Data.GetAiRaceDrivers(playerTeamId, FullWeekendAiCount, replacedId, transfers);

            for (int i = aiDrivers.Count - 1; i >= 0; i--)
            {
                if (aiDrivers[i].id == replacedId)
                {
                    GameLog.Warn("[Roster] Removed AI driver '" + aiDrivers[i].displayName + "' (" + aiDrivers[i].id + ") - matches the player's own seat id.");
                    aiDrivers.RemoveAt(i);
                }
            }

            DriverData teammate = Data.FindTeammateDriver(playerTeamId, replacedId, transfers);
            if (teammate != null)
            {
                bool teammateIncluded = false;
                for (int j = 0; j < aiDrivers.Count; j++)
                {
                    if (aiDrivers[j].id == teammate.id)
                    {
                        teammateIncluded = true;
                        break;
                    }
                }

                if (!teammateIncluded)
                {
                    GameLog.Warn("[Roster] Teammate '" + teammate.displayName + "' (" + teammate.id + ") was missing from the AI roster - adding explicitly.");
                    if (aiDrivers.Count >= FullWeekendAiCount && aiDrivers.Count > 0)
                    {
                        aiDrivers.RemoveAt(aiDrivers.Count - 1);
                    }

                    aiDrivers.Insert(0, teammate);
                }
            }

            // Driver market + progression: this is the single chokepoint all
            // three roster builders (race grid, live qualifying, sim qualifying)
            // funnel through (see the comment above this method), so resolving
            // each driver to its effective (post-transfer, post-progression)
            // form here - never mutating the shared drivers.json objects -
            // covers all of them at once.
            if (Career != null)
            {
                for (int i = 0; i < aiDrivers.Count; i++)
                {
                    aiDrivers[i] = Career.GetEffectiveDriver(aiDrivers[i]);
                }
            }

            return aiDrivers;
        }

        void PrepareAiQualifyingTargetsForPhase()
        {
            List<QualifyingSimEntry> active = ActiveQualifyingEntries(qualifyingPhase);
            for (int i = 0; i < active.Count; i++)
            {
                QualifyingSimEntry entry = active[i];
                if (!entry.isPlayer && GetQualifyingPhaseTime(entry, qualifyingPhase) <= 0f)
                {
                    SetAiQualifyingPhaseTime(entry, qualifyingPhase, SimulateAiQualifyingTime(entry, qualifyingPhase));
                }
            }
        }

    }
}
